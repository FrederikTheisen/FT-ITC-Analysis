using System.Text;
using AnalysisITC.Core.Interpretation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class InterpretationCancellationEndpointTests
{
    [Fact]
    public async Task CancellingHttpRequestReachesProviderThroughRelay()
    {
        using var factory = new CancellationApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        using var cancellation = new CancellationTokenSource();
        using var content = new StringContent("""
            {
              "requestSchemaVersion": "ft-itc-relay-request-3.0",
              "outputInstructions": "Use Markdown.",
              "outputFormatVersion": "itc-interpretation-markdown-3.0",
              "generationProfile": "fast",
              "clientRequestId": "0123456789abcdef0123456789abcdef",
              "package": { "packageSchemaVersion": "2.0", "results": [] }
            }
            """, Encoding.UTF8, "application/json");
        var pending = client.PostAsync("/api/interpretation/generate", content, cancellation.Token);
        try
        {
            await factory.Provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();

            await factory.Provider.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, factory.Provider.CallCount);
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    sealed class CancellationApplicationFactory : WebApplicationFactory<Program>
    {
        readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-cancellation-" + Guid.NewGuid().ToString("N"));
        public BlockingProvider Provider { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Interpretation:Enabled"] = "true",
                    ["Interpretation:UsageLog:Enabled"] = "true",
                    ["Interpretation:UsageLog:DatabasePath"] = Path.Combine(directory, "usage.db"),
                    ["Interpretation:OpenAI:ApiKey"] = "",
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAnalysisInterpretationProvider>();
                services.AddSingleton<IAnalysisInterpretationProvider>(Provider);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    sealed class BlockingProvider : IAnalysisInterpretationProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }

        public async Task<AnalysisInterpretationProviderResponse> GenerateAsync(
            AnalysisInterpretationGenerationRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("The test provider must be cancelled.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
    }
}
