using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class FileTypeFixtureCollectionDefinition
    {
        public const string Name = "File type fixtures";
    }

    [Collection(FileTypeFixtureCollectionDefinition.Name)]
    public sealed class FileTypeFixtureTests : IDisposable
    {
        static readonly string FixtureDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "FileTypeTests");

        public FileTypeFixtureTests()
        {
            IntegratedHeatReader.BeginImportQueue();
            PlatformServices.RegisterImportPromptService(new FixtureEnergyUnitPromptService());
        }

        public void Dispose()
        {
            IntegratedHeatReader.EndImportQueue();
            PlatformServices.RegisterImportPromptService(null);
        }

        [Fact]
        public void EveryFixtureExtensionHasExplicitCoverage()
        {
            var actual = Directory.EnumerateFiles(FixtureDirectory)
                .Select(Path.GetExtension)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var expected = new[]
            {
                ".aff",
                ".apj",
                ".csv",
                ".dat",
                ".ftitc",
                ".ftxtc",
                ".itc",
                ".ta",
            };

            Assert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("230908_PRLRlong_W392A_run1.itc", ITCDataFormat.ITC200)]
        [InlineData("16102024_Rocu_3mM_Sug_0_3mM_PBS_2.ta", ITCDataFormat.TAITC)]
        [InlineData("230908_PRLRlong_W392A_run1.dat", ITCDataFormat.IntegratedHeats)]
        [InlineData("CURVE-2.aff", ITCDataFormat.IntegratedHeats)]
        [InlineData("TpxWT_CtFT(1).apj", ITCDataFormat.PEAQITCProject)]
        [InlineData("legacy.ftitc", ITCDataFormat.FTITC)]
        [InlineData("JORS Example Project.ftxtc", ITCDataFormat.FTXTC)]
        [InlineData("CURVE-1.csv", ITCDataFormat.Unknown)]
        public void FormatDetectionMatchesTheRealFixture(string fileName, ITCDataFormat expected)
        {
            Assert.Equal(expected, DataReader.GetFormat(Fixture(fileName)));
        }

        [Fact]
        public void MicroCalItcFixtureLoadsTheCompleteThermogram()
        {
            var experiment = MicroCalITC200Reader.ReadPath(Fixture("230908_PRLRlong_W392A_run1.itc"));

            AssertExperiment(experiment, ITCDataFormat.ITC200, injectionCount: 19, dataPointCount: 2_998);
            Assert.Equal(37, experiment.TargetTemperature, 6);
            Assert.Contains("PRLR", experiment.Comments);
        }

        [Fact]
        public void TaFixtureLoadsTheCompleteThermogram()
        {
            var experiment = TAFileReader.ReadPath(Fixture("16102024_Rocu_3mM_Sug_0_3mM_PBS_2.ta"));

            AssertExperiment(experiment, ITCDataFormat.TAITC, injectionCount: 18, dataPointCount: 3_091);
            Assert.Equal(34.85, experiment.TargetTemperature, 4);
        }

        [Theory]
        [InlineData("230908_PRLRlong_W392A_run1.dat", 19, -1.84487794552268E-05)]
        [InlineData("CURVE-2.aff", 26, 1.12E-03)]
        public void IntegratedHeatFixturesLoadAllCompleteRows(
            string fileName,
            int injectionCount,
            double firstHeatJoules)
        {
            var experiment = IntegratedHeatReader.ReadFile(Fixture(fileName));

            AssertExperiment(experiment, ITCDataFormat.IntegratedHeats, injectionCount, dataPointCount: 0);
            Assert.False(experiment.Injections[0].Include);
            Assert.All(experiment.Injections, injection => Assert.True(injection.IsIntegrated));
            Assert.Equal(firstHeatJoules, experiment.Injections[0].RawPeakArea.Value, 12);
        }

        [Fact]
        public void PeaqApjFixtureLoadsRawAndInjectionData()
        {
            var experiment = PEAQReader.ReadFile(Fixture("TpxWT_CtFT(1).apj"));

            AssertExperiment(experiment, ITCDataFormat.PEAQITCProject, injectionCount: 20, dataPointCount: 745);
        }

        [Fact]
        public async Task LegacyFtitcFixtureRestoresAllSavedContainers()
        {
            using var stream = File.OpenRead(Fixture("legacy.ftitc"));
            var containers = await FTITCReader.ReadStream(stream, processProcessorData: false);

            Assert.Equal(7, containers.OfType<ExperimentData>().Count());
            Assert.Equal(2, containers.OfType<AnalysisResult>().Count());
        }

        [Fact]
        public async Task NativeFtxtcFixtureRestoresAllSavedContainers()
        {
            using var stream = File.OpenRead(Fixture("JORS Example Project.ftxtc"));
            var containers = await FTXTCReader.ReadStream(stream);

            Assert.Equal(3, containers.OfType<ExperimentData>().Count());
            Assert.Equal(2, containers.OfType<AnalysisResult>().Count());
        }

        [Fact]
        public void AnalysisExportCsvIsExplicitlyRejectedAsProcessedTandemInput()
        {
            var exception = Assert.Throws<FormatException>(() =>
                ProcessedTandemCsvReader.ReadFile(Fixture("CURVE-1.csv")));

            Assert.Contains("DP_X/DP_Y", exception.Message);
        }

        static string Fixture(string fileName) => Path.Combine(FixtureDirectory, fileName);

        static void AssertExperiment(
            ExperimentData experiment,
            ITCDataFormat expectedFormat,
            int injectionCount,
            int dataPointCount)
        {
            Assert.NotNull(experiment);
            Assert.Equal(expectedFormat, experiment.DataSourceFormat);
            Assert.Equal(injectionCount, experiment.Injections.Count);
            Assert.Equal(dataPointCount, experiment.DataPoints.Count);
        }

        sealed class FixtureEnergyUnitPromptService : IImportPromptService
        {
            public EnergyUnitPromptResult AskForEnergyUnit(
                string fileName,
                string encounteredValue,
                bool allowQueueReuse)
            {
                var unit = Path.GetExtension(fileName).Equals(".aff", StringComparison.OrdinalIgnoreCase)
                    ? EnergyUnit.KiloJoule
                    : EnergyUnit.Joule;
                return new EnergyUnitPromptResult(
                    unit,
                    useForRemainingFilesInQueue: false,
                    isCancelled: false);
            }
        }
    }
}
