using Microsoft.Data.Sqlite;

namespace AnalysisITC.Web;

public sealed partial class InterpretationUsageStore
{
    // Called inside schema initialization's write transaction, after migration has
    // populated request metadata. Request summaries never supply charge amounts.
    static void RebuildAccountingViews(SqliteConnection db, SqliteTransaction tx)
    {
        using (var command = Cmd(db, tx, """
            DROP VIEW IF EXISTS execution_usage;
            DROP VIEW IF EXISTS execution_accounting;
            DROP VIEW IF EXISTS effective_attempt_accounting;
            CREATE VIEW effective_attempt_accounting AS
            SELECT s.execution_id,s.attempt_number,
                   coalesce(z.billing_state,r.billing_state,'unresolved') AS billing_state,
                   CASE WHEN z.id IS NOT NULL THEN z.verified_cost ELSE r.combined_cost END AS cost
            FROM attempt_starts s
            LEFT JOIN attempt_receipts r ON r.execution_id=s.execution_id AND r.attempt_number=s.attempt_number
            LEFT JOIN reconciliation_entries z ON z.execution_id=s.execution_id AND z.attempt_number=s.attempt_number;
            CREATE VIEW execution_accounting AS
            SELECT e.execution_id,
              CASE WHEN h.billing_state IN ('known','known_zero') THEN h.verified_total_cost ELSE
              coalesce((SELECT sum(a.cost) FROM effective_attempt_accounting a
                WHERE a.execution_id=e.execution_id AND a.billing_state IN ('known','known_zero')),0)
                + coalesce((SELECT sum(l.cost) FROM legacy_balances l WHERE l.execution_id=e.execution_id),0) END AS known_cost,
              CASE WHEN h.execution_id IS NOT NULL THEN 0 ELSE
              (SELECT count(*) FROM effective_attempt_accounting a WHERE a.execution_id=e.execution_id
                AND a.billing_state='unresolved')
                + (SELECT count(*) FROM migration_issues i JOIN migration_execution_map m ON m.legacy_key=i.legacy_key
                   WHERE m.execution_id=e.execution_id AND i.blocking=1
                     AND i.kind IN ('aggregate_mismatch','unknown_request_cost')) END AS unresolved_cost_count,
              CASE WHEN h.billing_state='waived_unknown' THEN 1 WHEN h.execution_id IS NOT NULL THEN 0 ELSE
              (SELECT count(*) FROM effective_attempt_accounting a WHERE a.execution_id=e.execution_id
                AND a.billing_state='waived_unknown') END AS waived_unknown_count,
              (SELECT count(*) FROM attempt_starts s WHERE s.execution_id=e.execution_id) AS attempt_count
            FROM executions e LEFT JOIN legacy_settlements h ON h.execution_id=e.execution_id;
            """)) command.ExecuteNonQuery();

        var columns = new List<string>();
        using (var query = Cmd(db, tx, "SELECT name FROM pragma_table_info('requests') ORDER BY cid"))
        using (var reader = query.ExecuteReader())
            while (reader.Read()) columns.Add(reader.GetString(0));
        // Ownership, correlation and lifecycle times come from the immutable
        // execution identity even before a request summary can be finalized.
        var identity = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["request_id"] = "e.execution_id", ["execution_id"] = "e.execution_id",
            ["client_request_id"] = "e.client_request_id", ["operator_code_id"] = "CASE WHEN h.execution_id IS NOT NULL THEN h.operator_code_id ELSE e.operator_code_id END",
            ["task_type"] = "e.task_type", ["effective_preset"] = "e.effective_preset",
            ["started_utc"] = "coalesce(h.verified_started_utc,e.started_utc)", ["completed_utc"] = "e.completed_utc",
            ["trace_id"] = "coalesce(e.trace_id,r.trace_id)",
            ["provider_attempts"] = "a.attempt_count",
        };
        var selected = columns.Select(column =>
            $"{(identity.TryGetValue(column, out var expression) ? expression : "r." + QuoteIdentifier(column))} AS {QuoteIdentifier(column)}");
        using var view = Cmd(db, tx, "CREATE VIEW execution_usage AS SELECT " + string.Join(",", selected)
            + ",a.known_cost,a.unresolved_cost_count,a.unresolved_cost_count AS unresolved_count,a.waived_unknown_count"
            + " FROM executions e LEFT JOIN requests r ON r.request_id=e.execution_id"
            + " LEFT JOIN legacy_settlements h ON h.execution_id=e.execution_id"
            + " JOIN execution_accounting a ON a.execution_id=e.execution_id");
        view.ExecuteNonQuery();
    }

    static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
