using AnalysisITC.Core.Interpretation;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        CancellationToken cancellationToken,
        string? serverExecutionId = null)
    {
        if (provider is null)
            throw new InvalidOperationException("No interpretation provider is configured.");

        var prompt = request.TaskType == "summary"
            ? SummaryGuidance.BuildPrompt(request.OutputFormatVersion, request.OutputInstructions,
                request.PackageJson.GetRawText(), request.ClientRequestId)
            : ScientificGuidance.BuildPrompt(request, selection.GuidanceVariant);
        AnalysisInterpretationProviderResponse response;
        response = await provider.GenerateAsync(new AnalysisInterpretationGenerationRequest
        {
            ClientRequestId = request.ClientRequestId,
            TaskType = request.TaskType,
            GenerationProfile = request.GenerationProfile,
            Package = null,
            PackageJson = request.PackageJson,
            Prompt = prompt,
            RequestedModel = selection.Model,
            RequestedReasoningEffort = selection.ReasoningEffort,
            RequestedGuidanceVariant = selection.GuidanceVariant,
            OperatorCodeId = selection.OperatorCodeId,
            ServerExecutionId = serverExecutionId,
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
            selection.ResponseSchemaVersion,
            selection.ResponseSchemaVersion == FtItcInterpretationClient.ResponseSchemaVersion ? selection.TaskType : null,
            request.ClientRequestId,
            response.Provider,
            response.Model,
            response.ReasoningEffort,
            generatedAtUtc,
            markdown,
            response.EffectiveInputFingerprint ?? prompt.InputFingerprint,
            response.Omissions ?? new List<string>(),
            response.KnowledgeBaseIds ?? new List<string>(), response.RetrievedSourceIds ?? new List<string>(),
            prompt.PromptVersion,
            selection.ResponseSchemaVersion == FtItcInterpretationClient.ResponseSchemaVersion
                && selection.TaskType != "summary" ? selection.GuidanceVariant : null,
            response.ScientificInstructionsFingerprint ?? ScientificGuidance.Hash(prompt.SystemInstructions),
            response.OutputInstructionsFingerprint ?? prompt.OutputInstructionsFingerprint,
            request.OutputFormatVersion,
            selection.EffectivePreset,
            selection.PresetRevision);
    }

}

public sealed record InterpretationRelayResponse(
    string ResponseSchemaVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TaskType,
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ScientificGuidanceVariant,
    string ScientificInstructionsFingerprint,
    string OutputInstructionsFingerprint,
    string OutputFormatVersion,
    string EffectivePreset,
    string PresetRevision);

public sealed class InterpretationProviderResponseException : Exception
{
    public InterpretationProviderResponseException()
        : base("The interpretation provider returned an invalid response.")
    {
    }
}
