using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class InteractiveAdminTool
{
    const string LocalStatusUrl = "http://127.0.0.1:5000/api/interpretation/status";
    const string PublicStatusUrl = "https://app.ft-itc.org/api/interpretation/status";
    const string DefaultExportDirectory = "/home/logexports";
    readonly OperatorCodeRegistry registry;
    readonly InterpretationUsageStore usage;
    readonly GenerationPresetRegistry presets;
    readonly InterpretationQuotaService quotas;
    readonly InterpretationServiceAvailability availability;
    readonly RegistrationAvailability? registrationAvailability;
    readonly InterpretationOptions options;
    readonly SelfRegistrationStore? registrations;
    readonly TextReader input;
    readonly TextWriter output;
    readonly Func<string, Task<(bool Success, string Detail)>> serviceCheck;
    readonly Func<string, Task<(bool Success, string Detail)>> endpointCheck;
    readonly string exportDirectory;
    readonly TimeZoneInfo displayTimeZone;
    readonly bool testMode;
    readonly bool lineMenuFallback;
    readonly Func<ConsoleKeyInfo> readKey;
    readonly Dictionary<string, string> menuSelections = new(StringComparer.Ordinal);
    bool cancelRequested;

    InteractiveAdminTool(IServiceProvider services, TextReader input, TextWriter output,
        Func<string, Task<(bool Success, string Detail)>>? serviceCheck = null,
        Func<string, Task<(bool Success, string Detail)>>? endpointCheck = null,
        string exportDirectory = DefaultExportDirectory, bool testMode = false,
        Func<ConsoleKeyInfo>? readKey = null)
    {
        registry = services.GetRequiredService<OperatorCodeRegistry>();
        usage = services.GetRequiredService<InterpretationUsageStore>();
        registrations = services.GetService<SelfRegistrationStore>();
        presets = services.GetRequiredService<GenerationPresetRegistry>();
        quotas = services.GetRequiredService<InterpretationQuotaService>();
        availability = services.GetRequiredService<InterpretationServiceAvailability>();
        registrationAvailability = services.GetService<RegistrationAvailability>();
        options = services.GetRequiredService<IOptions<InterpretationOptions>>().Value;
        this.input = input;
        this.output = output;
        this.serviceCheck = serviceCheck ?? CheckServiceAsync;
        this.endpointCheck = endpointCheck ?? CheckEndpointAsync;
        this.exportDirectory = exportDirectory;
        this.testMode = testMode;
        lineMenuFallback = testMode && readKey is null;
        this.readKey = readKey ?? (() => Console.ReadKey(true));
        displayTimeZone = ResolveTimeZone(options.AdminDisplayTimeZone);
    }

    public static Task<int> RunAsync(IServiceProvider services, TextReader input, TextWriter output) =>
        new InteractiveAdminTool(services, input, output).RunAsync();

    internal static InteractiveAdminTool CreateForTests(IServiceProvider services, TextReader input, TextWriter output,
        Func<string, Task<(bool Success, string Detail)>> serviceCheck,
        Func<string, Task<(bool Success, string Detail)>> endpointCheck,
        string? exportDirectory = null, Func<ConsoleKeyInfo>? readKey = null) =>
        new(services, input, output, serviceCheck, endpointCheck, exportDirectory ?? DefaultExportDirectory,
            testMode: true, readKey: readKey);

    internal async Task<int> RunAsync()
    {
        if (!testMode && (Console.IsInputRedirected || Console.IsOutputRedirected
            || !ReferenceEquals(input, Console.In) || !ReferenceEquals(output, Console.Out)))
        {
            output.WriteLine("ftitc-admintool requires an interactive terminal. Use the non-interactive administration commands for scripts.");
            return 2;
        }
        while (true)
        {
            PrintMainMenuHeader();
            switch (SelectMenu("main", false,
                new("status", "Status"),
                new("accounts", "Operator accounts"),
                new("logs", "Logs"),
                new("presets", "Generation presets"),
                new("availability", "Service availability"),
                new("exit", "Exit")))
            {
                case "status": await StatusAsync(); Pause(); break;
                case "accounts": Accounts(); break;
                case "logs": Logs(); break;
                case "presets": Presets(); break;
                case "availability": Availability(); Pause(); break;
                case "exit": return 0;
                case null: return 0;
            }
            if (cancelRequested) return 0;
        }
    }

    void PrintMainMenuHeader()
    {
        output.WriteLine();
        output.WriteLine("FT-ITC administration");
        try
        {
            var assembly = typeof(InteractiveAdminTool).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString() ?? "unknown";
            var location = assembly.Location;
            var built = string.IsNullOrWhiteSpace(location) || !File.Exists(location)
                ? "unknown"
                : FormatTime(File.GetLastWriteTimeUtc(location));
            output.WriteLine($"Build: {version} · {built}");
        }
        catch { output.WriteLine("Build: unavailable"); }
        output.WriteLine($"Times: {displayTimeZone.Id}");

        try
        {
            var state = availability.Read();
            output.WriteLine($"Interpretation service: {state.Status} · {state.Message ?? "no explanation"}");
        }
        catch { output.WriteLine("Interpretation service: unavailable"); }

        try
        {
            var now = DateTime.UtcNow;
            var active = registry.List().Count(x => x.RevokedAtUtc is null && (x.ExpiresAtUtc is null || x.ExpiresAtUtc > now));
            output.WriteLine($"Active accounts: {active}");
        }
        catch { output.WriteLine("Active accounts: unavailable"); }

        try
        {
            using var connection = usage.OpenForCommand();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*),max(started_utc) FROM requests";
            using var reader = command.ExecuteReader(); reader.Read();
            output.WriteLine($"Requests: {reader.GetInt64(0):N0} · Last request: {TimeDb(reader, 1)}");
        }
        catch { output.WriteLine("Requests: unavailable · Last request: unavailable"); }
        output.WriteLine();
    }

    void Availability()
    {
        var current = availability.Read();
        output.WriteLine($"Current service availability: {current.Status} · {current.Message ?? "no explanation"}");
        var choice = SelectMenu("availability", true, current.Status, false,
            new("active", "Active"),
            new("paused", "Paused"),
            new("retired", "Retired"),
            new("registration", "Public registration"),
            new("back", "Back"));
        if (choice == "registration") { ToggleRegistration(); return; }
        if (choice is null or "back") return;
        var status = choice;
        var message = Prompt("Explanation (optional)");
        output.WriteLine($"Proposed change: {status} · {(string.IsNullOrWhiteSpace(message) ? "no explanation" : message)}");
        if (Confirm("Apply this service availability change?")) { availability.Set(status, message); output.WriteLine("Service availability updated."); }
        else output.WriteLine("Change cancelled.");
    }

    void ToggleRegistration()
    {
        if (registrationAvailability is null) { output.WriteLine("Registration controls are unavailable in this environment."); return; }
        var current = registrationAvailability.Read();
        output.WriteLine($"Public registration is currently {(current.Enabled ? "enabled" : "disabled")}.");
        var next = SelectMenu("registration-state", true, current.Enabled ? "enabled" : "disabled", false,
            new("enabled", "Enabled"), new("disabled", "Disabled"), new("back", "Back"));
        if (next is null or "back") { output.WriteLine("Change cancelled."); return; }
        var enabled = next == "enabled";
        if (enabled == current.Enabled) { output.WriteLine("No change made."); return; }
        if (!Confirm($"Set public registration to {(enabled ? "enabled" : "disabled")}?")) { output.WriteLine("Change cancelled."); return; }
        registrationAvailability.Set(enabled);
        output.WriteLine($"Public registration {(enabled ? "enabled" : "disabled")}.");
    }

    async Task StatusAsync()
    {
        output.WriteLine(); output.WriteLine("Service");
        var availabilityState = availability.Read();
        output.WriteLine($"  Interpretation availability: {availabilityState.Status} · {availabilityState.Message ?? "no explanation"}");
        if (registrationAvailability is not null)
        {
            var registrationState = registrationAvailability.Read();
            output.WriteLine($"  Public registration: {(registrationState.Enabled ? "enabled" : "disabled")}" + (registrationState.Message is null ? "" : $" · {registrationState.Message}"));
        }
        PrintCheck("ftitc-web", await serviceCheck("ftitc-web"));
        output.WriteLine(); output.WriteLine("Interpretation endpoints");
        PrintCheck("Local", await endpointCheck(LocalStatusUrl));
        PrintCheck("Public", await endpointCheck(PublicStatusUrl));
        output.WriteLine(); output.WriteLine("Operator accounts");
        try
        {
            var now = DateTime.UtcNow; var records = registry.List();
            var active = records.Count(x => x.RevokedAtUtc is null && (x.ExpiresAtUtc is null || x.ExpiresAtUtc > now));
            var expired = records.Count(x => x.RevokedAtUtc is null && x.ExpiresAtUtc is not null && x.ExpiresAtUtc <= now);
            output.WriteLine($"  Registry: {options.OperatorAccess.RegistryPath}");
            output.WriteLine($"  Active: {active}"); output.WriteLine($"  Expired: {expired}");
            output.WriteLine($"  Revoked: {records.Count(x => x.RevokedAtUtc is not null)}");
        }
        catch (Exception ex) { output.WriteLine("  Unavailable: " + Safe(ex)); }
        output.WriteLine(); output.WriteLine("Generation presets");
        try { PrintPresets(presets.Read()); }
        catch (Exception ex) { output.WriteLine("  Unavailable: " + Safe(ex)); }
        output.WriteLine(); output.WriteLine("Usage log");
        try
        {
            using var connection = usage.OpenForCommand(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*),min(started_utc),max(started_utc) FROM requests";
            using var reader = command.ExecuteReader(); reader.Read(); var path = connection.DataSource;
            output.WriteLine($"  Database: {path}");
            output.WriteLine($"  Size: {(File.Exists(path) ? new FileInfo(path).Length : 0):N0} bytes");
            output.WriteLine($"  Requests: {reader.GetInt64(0):N0}");
            output.WriteLine($"  Oldest: {TimeDb(reader, 1)}"); output.WriteLine($"  Newest: {TimeDb(reader, 2)}");
        }
        catch (Exception ex) { output.WriteLine("  Unavailable: " + Safe(ex)); }
    }

    void Accounts()
    {
        while (true)
        {
            output.WriteLine(); output.WriteLine("Operator accounts");
            switch (SelectMenu("accounts", true,
                new("create", "Create"),
                new("list", "List"),
                new("details", "Account details"),
                new("back", "Back")))
            {
                case "create": CreateAccount(); Pause(); break;
                case "list": ListAccounts(); Pause(); break;
                case "details": AccountDetails(); break;
                case "back": case null: return;
            }
            if (cancelRequested) return;
        }
    }

    void CreateAccount()
    {
        var label = Required("Label"); if (label is null) return;
        var name = Prompt("Name (optional)"); if (name is null) return;
        var email = Prompt("Email (optional)"); if (email is null) return;
        var organization = Prompt("Organization (optional)"); if (organization is null) return;
        var tier = SelectTier(InterpretationAccessTiers.Standard); if (tier is null) return;
        int? days = options.OperatorAccess.DefaultLifetimeDays; var noExpiry = false;
        var expiry = SelectMenu("account-expiry", true, "default", false,
            new("default", $"{options.OperatorAccess.DefaultLifetimeDays} days (default)"),
            new("custom", "Custom days"),
            new("none", "No expiry"));
        if (expiry is null) return;
        if (expiry == "none") { noExpiry = true; days = null; }
        else if (expiry == "custom")
        {
            var value = PromptPositiveInteger("Number of days"); if (value is null) return; days = value;
        }
        output.WriteLine(); output.WriteLine("Create operator account"); output.WriteLine($"  Label: {label}");
        output.WriteLine($"  Name: {name}"); output.WriteLine($"  Email: {email}"); output.WriteLine($"  Organization: {organization}");
        output.WriteLine($"  Access level: {InterpretationAccessTiers.DisplayName(tier)}");
        output.WriteLine($"  Expiry: {(noExpiry ? "never" : $"{days} days")}");
        if (!Confirm("Create this account?")) { output.WriteLine("Creation cancelled."); return; }
        var created = registry.Create(label, days, noExpiry, tier, name, email, organization);
        output.WriteLine(); output.WriteLine($"Created account ID: {created.Record.Id}");
        output.WriteLine("The following code is displayed once. Store it securely:"); output.WriteLine(created.Code);
    }

    void RevokeAccount(OperatorCodeRecord record)
    {
        output.WriteLine(); PrintAccount(record);
        if (record.RevokedAtUtc is not null) { output.WriteLine("This account is already revoked."); return; }
        if (!Confirm("Revoke this account?")) { output.WriteLine("Revocation cancelled."); return; }
        output.WriteLine(registry.Revoke(record.Id) ? "Account revoked." : "Account could not be found.");
    }

    void ListAccounts() => ListAccounts(registry.List());

    void ChangeTier(OperatorCodeRecord record)
    {
        output.WriteLine(); PrintAccount(record); var tier=SelectTier(record.EffectiveAccessTier); if(tier is null)return;
        output.WriteLine($"  Old access level: {record.EffectiveAccessTier}"); output.WriteLine($"  New access level: {tier}");
        if(!Confirm("Apply this access-level change?")){output.WriteLine("Change cancelled.");return;}
        output.WriteLine(registry.ChangeTier(record.Id,tier)?"Access level changed.":"Account could not be found.");
    }

    void EditDetails(OperatorCodeRecord record)
    {
        var name=Prompt("Name",record.Name); if(name is null)return;
        var email=Prompt("Email",record.Email); if(email is null)return;
        var organization=Prompt("Organization",record.Organization); if(organization is null)return;
        if(!Confirm("Apply these contact details?")){output.WriteLine("Change cancelled.");return;}
        output.WriteLine(registry.ChangeDetails(record.Id,name,email,organization)?"Contact details changed.":"Account could not be found.");
    }

    void ChangeQuota(OperatorCodeRecord record)
    {
        var current = record.QuotaUnlimited ? "unlimited" : record.MonthlyQuotaUsdOverride is null ? "default" : "custom";
        var choice=SelectMenu("quota-mode", true, current, false,
            new("default", "Tier default"), new("custom", "Custom monthly USD"), new("unlimited", "Unlimited"));
        if(choice is null)return;
        decimal? amount=null; var unlimited=choice=="unlimited";
        if(choice=="custom") { amount=PromptPositiveDecimal("Monthly USD"); if(amount is null)return; }
        output.WriteLine($"  New quota: {(unlimited?"unlimited":amount is null?"tier default":amount.Value.ToString("C",CultureInfo.GetCultureInfo("en-US")))}");
        if(!Confirm("Apply this quota change?")){output.WriteLine("Change cancelled.");return;}
        output.WriteLine(registry.ChangeQuota(record.Id,amount,unlimited)?"Quota changed.":"Account could not be found.");
    }
    void ListAccounts(IReadOnlyList<OperatorCodeRecord> records)
    {
        output.WriteLine();
        if (records.Count == 0) { output.WriteLine("No operator accounts."); return; }
        output.WriteLine("ID                                Name/Label                 Email                         Level");
        foreach (var record in records.OrderBy(x => x.CreatedAtUtc))
            output.WriteLine($"{record.Id,-32}  {Compact(record.Name ?? record.Label,26),-26}  {Compact(record.Email ?? "-",28),-28}  {InterpretationAccessTiers.DisplayName(record.EffectiveAccessTier)}");
    }

    void PrintAccount(OperatorCodeRecord record)
    {
        output.WriteLine($"  ID: {record.Id}"); output.WriteLine($"  Label: {record.Label}");
        output.WriteLine($"  Name: {record.Name ?? "not set"}"); output.WriteLine($"  Email: {record.Email ?? "not set"}"); output.WriteLine($"  Org: {record.Organization ?? "not set"}");
        output.WriteLine($"  Access: {InterpretationAccessTiers.DisplayName(record.EffectiveAccessTier)} ({record.EffectiveAccessTier})");
        output.WriteLine($"  Quota: {(record.QuotaUnlimited ? "unlimited" : record.MonthlyQuotaUsdOverride is decimal amount ? $"${amount:0.00} monthly override" : "tier default")}");
        output.WriteLine($"  Created date: {FormatTime(record.CreatedAtUtc)}"); output.WriteLine($"  Expiry date: {(record.ExpiresAtUtc is DateTime expiry ? FormatTime(expiry) : "never")}");
        output.WriteLine($"  Status: {AccountStatus(record)}");
    }

    void AccountDetails()
    {
        var id = Required("Exact account ID"); if (id is null) return;
        var record = registry.List().SingleOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        if (record is null) { output.WriteLine("No account has that ID."); return; }
        while (true)
        {
            record = registry.List().SingleOrDefault(x => x.Id == id);
            if (record is null) { output.WriteLine("Account could not be found."); return; }
            PrintAccountSummary(record);
            output.WriteLine(); output.WriteLine("Account settings");
            switch(SelectMenu("account-details", true,
                new("all", "All details"),
                new("update", "Update details"),
                new("tier", "Change access level"),
                new("quota", "Change quota"),
                new("revoke", "Revoke"),
                new("scrub", "Scrub personal data"),
                new("back", "Back")))
            {
                case "all":
                    var since=SelectUsagePeriod(); if(since is not null) PrintAccountUsage(record,since.Value);
                    Pause(); break;
                case "update": EditDetails(record); Pause(); break;
                case "tier": ChangeTier(record); Pause(); break;
                case "quota": ChangeQuota(record); Pause(); break;
                case "revoke": RevokeAccount(record); Pause(); break;
                case "scrub": ScrubAccount(record); Pause(); break;
                case "back": case null: return;
            }
            if (cancelRequested) return;
        }
    }

    void ScrubAccount(OperatorCodeRecord record)
    {
        output.WriteLine();
        output.WriteLine("Scrub personal data");
        output.WriteLine($"  Account ID: {record.Id}");
        output.WriteLine("  The label, name, email, organisation and bearer credential will be erased.");
        output.WriteLine("  Accounting totals, timestamps, models, outcomes and quota history remain under the opaque ID.");
        output.WriteLine("  Delivered email, exports, backups, journals and provider records are not changed.");
        var phrase = ExactPrompt("Type exact lowercase 'scrub' to continue");
        if (phrase != "scrub") { output.WriteLine("Scrub cancelled."); return; }
        if (!Confirm("Scrub this account permanently?")) { output.WriteLine("Scrub cancelled."); return; }
        var result = registry.Scrub(record.Id, registrations, usage);
        if (result is null) { output.WriteLine("Account could not be found."); return; }
        output.WriteLine($"Scrubbed account {result.AccountId} at {FormatTime(result.ScrubbedAtUtc)}.");
        output.WriteLine($"Pending delivery cancelled: {(result.PendingDeliveryCancelled ? "yes" : "no")}.");
    }

    void PrintAccountSummary(OperatorCodeRecord record)
    {
        output.WriteLine(); output.WriteLine("Account details"); PrintAccount(record);
        try
        {
            using var connection=usage.OpenForCommand(); using var command=connection.CreateCommand();
            command.CommandText="SELECT count(*),coalesce(sum(known_cost),0),coalesce(sum(unresolved_cost_count),0),coalesce(sum(waived_unknown_count),0) FROM execution_usage WHERE operator_code_id=$operator";
            command.Parameters.AddWithValue("$operator",record.Id); using var reader=command.ExecuteReader(); reader.Read();
            output.WriteLine($"  Total interpretations: {Db(reader,0)}");
            PrintCost(reader,1,2,3);
            var quotaStatus=quotas.GetStatus(record.Id,record.EffectiveAccessTier,"shared");
            if(!quotaStatus.IsLimited) output.WriteLine("  Remaining quota: unlimited / not applicable");
            else
            {
                var remaining=Math.Max(0m,quotaStatus.LimitUsd-quotaStatus.SpentUsd);
                output.WriteLine($"  Remaining quota: ${remaining:0.0000} of ${quotaStatus.LimitUsd:0.00} ({quotaStatus.RemainingPercent}%)");
                output.WriteLine($"  Quota resets: {FormatTime(quotaStatus.ResetsAtUtc)}");
            }
        }
        catch(Exception ex)
        {
            output.WriteLine("  Total interpretations: unavailable");
            output.WriteLine("  Estimated cost: unavailable");
            output.WriteLine("  Remaining quota: unavailable");
            output.WriteLine("  Usage details unavailable: "+Safe(ex));
        }
    }

    void PrintAccountUsage(OperatorCodeRecord record, DateTime since)
    {
        output.WriteLine(); output.WriteLine("Account"); PrintAccount(record);
        output.WriteLine($"  Usage period: {(since == DateTime.MinValue ? "all time" : $"since {FormatTime(since)}")}");

        using var connection = usage.OpenForCommand();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*),min(started_utc),max(started_utc),coalesce(sum(provider_attempts),0),coalesce(sum(input_tokens),0),coalesce(sum(cached_input_tokens),0),coalesce(sum(output_tokens),0),coalesce(sum(reasoning_tokens),0),coalesce(sum(visible_output_tokens),0),coalesce(sum(total_tokens),0),avg(latency_ms),max(latency_ms),coalesce(sum(known_cost),0),coalesce(sum(unresolved_cost_count),0),coalesce(sum(waived_unknown_count),0) FROM execution_usage WHERE operator_code_id=$operator AND started_utc >= $since";
            command.Parameters.AddWithValue("$operator", record.Id); command.Parameters.AddWithValue("$since", since.ToString("O"));
            using var reader = command.ExecuteReader(); reader.Read();
            string[] labels = ["Interpretation executions","First execution","Most recent execution","Provider attempts","Input tokens","Cached input tokens","Output tokens","Reasoning tokens","Visible output tokens","Total tokens","Average latency ms","Maximum latency ms"];
            output.WriteLine(); output.WriteLine("Usage totals");
            for (var i = 0; i < labels.Length; i++) output.WriteLine($"  {labels[i]}: {(i is 1 or 2 ? TimeDb(reader, i) : Db(reader,i))}");
            PrintCost(reader,12,13,14);
        }
        PrintAccountGroup(connection, record.Id, since, "Outcomes", "SELECT outcome,count(*) FROM requests WHERE operator_code_id=$operator AND started_utc >= $since GROUP BY outcome ORDER BY count(*) DESC,outcome");
        PrintAccountGroup(connection, record.Id, since, "Presets", "SELECT coalesce(effective_preset,'custom') AS effective_preset,count(*) AS executions,coalesce(sum(known_cost),0) AS known_cost,coalesce(sum(unresolved_cost_count),0) AS unresolved_cost_count,coalesce(sum(waived_unknown_count),0) AS waived_unknown_count FROM execution_usage WHERE operator_code_id=$operator AND started_utc >= $since GROUP BY effective_preset ORDER BY count(*) DESC,effective_preset");
        PrintAccountGroup(connection, record.Id, since, "Models and reasoning", "SELECT coalesce(effective_model,'unknown') AS model,coalesce(effective_reasoning,'unknown') AS reasoning,count(*) AS executions,coalesce(sum(total_tokens),0) AS total_tokens,coalesce(sum(known_cost),0) AS known_cost,coalesce(sum(unresolved_cost_count),0) AS unresolved_cost_count,coalesce(sum(waived_unknown_count),0) AS waived_unknown_count FROM execution_usage WHERE operator_code_id=$operator AND started_utc >= $since GROUP BY effective_model,effective_reasoning ORDER BY count(*) DESC,effective_model,effective_reasoning");
        PrintAccountGroup(connection, record.Id, since, "Recent executions", "SELECT request_id AS server_execution_id,client_request_id,started_utc,coalesce(effective_preset,'custom') AS effective_preset,effective_model,effective_reasoning,outcome,latency_ms,known_cost,unresolved_cost_count,waived_unknown_count FROM execution_usage WHERE operator_code_id=$operator AND started_utc >= $since ORDER BY started_utc DESC LIMIT 10");
    }

    void PrintCost(SqliteDataReader reader, int known, int unresolved, int waived)
    {
        var incomplete = reader.GetInt64(unresolved) > 0 || reader.GetInt64(waived) > 0;
        output.WriteLine($"  {(incomplete ? "Known cost subtotal" : "Estimated cost")}: {Db(reader,known)}");
        if (incomplete) output.WriteLine($"  Actual total: unknown; unresolved={Db(reader,unresolved)}, waived unknown={Db(reader,waived)}");
    }

    void PrintAccountGroup(SqliteConnection connection, string operatorId, DateTime since, string heading, string sql)
    {
        output.WriteLine(); output.WriteLine(heading);
        using var command = connection.CreateCommand(); command.CommandText = sql;
        command.Parameters.AddWithValue("$operator", operatorId); command.Parameters.AddWithValue("$since", since.ToString("O"));
        using var reader = command.ExecuteReader(); var count = 0;
        while (reader.Read()) { count++; output.WriteLine("  " + string.Join("  ", Enumerable.Range(0, reader.FieldCount).Select(i => $"{DisplayColumnName(reader.GetName(i))}={DisplayDb(reader,i)}"))); }
        if (count == 0) output.WriteLine("  No matching requests.");
    }

    DateTime? SelectUsagePeriod()
    {
        var choice = SelectMenu("usage-period", true, "7d", false,
            new("24h", "Last 24 hours"),
            new("7d", "Last 7 days (default)"),
            new("30d", "Last 30 days"),
            new("all", "All time"),
            new("custom", "Custom"));
        return choice switch
        {
            "24h" => DateTime.UtcNow.AddHours(-24),
            "7d" => DateTime.UtcNow.AddDays(-7),
            "30d" => DateTime.UtcNow.AddDays(-30),
            "all" => DateTime.MinValue,
            "custom" => PromptSince("Start date/time or horizon", "30d"),
            _ => null,
        };
    }

    void Logs()
    {
        while (true)
        {
            output.WriteLine(); output.WriteLine("Logs");
            switch (SelectMenu("logs", true,
                new("list", "List"), new("show", "Show"), new("summary", "Summary"),
                new("export", "Export"), new("back", "Back")))
            {
                case "list": ListLogs(); Pause(); break;
                case "show": ShowLog(); Pause(); break;
                case "summary": Summary(); Pause(); break;
                case "export": Export(); Pause(); break;
                case "back": case null: return;
            }
            if (cancelRequested) return;
        }
    }

    void ListLogs()
    {
        var since = PromptSince("Time horizon", "24h"); if (since is null) return;
        var limit = PromptPositiveInteger("Maximum entries", 100, 10000); if (limit is null) return;
        using var connection = usage.OpenForCommand(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT request_id,started_utc,operator_code_id,effective_model,effective_reasoning,effective_preset,outcome,http_status,latency_ms,known_cost,unresolved_cost_count,waived_unknown_count FROM execution_usage WHERE started_utc >= $since ORDER BY started_utc DESC LIMIT $limit";
        command.Parameters.AddWithValue("$since", since.Value.ToString("O")); command.Parameters.AddWithValue("$limit", limit.Value);
        using var reader = command.ExecuteReader(); var count = 0;
        output.WriteLine(); output.WriteLine();
        while (reader.Read())
        {
            if (count++ > 0) output.WriteLine();
            output.WriteLine($"Entry: {Db(reader,0)} · {TimeDb(reader,1)} · User: {UserId(reader,2)}");
            output.WriteLine($"  Model: {DisplayValue(reader,3)} · Reasoning: {DisplayValue(reader,4)} · Preset: {PresetName(reader,5)}");
            output.WriteLine($"  Status: {DisplayValue(reader,6)} · HTTP: {DisplayValue(reader,7)} · Time: {Seconds(reader,8)} s · {ListCost(reader,9,10,11)}");
        }
        if (count == 0) output.WriteLine("No matching requests.");
        output.WriteLine(); output.WriteLine();
    }

    void ShowLog()
    {
        var id = Required("Server execution ID"); if (id is null) return;
        using var connection = usage.OpenForCommand();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT request_id AS server_execution_id,client_request_id,trace_id,started_utc,completed_utc,operator_code_id,report_id,analysis_ids,request_bytes,generation_profile,requested_preset,effective_preset,access_tier,preset_revision,requested_model,requested_reasoning,effective_model,effective_reasoning,requested_guidance_variant,effective_guidance_variant,guidance_revision,request_version,response_version,package_version,prompt_version,output_version,knowledge_base_ids,latency_ms,outcome,http_status,error_code,provider_attempts,input_tokens,cached_input_tokens,cache_write_tokens,output_tokens,reasoning_tokens,visible_output_tokens,total_tokens,known_cost,unresolved_cost_count,waived_unknown_count FROM execution_usage WHERE request_id=$id";
            command.Parameters.AddWithValue("$id", id); using var reader = command.ExecuteReader();
            if (!reader.Read()) { output.WriteLine("No execution has that ID."); return; }
            output.WriteLine(); output.WriteLine("Execution");
            for (var i = 0; i < reader.FieldCount; i++) output.WriteLine($"  {Label(DisplayColumnName(reader.GetName(i)))}: {DisplayDb(reader,i)}");
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT attempt_number,timestamp_utc,guidance_variant,guidance_revision,openai_response_id,provider_request_id,latency_ms,model,reasoning,file_search_enabled,file_search_calls,input_tokens,cached_input_tokens,cache_write_tokens,output_tokens,reasoning_tokens,visible_output_tokens,total_tokens,model_cost,file_search_cost,combined_cost,pricing_revision,outcome,http_status,error_code,context_fallback,retrieval_fallback FROM attempts WHERE request_id=$id ORDER BY attempt_number";
            command.Parameters.AddWithValue("$id", id); using var reader = command.ExecuteReader();
            while (reader.Read()) { output.WriteLine(); output.WriteLine($"Provider attempt {Db(reader,0)}"); for (var i=1;i<reader.FieldCount;i++) output.WriteLine($"  {Label(DisplayColumnName(reader.GetName(i)))}: {DisplayDb(reader,i)}"); }
        }
    }

    void Summary()
    {
        var since = PromptSince("Time horizon", "7d"); if (since is null) return;
        var model = Prompt("Model filter (blank for all)") ?? ""; var op = Prompt("Operator ID filter (blank for all)") ?? "";
        using var connection = usage.OpenForCommand(); using var command = connection.CreateCommand(); var clauses = new List<string> { "started_utc >= $since" };
        command.Parameters.AddWithValue("$since", since.Value.ToString("O"));
        if (!string.IsNullOrWhiteSpace(model)) { clauses.Add("effective_model=$model"); command.Parameters.AddWithValue("$model", model); }
        if (!string.IsNullOrWhiteSpace(op)) { clauses.Add("operator_code_id=$operator"); command.Parameters.AddWithValue("$operator", op); }
        command.CommandText = $"SELECT count(*),coalesce(sum(provider_attempts),0),coalesce(sum(input_tokens),0),coalesce(sum(cached_input_tokens),0),coalesce(sum(output_tokens),0),coalesce(sum(reasoning_tokens),0),coalesce(sum(visible_output_tokens),0),coalesce(sum(total_tokens),0),coalesce(sum(known_cost),0),coalesce(sum(unresolved_cost_count),0),coalesce(sum(waived_unknown_count),0) FROM execution_usage WHERE {string.Join(" AND ",clauses)}";
        using var reader = command.ExecuteReader(); reader.Read();
        string[] labels = ["Executions","Provider attempts","Input tokens","Cached input tokens","Output tokens","Reasoning tokens","Visible output tokens","Total tokens"];
        output.WriteLine(); for (var i=0;i<labels.Length;i++) output.WriteLine($"  {labels[i]}: {Db(reader,i)}");
        PrintCost(reader,8,9,10);
    }

    void Export()
    {
        var selection = PromptExportPeriod(); if (selection is null) return;
        var defaultPath = UniqueExportPath(selection.Value.Label);
        string? path;
        while (true) { path = Prompt("Absolute CSV output path", defaultPath); if (path is null) return; if (Path.IsPathFullyQualified(path)) break; output.WriteLine("Enter an absolute path."); }
        output.WriteLine(); output.WriteLine($"  Since: {FormatTime(selection.Value.Since)}"); output.WriteLine($"  Output: {path}");
        if (!Confirm("Export this metadata?")) { output.WriteLine("Export cancelled."); return; }
        if (string.Equals(Path.GetDirectoryName(path), exportDirectory, StringComparison.Ordinal)) EnsureExportDirectory();
        InterpretationAdminCommands.ExportUsage(usage, selection.Value.Since, path!); output.WriteLine("Export completed.");
    }

    (DateTime Since, string Label)? PromptExportPeriod()
    {
        while (true)
        {
            var text = Prompt("Start date/time or horizon", "7d"); if (text is null) return null;
            try { return (ParseSince(text), SafeFilePart(text)); }
            catch { output.WriteLine("Enter a UTC date/time, or a horizon such as 24h or 7d."); }
        }
    }

    string UniqueExportPath(string period)
    {
        var stem = $"ftitc-usage-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{period}";
        var path = Path.Combine(exportDirectory, stem + ".csv"); var suffix = 2;
        while (File.Exists(path)) path = Path.Combine(exportDirectory, $"{stem}-{suffix++}.csv");
        return path;
    }

    void EnsureExportDirectory()
    {
        Directory.CreateDirectory(exportDirectory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(exportDirectory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    void Presets()
    {
        while(true)
        {
            output.WriteLine(); output.WriteLine("Generation presets");
            switch(SelectMenu("presets", true,
                new("list", "List"), new("mapping", "Edit mapping"),
                new("description", "Edit description"), new("quota", "Edit quota defaults"),
                new("size", "Request size limits"), new("guidance", "Scientific guidance"),
                new("back", "Back")))
            {case "list":PrintPresets(presets.Read());Pause();break;case "mapping":EditPreset();Pause();break;case "description":EditPresetDescription();Pause();break;case "quota":EditQuotaDefault();Pause();break;case "size":EditRequestSizeLimit();Pause();break;case "guidance":ScientificGuidanceMenu();break;case "back":case null:return;}
            if (cancelRequested) return;
        }
    }

    void ScientificGuidanceMenu()
    {
        while (true)
        {
            output.WriteLine(); output.WriteLine("Scientific guidance");
            PrintGuidance();
            switch (SelectMenu("guidance-menu", true,
                new("change", "Change default"), new("back", "Back")))
            {
                case "change": EditDefaultGuidance(); Pause(); break;
                case "back": case null: return;
            }
            if (cancelRequested) return;
        }
    }

    void PrintGuidance()
    {
        var selected = presets.Read().DefaultGuidanceVariant;
        foreach (var variant in ScientificGuidance.Variants)
            output.WriteLine($"  {variant.Id}: {variant.DisplayName} · {variant.Revision} · {(variant.Id == selected ? "DEFAULT · " : "")}sha256 {ScientificGuidance.Hash(ScientificGuidance.TextFor(variant.Id))}");
    }

    void EditDefaultGuidance()
    {
        var variants = ScientificGuidance.Variants.ToArray();
        var current=presets.Read();
        var choice=SelectMenu("guidance-choice", true, current.DefaultGuidanceVariant, false,
            variants.Select(value => new MenuOption(value.Id, value.DisplayName)).ToArray());
        if(choice is null)return;
        var selected=variants.Single(value => value.Id == choice);
        output.WriteLine($"  Old: {ScientificGuidance.DisplayNameFor(current.DefaultGuidanceVariant)}");
        output.WriteLine($"  New: {selected.DisplayName}");
        if(!Confirm("Apply this default guidance?")){output.WriteLine("Change cancelled.");return;}
        var updated=presets.UpdateDefaultGuidance(selected.Id); output.WriteLine($"Default guidance updated. Revision: {updated.Revision}");
    }

    void EditPreset()
    {
        var current=presets.Read(); PrintPresets(current);
        var all = new[] { current.Summary }.Concat(current.Presets).ToArray();
        var id=SelectMenu("preset-choice", true, all[0].Id, false,
            all.Select(value => new MenuOption(value.Id, value.DisplayName)).ToArray()); if(id is null)return;
        var preset=all.Single(value => value.Id == id);
        var models=options.AllowedModels.Keys.OrderBy(x=>x).ToArray();
        var model=SelectMenu("model-choice", true, preset.Model, false,
            models.Select(value => new MenuOption(value, value)).ToArray()); if(model is null)return;
        var efforts=options.AllowedModels[model].ReasoningEfforts;
        var defaultEffort=efforts.Contains(preset.ReasoningEffort,StringComparer.Ordinal)?preset.ReasoningEffort:efforts[0];
        var effort=SelectMenu("reasoning-choice", true, defaultEffort, false,
            efforts.Select(value => new MenuOption(value, value)).ToArray()); if(effort is null)return;
        output.WriteLine($"  Old: {preset.Model} / {preset.ReasoningEffort}"); output.WriteLine($"  New: {model} / {effort}");
        if(!Confirm("Apply this preset mapping?")){output.WriteLine("Change cancelled.");return;}
        var updated=presets.Update(id,model,effort); output.WriteLine($"Preset updated. Revision: {updated.Revision}");
    }

    void PrintPresets(GenerationPresetConfiguration value)
    {
        output.WriteLine($"  Revision: {value.Revision}"); output.WriteLine($"  Modified: {FormatTime(value.ModifiedAtUtc)}");
        output.WriteLine($"  Default scientific guidance: {ScientificGuidance.DisplayNameFor(value.DefaultGuidanceVariant)} ({value.DefaultGuidanceVariant})");
        output.WriteLine($"  {value.Summary.DisplayName} ({value.Summary.Id}): {value.Summary.Model} / {value.Summary.ReasoningEffort} · all tiers · quota-free · retrieval disabled");
        output.WriteLine($"    {value.Summary.Description}");
        foreach(var preset in value.Presets){output.WriteLine($"  {preset.DisplayName} ({preset.Id}): {preset.Model} / {preset.ReasoningEffort}");output.WriteLine($"    {preset.Description}");}
        output.WriteLine($"  Quota accounting started: {FormatTime(value.QuotaAccountingStartedAtUtc)}");
        foreach(var quota in value.Quotas)output.WriteLine($"  {InterpretationAccessTiers.DisplayName(quota.AccessTier)} account: ${quota.MonthlyUsd:0.00} monthly across all interpretations");
        foreach(var limit in value.RequestSizeLimits)output.WriteLine($"  {InterpretationAccessTiers.DisplayName(limit.AccessTier)} request limit: {limit.MaximumKiB} KiB");
    }

    void EditPresetDescription()
    {
        var current=presets.Read(); PrintPresets(current);
        var all = new[] { current.Summary }.Concat(current.Presets).ToArray();
        var id=SelectMenu("description-preset-choice", true, all[0].Id, false,
            all.Select(value => new MenuOption(value.Id, value.DisplayName)).ToArray()); if(id is null)return;
        var preset=all.Single(value => value.Id == id);
        var description=Required($"Description (maximum {GenerationPresetRegistry.MaximumDescriptionLength} characters)"); if(description is null)return;
        output.WriteLine($"  Old: {preset.Description}"); output.WriteLine($"  New: {description}");
        if(!Confirm("Apply this preset description?")){output.WriteLine("Change cancelled.");return;}
        try{var updated=presets.UpdateDescription(id,description);output.WriteLine($"Description updated. Revision: {updated.Revision}");}
        catch(ArgumentException ex){output.WriteLine(ex.Message);}
    }

    void EditQuotaDefault()
    {
        var current=presets.Read(); PrintPresets(current);
        var tier=SelectMenu("quota-tier", true, current.Quotas[0].AccessTier, false,
            current.Quotas.Select(value => new MenuOption(value.AccessTier,
                InterpretationAccessTiers.DisplayName(value.AccessTier) + " account")).ToArray()); if(tier is null)return;
        var selected=current.Quotas.Single(value=>value.AccessTier==tier); var amount=PromptPositiveDecimal("Monthly USD"); if(amount is null)return;
        output.WriteLine($"  Old: ${selected.MonthlyUsd:0.00}"); output.WriteLine($"  New: ${amount:0.00}");
        if(!Confirm("Apply this quota default?")){output.WriteLine("Change cancelled.");return;}
        var updated=presets.UpdateQuota(selected.AccessTier,amount.Value); output.WriteLine($"Quota updated. Revision: {updated.Revision}");
    }

    void EditRequestSizeLimit()
    {
        var current=presets.Read();
        var tier=SelectMenu("request-size-tier", true, current.RequestSizeLimits[0].AccessTier, false,
            current.RequestSizeLimits.Select(value => new MenuOption(value.AccessTier,
                $"{InterpretationAccessTiers.DisplayName(value.AccessTier)}: {value.MaximumKiB} KiB")).ToArray()); if(tier is null)return;
        var selected=current.RequestSizeLimits.Single(value=>value.AccessTier==tier);
        var maximum=PromptPositiveInteger("Maximum request size (KiB)",selected.MaximumKiB,GenerationPresetRegistry.AbsoluteMaximumRequestKiB); if(maximum is null)return;
        output.WriteLine($"  Old: {selected.MaximumKiB} KiB"); output.WriteLine($"  New: {maximum.Value} KiB");
        if(!Confirm("Apply this request-size limit?")){output.WriteLine("Change cancelled.");return;}
        var updated=presets.UpdateRequestSizeLimit(selected.AccessTier,maximum.Value); output.WriteLine($"Request-size limit updated. Revision: {updated.Revision}");
    }

    string? SelectTier(string current)
    {
        return SelectMenu("access-tier", true, current, false,
            new(InterpretationAccessTiers.Standard, "Registered"),
            new(InterpretationAccessTiers.Advanced, "Advanced"),
            new(InterpretationAccessTiers.Administrator, "Administrator"));
    }

    string? Prompt(string label, string? defaultValue = null)
    {
        output.Write(defaultValue is null ? $"{label} (Esc to cancel): " : $"{label} [{defaultValue}] (Esc to cancel): ");
        string? value;
        if (!ReferenceEquals(input, Console.In) || Console.IsInputRedirected)
        {
            value = input.ReadLine();
            if (value == "\u001b") { output.WriteLine("Cancelled"); return null; }
        }
        else value = ReadConsoleLineOrCancel();
        if (value is null) return null;
        value = value.Trim(); return value.Length == 0 ? defaultValue ?? "" : value;
    }

    string? ExactPrompt(string label)
    {
        output.Write($"{label} (Esc to cancel): ");
        if (!ReferenceEquals(input, Console.In) || Console.IsInputRedirected)
        {
            var value = input.ReadLine();
            if (value == "\u001b") { output.WriteLine("Cancelled"); return null; }
            return value;
        }
        return ReadConsoleLineOrCancel();
    }

    string? ReadConsoleLineOrCancel()
    {
        var value = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Escape) { output.WriteLine("Cancelled"); return null; }
            if (key.Key == ConsoleKey.Enter) { output.WriteLine(); return new string(value.ToArray()); }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Count == 0) continue;
                value.RemoveAt(value.Count - 1); output.Write("\b \b"); continue;
            }
            if (char.IsControl(key.KeyChar)) continue;
            value.Add(key.KeyChar); output.Write(key.KeyChar);
        }
    }
    string? SelectMenu(string key, bool allowBack, params MenuOption[] items) =>
        SelectMenu(key, allowBack, items[0].Id, rememberSelection: true, items);

    string? SelectMenu(string key, bool allowBack, string defaultId, params MenuOption[] items) =>
        SelectMenu(key, allowBack, defaultId, rememberSelection: true, items);

    string? SelectMenu(string key, bool allowBack, string defaultId, bool rememberSelection, params MenuOption[] items)
    {
        if (items.Length == 0) throw new ArgumentException("A menu must contain at least one item.", nameof(items));
        var selected = Array.FindIndex(items, item => item.Id == defaultId);
        if (rememberSelection && menuSelections.TryGetValue(key, out var remembered))
        {
            var rememberedIndex = Array.FindIndex(items, item => item.Id == remembered);
            if (rememberedIndex >= 0) selected = rememberedIndex;
        }
        if (selected < 0) selected = 0;

        if (lineMenuFallback)
        {
            while (true)
            {
                RenderMenu(items, selected, ansi: false, clearLines: false);
                var value = input.ReadLine();
                if (value is null || value is "\u001b" or "\b") return null;
                if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    && number >= 1 && number <= items.Length)
                {
                    selected = number - 1;
                    if (rememberSelection) menuSelections[key] = items[selected].Id;
                    return items[selected].Id;
                }
                var direct = Array.FindIndex(items, item => item.Id == value || item.Label == value);
                if (direct >= 0)
                {
                    selected = direct;
                    if (rememberSelection) menuSelections[key] = items[selected].Id;
                    return items[selected].Id;
                }
                output.WriteLine("Invalid menu selection.");
            }
        }

        var ansi = testMode || !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);
        if (ansi) output.Write("\u001b[?25l");
        RenderMenu(items, selected, ansi, clearLines: false);
        try
        {
            while (true)
            {
                var pressed = readKey();
                var previous = selected;
                switch (pressed.Key)
                {
                    case ConsoleKey.UpArrow: selected = selected == 0 ? items.Length - 1 : selected - 1; break;
                    case ConsoleKey.DownArrow: selected = selected == items.Length - 1 ? 0 : selected + 1; break;
                    case ConsoleKey.Home: selected = 0; break;
                    case ConsoleKey.End: selected = items.Length - 1; break;
                    case ConsoleKey.Enter:
                        if (rememberSelection) menuSelections[key] = items[selected].Id;
                        return items[selected].Id;
                    case ConsoleKey.Escape:
                    case ConsoleKey.Backspace:
                        return null;
                    case ConsoleKey.C when (pressed.Modifiers & ConsoleModifiers.Control) != 0:
                        cancelRequested = true;
                        return null;
                }
                if (selected == previous) continue;
                if (ansi) RenderMenu(items, selected, ansi: true, clearLines: true);
                else RenderMenu(items, selected, ansi: false, clearLines: false);
            }
        }
        finally
        {
            if (ansi) output.Write("\u001b[0m\u001b[?25h");
            output.Flush();
        }
    }

    void RenderMenu(IReadOnlyList<MenuOption> items, int selected, bool ansi, bool clearLines)
    {
        if (clearLines) output.Write($"\u001b[{items.Count}A");
        for (var index = 0; index < items.Count; index++)
        {
            if (clearLines) output.Write("\r\u001b[2K");
            var marker = index == selected ? "> " : "  ";
            if (ansi && index == selected)
                output.WriteLine($"{marker}\u001b[7m{items[index].Label}\u001b[0m");
            else output.WriteLine(marker + items[index].Label);
        }
        output.Flush();
    }

    readonly record struct MenuOption(string Id, string Label);
    string? Required(string label) { while (true) { var value=Prompt(label); if(value is null)return null; if(value.Length>0)return value; output.WriteLine("A value is required."); } }
    int? PromptPositiveInteger(string label, int? defaultValue=null, int maximum=int.MaxValue) { while(true){var text=Prompt(label,defaultValue?.ToString(CultureInfo.InvariantCulture));if(text is null)return null;if(int.TryParse(text,out var value)&&value>0&&value<=maximum)return value;output.WriteLine($"Enter a whole number from 1 to {maximum}.");} }
    decimal? PromptPositiveDecimal(string label) { while(true){var text=Prompt(label);if(text is null)return null;if(decimal.TryParse(text,NumberStyles.Number,CultureInfo.InvariantCulture,out var value)&&value>0)return value;output.WriteLine("Enter a positive amount using a decimal point.");} }
    DateTime? PromptSince(string label,string defaultValue) { while(true){var text=Prompt(label,defaultValue);if(text is null)return null;try{return ParseSince(text);}catch{output.WriteLine("Enter a UTC date/time, or a horizon such as 24h or 7d.");}} }
    bool Confirm(string label)
    {
        var answer = Prompt(label + " (y/N)", "N");
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }
    void Pause() { output.Write("Press Enter to continue..."); input.ReadLine(); output.WriteLine(); }
    void PrintCheck(string label,(bool Success,string Detail) check)=>output.WriteLine($"  {label}: {(check.Success ? "OK" : "FAILED")} - {check.Detail}");
    string AccountStatus(OperatorCodeRecord r)=>r.ScrubbedAtUtc is not null?$"scrubbed {FormatTime(r.ScrubbedAtUtc.Value)}":r.RevokedAtUtc is not null?$"revoked {FormatTime(r.RevokedAtUtc.Value)}":r.ExpiresAtUtc is not null&&r.ExpiresAtUtc<=DateTime.UtcNow?"expired":"active";
    static string UserId(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? "public" : reader.GetValue(index).ToString() ?? "public";
    static string Db(SqliteDataReader r,int i)=>r.IsDBNull(i)?"null":Convert.ToString(r.GetValue(i),CultureInfo.InvariantCulture)??"";
    static string DisplayValue(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? "—" : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? "—";
    static string PresetName(SqliteDataReader reader, int index)
    {
        if (reader.IsDBNull(index)) return "Custom";
        var id = Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? "";
        return id switch
        {
            "summary" => "Summary",
            "instant" => "Fast",
            "fast" => "Default",
            "standard" => "Advanced",
            "in-depth" => "Comprehensive",
            _ => id,
        };
    }
    static string ListCost(SqliteDataReader reader, int knownIndex, int unresolvedIndex, int waivedIndex)
    {
        var known = reader.IsDBNull(knownIndex) ? (decimal?)null : Convert.ToDecimal(reader.GetValue(knownIndex), CultureInfo.InvariantCulture);
        var incomplete = (!reader.IsDBNull(unresolvedIndex) && Convert.ToInt64(reader.GetValue(unresolvedIndex), CultureInfo.InvariantCulture) > 0)
            || (!reader.IsDBNull(waivedIndex) && Convert.ToInt64(reader.GetValue(waivedIndex), CultureInfo.InvariantCulture) > 0);
        if (incomplete) return known is decimal subtotal ? $"Cost: unknown (known subtotal ${subtotal:0.0000})" : "Cost: unknown";
        return known is decimal cost ? $"Estimated cost: ${cost:0.0000}" : "Estimated cost: unavailable";
    }
    string DisplayDb(SqliteDataReader reader, int index) => reader.GetName(index).EndsWith("_utc", StringComparison.OrdinalIgnoreCase) ? TimeDb(reader, index) : Db(reader, index);
    string TimeDb(SqliteDataReader reader, int index)
    {
        if (reader.IsDBNull(index)) return "none";
        var value = Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp)
            ? FormatTime(timestamp)
            : value ?? "unknown";
    }
    string FormatTime(DateTime value) => FormatTime(new DateTimeOffset(value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc)));
    string FormatTime(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value.ToUniversalTime(), displayTimeZone).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
    static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }
    static string Seconds(SqliteDataReader r,int i)=>r.IsDBNull(i)?"null":(Convert.ToDouble(r.GetValue(i),CultureInfo.InvariantCulture)/1000d).ToString("0.###",CultureInfo.InvariantCulture);
    static string DisplayColumnName(string value)=>value.EndsWith("_utc",StringComparison.OrdinalIgnoreCase)?value[..^4]:value;
    static string Label(string value)=>CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_',' '));
    static string Compact(string value,int maximum)=>value.Length<=maximum?value:value[..Math.Max(1,maximum-1)]+"…";
    static string SafeFilePart(string value){var chars=value.Trim().ToLowerInvariant().Select(c=>char.IsLetterOrDigit(c)?c:'-').ToArray();var result=new string(chars).Trim('-');while(result.Contains("--",StringComparison.Ordinal))result=result.Replace("--","-",StringComparison.Ordinal);return result.Length==0?"custom":result;}
    static string Safe(Exception ex)=>ex is UnauthorizedAccessException?"permission denied":ex.Message;
    static DateTime ParseSince(string value){if(value.EndsWith('h')&&double.TryParse(value[..^1],NumberStyles.Float,CultureInfo.InvariantCulture,out var h)&&h>0)return DateTime.UtcNow.AddHours(-h);if(value.EndsWith('d')&&double.TryParse(value[..^1],NumberStyles.Float,CultureInfo.InvariantCulture,out var d)&&d>0)return DateTime.UtcNow.AddDays(-d);return DateTime.Parse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal);}
    static async Task<(bool,string)> CheckServiceAsync(string name){try{using var process=Process.Start(new ProcessStartInfo("systemctl",$"is-active {name}"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false});if(process is null)return(false,"could not start systemctl");var text=(await process.StandardOutput.ReadToEndAsync()).Trim();await process.WaitForExitAsync();return(process.ExitCode==0,text.Length==0?"active":text);}catch(Exception ex){return(false,Safe(ex));}}
    static async Task<(bool,string)> CheckEndpointAsync(string url){try{using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(10)};using var response=await client.GetAsync(url);var build=response.Headers.TryGetValues("X-FTITC-Viewer-Build",out var values)?values.FirstOrDefault():null;using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var root=json.RootElement;var available=root.TryGetProperty("available",out var a)?a.ToString():"unknown";var request=root.TryGetProperty("requestSchemaVersion",out var q)?q.GetString():"unknown";var result=root.TryGetProperty("responseSchemaVersion",out var s)?s.GetString():"unknown";return(response.IsSuccessStatusCode,$"HTTP {(int)response.StatusCode}; available={available}; request={request}; response={result}; build={build??"unknown"}");}catch(Exception ex){return(false,Safe(ex));}}
}
