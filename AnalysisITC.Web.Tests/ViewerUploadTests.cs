using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AnalysisITC.Web.Tests;

using Buffer = AnalysisITC.Core.Data.Buffer;

public sealed class ViewerUploadTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly WebApplicationFactory<Program> factory;
    readonly HttpClient client;

    public ViewerUploadTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });
    }

    [Fact]
    public async Task DevelopmentAntiforgeryTokenSupportsLocalHttp()
    {
        using var httpClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost"),
            HandleCookies = true,
        });

        var response = await httpClient.GetAsync("/api/viewer/token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AntiforgeryCookieIsRestrictedToHttps()
    {
        var response = await client.GetAsync("/api/viewer/token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ViewerShellUsesListsAndExclusiveProcessingModes()
    {
        var page = await client.GetAsync("/");
        var html = await page.Content.ReadAsStringAsync();
        var script = await client.GetStringAsync("/app.js");

        Assert.True(page.Headers.CacheControl?.NoStore);
        Assert.Equal("2026.08.25-correlation.1", page.Headers.GetValues("X-FTITC-Viewer-Build").Single());
        Assert.Contains("name=\"description\" content=\"Open and review FT-ITC Analysis project files in your browser.", html);
        Assert.Contains("property=\"og:title\" content=\"FT-ITC Analysis Viewer\"", html);
        Assert.Contains("name=\"twitter:card\" content=\"summary\"", html);
        Assert.Contains("Open an FT-ITC project or raw data file", html);
        Assert.Contains("Review thermograms, baseline correction, integration regions, saved fits, and analysis results", html);
        Assert.Contains("id=\"experiment-list\"", html);
        Assert.Contains("Select an .ftxtc, .ftitc, .itc, .nitc, or .opj file", html);
        Assert.Contains("id=\"result-list\"", html);
        Assert.Contains("accept=\".ftxtc,.ftitc,.itc,.nitc,.opj\"", html);
        Assert.Contains("processed transiently on the server", html);
        Assert.Contains("not intentionally retained", html);
        Assert.Contains("temporary server storage", html);
        Assert.DoesNotContain("id=\"experiment-select\"", html);
        Assert.DoesNotContain("id=\"result-select\"", html);
        Assert.Contains("id=\"processed-mode-raw\"", html);
        Assert.Contains("id=\"processed-mode-corrected\"", html);
        Assert.Contains("id=\"processed-integration-ranges\"", html);
        Assert.Contains("id=\"processing-description\"", html);
        Assert.Contains("Saved polynomial and segmented baselines", html);
        Assert.Contains("[\"metadata\", \"raw\", \"processed\", \"fit\"]", script);
        Assert.DoesNotContain("renderIntegrated", script);
        Assert.Contains("Baseline = 0", script);
        Assert.Contains("integrationRangeTraces", script);
        Assert.Contains("fill: \"toself\"", script);
        Assert.Contains("Included integration range", script);
        Assert.Contains("Excluded integration range", script);
        Assert.Contains("useGrouping: false", script);
        Assert.DoesNotContain("toLocaleString", script);
        Assert.Contains("const xRange = [0,", script);
        Assert.Contains("minallowed: 0", script);
        Assert.Contains("anchor: \"y2\"", script);
        Assert.DoesNotContain("xaxis: \"x2\"", script);
        Assert.Contains("preferredExperimentView", script);
        Assert.Contains("parameterSection(\"Fitted parameters\"", script);
        Assert.Contains("parameterSection(\"Derived parameters\"", script);
        Assert.Contains("parameter.isLocked ? \"Locked\"", script);
        Assert.Contains("formatParameterInterval", script);
        Assert.Contains("Bootstrap interval unavailable", script);
        Assert.Contains("connectgaps: false", script);
        Assert.Contains("result-evaluation-temperature", html);
        Assert.Contains("id=\"result-advanced-card\"", html);
        Assert.Contains("id=\"result-correlation-card\"", html);
        Assert.Contains("id=\"result-correlation-view-select\"", html);
        Assert.Contains("id=\"result-correlation-plot\"", html);
        Assert.Contains("id=\"result-correlation-warnings\"", html);
        Assert.Contains("correlationViews", script);
        Assert.Contains("colorscale: [[0, \"#b94b45\"], [.5, \"#ffffff\"], [1, \"#386c93\"]]", script);
        Assert.Contains("zmin: -1", script);
        Assert.Contains("zmax: 1", script);
        Assert.Contains("texttemplate: \"%{text}\"", script);
        Assert.Contains("r = %{customdata[2]:.4f}", script);
        Assert.Contains("hoverlabel: { align: \"left\", font: { size: 11 } }", script);
        Assert.Contains("correlationViewKeysByResult", script);
        Assert.Contains("renderResultCorrelation", script);
        Assert.Contains("advanced-analysis-plot", html);
        Assert.Contains("2026.08.25-correlation.1", html);
        Assert.Contains("app.js?v=2026.08.25-correlation.1", html);
        Assert.Contains("viewer-charts-2.35.3.min.js?v=2026.08.25-correlation.1", html);
        Assert.Contains("href=\"https://ft-itc.org\"", html);
        Assert.Contains("href=\"https://github.com/FrederikTheisen/FT-ITC-Analysis\"", html);
        Assert.Contains("class=\"brand-mark\" src=\"/assets/ft-itc-icon-64.png", html);
        Assert.Contains("rel=\"icon\" type=\"image/png\"", html);
        Assert.Contains("rel=\"apple-touch-icon\"", html);
        Assert.Contains("const viewerBuild = \"2026.08.25-correlation.1\"", script);
        Assert.Contains("renderAdvancedAnalysis", script);
        Assert.Contains("advanced-analysis-metadata", html);
        Assert.Contains("advanced-analysis-parameter-table", html);
        Assert.Contains("legendgroup", script);
        Assert.DoesNotContain("layout.title = { text: plot.title", script);
        Assert.Contains("no displayable plot data", script);
        Assert.Contains("appendAdvancedCell", script);
        Assert.Contains("roundTemperatureToHalf", script);
        Assert.Contains("item.family", script);
        Assert.Contains("item.slotIndex", script);
        Assert.Contains("Binding steps", script);
        Assert.DoesNotContain("terms.get(\"Enthalpy2\")", script);
        Assert.Contains(".ftxtc", script);
        Assert.Contains("buildConfidenceBand", script);
        Assert.Contains("formatParameterNumber", script);
        Assert.Contains("95% bootstrap confidence", script);

        var charts = await client.GetStringAsync("/vendor/viewer-charts-2.35.3.min.js");
        Assert.Contains("plotly.js (cartesian - minified) v2.35.3", charts);

        var icon = await client.GetAsync("/assets/ft-itc-icon-32.png");
        Assert.Equal(HttpStatusCode.OK, icon.StatusCode);
        Assert.Equal("image/png", icon.Content.Headers.ContentType?.MediaType);
        Assert.True(icon.Content.Headers.ContentLength > 0);
    }

    [Fact]
    public async Task UploadRequiresAntiforgeryToken()
    {
        using var content = UploadContent("sample.itc", "$ITC\n");
        var response = await client.PostAsync("/api/viewer/open", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("antiforgery_validation_failed", await ProblemCode(response));
    }

    [Fact]
    public async Task RejectsEmptyUpload()
    {
        var token = await Token();
        using var content = UploadContent("empty.itc", Stream.Null);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/viewer/open") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("missing_file", await ProblemCode(response));
    }

    [Fact]
    public async Task RejectsUploadLargerThanFiftyMegabytes()
    {
        var token = await Token();
        using var content = UploadContent("oversize.itc", new ZeroStream(50L * 1024 * 1024 + 1));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/viewer/open") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("file_too_large", await ProblemCode(response));
    }

    [Theory]
    [InlineData("data_1.itc", "itc", 1)]
    [InlineData("data.ftitc", "ftitc", 3)]
    [InlineData("sample.nitc", "nitc", 1)]
    [InlineData("sample.opj", "opj", 1)]
    public async Task OpensRepresentativeFilesAndReturnsGraphArrays(string fixture, string expectedFormat, int experimentCount)
    {
        var token = await Token();
        using var content = UploadContent(fixture, File.OpenRead(Fixture(fixture)));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/viewer/open") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("completeReplicateCoordinates", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bootstrapSolutions", responseJson, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(responseJson);
        Assert.Equal(expectedFormat, json.RootElement.GetProperty("format").GetString());
        var experiments = json.RootElement.GetProperty("experiments");
        Assert.Equal(experimentCount, experiments.GetArrayLength());
        Assert.True(experiments[0].GetProperty("raw").GetProperty("timeSeconds").GetArrayLength() > 100);
        if (expectedFormat == "ftitc")
        {
            Assert.True(experiments[0].GetProperty("integrated").GetProperty("correctedHeatMicrojoules").GetArrayLength() > 0);
            var processed = experiments[0].GetProperty("processed");
            Assert.True(processed.GetProperty("correctedPowerMicrowatts").GetArrayLength() > 0);
            Assert.Equal(
                experiments[0].GetProperty("injectionCount").GetInt32(),
                processed.GetProperty("integrationStartSeconds").GetArrayLength());
            Assert.Equal(
                processed.GetProperty("integrationStartSeconds").GetArrayLength(),
                processed.GetProperty("integrationEndSeconds").GetArrayLength());
            var fits = experiments[0].GetProperty("fits");
            Assert.True(fits.GetArrayLength() > 0);
            var fitX = fits[0].GetProperty("x");
            var confidenceLower = fits[0].GetProperty("confidenceLowerKilojoulesPerMole");
            var confidenceUpper = fits[0].GetProperty("confidenceUpperKilojoulesPerMole");
            Assert.Equal(fitX.GetArrayLength(), confidenceLower.GetArrayLength());
            Assert.Equal(fitX.GetArrayLength(), confidenceUpper.GetArrayLength());
            Assert.Contains(fits.EnumerateArray(), fit =>
            {
                var lower = fit.GetProperty("confidenceLowerKilojoulesPerMole").EnumerateArray().ToArray();
                var upper = fit.GetProperty("confidenceUpperKilojoulesPerMole").EnumerateArray().ToArray();
                return lower.Zip(upper, (lo, hi) => hi.ValueKind == JsonValueKind.Number
                    && lo.ValueKind == JsonValueKind.Number
                    && hi.GetDouble() > lo.GetDouble()).Any(valid => valid);
            });
            var parameters = fits[0].GetProperty("parameters").EnumerateArray().ToArray();
            Assert.Contains(parameters, parameter =>
                parameter.GetProperty("key").GetString() == "Offset"
                && !parameter.GetProperty("isDerived").GetBoolean());
            Assert.Contains(parameters, parameter =>
                parameter.GetProperty("key").GetString() == "Gibbs1"
                && parameter.GetProperty("isDerived").GetBoolean());
            Assert.All(parameters, parameter =>
            {
                Assert.True(parameter.TryGetProperty("isLocked", out _));
                Assert.True(parameter.TryGetProperty("isGloballyDetermined", out _));
            });
            var results = json.RootElement.GetProperty("analysisResults");
            Assert.True(results.GetArrayLength() > 0);
            Assert.Contains(results.EnumerateArray(), result => result.GetProperty("solver").GetProperty("bootstrapIterations").GetInt32() > 0);
            var correlationViews = results[0].GetProperty("correlationViews");
            Assert.True(correlationViews.GetArrayLength() > 0);
            Assert.All(correlationViews.EnumerateArray(), view =>
            {
                Assert.False(string.IsNullOrWhiteSpace(view.GetProperty("key").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(view.GetProperty("availabilityStatus").GetString()));
                var parameters = view.GetProperty("parameters");
                if (view.GetProperty("isAvailable").GetBoolean())
                {
                    var matrix = view.GetProperty("correlationMatrix");
                    Assert.Equal(parameters.GetArrayLength(), matrix.GetArrayLength());
                    Assert.All(matrix.EnumerateArray(), row => Assert.Equal(parameters.GetArrayLength(), row.GetArrayLength()));
                }
                else
                {
                    Assert.Equal(JsonValueKind.Null, view.GetProperty("correlationMatrix").ValueKind);
                    Assert.False(string.IsNullOrWhiteSpace(view.GetProperty("reason").GetString()));
                }
            });
        }
        else
        {
            Assert.Empty(json.RootElement.GetProperty("analysisResults").EnumerateArray());
        }
    }

    [Fact]
    public async Task OpensNativeFtxtcProjectThroughUploadEndpoint()
    {
        using var source = File.OpenRead(Fixture("data.ftitc"));
        var containers = await FTITCReader.ReadStream(source);
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(
            package,
            containers.OfType<ExperimentData>(),
            containers.OfType<AnalysisResult>());
        package.Position = 0;

        var token = await Token();
        using var content = UploadContent("native-project.ftxtc", package);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/viewer/open") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", token);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("ftxtc", root.GetProperty("format").GetString());
        Assert.Equal(3, root.GetProperty("experiments").GetArrayLength());
        Assert.True(root.GetProperty("experiments")[0].GetProperty("raw").GetProperty("timeSeconds").GetArrayLength() > 100);
        Assert.True(root.GetProperty("analysisResults").GetArrayLength() > 0);
    }

    [Fact]
    public async Task UploadReturnsSavedAdvancedAnalysesAndPlotSeries()
    {
        using var source = File.OpenRead(Fixture("data.ftitc"));
        var containers = await FTITCReader.ReadStream(source);
        var sourceResult = Assert.Single(containers.OfType<AnalysisResult>());
        var buffers = new[] { Buffer.Hepes, Buffer.Tris, Buffer.SodiumPhosphate };
        for (var index = 0; index < sourceResult.Solution.Solutions.Count; index++)
        {
            var data = sourceResult.Solution.Solutions[index].Data;
            data.MeasuredTemperature = 20 + index * 5;
            data.Attributes.RemoveAll(attribute => attribute.Key is AttributeKey.Salt or AttributeKey.Buffer);
            var salt = ExperimentAttribute.FromKey(AttributeKey.Salt);
            salt.IntValue = (int)Salt.NaCl;
            salt.ParameterValue = new FloatWithError(0.05 + index * 0.05);
            data.Attributes.Add(salt);
            var buffer = ExperimentAttribute.FromKey(AttributeKey.Buffer);
            buffer.IntValue = (int)buffers[index];
            buffer.DoubleValue = 7.4;
            data.Attributes.Add(buffer);
        }

        var result = new AnalysisResult(sourceResult.Solution);
        var completed = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc);
        result.SpolarRecordAnalysis.RestoreResult(
            FTSRMethod.SRFoldedMode.Glob,
            FTSRMethod.SRTempMode.MeanTemperature,
            new FTSRMethod.SROutput(new FloatWithError(-0.11, 0.01), new FloatWithError(-0.22, 0.02),
                new FloatWithError(42, 2), new FloatWithError(25, 0.5)),
            20, completed);
        result.ElectrostaticsAnalysis.RestoreResult(
            new IonicStrengthDependenceFit(new FloatWithError(2e-6, 0.1e-6), new FloatWithError(1.2, 0.1), new FloatWithError(0), false),
            new LinearFitWithError(new FloatWithError(1.5, 0.1), new FloatWithError(-12, 0.2), 0),
            15, 16, completed, ErrorEstimationMethod.BootstrapResiduals);
        result.ProtonationAnalysis.RestoreResult(
            new FloatWithError(-25000, 500), new FloatWithError(0.8, 0.05), 18, completed,
            ErrorEstimationMethod.BootstrapResiduals);

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package,
            result.Solution.Solutions.Select(solution => solution.Data).Distinct(), new[] { result });
        package.Position = 0;
        var token = await Token();
        using var content = UploadContent("advanced.ftxtc", package);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/viewer/open") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var advanced = json.RootElement.GetProperty("analysisResults")[0].GetProperty("advancedAnalyses");
        var temperature = advanced.GetProperty("spolarRecord");
        var temperatureSeries = temperature.GetProperty("temperatureDependencePlot").GetProperty("series")
            .EnumerateArray().ToArray();
        foreach (var group in new[] { "Enthalpy1", "EntropyContribution1", "Gibbs1" })
        {
            Assert.Contains(temperatureSeries, series => series.GetProperty("kind").GetString() == "points"
                && series.GetProperty("group").GetString() == group
                && series.GetProperty("x").GetArrayLength() == sourceResult.Solution.Solutions.Count);
            Assert.Contains(temperatureSeries, series => series.GetProperty("kind").GetString() == "line"
                && series.GetProperty("group").GetString() == group
                && series.GetProperty("x").GetArrayLength() > 2);
        }
        Assert.DoesNotContain(temperatureSeries, series =>
            (series.GetProperty("label").GetString() ?? "").Contains("Hydration", StringComparison.OrdinalIgnoreCase)
            || (series.GetProperty("label").GetString() ?? "").Contains("Conformational", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0.0327965,
            temperature.GetProperty("hydrationContributionKilojoulesPerMole").GetProperty("value").GetDouble(), 8);
        Assert.Equal(0.065593,
            temperature.GetProperty("conformationalContributionKilojoulesPerMole").GetProperty("value").GetDouble(), 8);
        Assert.Equal(25,
            temperature.GetProperty("referenceTemperatureCelsius").GetProperty("value").GetDouble(), 8);
        Assert.Equal(42,
            temperature.GetProperty("residueEstimate").GetProperty("value").GetDouble(), 8);
        foreach (var property in new[]
                 {
                     "referenceTemperatureCelsius",
                     "hydrationContributionKilojoulesPerMole",
                     "conformationalContributionKilojoulesPerMole",
                     "residueEstimate",
                 })
        {
            var savedValue = temperature.GetProperty(property);
            Assert.True(savedValue.TryGetProperty("sd", out _));
            Assert.True(savedValue.TryGetProperty("confidenceLower", out _));
            Assert.True(savedValue.TryGetProperty("confidenceUpper", out _));
        }
        Assert.Equal(3, advanced.GetProperty("electrostatics").GetProperty("plots").GetArrayLength());
        Assert.True(advanced.GetProperty("protonation").GetProperty("plot").GetProperty("series").GetArrayLength() >= 2);
        Assert.Equal(2.0, advanced.GetProperty("electrostatics").GetProperty("kd0Micromolar").GetProperty("value").GetDouble(), 8);
    }

    [Fact]
    public async Task UploadReturnsCompleteResolvableAnalysisResultReferences()
    {
        using var json = await UploadAndReadJson("jors.ftxtc");
        var root = json.RootElement;
        var experiments = root.GetProperty("experiments").EnumerateArray().ToArray();
        var results = root.GetProperty("analysisResults").EnumerateArray().ToArray();

        Assert.Equal(2, results.Length);
        var dates = results.Select(item => item.GetProperty("date").GetDateTime()).ToArray();
        Assert.Equal(dates.OrderByDescending(item => item), dates);
        Assert.Contains(results, item => item.GetProperty("isGlobal").GetBoolean());
        Assert.All(results, result =>
        {
            Assert.True(result.GetProperty("members").GetArrayLength() > 0);
            Assert.True(result.GetProperty("solver").TryGetProperty("weightedFitting", out _));
            Assert.True(result.GetProperty("validity").TryGetProperty("status", out _));
            Assert.True(result.TryGetProperty("modelOptions", out _));
            Assert.True(result.TryGetProperty("constraints", out _));
            Assert.True(result.TryGetProperty("temperatureParameterEvaluation", out _));
            var resultKey = result.GetProperty("key").GetString();
            Assert.StartsWith("result-", resultKey);
            foreach (var member in result.GetProperty("members").EnumerateArray())
            {
                var experimentKey = member.GetProperty("experimentKey").GetString();
                var fitKey = member.GetProperty("fitKey").GetString();
                var experiment = Assert.Single(experiments, item => item.GetProperty("key").GetString() == experimentKey);
                var fit = Assert.Single(experiment.GetProperty("fits").EnumerateArray(), item => item.GetProperty("key").GetString() == fitKey);
                Assert.Equal(resultKey, fit.GetProperty("resultKey").GetString());
                Assert.True(fit.GetProperty("fittedKilojoulesPerMole").GetArrayLength() > 0);
                Assert.Equal(fit.GetProperty("observedKilojoulesPerMole").GetArrayLength(), fit.GetProperty("residualKilojoulesPerMole").GetArrayLength());
            }
        });
        var evaluations = results
            .Select(result => result.GetProperty("temperatureParameterEvaluation"))
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .ToArray();
        Assert.NotEmpty(evaluations);
        Assert.All(evaluations, evaluation =>
        {
            Assert.True(evaluation.GetProperty("dependences").GetArrayLength() > 0);
            Assert.True(evaluation.GetProperty("defaultTemperatureCelsius").GetDouble() > -273.15);
        });
    }

    [Fact]
    public async Task OpensOriginalTaggedFtitcDialect()
    {
        const string text =
            "<Experiment>" +
            "<FileName>old-project.itc</FileName>" +
            "<ID>legacy-experiment</ID>" +
            "<Date>2018-04-03T12:30:00.0000000</Date>" +
            "<SyringeConcentration>0.001,0</SyringeConcentration>" +
            "<CellConcentration>0.0001,0</CellConcentration>" +
            "<StirringSpeed>750</StirringSpeed>" +
            "<TargetTemperature>25</TargetTemperature>" +
            "<MeasuredTemperature>25.1</MeasuredTemperature>" +
            "<InitialDelay>60</InitialDelay>" +
            "<TargetPowerDiff>5</TargetPowerDiff>" +
            "<FeedBackMode>2</FeedBackMode>" +
            "<CellVolume>0.0002</CellVolume>" +
            "<Include>1</Include>" +
            "<InjectionList>0,0,10,0.000002,120,4,25,0,60;1,1,130,0.000002,120,4,25,0,60</InjectionList>" +
            "<DataPointList>0,0.000010,25,24.9;1,0.000011,25.01,24.9;2,0.000012,25.02,24.9</DataPointList>" +
            "</Experiment>";

        var token = await Token();
        using var content = UploadContent("legacy.ftitc", text);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/viewer/open") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", token);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("ftitc", root.GetProperty("format").GetString());
        var experiment = Assert.Single(root.GetProperty("experiments").EnumerateArray());
        Assert.Equal(2, experiment.GetProperty("injectionCount").GetInt32());
        Assert.Equal(3, experiment.GetProperty("raw").GetProperty("timeSeconds").GetArrayLength());
    }

    [Theory]
    [InlineData("data_1.itc", false)]
    [InlineData("temperature-series.ftxtc", true)]
    public async Task FilesWithoutProjectResultsKeepExperimentViews(string fixture, bool hasEmbeddedFits)
    {
        using var json = await UploadAndReadJson(fixture);
        var root = json.RootElement;

        Assert.Equal(0, root.GetProperty("analysisResults").GetArrayLength());
        var experiments = root.GetProperty("experiments");
        Assert.True(experiments.GetArrayLength() > 0);
        Assert.True(experiments[0].GetProperty("raw").GetProperty("timeSeconds").GetArrayLength() > 0);
        Assert.Equal(hasEmbeddedFits, experiments[0].GetProperty("fits").GetArrayLength() > 0);
    }

    [Theory]
    [InlineData("sample.txt", "$ITC\n", HttpStatusCode.UnsupportedMediaType, "unsupported_extension")]
    [InlineData("sample.ftxtc", "$ITC\n", HttpStatusCode.BadRequest, "format_mismatch")]
    [InlineData("sample.ftitc", "$ITC\n", HttpStatusCode.BadRequest, "format_mismatch")]
    [InlineData("sample.nitc", "$ITC\n", HttpStatusCode.BadRequest, "format_mismatch")]
    [InlineData("sample.opj", "$ITC\n", HttpStatusCode.BadRequest, "format_mismatch")]
    [InlineData("sample.ftitc", "FTITCVersion:1.1\nFILE:Experiment:broken.itc\nLIST:InjectionList\nbroken\n", HttpStatusCode.BadRequest, "malformed_file")]
    public async Task RejectsUnsupportedMismatchedAndMalformedFiles(string fileName, string body, HttpStatusCode status, string code)
    {
        var token = await Token();
        using var content = UploadContent(fileName, body);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/viewer/open") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await client.SendAsync(request);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, await ProblemCode(response));
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("AnalysisITC.Core", responseText);
        Assert.DoesNotContain("/Users/", responseText);
    }

    [Theory]
    [InlineData("../unsafe.itc")]
    [InlineData("C:\\private\\unsafe.itc")]
    public async Task SanitizesUploadedDisplayName(string uploadedName)
    {
        var token = await Token();
        using var content = UploadContent(uploadedName, File.OpenRead(Fixture("data_1.itc")));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/viewer/open") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await client.SendAsync(request);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("unsafe.itc", json.RootElement.GetProperty("displayName").GetString());
    }

    async Task<string> Token()
    {
        var token = await client.GetFromJsonAsync<TokenResponse>("/api/viewer/token");
        return token?.RequestToken ?? throw new InvalidOperationException("No antiforgery token was returned.");
    }

    async Task<JsonDocument> UploadAndReadJson(string fixture)
    {
        var token = await Token();
        using var content = UploadContent(fixture, File.OpenRead(Fixture(fixture)));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/viewer/open") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    static MultipartFormDataContent UploadContent(string fileName, string text) =>
        UploadContent(fileName, new MemoryStream(Encoding.UTF8.GetBytes(text)));

    static MultipartFormDataContent UploadContent(string fileName, Stream stream)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StreamContent(stream), "file", fileName);
        return content;
    }

    static async Task<string?> ProblemCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString();
    }

    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    sealed class TokenResponse
    {
        public string? RequestToken { get; set; }
    }

    sealed class ZeroStream(long length) : Stream
    {
        long position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => position; set => position = Math.Clamp(value, 0, length); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytes = (int)Math.Min(count, length - position);
            Array.Clear(buffer, offset, bytes);
            position += bytes;
            return bytes;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => position + offset,
                SeekOrigin.End => length + offset,
                _ => position,
            };
            return position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
