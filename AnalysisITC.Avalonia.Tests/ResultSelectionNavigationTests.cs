using System;

using Avalonia.Controls;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ResultSelectionNavigationTests
{
    public ResultSelectionNavigationTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Theory]
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
