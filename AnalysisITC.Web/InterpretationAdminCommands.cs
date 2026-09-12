using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AnalysisITC.Web;

public static class InterpretationAdminCommands
{
    public static Task<int> RunAsync(string[] args, IServiceProvider services, TextWriter output, TextWriter error)
    {
        try
        {
            return Task.FromResult(args[0] switch
            {
                "operator-code" => Operator(args.Skip(1).ToArray(), services.GetRequiredService<OperatorCodeRegistry>(), output, error),
                "generation-presets" => Presets(args.Skip(1).ToArray(), services.GetRequiredService<GenerationPresetRegistry>(), output, error),
                _ => Usage(args.Skip(1).ToArray(), services, output, error),
            });
        }
        catch (Exception ex) { error.WriteLine("Error: " + ex.Message); return Task.FromResult(1); }
    }

    static int Operator(string[] args, OperatorCodeRegistry registry, TextWriter output, TextWriter error)
    {
        if (args.Length == 0) return Help(error);
        if (args[0] == "create")
        {
            var label = Value(args, "--label") ?? throw new ArgumentException("--label is required.");
            var noExpiry = args.Contains("--no-expiry", StringComparer.Ordinal);
            var daysText = Value(args, "--expires-days");
            var tier = Value(args, "--tier") ?? InterpretationAccessTiers.Administrator;
            var name = Value(args, "--name"); var email = Value(args, "--email"); var organization = Value(args, "--organization");
            int? days = daysText is null ? null : int.Parse(daysText, CultureInfo.InvariantCulture);
            if (noExpiry && days is not null) throw new ArgumentException("Use either --no-expiry or --expires-days.");
            var created = registry.Create(label, days, noExpiry, tier, name, email, organization);
            output.WriteLine($"Created operator code {created.Record.Id}.");
            output.WriteLine("This secret is displayed once: " + created.Code);
            return 0;
        }
        if (args[0] == "list")
        {
            foreach (var item in registry.List())
                output.WriteLine($"{item.Id}  {item.Label}  tier={item.EffectiveAccessTier}  name={item.Name ?? "null"}  email={item.Email ?? "null"}  organization={item.Organization ?? "null"}  quota={(item.QuotaUnlimited ? "unlimited" : item.MonthlyQuotaUsdOverride?.ToString(CultureInfo.InvariantCulture) ?? "default")}  created={item.CreatedAtUtc:O}  expires={(item.ExpiresAtUtc?.ToString("O") ?? "never")}  revoked={(item.RevokedAtUtc?.ToString("O") ?? "no")}");
            return 0;
        }
        if (args[0] == "revoke" && args.Length == 2) return registry.Revoke(args[1]) ? 0 : NotFound(error);
        if (args[0] == "set-tier" && args.Length == 3) return registry.ChangeTier(args[1],args[2]) ? 0 : NotFound(error);
        if (args[0] == "set-details" && args.Length >= 2) return registry.ChangeDetails(args[1],Value(args,"--name"),Value(args,"--email"),Value(args,"--organization")) ? 0 : NotFound(error);
        if (args[0] == "set-quota" && args.Length == 3)
            return registry.ChangeQuota(args[1], string.Equals(args[2],"unlimited",StringComparison.OrdinalIgnoreCase) ? null : decimal.Parse(args[2],CultureInfo.InvariantCulture), string.Equals(args[2],"unlimited",StringComparison.OrdinalIgnoreCase)) ? 0 : NotFound(error);
        return Help(error);
    }

