using Microsoft.Data.Sqlite;

namespace AnalysisITC.Web;

public sealed partial class InterpretationUsageStore
{
    internal (decimal KnownCost, long UnknownCosts) ReadOpenAiCreditCosts(DateTimeOffset since, DateTimeOffset until)
    {
        EnsureEnabled();
        // A missing ledger must never be created and mistaken for zero spending.
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = options.UsageLog.DatabasePath, Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        db.Open();
        using var query = db.CreateCommand();
        query.CommandText = """
            SELECT coalesce(sum(CASE WHEN a.billing_state IN ('known','known_zero') THEN a.cost ELSE 0 END),0),
                   coalesce(sum(CASE WHEN a.billing_state NOT IN ('known','known_zero') OR a.cost IS NULL THEN 1 ELSE 0 END),0)
            FROM effective_attempt_accounting a
            JOIN attempt_starts s ON s.execution_id=a.execution_id AND s.attempt_number=a.attempt_number
            JOIN executions e ON e.execution_id=a.execution_id
            WHERE e.provider='openai' AND julianday(s.started_utc) >= julianday($since)
              AND julianday(s.started_utc) <= julianday($until)
            """;
        Add(query, "$since", Iso(since.UtcDateTime));
        Add(query, "$until", Iso(until.UtcDateTime));
        using var row = query.ExecuteReader();
        row.Read();
        return (row.GetDecimal(0), row.GetInt64(1));
    }
}
