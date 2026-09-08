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
        Assert.Equal("report-id", providerRequest.Package.Report.ReportId);
        Assert.Equal(AnalysisInterpretationPromptBuilder.PromptVersion, providerRequest.Prompt.PromptVersion);
        Assert.Equal(AnalysisInterpretationPromptBuilder.OutputFormatVersion, providerRequest.Prompt.OutputFormatVersion);
        Assert.Contains("PACKAGE_JSON", providerRequest.Prompt.UserMessage, StringComparison.Ordinal);
        Assert.Contains("\"reportId\":\"report-id\"", providerRequest.Prompt.CanonicalPackageJson, StringComparison.Ordinal);
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
    [InlineData("null")]
    public async Task RejectsMalformedOrEmptyJson(string json)
    {
        using var response = await PostJson(json);

        var problem = await AssertProblem(response, HttpStatusCode.BadRequest, "invalid_interpretation_json");
        Assert.NotEmpty(problem.GetProperty("errors").EnumerateObject());
    }

    public static IEnumerable<object[]> InvalidJsonShapes()
    {
        var unknown = ValidRequestNode();
        unknown["unexpected"] = true;
        yield return new object[] { unknown.ToJsonString() };

        var wrongCase = ValidRequestNode();
        var requestSchemaVersion = wrongCase["requestSchemaVersion"];
        wrongCase.Remove("requestSchemaVersion");
        wrongCase["RequestSchemaVersion"] = requestSchemaVersion;
        yield return new object[] { wrongCase.ToJsonString() };

        var nestedUnknown = ValidRequestNode();
        Package(nestedUnknown)["unexpected"] = true;
        yield return new object[] { nestedUnknown.ToJsonString() };

        var integerEnum = ValidRequestNode();
        ((JsonObject)Package(integerEnum)["requestedInterpretation"]!)["audience"] = 1;
        yield return new object[] { integerEnum.ToJsonString() };

        var wrongType = ValidRequestNode();
        wrongType["clientRequestId"] = 12;
        yield return new object[] { wrongType.ToJsonString() };

        var duplicate = ValidRequestJson();
        yield return new object[]
        {
            "{\"requestSchemaVersion\":\"" + FtItcInterpretationClient.RequestSchemaVersion + "\"," + duplicate[1..],
        };
    }

    [Theory]
    [MemberData(nameof(InvalidJsonShapes))]
    public async Task RejectsUnknownDuplicateIncorrectlyCasedAndInvalidTypedProperties(string json)
    {
        using var response = await PostJson(json);

        var problem = await AssertProblem(response, HttpStatusCode.BadRequest, "invalid_interpretation_json");
        Assert.NotEmpty(problem.GetProperty("errors").EnumerateObject());
    }

    [Theory]
    [InlineData("requestSchemaVersion")]
    [InlineData("promptProfileVersion")]
    [InlineData("outputFormatVersion")]
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
    [InlineData("packageSchemaVersion")]
    [InlineData("report")]
    [InlineData("results")]
    [InlineData("supportingExperiments")]
    [InlineData("studyContext")]
    [InlineData("requestedInterpretation")]
    [InlineData("evidenceCatalog")]
    [InlineData("dataBoundary")]
    public async Task RejectsMissingPackageRoots(string field)
    {
        var request = ValidRequestNode();
        Package(request).Remove(field);

        using var response = await PostJson(request.ToJsonString());

        var problem = await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "invalid_interpretation_request");
        AssertError(problem, "package." + field);
    }

    [Theory]
    [InlineData("requestSchemaVersion", "ft-itc-relay-request-9.0")]
    [InlineData("promptProfileVersion", "itc-interpretation-9.0")]
    [InlineData("outputFormatVersion", "itc-interpretation-markdown-9.0")]
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
    [InlineData("missingResult", "package.report.resultIds")]
    [InlineData("multipleResults", "package.report.resultIds")]
    [InlineData("mismatchedResult", "package.report.resultIds")]
    [InlineData("emptyEvidenceId", "package.evidenceCatalog")]
    [InlineData("duplicateEvidenceId", "package.evidenceCatalog")]
    [InlineData("missingReportEvidence", "package.report.evidenceId")]
    [InlineData("missingResultEvidence", "package.results.evidenceId")]
    [InlineData("rawThermograms", "package.dataBoundary.containsRawThermogramSamples")]
    [InlineData("baselineArrays", "package.dataBoundary.containsBaselineArrays")]
    [InlineData("bootstrapArrays", "package.dataBoundary.containsBootstrapReplicateArrays")]
    [InlineData("localPaths", "package.dataBoundary.containsLocalPaths")]
    public async Task RejectsInvalidPackageInvariants(string scenario, string expectedPath)
    {
        var request = ValidRequestNode();
        MutatePackageInvariant(request, scenario);

        using var response = await PostJson(request.ToJsonString());

        var problem = await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "invalid_interpretation_request");
        AssertError(problem, expectedPath);
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

    static JsonObject ValidRequestNode() => new()
    {
        ["requestSchemaVersion"] = FtItcInterpretationClient.RequestSchemaVersion,
        ["promptProfileVersion"] = AnalysisInterpretationPromptBuilder.PromptVersion,
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
        var problem = await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "invalid_interpretation_request");
        AssertError(problem, "package.omissions");
        AssertError(problem, "package.results.historicalFitInputs.hiddenRawSamples");
    }

    [Fact]
    public async Task RejectsStaleResultInformationCriteriaWithoutCallerSuppliedReason()
    {
        var request = ValidRequestNode();
        var result = (JsonObject)((JsonArray)Package(request)["results"]!)[0]!;
        result["validityStatus"] = "Invalid";
        result["informationCriteria"] = new JsonObject { ["observationCount"] = 20 };
        using var response = await PostJson(request.ToJsonString());
        var problem = await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "invalid_interpretation_request");
        AssertError(problem, "package.results.informationCriteria");
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

        public ProviderWebApplicationFactory(bool enabled, bool invalidResponse = false)
        {
            this.enabled = enabled;
            Provider = new FakeInterpretationProvider(invalidResponse);
        }

        public FakeInterpretationProvider Provider { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Interpretation:Enabled"] = enabled.ToString(),
                });
            });
            builder.ConfigureServices(services =>
                services.AddSingleton<IAnalysisInterpretationProvider>(Provider));
        }
    }

    sealed class FakeInterpretationProvider : IAnalysisInterpretationProvider
    {
        readonly bool invalidResponse;
        int callCount;

        public FakeInterpretationProvider(bool invalidResponse)
        {
            this.invalidResponse = invalidResponse;
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
}
