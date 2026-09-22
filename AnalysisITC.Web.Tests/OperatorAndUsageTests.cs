using System.Text.Json;
using AnalysisITC.Web;
using AnalysisITC.Core.Interpretation;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
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
    public void TerminalTextMakesControlCharactersVisible()
    {
        var rendered = TerminalText.Escape("Ada\u001b[2J\nLovelace\u202e");
        Assert.DoesNotContain('\u001b', rendered);
        Assert.Contains("\\u001B[2J\\u000A", rendered, StringComparison.Ordinal);
        Assert.Contains("\\u202E", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusEmailLoadsRegistrationSettingsWithoutLoadingProviderCredentials()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "interpretation.env");
        File.WriteAllText(path, """
            Interpretation__Registration__Enabled=true
            Interpretation__Registration__SiteKey=0x4AAAAAAdiagnostic
            Interpretation__Registration__DatabasePath='/var/lib/ftitc-web/self-registration.db'
            Interpretation__OpenAI__ApiKey=must-not-be-loaded
            """);

        var values = StatusEmailConfigurationLoader.ReadSafeEnvironment(path);

        Assert.Equal("true", values["Interpretation:Registration:Enabled"]);
        Assert.Equal("0x4AAAAAAdiagnostic", values["Interpretation:Registration:SiteKey"]);
        Assert.Equal("/var/lib/ftitc-web/self-registration.db", values["Interpretation:Registration:DatabasePath"]);
        Assert.DoesNotContain(values.Keys, key => key.Contains("OpenAI", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(values.Values, value => value?.Contains("must-not-be-loaded", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void NewAccountHumanTextRejectsTerminalControls()
    {
        var configured = Configuration(); var registry = Registry(configured);
        Assert.Throws<ArgumentException>(() => registry.Create("Label", 2, false, name: "Ada\u001b[31m"));
        Assert.Throws<ArgumentException>(() => registry.Create("Label", 2, false, organization: "Lab\nName"));
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
        Assert.Contains("FT-ITC administration", text);
        Assert.Contains("Build:", text);
        Assert.Contains("Interpretation service: active", text);
        Assert.Contains("Active accounts: 0", text);
        Assert.Contains("Last request:", text);
        Assert.Contains("Rate limiting", text);
        Assert.Contains("Interpretation: 5 request(s) per 10m per network", text);
        Assert.Contains("Registration submission: 5 request(s) per 15m per network", text);
        Assert.Contains("Registration activation: 10 request(s) per 10m per network", text);
    }

    [Fact]
    public async Task EscapeCancelsCurrentWorkflowAndReturnsToItsMenu()
    {
        var configured = Configuration(); var services = Services(configured); var output = new StringWriter();
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader("2\n1\n\u001b\n4\n6\n"), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        Assert.Empty(services.GetRequiredService<OperatorCodeRegistry>().List());
        Assert.Contains("Label (Esc to cancel): Cancelled", output.ToString());
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
        Assert.Contains("Invalid menu selection.", output.ToString());
        Assert.Contains("Revocation cancelled.", output.ToString());
    }

    [Fact]
    public async Task InteractiveAvailabilityCanPauseAndResumeGenerationPolicy()
    {
        var configured = Configuration(); var services = Services(configured); var output = new StringWriter();
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader("5\n\n2\nPlanned maintenance\ny\n\n1\n\ny\n3\n6\n"), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        var availability = services.GetRequiredService<InterpretationServiceAvailability>().Read();
        Assert.Equal("active", availability.Status);
        Assert.True(availability.IsAvailable);
        Assert.Contains("Proposed change: paused · Planned maintenance", output.ToString());
        Assert.Contains("Service availability updated.", output.ToString());
    }

    [Fact]
    public async Task InteractivePresetDescriptionRequiresConfirmationAndUpdatesImmediately()
    {
        var configured=Configuration(); var services=Services(configured); var output=new StringWriter();
        var tool=InteractiveAdminTool.CreateForTests(
            services,new StringReader("4\n3\nsummary\nUpdated summary wording.\ny\n6\n9\n6\n"),output,
            _=>Task.FromResult((true,"active")),_=>Task.FromResult((true,"HTTP 200")));
        Assert.Equal(0,await tool.RunAsync());
        Assert.Equal("Updated summary wording.",services.GetRequiredService<GenerationPresetRegistry>().Read().Summary.Description);
        Assert.Contains("Description updated.",output.ToString());
    }

    [Fact]
    public async Task NonInteractivePresetDescriptionCommandUpdatesRegistry()
    {
        var configured=Configuration(); var services=Services(configured); var output=new StringWriter(); var error=new StringWriter();
        Assert.Equal(0,await InterpretationAdminCommands.RunAsync(
            new[]{"generation-presets","set-description","instant","Updated fast wording."},services,output,error));
        Assert.Equal("Updated fast wording.",services.GetRequiredService<GenerationPresetRegistry>().Read().Presets[0].Description);
        Assert.Empty(error.ToString()); Assert.Contains("revision=",output.ToString());
    }

    [Fact]
    public async Task ScientificGuidanceCommandsListAndChangeTheHotLoadedDefault()
    {
        Assert.True(InterpretationAdminCommands.IsCommandMode("scientific-guidance"));
        var configured=Configuration(); var services=Services(configured); var output=new StringWriter(); var error=new StringWriter();
        Assert.Equal(0,await InterpretationAdminCommands.RunAsync(
            new[]{"scientific-guidance","list"},services,output,error));
        Assert.Contains("name=Standard 3.6",output.ToString());
        Assert.Contains("sha256=",output.ToString());

        Assert.Equal(0,await InterpretationAdminCommands.RunAsync(
            new[]{"scientific-guidance","set-default","3.4"},services,output,error));

        Assert.Equal("3.4",services.GetRequiredService<GenerationPresetRegistry>().Read().DefaultGuidanceVariant);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task InteractiveScientificGuidanceChangeRequiresConfirmation()
    {
        var configured=Configuration(); var services=Services(configured); var output=new StringWriter();
        var tool=InteractiveAdminTool.CreateForTests(
            services,new StringReader("4\n8\n1\n3.4\ny\n\n2\n9\n6\n"),output,
            _=>Task.FromResult((true,"active")),_=>Task.FromResult((true,"HTTP 200")));

        Assert.Equal(0,await tool.RunAsync());
        Assert.Equal("3.4",services.GetRequiredService<GenerationPresetRegistry>().Read().DefaultGuidanceVariant);
        Assert.Contains("Default guidance updated.",output.ToString());
    }

    [Fact]
    public async Task InteractiveAccountDetailsShowsOnlySelectedAccountUsage()
    {
        var configured = Configuration(); var services = Services(configured); var registry = services.GetRequiredService<OperatorCodeRegistry>();
        var selected = registry.Create("Selected evaluator", 30, false, InterpretationAccessTiers.Standard);
        var other = registry.Create("Other evaluator", 30, false, InterpretationAccessTiers.Advanced);
        var store = services.GetRequiredService<InterpretationUsageStore>();
        SeedCompleted(store, new InterpretationUsageRequest { RequestId="selected-request", TraceId="trace-1", OperatorCodeId=selected.Record.Id, StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, EffectivePreset="standard", EffectiveModel="gpt-5.6-terra", EffectiveReasoning="medium", Outcome="success", HttpStatus=200, ProviderAttempts=1, InputTokens=100, CachedInputTokens=25, OutputTokens=40, ReasoningTokens=10, VisibleOutputTokens=30, TotalTokens=140, EstimatedCost=.0123m, LatencyMs=2500 });
        SeedCompleted(store, new InterpretationUsageRequest { RequestId="other-request", TraceId="trace-2", OperatorCodeId=other.Record.Id, StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, EffectivePreset="in-depth", EffectiveModel="gpt-5.6-sol", EffectiveReasoning="high", Outcome="provider_error", HttpStatus=503, ProviderAttempts=1, TotalTokens=999, EstimatedCost=9m });
        var output = new StringWriter();
        var answers = $"2\n3\n{selected.Record.Id}\n1\n4\n\n6\n4\n5\n";
        var tool = InteractiveAdminTool.CreateForTests(services, new StringReader(answers), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        var text = output.ToString();
        Assert.Contains("Selected evaluator", text); Assert.Contains("Total interpretations: 1", text); Assert.Contains("Interpretation executions: 1", text);
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
        var tool=InteractiveAdminTool.CreateForTests(services,new StringReader("2\n2\n1\n\n3\nmissing-id\n4\n5\n"),output,
            _=>Task.FromResult((true,"active")),_=>Task.FromResult((true,"HTTP 200")));
        Assert.Equal(0,await tool.RunAsync());
        var text=output.ToString(); Assert.Contains("ID                                Name/Label",text); Assert.Contains("State",text);
        Assert.Contains(account.Record.Id,text); Assert.Contains("Ada Lovelace",text); Assert.Contains("ada@example.org",text); Assert.Contains("Registered",text);
        Assert.Contains("active", text, StringComparison.OrdinalIgnoreCase);
        var lookup=text.LastIndexOf("Exact account ID",StringComparison.Ordinal); Assert.True(lookup>=0);
        Assert.DoesNotContain(account.Record.Id,text[(lookup+"Exact account ID".Length)..]);
    }

    [Fact]
    public async Task InteractiveLogsShowLogicalRequestAndProviderAttempt()
    {
        var configured = Configuration(); var services = Services(configured); var store = services.GetRequiredService<InterpretationUsageStore>();
        var receipt = new InterpretationUsageAttempt { RequestId="request-1", AttemptNumber=1, TimestampUtc=DateTime.UtcNow, Model="gpt-5.6-terra", ReasoningEffort="high", InputTokens=10, OutputTokens=5, VisibleOutputTokens=3, ReasoningTokens=2, TotalTokens=15, Outcome="success", HttpStatus=200 };
        SeedCompleted(store, new InterpretationUsageRequest { RequestId="request-1", TraceId="trace-1", StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, EffectiveModel="gpt-5.6-terra", EffectiveReasoning="high", Outcome="success", HttpStatus=200, ProviderAttempts=1, TotalTokens=15 }, receipt);
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
        var account = services.GetRequiredService<OperatorCodeRegistry>().Create("Listed user", 1, false);
        SeedCompleted(store, new InterpretationUsageRequest { RequestId="timed-request", TraceId="trace", OperatorCodeId=account.Record.Id, StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, Outcome="success", HttpStatus=200, LatencyMs=2500 });
        SeedCompleted(store, new InterpretationUsageRequest { RequestId="public-request", TraceId="trace", StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, Outcome="success", HttpStatus=200, LatencyMs=1000 });
        var output = new StringWriter();
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader("3\n1\n\n\n\n5\n5\n"), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        var text = output.ToString();
        Assert.Contains("Entry: timed-request", text);
        Assert.Contains("Time: 2.5 s", text);
        Assert.Contains($"User: {account.Record.Id}", text);
        Assert.Contains("User: public", text);
        Assert.Contains("Model:", text); Assert.Contains("Reasoning:", text); Assert.Contains("Preset:", text);
        Assert.Contains("Status: success · HTTP: 200", text);
        Assert.Contains("cost:", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client_request=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tokens=", text, StringComparison.Ordinal);
        Assert.Contains(Environment.NewLine + Environment.NewLine + "Entry:", text, StringComparison.Ordinal);
        Assert.Contains(Environment.NewLine + Environment.NewLine + "Press Enter to continue", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DailyEmailUsesExactCopenhagenCalendarDaysAcrossDst()
    {
        var spring = DailyStatusEmail.Bounds(new DateOnly(2026, 3, 29));
        var autumn = DailyStatusEmail.Bounds(new DateOnly(2026, 10, 25));
        Assert.Equal(TimeSpan.FromHours(23), spring.EndUtc - spring.StartUtc);
        Assert.Equal(TimeSpan.FromHours(25), autumn.EndUtc - autumn.StartUtc);
    }

    [Fact]
    public async Task DailyEmailIncludesPerUserActivityWithoutRequestOrScientificIdentifiers()
    {
        Assert.Equal("mist@ft-itc.org", DailyStatusEmail.SenderAddress);
        Assert.Equal("support@ft-itc.org", DailyStatusEmail.ReplyToAddress);
        var configured = Configuration(); var store = Store(configured);
        var registry = Registry(configured);
        var created = registry.Create("Laboratory account", 30, false, InterpretationAccessTiers.Standard, "Test Scientist");
        var stamp = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        SeedCompleted(store, new InterpretationUsageRequest
        {
            RequestId="private-execution-id", ReportId="private-report-id", TraceId="private-trace",
            StartedUtc=stamp, CompletedUtc=stamp, Outcome="success", HttpStatus=200,
            ProviderAttempts=1, EstimatedCost=.0123m, OperatorCodeId=created.Record.Id,
            AccessTier=InterpretationAccessTiers.Standard
        });
        SeedCompleted(store, new InterpretationUsageRequest
        {
            RequestId="older-execution", StartedUtc=stamp.AddDays(-1), CompletedUtc=stamp.AddDays(-1),
            Outcome="success", HttpStatus=200, ProviderAttempts=1, EstimatedCost=.005m,
            OperatorCodeId=created.Record.Id, AccessTier=InterpretationAccessTiers.Standard
        });
        string? delivered = null;
        var reporter = new DailyStatusEmail(store, new InterpretationServiceAvailability(Options.Create(configured)),
            configured.StatusEmailConfigurationPath, () => Task.FromResult("active"),
            _ => Task.FromResult("HTTP 200; interpretation available"),
            (_, body) => { delivered = body; return Task.CompletedTask; },
            () => new DateTimeOffset(2026, 9, 15, 6, 0, 0, TimeSpan.Zero), operators: registry);
        var output = new StringWriter(); var error = new StringWriter();

        Assert.Equal(0, await DailyStatusEmail.RunAsync(["send"], reporter, output, error));
        Assert.NotNull(delivered);
        Assert.Contains("2026-09-14", delivered);
        Assert.Contains("Requests: 1", delivered);
        Assert.Contains("Provider attempts: 1", delivered);
        Assert.Contains("success: 1", delivered);
        Assert.Contains("Estimated cost: $0.0123", delivered);
        Assert.Contains($"Test Scientist — {created.Record.Id} (standard)", delivered);
        Assert.Contains("Previous day: 1 prompt(s); estimated cost $0.0123", delivered);
        Assert.Contains("All time: 2 prompt(s); estimated cost $0.0173", delivered);
        Assert.Contains("2026-09-14 14:00:00 +02:00", delivered);
        Assert.DoesNotContain("private-execution-id", delivered);
        Assert.DoesNotContain("private-report-id", delivered);
        Assert.DoesNotContain("private-trace", delivered);
        Assert.DoesNotContain("older-execution", delivered);
        Assert.DoesNotContain(created.Code, delivered);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task DailyEmailReportsZeroUsageAndIndependentHealthFailures()
    {
        var configured = Configuration(); var store = Store(configured);
        var reporter = new DailyStatusEmail(store, new InterpretationServiceAvailability(Options.Create(configured)),
            configured.StatusEmailConfigurationPath, () => throw new InvalidOperationException("system secret"),
            _ => throw new InvalidOperationException("endpoint secret"), null,
            () => new DateTimeOffset(2026, 9, 15, 6, 0, 0, TimeSpan.Zero));
        var report = await reporter.CreateReportAsync(new DateOnly(2026, 9, 14));
        Assert.Contains("ftitc-web: check unavailable", report);
        Assert.Contains("Local API: check unavailable", report);
        Assert.Contains("Public API: check unavailable", report);
        Assert.Contains("Requests: 0", report);
        Assert.Contains("Estimated cost: $0.0000", report);
        Assert.Contains("Last request: none", report);
        Assert.DoesNotContain("secret", report);
    }

    [Fact]
    public void StorageCapacityReportsFormattedCapacityAndDeduplicatesVolumes()
    {
        const long gib = 1024L * 1024 * 1024;
        var service = new StorageCapacityService(
            new[] { "usage.db", "registration.db" },
            _ => new StorageCapacityVolume("/", 100 * gib, 40 * gib));

        var report = service.Read();

        Assert.Equal(StorageCapacityStatus.Available, report.Status);
        var volume = Assert.Single(report.Volumes);
        Assert.Equal("/", volume.MountPoint);
        Assert.Equal(60 * gib, volume.UsedBytes);
        Assert.InRange(volume.UsedPercent, 59.99, 60.01);
        Assert.False(report.HasAttention);
        Assert.Equal("40.0 GiB", StorageCapacityService.FormatBytes(volume.AvailableBytes));
        Assert.Equal("100.0 GiB", StorageCapacityService.FormatBytes(volume.TotalBytes));
    }

    [Fact]
    public void StorageCapacityUsesTenGiBAsTheStrictLowSpaceBoundary()
    {
        const long gib = 1024L * 1024 * 1024;
        var healthy = new StorageCapacityService(["healthy"], _ => new StorageCapacityVolume("/", 20 * gib, 10 * gib));
        var low = new StorageCapacityService(["low"], _ => new StorageCapacityVolume("/", 20 * gib, 10 * gib - 1));

        Assert.False(healthy.Read().HasAttention);
        Assert.True(low.Read().HasAttention);
    }

    [Fact]
    public void StorageCapacityMarksOneLowVolumeAndUnavailableReadsSafely()
    {
        const long gib = 1024L * 1024 * 1024;
        var volumes = new Dictionary<string, StorageCapacityVolume>
        {
            ["application"] = new("/", 100 * gib, 20 * gib),
            ["data"] = new("/data", 100 * gib, 9 * gib),
        };
        var service = new StorageCapacityService(volumes.Keys, path => volumes[path]);
        var report = service.Read();

        Assert.Equal(StorageCapacityStatus.Available, report.Status);
        Assert.Equal(2, report.Volumes.Count);
        Assert.True(report.HasAttention);
        Assert.Contains(report.Volumes, volume => volume.MountPoint == "/data");

        var unavailable = new StorageCapacityService(["private-path"], _ => throw new IOException("private storage detail"));
        var unavailableReport = unavailable.Read();
        Assert.Equal(StorageCapacityStatus.Unavailable, unavailableReport.Status);
        Assert.Equal("storage capacity could not be read", unavailableReport.Detail);
        Assert.DoesNotContain("private", unavailableReport.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DailyEmailReportsStorageAndMarksLowSpaceAsAttention()
    {
        const long gib = 1024L * 1024 * 1024;
        var configured = Configuration();
        var storage = new StorageCapacityService(["application"], _ => new StorageCapacityVolume("/", 100 * gib, 9 * gib));
        string? subject = null;
        var reporter = new DailyStatusEmail(Store(configured), new InterpretationServiceAvailability(Options.Create(configured)),
            configured.StatusEmailConfigurationPath, () => Task.FromResult("active"),
            _ => Task.FromResult("HTTP 200; interpretation available"),
            (value, _) => { subject = value; return Task.CompletedTask; },
            () => new DateTimeOffset(2026, 9, 15, 6, 0, 0, TimeSpan.Zero),
            storageCapacity: storage);

        var output = new StringWriter();
        Assert.Equal(0, await DailyStatusEmail.RunAsync(["send", "--date", "2026-09-14"], reporter, output, new StringWriter()));
        var report = await reporter.CreateReportAsync(new DateOnly(2026, 9, 14));

        Assert.StartsWith("[ATTENTION]", subject, StringComparison.Ordinal);
        Assert.Contains("Server storage: ATTENTION", report);
        Assert.Contains("/: 9.0 GiB available of 100.0 GiB; 91.0 GiB used (91.0% used)", report);
        Assert.Contains("Low-space threshold: 10.0 GiB available", report);
        Assert.DoesNotContain("private storage detail", report, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationPipelineDiagnosticRunsAllStepsWithoutPersistenceOrProviders()
    {
        var configured = Configuration();
        ConfigureRegistration(configured, enabled: true);
        var registration = new RegistrationAvailability(Options.Create(configured));
        var interpretation = new InterpretationServiceAvailability(Options.Create(configured));
        var diagnostic = new RegistrationPipelineDiagnostic(configured.Registration, configured.UsageLog.DatabasePath, registration, interpretation);

        var report = diagnostic.Run();

        Assert.Equal(5, report.Steps.Count);
        Assert.All(report.Steps, step => Assert.Equal(RegistrationDiagnosticState.Pass, step.State));
        Assert.False(report.HasFailure);
        Assert.Contains("endpoint write was not invoked", report.Steps[0].Detail);
        Assert.Contains("live token verification was not attempted", report.Steps[1].Detail);
        Assert.Contains("no message was sent", report.Steps[3].Detail);
        Assert.Contains("remained unchanged", report.Steps[4].Detail);
    }

    [Fact]
    public void RegistrationPipelineDiagnosticReportsIndependentFailuresSafely()
    {
        var configured = Configuration();
        ConfigureRegistration(configured, enabled: true);
        File.WriteAllText(configured.Registration.SecretConfigurationPath, "not-json");
        File.Delete(configured.Registration.MailConfigurationPath);
        var diagnostic = new RegistrationPipelineDiagnostic(configured.Registration, configured.UsageLog.DatabasePath,
            new RegistrationAvailability(Options.Create(configured)), new InterpretationServiceAvailability(Options.Create(configured)));

        var report = diagnostic.Run();

        Assert.Equal(5, report.Steps.Count);
        Assert.Equal(RegistrationDiagnosticState.Fail, report.Steps[1].State);
        Assert.Equal(RegistrationDiagnosticState.Fail, report.Steps[3].State);
        Assert.Equal(RegistrationDiagnosticState.Pass, report.Steps[4].State);
        var output = string.Join("\n", report.Steps.Select(step => step.Detail));
        Assert.DoesNotContain("not-json", output, StringComparison.Ordinal);
        Assert.DoesNotContain("diagnostic@example.invalid", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledRegistrationIsSkippedWithoutAttention()
    {
        var configured = Configuration();
        var diagnostic = new RegistrationPipelineDiagnostic(configured.Registration, configured.UsageLog.DatabasePath,
            new RegistrationAvailability(Options.Create(configured)), new InterpretationServiceAvailability(Options.Create(configured)));

        var report = diagnostic.Run();

        Assert.Equal(5, report.Steps.Count);
        Assert.All(report.Steps.Take(4), step => Assert.Equal(RegistrationDiagnosticState.Skipped, step.State));
        Assert.Equal(RegistrationDiagnosticState.Pass, report.Steps[4].State);
        Assert.False(report.HasFailure);
    }

    [Fact]
    public async Task DailyEmailIncludesAllRegistrationDiagnosticSteps()
    {
        var configured = Configuration();
        ConfigureRegistration(configured, enabled: true);
        var registration = new RegistrationAvailability(Options.Create(configured));
        var interpretation = new InterpretationServiceAvailability(Options.Create(configured));
        var diagnostic = new RegistrationPipelineDiagnostic(configured.Registration, configured.UsageLog.DatabasePath, registration, interpretation);
        var reporter = new DailyStatusEmail(Store(configured), interpretation,
            configured.StatusEmailConfigurationPath, () => Task.FromResult("active"),
            _ => Task.FromResult("HTTP 200"), null,
            () => new DateTimeOffset(2026, 9, 15, 6, 0, 0, TimeSpan.Zero),
            registrationDiagnostic: diagnostic);

        var report = await reporter.CreateReportAsync(new DateOnly(2026, 9, 14));

        Assert.Contains("Registration pipeline dry-run", report);
        Assert.Contains("Registration pipeline: ok", report);
        for (var step = 1; step <= 5; step++)
            Assert.Contains($"Step {step} —", report);
    }

    [Fact]
    public async Task DailyEmailIncludesRegistrationSummaryWithoutIdentityData()
    {
        var configured = Configuration();
        ConfigureRegistration(configured, enabled: true);
        _ = new SelfRegistrationStore(Options.Create(configured));
        var interpretation = new InterpretationServiceAvailability(Options.Create(configured));
        var summary = new RegistrationSummaryService(Options.Create(configured),
            new RegistrationAvailability(Options.Create(configured)), interpretation);
        var reporter = new DailyStatusEmail(Store(configured), interpretation,
            configured.StatusEmailConfigurationPath, () => Task.FromResult("active"),
            _ => Task.FromResult("HTTP 200"), null,
            () => new DateTimeOffset(2026, 9, 15, 6, 0, 0, TimeSpan.Zero),
            registrationSummary: summary);

        var report = await reporter.CreateReportAsync(new DateOnly(2026, 9, 14));

        Assert.Contains("Registration summary: ok", report);
        Assert.Contains("Total registration records: 0", report);
        Assert.Contains("Email verified (current state estimate): 0", report);
        Assert.DoesNotContain("@", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DailyEmailMarksRegistrationDiagnosticFailureAsAttention()
    {
        var configured = Configuration();
        ConfigureRegistration(configured, enabled: true);
        File.WriteAllText(configured.Registration.SecretConfigurationPath, "malformed");
        var registration = new RegistrationAvailability(Options.Create(configured));
        var interpretation = new InterpretationServiceAvailability(Options.Create(configured));
        var diagnostic = new RegistrationPipelineDiagnostic(configured.Registration, configured.UsageLog.DatabasePath, registration, interpretation);
        string? subject = null;
        var reporter = new DailyStatusEmail(Store(configured), interpretation,
            configured.StatusEmailConfigurationPath, () => Task.FromResult("active"),
            _ => Task.FromResult("HTTP 200"),
            (value, _) => { subject = value; return Task.CompletedTask; },
            () => new DateTimeOffset(2026, 9, 15, 6, 0, 0, TimeSpan.Zero),
            registrationDiagnostic: diagnostic);

        Assert.Equal(0, await DailyStatusEmail.RunAsync(["send", "--date", "2026-09-14"], reporter, new StringWriter(), new StringWriter()));
        Assert.StartsWith("[ATTENTION]", subject, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationSummaryCountsLifecycleStatesWithoutReadingIdentityData()
    {
        var configured = Configuration();
        ConfigureRegistration(configured, enabled: true);
        var registrationStore = new SelfRegistrationStore(Options.Create(configured));
        var reportDate = new DateOnly(2026, 9, 14);
        var (start, _) = DailyStatusEmail.Bounds(reportDate);
        var old = start.AddDays(-2).ToString("O");
        var recent = start.AddHours(2).ToString("O");
        var states = new[] { "active", "active", "activating", "activation-sent", "pending", "failed", "scrubbed", "mystery" };
        using (var db = registrationStore.Open())
        using (var command = db.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO registration_accounts
                  (id,name,email,normalized_email,organization,state,terms_version,privacy_version,accepted_at_utc,created_at_utc)
                VALUES ($id,$name,$email,$normalized,$organization,$state,$terms,$privacy,$accepted,$created)
                """;
            for (var index = 0; index < states.Length; index++)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$id", $"identity-{index}");
                command.Parameters.AddWithValue("$name", $"Private name {index}");
                command.Parameters.AddWithValue("$email", $"private-{index}@example.invalid");
                command.Parameters.AddWithValue("$normalized", $"private-{index}@example.invalid");
                command.Parameters.AddWithValue("$organization", "Private organisation");
                command.Parameters.AddWithValue("$state", states[index]);
                command.Parameters.AddWithValue("$terms", configured.Registration.TermsVersion);
                command.Parameters.AddWithValue("$privacy", configured.Registration.PrivacyVersion);
                command.Parameters.AddWithValue("$accepted", old);
                command.Parameters.AddWithValue("$created", index < 4 ? recent : old);
                command.ExecuteNonQuery();
            }
        }
        var before = new FileInfo(configured.Registration.DatabasePath);
        var summary = new RegistrationSummaryService(Options.Create(configured),
            new RegistrationAvailability(Options.Create(configured)), new InterpretationServiceAvailability(Options.Create(configured)));

        var report = summary.Build(reportDate);

        Assert.Equal(RegistrationSummaryStatus.Available, report.Status);
        Assert.Equal(8, report.TotalRecords);
        Assert.Equal(2, report.ActiveAccounts);
        Assert.Equal(3, report.EmailVerified);
        Assert.Equal(2, report.AwaitingEmailVerification);
        Assert.Equal(1, report.AccessCodeDeliveryPending);
        Assert.Equal(1, report.Failed);
        Assert.Equal(1, report.Scrubbed);
        Assert.Equal(1, report.Unknown);
        Assert.Equal(4, report.PreviousDayCreated);
        Assert.Equal(2, report.PreviousDayByState["active"]);
        Assert.Equal(1, report.PreviousDayByState["activating"]);
        Assert.True(report.HasAttention);
        var after = new FileInfo(configured.Registration.DatabasePath);
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before.LastWriteTimeUtc, after.LastWriteTimeUtc);
    }

    [Fact]
    public void RegistrationSummaryHandlesDisabledAndUnavailableDatabasesSafely()
    {
        var disabled = Configuration();
        var disabledSummary = new RegistrationSummaryService(Options.Create(disabled),
            new RegistrationAvailability(Options.Create(disabled)), new InterpretationServiceAvailability(Options.Create(disabled)));
        Assert.Equal(RegistrationSummaryStatus.Skipped, disabledSummary.Build(new DateOnly(2026, 9, 14)).Status);

        var unavailable = Configuration();
        ConfigureRegistration(unavailable, enabled: true);
        var summary = new RegistrationSummaryService(Options.Create(unavailable),
            new RegistrationAvailability(Options.Create(unavailable)), new InterpretationServiceAvailability(Options.Create(unavailable)));
        var report = summary.Build(new DateOnly(2026, 9, 14));

        Assert.Equal(RegistrationSummaryStatus.Unavailable, report.Status);
        Assert.Equal("registration database could not be read", report.Detail);
        Assert.DoesNotContain(unavailable.Registration.DatabasePath, report.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2026, 3, 29, 23, 22, 23)]
    [InlineData(2026, 10, 25, 22, 23, 25)]
    public void RegistrationSummaryUsesCopenhagenCalendarBoundaries(int year, int month, int day,
        int startUtcHour, int endUtcHour, int expectedDurationHours)
    {
        var (start, end) = DailyStatusEmail.Bounds(new DateOnly(year, month, day));

        Assert.Equal(startUtcHour, start.Hour);
        Assert.Equal(endUtcHour, end.Hour);
        Assert.Equal(expectedDurationHours, (end - start).TotalHours);
    }

    [Fact]
    public async Task DailyEmailMarksUnresolvedCostUnknown()
    {
        var configured = Configuration(); var store = Store(configured);
        var stamp = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        SeedCompleted(store, new InterpretationUsageRequest
        {
            RequestId="unresolved", StartedUtc=stamp, CompletedUtc=stamp,
            Outcome="provider_error", HttpStatus=503, ProviderAttempts=1
        });
        var reporter = new DailyStatusEmail(store, new InterpretationServiceAvailability(Options.Create(configured)),
            configured.StatusEmailConfigurationPath, () => Task.FromResult("active"),
            _ => Task.FromResult("HTTP 200; interpretation available"), null,
            () => new DateTimeOffset(2026, 9, 15, 6, 0, 0, TimeSpan.Zero));
        var report = await reporter.CreateReportAsync(new DateOnly(2026, 9, 14));
        Assert.Contains("Cost: unknown (known subtotal $0.0000; unresolved 1", report);
        Assert.DoesNotContain("Estimated cost: $0.0000", report);
    }

    [Fact]
    public async Task DailyEmailDeliveryFailureIsSafeAndDoesNotAlterInterpretation()
    {
        var configured = Configuration(); var store = Store(configured);
        var reporter = new DailyStatusEmail(store, new InterpretationServiceAvailability(Options.Create(configured)),
            configured.StatusEmailConfigurationPath, () => Task.FromResult("active"),
            _ => Task.FromResult("HTTP 200; interpretation available"),
            (_, _) => throw new InvalidOperationException("provider password and private message"),
            () => new DateTimeOffset(2026, 9, 15, 6, 0, 0, TimeSpan.Zero));
        var output = new StringWriter(); var error = new StringWriter();
        Assert.Equal(1, await DailyStatusEmail.RunAsync(["send", "--date", "2026-09-14"], reporter, output, error));
        Assert.Contains("Status email delivery failed", error.ToString());
        Assert.DoesNotContain("provider password", error.ToString());
        Assert.DoesNotContain("private message", error.ToString());
        Assert.True(new InterpretationServiceAvailability(Options.Create(configured)).Read().IsAvailable);
    }

    [Fact]
    public async Task DailyEmailKeepsReportingWhenUsageDatabaseIsUnavailable()
    {
        var configured = Configuration(); configured.UsageLog.DatabasePath = directory;
        var reporter = new DailyStatusEmail(Store(configured), new InterpretationServiceAvailability(Options.Create(configured)),
            configured.StatusEmailConfigurationPath, () => Task.FromResult("active"),
            _ => Task.FromResult("HTTP 200; interpretation available"), null,
            () => new DateTimeOffset(2026, 9, 15, 6, 0, 0, TimeSpan.Zero));
        var report = await reporter.CreateReportAsync(new DateOnly(2026, 9, 14));
        Assert.Contains("Usage database: check unavailable", report);
        Assert.Contains("ftitc-web: active", report);
    }

    [Fact]
    public async Task InteractiveAdminDisplaysUtcRecordsInConfiguredLocalTime()
    {
        var configured = Configuration();
        configured.AdminDisplayTimeZone = "Europe/Copenhagen";
        var services = Services(configured); var store = services.GetRequiredService<InterpretationUsageStore>();
        var timestamp = new DateTime(2026, 9, 14, 12, 34, 56, DateTimeKind.Utc);
        SeedCompleted(store, new InterpretationUsageRequest
        {
            RequestId="local-time-request", TraceId="trace", StartedUtc=timestamp, CompletedUtc=timestamp,
            Outcome="success", HttpStatus=200
        });
        var output = new StringWriter();
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader("3\n2\nlocal-time-request\n\n5\n6\n"), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        var text = output.ToString();
        Assert.Contains("Times: Europe/Copenhagen", text);
        Assert.Contains("Started: 2026-09-14 14:34:56 +02:00", text);
        Assert.DoesNotContain("2026-09-14T12:34:56", text, StringComparison.Ordinal);
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
    public async Task BackFromNestedPresetMenuReturnsToImmediateParent()
    {
        var configured = Configuration(); var services = Services(configured); var output = new StringWriter();
        // Main -> Generation presets -> Preset access -> first preset -> Back,
        // then Back from the preset list, Back from presets and Exit.
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader("4\n7\n1\n5\n5\n9\n6\n"), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        Assert.Contains("User groups", output.ToString());
        Assert.Contains("Model:", output.ToString());
        Assert.Contains("Reasoning:", output.ToString());
        Assert.DoesNotContain("Invalid menu selection.", output.ToString());
    }

    [Fact]
    public async Task NestedPresetEditorsReturnToTheirEntityLists()
    {
        var configured = Configuration(); var services = Services(configured); var output = new StringWriter();
        // Mapping, description, quota and request-size editors: Escape returns to each
        // entity list, then explicit Back returns through the parent menus.
        var answers = "4\n2\n1\n5\n6\n9\n4\n3\n1\n\u001b\n6\n9\n4\n4\n1\n\u001b\n3\n9\n4\n5\n1\n\u001b\n5\n9\n6\n";
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader(answers), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")));

        Assert.Equal(0, await tool.RunAsync());
        var text = output.ToString();
        Assert.Contains("Preset mapping", text);
        Assert.Contains("Preset descriptions", text);
        Assert.Contains("Quota defaults", text);
        Assert.Contains("Request size limits", text);
        Assert.DoesNotContain("Invalid menu selection.", text);
    }

    [Fact]
    public async Task KeyboardMenusUseArrowsEnterBackspaceAndRememberSelection()
    {
        var configured = Configuration(); var services = Services(configured); var output = new StringWriter();
        var keys = new Queue<ConsoleKeyInfo>(
        [
            Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter),
            Key(ConsoleKey.Backspace),
            Key(ConsoleKey.End), Key(ConsoleKey.Enter),
        ]);
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader(string.Empty), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")),
            readKey: keys.Dequeue);

        Assert.Equal(0, await tool.RunAsync());
        var text = output.ToString();
        Assert.Contains("Operator accounts", text);
        Assert.Contains("> \u001b[7mOperator accounts\u001b[0m", text);
        Assert.Contains("\u001b[?25l", text);
        Assert.Contains("\u001b[?25h", text);
        Assert.Empty(keys);
    }

    [Fact]
    public async Task KeyboardMenuWrapsAndControlCExitsCleanly()
    {
        var configured = Configuration(); var services = Services(configured); var output = new StringWriter();
        var wrapKeys = new Queue<ConsoleKeyInfo>([Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)]);
        var wrapTool = InteractiveAdminTool.CreateForTests(
            services, new StringReader(string.Empty), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")),
            readKey: wrapKeys.Dequeue);
        Assert.Equal(0, await wrapTool.RunAsync());
        Assert.Empty(wrapKeys);

        output.GetStringBuilder().Clear();
        var cancelKeys = new Queue<ConsoleKeyInfo>([Key(ConsoleKey.C, '\u0003', ConsoleModifiers.Control)]);
        var cancelTool = InteractiveAdminTool.CreateForTests(
            services, new StringReader(string.Empty), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")),
            readKey: cancelKeys.Dequeue);
        Assert.Equal(0, await cancelTool.RunAsync());
        Assert.Contains("\u001b[?25h", output.ToString());
        Assert.Empty(cancelKeys);
    }

    [Fact]
    public async Task KeyboardLogNavigationPreservesListingForRequestIdLookup()
    {
        var configured = Configuration(); var services = Services(configured);
        var store = services.GetRequiredService<InterpretationUsageStore>();
        SeedCompleted(store, new InterpretationUsageRequest
        {
            RequestId="copyable-request-id", TraceId="trace", StartedUtc=DateTime.UtcNow,
            CompletedUtc=DateTime.UtcNow, Outcome="success", HttpStatus=200,
            EffectiveModel="gpt-5.6-terra", EffectiveReasoning="medium"
        });
        var output = new StringWriter();
        var keys = new Queue<ConsoleKeyInfo>(
        [
            Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter),
            Key(ConsoleKey.Enter),
            Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter),
            Key(ConsoleKey.End), Key(ConsoleKey.Enter),
            Key(ConsoleKey.End), Key(ConsoleKey.Enter),
        ]);
        var tool = InteractiveAdminTool.CreateForTests(
            services, new StringReader("\n1\n\ncopyable-request-id\n\n"), output,
            _ => Task.FromResult((true, "active")), _ => Task.FromResult((true, "HTTP 200")),
            readKey: keys.Dequeue);

        Assert.Equal(0, await tool.RunAsync());
        var text = output.ToString();
        var listed = text.IndexOf("copyable-request-id", StringComparison.Ordinal);
        var details = text.IndexOf("Execution", listed, StringComparison.Ordinal);
        Assert.True(listed >= 0 && details > listed);
        Assert.DoesNotContain("\u001b[2J", text, StringComparison.Ordinal);
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ProductionAdminModeRejectsRedirectedConsoleInput()
    {
        var configured = Configuration(); var services = Services(configured); var output = new StringWriter();

        Assert.Equal(2, await InteractiveAdminTool.RunAsync(services, new StringReader(string.Empty), output));
        Assert.Contains("requires an interactive terminal", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveExportRequiresConfirmationAndWritesMetadataCsv()
    {
        var configured = Configuration(); var services = Services(configured); var store = services.GetRequiredService<InterpretationUsageStore>();
        SeedCompleted(store, new InterpretationUsageRequest { RequestId="request-1", TraceId="trace-1", StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, EffectiveModel="gpt-5.6-terra", EffectiveReasoning="medium", Outcome="success", HttpStatus=200 });
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
    public void MigratesThoroughDisplayNameWithoutChangingConfiguredMappings()
    {
        var configured = Configuration();
        var existing = new GenerationPresetConfiguration
        {
            SchemaVersion = 6,
            Revision = "existing-revision",
            ModifiedAtUtc = DateTime.UtcNow.AddDays(-1),
            QuotaAccountingStartedAtUtc = DateTime.UtcNow.AddMonths(-1),
            Presets =
            [
                new() { Id="instant", DisplayName="Fast", Model="gpt-5.6-luna", ReasoningEffort="low" },
                new() { Id="fast", DisplayName="Default", Model="gpt-5.6-luna", ReasoningEffort="high" },
                new() { Id="standard", DisplayName="Advanced", Model="gpt-5.6-terra", ReasoningEffort="high" },
                new() { Id="in-depth", DisplayName="Thorough", Model="gpt-5.6-terra", ReasoningEffort="low" },
            ],
            Summary = new() { Id="summary", DisplayName="Summary", Model="gpt-5.6-sol", ReasoningEffort="medium" },
            Quotas =
            [
                new() { AccessTier=InterpretationAccessTiers.Standard, MonthlyUsd=2m },
                new() { AccessTier=InterpretationAccessTiers.Advanced, MonthlyUsd=4m },
            ],
            RequestSizeLimits =
            [
                new() { AccessTier=InterpretationAccessTiers.Public, MaximumKiB=64 },
                new() { AccessTier=InterpretationAccessTiers.Standard, MaximumKiB=256 },
                new() { AccessTier=InterpretationAccessTiers.Advanced, MaximumKiB=768 },
                new() { AccessTier=InterpretationAccessTiers.Administrator, MaximumKiB=2048 },
            ],
        };
        File.WriteAllText(configured.OperatorAccess.PresetRegistryPath, JsonSerializer.Serialize(existing, new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.CamelCase }));

        var presets = Presets(configured);
        presets.EnsureFile();
        var migrated = presets.Read();

        Assert.Equal(11, migrated.SchemaVersion);
        Assert.Equal("3.7.0", migrated.DefaultGuidanceVariant);
        var comprehensive = migrated.Presets.Single(x => x.Id == "in-depth");
        Assert.Equal("Comprehensive", comprehensive.DisplayName);
        Assert.Equal("The most extensive investigation of the supplied data package, using advanced reasoning.", comprehensive.Description);
        Assert.Equal("gpt-5.6-terra", comprehensive.Model);
        Assert.Equal("low", comprehensive.ReasoningEffort);
        Assert.Equal("gpt-5.6-sol", migrated.Summary.Model);
        Assert.Contains("concise factual summary", migrated.Summary.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4m, migrated.Quotas.Single(x => x.AccessTier == InterpretationAccessTiers.Advanced).MonthlyUsd);
        Assert.Equal(768, migrated.RequestSizeLimits.Single(x => x.AccessTier == InterpretationAccessTiers.Advanced).MaximumKiB);
    }

    [Fact]
    public void PresetDescriptionsAreEditableValidatedAndDoNotChangeMappings()
    {
        var configured=Configuration(); var registry=Presets(configured); registry.EnsureFile();
        var before=registry.Read(); var oldRevision=before.Revision; var oldModel=before.Summary.Model;
        var changed=registry.UpdateDescription("summary","  A revised summary description.  ");
        Assert.Equal("A revised summary description.",changed.Summary.Description);
        Assert.Equal(oldModel,changed.Summary.Model); Assert.NotEqual(oldRevision,changed.Revision);
        Assert.Throws<ArgumentException>(()=>registry.UpdateDescription("instant",""));
        Assert.Throws<ArgumentException>(()=>registry.UpdateDescription("instant",new string('x',GenerationPresetRegistry.MaximumDescriptionLength+1)));
        Assert.Throws<ArgumentException>(()=>registry.UpdateDescription("instant","line\nbreak"));
        Assert.Throws<ArgumentException>(()=>registry.UpdateDescription("unknown","Description"));
    }

    [Fact]
    public void ScientificGuidanceOverridesAreAdministratorOnlyAndAllowlisted()
    {
        var configured=Configuration(); var registry=Registry(configured); var presets=Presets(configured);
        var standard=registry.Create("Standard",1,false,InterpretationAccessTiers.Standard);
        var request=new DefaultHttpContext().Request; request.Headers.Authorization="Bearer "+standard.Code;
        request.Headers["X-FTITC-Guidance-Variant"]="3.7.0-structured";
        Assert.False(InterpretationGenerationSelector.TrySelect(request,Request("fast"),configured,registry,presets,out _,out var denied));
        Assert.Equal(403,denied.Status);

        var administrator=registry.Create("Administrator",1,false,InterpretationAccessTiers.Administrator);
        request=new DefaultHttpContext().Request; request.Headers.Authorization="Bearer "+administrator.Code;
        request.Headers["X-FTITC-Model"]="gpt-5.6-terra";
        request.Headers["X-FTITC-Reasoning-Effort"]="medium";
        request.Headers["X-FTITC-Guidance-Variant"]="3.7.0-structured";
        Assert.True(InterpretationGenerationSelector.TrySelect(request,Request("custom"),configured,registry,presets,out var selected,out _));
        Assert.Equal("3.7.0-structured",selected.GuidanceVariant);

        request.Headers["X-FTITC-Guidance-Variant"]="arbitrary-text";
        Assert.False(InterpretationGenerationSelector.TrySelect(request,Request("custom"),configured,registry,presets,out _,out var invalid));
        Assert.Equal("invalid_guidance_override",invalid.Code);

        request.Headers["X-FTITC-Guidance-Variant"]="standard";
        Assert.False(InterpretationGenerationSelector.TrySelect(request,Request("custom"),configured,registry,presets,out _,out var obsoleteStandard));
        Assert.Equal("invalid_guidance_override", obsoleteStandard.Code);

        request.Headers["X-FTITC-Guidance-Variant"]="structured";
        Assert.False(InterpretationGenerationSelector.TrySelect(request,Request("custom"),configured,registry,presets,out _,out var obsoleteStructured));
        Assert.Equal("invalid_guidance_override", obsoleteStructured.Code);
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
    public void AppliesOneMonthlyAccountQuotaAcrossAllModelsPresetsAndTasks()
    {
        var configured=Configuration(); var registry=Registry(configured); var presets=Presets(configured); presets.EnsureFile(); var store=Store(configured);
        var account=registry.Create("Registered",30,false,InterpretationAccessTiers.Standard);
        var now=DateTime.UtcNow; SeedCompleted(store, new InterpretationUsageRequest
        { RequestId="quota-1",TraceId="t",OperatorCodeId=account.Record.Id,EffectivePreset="standard",StartedUtc=now,CompletedUtc=now,EstimatedCost=.25m,Outcome="success",HttpStatus=200 });
        SeedCompleted(store, new InterpretationUsageRequest
        { RequestId="quota-2",TraceId="t",OperatorCodeId=account.Record.Id,EffectivePreset="fast",EffectiveModel="gpt-5.6-luna",StartedUtc=now,CompletedUtc=now,EstimatedCost=.10m,Outcome="success",HttpStatus=200 });
        SeedCompleted(store, new InterpretationUsageRequest
        { RequestId="quota-instant",TraceId="t",OperatorCodeId=account.Record.Id,EffectivePreset="instant",EffectiveModel="gpt-5.6-luna",StartedUtc=now,CompletedUtc=now,EstimatedCost=9m,Outcome="success",HttpStatus=200 });
        SeedCompleted(store, new InterpretationUsageRequest
        { RequestId="quota-summary",TaskType="summary",TraceId="t",OperatorCodeId=account.Record.Id,EffectivePreset="summary",EffectiveModel="gpt-5.6-luna",StartedUtc=now,CompletedUtc=now,EstimatedCost=20m,Outcome="success",HttpStatus=200 });
        var service=new InterpretationQuotaService(presets,registry,store);
        var status=service.GetStatus(account.Record.Id,InterpretationAccessTiers.Standard,"standard",now);
        Assert.True(status.IsLimited); Assert.False(status.IsAvailable); Assert.Equal(0,status.RemainingPercent); Assert.Equal(1m,status.LimitUsd); Assert.Equal(29.35m,status.SpentUsd);
        var otherPreset=service.GetStatus(account.Record.Id,InterpretationAccessTiers.Standard,"fast",now);
        Assert.True(otherPreset.IsLimited); Assert.Equal(0,otherPreset.RemainingPercent);
        Assert.True(service.GetStatus(account.Record.Id,InterpretationAccessTiers.Standard,"instant",now).IsLimited);
        Assert.True(service.GetStatus(account.Record.Id,InterpretationAccessTiers.Standard,"summary",now).IsLimited);
        Assert.True(registry.ChangeQuota(account.Record.Id,null,true));
        Assert.False(service.GetStatus(account.Record.Id,InterpretationAccessTiers.Standard,"standard",now).IsLimited);
    }

    [Fact]
    public void MigratesAndHotUpdatesTierRequestSizeLimits()
    {
        var configured=Configuration(); var registry=Presets(configured); registry.EnsureFile();
        var value=registry.Read();
        Assert.Equal("summary",value.Summary.Id); Assert.Equal("gpt-5.6-luna",value.Summary.Model); Assert.Equal("medium",value.Summary.ReasoningEffort);
        Assert.Equal(new[]{128,512,1024,2048},value.RequestSizeLimits.Select(x=>x.MaximumKiB));
        var revision=value.Revision; var changed=registry.UpdateRequestSizeLimit(InterpretationAccessTiers.Public,64);
        Assert.NotEqual(revision,changed.Revision); Assert.Equal(64*1024,registry.MaximumRequestBytes(InterpretationAccessTiers.Public));
        Assert.Throws<ArgumentOutOfRangeException>(()=>registry.UpdateRequestSizeLimit(InterpretationAccessTiers.Public,2049));
        var summary=registry.Update("summary","gpt-5.6-terra","high"); Assert.Equal("gpt-5.6-terra",summary.Summary.Model);
    }

    [Fact]
    public void VersionFourPresetMigrationPreservesOperationalConfiguration()
    {
        var configured=Configuration(); var registry=Presets(configured); registry.EnsureFile();
        var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configured.OperatorAccess.PresetRegistryPath))!.AsObject();
        node["schemaVersion"]=4; node.Remove("summary");
        node["quotaAccountingStartedAtUtc"]="2026-09-01T00:00:00Z";
        node["requestSizeLimits"]![0]!["maximumKiB"]=64;
        File.WriteAllText(configured.OperatorAccess.PresetRegistryPath,node.ToJsonString());

        registry.EnsureFile(); var migrated=registry.Read();

        Assert.Equal(11,migrated.SchemaVersion); Assert.Equal(64,migrated.RequestSizeLimits[0].MaximumKiB);
        Assert.Equal("3.7.0",migrated.DefaultGuidanceVariant);
        Assert.Equal(new DateTime(2026,9,1,0,0,0,DateTimeKind.Utc),migrated.QuotaAccountingStartedAtUtc);
        Assert.Equal("summary",migrated.Summary.Id); Assert.Equal("medium",migrated.Summary.ReasoningEffort);
    }

    [Fact]
    public void VersionSevenPresetMigrationAddsStandardThreePointFiveDefault()
    {
        var configured=Configuration(); var registry=Presets(configured); registry.EnsureFile();
        var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configured.OperatorAccess.PresetRegistryPath))!.AsObject();
        node["schemaVersion"]=7; node.Remove("defaultGuidanceVariant");
        File.WriteAllText(configured.OperatorAccess.PresetRegistryPath,node.ToJsonString());

        registry.EnsureFile(); var migrated=registry.Read();

        Assert.Equal(11,migrated.SchemaVersion);
        Assert.Equal("3.7.0",migrated.DefaultGuidanceVariant);
        Assert.Equal("itc-scientific-guidance-3.7.0-experimentdesign",ScientificGuidance.RevisionFor(migrated.DefaultGuidanceVariant));
    }

    [Fact]
    public void MigratesLogicalStandardGuidanceDefaultToExplicitVersion()
    {
        var configured = Configuration(); var registry = Presets(configured); registry.EnsureFile();
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configured.OperatorAccess.PresetRegistryPath))!.AsObject();
        node["schemaVersion"] = 10;
        node["defaultGuidanceVariant"] = "standard";
        File.WriteAllText(configured.OperatorAccess.PresetRegistryPath, node.ToJsonString());

        registry.EnsureFile(); var migrated = registry.Read();

        Assert.Equal(11, migrated.SchemaVersion);
        Assert.Equal("3.7.0", migrated.DefaultGuidanceVariant);
        Assert.Equal("3.7.0", System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(configured.OperatorAccess.PresetRegistryPath))!["defaultGuidanceVariant"]!.GetValue<string>());
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

    [Theory]
    [InlineData(InterpretationAccessTiers.Standard)]
    [InlineData(InterpretationAccessTiers.Advanced)]
    [InlineData(InterpretationAccessTiers.Administrator)]
    public void SummaryIsAvailableToEveryAuthenticatedTier(string tier)
    {
        var configured=Configuration(); var registry=Registry(configured); var created=registry.Create("Summary",1,false,tier);
        var request=new DefaultHttpContext().Request; request.Headers.Authorization="Bearer "+created.Code;
        using var document=System.Text.Json.JsonDocument.Parse("{}");
        var summary=new ValidatedInterpretationRequest(FtItcInterpretationClient.RequestSchemaVersion,"summary",
            "0123456789abcdef0123456789abcdef","summary","itc-summary-markdown-1.0","summary",document.RootElement.Clone());
        Assert.True(InterpretationGenerationSelector.TrySelect(request,summary,configured,registry,Presets(configured),out var selection,out _));
        Assert.Equal("summary",selection.TaskType); Assert.Equal("gpt-5.6-luna",selection.Model); Assert.Equal("medium",selection.ReasoningEffort);
    }

    [Fact]
    public void InitializesIndexedWalDatabaseAndCalculatesVisibleAndLongContextCosts()
    {
        var configured = Configuration(); var store = Store(configured);
        var estimate = store.Estimate("gpt-5.6-terra", 300_000, 100_000, 20_000, 10_000, 2);
        Assert.Equal(1.045m, estimate.Combined);
        var receipt = new InterpretationUsageAttempt { RequestId="r", TaskType="interpretation", GuidanceVariant="3.7.0-structured", GuidanceRevision=ScientificGuidance.RevisionFor("3.7.0-structured"), AttemptNumber=1, TimestampUtc=DateTime.UtcNow, Model="gpt-5.6-terra", ReasoningEffort="high", FileSearchEnabled=true, FileSearchCalls=2, InputTokens=300000, CachedInputTokens=100000, CacheWriteTokens=20000, OutputTokens=10000, ReasoningTokens=4000, VisibleOutputTokens=6000, TotalTokens=310000, CombinedCost=estimate.Combined, Outcome="success", HttpStatus=200 };
        SeedCompleted(store, new InterpretationUsageRequest { RequestId="r", TaskType="interpretation", TraceId="t", StartedUtc=DateTime.UtcNow, CompletedUtc=DateTime.UtcNow, EffectiveModel="gpt-5.6-terra", EffectiveReasoning="high", RequestedGuidanceVariant="3.7.0-structured", EffectiveGuidanceVariant="3.7.0-structured", GuidanceRevision=ScientificGuidance.RevisionFor("3.7.0-structured"), Outcome="success", HttpStatus=200 }, receipt);
        using var connection = store.OpenForCommand();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT visible_output_tokens FROM attempts WHERE request_id='r';";
        Assert.Equal(6000L, (long)command.ExecuteScalar()!);
        command.CommandText = "SELECT task_type FROM requests WHERE request_id='r';"; Assert.Equal("interpretation", (string)command.ExecuteScalar()!);
        command.CommandText = "SELECT effective_guidance_variant || ':' || guidance_revision FROM requests WHERE request_id='r';"; Assert.Equal("3.7.0-structured:"+ScientificGuidance.RevisionFor("3.7.0-structured"),(string)command.ExecuteScalar()!);
        command.CommandText = "SELECT guidance_variant || ':' || guidance_revision FROM attempts WHERE request_id='r';"; Assert.Equal("3.7.0-structured:"+ScientificGuidance.RevisionFor("3.7.0-structured"),(string)command.ExecuteScalar()!);
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
        query.CommandText="SELECT count(*) FROM pragma_table_info('requests') WHERE name='task_type'"; Assert.Equal(1L,(long)query.ExecuteScalar()!);
        query.CommandText="SELECT count(*) FROM pragma_table_info('attempts') WHERE name='task_type'"; Assert.Equal(1L,(long)query.ExecuteScalar()!);
        query.CommandText="SELECT count(*) FROM pragma_table_info('requests') WHERE name IN ('requested_guidance_variant','effective_guidance_variant','guidance_revision')"; Assert.Equal(3L,(long)query.ExecuteScalar()!);
        query.CommandText="SELECT count(*) FROM pragma_table_info('attempts') WHERE name IN ('guidance_variant','guidance_revision')"; Assert.Equal(2L,(long)query.ExecuteScalar()!);
        query.CommandText="SELECT version FROM schema_info"; Assert.True((long)query.ExecuteScalar()! > 4);
    }

    static void SeedCompleted(InterpretationUsageStore store, InterpretationUsageRequest request,
        InterpretationUsageAttempt? receipt = null)
    {
        request.ServerExecutionId = request.RequestId;
        request.ClientRequestId = request.RequestId;
        Assert.Equal(InterpretationAdmissionStatus.Admitted,
            store.TryAdmit(request, null, DateTime.MinValue).Status);
        if (receipt is not null || request.EstimatedCost.HasValue || request.ProviderAttempts > 0)
        {
            receipt ??= new InterpretationUsageAttempt
            {
                RequestId = request.ServerExecutionId, AttemptNumber = 1, TimestampUtc = request.StartedUtc,
                TaskType = request.TaskType, Model = request.EffectiveModel, ReasoningEffort = request.EffectiveReasoning,
                InputTokens = request.InputTokens, CachedInputTokens = request.CachedInputTokens,
                CacheWriteTokens = request.CacheWriteTokens, OutputTokens = request.OutputTokens,
                ReasoningTokens = request.ReasoningTokens, VisibleOutputTokens = request.VisibleOutputTokens,
                TotalTokens = request.TotalTokens, CombinedCost = request.EstimatedCost,
                Outcome = request.Outcome, HttpStatus = request.HttpStatus,
            };
            receipt.ServerExecutionId = request.ServerExecutionId;
            store.BeginAttempt(request.ServerExecutionId, receipt.AttemptNumber);
            store.RecordAttempt(receipt);
        }
        store.FinalizeRequest(request);
    }

    InterpretationOptions Configuration()
    {
        Directory.CreateDirectory(directory);
        return new InterpretationOptions
        {
            OpenAI = new OpenAIInterpretationOptions { Model="gpt-5.6-terra", ReasoningEffort="medium" },
            OperatorAccess = new InterpretationOperatorOptions { Enabled=true, RegistryPath=Path.Combine(directory,"operators.json"), PresetRegistryPath=Path.Combine(directory,"presets.json"), DefaultLifetimeDays=30 },
            UsageLog = new InterpretationUsageOptions { Enabled=true, DatabasePath=Path.Combine(directory,"usage.db") },
            AvailabilityPolicyPath = Path.Combine(directory,"interpretation-service.json"),
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

    void ConfigureRegistration(InterpretationOptions configured, bool enabled)
    {
        configured.Registration.Enabled = enabled;
        configured.Registration.SiteKey = "0x4AAAAAAdiagnostic";
        configured.Registration.DatabasePath = Path.Combine(directory, "registration.db");
        configured.Registration.SecretConfigurationPath = Path.Combine(directory, "turnstile.json");
        configured.Registration.MailConfigurationPath = Path.Combine(directory, "mail.json");
        configured.Registration.OperatorRegistryPath = Path.Combine(directory, "registered-operators.json");
        configured.Registration.AvailabilityPolicyPath = Path.Combine(directory, "registration-policy.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(configured.Registration.AvailabilityPolicyPath, "{\"Enabled\":true}");
        File.WriteAllText(configured.Registration.SecretConfigurationPath, "{\"siteKey\":\"0x4AAAAAAdiagnostic\",\"secretKey\":\"secret-not-used\"}");
        File.WriteAllText(configured.Registration.MailConfigurationPath, "{\"apiKey\":\"re_test_key_not_used\",\"from\":\"FT-ITC <mist@example.invalid>\",\"replyTo\":\"support@example.invalid\"}");
    }

    static OperatorCodeRegistry Registry(InterpretationOptions value) => new(Options.Create(value), NullLogger<OperatorCodeRegistry>.Instance);
    static InterpretationUsageStore Store(InterpretationOptions value) => new(Options.Create(value), NullLogger<InterpretationUsageStore>.Instance);
    static GenerationPresetRegistry Presets(InterpretationOptions value) => new(Options.Create(value));
    static ValidatedInterpretationRequest Request(string profile) { using var document=System.Text.Json.JsonDocument.Parse("{}"); return new(FtItcInterpretationClient.RequestSchemaVersion,"interpretation","0123456789abcdef0123456789abcdef",profile,"test","test",document.RootElement.Clone()); }
    static ConsoleKeyInfo Key(ConsoleKey key, char value = '\0', ConsoleModifiers modifiers = 0) =>
        new(value, key,
            (modifiers & ConsoleModifiers.Shift) != 0,
            (modifiers & ConsoleModifiers.Alt) != 0,
            (modifiers & ConsoleModifiers.Control) != 0);
    static IServiceProvider Services(InterpretationOptions value)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(Options.Create(value));
        services.AddSingleton<OperatorCodeRegistry>(); services.AddSingleton<GenerationPresetRegistry>(); services.AddSingleton<InterpretationUsageStore>(); services.AddSingleton<InterpretationQuotaService>(); services.AddSingleton<InterpretationServiceAvailability>();
        var provider=services.BuildServiceProvider(); provider.GetRequiredService<GenerationPresetRegistry>().EnsureFile(); return provider;
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
