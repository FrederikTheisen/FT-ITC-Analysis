using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using MathNet.Numerics.Distributions;

namespace AnalysisITC.Core.Processing
{
    internal static class TandemMixingUncertaintyEstimator
    {
        internal const int MonteCarloDraws = 200;

        internal static double GridThreshold(double minimumRss, int includedCount, int fittedParameterCount)
        {
            var degreesOfFreedom = includedCount - fittedParameterCount;
            return minimumRss > 0 && TandemMixingUncertainty.Finite(minimumRss) && degreesOfFreedom > 0
                ? minimumRss * (1 + FisherSnedecor.InvCDF(1, degreesOfFreedom, .95) / degreesOfFreedom)
                : double.NaN;
        }

        internal static TandemMixingUncertainty FromGrid(IReadOnlyList<TandemMixingScanPoint> grid,
            IReadOnlyList<double> coordinates, TandemMixingScanPoint best, int includedCount)
        {
            // The scan always fits the four one-site coordinates (N, affinity, enthalpy, offset).
            var threshold = GridThreshold(includedCount * best.Rmsd * best.Rmsd,
                includedCount, 4 + best.TransitionMixingFractions.Count);
            var intervals = Enumerable.Range(0, best.TransitionMixingFractions.Count).Select(transition =>
            {
                var profile = coordinates.Select(fraction =>
                {
                    var slice = grid.Where(point => point.TransitionMixingFractions[transition] == fraction).ToArray();
                    return slice.Length == 0 || slice.Any(point => !point.IsValid) ? double.NaN
                        : slice.Min(point => includedCount * point.Rmsd * point.Rmsd);
                }).ToArray();
                var interval = GridInterval(coordinates, profile, threshold);
                var selected = best.TransitionMixingFractions[transition];
                // A coarse nuisance grid may miss the region containing the refined minimum.
                if (interval.Lower > selected || interval.Upper < selected) return TandemMixingInterval.Unavailable();
                return interval;
            }).ToArray();
            return new TandemMixingUncertainty(MixingUncertaintyMethod.Grid95, intervals,
                gridStep: coordinates[1] - coordinates[0]);
        }

        internal static TandemMixingInterval GridInterval(IReadOnlyList<double> coordinates, IReadOnlyList<double> rss, double threshold)
        {
            if (!TandemMixingUncertainty.Finite(threshold)) return TandemMixingInterval.Unavailable();
            var accepted = Enumerable.Range(0, coordinates.Count)
                .Where(i => TandemMixingUncertainty.Finite(rss[i]) && rss[i] <= threshold).ToArray();
            if (accepted.Length == 0) return TandemMixingInterval.Unavailable();
            var first = accepted[0]; var last = accepted[accepted.Length - 1];
            double? lower = null, upper = null;
            var lowerStatus = MixingLimitStatus.Unavailable;
            var upperStatus = MixingLimitStatus.Unavailable;
            if (first == 0 && coordinates[first] == 0) { lower = 0; lowerStatus = MixingLimitStatus.BoundReached; }
            else if (first > 0 && TandemMixingUncertainty.Finite(rss[first - 1]))
            {
                lower = InterpolateCrossing(coordinates[first - 1], rss[first - 1], coordinates[first], rss[first], threshold);
                lowerStatus = MixingLimitStatus.Crossing;
            }
            if (last == coordinates.Count - 1 && coordinates[last] == 1) { upper = 1; upperStatus = MixingLimitStatus.BoundReached; }
            else if (last < coordinates.Count - 1 && TandemMixingUncertainty.Finite(rss[last + 1]))
            {
                upper = InterpolateCrossing(coordinates[last], rss[last], coordinates[last + 1], rss[last + 1], threshold);
                upperStatus = MixingLimitStatus.Crossing;
            }
            if (lower.HasValue && upper.HasValue && lower == upper) return TandemMixingInterval.Unavailable();
            return new TandemMixingInterval(lower, upper, lowerStatus, upperStatus);
        }

        static double InterpolateCrossing(double x0, double y0, double x1, double y1, double threshold)
            => x0 + (x1 - x0) * (threshold - y0) / (y1 - y0);

        internal static TandemMixingUncertainty MonteCarlo(IReadOnlyList<ExperimentData> sources,
            TandemConcatenation.BackMixingSettings settings, TandemMixingScanPoint best, DilutionMethod method,
            Action<int, int> progress, Random random = null, double? noiseOverrideForTesting = null)
        {
            var (experiment, segments) = TandemMixingScanner.BuildScanExperiment(sources);
            return MonteCarlo(experiment, segments, settings, best, method, progress, random, noiseOverrideForTesting);
        }

