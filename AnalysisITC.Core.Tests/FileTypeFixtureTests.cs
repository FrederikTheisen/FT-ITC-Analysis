using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
                ".nitc",
                ".opj",
                ".ta",
            };

            Assert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("230908_PRLRlong_W392A_run1.itc", ITCDataFormat.ITC200)]
        [InlineData("16102024_Rocu_3mM_Sug_0_3mM_PBS_2.ta", ITCDataFormat.TAITC)]
        [InlineData("230908_PRLRlong_W392A_run1.dat", ITCDataFormat.IntegratedHeats)]
        [InlineData("CURVE-1.aff", ITCDataFormat.IntegratedHeats)]
        [InlineData("CURVE-2.aff", ITCDataFormat.IntegratedHeats)]
        [InlineData("TpxWT_CtFT(1).apj", ITCDataFormat.PEAQITCProject)]
        [InlineData("legacy.ftitc", ITCDataFormat.FTITC)]
        [InlineData("JORS Example Project.ftxtc", ITCDataFormat.FTXTC)]
        [InlineData("sample.NITC", ITCDataFormat.NanoITC)]
        [InlineData("CURVE-1.csv", ITCDataFormat.Unknown)]
        [InlineData("unsupported.vpitc", ITCDataFormat.Unknown)]
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
        [InlineData("230908_PRLRlong_W392A_run1.dat", 19, -1.84487794552268E-05, 2.02e-3, 207.1e-6, 1e-6)]
        [InlineData("CURVE-1.aff", 48, -2.97601644e-5, 1.10e-3, 1.4e-3, 0.01)]
        [InlineData("CURVE-2.aff", 26, 4.68608e-6, 4.00e-3, 1.41e-3, 0.015)]
        public void IntegratedHeatFixturesRecoverHeatAndConcentrationMetadata(
            string fileName,
            int injectionCount,
            double firstHeatJoules,
            double syringeConcentrationMolar,
            double cellVolumeLiters,
            double relativeTolerance)
        {
            var experiment = ReadIntegratedFile(Fixture(fileName));

            AssertExperiment(experiment, ITCDataFormat.IntegratedHeats, injectionCount, dataPointCount: 0);
            Assert.False(experiment.Injections[0].Include);
            Assert.All(experiment.Injections, injection => Assert.True(injection.IsIntegrated));
            Assert.Equal(firstHeatJoules, experiment.Injections[0].RawPeakArea.Value, 12);
            AssertRelative(syringeConcentrationMolar, experiment.SyringeConcentration.Value, relativeTolerance);
            AssertRelative(cellVolumeLiters, experiment.CellVolume, relativeTolerance);
            AssertRelative(
                syringeConcentrationMolar * experiment.Injections[0].Volume,
                experiment.Injections[0].InjectionMass,
                relativeTolerance);
            Assert.All(experiment.Injections, injection =>
            {
                Assert.True(double.IsFinite(injection.ActualCellConcentration));
                Assert.True(double.IsFinite(injection.ActualTitrantConcentration));
                Assert.True(double.IsFinite(injection.Ratio));
            });
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal)]
        [InlineData(DilutionMethod.Exponential)]
        public void IntegratedHeatTrajectoryInferenceMatchesActiveDilutionMethod(DilutionMethod method)
        {
            var path = WriteTemporaryIntegratedFile(BuildTrajectory(method, concentrationsAreMilliMolar: true));

            try
            {
                var experiment = ReadIntegratedFile(path, dilutionMethod: method);

                Assert.Equal(4, experiment.Injections.Count);
                AssertRelative(1.4e-3, experiment.CellVolume, 1e-10);
                AssertRelative(100e-6, experiment.CellConcentration.Value, 1e-10);
                AssertRelative(4e-3, experiment.SyringeConcentration.Value, 1e-10);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void IntegratedHeatMolarAndMillimolarModesProduceIdenticalInternalValues()
        {
            var millimolarPath = WriteTemporaryIntegratedFile(BuildTrajectory(DilutionMethod.MicroCal, concentrationsAreMilliMolar: true));
            var molarPath = WriteTemporaryIntegratedFile(BuildTrajectory(DilutionMethod.MicroCal, concentrationsAreMilliMolar: false));

            try
            {
                var millimolar = ReadIntegratedFile(millimolarPath, concentrationsAreMilliMolar: true);
                var molar = ReadIntegratedFile(molarPath, concentrationsAreMilliMolar: false);

                Assert.Equal(millimolar.CellVolume, molar.CellVolume, 12);
                Assert.Equal(millimolar.CellConcentration.Value, molar.CellConcentration.Value, 12);
                Assert.Equal(millimolar.SyringeConcentration.Value, molar.SyringeConcentration.Value, 12);
                Assert.Equal(millimolar.Injections.Count, molar.Injections.Count);
                foreach (var pair in millimolar.Injections.Zip(molar.Injections, (left, right) => (left, right)))
                    Assert.Equal(pair.left.ActualTitrantConcentration, pair.right.ActualTitrantConcentration, 12);
            }
            finally
            {
                File.Delete(millimolarPath);
                File.Delete(molarPath);
            }
        }

        [Fact]
        public void IntegratedHeatWithoutTrajectoryRetainsUsableInjectionDataAndMarksMetadataUnresolved()
        {
            var path = WriteTemporaryIntegratedFile(
                "DH,INJV\n" +
                "1e-6,2\n" +
                "2e-6,3\n");

            try
            {
                var experiment = ReadIntegratedFile(path);

                Assert.Equal(2, experiment.Injections.Count);
                Assert.Equal(2e-6, experiment.Injections[0].Volume, 12);
                Assert.Equal(1e-6, experiment.Injections[0].RawPeakArea.Value, 12);
                Assert.False(double.IsFinite(experiment.CellVolume));
                Assert.False(double.IsFinite(experiment.CellConcentration.Value));
                Assert.False(double.IsFinite(experiment.SyringeConcentration.Value));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void IntegratedHeatInferenceToleratesAnIncompleteIntermediateState()
        {
            var lines = BuildTrajectory(DilutionMethod.MicroCal, concentrationsAreMilliMolar: true)
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var incomplete = lines[2].Split(',');
            incomplete[2] = "--";
            incomplete[3] = "--";
            lines[2] = string.Join(",", incomplete);
            var path = WriteTemporaryIntegratedFile(string.Join("\n", lines) + "\n");

            try
            {
                var experiment = ReadIntegratedFile(path);

                AssertRelative(1.4e-3, experiment.CellVolume, 1e-10);
                AssertRelative(4e-3, experiment.SyringeConcentration.Value, 1e-10);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void IntegratedHeatInconsistentTrajectoryIsLeftUnresolved()
        {
            var path = WriteTemporaryIntegratedFile(
                "DH,INJV,Xt,Mt\n" +
                "1e-6,10,0,0.1\n" +
                "1e-6,10,0.0283,0.0993\n" +
                "--,--,0.5,0.08\n");

            try
            {
                var experiment = ReadIntegratedFile(path);

                Assert.False(double.IsFinite(experiment.CellVolume));
                Assert.False(double.IsFinite(experiment.SyringeConcentration.Value));
                Assert.Equal(2, experiment.Injections.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void IntegratedHeatWithoutTerminalStateRetainsLastInjection()
        {
            var path = WriteTemporaryIntegratedFile(
                "DH,INJV,Xt,Mt,Xmt,NDH\n" +
                "1e-6,10,0,0.1,0.28,--\n" +
                "2e-6,10,0.0283,0.0993,0.57,--\n");

            try
            {
                var experiment = ReadIntegratedFile(path);

                Assert.Equal(2, experiment.Injections.Count);
                Assert.True(double.IsFinite(experiment.CellVolume));
                Assert.True(double.IsFinite(experiment.SyringeConcentration.Value));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void IntegratedHeatRequiresDhAndInjectionVolumeHeaders()
        {
            var path = WriteTemporaryIntegratedFile("DH,Xt,Mt\n1e-6,0,0.1\n");
            try
            {
                var exception = Assert.Throws<FormatException>(() => ReadIntegratedFile(path));
                Assert.Contains("DH and INJV", exception.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData("bad,10,0,0.1", "Invalid DH value on line 2")]
        [InlineData("1e-6,bad,0,0.1", "Invalid INJV value on line 2")]
        [InlineData("1e-6,0,0,0.1", "INJV must be positive on line 2")]
        [InlineData("1e-6,,0,0.1", "must provide both DH and INJV")]
        public void IntegratedHeatRejectsMalformedRequiredInjectionValues(string row, string expectedMessage)
        {
            var path = WriteTemporaryIntegratedFile("DH,INJV,Xt,Mt\n" + row + "\n");
            try
            {
                var exception = Assert.Throws<FormatException>(() => ReadIntegratedFile(path));
                Assert.Contains(expectedMessage, exception.Message);
            }
            finally
            {
                File.Delete(path);
            }
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

        static ExperimentData ReadIntegratedFile(
            string path,
            bool concentrationsAreMilliMolar = true,
            DilutionMethod dilutionMethod = DilutionMethod.MicroCal)
        {
            return IntegratedHeatReader.ReadFile(
                path,
                concentrationsAreMilliMolar,
                dilutionMethod,
                reprocessIntegratedHeatData: true);
        }

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

        static void AssertRelative(double expected, double actual, double relativeTolerance)
        {
            var scale = Math.Max(Math.Abs(expected), 1e-15);
            Assert.InRange(Math.Abs(actual - expected) / scale, 0, relativeTolerance);
        }

        static string BuildTrajectory(DilutionMethod method, bool concentrationsAreMilliMolar)
        {
            const double cellVolume = 1.4e-3;
            const double cellConcentration = 100e-6;
            const double syringeConcentration = 4e-3;
            const double injectionVolume = 10e-6;
            const int injectionCount = 4;
            var concentrationOutputScale = concentrationsAreMilliMolar ? 1000.0 : 1.0;
            var lines = new List<string> { "DH,INJV,Xt,Mt,Xmt,NDH" };

            for (var injectionIndex = 0; injectionIndex < injectionCount; injectionIndex++)
            {
                var state = ConcentrationState(method, injectionIndex * injectionVolume, cellVolume, cellConcentration, syringeConcentration);
                lines.Add(string.Join(",", new[]
                {
                    "1e-6",
                    "10",
                    (state.xt * concentrationOutputScale).ToString("R", CultureInfo.InvariantCulture),
                    (state.mt * concentrationOutputScale).ToString("R", CultureInfo.InvariantCulture),
                    state.mt > 0 ? (state.xt / state.mt).ToString("R", CultureInfo.InvariantCulture) : "--",
                    "--",
                }));
            }

            var terminal = ConcentrationState(method, injectionCount * injectionVolume, cellVolume, cellConcentration, syringeConcentration);
            lines.Add(string.Join(",", new[]
            {
                "--",
                "--",
                (terminal.xt * concentrationOutputScale).ToString("R", CultureInfo.InvariantCulture),
                (terminal.mt * concentrationOutputScale).ToString("R", CultureInfo.InvariantCulture),
                "--",
                "--",
            }));
            return string.Join("\n", lines) + "\n";
        }

        static (double xt, double mt) ConcentrationState(
            DilutionMethod method,
            double cumulativeVolume,
            double cellVolume,
            double cellConcentration,
            double syringeConcentration)
        {
            if (method == DilutionMethod.Exponential)
            {
                var remaining = Math.Exp(-cumulativeVolume / cellVolume);
                return (syringeConcentration * (1 - remaining), cellConcentration * remaining);
            }

            var a = cumulativeVolume / (2 * cellVolume);
            return (
                syringeConcentration * (cumulativeVolume / cellVolume) * (1 - a),
                cellConcentration * ((1 - a) / (1 + a)));
        }

        static string WriteTemporaryIntegratedFile(string contents)
        {
            var path = Path.Combine(Path.GetTempPath(), $"integrated-{Guid.NewGuid():N}.dat");
            File.WriteAllText(path, contents, Encoding.UTF8);
            return path;
        }

        sealed class FixtureEnergyUnitPromptService : IImportPromptService
        {
            public EnergyUnitPromptResult AskForEnergyUnit(
                string fileName,
                string encounteredValue,
                bool allowQueueReuse)
            {
                var unit = Path.GetFileName(fileName) switch
                {
                    "CURVE-1.aff" => EnergyUnit.MicroCal,
                    "CURVE-2.aff" => EnergyUnit.Cal,
                    _ => EnergyUnit.Joule,
                };
                return new EnergyUnitPromptResult(
                    unit,
                    useForRemainingFilesInQueue: false,
                    isCancelled: false);
            }
        }
    }
}
