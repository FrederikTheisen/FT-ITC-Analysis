using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AnalysisITC.Web;

public sealed partial class InterpretationUsageStore
{
    const string LegacyMigrationName = "legacy-usage-v1-v4-to-v6";
    const string HistoricalLossReport = "Historical usage rows surviving migration are preserved; any history already lost through legacy INSERT OR REPLACE cannot be reconstructed from this database and requires an external backup or provider record.";

    void MigrateLegacyTransactional(SqliteConnection db, SqliteTransaction tx)
    {
        using (var completed = Cmd(db, tx, "SELECT 1 FROM migration_state WHERE name=$name"))
        {
            Add(completed, "$name", LegacyMigrationName);
            if (completed.ExecuteScalar() is not null) return;
        }

        var requests = ReadLegacyRequests(db, tx);
        var attempts = ReadLegacyAttempts(db, tx);
        var requestIds = requests.Select(item => item.LegacyRequestId).ToHashSet(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            var execution = GetOrCreateExecution(db, tx, "request:" + request.LegacyRequestId,
                request.ClientRequestId ?? request.LegacyRequestId, request.OperatorCodeId, request.TaskType,
                request.EffectivePreset, request.StartedUtc, request.CompletedUtc, "migrated");
            MigrateRequest(db, tx, request, execution, attempts.Where(item => item.LegacyRequestId == request.LegacyRequestId).ToList());
        }

        foreach (var group in attempts.Where(item => !requestIds.Contains(item.LegacyRequestId)).GroupBy(item => item.LegacyRequestId, StringComparer.Ordinal))
        {
            var execution = GetOrCreateExecution(db, tx, "orphan-attempt:" + group.Key, group.Key, null,
                "interpretation", null, group.Select(item => item.TimestampText).FirstOrDefault(item => item is not null), null, "migration_blocked");
            foreach (var attempt in group) MigrateAttempt(db, tx, attempt, execution);
            RecordIssue(db, tx, "orphan-attempt:" + group.Key, "orphan_attempt",
                $"Attempt rows refer to legacy request '{group.Key}', which has no surviving request row. Ownership and request metadata are unavailable.");
            if (group.Any(item => item.TimestampText is null))
                RecordIssue(db, tx, "orphan-attempt:" + group.Key, "missing_attempt_timestamp", "One or more orphan attempts have no surviving timestamp.");
            if (group.Any(item => item.TimestampText is not null && !TryParseLegacyTime(item.TimestampText, out _)))
                RecordIssue(db, tx, "orphan-attempt:" + group.Key, "invalid_attempt_timestamp", "One or more orphan attempts have an invalid surviving timestamp; the original text remains in the legacy row.");
        }

        RecordIssue(db, tx, "migration", "historical_loss", HistoricalLossReport, blocking: false);
        using (var version = Cmd(db, tx, "UPDATE schema_info SET version=6")) version.ExecuteNonQuery();
        using var state = Cmd(db, tx, "INSERT INTO migration_state(name,completed_utc,limitation_report) VALUES($name,$time,$report)");
        Add(state, "$name", LegacyMigrationName); Add(state, "$time", Iso(DateTime.UtcNow)); Add(state, "$report", HistoricalLossReport); state.ExecuteNonQuery();
    }

