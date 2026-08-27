using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class SequentialBindingSitesModelTests
    {
        [Fact]
        public void NewEnumMembersAreAppendedWithoutChangingHistoricalOrdinals()
        {
            Assert.Equal(0, (int)ParameterType.Nvalue1);
            Assert.Equal(6, (int)ParameterType.Offset);
            Assert.Equal(18, (int)ParameterType.ApparentAffinity);
            Assert.Equal(19, (int)ParameterType.Affinity3);
            Assert.Equal(30, (int)ParameterType.EntropyContribution4);
            Assert.Equal(15, (int)AttributeKey.Species);
            Assert.Equal(16, (int)AttributeKey.SequentialSiteCount);
            Assert.Equal(2, (int)AnalysisModel.SequentialBindingSites);
            Assert.Contains(AnalysisModel.SequentialBindingSites, AnalysisModelAttribute.GetAll());
        }

        [Fact]
        public void DefaultShapeHasTwoStepsOffsetAndNoStoichiometryOrSyringeActivity()
        {
            var model = CreateModel(2);

            Assert.Equal(2, model.SiteCount);
            Assert.Equal(new[]
            {
                ParameterType.Affinity1, ParameterType.Enthalpy1,
                ParameterType.Affinity2, ParameterType.Enthalpy2,
                ParameterType.Offset,
            }, model.Parameters.Table.Keys);
            Assert.DoesNotContain(ParameterType.Nvalue1, model.Parameters.Table.Keys);
            Assert.DoesNotContain(AttributeKey.UseSyringeActiveFraction, model.ModelOptions.Keys);
        }

        [Fact]
        public void SiteCountOptionProvidesThreeConciseDropdownChoices()
        {
            var option = ExperimentAttribute.FromKey(AttributeKey.SequentialSiteCount);
            var choices = option.EnumOptions.ToList();

            Assert.Equal(new[] { 2, 3, 4 }, choices.Select(choice => choice.Item1));
            Assert.Equal(
                new[] { "2 binding sites", "3 binding sites", "4 binding sites" },
                choices.Select(choice => choice.Item2));
            Assert.All(choices, choice => Assert.True(choice.Item2.Length < 20));
        }

        [Fact]
        public void SiteCountValidationAndReductionDiscardInactiveValues()
        {
            var model = CreateModel(4);
            model.Parameters.Table[ParameterType.Affinity3].SetValue(12.345, true);

            model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = 2;
            model.ApplyModelOptions();

            Assert.Equal(5, model.Parameters.Table.Count);
            Assert.DoesNotContain(ParameterType.Affinity3, model.Parameters.Table.Keys);
            Assert.DoesNotContain(ParameterType.Enthalpy4, model.Parameters.Table.Keys);

            model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = 4;
            model.ApplyModelOptions();

            Assert.Equal(9, model.Parameters.Table.Count);
            Assert.NotEqual(12.345, model.Parameters.Table[ParameterType.Affinity3].Value);
            Assert.False(model.Parameters.Table[ParameterType.Affinity3].IsLocked);

            model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = 5;
            Assert.Throws<ArgumentOutOfRangeException>(() => model.ApplyModelOptions());
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void StateFractionsNormalizeAndMassBalanceMeetsScaleAwareTolerance(int count)
        {
            var model = CreateModel(count);
            var random = new Random(4100 + count);

            for (var sample = 0; sample < 150; sample++)
            {
                foreach (var slot in ThermodynamicParameterSlots.Active(count))
                    model.Parameters.Table[slot.Affinity].Update(-1.5 + random.NextDouble() * 21.0);

                var macromolecule = Math.Pow(10, -10 + random.NextDouble() * 7);
                var ligand = Math.Pow(10, -12 + random.NextDouble() * 10);
                var state = model.CalculateState(macromolecule, ligand);
                var tolerance = Math.Max(1e-24, 1e-14 * Math.Max(ligand, count * macromolecule));

                Assert.InRange(state.FreeLigand, 0, ligand);
                Assert.InRange(state.MeanOccupancy, 0, count);
                Assert.InRange(Math.Abs(state.Fractions.Sum() - 1), 0, 2e-14);
                Assert.InRange(Math.Abs(state.MassBalanceResidual), 0, tolerance * 1.01);
            }
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void OccupancyIsMonotonicAndFiniteAtZeroAndSaturation(int count)
        {
            var model = CreateModel(count);
            var previous = -1.0;
            foreach (var ligand in new[] { 0.0, 1e-15, 1e-12, 1e-9, 1e-6, 1e-3, 1.0 })
            {
                var state = model.CalculateState(5e-6, ligand);
                Assert.True(double.IsFinite(state.MeanOccupancy));
                Assert.True(state.MeanOccupancy + 1e-13 >= previous);
                previous = state.MeanOccupancy;
            }

            var zero = model.CalculateState(5e-6, 0);
            Assert.Equal(1, zero.Fractions[0]);
            Assert.All(zero.Fractions.Skip(1), value => Assert.Equal(0, value));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void IdenticalMicroscopicSitesMatchOneSetOfSites(int count)
        {
            const double microscopicKa = 3.2e6;
            const double enthalpy = -23500;
            var data = CreateExperiment();
            var sequential = CreateModel(count, data);
            var identical = new OneSetOfSites(data);
            identical.InitializeParameters(data);

            identical.Parameters.Table[ParameterType.Nvalue1].Update(count);
            identical.Parameters.Table[ParameterType.Affinity1].Update(Math.Log10(microscopicKa));
            identical.Parameters.Table[ParameterType.Enthalpy1].Update(enthalpy);
            identical.Parameters.Table[ParameterType.Offset].Update(0);

            foreach (var slot in ThermodynamicParameterSlots.Active(count))
            {
                var macroscopic = ((count - slot.Index + 1.0) / slot.Index) * microscopicKa;
                sequential.Parameters.Table[slot.Affinity].Update(Math.Log10(macroscopic));
                sequential.Parameters.Table[slot.Enthalpy].Update(enthalpy);
            }
            sequential.Parameters.Table[ParameterType.Offset].Update(0);

            foreach (var injection in data.Injections)
            {
                var expected = identical.Evaluate(injection.ID, withoffset: false);
                var actual = sequential.Evaluate(injection.ID, withoffset: false);
                AssertRelative(expected, actual, 2e-9);
            }
        }

        [Fact]
        public void OffsetUsesMolarInjectionMassConventionExactly()
        {
            var model = CreateModel(3);
            const double offset = 7654.321;
            model.Parameters.Table[ParameterType.Offset].Update(offset);

            foreach (var injection in model.Data.Injections)
            {
                var difference = model.Evaluate(injection.ID, true) - model.Evaluate(injection.ID, false);
                Assert.Equal(offset * injection.InjectionMass, difference, 13);
            }
        }

        [Fact]
        public void MixedSignEnthalpiesAndTighterBisectionRemainFiniteAndStable()
        {
            var model = CreateModel(4);
            var signs = new[] { -1, 1, -1, 1 };
            foreach (var slot in ThermodynamicParameterSlots.Active(4))
                model.Parameters.Table[slot.Enthalpy].Update(signs[slot.Index - 1] * slot.Index * 15000);

            foreach (var injection in model.Data.Injections)
                Assert.True(double.IsFinite(model.Evaluate(injection.ID)));

            var standard = model.CalculateState(8e-6, 19e-6);
            var tighter = model.CalculateState(8e-6, 19e-6, 1e-16);
            Assert.InRange(Math.Abs(standard.FreeLigand - tighter.FreeLigand), 0, 3e-18);
        }

        [Fact]
        public void AffinityBoundsExcludedInjectionsAndSegmentStartsRemainFinite()
        {
            var model = CreateModel(4);
            model.Parameters.Table[ParameterType.Affinity1].Update(
                ParameterType.Affinity1.GetProperties().DefaultLimits[0]);
            model.Parameters.Table[ParameterType.Affinity4].Update(
                ParameterType.Affinity1.GetProperties().DefaultLimits[1]);
            model.Data.ReplaceSegments(new[]
            {
                new TandemExperimentSegment(0, model.Data.CellConcentration, 0),
                new TandemExperimentSegment(8, 18e-6, 7e-6),
            });

            Assert.False(model.Data.Injections[0].Include);
            Assert.True(double.IsFinite(model.Evaluate(0)));
            Assert.True(double.IsFinite(model.Evaluate(8)));
            Assert.All(model.Data.Injections, injection =>
                Assert.True(double.IsFinite(model.Evaluate(injection.ID))));
        }

        [Fact]
        public void SyntheticCloneAndSolutionRetainConcreteModelAndDynamicReportShape()
        {
            var model = CreateModel(4);
            model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None };
            var synthetic = Assert.IsType<SequentialBindingSites>(model.GenerateSyntheticModel());
            Assert.Equal(4, synthetic.SiteCount);
            Assert.Equal(model.Parameters.Table.Keys, synthetic.Parameters.Table.Keys);

            var solution = Assert.IsType<SequentialBindingSites.ModelSolution>(
                SolutionInterface.FromModel(model, null));
            Assert.Equal(16, solution.ReportParameters.Count);
            Assert.Contains(ParameterType.Affinity4, solution.ReportParameters.Keys);
            Assert.Contains(ParameterType.Gibbs4, solution.ReportParameters.Keys);
            Assert.DoesNotContain(ParameterType.Nvalue1, solution.ReportParameters.Keys);
        }

        [Theory]
        [InlineData(SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(SolverAlgorithm.NelderMead)]
        public void SyntheticTwoStepDataRecoverWithBothOptimizers(SolverAlgorithm algorithm)
        {
            var truth = CreateModel(2);
            SetTruth(truth, new[] { 6.30, 5.20 }, new[] { -28000.0, 14500.0 });
            WriteSyntheticHeats(truth);

            var fitted = CreateModel(2, truth.Data);
            fitted.Parameters.Table[ParameterType.Affinity1].Update(6.05);
            fitted.Parameters.Table[ParameterType.Affinity2].Update(5.45);
            fitted.Parameters.Table[ParameterType.Enthalpy1].Update(-24000.0);
            fitted.Parameters.Table[ParameterType.Enthalpy2].Update(11000.0);
            fitted.Parameters.Table[ParameterType.Offset].Update(0, true);

            var convergence = Solve(fitted, algorithm);

            Assert.True(convergence.Success, convergence.Message);
            AssertRecovered(truth, fitted, 2, affinityTolerance: 3e-3, enthalpyTolerance: 5e-3);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        public void PartiallyLockedHigherStepSyntheticDataRecoverActiveCoordinates(int count)
        {
            var truth = CreateModel(count);
            var affinities = new[] { 6.45, 5.75, 5.10, 4.55 }.Take(count).ToArray();
            var enthalpies = new[] { -31000.0, 18000.0, -9000.0, 6500.0 }.Take(count).ToArray();
            SetTruth(truth, affinities, enthalpies);
            WriteSyntheticHeats(truth);

            var fitted = CreateModel(count, truth.Data);
            foreach (var slot in ThermodynamicParameterSlots.Active(count))
            {
                var index = slot.Index - 1;
                fitted.Parameters.Table[slot.Affinity].Update(affinities[index] + (slot.Index <= 2 ? 0.18 : 0), slot.Index > 2);
                fitted.Parameters.Table[slot.Enthalpy].Update(enthalpies[index], true);
            }
            fitted.Parameters.Table[ParameterType.Offset].Update(0, true);

            var convergence = Solve(fitted, SolverAlgorithm.LevenbergMarquardt);

            Assert.True(convergence.Success, convergence.Message);
            foreach (var slot in ThermodynamicParameterSlots.Active(count).Take(2))
                Assert.InRange(Math.Abs(
                    fitted.Parameters.Table[slot.Affinity].Value
                    - truth.Parameters.Table[slot.Affinity].Value), 0, 5e-3);
            Assert.All(fitted.Parameters.Table.Values, parameter =>
                Assert.True(double.IsFinite(parameter.Value)));
        }

        static SequentialBindingSites CreateModel(int count, ExperimentData data = null)
        {
            data ??= CreateExperiment();
            var model = new SequentialBindingSites(data);
            model.InitializeParameters(data);
            model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = count;
            model.ApplyModelOptions();
            return model;
        }

        static ExperimentData CreateExperiment()
        {
            var data = new ExperimentData("sequential-kernel.itc")
            {
                CellConcentration = new FloatWithError(35e-6),
                SyringeConcentration = new FloatWithError(420e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };

            for (var index = 0; index < 18; index++)
            {
                var volume = index == 0 ? 0.5e-6 : 2e-6;
                var injection = new InjectionData(data, index, volume,
                    data.SyringeConcentration * volume, include: index != 0)
                {
                    ActualCellConcentration = data.CellConcentration * Math.Pow(0.9986, index + 1),
                    ActualTitrantConcentration = 2.5e-6 * (index + 1),
                    Ratio = (2.5e-6 * (index + 1)) /
                        (data.CellConcentration * Math.Pow(0.9986, index + 1)),
                };
                injection.SetPeakArea(new FloatWithError(-2e-6 + index * 4e-8));
                data.Injections.Add(injection);
            }

            return data;
        }

        static void SetTruth(
            SequentialBindingSites model,
            IReadOnlyList<double> affinities,
            IReadOnlyList<double> enthalpies)
        {
            foreach (var slot in ThermodynamicParameterSlots.Active(model.SiteCount))
            {
                model.Parameters.Table[slot.Affinity].Update(affinities[slot.Index - 1]);
                model.Parameters.Table[slot.Enthalpy].Update(enthalpies[slot.Index - 1]);
            }
            model.Parameters.Table[ParameterType.Offset].Update(0, true);
        }

        static void WriteSyntheticHeats(SequentialBindingSites truth)
        {
            foreach (var injection in truth.Data.Injections)
                injection.SetPeakArea(new FloatWithError(truth.Evaluate(injection.ID)));
        }

        static SolverConvergence Solve(SequentialBindingSites model, SolverAlgorithm algorithm)
        {
            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = algorithm,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                UseErrorWeightedFitting = false,
                MaxOptimizerIterations = 6000,
                Silent = true,
            };
            return solver.Solve();
        }

        static void AssertRecovered(
            SequentialBindingSites truth,
            SequentialBindingSites fitted,
            int count,
            double affinityTolerance,
            double enthalpyTolerance)
        {
            foreach (var slot in ThermodynamicParameterSlots.Active(count))
            {
                Assert.InRange(Math.Abs(
                    fitted.Parameters.Table[slot.Affinity].Value
                    - truth.Parameters.Table[slot.Affinity].Value), 0, affinityTolerance);
                AssertRelative(
                    truth.Parameters.Table[slot.Enthalpy].Value,
                    fitted.Parameters.Table[slot.Enthalpy].Value,
                    enthalpyTolerance);
            }
        }

        static void AssertRelative(double expected, double actual, double tolerance)
        {
            var scale = Math.Max(Math.Abs(expected), 1e-20);
            Assert.InRange(Math.Abs(expected - actual) / scale, 0, tolerance);
        }
    }
}
