using System.Globalization;
using System.Text.Json;
using AnalysisITC.Web;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class OpenAiCreditBalanceTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-credit-balance-" + Guid.NewGuid().ToString("N"));
    static readonly DateTimeOffset BaselineTime = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset ReportTime = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    readonly InterpretationOptions configuration;
    readonly InterpretationUsageStore usage;
    string BaselinePath => Path.Combine(directory, "balance.json");

    public OpenAiCreditBalanceTests()
    {
        Directory.CreateDirectory(directory);
        configuration = new()
        {
            AvailabilityPolicyPath = Path.Combine(directory, "availability.json"),
            UsageLog = new() { DatabasePath = Path.Combine(directory, "usage.db") },
        };
        usage = new(Options.Create(configuration), NullLogger<InterpretationUsageStore>.Instance);
        using (usage.OpenForCommand()) { }
        SetBaseline(27.13m, BaselineTime);
    }

    [Fact]
    public void DeductsOnlyOpenAiAttemptsSinceBaselineIncludingRetriesAndDiagnosticsAcrossMonthBoundary()
    {
        Seed("openai", BaselineTime.AddSeconds(-1), 9m);
        Seed("mistral", BaselineTime.AddDays(1), 8m);
        Seed("openai", BaselineTime.AddHours(1), 1.20m, .30m);
        Seed("openai", BaselineTime.AddDays(2), .03m, preset: DailyInterpretationProbe.PresetId);
        Seed("openai", ReportTime.AddSeconds(1), 7m);

        var first = Read();
        var second = Read();

        Assert.False(first.Attention);
        Assert.Contains("Estimated remaining: 25.60 USD (estimate, not a provider balance)", first.Text);
        Assert.Contains("Recorded usage since baseline: 1.5300000 USD", first.Text);
        Assert.Equal(first, second); // Reading/previewing never deducts the same cost again.
    }

    [Fact]
    public void UsesAttemptTimeRatherThanOriginalRequestTimeAndIncludesInFlightReceipts()
    {
        var request = Admit("openai", BaselineTime.AddHours(-1));
        Attempt(request, 1, BaselineTime.AddHours(-1), 9m);
        Attempt(request, 2, BaselineTime, .13m);
        // No request summary has been finalized yet.

        var result = Read();

        Assert.False(result.Attention);
        Assert.Contains("Estimated remaining: 27.00 USD", result.Text);
    }

    [Theory]
    [InlineData("5.01", false)]
    [InlineData("5.00", false)]
    [InlineData("4.99", true)]
    [InlineData("0.00", true)]
    [InlineData("-0.25", true)]
    public void WarnsBelowThresholdAndDoesNotHideNegativeEstimates(string remainingText, bool warning)
    {
        var remaining = decimal.Parse(remainingText, CultureInfo.InvariantCulture);
        Seed("openai", BaselineTime.AddDays(1), 27.13m - remaining);

        var result = Read();

        Assert.Equal(warning, result.Attention);
        Assert.Contains($"Estimated remaining: {remainingText} USD", result.Text);
        Assert.Equal(warning, result.Text.Contains("Balance warning: WARNING", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownAndWaivedCostsNeverProduceAnOptimisticRemainingBalance(bool waived)
    {
        var id = Seed("openai", BaselineTime.AddDays(1), (decimal?)null);
        if (waived)
        {
            usage.BeginMaintenance("credit estimate test");
            usage.Reconcile(id, 1, null, false, "receipt unavailable", "explicit waiver");
        }

        var result = Read();

        Assert.True(result.Attention);
        Assert.Contains("Estimated remaining: unavailable", result.Text);
        Assert.Contains("1 attempt(s) have unknown costs", result.Text);
        Assert.DoesNotContain("Estimated remaining: 27.13", result.Text);
    }

    [Fact]
    public void StartedAttemptWithoutReceiptMakesEstimateIncomplete()
    {
        var request = Admit("openai", BaselineTime.AddDays(1));
        usage.BeginAttempt(request.ServerExecutionId, 1);
        SetAttemptTime(request.ServerExecutionId, 1, BaselineTime.AddDays(1));

        Assert.True(Read().Attention);
        Assert.Contains("Estimated remaining: unavailable", Read().Text);
    }

    [Fact]
    public void UsesVerifiedReconciliationAndIgnoresUnknownCostsOutsideScope()
    {
        Seed("mistral", BaselineTime.AddDays(1), (decimal?)null);
        Seed("openai", BaselineTime.AddSeconds(-1), (decimal?)null);
        var id = Seed("openai", BaselineTime.AddDays(1), (decimal?)null);
        usage.BeginMaintenance("credit estimate test");
        usage.Reconcile(id, 1, .13m, false, "provider invoice");

        Assert.False(Read().Attention);
        Assert.Contains("Estimated remaining: 27.00 USD", Read().Text);
    }

    [Fact]
    public void ReplacingBaselineAfterTopUpExcludesAlreadyAccountedUsage()
    {
        Seed("openai", BaselineTime.AddHours(1), 2m);
        Assert.Contains("Estimated remaining: 25.13 USD", Read().Text);

        SetBaseline(40m, BaselineTime.AddDays(1));
        Seed("openai", BaselineTime.AddDays(2), .25m);

        Assert.Contains("Estimated remaining: 39.75 USD", Read().Text);
        Assert.Contains("Baseline: 40.00 USD", Read().Text);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("missing-amount")]
    [InlineData("missing-time")]
    [InlineData("negative-amount")]
    [InlineData("negative-threshold")]
    [InlineData("future")]
    public void MissingOrInvalidBaselineNeedsAttention(string failure)
    {
        switch (failure)
        {
            case "missing": File.Delete(BaselinePath); break;
            case "malformed": File.WriteAllText(BaselinePath, "not JSON"); break;
            case "missing-amount": File.WriteAllText(BaselinePath, "{\"checkedAtUtc\":\"2026-10-30T12:00:00Z\"}"); break;
            case "missing-time": File.WriteAllText(BaselinePath, "{\"balanceUsd\":27.13}"); break;
            case "negative-amount": SetBaseline(-1m, BaselineTime); break;
            case "negative-threshold": SetBaseline(27.13m, BaselineTime, -1m); break;
            case "future": SetBaseline(27.13m, ReportTime.AddSeconds(1)); break;
        }

        Assert.True(Read().Attention);
        Assert.Contains("Estimated remaining: unavailable", Read().Text);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("disabled")]
    public void UnavailableAccountingNeverCreatesAZeroSpendEstimate(string failure)
    {
        var missing = Path.Combine(directory, "missing.db");
        if (failure == "missing") configuration.UsageLog.DatabasePath = missing;
        if (failure == "corrupt") { configuration.UsageLog.DatabasePath = missing; File.WriteAllText(missing, "not a database"); }
        if (failure == "disabled") configuration.UsageLog.Enabled = false;

        Assert.True(Read().Attention);
        Assert.Contains("Estimated remaining: unavailable", Read().Text);
        if (failure == "missing") Assert.False(File.Exists(missing));
    }

    [Fact]
    public async Task BalanceWarningAppearsInReportOverallStatusAndHtml()
    {
        Seed("openai", BaselineTime.AddDays(1), 23m);

        var report = await Reporter().CreateStatusReportAsync(new(2026, 11, 1));
        var html = DailyStatusEmailLayoutTests.VisibleText(DailyStatusEmail.RenderHtml(report));

        Assert.True(report.HasAttention);
        Assert.Contains("Overall status: ATTENTION", report.Text);
        Assert.Contains("OpenAI credit estimate", html);
        Assert.Contains("4.13 USD", html);
        Assert.Contains("WARNING", html);
        Assert.Contains("not a provider balance", html);
    }

    [Fact]
    public void StandaloneReportLoadsOnlyTheBalancePathAndNotTheProviderKey()
    {
        var path = Path.Combine(directory, "interpretation.env");
        File.WriteAllText(path, "Interpretation__OpenAiCreditBalanceConfigurationPath='/etc/custom/balance.json'\nInterpretation__OpenAI__ApiKey=private-key\n");

        var values = StatusEmailConfigurationLoader.ReadSafeEnvironment(path);

        Assert.Equal("/etc/custom/balance.json", values["Interpretation:OpenAiCreditBalanceConfigurationPath"]);
        Assert.DoesNotContain("private-key", values.Values);
    }

    (bool Attention, string Text) Read()
    {
        var lines = new List<string>();
        var attention = Reporter().AddOpenAiCreditBalance(lines);
        return (attention, string.Join('\n', lines));
    }

    DailyStatusEmail Reporter() => new(usage, new(Options.Create(configuration)), Path.Combine(directory, "mail.json"),
        () => Task.FromResult(new DailyStatusCheck("active", false)),
        _ => Task.FromResult(new DailyStatusCheck("HTTP 200", false)), null, () => ReportTime,
        openAiCreditBalancePath: BaselinePath);

    void SetBaseline(decimal amount, DateTimeOffset timestamp, decimal threshold = 5m) =>
        File.WriteAllText(BaselinePath, JsonSerializer.Serialize(new { balanceUsd = amount, checkedAtUtc = timestamp, warnBelowUsd = threshold }));

    string Seed(string provider, DateTimeOffset timestamp, decimal? cost, decimal? retry = null, string? preset = null)
    {
        var request = Admit(provider, timestamp, preset);
        Attempt(request, 1, timestamp, cost);
        if (retry is not null) Attempt(request, 2, timestamp.AddSeconds(1), retry);
        usage.FinalizeRequest(request);
        return request.ServerExecutionId;
    }

    InterpretationUsageRequest Admit(string provider, DateTimeOffset timestamp, string? preset = null)
    {
        var request = new InterpretationUsageRequest
        {
            ServerExecutionId = Guid.NewGuid().ToString("N"), Provider = provider,
            StartedUtc = timestamp.UtcDateTime, CompletedUtc = timestamp.AddMinutes(1).UtcDateTime,
            EffectivePreset = preset, Outcome = "success", HttpStatus = 200,
        };
        Assert.Equal(InterpretationAdmissionStatus.Admitted, usage.TryAdmit(request, null, DateTime.MinValue).Status);
        return request;
    }

    void Attempt(InterpretationUsageRequest request, int number, DateTimeOffset timestamp, decimal? cost)
    {
        usage.BeginAttempt(request.ServerExecutionId, number);
        SetAttemptTime(request.ServerExecutionId, number, timestamp);
        usage.RecordAttempt(new()
        {
            ServerExecutionId = request.ServerExecutionId, Provider = request.Provider, AttemptNumber = number,
            TimestampUtc = timestamp.UtcDateTime, CombinedCost = cost,
        });
    }

    void SetAttemptTime(string id, int number, DateTimeOffset timestamp)
    {
        using var db = usage.OpenForCommand();
        using var command = db.CreateCommand();
        command.CommandText = "UPDATE attempt_starts SET started_utc=$time WHERE execution_id=$id AND attempt_number=$number";
        command.Parameters.AddWithValue("$time", timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$number", number);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }
}
