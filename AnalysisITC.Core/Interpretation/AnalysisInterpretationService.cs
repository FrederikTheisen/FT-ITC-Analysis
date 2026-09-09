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
        public string ClientRequestId { get; set; }
        public string GenerationProfile { get; set; } = "fast";
        public AnalysisInterpretationPackage Package { get; set; }
        public AnalysisInterpretationPrompt Prompt { get; set; }
        // Kept on the provider-neutral request so a future streaming provider can
        // publish server-side progress without changing the service or UI contract.
        public IProgress<AnalysisInterpretationProgressUpdate> Progress { get; set; }
    }

    public sealed class AnalysisInterpretationProviderResponse
    {
        public string RequestId { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
        public string EffectiveInputFingerprint { get; set; }
        public List<string> Omissions { get; set; } = new List<string>();
        public List<string> KnowledgeBaseIds { get; set; } = new List<string>();
        public List<string> RetrievedSourceIds { get; set; } = new List<string>();
        public string InterpretationMarkdown { get; set; }
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
            CancellationToken cancellationToken = default, IProgress<AnalysisInterpretationProgressUpdate> progress = null) =>
            GenerateAsync(report, id => result?.UniqueID == id ? result : null, _ => null, options, cancellationToken, progress);

        public async Task<AnalysisInterpretationGenerationResult> GenerateAsync(
            AnalysisReport report, Func<string, AnalysisResult> resultResolver,
            Func<string, ExperimentData> experimentResolver, AnalysisInterpretationOptions options = null,
            CancellationToken cancellationToken = default, IProgress<AnalysisInterpretationProgressUpdate> progress = null)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            AnalysisInterpretationLog.Write("generation-start", requestId, "");
            try
            {
                Report(progress, AnalysisInterpretationProgressStage.BuildingPackage, "Building the analysis package…");
                cancellationToken.ThrowIfCancellationRequested();
                var package = AnalysisInterpretationPackageBuilder.Build(report, resultResolver, experimentResolver, options);
                var prompt = AnalysisInterpretationPromptBuilder.Build(package, requestId);
                var request = new AnalysisInterpretationGenerationRequest
                {
                    ClientRequestId = requestId,
                    GenerationProfile = "fast",
                    Package = package,
                    Prompt = prompt,
                    Progress = progress,
                };
                Report(progress, AnalysisInterpretationProgressStage.SendingRequest, "Sending the request…");
                var responseTask = provider.GenerateAsync(request, cancellationToken);
                Report(progress, AnalysisInterpretationProgressStage.WaitingForServer, "Waiting for the server…");
                var response = await responseTask.ConfigureAwait(false);
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
                        InterpretationMarkdown = markdown,
                        InputFingerprint = prompt.InputFingerprint,
                        EffectiveInputFingerprint = response.EffectiveInputFingerprint ?? prompt.InputFingerprint,
                        Omissions = package.Omissions.Concat(response.Omissions ?? new List<string>()).Distinct().ToList(),
                        KnowledgeBaseIds = response.KnowledgeBaseIds ?? new List<string>(),
                        RetrievedSourceIds = response.RetrievedSourceIds ?? new List<string>(),
                        PromptVersion = prompt.PromptVersion,
                        OutputFormatVersion = prompt.OutputFormatVersion,
                        Provider = response.Provider ?? "",
                        Model = response.Model ?? "",
                        ServiceRequestId = response.RequestId ?? request.ClientRequestId,
                        GeneratedAtUtc = generated,
                    },
                };
                Report(progress, AnalysisInterpretationProgressStage.Finished, "Finished — interpretation ready.", true);
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
            if (!string.Equals(record.PromptVersion, AnalysisInterpretationPromptBuilder.PromptVersion, StringComparison.Ordinal)
                || !string.Equals(record.OutputFormatVersion, AnalysisInterpretationPromptBuilder.OutputFormatVersion, StringComparison.Ordinal))
                return new AnalysisInterpretationFreshnessResult { Status = AnalysisInterpretationFreshness.Unverifiable, Reason = "The approved interpretation uses an unsupported prompt or output format version." };
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
                        : "The analysis, context, requested sections, or prompt version has changed.",
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
