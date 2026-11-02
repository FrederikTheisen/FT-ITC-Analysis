using System.Net;
using System.Text;
using AnalysisITC.Web;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class ProviderApiAccessCheckTests
{
    const string ModelList = "{\"object\":\"list\",\"data\":[]}";

    [Fact]
    public async Task BothProvidersUseAuthenticatedReadOnlyRequestsOnTheirConfiguredOrigins()
    {
        var options = Configured();
        options.OpenAI.Endpoint = "https://openai.example/gateway/v1/responses";
        options.Mistral.Endpoint = "https://api.eu.mistral.ai/region/v1/chat/completions";
        var calls = new List<string>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls.Add(request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(request.RequestUri.Host == "openai.example" ? "test-openai" : "test-mistral",
                request.Headers.Authorization.Parameter);
            Assert.True(request.Headers.CacheControl!.NoCache);
            Assert.True(request.Headers.CacheControl.NoStore);
            return Task.FromResult(Response());
        }));

        var result = await ProviderApiAccessCheck.RunAsync(options, http);

        Assert.Equal("Provider API access", result.Name);
        Assert.All(result.Checks, check => Assert.Equal(HealthCheckState.Pass, check.State));
        Assert.Equal(new[] { "https://openai.example/gateway/v1/models", "https://api.eu.mistral.ai/region/v1/models" }, calls);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task ProviderHttpFailuresCannotPassOrSuppressTheOtherProvidersResult(int status)
    {
        using var http = Client(request => request.RequestUri!.Host == "api.eu.mistral.ai"
            ? Response("private provider detail", (HttpStatusCode)status) : Response());

        var result = await ProviderApiAccessCheck.RunAsync(Configured(), http);

        Assert.Equal(HealthCheckState.Pass, Assert.Single(result.Checks, check => check.Id == "provider.openai").State);
        var failed = Assert.Single(result.Checks, check => check.Id == "provider.mistral");
        Assert.Equal(HealthCheckState.Fail, failed.State);
        Assert.Contains("HTTP " + status, failed.Reason);
        Assert.DoesNotContain("private", failed.Reason);
    }

    [Theory]
    [InlineData("<html>private error</html>")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"object\":\"list\",\"data\":{}}")]
    [InlineData("{\"object\":\"private\",\"data\":[]}")]
    public async Task HttpSuccessRequiresAnActualModelListResponse(string body)
    {
        using var http = Client(_ => Response(body));

        var result = await ProviderApiAccessCheck.RunAsync(Configured(), http);

        Assert.All(result.Checks, check =>
        {
            Assert.Equal(HealthCheckState.Fail, check.State);
            Assert.DoesNotContain("private", check.Reason);
        });
    }

    [Fact]
    public async Task ExplicitlyUnconfiguredProvidersAreSkippedWithoutAnHttpRequest()
    {
        var options = Configured();
        options.Enabled = false;
        using var http = Client(_ => throw new InvalidOperationException("Unexpected provider request."));

        var result = await ProviderApiAccessCheck.RunAsync(options, http);

        Assert.All(result.Checks, check => Assert.Equal(HealthCheckState.Skipped, check.State));
        options.Enabled = true;
        options.Mistral.Enabled = false;
        using var openAiOnly = Client(request =>
        {
            Assert.Equal("api.openai.com", request.RequestUri!.Host);
            return Response();
        });
        result = await ProviderApiAccessCheck.RunAsync(options, openAiOnly);
        Assert.Equal(HealthCheckState.Pass, Assert.Single(result.Checks, check => check.Id == "provider.openai").State);
        Assert.Equal(HealthCheckState.Skipped, Assert.Single(result.Checks, check => check.Id == "provider.mistral").State);
    }

    [Theory]
    [InlineData("http://insecure.example/v1/chat/completions")]
    [InlineData("https://user:private@api.eu.mistral.ai/v1/chat/completions")]
    [InlineData("https://api.eu.mistral.ai/unknown")]
    [InlineData("https://api.eu.mistral.ai/v1/chat/completions?private=1")]
    public async Task UnverifiableConfigurationCannotPassOrDispatchToAnotherEndpoint(string endpoint)
    {
        var options = Configured();
        options.Mistral.Endpoint = endpoint;
        using var http = Client(request =>
        {
            Assert.Equal("api.openai.com", request.RequestUri!.Host);
            return Response();
        });

        var result = await ProviderApiAccessCheck.RunAsync(options, http);

        var failed = Assert.Single(result.Checks, check => check.Id == "provider.mistral");
        Assert.Equal(HealthCheckState.Fail, failed.State);
        Assert.DoesNotContain("private", failed.Reason);
    }

    [Fact]
    public async Task MissingEnabledProviderCredentialsCannotPass()
    {
        var options = Configured();
        options.OpenAI.ApiKey = "";
        options.Mistral.ApiKey = "";
        var calls = 0;
        using var http = Client(_ => { calls++; return Response(); });

        Assert.All((await ProviderApiAccessCheck.RunAsync(options, http)).Checks,
            check => Assert.Equal(HealthCheckState.Fail, check.State));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task InvalidCredentialHeaderCannotHideTheOtherProvidersResult()
    {
        var options = Configured();
        options.Mistral.ApiKey = "private\r\ninvalid";
        using var http = Client(_ => Response());

        var result = await ProviderApiAccessCheck.RunAsync(options, http);

        Assert.Equal(HealthCheckState.Pass, Assert.Single(result.Checks, check => check.Id == "provider.openai").State);
        var failed = Assert.Single(result.Checks, check => check.Id == "provider.mistral");
        Assert.Equal(HealthCheckState.Fail, failed.State);
        Assert.DoesNotContain("private", failed.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportFailureAndTimeoutCannotPassOrExposeConnectionDetails(bool timeout)
    {
        using var http = Client(_ =>
        {
            if (timeout) throw new TaskCanceledException("private timeout");
            throw new HttpRequestException("private network failure");
        });

        var result = await ProviderApiAccessCheck.RunAsync(Configured(), http);

        Assert.All(result.Checks, check =>
        {
            Assert.Equal(HealthCheckState.Fail, check.State);
            Assert.DoesNotContain("private", check.Reason);
        });
    }

    [Fact]
    public async Task CachedOrOversizedResponsesCannotEstablishCurrentApiAccess()
    {
        using var cached = Client(_ =>
        {
            var response = Response();
            response.Headers.Age = TimeSpan.Zero;
            return response;
        });
        Assert.All((await ProviderApiAccessCheck.RunAsync(Configured(), cached)).Checks,
            check => Assert.Equal(HealthCheckState.Fail, check.State));
        using var oversized = Client(_ => Response("{\"object\":\"list\",\"data\":[],\"extra\":\"" + new string('x', 1024 * 1024) + "\"}"));
        Assert.All((await ProviderApiAccessCheck.RunAsync(Configured(), oversized)).Checks,
            check => Assert.Equal(HealthCheckState.Fail, check.State));
    }

    static InterpretationOptions Configured() => new()
    {
        Enabled = true,
        OpenAI = new() { ApiKey = "test-openai", Model = "test-model" },
        Mistral = new() { Enabled = true, ApiKey = "test-mistral" },
    };

    static HttpResponseMessage Response(string body = ModelList, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(new Handler((request, _) => Task.FromResult(response(request))));

    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            response(request, cancellationToken);
    }
}
