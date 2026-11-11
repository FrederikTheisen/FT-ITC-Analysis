using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection("Solver events")]
    public sealed class TandemMixingUncertaintyTests : IDisposable
    {
        readonly PreferencesState original = PreferencesState.FromSettings();
        public TandemMixingUncertaintyTests()
        {
            PreferencesState.Defaults().ApplyToSettings();
            AppSettings.DilutionCalculationMethod = DilutionMethod.DiscreteDisplacement;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
        public void Dispose() => original.ApplyToSettings();
        static TandemConcatenation.BackMixingSettings Settings() => new TandemConcatenation.BackMixingSettings
        { UseBackMixingMethod = true, DeadVolume = 80e-6, DidRemoveOverflow = true };
        static List<ExperimentData> Sources() => TandemMixingContinuityTests.CreateSyntheticTandem(150e-6, 4,
            new[] { .2, .4 }, .002, 23);

        [Fact]
        public void FThresholdMatchesIndependentFTableValue()
        {
            // F(1,10), 95th percentile = 4.9646027437307145.0
            Assert.Equal(29.92920548746143, TandemMixingUncertaintyEstimator.GridThreshold(20, 16, 6), 10);
            Assert.True(double.IsNaN(TandemMixingUncertaintyEstimator.GridThreshold(20, 6, 6)));
            Assert.True(double.IsNaN(TandemMixingUncertaintyEstimator.GridThreshold(0, 16, 6)));
        }

        [Fact]
        public void GridCrossingsInterpolateAsymmetricLimits()
        {
            var interval = TandemMixingUncertaintyEstimator.GridInterval(new[] { 0.0, .25, .5, .75, 1.0 }, new[] { 10.0, 5.0, 2.0, 4.0, 12.0 }, 6);
            Assert.Equal(.2, interval.Lower.Value, 12);
            Assert.Equal(.8125, interval.Upper.Value, 12);
            Assert.Equal(MixingLimitStatus.Crossing, interval.LowerStatus);
        }

        [Fact]
        public void GridEnvelopeIncludesSeparatedAcceptedRegions()
        {
            var interval = TandemMixingUncertaintyEstimator.GridInterval(new[] { 0.0, .25, .5, .75, 1.0 }, new[] { 10.0, 2.0, 10.0, 2.0, 10.0 }, 6);
            Assert.Equal(.125, interval.Lower.Value, 12);
            Assert.Equal(.875, interval.Upper.Value, 12);
        }

        [Fact]
        public void GridBoundsAndMissingCoordinatesHaveDistinctOutcomes()
        {
            var coordinates = new[] { 0.0, .25, .5, .75, 1.0 };
            var bounded = TandemMixingUncertaintyEstimator.GridInterval(coordinates, new[] { 1.0, 2.0, 3.0, 2.0, 1.0 }, 4);
            Assert.Equal(MixingLimitStatus.BoundReached, bounded.LowerStatus);
            Assert.Equal(MixingLimitStatus.BoundReached, bounded.UpperStatus);
            Assert.Equal(0, bounded.Lower); Assert.Equal(1, bounded.Upper);
            var missing = TandemMixingUncertaintyEstimator.GridInterval(coordinates, new[] { double.NaN, 2.0, 1.0, 2.0, double.NaN }, 3);
            Assert.Null(missing.Lower); Assert.Null(missing.Upper);
            var unresolved = TandemMixingUncertaintyEstimator.GridInterval(coordinates, new[] { 5.0, 4.0, 3.0, 4.0, 5.0 }, 2);
            Assert.Null(unresolved.Lower); Assert.Null(unresolved.Upper);
        }

        [Fact]
        public void GridUsesBroadSlicesAndCountsEveryEstimatedFraction()
        {
            TandemMixingScanPoint Point(double f1, double f2, double rss) => new TandemMixingScanPoint(new[] { f1, f2 }, Math.Sqrt(rss / 16), 1, 4, -40000, 0, "test", 0);
            var grid = new[] { Point(0, 0, 40), Point(0, 1, 30), Point(1, 0, 50), Point(1, 1, 25) };
            var best = Point(.4, .6, 20);
            var result = TandemMixingUncertaintyEstimator.FromGrid(grid, new[] { 0.0, 1.0 }, best, 16);
            Assert.Equal(MixingLimitStatus.Crossing, result.Intervals[0].LowerStatus);
            Assert.Equal((30 - 29.92920548746143) / 5, result.Intervals[0].Lower.Value, 10);
            Assert.Equal(MixingLimitStatus.BoundReached, result.Intervals[0].UpperStatus);
        }

        [Fact]
        public void PercentilesUseLinearInterpolationOfPairedSamples()
        {
            var samples = Enumerable.Range(0, 200).Select(i => (IReadOnlyList<double>)new[] { i / 199.0, 1 - i / 199.0 }).ToList();
            var intervals = TandemMixingUncertaintyEstimator.Summarize(samples);
            Assert.All(intervals, interval => { Assert.Equal(.025, interval.Lower.Value, 12); Assert.Equal(.975, interval.Upper.Value, 12); });
        }

        [Fact]
        public void CoarseProfileMissingTheRefinedOptimumIsUnavailable()
        {
            TandemMixingScanPoint Point(double fraction, double rss) => new TandemMixingScanPoint(new[] { fraction }, Math.Sqrt(rss / 16), 1, 4, -40000, 0, "test", 0);
            var result = TandemMixingUncertaintyEstimator.FromGrid(new[] { Point(0, 40), Point(1, 21) },
                new[] { 0.0, 1.0 }, Point(.1, 20), 16);
            Assert.Null(result.Intervals[0].Lower);
            Assert.Null(result.Intervals[0].Upper);
        }

        [Fact]
        public void PerturbationsDrawOncePerIncludedInjectionAndResetToObservedHeats()
        {
            var (experiment, _) = TandemMixingScanner.BuildScanExperiment(Sources());
            var observed = experiment.Injections.Select(i => i.PeakArea.Value).ToArray();
            var expectedRandom = new Random(32);
            var expected = experiment.Injections.Select((i, index) => i.Include
                ? observed[index] + 1e-7 * Math.Sqrt(-2 * Math.Log(1 - expectedRandom.NextDouble())) * Math.Cos(2 * Math.PI * expectedRandom.NextDouble())
                : observed[index]).ToArray();
            TandemMixingUncertaintyEstimator.PerturbHeats(experiment, observed, 1e-7, new Random(32));
            Assert.Equal(expected, experiment.Injections.Select(i => i.PeakArea.Value));
            TandemMixingUncertaintyEstimator.PerturbHeats(experiment, observed, 1e-7, new Random(32));
            Assert.Equal(expected, experiment.Injections.Select(i => i.PeakArea.Value));
        }

        [Fact]
        public void NoiseEstimateUsesJoulesAndCountsOverlappingInjectionsOnce()
        {
            var sources = Sources();
            // Shorten the middle segment so its two windows overlap.
            sources[1].Injections = sources[1].Injections.Take(10).ToList();
            var (experiment, segments) = TandemMixingScanner.BuildScanExperiment(sources);
            TandemConcatenation.ProcessInjectionsWithBackMixingModel(experiment, segments, Settings(), new[] { .2, .4 }, DilutionMethod.DiscreteDisplacement);
            var predictions = new Dictionary<int, List<double>>();
            var windowSize = TandemContinuityScanner.PointsPerSide;
            for (var t = 0; t < 2; t++)
            {
                var before = segments[t]; var after = segments[t + 1];
                var ids = Enumerable.Range(before.InjectionNumStart + before.InjectionCount - windowSize, windowSize)
                    .Concat(Enumerable.Range(after.InjectionNumStart, windowSize)).Where(i => experiment.Injections[i].Include).ToArray();
                var fit = TandemContinuityScanner.FitWindow(ids.Select(i => (TandemContinuityScanner.MidpointMolarRatio(experiment, i), experiment.Injections[i].Enthalpy)).ToList());
                foreach (var id in ids)
                {
                    if (!predictions.ContainsKey(id)) predictions[id] = new List<double>();
                    predictions[id].Add(fit.Evaluate(TandemContinuityScanner.MidpointMolarRatio(experiment, id)) * experiment.Injections[id].InjectionMass);
                }
            }
            Assert.Contains(predictions.Values, p => p.Count == 2);
            var expected = Math.Sqrt(predictions.Sum(p => Math.Pow(experiment.Injections[p.Key].PeakArea.Value - p.Value.Average(), 2)) / (predictions.Count - 10));
            Assert.Equal(expected, TandemMixingUncertaintyEstimator.EstimateHeatNoise(experiment, segments), 15);
        }

        [Fact]
        public void HeatNoiseCorrectionMatchesNominalParameterCount()
        {
            // RSS = 20 (microjoules)^2, n=30, p=10 gives exactly 1 microjoule SD.
            Assert.Equal(1e-6, TandemMixingUncertaintyEstimator.CorrectedHeatNoise(20e-12, 30, 2), 15);
            Assert.Equal(Math.Sqrt(1.5), TandemMixingUncertaintyEstimator.CorrectedHeatNoise(30, 30, 2), 12);
            Assert.True(double.IsNaN(TandemMixingUncertaintyEstimator.CorrectedHeatNoise(1, 10, 2)));
            Assert.True(double.IsNaN(TandemMixingUncertaintyEstimator.CorrectedHeatNoise(1, 9, 2)));
        }

        [Fact]
        public void MonteCarloIsDeterministicAndKeepsPrimaryAndSourcesIntact()
        {
            var sources = Sources(); var originalHeats = sources.SelectMany(e => e.Injections).Select(i => i.PeakArea.Value).ToArray();
            var primary = TandemMixingScanner.FindBestAdaptive(sources, Settings(), criterion: TandemMixingCriterion.ModelFree);
            var fractions = primary.TransitionMixingFractions.ToArray(); var progress = new List<int>();
            var first = TandemMixingUncertaintyEstimator.MonteCarlo(sources, Settings(), primary, DilutionMethod.DiscreteDisplacement, (n, _) => progress.Add(n), new Random(34));
            var second = TandemMixingUncertaintyEstimator.MonteCarlo(sources, Settings(), primary, DilutionMethod.DiscreteDisplacement, null, new Random(34));
            Assert.Equal(Enumerable.Range(1, 200), progress);
            Assert.Equal(fractions, primary.TransitionMixingFractions);
            Assert.Equal(originalHeats, sources.SelectMany(e => e.Injections).Select(i => i.PeakArea.Value));
            Assert.Equal(first.HeatNoiseSd, second.HeatNoiseSd);
            Assert.Equal(first.Intervals.Select(i => i.Lower), second.Intervals.Select(i => i.Lower));
            Assert.Equal(first.Intervals.Select(i => i.Upper), second.Intervals.Select(i => i.Upper));
        }

        [Fact]
        public void UnavailableNoiseAndIncompleteMonteCarloPreserveThePrimary()
        {
            var sources = Sources();
            var primary = TandemMixingScanner.FindBestAdaptive(sources, Settings(), criterion: TandemMixingCriterion.ModelFree);
            var zero = TandemMixingUncertaintyEstimator.MonteCarlo(sources, Settings(), primary, DilutionMethod.DiscreteDisplacement, null, new Random(1), 0);
            Assert.All(zero.Intervals, interval => Assert.Null(interval.Lower));
            try
            {
                var cancelled = TandemMixingUncertaintyEstimator.MonteCarlo(sources, Settings(), primary, DilutionMethod.DiscreteDisplacement,
                    (n, _) => { if (n == 2) SolverInterface.TerminateAnalysisFlag.Raise(); }, new Random(1));
                Assert.All(cancelled.Intervals, interval => Assert.Null(interval.Lower));
                Assert.Equal(2, primary.TransitionMixingFractions.Count);
            }
            finally { SolverInterface.TerminateAnalysisFlag.Lower(); }
        }

        [Fact]
        public void OneSiteUncertaintyAddsNoFitsAndPreservesTheOptimum()
        {
            var sources = Sources(); int plainTotal = 0, withTotal = 0;
            var plain = TandemMixingScanner.FindBestAdaptive(sources, Settings(), (_, n) => plainTotal = n);
            var with = TandemMixingScanner.FindBestAdaptive(sources, Settings(), (_, n) => withTotal = n, estimateUncertainty: true);
            Assert.Equal(plainTotal, withTotal);
            Assert.Equal(plain.TransitionMixingFractions, with.TransitionMixingFractions);
            Assert.Equal(plain.Rmsd, with.Rmsd);
            Assert.NotNull(with.Uncertainty);
            Assert.Null(plain.Uncertainty);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MergeSummaryRoundTripsAndIsSharedSafelyByCopies(bool monteCarlo)
        {
            var sources = Sources();
            foreach (var source in sources)
                source.DataPoints = new List<DataPoint> { new DataPoint(0, 0, 25), new DataPoint(4000, 0, 25) };
            var uncertainty = new TandemMixingUncertainty(monteCarlo ? MixingUncertaintyMethod.MonteCarlo95 : MixingUncertaintyMethod.Grid95,
                new[] { new TandemMixingInterval(0, .3, MixingLimitStatus.BoundReached, MixingLimitStatus.Crossing), TandemMixingInterval.Unavailable() },
                gridStep: monteCarlo ? null : (double?).05, monteCarloDraws: monteCarlo ? 200 : 0, heatNoiseSd: monteCarlo ? (double?)1e-7 : null);
            var merged = TandemConcatenation.ConcatTandemWithBackMixing(sources, Settings(), new[] { .2, .4 },
                automaticCriterion: monteCarlo ? TandemMixingCriterion.ModelFree : TandemMixingCriterion.OneSiteFit, uncertainty: uncertainty);
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { merged });
            FtxtcJsonSchemaValidationTests.AssertPackageJsonValidates(package);
            package.Position = 0;
            var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<ExperimentData>());
            Assert.Equal(merged.TandemMergeInfo.Fractions, restored.TandemMergeInfo.Fractions);
            Assert.Equal(uncertainty.Method, restored.TandemMergeInfo.Uncertainty.Method);
            Assert.Equal(uncertainty.HeatNoiseSd, restored.TandemMergeInfo.Uncertainty.HeatNoiseSd);
            Assert.Equal(MixingLimitStatus.BoundReached, restored.TandemMergeInfo.Uncertainty.Intervals[0].LowerStatus);
            Assert.Null(restored.TandemMergeInfo.Uncertainty.Intervals[1].Upper);
            var details = TandemOriginPresentation.BuildDetails(restored);
            Assert.Equal(("Method", monteCarlo ? "Automatic (model-free)" : "Automatic (one-site)"), details.Settings[0]);
            Assert.Equal(("Bookkeeping", "Discrete displacement"), details.Settings[1]);
            Assert.Equal(("Overflow removed", "Yes"), details.Settings[3]);
            Assert.Contains("(bound reached)", details.Refills[0].Limits);
            Assert.Equal("Unavailable", details.Refills[1].Limits);
            var expectedMethod = monteCarlo ? "Automatic (model-free)" : "Automatic (one-site)";
            Assert.Contains($"**Tandem merge:** 3 runs · {expectedMethod} · Back-mixing by refill 20% / 40%",
                restored.GetInfoString());
            var text = TandemOriginPresentation.FormatDescription(restored.TandemMergeDescription, restored.TandemMergeInfo);
            Assert.Contains("bound reached", text);Assert.Contains("unavailable", text);
            Assert.Contains(monteCarlo ? "Monte Carlo" : "grid-based", text);
            var report = new AnalysisReport();
            report.SetSupportingExperimentIds(new[] { restored.UniqueID });
            var document = AnalysisReportBuilder.Build(report, _ => null,
                id => id == restored.UniqueID ? restored : null, new AnalysisReportOptions());
            Assert.Contains(document.Sections.SelectMany(section => section.Blocks)
                .OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items),
                item => item.Label == "Tandem merge origin" && item.Value == text);
            var clone = restored.GetSynthClone(new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None });
            Assert.Same(restored.TandemMergeInfo, clone.TandemMergeInfo);
        }
    }
}
