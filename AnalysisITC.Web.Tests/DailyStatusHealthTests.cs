using System.Net;
using System.Text.Json;
using AnalysisITC.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

[Collection("Daily status storage")]
public sealed class DailyStatusHealthTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-status-health-" + Guid.NewGuid().ToString("N"));

    public DailyStatusHealthTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void RegistryProbePreservesReadOnlyRegistryAndRemovesItsSyntheticFiles()
    {
        var path = Path.Combine(directory, "registered.json");
        const string original = "[{\"id\":\"original-account\"}]";
        File.WriteAllText(path, original);
        var stamp = File.GetLastWriteTimeUtc(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead);
        try
        {
            var result = HealthCheckService.CheckRegistryStorage(path);

            Assert.Equal(HealthCheckState.Pass, result.State);
            Assert.Equal(original, File.ReadAllText(path));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(directory));
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(path));
        }
        finally
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void RegistryProbeDoesNotCreateTheRealRegistryWhenItIsAbsent()
    {
        var path = Path.Combine(directory, "registered.json");
        Assert.Equal(HealthCheckState.Pass, HealthCheckService.CheckRegistryStorage(path).State);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void RegistryProbeFailsWhenItCannotCreateAndReplaceSiblingFiles()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        var path = Path.Combine(directory, "registered.json");
        File.WriteAllText(path, "[]");
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var result = HealthCheckService.CheckRegistryStorage(path);
            Assert.Equal(HealthCheckState.Fail, result.State);
            Assert.DoesNotContain(directory, result.Reason);
            Assert.Equal("[]", File.ReadAllText(path));
        }
        finally { File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("WAL")]
    public void DatabaseProbeRollsBackItsSchemaAndRows(string journalMode)
    {
        var path = Path.Combine(directory, "database.db");
        using var db = new SqliteConnection($"Data Source={path};Pooling=False");
        db.Open();
        using var query = db.CreateCommand();
        query.CommandText = $"PRAGMA journal_mode={journalMode}; CREATE TABLE accounts (name TEXT); INSERT INTO accounts VALUES ('original');";
        query.ExecuteNonQuery();

        Assert.Equal(HealthCheckState.Pass, HealthCheckService.CheckDatabase("test", "Test database", path).State);

        query.CommandText = "SELECT name FROM accounts";
        Assert.Equal("original", query.ExecuteScalar());
        query.CommandText = "SELECT group_concat(name) FROM sqlite_master WHERE type='table'";
        Assert.Equal("accounts", query.ExecuteScalar());
        query.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", query.ExecuteScalar());
    }

    [Fact]
    public void DatabaseProbeFailsWhenDatabaseIsWritableButItsJournalCannotBeCreated()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        var path = Path.Combine(directory, "database.db");
        using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            db.Open();
            using var query = db.CreateCommand();
            query.CommandText = "PRAGMA journal_mode=DELETE; CREATE TABLE accounts (id INTEGER);";
            query.ExecuteNonQuery();
        }
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            // Opening the file for writing succeeds: only a real SQLite write exposes this failure.
            using (File.OpenWrite(path)) { }
            var result = HealthCheckService.CheckDatabase("test", "Test database", path);
            Assert.Equal(HealthCheckState.Fail, result.State);
            Assert.DoesNotContain(path, result.Reason);
        }
        finally { File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    [Fact]
    public void DatabaseProbeDoesNotInitializeMissingApplicationStorage()
    {
        var path = Path.Combine(directory, "missing.db");
        Assert.Equal(HealthCheckState.Fail, HealthCheckService.CheckDatabase("test", "Test database", path).State);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void RegistrationDiagnosticDoesNotFlagSidecarsCreatedByItsOwnReadOnlySnapshot()
    {
        var configured = new InterpretationOptions
        {
            AvailabilityPolicyPath = Path.Combine(directory, "availability.json"),
            StatusEmailConfigurationPath = Path.Combine(directory, "status-email.json"),
            PublicAccessRegistryPath = Path.Combine(directory, "public-access.json"),
            TombstonePath = Path.Combine(directory, "tombstones.json"),
            OperatorAccess = new()
            {
                RegistryPath = Path.Combine(directory, "operators.json"),
                PresetRegistryPath = Path.Combine(directory, "presets.json"),
            },
            UsageLog = new() { Enabled = true, DatabasePath = Path.Combine(directory, "usage.db") },
            Registration = new()
            {
                Enabled = false, AvailabilityPolicyPath = Path.Combine(directory, "registration.json"),
                DatabasePath = Path.Combine(directory, "registration.db"),
                DataProtectionKeysPath = Path.Combine(directory, "registration-keys"),
                OperatorRegistryPath = Path.Combine(directory, "registered.json"),
                SecretConfigurationPath = Path.Combine(directory, "registration-secret.json"),
                MailConfigurationPath = Path.Combine(directory, "registration-mail.json"),
            }
        };
        var options = Options.Create(configured);
        var usage = new InterpretationUsageStore(options, NullLogger<InterpretationUsageStore>.Instance);
        using (var db = usage.OpenForCommand())
        {
            db.Close();
            SqliteConnection.ClearPool(db);
        }
        Assert.False(File.Exists(configured.UsageLog.DatabasePath + "-wal"));
        Assert.False(File.Exists(configured.UsageLog.DatabasePath + "-shm"));
        var diagnostic = new RegistrationPipelineDiagnostic(options,
            new RegistrationAvailability(options), new InterpretationServiceAvailability(options));

        var report = diagnostic.Run();

        Assert.False(report.HasFailure, string.Join("\n", report.Steps.Select(step => step.Name + ": " + step.Detail)));
        Assert.Equal(RegistrationDiagnosticState.Pass, report.Steps[4].State);
        using var reader = usage.OpenForCommand();
        using var count = reader.CreateCommand();
        count.CommandText = "SELECT count(*) FROM execution_usage";
        Assert.Equal(0L, count.ExecuteScalar());
    }

    [Theory]
    [InlineData("pass", false)]
    [InlineData("warning", true)]
    [InlineData("fail", true)]
    [InlineData("skipped", false)]
    public async Task EmailSubjectUsesServiceHealthStatesRegardlessOfReasonWording(string state, bool attention)
    {
        using var http = Response(HealthJson(state));
        string? subject = null, html = null;
        var reporter = Reporter(() => ServiceHealthCheck.ReadAsync(http), (value, body) => { subject = value; html = body; });

        Assert.Equal(0, await DailyStatusEmail.RunAsync(["send"], reporter, new StringWriter(), new StringWriter()));

        Assert.True(subject?.StartsWith(attention ? "[ATTENTION]" : "[OK]", StringComparison.Ordinal) == true, subject + "\n" + html);
        if (attention) Assert.Contains(state.ToUpperInvariant(), html);
        // The intentionally contradictory overall label is ignored in favour of individual states.
        Assert.Equal(attention, (await ServiceHealthCheck.ReadAsync(http)).HasAttention);
    }

    [Theory]
    [InlineData("{}", 200)]
    [InlineData("not JSON containing private details", 200)]
    [InlineData("{}", 503)]
    [InlineData("missing-state", 200)]
    [InlineData("unknown-state", 200)]
    [InlineData("empty-checks", 200)]
    public async Task UnusableServiceReportFlagsEmailWithoutFallingBackToRootChecks(string body, int status)
    {
        body = body switch
        {
            "missing-state" => HealthJson("pass").Replace("\"state\":\"pass\",", ""),
            "unknown-state" => HealthJson("futureUnknownState"),
            "empty-checks" => "{\"checkedAtUtc\":\"2026-10-15T12:00:00Z\",\"overall\":\"healthy\",\"groups\":[]}",
            _ => body
        };
        using var http = Response(body, (HttpStatusCode)status);
        string? subject = null, html = null;
        var reporter = Reporter(() => ServiceHealthCheck.ReadAsync(http), (value, text) => { subject = value; html = text; });

        Assert.Equal(0, await DailyStatusEmail.RunAsync(["send"], reporter, new StringWriter(), new StringWriter()));
        Assert.StartsWith("[ATTENTION]", subject, StringComparison.Ordinal);
        Assert.Contains("service health checks unavailable", html);
        Assert.DoesNotContain("private details", html);
    }

    [Fact]
    public async Task ServiceReportConnectionFailureStillProducesAnAttentionEmail()
    {
        string? subject = null;
        var reporter = Reporter(() => throw new HttpRequestException("private connection detail"), (value, _) => subject = value);
        Assert.Equal(0, await DailyStatusEmail.RunAsync(["send"], reporter, new StringWriter(), new StringWriter()));
        Assert.StartsWith("[ATTENTION]", subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DailyReaderConsumesTheWebProcessHealthEndpointAndForwardedRequestsStayBlocked()
    {
        using var factory = new InterpretationTestFactory();
        using var loopbackFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter, LoopbackStartupFilter>()));
        using var http = loopbackFactory.CreateClient();

        var report = await ServiceHealthCheck.ReadAsync(http);
        Assert.Contains(report.Groups.SelectMany(group => group.Checks), check => check.Id == "interpretation.usage");
        var registries = report.Groups.SelectMany(group => group.Checks).Where(check => check.Id.StartsWith("interpretation.", StringComparison.Ordinal)).ToArray();
        Assert.Contains(registries, check => check.Id == "interpretation.presets" && check.State == HealthCheckState.Pass);
        Assert.Contains(registries, check => check.Id == "interpretation.public-access" && check.State == HealthCheckState.Pass);
        Assert.Contains(registries, check => check.Id == "interpretation.operator-codes" && check.State == HealthCheckState.Pass);
        Assert.Contains(registries, check => check.Id == "interpretation.registered-operators" && check.State == HealthCheckState.Pass);
        Assert.Contains(report.Groups, group => group.Name == "Registration");

        var presetPath = factory.Services.GetRequiredService<IOptions<InterpretationOptions>>().Value.OperatorAccess.PresetRegistryPath;
        await File.WriteAllTextAsync(presetPath, "not-json");
        var malformed = await ServiceHealthCheck.ReadAsync(http);
        Assert.Contains(malformed.Groups.SelectMany(group => group.Checks),
            check => check.Id == "interpretation.presets" && check.State == HealthCheckState.Fail);

        using var forwarded = new HttpRequestMessage(HttpMethod.Get, ServiceHealthCheck.Url);
        forwarded.Headers.Add("X-Forwarded-For", "203.0.113.1");
        using var response = await http.SendAsync(forwarded);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ServiceAndEndpointFailuresFlagEmailWithoutMatchingPhrases(bool serviceFailure, bool endpointFailure)
    {
        using var http = Response(HealthJson("pass"));
        string? subject = null;
        var reporter = Reporter(() => ServiceHealthCheck.ReadAsync(http), (value, _) => subject = value,
            service: new("inactive", serviceFailure), endpoint: new("arbitrary diagnostic wording", endpointFailure));
        Assert.Equal(0, await DailyStatusEmail.RunAsync(["send"], reporter, new StringWriter(), new StringWriter()));
        Assert.StartsWith("[ATTENTION]", subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiagnosticWordingCannotCreateAFailureWhenAllStatesPass()
    {
        using var http = Response(HealthJson("pass"));
        string? subject = null;
        var reporter = Reporter(() => ServiceHealthCheck.ReadAsync(http), (value, _) => subject = value,
            service: new("A label containing : FAIL is not a check state", false),
            endpoint: new("A label containing check unavailable is not a check state", false));
        Assert.Equal(0, await DailyStatusEmail.RunAsync(["send"], reporter, new StringWriter(), new StringWriter()));
        Assert.StartsWith("[OK]", subject, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task IntentionalPauseSuppressesOnlyInterpretationUnavailability(bool paused, bool transportFailure, bool attention)
    {
        using var http = Response(HealthJson("pass"));
        string? subject = null;
        var reporter = Reporter(() => ServiceHealthCheck.ReadAsync(http), (value, _) => subject = value,
            endpoint: new("HTTP response", transportFailure, InterpretationUnavailable: true), paused: paused);
        Assert.Equal(0, await DailyStatusEmail.RunAsync(["send"], reporter, new StringWriter(), new StringWriter()));
        Assert.StartsWith(attention ? "[ATTENTION]" : "[OK]", subject, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(200, "{\"available\":true}", false, false)]
    [InlineData(200, "{\"available\":false}", false, true)]
    [InlineData(503, "{\"available\":true}", true, false)]
    [InlineData(200, "{\"available\":\"true\"}", true, false)]
    [InlineData(200, "{}", true, false)]
    public async Task EndpointCheckSeparatesHttpAndProtocolFailureFromAvailability(int status, string body, bool attention, bool unavailable)
    {
        using var http = Response(body, (HttpStatusCode)status);
        var result = await DailyStatusEmail.CheckEndpointAsync(http, "https://example.invalid/status");
        Assert.Equal(attention, result.HasAttention);
        Assert.Equal(unavailable, result.InterpretationUnavailable);
    }

    DailyStatusEmail Reporter(Func<Task<HealthReport>> health, Action<string, string> sent,
        DailyStatusCheck? service = null, DailyStatusCheck? endpoint = null, bool paused = false)
    {
        var configured = new InterpretationOptions
        {
            AvailabilityPolicyPath = Path.Combine(directory, "availability.json"),
            StatusEmailConfigurationPath = Path.Combine(directory, "status-email.json"),
            PublicAccessRegistryPath = Path.Combine(directory, "public-access.json"),
            TombstonePath = Path.Combine(directory, "tombstones.json"),
            OperatorAccess = new()
            {
                RegistryPath = Path.Combine(directory, "operators.json"),
                PresetRegistryPath = Path.Combine(directory, "presets.json"),
            },
            UsageLog = new() { Enabled = true, DatabasePath = Path.Combine(directory, "usage.db") },
            Registration = new()
            {
                Enabled = false, AvailabilityPolicyPath = Path.Combine(directory, "registration.json"),
                DatabasePath = Path.Combine(directory, "registration.db"),
                DataProtectionKeysPath = Path.Combine(directory, "registration-keys"),
                OperatorRegistryPath = Path.Combine(directory, "registered.json"),
                SecretConfigurationPath = Path.Combine(directory, "registration-secret.json"),
                MailConfigurationPath = Path.Combine(directory, "registration-mail.json"),
            }
        };
        var options = Options.Create(configured);
        var availability = new InterpretationServiceAvailability(options);
        if (paused) availability.Set("paused", null);
        var registration = new RegistrationAvailability(options);
        var usage = new InterpretationUsageStore(options, NullLogger<InterpretationUsageStore>.Instance);
        using (usage.OpenForCommand()) { }
        return new(usage, availability, Path.Combine(directory, "mail.json"),
            () => Task.FromResult(service ?? new("active", false)),
            _ => Task.FromResult(endpoint ?? new("HTTP 200; interpretation available", false)),
            (subject, body) => { sent(subject, body); return Task.CompletedTask; },
            () => new DateTimeOffset(2026, 10, 15, 6, 0, 0, TimeSpan.Zero),
            // Registration is disabled in these alert-aggregation tests. Do not
            // fingerprint pooled accounting journals while other suites clear pools.
            registrationDiagnostic: new(configured.Registration, Path.Combine(directory, "unused-diagnostic.db"), registration, availability),
            registrationSummary: new(options, registration, availability),
            storageCapacity: new([directory], _ => new("/", 100L << 30, 40L << 30)), healthCheck: health);
    }

    static string HealthJson(string state) => JsonSerializer.Serialize(new
    {
        checkedAtUtc = DateTimeOffset.UtcNow, overall = "healthy",
        groups = new[] { "Interpretation", "Registration", "Viewer", "Storage" }.Select(name => new
        {
            name, checks = new[] { new { id = name + ".check", name = "Storage operation", state, reason = "arbitrary diagnostic wording" } }
        })
    });

    static HttpClient Response(string body, HttpStatusCode status = HttpStatusCode.OK) => new(new ResponseHandler(body, status));
    sealed class ResponseHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    sealed class LoopbackStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                await continuation(context);
            });
            next(app);
        };
    }

    public void Dispose() => Directory.Delete(directory, true);
}

// Other suites clear every SQLite pool. These tests deliberately inspect the
// lifetime of a first read-only connection and its transient journal files.
[CollectionDefinition("Daily status storage", DisableParallelization = true)]
public sealed class DailyStatusHealthCollection;
