using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AnalysisITC.Web;

public sealed partial class InterpretationUsageStore
{
    public MaintenanceStatus ReadMaintenance()
    {
        EnsureEnabled();
        using var db = Open();
        return ReadMaintenance(db, null);
    }

    /// <summary>Closes durable admission on every instance sharing this ledger.</summary>
    public MaintenanceStatus BeginMaintenance(string reason)
    {
        RequireEvidence(reason);
        EnsureEnabled();
        using var db = Open();
        using var tx = db.BeginTransaction(deferred: false);
        var current = ReadMaintenance(db, tx);
        if (current.Active) return current;
        var now = DateTime.UtcNow;
        using var update = Cmd(db, tx, "UPDATE maintenance_gate SET active=1,reason=$reason,changed_utc=$time WHERE id=1");
        Add(update, "$reason", reason); Add(update, "$time", Iso(now));
        if (update.ExecuteNonQuery() != 1) throw new AccountingUnavailableException("The accounting maintenance gate is missing.");
        RecordAdminEvent(db, tx, "maintenance_started", null, reason);
        tx.Commit();
        return new(true, reason, now);
    }

    /// <summary>Reopens ledger admission; the separate service-availability policy is unchanged.</summary>
    public MaintenanceStatus EndMaintenance()
    {
        EnsureEnabled();
        using var db = Open();
        using var tx = db.BeginTransaction(deferred: false);
        var current = ReadMaintenance(db, tx);
        if (!current.Active) return current;
        EnsureDrained(db, tx);
        var now = DateTime.UtcNow;
        using var update = Cmd(db, tx, "UPDATE maintenance_gate SET active=0,reason=NULL,changed_utc=$time WHERE id=1");
        Add(update, "$time", Iso(now)); update.ExecuteNonQuery();
        RecordAdminEvent(db, tx, "maintenance_ended", null, "Accounting maintenance ended; service availability remains separately controlled.");
        tx.Commit();
        return new(false, null, now);
    }

    /// <summary>
    /// Explicit operator attestation for crash recovery, never a timeout-based release.
    /// Every service instance must actually have stopped before this is called.
    /// Attempt costs remain unknown until separately reconciled.
    /// </summary>
    public int ConfirmStoppedExecutions(string evidence)
    {
        RequireEvidence(evidence);
        EnsureEnabled();
        using var db = Open();
        using var tx = db.BeginTransaction(deferred: false);
        EnsureMaintenance(db, tx);
        var ids = new List<string>();
        using (var query = Cmd(db, tx, "SELECT execution_id FROM executions WHERE completed_utc IS NULL AND lifecycle='admitted' ORDER BY execution_id"))
        using (var reader = query.ExecuteReader())
            while (reader.Read()) ids.Add(reader.GetString(0));
        foreach (var id in ids)
        {
            using var update = Cmd(db, tx, "UPDATE executions SET lifecycle='recovered',completed_utc=$time WHERE execution_id=$id AND completed_utc IS NULL AND lifecycle='admitted'");
            Add(update, "$time", Iso(DateTime.UtcNow)); Add(update, "$id", id);
            update.ExecuteNonQuery();
            RecordAdminEvent(db, tx, "all_hosts_stopped_attestation", id, evidence);
            ReleaseSettledAdministrativeHold(db, tx, id);
        }
        tx.Commit();
        return ids.Count;
    }

