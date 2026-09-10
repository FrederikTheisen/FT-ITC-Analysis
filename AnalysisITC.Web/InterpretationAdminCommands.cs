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
                _ => Usage(args.Skip(1).ToArray(), services.GetRequiredService<InterpretationUsageStore>(), output, error),
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

    static int Usage(string[] args, InterpretationUsageStore store, TextWriter output, TextWriter error)
    {
        if (args.Length == 0) return Help(error);
        using var connection = store.OpenForCommand();
        if (args[0] == "status")
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT count(*),min(started_utc),max(started_utc) FROM requests";
            using var reader = command.ExecuteReader(); reader.Read();
            var path = connection.DataSource; output.WriteLine($"database={path} bytes={(File.Exists(path) ? new FileInfo(path).Length : 0)} requests={reader.GetInt64(0)} oldest={Db(reader,1)} newest={Db(reader,2)}"); return 0;
        }
        if (args[0] == "show" && args.Length >= 2)
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT request_id,started_utc,outcome,http_status,effective_model,effective_reasoning,provider_attempts,total_tokens,estimated_cost,effective_guidance_variant,guidance_revision FROM requests WHERE request_id=$id"; command.Parameters.AddWithValue("$id", args[1]);
            using var reader = command.ExecuteReader(); if (!reader.Read()) return NotFound(error); Row(reader, output); return 0;
        }
        var since = ParseSince(Value(args, "--since") ?? (args[0] == "list" ? "24h" : "7d"));
        if (args[0] == "list")
        {
            var limit = int.TryParse(Value(args, "--limit"), out var parsed) ? Math.Clamp(parsed, 1, 10000) : 100;
            using var command = connection.CreateCommand(); command.CommandText = "SELECT request_id,started_utc,outcome,http_status,effective_model,effective_reasoning,provider_attempts,total_tokens,estimated_cost,coalesce(operator_code_id,'public') AS user_id FROM requests WHERE started_utc >= $since ORDER BY started_utc DESC LIMIT $limit"; command.Parameters.AddWithValue("$since", since.ToString("O")); command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader(); while (reader.Read()) UsageListRow(reader, output); return 0;
        }
        if (args[0] == "summary")
        {
            var clauses = new List<string> { "started_utc >= $since" }; using var command = connection.CreateCommand(); command.Parameters.AddWithValue("$since", since.ToString("O"));
            if (Value(args,"--model") is string model) { clauses.Add("effective_model=$model"); command.Parameters.AddWithValue("$model",model); }
            if (Value(args,"--operator") is string op) { clauses.Add("operator_code_id=$operator"); command.Parameters.AddWithValue("$operator",op); }
            command.CommandText = $"SELECT count(*),coalesce(sum(provider_attempts),0),coalesce(sum(input_tokens),0),coalesce(sum(output_tokens),0),sum(estimated_cost) FROM requests WHERE {string.Join(" AND ",clauses)}";
            using var reader = command.ExecuteReader(); reader.Read(); output.WriteLine($"requests={reader.GetInt64(0)} attempts={reader.GetInt64(1)} input_tokens={reader.GetInt64(2)} output_tokens={reader.GetInt64(3)} estimated_cost={Db(reader,4)}"); return 0;
        }
        if (args[0] == "export")
        {
            var file = Value(args,"--output") ?? throw new ArgumentException("--output is required.");
            ExportUsage(store, since, file); return 0;
        }
        return Help(error);
    }

    static int Presets(string[] args,GenerationPresetRegistry registry,TextWriter output,TextWriter error)
    {
        if(args.Length==1&&args[0]=="ensure"){registry.EnsureFile();return 0;}
        if(args.Length==1&&args[0]=="list"){var value=registry.Read();output.WriteLine($"revision={value.Revision} modified={value.ModifiedAtUtc:O} quota_started={value.QuotaAccountingStartedAtUtc:O}");output.WriteLine($"{value.Summary.Id}  {value.Summary.DisplayName}  {value.Summary.Model}  {value.Summary.ReasoningEffort}  all_tiers quota_free retrieval_disabled");foreach(var item in value.Presets)output.WriteLine($"{item.Id}  {item.DisplayName}  {item.Model}  {item.ReasoningEffort}");foreach(var quota in value.Quotas)output.WriteLine($"quota  tier={quota.AccessTier} monthly_usd={quota.MonthlyUsd.ToString(CultureInfo.InvariantCulture)}");foreach(var limit in value.RequestSizeLimits)output.WriteLine($"request_size  tier={limit.AccessTier} maximum_kib={limit.MaximumKiB}");return 0;}
        if(args.Length==4&&args[0]=="set"){var value=registry.Update(args[1],args[2],args[3]);output.WriteLine($"revision={value.Revision}");return 0;}
        if(args.Length==3&&args[0]=="set-quota"){var value=registry.UpdateQuota(args[1],decimal.Parse(args[2],CultureInfo.InvariantCulture));output.WriteLine($"revision={value.Revision}");return 0;}
        if(args.Length==3&&args[0]=="set-request-size"){var value=registry.UpdateRequestSizeLimit(args[1],int.Parse(args[2],CultureInfo.InvariantCulture));output.WriteLine($"revision={value.Revision}");return 0;}
        return Help(error);
    }

    static string? Value(string[] args,string key) { var i=Array.IndexOf(args,key); return i>=0 && i+1<args.Length ? args[i+1] : null; }
    static DateTime ParseSince(string value) { if (value.EndsWith('h') && double.TryParse(value[..^1],out var h)) return DateTime.UtcNow.AddHours(-h); if(value.EndsWith('d')&&double.TryParse(value[..^1],out var d))return DateTime.UtcNow.AddDays(-d); return DateTime.Parse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal); }
    static string Db(SqliteDataReader reader,int i)=>reader.IsDBNull(i)?"null":Convert.ToString(reader.GetValue(i),CultureInfo.InvariantCulture)??"";
    static void Row(SqliteDataReader r,TextWriter o)=>o.WriteLine($"{Db(r,0)}  {Db(r,1)}  {Db(r,2)}  http={Db(r,3)} model={Db(r,4)} reasoning={Db(r,5)} attempts={Db(r,6)} tokens={Db(r,7)} estimated_cost={Db(r,8)}"+(r.FieldCount>9?$" guidance={Db(r,9)} guidance_revision={Db(r,10)}":""));
    static void UsageListRow(SqliteDataReader r,TextWriter o)=>o.WriteLine($"{Db(r,0)}  {Db(r,1)}  user_id={Db(r,9)}  {Db(r,2)}  http={Db(r,3)} model={Db(r,4)} reasoning={Db(r,5)} attempts={Db(r,6)} tokens={Db(r,7)} estimated_cost={Db(r,8)}");
    static string Csv(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
    internal static void ExportUsage(InterpretationUsageStore store, DateTime since, string file)
    {
        using var connection = store.OpenForCommand(); using var command = connection.CreateCommand(); command.CommandText = "SELECT request_id,trace_id,started_utc,completed_utc,operator_code_id,report_id,analysis_ids,request_bytes,generation_profile,requested_preset,effective_preset,access_tier,preset_revision,requested_model,requested_reasoning,effective_model,effective_reasoning,requested_guidance_variant,effective_guidance_variant,guidance_revision,outcome,http_status,error_code,provider_attempts,input_tokens,cached_input_tokens,cache_write_tokens,output_tokens,reasoning_tokens,visible_output_tokens,total_tokens,estimated_cost FROM requests WHERE started_utc >= $since ORDER BY started_utc"; command.Parameters.AddWithValue("$since",since.ToString("O"));
        using var reader = command.ExecuteReader(); using var writer = new StreamWriter(file, false, new UTF8Encoding(false));
        writer.WriteLine(string.Join(",", Enumerable.Range(0,reader.FieldCount).Select(reader.GetName).Select(Csv)));
        while(reader.Read()) writer.WriteLine(string.Join(",",Enumerable.Range(0,reader.FieldCount).Select(i=>Csv(Db(reader,i)))));
    }
    static int NotFound(TextWriter error){error.WriteLine("Not found.");return 2;} static int Help(TextWriter error){error.WriteLine("Invalid command.");return 2;}
}
