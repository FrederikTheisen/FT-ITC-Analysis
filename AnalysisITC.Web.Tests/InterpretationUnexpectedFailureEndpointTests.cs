using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AnalysisITC.Core.Interpretation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class InterpretationUnexpectedFailureEndpointTests
{
    [Theory]
    [InlineData(true, "before-attempt")]
    [InlineData(true, "resolved")]
    [InlineData(true, "unresolved")]
    [InlineData(false, "before-attempt")]
    [InlineData(false, "resolved")]
    [InlineData(false, "unresolved")]
    public async Task UnexpectedFailureFinalizesExecutionAndPreservesAccounting(bool publicAccess, string failureStage)
    {
        using var factory = new UnexpectedFailureApplicationFactory(failureStage);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var code = publicAccess
            ? factory.Services.GetRequiredService<PublicAccessRegistry>().Create().Code
            : factory.Services.GetRequiredService<OperatorCodeRegistry>()
                .Create("Unexpected failure test", 10, false, InterpretationAccessTiers.Standard).Code;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", code);
        var profile = publicAccess ? "instant" : "standard";
        var failedClientRequestId = Guid.NewGuid().ToString("N");

        using var failed = await Send(client, failedClientRequestId, profile);
        await AssertProblem(failed, HttpStatusCode.InternalServerError, "unexpected_error");
        var provider = factory.Services.GetRequiredService<UnexpectedFailureProvider>();
        var store = factory.Services.GetRequiredService<InterpretationUsageStore>();
        var executionId = Assert.IsType<string>(provider.FailedExecutionId);
        using (var db = store.OpenForCommand())
        {
            Assert.Equal("finalized", Scalar<string>(db, "SELECT lifecycle FROM executions WHERE execution_id=$id", executionId));
            Assert.Equal(1L, Scalar<long>(db, "SELECT count(*) FROM executions WHERE execution_id=$id AND completed_utc IS NOT NULL", executionId));
            Assert.Equal(1L, Scalar<long>(db, "SELECT count(*) FROM requests WHERE execution_id=$id", executionId));
            Assert.Equal("server_error", Scalar<string>(db, "SELECT outcome FROM requests WHERE execution_id=$id", executionId));
            Assert.Equal(500L, Scalar<long>(db, "SELECT http_status FROM requests WHERE execution_id=$id", executionId));
            Assert.Equal("unexpected_error", Scalar<string>(db, "SELECT error_code FROM requests WHERE execution_id=$id", executionId));
            Assert.Equal(!publicAccess && failureStage == "unresolved" ? 1L : 0L,
                Scalar<long>(db, "SELECT count(*) FROM account_holds WHERE execution_id=$id", executionId));
        }
        var accounting = store.ReadExecutionAccounting(executionId);
        Assert.Equal(failureStage == "resolved" ? 0.02m : 0m, accounting.KnownCost);
        Assert.Equal(failureStage == "unresolved" ? 1 : 0, accounting.UnresolvedCount);
        Assert.Equal(failureStage == "before-attempt" ? 0 : 1, accounting.AttemptCount);

        using var duplicate = await Send(client, failedClientRequestId, profile);
        await AssertProblem(duplicate, HttpStatusCode.Conflict, "interpretation_duplicate_request");
        Assert.Equal(1, provider.CallCount);

        using var fresh = await Send(client, Guid.NewGuid().ToString("N"), profile);
        if (failureStage == "unresolved")
        {
            await AssertProblem(fresh, publicAccess ? HttpStatusCode.TooManyRequests : HttpStatusCode.ServiceUnavailable,
                publicAccess ? "interpretation_public_accounting_unresolved" : "interpretation_accounting_unresolved");
            Assert.Equal(1, provider.CallCount);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
            Assert.Equal(2, provider.CallCount);
        }
        Assert.Equal(accounting, store.ReadExecutionAccounting(executionId));
        using var verify = store.OpenForCommand();
        Assert.Equal(1L, Scalar<long>(verify, "SELECT count(*) FROM requests WHERE execution_id=$id", executionId));
    }

    static Task<HttpResponseMessage> Send(HttpClient client, string clientRequestId, string profile) =>
        client.PostAsync("/api/interpretation/generate", new StringContent(JsonSerializer.Serialize(new
        {
            requestSchemaVersion = FtItcInterpretationClient.RequestSchemaVersion,
            omitScientificGuidance = false,
            taskType = "interpretation",
            outputInstructions = "Use Markdown.",
            outputFormatVersion = AnalysisInterpretationPromptBuilder.OutputFormatVersion,
            generationProfile = profile,
            clientRequestId,
            package = new { packageSchemaVersion = "2.0", results = Array.Empty<object>() },
        }), Encoding.UTF8, "application/json"));

    static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.DoesNotContain("Injected unexpected failure", body.GetRawText(), StringComparison.Ordinal);
    }

    static T Scalar<T>(SqliteConnection db, string sql, string executionId)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", executionId);
        return (T)command.ExecuteScalar()!;
    }

    sealed class UnexpectedFailureApplicationFactory(string failureStage) : WebApplicationFactory<Program>
    {
        readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-unexpected-failure-" + Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            WebTestHostIsolation.ConfigureIsolatedHost(builder, directory);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Interpretation:Enabled"] = "true",
                ["Interpretation:UsageLog:Enabled"] = "true",
                ["Interpretation:OperatorAccess:Enabled"] = "true",
                ["Interpretation:Registration:Enabled"] = "false",
                ["Interpretation:OpenAI:ApiKey"] = "",
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAnalysisInterpretationProvider>();
                services.AddSingleton(provider => new UnexpectedFailureProvider(provider.GetRequiredService<InterpretationUsageStore>(), failureStage));
                services.AddSingleton<IAnalysisInterpretationProvider>(provider => provider.GetRequiredService<UnexpectedFailureProvider>());
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    sealed class UnexpectedFailureProvider(InterpretationUsageStore store, string failureStage) : IAnalysisInterpretationProvider
    {
        public int CallCount { get; private set; }
        public string? FailedExecutionId { get; private set; }

        public Task<AnalysisInterpretationProviderResponse> GenerateAsync(AnalysisInterpretationGenerationRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
            {
                FailedExecutionId = request.ServerExecutionId;
                if (failureStage != "before-attempt")
                {
                    store.BeginAttempt(request.ServerExecutionId, 1);
                    if (failureStage == "resolved")
                        store.RecordAttempt(new InterpretationUsageAttempt
                        {
                            ServerExecutionId = request.ServerExecutionId, AttemptNumber = 1,
                            TimestampUtc = DateTime.UtcNow, BillingState = "known", CombinedCost = 0.02m,
                            Outcome = "success", HttpStatus = 200,
                        });
                }
                throw new InvalidOperationException("Injected unexpected failure");
            }
            return Task.FromResult(new AnalysisInterpretationProviderResponse
            {
                RequestId = request.ClientRequestId, Provider = "test-provider", Model = "test-model",
                GeneratedAtUtc = DateTime.UtcNow, InterpretationMarkdown = "## Overall interpretation\n\nTest response.",
            });
        }
    }
}