    public IReadOnlyList<UnresolvedInterpretationAccounting> InspectUnresolved(string? operatorCodeId = null)
    {
        EnsureEnabled();
        using var db = Open();
        using var query = db.CreateCommand();
        query.CommandText = """
            SELECT e.execution_id,e.operator_code_id,e.client_request_id,s.attempt_number,
                   coalesce(z.billing_state,r.billing_state,'unresolved') AS effective_state,
                   CASE WHEN z.id IS NOT NULL THEN z.verified_cost ELSE r.combined_cost END AS effective_cost
            FROM executions e JOIN attempt_starts s ON s.execution_id=e.execution_id
            LEFT JOIN attempt_receipts r ON r.execution_id=s.execution_id AND r.attempt_number=s.attempt_number
            LEFT JOIN reconciliation_entries z ON z.execution_id=s.execution_id AND z.attempt_number=s.attempt_number
            WHERE coalesce(z.billing_state,r.billing_state,'unresolved') IN ('unresolved','waived_unknown')
              AND ($operator IS NULL OR e.operator_code_id=$operator)
              AND NOT EXISTS(SELECT 1 FROM legacy_settlements h WHERE h.execution_id=e.execution_id)
            ORDER BY e.started_utc,e.execution_id,s.attempt_number
            """;
        Add(query, "$operator", operatorCodeId);
        using var reader = query.ExecuteReader();
        var records = new List<UnresolvedInterpretationAccounting>();
        while (reader.Read()) records.Add(new(reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetDecimal(5)));
        return records;
    }

    public void Reconcile(string executionId, int attemptNumber, decimal? verifiedCost, bool confirmedUnsent,
        string evidence, string? waiverReason = null)
    {
        RequireEvidence(evidence);
        var waived = !string.IsNullOrWhiteSpace(waiverReason);
        if ((verifiedCost.HasValue ? 1 : 0) + (confirmedUnsent ? 1 : 0) + (waived ? 1 : 0) != 1)
            throw new ArgumentException("Choose exactly one verified cost, confirmed non-dispatch, or explicit waiver.");
        if (verifiedCost < 0 || attemptNumber < 1 || string.IsNullOrWhiteSpace(executionId))
            throw new ArgumentException("A valid execution, attempt number and nonnegative cost are required.");
        var state = waived ? "waived_unknown" : confirmedUnsent || verifiedCost == 0 ? "known_zero" : "known";
        decimal? cost = confirmedUnsent ? 0m : verifiedCost;
        EnsureEnabled();
        using var db = Open();
        using var tx = db.BeginTransaction(deferred: false);
        EnsureMaintenance(db, tx);
        EnsureDrained(db, tx);
        using (var historical = Cmd(db, tx, "SELECT 1 FROM legacy_settlements WHERE execution_id=$id"))
        {
            Add(historical,"$id",executionId);
            if (historical.ExecuteScalar() is not null)
                throw new InvalidOperationException("This historical execution already has an audited total settlement. Its original receipts are retained for inspection.");
        }

        using (var query = Cmd(db, tx, "SELECT billing_state,verified_cost,evidence,waiver_reason FROM reconciliation_entries WHERE execution_id=$id AND attempt_number=$n"))
        {
            Add(query, "$id", executionId); Add(query, "$n", attemptNumber);
            using var reader = query.ExecuteReader();
            if (reader.Read())
            {
                var existingCost = reader.IsDBNull(1) ? (decimal?)null : reader.GetDecimal(1);
                var existingWaiver = reader.IsDBNull(3) ? null : reader.GetString(3);
                if (reader.GetString(0) == state && existingCost == cost && reader.GetString(2) == evidence
                    && existingWaiver == waiverReason) return;
                throw new AccountingConflictException(executionId, attemptNumber);
            }
        }
        using (var query = Cmd(db, tx, """
            SELECT coalesce(r.billing_state,'unresolved') FROM attempt_starts s
            LEFT JOIN attempt_receipts r ON r.execution_id=s.execution_id AND r.attempt_number=s.attempt_number
            WHERE s.execution_id=$id AND s.attempt_number=$n
            """))
        {
            Add(query, "$id", executionId); Add(query, "$n", attemptNumber);
            var existing = query.ExecuteScalar() as string;
            if (existing is null) throw new ArgumentException("The provider attempt does not exist.");
            if (existing != "unresolved") throw new AccountingConflictException(executionId, attemptNumber);
        }
        using (var unique = Cmd(db, tx, "CREATE UNIQUE INDEX IF NOT EXISTS ix_reconciliation_attempt ON reconciliation_entries(execution_id,attempt_number)"))
            unique.ExecuteNonQuery();
        using (var insert = Cmd(db, tx, """
            INSERT INTO reconciliation_entries(execution_id,attempt_number,billing_state,verified_cost,evidence,waiver_reason,recorded_utc)
            VALUES($id,$n,$state,$cost,$evidence,$waiver,$time)
            """))
        {
            Add(insert, "$id", executionId); Add(insert, "$n", attemptNumber); Add(insert, "$state", state);
            Add(insert, "$cost", cost); Add(insert, "$evidence", evidence); Add(insert, "$waiver", waiverReason);
            Add(insert, "$time", Iso(DateTime.UtcNow)); insert.ExecuteNonQuery();
        }
        RecordAdminEvent(db, tx, "attempt_reconciled", executionId,
            JsonSerializer.Serialize(new { attemptNumber, state, verifiedCost = cost, evidence, waiverReason }));
        ReleaseSettledAdministrativeHold(db, tx, executionId);
        tx.Commit();
    }

