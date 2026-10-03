using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using MathNet.Numerics.LinearAlgebra.Double;

namespace AnalysisITC.Core.Processing
{
    public enum TandemMixingCriterion
    {
        /// <summary>Minimise the RMSD of a one-site fit to the concatenated isotherm.</summary>
        OneSiteFit,

        /// <summary>
        /// Minimise the discontinuity between the isotherms on either side of each transition,
        /// measured with a penalised spline and regularised by an empirical mixing fraction prior.
        /// </summary>
        ModelFree,
    }

    /// <summary>
    /// Sensitivity information for one transition of a model-free tandem mixing search.
    /// Scores are negative log-likelihoods: the data term is RSS / (2 sigma^2) and the prior
    /// term is 0.5 ((f - center) / width)^2.
    /// </summary>
    public sealed class TandemMixingTransitionProfile
    {
        public int TransitionIndex { get; }
        public double BestMixingFraction { get; }
        public double BestScore { get; }
        public IReadOnlyList<double> MixingFractions { get; }
        public IReadOnlyList<double> DataScores { get; }
        public IReadOnlyList<double> PriorScores { get; }
        public double Sigma { get; }
        public double SmoothingParameter { get; }
        public int PreTransitionPointCount { get; }
        public int PostTransitionPointCount { get; }

        internal TandemMixingTransitionProfile(
            int transitionIndex,
            double bestMixingFraction,
            double bestScore,
            IReadOnlyList<double> mixingFractions,
            IReadOnlyList<double> dataScores,
            IReadOnlyList<double> priorScores,
            double sigma,
            double smoothingParameter,
            int preTransitionPointCount,
            int postTransitionPointCount)
        {
            TransitionIndex = transitionIndex;
            BestMixingFraction = bestMixingFraction;
            BestScore = bestScore;
            MixingFractions = mixingFractions.ToList();
            DataScores = dataScores.ToList();
            PriorScores = priorScores.ToList();
            Sigma = sigma;
            SmoothingParameter = smoothingParameter;
            PreTransitionPointCount = preTransitionPointCount;
            PostTransitionPointCount = postTransitionPointCount;
        }
    }

    /// <summary>
    /// Model-free tandem mixing search. Each transition is solved in order: the candidate
    /// concentrations are produced by the real back-mixing bookkeeping, and the score measures
    /// how well the included heats on both sides of the transition lie on one smooth curve of
    /// normalised heat versus molar ratio.
    /// </summary>
    internal static class TandemContinuityScanner
    {
        public const double PriorCenter = 0.043;
        public const double PriorWidth = 0.12;
        public const double ProfileStep = 0.02;
        public const double RefinementTolerance = 1e-4;
        public const int MinimumSegmentPointCount = 5;

        const int GoldenSectionEstimatedEvaluations = 16;

        public static TandemMixingScanPoint FindBest(
            IReadOnlyList<ExperimentData> sources,
            TandemConcatenation.BackMixingSettings settings,
            Action<int, int> reportProgress,
            DilutionMethod dilutionMethod)
        {
            var transitionCount = sources.Count - 1;
            var profileFractions = TandemMixingScanner.MixingFractionsForStep(
                TandemMixingScanner.DefaultMinimumMixingFraction,
                TandemMixingScanner.AdaptiveMaximumMixingFraction,
                ProfileStep);
            var progressTotal = transitionCount * (profileFractions.Count + GoldenSectionEstimatedEvaluations);
            var completed = 0;

            AppEventHandler.PrintAndLog(
                $"Model-free tandem mixing search: sources={sources.Count}, transitions={transitionCount}, " +
                $"profileStep={FormatFraction(ProfileStep)}, prior={FormatFraction(PriorCenter)}±{FormatFraction(PriorWidth)}");

            var (scanExperiment, segments) = TandemMixingScanner.BuildScanExperiment(sources);
            var fractions = new double[transitionCount];
            var profiles = new List<TandemMixingTransitionProfile>(transitionCount);

            for (var transition = 0; transition < transitionCount; transition++)
            {
                var profile = SolveTransition(
                    scanExperiment,
                    segments,
                    settings,
                    dilutionMethod,
                    fractions,
                    transition,
                    profileFractions,
                    () =>
                    {
                        completed++;
                        reportProgress?.Invoke(Math.Min(completed, progressTotal), progressTotal);
                    });

                if (profile == null) return null;

                fractions[transition] = profile.BestMixingFraction;
                profiles.Add(profile);
            }

            reportProgress?.Invoke(progressTotal, progressTotal);

            var point = TandemMixingScanPoint.ModelFree(fractions, profiles.Sum(profile => profile.BestScore), profiles);
            AppEventHandler.PrintAndLog(
                $"Model-free tandem mixing search final: mix={string.Join("/", fractions.Select(FormatFraction))}",
                1);

            return point;
        }

