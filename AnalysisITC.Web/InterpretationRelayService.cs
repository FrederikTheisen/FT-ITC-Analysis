using AnalysisITC.Core.Interpretation;
using System.Text.Json;

namespace AnalysisITC.Web;

public sealed class InterpretationRelayService
{
    readonly IAnalysisInterpretationProvider? provider;

    public InterpretationRelayService(IEnumerable<IAnalysisInterpretationProvider> providers)
    {
        provider = providers.SingleOrDefault();
    }

    public bool IsConfigured => provider is not null;

    public async Task<InterpretationRelayResponse> GenerateAsync(
        ValidatedInterpretationRequest request,
        InterpretationGenerationSelection selection,
        CancellationToken cancellationToken)
    {
        if (provider is null)
            throw new InvalidOperationException("No interpretation provider is configured.");

        var prompt = ScientificGuidance.BuildPrompt(request);
        AnalysisInterpretationProviderResponse response;
        response = await provider.GenerateAsync(new AnalysisInterpretationGenerationRequest
        {
            ClientRequestId = request.ClientRequestId,
            GenerationProfile = request.GenerationProfile,
            Package = null,
            PackageJson = request.PackageJson,
            Prompt = prompt,
            RequestedModel = selection.Model,
            RequestedReasoningEffort = selection.ReasoningEffort,
            OperatorCodeId = selection.OperatorCodeId,
        }, cancellationToken);

        if (response is null
            || string.IsNullOrWhiteSpace(response.RequestId)
            || string.IsNullOrWhiteSpace(response.Provider)
            || string.IsNullOrWhiteSpace(response.Model)
            || response.GeneratedAtUtc == default
            || string.IsNullOrWhiteSpace(response.InterpretationMarkdown))
        {
            throw new InterpretationProviderResponseException();
        }

        var markdown = AnalysisInterpretationResponseParser.Parse(response.InterpretationMarkdown);

        var generatedAtUtc = response.GeneratedAtUtc.Kind switch
        {
            DateTimeKind.Utc => response.GeneratedAtUtc,
            DateTimeKind.Local => response.GeneratedAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(response.GeneratedAtUtc, DateTimeKind.Utc),
        };

        return new InterpretationRelayResponse(
            FtItcInterpretationClient.ResponseSchemaVersion,
            request.ClientRequestId,
            response.Provider,
            response.Model,
            response.ReasoningEffort,
            generatedAtUtc,
            markdown,
            response.EffectiveInputFingerprint ?? prompt.InputFingerprint,
            response.Omissions ?? new List<string>(),
            response.KnowledgeBaseIds ?? new List<string>(), response.RetrievedSourceIds ?? new List<string>(),
            response.ScientificGuidanceRevision ?? ScientificGuidance.Revision,
            response.ScientificInstructionsFingerprint ?? ScientificGuidance.Hash(prompt.SystemInstructions),
            response.OutputInstructionsFingerprint ?? prompt.OutputInstructionsFingerprint,
            request.OutputFormatVersion);
    }

}

public sealed record InterpretationRelayResponse(
    string ResponseSchemaVersion,
    string RequestId,
    string Provider,
    string Model,
    string ReasoningEffort,
    DateTime GeneratedAtUtc,
    string InterpretationMarkdown,
    string EffectiveInputFingerprint,
    IReadOnlyList<string> Omissions,
    IReadOnlyList<string> KnowledgeBaseIds,
    IReadOnlyList<string> RetrievedSourceIds,
    string ScientificGuidanceRevision,
    string ScientificInstructionsFingerprint,
    string OutputInstructionsFingerprint,
    string OutputFormatVersion);

public sealed class InterpretationProviderResponseException : Exception
{
    public InterpretationProviderResponseException()
        : base("The interpretation provider returned an invalid response.")
    {
    }
}