    static void EnsureMaintenance(SqliteConnection db, SqliteTransaction tx)
    {
        if (!ReadMaintenance(db, tx).Active)
            throw new InvalidOperationException("Pause generation with accounting maintenance before reconciliation.");
    }

    static void EnsureDrained(SqliteConnection db, SqliteTransaction tx)
    {
        using var query = Cmd(db, tx, "SELECT count(*) FROM executions WHERE completed_utc IS NULL AND lifecycle='admitted'");
        if (Convert.ToInt64(query.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
            throw new InvalidOperationException("Generation has not drained. Stop every service instance before explicitly confirming abandoned executions.");
    }

    static void ReleaseSettledAdministrativeHold(SqliteConnection db, SqliteTransaction tx, string executionId)
    {
        using var release = Cmd(db, tx, """
            DELETE FROM account_holds WHERE execution_id=$id
              AND EXISTS(SELECT 1 FROM executions e WHERE e.execution_id=$id AND e.completed_utc IS NOT NULL)
              AND NOT EXISTS(
                SELECT 1 FROM attempt_starts s
                LEFT JOIN attempt_receipts r ON r.execution_id=s.execution_id AND r.attempt_number=s.attempt_number
                LEFT JOIN reconciliation_entries z ON z.execution_id=s.execution_id AND z.attempt_number=s.attempt_number
                WHERE s.execution_id=$id AND coalesce(z.billing_state,r.billing_state,'unresolved')='unresolved')
            """);
        Add(release, "$id", executionId); release.ExecuteNonQuery();
    }

    static void RecordAdminEvent(SqliteConnection db, SqliteTransaction tx, string kind, string? executionId, string evidence)
    {
        using (var schema = Cmd(db, tx, """
            CREATE TABLE IF NOT EXISTS accounting_admin_events(
                id INTEGER PRIMARY KEY AUTOINCREMENT,kind TEXT NOT NULL,execution_id TEXT,
                actor TEXT NOT NULL,evidence TEXT NOT NULL,recorded_utc TEXT NOT NULL)
            """)) schema.ExecuteNonQuery();
        using var insert = Cmd(db, tx, "INSERT INTO accounting_admin_events(kind,execution_id,actor,evidence,recorded_utc) VALUES($kind,$id,$actor,$evidence,$time)");
        Add(insert, "$kind", kind); Add(insert, "$id", executionId); Add(insert, "$actor", Environment.UserName);
        Add(insert, "$evidence", evidence); Add(insert, "$time", Iso(DateTime.UtcNow)); insert.ExecuteNonQuery();
    }

    static void RequireEvidence(string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence)) throw new ArgumentException("A reason or evidence reference is required for the accounting audit.");
    }
}