    void MigrateRequest(SqliteConnection db, SqliteTransaction tx, LegacyRequest request, string execution, IReadOnlyList<LegacyAttempt> attempts)
    {
        var knownAttemptCost = attempts.Where(item => item.CombinedCost.HasValue).Select(item => item.CombinedCost!.Value).Sum();
        var hasUnknownAttempt = attempts.Any(item => !item.CombinedCost.HasValue);
        if (hasUnknownAttempt)
            RecordIssue(db, tx, "request:" + request.LegacyRequestId, "unknown_attempt_cost",
                $"One or more attempts for legacy request '{request.LegacyRequestId}' have no recorded cost.");
        if (attempts.Count > 0 && request.EstimatedCost.HasValue && request.EstimatedCost.Value != knownAttemptCost)
            RecordIssue(db, tx, "request:" + request.LegacyRequestId, "aggregate_mismatch",
                $"Legacy request aggregate {request.EstimatedCost.Value.ToString(CultureInfo.InvariantCulture)} differs from surviving attempt total {knownAttemptCost.ToString(CultureInfo.InvariantCulture)}; attempt charges are authoritative.");
        if (attempts.Count == 0 && request.EstimatedCost.HasValue)
            InsertLegacyBalance(db, tx, execution, request.OperatorCodeId, request.EstimatedCost.Value);
        if (attempts.Count == 0 && !request.EstimatedCost.HasValue && IsAdmitted(request, 0))
            RecordIssue(db, tx, "request:" + request.LegacyRequestId, "unknown_request_cost", $"Admitted legacy request '{request.LegacyRequestId}' has no surviving attempt or request cost.");
        if (string.IsNullOrWhiteSpace(request.OperatorCodeId) && (attempts.Count > 0 || request.EstimatedCost.HasValue))
            RecordIssue(db, tx, "request:" + request.LegacyRequestId, "unknown_ownership",
                $"Legacy request '{request.LegacyRequestId}' has charge or attempt history without an identifiable operator account.");
        if (request.StartedUtc is null) RecordIssue(db, tx, "request:" + request.LegacyRequestId, "missing_timestamp", "Legacy request has no surviving start timestamp.");
        else if (!TryParseLegacyTime(request.StartedUtc, out _)) RecordIssue(db, tx, "request:" + request.LegacyRequestId, "invalid_timestamp", "Legacy request has an invalid surviving start timestamp; the original text remains in the legacy row.");
        if (request.CompletedUtc is null) RecordIssue(db, tx, "request:" + request.LegacyRequestId, "missing_completed_timestamp", "Legacy request has no surviving completion timestamp.");
        else if (!TryParseLegacyTime(request.CompletedUtc, out _)) RecordIssue(db, tx, "request:" + request.LegacyRequestId, "invalid_completed_timestamp", "Legacy request has an invalid surviving completion timestamp; the original text remains in the legacy row.");
        if (attempts.Any(item => item.TimestampText is null)) RecordIssue(db, tx, "request:" + request.LegacyRequestId, "missing_attempt_timestamp", "One or more legacy attempts have no surviving timestamp.");
        if (attempts.Any(item => item.TimestampText is not null && !TryParseLegacyTime(item.TimestampText, out _))) RecordIssue(db, tx, "request:" + request.LegacyRequestId, "invalid_attempt_timestamp", "One or more legacy attempts have an invalid surviving timestamp; the original text remains in the legacy row.");

        if (attempts.Count > 0 && string.Equals(request.Outcome, "rejected", StringComparison.OrdinalIgnoreCase))
            RecordIssue(db, tx, "request:" + request.LegacyRequestId, "outcome_attempt_inconsistency",
                $"Legacy request '{request.LegacyRequestId}' is marked rejected but has surviving provider attempts; admitted work is retained and must be reviewed.");

        using (var update = Cmd(db, tx, "UPDATE requests SET request_id=$new_id,execution_id=$execution,client_request_id=CASE WHEN client_request_id IS NULL OR client_request_id='' THEN $client ELSE client_request_id END WHERE request_id=$old_id"))
        {
            Add(update, "$new_id", execution); Add(update, "$execution", execution); Add(update, "$client", request.ClientRequestId ?? request.LegacyRequestId); Add(update, "$old_id", request.LegacyRequestId); update.ExecuteNonQuery();
        }
        foreach (var attempt in attempts) MigrateAttempt(db, tx, attempt, execution);
        var effectiveClientRequestId = string.IsNullOrWhiteSpace(request.ClientRequestId) ? request.LegacyRequestId : request.ClientRequestId;
        if (IsAdmitted(request, attempts.Count) && !string.IsNullOrWhiteSpace(request.OperatorCodeId) && !string.IsNullOrWhiteSpace(effectiveClientRequestId))
        {
            using var claim = Cmd(db, tx, "INSERT OR IGNORE INTO submission_claims(operator_code_id,client_request_id,execution_id,claimed_utc) VALUES($operator,$client,$execution,$time)");
            Add(claim, "$operator", request.OperatorCodeId); Add(claim, "$client", effectiveClientRequestId); Add(claim, "$execution", execution); Add(claim, "$time", Iso(DateTime.UtcNow));
            if (claim.ExecuteNonQuery() != 1)
                RecordIssue(db, tx, "request:" + request.LegacyRequestId, "duplicate_claim", $"Legacy claim ({request.OperatorCodeId},{effectiveClientRequestId}) conflicts with another surviving execution.");
        }
    }

