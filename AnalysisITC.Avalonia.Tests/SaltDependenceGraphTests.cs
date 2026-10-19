using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using AnalysisITC.Core.Application;
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
    [AvaloniaFact]
    public void DebyeHuckelCurvePlotsLogKdAgainstSquareRootOfIonicStrength()
    {
        var result = CreateSaltResult();
        result.ElectrostaticsAnalysis.RestoreResult(
            new IonicStrengthDependenceFit(new(1e-6), new(10), new(0)),
            null, 100, 0, DateTime.UtcNow, ErrorEstimationMethod.None, false);
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedObservationsUseCompletedPolicyWhileUncalculatedObservationsUseRunPolicy(bool savedPolicy)
    {
        var previous = AppSettings.IncludeBufferInIonicStrengthCalc;
        try
        {
            AppSettings.IncludeBufferInIonicStrengthCalc = !savedPolicy;
            var result = CreateSaltResult();
            foreach (var member in result.Solution.Solutions)
            {
                var buffer = ExperimentAttribute.FromKey(AttributeKey.Buffer);
                buffer.IntValue = (int)AnalysisITC.Core.Data.Buffer.Hepes;
                buffer.DoubleValue = 7.4; buffer.ParameterValue = new(.01);
                member.Data.Attributes.Add(buffer);
            }
            result.UpdateSolution(result.Solution);
            var graph = new ResultDependenceGraphControl { Result = result, Mode = ResultAnalysisViewMode.Salt,
                SaltMode = ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel };
            CheckPoints(!savedPolicy);
            result.ElectrostaticsAnalysis.RestoreResult(new(new(1e-6), new(10), new(0)), null,
                100, 0, DateTime.UtcNow, ErrorEstimationMethod.None, savedPolicy);
            graph.Rebuild();
            CheckPoints(savedPolicy);
            Assert.All(graph.FitPointsForTesting, point => Assert.Equal(-6 + 10 * point.X / Math.Log(10), point.Y, 10));

            void CheckPoints(bool policy)
            {
                var series = (IEnumerable)typeof(ResultDependenceGraphControl)
                    .GetField("cachedSeries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graph)!;
                var item = Assert.Single(series.Cast<object>());
                var points = ((IEnumerable)item.GetType().GetProperty("Points")!.GetValue(item)!).Cast<object>().ToArray();
                Assert.Equal(3, points.Length);
                for (var i = 0; i < points.Length; i++)
                    Assert.Equal(Math.Sqrt(new[] { .04, .16, .36 }[i] + (policy ? .0037005197012182495 : 0)),
                        (double)points[i].GetType().GetProperty("X")!.GetValue(points[i])!, 12);
            }
        }
        finally { AppSettings.IncludeBufferInIonicStrengthCalc = previous; }
    }

    internal static AnalysisResult CreateSaltResult(double[]? strengths = null)
    {
        var models = new List<Model>();
        var solutions = new List<SolutionInterface>();
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 1,
        });
        foreach (var ionicStrength in strengths ?? new[] { 0.04, 0.16, 0.36 })
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
