using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class AccountingConcurrencyEndpointTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-accounting-hosts-" + Guid.NewGuid().ToString("N"));
    const string FirstId = "11111111111111111111111111111111";
    const string SecondId = "22222222222222222222222222222222";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoHostsAdmitOnlyOneQuotaLimitedExecution(bool duplicate)
    {
        var control = new ProviderControl();
        using var firstHost = new AccountingHost(directory, control);
        using var firstClient = firstHost.CreateClient();
        var code = firstHost.Services.GetRequiredService<OperatorCodeRegistry>()
            .Create("Concurrency", 1, false, InterpretationAccessTiers.Standard).Code;
        using var secondHost = new AccountingHost(directory, control);
        using var secondClient = secondHost.CreateClient();

        var first = Send(firstClient, FirstId, code);
        var second = Send(secondClient, duplicate ? FirstId : SecondId, code);
        try
        {
            await control.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var rejected = await (await Task.WhenAny(first, second).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(duplicate ? HttpStatusCode.Conflict : HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.Equal(1, control.Calls);
        }
        finally { control.Release.TrySetResult(); }

        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        foreach (var response in results) response.Dispose();
        Assert.Single(results, result => result.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, control.Calls);

        if (!duplicate)
        {
            // Rejection before admission must leave the unused key available.
            var rejectedId = results[0].StatusCode == HttpStatusCode.OK ? SecondId : FirstId;
            using var retried = await Send(firstClient, rejectedId, code);
            Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
            Assert.Equal(2, control.Calls);
        }
    }

    [Theory]
    [InlineData(AnalysisInterpretationFailureKind.Timeout)]
    [InlineData(AnalysisInterpretationFailureKind.Cancelled)]
    public async Task UncertainTerminalOutcomesConsumeKeyAcrossRestart(AnalysisInterpretationFailureKind failure)
    {
        var control = new ProviderControl { Failure = failure };
        control.Release.TrySetResult();
        string code;
        using (var host = new AccountingHost(directory, control))
        using (var client = host.CreateClient())
        {
            code = host.Services.GetRequiredService<OperatorCodeRegistry>()
                .Create("Uncertain", 1, false, InterpretationAccessTiers.Standard).Code;
            using var first = await Send(client, FirstId, code);
            Assert.Equal(failure == AnalysisInterpretationFailureKind.Timeout ? 504 : 499, (int)first.StatusCode);
        }
        using var restarted = new AccountingHost(directory, control);
        using var restartedClient = restarted.CreateClient();
        using var duplicate = await Send(restartedClient, FirstId, code);
        await AssertCode(duplicate, HttpStatusCode.Conflict, "interpretation_duplicate_request");
        using var different = await Send(restartedClient, SecondId, code);
        await AssertCode(different, HttpStatusCode.ServiceUnavailable, "interpretation_accounting_unresolved");
        Assert.Equal(1, control.Calls);
    }

    [Fact]
    public async Task AnonymousCorrelationCollisionsPreserveBothCharges()
    {
        var control = new ProviderControl();
        control.Release.TrySetResult();
        using var host = new AccountingHost(directory, control);
        using var client = host.CreateClient();
        using var first = await Send(client, FirstId);
        using var second = await Send(client, FirstId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, control.Calls);
        using var db = host.Services.GetRequiredService<InterpretationUsageStore>().OpenForCommand();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT count(DISTINCT request_id),sum(known_cost) FROM execution_usage WHERE client_request_id=$id";
        query.Parameters.AddWithValue("$id", FirstId);
        using var reader = query.ExecuteReader(); Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt64(0)); Assert.Equal(.50m, reader.GetDecimal(1));
    }

    static async Task<HttpResponseMessage> Send(HttpClient client, string id, string? code = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/interpretation/generate")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                requestSchemaVersion = FtItcInterpretationClient.RequestSchemaVersion,
                taskType = "interpretation",
                outputInstructions = "Use Markdown.", outputFormatVersion = "itc-interpretation-markdown-3.0",
                generationProfile = code is null ? "instant" : "standard", clientRequestId = id,
                package = new { packageSchemaVersion = "2.0", results = Array.Empty<object>() },
            }), Encoding.UTF8, "application/json"),
        };
        if (code is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", code);
        return await client.SendAsync(request);
    }

    static async Task AssertCode(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    sealed class ProviderControl
    {
        public int Calls;
        public AnalysisInterpretationFailureKind? Failure;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    sealed class AccountingHost(string directory, ProviderControl control) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["Interpretation:Enabled"] = "true",
                ["Interpretation:UsageLog:Enabled"] = "true",
                ["Interpretation:UsageLog:DatabasePath"] = Path.Combine(directory, "usage.db"),
                ["Interpretation:OperatorAccess:Enabled"] = "true",
                ["Interpretation:OperatorAccess:RegistryPath"] = Path.Combine(directory, "accounts.json"),
                ["Interpretation:OperatorAccess:PresetRegistryPath"] = Path.Combine(directory, "presets.json"),
                ["Interpretation:AvailabilityPolicyPath"] = Path.Combine(directory, "availability.json"),
                ["Interpretation:RateLimit:PermitLimit"] = "100",
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAnalysisInterpretationProvider>();
                services.AddSingleton<IAnalysisInterpretationProvider>(provider =>
                    new AccountingProvider(provider.GetRequiredService<InterpretationUsageStore>(), control));
            });
        }
    }

    sealed class AccountingProvider(InterpretationUsageStore store, ProviderControl control) : IAnalysisInterpretationProvider
    {
        public async Task<AnalysisInterpretationProviderResponse> GenerateAsync(
            AnalysisInterpretationGenerationRequest request, CancellationToken cancellationToken)
        {
            store.BeginAttempt(request.ServerExecutionId, 1);
            Interlocked.Increment(ref control.Calls);
            control.Started.TrySetResult();
            await control.Release.Task.WaitAsync(cancellationToken);
            store.RecordAttempt(new InterpretationUsageAttempt
            {
                ServerExecutionId = request.ServerExecutionId, RequestId = request.ServerExecutionId,
                AttemptNumber = 1, TimestampUtc = DateTime.UtcNow, Model = "fake-provider",
                CombinedCost = control.Failure is null ? .25m : null,
                Outcome = control.Failure is null ? "success" : "timeout", HttpStatus = control.Failure is null ? 200 : null,
            });
            if (control.Failure is { } failure)
                throw new AnalysisInterpretationProviderException(failure, "Synthetic uncertain provider outcome.");
            return new AnalysisInterpretationProviderResponse
            {
                RequestId = "provider-correlation", Provider = "fake-provider", Model = "fake-model",
                GeneratedAtUtc = DateTime.UtcNow, InterpretationMarkdown = "Synthetic interpretation.",
            };
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
