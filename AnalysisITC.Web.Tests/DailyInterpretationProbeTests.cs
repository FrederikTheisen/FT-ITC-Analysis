using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class DailyInterpretationProbeTests
{
    [Fact]
    public async Task PublicSubmissionExercisesRealRelayProviderAndAccountingThenReusesSavedResult()
    {
        using var fixture = new ProbeFixture();
        var now = DateTimeOffset.UtcNow;

        var result = await fixture.Probe.RunAsync(true, now);
        var repeated = await fixture.NewProbe().RunAsync(true, now.AddMinutes(1));
        var preview = await fixture.NewProbe().RunAsync(false, now.AddMinutes(2));

        Assert.Equal(HealthCheckState.Pass, result.State);
        Assert.Equal(result, repeated);
        Assert.Equal(result, preview);
        Assert.Equal(1, fixture.ProviderCalls);
        Assert.Equal(1, result.ProviderAttempts);
        Assert.True(result.KnownCost > 0);
        var payload = Assert.Single(fixture.ProviderPayloads);
        Assert.Equal(256, payload.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("none", payload.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.False(payload.TryGetProperty("tools", out _));
        Assert.False(payload.TryGetProperty("tool_choice", out _));
        Assert.Contains("synthetic", payload.GetProperty("input").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("instructions").GetString()));
        var state = File.ReadAllText(Assert.Single(Directory.GetFiles(fixture.Options.DailyProbe.StateDirectory)));
        Assert.DoesNotContain(fixture.Code, state);
        Assert.DoesNotContain("## Overall interpretation", state);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Assert.Single(Directory.GetFiles(fixture.Options.DailyProbe.StateDirectory))));
    }

    [Fact]
    public async Task PreviewDoesNotSubmitOrCreateStateAndIntentionalPauseIsSkipped()
    {
        using var fixture = new ProbeFixture();
        Assert.Equal(HealthCheckState.Skipped, (await fixture.Probe.RunAsync(false, DateTimeOffset.UtcNow)).State);
        Assert.False(Directory.Exists(fixture.Options.DailyProbe.StateDirectory));
        fixture.Availability.Set("paused", "planned maintenance");
        Assert.Equal(HealthCheckState.Skipped, (await fixture.Probe.RunAsync(true, DateTimeOffset.UtcNow)).State);
        Assert.False(Directory.Exists(fixture.Options.DailyProbe.StateDirectory));
        Assert.Equal(0, fixture.ProviderCalls);
    }

    [Fact]
    public async Task CorruptOrInterruptedSavedRunNeverDispatchesAgain()
    {
        using var fixture = new ProbeFixture();
        Directory.CreateDirectory(fixture.Options.DailyProbe.StateDirectory);
        var now = DateTimeOffset.UtcNow;
        var path = Path.Combine(fixture.Options.DailyProbe.StateDirectory, DailyInterpretationProbe.Day(now).ToString("yyyy-MM-dd") + ".json");
        File.WriteAllText(path, "unfinished JSON");
        Assert.Equal(HealthCheckState.Fail, (await fixture.Probe.RunAsync(true, now)).State);
        Assert.Equal(0, fixture.ProviderCalls);
    }

    [Fact]
    public async Task ConcurrentStandaloneChecksClaimTheDayBeforeDispatch()
    {
        using var fixture = new ProbeFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ProviderResponse = async () => { entered.SetResult(); await release.Task; return ProbeFixture.Success(); };
        var now = DateTimeOffset.UtcNow;
        var first = fixture.Probe.RunAsync(true, now);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var second = await fixture.NewProbe().RunAsync(true, now);
            Assert.Equal(HealthCheckState.Warning, second.State);
            Assert.Equal(1, fixture.ProviderCalls);
        }
        finally { release.TrySetResult(); }
        Assert.Equal(HealthCheckState.Pass, (await first).State);
        Assert.Equal(HealthCheckState.Pass, (await fixture.NewProbe().RunAsync(true, now)).State);
        Assert.Equal(1, fixture.ProviderCalls);
    }

    [Fact]
    public async Task FailedProviderReceiptIsRetainedAndFailureIsNotRetried()
    {
        using var fixture = new ProbeFixture();
        fixture.ProviderResponse = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"code":"retrieval_failed"},"usage":{"input_tokens":100,"output_tokens":5,"total_tokens":105}}"""),
        });
        var now = DateTimeOffset.UtcNow;
        var result = await fixture.Probe.RunAsync(true, now);
        Assert.Equal(HealthCheckState.Fail, result.State);
        Assert.Equal(result, await fixture.NewProbe().RunAsync(true, now));
        Assert.Equal(1, fixture.ProviderCalls);
        Assert.Equal(1, result.ProviderAttempts);
        Assert.True(result.KnownCost > 0);
        Assert.Equal(1L, fixture.Scalar("SELECT count(*) FROM attempt_receipts"));
    }

    [Fact]
    public async Task TransportFailureIsSavedWithoutCredentialsAndWithoutRetry()
    {
        using var fixture = new ProbeFixture();
        var calls = 0;
        using var http = new HttpClient(new CallbackHandler((_, _) =>
        {
            calls++; throw new HttpRequestException("private transport details " + fixture.Code);
        }));
        var probe = fixture.NewProbe(http);
        var now = DateTimeOffset.UtcNow;
        var result = await probe.RunAsync(true, now);
        Assert.Equal(HealthCheckState.Fail, result.State);
        Assert.Equal(result, await probe.RunAsync(true, now));
        Assert.Equal(1, calls);
        Assert.DoesNotContain(fixture.Code, JsonSerializer.Serialize(result));
        Assert.DoesNotContain("private transport", result.Reason);
    }

    [Fact]
    public async Task InvalidResponseCannotBeReportedAsPass()
    {
        using var fixture = new ProbeFixture();
        using var http = new HttpClient(new CallbackHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") })));
        Assert.Equal(HealthCheckState.Fail, (await fixture.NewProbe(http).RunAsync(true, DateTimeOffset.UtcNow)).State);
    }

    [Fact]
    public async Task ServerRejectsAnArbitraryRequestIdForTheDiagnosticAccount()
    {
        using var fixture = new ProbeFixture();
        var body = DailyInterpretationProbe.BuildRequest(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/interpretation/generate")
        { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new("Bearer", fixture.Code);
        request.Headers.Add(DailyInterpretationProbe.RequestHeader, "1");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, fixture.ProviderCalls);
    }

    [Fact]
    public async Task TheApiAlsoPreventsAnotherDailyDispatchWithoutRelyingOnTheStandaloneCache()
    {
        using var fixture = new ProbeFixture();
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(HealthCheckState.Pass, (await fixture.Probe.RunAsync(true, now)).State);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/interpretation/generate")
        { Content = JsonContent.Create(DailyInterpretationProbe.BuildRequest(DailyInterpretationProbe.RequestId(fixture.AccountId, now), now)) };
        request.Headers.Authorization = new("Bearer", fixture.Code);
        request.Headers.Add(DailyInterpretationProbe.RequestHeader, "1");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, fixture.ProviderCalls);
        Assert.Equal(1L, fixture.Scalar("SELECT sum(request_count_eligible) FROM executions"));
        Assert.Equal(1L, fixture.Scalar("SELECT count(*) FROM attempt_receipts"));
    }

    [Fact]
    public async Task ARequestLargerThanFourKiBIsRejectedBeforeProviderDispatch()
    {
        using var fixture = new ProbeFixture();
        var now = DateTimeOffset.UtcNow;
        var body = DailyInterpretationProbe.BuildRequest(DailyInterpretationProbe.RequestId(fixture.AccountId, now), now);
        body["padding"] = new string('x', 4096);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/interpretation/generate") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new("Bearer", fixture.Code);
        request.Headers.Add(DailyInterpretationProbe.RequestHeader, "1");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, fixture.ProviderCalls);
    }

    [Fact]
    public async Task MisconfiguredAccountCannotFallBackToOrdinaryProviderGeneration()
    {
        using var fixture = new ProbeFixture();
        var code = fixture.Services.GetRequiredService<PublicAccessRegistry>().Create().Code;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/interpretation/generate")
        { Content = JsonContent.Create(DailyInterpretationProbe.BuildRequest(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow)) };
        request.Headers.Authorization = new("Bearer", code);
        request.Headers.Add(DailyInterpretationProbe.RequestHeader, "1");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, fixture.ProviderCalls);
    }

    [Fact]
    public async Task AValidResponseWithFailedFinalizationIsNotAHealthyDiagnostic()
    {
        using var fixture = new ProbeFixture();
        fixture.Usage.FaultInjector = phase => phase == "finalize" ? new IOException("test failure") : null;
        var result = await fixture.Probe.RunAsync(true, DateTimeOffset.UtcNow);
        Assert.Equal(HealthCheckState.Fail, result.State);
        Assert.Contains("accounting", result.Reason);
        Assert.Equal(1, result.ProviderAttempts);
        Assert.Equal(1L, fixture.Scalar("SELECT count(*) FROM attempt_receipts"));
        Assert.True(result.KnownCost > 0);
    }

    [Fact]
    public async Task OrdinaryCallerCannotClaimDiagnosticTreatmentAndUserReportExcludesOnlyRealDiagnostics()
    {
        using var fixture = new ProbeFixture();
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(HealthCheckState.Pass, (await fixture.Probe.RunAsync(true, now)).State);
        var code = fixture.Services.GetRequiredService<PublicAccessRegistry>().Create().Code;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/interpretation/generate")
        { Content = JsonContent.Create(DailyInterpretationProbe.BuildRequest(DailyInterpretationProbe.RequestId(fixture.AccountId, now), now)) };
        request.Headers.Authorization = new("Bearer", code);
        request.Headers.Add("X-FTITC-Diagnostic", "true");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var normalPayload = fixture.ProviderPayloads[1];
        Assert.Equal(4321, normalPayload.GetProperty("max_output_tokens").GetInt32());
        Assert.True(normalPayload.TryGetProperty("tools", out _));
        Assert.Equal("low", normalPayload.GetProperty("reasoning").GetProperty("effort").GetString());

        var report = await fixture.Reporter().CreateStatusReportAsync(DailyInterpretationProbe.Day(now));
        var ordinary = report.Text.Split("Interpretation diagnostic", StringSplitOptions.None)[0];
        Assert.Contains("Requests: 1\n", ordinary);
        Assert.Contains("Provider attempts: 1\n", ordinary);
        Assert.DoesNotContain(fixture.AccountId, ordinary);
        Assert.Contains("Daily public submission result: PASS", report.Text);
        Assert.Contains("This result applies only to the checked time", report.Text);
        Assert.Contains("Previous-day submissions: 1", report.Text);
        Assert.Equal(2L, fixture.Scalar("SELECT count(*) FROM attempt_receipts"));
        Assert.Equal(1L, fixture.Scalar("SELECT count(*) FROM execution_usage WHERE effective_preset='daily-diagnostic'"));
    }

    [Fact]
    public async Task FailedMailRetriesReuseOneCompletedProbeAndCheckCommandDoesNotSendMail()
    {
        using var fixture = new ProbeFixture();
        var sends = 0;
        var reporter = fixture.Reporter((_, _) => ++sends < 3 ? Task.FromException(new IOException()) : Task.CompletedTask);
        Assert.Equal(0, await DailyStatusEmail.RunAsync(["send", "--live-checks"], reporter, new StringWriter(), new StringWriter()));
        Assert.Equal(3, sends);
        Assert.Equal(1, fixture.ProviderCalls);
        await DailyStatusEmail.RunAsync(["check"], reporter, new StringWriter(), new StringWriter());
        Assert.Equal(3, sends);
        Assert.Equal(1, fixture.ProviderCalls);
    }

    [Fact]
    public void DailyIdentityUsesCopenhagenDateAcrossUtcMidnightAndDaylightSaving()
    {
        const string account = "account";
        var first = DateTimeOffset.Parse("2026-10-18T22:30:00Z");
        var sameDay = DateTimeOffset.Parse("2026-10-19T12:00:00Z");
        var nextDay = DateTimeOffset.Parse("2026-10-19T22:30:00Z");
        Assert.Equal(new DateOnly(2026, 10, 19), DailyInterpretationProbe.Day(first));
        Assert.Equal(DailyInterpretationProbe.RequestId(account, first), DailyInterpretationProbe.RequestId(account, sameDay));
        Assert.NotEqual(DailyInterpretationProbe.RequestId(account, first), DailyInterpretationProbe.RequestId(account, nextDay));
        Assert.Equal(new DateOnly(2026, 10, 25), DailyInterpretationProbe.Day(DateTimeOffset.Parse("2026-10-24T22:30:00Z")));
        Assert.Equal(new DateOnly(2026, 10, 25), DailyInterpretationProbe.Day(DateTimeOffset.Parse("2026-10-25T22:30:00Z")));
    }

    sealed class ProbeFixture : WebApplicationFactory<Program>
    {
        readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-daily-probe-test-" + Guid.NewGuid().ToString("N"));
        bool disposed;
        public int ProviderCalls;
        public List<JsonElement> ProviderPayloads { get; } = [];
        public Func<Task<HttpResponseMessage>> ProviderResponse { get; set; } = () => Task.FromResult(Success());
        public HttpClient Client { get; }
        public InterpretationOptions Options { get; }
        public InterpretationServiceAvailability Availability => Services.GetRequiredService<InterpretationServiceAvailability>();
        public InterpretationUsageStore Usage => Services.GetRequiredService<InterpretationUsageStore>();
        public string Code { get; }
        public string AccountId => Options.DailyProbe.OperatorCodeId;
        public DailyInterpretationProbe Probe => NewProbe();

        public ProbeFixture()
        {
            Directory.CreateDirectory(directory);
            Client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://app.ft-itc.org"), AllowAutoRedirect = false });
            Options = Services.GetRequiredService<IOptions<InterpretationOptions>>().Value;
            var created = Services.GetRequiredService<OperatorCodeRegistry>().Create("Daily diagnostic", null, true, InterpretationAccessTiers.Standard);
            Options.DailyProbe.OperatorCodeId = created.Record.Id;
            Code = created.Code;
            File.WriteAllText(Options.DailyProbe.CredentialPath, JsonSerializer.Serialize(new { accessCode = Code }));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Options.DailyProbe.CredentialPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        public DailyInterpretationProbe NewProbe(HttpClient? http = null) => new(Options, Availability, Usage, http ?? Client);

        public DailyStatusEmail Reporter(Func<string, string, Task>? send = null) => new(Usage, Availability, Options.StatusEmailConfigurationPath,
            () => Task.FromResult(new DailyStatusCheck("active", false)),
            _ => Task.FromResult(new DailyStatusCheck("HTTP 200; interpretation available", false)), send,
            () => DateTimeOffset.UtcNow, interpretationDiagnostic: Probe);

        public long Scalar(string sql)
        { using var db = Usage.OpenForCommand(); using var q = db.CreateCommand(); q.CommandText = sql; return Convert.ToInt64(q.ExecuteScalar()); }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            WebTestHostIsolation.ConfigureIsolatedHost(builder, directory);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["Interpretation:Enabled"] = "true", ["Interpretation:OperatorAccess:Enabled"] = "true",
                    ["Interpretation:Registration:Enabled"] = "false", ["Interpretation:UsageLog:Enabled"] = "true",
                    ["Interpretation:RateLimit:PermitLimit"] = "100",
                    ["Interpretation:OpenAI:Model"] = "gpt-5.6-luna", ["Interpretation:OpenAI:ApiKey"] = "test-only-not-a-key",
                    ["Interpretation:OpenAI:ReasoningEffort"] = "low", ["Interpretation:OpenAI:VectorStoreId"] = "vs_test",
                    ["Interpretation:OpenAI:MaxOutputTokens"] = "4321",
                    ["Interpretation:DailyProbe:Enabled"] = "true",
                    ["Interpretation:DailyProbe:CredentialPath"] = Path.Combine(directory, "probe-credential.json"),
                    ["Interpretation:DailyProbe:StateDirectory"] = Path.Combine(directory, "probe-state"),
                    ["Interpretation:Pricing:gpt-5.6-luna:Revision"] = "test",
                    ["Interpretation:Pricing:gpt-5.6-luna:InputPerMillion"] = "2",
                    ["Interpretation:Pricing:gpt-5.6-luna:OutputPerMillion"] = "12",
                };
                foreach (var model in new[] { "gpt-5.6-luna", "gpt-5.6-terra", "gpt-5.6-sol" })
                    foreach (var (effort, index) in new[] { "none", "low", "medium", "high" }.Select((x, i) => (x, i)))
                        values[$"Interpretation:AllowedModels:{model}:ReasoningEfforts:{index}"] = effort;
                config.AddInMemoryCollection(values);
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAnalysisInterpretationProvider>();
                services.AddScoped<IAnalysisInterpretationProvider>(provider => new OpenAIInterpretationProvider(
                    new HttpClient(new CallbackHandler(async (request, _) =>
                    {
                        Interlocked.Increment(ref ProviderCalls);
                        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                        ProviderPayloads.Add(json.RootElement.Clone());
                        return await ProviderResponse();
                    })), provider.GetRequiredService<IOptions<InterpretationOptions>>(), provider.GetRequiredService<InterpretationUsageStore>()));
            });
        }

        public static HttpResponseMessage Success() => new(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"id":"response_test","status":"completed","output":[{"content":[{"type":"output_text","text":"## Overall interpretation\nSynthetic evidence contains no experimental observations."}]}],"usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}
                """, Encoding.UTF8, "application/json"),
        };

        protected override void Dispose(bool disposing)
        {
            if (disposed) return;
            disposed = true;
            if (disposing)
            {
                Client.Dispose();
                using var db = Usage.OpenForCommand();
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(db);
            }
            base.Dispose(disposing);
            if (disposing) Directory.Delete(directory, true);
        }
    }

    sealed class CallbackHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
}
