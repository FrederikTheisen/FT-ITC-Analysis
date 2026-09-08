using System;
using System.Globalization;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>
    /// Shared presentation text for the compact information-criteria summary
    /// shown by the platform result inspectors.
    /// </summary>
    internal sealed class InformationCriteriaSummaryPresentation
    {
        public string CriterionLabel { get; }
        public string CriterionValue { get; }
        public string Tooltip { get; }
        public string Footer { get; }

        InformationCriteriaSummaryPresentation(
            string criterionLabel,
            string criterionValue,
            string tooltip,
            string footer)
        {
            CriterionLabel = criterionLabel;
            CriterionValue = criterionValue;
            Tooltip = tooltip;
            Footer = footer;
        }

        public static InformationCriteriaSummaryPresentation For(
            AnalysisResult result,
            IFormatProvider provider = null)
        {
            provider ??= CultureInfo.CurrentCulture;
            var criteria = result?.InformationCriteria;
            if (criteria == null)
            {
                return new InformationCriteriaSummaryPresentation(
                    "AIC",
                    "Unavailable",
                    "AIC unavailable (no information-criteria result). n = included injections. K = fitted parameters. Compare only like-for-like fits.",
                    "Compare only like-for-like fits.");
            }

            var criterionLabel = criteria.IsAiccAvailable ? "AICc" : "AIC";
            var criterionValue = criteria.IsAiccAvailable
                ? Format(criteria.Aicc, provider)
                : criteria.IsAicAvailable
                    ? Format(criteria.Aic, provider)
                    : FirstReason(criteria.AicUnavailableReason, criteria.AiccUnavailableReason);

            var tooltip = BuildTooltip(criteria, provider);
            var footer = BuildFooter(result, criteria);
            return new InformationCriteriaSummaryPresentation(
                criterionLabel,
                criterionValue,
                tooltip,
                footer);
        }

        static string BuildTooltip(
            FitInformationCriteria criteria,
            IFormatProvider provider)
        {
            string criterionText;
            if (criteria.IsAiccAvailable)
            {
                criterionText = "AICc shown; AIC = "
                    + Format(criteria.Aic, provider)
                    + ".";
            }
            else if (criteria.IsAicAvailable)
            {
                criterionText = "AIC shown; AICc unavailable ("
                    + AiccUnavailableReason(criteria)
                    + ").";
            }
            else
            {
                var aicReason = FirstReason(
                    criteria.AicUnavailableReason,
                    "unknown reason");
                var aiccReason = FirstReason(
                    criteria.AiccUnavailableReason,
                    aicReason);
                criterionText = "AIC unavailable ("
                    + aicReason
                    + "); AICc unavailable ("
                    + aiccReason
                    + ").";
            }

            var parameterText = criteria.LikelihoodMode == GaussianLikelihoodMode.EstimatedWeightedVariance
                ? "K = fitted parameters + 1 estimated variance multiplier; injection errors supply relative uncertainties."
                : "K = fitted parameters + 1 estimated common residual variance.";

            return string.Join(
                " ",
                criterionText,
                "n = included injections.",
                parameterText,
                "AICc uses the standard small-sample approximation for nonlinear fits.",
                "Compare only like-for-like fits.");
        }

        static string BuildFooter(
            AnalysisResult result,
            FitInformationCriteria criteria)
        {
            var memberCount = result?.Solution?.Solutions?.Count ?? 0;
            if (memberCount <= 1)
                return "Compare only like-for-like fits.";

            if (result?.Model?.ShouldFitIndividually == true)
            {
                return criteria.LikelihoodMode == GaussianLikelihoodMode.EstimatedWeightedVariance
                    ? "Pooled with one common variance multiplier for the injection errors; neither AIC nor AICc is a sum of member values."
                    : "Pooled with one common residual variance; neither AIC nor AICc is a sum of member values.";
            }

            return "Combined across all members using shared fitted parameters.";
        }

        static string AiccUnavailableReason(FitInformationCriteria criteria)
        {
            if (criteria.ObservationCount <= criteria.LikelihoodParameterCount + 1)
                return "n ≤ K + 1";

            return FirstReason(
                criteria.AiccUnavailableReason,
                "unknown reason");
        }

        static string FirstReason(params string[] reasons)
        {
            foreach (var reason in reasons)
            {
                if (!string.IsNullOrWhiteSpace(reason))
                    return reason;
            }

            return "unknown reason";
        }

        static string Format(double? value, IFormatProvider provider)
        {
            return value.HasValue
                ? value.Value.ToString("G6", provider)
                : "unavailable";
        }
    }
}
