using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Interpretation
{
    public interface IAnalysisInterpretationProvider
    {
        Task<AnalysisInterpretationProviderResponse> GenerateAsync(
            AnalysisInterpretationGenerationRequest request,
            CancellationToken cancellationToken);
    }

    public enum AnalysisInterpretationProgressStage
    {
        BuildingPackage,
        SendingRequest,
        WaitingForServer,
        ValidatingResponse,
        Finished,
    }

    public sealed class AnalysisInterpretationProgressUpdate
    {
        public AnalysisInterpretationProgressStage Stage { get; internal set; }
        public string Message { get; internal set; }
        public bool? Succeeded { get; internal set; }
    }

    public sealed class AnalysisInterpretationGenerationRequest
    {
        public string TaskType { get; set; } = "interpretation";
        public string ClientRequestId { get; set; }
        public string GenerationProfile { get; set; } = "instant";
        /// <summary>
        /// Optional preset chosen for this generation. When present it takes
        /// precedence over the persisted Preferences preset after the relay
        /// revalidates access. This remains separate from GenerationProfile so
        /// callers can distinguish an explicit choice from the legacy default.
        /// </summary>
        public string RequestedPreset { get; set; }
        public AnalysisInterpretationPackage Package { get; set; }
        public System.Text.Json.JsonElement? PackageJson { get; set; }
        public AnalysisInterpretationPrompt Prompt { get; set; }
        public string OperatorCode { get; set; }
        public string RequestedModel { get; set; }
        public string RequestedReasoningEffort { get; set; }
        public string RequestedGuidanceVariant { get; set; }
        public string OperatorCodeId { get; set; }
        // Kept on the provider-neutral request so a future streaming provider can
        // publish server-side progress without changing the service or UI contract.
        public IProgress<AnalysisInterpretationProgressUpdate> Progress { get; set; }
    }

    /// <summary>
    /// A one-generation interpretation setting selected in the report dialog.
    /// Preset selections set <see cref="PresetId"/>; administrator custom
    /// selections set both <see cref="Model"/> and
    /// <see cref="ReasoningEffort"/>.
    /// </summary>
    public sealed class AnalysisInterpretationGenerationSelection
    {
        public string TaskType { get; set; } = "interpretation";
        public string PresetId { get; set; }
        public string Model { get; set; }
        public string ReasoningEffort { get; set; }

        public bool IsSummary => string.Equals(TaskType, "summary", StringComparison.Ordinal);
        public bool IsCustom => !IsSummary && (!string.IsNullOrWhiteSpace(Model)
            || !string.IsNullOrWhiteSpace(ReasoningEffort));
    }

    public sealed class AnalysisInterpretationProviderResponse
    {
        public string TaskType { get; set; } = "interpretation";
        public string RequestId { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public string ReasoningEffort { get; set; }
        public string EffectivePreset { get; set; }
        public string PresetRevision { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
        public string EffectiveInputFingerprint { get; set; }
        public List<string> Omissions { get; set; } = new List<string>();
        public List<string> KnowledgeBaseIds { get; set; } = new List<string>();
        public List<string> RetrievedSourceIds { get; set; } = new List<string>();
        public string ScientificGuidanceRevision { get; set; }
        public string ScientificGuidanceVariant { get; set; }
        public string ScientificInstructionsFingerprint { get; set; }
        public string OutputInstructionsFingerprint { get; set; }
        public string InterpretationMarkdown { get; set; }
        public int ProviderAttempts { get; set; }
        public int? InputTokens { get; set; }
        public int? CachedInputTokens { get; set; }
        public int? CacheWriteTokens { get; set; }
        public int? OutputTokens { get; set; }
        public int? ReasoningTokens { get; set; }
        public int? TotalTokens { get; set; }
        public decimal? EstimatedCost { get; set; }
    }

    public sealed class AnalysisInterpretationGenerationResult
    {
        public AnalysisInterpretationPackage Package { get; internal set; }
        public AnalysisInterpretationPrompt Prompt { get; internal set; }
        public AnalysisInterpretationRecord Interpretation { get; internal set; }
    }

    public enum AnalysisInterpretationFreshness { Current, Stale, Unverifiable }

    public sealed class AnalysisInterpretationFreshnessResult
    {
        public AnalysisInterpretationFreshness Status { get; internal set; }
        public string Reason { get; internal set; }
        public string CurrentFingerprint { get; internal set; }
    }

    public sealed class AnalysisInterpretationService
    {
        readonly IAnalysisInterpretationProvider provider;

        public AnalysisInterpretationService(IAnalysisInterpretationProvider provider)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public Task<AnalysisInterpretationGenerationResult> GenerateAsync(
            AnalysisReport report, AnalysisResult result, AnalysisInterpretationOptions options = null,
            CancellationToken cancellationToken = default, IProgress<AnalysisInterpretationProgressUpdate> progress = null,
            AnalysisInterpretationGenerationSelection generationSelection = null) =>
            GenerateAsync(report, id => result?.UniqueID == id ? result : null, _ => null, options, cancellationToken, progress, generationSelection);

        public async Task<AnalysisInterpretationGenerationResult> GenerateAsync(
            AnalysisReport report, Func<string, AnalysisResult> resultResolver,
            Func<string, ExperimentData> experimentResolver, AnalysisInterpretationOptions options = null,
            CancellationToken cancellationToken = default, IProgress<AnalysisInterpretationProgressUpdate> progress = null,
            AnalysisInterpretationGenerationSelection generationSelection = null)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            AnalysisInterpretationLog.Write("generation-start", requestId, "");
            try
            {
                Report(progress, AnalysisInterpretationProgressStage.BuildingPackage, "Building the analysis package…");
                cancellationToken.ThrowIfCancellationRequested();
                var package = AnalysisInterpretationPackageBuilder.Build(report, resultResolver, experimentResolver, options);
                var taskType = generationSelection?.IsSummary == true ? "summary" : "interpretation";
                var prompt = AnalysisInterpretationPromptBuilder.Build(package, requestId, taskType);
                var request = new AnalysisInterpretationGenerationRequest
                {
                    ClientRequestId = requestId,
                    TaskType = taskType,
                    GenerationProfile = taskType == "summary" ? "summary" : generationSelection?.IsCustom == true
                        ? "custom"
                        : generationSelection?.PresetId ?? "instant",
                    RequestedPreset = taskType == "summary" ? "summary" : generationSelection?.IsCustom == true ? null : generationSelection?.PresetId,
                    RequestedModel = generationSelection?.Model,
                    RequestedReasoningEffort = generationSelection?.ReasoningEffort,
                    Package = package,
                    Prompt = prompt,
                    Progress = progress,
                };
                Report(progress, AnalysisInterpretationProgressStage.SendingRequest, "Sending the request…");
                var responseTask = provider.GenerateAsync(request, cancellationToken);
                Report(progress, AnalysisInterpretationProgressStage.WaitingForServer, "Waiting for the server…");
                var response = await responseTask.ConfigureAwait(false);
                // A provider may complete successfully after cancellation was requested.
                // Do not let that late response become a publishable draft.
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, AnalysisInterpretationProgressStage.ValidatingResponse, "Validating the response…");
                if (response == null || string.IsNullOrWhiteSpace(response.InterpretationMarkdown))
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse, "The provider returned no interpretation text.");
                var markdown = AnalysisInterpretationResponseParser.Parse(response.InterpretationMarkdown, package);
                var generated = response.GeneratedAtUtc == default(DateTime) ? DateTime.UtcNow : response.GeneratedAtUtc;
                if (generated.Kind != DateTimeKind.Utc) generated = generated.ToUniversalTime();
                var value = new AnalysisInterpretationGenerationResult
                {
                    Package = package,
                    Prompt = prompt,
                    Interpretation = new AnalysisInterpretationRecord
                    {
                        Origin = AnalysisInterpretationOrigin.AiGenerated,
                        TaskType = response.TaskType ?? taskType,
                        InterpretationMarkdown = markdown,
                        InputFingerprint = prompt.InputFingerprint,
                        EffectiveInputFingerprint = response.EffectiveInputFingerprint ?? prompt.InputFingerprint,
                        Omissions = package.Omissions.Concat(response.Omissions ?? new List<string>()).Distinct().ToList(),
                        KnowledgeBaseIds = response.KnowledgeBaseIds ?? new List<string>(),
                        RetrievedSourceIds = response.RetrievedSourceIds ?? new List<string>(),
                        PromptVersion = prompt.PromptVersion,
                        OutputFormatVersion = prompt.OutputFormatVersion,
                        EvidenceFingerprintScheme = AnalysisInterpretationPromptBuilder.EvidenceFingerprintScheme,
                        ScientificGuidanceRevision = response.ScientificGuidanceRevision ?? "",
                        ScientificGuidanceVariant = response.ScientificGuidanceVariant ?? "",
                        ScientificInstructionsFingerprint = response.ScientificInstructionsFingerprint ?? "",
                        OutputInstructionsFingerprint = response.OutputInstructionsFingerprint ?? prompt.OutputInstructionsFingerprint,
                        Provider = response.Provider ?? "",
                        Model = response.Model ?? "",
                        ReasoningEffort = response.ReasoningEffort ?? "",
                        EffectivePreset = response.EffectivePreset ?? "",
                        PresetRevision = response.PresetRevision ?? "",
                        ServiceRequestId = response.RequestId ?? request.ClientRequestId,
                        GeneratedAtUtc = generated,
                    },
                };
                Report(progress, AnalysisInterpretationProgressStage.Finished,
                    taskType == "summary" ? "Finished — summary ready." : "Finished — interpretation ready.", true);
                AnalysisInterpretationLog.Write("generation-ready", requestId, $"characters={markdown.Length} elapsedMs={timer.ElapsedMilliseconds}");
                return value;
            }
            catch (Exception ex)
            {
                AnalysisInterpretationLog.Write("generation-failed", requestId,
                    $"exception={ex.GetType().Name} kind={(ex is AnalysisInterpretationProviderException failure ? failure.Kind.ToString() : "local")} elapsedMs={timer.ElapsedMilliseconds} stack={AnalysisInterpretationLog.Token(ex.TargetSite?.Name)}");
                var cancelled = ex is OperationCanceledException
                    || ex is AnalysisInterpretationProviderException providerException
                    && providerException.Kind == AnalysisInterpretationFailureKind.Cancelled;
                Report(progress, AnalysisInterpretationProgressStage.Finished,
                    cancelled ? "Finished — generation cancelled." : "Finished — generation failed.", false);
                throw;
            }
        }

        public static IReadOnlyList<string> GetGenerationWarnings(AnalysisReport report,
            Func<string, AnalysisResult> resultResolver, Func<string, ExperimentData> experimentResolver)
        {
            var warnings = new List<string>();
            foreach (var id in report.ResultIds)
            {
                var result = resultResolver(id);
                if (result == null) { warnings.Add("Missing analysis result: " + id); continue; }
                if (!result.ValidityReport.IsValid)
                    warnings.Add(result.Name + ": " + string.Join("; ", result.ValidityReport.Reasons));
                var runs = new[] { (Label: result.Name, Run: result.Solution?.Convergence) }
                    .Concat((result.Solution?.Solutions ?? new List<AnalysisITC.Core.Analysis.Models.SolutionInterface>())
                        .Select(member => (Label: result.Name + "/" + member.Data?.Name, Run: member.Convergence)));
                foreach (var run in runs)
                {
                    var convergence = run.Run;
                    if (convergence == null) { warnings.Add(run.Label + ": optimizer termination metadata is unavailable."); continue; }
                    if (!convergence.Success) warnings.Add(run.Label + ": optimizer — " + convergence.Termination + ". " + convergence.FailureReason);
                    var outcome = convergence.ErrorEstimationOutcome.ToString();
                    if (outcome == "PartialFailure" || outcome == "CompleteFailure" || outcome == "Cancelled")
                        warnings.Add(run.Label + ": uncertainty — " + outcome + ". " + convergence.ErrorEstimationSummary);
                }
            }
            return warnings;
        }

        static void Report(
            IProgress<AnalysisInterpretationProgressUpdate> progress,
            AnalysisInterpretationProgressStage stage,
            string message,
            bool? succeeded = null) =>
            progress?.Report(new AnalysisInterpretationProgressUpdate
            { Stage = stage, Message = message, Succeeded = succeeded });

        public static AnalysisInterpretationFreshnessResult EvaluateFreshness(AnalysisReport report, AnalysisResult result) =>
            EvaluateFreshness(report, id => result?.UniqueID == id ? result : null, _ => null);

        public static AnalysisInterpretationFreshnessResult EvaluateFreshness(
            AnalysisReport report, Func<string, AnalysisResult> resultResolver, Func<string, ExperimentData> experimentResolver)
        {
            if (report?.ApprovedInterpretation == null)
                return new AnalysisInterpretationFreshnessResult { Status = AnalysisInterpretationFreshness.Unverifiable, Reason = "No approved interpretation is stored." };
            if (report.ApprovedInterpretation.Origin == AnalysisInterpretationOrigin.Manual)
                return new AnalysisInterpretationFreshnessResult { Status = AnalysisInterpretationFreshness.Current, Reason = "The interpretation was written by the user." };
            var record = report.ApprovedInterpretation;
            if (!string.Equals(record.EvidenceFingerprintScheme, AnalysisInterpretationPromptBuilder.EvidenceFingerprintScheme, StringComparison.Ordinal))
                return new AnalysisInterpretationFreshnessResult { Status = AnalysisInterpretationFreshness.Unverifiable, Reason = "The approved interpretation has no verifiable evidence-fingerprint scheme." };
            try
            {
                var fingerprint = AnalysisInterpretationPromptBuilder.Build(
                    AnalysisInterpretationPackageBuilder.Build(report, resultResolver, experimentResolver)).InputFingerprint;
                return new AnalysisInterpretationFreshnessResult
                {
                    Status = string.Equals(record.InputFingerprint, fingerprint, StringComparison.Ordinal)
                        ? AnalysisInterpretationFreshness.Current : AnalysisInterpretationFreshness.Stale,
                    Reason = string.Equals(record.InputFingerprint, fingerprint, StringComparison.Ordinal)
                        ? "The approved interpretation matches the current analysis and report context."
                        : "The analysis, context, or report choices have changed.",
                    CurrentFingerprint = fingerprint,
                };
            }
            catch (Exception ex)
            {
                return new AnalysisInterpretationFreshnessResult { Status = AnalysisInterpretationFreshness.Unverifiable, Reason = ex.Message };
            }
        }
    }
}
