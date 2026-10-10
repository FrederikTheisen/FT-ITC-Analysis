using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ProcessingClearMenuTests
{
    [AvaloniaFact]
    public void MenusResetAndBatchConfirmationBehaveTogether()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var data = Experiment();
            DataManager.AddData(data);
            var window = new MainWindow();
            window.Show();
            var savedConfirmation = AppSettings.ConfirmRemoveDelete;
            AppSettings.ConfirmRemoveDelete = false;
            try
            {
                Assert.False(CommandEnabled(window, "Delete All Results"));
                Assert.True(window.CanClearProcessing(ProcessingClearScope.Selected));
                Assert.Equal(new[] { "Selected", "Active", "All" }, ResetLabels(window));
                window.ClearProcessingAsync(ProcessingClearScope.Selected).GetAwaiter().GetResult();
                Assert.Null(data.Model);
                Assert.False(data.CanBeAnalyzed);
                Assert.False(data.Processor.BaselineCompleted);
                Assert.IsType<SplineInterpolator>(data.Processor.Interpolator);
                Assert.Empty(window.OwnedWindows);
                Dispatcher.UIThread.RunJobs();
                Assert.False(data.CanBeAnalyzed); // Refresh must not process the empty default spline.
                Assert.False(window.CanClearProcessing(ProcessingClearScope.All));

                var first = Experiment();
                var second = Experiment();
                DataManager.AddData(new[] { first, second });
                DataManager.SelectIndex(DataManager.SourceItems.Count - 2);
                var pending = window.ClearProcessingAsync(ProcessingClearScope.All);
                var dialog = Assert.Single(window.OwnedWindows);
                Assert.Equal("Clear Processing", dialog.Title);
                Assert.NotNull(first.Model);
                Assert.NotNull(second.Model);
                dialog.Close(false);
                Dispatcher.UIThread.RunJobs();
                Assert.True(pending.IsCompleted);
                Assert.NotNull(first.Model);
                Assert.NotNull(second.Model);
                pending = window.ClearProcessingAsync(ProcessingClearScope.All);
                Assert.Single(window.OwnedWindows).Close(true);
                Dispatcher.UIThread.RunJobs();
                Assert.True(pending.IsCompleted);
                Assert.Null(first.Model);
                Assert.Null(second.Model);
                Assert.Empty(DataManager.DeletedDataList);

                var fitted = Experiment();
                var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = fitted.Model }));
                DataManager.AddData(new ITCDataContainer[] { fitted, result });
                DataManager.SelectIndex(DataManager.SourceItems.Count - 1);
                Dispatcher.UIThread.RunJobs();
                Assert.False(window.CanClearProcessing(ProcessingClearScope.Selected));
                Assert.True(CommandEnabled(window, "Delete All Results"));
                window.DeleteAllResultsAsync().GetAwaiter().GetResult();
                Assert.Empty(DataManager.Results);
                Assert.NotNull(fitted.Model);
                Assert.True(fitted.CanBeAnalyzed);
            }
            finally
            {
                AppSettings.ConfirmRemoveDelete = savedConfirmation;
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    static string[] ResetLabels(MainWindow window)
    {
        if (OperatingSystem.IsMacOS())
            return EditNative(window).Items.OfType<NativeMenuItem>().Single(item => item.Header?.ToString() == "Clear Processing")
                .Menu!.Items.OfType<NativeMenuItem>().Select(item => item.Header!.ToString()!).ToArray();
        return Edit(window).Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Clear Processing")
            .Items.OfType<MenuItem>().Select(item => item.Header!.ToString()!).ToArray();
    }
    static bool CommandEnabled(MainWindow window, string title) => OperatingSystem.IsMacOS()
        ? EditNative(window).Items.OfType<NativeMenuItem>().Single(item => item.Header?.ToString() == title).Command!.CanExecute(null)
        : Edit(window).Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == title).Command!.CanExecute(null);
    static NativeMenu EditNative(MainWindow window) => NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>()
        .Single(item => item.Header?.ToString() == "Edit").Menu!;
    static MenuItem Edit(MainWindow window) => window.MenuHost.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Edit");

    static ExperimentData Experiment()
    {
        var data = new ExperimentData("ui-clear.itc") { CellConcentration = new FloatWithError(30e-6),
            SyringeConcentration = new FloatWithError(400e-6), CellVolume = 1.4e-3, MeasuredTemperature = 25 };
        for (var time = 0; time <= 100; time++) data.DataPoints.Add(new DataPoint(time, 1));
        data.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
        for (var index = 0; index < 5; index++)
        {
            var injection = InjectionData.FromPEAQFile(data, index, true, index * 20, 2e-6, 20, 2, 25);
            injection.ActualCellConcentration = 30e-6;
            injection.ActualTitrantConcentration = (index + 1) * 4e-6;
            injection.Ratio = (index + 1) * 4 / 30d;
            injection.SetPeakArea(new FloatWithError(-2e-6, 1e-8));
            data.Injections.Add(injection);
        }
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        data.UpdateSolution(model);
        return data;
    }
}
