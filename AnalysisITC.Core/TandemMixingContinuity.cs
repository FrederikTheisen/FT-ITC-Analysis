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
        /// Minimise the discontinuity of a quadratic fitted through the injections on either side
        /// of each transition, regularised by an empirical mixing fraction prior.
        /// </summary>
        ModelFree,
    }

    /// <summary>
    /// Model-free tandem mixing search. Each transition is solved in order: the candidate
    /// concentrations are produced by the real back-mixing bookkeeping, and the score measures how
    /// well one quadratic in molar ratio describes the included heats just before and just after
    /// the transition.
    /// </summary>
    internal static class TandemContinuityScanner
    {
        public const double PriorCenter = 0.043;
        public const double PriorWidth = 0.12;
        public const int PointsPerSide = 6;
        public const double Tolerance = 1e-4;

        const int PolynomialOrder = 2;
        static readonly double GoldenRatio = (Math.Sqrt(5) - 1) / 2;

        static int EvaluationsPerTransition =>
            2 + (int)Math.Ceiling(Math.Log(Tolerance) / Math.Log(GoldenRatio));

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

                var pre = IncludedPoints(experiment, segments[transition]);
                var post = IncludedPoints(experiment, segments[transition + 1]);
                return (pre.Skip(Math.Max(0, pre.Count - PointsPerSide)).ToList(), post.Take(PointsPerSide).ToList());
            }

            // The pre-transition points do not depend on this transition's fraction.
            var (prePoints, postPoints) = Points(0.0);
            if (prePoints.Count <= PolynomialOrder + 1 || postPoints.Count == 0) return double.NaN;

            var sigmaSquared = ResidualSumOfSquares(prePoints) / (prePoints.Count - (PolynomialOrder + 1));
            if (!(sigmaSquared > 0) || double.IsInfinity(sigmaSquared)) return double.NaN;

            double Score(double fraction)
            {
                var (pre, post) = Points(fraction);
                reportEvaluation();

                var rss = ResidualSumOfSquares(pre.Concat(post).ToList());
                var z = (fraction - PriorCenter) / PriorWidth;
                var score = rss / (2 * sigmaSquared) + 0.5 * z * z;
                return double.IsNaN(score) ? double.PositiveInfinity : score;
            }

            return GoldenSectionMinimum(Score, TandemMixingScanner.DefaultMinimumMixingFraction, TandemMixingScanner.AdaptiveMaximumMixingFraction);
        }

        static double ResidualSumOfSquares(IReadOnlyList<(double x, double y)> points)
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

                var (cellBefore, titrantBefore) = PreInjectionState(experiment, index);
                var x = 0.5 * (titrantBefore / cellBefore + injection.ActualTitrantConcentration / injection.ActualCellConcentration);
                points.Add((x, injection.Enthalpy));
            }

            return points;
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

        static double GoldenSectionMinimum(Func<double, double> function, double lower, double upper)
        {
            var a = lower;
            var b = upper;
            var c = b - GoldenRatio * (b - a);
            var d = a + GoldenRatio * (b - a);
            var fc = function(c);
            var fd = function(d);

            while (b - a > Tolerance)
            {
                if (fc < fd)
                {
                    b = d;
                    d = c;
                    fd = fc;
                    c = b - GoldenRatio * (b - a);
                    fc = function(c);
                }
                else
                {
                    a = c;
                    c = d;
                    fc = fd;
                    d = a + GoldenRatio * (b - a);
                    fd = function(d);
                }
            }

            return fc < fd ? c : d;
        }
    }
}
