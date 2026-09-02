using System;

using Avalonia.Threading;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisResultTableColumnWidthTests
{
    public AnalysisResultTableColumnWidthTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void AutomaticWidthIsContentAwareAndClamped()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            AppSettings.RememberResultTableColumnWidthsForSession = false;
            var workspace = new AnalysisResultWorkspaceControl
            {
                Result = CreateResult(new string('W', 200))
            };

            var experiment = workspace.ResultTableGridForTesting!.ColumnDefinitions[0].Width.Value;
            Assert.Equal(AnalysisResultOverviewColumnWidthPolicy.MaximumAutomaticWidth, experiment);
            Assert.All(
                workspace.ResultTableGridForTesting.ColumnDefinitions,
                definition => Assert.True(definition.Width.Value >= AnalysisResultOverviewColumnWidthPolicy.MinimumWidth));
        });
    }

    [Fact]
    public void ManualWidthUsesMinimumAndSurvivesRefresh()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            AppSettings.RememberResultTableColumnWidthsForSession = false;
            var workspace = new AnalysisResultWorkspaceControl { Result = CreateResult("first.itc") };

            workspace.SetResultTableColumnWidthForTesting("Experiment", 20);
            Assert.Equal(90, workspace.ResultTableColumnWidthsForTesting["Experiment"]);

            workspace.SetResultTableColumnWidthForTesting("Experiment", 480);
            workspace.Refresh();

            Assert.Equal(480, workspace.ResultTableColumnWidthsForTesting["Experiment"]);
            Assert.Equal(480, workspace.ResultTableGridForTesting!.ColumnDefinitions[0].Width.Value);
        });
    }

    [Fact]
    public void ResultChangeClearsWidthsUnlessSessionMemoryIsEnabled()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            AnalysisResultWorkspaceControl.ResetSessionResultTableColumnWidthsForTesting();
            AppSettings.RememberResultTableColumnWidthsForSession = false;
            var workspace = new AnalysisResultWorkspaceControl { Result = CreateResult("first.itc") };
            workspace.SetResultTableColumnWidthForTesting("Experiment", 210);
            workspace.Result = CreateResult("second.itc");
            Assert.Empty(workspace.ResultTableColumnWidthsForTesting);

            AppSettings.RememberResultTableColumnWidthsForSession = true;
            workspace.SetResultTableColumnWidthForTesting("Experiment", 230);
            workspace.Result = CreateResult("third.itc");
            Assert.Equal(230, workspace.ResultTableColumnWidthsForTesting["Experiment"]);

            AppSettings.RememberResultTableColumnWidthsForSession = false;
            AnalysisResultWorkspaceControl.ResetSessionResultTableColumnWidthsForTesting();
        });
    }

    static AnalysisResult CreateResult(string experimentName)
    {
        var experiment = new ExperimentData(experimentName)
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            MeasuredTemperature = 25,
        };
        experiment.Injections.Add(new InjectionData(experiment, volume: 1e-6)
        {
            IsIntegrated = true,
            Ratio = 1,
        });

        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(
            model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        return new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
    }
}