    void MigrateAttempt(SqliteConnection db, SqliteTransaction tx, LegacyAttempt attempt, string execution)
    {
        var state = attempt.CombinedCost is null ? "unresolved" : attempt.CombinedCost.Value == 0 ? "known_zero" : "known";
        using (var update = Cmd(db, tx, "UPDATE attempts SET request_id=$new_id WHERE request_id=$old_id AND attempt_number=$number"))
        {
            Add(update, "$new_id", execution); Add(update, "$old_id", attempt.LegacyRequestId); Add(update, "$number", attempt.AttemptNumber); update.ExecuteNonQuery();
        }
        // attempt_starts.started_utc is a required ledger bookkeeping column. An
        // empty value records that the legacy source timestamp was unavailable;
        // the original null/invalid text remains in attempts.timestamp_utc and a
        // blocking migration issue prevents activation until reviewed.
        var sourceTimestamp = attempt.ParsedTimestampUtc is DateTime parsed ? Iso(parsed) : string.Empty;
        using (var start = Cmd(db, tx, "INSERT OR IGNORE INTO attempt_starts(execution_id,attempt_number,started_utc) VALUES($execution,$number,$time)"))
        {
            Add(start, "$execution", execution); Add(start, "$number", attempt.AttemptNumber); Add(start, "$time", sourceTimestamp); start.ExecuteNonQuery();
        }
        using var receipt = Cmd(db, tx, "INSERT OR IGNORE INTO attempt_receipts(execution_id,attempt_number,billing_state,combined_cost,receipt_fingerprint,recorded_utc,provider_request_id,openai_response_id,outcome,error_code,input_tokens,output_tokens,total_tokens) VALUES($execution,$number,$state,$cost,$fingerprint,$time,$provider,$openai,$outcome,$error,$input,$output,$total)");
        Add(receipt, "$execution", execution); Add(receipt, "$number", attempt.AttemptNumber); Add(receipt, "$state", state); Add(receipt, "$cost", attempt.CombinedCost);
        Add(receipt, "$fingerprint", $"legacy|{attempt.LegacyRequestId}|{attempt.AttemptNumber}|{attempt.CombinedCost?.ToString(CultureInfo.InvariantCulture)}|{attempt.ProviderRequestId}|{attempt.OpenAIResponseId}");
        Add(receipt, "$time", Iso(DateTime.UtcNow)); Add(receipt, "$provider", attempt.ProviderRequestId); Add(receipt, "$openai", attempt.OpenAIResponseId); Add(receipt, "$outcome", attempt.Outcome); Add(receipt, "$error", attempt.ErrorCode);
        Add(receipt, "$input", attempt.InputTokens); Add(receipt, "$output", attempt.OutputTokens); Add(receipt, "$total", attempt.TotalTokens); receipt.ExecuteNonQuery();
    }

    string GetOrCreateExecution(SqliteConnection db, SqliteTransaction tx, string legacyKey, string? clientRequestId,
        string? operatorCodeId, string? taskType, string? effectivePreset, string? startedUtc, string? completedUtc, string lifecycle)
    {
        using (var existing = Cmd(db, tx, "SELECT execution_id FROM migration_execution_map WHERE legacy_key=$key"))
        {
            Add(existing, "$key", legacyKey);
            if (existing.ExecuteScalar() is string execution) return execution;
        }
        var id = Guid.NewGuid().ToString("N");
        using (var map = Cmd(db, tx, "INSERT INTO migration_execution_map(legacy_key,execution_id,created_utc) VALUES($key,$id,$time)"))
        {
            Add(map, "$key", legacyKey); Add(map, "$id", id); Add(map, "$time", Iso(DateTime.UtcNow)); map.ExecuteNonQuery();
        }
        using var insert = Cmd(db, tx, "INSERT INTO executions(execution_id,client_request_id,operator_code_id,task_type,effective_preset,started_utc,completed_utc,lifecycle) VALUES($id,$client,$operator,$task,$preset,$started,$completed,$lifecycle)");
        Add(insert, "$id", id); Add(insert, "$client", clientRequestId); Add(insert, "$operator", operatorCodeId); Add(insert, "$task", taskType); Add(insert, "$preset", effectivePreset); Add(insert, "$started", startedUtc); Add(insert, "$completed", completedUtc); Add(insert, "$lifecycle", lifecycle); insert.ExecuteNonQuery();
        return id;
    }

    void InsertLegacyBalance(SqliteConnection db, SqliteTransaction tx, string execution, string? operatorCodeId, decimal cost)
    {
        using var balance = Cmd(db, tx, "INSERT OR IGNORE INTO legacy_balances(execution_id,operator_code_id,cost,source,recorded_utc) VALUES($execution,$operator,$cost,'request-summary',$time)");
        Add(balance, "$execution", execution); Add(balance, "$operator", operatorCodeId); Add(balance, "$cost", cost); Add(balance, "$time", Iso(DateTime.UtcNow)); balance.ExecuteNonQuery();
    }

    void RecordIssue(SqliteConnection db, SqliteTransaction tx, string key, string kind, string details, bool blocking = true)
    {
        using var issue = Cmd(db, tx, "INSERT OR IGNORE INTO migration_issues(legacy_key,kind,details,blocking,recorded_utc) VALUES($key,$kind,$details,$blocking,$time)");
        Add(issue, "$key", key); Add(issue, "$kind", kind); Add(issue, "$details", details); Add(issue, "$blocking", blocking ? 1 : 0); Add(issue, "$time", Iso(DateTime.UtcNow)); issue.ExecuteNonQuery();
    }

