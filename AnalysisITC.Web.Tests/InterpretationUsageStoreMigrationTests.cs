using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class InterpretationUsageStoreMigrationTests
{
    [Fact]
    public void MigratesRequestOnlyLegacyBalanceAndAdmittedClaimExactlyOnce()
    {
        var path = TemporaryPath();
        try
        {
            var settings = Settings(path);
            var initial = Store(settings);
            _ = initial.ReadMigrationStatus();
            ResetMigrationMarker(initial, "DELETE FROM migration_execution_map; DELETE FROM migration_issues; DELETE FROM migration_state; DELETE FROM executions; DELETE FROM submission_claims;");
            InsertRequest(initial, "legacy-request", "operator-a", null, "success", 0.75m);

            var status = Store(settings).ReadMigrationStatus();
            Assert.True(status.Completed);
            Assert.False(status.IsBlocked);
            using var connection = Store(settings).OpenForCommand();
            using var map = connection.CreateCommand(); map.CommandText = "SELECT execution_id FROM migration_execution_map WHERE legacy_key='request:legacy-request'";
            var execution = Assert.IsType<string>(map.ExecuteScalar());
            Assert.Equal(0.75m, Store(settings).ReadExecutionAccounting(execution).KnownCost);
            using var claims = connection.CreateCommand(); claims.CommandText = "SELECT count(*) FROM submission_claims WHERE operator_code_id='operator-a' AND client_request_id='legacy-request'";
            Assert.Equal(1L, (long)claims.ExecuteScalar()!);
            Assert.Equal(0.75m, Store(settings).ReadExecutionAccounting(execution).KnownCost);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void RelinksAttemptsAndDoesNotDoubleCountMatchingRequestAggregate()
    {
        var path = TemporaryPath();
        try
        {
            var settings = Settings(path);
            var initial = Store(settings); _ = initial.ReadMigrationStatus();
            ResetMigrationMarker(initial, "DELETE FROM migration_execution_map; DELETE FROM migration_issues; DELETE FROM migration_state; DELETE FROM executions; DELETE FROM submission_claims;");
            InsertRequest(initial, "legacy-request", "operator-a", null, "provider_error", 0.50m);
            InsertAttempt(initial, "legacy-request", 1, 0.25m);
            InsertAttempt(initial, "legacy-request", 2, 0.25m);

            var store = Store(settings);
            var status = store.ReadMigrationStatus();
            Assert.False(status.IsBlocked);
            using var connection = store.OpenForCommand();
            using var map = connection.CreateCommand(); map.CommandText = "SELECT execution_id FROM migration_execution_map WHERE legacy_key='request:legacy-request'";
            var execution = Assert.IsType<string>(map.ExecuteScalar());
            var accounting = store.ReadExecutionAccounting(execution);
            Assert.Equal(2, accounting.AttemptCount);
            Assert.Equal(0.50m, accounting.KnownCost);
            using var attempts = connection.CreateCommand(); attempts.CommandText = "SELECT count(*) FROM attempts WHERE request_id=$id"; attempts.Parameters.AddWithValue("$id", execution);
            Assert.Equal(2L, (long)attempts.ExecuteScalar()!);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void PreservesOrphansAndUnknownCostsAsActivationBlockersAndRerunsSafely()
    {
        var path = TemporaryPath();
        try
        {
            var settings = Settings(path);
            var initial = Store(settings); _ = initial.ReadMigrationStatus();
            ResetMigrationMarker(initial, "DELETE FROM migration_execution_map; DELETE FROM migration_issues; DELETE FROM migration_state; DELETE FROM executions; DELETE FROM submission_claims;");
            InsertRequest(initial, "legacy-request", null, null, "success", 0.50m);
            InsertAttempt(initial, "legacy-request", 1, null);
            InsertRequest(initial, "mismatch-request", "operator-a", null, "provider_error", 0.50m);
            InsertAttempt(initial, "mismatch-request", 1, 0.25m);
            InsertAttempt(initial, "orphan-request", 1, 0.10m);

            var store = Store(settings);
            var status = store.ReadMigrationStatus();
            Assert.True(status.IsBlocked);
            var issues = store.ReadMigrationIssues(true);
            Assert.Contains(issues, issue => issue.Kind == "unknown_ownership");
            Assert.Contains(issues, issue => issue.Kind == "unknown_attempt_cost");
            Assert.Contains(issues, issue => issue.Kind == "aggregate_mismatch");
            Assert.Contains(issues, issue => issue.Kind == "orphan_attempt");
            var before = issues.Count;
            var rerun = Store(settings).ReadMigrationStatus();
            Assert.Equal(status.ActivationBlockerCount, rerun.ActivationBlockerCount);
            Assert.Equal(before, Store(settings).ReadMigrationIssues(true).Count);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void RetainsAdmittedAttemptWhenLegacyRequestSaysRejectedAndFlagsConflict()
    {
        var path = TemporaryPath();
        try
        {
            var settings = Settings(path);
            var initial = Store(settings); _ = initial.ReadMigrationStatus();
            ResetMigrationMarker(initial, "DELETE FROM migration_execution_map; DELETE FROM migration_issues; DELETE FROM migration_state; DELETE FROM executions; DELETE FROM submission_claims;");
            InsertRequest(initial, "contradictory-request", "operator-a", null, "rejected", 0.25m);
            InsertAttempt(initial, "contradictory-request", 1, 0.25m);

            var store = Store(settings);
            var status = store.ReadMigrationStatus();
            Assert.True(status.IsBlocked);
            Assert.Contains(store.ReadMigrationIssues(true), issue => issue.Kind == "outcome_attempt_inconsistency");
            using var connection = store.OpenForCommand();
            using var map = connection.CreateCommand(); map.CommandText = "SELECT execution_id FROM migration_execution_map WHERE legacy_key='request:contradictory-request'";
            var execution = Assert.IsType<string>(map.ExecuteScalar());
            using var claim = connection.CreateCommand(); claim.CommandText = "SELECT count(*) FROM submission_claims WHERE operator_code_id='operator-a' AND client_request_id='contradictory-request' AND execution_id=$id"; claim.Parameters.AddWithValue("$id", execution);
            Assert.Equal(1L, (long)claim.ExecuteScalar()!);
            Assert.Equal(0.25m, store.ReadExecutionAccounting(execution).KnownCost);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void InvalidLegacyTimesRemainUnchangedAndNeverBecomeCurrentTime()
    {
        var path = TemporaryPath();
        try
        {
            var settings = Settings(path);
            var initial = Store(settings); _ = initial.ReadMigrationStatus();
            ResetMigrationMarker(initial, "DELETE FROM migration_execution_map; DELETE FROM migration_issues; DELETE FROM migration_state; DELETE FROM executions; DELETE FROM submission_claims;");
            InsertRequestRaw(initial, "invalid-time-request", "operator-a", "success", 0.25m, "not-a-time", null);
            InsertAttemptRaw(initial, "invalid-time-request", 1, 0.25m, "also-not-a-time");

            var store = Store(settings);
            var issues = store.ReadMigrationIssues(true);
            Assert.Contains(issues, issue => issue.Kind == "invalid_timestamp");
            Assert.Contains(issues, issue => issue.Kind == "missing_completed_timestamp");
            Assert.Contains(issues, issue => issue.Kind == "invalid_attempt_timestamp");
            using var connection = store.OpenForCommand();
            using var execution = connection.CreateCommand(); execution.CommandText = "SELECT execution_id,started_utc FROM executions WHERE client_request_id='invalid-time-request'";
            using var executionReader = execution.ExecuteReader();
            Assert.True(executionReader.Read());
            var executionId = executionReader.GetString(0);
            Assert.Equal("not-a-time", executionReader.GetString(1));
            executionReader.Close();
            using var source = connection.CreateCommand(); source.CommandText = "SELECT timestamp_utc FROM attempts WHERE request_id=$id"; source.Parameters.AddWithValue("$id", executionId);
            Assert.Equal("also-not-a-time", source.ExecuteScalar());
            using var starts = connection.CreateCommand(); starts.CommandText = "SELECT started_utc FROM attempt_starts WHERE execution_id=$id"; starts.Parameters.AddWithValue("$id", executionId);
            Assert.Equal(string.Empty, starts.ExecuteScalar());
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void ReconcileLegacyKnownTotalIsIdempotentAndDoesNotDoubleCharge()
    {
        var path = TemporaryPath();
        try
        {
            var settings = Settings(path);
            var initial = Store(settings); _ = initial.ReadMigrationStatus();
            ResetMigrationMarker(initial, "DELETE FROM migration_execution_map; DELETE FROM migration_issues; DELETE FROM migration_state; DELETE FROM executions; DELETE FROM submission_claims;");
            InsertRequest(initial, "mismatch-request", "operator-a", null, "provider_error", 0.60m);
            InsertAttempt(initial, "mismatch-request", 1, 0.25m);
            InsertAttempt(initial, "mismatch-request", 2, 0.25m);
            var store = Store(settings); _ = store.ReadMigrationStatus();
            store.BeginMaintenance("settle historical mismatch");
            store.ReconcileLegacyExecution("request:mismatch-request", 0.60m, "provider invoice confirms total", verifiedOperatorCodeId: "operator-a");
            store.ReconcileLegacyExecution("request:mismatch-request", 0.60m, "provider invoice confirms total", verifiedOperatorCodeId: "operator-a");
            Assert.Throws<AccountingConflictException>(() => store.ReconcileLegacyExecution("request:mismatch-request", 0.70m, "conflicting invoice", verifiedOperatorCodeId: "operator-a"));
            store.EndMaintenance();

            var execution = ExecutionFor(store, "request:mismatch-request");
            var accounting = store.ReadExecutionAccounting(execution);
            Assert.Equal(0.60m, accounting.KnownCost);
            Assert.Equal(0, accounting.UnresolvedCount);
            Assert.Contains(store.ReadMigrationIssues(), issue => issue.Kind == "aggregate_mismatch" && !issue.Blocking);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void ReconcileLegacyOwnershipAddsClaimAndQuotaUsesVerifiedOwner()
    {
        var path = TemporaryPath();
        try
        {
            var settings = Settings(path);
            var initial = Store(settings); _ = initial.ReadMigrationStatus();
            ResetMigrationMarker(initial, "DELETE FROM migration_execution_map; DELETE FROM migration_issues; DELETE FROM migration_state; DELETE FROM executions; DELETE FROM submission_claims;");
            InsertRequest(initial, "unowned-request", null, null, "success", 0.50m);
            var store = Store(settings); _ = store.ReadMigrationStatus();
            store.BeginMaintenance("settle historical ownership");
            store.ReconcileLegacyExecution("request:unowned-request", 0.50m, "operator ledger identifies account", verifiedOperatorCodeId: "operator-a");
            store.EndMaintenance();

            var execution = ExecutionFor(store, "request:unowned-request");
            using var connection = store.OpenForCommand();
            using var claim = connection.CreateCommand(); claim.CommandText = "SELECT count(*) FROM submission_claims WHERE operator_code_id='operator-a' AND client_request_id='unowned-request' AND execution_id=$id"; claim.Parameters.AddWithValue("$id", execution);
            Assert.Equal(1L, (long)claim.ExecuteScalar()!);
            Assert.Equal(0.50m, store.GetOperatorUsage("operator-a", DateTime.UtcNow.AddDays(-1)).KnownCost);
            Assert.Contains(store.ReadMigrationIssues(), issue => issue.Kind == "unknown_ownership" && !issue.Blocking);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void ReconcileLegacyUnknownCostWaiverRetainsUnknownState()
    {
        var path = TemporaryPath();
        try
        {
            var settings = Settings(path);
            var initial = Store(settings); _ = initial.ReadMigrationStatus();
            ResetMigrationMarker(initial, "DELETE FROM migration_execution_map; DELETE FROM migration_issues; DELETE FROM migration_state; DELETE FROM executions; DELETE FROM submission_claims;");
            InsertRequest(initial, "unknown-request", null, null, "success", null);
            var store = Store(settings); _ = store.ReadMigrationStatus();
            store.BeginMaintenance("waive unavailable historical cost");
            store.ReconcileLegacyExecution("request:unknown-request", null, "source unavailable", waiverReason: "provider record unavailable");
            store.EndMaintenance();

            var execution = ExecutionFor(store, "request:unknown-request");
            var accounting = store.ReadExecutionAccounting(execution);
            Assert.Equal(0m, accounting.KnownCost);
            Assert.Equal(0, accounting.UnresolvedCount);
            Assert.True(accounting.WaivedUnknownCount > 0);
            Assert.Contains(store.ReadMigrationIssues(), issue => issue.Kind == "unknown_request_cost" && !issue.Blocking);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void KnownLegacySettlementWithInvalidStartRequiresVerifiedStart()
    {
        var path = TemporaryPath();
        try
        {
            var settings = Settings(path);
            var initial = Store(settings); _ = initial.ReadMigrationStatus();
            ResetMigrationMarker(initial, "DELETE FROM migration_execution_map; DELETE FROM migration_issues; DELETE FROM migration_state; DELETE FROM executions; DELETE FROM submission_claims;");
            InsertRequestRaw(initial, "invalid-start-request", "operator-a", "success", 0.25m, "not-a-time", DateTime.UtcNow.ToString("O"));
            var store = Store(settings); _ = store.ReadMigrationStatus();
            store.BeginMaintenance("settle invalid historical start");
            Assert.Throws<InvalidOperationException>(() => store.ReconcileLegacyExecution("request:invalid-start-request", 0.25m, "verified cost", verifiedOperatorCodeId: "operator-a"));
            store.ReconcileLegacyExecution("request:invalid-start-request", 0.25m, "provider record supplies start", verifiedOperatorCodeId: "operator-a", verifiedStartedUtc: DateTime.UtcNow.AddHours(-2));
            store.EndMaintenance();
            Assert.Contains(store.ReadMigrationIssues(), issue => issue.Kind == "invalid_timestamp" && !issue.Blocking);
            Assert.Equal(0.25m, store.GetOperatorUsage("operator-a", DateTime.UtcNow.AddDays(-1)).KnownCost);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void MigrationFailureRollsBackMarkerMappingsVersionAndSourceRows()
    {
        var path = TemporaryPath();
        try
        {
            var settings = Settings(path);
            var initial = Store(settings); _ = initial.ReadMigrationStatus();
            ResetMigrationMarker(initial, "DELETE FROM migration_execution_map; DELETE FROM migration_issues; DELETE FROM migration_state; DELETE FROM executions; DELETE FROM submission_claims;");
            InsertRequest(initial, "rollback-request", "operator-a", null, "success", 0.25m);
            using (var connection = Raw(path))
            using (var trigger = connection.CreateCommand())
            {
                trigger.CommandText = "CREATE TRIGGER fail_legacy_migration BEFORE UPDATE OF request_id ON requests BEGIN SELECT RAISE(ABORT,'migration update blocked'); END;";
                trigger.ExecuteNonQuery();
            }

            Assert.Throws<SqliteException>(() => Store(settings).ReadMigrationStatus());
            using (var connection = Raw(path))
            using (var check = connection.CreateCommand())
            {
                check.CommandText = "SELECT request_id FROM requests WHERE request_id='rollback-request';";
                Assert.Equal("rollback-request", check.ExecuteScalar());
                check.CommandText = "SELECT count(*) FROM migration_execution_map;";
                Assert.Equal(0L, (long)check.ExecuteScalar()!);
                check.CommandText = "SELECT count(*) FROM migration_state;";
                Assert.Equal(0L, (long)check.ExecuteScalar()!);
                check.CommandText = "SELECT version FROM schema_info;";
                Assert.Equal(1L, (long)check.ExecuteScalar()!);
            }

            using (var connection = Raw(path))
            using (var drop = connection.CreateCommand())
            {
                drop.CommandText = "DROP TRIGGER fail_legacy_migration;";
                drop.ExecuteNonQuery();
            }
            Assert.True(Store(settings).ReadMigrationStatus().Completed);
            Assert.Equal(1, Store(settings).ReadMigrationIssues().Count(issue => issue.Kind == "historical_loss"));
        }
        finally { DeleteDatabase(path); }
    }

    static string ExecutionFor(InterpretationUsageStore store, string legacyKey)
    {
        using var connection = store.OpenForCommand();
        using var map = connection.CreateCommand(); map.CommandText = "SELECT execution_id FROM migration_execution_map WHERE legacy_key=$key"; map.Parameters.AddWithValue("$key", legacyKey);
        return Assert.IsType<string>(map.ExecuteScalar());
    }

    static SqliteConnection Raw(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        return connection;
    }

    static InterpretationOptions Settings(string path) => new()
    {
        UsageLog = new InterpretationUsageOptions { Enabled = true, DatabasePath = path },
    };

    static InterpretationUsageStore Store(InterpretationOptions settings) =>
        new(Options.Create(settings), NullLogger<InterpretationUsageStore>.Instance);

    static string TemporaryPath() => Path.Combine(Path.GetTempPath(), "ftitc-migration-" + Guid.NewGuid().ToString("N") + ".db");

    static void ResetMigrationMarker(InterpretationUsageStore store, string sql)
    {
        using var connection = store.OpenForCommand(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
        using var version = connection.CreateCommand(); version.CommandText = "UPDATE schema_info SET version=1"; version.ExecuteNonQuery();
    }

    static void InsertRequest(InterpretationUsageStore store, string id, string? operatorId, string? clientId, string outcome, decimal? cost)
    {
        using var connection = store.OpenForCommand(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO requests(request_id,operator_code_id,client_request_id,task_type,effective_preset,started_utc,completed_utc,outcome,estimated_cost) VALUES($id,$operator,$client,'interpretation','standard',$time,$time,$outcome,$cost)";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$operator", operatorId ?? (object)DBNull.Value); command.Parameters.AddWithValue("$client", clientId ?? (object)DBNull.Value); command.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O")); command.Parameters.AddWithValue("$outcome", outcome); command.Parameters.AddWithValue("$cost", cost ?? (object)DBNull.Value); command.ExecuteNonQuery();
    }

    static void InsertRequestRaw(InterpretationUsageStore store, string id, string? operatorId, string outcome, decimal? cost, string? started, string? completed)
    {
        using var connection = store.OpenForCommand(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO requests(request_id,operator_code_id,client_request_id,task_type,effective_preset,started_utc,completed_utc,outcome,estimated_cost) VALUES($id,$operator,NULL,'interpretation','standard',$started,$completed,$outcome,$cost)";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$operator", operatorId ?? (object)DBNull.Value); command.Parameters.AddWithValue("$started", started ?? (object)DBNull.Value); command.Parameters.AddWithValue("$completed", completed ?? (object)DBNull.Value); command.Parameters.AddWithValue("$outcome", outcome); command.Parameters.AddWithValue("$cost", cost ?? (object)DBNull.Value); command.ExecuteNonQuery();
    }

    static void InsertAttempt(InterpretationUsageStore store, string requestId, int number, decimal? cost)
    {
        using var connection = store.OpenForCommand(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO attempts(request_id,attempt_number,timestamp_utc,combined_cost,outcome,input_tokens,output_tokens,total_tokens) VALUES($id,$number,$time,$cost,'provider_error',10,20,30)";
        command.Parameters.AddWithValue("$id", requestId); command.Parameters.AddWithValue("$number", number); command.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O")); command.Parameters.AddWithValue("$cost", cost ?? (object)DBNull.Value); command.ExecuteNonQuery();
    }

    static void InsertAttemptRaw(InterpretationUsageStore store, string requestId, int number, decimal? cost, string? timestamp)
    {
        using var connection = store.OpenForCommand(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO attempts(request_id,attempt_number,timestamp_utc,combined_cost,outcome,input_tokens,output_tokens,total_tokens) VALUES($id,$number,$time,$cost,'provider_error',10,20,30)";
        command.Parameters.AddWithValue("$id", requestId); command.Parameters.AddWithValue("$number", number); command.Parameters.AddWithValue("$time", timestamp ?? (object)DBNull.Value); command.Parameters.AddWithValue("$cost", cost ?? (object)DBNull.Value); command.ExecuteNonQuery();
    }

    static void DeleteDatabase(string path)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
