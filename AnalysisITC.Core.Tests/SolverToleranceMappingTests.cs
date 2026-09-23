using System;
using System.Reflection;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Solver events")]
public sealed class SolverToleranceMappingTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void NelderMeadFunctionToleranceMapsPreferencesForBothSolverTypes(double preference)
    {
        var oldPreference = AppSettings.OptimizerTolerance;
        try
        {
            AppSettings.OptimizerTolerance = preference;
            var method = typeof(SolverInterface).GetMethod("NMFunctionTolerance",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var tolerance = Math.Pow(10, -(9 + 5 * preference));
            foreach (var solver in new SolverInterface[] { new Solver(), new GlobalSolver() })
            {
                foreach (var modifier in new[] { 0.0, 0.5, 1.0, 2.0 })
                {
                    solver.SolverToleranceModifier = modifier;
                    var expected = Math.Max(1e-30, 2.5 * tolerance * modifier);
                    var actual = (double)method.Invoke(solver, new object[] { 2.5 });
                    Assert.InRange(Math.Abs(actual / expected - 1), 0, 2e-14);
                }
                foreach (var initialLoss in new[] { 0.0, 1e-40 })
                {
                    solver.SolverToleranceModifier = 1.0;
                    Assert.Equal(1e-30, (double)method.Invoke(solver, new object[] { initialLoss }));
                }
            }
        }
        finally
        {
            AppSettings.OptimizerTolerance = oldPreference;
        }
    }

}
