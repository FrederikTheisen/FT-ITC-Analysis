using System;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

using Xunit;
using DataListEntry = AnalysisITC.Avalonia.MainWindow.DataListEntry;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ResultSelectionNavigationTests
{
    [AvaloniaFact]
    public void BrowserValidityUpdatesAfterSourceRemovalAndUndo()
    {
        var experiment = CreateExperiment("availability.itc");
        var result = CreateResult(experiment);
        RunWithMainWindow(new ITCDataContainer[] { experiment, result }, (window, items) =>
        {
            items.SelectedIndex = 1;
            Assert.Equal("Valid", items.Items.OfType<DataListEntry>().Single(entry => entry.IsResult).ValidityLabel);

            DataManager.RemoveSourceItemAt(0);
            Dispatcher.UIThread.RunJobs();
            var missing = items.Items.OfType<DataListEntry>().Single();
            Assert.Same(result, missing.Item);
            Assert.Equal("Invalid", missing.ValidityLabel);
            Assert.True(missing.IsValidityInvalid);
            Assert.Contains("Experiment missing: " + experiment.Name, missing.ValidityTooltip);
            AssertMainWindowSelection(window, items, result);

            DataManager.UndoDeleteData();
            Dispatcher.UIThread.RunJobs();
            var restored = items.Items.OfType<DataListEntry>().Single(entry => entry.IsResult);
            Assert.Equal("Valid", restored.ValidityLabel);
            Assert.DoesNotContain("Experiment missing", restored.ValidityTooltip);
            AssertMainWindowSelection(window, items, experiment);
        });
    }

    [AvaloniaTheory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MainWindowRefreshFollowsCurrentCoreSelection(int selectedIndex)
    {
        var first = CreateExperiment("selection-first.itc");
        var second = CreateExperiment("selection-second.itc");
        var result = CreateResult(first);
        var sourceItems = new ITCDataContainer[] { first, result, second };

        RunWithMainWindow(sourceItems, (window, items) =>
        {
            DataManager.SelectIndex(selectedIndex);
            var expected = selectedIndex < 0 ? null : sourceItems[selectedIndex];
            var selectionNotifications = 0;
            EventHandler<ExperimentData> onExperimentSelected = (_, _) => selectionNotifications++;
            EventHandler<AnalysisResult> onResultSelected = (_, _) => selectionNotifications++;
            DataManager.SelectionDidChange += onExperimentSelected;
            DataManager.AnalysisResultSelected += onResultSelected;
            try
            {
                for (var refresh = 0; refresh < 3; refresh++)
                {
                    DataManager.InvokeDataDidChange();
                    Dispatcher.UIThread.RunJobs();
                    AssertMainWindowSelection(window, items, expected);
                }
                Assert.Equal(0, selectionNotifications);
            }
            finally
            {
                DataManager.SelectionDidChange -= onExperimentSelected;
                DataManager.AnalysisResultSelected -= onResultSelected;
            }
        });
    }

    [AvaloniaFact]
    public void MainWindowQueuedResultRefreshKeepsLaterUserSelection()
    {
        var first = CreateExperiment("selection-first.itc");
        var second = CreateExperiment("selection-second.itc");
        var result = CreateResult(first);
        var previousAutoOpen = AppSettings.AutoOpenNewAnalysisResult;
        try
        {
            AppSettings.AutoOpenNewAnalysisResult = true;
            RunWithMainWindow(new ITCDataContainer[] { first, second }, (window, items) =>
            {
                DataManager.AddData(result);
                Assert.Same(result, DataManager.SelectedResult);

                // The user selects another existing row before the queued rebuild.
                items.SelectedIndex = 1;
                DataManager.InvokeDataDidChange();
                Dispatcher.UIThread.RunJobs();
                AssertMainWindowSelection(window, items, second);
            });
        }
        finally
        {
            AppSettings.AutoOpenNewAnalysisResult = previousAutoOpen;
        }
    }

    [AvaloniaTheory]
    [InlineData("reorder")]
    [InlineData("remove earlier")]
    [InlineData("remove selected")]
    public void MainWindowRefreshFollowsCoreAfterMixedListChanges(string change)
    {
        var first = CreateExperiment("selection-first.itc");
        var second = CreateExperiment("selection-second.itc");
        var result = CreateResult(first);
        RunWithMainWindow(new ITCDataContainer[] { first, result, second }, (window, items) =>
        {
            items.SelectedIndex = 1;
            ITCDataContainer expected = result;
            switch (change)
            {
                case "reorder":
                    DataManager.MoveSourceItem(2, 0);
                    break;
                case "remove earlier":
                    DataManager.RemoveSourceItemAt(0);
                    break;
                case "remove selected":
                    DataManager.RemoveSourceItemAt(1);
                    expected = second;
                    break;
            }

            Dispatcher.UIThread.RunJobs();
            AssertMainWindowSelection(window, items, expected);
            DataManager.InvokeDataDidChange();
            Dispatcher.UIThread.RunJobs();
            AssertMainWindowSelection(window, items, expected);

            DataManager.Clear(DataClearMode.ResetSession);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.DataListEntries);
            AssertMainWindowSelection(window, items, null);
        });
    }

    static void AssertMainWindowSelection(MainWindow window, ListBox items, ITCDataContainer? expected)
    {
        Assert.Equal(expected == null ? -1 : DataManager.IndexOfSourceItem(expected), items.SelectedIndex);
        Assert.Equal(DataManager.SelectedContentIndex, items.SelectedIndex);
        if (expected == null)
            Assert.Null(items.SelectedItem);
        else
            Assert.Same(expected, Assert.IsType<DataListEntry>(items.SelectedItem).Item);

        var resultWorkspace = window.FindControl<AnalysisResultWorkspaceControl>("ResultWorkspace")!;
        Assert.Same(expected as AnalysisResult, resultWorkspace.Result);
        Assert.Equal(expected is AnalysisResult, resultWorkspace.IsVisible);
        Assert.Equal(expected is not AnalysisResult, window.FindControl<TabControl>("WorkspaceTabs")!.IsVisible);
    }

    static void RunWithMainWindow(ITCDataContainer[] sourceItems, Action<MainWindow, ListBox> assertion)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
        DataManager.AddData(sourceItems);
        var window = new MainWindow();
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var items = window.FindControl<ListBox>("ItemsList")!;
            items.SelectedIndex = -1;
            items.SelectedIndex = 0;
            assertion(window, items);
        }
        finally
        {
            DocumentDirtyTracker.MarkClean();
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
            AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTheory]
    [InlineData(ResultAnalysisViewMode.Fit)]
    [InlineData(ResultAnalysisViewMode.Correlation)]
    public void ResultNavigationKeepsActiveModeAndDestinationMember(
        ResultAnalysisViewMode activeMode)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            AnalysisResultWorkspaceControl.ResetSessionViewForTesting();

            var experiment = CreateExperiment("avalonia-selection.itc");
            var firstResult = CreateResult(experiment);
            var secondResult = CreateResult(experiment);
            DataManager.AddData(new ITCDataContainer[] { firstResult, secondResult });

            var workspace = new AnalysisResultWorkspaceControl();
            var window = new Window { Content = workspace };
            window.Show();
            try
            {
                DataManager.SelectIndex(0);
                workspace.Result = firstResult;
                var firstMember = firstResult.Solution.Solutions[0];
                DataManager.SelectResultSolution(firstMember);
                workspace.SetResultViewMode(activeMode);
                Assert.Equal(activeMode, workspace.ActiveViewMode);

                DataManager.SelectIndex(1);
                workspace.Result = secondResult;
                var secondMember = secondResult.Solution.Solutions[0];

                Assert.Same(secondMember, DataManager.SelectedResultSolution);
                Assert.Same(secondMember, workspace.SelectedResultTableSolutionForTesting);
                Assert.Equal(activeMode, workspace.ActiveViewMode);

                if (activeMode == ResultAnalysisViewMode.Fit)
                {
                    Assert.Same(experiment, workspace.SelectedFitGraphForTesting.Experiment);
                    Assert.Same(secondMember, workspace.SelectedFitGraphForTesting.SolutionOverride);
                }
                else
                {
                    Assert.Same(workspace.CorrelationGraphForTesting, workspace.GraphHostContentForTesting);
                    Assert.Equal(experiment.Name, workspace.CorrelationGraphForTesting.SelectedLabel);
                    Assert.Equal(1, workspace.CorrelationGraphForTesting.SelectedCount);
                }

                DataManager.SelectIndex(0);
                workspace.Result = firstResult;
                Assert.Same(firstMember, DataManager.SelectedResultSolution);
                Assert.Same(firstMember, workspace.SelectedResultTableSolutionForTesting);
                Assert.Equal(activeMode, workspace.ActiveViewMode);

                if (activeMode == ResultAnalysisViewMode.Fit)
                {
                    Assert.Same(firstMember, workspace.SelectedFitGraphForTesting.SolutionOverride);
                }
                else
                {
                    Assert.Same(workspace.CorrelationGraphForTesting, workspace.GraphHostContentForTesting);
                    Assert.Equal(experiment.Name, workspace.CorrelationGraphForTesting.SelectedLabel);
                    Assert.Equal(1, workspace.CorrelationGraphForTesting.SelectedCount);
                }
            }
            finally
            {
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
                AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
            }
        });
    }

    static AnalysisResult CreateResult(ExperimentData experiment)
    {
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(
            model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Algorithm = SolverAlgorithm.LevenbergMarquardt,
                Termination = SolverTermination.Converged,
            }));
        return new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(
            new Solver { Model = model }));
    }

    static ExperimentData CreateExperiment(string fileName)
    {
        var experiment = new ExperimentData(fileName)
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        experiment.SetID(Guid.NewGuid().ToString());
        var injection = new InjectionData(experiment, 0, 2e-6, 2e-10, true)
        {
            ActualCellConcentration = experiment.CellConcentration,
            ActualTitrantConcentration = 2e-6,
            Ratio = 1,
        };
        injection.SetPeakArea(new FloatWithError(-1e-6, 1e-8));
        experiment.Injections.Add(injection);
        return experiment;
    }
}
