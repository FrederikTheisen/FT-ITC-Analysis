using System;
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

    public sealed class AnalysisInterpretationGenerationRequest
    {
        public string ClientRequestId { get; set; }
        public string GenerationProfile { get; set; } = "fast";
        public AnalysisInterpretationPackage Package { get; set; }
        public AnalysisInterpretationPrompt Prompt { get; set; }
    }

    public sealed class AnalysisInterpretationProviderResponse
    {
        public string RequestId { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
        public string DocumentJson { get; set; }
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

        public async Task<AnalysisInterpretationGenerationResult> GenerateAsync(
            AnalysisReport report,
            AnalysisResult result,
            AnalysisInterpretationOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var package = AnalysisInterpretationPackageBuilder.Build(report, result, options);
            var prompt = AnalysisInterpretationPromptBuilder.Build(package);
            var request = new AnalysisInterpretationGenerationRequest
            {
                ClientRequestId = Guid.NewGuid().ToString("N"),
                GenerationProfile = "fast",
                Package = package,
                Prompt = prompt,
            };
            var response = await provider.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
            if (response == null)
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse, "The provider returned no response.");
            var document = AnalysisInterpretationResponseParser.Parse(response.DocumentJson, package);
            var generated = response.GeneratedAtUtc == default(DateTime) ? DateTime.UtcNow : response.GeneratedAtUtc;
            if (generated.Kind != DateTimeKind.Utc) generated = generated.ToUniversalTime();
            return new AnalysisInterpretationGenerationResult
            {
                Package = package,
                Prompt = prompt,
                Interpretation = new AnalysisInterpretationRecord
                {
                    Interpretation = document,
                    InputFingerprint = prompt.InputFingerprint,
                    PromptVersion = prompt.PromptVersion,
                    OutputSchemaVersion = prompt.OutputSchemaVersion,
                    Provider = response.Provider ?? "",
                    Model = response.Model ?? "",
                    ServiceRequestId = response.RequestId ?? request.ClientRequestId,
                    GeneratedAtUtc = generated,
                },
            };
        }

        public static AnalysisInterpretationFreshnessResult EvaluateFreshness(
            AnalysisReport report,
            AnalysisResult result)
        {
            if (report?.ApprovedInterpretation == null)
                return new AnalysisInterpretationFreshnessResult { Status = AnalysisInterpretationFreshness.Unverifiable, Reason = "No approved interpretation is stored." };
            if (result == null || report.ResultIds.Count != 1
                || !string.Equals(report.ResultIds[0], result.UniqueID, StringComparison.Ordinal))
                return new AnalysisInterpretationFreshnessResult { Status = AnalysisInterpretationFreshness.Unverifiable, Reason = "The referenced analysis result is missing or unresolved." };
            var record = report.ApprovedInterpretation;
            if (!string.Equals(record.PromptVersion, AnalysisInterpretationPromptBuilder.PromptVersion, StringComparison.Ordinal)
                || !string.Equals(record.OutputSchemaVersion, AnalysisInterpretationPromptBuilder.OutputSchemaVersion, StringComparison.Ordinal))
                return new AnalysisInterpretationFreshnessResult { Status = AnalysisInterpretationFreshness.Unverifiable, Reason = "The approved interpretation uses an unsupported prompt or output schema version." };
            try
            {
                var fingerprint = AnalysisInterpretationPromptBuilder.Build(
                    AnalysisInterpretationPackageBuilder.Build(report, result)).InputFingerprint;
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
