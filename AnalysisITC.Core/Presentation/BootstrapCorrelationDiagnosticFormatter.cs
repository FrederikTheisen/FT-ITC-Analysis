using System;
using System.Collections.Generic;
using System.Globalization;
using AnalysisITC.Core.Analysis;

namespace AnalysisITC.Core.Presentation
{
    public static class BootstrapCorrelationDiagnosticFormatter
    {
        public static string CellDetails(
            string rowLabel,
            string columnLabel,
            BootstrapCorrelationCellDiagnostic cell)
        {
            if (cell == null) return string.Empty;
            var lines = new List<string>
            {
                $"{rowLabel} vs {columnLabel}",
                $"Pearson r: {cell.PearsonR.ToString("0.000", CultureInfo.InvariantCulture)}",
            };
            if (cell.IsSelfCorrelation)
            {
                lines.Add("Self-correlation; Monte Carlo precision interval is not applicable.");
            }
            else if (cell.HasMonteCarloPrecisionInterval)
            {
                lines.Add($"Approx. 95% MC precision: [{cell.MonteCarloPrecisionLower.Value.ToString("0.000", CultureInfo.InvariantCulture)}, {cell.MonteCarloPrecisionUpper.Value.ToString("0.000", CultureInfo.InvariantCulture)}]");
                if (cell.IsSignUncertain)
                    lines.Add("Sign unresolved at this Monte Carlo precision.");
            }
            return string.Join(Environment.NewLine, lines);
        }

        public static IReadOnlyList<string> ReliabilityWarnings(BootstrapCorrelationResult result)
        {
            var warnings = new List<string>();
            if (result?.Reliability == null) return warnings;
            var reliability = result.Reliability;
            if (reliability.HasCoarseMonteCarloPrecision)
                warnings.Add($"Only {reliability.CompleteRefitCount.ToString(CultureInfo.CurrentCulture)} complete refits are available; fewer than {BootstrapCorrelationReliability.RecommendedCompleteReplicates} makes Monte Carlo precision coarse.");
            if (reliability.HasFrequentFailures)
                warnings.Add($"{reliability.FailedRefitCount.Value.ToString(CultureInfo.CurrentCulture)} of {reliability.AttemptedRefitCount.Value.ToString(CultureInfo.CurrentCulture)} attempted refits failed ({reliability.FailureFraction.Value.ToString("P0", CultureInfo.CurrentCulture)}); retained refits may be a selective subset.");
            if (reliability.CoordinateIncompleteRefitCount > 0)
                warnings.Add($"{reliability.CoordinateIncompleteRefitCount.ToString(CultureInfo.CurrentCulture)} usable refits lacked a finite displayed coordinate and were excluded from the complete matrix ensemble.");
            if (reliability.HasUncertainSigns)
                warnings.Add(reliability.UncertainSignPairCount == 1
                    ? "1 off-diagonal correlation has a 95% Monte Carlo precision interval spanning zero; its sign is unresolved at this simulation precision."
                    : $"{reliability.UncertainSignPairCount.ToString(CultureInfo.CurrentCulture)} off-diagonal correlations have 95% Monte Carlo precision intervals spanning zero; their signs are unresolved at this simulation precision.");
            if (result.IsRankLimited)
            {
                var maximumRank = Math.Max(0, result.CompleteReplicateCount - 1);
                warnings.Add($"Structural rank warning: {result.CompleteReplicateCount.ToString(CultureInfo.CurrentCulture)} complete refits for {result.Parameters.Count.ToString(CultureInfo.CurrentCulture)} parameters limit the centered covariance rank to at most {maximumRank.ToString(CultureInfo.CurrentCulture)}.");
            }
            return warnings;
        }

        public static string AccessiblePairSummary(
            BootstrapCorrelationResult result,
            IReadOnlyList<string> labels)
        {
            if (result?.CellDiagnostics == null || labels == null) return string.Empty;
            var lines = new List<string>
            {
                $"Complete refits: {result.CompleteReplicateCount.ToString(CultureInfo.CurrentCulture)}",
            };
            var width = Math.Min(labels.Count, result.CellDiagnostics.GetLength(0));
            for (var row = 0; row < width; row++)
                for (var column = row + 1; column < width; column++)
                    lines.Add(CellDetails(labels[row], labels[column], result.CellDiagnostics[row, column]));
            return string.Join(Environment.NewLine + Environment.NewLine, lines);
        }
    }
}
