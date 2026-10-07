using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Avalonia.Units;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class SpolarSavedTemperatureViewTests
{
    public SpolarSavedTemperatureViewTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void ReferenceTemperatureOutputUsesTheSavedRunTemperatureAfterPreferenceChanges()
    {
        var previousReferenceTemperature = AppSettings.ReferenceTemperature;
        var previousEnergyUnitFamily = AppSettings.EnergyUnitFamily;
        try
        {
            AppSettings.ReferenceTemperature = 25;
            AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;

            Dispatcher.UIThread.Invoke(() =>
            {
                AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
                var result = CreateTemperatureResult();
                result.SpolarRecordAnalysis!.RestoreResult(
                    FTSRMethod.SRFoldedMode.Glob,
                    FTSRMethod.SRTempMode.ReferenceTemperature,
                    new FTSRMethod.SROutput(
                        new FloatWithError(-2), new FloatWithError(3),
                        new FloatWithError(1), new FloatWithError(25)),
                    4,
                    DateTime.UtcNow);
                var expectedHydration = new Energy(new FloatWithError(596.3))
                    .ToFormattedString(EnergyDisplay.ResultMolarUnit(result), permole: true);
                var expectedConformation = new Energy(new FloatWithError(-894.45))
                    .ToFormattedString(EnergyDisplay.ResultMolarUnit(result), permole: true);

                var workspace = new AnalysisResultWorkspaceControl { Result = result };
                var window = new Window { Content = workspace };
                window.Show();
                try
                {
                    workspace.SetResultViewMode(ResultAnalysisViewMode.Temperature);
                    AssertStructuringOutput(workspace, expectedHydration, expectedConformation);

                    AppSettings.ReferenceTemperature = 37;
                    workspace.Refresh();

                    AssertStructuringOutput(workspace, expectedHydration, expectedConformation);
                }
                finally
                {
                    window.Close();
                    AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
                }
            });
        }
        finally
        {
            AppSettings.ReferenceTemperature = previousReferenceTemperature;
            AppSettings.EnergyUnitFamily = previousEnergyUnitFamily;
        }
    }

    static void AssertStructuringOutput(
        AnalysisResultWorkspaceControl workspace,
        string expectedHydration,
        string expectedConformation)
    {
        var outputHeader = Assert.Single(workspace.GetLogicalDescendants().OfType<TextBlock>(),
            block => block.Text == "Output");
        Control outputSection = outputHeader;
        while (outputSection is not Border)
            outputSection = Assert.IsAssignableFrom<Control>(outputSection.Parent);

        Assert.Equal(expectedHydration, OutputValue(outputSection, "−TΔS_HE"));
        Assert.Equal(expectedConformation, OutputValue(outputSection, "−TΔS_conf"));
        Assert.Equal("25 °C", OutputValue(outputSection, "Evaluated at"));
        Assert.Equal("1", OutputValue(outputSection, "Residues"));
    }

    static string OutputValue(Control outputSection, string labelText)
    {
        var label = Assert.Single(outputSection.GetLogicalDescendants().OfType<TextBlock>(),
            block => block.Text == labelText);
        var row = Assert.IsType<Grid>(label.Parent);
        return Assert.Single(row.Children.OfType<TextBlock>(), block => !ReferenceEquals(block, label)).Text!;
    }

    static AnalysisResult CreateTemperatureResult()
    {
        var models = new List<Model>();
        var members = new List<SolutionInterface>();
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 0.1,
        });
        foreach (var temperature in new[] { 10.0, 25.0, 45.0 })
        {
            var data = new ExperimentData($"spolar-view-{temperature}.itc")
            {
                Name = $"T {temperature}",
                CellConcentration = new FloatWithError(30e-6),
                SyringeConcentration = new FloatWithError(400e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = temperature,
                TargetTemperature = temperature,
            };
            var model = new OneSetOfSites(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25_000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            var member = SolutionInterface.FromModel(model, convergence);
            model.Solution = member;
            models.Add(model);
            members.Add(member);
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
            UseErrorWeightedFitting = false,
        };
        var globalSolution = new GlobalSolution(solver, members, convergence);
        global.Solution = globalSolution;
        return new AnalysisResult(globalSolution);
    }
}