    static int Usage(string[] args, IServiceProvider services, TextWriter output, TextWriter error)
    {
        if (args.Length == 0) return Help(error);
        var store = services.GetRequiredService<InterpretationUsageStore>();
        if (args[0] == "maintenance") return Maintenance(args.Skip(1).ToArray(), services, store, output, error);
        if (args[0] == "migration" && args.Length >= 2 && args[1] == "status")
        {
            var migration = store.ReadMigrationStatus();
            output.WriteLine($"migration_complete={migration.Completed} activation_blockers={migration.ActivationBlockerCount} completed={migration.CompletedUtc:O}");
            output.WriteLine(migration.LimitationReport);
            foreach (var issue in store.ReadMigrationIssues())
                output.WriteLine($"legacy_key={issue.LegacyKey} kind={issue.Kind} blocking={issue.Blocking} details={issue.Details}");
            foreach (var settlement in store.ReadLegacySettlements())
                output.WriteLine($"legacy_key={settlement.LegacyKey} execution={settlement.ServerExecutionId} account={settlement.OperatorCodeId ?? "unassigned"} settlement={settlement.BillingState} verified_total={settlement.VerifiedTotalCost?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} evidence={settlement.Evidence}");
            return 0;
        }
        if (args[0] == "migration" && args.Length >= 3 && args[1] == "reconcile")
        {
            var account = Value(args,"--operator");
            if (account is not null && !services.GetRequiredService<OperatorCodeRegistry>().List().Any(item => item.Id==account))
                throw new ArgumentException("The verified operator account does not exist in the registry.");
            decimal? cost = Value(args,"--cost") is string amount ? decimal.Parse(amount,CultureInfo.InvariantCulture) : null;
            DateTime? started = Value(args,"--started") is string timestamp
                ? DateTime.Parse(timestamp,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal) : null;
            store.ReconcileLegacyExecution(args[2],cost,Value(args,"--evidence") ?? throw new ArgumentException("--evidence is required."),
                account,args.Contains("--anonymous",StringComparer.Ordinal),started,Value(args,"--waive"));
            output.WriteLine($"Historical execution {args[2]} reconciled; surviving records remain unchanged.");
            return 0;
        }
        if (args[0] == "reconcile" && args.Length >= 2)
        {
            var attempt = int.Parse(Value(args,"--attempt") ?? throw new ArgumentException("--attempt is required."), CultureInfo.InvariantCulture);
            decimal? cost = Value(args,"--cost") is string amount ? decimal.Parse(amount, CultureInfo.InvariantCulture) : null;
            var evidence = Value(args,"--evidence") ?? throw new ArgumentException("--evidence is required.");
            var waiver = Value(args,"--waive");
            store.Reconcile(args[1], attempt, cost, args.Contains("--confirmed-unsent",StringComparer.Ordinal), evidence, waiver);
            output.WriteLine($"Reconciled execution {args[1]}, attempt {attempt}." + (waiver is null ? "" : " Actual provider cost remains unknown (waived)."));
            return 0;
        }
        using var connection = store.OpenForCommand();
        if (args[0] == "status")
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT count(*),min(started_utc),max(started_utc),coalesce(sum(known_cost),0),coalesce(sum(unresolved_cost_count),0),coalesce(sum(waived_unknown_count),0) FROM execution_usage";
            using var reader = command.ExecuteReader(); reader.Read();
            var path = connection.DataSource; output.WriteLine($"database={path} bytes={(File.Exists(path) ? new FileInfo(path).Length : 0)} executions={reader.GetInt64(0)} oldest={Db(reader,1)} newest={Db(reader,2)} known_cost={Db(reader,3)} unresolved={Db(reader,4)} waived_unknown={Db(reader,5)}"); return 0;
        }
        if (args[0] == "show" && args.Length >= 2)
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT request_id AS server_execution_id,client_request_id,trace_id,operator_code_id,started_utc,outcome,http_status,effective_model,effective_reasoning,provider_attempts,total_tokens,known_cost,unresolved_cost_count,waived_unknown_count,effective_guidance_variant,guidance_revision FROM execution_usage WHERE request_id=$id"; command.Parameters.AddWithValue("$id", args[1]);
            using var reader = command.ExecuteReader(); if (!reader.Read()) return NotFound(error); NamedRow(reader, output); return 0;
        }
        if (args[0] == "inspect")
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT request_id AS server_execution_id,client_request_id,operator_code_id,started_utc,outcome,known_cost,unresolved_cost_count,waived_unknown_count FROM execution_usage WHERE (unresolved_cost_count>0 OR waived_unknown_count>0)";
            if (Value(args,"--operator") is string account)
            {
                command.CommandText += " AND operator_code_id=$operator";
                command.Parameters.AddWithValue("$operator", account);
            }
            command.CommandText += " ORDER BY started_utc,request_id";
            using var reader = command.ExecuteReader(); var count = 0;
            while (reader.Read()) { NamedRow(reader, output); count++; }
            if (count == 0) output.WriteLine("No executions with unresolved or waived unknown cost.");
            reader.Close();
            foreach (var attempt in store.InspectUnresolved(Value(args,"--operator")))
                output.WriteLine($"execution={attempt.ExecutionId} attempt={attempt.AttemptNumber} state={attempt.BillingState} actual_cost={(attempt.Cost?.ToString(CultureInfo.InvariantCulture) ?? "unknown")}");
            foreach (var settlement in store.ReadLegacySettlements().Where(item => item.BillingState=="waived_unknown"
                && (Value(args,"--operator") is not string filter || item.OperatorCodeId==filter)))
                output.WriteLine($"execution={settlement.ServerExecutionId} legacy_key={settlement.LegacyKey} state=waived_unknown actual_cost=unknown");
            return 0;
        }
        var since = ParseSince(Value(args, "--since") ?? (args[0] == "list" ? "24h" : "7d"));
        if (args[0] == "list")
        {
            var limit = int.TryParse(Value(args, "--limit"), out var parsed) ? Math.Clamp(parsed, 1, 10000) : 100;
            using var command = connection.CreateCommand(); command.CommandText = "SELECT request_id AS server_execution_id,client_request_id,started_utc,outcome,http_status,effective_model,effective_reasoning,provider_attempts,total_tokens,known_cost,unresolved_cost_count,waived_unknown_count,coalesce(operator_code_id,'public') AS user_id FROM execution_usage WHERE started_utc >= $since ORDER BY started_utc DESC LIMIT $limit"; command.Parameters.AddWithValue("$since", since.ToString("O")); command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader(); while (reader.Read()) NamedRow(reader, output); return 0;
        }
        if (args[0] == "summary")
        {
            var clauses = new List<string> { "started_utc >= $since" }; using var command = connection.CreateCommand(); command.Parameters.AddWithValue("$since", since.ToString("O"));
            if (Value(args,"--model") is string model) { clauses.Add("effective_model=$model"); command.Parameters.AddWithValue("$model",model); }
            if (Value(args,"--operator") is string op) { clauses.Add("operator_code_id=$operator"); command.Parameters.AddWithValue("$operator",op); }
            command.CommandText = $"SELECT count(*),coalesce(sum(provider_attempts),0),coalesce(sum(input_tokens),0),coalesce(sum(output_tokens),0),coalesce(sum(known_cost),0),coalesce(sum(unresolved_cost_count),0),coalesce(sum(waived_unknown_count),0) FROM execution_usage WHERE {string.Join(" AND ",clauses)}";
            using var reader = command.ExecuteReader(); reader.Read(); output.WriteLine($"executions={reader.GetInt64(0)} attempts={reader.GetInt64(1)} input_tokens={reader.GetInt64(2)} output_tokens={reader.GetInt64(3)} known_cost={Db(reader,4)} unresolved={Db(reader,5)} waived_unknown={Db(reader,6)} total_cost={(reader.GetInt64(5)>0||reader.GetInt64(6)>0 ? "unknown" : Db(reader,4))}"); return 0;
        }
        if (args[0] == "export")
        {
            var file = Value(args,"--output") ?? throw new ArgumentException("--output is required.");
            ExportUsage(store, since, file); return 0;
        }
        return Help(error);
    }

    static int Maintenance(string[] args, IServiceProvider services, InterpretationUsageStore store, TextWriter output, TextWriter error)
    {
        if (args.Length == 0) return Help(error);
        switch (args[0])
        {
            case "pause":
                var reason = Value(args,"--reason") ?? throw new ArgumentException("--reason is required.");
                var availability = services.GetRequiredService<InterpretationServiceAvailability>();
                if (availability.Read().Status != "retired") availability.Set("paused",reason);
                store.BeginMaintenance(reason);
                output.WriteLine("Generation is paused. Drain every service instance before reconciling accounting.");
                return 0;
            case "status":
                var status = store.ReadMaintenance();
                using (var db = store.OpenForCommand())
                using (var query = db.CreateCommand())
                {
                    query.CommandText="SELECT count(*) FROM executions WHERE completed_utc IS NULL AND lifecycle='admitted'";
                    output.WriteLine($"maintenance={status.Active} active_executions={query.ExecuteScalar()} reason={status.Reason ?? "none"}");
                }
                return 0;
            case "confirm-stopped":
                if (!args.Contains("--all-hosts-stopped",StringComparer.Ordinal))
                    throw new ArgumentException("Stop every service instance, then explicitly pass --all-hosts-stopped. Elapsed time is not evidence of stopping.");
                var evidence = Value(args,"--evidence") ?? throw new ArgumentException("--evidence is required.");
                output.WriteLine($"Marked {store.ConfirmStoppedExecutions(evidence)} abandoned executions as stopped; unresolved costs and holds are retained.");
                return 0;
            case "resume":
                store.EndMaintenance();
                output.WriteLine("Accounting maintenance ended. The service availability policy remains unchanged; resume generation separately when ready.");
                return 0;
            default: return Help(error);
        }
    }

    static int Presets(string[] args,GenerationPresetRegistry registry,TextWriter output,TextWriter error)
    {
        if(args.Length==1&&args[0]=="ensure"){registry.EnsureFile();return 0;}
        if(args.Length==1&&args[0]=="list"){var value=registry.Read();output.WriteLine($"revision={value.Revision} modified={value.ModifiedAtUtc:O} quota_started={value.QuotaAccountingStartedAtUtc:O}");output.WriteLine($"{value.Summary.Id}  {value.Summary.DisplayName}  {value.Summary.Model}  {value.Summary.ReasoningEffort}  all_tiers quota_free retrieval_disabled\n  description={value.Summary.Description}");foreach(var item in value.Presets)output.WriteLine($"{item.Id}  {item.DisplayName}  {item.Model}  {item.ReasoningEffort}\n  description={item.Description}");foreach(var quota in value.Quotas)output.WriteLine($"quota  tier={quota.AccessTier} monthly_usd={quota.MonthlyUsd.ToString(CultureInfo.InvariantCulture)}");foreach(var limit in value.RequestSizeLimits)output.WriteLine($"request_size  tier={limit.AccessTier} maximum_kib={limit.MaximumKiB}");return 0;}
        if(args.Length==4&&args[0]=="set"){var value=registry.Update(args[1],args[2],args[3]);output.WriteLine($"revision={value.Revision}");return 0;}
        if(args.Length==3&&args[0]=="set-description"){var value=registry.UpdateDescription(args[1],args[2]);output.WriteLine($"revision={value.Revision}");return 0;}
        if(args.Length==3&&args[0]=="set-quota"){var value=registry.UpdateQuota(args[1],decimal.Parse(args[2],CultureInfo.InvariantCulture));output.WriteLine($"revision={value.Revision}");return 0;}
        if(args.Length==3&&args[0]=="set-request-size"){var value=registry.UpdateRequestSizeLimit(args[1],int.Parse(args[2],CultureInfo.InvariantCulture));output.WriteLine($"revision={value.Revision}");return 0;}
        return Help(error);
    }

    static string? Value(string[] args,string key) { var i=Array.IndexOf(args,key); return i>=0 && i+1<args.Length ? args[i+1] : null; }
    static DateTime ParseSince(string value) { if (value.EndsWith('h') && double.TryParse(value[..^1],out var h)) return DateTime.UtcNow.AddHours(-h); if(value.EndsWith('d')&&double.TryParse(value[..^1],out var d))return DateTime.UtcNow.AddDays(-d); return DateTime.Parse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal); }
    static string Db(SqliteDataReader reader,int i)=>reader.IsDBNull(i)?"null":Convert.ToString(reader.GetValue(i),CultureInfo.InvariantCulture)??"";
    static void NamedRow(SqliteDataReader reader,TextWriter output)=>output.WriteLine(string.Join("  ",Enumerable.Range(0,reader.FieldCount).Select(i=>$"{reader.GetName(i)}={Db(reader,i)}")));
    static string Csv(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
    internal static void ExportUsage(InterpretationUsageStore store, DateTime since, string file)
    {
        using var connection = store.OpenForCommand(); using var command = connection.CreateCommand(); command.CommandText = "SELECT request_id AS server_execution_id,client_request_id,trace_id,started_utc,completed_utc,operator_code_id,report_id,analysis_ids,request_bytes,generation_profile,requested_preset,effective_preset,access_tier,preset_revision,requested_model,requested_reasoning,effective_model,effective_reasoning,requested_guidance_variant,effective_guidance_variant,guidance_revision,outcome,http_status,error_code,provider_attempts,input_tokens,cached_input_tokens,cache_write_tokens,output_tokens,reasoning_tokens,visible_output_tokens,total_tokens,known_cost,unresolved_cost_count,waived_unknown_count,CASE WHEN unresolved_cost_count=0 AND waived_unknown_count=0 THEN known_cost ELSE NULL END AS total_cost FROM execution_usage WHERE started_utc >= $since ORDER BY started_utc"; command.Parameters.AddWithValue("$since",since.ToString("O"));
        using var reader = command.ExecuteReader(); using var writer = new StreamWriter(file, false, new UTF8Encoding(false));
        writer.WriteLine(string.Join(",", Enumerable.Range(0,reader.FieldCount).Select(reader.GetName).Select(Csv)));
        while(reader.Read()) writer.WriteLine(string.Join(",",Enumerable.Range(0,reader.FieldCount).Select(i=>Csv(Db(reader,i)))));
    }
    static int NotFound(TextWriter error){error.WriteLine("Not found.");return 2;} static int Help(TextWriter error){error.WriteLine("Invalid command.");return 2;}
}
