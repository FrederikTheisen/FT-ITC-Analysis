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
        }

        [Fact]
        public void TooFewIncludedInjectionsReturnsNull()
        {
            // A quadratic noise estimate needs more than three points before the transition.
            var sources = CreateSyntheticTandem(150e-6, 6.5, new[] { 0.1 }, 0.002, 2);
            foreach (var injection in sources[0].Injections.Skip(4)) injection.Include = false;

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
            output.WriteLine($"model-free fractions: {(point == null ? "none" : string.Join("/", point.TransitionMixingFractions.Select(f => f.ToString("G4"))))}");

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

        static double Gaussian(Random random)
        {
            var u1 = 1.0 - random.NextDouble();
            var u2 = random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
