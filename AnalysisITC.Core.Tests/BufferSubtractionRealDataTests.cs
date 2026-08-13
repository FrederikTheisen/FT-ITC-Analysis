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
            Assert.Equal(0.220516655738977, MicrocaloriesSd(last), 10);

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
