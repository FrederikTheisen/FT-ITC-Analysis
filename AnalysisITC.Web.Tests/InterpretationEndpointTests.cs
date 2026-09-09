using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnalysisITC.Core.Interpretation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class InterpretationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    static int nextClientIp;

    readonly WebApplicationFactory<Program> factory;
    readonly HttpClient client;

    public InterpretationEndpointTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
    }

    [Theory]
    [InlineData("knownObservationSigmas")]
    [InlineData("estimatedCommonVariance")]
    [InlineData("estimatedWeightedVariance")]
    public async Task CurrentLikelihoodModeFieldPassesOpaqueContract(string mode)
    {
        var request = ValidRequestNode();
        request["package"]!["results"]![0]!["validityStatus"] = "Valid";
        request["package"]!["results"]![0]!["informationCriteria"] = new JsonObject { ["likelihoodMode"] = mode };
        using var response = await PostJson(request.ToJsonString());
        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");
    }

    [Fact]
    public async Task StatusStartsWithoutProviderConfigurationAndReportsUnavailable()
    {
        var options = factory.Services.GetRequiredService<IOptions<InterpretationOptions>>();
        Assert.False(options.Value.Enabled);

        using var response = await client.GetAsync("/api/interpretation/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(document.GetProperty("available").GetBoolean());
        Assert.Equal(FtItcInterpretationClient.RequestSchemaVersion, document.GetProperty("requestSchemaVersion").GetString());
        Assert.Equal(FtItcInterpretationClient.ResponseSchemaVersion, document.GetProperty("responseSchemaVersion").GetString());
        Assert.Equal(3, document.EnumerateObject().Count());
    }

    [Fact]
    public async Task ValidRequestReturnsUnavailableWithoutAntiforgery()
    {
        using var response = await PostJson(ValidRequestJson());

        var problem = await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");
        Assert.Equal("Interpretation unavailable", problem.GetProperty("title").GetString());
        Assert.Equal("Interpretation generation is not available.", problem.GetProperty("detail").GetString());
        Assert.False(problem.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task CurrentCoreRelayClientReachesDisabledBoundary()
    {
        using var relayHttpClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        relayHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", NextClientIp());
        var relay = new FtItcInterpretationClient(relayHttpClient, new Uri("https://localhost"));
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var package = Package(ValidRequestNode()).Deserialize<AnalysisInterpretationPackage>(jsonOptions)!;
        var prompt = AnalysisInterpretationPromptBuilder.Build(package);

        var exception = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            relay.GenerateAsync(new AnalysisInterpretationGenerationRequest
            {
                ClientRequestId = "0123456789abcdef0123456789abcdef",
                GenerationProfile = "fast",
                Package = package,
                Prompt = prompt,
            }, CancellationToken.None));

        Assert.Equal(AnalysisInterpretationFailureKind.ServiceFailure, exception.Kind);
        Assert.Contains("HTTP 503", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnabledFakeProviderReceivesServerBuiltPromptAndReturnsRelayResponse()
    {
        using var providerFactory = new ProviderWebApplicationFactory(enabled: true);
        using var providerClient = providerFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

        using (var status = await providerClient.GetAsync("/api/interpretation/status"))
        {
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            var statusBody = await status.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(statusBody.GetProperty("available").GetBoolean());
        }

        using var response = await PostJsonWithClient(
            providerClient,
            ValidRequestJson(),
            "198.51.100.101");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(FtItcInterpretationClient.ResponseSchemaVersion, body.GetProperty("responseSchemaVersion").GetString());
        Assert.Equal("0123456789abcdef0123456789abcdef", body.GetProperty("requestId").GetString());
        Assert.Equal("fake-provider", body.GetProperty("provider").GetString());
        Assert.Equal("fake-model", body.GetProperty("model").GetString());
        Assert.Equal(new DateTime(2026, 9, 4, 7, 0, 0, DateTimeKind.Utc), body.GetProperty("generatedAtUtc").GetDateTime());
        Assert.Equal(
            "## Overall interpretation\n\nThe fake provider returned a valid interpretation.",
            body.GetProperty("interpretationMarkdown").GetString());

        var providerRequest = Assert.IsType<AnalysisInterpretationGenerationRequest>(providerFactory.Provider.LastRequest);
        Assert.Equal("0123456789abcdef0123456789abcdef", providerRequest.ClientRequestId);
        Assert.Equal("fast", providerRequest.GenerationProfile);
        Assert.Null(providerRequest.Package);
        Assert.True(providerRequest.PackageJson.HasValue);
        Assert.Equal("report-id", providerRequest.PackageJson.Value.GetProperty("report").GetProperty("reportId").GetString());
        Assert.Equal(ScientificGuidance.Revision, providerRequest.Prompt.PromptVersion);
        Assert.Equal(AnalysisInterpretationPromptBuilder.OutputFormatVersion, providerRequest.Prompt.OutputFormatVersion);
        Assert.Contains("PACKAGE_JSON", providerRequest.Prompt.UserMessage, StringComparison.Ordinal);
        Assert.Contains("\"reportId\":\"report-id\"", providerRequest.Prompt.CanonicalPackageJson, StringComparison.Ordinal);
        Assert.Equal(1, providerFactory.Provider.CallCount);
    }

    [Theory]
    [InlineData("nullReport", false)]
    [InlineData("numericReportId", false)]
    [InlineData("nullResult", false)]
    [InlineData("nullReport", true)]
    [InlineData("numericReportId", true)]
    [InlineData("nullResult", true)]
    public async Task OpaqueOptionalMetadataDoesNotDiscardSuccessfulDraft(string shape, bool usageLoggingEnabled)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "ftitc-endpoint-usage-" + Guid.NewGuid().ToString("N") + ".db");
        using var providerFactory = new ProviderWebApplicationFactory(enabled: true, usageLoggingEnabled: usageLoggingEnabled, usageDatabasePath: databasePath);
        using var providerClient = providerFactory.CreateClient();
        var request = ValidRequestNode();
        switch (shape)
        {
            case "nullReport": request["package"]!["report"] = null; break;
            case "numericReportId": request["package"]!["report"]!["reportId"] = 17; break;
            case "nullResult": request["package"]!["results"] = new JsonArray((JsonNode?)null); break;
        }

        using var response = await PostJsonWithClient(providerClient, request.ToJsonString(), NextClientIp());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, providerFactory.Provider.CallCount);
        var package = providerFactory.Provider.LastRequest!.PackageJson!.Value;
        if (shape == "nullReport") Assert.Equal(JsonValueKind.Null, package.GetProperty("report").ValueKind);
        if (shape == "numericReportId") Assert.Equal(17, package.GetProperty("report").GetProperty("reportId").GetInt32());
        if (shape == "nullResult") Assert.Equal(JsonValueKind.Null, package.GetProperty("results").EnumerateArray().Single().ValueKind);
    }

    [Fact]
    public async Task IncompleteProviderResponseKeepsAttemptTotalsInFailedRequestSummary()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "ftitc-endpoint-usage-" + Guid.NewGuid().ToString("N") + ".db");
        using var providerFactory = new ProviderWebApplicationFactory(enabled: true, usageLoggingEnabled: true,
            usageDatabasePath: databasePath, incompleteProvider: true);
        using var providerClient = providerFactory.CreateClient();

        using var response = await PostJsonWithClient(providerClient, ValidRequestJson(), NextClientIp());

        await AssertProblem(response, HttpStatusCode.BadGateway, "interpretation_provider_invalid_response");
        var store = providerFactory.Services.GetRequiredService<InterpretationUsageStore>();
        using var connection = store.OpenForCommand(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT provider_attempts,input_tokens,output_tokens,reasoning_tokens,total_tokens,estimated_cost FROM requests WHERE request_id=$id";
        command.Parameters.AddWithValue("$id", "0123456789abcdef0123456789abcdef"); using var reader = command.ExecuteReader();
        Assert.True(reader.Read()); Assert.Equal(1, reader.GetInt32(0)); Assert.Equal(1000, reader.GetInt32(1));
        Assert.Equal(6000, reader.GetInt32(2)); Assert.Equal(5000, reader.GetInt32(3)); Assert.Equal(7000, reader.GetInt32(4));
        Assert.False(reader.IsDBNull(5)); Assert.True(reader.GetDecimal(5) > 0);
    }

    [Fact]
    public async Task MalformedOptionalReportIdPreservesProviderFailureResponse()
    {
        using var providerFactory = new ProviderWebApplicationFactory(enabled: true, providerFailure: true);
        using var providerClient = providerFactory.CreateClient();
        var request = ValidRequestNode();
        request["package"]!["report"]!["reportId"] = 17;

        using var response = await PostJsonWithClient(providerClient, request.ToJsonString(), NextClientIp());

        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "interpretation_provider_unavailable");
        Assert.Equal(1, providerFactory.Provider.CallCount);
    }

    [Fact]
    public async Task DisabledFeatureDoesNotInvokeConfiguredProvider()
    {
        using var providerFactory = new ProviderWebApplicationFactory(enabled: false);
        using var providerClient = providerFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

        using var response = await PostJsonWithClient(
            providerClient,
            ValidRequestJson(),
            "198.51.100.102");

        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");
        Assert.Equal(0, providerFactory.Provider.CallCount);
    }

    [Fact]
    public async Task ProviderTextWithoutRequestedHeadingReachesReview()
    {
        using var providerFactory = new ProviderWebApplicationFactory(enabled: true, invalidResponse: true);
        using var providerClient = providerFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

        using var response = await PostJsonWithClient(
            providerClient,
            ValidRequestJson(),
            "198.51.100.103");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("This response has no required heading.", body.RootElement.GetProperty("interpretationMarkdown").GetString());
        Assert.Equal(1, providerFactory.Provider.CallCount);
    }

    [Fact]
    public async Task RejectsNonJsonContentType()
    {
        using var body = new StringContent(ValidRequestJson(), Encoding.UTF8, "text/plain");
        using var response = await Send(body);

        await AssertProblem(response, HttpStatusCode.UnsupportedMediaType, "invalid_content_type");
    }

    [Fact]
    public async Task RateLimitsGenerationPerForwardedClientIp()
    {
        var settings = factory.Services.GetRequiredService<IOptions<InterpretationOptions>>().Value.RateLimit;
        const string limitedIp = "198.51.100.40";

        for (var index = 0; index < settings.PermitLimit; index++)
        {
            using var accepted = await PostJson(ValidRequestJson(), limitedIp);
            await AssertProblem(accepted, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");
        }

        using var rejected = await PostJson(ValidRequestJson(), limitedIp);
        var problem = await AssertProblem(rejected, HttpStatusCode.TooManyRequests, "interpretation_rate_limited");
        Assert.Equal("Interpretation rate limit reached", problem.GetProperty("title").GetString());
        var retryAfter = Assert.Single(rejected.Headers.GetValues("Retry-After"));
        Assert.InRange(int.Parse(retryAfter), 1, settings.WindowSeconds);

        using var differentIp = await PostJson(ValidRequestJson(), "198.51.100.41");
        await AssertProblem(differentIp, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");
    }

    [Fact]
    public async Task ForwardLimitPreventsLeftmostSpoofingFromChangingRateLimitPartition()
    {
        var settings = factory.Services.GetRequiredService<IOptions<InterpretationOptions>>().Value.RateLimit;
        const string caddyReportedClientIp = "198.51.100.60";

        for (var index = 0; index < settings.PermitLimit; index++)
        {
            var forwardedFor = $"203.0.113.{index + 1}, {caddyReportedClientIp}";
            using var accepted = await PostJson(ValidRequestJson(), forwardedFor);
            await AssertProblem(accepted, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");
        }

        using var rejected = await PostJson(
            ValidRequestJson(),
            $"192.0.2.99, {caddyReportedClientIp}");
        await AssertProblem(rejected, HttpStatusCode.TooManyRequests, "interpretation_rate_limited");
    }

    [Fact]
    public async Task StatusEndpointIsNotRateLimited()
    {
        var settings = factory.Services.GetRequiredService<IOptions<InterpretationOptions>>().Value.RateLimit;

        for (var index = 0; index <= settings.PermitLimit; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/interpretation/status");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.80");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ deliberately invalid JSON")]
    public async Task RejectsMalformedJson(string json)
    {
        using var response = await PostJson(json);

        await AssertProblem(response, HttpStatusCode.BadRequest, "invalid_interpretation_json");
    }

    [Fact]
    public async Task RejectsNonObjectJson()
    {
        using var response = await PostJson("null");

        await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "invalid_interpretation_request");
    }

    public static IEnumerable<object[]> InvalidEnvelopeShapes()
    {
        var wrongCase = ValidRequestNode();
        var requestSchemaVersion = wrongCase["requestSchemaVersion"];
        wrongCase.Remove("requestSchemaVersion");
        wrongCase["RequestSchemaVersion"] = requestSchemaVersion;
        yield return new object[] { wrongCase.ToJsonString() };

        var wrongType = ValidRequestNode();
        wrongType["clientRequestId"] = 12;
        yield return new object[] { wrongType.ToJsonString() };

    }

    [Theory]
    [MemberData(nameof(InvalidEnvelopeShapes))]
    public async Task RejectsMalformedEnvelopeShapes(string json)
    {
        using var response = await PostJson(json);

        await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "invalid_interpretation_request");
    }

    [Theory]
    [InlineData("unexpected", true)]
    [InlineData("numericEnum", false)]
    public async Task PreservesUnknownScientificProperties(string property, bool topLevel)
    {
        var request = ValidRequestNode();
        if (topLevel)
            Package(request)[property] = true;
        else
            ((JsonObject)Package(request)["requestedInterpretation"]!)["audience"] = 1;

        using var response = await PostJson(request.ToJsonString());

        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");
    }

    [Theory]
    [InlineData("requestSchemaVersion")]
    [InlineData("outputInstructions")]
    [InlineData("generationProfile")]
    [InlineData("package")]
    [InlineData("clientRequestId")]
    public async Task RejectsMissingEnvelopeFields(string field)
    {
        var request = ValidRequestNode();
        request.Remove(field);

        using var response = await PostJson(request.ToJsonString());

        var problem = await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "invalid_interpretation_request");
        AssertError(problem, field);
    }

    [Theory]
    [InlineData("report")]
    [InlineData("results")]
    [InlineData("supportingExperiments")]
    [InlineData("studyContext")]
    [InlineData("requestedInterpretation")]
    [InlineData("evidenceCatalog")]
    [InlineData("dataBoundary")]
    public async Task PreservesMissingOptionalPackageRoots(string field)
    {
        var request = ValidRequestNode();
        Package(request).Remove(field);

        using var response = await PostJson(request.ToJsonString());

        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");
    }

    [Fact]
    public async Task RejectsMissingPackageSchema()
    {
        var request = ValidRequestNode();
        Package(request).Remove("packageSchemaVersion");

        using var response = await PostJson(request.ToJsonString());

        var problem = await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "invalid_interpretation_request");
        AssertError(problem, "package.packageSchemaVersion");
    }

    [Theory]
    [InlineData("requestSchemaVersion", "ft-itc-relay-request-9.0")]
    [InlineData("outputInstructions", "")]
    [InlineData("generationProfile", "slow")]
    public async Task RejectsUnsupportedEnvelopeVersionsAndProfile(string field, string value)
    {
        var request = ValidRequestNode();
        request[field] = value;

        using var response = await PostJson(request.ToJsonString());

        var problem = await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "invalid_interpretation_request");
        AssertError(problem, field);
    }

    [Fact]
    public async Task AcceptsArbitraryOutputFormatVersion()
    {
        var request = ValidRequestNode();
        request["outputFormatVersion"] = "future-format-2042";

        using var response = await PostJson(request.ToJsonString());

        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");
    }

    [Fact]
    public async Task RelaysCallerOutputInstructionsExactly()
    {
        using var providerFactory = new ProviderWebApplicationFactory(enabled: true);
        using var providerClient = providerFactory.CreateClient();
        var request = ValidRequestNode();
        const string instructions = "## Overall interpretation\nUse the supplied headings exactly.\nDo not invent sections.";
        request["outputInstructions"] = instructions;

        using var response = await PostJsonWithClient(providerClient, request.ToJsonString(), "198.51.100.202");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(instructions, providerFactory.Provider.LastRequest!.Prompt.ResponseFormatInstructions);
        Assert.Equal(instructions, providerFactory.Provider.LastRequest.Prompt.UserMessage.Split("\n\nPACKAGE_JSON\n", 2)[0].Replace("PRESENTATION_INSTRUCTIONS\n", ""));
    }

    [Fact]
    public async Task RejectsUnsupportedPackageSchema()
    {
        var request = ValidRequestNode();
        Package(request)["packageSchemaVersion"] = "9.0";

        using var response = await PostJson(request.ToJsonString());

        var problem = await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "invalid_interpretation_request");
        AssertError(problem, "package.packageSchemaVersion");
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("not-a-request-id")]
    public async Task RejectsInvalidRequestIds(string requestId)
    {
        var request = ValidRequestNode();
        request["clientRequestId"] = requestId;

        using var response = await PostJson(request.ToJsonString());

        var problem = await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "invalid_interpretation_request");
        AssertError(problem, "clientRequestId");
    }

    [Theory]
    [InlineData("missingResult")]
    [InlineData("multipleResults")]
    [InlineData("mismatchedResult")]
    [InlineData("emptyEvidenceId")]
    [InlineData("duplicateEvidenceId")]
    [InlineData("missingReportEvidence")]
    [InlineData("missingResultEvidence")]
    [InlineData("rawThermograms")]
    [InlineData("baselineArrays")]
    [InlineData("bootstrapArrays")]
    [InlineData("localPaths")]
    public async Task PreservesInvalidOrFutureScientificPackageShapes(string scenario)
    {
        var request = ValidRequestNode();
        MutatePackageInvariant(request, scenario);

        using var response = await PostJson(request.ToJsonString());

        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");
    }

    [Fact]
    public async Task EnforcesTwoMegabyteLimitForDeclaredAndChunkedBodies()
    {
        var valid = ValidRequestJson();
        var atLimit = PadToUtf8ByteCount(valid, checked((int)InterpretationRequestReader.MaxRequestBytes));
        var overLimit = atLimit + " ";

        using (var accepted = await PostJson(atLimit))
            await AssertProblem(accepted, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");

        using (var declared = await PostJson(overLimit))
            await AssertProblem(declared, HttpStatusCode.RequestEntityTooLarge, "interpretation_request_too_large");

        using var chunkedBody = new UnknownLengthJsonContent(overLimit);
        using var chunked = await Send(chunkedBody);
        await AssertProblem(chunked, HttpStatusCode.RequestEntityTooLarge, "interpretation_request_too_large");
    }

    [Fact]
    public async Task ExistingViewerAndUploadBoundariesRemainReachable()
    {
        using var viewer = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, viewer.StatusCode);

        using var uploadBody = new MultipartFormDataContent();
        uploadBody.Add(new StringContent("not read without a token"), "file", "sample.ftxtc");
        using var upload = await client.PostAsync("/api/viewer/open", uploadBody);

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        var problem = await upload.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("antiforgery_validation_failed", problem.GetProperty("code").GetString());
    }

    async Task<HttpResponseMessage> PostJson(string json, string? forwardedFor = null)
    {
        var body = new StringContent(json, Encoding.UTF8, "application/json");
        return await Send(body, forwardedFor);
    }

    async Task<HttpResponseMessage> Send(HttpContent body, string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/interpretation/generate")
        {
            Content = body,
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor ?? NextClientIp());
        return await client.SendAsync(request);
    }

    static async Task<HttpResponseMessage> PostJsonWithClient(
        HttpClient targetClient,
        string json,
        string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/interpretation/generate")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        return await targetClient.SendAsync(request);
    }

    static string NextClientIp()
    {
        var value = Interlocked.Increment(ref nextClientIp);
        return $"198.18.{value / 250}.{value % 250 + 1}";
    }

    static async Task<JsonElement> AssertProblem(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }

    static void AssertError(JsonElement problem, string path)
    {
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty(path, out var messages), $"No validation error was returned for {path}.");
        Assert.NotEmpty(messages.EnumerateArray());
    }

    static string ValidRequestJson() => ValidRequestNode().ToJsonString();

    [Fact]
    public async Task OpaquePackagePreservesUnknownNullAndHighPrecisionEvidence()
    {
        using var providerFactory = new ProviderWebApplicationFactory(enabled: true);
        using var providerClient = providerFactory.CreateClient();
        var request = ValidRequestNode();
        request["package"]!["futureEvidence"] = new JsonObject { ["enum"] = "future-value", ["nullable"] = null, ["precise"] = JsonNode.Parse("1234567890.1234567890123456789") };
        using var response = await PostJsonWithClient(providerClient, request.ToJsonString(), "198.51.100.201");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = providerFactory.Provider.LastRequest!.PackageJson!.Value.GetRawText();
        Assert.Contains("future-value", raw, StringComparison.Ordinal);
        Assert.Contains("1234567890.1234567890123456789", raw, StringComparison.Ordinal);
        Assert.Contains("\"nullable\":null", raw, StringComparison.Ordinal);
    }

    static JsonObject ValidRequestNode() => new()
    {
        ["requestSchemaVersion"] = FtItcInterpretationClient.RequestSchemaVersion,
        ["outputInstructions"] = AnalysisInterpretationPromptBuilder.BuildResponseFormatInstructions(),
        ["outputFormatVersion"] = AnalysisInterpretationPromptBuilder.OutputFormatVersion,
        ["generationProfile"] = "fast",
        ["package"] = new JsonObject
        {
            ["packageSchemaVersion"] = AnalysisInterpretationPackageBuilder.PackageSchemaVersion,
            ["report"] = new JsonObject
            {
                ["evidenceId"] = "report-1",
                ["reportId"] = "report-id",
                ["name"] = "Report",
                ["dateUtc"] = "2026-09-04T00:00:00Z",
                ["authorComments"] = "",
                ["resultIds"] = new JsonArray("result-id"),
                ["references"] = new JsonArray
                {
                    new JsonObject { ["reportReference"] = "1", ["kind"] = "result", ["id"] = "result-id" },
                    new JsonObject { ["reportReference"] = "1A", ["kind"] = "result-experiment", ["id"] = "experiment-id", ["parentReference"] = "1" },
                },
            },
            ["results"] = new JsonArray { new JsonObject
            {
                ["evidenceId"] = "result-1", ["reportReference"] = "1",
                ["resultId"] = "result-id", ["name"] = "Result",
                ["model"] = new JsonObject { ["type"] = "one-set-of-sites" },
                ["solver"] = new JsonObject(),
                ["experiments"] = new JsonArray { new JsonObject
                {
                    ["evidenceId"] = "result-1/experiment-1", ["reportReference"] = "1A", ["experimentId"] = "experiment-id",
                    ["sourceStateFingerprint"] = new string('a', 64), ["injections"] = new JsonArray(),
                } },
            } },
            ["supportingExperiments"] = new JsonArray(),
            ["studyContext"] = new JsonObject(),
            ["requestedInterpretation"] = new JsonObject
            {
                ["audience"] = "mixedScientific",
                ["detail"] = "detailed",
                ["injectionRows"] = "all",
                ["allowGeneralModelKnowledge"] = true,
                ["requestedSections"] = new JsonArray("overallInterpretation"),
            },
            ["evidenceCatalog"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "report-1",
                    ["kind"] = "report",
                    ["label"] = "Report",
                    ["parentId"] = null,
                },
                new JsonObject
                {
                    ["id"] = "result-1",
                    ["kind"] = "result",
                    ["label"] = "Result",
                    ["parentId"] = "report-1",
                },
                new JsonObject { ["id"] = "result-1/experiment-1", ["kind"] = "experiment", ["parentId"] = "result-1" },
            },
            ["dataBoundary"] = new JsonObject
            {
                ["containsRawThermogramSamples"] = false,
                ["containsBaselineArrays"] = false,
                ["containsBootstrapReplicateArrays"] = false,
                ["containsLocalPaths"] = false,
                ["modelObservationRestriction"] = "Raw data are not supplied.",
            },
        },
        ["clientRequestId"] = "0123456789abcdef0123456789abcdef",
    };

    static JsonObject Package(JsonObject request) => (JsonObject)request["package"]!;

    static void MutatePackageInvariant(JsonObject request, string scenario)
    {
        var package = Package(request);
        var report = (JsonObject)package["report"]!;
        var catalog = (JsonArray)package["evidenceCatalog"]!;
        var boundary = (JsonObject)package["dataBoundary"]!;

        switch (scenario)
        {
            case "missingResult": report["resultIds"] = new JsonArray(); break;
            case "multipleResults": report["resultIds"] = new JsonArray("result-id", "second-result"); break;
            case "mismatchedResult": report["resultIds"] = new JsonArray("different-result"); break;
            case "emptyEvidenceId": ((JsonObject)catalog[0]!)["id"] = ""; break;
            case "duplicateEvidenceId": ((JsonObject)catalog[1]!)["id"] = "report-1"; break;
            case "missingReportEvidence": catalog.RemoveAt(0); break;
            case "missingResultEvidence": catalog.RemoveAt(1); break;
            case "rawThermograms": boundary["containsRawThermogramSamples"] = true; break;
            case "baselineArrays": boundary["containsBaselineArrays"] = true; break;
            case "bootstrapArrays": boundary["containsBootstrapReplicateArrays"] = true; break;
            case "localPaths": boundary["containsLocalPaths"] = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    [Fact]
    public async Task CompleteStoredPackagePassesStrictLocalContractWithHistoricalEvidence()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "jors.ftxtc"));
        var contents = await AnalysisITC.Core.DataReaders.FTXTCReader.ReadStream(stream);
        var results = contents.OfType<AnalysisITC.Core.Data.AnalysisResult>().ToList();
        // Create stale input explicitly so refreshing the example project does
        // not remove this test's historical-evidence coverage.
        var result = results[0];
        result.SetValiditySnapshot(AnalysisITC.Core.Data.AnalysisResultValiditySnapshot.Capture(result.Solution));
        var experiment = result.Solution.Solutions[0].Data;
        var originalConcentration = experiment.CellConcentration.Value;
        experiment.CellConcentration = new AnalysisITC.Core.Numerics.FloatWithError(originalConcentration * 2);
        // Establish the historical-evidence condition explicitly; the saved fixture may contain current fits.
        var injection = results[0].Solution.Solutions[0].Data.Injections[1];
        injection.Include = !injection.Include;
        var report = new AnalysisITC.Core.Data.AnalysisReport(); report.SetResultIds(results.Select(item => item.UniqueID));
        var package = AnalysisInterpretationPackageBuilder.Build(report, id => results.Single(item => item.UniqueID == id), _ => null);
        var historicalResult = Assert.Single(package.Results, item => item.ResultId == result.UniqueID);
        Assert.Contains(historicalResult.HistoricalFitInputs, input =>
            input.GetProperty("cellConcentration").GetDouble() == originalConcentration);
        var request = ValidRequestNode(); request["package"] = JsonNode.Parse(AnalysisInterpretationPromptBuilder.Build(package).CanonicalPackageJson);
        using var response = await PostJson(request.ToJsonString());
        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");
    }

    [Fact]
    public async Task RejectsHistoricalSnapshotUnknownArraysAndNullOmissions()
    {
        var request = ValidRequestNode();
        Package(request)["omissions"] = null;
        ((JsonObject)((JsonArray)Package(request)["results"]!)[0]!)["historicalFitInputs"] = new JsonArray
        { new JsonObject { ["experimentID"] = "experiment-id", ["hiddenRawSamples"] = new JsonArray(1, 2, 3) } };
        using var response = await PostJson(request.ToJsonString());
        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");
    }

    [Fact]
    public async Task RejectsStaleResultInformationCriteriaWithoutCallerSuppliedReason()
    {
        var request = ValidRequestNode();
        var result = (JsonObject)((JsonArray)Package(request)["results"]!)[0]!;
        result["validityStatus"] = "Invalid";
        result["informationCriteria"] = new JsonObject { ["observationCount"] = 20 };
        using var response = await PostJson(request.ToJsonString());
        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "interpretation_unavailable");
    }

    static string PadToUtf8ByteCount(string value, int byteCount)
    {
        var currentBytes = Encoding.UTF8.GetByteCount(value);
        Assert.True(currentBytes <= byteCount);
        return value + new string(' ', byteCount - currentBytes);
    }

    sealed class UnknownLengthJsonContent : HttpContent
    {
        readonly byte[] body;

        public UnknownLengthJsonContent(string json)
        {
            body = Encoding.UTF8.GetBytes(json);
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(body).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    sealed class ProviderWebApplicationFactory : WebApplicationFactory<Program>
    {
        readonly bool enabled;
        readonly bool usageLoggingEnabled;
        readonly string? usageDatabasePath;

        readonly bool incompleteProvider;

        public ProviderWebApplicationFactory(bool enabled, bool invalidResponse = false, bool usageLoggingEnabled = false,
            string? usageDatabasePath = null, bool incompleteProvider = false, bool providerFailure = false)
        {
            this.enabled = enabled;
            this.usageLoggingEnabled = usageLoggingEnabled;
            this.usageDatabasePath = usageDatabasePath;
            this.incompleteProvider = incompleteProvider;
            Provider = new FakeInterpretationProvider(invalidResponse, providerFailure);
        }

        public FakeInterpretationProvider Provider { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Interpretation:Enabled"] = enabled.ToString(),
                    ["Interpretation:UsageLog:Enabled"] = usageLoggingEnabled.ToString(),
                    ["Interpretation:UsageLog:DatabasePath"] = usageDatabasePath,
                    ["Interpretation:Pricing:fake-model:Revision"] = "test",
                    ["Interpretation:Pricing:fake-model:InputPerMillion"] = "2",
                    ["Interpretation:Pricing:fake-model:OutputPerMillion"] = "12",
                });
            });
            builder.ConfigureServices(services =>
            {
                if (incompleteProvider)
                    services.AddSingleton<IAnalysisInterpretationProvider, IncompleteAccountingProvider>();
                else
                    services.AddSingleton<IAnalysisInterpretationProvider>(Provider);
            });
        }
    }

    sealed class FakeInterpretationProvider : IAnalysisInterpretationProvider
    {
        readonly bool invalidResponse;
        readonly bool providerFailure;
        int callCount;

        public FakeInterpretationProvider(bool invalidResponse, bool providerFailure = false)
        {
            this.invalidResponse = invalidResponse;
            this.providerFailure = providerFailure;
        }

        public int CallCount => callCount;
        public AnalysisInterpretationGenerationRequest? LastRequest { get; private set; }

        public Task<AnalysisInterpretationProviderResponse> GenerateAsync(
            AnalysisInterpretationGenerationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            Interlocked.Increment(ref callCount);
            if (providerFailure)
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure,
                    "Simulated provider failure.");
            return Task.FromResult(new AnalysisInterpretationProviderResponse
            {
                RequestId = "provider-request-id",
                Provider = "fake-provider",
                Model = "fake-model",
                GeneratedAtUtc = new DateTime(2026, 9, 4, 7, 0, 0, DateTimeKind.Utc),
                InterpretationMarkdown = invalidResponse
                    ? "This response has no required heading."
                    : "## Overall interpretation\n\nThe fake provider returned a valid interpretation.",
            });
        }
    }

    sealed class IncompleteAccountingProvider(InterpretationUsageStore store) : IAnalysisInterpretationProvider
    {
        public Task<AnalysisInterpretationProviderResponse> GenerateAsync(
            AnalysisInterpretationGenerationRequest request,
            CancellationToken cancellationToken)
        {
            var cost = store.Estimate("fake-model", 1000, 0, 0, 6000, 0);
            store.RecordAttempt(new InterpretationUsageAttempt
            {
                RequestId = request.ClientRequestId, AttemptNumber = 1, TimestampUtc = DateTime.UtcNow,
                Model = "fake-model", ReasoningEffort = "medium", InputTokens = 1000, OutputTokens = 6000,
                ReasoningTokens = 5000, VisibleOutputTokens = 1000, TotalTokens = 7000,
                ModelCost = cost.Model, FileSearchCost = cost.FileSearch, CombinedCost = cost.Combined,
                Outcome = "provider_error", HttpStatus = 200, ErrorCode = "max_output_tokens",
            });
            throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                "The model service did not complete the response.");
        }
    }
}
