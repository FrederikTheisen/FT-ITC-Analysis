using AnalysisITC.Core.Interpretation;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

// Exercises the real provider after the admission normally performed by Program.
// It deliberately leaves finalization to tests that exercise the endpoint/store.
internal sealed class AdmittedProviderFixture(HttpClient client, InterpretationOptions options,
    InterpretationUsageStore store) : IAnalysisInterpretationProvider
{
    public Task<AnalysisInterpretationProviderResponse> GenerateAsync(
        AnalysisInterpretationGenerationRequest request, CancellationToken cancellationToken)
    {
        Assert.Equal(InterpretationAdmissionStatus.Admitted, store.TryAdmit(new InterpretationUsageRequest
        {
            ServerExecutionId = request.ServerExecutionId, ClientRequestId = request.ClientRequestId,
            RequestId = request.ServerExecutionId, OperatorCodeId = request.OperatorCodeId,
            TaskType = request.TaskType, EffectivePreset = request.RequestedPreset,
            StartedUtc = DateTime.UtcNow, TraceId = "provider-test",
        }, null, DateTime.MinValue).Status);
        return new OpenAIInterpretationProvider(client, Options.Create(options), store).GenerateAsync(request, cancellationToken);
    }
}
