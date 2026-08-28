using System;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class TwoSetsOfSitesRootTests
    {
        [Fact]
        public void AuditCaseAboveOneMillimolarFindsPhysicalRoot()
        {
            var state = TwoSetsOfSites.CalculateState(
                totalMacromolecule: 10e-6,
                totalTitrant: 1.1e-3,
                kd1: 1e-6,
                kd2: 1e-5,
                n1: 1,
                n2: 1);

            Assert.True(state.Success);
            Assert.InRange(state.FreeTitrant, 0, 1.1e-3);
            Assert.InRange(Math.Abs(state.FreeTitrant - 1.080100984e-3), 0, 5e-13);
            Assert.InRange(Math.Abs(state.MassBalanceResidual), 0, 1e-12);

            var cubic = TwoSetsOfSites.CalculateStateWithExpandedCubic(
                10e-6, 1.1e-3, 1e-6, 1e-5, 1, 1);
            Assert.True(cubic.Success);
            Assert.InRange(Math.Abs(cubic.FreeTitrant - state.FreeTitrant), 0, 5e-16);
            Assert.InRange(Math.Abs(cubic.MassBalanceResidual), 0, 1e-12);
        }

        [Fact]
        public void DirectAndRetainedCubicSolversAgreeWithIndependentOracleAcrossGrid()
        {
            var random = new Random(2002);
            for (var sample = 0; sample < 200; sample++)
            {
                var totalMacromolecule = Math.Pow(10, -9 + 6 * random.NextDouble());
                var totalTitrant = Math.Pow(10, -10 + 8 * random.NextDouble());
                var kd1 = Math.Pow(10, -12 + 14 * random.NextDouble());
                var kd2 = Math.Pow(10, -12 + 14 * random.NextDouble());
                var n1 = 0.1 + 3.9 * random.NextDouble();
                var n2 = 0.1 + 3.9 * random.NextDouble();

                var direct = TwoSetsOfSites.CalculateState(
                    totalMacromolecule, totalTitrant, kd1, kd2, n1, n2);
                var cubic = TwoSetsOfSites.CalculateStateWithExpandedCubic(
                    totalMacromolecule, totalTitrant, kd1, kd2, n1, n2);
                var oracle = IndependentBisection(
                    totalMacromolecule, totalTitrant, kd1, kd2, n1, n2);
                var tolerance = Math.Max(
                    TwoSetsOfSites.AbsoluteMassBalanceTolerance,
                    TwoSetsOfSites.RelativeMassBalanceTolerance
                    * Math.Max(totalTitrant, totalMacromolecule * (n1 + n2)));

                Assert.True(direct.Success, $"Direct solver failed at sample {sample}.");
                Assert.True(cubic.Success, $"Cubic solver failed at sample {sample}.");
                Assert.InRange(direct.FreeTitrant, 0, totalTitrant);
                Assert.InRange(cubic.FreeTitrant, 0, totalTitrant);
                Assert.InRange(Math.Abs(direct.FreeTitrant - oracle), 0, 4 * tolerance);
                Assert.InRange(Math.Abs(cubic.FreeTitrant - oracle), 0, 4 * tolerance);
                Assert.InRange(Math.Abs(direct.MassBalanceResidual), 0, tolerance);
                Assert.InRange(Math.Abs(cubic.MassBalanceResidual), 0, tolerance);
            }
        }

        [Fact]
        public void ZeroTitrantAndInvalidInputsAreReportedWithoutGuessing()
        {
            var zero = TwoSetsOfSites.CalculateState(10e-6, 0, 1e-6, 1e-5, 1, 1);
            Assert.True(zero.Success);
            Assert.Equal(0, zero.FreeTitrant);
            Assert.Equal(0, zero.Occupancy1);
            Assert.Equal(0, zero.Occupancy2);
            Assert.Equal(0, zero.MassBalanceResidual);

            Assert.False(TwoSetsOfSites.CalculateState(
                10e-6, 1.1e-3, double.NaN, 1e-5, 1, 1).Success);
            Assert.False(TwoSetsOfSites.CalculateState(
                -10e-6, 1.1e-3, 1e-6, 1e-5, 1, 1).Success);
            Assert.False(TwoSetsOfSites.CalculateStateWithExpandedCubic(
                10e-6, double.PositiveInfinity, 1e-6, 1e-5, 1, 1).Success);
        }

        [Fact]
        public void ConfiguredAffinityAndStoichiometryBoundsRemainFinite()
        {
            var state = TwoSetsOfSites.CalculateState(
                totalMacromolecule: 1e-3,
                totalTitrant: 1e-2,
                kd1: 1e-20,
                kd2: 100,
                n1: 0.1,
                n2: 10);

            Assert.True(state.Success);
            Assert.InRange(state.FreeTitrant, 0, 1e-2);
            Assert.InRange(state.Occupancy1, 0, 1);
            Assert.InRange(state.Occupancy2, 0, 1);
            Assert.InRange(Math.Abs(state.MassBalanceResidual), 0, 1e-16);
        }

        [Fact]
        public void UnattainableToleranceEndsAtBracketStagnationAsInvalid()
        {
            var state = TwoSetsOfSites.CalculateState(
                totalMacromolecule: 0.001699404052691556,
                totalTitrant: 0.00022387795786860527,
                kd1: 2.0882803082284584,
                kd2: 4.556843092012558e-11,
                n1: 2.4001855482813834,
                n2: 7.45295264392763,
                relativeTolerance: double.Epsilon);

            Assert.False(state.Success);
            Assert.True(state.Iterations > 0);
        }

        [Fact]
        public void AboveOneMillimolarEvaluationIsFiniteAndExothermic()
        {
            var model = CreateModel(actualTitrant: 1.1e-3);

            var heat = model.Evaluate(0, withoffset: false);

            Assert.True(double.IsFinite(heat));
            Assert.True(heat < 0);
        }

        [Fact]
        public void SyringeCorrectionUsesCorrectedTotalTitrantAsPhysicalBracket()
        {
            var standard = CreateModel(actualTitrant: 1.2e-3);
            var corrected = CreateModel(actualTitrant: 0.6e-3);
            corrected.ModelOptions[AttributeKey.UseSyringeActiveFraction].BoolValue = true;
            corrected.ModelOptions[AttributeKey.NumberOfSites1].DoubleValue = 1;
            corrected.ModelOptions[AttributeKey.NumberOfSites2].DoubleValue = 1;
            corrected.Parameters.Table[ParameterType.Nvalue1].Update(2);
            corrected.ApplyModelOptions();

            var expected = standard.Evaluate(0, withoffset: false);
            var actual = corrected.Evaluate(0, withoffset: false);

            Assert.True(double.IsFinite(actual));
            Assert.InRange(Math.Abs(actual - expected), 0, Math.Abs(expected) * 1e-12);
        }

        [Fact]
        public void InvalidDirectEvaluationBecomesFiniteObjectivePenalty()
        {
            var model = CreateModel(actualTitrant: 1.1e-3);
            model.Parameters.Table[ParameterType.Affinity1].Update(400);
            var parameters = model.Parameters.GetFittedParameterArray();

            Assert.True(double.IsNaN(model.Evaluate(0)));

            var unweighted = model.LossFunction(parameters, errorweighted: false);
            var weighted = model.LossFunction(parameters, errorweighted: true);
            var residuals = model.LossFunctionResiduals(parameters, errorweighted: true);

            Assert.Equal(Model.InvalidObjectiveResidualPenalty * Model.InvalidObjectiveResidualPenalty, unweighted);
            Assert.Equal(Model.InvalidObjectiveResidualPenalty * Model.InvalidObjectiveResidualPenalty, weighted);
            Assert.Single(residuals);
            Assert.Equal(Model.InvalidObjectiveResidualPenalty, residuals[0]);
        }

        static TwoSetsOfSites CreateModel(double actualTitrant)
        {
            var data = new ExperimentData("two-site-root.itc")
            {
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(2e-3),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };
            var injection = new InjectionData(data, 0, 2e-6, data.SyringeConcentration * 2e-6, true)
            {
                ActualCellConcentration = 10e-6,
                ActualTitrantConcentration = actualTitrant,
                Ratio = actualTitrant / 10e-6,
            };
            injection.SetPeakArea(new FloatWithError(-1e-6, 1e-8));
            data.Injections.Add(injection);

            var model = new TwoSetsOfSites(data);
            model.InitializeParameters(data);
            model.Parameters.Table[ParameterType.Nvalue1].Update(1);
            model.Parameters.Table[ParameterType.Nvalue2].Update(1);
            model.Parameters.Table[ParameterType.Affinity1].Update(6);
            model.Parameters.Table[ParameterType.Affinity2].Update(5);
            model.Parameters.Table[ParameterType.Enthalpy1].Update(-20000);
            model.Parameters.Table[ParameterType.Enthalpy2].Update(-10000);
            model.Parameters.Table[ParameterType.Offset].Update(0);
            data.Model = model;
            return model;
        }

        static double IndependentBisection(
            double totalMacromolecule,
            double totalTitrant,
            double kd1,
            double kd2,
            double n1,
            double n2)
        {
            var lower = 0.0;
            var upper = totalTitrant;
            for (var iteration = 0; iteration < 200; iteration++)
            {
                var midpoint = lower + (upper - lower) * 0.5;
                if (midpoint == lower || midpoint == upper) break;

                var residual = midpoint
                               + totalMacromolecule
                               * (n1 * midpoint / (kd1 + midpoint)
                                  + n2 * midpoint / (kd2 + midpoint))
                               - totalTitrant;
                if (residual < 0) lower = midpoint;
                else upper = midpoint;
            }

            return lower + (upper - lower) * 0.5;
        }
    }
}
