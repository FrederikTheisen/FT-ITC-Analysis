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
            return Task.FromResult(args[0] == "operator-code"
                ? Operator(args.Skip(1).ToArray(), services.GetRequiredService<OperatorCodeRegistry>(), output, error)
                : Usage(args.Skip(1).ToArray(), services.GetRequiredService<InterpretationUsageStore>(), output, error));
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
            int? days = daysText is null ? null : int.Parse(daysText, CultureInfo.InvariantCulture);
            if (noExpiry && days is not null) throw new ArgumentException("Use either --no-expiry or --expires-days.");
            var created = registry.Create(label, days, noExpiry);
            output.WriteLine($"Created operator code {created.Record.Id}.");
            output.WriteLine("This secret is displayed once: " + created.Code);
            return 0;
        }
        if (args[0] == "list")
        {
            foreach (var item in registry.List())
                output.WriteLine($"{item.Id}  {item.Label}  created={item.CreatedAtUtc:O}  expires={(item.ExpiresAtUtc?.ToString("O") ?? "never")}  revoked={(item.RevokedAtUtc?.ToString("O") ?? "no")}");
            return 0;
        }
        if (args[0] == "revoke" && args.Length == 2) return registry.Revoke(args[1]) ? 0 : NotFound(error);
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
            using var command = connection.CreateCommand(); command.CommandText = "SELECT request_id,started_utc,outcome,http_status,effective_model,effective_reasoning,provider_attempts,total_tokens,estimated_cost FROM requests WHERE request_id=$id"; command.Parameters.AddWithValue("$id", args[1]);
            using var reader = command.ExecuteReader(); if (!reader.Read()) return NotFound(error); Row(reader, output); return 0;
        }
        var since = ParseSince(Value(args, "--since") ?? (args[0] == "list" ? "24h" : "7d"));
        if (args[0] == "list")
        {
            var limit = int.TryParse(Value(args, "--limit"), out var parsed) ? Math.Clamp(parsed, 1, 10000) : 100;
            using var command = connection.CreateCommand(); command.CommandText = "SELECT request_id,started_utc,outcome,http_status,effective_model,effective_reasoning,provider_attempts,total_tokens,estimated_cost FROM requests WHERE started_utc >= $since ORDER BY started_utc DESC LIMIT $limit"; command.Parameters.AddWithValue("$since", since.ToString("O")); command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader(); while (reader.Read()) Row(reader, output); return 0;
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

    static string? Value(string[] args,string key) { var i=Array.IndexOf(args,key); return i>=0 && i+1<args.Length ? args[i+1] : null; }
    static DateTime ParseSince(string value) { if (value.EndsWith('h') && double.TryParse(value[..^1],out var h)) return DateTime.UtcNow.AddHours(-h); if(value.EndsWith('d')&&double.TryParse(value[..^1],out var d))return DateTime.UtcNow.AddDays(-d); return DateTime.Parse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal); }
    static string Db(SqliteDataReader reader,int i)=>reader.IsDBNull(i)?"null":Convert.ToString(reader.GetValue(i),CultureInfo.InvariantCulture)??"";
    static void Row(SqliteDataReader r,TextWriter o)=>o.WriteLine($"{Db(r,0)}  {Db(r,1)}  {Db(r,2)}  http={Db(r,3)} model={Db(r,4)} reasoning={Db(r,5)} attempts={Db(r,6)} tokens={Db(r,7)} estimated_cost={Db(r,8)}");
    static string Csv(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
    internal static void ExportUsage(InterpretationUsageStore store, DateTime since, string file)
    {
        using var connection = store.OpenForCommand(); using var command = connection.CreateCommand(); command.CommandText = "SELECT request_id,trace_id,started_utc,completed_utc,operator_code_id,report_id,analysis_ids,request_bytes,generation_profile,requested_model,requested_reasoning,effective_model,effective_reasoning,outcome,http_status,error_code,provider_attempts,input_tokens,cached_input_tokens,cache_write_tokens,output_tokens,reasoning_tokens,visible_output_tokens,total_tokens,estimated_cost FROM requests WHERE started_utc >= $since ORDER BY started_utc"; command.Parameters.AddWithValue("$since",since.ToString("O"));
        using var reader = command.ExecuteReader(); using var writer = new StreamWriter(file, false, new UTF8Encoding(false));
        writer.WriteLine(string.Join(",", Enumerable.Range(0,reader.FieldCount).Select(reader.GetName).Select(Csv)));
        while(reader.Read()) writer.WriteLine(string.Join(",",Enumerable.Range(0,reader.FieldCount).Select(i=>Csv(Db(reader,i)))));
    }
    static int NotFound(TextWriter error){error.WriteLine("Not found.");return 2;} static int Help(TextWriter error){error.WriteLine("Invalid command.");return 2;}
}
