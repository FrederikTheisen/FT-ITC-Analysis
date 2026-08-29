using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class CompetitiveBindingModelTests
    {
        [Fact]
        public void TotalCompetitorUsesCanonicalDisplayLabelForLegacyOptionNames()
        {
            var key = AttributeKey.PreboundLigandConc;
            var tooltip = key.GetProperties().ToolTip;
            var option = ExperimentAttribute.FromKey(key);

            Assert.Equal("Total competitor", key.GetProperties().Name);
            Assert.True(key.GetProperties().Name.Length <= 20);
            Assert.Contains(
                "Total analytical competitor concentration in the cell after pre-equilibration",
                tooltip);
            Assert.Contains("free + bound", tooltip);
            Assert.Contains("not only the initially bound complex", tooltip);
            Assert.Equal("Total competitor", option.GetDisplayName());

            option.OptionName = "[Ligand]";

            Assert.Equal("[Ligand]", option.OptionName);
            Assert.Equal("Total competitor", option.GetDisplayName());
        }

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
        public void ApparentKdUsesFreeCompetitorAfterFiniteDepletion()
        {
            var model = CreateModel(competitorConcentration: 10e-6);
            model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue =
                new FloatWithError(6.0);

            Assert.True(CompetitiveBinding.TryCalculateInitialFreeCompetitorConcentration(
                totalSites: 10e-6,
                totalCompetitor: 10e-6,
                competitorAffinity: 1e6,
                out var freeCompetitor));
            AssertRelative(2.7015621187164244e-6, freeCompetitor, 2e-14);

            var solution = CreateSolution(model);
            var expectedFactor = 1.0 + 1e6 * freeCompetitor;
            var expectedKapp = solution.K.Value / expectedFactor;
            var expectedKdapp = solution.Kd.Value * expectedFactor;

            AssertRelative(expectedKapp, solution.Kapp.Value, 2e-14);
            AssertRelative(expectedKdapp, solution.Kdapp.Value, 2e-14);
            AssertRelative(solution.Kapp.Value * solution.Kdapp.Value, 1.0, 2e-14);
            AssertRelative(expectedKdapp, solution.ReportParameters[ParameterType.ApparentAffinity].Value, 2e-14);

            var uiValue = solution.UISolutionParameters(FinalFigureDisplayParameters.Affinity)
                .Single(item => item.Item1 == MarkdownStrings.ApparentDissociationConstant)
                .Item2;
            Assert.Equal(solution.Kdapp.AsFormattedConcentration(true), uiValue);
        }

        [Fact]
        public void ApparentKdZeroCompetitorDoesNotReadCompetitorProperties()
        {
            var model = CreateModel(competitorConcentration: 0);
            model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue =
                new FloatWithError(double.NaN);
            model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue =
                new FloatWithError(double.NaN);

            var solution = CreateSolution(model);

            AssertRelative(solution.Kd.Value, solution.Kdapp.Value, 2e-14);
            AssertRelative(solution.K.Value, solution.Kapp.Value, 2e-14);
        }

        [Fact]
        public void ApparentKdUsesFixedStoichiometryWithSyringeCorrection()
        {
            var model = CreateModel(competitorConcentration: 10e-6);
            model.Parameters.Table[ParameterType.Nvalue1].Update(0.5);
            model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue =
                new FloatWithError(6.0);
            model.ModelOptions[AttributeKey.UseSyringeActiveFraction].BoolValue = true;
            model.ModelOptions[AttributeKey.NumberOfSites1].DoubleValue = 2.0;

            var solution = CreateSolution(model);
            Assert.True(CompetitiveBinding.TryCalculateInitialFreeCompetitorConcentration(
                totalSites: 2.0 * 10e-6,
                totalCompetitor: 10e-6,
                competitorAffinity: 1e6,
                out var freeCompetitor));

            var expectedFactor = 1.0 + 1e6 * freeCompetitor;
            AssertRelative(solution.K.Value / expectedFactor, solution.Kapp.Value, 2e-14);
            AssertRelative(solution.Kd.Value * expectedFactor, solution.Kdapp.Value, 2e-14);
        }

        [Fact]
        public void ApparentKdApproachesTotalCompetitorApproximationInExcess()
        {
            Assert.True(CompetitiveBinding.TryCalculateInitialFreeCompetitorConcentration(
                totalSites: 10e-6,
                totalCompetitor: 10e-3,
                competitorAffinity: 1e6,
                out var freeCompetitor));

            Assert.InRange(freeCompetitor, 0.999 * 10e-3, 10e-3);
            Assert.InRange(10e-3 - freeCompetitor, 0, 10e-6 * (1 + 1e-12));
        }

        [Fact]
        public async Task Me6PxdaCompetitiveMemberUsesFreeCompetitorInApparentKd()
        {
            using var stream = File.OpenRead(Fixture("competitive.ftxtc"));
            var containers = await FTXTCReader.ReadStream(stream);
            var competitiveExperiments = containers
                .OfType<ExperimentData>()
                .Where(experiment => experiment.Solution?.ModelType == AnalysisModel.CompetitiveBinding
                    && experiment.Name.Contains("Me6PXDA", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(2, competitiveExperiments.Count);

            foreach (var experiment in competitiveExperiments)
            {
                var competitive = Assert.IsType<CompetitiveBinding.ModelSolution>(experiment.Solution);
                var totalCompetitor = competitive.ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue.Value;
                var competitorAffinity = competitive.LigandK.Value;
                var totalSites = competitive.N.Value * competitive.Data.CellConcentration.Value;

                // Independent positive root of the target-free competitor mass balance:
                // K*b^2 + (K*(S-B)+1)*b - B = 0.
                var linear = 1.0 + competitorAffinity * (totalSites - totalCompetitor);
                var discriminant = linear * linear + 4.0 * competitorAffinity * totalCompetitor;
                var freeCompetitor = (-linear + Math.Sqrt(discriminant)) / (2.0 * competitorAffinity);
                var expectedFactor = 1.0 + competitorAffinity * freeCompetitor;
                var expectedKdapp = competitive.Kd.Value * expectedFactor;
                var totalApproximation = competitive.Kd.Value * (1.0 + competitorAffinity * totalCompetitor);

                Assert.True(freeCompetitor > 0 && freeCompetitor < totalCompetitor);
                AssertRelative(expectedKdapp, competitive.Kdapp.Value, 2e-12);
                Assert.True(competitive.Kdapp.Value < totalApproximation);
                AssertRelative(expectedKdapp, competitive.ReportParameters[ParameterType.ApparentAffinity].Value, 2e-12);

                if (experiment.Name.Contains("trial 3", StringComparison.Ordinal))
                {
                    Assert.InRange(freeCompetitor, 715.0e-6, 716.5e-6);
                    Assert.InRange(expectedKdapp, 1.648e-6, 1.650e-6);
                }
            }
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

        static CompetitiveBinding.ModelSolution CreateSolution(CompetitiveBinding model) =>
            Assert.IsType<CompetitiveBinding.ModelSolution>(SolutionInterface.FromModel(model, null));

        static string Fixture(string name) =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

        static void AssertRelative(double expected, double actual, double tolerance)
        {
            var scale = Math.Max(1e-300, Math.Abs(expected));
            Assert.InRange(Math.Abs(actual - expected) / scale, 0, tolerance);
        }
    }
}
