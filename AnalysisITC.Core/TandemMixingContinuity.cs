using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using MathNet.Numerics;

namespace AnalysisITC.Core.Processing
{
    public enum TandemMixingCriterion
    {
        /// <summary>Minimise the RMSD of a one-site fit to the concatenated isotherm.</summary>
        OneSiteFit,

        /// <summary>
        /// For each transition, minimise the residual of a quadratic through the included
        /// injections on either side of it.
        /// </summary>
        ModelFree,
    }

    /// <summary>
    /// Model-free tandem mixing search. Each transition is solved in order: the candidate
    /// concentrations are produced by the real back-mixing bookkeeping, and the score is the residual
    /// sum of squares of one quadratic in molar ratio through the last included injections
    /// before the transition and the first included injections after it, modulated by a weak
    /// preference for a typical mixing fraction.
    /// </summary>
    internal static class TandemContinuityScanner
    {
        public const int PointsPerSide = 6;
        internal const int PolynomialOrder = 2;
        public const double ScanStep = 0.02;

        internal static MixingFractionBias Bias => MixingFractionBias.Default;

        static readonly IReadOnlyList<double> ScanFractions = TandemMixingScanner.MixingFractionsForStep(
            TandemMixingScanner.DefaultMinimumMixingFraction,
            TandemMixingScanner.AdaptiveMaximumMixingFraction,
            ScanStep);

        // Coarse scan plus an estimate of the medium and fine neighbour searches.
        static int EvaluationsPerTransition => ScanFractions.Count + 4 + 8;

        public static TandemMixingScanPoint FindBest(
            IReadOnlyList<ExperimentData> sources,
            TandemConcatenation.BackMixingSettings settings,
            Action<int, int> reportProgress,
            DilutionMethod dilutionMethod)
        {
            var transitionCount = sources.Count - 1;
            var progressTotal = transitionCount * EvaluationsPerTransition;
            var completed = 0;

            var (scanExperiment, segments) = TandemMixingScanner.BuildScanExperiment(sources);
            var fractions = new double[transitionCount];

            for (var transition = 0; transition < transitionCount; transition++)
            {
                var fraction = SolveTransition(
                    scanExperiment,
                    segments,
                    settings,
                    dilutionMethod,
                    fractions,
                    transition,
                    () =>
                    {
                        completed++;
                        reportProgress?.Invoke(Math.Min(completed, progressTotal), progressTotal);
                    });

                if (double.IsNaN(fraction)) return null;

                fractions[transition] = fraction;
                AppEventHandler.PrintAndLog(
                    $"Model-free tandem mixing transition {transition + 1}: mix={(100 * fraction).ToString("0.###", CultureInfo.InvariantCulture)}%",
                    1);
            }

            reportProgress?.Invoke(progressTotal, progressTotal);

            return new TandemMixingScanPoint(
                fractions,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                "ModelFree",
                0);
        }

        /// <summary>Returns the selected fraction for one transition, or NaN when it cannot be scored.</summary>
        static double SolveTransition(
            ExperimentData experiment,
            IList<TandemConcatenation.TandemInjectionSegment> segments,
            TandemConcatenation.BackMixingSettings settings,
            DilutionMethod dilutionMethod,
            double[] solvedFractions,
            int transition,
            Action reportEvaluation)
        {
            // Segments 0..transition+1 only depend on the fractions up to this transition, so the
            // unsolved later fractions are left at zero.
            var candidateFractions = solvedFractions.ToArray();

            (List<(double x, double y)> pre, List<(double x, double y)> post) Points(double fraction)
            {
                candidateFractions[transition] = fraction;
                TandemConcatenation.ProcessInjectionsWithBackMixingModel(
                    experiment, segments, settings, candidateFractions, dilutionMethod);

                return TransitionWindow(experiment, segments, transition);
            }

            var (prePoints, postPoints) = Points(0.0);
            if (prePoints.Count == 0 || postPoints.Count == 0 || prePoints.Count + postPoints.Count <= PolynomialOrder) return double.NaN;

            double Score(double fraction)
            {
                var (pre, post) = Points(fraction);
                reportEvaluation();

                var rss = ResidualSumOfSquares(pre.Concat(post).ToList());
                return double.IsNaN(rss) ? double.PositiveInfinity : Bias.Apply(rss, fraction);
            }

            // Scan 0-100% in 2% steps, then refine around the best point in 1% and 0.2% steps,
            // as the one-site search does.
            var bestFraction = double.NaN;
            var bestScore = double.PositiveInfinity;
            foreach (var fraction in ScanFractions)
            {
                var score = Score(fraction);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestFraction = fraction;
                }
            }

