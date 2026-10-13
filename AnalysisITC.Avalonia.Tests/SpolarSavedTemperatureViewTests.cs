using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

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
    public async Task NewRunsUseIsoentropicPointAndCancelledRerunsPreserveSavedOutputs()
    {
        var previousIterations = ResultAnalysisController.CalculationIterations;
        var errors = new ConcurrentQueue<string>();
        EventHandler<HandledException> onError = (_, error) => errors.Enqueue(error.Message);
        EventHandler<Tuple<int, int, float, string>> cancel = (_, _) => ResultAnalysisController.TerminateAnalysisFlag.Raise();
        AppEventHandler.ShowAppMessage += onError;
        try
        {
            ResultAnalysisController.CalculationIterations = 2;
            foreach (var savedMode in new FTSRMethod.SRTempMode?[] { null,
                FTSRMethod.SRTempMode.MeanTemperature, FTSRMethod.SRTempMode.ReferenceTemperature })
            {
                var result = CreateTemperatureResult();
                var analysis = result.SpolarRecordAnalysis!;
                var saved = new FTSRMethod.SROutput(new FloatWithError(-2), new FloatWithError(3),
                    new FloatWithError(1), new FloatWithError(25));
                var completed = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
                if (savedMode.HasValue)
                    analysis.RestoreResult(FTSRMethod.SRFoldedMode.Glob, savedMode.Value, saved, 4, completed);
                var workspace = new AnalysisResultWorkspaceControl { Result = result };
                var run = typeof(AnalysisResultWorkspaceControl).GetMethod("RunTemperatureAnalysisAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                if (savedMode.HasValue)
                {
                    ResultAnalysisController.IterationFinished += cancel;
                    try { await ((Task)run.Invoke(workspace, null)!).WaitAsync(TimeSpan.FromSeconds(20)); }
                    finally { ResultAnalysisController.IterationFinished -= cancel; }
                    Assert.Same(saved, analysis.Result);
                    Assert.Equal(savedMode, analysis.CompletedTempMode);
                    Assert.Equal(savedMode, analysis.TempMode);
                    Assert.Equal(completed, analysis.CompletedAtUtc);
                    Assert.Equal(4, analysis.CompletedIterations);
                }

                await ((Task)run.Invoke(workspace, null)!).WaitAsync(TimeSpan.FromSeconds(20));
                Assert.Empty(errors);
                Assert.NotNull(analysis.Result);
                Assert.NotSame(saved, analysis.Result);
                Assert.Equal(FTSRMethod.SRTempMode.IsoEntropicPoint, analysis.CompletedTempMode);
                Assert.Equal(FTSRMethod.SRTempMode.IsoEntropicPoint, analysis.TempMode);
                Assert.True(double.IsFinite(analysis.Result.ReferenceTemperature.Value));
                Assert.Equal(10, analysis.CompletedIterations);
            }
        }
        finally
        {
            AppEventHandler.ShowAppMessage -= onError;
            ResultAnalysisController.CalculationIterations = previousIterations;
            ResultAnalysisController.TerminateAnalysisFlag.Lower();
            AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
        }
    }

    [AvaloniaFact]
    public void SavedOutputUsesTheSavedRunTemperatureAfterPreferenceChanges()
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
                    foreach (var mode in new[] { FTSRMethod.SRTempMode.IsoEntropicPoint,
                        FTSRMethod.SRTempMode.MeanTemperature, FTSRMethod.SRTempMode.ReferenceTemperature })
                    foreach (var hasUncertainty in new[] { false, true })
                    {
                        AppSettings.ReferenceTemperature = 25;
                        AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
                        var result = CreateTemperatureResult();
                        var savedTemperature = hasUncertainty ? new FloatWithError(25, 1, 23, 29) : new FloatWithError(25);
                        result.SpolarRecordAnalysis!.RestoreResult(
                            FTSRMethod.SRFoldedMode.Glob,
                            mode,
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
                            var settings = Section(workspace, "Structuring");
                            Assert.Equal("Isoentropic point", OutputValue(settings, "Evaluated at"));
                            Assert.Single(settings.GetLogicalDescendants().OfType<ComboBox>()); // Interaction only.
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
                            Assert.Equal(mode, result.SpolarRecordAnalysis.TempMode);
                            Assert.Equal(mode, result.SpolarRecordAnalysis.CompletedTempMode);
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
        var outputSection = Section(workspace, "Output");

        Assert.Equal(expectedHydration, OutputValue(outputSection, "−TΔS_HE"));
        Assert.Equal(expectedConformation, OutputValue(outputSection, "−TΔS_conf"));
        Assert.Equal(expectedTemperature, OutputValue(outputSection, "Evaluated at"));
        Assert.Equal("1", OutputValue(outputSection, "Residues"));
    }

    static Control Section(AnalysisResultWorkspaceControl workspace, string title)
    {
        Control section = Assert.Single(workspace.GetLogicalDescendants().OfType<TextBlock>(),
            block => block.Text == title);
        while (section is not Border)
            section = Assert.IsAssignableFrom<Control>(section.Parent);
        return section;
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
