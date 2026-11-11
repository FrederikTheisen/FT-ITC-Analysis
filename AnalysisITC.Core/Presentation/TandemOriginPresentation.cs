using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>How a residual-bootstrap result treated one tandem's back-mixing fractions.</summary>
    public enum TandemMixingBootstrapStatus { Included, FixedFractions, Unavailable }

    public static class TandemOriginPresentation
    {
        public const string BootstrapStatusLabel = "Tandem mixing uncertainty";
        internal const string DetailsTitle = "Tandem merge";
        internal const string SourceRunsLabel = "Source runs";
        internal const string MergeLimitsLabel = "95% limits at merge";

        internal sealed class MergeDetails
        {
            public IReadOnlyList<(string Label, string Value)> Settings { get; }
            public IReadOnlyList<(string Fraction, string Limits)> Refills { get; }
            public string SourceRuns { get; }
            public string Description { get; }
            public string LimitsTooltip { get; }

            internal MergeDetails(IReadOnlyList<(string, string)> settings, IReadOnlyList<(string, string)> refills,
                string sourceRuns, string description, string limitsTooltip)
            {
                Settings = settings;
                Refills = refills;
                SourceRuns = sourceRuns;
                Description = description;
                LimitsTooltip = limitsTooltip;
            }
        }

        /// <summary>Returns null when the member is not a tandem or no residual bootstrap ran for it.</summary>
        public static TandemMixingBootstrapStatus? BootstrapStatus(GlobalSolution result, SolutionInterface member)
        {
            if (member?.Data?.IsTandemExperiment != true
                || result?.ErrorEstimationMethod != ErrorEstimationMethod.BootstrapResiduals
                || result.Convergence?.ErrorEstimationOutcome == ErrorEstimationOutcome.SkippedNoBinding
                || member.Convergence?.ErrorEstimationOutcome == ErrorEstimationOutcome.SkippedNoBinding)
                return null;
            if (member.Model?.ModelCloneOptions?.ReestimateTandemMixing != true)
                return TandemMixingBootstrapStatus.FixedFractions;
            return member.BootstrapSolutions?.Count > 0
                ? TandemMixingBootstrapStatus.Included : TandemMixingBootstrapStatus.Unavailable;
        }

        public static string FormatBootstrapStatus(TandemMixingBootstrapStatus status) =>
            status == TandemMixingBootstrapStatus.Included ? "Included in bootstrap"
            : status == TandemMixingBootstrapStatus.FixedFractions ? "Fixed fractions" : "Unavailable";

        static readonly Regex BackMixingDescription = new Regex(
            @"^Tandem concatenation \((?<mode>[^\r\n]+)\): DeadVolume=(?<volume>[^,\r\n]+), RemoveOverflow=(?<overflow>True|False), MixFrac=(?<fractions>[^,\r\n]+), (?<method>[^\r\n]+) bookkeeping$");
        static readonly Regex WithoutBackMixingDescription = new Regex(
            @"^Tandem concatenation \(MicroCal concat; no back-mixing; (?<method>[^\r\n]+) bookkeeping\)\.$");
        const string SourceFilesPrefix = "Source files: ";
        const string MethodLabel = "Method";

        internal static string OverviewSummary(ExperimentData data)
        {
            var details = BuildDetails(data);
            if (details == null) return "";

            var summary = new List<string>();
            if (data.Segments?.Count > 1)
                summary.Add($"{data.Segments.Count} runs");
            var method = details.Settings.FirstOrDefault(row => row.Label == MethodLabel).Value;
            if (method != null)
                summary.Add(method);
            if (details.Refills.Count > 0)
                summary.Add("Back-mixing by refill " + string.Join(" / ", details.Refills.Select(row => row.Fraction)));
            return string.Join(" · ", summary);
        }

        internal static MergeDetails BuildDetails(ExperimentData data)
        {
            var info = data?.TandemMergeInfo;
            var description = data?.TandemMergeDescription;
            if (info == null && string.IsNullOrWhiteSpace(description)) return null;

            var settings = new List<(string, string)>();
            var refills = new List<(string, string)>();
            var otherLines = new List<string>();
            string sourceRuns = null;
            string limitsTooltip = null;
            if (info != null)
            {
                settings.Add((MethodLabel, info.Criterion == TandemMixingCriterion.ModelFree
                    ? "Automatic (model-free)" : "Automatic (one-site)"));
                settings.Add(("Bookkeeping", info.Bookkeeping.DisplayName()));
                settings.Add(("Dead volume", new FloatWithError(info.DeadVolume * 1e6).AsNumber() + " µL"));
                settings.Add(("Overflow removed", info.DidRemoveOverflow ? "Yes" : "No"));
                for (var i = 0; i < info.Fractions.Count; i++)
                    refills.Add((Percent(info.Fractions[i]), FormatMergeRange(info.Uncertainty.Intervals[i])));
                limitsTooltip = info.Uncertainty.Method == MixingUncertaintyMethod.Grid95
                    ? "Approximate limits from the one-site grid"
                    : $"Range from {info.Uncertainty.MonteCarloDraws} Monte Carlo draws";
            }

            foreach (var line in (description ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (line.StartsWith(SourceFilesPrefix, StringComparison.Ordinal))
                {
                    sourceRuns = line.Substring(SourceFilesPrefix.Length);
                    continue;
                }

                var mixing = BackMixingDescription.Match(line);
                var noMixing = WithoutBackMixingDescription.Match(line);
                if (info == null && mixing.Success)
                {
                    var mode = mixing.Groups["mode"].Value;
                    settings.Add((MethodLabel, char.ToUpperInvariant(mode[0]) + mode.Substring(1).Replace("; ", ", ")));
                    settings.Add(("Bookkeeping", mixing.Groups["method"].Value));
                    settings.Add(("Dead volume", mixing.Groups["volume"].Value));
                    settings.Add(("Overflow removed", mixing.Groups["overflow"].Value == "True" ? "Yes" : "No"));

                    var fractions = mixing.Groups["fractions"].Value.Split(new[] { " / " }, StringSplitOptions.None);
                    var count = mode == "fixed back-mixing" && fractions.Length == 1
                        ? Math.Max(1, (data.Segments?.Count ?? 0) - 1) : fractions.Length;
                    for (var i = 0; i < count; i++)
                        refills.Add((fractions[fractions.Length == 1 ? 0 : i], null));
                }
                else if (info == null && noMixing.Success)
                {
                    settings.Add((MethodLabel, "Simple (no back-mixing)"));
                    settings.Add(("Bookkeeping", noMixing.Groups["method"].Value));
                }
                else if (!mixing.Success && !noMixing.Success)
                    otherLines.Add(line);
            }

            return new MergeDetails(settings, refills, sourceRuns, string.Join("\n", otherLines).Trim(), limitsTooltip);
        }

        static string FormatMergeRange(TandemMixingInterval interval)
        {
            if (!interval.Lower.HasValue && !interval.Upper.HasValue) return "Unavailable";
            var lower = FormatLimit(interval.Lower, interval.LowerStatus);
            var upper = FormatLimit(interval.Upper, interval.UpperStatus);
            return interval.Lower.HasValue && interval.Upper.HasValue
                ? $"{lower} – {upper}" : $"Lower {lower}, upper {upper}";
        }

        /// <summary>Formats recorded provenance for display without changing the saved description.</summary>
        public static string FormatDescription(string description, TandemMergeInfo info = null)
        {
            if (string.IsNullOrWhiteSpace(description)) return description;

            var lines = new List<string>();
            foreach (var line in description.Replace("\r\n", "\n").Split('\n'))
            {
                var mixing = BackMixingDescription.Match(line);
                var noMixing = WithoutBackMixingDescription.Match(line);
                if (mixing.Success)
                {
                    lines.Add("Tandem concatenation (" + mixing.Groups["mode"].Value + ")");
                    lines.Add("Dead volume: " + mixing.Groups["volume"].Value);
                    lines.Add("Overflow removed: " + (mixing.Groups["overflow"].Value == "True" ? "Yes" : "No"));
                    lines.Add("Back-mixing fraction: " + mixing.Groups["fractions"].Value);
                    lines.Add("Bookkeeping: " + mixing.Groups["method"].Value);
                }
                else if (noMixing.Success)
                {
                    lines.Add("Tandem concatenation (MicroCal concat; no back-mixing)");
                    lines.Add("Bookkeeping: " + noMixing.Groups["method"].Value);
                }
                else if (line.StartsWith(SourceFilesPrefix, StringComparison.Ordinal))
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Length > 0) lines.Add("");
                    lines.Add("Source files:");
                    lines.Add(line.Substring(SourceFilesPrefix.Length));
                }
                else lines.Add(line);
            }
            if (info != null)
            {
                lines.Add("");
                var available = info.Uncertainty.Intervals.Any(interval => interval.Lower.HasValue || interval.Upper.HasValue);
                lines.Add(info.Uncertainty.Method == MixingUncertaintyMethod.Grid95
                    ? "Approximate grid-based 95% mixing limits"
                    : available ? $"95% Monte Carlo mixing ranges ({info.Uncertainty.MonteCarloDraws} draws)"
                    : $"Monte Carlo mixing uncertainty unavailable ({info.Uncertainty.MonteCarloDraws} draws requested)");
                for (var index = 0; index < info.Fractions.Count; index++)
                {
                    var interval = info.Uncertainty.Intervals[index];
                    var lower = FormatLimit(interval.Lower, interval.LowerStatus);
                    var upper = FormatLimit(interval.Upper, interval.UpperStatus);
                    lines.Add($"Refill {index + 1}: {Percent(info.Fractions[index])}; lower {lower}, upper {upper}");
                }
            }
            return string.Join("\n", lines);
        }

        static string Percent(double value) => new FloatWithError(100 * value).AsNumber() + "%";
        static string FormatLimit(double? value, MixingLimitStatus status) => value.HasValue
            ? Percent(value.Value) + (status == MixingLimitStatus.BoundReached ? " (bound reached)" : "") : "unavailable";
    }
}
