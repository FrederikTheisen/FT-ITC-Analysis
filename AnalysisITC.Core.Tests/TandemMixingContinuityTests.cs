using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using Xunit;
using Xunit.Abstractions;

namespace AnalysisITC.Core.Tests
{
    [Collection("Solver events")]
    public sealed class TandemMixingContinuityTests : IDisposable
    {
        const double CellVolume = 200e-6;
        const double CellConcentration = 30e-6;

        readonly PreferencesState original = PreferencesState.FromSettings();
        readonly ITestOutputHelper output;

        public TandemMixingContinuityTests(ITestOutputHelper output)
        {
            this.output = output;
            PreferencesState.Defaults().ApplyToSettings();
        }

        public void Dispose() => original.ApplyToSettings();

        [Fact]
        public void PenalizedSplineReproducesSmoothCurveWithTiedAndOverlappingX()
        {
            var random = new Random(3);
            var points = Enumerable.Range(0, 40)
                .Select(index => 0.05 * (index % 30))
                .Select(x => new IsothermPoint(x, Sigmoid(x) + 0.002 * Gaussian(random), 1.0))
                .ToList();

            var fit = PenalizedBSpline.FitWithGcv(points, 1.0);

            Assert.NotNull(fit);
            Assert.True(fit.Lambda > 0);
            Assert.InRange(fit.EffectiveDegreesOfFreedom, 2, points.Count - 1);
            foreach (var x in new[] { 0.1, 0.5, 0.8, 1.2 })
                Assert.InRange(fit.Evaluate(x) - Sigmoid(x), -0.01, 0.01);
            Assert.InRange(Math.Sqrt(fit.ResidualVariance), 0.001, 0.004);
        }

        [Fact]
        public void PenalizedSplinePenalisesAJumpAtFixedSmoothing()
        {
            var smooth = Enumerable.Range(0, 30).Select(index => new IsothermPoint(index / 20.0, Sigmoid(index / 20.0), 1.0)).ToList();
            var fit = PenalizedBSpline.FitWithGcv(smooth.Take(20).ToList(), 1.0);
            var jumped = smooth.Select((point, index) => index < 20 ? point : new IsothermPoint(point.X, point.Y + 0.2, 1.0)).ToList();

            var smoothRss = PenalizedBSpline.WeightedResidualSumOfSquares(smooth, 1.0, fit.Grid, fit.Lambda);
            var jumpedRss = PenalizedBSpline.WeightedResidualSumOfSquares(jumped, 1.0, fit.Grid, fit.Lambda);

            Assert.True(jumpedRss > 100 * smoothRss, $"smooth={smoothRss}, jumped={jumpedRss}");
        }

        [Theory]
        [InlineData(0.05)]
        [InlineData(0.15)]
        [InlineData(0.35)]
        public void RecoversKnownFractionForTwoExperiments(double trueFraction)
        {
            var sources = CreateSyntheticTandem(
                syringeConcentration: 150e-6,
                logK: 6.5,
                transitionMixingFractions: new[] { trueFraction },
                noiseFraction: 0.002,
                seed: 11);

            var point = FindModelFree(sources);

            Assert.NotNull(point);
            Assert.Equal(TandemMixingCriterion.ModelFree, point.Criterion);
            Assert.InRange(point.FirstTransitionMixingFraction, trueFraction - 0.03, trueFraction + 0.03);
        }

        [Fact]
        public void RecoversKnownFractionsForThreeExperiments()
        {
            var trueFractions = new[] { 0.10, 0.25 };
            var sources = CreateSyntheticTandem(
                syringeConcentration: 100e-6,
                logK: 5.5,
                transitionMixingFractions: trueFractions,
                noiseFraction: 0.002,
                seed: 5);

            var point = FindModelFree(sources);

            Assert.NotNull(point);
            Assert.Equal(2, point.TransitionMixingFractions.Count);
            for (var index = 0; index < trueFractions.Length; index++)
                Assert.InRange(point.TransitionMixingFractions[index], trueFractions[index] - 0.04, trueFractions[index] + 0.04);
        }

