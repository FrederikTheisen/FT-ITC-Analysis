using System.Text.Json;
using AnalysisITC.Web;
using AnalysisITC.Core.Interpretation;
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
    public void ActiveCodeMetadataIsAvailableWithoutExposingSecrets()
    {
        var configured = Configuration(); var registry = Registry(configured);
        var created = registry.Create("Named evaluation", 2, false, "standard");
        var active = registry.FindActive(created.Code);
        Assert.NotNull(active);
        Assert.Equal("Named evaluation", active!.Label);
        Assert.Equal("standard", active.EffectiveAccessTier);
        Assert.NotNull(active.ExpiresAtUtc);
        Assert.DoesNotContain(created.Code, JsonSerializer.Serialize(active), StringComparison.Ordinal);
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
            services, new StringReader("1\n\n5\n"), output,
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
            services, new StringReader("2\n1\nEvaluator\n\n\n\n3\n\ny\n\n4\n5\n"), output,
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
        var answers = $"9\n2\n3\n{created.Record.Id}\n5\nn\n\n6\n4\n5\n";
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader(answers), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        Assert.Null(registry.List().Single().RevokedAtUtc);
        Assert.Contains("Please enter a number from 1 to 5.", output.ToString());
        Assert.Contains("Revocation cancelled.", output.ToString());
    }

    [Fact]
    public async Task InteractiveAccountDetailsShowsOnlySelectedAccountUsage()
    {
        var configured = Configuration(); var services = Services(configured); var registry = services.GetRequiredService<OperatorCodeRegistry>();
        var selected = registry.Create("Selected evaluator", 30, false, InterpretationAccessTiers.Standard);
        var other = registry.Create("Other evaluator", 30, false, InterpretationAccessTiers.Advanced);
        var store = services.GetRequiredService<InterpretationUsageStore>();
        store.RecordRequest(new InterpretationUsageRequest { RequestId="selected-request", TraceId="trace-1", OperatorCodeId=selected.Record.Id, StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, EffectivePreset="standard", EffectiveModel="gpt-5.6-terra", EffectiveReasoning="medium", Outcome="success", HttpStatus=200, ProviderAttempts=1, InputTokens=100, CachedInputTokens=25, OutputTokens=40, ReasoningTokens=10, VisibleOutputTokens=30, TotalTokens=140, EstimatedCost=.0123m, LatencyMs=2500 });
        store.RecordRequest(new InterpretationUsageRequest { RequestId="other-request", TraceId="trace-2", OperatorCodeId=other.Record.Id, StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, EffectivePreset="in-depth", EffectiveModel="gpt-5.6-sol", EffectiveReasoning="high", Outcome="provider_error", HttpStatus=503, ProviderAttempts=1, TotalTokens=999, EstimatedCost=9m });
        var output = new StringWriter();
        var answers = $"2\n3\n{selected.Record.Id}\n1\n4\n\n6\n4\n5\n";
        var tool = InteractiveAdminTool.CreateForTests(services, new StringReader(answers), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        var text = output.ToString();
        Assert.Contains("Selected evaluator", text); Assert.Contains("Total interpretations: 1", text); Assert.Contains("Interpretation requests: 1", text);
        Assert.Contains("Remaining quota:",text);
        Assert.Contains("Quota resets:",text);
        Assert.Contains("Estimated cost: 0.0123", text); Assert.Contains("selected-request", text);
        Assert.Contains("effective_preset=standard", text); Assert.DoesNotContain("other-request", text);
    }

    [Fact]
    public async Task InteractiveAccountListIsCompactAndDetailsLookupDoesNotListAccounts()
    {
        var configured=Configuration(); var services=Services(configured); var registry=services.GetRequiredService<OperatorCodeRegistry>();
        var account=registry.Create("Internal label",30,false,InterpretationAccessTiers.Standard,"Ada Lovelace","ada@example.org","Lab");
        var output=new StringWriter();
        var tool=InteractiveAdminTool.CreateForTests(services,new StringReader("2\n2\n\n3\nmissing-id\n4\n5\n"),output,
            _=>Task.FromResult((true,"active")),_=>Task.FromResult((true,"HTTP 200")));
        Assert.Equal(0,await tool.RunAsync());
        var text=output.ToString(); Assert.Contains("ID                                Name/Label",text);
        Assert.Contains(account.Record.Id,text); Assert.Contains("Ada Lovelace",text); Assert.Contains("ada@example.org",text); Assert.Contains("Registered",text);
        var lookup=text.LastIndexOf("Exact account ID:",StringComparison.Ordinal); Assert.True(lookup>=0);
        Assert.DoesNotContain(account.Record.Id,text[(lookup+"Exact account ID:".Length)..]);
    }

    [Fact]
    public async Task InteractiveLogsShowLogicalRequestAndProviderAttempt()
    {
        var configured = Configuration(); var services = Services(configured); var store = services.GetRequiredService<InterpretationUsageStore>();
        store.RecordAttempt(new InterpretationUsageAttempt { RequestId="request-1", AttemptNumber=1, TimestampUtc=DateTime.UtcNow, Model="gpt-5.6-terra", ReasoningEffort="high", InputTokens=10, OutputTokens=5, VisibleOutputTokens=3, ReasoningTokens=2, TotalTokens=15, Outcome="success", HttpStatus=200 });
        store.RecordRequest(new InterpretationUsageRequest { RequestId="request-1", TraceId="trace-1", StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, EffectiveModel="gpt-5.6-terra", EffectiveReasoning="high", Outcome="success", HttpStatus=200, ProviderAttempts=1, TotalTokens=15 });
        var output = new StringWriter();
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader("3\n2\nrequest-1\n\n5\n5\n"), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        var text = output.ToString(); Assert.Contains("Request", text); Assert.Contains("Trace Id: trace-1", text);
        Assert.Contains("Provider attempt 1", text); Assert.Contains("Visible Output Tokens: 3", text);
    }

    [Fact]
    public async Task InteractiveLogListShowsElapsedSeconds()
    {
        var configured = Configuration(); var services = Services(configured); var store = services.GetRequiredService<InterpretationUsageStore>();
        store.RecordRequest(new InterpretationUsageRequest { RequestId="timed-request", TraceId="trace", StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, Outcome="success", HttpStatus=200, LatencyMs=2500 });
        var output = new StringWriter();
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader("3\n1\n\n\n\n5\n5\n"), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        Assert.Contains("timed-request", output.ToString());
        Assert.Contains("time_s=2.5", output.ToString());
    }

    [Fact]
    public async Task BackspaceReturnsFromSubmenuForRedirectedInput()
    {
        var configured = Configuration(); var services = Services(configured); var output = new StringWriter();
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader("2\n\b\n5\n"), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        Assert.DoesNotContain("Please enter a number from 1 to 6.", output.ToString());
    }

    [Fact]
    public async Task InteractiveExportRequiresConfirmationAndWritesMetadataCsv()
    {
        var configured = Configuration(); var services = Services(configured); var store = services.GetRequiredService<InterpretationUsageStore>();
        store.RecordRequest(new InterpretationUsageRequest { RequestId="request-1", TraceId="trace-1", StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, EffectiveModel="gpt-5.6-terra", EffectiveReasoning="medium", Outcome="success", HttpStatus=200 });
        var path = Path.Combine(directory, "export.csv"); var output = new StringWriter();
        var answers = $"3\n4\n\n{path}\ny\n\n5\n5\n";
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader(answers), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        Assert.Contains("request-1", File.ReadAllText(path));
        Assert.Contains("Export completed.", output.ToString());
    }

    [Fact]
    public async Task InteractiveExportOffersTimestampedStandardLocation()
    {
        var configured = Configuration(); var services = Services(configured); var exportDirectory = Path.Combine(directory, "logexports");
        var output = new StringWriter();
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader("3\n4\n\n\ny\n\n5\n5\n"), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")), exportDirectory);

        Assert.Equal(0, await tool.RunAsync());
        var file = Assert.Single(Directory.GetFiles(exportDirectory));
        Assert.Matches(@"ftitc-usage-\d{8}-\d{6}-7d\.csv$", Path.GetFileName(file));
        Assert.Contains(file, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SelectsAnonymousDefaultsAndRejectsUnauthorizedOrUnsupportedOverrides()
    {
        var configured = Configuration(); var registry = Registry(configured); var presets = Presets(configured);
        var request = new DefaultHttpContext().Request;
        var instant = Request("instant");
        Assert.True(InterpretationGenerationSelector.TrySelect(request, instant, configured, registry, presets, out var defaults, out _));
        Assert.Equal("gpt-5.6-luna", defaults.Model); Assert.Equal("low", defaults.ReasoningEffort);

        request.Headers["X-FTITC-Model"] = "gpt-6-astra";
        Assert.False(InterpretationGenerationSelector.TrySelect(request, instant, configured, registry, presets, out _, out var denied));
        Assert.Equal(403, denied.Status);

        var created = registry.Create("Evaluator", 1, false);
        request.Headers.Authorization = "Bearer " + created.Code;
        request.Headers["X-FTITC-Reasoning-Effort"] = "none";
        Assert.False(InterpretationGenerationSelector.TrySelect(request, Request("custom"), configured, registry, presets, out _, out var invalid));
        Assert.Equal("invalid_generation_override", invalid.Code);
    }

    [Fact]
    public void EnforcesTierPresetsAndAppliesPresetChangesImmediately()
    {
        var configured=Configuration(); var registry=Registry(configured); var presets=Presets(configured); var request=new DefaultHttpContext().Request;
        var standard=registry.Create("Standard",1,false,InterpretationAccessTiers.Standard); request.Headers.Authorization="Bearer "+standard.Code;
        Assert.True(InterpretationGenerationSelector.TrySelect(request,Request("fast"),configured,registry,presets,out var fast,out _));
        Assert.Equal("gpt-5.6-luna",fast.Model); Assert.Equal("high",fast.ReasoningEffort); Assert.Equal(InterpretationAccessTiers.Standard,fast.AccessTier);
        Assert.False(InterpretationGenerationSelector.TrySelect(request,Request("in-depth"),configured,registry,presets,out _,out var denied)); Assert.Equal(403,denied.Status);
        Assert.True(registry.ChangeTier(standard.Record.Id,InterpretationAccessTiers.Advanced));
        Assert.True(InterpretationGenerationSelector.TrySelect(request,Request("in-depth"),configured,registry,presets,out var deep,out _)); Assert.Equal("gpt-5.6-sol",deep.Model); Assert.Equal("high",deep.ReasoningEffort);
        var changed=presets.Update("in-depth","gpt-5.6-terra","low");
        Assert.True(InterpretationGenerationSelector.TrySelect(request,Request("in-depth"),configured,registry,presets,out var updated,out _)); Assert.Equal("gpt-5.6-terra",updated.Model); Assert.Equal("low",updated.ReasoningEffort); Assert.Equal(changed.Revision,updated.PresetRevision);
    }

    [Fact]
    public void StoresOptionalAccountDetailsAndQuotaOverridesWithoutAffectingAuthentication()
    {
        var configured=Configuration(); var registry=Registry(configured);
        var created=registry.Create("Research access",30,false,InterpretationAccessTiers.Standard,"Ada Lovelace","ada@example.org","Example Lab");
        Assert.Equal("Ada Lovelace",created.Record.Name); Assert.Equal("ada@example.org",created.Record.Email);
        Assert.True(registry.ChangeQuota(created.Record.Id,2.5m,false));
        var changed=registry.List().Single(); Assert.Equal(2.5m,changed.MonthlyQuotaUsdOverride); Assert.False(changed.QuotaUnlimited);
        Assert.True(registry.Authenticate("Bearer "+created.Code).IsAuthorized);
        Assert.Throws<ArgumentException>(()=>registry.ChangeDetails(created.Record.Id,null,"not-an-email",null));
    }

    [Fact]
    public void AppliesOneMonthlyAccountQuotaAcrossModelsButExemptsFastPreset()
    {
        var configured=Configuration(); var registry=Registry(configured); var presets=Presets(configured); presets.EnsureFile(); var store=Store(configured);
        var account=registry.Create("Registered",30,false,InterpretationAccessTiers.Standard);
        var now=DateTime.UtcNow; store.RecordRequest(new InterpretationUsageRequest
        { RequestId="quota-1",TraceId="t",OperatorCodeId=account.Record.Id,EffectivePreset="standard",StartedUtc=now,CompletedUtc=now,EstimatedCost=.25m,Outcome="success",HttpStatus=200 });
        store.RecordRequest(new InterpretationUsageRequest
        { RequestId="quota-2",TraceId="t",OperatorCodeId=account.Record.Id,EffectivePreset="fast",EffectiveModel="gpt-5.6-luna",StartedUtc=now,CompletedUtc=now,EstimatedCost=.10m,Outcome="success",HttpStatus=200 });
        store.RecordRequest(new InterpretationUsageRequest
        { RequestId="quota-free",TraceId="t",OperatorCodeId=account.Record.Id,EffectivePreset="instant",EffectiveModel="gpt-5.6-luna",StartedUtc=now,CompletedUtc=now,EstimatedCost=9m,Outcome="success",HttpStatus=200 });
        var service=new InterpretationQuotaService(presets,registry,store);
        var status=service.GetStatus(account.Record.Id,InterpretationAccessTiers.Standard,"standard",now);
        Assert.True(status.IsLimited); Assert.True(status.IsAvailable); Assert.Equal(65,status.RemainingPercent); Assert.Equal(1m,status.LimitUsd);
        var otherPreset=service.GetStatus(account.Record.Id,InterpretationAccessTiers.Standard,"fast",now);
        Assert.True(otherPreset.IsLimited); Assert.Equal(65,otherPreset.RemainingPercent);
        Assert.False(service.GetStatus(account.Record.Id,InterpretationAccessTiers.Standard,"instant",now).IsLimited);
        Assert.True(registry.ChangeQuota(account.Record.Id,null,true));
        Assert.False(service.GetStatus(account.Record.Id,InterpretationAccessTiers.Standard,"standard",now).IsLimited);
    }

    [Fact]
    public void MigratesAndHotUpdatesTierRequestSizeLimits()
    {
        var configured=Configuration(); var registry=Presets(configured); registry.EnsureFile();
        var value=registry.Read();
        Assert.Equal(new[]{128,512,1024,2048},value.RequestSizeLimits.Select(x=>x.MaximumKiB));
        var revision=value.Revision; var changed=registry.UpdateRequestSizeLimit(InterpretationAccessTiers.Public,64);
        Assert.NotEqual(revision,changed.Revision); Assert.Equal(64*1024,registry.MaximumRequestBytes(InterpretationAccessTiers.Public));
        Assert.Throws<ArgumentOutOfRangeException>(()=>registry.UpdateRequestSizeLimit(InterpretationAccessTiers.Public,2049));
    }

    [Fact]
    public void LegacyRequestsReceiveLegacyResponseAndExistingRecordsRemainAdministrators()
    {
        var configured=Configuration(); var registry=Registry(configured); var created=registry.Create("Legacy",1,false);
        var records=System.Text.Json.JsonSerializer.Deserialize<List<OperatorCodeRecord>>(File.ReadAllText(configured.OperatorAccess.RegistryPath),new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        records[0].AccessTier=null; File.WriteAllText(configured.OperatorAccess.RegistryPath,System.Text.Json.JsonSerializer.Serialize(records,new System.Text.Json.JsonSerializerOptions{PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase}));
        var request=new DefaultHttpContext().Request; request.Headers.Authorization="Bearer "+created.Code;
        var legacy=Request("fast") with { RequestSchemaVersion=FtItcInterpretationClient.LegacyRequestSchemaVersion };
        Assert.True(InterpretationGenerationSelector.TrySelect(request,legacy,configured,registry,Presets(configured),out var selection,out _));
        Assert.Equal(InterpretationAccessTiers.Administrator,selection.AccessTier); Assert.Equal("instant",selection.EffectivePreset); Assert.Equal(FtItcInterpretationClient.LegacyResponseSchemaVersion,selection.ResponseSchemaVersion);
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

    [Fact]
    public void MigratesExistingUsageDatabaseWithNullablePresetMetadata()
    {
        var configured=Configuration(); Directory.CreateDirectory(directory);
        using(var connection=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={configured.UsageLog.DatabasePath}")){connection.Open();using var command=connection.CreateCommand();command.CommandText="CREATE TABLE schema_info(version INTEGER NOT NULL); INSERT INTO schema_info VALUES(1); CREATE TABLE requests(request_id TEXT PRIMARY KEY,started_utc TEXT,report_id TEXT,operator_code_id TEXT,effective_model TEXT,outcome TEXT); CREATE TABLE attempts(request_id TEXT,attempt_number INTEGER);";command.ExecuteNonQuery();}
        using var migrated=Store(configured).OpenForCommand(); using var query=migrated.CreateCommand();
        query.CommandText="SELECT count(*) FROM pragma_table_info('requests') WHERE name IN ('requested_preset','effective_preset','access_tier','preset_revision')"; Assert.Equal(4L,(long)query.ExecuteScalar()!);
        query.CommandText="SELECT version FROM schema_info"; Assert.Equal(2L,(long)query.ExecuteScalar()!);
    }

    InterpretationOptions Configuration()
    {
        Directory.CreateDirectory(directory);
        return new InterpretationOptions
        {
            OpenAI = new OpenAIInterpretationOptions { Model="gpt-5.6-terra", ReasoningEffort="medium" },
            OperatorAccess = new InterpretationOperatorOptions { Enabled=true, RegistryPath=Path.Combine(directory,"operators.json"), PresetRegistryPath=Path.Combine(directory,"presets.json"), DefaultLifetimeDays=30 },
            UsageLog = new InterpretationUsageOptions { Enabled=true, DatabasePath=Path.Combine(directory,"usage.db") },
            AllowedModels = new Dictionary<string, InterpretationModelOptions>(StringComparer.Ordinal)
            {
                ["gpt-5.6-luna"] = new() { ReasoningEfforts=["none","low","medium","high","xhigh","max"] },
                ["gpt-5.6-terra"] = new() { ReasoningEfforts=["none","low","medium","high","xhigh","max"] },
                ["gpt-5.6-sol"] = new() { ReasoningEfforts=["none","low","medium","high","xhigh","max"] },
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
    static GenerationPresetRegistry Presets(InterpretationOptions value) => new(Options.Create(value));
    static ValidatedInterpretationRequest Request(string profile) { using var document=System.Text.Json.JsonDocument.Parse("{}"); return new(FtItcInterpretationClient.RequestSchemaVersion,"0123456789abcdef0123456789abcdef",profile,"test","test",document.RootElement.Clone()); }
    static IServiceProvider Services(InterpretationOptions value)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(Options.Create(value));
        services.AddSingleton<OperatorCodeRegistry>(); services.AddSingleton<GenerationPresetRegistry>(); services.AddSingleton<InterpretationUsageStore>(); services.AddSingleton<InterpretationQuotaService>();
        var provider=services.BuildServiceProvider(); provider.GetRequiredService<GenerationPresetRegistry>().EnsureFile(); return provider;
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
