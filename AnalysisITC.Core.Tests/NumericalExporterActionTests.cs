using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection(FileTypeFixtureCollectionDefinition.Name)]
    public sealed class NumericalExporterActionTests : IAsyncLifetime
    {
        readonly string folder = Path.Combine(Path.GetTempPath(), "numerical-export-action-" + Guid.NewGuid().ToString("N"));
        readonly ISettingsStore oldStore = PlatformServices.SettingsStore;
        readonly IExportPromptService oldPrompt = PlatformServices.ExportPromptService;
        readonly string oldName = AppSettings.ExportOutputBaseName;
        readonly ExportType oldType = AppSettings.DefaultExportType;
        readonly ExportDataSelection oldSelection = AppSettings.ExportSelectionMode;
        readonly bool oldUnifiedAxis = AppSettings.UnifyTimeAxisForExport;
        readonly bool oldBaselineCorrected = AppSettings.ExportBaselineCorrectedData;
        readonly bool oldFitPoints = AppSettings.ExportFitPointsWithPeaks;
        readonly ExportColumns oldColumns = AppSettings.ExportColumns;
        readonly EventHandler<HandledException> oldMessageHandler = AppEventHandler.ShowAppMessage;
        readonly List<string> statuses = new List<string>();
        readonly EventHandler<string> statusHandler;
        readonly List<HandledException> messages = new List<HandledException>();
        readonly EventHandler<HandledException> messageHandler;

        public NumericalExporterActionTests()
        {
            Directory.CreateDirectory(folder);
            PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
            DataManager.Clear(DataClearMode.ResetSession);
            AppSettings.ExportOutputBaseName = "numerical";
            AppSettings.DefaultExportType = ExportType.Data;
            AppSettings.ExportSelectionMode = ExportDataSelection.AllData;
            statusHandler = (_, value) => statuses.Add(value);
            messageHandler = (_, error) => messages.Add(error);
            StatusBarManager.StatusUpdated += statusHandler;
            AppEventHandler.ShowAppMessage += messageHandler;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            StatusBarManager.StatusUpdated -= statusHandler;
            // Export starts an async-void status animation. Stop and drain it before
            // another fixture observes secondary status updates in this process.
            StatusBarManager.SetStatus("", 0);
            var scrolling = typeof(StatusBarManager).GetField("secondaryStatusSuppressionCount", BindingFlags.Static | BindingFlags.NonPublic)!;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((int)scrolling.GetValue(null)! > 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            var remainingScrollers = (int)scrolling.GetValue(null)!;
            StatusBarManager.ClearAppStatus();
            AppEventHandler.ShowAppMessage -= messageHandler;
            AppEventHandler.ShowAppMessage = oldMessageHandler;
            PlatformServices.RegisterExportPromptService(oldPrompt);
            PlatformServices.RegisterSettingsStore(oldStore);
            AppSettings.ExportOutputBaseName = oldName;
            AppSettings.DefaultExportType = oldType;
            AppSettings.ExportSelectionMode = oldSelection;
            AppSettings.UnifyTimeAxisForExport = oldUnifiedAxis;
            AppSettings.ExportBaselineCorrectedData = oldBaselineCorrected;
            AppSettings.ExportFitPointsWithPeaks = oldFitPoints;
            AppSettings.ExportColumns = oldColumns;
            DataManager.Clear(DataClearMode.ResetSession);
            Directory.Delete(folder, recursive: true);
            Assert.Equal(0, remainingScrollers);
        }

        [Fact]
        public async Task EmptyDataSelectionRetainsTheNoValidDataWarningWithoutSuccess()
        {
            PlatformServices.RegisterExportPromptService(new FakeExportPrompt
            {
                Folder = folder,
                Confirm = _ => throw new InvalidOperationException("An empty batch must not request replacement approval."),
            });

            await Exporter.ExportAsync(ExportType.Data, ExportDataSelection.AllData);

            var warning = Assert.Single(messages);
            Assert.Equal(HandledException.Severity.Warning, warning.Level);
            Assert.Equal("No Valid Data", warning.Title);
            Assert.Empty(Directory.GetFiles(folder));
            Assert.DoesNotContain(statuses, status => status.Contains("Finished exporting", StringComparison.Ordinal));
        }

        [Fact]
        public async Task PickerExceptionIsReportedOnceByTheActionBoundary()
        {
            PlatformServices.RegisterExportPromptService(new FakeExportPrompt
            {
                ChooseFolder = _ => Task.FromException<string>(new IOException("picker unavailable")),
            });

            await Exporter.ExportAsync(ExportType.Data, ExportDataSelection.AllData);

            var error = Assert.Single(messages);
            Assert.Contains("picker unavailable", error.Message);
            Assert.DoesNotContain(statuses, status => status.Contains("Finished exporting", StringComparison.Ordinal));
        }

        [Fact]
        public async Task CancelledFolderChoiceDoesNotPersistEditedExportSettingsOrWriteFiles()
        {
            DataManager.AddData(Experiment("cancelled"));
            AppSettings.DefaultExportType = ExportType.Data;
            AppSettings.ExportOutputBaseName = "before";
            AppSettings.ExportSelectionMode = ExportDataSelection.AllData;
            AppSettings.UnifyTimeAxisForExport = false;
            AppSettings.ExportBaselineCorrectedData = false;
            AppSettings.ExportFitPointsWithPeaks = false;
            AppSettings.ExportColumns = ExportColumns.MolarRatio;
            PlatformServices.RegisterExportPromptService(new FakeExportPrompt
            {
                ChooseFolder = settings =>
                {
                    settings.Export = ExportType.InterchangeCsv;
                    settings.OutputBaseName = "edited";
                    settings.Selection = ExportDataSelection.SelectedData;
                    settings.UnifyTimeAxis = true;
                    settings.ExportBaselineCorrectDataPoints = true;
                    settings.ExportFittedPeaks = true;
                    settings.Columns = ExportColumns.Default;
                    return Task.FromResult<string>(null);
                },
            });

            await Exporter.ExportAsync();

            Assert.Equal(ExportType.Data, AppSettings.DefaultExportType);
            Assert.Equal("before", AppSettings.ExportOutputBaseName);
            Assert.Equal(ExportDataSelection.AllData, AppSettings.ExportSelectionMode);
            Assert.False(AppSettings.UnifyTimeAxisForExport);
            Assert.False(AppSettings.ExportBaselineCorrectedData);
            Assert.False(AppSettings.ExportFitPointsWithPeaks);
            Assert.Equal(ExportColumns.MolarRatio, AppSettings.ExportColumns);
            Assert.Empty(Directory.GetFiles(folder));
            Assert.Empty(messages);
        }

        [Fact]
        public async Task DestinationCreatedDuringConfirmationIsPreservedAndReportedOnce()
        {
            DataManager.AddData(Experiment("single"));
            var raceBytes = System.Text.Encoding.UTF8.GetBytes("external file");
            PlatformServices.RegisterExportPromptService(new FakeExportPrompt
            {
                Folder = folder,
                Confirm = paths =>
                {
                    File.WriteAllBytes(Assert.Single(paths), raceBytes);
                    return true;
                },
            });

            await Exporter.ExportAsync(ExportType.Data, ExportDataSelection.AllData);

            var error = Assert.Single(messages);
            Assert.Contains("0 of 1", error.Message);
            Assert.Contains("numerical.csv", error.Message);
            Assert.Equal(raceBytes, File.ReadAllBytes(Path.Combine(folder, "numerical.csv")));
            Assert.DoesNotContain(statuses, status => status.Contains("Finished exporting", StringComparison.Ordinal));
        }

        [Fact]
        public async Task MidBatchFilesystemFailureKeepsFirstOutputAndDoesNotReportSuccess()
        {
            DataManager.AddData(new[] { Experiment("first"), Experiment("second") });
            var paths = new List<string>();
            PlatformServices.RegisterExportPromptService(new FakeExportPrompt
            {
                Folder = folder,
                Confirm = outputPaths =>
                {
                    paths.AddRange(outputPaths);
                    Directory.CreateDirectory(outputPaths.Last());
                    return true;
                },
            });

            await Exporter.ExportAsync(ExportType.Data, ExportDataSelection.AllData);

            var error = Assert.Single(messages);
            Assert.Contains("1 of 2", error.Message);
            Assert.True(File.Exists(paths[0]));
            Assert.True(Directory.Exists(paths[1]));
            Assert.DoesNotContain(statuses, status => status.Contains("Finished exporting", StringComparison.Ordinal));
        }

        static ExperimentData Experiment(string name) => new ExperimentData(name)
        {
            DataPoints = new List<DataPoint> { new DataPoint(0, 1), new DataPoint(1, 2) },
        };

        sealed class FakeExportPrompt : IExportPromptService
        {
            public string Folder;
            public Func<ExportAccessoryViewSettings, Task<string>> ChooseFolder;
            public Func<IEnumerable<string>, bool> Confirm;
            public Task<string> ChooseExportFolderAsync(ExportAccessoryViewSettings settings) =>
                ChooseFolder != null ? ChooseFolder(settings) : Task.FromResult(Folder);
            public bool ConfirmOverwrite(IEnumerable<string> outputPaths) => Confirm?.Invoke(outputPaths) ?? true;
        }
    }
}
