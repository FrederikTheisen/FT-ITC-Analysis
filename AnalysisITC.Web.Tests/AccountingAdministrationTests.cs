using System.Globalization;
using AnalysisITC.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class AccountingAdministrationTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-accounting-admin-" + Guid.NewGuid().ToString("N"));
    readonly InterpretationUsageStore store;

    public AccountingAdministrationTests()
    {
        Directory.CreateDirectory(directory);
        store = new InterpretationUsageStore(Options.Create(new InterpretationOptions
        {
            UsageLog = new() { Enabled = true, DatabasePath = Path.Combine(directory, "usage.db") },
        }), NullLogger<InterpretationUsageStore>.Instance);
    }

    [Fact]
    public async Task SummaryDistinguishesKnownSubtotalFromUnknownTotal()
    {
        AddExecution("known", ".25");
        AddExecution("unknown", null);
        using var services = new ServiceCollection().AddSingleton(store).BuildServiceProvider();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        var result = await InterpretationAdminCommands.RunAsync(
            ["usage-log", "summary", "--since", "2000-01-01"], services, output, error);

        Assert.Equal(0, result);
        Assert.Empty(error.ToString());
        Assert.Contains("known_cost=0.25", output.ToString());
        Assert.Contains("unresolved=1", output.ToString());
        Assert.Contains("total_cost=unknown", output.ToString());
    }

    [Fact]
    public void ExportDistinguishesExecutionAndClientIdentityAndPreservesUnknownCost()
    {
        var executionId = AddExecution("same-client", null);
        var path = Path.Combine(directory, "usage.csv");
        InterpretationAdminCommands.ExportUsage(store, DateTime.MinValue, path);

        var csv = File.ReadAllText(path);
        Assert.Contains("\"server_execution_id\",\"client_request_id\",\"trace_id\"", csv);
        Assert.Contains($"\"{executionId}\",\"same-client\"", csv);
        Assert.Contains("\"known_cost\",\"unresolved_cost_count\",\"waived_unknown_count\",\"total_cost\"", csv);
        Assert.EndsWith("\"0\",\"1\",\"0\",\"null\"", File.ReadAllLines(path)[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReconciliationPreservesOriginalReceiptAndIsIdempotent(bool waive)
    {
        var execution = AddExecution("uncertain", null, "account");
        Assert.Throws<InvalidOperationException>(() => store.Reconcile(execution, 1, .25m, false, "invoice"));
        store.BeginMaintenance("Review provider receipt");
        decimal? cost = waive ? null : .25m;
        var waiver = waive ? "Administrator accepts this unresolved charge" : null;
        store.Reconcile(execution, 1, cost, false, "invoice", waiver);
        store.Reconcile(execution, 1, cost, false, "invoice", waiver);

        var accounting = store.ReadExecutionAccounting(execution);
        Assert.Equal(waive ? 0m : .25m, accounting.KnownCost);
        Assert.Equal(0, accounting.UnresolvedCount);
        Assert.Equal(waive ? 1 : 0, accounting.WaivedUnknownCount);
        Assert.Equal(!waive, accounting.IsComplete);
        if (waive) Assert.Null(store.Aggregate(execution).Cost);
        using (var db = store.OpenForCommand())
        using (var query = db.CreateCommand())
        {
            query.CommandText = "SELECT billing_state,combined_cost FROM attempt_receipts WHERE execution_id=$id";
            query.Parameters.AddWithValue("$id",execution);
            using (var reader = query.ExecuteReader())
            {
                Assert.True(reader.Read()); Assert.Equal("unresolved",reader.GetString(0)); Assert.True(reader.IsDBNull(1));
            }
            query.CommandText = "SELECT count(*) FROM reconciliation_entries WHERE execution_id=$id";
            Assert.Equal(1L,query.ExecuteScalar());
            query.CommandText = "SELECT verified_cost FROM reconciliation_entries WHERE execution_id=$id";
            if (waive) Assert.Equal(DBNull.Value,query.ExecuteScalar());
            query.CommandText = "SELECT count(*) FROM account_holds WHERE execution_id=$id";
            Assert.Equal(0L,query.ExecuteScalar());
        }
        Assert.Throws<AccountingConflictException>(() => store.Reconcile(execution,1,1m,false,"different invoice"));
        store.EndMaintenance();
    }

    [Fact]
    public void AccountHistoryIncludesExecutionWhenFinalSummaryCannotBeWritten()
    {
        var request = PendingExecution("bookkeeping-failed");
        store.BeginAttempt(request.ServerExecutionId, 1);
        store.RecordAttempt(new InterpretationUsageAttempt
        {
            ServerExecutionId=request.ServerExecutionId, AttemptNumber=1,
            CombinedCost=.25m, Outcome="success", TimestampUtc=DateTime.UtcNow,
        });
        store.FaultInjector = phase => phase == "finalize" ? new IOException("fault") : null;
        Assert.Throws<IOException>(() => store.FinalizeRequest(request));

        var snapshot = store.GetAccountSnapshot(request.OperatorCodeId!);
        Assert.Equal(1, snapshot.TotalRequests);
        Assert.NotNull(snapshot.MostRecentStartedAtUtc);
        Assert.Null(snapshot.MostRecentCompletedAtUtc);
        Assert.Null(snapshot.MostRecentOutcome);
        Assert.Equal(.25m, store.ReadExecutionAccounting(request.ServerExecutionId).KnownCost);
    }

    [Fact]
    public void CrashDrainAttestationKeepsPendingChargeUntilExplicitSettlement()
    {
        var request = PendingExecution("crashed");
        store.BeginAttempt(request.ServerExecutionId, 1);
        store.BeginMaintenance("Service stopped for recovery");
        Assert.Throws<InvalidOperationException>(() => store.Reconcile(request.ServerExecutionId,1,.25m,false,"invoice"));
        Assert.Equal(1,store.ConfirmStoppedExecutions("Every service instance was stopped by the administrator"));
        Assert.Equal(0,store.ConfirmStoppedExecutions("Every service instance was stopped by the administrator"));
        Assert.Equal(1,store.ReadExecutionAccounting(request.ServerExecutionId).UnresolvedCount);
        var pending = Assert.Single(store.InspectUnresolved());
        Assert.Equal(1,pending.AttemptNumber); Assert.Null(pending.Cost);
        store.Reconcile(request.ServerExecutionId,1,null,true,"Provider confirms request was never dispatched");
        Assert.Equal(0,store.ReadExecutionAccounting(request.ServerExecutionId).UnresolvedCount);
        Assert.Throws<InvalidOperationException>(() => store.BeginAttempt(request.ServerExecutionId,2));
        store.EndMaintenance();
    }

    [Fact]
    public void KnownChargesCannotBeErasedByReconciliation()
    {
        var execution = AddExecution("known", ".25");
        store.BeginMaintenance("Review");
        Assert.Throws<AccountingConflictException>(() => store.Reconcile(execution,1,null,false,"review","waive"));
        Assert.Equal(.25m,store.ReadExecutionAccounting(execution).KnownCost);
    }

    [Fact]
    public async Task ConfirmStoppedCommandRequiresExplicitAttestation()
    {
        var request = PendingExecution("still-active");
        store.BeginMaintenance("Review");
        using var services = new ServiceCollection().AddSingleton(store).BuildServiceProvider();
        using var output = new StringWriter(); using var error = new StringWriter();
        var result = await InterpretationAdminCommands.RunAsync(
            ["usage-log","maintenance","confirm-stopped","--evidence","elapsed time"], services, output,error);
        Assert.Equal(1,result);
        Assert.Contains("--all-hosts-stopped",error.ToString());
        using var db = store.OpenForCommand(); using var query = db.CreateCommand();
        query.CommandText="SELECT lifecycle FROM executions WHERE execution_id=$id";
        query.Parameters.AddWithValue("$id",request.ServerExecutionId);
        Assert.Equal("admitted",query.ExecuteScalar());
    }

    [Fact]
    public async Task MigrationCommandSettlesVerifiedAnonymousTotalAndReportsItsAudit()
    {
        using (var db=store.OpenForCommand())
        using (var seed=db.CreateCommand())
        {
            seed.CommandText="""
                DELETE FROM migration_state;
                INSERT INTO requests(request_id,started_utc,completed_utc,outcome,estimated_cost)
                VALUES('cli-old','2026-09-01T12:00:00Z','2026-09-01T12:01:00Z','success',0.5);
                """;
            seed.ExecuteNonQuery();
        }
        var migrated=new InterpretationUsageStore(Options.Create(new InterpretationOptions
        { UsageLog=new() {Enabled=true,DatabasePath=Path.Combine(directory,"usage.db")} }),NullLogger<InterpretationUsageStore>.Instance);
        migrated.BeginMaintenance("Review historical ownership");
        using var services=new ServiceCollection().AddSingleton(migrated).BuildServiceProvider();
        using var output=new StringWriter(); using var error=new StringWriter();
        var result=await InterpretationAdminCommands.RunAsync(
            ["usage-log","migration","reconcile","request:cli-old","--anonymous","--cost","0.50","--evidence","provider invoice"],
            services,output,error);
        Assert.Equal(0,result); Assert.Empty(error.ToString());
        Assert.False(migrated.ReadMigrationStatus().IsBlocked);
        Assert.Equal(.50m,Assert.Single(migrated.ReadLegacySettlements()).VerifiedTotalCost);
        Assert.Equal(0,await InterpretationAdminCommands.RunAsync(["usage-log","migration","status"],services,output,error));
        Assert.Contains("settlement=known",output.ToString());
        Assert.Contains("provider invoice",output.ToString());
    }

    InterpretationUsageRequest PendingExecution(string clientId)
    {
        var request = new InterpretationUsageRequest
        {
            ServerExecutionId=Guid.NewGuid().ToString("N"), ClientRequestId=clientId,
            OperatorCodeId="account", EffectivePreset="standard", StartedUtc=DateTime.UtcNow,
        };
        Assert.Equal(InterpretationAdmissionStatus.Admitted,store.TryAdmit(request,1m,DateTime.MinValue).Status);
        return request;
    }

    string AddExecution(string clientId, string? cost, string? account = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var request = new InterpretationUsageRequest
        {
            ServerExecutionId = id, ClientRequestId = clientId, RequestId = id,
            TraceId = "http-trace", StartedUtc = now, CompletedUtc = now, OperatorCodeId = account,
            EffectivePreset = "standard", Outcome = "admitted",
        };
        Assert.Equal(InterpretationAdmissionStatus.Admitted, store.TryAdmit(request, account is null ? null : 1m, DateTime.MinValue).Status);
        store.BeginAttempt(id, 1);
        store.RecordAttempt(new InterpretationUsageAttempt
        {
            ServerExecutionId = id, RequestId = id, AttemptNumber = 1, TimestampUtc = now,
            Outcome = "success", HttpStatus = 200, Model = "test-model",
            CombinedCost = cost is null ? null : decimal.Parse(cost, CultureInfo.InvariantCulture),
        });
        request.Outcome = "success";
        request.HttpStatus = 200;
        request.CompletedUtc = DateTime.UtcNow;
        store.FinalizeRequest(request);
        return id;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(directory, true);
    }
}