        static TandemMixingTransitionProfile SolveTransition(
            ExperimentData experiment,
            IList<TandemConcatenation.TandemInjectionSegment> segments,
            TandemConcatenation.BackMixingSettings settings,
            DilutionMethod dilutionMethod,
            double[] solvedFractions,
            int transition,
            IReadOnlyList<double> profileFractions,
            Action reportEvaluation)
        {
            // Segments 0..transition+1 only depend on the fractions up to this transition, so the
            // unsolved later fractions are left at zero.
            var candidateFractions = solvedFractions.ToArray();

            TransitionPoints Extract(double fraction)
            {
                candidateFractions[transition] = fraction;
                TandemConcatenation.ProcessInjectionsWithBackMixingModel(
                    experiment, segments, settings, candidateFractions, dilutionMethod);
                return TransitionPoints.Extract(experiment, segments[transition], segments[transition + 1]);
            }

            var initial = Extract(profileFractions[0]);
            if (initial.Pre.Count < MinimumSegmentPointCount || initial.Post.Count < MinimumSegmentPointCount)
            {
                AppEventHandler.PrintAndLog(
                    $"Model-free tandem mixing transition {transition + 1}: too few included injections " +
                    $"(before={initial.Pre.Count}, after={initial.Post.Count}, minimum={MinimumSegmentPointCount}).");
                return null;
            }

            // The weights do not depend on the mixing fraction, so a single normalisation keeps
            // the smoothing parameter and the noise estimate on one scale for every candidate.
            var weightScale = initial.Pre.Concat(initial.Post).Average(point => point.Weight);
            var preFit = PenalizedBSpline.FitWithGcv(initial.Pre, weightScale);
            if (preFit == null)
            {
                AppEventHandler.PrintAndLog($"Model-free tandem mixing transition {transition + 1}: the pre-transition spline could not be fitted.");
                return null;
            }

            var sigmaSquared = preFit.ResidualVariance;

            double DataScore(TransitionPoints points)
            {
                var joint = points.Pre.Concat(points.Post).ToList();
                var rss = PenalizedBSpline.WeightedResidualSumOfSquares(joint, weightScale, preFit.Grid, preFit.Lambda);
                return TandemMixingScanPoint.IsFinite(rss) ? rss / (2 * sigmaSquared) : double.NaN;
            }

            double Score(double fraction, out double dataScore)
            {
                dataScore = DataScore(Extract(fraction));
                reportEvaluation();
                return TandemMixingScanPoint.IsFinite(dataScore) ? dataScore + PriorScore(fraction) : double.PositiveInfinity;
            }

            var dataScores = new double[profileFractions.Count];
            var priorScores = new double[profileFractions.Count];
            var bestIndex = -1;
            var bestScore = double.PositiveInfinity;

            for (var index = 0; index < profileFractions.Count; index++)
            {
                var score = Score(profileFractions[index], out var dataScore);
                dataScores[index] = dataScore;
                priorScores[index] = PriorScore(profileFractions[index]);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = index;
                }
            }

            if (bestIndex < 0)
            {
                AppEventHandler.PrintAndLog($"Model-free tandem mixing transition {transition + 1}: no candidate produced a finite score.");
                return null;
            }

