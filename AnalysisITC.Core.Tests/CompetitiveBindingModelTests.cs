using System;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class CompetitiveBindingModelTests
    {
        [Fact]
        public void NoCompetitorUsesStableOneLigandLimit()
        {
            var state = CompetitiveBinding.CalculateState(
                ratioA: 10,
                ratioB: 0,
                cA: 1e7,
                cB: double.NaN);

            Assert.True(state.Success);
            Assert.False(state.UsedFallback);
            AssertRelative(1.1111110973936902e-8, state.FreeSiteFraction, 2e-15);
            AssertRelative(0.9999999888888890, state.BoundAFraction, 2e-15);
            Assert.Equal(0, state.BoundBFraction);
            Assert.InRange(Math.Abs(state.SiteBalanceResidual), 0, 1e-15);
        }

        [Fact]
        public void IllConditionedTwoLigandCaseUsesSafeguardedFallback()
        {
            var state = CompetitiveBinding.CalculateState(
                ratioA: 0.8,
                ratioB: 0.5,
                cA: 1e10,
                cB: 1e10);

            Assert.True(state.Success);
            Assert.True(state.UsedFallback);
            Assert.InRange(state.Iterations, 1, 8);
            AssertRelative(3.3333333285185185e-10, state.FreeSiteFraction, 2e-12);
            AssertRelative(0.6153846151794872, state.BoundAFraction, 2e-12);
            AssertRelative(0.3846153844871795, state.BoundBFraction, 2e-12);
            Assert.InRange(Math.Abs(state.SiteBalanceResidual), 0, 1e-13);
        }

        [Fact]
        public void HighAffinityVerifierCaseConservesAllSites()
        {
            var state = CompetitiveBinding.CalculateState(
                ratioA: 100,
                ratioB: 100,
                cA: 1e18,
                cB: 1e18);

            Assert.True(state.Success);
            Assert.True(state.UsedFallback);
            AssertRelative(5.0251256281407035e-21, state.FreeSiteFraction, 2e-12);
            AssertRelative(0.5, state.BoundAFraction, 2e-14);
            AssertRelative(0.5, state.BoundBFraction, 2e-14);
            Assert.InRange(Math.Abs(state.SiteBalanceResidual), 0, 1e-13);
        }

        [Fact]
        public void DeterministicLogGridAlwaysReturnsPhysicalConservedState()
        {
            var ratios = new[] { 1e-3, 0.1, 0.8, 1.0, 2.0, 10.0, 100.0 };
            var bindingScales = new[] { 1e-11, 1e-5, 1.0, 1e5, 1e10, 1e17 };

            foreach (var ratioA in ratios)
            foreach (var ratioB in ratios)
            foreach (var cA in bindingScales)
            foreach (var cB in bindingScales)
            {
                var state = CompetitiveBinding.CalculateState(ratioA, ratioB, cA, cB);

                Assert.True(state.Success,
                    $"State failed for rA={ratioA:G17}, rB={ratioB:G17}, cA={cA:G17}, cB={cB:G17}");
                Assert.InRange(state.FreeSiteFraction, 0, 1);
                Assert.InRange(state.BoundAFraction, 0, Math.Min(1, ratioA) + 1e-12);
                Assert.InRange(state.BoundBFraction, 0, Math.Min(1, ratioB) + 1e-12);
                Assert.InRange(Math.Abs(state.SiteBalanceResidual), 0, 1e-12);
                Assert.InRange(state.Iterations, 0, CompetitiveBinding.MaximumFallbackIterations);
            }
        }

        [Fact]
        public void ExactLimitsAndNearZeroCompetitorAreContinuous()
        {
            var empty = CompetitiveBinding.CalculateState(0, 0, 1e7, double.NaN);
            var targetOnly = CompetitiveBinding.CalculateState(0.7, 0, 1e7, double.NaN);
            var competitorOnly = CompetitiveBinding.CalculateState(0, 0.6, double.NaN, 2e6);
            var nearZeroCompetitor = CompetitiveBinding.CalculateState(0.7, 1e-14, 1e7, 2e6);
            var nearZeroTarget = CompetitiveBinding.CalculateState(1e-14, 0.6, 1e7, 2e6);

            Assert.True(empty.Success);
            Assert.Equal(1, empty.FreeSiteFraction);
            Assert.Equal(0, empty.BoundAFraction);
            Assert.Equal(0, empty.BoundBFraction);
            Assert.True(targetOnly.Success);
            Assert.True(competitorOnly.Success);
            Assert.True(nearZeroCompetitor.Success);
            Assert.True(nearZeroTarget.Success);
            Assert.InRange(
                Math.Abs(nearZeroCompetitor.BoundAFraction - targetOnly.BoundAFraction),
                0,
                2e-13);
            Assert.InRange(nearZeroCompetitor.BoundBFraction, 0, 1.1e-14);
            Assert.InRange(
                Math.Abs(nearZeroTarget.BoundBFraction - competitorOnly.BoundBFraction),
                0,
                2e-13);
            Assert.InRange(nearZeroTarget.BoundAFraction, 0, 1.1e-14);
        }

        [Fact]
        public void WellConditionedCubicFastPathRemainsActive()
        {
            var state = CompetitiveBinding.CalculateState(
                ratioA: 0.2,
                ratioB: 0.3,
                cA: 10,
                cB: 5);

            Assert.True(state.Success);
            Assert.False(state.UsedFallback);
            // Independent 80-digit decimal solution of the two ligand balance.
            AssertRelative(0.6031493637707981, state.FreeSiteFraction, 2e-15);
            AssertRelative(0.17155654114120866, state.BoundAFraction, 2e-15);
            AssertRelative(0.2252940950879932, state.BoundBFraction, 2e-15);
            Assert.InRange(Math.Abs(state.SiteBalanceResidual), 0, 1e-12);
        }

        [Fact]
        public void PublicEvaluationWithNoCompetitorIgnoresCompetitorProperties()
        {
            var model = CreateModel(competitorConcentration: 0);
            model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue =
                new FloatWithError(double.NaN);
            model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue =
                new FloatWithError(double.NaN);

            var state = CompetitiveBinding.CalculateState(10, 0, 1e7, double.NaN);
            var totalSites = model.Data.CellConcentration.Value;
            var heatContent = model.Data.CellVolume * totalSites * -10000 * state.BoundAFraction;
            var injection = model.Data.Injections[0];
            var expected = heatContent
                           + (injection.Volume / model.Data.CellVolume) * heatContent / 2.0;

            AssertRelative(expected, model.Evaluate(0, withoffset: false), 2e-14);

            model.ModelOptions[AttributeKey.UseSyringeActiveFraction].BoolValue = true;
            model.ModelOptions[AttributeKey.NumberOfSites1].DoubleValue = 1;
            AssertRelative(expected, model.Evaluate(0, withoffset: false), 2e-14);
        }

        [Fact]
        public void NegativeFixedCompetitorConcentrationRemainsAConfigurationError()
        {
            var model = CreateModel(competitorConcentration: -1e-6);
            var parameters = model.Parameters.GetFittedParameterArray();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                model.TryLossFunction(parameters, errorweighted: false, out _);
            });
        }

        [Fact]
        public void StateResolverHasNoSteadyStateAllocation()
        {
            for (var i = 0; i < 1000; i++)
                CompetitiveBinding.CalculateState(0.8, 0.5, 1e10, 1e10);

            var before = GC.GetAllocatedBytesForCurrentThread();
            var checksum = 0.0;
            for (var i = 0; i < 100000; i++)
                checksum += CompetitiveBinding.CalculateState(0.8, 0.5, 1e10, 1e10)
                    .BoundAFraction;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(checksum > 0);
            Assert.Equal(0, allocated);
        }

        static CompetitiveBinding CreateModel(double competitorConcentration)
        {
            var data = new ExperimentData("competitive-f003.itc")
            {
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(1e-3),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };
            var injection = new InjectionData(
                data,
                id: 0,
                volume: 2e-6,
                mass: data.SyringeConcentration * 2e-6,
                include: true)
            {
                ActualCellConcentration = 10e-6,
                ActualTitrantConcentration = 100e-6,
                Ratio = 10,
            };
            injection.SetPeakArea(new FloatWithError(-1e-6, 1e-9));
            data.Injections.Add(injection);

            var model = new CompetitiveBinding(data);
            model.InitializeParameters(data);
            model.Parameters.Table[ParameterType.Nvalue1].Update(1);
            model.Parameters.Table[ParameterType.Enthalpy1].Update(-10000);
            model.Parameters.Table[ParameterType.Affinity1].Update(12);
            model.Parameters.Table[ParameterType.Offset].Update(0);
            model.ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue =
                new FloatWithError(competitorConcentration);
            data.Model = model;
            return model;
        }

        static void AssertRelative(double expected, double actual, double tolerance)
        {
            var scale = Math.Max(1e-300, Math.Abs(expected));
            Assert.InRange(Math.Abs(actual - expected) / scale, 0, tolerance);
        }
    }
}
