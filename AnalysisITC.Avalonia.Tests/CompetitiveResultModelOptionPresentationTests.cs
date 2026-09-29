using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class CompetitiveResultModelOptionPresentationTests
{
    public CompetitiveResultModelOptionPresentationTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void ModelPageShowsSavedCompetitorValuesForEachExperiment()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var originalUnit = AppSettings.DefaultConcentrationUnit;
            var originalEnergyFamily = AppSettings.EnergyUnitFamily;
            try
            {
                AppSettings.DefaultConcentrationUnit = ConcentrationUnit.µM;
                AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
                var models = new List<Model>
                {
                    CreateModel("competitor-a", 2e-6, -32000),
                    CreateModel("competitor-b", 5e-6, -24000),
                };
                var solutions = models.Select(model =>
                {
                    model.Solution = SolutionInterface.FromModel(model, Convergence());
                    return model.Solution;
                }).ToList();
                var global = new GlobalModel(models)
                {
                    ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
                };
                var globalSolution = new GlobalSolution(
                    new GlobalSolver { Model = global }, solutions, Convergence());
                global.Solution = globalSolution;

                var workspace = new AnalysisResultWorkspaceControl
                {
                    Result = new AnalysisResult(globalSolution),
                };
                var sections = workspace.ModelPanelForTesting.Children
                    .Select(child => TextFrom(child))
                    .ToArray();

                Assert.Equal(2, sections[0].Count(value => value == "From experiment attribute"));
                Assert.Contains("2 µM", sections[1]);
                Assert.Contains("-32 kJ/mol", sections[1]);
                Assert.Contains("5 µM", sections[2]);
                Assert.Contains("-24 kJ/mol", sections[2]);
            }
            finally
            {
                AppSettings.DefaultConcentrationUnit = originalUnit;
                AppSettings.EnergyUnitFamily = originalEnergyFamily;
            }
        });
    }

    static Model CreateModel(string name, double kd, double enthalpy)
    {
        var data = new ExperimentData(name + ".itc")
        {
            Name = name,
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
        };
        data.Injections.Add(new InjectionData(data, volume: 1e-6)
        {
            IsIntegrated = true,
            Ratio = 1,
        });
        var model = new CompetitiveBinding(data);
        model.InitializeParameters(data);
        model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
        model.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue = true;
        model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue = new FloatWithError(-System.Math.Log10(kd));
        model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].BoolValue = true;
        model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue = new FloatWithError(enthalpy);
        return model;
    }

    static SolverConvergence Convergence() => SolverConvergence.FromSnapshot(
        new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 0.1,
        });

    static string[] TextFrom(Control root) => root
        .GetLogicalDescendants()
        .OfType<TextBlock>()
        .Select(text => text.Text ?? string.Empty)
        .ToArray();
}
