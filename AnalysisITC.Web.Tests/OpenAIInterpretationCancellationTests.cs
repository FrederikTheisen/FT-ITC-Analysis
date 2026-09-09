using System.Net;
using System.Text;
using System.Text.Json;
using AnalysisITC.Core.Interpretation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class OpenAIInterpretationCancellationTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-cancellation-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StalledBodyStopsAndLogsUnknownUsage(bool callerCancels)
    {
        using var body = new WaitingContent();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = body }))) { Timeout = Timeout.InfiniteTimeSpan };
        var settings = Settings(callerCancels ? 30 : 1);
        var store = Store(settings);
        var request = Request();
        var pending = new OpenAIInterpretationProvider(client, Options.Create(settings), store)
            .GenerateAsync(request, cancellation.Token);
        try
        {
            await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (callerCancels) cancellation.Cancel();

            var failure = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(callerCancels ? AnalysisInterpretationFailureKind.Cancelled : AnalysisInterpretationFailureKind.Timeout, failure.Kind);
            Assert.True(body.Stopped.Task.IsCompleted);
            var aggregate = store.Aggregate(request.ClientRequestId);
            Assert.Equal(1, aggregate.Attempts);
            Assert.Null(aggregate.Input);
            Assert.Null(aggregate.Output);
            Assert.Null(aggregate.Total);
            Assert.Null(aggregate.Cost);
            using var connection = store.OpenForCommand();
            using var query = connection.CreateCommand();
            query.CommandText = "SELECT outcome FROM attempts WHERE request_id=$id";
            query.Parameters.AddWithValue("$id", request.ClientRequestId);
            Assert.Equal(callerCancels ? "cancelled" : "timeout", query.ExecuteScalar());
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    [Fact]
    public async Task FallbackSharesTheOriginalGenerationDeadline()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            var attempt = Interlocked.Increment(ref calls);
            await Task.Delay(TimeSpan.FromMilliseconds(1250), token);
            return attempt == 1
                ? JsonResponse(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"retrieval_failed\"}}")
                : JsonResponse(HttpStatusCode.OK, "{\"status\":\"completed\",\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":\"Draft\"}]}]}");
        })) { Timeout = Timeout.InfiniteTimeSpan };
        var settings = Settings(2);
        settings.OpenAI.VectorStoreId = "vs_test";
        var store = Store(settings);
        var request = Request();

        var failure = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            new OpenAIInterpretationProvider(client, Options.Create(settings), store)
                .GenerateAsync(request, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(6)));

        Assert.Equal(AnalysisInterpretationFailureKind.Timeout, failure.Kind);
        Assert.Equal(2, calls);
        Assert.Equal(2, store.Aggregate(request.ClientRequestId).Attempts);
    }

    [Fact]
    public async Task CancellationAtRetryableFailureDoesNotStartFallback()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            Interlocked.Increment(ref calls);
            cancellation.Cancel();
            return Task.FromResult(JsonResponse(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"retrieval_failed\"}}"));
        }));
        var settings = Settings(30);
        settings.OpenAI.VectorStoreId = "vs_test";

        var failure = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            new OpenAIInterpretationProvider(client, Options.Create(settings)).GenerateAsync(Request(), cancellation.Token)
                .WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(AnalysisInterpretationFailureKind.Cancelled, failure.Kind);
        Assert.Equal(1, calls);
    }

    InterpretationOptions Settings(int timeoutSeconds) => new()
    {
        OpenAI = new OpenAIInterpretationOptions
        {
            Endpoint = "https://api.openai.test/v1/responses", ApiKey = "test-secret", Model = "test-model",
            TimeoutSeconds = timeoutSeconds,
        },
        UsageLog = new InterpretationUsageOptions { Enabled = true, DatabasePath = Path.Combine(directory, "usage.db") },
    };

    static InterpretationUsageStore Store(InterpretationOptions settings) =>
        new(Options.Create(settings), NullLogger<InterpretationUsageStore>.Instance);

    static AnalysisInterpretationGenerationRequest Request()
    {
        using var document = JsonDocument.Parse("{\"packageSchemaVersion\":\"2.0\",\"results\":[]}");
        var evidence = document.RootElement.Clone();
        return new AnalysisInterpretationGenerationRequest
        {
            ClientRequestId = Guid.NewGuid().ToString("N"), PackageJson = evidence,
            Prompt = ScientificGuidance.BuildPrompt("itc-interpretation-markdown-3.0", "Use Markdown.", evidence.GetRawText()),
        };
    }

    static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }

    sealed class WaitingContent : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { Stopped.TrySetResult(); }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
