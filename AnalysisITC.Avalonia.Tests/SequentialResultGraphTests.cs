using System.Collections.Generic;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Units;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class SequentialResultGraphTests
{
    public SequentialResultGraphTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void FourStepResultGraphsExposeEveryStepAndAccessibleDescriptions()
    {
        var result = CreateFourStepResult();
        var parameterGraph = new ResultParameterGraphControl { Result = result };

        Assert.Contains(ParameterType.Enthalpy3, parameterGraph.AvailableParametersForTesting);
        Assert.Contains(ParameterType.Gibbs3, parameterGraph.AvailableParametersForTesting);
        Assert.Contains(ParameterType.EntropyContribution4, parameterGraph.AvailableParametersForTesting);
        Assert.Contains(ParameterType.Gibbs4, parameterGraph.AvailableParametersForTesting);
        Assert.Equal("∆H1", parameterGraph.ParameterLabelForTesting(ParameterType.Enthalpy1));
        Assert.Equal("∆G4", parameterGraph.ParameterLabelForTesting(ParameterType.Gibbs4));
        Assert.Equal("Thermodynamic result parameter graph",
            AutomationProperties.GetName(parameterGraph));
        Assert.Contains("every active binding step",
            AutomationProperties.GetHelpText(parameterGraph));

        var dependenceGraph = new ResultDependenceGraphControl
        {
            Result = result,
            Mode = ResultAnalysisViewMode.Temperature,
        };
        Assert.True(dependenceGraph.HasPrintableData);
        Assert.Contains("Enthalpy 3", dependenceGraph.SeriesLabelsForTesting);
        Assert.Contains("Gibbs free energy 4", dependenceGraph.SeriesLabelsForTesting);
        Assert.Contains("Entropy contribution 4", dependenceGraph.SeriesLabelsForTesting);
        Assert.Equal("Thermodynamic dependence graph",
            AutomationProperties.GetName(dependenceGraph));
        Assert.Contains("every applicable binding step",
            AutomationProperties.GetHelpText(dependenceGraph));
    }

    [Fact]
    public void NavigationAndRedrawReuseCurvesAndDoNotPrepareHiddenViews()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
            var result = CreateFourStepResult();
            var other = CreateFourStepResult();
            var data = result.PresentationData;
            var workspace = new AnalysisResultWorkspaceControl { Result = result };
            var window = new Window { Content = workspace };
            var originalUnits = AppSettings.EnergyUnitFamily;
            window.Show();
            try
            {
                Assert.Equal(0, data.CachedEnvelopeCount);
                Assert.Equal(0, data.CachedCorrelationCount);
                workspace.SetResultViewMode(ResultAnalysisViewMode.Temperature);
                Assert.Equal(12, data.CachedEnvelopeCount);
                Assert.Equal(0, data.CachedCorrelationCount);
                var graph = Assert.IsType<ResultDependenceGraphControl>(workspace.GraphHostContentForTesting);
                var before = graph.FitPointsForTesting;
                using (var drawing = new DrawingGroup().Open()) graph.Render(drawing);
                graph.Arrange(new global::Avalonia.Rect(0, 0, 900, 600));
                graph.FitToData();
                DataManager.SelectResultSolution(result.Solution.Solutions[0]);
                Assert.Equal(before, graph.FitPointsForTesting);
                Assert.Equal(12, data.CachedEnvelopeCount);
                workspace.SetResultViewMode(ResultAnalysisViewMode.Summary);
                workspace.Result = other;
                Assert.Equal(0, other.PresentationData.CachedEnvelopeCount);
                workspace.Result = result;
                workspace.SetTemperatureDisplay(true);
                AppSettings.EnergyUnitFamily = EnergyUnitFamily.Calories;
                workspace.RefreshPresentationData();
                Assert.Same(data, result.PresentationData);
                Assert.Equal(12, data.CachedEnvelopeCount);
                Assert.Equal(0, data.CachedCorrelationCount);
                workspace.SetResultViewMode(ResultAnalysisViewMode.Correlation);
                var correlationCount = data.CachedCorrelationCount;
                workspace.Refresh();
                Assert.Equal(correlationCount, data.CachedCorrelationCount);
                result.Solution.SetBootstrapSolutions(new List<GlobalSolution>());
                workspace.Refresh();
                Assert.NotSame(data, result.PresentationData);
                workspace.Result = null;
                Assert.Null(workspace.Result);
            }
            finally
            {
                window.Close();
                AppSettings.EnergyUnitFamily = originalUnits;
                DataManager.ClearResultSolutionSelection();
                AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
            }
        });
    }

    static AnalysisResult CreateFourStepResult()
    {
        var models = new List<Model>
        {
            CreateModel(20),
            CreateModel(35),
        };
        var solutions = new List<SolutionInterface>();
        foreach (var model in models)
        {
            var solution = SolutionInterface.FromModel(model, Convergence());
            model.Solution = solution;
            solutions.Add(solution);
        }

        var global = new GlobalModel(models)
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
        };
        foreach (var model in models)
            global.Parameters.AddIndivdualParameter(model.Parameters);
        var solver = new GlobalSolver
        {
            Model = global,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
        };
        var globalSolution = new GlobalSolution(solver, solutions, Convergence());
        global.Solution = globalSolution;
        return new AnalysisResult(globalSolution);
    }

    static Model CreateModel(double temperature)
    {
        var data = new ExperimentData($"sequential-graph-{temperature}.itc")
        {
            Name = $"T {temperature}",
            CellConcentration = new FloatWithError(30e-6),
            SyringeConcentration = new FloatWithError(400e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = temperature,
            TargetTemperature = temperature,
        };
        for (var index = 0; index < 5; index++)
        {
            var injection = new InjectionData(
                data,
                index,
                2e-6,
                data.SyringeConcentration * 2e-6,
                include: true)
            {
                ActualCellConcentration = data.CellConcentration * 0.99,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = (index + 1) * 5e-6 / (data.CellConcentration * 0.99),
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7));
            data.Injections.Add(injection);
        }

        var model = new SequentialBindingSites(data);
        model.InitializeParameters(data);
        model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = 4;
        model.ApplyModelOptions();
        model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
        model.Parameters.Table[ParameterType.Offset].Update(0, true);
        return model;
    }

    static SolverConvergence Convergence() => SolverConvergence.FromSnapshot(
        new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 0.1,
        });
}
