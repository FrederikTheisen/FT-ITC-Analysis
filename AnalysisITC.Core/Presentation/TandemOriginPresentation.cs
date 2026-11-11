using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>How a residual-bootstrap result treated one tandem's back-mixing fractions.</summary>
    public enum TandemMixingBootstrapStatus { Included, FixedFractions, Unavailable }

    public static class TandemOriginPresentation
    {
        public const string BootstrapStatusLabel = "Tandem mixing uncertainty";

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
                else if (line.StartsWith("Source files: ", StringComparison.Ordinal))
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Length > 0) lines.Add("");
                    lines.Add("Source files:");
                    lines.Add(line.Substring("Source files: ".Length));
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
