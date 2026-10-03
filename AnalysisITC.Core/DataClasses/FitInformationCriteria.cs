using System;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;

namespace AnalysisITC.Core.Data
{
    /// <summary>
    /// AIC and finite-sample AICc for a saved analysis result or member fit.
    /// </summary>
    public sealed class FitInformationCriteria
    {
        public int ObservationCount { get; }
        public int FittedParameterCount { get; }
        public int LikelihoodParameterCount { get; }
        public GaussianLikelihoodMode LikelihoodMode { get; }
        public bool UsesKnownObservationSigmas => LikelihoodMode == GaussianLikelihoodMode.KnownObservationSigmas;

        public double? MinusTwoLogLikelihood { get; }
        public double? Aic { get; }
        public double? Aicc { get; }
        public double RawResidualSumOfSquares { get; internal set; }
        public double ResidualRmsdMicrojoules { get; internal set; }
        public double StandardizedResidualSumOfSquares { get; internal set; }
        public double LogSigmaSquaredSum { get; internal set; }

        public bool IsAicAvailable { get; }
        public bool IsAiccAvailable { get; }
        public string AicUnavailableReason { get; }
        public string AiccUnavailableReason { get; }

        internal FitInformationCriteria(
            int observationCount,
            int fittedParameterCount,
            int likelihoodParameterCount,
            GaussianLikelihoodMode likelihoodMode,
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
            LikelihoodMode = likelihoodMode;
            MinusTwoLogLikelihood = minusTwoLogLikelihood;
            Aic = aic;
            Aicc = aicc;
            IsAicAvailable = isAicAvailable;
            IsAiccAvailable = isAiccAvailable;
            AicUnavailableReason = aicUnavailableReason ?? string.Empty;
            AiccUnavailableReason = aiccUnavailableReason ?? string.Empty;
        }

        internal static FitInformationCriteria Restore(
            int observationCount, int fittedParameterCount, int likelihoodParameterCount,
            GaussianLikelihoodMode likelihoodMode, double? minusTwoLogLikelihood,
            double? aic, double? aicc, bool isAicAvailable, bool isAiccAvailable,
            string aicUnavailableReason, string aiccUnavailableReason,
            double rawResidualSumOfSquares, double residualRmsdMicrojoules,
            double standardizedResidualSumOfSquares, double logSigmaSquaredSum)
        {
            return new FitInformationCriteria(observationCount, fittedParameterCount,
                likelihoodParameterCount, likelihoodMode, minusTwoLogLikelihood, aic,
                aicc, isAicAvailable, isAiccAvailable, aicUnavailableReason,
                aiccUnavailableReason)
            {
                RawResidualSumOfSquares = rawResidualSumOfSquares,
                ResidualRmsdMicrojoules = residualRmsdMicrojoules,
                StandardizedResidualSumOfSquares = standardizedResidualSumOfSquares,
                LogSigmaSquaredSum = logSigmaSquaredSum,
            };
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

            var mode = solution.UseWeightedFitting
                ? GaussianLikelihoodMode.EstimatedWeightedVariance
                : GaussianLikelihoodMode.EstimatedCommonVariance;
            var evaluation = GaussianLikelihoodEvaluator.Evaluate(solution.Model, mode);
            return WithResidualStatistics(Calculate(evaluation, solution.Model.NumberOfParameters), evaluation);
        }

        internal static FitInformationCriteria Calculate(SolutionInterface solution)
        {
            if (solution == null) throw new ArgumentNullException(nameof(solution));
            if (solution.Model == null)
                throw new ArgumentException("The solution must contain a model.", nameof(solution));

            var mode = solution.UseWeightedFitting
                ? GaussianLikelihoodMode.EstimatedWeightedVariance
                : GaussianLikelihoodMode.EstimatedCommonVariance;
            var evaluation = GaussianLikelihoodEvaluator.Evaluate(solution.Model, mode);
            return WithResidualStatistics(Calculate(evaluation, solution.Model.NumberOfParameters), evaluation);
        }

        internal static FitInformationCriteria Calculate(SolutionInterface solution, bool weighted)
        {
            if (solution == null) throw new ArgumentNullException(nameof(solution));
            if (solution.Model == null)
                throw new ArgumentException("The solution must contain a model.", nameof(solution));
            var mode = weighted ? GaussianLikelihoodMode.EstimatedWeightedVariance
                : GaussianLikelihoodMode.EstimatedCommonVariance;
            var evaluation = GaussianLikelihoodEvaluator.Evaluate(solution.Model, mode);
            return WithResidualStatistics(Calculate(evaluation, solution.Model.NumberOfParameters), evaluation);
        }

        internal static FitInformationCriteria Calculate(GaussianLikelihoodEvaluation likelihood, int fittedParameterCount)
            => WithResidualStatistics(CalculateCore(likelihood, fittedParameterCount), likelihood);

        static FitInformationCriteria WithResidualStatistics(FitInformationCriteria criteria, GaussianLikelihoodEvaluation evaluation)
        {
            criteria.RawResidualSumOfSquares = evaluation.RawResidualSumOfSquares;
            criteria.ResidualRmsdMicrojoules = evaluation.RmsdMicrojoules;
            criteria.StandardizedResidualSumOfSquares = evaluation.StandardizedResidualSumOfSquares;
            criteria.LogSigmaSquaredSum = evaluation.LogSigmaSquaredSum;
            return criteria;
        }

        static FitInformationCriteria CalculateCore(
            GaussianLikelihoodEvaluation likelihood,
            int fittedParameterCount)
        {
            var observationCount = likelihood.ObservationCount;
            // Both information-criteria conventions estimate one residual variance.
            var likelihoodParameterCount = fittedParameterCount + 1;

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
                    likelihood.Mode,
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
                    likelihood.Mode,
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
                    likelihood.Mode,
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
                    likelihood.Mode,
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
                likelihood.Mode,
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