        [Fact]
        public void FlatTransitionFallsBackToThePrior()
        {
            // The transition is far into saturation, where the isotherm no longer depends on the cell state.
            var sources = CreateSyntheticTandem(
                syringeConcentration: 500e-6,
                logK: 7.5,
                transitionMixingFractions: new[] { 0.40 },
                noiseFraction: 0.002,
                seed: 17);

            var point = FindModelFree(sources);

            Assert.NotNull(point);
            Assert.InRange(point.FirstTransitionMixingFraction, TandemContinuityScanner.PriorCenter - 0.03, TandemContinuityScanner.PriorCenter + 0.03);
        }

        [Fact]
        public void ExcludedOutlierDoesNotAffectTheResult()
        {
            const double trueFraction = 0.2;
            var clean = FindModelFree(CreateSyntheticTandem(150e-6, 6.5, new[] { trueFraction }, 0.002, 23));

            var withOutlier = CreateSyntheticTandem(150e-6, 6.5, new[] { trueFraction }, 0.002, 23);
            var outlier = withOutlier[1].Injections[1];
            outlier.SetPeakArea(new FloatWithError(outlier.PeakArea.Value * 0.5, outlier.PeakArea.SD));
            var included = FindModelFree(withOutlier);

            outlier.Include = false;
            var excluded = FindModelFree(withOutlier);

            output.WriteLine($"clean={clean.FirstTransitionMixingFraction}, outlierIncluded={included.FirstTransitionMixingFraction}, outlierExcluded={excluded.FirstTransitionMixingFraction}");
            Assert.True(Math.Abs(included.FirstTransitionMixingFraction - trueFraction) > 0.05);
            Assert.InRange(excluded.FirstTransitionMixingFraction, trueFraction - 0.03, trueFraction + 0.03);
            Assert.Equal(clean.TransitionProfiles[0].PostTransitionPointCount - 1, excluded.TransitionProfiles[0].PostTransitionPointCount);
        }

        [Fact]
        public void StoresAProfilePerTransition()
        {
            var sources = CreateSyntheticTandem(100e-6, 5.5, new[] { 0.10, 0.25 }, 0.002, 5);

            var point = FindModelFree(sources);

            Assert.Equal(2, point.TransitionProfiles.Count);
            Assert.All(point.TransitionProfiles, profile =>
            {
                Assert.Equal(51, profile.MixingFractions.Count);
                Assert.Equal(51, profile.DataScores.Count);
                Assert.Equal(51, profile.PriorScores.Count);
                Assert.All(profile.DataScores, score => Assert.True(double.IsFinite(score)));
                Assert.True(profile.Sigma > 0);
            });
            Assert.True(double.IsNaN(point.Rmsd));
            Assert.True(point.IsValid);
            Assert.Equal(point.TransitionProfiles.Sum(profile => profile.BestScore), point.Score, 10);
        }

        [Fact]
        public void TooFewIncludedInjectionsReturnsNull()
        {
            var sources = CreateSyntheticTandem(150e-6, 6.5, new[] { 0.1 }, 0.002, 2);
            foreach (var injection in sources[1].Injections.Skip(3)) injection.Include = false;

            Assert.Null(FindModelFree(sources));
        }

        [Fact]
        public void ReportsProgressToCompletion()
        {
            var sources = CreateSyntheticTandem(150e-6, 6.5, new[] { 0.1 }, 0.002, 2);
            var reports = new List<(int Completed, int Total)>();

            TandemMixingScanner.FindBestAdaptive(sources, Settings(), (completed, total) => reports.Add((completed, total)), TandemMixingCriterion.ModelFree);

            Assert.NotEmpty(reports);
            Assert.All(reports, report => Assert.InRange(report.Completed, 0, report.Total));
            Assert.Equal(reports[^1].Total, reports[^1].Completed);
        }

        [Fact]
        public async Task HistoricalTandemAgreesWithOneSiteCriterion()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tandem", "280-430-D2mut-1p6mM-JNK-200uM-1.ftxtc");
            List<ExperimentData> experiments;
            await using (var stream = File.OpenRead(path))
                experiments = (await FTXTCReader.ReadStream(stream)).OfType<ExperimentData>().ToList();
            var sources = experiments.Take(3).ToList();
            foreach (var source in sources)
                await source.Processor.ProcessData(replace: false, invalidate: false, showProgress: false);

