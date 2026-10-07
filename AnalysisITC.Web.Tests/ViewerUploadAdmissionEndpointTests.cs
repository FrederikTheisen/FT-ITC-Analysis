using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AnalysisITC.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class ViewerUploadAdmissionEndpointTests
{
    [Theory]
    [InlineData("/api/viewer/open")]
    [InlineData("/api/viewer/open/")]
    [InlineData("/API/Viewer/Open")]
    [InlineData("/API/Viewer/Open/")]
    public async Task EveryAcceptedRouteSpellingSharesAdmissionAndCanOpenProjects(string path)
    {
        using var factory = new UploadFactory(new() { ["ViewerUpload:QueueLimit"] = "0" });
        using var client = CreateClient(factory);
        var token = await Token(client);
        var admission = factory.Services.GetRequiredService<ViewerUploadAdmission>();

        using (var held = await admission.AcquireAsync())
        {
            Assert.True(held.IsAcquired);
            using var response = await Upload(client, path, token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("viewer_busy", problem.GetProperty("code").GetString());
        }

        using var opened = await Upload(client, path, token);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var document = await opened.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ftxtc", document.GetProperty("format").GetString());
        Assert.NotEmpty(document.GetProperty("experiments").EnumerateArray());
    }

    [Theory]
    [InlineData("/api/viewer/token")]
    [InlineData("/api/interpretation/status")]
    [InlineData("/")]
    [InlineData("/app.js")]
    public async Task OtherEndpointsRemainAvailableWhileAdmissionIsSaturated(string path)
    {
        using var factory = new UploadFactory(new() { ["ViewerUpload:QueueLimit"] = "0" });
        using var client = CreateClient(factory);
        using var held = await factory.Services.GetRequiredService<ViewerUploadAdmission>().AcquireAsync();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/viewer/open")]
    [InlineData("HEAD", "/api/viewer/open/")]
    [InlineData("PUT", "/api/viewer/open")]
    [InlineData("DELETE", "/api/viewer/open/")]
    [InlineData("POST", "/api/viewer/not-an-upload")]
    public async Task NonUploadRequestsRetainNormalRoutingWhileAdmissionIsSaturated(string method, string path)
    {
        using var factory = new UploadFactory(new() { ["ViewerUpload:QueueLimit"] = "0" });
        using var client = CreateClient(factory);
        using var baselineRequest = new HttpRequestMessage(new HttpMethod(method), path);
        using var baseline = await client.SendAsync(baselineRequest);
        using var held = await factory.Services.GetRequiredService<ViewerUploadAdmission>().AcquireAsync();
        using var saturatedRequest = new HttpRequestMessage(new HttpMethod(method), path);

        using var saturated = await client.SendAsync(saturatedRequest);

        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, saturated.StatusCode);
        Assert.Equal(baseline.StatusCode, saturated.StatusCode);
        Assert.Equal(baseline.Content.Headers.ContentType, saturated.Content.Headers.ContentType);
    }

    [Fact]
    public async Task AntiforgeryRejectionReleasesAdmissionForTheNextValidUpload()
    {
        using var factory = new UploadFactory(new() { ["ViewerUpload:QueueLimit"] = "0" });
        using var client = CreateClient(factory);
        using var rejected = await Upload(client, "/api/viewer/open/", token: null);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("antiforgery_validation_failed", problem.GetProperty("code").GetString());

        using var opened = await Upload(client, "/api/viewer/open", await Token(client));

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public void ShippedAndTypeDefaultsAgreeOnOneActiveFiveWaitingAndThirtySeconds()
    {
        using var factory = new UploadFactory();
        var configured = factory.Services.GetRequiredService<IOptions<ViewerUploadOptions>>().Value;
        AssertDefaults(new ViewerUploadOptions());
        AssertDefaults(configured);

        static void AssertDefaults(ViewerUploadOptions options)
        {
            Assert.Equal(1, options.ActiveUploads);
            Assert.Equal(5, options.QueueLimit);
            Assert.Equal(30, options.QueueWaitTimeoutSeconds);
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 7)]
    public void QueueAndWaitTimeoutCanBeConfigured(int queue, int timeout)
    {
        using var factory = new UploadFactory(new()
        {
            ["ViewerUpload:QueueLimit"] = queue.ToString(),
            ["ViewerUpload:QueueWaitTimeoutSeconds"] = timeout.ToString(),
        });

        var options = factory.Services.GetRequiredService<IOptions<ViewerUploadOptions>>().Value;

        Assert.Equal(1, options.ActiveUploads);
        Assert.Equal(queue, options.QueueLimit);
        Assert.Equal(timeout, options.QueueWaitTimeoutSeconds);
    }

    [Theory]
    [InlineData("ActiveUploads", "0")]
    [InlineData("ActiveUploads", "2")]
    [InlineData("QueueLimit", "-1")]
    [InlineData("QueueWaitTimeoutSeconds", "0")]
    [InlineData("QueueWaitTimeoutSeconds", "-1")]
    [InlineData("QueueWaitTimeoutSeconds", "4294968")]
    public void InvalidAdmissionSettingsFailAtStartup(string setting, string value)
    {
        using var factory = new UploadFactory(new() { [$"ViewerUpload:{setting}"] = value });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(exception.Failures, failure => failure.Contains($"ViewerUpload.{setting}", StringComparison.Ordinal));
    }

    static HttpClient CreateClient(WebApplicationFactory<Program> factory) => factory.CreateClient(new()
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false,
    });

    static async Task<string> Token(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/viewer/token");
        return token.GetProperty("requestToken").GetString()!;
    }

    static async Task<HttpResponseMessage> Upload(HttpClient client, string path, string? token)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "jors.ftxtc"))), "file", "jors.ftxtc");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    sealed class UploadFactory(Dictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            if (settings is not null)
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        }
    }
}
