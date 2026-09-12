using AnalysisITC.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class InterpretationUsageStoreDurabilityTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-ledger-" + Guid.NewGuid().ToString("N"));
    string Database => Path.Combine(directory, "usage.db");

    [Fact]
    public void AdmissionClaimSurvivesStoreRecreationAndReceiptIsImmutable()
    {
        Directory.CreateDirectory(directory);
        var first = Store();
        var request = new InterpretationUsageRequest { ServerExecutionId = "exec-1", ClientRequestId = "same", OperatorCodeId = "acct", StartedUtc = DateTime.UtcNow };
        Assert.Equal(InterpretationAdmissionStatus.Admitted, first.TryAdmit(request, 10m, DateTime.UtcNow.AddDays(-1)).Status);
        first.BeginAttempt("exec-1", 1);
        first.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId = "exec-1", AttemptNumber = 1, CombinedCost = .25m, BillingState = "known", TimestampUtc = DateTime.UtcNow, Outcome = "success" });
        first.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId = "exec-1", AttemptNumber = 1, CombinedCost = .25m, BillingState = "known", TimestampUtc = DateTime.UtcNow, Outcome = "success" });
        Assert.Throws<AccountingConflictException>(() => first.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId = "exec-1", AttemptNumber = 1, CombinedCost = .5m, BillingState = "known", TimestampUtc = DateTime.UtcNow, Outcome = "success" }));
        var second = Store();
        Assert.Equal(InterpretationAdmissionStatus.Duplicate, second.TryAdmit(new InterpretationUsageRequest { ServerExecutionId = "exec-2", ClientRequestId = "same", OperatorCodeId = "acct", StartedUtc = DateTime.UtcNow }, 10m, DateTime.UtcNow.AddDays(-1)).Status);
        Assert.Equal(.25m, second.GetOperatorUsage("acct", DateTime.UtcNow.AddDays(-1)).KnownCost);
        request.Outcome="success"; request.CompletedUtc=DateTime.UtcNow; request.HttpStatus=200;
        first.FinalizeRequest(request);
        // A redelivered receipt after endpoint finalization is still harmless.
        second.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId="exec-1", AttemptNumber=1, CombinedCost=.25m, BillingState="known", TimestampUtc=DateTime.UtcNow, Outcome="success" });
        Assert.Throws<AccountingConflictException>(() => second.RecordAttempt(new InterpretationUsageAttempt
        { ServerExecutionId="exec-1", AttemptNumber=1, CombinedCost=.5m, BillingState="known", Outcome="success" }));
    }

    [Fact]
    public void UnresolvedStartedAttemptBlocksQuotaAdmission()
    {
        Directory.CreateDirectory(directory);
        var store = Store();
        var request = new InterpretationUsageRequest { ServerExecutionId = "exec-1", ClientRequestId = "one", OperatorCodeId = "acct", StartedUtc = DateTime.UtcNow };
        Assert.Equal(InterpretationAdmissionStatus.Admitted, store.TryAdmit(request, 10m, DateTime.UtcNow.AddDays(-1)).Status);
        store.BeginAttempt("exec-1", 1);
        Assert.Equal(1, store.GetOperatorUsage("acct", DateTime.UtcNow.AddDays(-1)).UnresolvedCount);
        Assert.Equal(InterpretationAdmissionStatus.Busy, store.TryAdmit(new InterpretationUsageRequest { ServerExecutionId = "exec-2", ClientRequestId = "two", OperatorCodeId = "acct", StartedUtc = DateTime.UtcNow }, 10m, DateTime.UtcNow.AddDays(-1)).Status);
    }

    [Fact]
    public void ReceiptRequiresDurableStartAndRejectsInvalidBilling()
    {
        Directory.CreateDirectory(directory);
        var store = Store();
        var request = new InterpretationUsageRequest { ServerExecutionId = "exec-1", ClientRequestId = "one", OperatorCodeId = "acct", StartedUtc = DateTime.UtcNow };
        Assert.Equal(InterpretationAdmissionStatus.Admitted, store.TryAdmit(request, 10m, DateTime.UtcNow.AddDays(-1)).Status);
        Assert.Throws<InvalidOperationException>(() => store.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId = "exec-1", AttemptNumber = 1, CombinedCost = 1m }));
        store.BeginAttempt("exec-1", 1);
        Assert.Throws<ArgumentException>(() => store.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId = "exec-1", AttemptNumber = 1, BillingState = "known", CombinedCost = null }));
        store.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId = "exec-1", AttemptNumber = 1, BillingState = "known_zero", CombinedCost = 0m });
        store.BeginAttempt("exec-1", 2);
    }

    [Fact]
    public void AdmissionFaultIsExplicitAndDoesNotCreateExecution()
    {
        Directory.CreateDirectory(directory);
        var store = Store();
        store.FaultInjector = phase => phase == "admission" ? new IOException("fault") : null;
        var result = store.TryAdmit(new InterpretationUsageRequest { ServerExecutionId = "exec-1", ClientRequestId = "one", OperatorCodeId = "acct", StartedUtc = DateTime.UtcNow }, 10m, DateTime.UtcNow.AddDays(-1));
        Assert.Equal(InterpretationAdmissionStatus.Unavailable, result.Status);
        using var db = store.OpenForCommand(); using var query = db.CreateCommand(); query.CommandText = "SELECT count(*) FROM executions";
        Assert.Equal(0L, Convert.ToInt64(query.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SameClientCorrelationCanCarryIndependentAccountChargesAndRejectionCannotEraseLedger()
    {
        Directory.CreateDirectory(directory);
        var store = Store();
        AdmitCharge(store, "exec-a", "account-a", .25m, "standard");
        AdmitCharge(store, "exec-b", "account-b", .25m, "standard");

        Assert.Equal(.25m, store.GetOperatorUsage("account-a", DateTime.UtcNow.AddDays(-1)).KnownCost);
        Assert.Equal(.25m, store.GetOperatorUsage("account-b", DateTime.UtcNow.AddDays(-1)).KnownCost);
        using (var db = store.OpenForCommand()) using (var total = db.CreateCommand())
        {
            total.CommandText = "SELECT sum(known_cost) FROM execution_usage WHERE client_request_id=$client";
            total.Parameters.AddWithValue("$client", "shared-client");
            Assert.Equal(.5m, Convert.ToDecimal(total.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
        }

        store.RecordRejectedRequest(new InterpretationUsageRequest
        {
            ServerExecutionId = "rejected-after-charges", ClientRequestId = "shared-client", OperatorCodeId = "account-a",
            StartedUtc = DateTime.UtcNow, CompletedUtc = DateTime.UtcNow, Outcome = "rejected", HttpStatus = 400,
        });
        Assert.Equal(.25m, store.GetOperatorUsage("account-a", DateTime.UtcNow.AddDays(-1)).KnownCost);
        Assert.Equal(.25m, store.GetOperatorUsage("account-b", DateTime.UtcNow.AddDays(-1)).KnownCost);
    }

    [Fact]
    public void TerminalUnresolvedHoldSurvivesMonthBoundaryAndStoreRestart()
    {
        Directory.CreateDirectory(directory);
        var oldMonth = new DateTime(2026, 8, 31, 23, 55, 0, DateTimeKind.Utc);
        var newMonth = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var store = Store();
        var request = new InterpretationUsageRequest { ServerExecutionId = "old-exec", ClientRequestId = "old-client", OperatorCodeId = "account", StartedUtc = oldMonth };
        Assert.Equal(InterpretationAdmissionStatus.Admitted, store.TryAdmit(request, 10m, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)).Status);
        store.BeginAttempt("old-exec", 1);
        store.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId = "old-exec", AttemptNumber = 1, BillingState = "unresolved", TimestampUtc = oldMonth, Outcome = "timeout" });
        request.CompletedUtc = oldMonth.AddMinutes(1); request.Outcome = "timeout"; request.HttpStatus = 504;
        store.FinalizeRequest(request);

        var restarted = Store();
        var result = restarted.TryAdmit(new InterpretationUsageRequest { ServerExecutionId = "new-exec", ClientRequestId = "new-client", OperatorCodeId = "account", StartedUtc = newMonth }, 10m, newMonth);
        Assert.Equal(InterpretationAdmissionStatus.Unresolved, result.Status);
        Assert.Equal(1, restarted.GetOperatorUsage("account", newMonth).UnresolvedCount);
    }

    [Theory]
    [InlineData("instant", "interpretation")]
    [InlineData("standard", "summary")]
    public void ExemptExecutionsRemainAccountedButDoNotConsumeQuota(string preset, string taskType)
    {
        Directory.CreateDirectory(directory);
        var store = Store();
        var request = new InterpretationUsageRequest { ServerExecutionId = preset + "-exec", ClientRequestId = preset + "-client", OperatorCodeId = "account", EffectivePreset = preset, TaskType = taskType, StartedUtc = DateTime.UtcNow };
        Assert.Equal(InterpretationAdmissionStatus.Admitted, store.TryAdmit(request, null, DateTime.UtcNow.AddDays(-1)).Status);
        store.BeginAttempt(request.ServerExecutionId, 1);
        store.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId = request.ServerExecutionId, AttemptNumber = 1, BillingState = "known", CombinedCost = .25m, TimestampUtc = DateTime.UtcNow, Outcome = "success" });
        request.CompletedUtc = DateTime.UtcNow; request.Outcome = "success"; request.HttpStatus = 200;
        store.FinalizeRequest(request);

        Assert.Equal(0m, store.GetOperatorUsage("account", DateTime.UtcNow.AddDays(-1)).KnownCost);
        Assert.Equal(.25m, store.ReadExecutionAccounting(request.ServerExecutionId).KnownCost);
    }

    [Fact]
    public void PartialKnownSpendRemainsVisibleButCannotAdmitAnotherQuotaExecution()
    {
        Directory.CreateDirectory(directory);
        var store = Store();
        var request = new InterpretationUsageRequest { ServerExecutionId = "partial-exec", ClientRequestId = "partial-client", OperatorCodeId = "account", StartedUtc = DateTime.UtcNow };
        Assert.Equal(InterpretationAdmissionStatus.Admitted, store.TryAdmit(request, 10m, DateTime.UtcNow.AddDays(-1)).Status);
        store.BeginAttempt("partial-exec", 1);
        store.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId = "partial-exec", AttemptNumber = 1, BillingState = "known", CombinedCost = .25m, TimestampUtc = DateTime.UtcNow, Outcome = "provider_error" });
        store.BeginAttempt("partial-exec", 2);
        store.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId = "partial-exec", AttemptNumber = 2, BillingState = "unresolved", TimestampUtc = DateTime.UtcNow, Outcome = "timeout" });
        var accounting = store.ReadExecutionAccounting("partial-exec");
        Assert.Equal(.25m, accounting.KnownCost);
        Assert.Equal(1, accounting.UnresolvedCount);
        request.CompletedUtc = DateTime.UtcNow; request.Outcome = "timeout"; request.HttpStatus = 504;
        store.FinalizeRequest(request);
        Assert.Equal(InterpretationAdmissionStatus.Unresolved, store.TryAdmit(new InterpretationUsageRequest { ServerExecutionId = "next-exec", ClientRequestId = "next-client", OperatorCodeId = "account", StartedUtc = DateTime.UtcNow }, 10m, DateTime.UtcNow.AddDays(-1)).Status);
    }

    void AdmitCharge(InterpretationUsageStore store, string execution, string account, decimal cost, string preset)
    {
        var request = new InterpretationUsageRequest { ServerExecutionId = execution, ClientRequestId = "shared-client", OperatorCodeId = account, EffectivePreset = preset, StartedUtc = DateTime.UtcNow };
        Assert.Equal(InterpretationAdmissionStatus.Admitted, store.TryAdmit(request, 10m, DateTime.UtcNow.AddDays(-1)).Status);
        store.BeginAttempt(execution, 1);
        store.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId = execution, AttemptNumber = 1, BillingState = "known", CombinedCost = cost, TimestampUtc = DateTime.UtcNow, Outcome = "success" });
        request.CompletedUtc = DateTime.UtcNow; request.Outcome = "success"; request.HttpStatus = 200;
        store.FinalizeRequest(request);
    }

    InterpretationUsageStore Store() => new(Options.Create(new InterpretationOptions { UsageLog = new InterpretationUsageOptions { Enabled = true, DatabasePath = Database } }), NullLogger<InterpretationUsageStore>.Instance);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
