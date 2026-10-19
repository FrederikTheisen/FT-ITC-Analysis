using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Platform;
using AnalysisITC.Platform.Avalonia;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class SettingsUpdateTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();
    readonly ISettingsStore originalStore = PlatformServices.SettingsStore;
    readonly IMainThreadDispatcher originalDispatcher = PlatformServices.MainThreadDispatcher;
    readonly IFileSavePromptService originalPrompt = PlatformServices.FileSavePromptService;
    readonly string originalPath = AppSettings.LastDocumentPath;
    readonly string originalPublicCode = AppSettings.InterpretationPublicClientCode;
    readonly object[] originalLive = CaptureLive();
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-settings-ui-" + Guid.NewGuid().ToString("N"));

    public SettingsUpdateTests()
    {
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        PlatformServices.RegisterMainThreadDispatcher(new AvaloniaMainThreadDispatcher());
        PreferencesState.Defaults().ApplyToSettings();
        AppSettings.DefaultSolverAlgorithm = SolverAlgorithm.NelderMead;
        AppSettings.DefaultErrorEstimationMethod = ErrorEstimationMethod.None;
        AppSettings.DefaultBootstrapIterations = 10;
        AppSettings.IncludeConcentrationErrorsInBootstrap = false;
        AppSettings.UseInjectionErrorWeightedFitting = false;
        AppSettings.CreateSingleAnalysisResult = false;
        AppSettings.SkipUncertaintyEstimationForNoBinding = false;
        FittingOptionsController.Algorithm = SolverAlgorithm.LevenbergMarquardt;
        FittingOptionsController.ErrorEstimationMethod = ErrorEstimationMethod.LeaveOneOut;
        FittingOptionsController.BootstrapIterations = 100;
        FittingOptionsController.IncludeConcentrationVariance = true;
        FittingOptionsController.UseErrorWeightedFitting = true;
        FittingOptionsController.UnlockBootstrapParameters = true;
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.Initialize();
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.MarkClean();
        original.ApplyToSettings();
        AppSettings.InterpretationPublicClientCode = originalPublicCode;
        AppSettings.LastDocumentPath = originalPath;
        AppSettings.ApplySettings();
        Dispatcher.UIThread.RunJobs();
        RestoreLive(originalLive);
        PlatformServices.RegisterSettingsStore(originalStore);
        PlatformServices.RegisterMainThreadDispatcher(originalDispatcher);
        PlatformServices.RegisterFileSavePromptService(originalPrompt);
        Directory.Delete(directory, true);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAsAndPreferencesPreserveReopenedControlsAndNextFit(bool applyPreferences)
    {
        var data = Experiment();
        DataManager.AddData(data);
        DataManager.SelectIndex(0);
        var expected = CaptureLive();
        var workspace = new AnalysisWorkspaceControl { Experiment = data };
        var window = new Window { Content = workspace };
        window.Show();
        try
        {
            var path = Path.Combine(directory, "saved.ftxtc");
            PlatformServices.RegisterFileSavePromptService(new FixedPrompt(path));
            Assert.True(await ProjectWriter.SaveAsync());
            Assert.True(File.Exists(path));
            Assert.Equal(path, AppSettings.LastDocumentPath);
            if (applyPreferences)
            {
                var staged = PreferencesState.FromSettings();
                staged.DefaultBootstrapIterations = 500;
                staged.DefaultErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
                staged.Apply();
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected, CaptureLive());

            window.Content = null;
            workspace = new AnalysisWorkspaceControl { Experiment = data };
            window.Content = workspace;
            Assert.Equal(2, workspace.ErrorMethodComboForTesting.SelectedIndex);
            Assert.Equal("100", workspace.BootstrapIterationsBoxForTesting.Text);
            Assert.True(workspace.WeightedFitCheckForTesting.IsChecked);
            Assert.True(workspace.ConcentrationUncertaintyCheck.IsChecked);
            Assert.True(workspace.UnlockParametersCheck.IsChecked);
            workspace.ModelComboForTesting.SelectedItem = workspace.ModelComboForTesting.Items.OfType<ComboBoxItem>()
                .Single(item => item.Tag is AnalysisModel model && model == AnalysisModel.OneSetOfSites);
            SolverInterface? started = null;
            EventHandler<TerminationFlag> onStarted = (sender, _) => started = (SolverInterface)sender!;
            SolverInterface.AnalysisStarted += onStarted;
            try
            {
                workspace.RunFit();
                Assert.NotNull(started);
                Assert.Equal(SolverAlgorithm.LevenbergMarquardt, started.SolverAlgorithm);
                Assert.Equal(ErrorEstimationMethod.LeaveOneOut, started.ErrorEstimationMethod);
                Assert.Equal(100, started.BootstrapIterations);
                Assert.True(started.UseErrorWeightedFitting);
            }
            finally
            {
                SolverInterface.AnalysisStarted -= onStarted;
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (ProcessingActivity.IsBusy && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.False(ProcessingActivity.IsBusy);
                Dispatcher.UIThread.RunJobs();
            }

            workspace.RestoreAnalysisDefaults();
            Assert.Equal(applyPreferences ? 1 : 0, workspace.ErrorMethodComboForTesting.SelectedIndex);
            Assert.Equal(applyPreferences ? "500" : "10", workspace.BootstrapIterationsBoxForTesting.Text);
            Assert.False(workspace.UnlockParametersCheck.IsChecked);
            Assert.False(workspace.WeightedFitCheckForTesting.IsChecked);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task BackgroundEnrollmentAndAccountUpdatesRefreshOnUiThreadAndKeepFitOptions()
    {
        using var http = new HttpClient(new EnrollmentHandler());
        var client = new FtItcInterpretationClient(http, new Uri("https://example.invalid"));
        var workspace = new AnalysisWorkspaceControl();
        var window = new Window { Content = workspace };
        var expected = CaptureLive();
        var observed = new List<string>();
        EventHandler onApplied = (_, _) => Observe("applied");
        EventHandler onUpdated = (_, _) => Observe("updated");
        window.Show();
        AppSettings.InterpretationPublicClientCode = "";
        AppSettings.SettingsApplied += onApplied;
        AppSettings.SettingsDidUpdate += onUpdated;
        try
        {
            await Task.Run(() => client.GetInterpretationOptionsAsync(""));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "applied", "updated" }, observed);
            Assert.Equal(expected, CaptureLive());
            observed.Clear();
            await Task.Run(() => AppSettings.PersistInterpretationAccount("test-code", new InterpretationAccountResponse()));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "updated" }, observed);
            Assert.Equal(expected, CaptureLive());
        }
        finally
        {
            AppSettings.SettingsApplied -= onApplied;
            AppSettings.SettingsDidUpdate -= onUpdated;
            window.Close();
        }
        void Observe(string name)
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            observed.Add(name);
        }
    }

    static object[] CaptureLive() => new object[]
    {
        FittingOptionsController.Algorithm, FittingOptionsController.ErrorEstimationMethod,
        FittingOptionsController.BootstrapIterations, FittingOptionsController.IncludeConcentrationVariance,
        FittingOptionsController.UseErrorWeightedFitting, FittingOptionsController.UnlockBootstrapParameters
    };
    static void RestoreLive(object[] values)
    {
        FittingOptionsController.Algorithm = (SolverAlgorithm)values[0];
        FittingOptionsController.ErrorEstimationMethod = (ErrorEstimationMethod)values[1];
        FittingOptionsController.BootstrapIterations = (int)values[2];
        FittingOptionsController.IncludeConcentrationVariance = (bool)values[3];
        FittingOptionsController.UseErrorWeightedFitting = (bool)values[4];
        FittingOptionsController.UnlockBootstrapParameters = (bool)values[5];
    }

    static ExperimentData Experiment()
    {
        var data = new ExperimentData("settings.itc")
        {
            CellConcentration = new FloatWithError(35e-6), SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3, MeasuredTemperature = 25, TargetTemperature = 25
        };
        for (var i = 0; i < 5; i++)
        {
            var injection = new InjectionData(data, i, 2e-6, new FloatWithError(420e-6 * 2e-6), true)
            {
                ActualCellConcentration = data.CellConcentration * 0.99,
                ActualTitrantConcentration = (i + 1) * 5e-6,
                Ratio = (i + 1) * 5e-6 / (35e-6 * 0.99)
            };
            injection.SetPeakArea(new FloatWithError(2000 * injection.InjectionMass, 1e-8));
            data.Injections.Add(injection);
        }
        return data;
    }

    sealed class FixedPrompt(string path) : IFileSavePromptService
    {
        public Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes) => Task.FromResult(path);
    }
    sealed class EnrollmentHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.Method == HttpMethod.Post
                    ? "{\"publicAccessCode\":\"ftitc_pub_avalonia_test\"}"
                    : "{\"mode\":\"presets\",\"presets\":[{\"id\":\"instant\",\"name\":\"Fast\"}]}")
            };
        }
    }
}