            var bestFraction = profileFractions[bestIndex];
            var lower = Math.Max(TandemMixingScanner.DefaultMinimumMixingFraction, bestFraction - ProfileStep);
            var upper = Math.Min(TandemMixingScanner.AdaptiveMaximumMixingFraction, bestFraction + ProfileStep);
            var (refinedFraction, refinedScore) = GoldenSectionMinimum(fraction => Score(fraction, out _), lower, upper);
            if (refinedScore < bestScore)
            {
                bestFraction = refinedFraction;
                bestScore = refinedScore;
            }

            // Leave the experiment in the state of the selected fraction for the next transition.
            candidateFractions[transition] = bestFraction;
            var bestData = DataScore(Extract(bestFraction));

            AppEventHandler.PrintAndLog(
                $"Model-free tandem mixing transition {transition + 1}: mix={FormatFraction(bestFraction)}, " +
                $"score={FormatNumber(bestScore)} (data={FormatNumber(bestData)}, prior={FormatNumber(PriorScore(bestFraction))}), " +
                $"sigma={FormatNumber(Math.Sqrt(sigmaSquared))}, lambda={FormatNumber(preFit.Lambda)}, " +
                $"points={initial.Pre.Count}+{initial.Post.Count}",
                1);

            return new TandemMixingTransitionProfile(
                transition,
                bestFraction,
                bestScore,
                profileFractions,
                dataScores,
                priorScores,
                Math.Sqrt(sigmaSquared),
                preFit.Lambda,
                initial.Pre.Count,
                initial.Post.Count);
        }

        internal static double PriorScore(double fraction)
        {
            var z = (fraction - PriorCenter) / PriorWidth;
            return 0.5 * z * z;
        }

        static (double x, double value) GoldenSectionMinimum(Func<double, double> function, double lower, double upper)
        {
            var ratio = (Math.Sqrt(5) - 1) / 2;
            var a = lower;
            var b = upper;
            var c = b - ratio * (b - a);
            var d = a + ratio * (b - a);
            var fc = function(c);
            var fd = function(d);

            while (b - a > RefinementTolerance)
            {
                if (fc < fd)
                {
                    b = d;
                    d = c;
                    fd = fc;
                    c = b - ratio * (b - a);
                    fc = function(c);
                }
                else
                {
                    a = c;
                    c = d;
                    fc = fd;
                    d = a + ratio * (b - a);
                    fd = function(d);
                }
            }

            return fc < fd ? (c, fc) : (d, fd);
        }

        static string FormatFraction(double fraction)
        {
            return $"{(100 * fraction).ToString("0.###", CultureInfo.InvariantCulture)}%";
        }

