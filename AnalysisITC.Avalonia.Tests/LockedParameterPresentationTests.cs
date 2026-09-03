using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class LockedParameterPresentationTests
{
    public LockedParameterPresentationTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void ModelInspectorShowsOnlyLockedExposedParametersOnce()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var workspace = new AnalysisResultWorkspaceControl
            {
                Result = CreateGlobalResult(includeLockedParameters: true),
            };

            var text = TextFrom(LockedSection(workspace));
            Assert.Single(text, value => value == "Affinity");
            Assert.Single(text, value => value == "Enthalpy");
            Assert.DoesNotContain("Offset", text);
        });
    }

    [Fact]
    public void ModelInspectorReportsWhenNoParametersAreLocked()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var workspace = new AnalysisResultWorkspaceControl
            {
                Result = CreateGlobalResult(includeLockedParameters: false),
            };

            var text = TextFrom(LockedSection(workspace));
            Assert.Equal(new[] { "Locked Parameters", "None" }, text);
        });
    }

    [Fact]
    public void SingleExperimentResultUsesItsMemberParameterTable()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var model = CreateModel("locked-single", 25);
            model.Parameters.Table[ParameterType.Offset].Update(2500, lockpar: true);
            model.Solution = SolutionInterface.FromModel(model, Convergence());
            var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(
                new Solver { Model = model }));
            var workspace = new AnalysisResultWorkspaceControl { Result = result };

            var text = TextFrom(LockedSection(workspace));
            Assert.Contains("Offset", text);
            Assert.Equal(3, text.Length);
            Assert.False(string.IsNullOrWhiteSpace(text[2]));
        });
    }

    [Fact]
    public void ReadOnlyValuesUseAffinityAndEnergyDisplayUnits()
    {
        var originalConcentration = AppSettings.DefaultConcentrationUnit;
        var originalEnergyFamily = AppSettings.EnergyUnitFamily;
        try
        {
            AppSettings.DefaultConcentrationUnit = ConcentrationUnit.µM;
            AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;

            var affinity = AnalysisParameterRowBuilder.ReadOnlyPresentation(
                new Parameter(ParameterType.Affinity1, 6, islocked: true));
            var enthalpy = AnalysisParameterRowBuilder.ReadOnlyPresentation(
                new Parameter(ParameterType.Enthalpy1, -5000, islocked: true));

            Assert.Equal("Affinity", affinity.Name);
            Assert.Equal($"1 {ConcentrationUnit.µM.GetName()}", affinity.Value);
            Assert.Equal("Enthalpy", enthalpy.Name);
            Assert.Equal("-5 kJ/mol", enthalpy.Value);
        }
        finally
        {
            AppSettings.DefaultConcentrationUnit = originalConcentration;
            AppSettings.EnergyUnitFamily = originalEnergyFamily;
        }
    }

    static AnalysisResult CreateGlobalResult(bool includeLockedParameters)
    {
        var models = new List<Model>
        {
            CreateModel("locked-a", 20),
            CreateModel("locked-b", 30),
        };
        var solutions = models.Select(model =>
        {
            var solution = SolutionInterface.FromModel(model, Convergence());
            model.Solution = solution;
            return solution;
        }).ToList();

        var global = new GlobalModel(models)
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
        };
        global.Parameters.SetConstraintForParameter(
            ParameterType.Affinity1,
            VariableConstraint.SameForAll);
        global.Parameters.SetConstraintForParameter(
            ParameterType.Enthalpy1,
            VariableConstraint.SameForAll);
        global.Parameters.AddorUpdateGlobalParameter(
            ParameterType.Affinity1,
            6,
            includeLockedParameters);
        global.Parameters.AddorUpdateGlobalParameter(
            ParameterType.Enthalpy1,
            -5000,
            includeLockedParameters);
        global.Parameters.AddorUpdateGlobalParameter(
            ParameterType.Offset,
            0,
            islocked: false);

        foreach (var model in models)
        {
            model.Parameters.Table[ParameterType.Affinity1].Update(6, includeLockedParameters);
            model.Parameters.Table[ParameterType.Affinity1].SetGlobal(6);
            model.Parameters.Table[ParameterType.Enthalpy1].Update(-5000, includeLockedParameters);
            model.Parameters.Table[ParameterType.Enthalpy1].SetGlobal(-5000);
            global.Parameters.AddIndivdualParameter(model.Parameters);
        }

        var solver = new GlobalSolver { Model = global };
        var globalSolution = new GlobalSolution(solver, solutions, Convergence());
        global.Solution = globalSolution;
        return new AnalysisResult(globalSolution);
    }

    static Model CreateModel(string id, double temperature)
    {
        var data = new ExperimentData(id + ".itc")
        {
            Name = id,
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = temperature,
        };
        data.SetID(id);
        data.Injections.Add(new InjectionData(data, volume: 1e-6)
        {
            IsIntegrated = true,
            Ratio = 1,
        });

        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
        return model;
    }

    static SolverConvergence Convergence() => SolverConvergence.FromSnapshot(
        new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 0.1,
        });

    static Control LockedSection(AnalysisResultWorkspaceControl workspace)
    {
        return workspace.ModelPanelForTesting.Children
            .Single(child => TextFrom(child).FirstOrDefault() == "Locked Parameters");
    }

    static string[] TextFrom(Control root) => root
        .GetLogicalDescendants()
        .OfType<TextBlock>()
        .Select(text => text.Text ?? string.Empty)
        .ToArray();
}
