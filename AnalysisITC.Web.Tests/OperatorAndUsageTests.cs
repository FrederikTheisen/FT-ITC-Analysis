using AnalysisITC.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class OperatorAndUsageTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-operator-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreatesHashesAuthenticatesAndRevokesCode()
    {
        var configured = Configuration(); var registry = Registry(configured);
        var created = registry.Create("Evaluator", 2, false);

        Assert.StartsWith("ftitc_op_", created.Code, StringComparison.Ordinal);
        Assert.DoesNotContain(created.Code, File.ReadAllText(configured.OperatorAccess.RegistryPath), StringComparison.Ordinal);
        Assert.True(registry.Authenticate("Bearer " + created.Code).IsAuthorized);
        Assert.DoesNotContain(created.Code, string.Join(" ", registry.List().Select(value => value.CodeHash)), StringComparison.Ordinal);

        Assert.True(registry.Revoke(created.Record.Id));
        Assert.False(registry.Authenticate("Bearer " + created.Code).IsAuthorized);
    }

    [Fact]
    public void RegistryFileUsesOwnerWriteAndGroupReadPermissions()
    {
        if (OperatingSystem.IsWindows()) return;
        var configured = Configuration(); var registry = Registry(configured);
        var created = registry.Create("Evaluator", 2, false);
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
            File.GetUnixFileMode(configured.OperatorAccess.RegistryPath));
        registry.Revoke(created.Record.Id);
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
            File.GetUnixFileMode(configured.OperatorAccess.RegistryPath));
    }

    [Fact]
    public void SelectsAnonymousDefaultsAndRejectsUnauthorizedOrUnsupportedOverrides()
    {
        var configured = Configuration(); var registry = Registry(configured);
        var request = new DefaultHttpContext().Request;
        Assert.True(InterpretationGenerationSelector.TrySelect(request, configured, registry, out var defaults, out _));
        Assert.Equal("gpt-5.6-terra", defaults.Model); Assert.Equal("medium", defaults.ReasoningEffort);

        request.Headers["X-FTITC-Model"] = "gpt-6-astra";
        Assert.False(InterpretationGenerationSelector.TrySelect(request, configured, registry, out _, out var denied));
        Assert.Equal(403, denied.Status);

        var created = registry.Create("Evaluator", 1, false);
        request.Headers.Authorization = "Bearer " + created.Code;
        request.Headers["X-FTITC-Reasoning-Effort"] = "none";
        Assert.False(InterpretationGenerationSelector.TrySelect(request, configured, registry, out _, out var invalid));
        Assert.Equal("invalid_generation_override", invalid.Code);
    }

    [Fact]
    public void InitializesIndexedWalDatabaseAndCalculatesVisibleAndLongContextCosts()
    {
        var configured = Configuration(); var store = Store(configured);
        var estimate = store.Estimate("gpt-5.6-terra", 300_000, 100_000, 20_000, 10_000, 2);
        Assert.Equal(1.045m, estimate.Combined);
        store.RecordAttempt(new InterpretationUsageAttempt { RequestId="r", AttemptNumber=1, TimestampUtc=DateTime.UtcNow, Model="gpt-5.6-terra", ReasoningEffort="high", FileSearchEnabled=true, FileSearchCalls=2, InputTokens=300000, CachedInputTokens=100000, CacheWriteTokens=20000, OutputTokens=10000, ReasoningTokens=4000, VisibleOutputTokens=6000, TotalTokens=310000, CombinedCost=estimate.Combined, Outcome="success", HttpStatus=200 });
        store.RecordRequest(new InterpretationUsageRequest { RequestId="r", TraceId="t", StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, EffectiveModel="gpt-5.6-terra", EffectiveReasoning="high", Outcome="success", HttpStatus=200 });
        using var connection = store.OpenForCommand();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT visible_output_tokens FROM attempts WHERE request_id='r';";
        Assert.Equal(6000L, (long)command.ExecuteScalar()!);
        command.CommandText = "PRAGMA journal_mode;"; Assert.Equal("wal", (string)command.ExecuteScalar()!);
        command.CommandText = "SELECT count(*) FROM pragma_index_list('requests');"; Assert.True((long)command.ExecuteScalar()! >= 5);
    }

    InterpretationOptions Configuration()
    {
        Directory.CreateDirectory(directory);
        return new InterpretationOptions
        {
            OpenAI = new OpenAIInterpretationOptions { Model="gpt-5.6-terra", ReasoningEffort="medium" },
            OperatorAccess = new InterpretationOperatorOptions { Enabled=true, RegistryPath=Path.Combine(directory,"operators.json"), DefaultLifetimeDays=30 },
            UsageLog = new InterpretationUsageOptions { Enabled=true, DatabasePath=Path.Combine(directory,"usage.db") },
            AllowedModels = new Dictionary<string, InterpretationModelOptions>(StringComparer.Ordinal)
            {
                ["gpt-5.6-terra"] = new() { ReasoningEfforts=["none","low","medium","high","xhigh","max"] },
                ["gpt-6-astra"] = new() { ReasoningEfforts=["low","medium","high","xhigh","max"] },
            },
            Pricing = new Dictionary<string, InterpretationPricingOptions>(StringComparer.Ordinal)
            {
                ["gpt-5.6-terra"] = new() { Revision="test", InputPerMillion=2, CachedInputPerMillion=.2m, CacheWritePerMillion=2.5m, OutputPerMillion=12, LongContextThreshold=272000, LongInputPerMillion=4, LongCachedInputPerMillion=.4m, LongCacheWritePerMillion=5, LongOutputPerMillion=18, FileSearchPerCall=.0025m }
            }
        };
    }

    static OperatorCodeRegistry Registry(InterpretationOptions value) => new(Options.Create(value), NullLogger<OperatorCodeRegistry>.Instance);
    static InterpretationUsageStore Store(InterpretationOptions value) => new(Options.Create(value), NullLogger<InterpretationUsageStore>.Instance);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