            if (double.IsNaN(bestFraction)) return double.NaN;

            foreach (var step in new[] { TandemMixingScanner.AdaptiveMediumRefinementStep, TandemMixingScanner.AdaptiveRefinementStep })
            {
                bool improved;
                do
                {
                    improved = false;
                    foreach (var direction in new[] { -1.0, 1.0 })
                    {
                        var candidate = Math.Round(bestFraction + direction * step, 10);
                        if (candidate < TandemMixingScanner.DefaultMinimumMixingFraction
                            || candidate > TandemMixingScanner.AdaptiveMaximumMixingFraction) continue;

                        var score = Score(candidate);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestFraction = candidate;
                            improved = true;
                            break;
                        }
                    }
                } while (improved);
            }

            return bestFraction;
        }

        /// <summary>
        /// The last included injections before the transition and the first included injections after
        /// it, for the concentrations currently stored in the experiment.
        /// </summary>
        internal static (List<(double x, double y)> pre, List<(double x, double y)> post) TransitionWindow(
            ExperimentData experiment,
            IList<TandemConcatenation.TandemInjectionSegment> segments,
            int transition)
        {
            var pre = IncludedPoints(experiment, segments[transition]);
            var post = IncludedPoints(experiment, segments[transition + 1]);
            return (pre.Skip(Math.Max(0, pre.Count - PointsPerSide)).ToList(), post.Take(PointsPerSide).ToList());
        }

        internal static double ResidualSumOfSquares(IReadOnlyList<(double x, double y)> points)
        {
            var x = points.Select(point => point.x).ToArray();
            var y = points.Select(point => point.y).ToArray();
            var coefficients = Fit.Polynomial(x, y, PolynomialOrder);

            return points.Sum(point =>
            {
                var residual = point.y - Polynomial.Evaluate(point.x, coefficients);
                return residual * residual;
            });
        }

        static List<(double x, double y)> IncludedPoints(ExperimentData experiment, TandemConcatenation.TandemInjectionSegment segment)
        {
            var points = new List<(double x, double y)>(segment.InjectionCount);

            for (var index = segment.InjectionNumStart; index < segment.InjectionNumStart + segment.InjectionCount; index++)
            {
                var injection = experiment.Injections[index];
                if (!injection.Include) continue;

                points.Add((MidpointMolarRatio(experiment, index), injection.Enthalpy));
            }

            return points;
        }

        /// <summary>The molar ratio halfway between the states before and after an injection.</summary>
        internal static double MidpointMolarRatio(ExperimentData experiment, int index)
        {
            var injection = experiment.Injections[index];
            var (cellBefore, titrantBefore) = PreInjectionState(experiment, index);
            return 0.5 * (titrantBefore / cellBefore + injection.ActualTitrantConcentration / injection.ActualCellConcentration);
        }

        static (double cell, double titrant) PreInjectionState(ExperimentData experiment, int index)
        {
            // Same convention as the models: a segment start uses its stored back-mixed state.
            var segment = experiment.Segments?.FirstOrDefault(candidate => candidate.FirstInjectionID == index);
            if (segment != null) return (segment.SegmentInitialActiveCellConc, segment.SegmentInitialActiveTitrantConc);
            if (index <= 0) return (experiment.CellConcentration.Value, 0.0);

            var previous = experiment.Injections[index - 1];
            return (previous.ActualCellConcentration, previous.ActualTitrantConcentration);
        }
    }
}
