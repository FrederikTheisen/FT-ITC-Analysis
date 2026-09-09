using AnalysisITC.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task InteractiveStatusReportsIndependentChecksAndReturnsToMenu()
    {
        var configured = Configuration(); var services = Services(configured);
        var output = new StringWriter();
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader("1\n\n4\n"), output,
            _ => Task.FromResult((true, "active")),
            url => Task.FromResult(url.Contains("127.0.0.1", StringComparison.Ordinal)
                ? (true, "HTTP 200; available=True; request=3.0; response=3.0; build=test")
                : (false, "HTTP 503")));

        Assert.Equal(0, await tool.RunAsync());
        var text = output.ToString();
        Assert.Contains("ftitc-web: OK - active", text);
        Assert.Contains("Local: OK", text); Assert.Contains("Public: FAILED", text);
        Assert.Contains("Active: 0", text); Assert.Contains("Requests: 0", text);
    }

    [Fact]
    public async Task InteractiveCreateUsesDefaultsAndNeverPrintsHash()
    {
        var configured = Configuration(); var services = Services(configured); var output = new StringWriter();
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader("2\n1\nEvaluator\n\ny\n\n4\n4\n"), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        var record = services.GetRequiredService<OperatorCodeRegistry>().List().Single();
        Assert.Equal("Evaluator", record.Label);
        Assert.InRange(record.ExpiresAtUtc!.Value - record.CreatedAtUtc, TimeSpan.FromDays(29.99), TimeSpan.FromDays(30.01));
        Assert.Contains("ftitc_op_", output.ToString());
        Assert.DoesNotContain(record.CodeHash, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveRevokeRequiresConfirmationAndRepromptsInvalidMenuChoice()
    {
        var configured = Configuration(); var services = Services(configured); var registry = services.GetRequiredService<OperatorCodeRegistry>();
        var created = registry.Create("Keep active", 1, false); var output = new StringWriter();
        var answers = $"9\n2\n2\n{created.Record.Id}\nn\n\n4\n4\n";
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader(answers), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        Assert.Null(registry.List().Single().RevokedAtUtc);
        Assert.Contains("Please enter a number from 1 to 4.", output.ToString());
        Assert.Contains("Revocation cancelled.", output.ToString());
    }

    [Fact]
    public async Task InteractiveLogsShowLogicalRequestAndProviderAttempt()
    {
        var configured = Configuration(); var services = Services(configured); var store = services.GetRequiredService<InterpretationUsageStore>();
        store.RecordAttempt(new InterpretationUsageAttempt { RequestId="request-1", AttemptNumber=1, TimestampUtc=DateTime.UtcNow, Model="gpt-5.6-terra", ReasoningEffort="high", InputTokens=10, OutputTokens=5, VisibleOutputTokens=3, ReasoningTokens=2, TotalTokens=15, Outcome="success", HttpStatus=200 });
        store.RecordRequest(new InterpretationUsageRequest { RequestId="request-1", TraceId="trace-1", StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, EffectiveModel="gpt-5.6-terra", EffectiveReasoning="high", Outcome="success", HttpStatus=200, ProviderAttempts=1, TotalTokens=15 });
        var output = new StringWriter();
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader("3\n2\nrequest-1\n\n5\n4\n"), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        var text = output.ToString(); Assert.Contains("Request", text); Assert.Contains("Trace Id: trace-1", text);
        Assert.Contains("Provider attempt 1", text); Assert.Contains("Visible Output Tokens: 3", text);
    }

    [Fact]
    public async Task InteractiveExportRequiresConfirmationAndWritesMetadataCsv()
    {
        var configured = Configuration(); var services = Services(configured); var store = services.GetRequiredService<InterpretationUsageStore>();
        store.RecordRequest(new InterpretationUsageRequest { RequestId="request-1", TraceId="trace-1", StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, EffectiveModel="gpt-5.6-terra", EffectiveReasoning="medium", Outcome="success", HttpStatus=200 });
        var path = Path.Combine(directory, "export.csv"); var output = new StringWriter();
        var answers = $"3\n4\n\n{path}\ny\n\n5\n4\n";
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader(answers), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        Assert.Contains("request-1", File.ReadAllText(path));
        Assert.Contains("Export completed.", output.ToString());
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
    static IServiceProvider Services(InterpretationOptions value)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(Options.Create(value));
        services.AddSingleton<OperatorCodeRegistry>(); services.AddSingleton<InterpretationUsageStore>(); return services.BuildServiceProvider();
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
