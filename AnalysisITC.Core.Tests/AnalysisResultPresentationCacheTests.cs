using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
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
    public void LocalTrendCachesSeparateSummaryAndEnvelopeForEachMode()
    {
        var result = new AnalysisResult(LinkedThermodynamicUncertaintyTests.Create(
            VariableConstraint.None, affinity: VariableConstraint.None));
        var parameter = ParameterType.Enthalpy1;
        var pointTemperature = result.Solution.MeanTemperature;
        var meanCalculator = new AnalysisResultAggregateSummaryCalculator(result);
        var spreadCalculator = new AnalysisResultAggregateSummaryCalculator(result, SummaryUncertaintyMode.ReplicateSpread);

        var mean = meanCalculator.Evaluate(parameter, pointTemperature);
        var spread = spreadCalculator.Evaluate(parameter, pointTemperature);
        Assert.Same(mean, meanCalculator.Evaluate(parameter, pointTemperature));
        Assert.Same(spread, spreadCalculator.Evaluate(parameter, pointTemperature));
        Assert.Equal(mean.Value.Value, spread.Value.Value, 12);
        // Observations -40000,-30000,-14000 at equally spaced temperatures
        // have residuals +1000,-2000,+1000 (one degree of freedom).
        Assert.Equal(Math.Sqrt(2_000_000), mean.Value.SD, 10);
        Assert.Equal(Math.Sqrt(6_000_000), spread.Value.SD, 10);

        var snapshot = result.PresentationData;
        var meanEnvelope = snapshot.GetTemperatureEnvelope(parameter, pointTemperature, pointTemperature + 20, out var meanKind);
        var spreadEnvelope = snapshot.GetTemperatureEnvelope(parameter, pointTemperature, pointTemperature + 20,
            out var spreadKind, mode: SummaryUncertaintyMode.ReplicateSpread);
        Assert.Equal(TemperatureDependenceIntervalKind.FittedTrend, meanKind);
        Assert.Equal(TemperatureDependenceIntervalKind.ReplicateRange, spreadKind);
        Assert.Same(meanEnvelope, snapshot.GetTemperatureEnvelope(parameter, pointTemperature, pointTemperature + 20));
        Assert.Same(spreadEnvelope, snapshot.GetTemperatureEnvelope(parameter, pointTemperature, pointTemperature + 20,
            mode: SummaryUncertaintyMode.ReplicateSpread));
        Assert.Equal(2, snapshot.CachedEnvelopeCount);
        Assert.Equal(mean.Value.Lower, meanEnvelope[0].Lower, 10);
        Assert.Equal(spread.Value.Lower, spreadEnvelope[0].Lower, 10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedLinearBandsMatchEvaluatedIntervalsEvenWithRetainedRefits(bool retainRefits)
    {
        var solution = LinkedThermodynamicUncertaintyTests.Create(
            VariableConstraint.TemperatureDependent, affinity: VariableConstraint.SameForAll);
        var parameter = ParameterType.Enthalpy1;
        if (retainRefits)
        {
            var first = LinkedThermodynamicUncertaintyTests.Create(
                VariableConstraint.TemperatureDependent, enthalpy: -41000, affinity: VariableConstraint.SameForAll);
            var second = LinkedThermodynamicUncertaintyTests.Create(
                VariableConstraint.TemperatureDependent, enthalpy: -39000, affinity: VariableConstraint.SameForAll);
            solution.RestoreGlobalBootstrapSolutions(new List<GlobalSolution> { first, second });
        }
        solution.TemperatureDependence[parameter] = new LinearFitWithError(
            new FloatWithError(3, 1, 2, 5), new FloatWithError(-40000, 3, -40002, -39995), 25);

        var result = new AnalysisResult(solution);
        foreach (var mode in Enum.GetValues<SummaryUncertaintyMode>())
        {
            var curve = result.PresentationData.GetTemperatureEnvelope(parameter, 15, 35, out var kind, intervals: 2, mode: mode);
            Assert.Equal(TemperatureDependenceIntervalKind.Model, kind);
            Assert.True(TemperatureDependenceUncertaintyCaption.HasBand(curve));
            Assert.Equal(-40030, curve[0].Center);
            Assert.Equal(-40030 - Math.Sqrt(404), curve[0].Lower, 8);
            Assert.Equal(-40030 + Math.Sqrt(125), curve[0].Upper, 8);
            Assert.Equal(-40002, curve[1].Lower);
            Assert.Equal(-39995, curve[1].Upper);
            Assert.Equal(-39970 - Math.Sqrt(104), curve[2].Lower, 8);
            Assert.Equal(-39970 + Math.Sqrt(425), curve[2].Upper, 8);
            var calculator = new AnalysisResultAggregateSummaryCalculator(result, mode);
            foreach (var point in curve)
            {
                var evaluated = calculator.Evaluate(parameter, point.X).Value;
                Assert.Equal(evaluated.Value, point.Center);
                Assert.Equal(evaluated.Lower, point.Lower);
                Assert.Equal(evaluated.Upper, point.Upper);
            }
        }
    }

    [Fact]
    public void EnvelopeRetainsCentralLineAndRawEndpointsWhenOnlyOneLimitIsAvailable()
    {
        var solution = LinkedThermodynamicUncertaintyTests.Create(
            VariableConstraint.TemperatureDependent, affinity: VariableConstraint.SameForAll);
        var parameter = ParameterType.Enthalpy1;
        solution.TemperatureDependence[parameter] = new LinearFitWithError(
            new FloatWithError(3, 1, double.NaN, 5), new FloatWithError(-40000, 3, -40002, -39995), 25);

        var result = new AnalysisResult(solution);
        var curve = result.PresentationData.GetTemperatureEnvelope(parameter, 15, 35, out var kind, intervals: 2);
        Assert.Equal(TemperatureDependenceIntervalKind.Model, kind);
        Assert.Equal(new[] { -40030.0, -40000, -39970 }, curve.Select(point => point.Center));
        Assert.Equal(-40030 - Math.Sqrt(404), curve[0].Lower, 8);
        Assert.True(double.IsNaN(curve[0].Upper));
        Assert.Equal(-40002, curve[1].Lower);
        Assert.Equal(-39995, curve[1].Upper);
        Assert.True(double.IsNaN(curve[2].Lower));
        Assert.Equal(-39970 + Math.Sqrt(425), curve[2].Upper, 8);
        Assert.False(TemperatureDependenceUncertaintyCaption.HasBand(curve));
    }

    [Fact]
    public void CaptionNamesDifferentActuallyPlottedBandTypesAndOmitsMissingBars()
    {
        var caption = TemperatureDependenceUncertaintyCaption.Build(false, new[]
        {
            ("∆H", TemperatureDependenceIntervalKind.ReplicateRange),
            ("∆G", TemperatureDependenceIntervalKind.Model),
            ("−T∆S", TemperatureDependenceIntervalKind.Model),
        });
        Assert.Equal("∆H bands show approximate 95% replicate ranges. ∆G, −T∆S bands show 95% model-fit intervals.", caption);
        Assert.Equal("Bars show the 95% limits for each fit.", TemperatureDependenceUncertaintyCaption.Build(
            true, Array.Empty<(string, TemperatureDependenceIntervalKind)>()));
        Assert.Empty(TemperatureDependenceUncertaintyCaption.Build(
            false, Array.Empty<(string, TemperatureDependenceIntervalKind)>()));
    }

    [Theory]
    [InlineData(VariableConstraint.None)]
    [InlineData(VariableConstraint.SameForAll)]
    public void IndividualTemperatureBarsOmitFixedParametersAndKeepFittedAndDerivedIntervals(VariableConstraint constraint)
    {
        var result = new AnalysisResult(LinkedThermodynamicUncertaintyTests.Create(
            constraint, affinity: VariableConstraint.None));
        var solution = result.Solution.Solutions[0];
        var enthalpy = constraint == VariableConstraint.None
            ? solution.Model.Parameters.Table[ParameterType.Enthalpy1]
            : result.Model.Parameters.GlobalTable[ParameterType.Enthalpy1];
        var value = new FloatWithError(-40000, 100, -40200, -39700);

        Assert.True(TemperatureDependenceUncertaintyCaption.TryGetIndividualInterval(
            result, solution, ParameterType.Enthalpy1, value, out var lower, out var upper));
        Assert.Equal(-40200, lower);
        Assert.Equal(-39700, upper);

        enthalpy.Update(enthalpy.Value, lockpar: true);
        Assert.False(TemperatureDependenceUncertaintyCaption.TryGetIndividualInterval(
            result, solution, ParameterType.Enthalpy1, value, out _, out _));
        Assert.Equal("Derived", ParameterFitPresentation.GetStatus(result, solution, ParameterType.EntropyContribution1));
        Assert.True(TemperatureDependenceUncertaintyCaption.TryGetIndividualInterval(
            result, solution, ParameterType.EntropyContribution1, value, out _, out _));
    }

    [Fact]
    public void BandRunsBreakAtUnavailableBoundsAndUnavailableCenters()
    {
        var points = new[]
        {
            new FitEnvelopePoint(0, 3, 2, 4),
            new FitEnvelopePoint(1, 3, 2, 4),
            new FitEnvelopePoint(2, 3, 2, double.NaN),
            new FitEnvelopePoint(3, 3, 2, 4),
            new FitEnvelopePoint(4, double.NaN, 2, 4),
            new FitEnvelopePoint(5, 3, 2, 4),
            new FitEnvelopePoint(6, 3, 2, 4),
        };
        Assert.Equal(new[] { (0, 2), (5, 2) }, TemperatureDependenceUncertaintyCaption.UsableBandRuns(points));
    }

    [Fact]
    public void CompetitorCaptureKeepsEstablishedReplicateSdAndMeanInterval()
    {
        var result = new AnalysisResult(LinkedThermodynamicUncertaintyTests.Create(
            VariableConstraint.None, affinity: VariableConstraint.None));
        var parameter = ParameterType.Enthalpy1;
        var pointTemperature = result.Solution.MeanTemperature;
        var mean = new AnalysisResultAggregateSummaryCalculator(result).Evaluate(parameter, pointTemperature).Value;
        var captured = AnalysisResultAggregateSummaryCalculator.ForCompetitorCapture(result).Evaluate(parameter, pointTemperature).Value;
        Assert.Equal(mean.Value, captured.Value, 12);
        Assert.Equal(Math.Sqrt(6_000_000), captured.SD, 10);
        Assert.Equal(mean.Lower, captured.Lower, 10);
        Assert.Equal(mean.Upper, captured.Upper, 10);
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
