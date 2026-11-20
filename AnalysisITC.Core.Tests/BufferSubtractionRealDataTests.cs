using System;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [CollectionDefinition("Buffer subtraction real data", DisableParallelization = true)]
    public sealed class BufferSubtractionRealDataCollectionDefinition
    {
    }

    [Collection("Buffer subtraction real data")]
    public sealed class BufferSubtractionRealDataTests : IDisposable
    {
        public BufferSubtractionRealDataTests()
        {
            IntegratedHeatReader.BeginImportQueue();
            PlatformServices.RegisterImportPromptService(new FixedEnergyUnitPromptService(EnergyUnit.MicroCal));
        }

        public void Dispose()
        {
            IntegratedHeatReader.EndImportQueue();
            PlatformServices.RegisterImportPromptService(null);
        }

        [Theory]
        [InlineData("hepes-01.DH", -17.92782)]
        [InlineData("hepes-02.DH", -15.98349)]
        [InlineData("hepes-03.DH", -17.53788)]
        [InlineData("hepes-blank.DH", -0.58436)]
        public void DhFixturesLoadWithExpectedScientificMetadata(string fileName, double secondHeatMicrocalories)
        {
            var experiment = Read(fileName);

            Assert.Equal(56, experiment.Injections.Count);
            Assert.False(experiment.Injections[0].Include);
            Assert.All(experiment.Injections.Skip(1), injection => Assert.True(injection.Include));
            Assert.All(experiment.Injections, injection => Assert.True(injection.IsIntegrated));
            Assert.Equal(0.0001, experiment.CellConcentration.Value, 12);
            Assert.Equal(0.001, experiment.SyringeConcentration.Value, 12);
            Assert.Equal(0.0014103, experiment.CellVolume, 12);
            Assert.Equal(secondHeatMicrocalories, Microcalories(experiment.Injections[1].RawPeakArea), 5);
        }

        [Theory]
        [InlineData("hepes-01.DH", -17.34346, -8.19329, 0.39890)]
        [InlineData("hepes-02.DH", -15.39913, -9.42222, 0.43147)]
        [InlineData("hepes-03.DH", -16.95352, -10.76974, 0.37476)]
        public void MatchedSubtractionUsesObservedBlankHeat(
            string fileName,
            double expectedSecond,
            double expectedThirtySecond,
            double expectedLast)
        {
            var reference = Read("hepes-blank.DH");
            var target = Read(fileName);
            var settings = new BufferSubtractionSettings(reference.UniqueID, BufferSubtractionMethod.MatchedInjection);
            var model = BufferSubtractionCalculator.BuildModel(reference, settings);

            foreach (var injection in target.Injections)
                injection.UpdateCorrectedPeakArea(model);

            Assert.Equal(expectedSecond, Microcalories(target.Injections[1].PeakArea), 5);
            Assert.Equal(expectedThirtySecond, Microcalories(target.Injections[31].PeakArea), 5);
            Assert.Equal(expectedLast, Microcalories(target.Injections[55].PeakArea), 5);
        }

        [Fact]
        public void LinearModelFitsTheMeasuredNonzeroBlankBaseline()
        {
            var reference = Read("hepes-blank.DH");
            var target = Read("hepes-01.DH");
            var settings = new BufferSubtractionSettings(reference.UniqueID, BufferSubtractionMethod.Linear);
            var model = BufferSubtractionCalculator.BuildModel(reference, settings);

            Assert.True(model.TryEvaluate(2, out var second));
            Assert.True(model.TryEvaluate(32, out var thirtySecond));
            Assert.True(model.TryEvaluate(56, out var last));
            Assert.Equal(-0.386642512987013, Microcalories(second), 10);
            Assert.Equal(-0.385189417748918, Microcalories(thirtySecond), 10);
            Assert.Equal(-0.384026941558441, Microcalories(last), 10);

            // SD of the fitted line, s·√(1/n + (i − ī)²/Sxx), from numpy on the raw .DH heats (s = 0.220516655739).
            Assert.Equal(0.058667054766, MicrocaloriesSd(second), 10);
            Assert.Equal(0.030260774278, MicrocaloriesSd(thirtySecond), 10);
            Assert.Equal(0.058667054766, MicrocaloriesSd(last), 10);

            target.Injections[55].UpdateCorrectedPeakArea(model);
            Assert.Equal(0.298016941558441, Microcalories(target.Injections[55].PeakArea), 10);
        }

        [Fact]
        public void ExponentialModelDoesNotAssumeTheBlankDecaysToZero()
        {
            var reference = Read("hepes-blank.DH");
            var target = Read("hepes-01.DH");
            var settings = new BufferSubtractionSettings(reference.UniqueID, BufferSubtractionMethod.ExponentialDecay);
            var model = BufferSubtractionCalculator.BuildModel(reference, settings);

            Assert.True(model.TryEvaluate(56, out var last));
            var fittedHeat = Microcalories(last);
            Assert.True(double.IsFinite(fittedHeat));
            Assert.InRange(fittedHeat, -0.37, -0.33);

            target.Injections[55].UpdateCorrectedPeakArea(model);
            Assert.InRange(Microcalories(target.Injections[55].PeakArea), 0.24, 0.29);
        }

        [Fact]
        public void MeanUsesUnweightedPerVolumeHeatAndSampleSem()
        {
            var snapshot = new BufferSubtractionReferenceSnapshot("blank", new[]
            {
                new BufferSubtractionReferencePoint(1, new FloatWithError(2e-6, 1e-8), 1e-6),
                new BufferSubtractionReferencePoint(2, new FloatWithError(12e-6, 1e-8), 3e-6),
            });
            var model = BufferSubtractionCalculator.BuildModel(snapshot,
                new BufferSubtractionSettings("blank", BufferSubtractionMethod.Mean));

            Assert.False(model.TryEvaluate(1, out _));
            Assert.True(model.TryEvaluate(7, 2e-6, out var heat));
            Assert.Equal(6e-6, heat.Value, 15);
            Assert.Equal(2e-6, heat.SD, 15);

            Assert.False(model.TryEvaluate(7, 0, out _));
            Assert.False(model.TryEvaluate(7, double.NaN, out _));
        }

        [Fact]
        public void MeanPreviewUsesIncludedMeanAndActualReferenceInjectionVolumes()
        {
            var reference = Read("hepes-blank.DH");
            var model = BufferSubtractionCalculator.BuildModel(reference,
                new BufferSubtractionSettings(reference.UniqueID, BufferSubtractionMethod.Mean));

            var points = model.GetLinePoints(1, reference.InjectionCount);
            Assert.Equal(reference.InjectionCount, points.Count);
            Assert.Equal(1, points[0].InjectionNumber);
            Assert.Equal(reference.InjectionCount, points[^1].InjectionNumber);

            // Independently calculated from the 55 included 5 µL injections in the DH file.
            const double meanMicrocalories = -0.3853347272727273;
            const double semMicrocalories = 0.0294580399878007;
            Assert.All(points.Skip(1), point =>
                Assert.Equal(meanMicrocalories, Microcalories(new FloatWithError(point.Heat)), 10));
            Assert.False(reference.Injections[0].Include);
            Assert.Equal(meanMicrocalories / 5, Microcalories(new FloatWithError(points[0].Heat)), 10);
            Assert.True(BufferSubtractionCalculator.TryGetReferenceHeat(reference.Injections[0], model, out var firstHeat));
            Assert.Equal(semMicrocalories / 5, MicrocaloriesSd(firstHeat), 10);
        }

        [Fact]
        public void MeanUsesEqualWeightsAndHandlesOneOrNoUsableVolumePoints()
        {
            var settings = new BufferSubtractionSettings("blank", BufferSubtractionMethod.Mean);
            var equallyWeighted = new BufferSubtractionReferenceSnapshot("blank", new[]
            {
                new BufferSubtractionReferencePoint(1, new FloatWithError(2e-6, double.NaN), 1e-6),
                new BufferSubtractionReferencePoint(2, new FloatWithError(4e-6, 0), 1e-6),
                new BufferSubtractionReferencePoint(3, new FloatWithError(1e-6, 0), 0),
                new BufferSubtractionReferencePoint(4, new FloatWithError(double.NaN, 0), 1e-6),
            });
            var model = BufferSubtractionCalculator.BuildModel(equallyWeighted, settings);
            Assert.True(model.TryEvaluate(5, 2e-6, out var heat));
            Assert.Equal(6e-6, heat.Value, 15);
            Assert.Equal(2e-6, heat.SD, 15);
            var equalVolumeLine = model.GetLinePoints(1, 2);
            Assert.Equal(new[] { 1d, 2d }, equalVolumeLine.Select(point => point.InjectionNumber));
            Assert.All(equalVolumeLine, point => Assert.Equal(3e-6, point.Heat, 15));

            var onePoint = BufferSubtractionCalculator.BuildModel(new BufferSubtractionReferenceSnapshot("blank", new[]
                { new BufferSubtractionReferencePoint(1, new FloatWithError(3e-6), 1e-6) }), settings);
            Assert.True(onePoint.TryEvaluate(1, 2e-6, out var oneHeat));
            Assert.Equal(6e-6, oneHeat.Value, 15);
            Assert.Equal(0, oneHeat.SD);

            var noPoints = BufferSubtractionCalculator.BuildModel(new BufferSubtractionReferenceSnapshot("blank", new[]
                { new BufferSubtractionReferencePoint(1, new FloatWithError(3e-6), double.NaN) }), settings);
            Assert.False(noPoints.TryEvaluate(1, 2e-6, out _));
        }

        [Fact]
        public void ExponentialModelSdIsTheStandardErrorOfTheFittedCurve()
        {
            var reference = Read("hepes-blank.DH");
            var settings = new BufferSubtractionSettings(reference.UniqueID, BufferSubtractionMethod.ExponentialDecay);
            var model = BufferSubtractionCalculator.BuildModel(reference, settings);

            Assert.True(model.TryEvaluate(2, out var second));
            Assert.True(model.TryEvaluate(32, out var thirtySecond));
            Assert.True(model.TryEvaluate(56, out var last));

            // √(gᵀCg) with C from scipy curve_fit (s²(JᵀJ)⁻¹) on the raw .DH heats (s = 0.194623152431).
            // The tolerance covers the difference between the scipy and MathNet exponential optima.
            Assert.Equal(0.174423151156, MicrocaloriesSd(second), 2e-5);
            Assert.Equal(0.029042559992, MicrocaloriesSd(thirtySecond), 2e-5);
            Assert.Equal(0.029049853803, MicrocaloriesSd(last), 2e-5);
        }

        [Theory]
        [InlineData(BufferSubtractionMethod.Linear, -0.386690949495, 0.060289219298)]
        [InlineData(BufferSubtractionMethod.ExponentialDecay, -1.258636121977, 0.393009158977)]
        public void ExcludedFirstInjectionRetainsFittedCurveUncertainty(
            BufferSubtractionMethod method, double expectedHeat, double expectedSd)
        {
            var reference = Read("hepes-blank.DH");
            var target = Read("hepes-01.DH");
            Assert.False(reference.Injections[0].Include);
            var model = BufferSubtractionCalculator.BuildModel(reference,
                new BufferSubtractionSettings(reference.UniqueID, method));

            Assert.True(model.TryEvaluate(1, out var first));
            // Independent NumPy linear regression / SciPy curve_fit on injections 2–56.
            // Exponential tolerances allow the different optimizers' fitted parameters.
            Assert.Equal(expectedHeat, Microcalories(first), 5e-5);
            Assert.Equal(expectedSd, MicrocaloriesSd(first), 5e-5);

            target.Injections[0].UpdateCorrectedPeakArea(model);
            Assert.Equal(Microcalories(target.Injections[0].RawPeakArea) - expectedHeat,
                Microcalories(target.Injections[0].PeakArea), 5e-5);
            var targetSd = MicrocaloriesSd(target.Injections[0].RawPeakArea);
            Assert.Equal(Math.Sqrt(targetSd * targetSd + expectedSd * expectedSd),
                MicrocaloriesSd(target.Injections[0].PeakArea), 5e-5);
        }

        [Fact]
        public void LinearExtrapolationUncertaintyCanExceedReferenceScatter()
        {
            var snapshot = new BufferSubtractionReferenceSnapshot("Short reference", new[]
            {
                new BufferSubtractionReferencePoint(2, new FloatWithError(1e-6)),
                new BufferSubtractionReferencePoint(3, new FloatWithError(3e-6)),
                new BufferSubtractionReferencePoint(4, new FloatWithError(3e-6)),
            });
            var model = BufferSubtractionCalculator.BuildModel(snapshot,
                new BufferSubtractionSettings(null, BufferSubtractionMethod.Linear, snapshot));

            Assert.True(model.TryEvaluate(1, out var first));
            // In µJ: fitted line = x − 2/3; residual variance = 2/3.
            // At x=1, fitted-mean variance = (2/3)·(1/3 + (1−3)²/2) = 14/9.
            Assert.Equal(1e-6 / 3, first.Value, 15);
            Assert.Equal(1e-6 * Math.Sqrt(14.0 / 9), first.SD, 15);
        }

        static ExperimentData Read(string fileName) =>
            IntegratedHeatReader.ReadFile(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "BufferSubtraction",
                fileName));

        static double Microcalories(FloatWithError heat) =>
            Energy.ConvertFromJoule(heat.Value, EnergyUnit.MicroCal);

        static double MicrocaloriesSd(FloatWithError heat) =>
            Energy.ConvertFromJoule(heat.SD, EnergyUnit.MicroCal);

        sealed class FixedEnergyUnitPromptService : IImportPromptService
        {
            readonly EnergyUnit unit;

            public FixedEnergyUnitPromptService(EnergyUnit unit)
            {
                this.unit = unit;
            }

            public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse) =>
                new EnergyUnitPromptResult(unit, useForRemainingFilesInQueue: false, isCancelled: false);
        }
    }
}
