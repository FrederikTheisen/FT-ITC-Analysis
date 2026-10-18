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

    [Fact]
    public void PublicUnresolvedLiabilityBlocksOnlyItsInstallationAcrossMonthBoundary()
    {
        Directory.CreateDirectory(directory);
        var store = Store();
        var oldMonth = new DateTime(2026, 8, 31, 23, 55, 0, DateTimeKind.Utc);
        var first = new InterpretationUsageRequest { ServerExecutionId = "public-old", ClientRequestId = "old", OperatorCodeId = "install-a", AccessTier = "public", StartedUtc = oldMonth };
        var limits = new PublicAdmissionLimits(10, oldMonth.AddDays(-1), 10m, 100, oldMonth.AddDays(-1), 100m, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(InterpretationAdmissionStatus.Admitted, store.TryAdmit(first, new(false, null, oldMonth, true, limits)).Status);
        store.BeginAttempt("public-old", 1);
        store.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId = "public-old", AttemptNumber = 1, BillingState = "unresolved", TimestampUtc = oldMonth, Outcome = "timeout" });
        first.Outcome = "provider_error"; first.CompletedUtc = oldMonth; first.HttpStatus = 502; first.ProviderAttempts = 1;
        store.FinalizeRequest(first);

        var nextMonth = new DateTime(2026, 9, 1, 0, 5, 0, DateTimeKind.Utc);
        var nextLimits = limits with { InstallationWindowStartUtc = nextMonth.AddDays(-1), GlobalWindowStartUtc = nextMonth.AddDays(-1), MonthStartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) };
        Assert.Equal(InterpretationAdmissionStatus.Unresolved, store.TryAdmit(new InterpretationUsageRequest { ServerExecutionId = "a-next", ClientRequestId = "a-next", OperatorCodeId = "install-a", AccessTier = "public", StartedUtc = nextMonth }, new(false, null, nextMonth, true, nextLimits)).Status);
        var otherInstallation = new InterpretationUsageRequest { ServerExecutionId = "b-next", ClientRequestId = "b-next", OperatorCodeId = "install-b", AccessTier = "public", StartedUtc = nextMonth };
        Assert.Equal(InterpretationAdmissionStatus.Admitted, store.TryAdmit(otherInstallation, new(false, null, nextMonth, true, nextLimits)).Status);
        Assert.Equal(1, store.GetPublicUsage("install-a", nextMonth.AddDays(-1), nextLimits.MonthStartUtc).UnresolvedCount);
        Assert.Equal(1, store.GetPublicUsage(null, nextMonth.AddDays(-1), nextLimits.MonthStartUtc).UnresolvedCount);
        otherInstallation.Outcome="success"; otherInstallation.CompletedUtc=nextMonth; otherInstallation.HttpStatus=200; store.FinalizeRequest(otherInstallation);
        store.BeginMaintenance("settle public liability"); store.Reconcile("public-old",1,.05m,false,"provider record confirms charge"); store.EndMaintenance();
        var afterSettlement = new InterpretationUsageRequest { ServerExecutionId="a-after-settlement",ClientRequestId="a-after-settlement",OperatorCodeId="install-a",AccessTier="public",StartedUtc=nextMonth };
        Assert.Equal(InterpretationAdmissionStatus.Admitted,store.TryAdmit(afterSettlement,new(false,null,nextMonth,true,nextLimits)).Status);
        afterSettlement.Outcome="success"; afterSettlement.CompletedUtc=nextMonth; afterSettlement.HttpStatus=200; store.FinalizeRequest(afterSettlement);

        var waived = new InterpretationUsageRequest { ServerExecutionId="public-waived",ClientRequestId="waived",OperatorCodeId="install-c",AccessTier="public",StartedUtc=nextMonth };
        Assert.Equal(InterpretationAdmissionStatus.Admitted,store.TryAdmit(waived,new(false,null,nextMonth,true,nextLimits)).Status);
        store.BeginAttempt("public-waived",1);
        store.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId="public-waived",AttemptNumber=1,BillingState="unresolved",TimestampUtc=nextMonth,Outcome="timeout" });
        waived.Outcome="provider_error"; waived.CompletedUtc=nextMonth; waived.HttpStatus=502; waived.ProviderAttempts=1;
        store.FinalizeRequest(waived);
        store.BeginMaintenance("waive unavailable public provider charge");
        store.Reconcile("public-waived",1,null,false,"provider billing record unavailable","provider records are no longer available");
        store.EndMaintenance();
        var waivedUsage=store.GetPublicUsage("install-c",nextMonth.AddDays(-1),nextLimits.MonthStartUtc);
        Assert.Equal(0,waivedUsage.UnresolvedCount);
        Assert.Equal(0,store.GetPublicUsage(null,nextMonth.AddDays(-1),nextLimits.MonthStartUtc).UnresolvedCount);
        Assert.Equal(InterpretationAdmissionStatus.Admitted,store.TryAdmit(new InterpretationUsageRequest { ServerExecutionId="c-after-waiver",ClientRequestId="c-after-waiver",OperatorCodeId="install-c",AccessTier="public",StartedUtc=nextMonth },new(false,null,nextMonth,true,nextLimits)).Status);
        using var waivedDb=store.OpenForCommand(); using var waivedQuery=waivedDb.CreateCommand();
        waivedQuery.CommandText="SELECT waived_unknown_count FROM execution_usage WHERE request_id='public-waived'";
        Assert.Equal(1L,(long)waivedQuery.ExecuteScalar()!);
    }

    [Fact]
    public async Task CompetingStoresAdmitOnlyOnePublicRequestAtGlobalLimit()
    {
        Directory.CreateDirectory(directory);
        var firstStore = Store(); var secondStore = Store();
        var now = DateTime.UtcNow;
        var limits = new PublicAdmissionLimits(20, now.AddDays(-1), 100m, 1, now.AddDays(-1), 100m, new DateTime(now.Year,now.Month,1,0,0,0,DateTimeKind.Utc));
        var first = Task.Run(() => firstStore.TryAdmit(new InterpretationUsageRequest { ServerExecutionId="public-one",ClientRequestId="one",OperatorCodeId="install-one",AccessTier="public",StartedUtc=now }, new(false,null,now,true,limits)));
        var second = Task.Run(() => secondStore.TryAdmit(new InterpretationUsageRequest { ServerExecutionId="public-two",ClientRequestId="two",OperatorCodeId="install-two",AccessTier="public",StartedUtc=now }, new(false,null,now,true,limits)));
        var results = await Task.WhenAll(first,second);
        Assert.Single(results, result => result.Status == InterpretationAdmissionStatus.Admitted);
        Assert.Single(results, result => result.Status == InterpretationAdmissionStatus.Exhausted && result.Reason == "interpretation_public_global_request_limit");
        Assert.Equal(1, firstStore.GetPublicUsage(null,now.AddDays(-1),limits.MonthStartUtc).RequestCount);
    }

    [Fact]
    public async Task CompetingStoresAdmitOnlyOneActivePublicRequestPerInstallation()
    {
        Directory.CreateDirectory(directory);
        var firstStore=Store(); var secondStore=Store(); var now=DateTime.UtcNow;
        var limits=new PublicAdmissionLimits(10,now.AddDays(-1),100m,10,now.AddDays(-1),100m,new DateTime(now.Year,now.Month,1,0,0,0,DateTimeKind.Utc));
        var first=Task.Run(()=>firstStore.TryAdmit(new InterpretationUsageRequest { ServerExecutionId="install-one-a",ClientRequestId="a",OperatorCodeId="same-install",AccessTier="public",StartedUtc=now },new(false,null,now,true,limits)));
        var second=Task.Run(()=>secondStore.TryAdmit(new InterpretationUsageRequest { ServerExecutionId="install-one-b",ClientRequestId="b",OperatorCodeId="same-install",AccessTier="public",StartedUtc=now },new(false,null,now,true,limits)));
        var results=await Task.WhenAll(first,second);
        Assert.Single(results,result=>result.Status==InterpretationAdmissionStatus.Admitted);
        Assert.Single(results,result=>result.Status==InterpretationAdmissionStatus.Busy && result.Reason=="public_concurrent");
    }

    [Fact]
    public void PublicRequestCountLimitAndDuplicatePrecedenceAreDurable()
    {
        Directory.CreateDirectory(directory);
        var store = Store(); var now = DateTime.UtcNow;
        var limits = new PublicAdmissionLimits(1,now.AddDays(-1),100m,10,now.AddDays(-1),100m,new DateTime(now.Year,now.Month,1,0,0,0,DateTimeKind.Utc));
        var first = new InterpretationUsageRequest { ServerExecutionId="public-first",ClientRequestId="same",OperatorCodeId="install",AccessTier="public",StartedUtc=now,Outcome="success",CompletedUtc=now,HttpStatus=200 };
        Assert.Equal(InterpretationAdmissionStatus.Admitted,store.TryAdmit(first,new(false,null,now,true,limits)).Status);
        store.FinalizeRequest(first);
        Assert.Equal(InterpretationAdmissionStatus.Exhausted,store.TryAdmit(new InterpretationUsageRequest { ServerExecutionId="public-next",ClientRequestId="next",OperatorCodeId="install",AccessTier="public",StartedUtc=now },new(false,null,now,true,limits)).Status);
        store.BeginMaintenance("duplicate precedence test");
        var duplicate = store.TryAdmit(new InterpretationUsageRequest { ServerExecutionId="public-duplicate",ClientRequestId="same",OperatorCodeId="install",AccessTier="public",StartedUtc=now },new(false,null,now,true,limits));
        Assert.Equal(InterpretationAdmissionStatus.Duplicate,duplicate.Status);
    }

    [Fact]
    public void RejectedPublicExecutionNeverConsumesRequestEligibility()
    {
        Directory.CreateDirectory(directory);
        var store = Store(); var now = DateTime.UtcNow;
        store.RecordRejectedRequest(new InterpretationUsageRequest { ServerExecutionId="public-rejected",ClientRequestId="bad",OperatorCodeId="install",AccessTier="public",StartedUtc=now,CompletedUtc=now,Outcome="rejected",HttpStatus=400 });
        var usage = store.GetPublicUsage("install",now.AddDays(-1),new DateTime(now.Year,now.Month,1,0,0,0,DateTimeKind.Utc));
        Assert.Equal(0,usage.RequestCount);
        using var db = store.OpenForCommand(); using var query=db.CreateCommand(); query.CommandText="SELECT access_tier,request_count_eligible FROM executions WHERE execution_id='public-rejected'";
        using var row=query.ExecuteReader(); Assert.True(row.Read()); Assert.Equal("public",row.GetString(0)); Assert.Equal(0,row.GetInt32(1));
    }

    [Theory]
    [InlineData("instant", "interpretation")]
    [InlineData("standard", "summary")]
    public void EveryPresetAndTaskConsumesTheSharedAccountQuota(string preset, string taskType)
    {
        Directory.CreateDirectory(directory);
        var store = Store();
        var request = new InterpretationUsageRequest { ServerExecutionId = preset + "-exec", ClientRequestId = preset + "-client", OperatorCodeId = "account", EffectivePreset = preset, TaskType = taskType, StartedUtc = DateTime.UtcNow };
        Assert.Equal(InterpretationAdmissionStatus.Admitted, store.TryAdmit(request, null, DateTime.UtcNow.AddDays(-1)).Status);
        store.BeginAttempt(request.ServerExecutionId, 1);
        store.RecordAttempt(new InterpretationUsageAttempt { ServerExecutionId = request.ServerExecutionId, AttemptNumber = 1, BillingState = "known", CombinedCost = .25m, TimestampUtc = DateTime.UtcNow, Outcome = "success" });
        request.CompletedUtc = DateTime.UtcNow; request.Outcome = "success"; request.HttpStatus = 200;
        store.FinalizeRequest(request);

        Assert.Equal(.25m, store.GetOperatorUsage("account", DateTime.UtcNow.AddDays(-1)).KnownCost);
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
