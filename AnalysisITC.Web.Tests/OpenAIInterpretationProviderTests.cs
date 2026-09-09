using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnalysisITC.Core.Interpretation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class OpenAIInterpretationProviderTests
{
    [Fact]
    public async Task DistinguishesQuotaFromRateLimitWithoutLoggingResponseBody()
    {
        var secret = "private-provider-" + Guid.NewGuid().ToString("N");
        using var client = new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":\"insufficient_quota\",\"message\":\"" + secret + "\"}}"))));
        var error = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() => Provider(client).GenerateAsync(Request(), CancellationToken.None));
        Assert.Equal(AnalysisInterpretationFailureKind.QuotaExceeded, error.Kind);
        var log = AnalysisITC.Core.Application.AppEventHandler.GetLogReport();
        Assert.Contains("code=insufficient_quota", log);
        Assert.DoesNotContain(secret, log);
        Assert.DoesNotContain(secret, error.ToString());
    }

    [Fact]
    public async Task SendsTrustedPromptWithoutStorageAndReturnsMarkdown()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, """
                {
                  "id": "resp_test_123",
                  "created_at": 1788512400,
                  "status": "completed",
                  "model": "test-model-2026-09-04",
                  "output": [
                    {
                      "type": "message",
                      "content": [
                        {
                          "type": "output_text",
                          "text": "## Overall interpretation\n\nThe fitted interaction is internally consistent."
                        }
                      ]
                    }
                  ]
                }
                """);
        });
        using var httpClient = new HttpClient(handler);
        var provider = Provider(httpClient, "vs_test_knowledge_base");
        var generationRequest = Request();

        var response = await provider.GenerateAsync(generationRequest, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://api.openai.test/v1/responses", capturedRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-secret", capturedRequest.Headers.Authorization.Parameter);
        Assert.Equal("application/json", capturedRequest.Content!.Headers.ContentType!.MediaType);

        using var outbound = JsonDocument.Parse(capturedBody!);
        var root = outbound.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.Equal(generationRequest.Prompt.SystemInstructions, root.GetProperty("instructions").GetString());
        Assert.Equal(generationRequest.Prompt.UserMessage, root.GetProperty("input").GetString());
        Assert.Equal(4321, root.GetProperty("max_output_tokens").GetInt32());
        Assert.False(root.GetProperty("store").GetBoolean());
        var tool = Assert.Single(root.GetProperty("tools").EnumerateArray());
        Assert.Equal("file_search", tool.GetProperty("type").GetString());
        Assert.Equal("vs_test_knowledge_base", Assert.Single(tool.GetProperty("vector_store_ids").EnumerateArray()).GetString());
        Assert.Equal(8, tool.GetProperty("max_num_results").GetInt32());
        Assert.Equal("required", root.GetProperty("tool_choice").GetString());
        Assert.DoesNotContain("test-secret", capturedBody, StringComparison.Ordinal);

        Assert.Equal("resp_test_123", response.RequestId);
        Assert.Equal("openai", response.Provider);
        Assert.Equal("test-model-2026-09-04", response.Model);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788512400).UtcDateTime, response.GeneratedAtUtc);
        Assert.Equal(
            "## Overall interpretation\n\nThe fitted interaction is internally consistent.",
            response.InterpretationMarkdown);
    }

    [Fact]
    public async Task OmitsFileSearchWhenNoVectorStoreIsConfigured()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, """
                {"id":"resp_1","created_at":1788512400,"status":"completed","model":"test-model","output":[{"content":[{"type":"output_text","text":"## Overall interpretation\n\nNo searchable knowledge base was configured."}]}]}
                """);
        });
        using var httpClient = new HttpClient(handler);

        await Provider(httpClient).GenerateAsync(Request(), CancellationToken.None);

        using var outbound = JsonDocument.Parse(capturedBody!);
        Assert.False(outbound.RootElement.TryGetProperty("tools", out _));
        Assert.False(outbound.RootElement.TryGetProperty("tool_choice", out _));
    }

    [Fact]
    public async Task MapsRateLimitWithoutExposingProviderBody()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = JsonResponse(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"secret detail\"}}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            Provider(httpClient).GenerateAsync(Request(), CancellationToken.None));

        Assert.Equal(AnalysisInterpretationFailureKind.RateLimited, exception.Kind);
        Assert.Equal(TimeSpan.FromSeconds(17), exception.RetryAfter);
        Assert.DoesNotContain("secret detail", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ deliberately invalid JSON")]
    [InlineData("{\"id\":\"resp_1\",\"created_at\":1788512400,\"status\":\"incomplete\",\"model\":\"test-model\",\"output\":[]}")]
    [InlineData("{\"id\":\"resp_1\",\"created_at\":1788512400,\"status\":\"completed\",\"model\":\"test-model\",\"output\":[{\"content\":[{\"type\":\"refusal\",\"refusal\":\"No\"}]}]}")]
    [InlineData("{\"id\":\"resp_1\",\"created_at\":1788512400,\"status\":\"completed\",\"model\":\"test-model\",\"output\":[]}")]
    public async Task RejectsMalformedIncompleteRefusedAndEmptyResponses(string responseJson)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, responseJson)));
        using var httpClient = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            Provider(httpClient).GenerateAsync(Request(), CancellationToken.None));

        Assert.Equal(AnalysisInterpretationFailureKind.InvalidResponse, exception.Kind);
    }

    [Fact]
    public async Task CompletedUsageWithoutOptionalDetailsPreservesDraftAndAggregateTokens()
    {
        var settings = ProviderSettings();
        var databasePath = Path.Combine(Path.GetTempPath(), "ftitc-provider-usage-" + Guid.NewGuid().ToString("N") + ".db");
        settings.UsageLog = new InterpretationUsageOptions { Enabled = true, DatabasePath = databasePath };
        settings.Pricing["test-model"] = new InterpretationPricingOptions { Revision = "test", InputPerMillion = 2, OutputPerMillion = 12 };
        var store = new InterpretationUsageStore(Options.Create(settings), NullLogger<InterpretationUsageStore>.Instance);
        using var client = new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK,
            """
            {"id":"resp_1","status":"completed","model":"test-model","output":[{"content":[{"type":"output_text","text":"A useful draft."}]}],"usage":{"input_tokens":1000,"output_tokens":6000,"total_tokens":7000}}
            """))));

        var response = await new OpenAIInterpretationProvider(client, Options.Create(settings), store).GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal("A useful draft.", response.InterpretationMarkdown);
        var aggregate = store.Aggregate(Request().ClientRequestId);
        Assert.Equal(1, aggregate.Attempts); Assert.Equal(1000, aggregate.Input); Assert.Equal(6000, aggregate.Output);
        Assert.Null(aggregate.Reasoning);
    }

    [Fact]
    public async Task MalformedOptionalUsageDetailsDoNotDiscardValidAggregateCountsOrDraft()
    {
        var settings = ProviderSettings();
        settings.UsageLog = new InterpretationUsageOptions { Enabled = true, DatabasePath = Path.Combine(Path.GetTempPath(), "ftitc-provider-usage-" + Guid.NewGuid().ToString("N") + ".db") };
        var store = new InterpretationUsageStore(Options.Create(settings), NullLogger<InterpretationUsageStore>.Instance);
        using var client = new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK,
            """
            {"status":"completed","output":[{"content":[{"type":"output_text","text":"A useful draft."}]}],"usage":{"input_tokens":1000,"output_tokens":6000,"total_tokens":7000,"input_tokens_details":{"cached_tokens":"unknown","cache_write_tokens":null},"output_tokens_details":{"reasoning_tokens":-5}}}
            """))));

        var response = await new OpenAIInterpretationProvider(client, Options.Create(settings), store).GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal("A useful draft.", response.InterpretationMarkdown);
        Assert.Equal(1000, response.InputTokens); Assert.Equal(6000, response.OutputTokens); Assert.Equal(7000, response.TotalTokens);
        Assert.Null(response.CachedInputTokens); Assert.Null(response.CacheWriteTokens); Assert.Null(response.ReasoningTokens);
        var aggregate = store.Aggregate(Request().ClientRequestId);
        Assert.Equal(1, aggregate.Attempts); Assert.Equal(1000, aggregate.Input); Assert.Equal(6000, aggregate.Output); Assert.Null(aggregate.Reasoning);
    }

    [Fact]
    public async Task IncompleteResponseRecordsUsageAndMaxOutputReasonOnce()
    {
        var settings = ProviderSettings();
        var databasePath = Path.Combine(Path.GetTempPath(), "ftitc-provider-usage-" + Guid.NewGuid().ToString("N") + ".db");
        settings.UsageLog = new InterpretationUsageOptions { Enabled = true, DatabasePath = databasePath };
        settings.Pricing["test-model"] = new InterpretationPricingOptions { Revision = "test", InputPerMillion = 2, OutputPerMillion = 12 };
        var store = new InterpretationUsageStore(Options.Create(settings), NullLogger<InterpretationUsageStore>.Instance);
        using var client = new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK,
            """
            {"id":"resp_1","status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"model":"test-model","output":[],"usage":{"input_tokens":1000,"output_tokens":6000,"total_tokens":7000,"output_tokens_details":{"reasoning_tokens":5000}}}
            """))));

        var error = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            new OpenAIInterpretationProvider(client, Options.Create(settings), store).GenerateAsync(Request(), CancellationToken.None));

        Assert.Equal(AnalysisInterpretationFailureKind.InvalidResponse, error.Kind);
        var aggregate = store.Aggregate(Request().ClientRequestId);
        Assert.Equal(1, aggregate.Attempts); Assert.Equal(1000, aggregate.Input); Assert.Equal(6000, aggregate.Output);
        Assert.Equal(5000, aggregate.Reasoning); Assert.Equal(1000, aggregate.Visible); Assert.Equal(7000, aggregate.Total);
        Assert.Equal(0.074m, aggregate.Cost);
        using var connection = store.OpenForCommand(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT outcome,error_code FROM attempts WHERE request_id=$id";
        command.Parameters.AddWithValue("$id", Request().ClientRequestId); using var reader = command.ExecuteReader();
        Assert.True(reader.Read()); Assert.Equal("provider_error", reader.GetString(0)); Assert.Equal("max_output_tokens", reader.GetString(1));
    }

    [Fact]
    public async Task MalformedOutputElementRecordsAvailableUsageBeforeRejectingResponse()
    {
        var settings = ProviderSettings();
        settings.UsageLog = new InterpretationUsageOptions { Enabled = true, DatabasePath = Path.Combine(Path.GetTempPath(), "ftitc-provider-usage-" + Guid.NewGuid().ToString("N") + ".db") };
        var store = new InterpretationUsageStore(Options.Create(settings), NullLogger<InterpretationUsageStore>.Instance);
        using var client = new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK,
            """
            {"id":"resp_1","status":"completed","output":[null],"usage":{"input_tokens":1000,"output_tokens":6000,"total_tokens":7000}}
            """))));

        var error = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            new OpenAIInterpretationProvider(client, Options.Create(settings), store).GenerateAsync(Request(), CancellationToken.None));

        Assert.Equal(AnalysisInterpretationFailureKind.InvalidResponse, error.Kind);
        var aggregate = store.Aggregate(Request().ClientRequestId);
        Assert.Equal(1, aggregate.Attempts); Assert.Equal(1000, aggregate.Input); Assert.Equal(6000, aggregate.Output); Assert.Equal(7000, aggregate.Total);
    }

    [Fact]
    public async Task RetryRecordsFailedAttemptUsageOnceAlongsideSuccessfulRetry()
    {
        var settings = ProviderSettings("vs_test");
        var databasePath = Path.Combine(Path.GetTempPath(), "ftitc-provider-usage-" + Guid.NewGuid().ToString("N") + ".db");
        settings.UsageLog = new InterpretationUsageOptions { Enabled = true, DatabasePath = databasePath };
        var store = new InterpretationUsageStore(Options.Create(settings), NullLogger<InterpretationUsageStore>.Instance);
        var calls = 0;
        using var client = new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(++calls == 1
            ? JsonResponse(HttpStatusCode.OK, """
              {"id":"resp_1","status":"in_progress","output":[{"type":"file_search_call","status":"failed"}],"usage":{"input_tokens":100,"output_tokens":200,"total_tokens":300}}
              """)
            : JsonResponse(HttpStatusCode.OK, """
              {"id":"resp_2","status":"completed","output":[{"content":[{"type":"output_text","text":"A useful draft."}]}],"usage":{"input_tokens":300,"output_tokens":400,"total_tokens":700}}
              """))));

        await new OpenAIInterpretationProvider(client, Options.Create(settings), store).GenerateAsync(Request(), CancellationToken.None);

        var aggregate = store.Aggregate(Request().ClientRequestId);
        Assert.Equal(2, aggregate.Attempts); Assert.Equal(400, aggregate.Input); Assert.Equal(600, aggregate.Output); Assert.Equal(1000, aggregate.Total);
    }

    [Fact]
    public async Task MapsTransportTimeout()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new TaskCanceledException("simulated timeout"));
        using var httpClient = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            Provider(httpClient).GenerateAsync(Request(), CancellationToken.None));

        Assert.Equal(AnalysisInterpretationFailureKind.Timeout, exception.Kind);
    }

    [Fact]
    public async Task PreservesCallerCancellationAsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new StubHttpMessageHandler((_, token) =>
            throw new OperationCanceledException(token));
        using var httpClient = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            Provider(httpClient).GenerateAsync(Request(), cancellation.Token));

        Assert.Equal(AnalysisInterpretationFailureKind.Cancelled, exception.Kind);
    }

    [Fact]
    public async Task RejectsOversizedProviderResponse()
    {
        var oversized = new string('x', 1024 * 1024 + 1);
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(oversized, Encoding.UTF8, "application/json"),
            }));
        using var httpClient = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            Provider(httpClient).GenerateAsync(Request(), CancellationToken.None));

        Assert.Equal(AnalysisInterpretationFailureKind.InvalidResponse, exception.Kind);
    }

    [Theory]
    [InlineData("retrieval_failed", true)]
    [InlineData("context_length_exceeded", false)]
    public async Task RetriesOnlySpecificFailuresAndRecordsEffectiveOmissions(string code, bool retrievalFailure)
    {
        var bodies = new List<string>();
        var handler = new StubHttpMessageHandler(async (message, _) =>
        {
            bodies.Add(await message.Content!.ReadAsStringAsync());
            return bodies.Count == 1 ? JsonResponse(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"" + code + "\"}}")
                : JsonResponse(HttpStatusCode.OK, """
                  {"status":"completed","output":[{"type":"file_search_call","results":[{"file_id":"file_test","text":"Retrieved support"}]},{"type":"message","phase":"commentary","content":[{"type":"output_text","text":"Searching..."}]},{"type":"message","phase":"final_answer","content":[{"type":"output_text","text":"## Overall interpretation\n"},{"type":"output_text","text":"Evidence supports a qualified assessment."}]}]}
                  """);
        });
        using var client = new HttpClient(handler);
        var request = Request();
        request.Package.Results.Add(new InterpretationResultEvidence { Experiments = new List<InterpretationExperimentEvidence>
        { new() { Thermogram = AnalysisInterpretationThermograms.Compress(new[] { (0d, 1d, (double?)0d), (20d, 2d, (double?)0d) }) } } });
        using var evidenceDocument = JsonDocument.Parse(AnalysisInterpretationPromptBuilder.Build(request.Package).CanonicalPackageJson);
        var evidence = evidenceDocument.RootElement.Clone();
        request.PackageJson = evidence;
        request.Prompt = ScientificGuidance.BuildPrompt(AnalysisInterpretationPromptBuilder.OutputFormatVersion,
            AnalysisInterpretationPromptBuilder.BuildResponseFormatInstructions(request.Package), evidence.GetRawText());
        var response = await Provider(client, "vs_test").GenerateAsync(request, CancellationToken.None);
        Assert.Equal(2, bodies.Count);
        using var firstAttempt = JsonDocument.Parse(bodies[0]);
        using var retry = JsonDocument.Parse(bodies[1]);
        Assert.Equal("disabled", retry.RootElement.GetProperty("truncation").GetString());
        Assert.Equal(!retrievalFailure, retry.RootElement.TryGetProperty("tools", out _));
        if (retrievalFailure)
            Assert.NotEqual(firstAttempt.RootElement.GetProperty("instructions").GetString(), retry.RootElement.GetProperty("instructions").GetString());
        Assert.NotEmpty(response.Omissions);
        Assert.NotEqual(request.Prompt.InputFingerprint, response.EffectiveInputFingerprint);
        Assert.Equal(ScientificGuidance.Hash(retry.RootElement.GetProperty("instructions").GetString()!), response.ScientificInstructionsFingerprint);
        Assert.Equal(request.Prompt.OutputInstructionsFingerprint, response.OutputInstructionsFingerprint);
        Assert.Contains(response.Omissions, omission => omission.Contains(retrievalFailure ? "Knowledge retrieval failed" : "provider context-size rejection", StringComparison.Ordinal));
        Assert.Equal("## Overall interpretation\nEvidence supports a qualified assessment.", response.InterpretationMarkdown);
        Assert.Equal("file_test", Assert.Single(response.RetrievedSourceIds));
        Assert.NotNull(request.Package.Result.Experiments[0].Thermogram);
    }

    [Fact]
    public async Task ContextFallbackOmitsOnlyKnownThermogramPathsAndPreservesPrecision()
    {
        var bodies = new List<string>();
        using var client = new HttpClient(new StubHttpMessageHandler(async (message, _) =>
        {
            bodies.Add(await message.Content!.ReadAsStringAsync());
            return bodies.Count == 1
                ? JsonResponse(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"context_length_exceeded\"}}")
                : JsonResponse(HttpStatusCode.OK, "{\"status\":\"completed\",\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":\"## Overall interpretation\\nDone.\"}]}]}");
        }));

        var request = Request();
        var package = JsonNode.Parse(request.PackageJson!.Value.GetRawText())!.AsObject();
        package["results"] = new JsonArray(new JsonObject
        {
            ["experiments"] = new JsonArray(new JsonObject()),
        });
        var experiment = package["results"]![0]!["experiments"]![0]!.AsObject();
        experiment["thermogram"] = new JsonObject { ["samples"] = new JsonArray(1, 2, 3) };
        package["studyContext"] = new JsonObject { ["thermogram"] = "unrelated" };
        package["preciseValue"] = JsonNode.Parse("1234567890.1234567890123456789");
        using var document = JsonDocument.Parse(package.ToJsonString());
        request.PackageJson = document.RootElement.Clone();
        request.Prompt = ScientificGuidance.BuildPrompt(AnalysisInterpretationPromptBuilder.OutputFormatVersion,
            request.Prompt.ResponseFormatInstructions, request.PackageJson.Value.GetRawText());

        await Provider(client).GenerateAsync(request, CancellationToken.None);

        using var retry = JsonDocument.Parse(bodies[1]);
        var input = retry.RootElement.GetProperty("input").GetString()!;
        Assert.DoesNotContain("\"thermogram\":{\"samples\"", input, StringComparison.Ordinal);
        Assert.Contains("\"thermogram\":\"unrelated\"", input, StringComparison.Ordinal);
        Assert.Contains("1234567890.1234567890123456789", input, StringComparison.Ordinal);
        Assert.Contains("\"containsRawThermogramSamples\":false", input, StringComparison.Ordinal);
        Assert.Contains("All thermograms and sampled baselines omitted", input, StringComparison.Ordinal);
        Assert.Contains("\"thermogram\":{\"samples\":", request.PackageJson.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "retrieval_failed")]
    [InlineData(HttpStatusCode.Forbidden, "retrieval_failed")]
    [InlineData(HttpStatusCode.InternalServerError, "server_error")]
    public async Task DoesNotRetryAuthenticationOrGeneralFailures(HttpStatusCode status, string code)
    {
        var calls = 0;
        using var client = new HttpClient(new StubHttpMessageHandler((_, _) =>
        { calls++; return Task.FromResult(JsonResponse(status, "{\"error\":{\"code\":\"" + code + "\"}}")); }));
        await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() => Provider(client, "vs_test").GenerateAsync(Request(), CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ContextOverflowWithoutTracesIsActionableAndDoesNotRetry()
    {
        var calls = 0;
        using var client = new HttpClient(new StubHttpMessageHandler((_, _) =>
        { calls++; return Task.FromResult(JsonResponse(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"context_length_exceeded\"}}")); }));
        var error = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() => Provider(client).GenerateAsync(Request(), CancellationToken.None));
        Assert.Equal(AnalysisInterpretationFailureKind.PayloadRejected, error.Kind);
        Assert.Contains("Shorten background", error.Message);
        Assert.Equal(1, calls);
    }

    static OpenAIInterpretationProvider Provider(HttpClient httpClient, string vectorStoreId = "") => new(httpClient, Options.Create(ProviderSettings(vectorStoreId)));

    static InterpretationOptions ProviderSettings(string vectorStoreId = "") => new()
    {
        OpenAI = new OpenAIInterpretationOptions
        {
            Endpoint = "https://api.openai.test/v1/responses",
            Model = "test-model",
            ApiKey = "test-secret",
            VectorStoreId = vectorStoreId,
            MaxFileSearchResults = 8,
            TimeoutSeconds = 30,
            MaxOutputTokens = 4321,
        },
    };

    static AnalysisInterpretationGenerationRequest Request()
    {
        var package = new AnalysisInterpretationPackage();
        using var evidenceDocument = JsonDocument.Parse(AnalysisInterpretationPromptBuilder.Build(package).CanonicalPackageJson);
        var evidence = evidenceDocument.RootElement.Clone();
        return new AnalysisInterpretationGenerationRequest
        {
            ClientRequestId = "0123456789abcdef0123456789abcdef",
            GenerationProfile = "fast",
            Package = package,
            PackageJson = evidence,
            Prompt = ScientificGuidance.BuildPrompt(AnalysisInterpretationPromptBuilder.OutputFormatVersion,
                AnalysisInterpretationPromptBuilder.BuildResponseFormatInstructions(package), evidence.GetRawText()),
        };
    }

    static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            this.send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
