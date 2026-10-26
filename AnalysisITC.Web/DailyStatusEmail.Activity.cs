using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AnalysisITC.Web;

internal sealed record DailyActivityCost(decimal Known, long Unresolved, long Waived)
{
    public string Description => Unresolved + Waived > 0
        ? $"cost unknown (known subtotal ${Known.ToString("0.0000", CultureInfo.InvariantCulture)}; unresolved {Unresolved}, waived unknown {Waived})"
        : $"estimated cost ${Known.ToString("0.0000", CultureInfo.InvariantCulture)}";
}

internal sealed record DailyUserAction(string Task, string Provider, string Model, string Outcome, long Requests);
internal sealed record DailyUserActivity(string? Id, string Name, string Tier, long Requests, DailyActivityCost Cost,
    long TotalRequests, DailyActivityCost TotalCost, string FirstRequest, string LastRequest, IReadOnlyList<DailyUserAction> Actions);

public sealed partial class DailyStatusEmail
{
    IReadOnlyList<DailyUserActivity> ReadUserActivity(SqliteConnection db, DateTime start, DateTime end)
    {
        var accounts = new Dictionary<string, OperatorCodeRecord>(StringComparer.Ordinal);
        try
        {
            if (operators is not null)
                accounts = operators.List().ToDictionary(record => record.Id, StringComparer.Ordinal);
        }
        catch { /* Activity remains reportable when identity metadata is unavailable. */ }

        var actions = new List<(string? Id, DailyUserAction Action)>();
        using (var query = db.CreateCommand())
        {
            query.CommandText = """
                SELECT operator_code_id,task_type,provider,effective_model,outcome,count(*)
                FROM execution_usage
                WHERE coalesce(effective_preset,'') <> 'daily-diagnostic' AND started_utc >= $start AND started_utc < $end
                GROUP BY operator_code_id,task_type,provider,effective_model,outcome
                ORDER BY task_type,provider,effective_model,outcome
                """;
            query.Parameters.AddWithValue("$start", start.ToString("O", CultureInfo.InvariantCulture));
            query.Parameters.AddWithValue("$end", end.ToString("O", CultureInfo.InvariantCulture));
            using var row = query.ExecuteReader();
            while (row.Read())
                actions.Add((row.IsDBNull(0) ? null : row.GetString(0), new(
                    ReadText(row, 1), ReadText(row, 2), ReadText(row, 3), ReadText(row, 4), row.GetInt64(5))));
        }
        var byUser = actions.ToLookup(item => item.Id, item => item.Action, StringComparer.Ordinal);
        var users = new List<DailyUserActivity>();
        using (var query = db.CreateCommand())
        {
            query.CommandText = """
                WITH period AS (
                  SELECT operator_code_id,coalesce(max(access_tier),'public') AS access_tier,count(*) AS prompts,
                         coalesce(sum(known_cost),0) AS known_cost,coalesce(sum(unresolved_cost_count),0) AS unresolved,
                         coalesce(sum(waived_unknown_count),0) AS waived,min(started_utc) AS first_request,max(started_utc) AS last_request
                  FROM execution_usage WHERE coalesce(effective_preset,'') <> 'daily-diagnostic' AND started_utc >= $start AND started_utc < $end
                  GROUP BY operator_code_id
                ), totals AS (
                  SELECT operator_code_id,count(*) AS prompts,coalesce(sum(known_cost),0) AS known_cost,
                         coalesce(sum(unresolved_cost_count),0) AS unresolved,coalesce(sum(waived_unknown_count),0) AS waived
                  FROM execution_usage WHERE coalesce(effective_preset,'') <> 'daily-diagnostic' GROUP BY operator_code_id
                )
                SELECT period.operator_code_id,period.access_tier,period.prompts,period.known_cost,period.unresolved,period.waived,
                       totals.prompts,totals.known_cost,totals.unresolved,totals.waived,period.first_request,period.last_request
                FROM period JOIN totals ON period.operator_code_id IS totals.operator_code_id
                ORDER BY period.prompts DESC,period.operator_code_id
                """;
            query.Parameters.AddWithValue("$start", start.ToString("O", CultureInfo.InvariantCulture));
            query.Parameters.AddWithValue("$end", end.ToString("O", CultureInfo.InvariantCulture));
            using var row = query.ExecuteReader();
            while (row.Read())
            {
                var id = row.IsDBNull(0) ? null : row.GetString(0);
                var tier = ReadText(row, 1);
                var name = id is not null && accounts.TryGetValue(id, out var account)
                    ? string.IsNullOrWhiteSpace(account.Name) ? account.Label : account.Name
                    : id is null ? "Unattributed public activity" : tier == InterpretationAccessTiers.Public ? "Public installation" : "Unavailable account";
                users.Add(new(id, name, tier, row.GetInt64(2), ReadCost(row, 3), row.GetInt64(6), ReadCost(row, 7),
                    FormatActivityTime(ReadText(row, 10)), FormatActivityTime(ReadText(row, 11)), byUser[id].ToArray()));
            }
        }
        return users;
    }

    static string ReadText(SqliteDataReader row, int index) => row.IsDBNull(index) || string.IsNullOrWhiteSpace(row.GetString(index))
        ? "unknown" : row.GetString(index);

    static DailyActivityCost ReadCost(SqliteDataReader row, int start) => new(
        Convert.ToDecimal(row.GetValue(start), CultureInfo.InvariantCulture), row.GetInt64(start + 1), row.GetInt64(start + 2));

    string FormatActivityTime(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant) ? Format(instant) : "unavailable";

    static string TaskLabel(string task) => task switch { "interpretation" => "Interpretation", "summary" => "Summary", _ => "Unknown task" };
    static string OutcomeLabel(string outcome) => outcome switch
    {
        "success" => "Succeeded", "rejected" => "Rejected", "cancelled" => "Cancelled", "timeout" => "Timed out",
        "provider_error" => "Provider error", "server_error" => "Server error", _ => "Unknown outcome",
    };

    static void AddUserActivity(List<string> lines, IReadOnlyList<DailyUserActivity> users)
    {
        lines.Add(""); lines.Add("User activity");
        foreach (var user in users)
        {
            lines.Add(user.Id is null ? $"  {user.Name}" : $"  {user.Name} — {user.Id} ({user.Tier})");
            lines.Add($"    Previous day: {user.Requests} prompt(s); {user.Cost.Description}");
            foreach (var action in user.Actions)
                lines.Add($"    {TaskLabel(action.Task)}: {action.Requests} request(s) — {action.Provider} / {action.Model}; {OutcomeLabel(action.Outcome)}");
            lines.Add($"    First request: {user.FirstRequest}");
            lines.Add($"    Last request: {user.LastRequest}");
            lines.Add($"    All time: {user.TotalRequests} prompt(s); {user.TotalCost.Description}");
        }
        if (users.Count == 0) lines.Add("  No user activity.");
    }
}
