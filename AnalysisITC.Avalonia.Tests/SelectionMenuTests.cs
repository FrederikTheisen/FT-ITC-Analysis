using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Threading;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class SelectionMenuTests
{
    public SelectionMenuTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Fact]
    public void ExperimentSelectionAndContextMenusUseCanonicalActions()
    {
        RunWithWindow(
            CreateExperiment("menu-experiment.itc"),
            window =>
            {
                var context = window.CreateDataListItemMenu(window.DataListEntries[0]);
                var selection = window.MenuController.CreateSelectionFlyout(includeSelectionOnlyTools: true);

                Assert.Equal(new[]
                {
                    "Details...",
                    "Active",
                    "Attribute Operations...",
                    "Clear Attributes",
                    "Save Selected...",
                    "Export Selected Data...",
                    "Duplicate Data",
                    "Clear Solution",
                    "Remove Data",
                }, Labels(context));

                Assert.Equal(new[]
                {
                    "Details...",
                    "Active",
                    "Attribute Operations...",
                    "Clear Attributes",
                    "Save Selected...",
                    "Export Selected Data...",
                    "Duplicate Data",
                    "Clear Solution",
                    "Experiment Merger...",
                    "Buffer Subtraction...",
                    "Remove Data",
                }, Labels(selection));

                AssertWellFormed(context);
                AssertWellFormed(selection);
            });
    }

    [Fact]
    public void ResultSelectionAndContextMenusUseCanonicalActions()
    {
        var experiment = CreateExperiment("menu-result.itc", integrated: true);
        var result = CreateResult(experiment);

        RunWithWindow(
            result,
            window =>
            {
                var context = window.CreateDataListItemMenu(window.DataListEntries[0]);
                var selection = window.MenuController.CreateSelectionFlyout(includeSelectionOnlyTools: true);
                var expected = new[]
                {
                    "Details...",
                    "Update Result",
                    "Save Selected...",
                    "Copy Result Table",
                    "Load Solutions to Experiments",
                    "Set Active Experiments",
                    "Export Associated Final Figures...",
                    "Export Analysis Report...",
                    "Remove Result",
                };

                Assert.Equal(expected, Labels(context));
                Assert.Equal(expected, Labels(selection));
                AssertWellFormed(context);
                AssertWellFormed(selection);

                Assert.All(
                    new[]
                    {
                        "Update Result",
                        "Copy Result Table",
                        "Load Solutions to Experiments",
                        "Set Active Experiments",
                        "Export Associated Final Figures...",
                        "Export Analysis Report...",
                    },
                    title => Assert.True(Item(context, title).Command?.CanExecute(null)));
            });
    }

    [Fact]
    public void ExperimentCommandStateTracksProcessingInclusionAndSolution()
    {
        var experiment = CreateExperiment("menu-state.itc");
        var injection = experiment.Injections[0];
        experiment.Include = true;

        RunWithWindow(
            experiment,
            window =>
            {
                var menu = window.CreateDataListItemMenu(window.DataListEntries[0]);
                var active = Item(menu, "Active");
                var clearSolution = Item(menu, "Clear Solution");

                Assert.Equal(MenuItemToggleType.CheckBox, active.ToggleType);
                Assert.False(active.IsChecked);
                Assert.False(active.Command?.CanExecute(null));
                Assert.False(clearSolution.Command?.CanExecute(null));

                injection.IsIntegrated = true;
                experiment.Model = CreateSolvedModel(experiment);

                menu = window.MenuController.CreateSelectionContextFlyout();
                active = Item(menu, "Active");
                Assert.True(active.IsChecked);
                Assert.True(active.Command?.CanExecute(null));
                Assert.True(Item(menu, "Clear Solution").Command?.CanExecute(null));
            });
    }

    [Fact]
    public void CreatingContextMenuSelectsItsTargetBeforeResolvingCommands()
    {
        var first = CreateExperiment("first.itc", integrated: true);
        var second = CreateExperiment("second.itc", integrated: true);
        first.Include = false;
        second.Include = true;

        RunWithWindow(
            new ITCDataContainer[] { first, second },
            window =>
            {
                Assert.Same(second, DataManager.Current);

                var menu = window.CreateDataListItemMenu(window.DataListEntries[0]);

                Assert.Same(first, DataManager.Current);
                Assert.False(Item(menu, "Active").IsChecked);
                AssertWellFormed(menu);
            });
    }

    static void RunWithWindow(ITCDataContainer item, Action<MainWindow> assertion) =>
        RunWithWindow(new[] { item }, assertion);

    static void RunWithWindow(IEnumerable<ITCDataContainer> items, Action<MainWindow> assertion)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            DataManager.AddData(items);
            var window = new MainWindow();
            window.Show();

            try
            {
                assertion(window);
            }
            finally
            {
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    static AnalysisResult CreateResult(ExperimentData experiment)
    {
        var model = CreateSolvedModel(experiment);
        var solver = new Solver { Model = model };
        return new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(solver));
    }

    static OneSetOfSites CreateSolvedModel(ExperimentData experiment)
    {
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        model.Solution = SolutionInterface.FromModel(
            model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        return model;
    }

    static ExperimentData CreateExperiment(string fileName, bool integrated = false)
    {
        var experiment = new ExperimentData(fileName)
        {
            CellConcentration = new FloatWithError(0.00001),
            SyringeConcentration = new FloatWithError(0.0001),
        };
        experiment.Injections.Add(new InjectionData(experiment, volume: 0.000001)
        {
            IsIntegrated = integrated,
        });
        return experiment;
    }

    static string[] Labels(MenuFlyout menu) => menu.Items
        .OfType<MenuItem>()
        .Select(item => item.Header?.ToString() ?? "")
        .ToArray();

    static MenuItem Item(MenuFlyout menu, string title) => Assert.Single(
        menu.Items.OfType<MenuItem>(),
        item => string.Equals(item.Header?.ToString(), title, StringComparison.Ordinal));

    static void AssertWellFormed(MenuFlyout menu)
    {
        Assert.NotEmpty(menu.Items);
        Assert.IsNotType<Separator>(menu.Items[0]);
        Assert.IsNotType<Separator>(menu.Items[^1]);

        for (var index = 1; index < menu.Items.Count; index++)
            Assert.False(menu.Items[index - 1] is Separator && menu.Items[index] is Separator);
    }
}
