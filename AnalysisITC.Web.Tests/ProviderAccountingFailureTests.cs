using System.Net;
using System.Text;
using System.Text.Json;
using AnalysisITC.Core.Interpretation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class ProviderAccountingFailureTests
{
    [Fact]
    public async Task ValidDraftSurvivesReceiptInsertFailureAfterDispatch()
    {
        using var test = TestContext.Create(includePricing: true);
        var request = Request("receipt-fault-execution");
        var store = test.Store;
        Admit(store, request, operatorCodeId: null, quotaLimitUsd: null);
        AddTrigger(test.DatabasePath,
            "CREATE TRIGGER fail_receipt AFTER INSERT ON attempt_receipts BEGIN SELECT RAISE(ABORT, 'injected receipt failure'); END;");

        using var client = test.HttpClient(ValidResponse(withUsage: true));
        var response = await test.Provider(client).GenerateAsync(request, CancellationToken.None);

        Assert.Equal("## Overall interpretation\n\nA valid draft.", response.InterpretationMarkdown);
        Assert.Equal(1, test.HttpCalls);
        Assert.Equal(1, store.ReadExecutionAccounting(request.ServerExecutionId).AttemptCount);
        Assert.Equal(1, store.ReadExecutionAccounting(request.ServerExecutionId).UnresolvedCount);
    }

    [Fact]
    public async Task MissingPricingOrUsageRemainsUnknownAndBlocksNextQuotaExecution()
    {
        using var test = TestContext.Create(includePricing: false);
        var request = Request("unknown-cost-execution");
        var store = test.Store;
        Admit(store, request, "account-1", 1m);

        using var client = test.HttpClient(ValidResponse(withUsage: false));
        var response = await test.Provider(client).GenerateAsync(request, CancellationToken.None);
        Assert.Equal("## Overall interpretation\n\nA valid draft.", response.InterpretationMarkdown);

        Finalize(store, request, "account-1");
        var next = new InterpretationUsageRequest
        {
            RequestId = "next-client-request", ClientRequestId = "next-client-request",
            ServerExecutionId = "next-execution", OperatorCodeId = "account-1",
            TaskType = "interpretation", EffectivePreset = "standard", StartedUtc = DateTime.UtcNow,
        };
        Assert.Equal(InterpretationAdmissionStatus.Unresolved,
            store.TryAdmit(next, 1m, DateTime.MinValue).Status);
    }

    [Fact]
    public async Task AttemptStartFailureAfterAdmissionMakesNoProviderCall()
    {
        using var test = TestContext.Create(includePricing: true);
        var request = Request("attempt-start-fault");
        Admit(test.Store, request, operatorCodeId: null, quotaLimitUsd: null);
        AddTrigger(test.DatabasePath,
            "CREATE TRIGGER fail_attempt_start AFTER INSERT ON attempt_starts BEGIN SELECT RAISE(ABORT, 'injected attempt-start failure'); END;");

        using var client = test.HttpClient(ValidResponse(withUsage: true));
        var error = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            test.Provider(client).GenerateAsync(request, CancellationToken.None));

        Assert.Equal(AnalysisInterpretationFailureKind.AccountingUnavailable, error.Kind);
        Assert.Equal(0, test.HttpCalls);
    }

    [Fact]
    public async Task UnresolvedRetryableAttemptReturnsAccountingFailureWithoutFallbackCall()
    {
        using var test = TestContext.Create(includePricing: false);
        var request = Request("unresolved-retry-execution");
        Admit(test.Store, request, operatorCodeId: null, quotaLimitUsd: null);

        using var client = test.Responses(
            (HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"context_length_exceeded\"}}"),
            (HttpStatusCode.OK, ValidResponse(withUsage: true)));
        var error = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            test.Provider(client).GenerateAsync(request, CancellationToken.None));

        Assert.Equal(AnalysisInterpretationFailureKind.AccountingUnresolved, error.Kind);
        Assert.Contains("usage accounting", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, test.HttpCalls);
    }

    [Fact]
    public void AccountingReadFailureDoesNotBecomeFullAllowance()
    {
        using var test = TestContext.Create(includePricing: true);
        using (test.Store.OpenForCommand()) { }
        using (var connection = new SqliteConnection($"Data Source={test.DatabasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP VIEW execution_usage";
            command.ExecuteNonQuery();
        }

        Assert.Throws<AccountingUnavailableException>(() =>
            test.Store.GetOperatorUsage("account-1", DateTime.MinValue));
    }

    static void Admit(InterpretationUsageStore store, AnalysisInterpretationGenerationRequest request,
        string? operatorCodeId, decimal? quotaLimitUsd)
    {
        var admission = store.TryAdmit(new InterpretationUsageRequest
        {
            RequestId = request.ServerExecutionId, ClientRequestId = request.ClientRequestId,
            ServerExecutionId = request.ServerExecutionId, OperatorCodeId = operatorCodeId,
            TaskType = request.TaskType, EffectivePreset = "standard", StartedUtc = DateTime.UtcNow,
        }, quotaLimitUsd, DateTime.MinValue);
        Assert.Equal(InterpretationAdmissionStatus.Admitted, admission.Status);
    }

    static void Finalize(InterpretationUsageStore store, AnalysisInterpretationGenerationRequest request, string account)
    {
        store.FinalizeRequest(new InterpretationUsageRequest
        {
            RequestId = request.ServerExecutionId, ClientRequestId = request.ClientRequestId,
            ServerExecutionId = request.ServerExecutionId, OperatorCodeId = account,
            TaskType = request.TaskType, EffectivePreset = "standard", StartedUtc = DateTime.UtcNow.AddSeconds(-1),
            CompletedUtc = DateTime.UtcNow, Outcome = "success", HttpStatus = 200,
        });
    }

    static void AddTrigger(string databasePath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static AnalysisInterpretationGenerationRequest Request(string executionId)
    {
        var package = new AnalysisInterpretationPackage();
        using var evidenceDocument = JsonDocument.Parse(AnalysisInterpretationPromptBuilder.Build(package).CanonicalPackageJson);
        var evidence = evidenceDocument.RootElement.Clone();
        return new AnalysisInterpretationGenerationRequest
        {
            ClientRequestId = "client-" + executionId,
            ServerExecutionId = executionId,
            GenerationProfile = "standard",
            RequestedModel = "test-model",
            RequestedReasoningEffort = "medium",
            Package = package,
            PackageJson = evidence,
            Prompt = ScientificGuidance.BuildPrompt(
                AnalysisInterpretationPromptBuilder.OutputFormatVersion,
                AnalysisInterpretationPromptBuilder.BuildResponseFormatInstructions(package),
                evidence.GetRawText()),
        };
    }

    static string ValidResponse(bool withUsage) => withUsage
        ? "{\"id\":\"resp_test\",\"created_at\":1788512400,\"status\":\"completed\",\"model\":\"test-model\",\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":\"## Overall interpretation\\n\\nA valid draft.\"}]}],\"usage\":{\"input_tokens\":100,\"output_tokens\":50,\"total_tokens\":150}}"
        : "{\"id\":\"resp_test\",\"created_at\":1788512400,\"status\":\"completed\",\"model\":\"test-model\",\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":\"## Overall interpretation\\n\\nA valid draft.\"}]}]}";

    sealed class TestContext : IDisposable
    {
        readonly string directory;
        public string DatabasePath { get; }
        public InterpretationOptions Settings { get; }
        public InterpretationUsageStore Store { get; }
        public int HttpCalls;

        TestContext(string directory, InterpretationOptions settings)
        {
            this.directory = directory;
            Settings = settings;
            DatabasePath = settings.UsageLog.DatabasePath;
            Store = new InterpretationUsageStore(Options.Create(settings), NullLogger<InterpretationUsageStore>.Instance);
        }

        public static TestContext Create(bool includePricing)
        {
            var directory = Path.Combine(Path.GetTempPath(), "ftitc-provider-accounting-fault-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var settings = new InterpretationOptions
            {
                OpenAI = new OpenAIInterpretationOptions
                {
                    Endpoint = "https://api.openai.test/v1/responses", ApiKey = "test-secret",
                    Model = "test-model", ReasoningEffort = "medium", TimeoutSeconds = 30,
                },
                UsageLog = new InterpretationUsageOptions { Enabled = true, DatabasePath = Path.Combine(directory, "usage.db") },
                Pricing = includePricing
                    ? new Dictionary<string, InterpretationPricingOptions>(StringComparer.Ordinal)
                    {
                        ["test-model"] = new() { Revision = "test", InputPerMillion = 2, OutputPerMillion = 12 },
                    }
                    : new Dictionary<string, InterpretationPricingOptions>(StringComparer.Ordinal),
            };
            return new TestContext(directory, settings);
        }

        public HttpClient HttpClient(string response)
        {
            var handler = new StubHandler((_, _) =>
            {
                HttpCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(response, Encoding.UTF8, "application/json"),
                });
            });
            return new HttpClient(handler);
        }

        public HttpClient Responses(params (HttpStatusCode Status, string Body)[] responses)
        {
            var index = 0;
            var handler = new StubHandler((_, _) =>
            {
                var response = responses[Math.Min(index++, responses.Length - 1)];
                HttpCalls++;
                return Task.FromResult(new HttpResponseMessage(response.Status)
                {
                    Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
                });
            });
            return new HttpClient(handler);
        }

        public OpenAIInterpretationProvider Provider(HttpClient client) =>
            new(client, Options.Create(Settings), Store);

        public void Dispose()
        {
            Store.OpenForCommand().Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
