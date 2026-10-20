using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection(FileTypeFixtureCollectionDefinition.Name)]
    public sealed class ExportDecimalCultureTests : IAsyncLifetime
    {
        static readonly ExportColumns EveryColumn = ExportColumns.MolarRatio
            | ExportColumns.Included
            | ExportColumns.Peak
            | ExportColumns.PeakError
            | ExportColumns.Fit
            | ExportColumns.InjectionVolume
            | ExportColumns.InjectionDelay
            | ExportColumns.IntegrationLength
            | ExportColumns.CellConc
            | ExportColumns.SyrConc
            | ExportColumns.Temperature;

        readonly string folder = Path.Combine(Path.GetTempPath(), "export-decimal-culture-" + Guid.NewGuid().ToString("N"));
        readonly ISettingsStore oldStore = PlatformServices.SettingsStore;
        readonly IExportPromptService oldExportPrompt = PlatformServices.ExportPromptService;
        readonly IImportPromptService oldImportPrompt = PlatformServices.ImportPromptService;
        readonly string oldOutputName = AppSettings.ExportOutputBaseName;
        readonly ExportType oldExportType = AppSettings.DefaultExportType;
        readonly ExportDataSelection oldSelection = AppSettings.ExportSelectionMode;
        readonly ExportColumns oldColumns = AppSettings.ExportColumns;
        readonly bool oldUnifiedAxis = AppSettings.UnifyTimeAxisForExport;
        readonly bool oldBaselineCorrected = AppSettings.ExportBaselineCorrectedData;
        readonly bool oldFitPoints = AppSettings.ExportFitPointsWithPeaks;
        readonly EnergyUnitFamily oldEnergyFamily = AppSettings.EnergyUnitFamily;
        readonly CultureInfo oldCulture = CultureInfo.CurrentCulture;
        readonly CultureInfo oldUiCulture = CultureInfo.CurrentUICulture;

        public ExportDecimalCultureTests()
        {
            Directory.CreateDirectory(folder);
            PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
            DataManager.Clear(DataClearMode.ResetSession);
            AppSettings.ExportSelectionMode = ExportDataSelection.AllData;
            AppSettings.ExportColumns = EveryColumn;
            AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            // ExportAsync starts an async-void status scroller. Let it finish before
            // the next fixture checks project-loading messages in the same process.
            StatusBarManager.SetStatus("", 0);
            var scrolling = typeof(StatusBarManager).GetField("secondaryStatusSuppressionCount", BindingFlags.Static | BindingFlags.NonPublic)!;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((int)scrolling.GetValue(null)! > 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            var remainingScrollers = (int)scrolling.GetValue(null)!;
            StatusBarManager.ClearAppStatus();
            IntegratedHeatReader.EndImportQueue();
            PlatformServices.RegisterImportPromptService(oldImportPrompt);
            PlatformServices.RegisterExportPromptService(oldExportPrompt);
            PlatformServices.RegisterSettingsStore(oldStore);
            AppSettings.ExportOutputBaseName = oldOutputName;
            AppSettings.DefaultExportType = oldExportType;
            AppSettings.ExportSelectionMode = oldSelection;
            AppSettings.ExportColumns = oldColumns;
            AppSettings.UnifyTimeAxisForExport = oldUnifiedAxis;
            AppSettings.ExportBaselineCorrectedData = oldBaselineCorrected;
            AppSettings.ExportFitPointsWithPeaks = oldFitPoints;
            AppSettings.EnergyUnitFamily = oldEnergyFamily;
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUiCulture;
            DataManager.Clear(DataClearMode.ResetSession);
            Directory.Delete(folder, recursive: true);
            Assert.Equal(0, remainingScrollers);
        }

        [Fact]
        public async Task CsvAndThirdPartyWriteEveryNumericColumnWithInvariantSeparators()
        {
            var expectedHeaders = new[]
            {
                "MolarRatio", "Included", "PeakHeat", "PeakHeatError", "Fit", "InjVolume",
                "InjDelay", "PeakIntegrationLength", "[cell]", "[syr]", "Temperature"
            };
            var referenceRows = new System.Collections.Generic.Dictionary<(ExportType format, bool corrected), string>();

            foreach (var cultureName in new[] { "en-US", "da-DK", "de-DE", "fr-FR" })
            foreach (var format in new[] { ExportType.CSV, ExportType.ThirdParty })
            foreach (var offsetCorrected in new[] { false, true })
            {
                SetCulture(cultureName);
                DataManager.Clear(DataClearMode.ResetSession);
                DataManager.AddData(CreateExperiment());

                var name = $"{format}-{cultureName}-{offsetCorrected}";
                var outputFolder = Path.Combine(folder, name);
                AppSettings.ExportOutputBaseName = name;
                PlatformServices.RegisterExportPromptService(new ExportPrompt(outputFolder, EveryColumn, offsetCorrected));
                await Exporter.ExportAsync(format, ExportDataSelection.AllData);

                var path = Path.Combine(outputFolder, name + ".csv");
                Assert.True(File.Exists(path), $"{format} output was not written for {cultureName}.");
                var lines = File.ReadAllLines(path);
                var header = lines[0].Split(',');
                var values = lines[1].Split(',');

                Assert.Equal(expectedHeaders, header);
                Assert.Equal(expectedHeaders.Length, values.Length);
                Assert.Equal("0.75000", values[0]);
                Assert.Equal("1", values[1]);
                Assert.Equal("2.50E-006", values[5]);
                Assert.Equal("120.5", values[6]);
                Assert.Equal("45.3", values[7]);
                Assert.Equal("0.00002000", values[8]);
                Assert.Equal("0.00020000", values[9]);
                Assert.Equal("25.50", values[10]);
                Assert.All(values.Where((_, index) => index != 1), value =>
                    Assert.True(double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
                        $"'{value}' was not one invariant numeric field for {cultureName}/{format}."));

                var key = (format, offsetCorrected);
                if (cultureName == "en-US") referenceRows[key] = lines[1];
                else Assert.Equal(referenceRows[key], lines[1]);
            }

            Assert.NotEqual(referenceRows[(ExportType.CSV, false)], referenceRows[(ExportType.CSV, true)]);
            Assert.NotEqual(referenceRows[(ExportType.ThirdParty, false)], referenceRows[(ExportType.ThirdParty, true)]);
        }

        [Fact]
        public async Task PreviouslyInvariantExportsKeepTheirNumericRowsAndMetadataAcrossCultures()
        {
            var cases = new[]
            {
                (ExportType.Data, "thermogram-raw", false),
                (ExportType.Data, "thermogram-corrected", true),
                (ExportType.Peaks, "peaks-unoffset", false),
                (ExportType.Peaks, "peaks-offset", true),
                (ExportType.InterchangeCsv, "combined", false),
                (ExportType.ITCsim, "itcsim-unoffset", false),
                (ExportType.ITCsim, "itcsim-offset", true),
                (ExportType.MicroCal, "microcal", false),
            };
            var referenceOutputs = new System.Collections.Generic.Dictionary<string, string>();

            foreach (var cultureName in new[] { "en-US", "da-DK", "de-DE", "fr-FR" })
            foreach (var (format, variant, option) in cases)
            {
                SetCulture(cultureName);
                DataManager.Clear(DataClearMode.ResetSession);
                DataManager.AddData(CreateExperiment());

                var name = $"{variant}-{cultureName}";
                var outputFolder = Path.Combine(folder, name);
                AppSettings.ExportOutputBaseName = name;
                PlatformServices.RegisterExportPromptService(new ExportPrompt(
                    outputFolder, EveryColumn, option, baselineCorrected: format == ExportType.Data && option));
                await Exporter.ExportAsync(format, ExportDataSelection.AllData);

                var extension = format == ExportType.MicroCal ? ".dat" : ".csv";
                var path = Path.Combine(outputFolder, name + extension);
                Assert.True(File.Exists(path), $"{format} output was not written for {cultureName}.");
                var lines = File.ReadAllLines(path);
                var output = string.Join("\n", lines);
                var key = $"{format}:{variant}";
                if (cultureName == "en-US") referenceOutputs[key] = output;
                else Assert.Equal(referenceOutputs[key], output);

                switch (format)
                {
                    case ExportType.Data:
                        Assert.Equal("time_s,power_w", lines[0]);
                        Assert.Equal(3, lines.Length);
                        Assert.Equal(2, lines[1].Split(',').Length);
                        Assert.Equal(2, lines[2].Split(',').Length);
                        AssertNumericFields(lines.Skip(1), allowUnavailable: false);
                        break;
                    case ExportType.Peaks:
                        Assert.Equal(2, lines.Length);
                        Assert.Equal(5, lines[0].Split(',').Length);
                        Assert.Equal(5, lines[1].Split(',').Length);
                        AssertNumericFields(lines.Skip(1), allowUnavailable: true);
                        break;
                    case ExportType.InterchangeCsv:
                        Assert.Equal(3, lines.Length);
                        Assert.Equal(8, lines[0].Split(',').Length);
                        Assert.All(lines.Skip(1), line => Assert.Equal(8, line.Split(',').Length));
                        AssertNumericFields(lines.Skip(1), allowUnavailable: true);
                        break;
                    case ExportType.ITCsim:
                        Assert.Equal(7, lines.Length);
                        Assert.Equal(5, lines[0].Split(',').Length);
                        Assert.Equal(5, lines[1].Split(',').Length);
                        AssertNumericFields(lines.Skip(1).Take(1), allowUnavailable: false);
                        Assert.Equal(new[]
                        {
                            "#ITCSIM METADATA LIST",
                            "#EXPINFO ENERGYUNIT KJ",
                            "#EXPINFO CELLCONC 20.00 uM",
                            "#EXPINFO SYRINGECONC 200.00 uM",
                            "#EXPINFO CELLVOLUME 0.000200000 L"
                        }, lines.Skip(2));
                        break;
                    case ExportType.MicroCal:
                        Assert.Equal(3, lines.Length);
                        Assert.All(lines, line => Assert.Equal(8, line.Split(',').Length));
                        AssertNumericFields(lines.Skip(1), allowUnavailable: true);
                        break;
                }
            }

            Assert.NotEqual(referenceOutputs[$"{ExportType.Data}:thermogram-raw"], referenceOutputs[$"{ExportType.Data}:thermogram-corrected"]);
            Assert.NotEqual(referenceOutputs[$"{ExportType.Peaks}:peaks-unoffset"], referenceOutputs[$"{ExportType.Peaks}:peaks-offset"]);
            Assert.NotEqual(referenceOutputs[$"{ExportType.ITCsim}:itcsim-unoffset"], referenceOutputs[$"{ExportType.ITCsim}:itcsim-offset"]);
        }

        [Fact]
        public async Task PytcFileUsesInvariantDecimalsAndReopensToTheOriginalNumericValues()
        {
            foreach (var cultureName in new[] { "en-US", "da-DK", "de-DE", "fr-FR" })
            {
                SetCulture(cultureName);
                DataManager.Clear(DataClearMode.ResetSession);
                var data = CreateExperiment();
                DataManager.AddData(data);

                var name = "pytc-" + cultureName;
                var outputFolder = Path.Combine(folder, name);
                AppSettings.ExportOutputBaseName = name;
                PlatformServices.RegisterExportPromptService(new ExportPrompt(outputFolder, EveryColumn, false));
                await Exporter.ExportAsync(ExportType.PYTC, ExportDataSelection.AllData);

                var path = Path.Combine(outputFolder, name + ".dh");
                Assert.True(File.Exists(path), $"pytc output was not written for {cultureName}.");
                var lines = File.ReadAllLines(path);
                var headerValues = lines[2].Split(',');
                var injectionValues = lines[5].Split(',');
                Assert.Equal(4, headerValues.Length);
                Assert.Equal(2, injectionValues.Length);
                Assert.Equal(new[] { "25.500", "0.0200", "0.2000", "0.20000" }, headerValues);
                Assert.Equal(new[] { "2.50", "-2.00000" }, injectionValues);

                IntegratedHeatReader.BeginImportQueue();
                PlatformServices.RegisterImportPromptService(new MicroCalUnitPrompt());
                try
                {
                    var reopened = IntegratedHeatReader.ReadFile(
                        path,
                        concentrationsAreMilliMolar: true,
                        dilutionMethod: DilutionMethod.MicroCal,
                        reprocessIntegratedHeatData: false);
                    AssertClose(25.5, reopened.MeasuredTemperature);
                    AssertClose(20e-6, reopened.CellConcentration.Value);
                    AssertClose(200e-6, reopened.SyringeConcentration.Value);
                    AssertClose(200e-6, reopened.CellVolume);
                    AssertClose(2.5e-6, reopened.Injections.Single().Volume);
                    AssertClose(-2.0 * 4.184e-6, reopened.Injections.Single().PeakArea.Value);
                }
                finally
                {
                    IntegratedHeatReader.EndImportQueue();
                    PlatformServices.RegisterImportPromptService(oldImportPrompt);
                }
            }
        }

        void SetCulture(string cultureName)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
        }

        static ExperimentData CreateExperiment()
        {
            var data = new ExperimentData("decimal-culture-export")
            {
                MeasuredTemperature = 25.5,
                TargetTemperature = 25.5,
                CellConcentration = new FloatWithError(20e-6),
                SyringeConcentration = new FloatWithError(200e-6),
                CellVolume = 200e-6,
                DataPoints = new() { new DataPoint(0, 1.25f), new DataPoint(1.5f, -2.75f) },
                BaseLineCorrectedDataPoints = new() { new DataPoint(0, 0.25f), new DataPoint(1.5f, -0.75f) },
            };
            var injection = new InjectionData(data, "(2.5,60,120.5,5", 0)
            {
                Temperature = 25.5,
                Ratio = 0.75,
                ActualCellConcentration = 20e-6,
                ActualTitrantConcentration = 200e-6,
            };
            injection.Include = true;
            injection.SetIntegrationLengthByTime(45.26f);
            injection.SetPeakArea(new FloatWithError(-8.368e-6, 0.125e-6));
            data.Injections.Add(injection);

            var model = new CultureProbeModel(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 123.456789);
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            data.Model = model;
            return data;
        }

        static void AssertClose(double expected, double actual)
        {
            var tolerance = Math.Max(1e-15, Math.Abs(expected) * 1e-12);
            Assert.InRange(actual, expected - tolerance, expected + tolerance);
        }

        static void AssertNumericFields(System.Collections.Generic.IEnumerable<string> rows, bool allowUnavailable)
        {
            foreach (var row in rows)
            foreach (var value in row.Split(','))
            {
                if (allowUnavailable && (value.Length == 0 || value == "--")) continue;
                Assert.True(double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
                    $"'{value}' was not one invariant numeric field.");
            }
        }

        sealed class ExportPrompt(string outputFolder, ExportColumns columns, bool offsetCorrected, bool baselineCorrected = false) : IExportPromptService
        {
            public Task<string> ChooseExportFolderAsync(ExportAccessoryViewSettings settings)
            {
                Directory.CreateDirectory(outputFolder);
                settings.Columns = columns;
                settings.ExportOffsetCorrected = offsetCorrected;
                settings.ExportBaselineCorrectDataPoints = baselineCorrected;
                return Task.FromResult(outputFolder);
            }

            public bool ConfirmOverwrite(System.Collections.Generic.IEnumerable<string> outputPaths) => true;
        }

        sealed class MicroCalUnitPrompt : IImportPromptService
        {
            public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse)
                => new(EnergyUnit.MicroCal, allowQueueReuse, false);
        }

        sealed class CultureProbeModel(ExperimentData data) : Model(data)
        {
            public override double Evaluate(int injectionindex, bool withoffset = true)
            {
                var heat = withoffset ? 12_345.678 : 12_222.221;
                return heat * Data.Injections[injectionindex].InjectionMass;
            }
        }
    }
}
