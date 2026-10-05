using System.Collections.Generic;
using System.Globalization;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Presentation
{
    public sealed class NullModelComparisonDisplayRow
    {
        public NullModelComparisonDisplayRow(string label, string value, string tooltip)
        {
            Label = label ?? "";
            Value = value ?? "";
            Tooltip = tooltip ?? "";
        }

        public string Label { get; }
        public string Value { get; }
        public string Tooltip { get; }
    }

    /// <summary>Shared wording and formatting for the result-level null hypothesis test.</summary>
    public static class NullModelComparisonPresentation
    {
        public const string RuleExplanation = "ΔAICc = AICc(null) − AICc(binding). Values ≤ 6 recommend no binding detected; values > 6 and < 10 are inconclusive; values ≥ 10 recommend binding detected. These are chosen cutoffs without a calibrated false-positive guarantee. No binding detected means this experiment does not establish binding relative to Offset; it does not establish that the molecules cannot bind.";

        public const string AnalysisInspectorTitle = "Null hypothesis test";

        /// <summary>Rows shown by the live analysis inspector on every platform.</summary>
        public static IReadOnlyList<NullModelComparisonDisplayRow> AnalysisInspectorRows(NullModelComparison comparison, EnergyUnitFamily energyUnitFamily)
        {
            var evidenceTooltip = AnalysisEvidenceTooltip(comparison, energyUnitFamily);
            return new[]
            {
                new NullModelComparisonDisplayRow("Model", NullModel(comparison), null),
                new NullModelComparisonDisplayRow("Null RMSD", NullRmsd(comparison, energyUnitFamily), evidenceTooltip),
                new NullModelComparisonDisplayRow("ΔAICc", Delta(comparison), evidenceTooltip),
                new NullModelComparisonDisplayRow("Conclusion", Conclusion(comparison), null),
            };
        }

        public static string NullModel(NullModelComparison comparison)
        {
            if (comparison == null) return "Offset (not calculated)";
            var model = comparison.IsIndependentMemberComparison ? "Offset fitted per experiment" : "Offset";
            if (comparison.NullFitSucceeded) return model;
            return model + " (failed)";
        }

        public static string NullRmsdAndDeltaAicc(NullModelComparison comparison, EnergyUnitFamily energyUnitFamily)
        {
            return $"{NullRmsd(comparison, energyUnitFamily)} / {Delta(comparison)}";
        }

        public static string NullRmsd(NullModelComparison comparison, EnergyUnitFamily energyUnitFamily)
        {
            var value = comparison?.NullInformationCriteria?.ResidualRmsdMicrojoules;
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                ? (value.Value / 1_000_000d * ThermogramUnits.IntegratedHeatScale(energyUnitFamily)).ToString("G4", CultureInfo.CurrentCulture)
                : "Unavailable";
        }

        public static string NullEvidenceTooltip(NullModelComparison comparison, EnergyUnitFamily energyUnitFamily)
        {
            var rmsdDescription = NullRmsdReason(comparison);
            var criteria = comparison?.NullInformationCriteria;
            var aicc = criteria?.IsAiccAvailable == true
                ? Aicc(criteria)
                : "Unavailable" + (string.IsNullOrWhiteSpace(criteria?.AiccUnavailableReason)
                    ? string.Empty
                    : $" ({criteria.AiccUnavailableReason})");
            var unavailableComparison = AutomaticOutcome(comparison) == BindingAssessmentOutcome.NotAssessed
                ? $" Comparison unavailable: {ComparisonReason(comparison)}"
                : string.Empty;
            return $"{rmsdDescription} Null AICc: {aicc}. RMSD unit: {ThermogramUnits.IntegratedHeatUnit(energyUnitFamily)}.{unavailableComparison}";
        }

        /// <summary>Brief numerical tooltip for the live analysis inspector.</summary>
        public static string AnalysisEvidenceTooltip(NullModelComparison comparison, EnergyUnitFamily energyUnitFamily)
        {
            var criteria = comparison?.NullInformationCriteria;
            var aicc = criteria?.IsAiccAvailable == true ? Aicc(criteria) : "Unavailable";
            var reason = comparison == null
                ? ComparisonReason(null)
                : !comparison.NullFitSucceeded
                    ? NullFitReason(comparison)
                    : AutomaticOutcome(comparison) == BindingAssessmentOutcome.NotAssessed
                        ? !string.IsNullOrWhiteSpace(comparison.ComparisonUnavailableReason)
                            ? comparison.ComparisonUnavailableReason
                            : !string.IsNullOrWhiteSpace(criteria?.AiccUnavailableReason)
                                ? criteria.AiccUnavailableReason
                                : ComparisonReason(comparison)
                        : string.Empty;
            return $"Null AICc: {aicc}. RMSD unit: {ThermogramUnits.IntegratedHeatUnit(energyUnitFamily)}." +
                (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" {reason}");
        }

        public static string Conclusion(NullModelComparison comparison)
            => OutcomeText(AutomaticOutcome(comparison));

        public static string Conclusion(BindingAssessmentState assessment)
            => $"{OutcomeText(assessment?.EffectiveOutcome ?? BindingAssessmentOutcome.NotAssessed)} ({Mode(assessment)})";

        public static string AutomaticRecommendation(BindingAssessmentState assessment, NullModelComparison comparison)
        {
            var automaticOutcome = assessment?.AutomaticOutcome ?? BindingAssessmentOutcome.NotAssessed;
            if (assessment?.IsManual == true)
                return automaticOutcome == BindingAssessmentOutcome.NotAssessed
                    ? $"Manual override. Automatic unavailable: {AssessmentComparisonReason(comparison)}"
                    : $"Manual override. Automatic: {OutcomeText(automaticOutcome)}.";

            return automaticOutcome == BindingAssessmentOutcome.NotAssessed
                ? $"Automatic assessment unavailable: {AssessmentComparisonReason(comparison)}"
                : "Automatic assessment.";
        }

        static string AssessmentComparisonReason(NullModelComparison comparison)
            => comparison == null ? "No comparison available."
                : string.IsNullOrWhiteSpace(comparison.ComparisonUnavailableReason)
                    ? "Comparison unavailable."
                    : comparison.ComparisonUnavailableReason;

        public static string ComparisonReason(NullModelComparison comparison)
            => comparison == null ? "No saved null-model comparison is available."
                : string.IsNullOrWhiteSpace(comparison.ComparisonUnavailableReason)
                    ? "The saved comparison is unavailable."
                    : comparison.ComparisonUnavailableReason;

        public static string NullFitReason(NullModelComparison comparison)
            => comparison == null ? "No saved null-model comparison is available."
                : comparison.NullFitSucceeded ? "The Offset fit converged."
                : string.IsNullOrWhiteSpace(comparison.NullFitReason) ? "The Offset fit failed."
                : comparison.NullFitReason;

        public static string NullRmsdReason(NullModelComparison comparison)
            => comparison?.NullInformationCriteria == null ? ComparisonReason(comparison)
                : !comparison.NullFitSucceeded ? NullFitReason(comparison)
                : "Saved unweighted RMSD from the Offset fit.";

        public static BindingAssessmentOutcome AutomaticOutcome(NullModelComparison comparison)
            => BindingAssessmentState.FromComparison(comparison).AutomaticOutcome;

        public static string OutcomeText(BindingAssessmentOutcome outcome) => outcome switch
        {
            BindingAssessmentOutcome.NoBindingDetected => "No binding detected",
            BindingAssessmentOutcome.Inconclusive => "Inconclusive",
            BindingAssessmentOutcome.BindingDetected => "Binding detected",
            _ => "Not assessed"
        };

        public static string Mode(BindingAssessmentState assessment) => assessment?.IsManual == true ? "Manual" : "Automatic";

        public static string NullStatus(NullModelComparison comparison)
            => comparison == null ? "Not calculated"
                : comparison.NullFitSucceeded ? "Converged"
                : string.IsNullOrWhiteSpace(comparison.NullFitReason) ? "Failed" : comparison.NullFitReason;

        public static string Aicc(FitInformationCriteria criteria)
            => criteria?.IsAiccAvailable == true
                ? criteria.Aicc.Value.ToString("G6", CultureInfo.CurrentCulture)
                : criteria == null ? "Not calculated" : criteria.AiccUnavailableReason;

        public static string Delta(NullModelComparison comparison)
            => AutomaticOutcome(comparison) != BindingAssessmentOutcome.NotAssessed
                && comparison?.DeltaAicc is double delta
                ? (delta > 0 ? "+" : string.Empty) + delta.ToString("G6", CultureInfo.CurrentCulture)
                : comparison == null ? "Not calculated" : "Unavailable";

        public static string BindingStatus(NullModelComparison comparison)
            => comparison == null ? "Not calculated."
                : comparison.BindingFitSucceeded ? "Converged"
                : string.IsNullOrWhiteSpace(comparison.BindingFitReason) ? "Did not converge" : comparison.BindingFitReason;

        public static string Weighting(FitInformationCriteria criteria)
            => criteria == null ? "Not calculated"
                : criteria.UsesKnownObservationSigmas ? "Known observation SDs"
                : criteria.LikelihoodMode == GaussianLikelihoodMode.EstimatedWeightedVariance
                    ? "Relative observation SDs; residual scale estimated"
                    : "Unweighted; residual variance estimated";
    }
}
