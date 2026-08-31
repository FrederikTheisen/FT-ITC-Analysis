using System;

using AnalysisITC.Core.Analysis;

namespace AnalysisITC.Core.Data
{
    /// <summary>
    /// AIC and finite-sample AICc for a saved analysis result.
    /// </summary>
    public sealed class FitInformationCriteria
    {
        public int ObservationCount { get; }
        public int FittedParameterCount { get; }
        public int LikelihoodParameterCount { get; }
        public bool UsesKnownObservationSigmas { get; }

        public double? MinusTwoLogLikelihood { get; }
        public double? Aic { get; }
        public double? Aicc { get; }

        public bool IsAicAvailable { get; }
        public bool IsAiccAvailable { get; }
        public string AicUnavailableReason { get; }
        public string AiccUnavailableReason { get; }

        internal FitInformationCriteria(
            int observationCount,
            int fittedParameterCount,
            int likelihoodParameterCount,
            bool usesKnownObservationSigmas,
            double? minusTwoLogLikelihood,
            double? aic,
            double? aicc,
            bool isAicAvailable,
            bool isAiccAvailable,
            string aicUnavailableReason,
            string aiccUnavailableReason)
        {
            ObservationCount = observationCount;
            FittedParameterCount = fittedParameterCount;
            LikelihoodParameterCount = likelihoodParameterCount;
            UsesKnownObservationSigmas = usesKnownObservationSigmas;
            MinusTwoLogLikelihood = minusTwoLogLikelihood;
            Aic = aic;
            Aicc = aicc;
            IsAicAvailable = isAicAvailable;
            IsAiccAvailable = isAiccAvailable;
            AicUnavailableReason = aicUnavailableReason ?? string.Empty;
            AiccUnavailableReason = aiccUnavailableReason ?? string.Empty;
        }
    }

    internal static class FitInformationCriteriaCalculator
    {
        internal const string NonFiniteAicReason = "AIC is non-finite.";
        internal const string NonFiniteAiccReason = "AICc is non-finite.";
        internal const string AiccSampleSizeReason = "Unavailable (n ≤ K + 1)";

        internal static FitInformationCriteria Calculate(GlobalSolution solution)
        {
            if (solution == null) throw new ArgumentNullException(nameof(solution));
            if (solution.Model == null)
                throw new ArgumentException("The solution must contain a model.", nameof(solution));

            var usesKnownObservationSigmas = solution.UseWeightedFitting;
            var mode = usesKnownObservationSigmas
                ? GaussianLikelihoodMode.KnownObservationSigmas
                : GaussianLikelihoodMode.EstimatedCommonVariance;
            var likelihood = GaussianLikelihoodEvaluator.Evaluate(solution.Model, mode);
            var observationCount = likelihood.ObservationCount;
            var fittedParameterCount = solution.Model.NumberOfParameters;
            var likelihoodParameterCount = fittedParameterCount
                + (usesKnownObservationSigmas ? 0 : 1);

            if (!likelihood.IsLikelihoodAvailable
                || !IsFinite(likelihood.MinusTwoLogLikelihood))
            {
                var reason = string.IsNullOrWhiteSpace(likelihood.UnavailableReason)
                    ? GaussianLikelihoodEvaluator.NonFiniteLikelihoodReason
                    : likelihood.UnavailableReason;
                return new FitInformationCriteria(
                    observationCount,
                    fittedParameterCount,
                    likelihoodParameterCount,
                    usesKnownObservationSigmas,
                    null,
                    null,
                    null,
                    false,
                    false,
                    reason,
                    reason);
            }

            var minusTwoLogLikelihood = likelihood.MinusTwoLogLikelihood;
            var aic = minusTwoLogLikelihood + 2.0 * likelihoodParameterCount;
            if (!IsFinite(aic))
            {
                return new FitInformationCriteria(
                    observationCount,
                    fittedParameterCount,
                    likelihoodParameterCount,
                    usesKnownObservationSigmas,
                    minusTwoLogLikelihood,
                    null,
                    null,
                    false,
                    false,
                    NonFiniteAicReason,
                    NonFiniteAicReason);
            }

            if (observationCount <= likelihoodParameterCount + 1)
            {
                return new FitInformationCriteria(
                    observationCount,
                    fittedParameterCount,
                    likelihoodParameterCount,
                    usesKnownObservationSigmas,
                    minusTwoLogLikelihood,
                    aic,
                    null,
                    true,
                    false,
                    string.Empty,
                    AiccSampleSizeReason);
            }

            var aicc = aic
                + 2.0 * likelihoodParameterCount * (likelihoodParameterCount + 1.0)
                    / (observationCount - likelihoodParameterCount - 1.0);
            if (!IsFinite(aicc))
            {
                return new FitInformationCriteria(
                    observationCount,
                    fittedParameterCount,
                    likelihoodParameterCount,
                    usesKnownObservationSigmas,
                    minusTwoLogLikelihood,
                    aic,
                    null,
                    true,
                    false,
                    string.Empty,
                    NonFiniteAiccReason);
            }

            return new FitInformationCriteria(
                observationCount,
                fittedParameterCount,
                likelihoodParameterCount,
                usesKnownObservationSigmas,
                minusTwoLogLikelihood,
                aic,
                aicc,
                true,
                true,
                string.Empty,
                string.Empty);
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
