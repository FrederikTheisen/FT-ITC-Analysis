using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class InteractiveAdminTool
{
    const string LocalStatusUrl = "http://127.0.0.1:5000/api/interpretation/status";
    const string PublicStatusUrl = "https://app.ft-itc.org/api/interpretation/status";
    readonly OperatorCodeRegistry registry;
    readonly InterpretationUsageStore usage;
    readonly InterpretationOptions options;
    readonly TextReader input;
    readonly TextWriter output;
    readonly Func<string, Task<(bool Success, string Detail)>> serviceCheck;
    readonly Func<string, Task<(bool Success, string Detail)>> endpointCheck;

    InteractiveAdminTool(IServiceProvider services, TextReader input, TextWriter output,
        Func<string, Task<(bool Success, string Detail)>>? serviceCheck = null,
        Func<string, Task<(bool Success, string Detail)>>? endpointCheck = null)
    {
        registry = services.GetRequiredService<OperatorCodeRegistry>();
        usage = services.GetRequiredService<InterpretationUsageStore>();
        options = services.GetRequiredService<IOptions<InterpretationOptions>>().Value;
        this.input = input;
        this.output = output;
        this.serviceCheck = serviceCheck ?? CheckServiceAsync;
        this.endpointCheck = endpointCheck ?? CheckEndpointAsync;
    }

    public static Task<int> RunAsync(IServiceProvider services, TextReader input, TextWriter output) =>
        new InteractiveAdminTool(services, input, output).RunAsync();

    internal static InteractiveAdminTool CreateForTests(IServiceProvider services, TextReader input, TextWriter output,
        Func<string, Task<(bool Success, string Detail)>> serviceCheck,
        Func<string, Task<(bool Success, string Detail)>> endpointCheck) =>
        new(services, input, output, serviceCheck, endpointCheck);

    internal async Task<int> RunAsync()
    {
        output.WriteLine("FT-ITC administration");
        while (true)
        {
            output.WriteLine();
            output.WriteLine("1. Status");
            output.WriteLine("2. Operator accounts");
            output.WriteLine("3. Logs");
            output.WriteLine("4. Exit");
            switch (Prompt("Select an option"))
            {
                case "1": await StatusAsync(); Pause(); break;
                case "2": Accounts(); break;
                case "3": Logs(); break;
                case "4": return 0;
                case null: return 0;
                default: output.WriteLine("Please enter a number from 1 to 4."); break;
            }
        }
    }

    async Task StatusAsync()
    {
        output.WriteLine(); output.WriteLine("Service");
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
        output.WriteLine(); output.WriteLine("Usage log");
        try
        {
            using var connection = usage.OpenForCommand(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*),min(started_utc),max(started_utc) FROM requests";
            using var reader = command.ExecuteReader(); reader.Read(); var path = connection.DataSource;
            output.WriteLine($"  Database: {path}");
            output.WriteLine($"  Size: {(File.Exists(path) ? new FileInfo(path).Length : 0):N0} bytes");
            output.WriteLine($"  Requests: {reader.GetInt64(0):N0}");
            output.WriteLine($"  Oldest: {Db(reader, 1)}"); output.WriteLine($"  Newest: {Db(reader, 2)}");
        }
        catch (Exception ex) { output.WriteLine("  Unavailable: " + Safe(ex)); }
    }

    void Accounts()
    {
        while (true)
        {
            output.WriteLine(); output.WriteLine("Operator accounts");
            output.WriteLine("1. Create"); output.WriteLine("2. Revoke"); output.WriteLine("3. List"); output.WriteLine("4. Back");
            switch (Prompt("Select an option"))
            {
                case "1": CreateAccount(); Pause(); break;
                case "2": RevokeAccount(); Pause(); break;
                case "3": ListAccounts(); Pause(); break;
                case "4": case null: return;
                default: output.WriteLine("Please enter a number from 1 to 4."); break;
            }
        }
    }

    void CreateAccount()
    {
        var label = Required("Label"); if (label is null) return;
        output.WriteLine("Expiry: 1. 30 days (default)  2. Custom days  3. No expiry");
        int? days = options.OperatorAccess.DefaultLifetimeDays; var noExpiry = false;
        while (true)
        {
            var choice = Prompt("Select expiry", "1"); if (choice is null) return;
            if (choice == "1") break;
            if (choice == "3") { noExpiry = true; days = null; break; }
            if (choice == "2")
            {
                var value = PromptPositiveInteger("Number of days"); if (value is null) return; days = value; break;
            }
            output.WriteLine("Please enter 1, 2 or 3.");
        }
        output.WriteLine(); output.WriteLine("Create operator account"); output.WriteLine($"  Label: {label}");
        output.WriteLine($"  Expiry: {(noExpiry ? "never" : $"{days} days")}");
        if (!Confirm("Create this account?")) { output.WriteLine("Creation cancelled."); return; }
        var created = registry.Create(label, days, noExpiry);
        output.WriteLine(); output.WriteLine($"Created account ID: {created.Record.Id}");
        output.WriteLine("The following code is displayed once. Store it securely:"); output.WriteLine(created.Code);
    }

    void RevokeAccount()
    {
        var records = registry.List(); ListAccounts(records);
        if (records.Count == 0) return;
        var id = Required("Exact account ID to revoke"); if (id is null) return;
        var record = records.SingleOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        if (record is null) { output.WriteLine("No account has that ID."); return; }
        output.WriteLine(); PrintAccount(record);
        if (record.RevokedAtUtc is not null) { output.WriteLine("This account is already revoked."); return; }
        if (!Confirm("Revoke this account?")) { output.WriteLine("Revocation cancelled."); return; }
        output.WriteLine(registry.Revoke(record.Id) ? "Account revoked." : "Account could not be found.");
    }

    void ListAccounts() => ListAccounts(registry.List());
    void ListAccounts(IReadOnlyList<OperatorCodeRecord> records)
    {
        output.WriteLine();
        if (records.Count == 0) { output.WriteLine("No operator accounts."); return; }
        foreach (var record in records.OrderBy(x => x.CreatedAtUtc)) { PrintAccount(record); output.WriteLine(); }
    }

    void PrintAccount(OperatorCodeRecord record)
    {
        output.WriteLine($"  ID: {record.Id}"); output.WriteLine($"  Label: {record.Label}");
        output.WriteLine($"  Created: {record.CreatedAtUtc:O}"); output.WriteLine($"  Expires: {record.ExpiresAtUtc?.ToString("O") ?? "never"}");
        output.WriteLine($"  Status: {AccountStatus(record)}");
    }

    void Logs()
    {
        while (true)
        {
            output.WriteLine(); output.WriteLine("Logs");
            output.WriteLine("1. List"); output.WriteLine("2. Show"); output.WriteLine("3. Summary"); output.WriteLine("4. Export"); output.WriteLine("5. Back");
            switch (Prompt("Select an option"))
            {
                case "1": ListLogs(); Pause(); break;
                case "2": ShowLog(); Pause(); break;
                case "3": Summary(); Pause(); break;
                case "4": Export(); Pause(); break;
                case "5": case null: return;
                default: output.WriteLine("Please enter a number from 1 to 5."); break;
            }
        }
    }

    void ListLogs()
    {
        var since = PromptSince("Time horizon", "24h"); if (since is null) return;
        var limit = PromptPositiveInteger("Maximum entries", 100, 10000); if (limit is null) return;
        using var connection = usage.OpenForCommand(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT request_id,started_utc,outcome,http_status,effective_model,effective_reasoning,provider_attempts,total_tokens,estimated_cost FROM requests WHERE started_utc >= $since ORDER BY started_utc DESC LIMIT $limit";
        command.Parameters.AddWithValue("$since", since.Value.ToString("O")); command.Parameters.AddWithValue("$limit", limit.Value);
        using var reader = command.ExecuteReader(); var count = 0;
        while (reader.Read()) { count++; output.WriteLine($"{Db(reader,0)}  {Db(reader,1)}  {Db(reader,2)}  http={Db(reader,3)}  model={Db(reader,4)}  reasoning={Db(reader,5)}  attempts={Db(reader,6)}  tokens={Db(reader,7)}  estimated_cost={Db(reader,8)}"); }
        if (count == 0) output.WriteLine("No matching requests.");
    }

    void ShowLog()
    {
        var id = Required("Request ID"); if (id is null) return;
        using var connection = usage.OpenForCommand();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT request_id,trace_id,started_utc,completed_utc,operator_code_id,report_id,analysis_ids,request_bytes,generation_profile,requested_model,requested_reasoning,effective_model,effective_reasoning,request_version,response_version,package_version,prompt_version,output_version,knowledge_base_ids,latency_ms,outcome,http_status,error_code,provider_attempts,input_tokens,cached_input_tokens,cache_write_tokens,output_tokens,reasoning_tokens,visible_output_tokens,total_tokens,estimated_cost FROM requests WHERE request_id=$id";
            command.Parameters.AddWithValue("$id", id); using var reader = command.ExecuteReader();
            if (!reader.Read()) { output.WriteLine("No request has that ID."); return; }
            output.WriteLine(); output.WriteLine("Request");
            for (var i = 0; i < reader.FieldCount; i++) output.WriteLine($"  {Label(reader.GetName(i))}: {Db(reader,i)}");
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT attempt_number,timestamp_utc,openai_response_id,provider_request_id,latency_ms,model,reasoning,file_search_enabled,file_search_calls,input_tokens,cached_input_tokens,cache_write_tokens,output_tokens,reasoning_tokens,visible_output_tokens,total_tokens,model_cost,file_search_cost,combined_cost,pricing_revision,outcome,http_status,error_code,context_fallback,retrieval_fallback FROM attempts WHERE request_id=$id ORDER BY attempt_number";
            command.Parameters.AddWithValue("$id", id); using var reader = command.ExecuteReader();
            while (reader.Read()) { output.WriteLine(); output.WriteLine($"Provider attempt {Db(reader,0)}"); for (var i=1;i<reader.FieldCount;i++) output.WriteLine($"  {Label(reader.GetName(i))}: {Db(reader,i)}"); }
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
        command.CommandText = $"SELECT count(*),coalesce(sum(provider_attempts),0),coalesce(sum(input_tokens),0),coalesce(sum(cached_input_tokens),0),coalesce(sum(output_tokens),0),coalesce(sum(reasoning_tokens),0),coalesce(sum(visible_output_tokens),0),coalesce(sum(total_tokens),0),sum(estimated_cost) FROM requests WHERE {string.Join(" AND ",clauses)}";
        using var reader = command.ExecuteReader(); reader.Read();
        string[] labels = ["Requests","Provider attempts","Input tokens","Cached input tokens","Output tokens","Reasoning tokens","Visible output tokens","Total tokens","Estimated cost"];
        output.WriteLine(); for (var i=0;i<labels.Length;i++) output.WriteLine($"  {labels[i]}: {Db(reader,i)}");
    }

    void Export()
    {
        var since = PromptSince("Start date/time or horizon", "7d"); if (since is null) return;
        string? path;
        while (true) { path = Required("Absolute CSV output path"); if (path is null) return; if (Path.IsPathFullyQualified(path)) break; output.WriteLine("Enter an absolute path."); }
        output.WriteLine(); output.WriteLine($"  Since: {since.Value:O}"); output.WriteLine($"  Output: {path}");
        if (!Confirm("Export this metadata?")) { output.WriteLine("Export cancelled."); return; }
        InterpretationAdminCommands.ExportUsage(usage, since.Value, path!); output.WriteLine("Export completed.");
    }

    string? Prompt(string label, string? defaultValue = null)
    {
        output.Write(defaultValue is null ? $"{label}: " : $"{label} [{defaultValue}]: ");
        var value = input.ReadLine(); if (value is null) return null; value = value.Trim(); return value.Length == 0 ? defaultValue ?? "" : value;
    }
    string? Required(string label) { while (true) { var value=Prompt(label); if(value is null)return null; if(value.Length>0)return value; output.WriteLine("A value is required."); } }
    int? PromptPositiveInteger(string label, int? defaultValue=null, int maximum=int.MaxValue) { while(true){var text=Prompt(label,defaultValue?.ToString(CultureInfo.InvariantCulture));if(text is null)return null;if(int.TryParse(text,out var value)&&value>0&&value<=maximum)return value;output.WriteLine($"Enter a whole number from 1 to {maximum}.");} }
    DateTime? PromptSince(string label,string defaultValue) { while(true){var text=Prompt(label,defaultValue);if(text is null)return null;try{return ParseSince(text);}catch{output.WriteLine("Enter a UTC date/time, or a horizon such as 24h or 7d.");}} }
    bool Confirm(string label)
    {
        var answer = Prompt(label + " (y/N)", "N");
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }
    void Pause() { output.Write("Press Enter to continue..."); input.ReadLine(); output.WriteLine(); }
    void PrintCheck(string label,(bool Success,string Detail) check)=>output.WriteLine($"  {label}: {(check.Success ? "OK" : "FAILED")} - {check.Detail}");
    static string AccountStatus(OperatorCodeRecord r)=>r.RevokedAtUtc is not null?$"revoked {r.RevokedAtUtc:O}":r.ExpiresAtUtc is not null&&r.ExpiresAtUtc<=DateTime.UtcNow?"expired":"active";
    static string Db(SqliteDataReader r,int i)=>r.IsDBNull(i)?"null":Convert.ToString(r.GetValue(i),CultureInfo.InvariantCulture)??"";
    static string Label(string value)=>CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_',' '));
    static string Safe(Exception ex)=>ex is UnauthorizedAccessException?"permission denied":ex.Message;
    static DateTime ParseSince(string value){if(value.EndsWith('h')&&double.TryParse(value[..^1],NumberStyles.Float,CultureInfo.InvariantCulture,out var h)&&h>0)return DateTime.UtcNow.AddHours(-h);if(value.EndsWith('d')&&double.TryParse(value[..^1],NumberStyles.Float,CultureInfo.InvariantCulture,out var d)&&d>0)return DateTime.UtcNow.AddDays(-d);return DateTime.Parse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal);}
    static async Task<(bool,string)> CheckServiceAsync(string name){try{using var process=Process.Start(new ProcessStartInfo("systemctl",$"is-active {name}"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false});if(process is null)return(false,"could not start systemctl");var text=(await process.StandardOutput.ReadToEndAsync()).Trim();await process.WaitForExitAsync();return(process.ExitCode==0,text.Length==0?"active":text);}catch(Exception ex){return(false,Safe(ex));}}
    static async Task<(bool,string)> CheckEndpointAsync(string url){try{using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(10)};using var response=await client.GetAsync(url);var build=response.Headers.TryGetValues("X-FTITC-Viewer-Build",out var values)?values.FirstOrDefault():null;using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var root=json.RootElement;var available=root.TryGetProperty("available",out var a)?a.ToString():"unknown";var request=root.TryGetProperty("requestSchemaVersion",out var q)?q.GetString():"unknown";var result=root.TryGetProperty("responseSchemaVersion",out var s)?s.GetString():"unknown";return(response.IsSuccessStatusCode,$"HTTP {(int)response.StatusCode}; available={available}; request={request}; response={result}; build={build??"unknown"}");}catch(Exception ex){return(false,Safe(ex));}}
}
