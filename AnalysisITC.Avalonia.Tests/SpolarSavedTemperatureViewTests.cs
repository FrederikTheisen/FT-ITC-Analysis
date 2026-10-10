using System;
using System.Collections.Generic;
using System.Globalization;
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
    [AvaloniaFact]
    public void ReferenceTemperatureOutputUsesTheSavedRunTemperatureAfterPreferenceChanges()
    {
        var previousReferenceTemperature = AppSettings.ReferenceTemperature;
        var previousEnergyUnitFamily = AppSettings.EnergyUnitFamily;
        var previousPrecision = AppSettings.NumberPrecision;
        var previousStyle = AppSettings.UncertaintyDisplayStyle;
        try
        {
            AppSettings.ReferenceTemperature = 25;
            AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
            AppSettings.NumberPrecision = NumberPrecision.AllDecimals;
            AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;

            Dispatcher.UIThread.Invoke(() =>
            {
                var previousCulture = CultureInfo.CurrentCulture;
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                try
                {
                    foreach (var hasUncertainty in new[] { false, true })
                    {
                        AppSettings.ReferenceTemperature = 25;
                        AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
                        var result = CreateTemperatureResult();
                        var savedTemperature = hasUncertainty ? new FloatWithError(25, 1, 23, 29) : new FloatWithError(25);
                        result.SpolarRecordAnalysis!.RestoreResult(
                            FTSRMethod.SRFoldedMode.Glob,
                            FTSRMethod.SRTempMode.ReferenceTemperature,
                            new FTSRMethod.SROutput(
                                new FloatWithError(-2), new FloatWithError(3),
                                new FloatWithError(1), savedTemperature),
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
                            var expectedCelsius = hasUncertainty ? "25 ± 1 [23, 29] °C" : "25 °C";
                            var expectedKelvin = hasUncertainty ? "298.15 ± 1 [296.15, 302.15] K" : "298.15 K";
                            AssertStructuringOutput(workspace, expectedHydration, expectedConformation, expectedCelsius);

                            workspace.SetTemperatureDisplay(true);
                            AssertStructuringOutput(workspace, expectedHydration, expectedConformation, expectedKelvin);

                            AppSettings.ReferenceTemperature = 37;
                            workspace.Refresh();

                            AssertStructuringOutput(workspace, expectedHydration, expectedConformation, expectedKelvin);
                            workspace.SetTemperatureDisplay(false);
                            AssertStructuringOutput(workspace, expectedHydration, expectedConformation, expectedCelsius);
                            AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviation;
                            workspace.Refresh();
                            AssertStructuringOutput(workspace, expectedHydration, expectedConformation,
                                hasUncertainty ? "25 ± 1 °C" : "25 °C");
                            AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;
                            Assert.Equal(25, result.SpolarRecordAnalysis.Result.ReferenceTemperature.Value);
                            Assert.Equal(hasUncertainty ? 1 : 0, result.SpolarRecordAnalysis.Result.ReferenceTemperature.SD);
                        }
                        finally
                        {
                            window.Close();
                            AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
                        }
                    }
                }
                finally { CultureInfo.CurrentCulture = previousCulture; }
            });
        }
        finally
        {
            AppSettings.ReferenceTemperature = previousReferenceTemperature;
            AppSettings.EnergyUnitFamily = previousEnergyUnitFamily;
            AppSettings.NumberPrecision = previousPrecision;
            AppSettings.UncertaintyDisplayStyle = previousStyle;
        }
    }

    static void AssertStructuringOutput(
        AnalysisResultWorkspaceControl workspace,
        string expectedHydration,
        string expectedConformation,
        string expectedTemperature)
    {
        var outputHeader = Assert.Single(workspace.GetLogicalDescendants().OfType<TextBlock>(),
            block => block.Text == "Output");
        Control outputSection = outputHeader;
        while (outputSection is not Border)
            outputSection = Assert.IsAssignableFrom<Control>(outputSection.Parent);

        Assert.Equal(expectedHydration, OutputValue(outputSection, "−TΔS_HE"));
        Assert.Equal(expectedConformation, OutputValue(outputSection, "−TΔS_conf"));
        Assert.Equal(expectedTemperature, OutputValue(outputSection, "Evaluated at"));
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