        static string FormatNumber(double value)
        {
            return TandemMixingScanPoint.IsFinite(value)
                ? value.ToString("G6", CultureInfo.InvariantCulture)
                : "NaN";
        }
    }

    internal readonly struct IsothermPoint
    {
        public double X { get; }
        public double Y { get; }
        public double Weight { get; }

        public IsothermPoint(double x, double y, double weight)
        {
            X = x;
            Y = y;
            Weight = weight;
        }
    }

    internal sealed class TransitionPoints
    {
        public List<IsothermPoint> Pre { get; }
        public List<IsothermPoint> Post { get; }

        TransitionPoints(List<IsothermPoint> pre, List<IsothermPoint> post)
        {
            Pre = pre;
            Post = post;
        }

        public static TransitionPoints Extract(
            ExperimentData experiment,
            TandemConcatenation.TandemInjectionSegment before,
            TandemConcatenation.TandemInjectionSegment after)
        {
            var pre = SegmentPoints(experiment, before);
            var post = SegmentPoints(experiment, after);

            // Use uncertainty weights only when every point has one; mixing weighted and
            // unweighted points would make the weights meaningless.
            if (pre.Concat(post).Any(point => !(point.Weight > 0) || !TandemMixingScanPoint.IsFinite(point.Weight)))
            {
                pre = pre.Select(point => new IsothermPoint(point.X, point.Y, 1.0)).ToList();
                post = post.Select(point => new IsothermPoint(point.X, point.Y, 1.0)).ToList();
            }

            return new TransitionPoints(pre, post);
        }

        static List<IsothermPoint> SegmentPoints(ExperimentData experiment, TandemConcatenation.TandemInjectionSegment segment)
        {
            var points = new List<IsothermPoint>(segment.InjectionCount);

            for (var index = segment.InjectionNumStart; index < segment.InjectionNumStart + segment.InjectionCount; index++)
            {
                var injection = experiment.Injections[index];
                if (!injection.Include || !injection.IsIntegrated) continue;

                var (cellBefore, titrantBefore) = PreInjectionState(experiment, index);
                var x = 0.5 * (titrantBefore / cellBefore + injection.ActualTitrantConcentration / injection.ActualCellConcentration);
                var y = injection.Enthalpy;
                var sd = injection.SD;
                var weight = sd > 0 ? 1.0 / (sd * sd) : double.NaN;

                if (TandemMixingScanPoint.IsFinite(x) && TandemMixingScanPoint.IsFinite(y))
                    points.Add(new IsothermPoint(x, y, weight));
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
    }

    /// <summary>
    /// Uniform cubic B-spline with a second-order difference penalty on the coefficients (a P-spline).
    /// The basis is defined by an origin and knot spacing in x, so overlapping or tied x values are fine.
    /// </summary>
    internal static class PenalizedBSpline
    {
        public const int MinimumSegmentCount = 4;
        public const int MaximumSegmentCount = 12;

        static readonly double[] LambdaGrid = Enumerable.Range(0, 49)
            .Select(index => Math.Pow(10, -6 + 0.25 * index))
            .ToArray();

        public sealed class Grid
        {
            public double Origin { get; }
            public double Spacing { get; }
            public int SegmentCount { get; }
            public int BasisCount => SegmentCount + 3;

            public Grid(double origin, double spacing, int segmentCount)
            {
                Origin = origin;
                Spacing = spacing;
                SegmentCount = segmentCount;
            }

            /// <summary>A grid with the same knot spacing, extended so it covers [min, max].</summary>
            public Grid ExtendedTo(double min, double max)
            {
                var origin = Origin;
                if (min < origin) origin -= Math.Ceiling((origin - min) / Spacing) * Spacing;

                var end = Math.Max(Origin + SegmentCount * Spacing, origin + Spacing);
                if (max > end) end += Math.Ceiling((max - end) / Spacing) * Spacing;

                return new Grid(origin, Spacing, Math.Max(1, (int)Math.Round((end - origin) / Spacing)));
            }
        }

        public sealed class FitResult
        {
            public Grid Grid { get; }
            public double Lambda { get; }
            public double WeightedRss { get; }
            public double EffectiveDegreesOfFreedom { get; }
            public double ResidualVariance { get; }
            public double[] Coefficients { get; }

            public FitResult(Grid grid, double lambda, double weightedRss, double edf, double residualVariance, double[] coefficients)
            {
                Grid = grid;
                Lambda = lambda;
                WeightedRss = weightedRss;
                EffectiveDegreesOfFreedom = edf;
                ResidualVariance = residualVariance;
                Coefficients = coefficients;
            }

            public double Evaluate(double x) => PenalizedBSpline.Evaluate(Grid, Coefficients, x);
        }

        /// <summary>
        /// Fits with the smoothing parameter chosen by generalised cross-validation. The residual
        /// variance is floored so a noise-free curve cannot produce an infinite likelihood scale.
        /// </summary>
        public static FitResult FitWithGcv(IReadOnlyList<IsothermPoint> points, double weightScale)
        {
            if (points == null || points.Count < MinimumSegmentCount + 1) return null;

            var min = points.Min(point => point.X);
            var max = points.Max(point => point.X);
            if (!(max > min)) return null;

            var segmentCount = FWEMath.Clamp(points.Count / 3, MinimumSegmentCount, MaximumSegmentCount);
            var grid = new Grid(min, (max - min) / segmentCount, segmentCount);

            FitResult best = null;
            var bestGcv = double.PositiveInfinity;
            foreach (var lambda in LambdaGrid)
            {
                var fit = Fit(points, weightScale, grid, lambda);
                if (fit == null) continue;

                var denominator = points.Count - fit.EffectiveDegreesOfFreedom;
                if (denominator <= 0) continue;

                var gcv = points.Count * fit.WeightedRss / (denominator * denominator);
                if (gcv < bestGcv)
                {
                    bestGcv = gcv;
                    best = fit;
                }
            }

            if (best == null) return null;

            var meanSquare = points.Average(point => point.Weight / weightScale * point.Y * point.Y);
            var variance = Math.Max(
                best.WeightedRss / (points.Count - best.EffectiveDegreesOfFreedom),
                1e-12 * meanSquare + double.Epsilon);

            return new FitResult(best.Grid, best.Lambda, best.WeightedRss, best.EffectiveDegreesOfFreedom, variance, best.Coefficients);
        }

        public static double WeightedResidualSumOfSquares(
            IReadOnlyList<IsothermPoint> points,
            double weightScale,
            Grid knotSpacingSource,
            double lambda)
        {
            var grid = knotSpacingSource.ExtendedTo(points.Min(point => point.X), points.Max(point => point.X));
            var fit = Fit(points, weightScale, grid, lambda);
            return fit?.WeightedRss ?? double.NaN;
        }

        public static FitResult Fit(IReadOnlyList<IsothermPoint> points, double weightScale, Grid grid, double lambda)
        {
            var basisCount = grid.BasisCount;
            var normal = new DenseMatrix(basisCount, basisCount);
            var rhs = new DenseVector(basisCount);
            var basis = new double[4];

            foreach (var point in points)
            {
                var weight = point.Weight / weightScale;
                var first = BasisValues(grid, point.X, basis);
                for (var i = 0; i < 4; i++)
                {
                    rhs[first + i] += weight * basis[i] * point.Y;
                    for (var j = 0; j < 4; j++)
                        normal[first + i, first + j] += weight * basis[i] * basis[j];
                }
            }

            var system = normal.Clone();
            for (var k = 0; k < basisCount - 2; k++)
            {
                // D'D for the second difference row (1, -2, 1) at coefficients k..k+2.
                var row = new[] { 1.0, -2.0, 1.0 };
                for (var i = 0; i < 3; i++)
                    for (var j = 0; j < 3; j++)
                        system[k + i, k + j] += lambda * row[i] * row[j];
            }

            double[] coefficients;
            double edf;
            try
            {
                var cholesky = system.Cholesky();
                coefficients = cholesky.Solve(rhs).ToArray();
                edf = cholesky.Solve(normal).Diagonal().Sum();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                // Not positive definite: too few distinct x values for this grid and penalty.
                return null;
            }

            if (coefficients.Any(value => !TandemMixingScanPoint.IsFinite(value))) return null;

            var rss = 0.0;
            foreach (var point in points)
            {
                var residual = point.Y - Evaluate(grid, coefficients, point.X);
                rss += point.Weight / weightScale * residual * residual;
            }

            return new FitResult(grid, lambda, rss, edf, double.NaN, coefficients);
        }

        public static double Evaluate(Grid grid, double[] coefficients, double x)
        {
            var basis = new double[4];
            var first = BasisValues(grid, x, basis);
            var value = 0.0;
            for (var i = 0; i < 4; i++) value += coefficients[first + i] * basis[i];
            return value;
        }

        /// <summary>Returns the index of the first non-zero basis function and fills its four values.</summary>
        static int BasisValues(Grid grid, double x, double[] values)
        {
            var u = (x - grid.Origin) / grid.Spacing;
            var interval = FWEMath.Clamp((int)Math.Floor(u), 0, grid.SegmentCount - 1);
            var t = u - interval;
            var oneMinusT = 1 - t;

            values[0] = oneMinusT * oneMinusT * oneMinusT / 6;
            values[1] = (3 * t * t * t - 6 * t * t + 4) / 6;
            values[2] = (-3 * t * t * t + 3 * t * t + 3 * t + 1) / 6;
            values[3] = t * t * t / 6;

            return interval;
        }
    }
}
