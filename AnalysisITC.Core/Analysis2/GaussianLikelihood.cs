using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Analysis
{
    /// <summary>
    /// Selects the residual variance convention used by the Gaussian likelihood.
    /// </summary>
    internal enum GaussianLikelihoodMode
    {
        EstimatedCommonVariance,
        KnownObservationSigmas,
    }

    /// <summary>
    /// Immutable residual statistics and likelihood evaluation.
    /// </summary>
    internal sealed class GaussianLikelihoodEvaluation
    {
        internal GaussianLikelihoodMode Mode { get; }
        internal int ObservationCount { get; }

        internal bool HasFiniteResidualStatistics { get; }
        internal double RawResidualSumOfSquares { get; }
        internal double RmsdMicrojoules { get; }
        internal double MolarResidualSumOfSquares { get; }
        internal double? MolarRmsdJoulesPerMole { get; }

        internal double StandardizedResidualSumOfSquares { get; }
        internal double LogSigmaSquaredSum { get; }

        internal bool IsLikelihoodAvailable { get; }
        internal string UnavailableReason { get; }
        internal double MinusTwoLogLikelihood { get; }

        internal GaussianLikelihoodEvaluation(
            GaussianLikelihoodMode mode,
            int observationCount,
            bool hasFiniteResidualStatistics,
            double rawResidualSumOfSquares,
            double rmsdMicrojoules,
            double molarResidualSumOfSquares,
            double? molarRmsdJoulesPerMole,
            double standardizedResidualSumOfSquares,
            double logSigmaSquaredSum,
            bool isLikelihoodAvailable,
            string unavailableReason,
            double minusTwoLogLikelihood)
        {
            Mode = mode;
            ObservationCount = observationCount;
            HasFiniteResidualStatistics = hasFiniteResidualStatistics;
            RawResidualSumOfSquares = rawResidualSumOfSquares;
            RmsdMicrojoules = rmsdMicrojoules;
            MolarResidualSumOfSquares = molarResidualSumOfSquares;
            MolarRmsdJoulesPerMole = molarRmsdJoulesPerMole;
            StandardizedResidualSumOfSquares = standardizedResidualSumOfSquares;
            LogSigmaSquaredSum = logSigmaSquaredSum;
            IsLikelihoodAvailable = isLikelihoodAvailable;
            UnavailableReason = unavailableReason ?? string.Empty;
            MinusTwoLogLikelihood = minusTwoLogLikelihood;
        }

        internal static GaussianLikelihoodEvaluation Empty(GaussianLikelihoodMode mode)
        {
            return new GaussianLikelihoodEvaluation(
                mode,
                0,
                true,
                0,
                double.NaN,
                0,
                null,
                0,
                0,
                false,
                GaussianLikelihoodEvaluator.NoObservationsReason,
                double.NaN);
        }

        internal static GaussianLikelihoodEvaluation Unavailable(
            GaussianLikelihoodMode mode,
            int observationCount,
            double rawResidualSumOfSquares,
            double rmsdMicrojoules,
            double standardizedResidualSumOfSquares,
            double logSigmaSquaredSum,
            string reason)
        {
            return new GaussianLikelihoodEvaluation(
                mode,
                observationCount,
                false,
                rawResidualSumOfSquares,
                rmsdMicrojoules,
                double.NaN,
                null,
                standardizedResidualSumOfSquares,
                logSigmaSquaredSum,
                false,
                reason,
                double.NaN);
        }
    }

    /// <summary>
    /// Computes residual statistics and Gaussian likelihoods without mutating a model.
    /// </summary>
    internal static class GaussianLikelihoodEvaluator
    {
        internal const string NoObservationsReason = "No included observations.";
        internal const string NonFiniteResidualReason = "A residual is non-finite.";
        internal const string NonFiniteResidualStatisticsReason = "Residual statistics are non-finite.";
        internal const string InvalidSigmaReason = "An observation sigma is non-positive or non-finite.";
        internal const string NonFiniteWeightedStatisticsReason = "Weighted residual statistics are non-finite.";
        internal const string ZeroResidualVarianceReason = "The residual sum of squares is zero, so the estimated variance likelihood is unavailable.";
        internal const string NonFiniteLikelihoodReason = "The Gaussian likelihood is non-finite.";

        internal static GaussianLikelihoodEvaluation Evaluate(
            Model model,
            GaussianLikelihoodMode mode)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            var included = model.Data?.Injections?
                .Where(injection => injection.Include)
                .ToList() ?? new List<InjectionData>();

            if (included.Count == 0)
                return GaussianLikelihoodEvaluation.Empty(mode);

            double rawRss = 0;
            double molarRss = 0;
            double standardizedRss = 0;
            double logSigmaSquaredSum = 0;

            foreach (var injection in included)
            {
                double residual;
                try
                {
                    residual = model.Residual(injection);
                }
                catch (Exception ex)
                {
                    var context =
                        $"[GaussianLikelihood] {model.GetType().Name}, injection {injection.ID}: "
                        + $"residual evaluation failed; AIC/RMSD marked unavailable. "
                        + $"{ex.GetType().Name}: {ex.Message}";
                    AppEventHandler.PrintAndLog(context, code: "likelihood");

#if DEBUG
                    // During development, make swallowed evaluation failures visible
                    // immediately while retaining the release-mode graceful fallback.
                    AppEventHandler.DisplayHandledException(ex);
#else
                    // Preserve the original exception and stack trace in the diagnostic log.
                    AppEventHandler.AddLog(ex);
#endif

                    return GaussianLikelihoodEvaluation.Unavailable(
                        mode,
                        included.Count,
                        double.NaN,
                        double.NaN,
                        double.NaN,
                        double.NaN,
                        NonFiniteResidualReason);
                }

                if (!FWEMath.IsFinite(residual))
                {
                    return GaussianLikelihoodEvaluation.Unavailable(
                        mode,
                        included.Count,
                        double.NaN,
                        double.NaN,
                        double.NaN,
                        double.NaN,
                        NonFiniteResidualReason);
                }

                var squaredResidual = residual * residual;
                if (!FWEMath.IsFinite(squaredResidual)
                    || !TryAdd(rawRss, squaredResidual, out rawRss))
                {
                    return GaussianLikelihoodEvaluation.Unavailable(
                        mode,
                        included.Count,
                        double.NaN,
                        double.NaN,
                        double.NaN,
                        double.NaN,
                        NonFiniteResidualStatisticsReason);
                }

                var injectionMass = injection.InjectionMass;
                if (!FWEMath.IsFinite(injectionMass) || injectionMass <= 0)
                {
                    // Molar RMSD is display-only. Synthetic or incomplete data may
                    // not have an injected amount, but that must not make the raw
                    // residual statistics or likelihood unavailable.
                    molarRss = double.NaN;
                }
                else if (FWEMath.IsFinite(molarRss))
                {
                    var molarResidual = residual / injectionMass;
                    var squaredMolarResidual = molarResidual * molarResidual;
                    if (!FWEMath.IsFinite(squaredMolarResidual)
                        || !TryAdd(molarRss, squaredMolarResidual, out molarRss))
                    {
                        molarRss = double.NaN;
                    }
                }

                if (mode == GaussianLikelihoodMode.KnownObservationSigmas)
                {
                    var sigma = Model.GetSigmaForWeighting(injection, included);
                    if (!FWEMath.IsFinite(sigma) || sigma <= 0)
                    {
                        return GaussianLikelihoodEvaluation.Unavailable(
                            mode,
                            included.Count,
                            rawRss,
                            double.NaN,
                            double.NaN,
                            double.NaN,
                            InvalidSigmaReason);
                    }

                    var standardizedResidual = residual / sigma;
                    var standardizedSquared = standardizedResidual * standardizedResidual;
                    var logSigmaSquared = 2.0 * Math.Log(sigma);
                    if (!FWEMath.IsFinite(standardizedSquared)
                        || !FWEMath.IsFinite(logSigmaSquared)
                        || !TryAdd(standardizedRss, standardizedSquared, out standardizedRss)
                        || !TryAdd(logSigmaSquaredSum, logSigmaSquared, out logSigmaSquaredSum))
                    {
                        return GaussianLikelihoodEvaluation.Unavailable(
                            mode,
                            included.Count,
                            rawRss,
                            double.NaN,
                            double.NaN,
                            double.NaN,
                            NonFiniteWeightedStatisticsReason);
                    }
                }
            }

            return Finalize(mode, included.Count, rawRss, molarRss, standardizedRss, logSigmaSquaredSum);
        }

        internal static GaussianLikelihoodEvaluation Evaluate(
            GlobalModel model,
            GaussianLikelihoodMode mode)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            var members = model.Models ?? new List<Model>();
            if (members.Count == 0)
                return GaussianLikelihoodEvaluation.Empty(mode);

            var components = members.Select(member => Evaluate(member, mode));
            return Combine(components);
        }

        internal static GaussianLikelihoodEvaluation Combine(
            IEnumerable<GaussianLikelihoodEvaluation> components)
        {
            if (components == null) throw new ArgumentNullException(nameof(components));

            var list = components.ToList();
            if (list.Any(component => component == null))
                throw new ArgumentNullException(nameof(components), "A likelihood component cannot be null.");

            if (list.Count == 0)
                return GaussianLikelihoodEvaluation.Empty(GaussianLikelihoodMode.EstimatedCommonVariance);

            var mode = list[0].Mode;
            if (list.Any(component => component.Mode != mode))
                throw new ArgumentException("Likelihood components must use the same mode.", nameof(components));

            var observations = 0;
            foreach (var component in list)
            {
                if (component.ObservationCount < 0
                    || !TryAdd(observations, component.ObservationCount, out observations))
                {
                    return GaussianLikelihoodEvaluation.Unavailable(
                        mode,
                        observations,
                        double.NaN,
                        double.NaN,
                        double.NaN,
                        double.NaN,
                        NonFiniteResidualStatisticsReason);
                }
            }

            var invalid = list.FirstOrDefault(component => !component.HasFiniteResidualStatistics);
            if (invalid != null)
            {
                return GaussianLikelihoodEvaluation.Unavailable(
                    mode,
                    observations,
                    double.NaN,
                    double.NaN,
                    double.NaN,
                    double.NaN,
                    FirstReason(invalid, NonFiniteResidualStatisticsReason));
            }

            var rawRss = 0.0;
            var molarRss = 0.0;
            var standardizedRss = 0.0;
            var logSigmaSquaredSum = 0.0;

            foreach (var component in list)
            {
                if (!TryAdd(rawRss, component.RawResidualSumOfSquares, out rawRss)
                    || !TryAdd(standardizedRss, component.StandardizedResidualSumOfSquares, out standardizedRss)
                    || !TryAdd(logSigmaSquaredSum, component.LogSigmaSquaredSum, out logSigmaSquaredSum))
                {
                    return GaussianLikelihoodEvaluation.Unavailable(
                        mode,
                        observations,
                        double.NaN,
                        double.NaN,
                        double.NaN,
                        double.NaN,
                        NonFiniteResidualStatisticsReason);
                }

                if (FWEMath.IsFinite(molarRss)
                    && !TryAdd(molarRss, component.MolarResidualSumOfSquares, out molarRss))
                {
                    molarRss = double.NaN;
                }
            }

            return Finalize(mode, observations, rawRss, molarRss, standardizedRss, logSigmaSquaredSum);
        }

        static GaussianLikelihoodEvaluation Finalize(
            GaussianLikelihoodMode mode,
            int observationCount,
            double rawRss,
            double molarRss,
            double standardizedRss,
            double logSigmaSquaredSum)
        {
            if (observationCount <= 0)
                return GaussianLikelihoodEvaluation.Empty(mode);

            if (!FWEMath.IsFinite(rawRss)
                || rawRss < 0
                || !FWEMath.IsFinite(standardizedRss)
                || standardizedRss < 0
                || !FWEMath.IsFinite(logSigmaSquaredSum))
            {
                return GaussianLikelihoodEvaluation.Unavailable(
                    mode,
                    observationCount,
                    rawRss,
                    double.NaN,
                    standardizedRss,
                    logSigmaSquaredSum,
                    NonFiniteResidualStatisticsReason);
            }

            var meanSquaredResidual = rawRss / observationCount;
            var rmsd = 1000000.0 * Math.Sqrt(meanSquaredResidual);
            if (!FWEMath.IsFinite(meanSquaredResidual)
                || !FWEMath.IsFinite(rmsd))
            {
                return GaussianLikelihoodEvaluation.Unavailable(
                    mode,
                    observationCount,
                    rawRss,
                    double.NaN,
                    standardizedRss,
                    logSigmaSquaredSum,
                    NonFiniteResidualStatisticsReason);
            }

            double? molarRmsd = null;
            if (FWEMath.IsFinite(molarRss) && molarRss >= 0)
            {
                var meanSquaredMolarResidual = molarRss / observationCount;
                var candidate = Math.Sqrt(meanSquaredMolarResidual);
                if (FWEMath.IsFinite(meanSquaredMolarResidual) && FWEMath.IsFinite(candidate))
                    molarRmsd = candidate;
            }

            if (mode == GaussianLikelihoodMode.EstimatedCommonVariance && rawRss == 0)
            {
                return new GaussianLikelihoodEvaluation(
                    mode,
                    observationCount,
                    true,
                    rawRss,
                    rmsd,
                    molarRss,
                    molarRmsd,
                    standardizedRss,
                    logSigmaSquaredSum,
                    false,
                    ZeroResidualVarianceReason,
                    double.NaN);
            }

            double minusTwoLogLikelihood;
            if (mode == GaussianLikelihoodMode.EstimatedCommonVariance)
            {
                minusTwoLogLikelihood = observationCount
                    * (Math.Log(2.0 * Math.PI)
                        + Math.Log(rawRss)
                        - Math.Log(observationCount)
                        + 1.0);
            }
            else
            {
                minusTwoLogLikelihood = standardizedRss
                    + observationCount * Math.Log(2.0 * Math.PI)
                    + logSigmaSquaredSum;
            }

            if (!FWEMath.IsFinite(minusTwoLogLikelihood))
            {
                return new GaussianLikelihoodEvaluation(
                    mode,
                    observationCount,
                    true,
                    rawRss,
                    rmsd,
                    molarRss,
                    molarRmsd,
                    standardizedRss,
                    logSigmaSquaredSum,
                    false,
                    NonFiniteLikelihoodReason,
                    double.NaN);
            }

            return new GaussianLikelihoodEvaluation(
                mode,
                observationCount,
                true,
                rawRss,
                rmsd,
                molarRss,
                molarRmsd,
                standardizedRss,
                logSigmaSquaredSum,
                true,
                string.Empty,
                minusTwoLogLikelihood);
        }

        static string FirstReason(GaussianLikelihoodEvaluation evaluation, string fallback)
        {
            return string.IsNullOrWhiteSpace(evaluation.UnavailableReason)
                ? fallback
                : evaluation.UnavailableReason;
        }

        static bool TryAdd(double left, double right, out double sum)
        {
            sum = left + right;
            return FWEMath.IsFinite(sum);
        }

        static bool TryAdd(int left, int right, out int sum)
        {
            long candidate = (long)left + right;
            if (candidate < 0 || candidate > int.MaxValue)
            {
                sum = 0;
                return false;
            }

            sum = (int)candidate;
            return true;
        }
    }
}
