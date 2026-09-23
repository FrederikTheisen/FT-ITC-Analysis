using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public class AnalysisResultPresentationCacheTests
{
    static GlobalSolution Create() => LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.SameForAll);

    [Fact]
    public void FinishedValuesAreReusedAcrossPresentersAndConcurrentRequests()
    {
        var result = new AnalysisResult(Create());
        var first = result.PresentationData;
        Assert.Same(first, result.PresentationData);
        Assert.Equal(0, first.CachedEnvelopeCount);
        var curve = first.GetTemperatureEnvelope(ParameterType.Gibbs1, 6.85, 46.85);
        Assert.Equal(301, curve.Count);
        Assert.Equal(6.85, curve[0].X);
        Assert.Equal(46.85, curve[^1].X);
        Assert.Equal(-24000, curve[^1].Center, 8);
        Parallel.For(0, 8, _ => Assert.Same(curve,
            result.PresentationData.GetTemperatureEnvelope(ParameterType.Gibbs1, 6.85, 46.85)));
        Assert.Equal(1, first.CachedEnvelopeCount);
        var originalResolution = first.GetTemperatureEnvelope(ParameterType.Gibbs1, 6.85, 46.85, 400);
        Assert.Equal(401, originalResolution.Count);
        // Both grids share every fourth/third sample and must evaluate the same equation.
        for (var i = 0; i <= 100; i++)
            Assert.Equal(originalResolution[4 * i].Center, curve[3 * i].Center, 8);
        var calculator = new AnalysisResultAggregateSummaryCalculator(result);
        Assert.Same(calculator.Evaluate(ParameterType.Gibbs1, 25),
            new AnalysisResultAggregateSummaryCalculator(result).Evaluate(ParameterType.Gibbs1, 25));
        Assert.NotSame(calculator.Evaluate(ParameterType.Gibbs1, 25), calculator.Evaluate(ParameterType.Gibbs1, 30));
        Assert.Same(calculator.EvaluateHeatCapacity(ThermodynamicParameterSlots.ForStep(1)),
            calculator.EvaluateSummaryParameter(ParameterType.HeatCapacity1, 0));
    }

    [Fact]
    public void UncertaintyReplacementAndSameObjectUpdateInvalidatePresentation()
    {
        var solution = Create();
        var result = new AnalysisResult(solution);
        var snapshot = result.PresentationData;
        solution.RestoreGlobalBootstrapSolutions(new List<GlobalSolution> { Create() });
        Assert.NotSame(snapshot, result.PresentationData);
        snapshot = result.PresentationData;
        solution.RestoreGlobalBootstrapSolutions(new List<GlobalSolution>());
        Assert.NotSame(snapshot, result.PresentationData);
        snapshot = result.PresentationData;
        solution.Solutions[0].RestoreBootstrapSolutions(new List<SolutionInterface>());
        Assert.NotSame(snapshot, result.PresentationData);
        snapshot = result.PresentationData;
        solution.ProfileLikelihoodRun = LinkedThermodynamicUncertaintyTests.Run(
            LinkedThermodynamicUncertaintyTests.Coordinate(ParameterType.Gibbs1, -25000, 300, 800));
        Assert.NotSame(snapshot, result.PresentationData);
        snapshot = result.PresentationData;
        solution.Solutions[0].ProfileLikelihoodRun = solution.ProfileLikelihoodRun;
        Assert.NotSame(snapshot, result.PresentationData);
        snapshot = result.PresentationData;
        solution.ApplyProfileTemperatureCoordinates(LinkedThermodynamicUncertaintyTests.Run(
            LinkedThermodynamicUncertaintyTests.Coordinate(ParameterType.Enthalpy1, -40000, 500, 900)));
        Assert.NotSame(snapshot, result.PresentationData);
        snapshot = result.PresentationData;
        result.UpdateSolution(solution);
        Assert.NotSame(snapshot, result.PresentationData);
    }

    [Fact]
    public void SampledNonlinearCurveAgreesWithAnalyticRelationshipBetweenPoints()
    {
        var result = new AnalysisResult(LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.TemperatureDependent));
        foreach (var intervals in new[] { 300, 400 })
        {
            var curve = result.PresentationData.GetTemperatureEnvelope(ParameterType.Gibbs1, 6.85, 46.85, intervals);
            for (var i = 1; i < curve.Count; i++)
            {
                var t = (curve[i - 1].X + curve[i].X) / 2 + 273.15;
                var exact = t / 300 * -25000 + (1 - t / 300) * -40000
                    + 300 * ((t - 300) - t * Math.Log(t / 300));
                var drawn = (curve[i - 1].Center + curve[i].Center) / 2;
                // G'' = -Cp/T bounds linear interpolation error by Cp/Tmin * step²/8.
                var bound = 300.0 / 280 * Math.Pow(40.0 / intervals, 2) / 8;
                Assert.InRange(Math.Abs(drawn - exact), 0, bound + 1e-8);
            }
        }
    }

    [Fact]
    public void ColdTenThousandReplicateEnvelopeFitsAllocationBudgetAndWarmReadDoesNotEvaluate()
    {
        var solution = Create();
        var lower = LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.SameForAll, gibbs: -25500);
        var upper = LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.SameForAll, gibbs: -24500);
        solution.RestoreGlobalBootstrapSolutions(Enumerable.Range(0, 10000)
            .Select(i => i % 2 == 0 ? lower : upper).ToList());
        var result = new AnalysisResult(solution);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var envelope = result.PresentationData.GetTemperatureEnvelope(ParameterType.Gibbs1, 6.85, 46.85);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 10 * 1024 * 1024, $"Cold envelope allocated {allocated:N0} bytes.");
        Assert.Equal(-24000, envelope[^1].Center, 8);
        Assert.Equal(-24000 - 500 * 320.0 / 300, envelope[^1].Lower, 8);
        Assert.Equal(-24000 + 500 * 320.0 / 300, envelope[^1].Upper, 8);
        before = GC.GetAllocatedBytesForCurrentThread();
        var cached = result.PresentationData.GetTemperatureEnvelope(ParameterType.Gibbs1, 6.85, 46.85);
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Same(envelope, cached);
        Assert.True(allocated < 1024, $"Cached read allocated {allocated} bytes.");
    }
}
