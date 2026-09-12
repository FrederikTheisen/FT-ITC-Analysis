using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AnalysisITC.Web;

public sealed partial class InterpretationUsageStore
{
    /// <summary>
    /// Records a reviewed historical execution total, without allocating invented
    /// per-attempt costs or rewriting surviving receipts, ownership or timestamps.
    /// </summary>
    public void ReconcileLegacyExecution(string legacyKey, decimal? verifiedTotalCost, string evidence,
        string? verifiedOperatorCodeId = null, bool confirmedAnonymous = false,
        DateTime? verifiedStartedUtc = null, string? waiverReason = null)
    {
        RequireEvidence(evidence);
        var waived = !string.IsNullOrWhiteSpace(waiverReason);
        if (string.IsNullOrWhiteSpace(legacyKey) || verifiedTotalCost.HasValue == waived || verifiedTotalCost < 0)
            throw new ArgumentException("Supply a legacy key and exactly one verified total cost or explicit waiver.");
        if (confirmedAnonymous && !string.IsNullOrWhiteSpace(verifiedOperatorCodeId))
            throw new ArgumentException("Choose a verified account or confirmed anonymous ownership, not both.");
        if (verifiedStartedUtc is { Kind: not DateTimeKind.Utc } || verifiedStartedUtc == default(DateTime))
            throw new ArgumentException("A verified historical start time must be a nonzero UTC timestamp.");

        EnsureEnabled();
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        EnsureMaintenance(db, tx); EnsureDrained(db, tx);
        string executionId; string? originalAccount, clientId, originalStarted; bool admitted;
        using (var query = Cmd(db, tx, """
            SELECT e.execution_id,e.operator_code_id,e.client_request_id,e.started_utc,
              EXISTS(SELECT 1 FROM attempt_starts s WHERE s.execution_id=e.execution_id)
                OR coalesce(r.outcome,'') IN ('success','provider_error','timeout','cancelled')
            FROM migration_execution_map m JOIN executions e ON e.execution_id=m.execution_id
            LEFT JOIN requests r ON r.request_id=e.execution_id WHERE m.legacy_key=$key
            """))
        {
            Add(query,"$key",legacyKey); using var row = query.ExecuteReader();
            if (!row.Read()) throw new ArgumentException("No migrated execution has that legacy key.");
            executionId=row.GetString(0); originalAccount=row.IsDBNull(1)?null:row.GetString(1);
            clientId=row.IsDBNull(2)?null:row.GetString(2); originalStarted=row.IsDBNull(3)?null:row.GetString(3);
            admitted=row.GetBoolean(4);
        }
        // Known ownership is immutable. Ambiguous ownership may be established
        // by evidence, confirmed anonymous, or explicitly left unknown by waiver.
        var account = string.IsNullOrWhiteSpace(verifiedOperatorCodeId) ? originalAccount : verifiedOperatorCodeId;
        if (!string.IsNullOrWhiteSpace(originalAccount) && (confirmedAnonymous || account != originalAccount))
            throw new InvalidOperationException("A historical settlement cannot reassign known account ownership.");
        if (string.IsNullOrWhiteSpace(account) && !confirmedAnonymous && !waived)
            throw new InvalidOperationException("Verify the historical account or confirm anonymous ownership before settlement.");
        if (!waived && verifiedStartedUtc is null && !TryParseLegacyTime(originalStarted, out _))
            throw new InvalidOperationException("Verify the historical start time before assigning a known charge to a quota period.");
        var state = waived ? "waived_unknown" : verifiedTotalCost == 0 ? "known_zero" : "known";
        var verifiedStart = verifiedStartedUtc is DateTime time ? Iso(time) : null;
        using (var existing = Cmd(db, tx, "SELECT operator_code_id,confirmed_anonymous,verified_started_utc,billing_state,verified_total_cost,waiver_reason,evidence FROM legacy_settlements WHERE execution_id=$id"))
        {
            Add(existing,"$id",executionId); using var row = existing.ExecuteReader();
            if (row.Read())
            {
                string? Text(int column) => row.IsDBNull(column)?null:row.GetString(column);
                if (Text(0)==account && row.GetBoolean(1)==confirmedAnonymous && Text(2)==verifiedStart
                    && Text(3)==state && (row.IsDBNull(4)?(decimal?)null:row.GetDecimal(4))==verifiedTotalCost
                    && Text(5)==waiverReason && Text(6)==evidence) return;
                throw new AccountingConflictException(executionId,0);
            }
        }
        using (var known = Cmd(db, tx, "SELECT known_cost FROM execution_accounting WHERE execution_id=$id"))
        {
            Add(known,"$id",executionId);
            if (verifiedTotalCost is decimal total && total < Convert.ToDecimal(known.ExecuteScalar(),CultureInfo.InvariantCulture))
                throw new InvalidOperationException("A verified legacy total cannot erase surviving authoritative charges.");
        }
        using (var insert = Cmd(db, tx, """
            INSERT INTO legacy_settlements(execution_id,legacy_key,operator_code_id,confirmed_anonymous,
              verified_started_utc,billing_state,verified_total_cost,waiver_reason,evidence,recorded_utc)
            VALUES($id,$key,$account,$anonymous,$started,$state,$cost,$waiver,$evidence,$time)
            """))
        {
            Add(insert,"$id",executionId); Add(insert,"$key",legacyKey); Add(insert,"$account",account);
            Add(insert,"$anonymous",confirmedAnonymous?1:0); Add(insert,"$started",verifiedStart);
            Add(insert,"$state",state); Add(insert,"$cost",verifiedTotalCost); Add(insert,"$waiver",waiverReason);
            Add(insert,"$evidence",evidence); Add(insert,"$time",Iso(DateTime.UtcNow)); insert.ExecuteNonQuery();
        }
        if (admitted && !string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(clientId))
        {
            // A colliding historical key is already consumed; retain both
            // executions and charges without replacing the existing claim.
            using var claim = Cmd(db,tx,"INSERT OR IGNORE INTO submission_claims(operator_code_id,client_request_id,execution_id,claimed_utc) VALUES($account,$client,$id,$time)");
            Add(claim,"$account",account); Add(claim,"$client",clientId); Add(claim,"$id",executionId);
            Add(claim,"$time",Iso(DateTime.UtcNow)); claim.ExecuteNonQuery();
        }
        using (var review = Cmd(db,tx,"UPDATE migration_issues SET blocking=0 WHERE legacy_key=$key"))
        { Add(review,"$key",legacyKey); review.ExecuteNonQuery(); }
        RecordAdminEvent(db,tx,"legacy_execution_reconciled",executionId,
            JsonSerializer.Serialize(new {legacyKey,account,confirmedAnonymous,verifiedStart,state,verifiedTotalCost,waiverReason,evidence}));
        tx.Commit();
    }

    public IReadOnlyList<InterpretationLegacySettlement> ReadLegacySettlements()
    {
        EnsureEnabled(); using var db=Open(); using var query=db.CreateCommand();
        query.CommandText="SELECT legacy_key,execution_id,operator_code_id,billing_state,verified_total_cost,evidence,waiver_reason,verified_started_utc FROM legacy_settlements ORDER BY recorded_utc,legacy_key";
        using var row=query.ExecuteReader(); var result=new List<InterpretationLegacySettlement>();
        while(row.Read()) result.Add(new(row.GetString(0),row.GetString(1),row.IsDBNull(2)?null:row.GetString(2),row.GetString(3),
            row.IsDBNull(4)?null:row.GetDecimal(4),row.GetString(5),row.IsDBNull(6)?null:row.GetString(6),row.IsDBNull(7)?null:ParseUtc(row.GetString(7))));
        return result;
    }
}

public readonly record struct InterpretationLegacySettlement(string LegacyKey,string ServerExecutionId,string? OperatorCodeId,
    string BillingState,decimal? VerifiedTotalCost,string Evidence,string? WaiverReason,DateTime? VerifiedStartedUtc);
