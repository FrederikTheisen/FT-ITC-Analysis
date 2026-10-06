using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using Accord.Math.Optimization;
using System.ComponentModel;

namespace AnalysisITC.Core.Processing
{
    public enum TandemMixingCriterion
    {
        /// <summary>Minimise the RMSD of a one-site fit to the concatenated isotherm.</summary>
        [Description("One-site")]
        OneSiteFit,

        /// <summary>
        /// For each transition, minimise the residual of a monotonic cubic through the included
        /// injections on either side of it.
        /// </summary>
        [Description("Model-free")]
        ModelFree,
    }

    /// <summary>
    /// Model-free tandem mixing search. Each transition is solved in order: the candidate
    /// concentrations are produced by the real back-mixing bookkeeping, and the score is the residual
    /// sum of squares of one monotonic cubic in molar ratio through the included injections among the
    /// last injections before the transition and the first injections after it, modulated by a weak
    /// preference for a typical mixing fraction.
    /// </summary>
    internal static class TandemContinuityScanner
    {
        public const int PointsPerSide = 6;
        public const int MinimumIncludedPointsPerSide = 3;
        internal const int PolynomialOrder = 3;

        // Slope sign is enforced at this many evenly spaced points across the window.
        const int MonotoneConstraintPointCount = 41;
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

            // Exclusions decide the point counts, so they do not depend on the fraction.
            var (prePoints, postPoints) = Points(0.0);
            if (prePoints.Count < MinimumIncludedPointsPerSide || postPoints.Count < MinimumIncludedPointsPerSide)
            {
                AppEventHandler.PrintAndLog(
                    $"Model-free tandem mixing transition {transition + 1}: too few included injections " +
                    $"(before={prePoints.Count}, after={postPoints.Count}, minimum={MinimumIncludedPointsPerSide} on each side).");
                return double.NaN;
            }

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
        /// The included injections among the last injections before the transition and the first
        /// injections after it, for the concentrations currently stored in the experiment. The window
        /// is fixed by injection position: an excluded injection is dropped, not replaced by one further
        /// from the transition, so exclusions never widen the molar-ratio span of the fit.
        /// </summary>
        internal static (List<(double x, double y)> pre, List<(double x, double y)> post) TransitionWindow(
            ExperimentData experiment,
            IList<TandemConcatenation.TandemInjectionSegment> segments,
            int transition)
        {
            var before = segments[transition];
            var after = segments[transition + 1];
            var preCount = Math.Min(PointsPerSide, before.InjectionCount);
            var postCount = Math.Min(PointsPerSide, after.InjectionCount);

            return (
                IncludedPoints(experiment, before.InjectionNumStart + before.InjectionCount - preCount, preCount),
                IncludedPoints(experiment, after.InjectionNumStart, postCount));
        }

        internal static double ResidualSumOfSquares(IReadOnlyList<(double x, double y)> points)
        {
            return FitWindow(points)?.Rss ?? double.NaN;
        }

        /// <summary>
        /// A polynomial in molar ratio, fitted by least squares to the window. The isotherm of a single
        /// binding process never changes direction, so the slope is constrained to keep one sign across
        /// the window; both directions are tried and the better fit is kept.
        /// </summary>
        internal sealed class WindowFit
        {
            public double Center { get; }
            public double HalfWidth { get; }
            public double[] Coefficients { get; }
            public double Rss { get; }

            public WindowFit(double center, double halfWidth, double[] coefficients, double rss)
            {
                Center = center;
                HalfWidth = halfWidth;
                Coefficients = coefficients;
                Rss = rss;
            }

            public double Evaluate(double x)
            {
                var t = (x - Center) / HalfWidth;
                var value = 0.0;
                for (var k = Coefficients.Length - 1; k >= 0; k--) value = value * t + Coefficients[k];
                return value;
            }
        }

        internal static WindowFit FitWindow(IReadOnlyList<(double x, double y)> points)
        {
            var terms = PolynomialOrder + 1;
            var min = points.Min(point => point.x);
            var max = points.Max(point => point.x);
            if (points.Count < terms || !(max > min)) return null;

            // Work in t = (x - centre) / half-width, which spans -1..1 and keeps the normal matrix well conditioned.
            var center = 0.5 * (min + max);
            var halfWidth = 0.5 * (max - min);
            var normal = new double[terms, terms];
            var linear = new double[terms];
            foreach (var (x, y) in points)
            {
                var t = (x - center) / halfWidth;
                var powers = Enumerable.Range(0, terms).Select(k => Math.Pow(t, k)).ToArray();
                for (var i = 0; i < terms; i++)
                {
                    linear[i] -= powers[i] * y;
                    for (var j = 0; j < terms; j++) normal[i, j] += powers[i] * powers[j];
                }
            }

            WindowFit Solve(double slopeSign)
            {
                // Rows are slopeSign * p'(t) at evenly spaced t; the solver keeps each row >= 0.
                var constraints = new double[MonotoneConstraintPointCount, terms];
                for (var row = 0; row < MonotoneConstraintPointCount; row++)
                {
                    var t = -1.0 + 2.0 * row / (MonotoneConstraintPointCount - 1);
                    for (var k = 1; k < terms; k++) constraints[row, k] = slopeSign * k * Math.Pow(t, k - 1);
                }

                var solver = new GoldfarbIdnani(
                    (double[,])normal.Clone(), (double[])linear.Clone(), constraints, new double[constraints.GetLength(0)], 0);
                if (!solver.Minimize()) return null;

                var coefficients = solver.Solution.ToArray();
                var fit = new WindowFit(center, halfWidth, coefficients, 0);
                var rss = points.Sum(point =>
                {
                    var residual = point.y - fit.Evaluate(point.x);
                    return residual * residual;
                });
                return new WindowFit(center, halfWidth, coefficients, rss);
            }

            var rising = Solve(1);
            var falling = Solve(-1);
            if (rising == null) return falling;
            if (falling == null) return rising;
            return rising.Rss <= falling.Rss ? rising : falling;
        }

        static List<(double x, double y)> IncludedPoints(ExperimentData experiment, int start, int count)
        {
            var points = new List<(double x, double y)>(count);

            for (var index = start; index < start + count; index++)
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
