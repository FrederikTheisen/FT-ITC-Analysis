using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnalysisITC.Core.Interpretation
{
    public sealed class AnalysisInterpretationPrompt
    {
        public string PromptVersion { get; set; }
        public string SystemInstructions { get; set; }
        public string UserMessage { get; set; }
        public string ResponseFormatInstructions { get; set; }
        public string OutputFormatVersion { get; set; }
        public string CanonicalPackageJson { get; set; }
        /// <summary>Compact model payload derived from CanonicalPackageJson; freshness uses the canonical package.</summary>
        public string ModelPackageJson { get; set; }
        public string ModelInputEncoding { get; set; }
        public string InputFingerprint { get; set; }
        // The evidence fingerprint intentionally excludes server-owned guidance.
        // It is the sole freshness key for a saved interpretation.
        public string EvidenceFingerprint { get; set; }
        public string OutputInstructionsFingerprint { get; set; }
    }

    public static class AnalysisInterpretationPromptBuilder
    {
        public const string PromptVersion = "itc-interpretation-output-3.0";
        public const string OutputFormatVersion = "itc-interpretation-markdown-3.0";
        public const string EvidenceFingerprintScheme = "sha256:utf8:canonical-package-json-v1";
        internal static readonly JsonSerializerOptions CanonicalJsonOptions = CreateJsonOptions();

        public static AnalysisInterpretationPrompt Build(AnalysisInterpretationPackage package) => Build(package, null);

        public static AnalysisInterpretationPrompt Build(AnalysisInterpretationPackage package, string requestId)
        {
            requestId = requestId ?? Guid.NewGuid().ToString("N");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var prompt = BuildCore(package);
                var fullBytes = Encoding.UTF8.GetByteCount(prompt.CanonicalPackageJson);
                var modelBytes = Encoding.UTF8.GetByteCount(prompt.ModelPackageJson);
                var change = fullBytes == 0 ? 0 : 100.0 * (modelBytes - fullBytes) / fullBytes;
                var sizeChange = FormattableString.Invariant($"{Math.Abs(change):0.0}% {(change <= 0 ? "smaller" : "larger")}");
                AnalysisInterpretationLog.Summary(FormattableString.Invariant(
                    $"Report input prepared: {package.Results?.Count ?? 0} results; full evidence {fullBytes / 1024.0:0.0} KiB, model input {modelBytes / 1024.0:0.0} KiB ({sizeChange}); output instructions included. Built in {timer.ElapsedMilliseconds} ms."));
                return prompt;
            }
            catch (Exception ex)
            {
                AnalysisInterpretationLog.Write("prompt-failed", requestId, $"exception={ex.GetType().Name} elapsedMs={timer.ElapsedMilliseconds}");
                throw;
            }
        }

        static AnalysisInterpretationPrompt BuildCore(AnalysisInterpretationPackage package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (package.PackageSchemaVersion != AnalysisInterpretationPackageBuilder.PackageSchemaVersion)
                throw new NotSupportedException("Unsupported interpretation package schema: " + package.PackageSchemaVersion);
            var canonical = JsonSerializer.Serialize(package, CanonicalJsonOptions);
            var modelPackage = AnalysisInterpretationModelInputWriter.Write(canonical);
            var format = BuildResponseFormatInstructions(package);
            var evidenceFingerprint = Sha256(canonical);
            return new AnalysisInterpretationPrompt
            {
                PromptVersion = PromptVersion, OutputFormatVersion = OutputFormatVersion,
                // Scientific instructions are deliberately server-owned.  These fields remain
                // available to provider-neutral callers, but contain no scientific guidance.
                SystemInstructions = "", UserMessage = "PACKAGE_JSON\n" + modelPackage, ResponseFormatInstructions = format,
                CanonicalPackageJson = canonical,
                ModelPackageJson = modelPackage,
                ModelInputEncoding = AnalysisInterpretationModelInputWriter.Encoding,
                EvidenceFingerprint = evidenceFingerprint,
                OutputInstructionsFingerprint = Sha256(format),
                InputFingerprint = evidenceFingerprint,
            };
        }

        static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false, WriteIndented = false };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false));
            return options;
        }

        public static string BuildResponseFormatInstructions(AnalysisInterpretationPackage package = null) =>
            "Output format version: " + OutputFormatVersion + ".\n" +
            "The first heading must be exactly: ## Overall interpretation\n" +
            "Optional headings may follow only in this order: ## Experiment observations; ## Limitations; ## Suggested checks; ## Suggested investigations.\n" +
            "Within any ## section, descriptive subsection headings beginning exactly with '### ' are allowed. Subsection titles must be non-empty plain text.\n" +
            "Use paragraphs, blank lines, single-level bullet items beginning with '- ', and compact Markdown pipe tables when a comparison is clearer in a table. Tables need a header row and a separator row (for example | --- | ---: |); keep cells short and prefer two to four columns.\n" +
            "Write the entire result reference in **bold**, including the word Result: **Result 2** or **Results 1 and 2**. Prefer **Experiment 1B** and **Experiments 1A–1C** when they fit naturally; compact **1B** or **1A–1C** is acceptable to avoid cumbersome repetition. Whenever Result or Experiment accompanies a reference, include that word within the same bold span. Use other **bold** or *italic* emphasis sparingly when it materially improves scientific readability. " +
            "Do not use any other headings, HTML, links, images, code, blockquotes, nested lists, internal evidence-ID citation syntax, or control characters. Sparse supplied knowledge-base name/title, journal and year references are allowed.\n" +
            "Omit optional sections that do not add useful interpretation. Prefer around 500–600 words or fewer; use up to roughly 1,000 words when the evidence warrants more detail. Treat these as flexible targets including references, not hard limits.\n" +
            "Requested optional headings for this report (omit any that add no useful interpretation):\n" +
            (package == null ? "None." : RequestedOptionalHeadings(package));

        static string RequestedOptionalHeadings(AnalysisInterpretationPackage package)
        {
            var requested = package.RequestedInterpretation?.RequestedSections ?? new System.Collections.Generic.List<AnalysisInterpretationSection>();
            var values = new[]
            {
                (AnalysisInterpretationSection.ExperimentObservations, "## Experiment observations"),
                (AnalysisInterpretationSection.Limitations, "## Limitations"),
                (AnalysisInterpretationSection.SuggestedChecks, "## Suggested checks"),
                (AnalysisInterpretationSection.SuggestedInvestigations, "## Suggested investigations"),
            }.Where(item => requested.Contains(item.Item1)).Select(item => item.Item2).ToArray();
            return values.Length == 0 ? "None; return only the mandatory overall interpretation." : string.Join("\n", values);
        }

        static string Sha256(string value)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(item => item.ToString("x2")));
        }
    }
}
