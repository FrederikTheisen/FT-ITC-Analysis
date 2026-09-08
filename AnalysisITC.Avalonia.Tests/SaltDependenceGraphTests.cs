using System;
using System.Collections.Generic;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class SaltDependenceGraphTests
{
    public SaltDependenceGraphTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void DebyeHuckelCurvePlotsLogKdAgainstSquareRootOfIonicStrength()
    {
        var result = CreateSaltResult();
        result.ElectrostaticsAnalysis.RestoreResult(
            new IonicStrengthDependenceFit(new(1e-6), new(10), new(0)),
            null, 100, 0, DateTime.UtcNow, ErrorEstimationMethod.None);
        var graph = new ResultDependenceGraphControl
        {
            Result = result,
            Mode = ResultAnalysisViewMode.Salt,
            SaltMode = ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel,
        };

        Assert.True(graph.FitPointsForTesting.Count >= 2);
        Assert.All(graph.FitPointsForTesting, point =>
        {
            // Kd = 1 µM * exp(10 sqrt(I)); tolerate the evaluator's existing rounded ln(10).
            var expected = Math.Log10(1e-6 * Math.Exp(10 * point.X));
            Assert.True(double.IsFinite(point.Y));
            Assert.InRange(Math.Abs(point.Y - expected), 0, 0.002);
        });
    }

    static AnalysisResult CreateSaltResult()
    {
        var models = new List<Model>();
        var solutions = new List<SolutionInterface>();
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 1,
        });
        foreach (var ionicStrength in new[] { 0.04, 0.16 })
        {
            var data = new ExperimentData("salt.itc")
            {
                CellConcentration = new(30e-6),
                SyringeConcentration = new(400e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
            };
            var salt = ExperimentAttribute.FromKey(AttributeKey.Salt);
            salt.IntValue = (int)Salt.NaCl;
            salt.ParameterValue = new FloatWithError(ionicStrength);
            data.Attributes.Add(salt);
            var model = new OneSetOfSites(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            model.Solution = SolutionInterface.FromModel(model, convergence);
            models.Add(model);
            solutions.Add(model.Solution);
        }
        var global = new GlobalModel(models)
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
        };
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        var solver = new GlobalSolver { Model = global, ErrorEstimationMethod = ErrorEstimationMethod.None };
        global.Solution = new GlobalSolution(solver, solutions, convergence);
        return new AnalysisResult(global.Solution);
    }
}