    static bool IsAdmitted(LegacyRequest request, int attemptCount) =>
        attemptCount > 0 || request.Outcome is not null &&
        (string.Equals(request.Outcome, "success", StringComparison.OrdinalIgnoreCase)
        || string.Equals(request.Outcome, "provider_error", StringComparison.OrdinalIgnoreCase)
        || string.Equals(request.Outcome, "timeout", StringComparison.OrdinalIgnoreCase)
        || string.Equals(request.Outcome, "cancelled", StringComparison.OrdinalIgnoreCase));

    static bool TryParseLegacyTime(string? value, out DateTime parsed) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed)
        && parsed != default;

    static List<LegacyRequest> ReadLegacyRequests(SqliteConnection db, SqliteTransaction tx)
    {
        using var query = Cmd(db, tx, "SELECT request_id,operator_code_id,task_type,effective_preset,started_utc,completed_utc,client_request_id,estimated_cost,outcome FROM requests ORDER BY request_id");
        using var reader = query.ExecuteReader(); var rows = new List<LegacyRequest>();
        while (reader.Read()) rows.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetDecimal(7), reader.IsDBNull(8) ? null : reader.GetString(8)));
        return rows;
    }

    static List<LegacyAttempt> ReadLegacyAttempts(SqliteConnection db, SqliteTransaction tx)
    {
        using var query = Cmd(db, tx, "SELECT request_id,attempt_number,combined_cost,timestamp_utc,provider_request_id,openai_response_id,outcome,error_code,input_tokens,output_tokens,total_tokens FROM attempts ORDER BY request_id,attempt_number");
        using var reader = query.ExecuteReader(); var rows = new List<LegacyAttempt>();
        while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetDecimal(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetInt32(8), reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.IsDBNull(10) ? null : reader.GetInt32(10)));
        return rows;
    }

    public InterpretationMigrationStatus ReadMigrationStatus()
    {
        EnsureEnabled(); EnsureInitialized(); using var db = Open();
        using var query = db.CreateCommand(); query.CommandText = "SELECT count(*) FROM migration_issues WHERE blocking=1";
        var blockers = Convert.ToInt32(query.ExecuteScalar(), CultureInfo.InvariantCulture);
        using var state = db.CreateCommand(); state.CommandText = "SELECT completed_utc,limitation_report FROM migration_state WHERE name=$name"; Add(state, "$name", LegacyMigrationName);
        using var reader = state.ExecuteReader(); return reader.Read() ? new(true, blockers, ParseUtc(reader.GetString(0)), reader.GetString(1)) : new(false, blockers, null, null);
    }

    public IReadOnlyList<InterpretationMigrationIssue> ReadMigrationIssues(bool blockingOnly = false)
    {
        EnsureEnabled(); EnsureInitialized(); using var db = Open(); using var query = db.CreateCommand();
        query.CommandText = "SELECT legacy_key,kind,details,blocking,recorded_utc FROM migration_issues WHERE $all=1 OR blocking=1 ORDER BY id"; Add(query, "$all", blockingOnly ? 0 : 1);
        using var reader = query.ExecuteReader(); var result = new List<InterpretationMigrationIssue>(); while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3) != 0, ParseUtc(reader.GetString(4)))); return result;
    }

    static bool MigrationActivationBlocked(SqliteConnection db, SqliteTransaction tx)
    {
        using var query = Cmd(db, tx, "SELECT count(*) FROM migration_issues WHERE blocking=1");
        return Convert.ToInt32(query.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    bool MigrationActivationBlocked()
    {
        EnsureInitialized(); using var db = Open(); using var query = db.CreateCommand(); query.CommandText = "SELECT count(*) FROM migration_issues WHERE blocking=1";
        return Convert.ToInt32(query.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    sealed record LegacyRequest(string LegacyRequestId, string? OperatorCodeId, string? TaskType, string? EffectivePreset, string? StartedUtc, string? CompletedUtc, string? ClientRequestId, decimal? EstimatedCost, string? Outcome);
    sealed record LegacyAttempt(string LegacyRequestId, int AttemptNumber, decimal? CombinedCost, string? TimestampText, string? ProviderRequestId, string? OpenAIResponseId, string? Outcome, string? ErrorCode, int? InputTokens, int? OutputTokens, int? TotalTokens)
    {
        public DateTime? ParsedTimestampUtc => TryParseLegacyTime(TimestampText, out var value) ? value.ToUniversalTime() : null;
    }
}

public readonly record struct InterpretationMigrationStatus(bool Completed, int ActivationBlockerCount, DateTime? CompletedUtc, string? LimitationReport)
{
    public bool IsBlocked => ActivationBlockerCount > 0;
}

public readonly record struct InterpretationMigrationIssue(string LegacyKey, string Kind, string Details, bool Blocking, DateTime? RecordedUtc);
