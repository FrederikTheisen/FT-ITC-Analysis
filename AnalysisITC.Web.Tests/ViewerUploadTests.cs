using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class ViewerUploadTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly HttpClient client;

    public ViewerUploadTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
    }

    [Fact]
    public async Task ViewerShellUsesListsAndExclusiveProcessingModes()
    {
        var html = await client.GetStringAsync("/");
        var script = await client.GetStringAsync("/app.js");

        Assert.Contains("id=\"experiment-list\"", html);
        Assert.Contains("id=\"result-list\"", html);
        Assert.DoesNotContain("id=\"experiment-select\"", html);
        Assert.DoesNotContain("id=\"result-select\"", html);
        Assert.Contains("id=\"processed-mode-raw\"", html);
        Assert.Contains("id=\"processed-mode-corrected\"", html);
        Assert.Contains("[\"metadata\", \"raw\", \"processed\", \"fit\"]", script);
        Assert.DoesNotContain("renderIntegrated", script);
        Assert.Contains("Baseline = 0", script);
        Assert.Contains("useGrouping: false", script);
        Assert.DoesNotContain("toLocaleString", script);
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
    public async Task OpensRepresentativeFilesAndReturnsGraphArrays(string fixture, string expectedFormat, int experimentCount)
    {
        var token = await Token();
        using var content = UploadContent(fixture, File.OpenRead(Fixture(fixture)));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/viewer/open") { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedFormat, json.RootElement.GetProperty("format").GetString());
        var experiments = json.RootElement.GetProperty("experiments");
        Assert.Equal(experimentCount, experiments.GetArrayLength());
        Assert.True(experiments[0].GetProperty("raw").GetProperty("timeSeconds").GetArrayLength() > 100);
        if (expectedFormat == "ftitc")
        {
            Assert.True(experiments[0].GetProperty("integrated").GetProperty("correctedHeatMicrojoules").GetArrayLength() > 0);
            Assert.True(experiments[0].GetProperty("processed").GetProperty("correctedPowerMicrowatts").GetArrayLength() > 0);
            Assert.True(experiments[0].GetProperty("fits").GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task UploadReturnsCompleteResolvableAnalysisResultReferences()
    {
        using var json = await UploadAndReadJson("jors.ftitc");
        var root = json.RootElement;
        var experiments = root.GetProperty("experiments").EnumerateArray().ToArray();
        var results = root.GetProperty("analysisResults").EnumerateArray().ToArray();

        Assert.Equal(3, results.Length);
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
    }

    [Theory]
    [InlineData("data_1.itc", false)]
    [InlineData("temperature-series.ftitc", true)]
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
    [InlineData("sample.ftitc", "$ITC\n", HttpStatusCode.BadRequest, "format_mismatch")]
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