        internal static TandemMixingUncertainty MonteCarlo(ExperimentData experiment,
            IList<TandemConcatenation.TandemInjectionSegment> segments,
            TandemConcatenation.BackMixingSettings settings, TandemMixingScanPoint best, DilutionMethod method,
            Action<int, int> progress, Random random = null, double? noiseOverrideForTesting = null)
        {
            var unavailable = Enumerable.Range(0, best.TransitionMixingFractions.Count)
                .Select(_ => TandemMixingInterval.Unavailable()).ToArray();
            TandemConcatenation.ProcessInjectionsWithBackMixingModel(experiment, segments, settings, best.TransitionMixingFractions, method);
            var noise = noiseOverrideForTesting ?? EstimateHeatNoise(experiment, segments);
            var savedNoise = TandemMixingUncertainty.Finite(noise) && noise >= 0 ? (double?)noise : null;
            TandemMixingUncertainty Result(IEnumerable<TandemMixingInterval> intervals) => new TandemMixingUncertainty(
                MixingUncertaintyMethod.MonteCarlo95, intervals, monteCarloDraws: MonteCarloDraws, heatNoiseSd: savedNoise);
            if (!(noise > 0) || !TandemMixingUncertainty.Finite(noise)) return Result(unavailable);

            random = random ?? BootstrapRandomStreams.CreateOne();
            var samples = new List<IReadOnlyList<double>>();
            var observations = experiment.Injections.Select(injection => injection.PeakArea.Value).ToArray();
            for (var draw = 0; draw < MonteCarloDraws; draw++)
            {
                if (!SolverInterface.TerminateAnalysisFlag.Down) return Result(unavailable);
                PerturbHeats(experiment, observations, noise, random);
                var point = TandemContinuityScanner.FindBest(experiment, segments, settings, null, method, logResults: false);
                if (point == null) return Result(unavailable);
                samples.Add(point.TransitionMixingFractions);
                progress?.Invoke(draw + 1, MonteCarloDraws);
            }
            return Result(Summarize(samples));
        }

        internal static void PerturbHeats(ExperimentData experiment, IReadOnlyList<double> observations, double noise, Random random)
        {
            for (var index = 0; index < experiment.Injections.Count; index++)
            {
                var injection = experiment.Injections[index];
                if (!injection.Include) continue;
                var normal = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
                injection.SetPeakArea(new FloatWithError(observations[index] + noise * normal, injection.PeakArea.SD));
            }
        }

        internal static double EstimateHeatNoise(ExperimentData experiment, IList<TandemConcatenation.TandemInjectionSegment> segments)
        {
            var predictions = new Dictionary<int, List<double>>();
            for (var transition = 0; transition < segments.Count - 1; transition++)
            {
                var (pre, post) = TandemContinuityScanner.TransitionWindow(experiment, segments, transition);
                var fit = TandemContinuityScanner.FitWindow(pre.Concat(post).ToList());
                if (fit == null) return double.NaN;
                foreach (var index in WindowIndices(experiment, segments, transition))
                {
                    if (!predictions.TryGetValue(index, out var values)) predictions[index] = values = new List<double>();
                    values.Add(fit.Evaluate(TandemContinuityScanner.MidpointMolarRatio(experiment, index)) * experiment.Injections[index].InjectionMass);
                }
            }
            var sumSquares = predictions.Sum(pair =>
                Math.Pow(experiment.Injections[pair.Key].PeakArea.Value - pair.Value.Average(), 2));
            return CorrectedHeatNoise(sumSquares, predictions.Count, segments.Count - 1);
        }

        internal static double CorrectedHeatNoise(double sumSquares, int includedCount, int transitionCount)
        {
            // Nominal count: four cubic coefficients and one mixing fraction per refill.
            // Shape constraints can reduce the effective count, so this is an approximation.
            var degreesOfFreedom = includedCount - (TandemContinuityScanner.PolynomialOrder + 2) * transitionCount;
            return transitionCount > 0 && degreesOfFreedom > 0 && sumSquares >= 0 && TandemMixingUncertainty.Finite(sumSquares)
                ? Math.Sqrt(sumSquares / degreesOfFreedom) : double.NaN;
        }

        internal static IEnumerable<int> WindowIndices(ExperimentData experiment, IList<TandemConcatenation.TandemInjectionSegment> segments, int transition)
        {
            var before = segments[transition]; var after = segments[transition + 1];
            var beforeCount = Math.Min(TandemContinuityScanner.PointsPerSide, before.InjectionCount);
            var afterCount = Math.Min(TandemContinuityScanner.PointsPerSide, after.InjectionCount);
            return Enumerable.Range(before.InjectionNumStart + before.InjectionCount - beforeCount, beforeCount)
                .Concat(Enumerable.Range(after.InjectionNumStart, afterCount)).Where(i => experiment.Injections[i].Include);
        }

        internal static IReadOnlyList<TandemMixingInterval> Summarize(IReadOnlyList<IReadOnlyList<double>> samples)
            => Enumerable.Range(0, samples[0].Count).Select(index =>
            {
                var values = samples.Select(sample => sample[index]).OrderBy(value => value).ToArray();
                var lower = Quantile(values, .025); var upper = Quantile(values, .975);
                return lower == upper ? TandemMixingInterval.Unavailable() : new TandemMixingInterval(lower, upper,
                    lower == 0 ? MixingLimitStatus.BoundReached : MixingLimitStatus.Crossing,
                    upper == 1 ? MixingLimitStatus.BoundReached : MixingLimitStatus.Crossing);
            }).ToArray();

        static double Quantile(double[] sorted, double probability)
        {
            var position = (sorted.Length - 1) * probability;
            var lower = (int)Math.Floor(position); var upper = (int)Math.Ceiling(position);
            return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
        }
    }
}