            var modelFree = FindModelFree(sources);
            var oneSite = TandemMixingScanner.FindBestAdaptive(sources, Settings());

            output.WriteLine($"model-free={string.Join("/", modelFree.TransitionMixingFractions)}, one-site={string.Join("/", oneSite.TransitionMixingFractions)}");
            Assert.Equal(2, modelFree.TransitionMixingFractions.Count);
            for (var index = 0; index < 2; index++)
                Assert.InRange(modelFree.TransitionMixingFractions[index], oneSite.TransitionMixingFractions[index] - 0.1, oneSite.TransitionMixingFractions[index] + 0.1);
        }

        TandemMixingScanPoint FindModelFree(IReadOnlyList<ExperimentData> sources)
        {
            var point = TandemMixingScanner.FindBestAdaptive(sources, Settings(), criterion: TandemMixingCriterion.ModelFree);
            foreach (var profile in point?.TransitionProfiles ?? Array.Empty<TandemMixingTransitionProfile>())
            {
                output.WriteLine(
                    $"transition {profile.TransitionIndex + 1}: best={profile.BestMixingFraction:G4}, " +
                    $"data range={profile.DataScores.Min():G4}..{profile.DataScores.Max():G4}, " +
                    $"points={profile.PreTransitionPointCount}+{profile.PostTransitionPointCount}");
            }

            return point;
        }

        static TandemConcatenation.BackMixingSettings Settings()
        {
            return new TandemConcatenation.BackMixingSettings
            {
                UseBackMixingMethod = true,
                DidRemoveOverflow = true,
                DeadVolume = 80e-6,
            };
        }

        /// <summary>
        /// Builds tandem source experiments whose heats are generated from a one-site model evaluated
        /// on the concentrations produced by the real back-mixing bookkeeping at the given fractions.
        /// </summary>
        internal static List<ExperimentData> CreateSyntheticTandem(
            double syringeConcentration,
            double logK,
            IReadOnlyList<double> transitionMixingFractions,
            double noiseFraction,
            int seed)
        {
            const int injectionsPerRun = 19;
            var sources = Enumerable.Range(0, transitionMixingFractions.Count + 1)
                .Select(run =>
                {
                    var experiment = new ExperimentData($"synthetic-run-{run + 1}")
                    {
                        CellConcentration = new FloatWithError(CellConcentration),
                        SyringeConcentration = new FloatWithError(syringeConcentration),
                        CellVolume = CellVolume,
                        MeasuredTemperature = 25,
                        TargetTemperature = 25,
                    };
                    experiment.Injections = Enumerable.Range(0, injectionsPerRun)
                        .Select(index => InjectionData.FromPEAQFile(
                            experiment, index, include: index != 0, time: 60 + 150 * index,
                            volume: index == 0 ? 0.4e-6 : 2e-6, delay: 150, duration: 4, temperature: 25))
                        .ToList();
                    return experiment;
                })
                .ToList();

            var (truth, segments) = TandemMixingScanner.BuildScanExperiment(sources);
            TandemConcatenation.ProcessInjectionsWithBackMixingModel(
                truth, segments, Settings(), transitionMixingFractions, AppSettings.DilutionCalculationMethod);

            var model = new OneSetOfSites(truth);
            model.InitializeParameters(truth);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1.0);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -40000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, logK);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, -500);

            var heats = truth.Injections.Select(injection => model.Evaluate(injection.ID)).ToArray();
            var noise = noiseFraction * heats.Max(Math.Abs);
            var random = new Random(seed);
            var globalIndex = 0;
            foreach (var injection in sources.SelectMany(source => source.Injections))
            {
                var heat = heats[globalIndex++];
                injection.SetPeakArea(new FloatWithError(heat + noise * Gaussian(random), noise));
            }

            return sources;
        }

        static double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp((x - 0.8) / 0.12));

        static double Gaussian(Random random)
        {
            var u1 = 1.0 - random.NextDouble();
            var u2 = random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
