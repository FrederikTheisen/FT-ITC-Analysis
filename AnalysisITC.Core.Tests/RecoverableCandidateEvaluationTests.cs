using System;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection("AutoSaveManager")]
    public sealed class RecoverableCandidateEvaluationTests
    {
        [Fact]
        public void LocalInvalidCandidateReceivesScaledWholeVectorPenalty()
        {
            var model = CreateProbe(initialOffset: -1);
            var solver = new CandidateProbeSolver { Model = model };
            var initial = model.Parameters.GetFittedParameterArray();
            var baseline = solver.Prepare(model, initial);

            var invalid = new[] { 1.0 };
            var scalarPenalty = solver.Candidate(model, invalid);
            var residualPenalty = solver.CandidateResiduals(model, invalid);

            Assert.Equal(Math.Max(1, baseline) * 1e12, scalarPenalty);
            Assert.Equal(model.NumberOfPoints, residualPenalty.Length);
            Assert.All(residualPenalty, residual =>
                Assert.Equal(Math.Sqrt(scalarPenalty / model.NumberOfPoints), residual));
            Assert.Equal(2, solver.RejectedTrialEvaluationCount);
        }

        [Fact]
        public void GlobalInvalidMemberRejectsEntireParameterVector()
        {
            var first = CreateProbe(initialOffset: -1);
            var second = CreateProbe(initialOffset: -1);
            var global = new GlobalModel();
            global.AddModel(first);
            global.AddModel(second);
            global.Parameters.AddIndivdualParameter(first.Parameters);
            global.Parameters.AddIndivdualParameter(second.Parameters);

            var solver = new GlobalCandidateProbeSolver { Model = global };
            var initial = global.Parameters.GetFittedParameterArray();
            var baseline = solver.Prepare(global, initial);
            var invalid = new[] { 1.0, -1.0 };

            var penalty = solver.Candidate(global, invalid);
            var residuals = solver.CandidateResiduals(global, invalid);

            Assert.Equal(Math.Max(1, baseline) * 1e12, penalty);
            Assert.Equal(global.GetNumberOfPoints(), residuals.Length);
            Assert.All(residuals, residual =>
                Assert.Equal(Math.Sqrt(penalty / global.GetNumberOfPoints()), residual));
            Assert.Equal(2, solver.RejectedTrialEvaluationCount);
        }

        [Fact]
        public void TryResidualContractReturnsNoPartialVector()
        {
            var model = CreateProbe(initialOffset: -1);

            var success = model.TryLossFunctionResiduals(
                new[] { 1.0 }, errorweighted: false, out var residuals);

            Assert.False(success);
            Assert.Null(residuals);
        }

        [Fact]
        public void GlobalAffinityGuardRejectsOverflowButAllowsFiniteAffinityOutsideLocalBounds()
        {
            var member = CreateProbe(initialOffset: -1);
            var global = new GlobalModel();
            global.AddModel(member);
            global.Parameters.AddIndivdualParameter(member.Parameters);
            global.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.SameForAll);
            global.Parameters.AddorUpdateGlobalParameter(ParameterType.Affinity1, 25);
            global.Parameters.SetIndividualFromGlobal();

            Assert.True(global.TryLossFunction(new[] { 25.0, -1.0 }, false, out _));
            Assert.True(global.TryLossFunctionResiduals(new[] { 25.0, -1.0 }, false, out var residuals));
            Assert.Equal(global.GetNumberOfPoints(), residuals.Length);

            Assert.False(global.TryLossFunction(new[] { 400.0, -1.0 }, false, out _));
            Assert.False(global.TryLossFunctionResiduals(new[] { 400.0, -1.0 }, false, out residuals));
            Assert.Null(residuals);
            Assert.False(global.TryLossFunction(new[] { -400.0, -1.0 }, false, out _));
            Assert.False(global.TryLossFunction(new[] { -310.0, -1.0 }, false, out _));
        }

        [Fact]
        public void TemperatureDependentGlobalAffinityIsCheckedAtEachMemberTemperature()
        {
            var warm = CreateProbe(-1, temperature: 40);
            var cold = CreateProbe(-1, temperature: 20);
            var global = new GlobalModel();
            global.AddModel(warm);
            global.AddModel(cold);
            global.Parameters.AddIndivdualParameter(warm.Parameters);
            global.Parameters.AddIndivdualParameter(cold.Parameters);
            global.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.TemperatureDependent);
            global.Parameters.AddorUpdateGlobalParameter(ParameterType.Gibbs1, -1.78e6);
            global.Parameters.SetIndividualFromGlobal();

            Assert.True(FWEMath.IsFinite(Math.Pow(10, warm.Parameters.Table[ParameterType.Affinity1].Value)));
            Assert.True(double.IsPositiveInfinity(Math.Pow(10, cold.Parameters.Table[ParameterType.Affinity1].Value)));
            var values = global.Parameters.GetFittedParameterArray();
            Assert.False(global.TryLossFunction(values, false, out _));
            Assert.False(global.TryLossFunctionResiduals(values, false, out _));
        }

        [Fact]
        public void LinkedAffinityChecksWhenLocalEnthalpyIsFittedButSkipsFixedRelationship()
        {
            var member = CreateProbe(initialOffset: -1);
            member.Parameters.Table[ParameterType.Enthalpy1].Update(-1000, lockpar: false);
            var global = new GlobalModel();
            global.AddModel(member);
            global.Parameters.AddIndivdualParameter(member.Parameters);
            global.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.ThermodynamicallyLinked);
            global.Parameters.AddorUpdateGlobalParameter(ParameterType.Gibbs1, -1e9, islocked: true);
            global.Parameters.SetIndividualFromGlobal();

            Assert.False(global.TryLossFunction(global.Parameters.GetFittedParameterArray(), false, out _));

            member.Parameters.Table[ParameterType.Enthalpy1].Update(-1000, lockpar: true);
            global.Parameters.SetIndividualFromGlobal();
            Assert.True(global.TryLossFunction(global.Parameters.GetFittedParameterArray(), false, out _));
        }

        [Fact]
        public void LinkedAffinityChecksFittedSharedEnthalpyAndHeatCapacityCoordinates()
        {
            AssertLinkedAffinityRejects(VariableConstraint.SameForAll, enthalpyLocked: false, heatCapacityLocked: true);
            AssertLinkedAffinityRejects(VariableConstraint.TemperatureDependent, enthalpyLocked: false, heatCapacityLocked: true);
            AssertLinkedAffinityRejects(VariableConstraint.TemperatureDependent, enthalpyLocked: true, heatCapacityLocked: false);
        }

        static void AssertLinkedAffinityRejects(VariableConstraint enthalpyConstraint, bool enthalpyLocked, bool heatCapacityLocked)
        {
            var member = CreateProbe(initialOffset: -1, temperature: 30);
            var global = new GlobalModel();
            global.AddModel(member);
            global.Parameters.AddIndivdualParameter(member.Parameters);
            global.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.ThermodynamicallyLinked);
            global.Parameters.SetConstraintForParameter(ParameterType.Enthalpy1, enthalpyConstraint);
            global.Parameters.AddorUpdateGlobalParameter(ParameterType.Gibbs1, -1e9, islocked: true);
            global.Parameters.AddorUpdateGlobalParameter(ParameterType.Enthalpy1, -1000, islocked: enthalpyLocked);
            if (enthalpyConstraint == VariableConstraint.TemperatureDependent)
                global.Parameters.AddorUpdateGlobalParameter(ParameterType.HeatCapacity1, 0, islocked: heatCapacityLocked);
            global.Parameters.SetIndividualFromGlobal();

            Assert.False(global.TryLossFunction(global.Parameters.GetFittedParameterArray(), false, out _));
        }

        [Fact]
        public void GlobalAffinityGuardSkipsWhollyLockedRelationship()
        {
            var member = CreateProbe(initialOffset: -1);
            var global = new GlobalModel();
            global.AddModel(member);
            global.Parameters.AddIndivdualParameter(member.Parameters);
            global.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.SameForAll);
            global.Parameters.AddorUpdateGlobalParameter(ParameterType.Affinity1, 400, islocked: true);
            global.Parameters.SetIndividualFromGlobal();

            Assert.True(global.TryLossFunction(new[] { -1.0 }, false, out _));
        }

        [Fact]
        public void OneSiteFiniteHeatAtLogAffinity400IsRejectedWhenGlobalCoordinateIsFitted()
        {
            var source = CreateProbe(initialOffset: -1);
            var member = new OneSetOfSites(source.Data);
            member.InitializeParameters(member.Data);
            member.Parameters.Table[ParameterType.Nvalue1].Update(1, lockpar: true);
            member.Parameters.Table[ParameterType.Enthalpy1].Update(-1000, lockpar: true);
            member.Parameters.Table[ParameterType.Affinity1].Update(400, lockpar: true);
            member.Parameters.Table[ParameterType.Offset].Update(0, lockpar: true);
            member.Data.Model = member;
            Assert.True(member.HasFiniteIncludedPredictions());

            var global = new GlobalModel();
            global.AddModel(member);
            global.Parameters.AddIndivdualParameter(member.Parameters);
            global.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.SameForAll);
            global.Parameters.AddorUpdateGlobalParameter(ParameterType.Affinity1, 400);
            global.Parameters.SetIndividualFromGlobal();

            Assert.False(global.TryLossFunction(new[] { 400.0 }, false, out _));
        }

        [Theory]
        [InlineData(SolverAlgorithm.NelderMead)]
        [InlineData(SolverAlgorithm.LevenbergMarquardt)]
        public void GlobalSolverRejectsNumericallyInvalidInitialAffinityForBothAlgorithms(SolverAlgorithm algorithm)
        {
            var previous = AppSettings.ParameterLimitSetting;
            try
            {
                AppSettings.ParameterLimitSetting = ParameterLimitSetting.NoLimit;
                var member = CreateProbe(initialOffset: -1);
                var global = new GlobalModel();
                global.AddModel(member);
                global.Parameters.AddIndivdualParameter(member.Parameters);
                global.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.TemperatureDependent);
                global.Parameters.AddorUpdateGlobalParameter(ParameterType.Gibbs1, -2.1e6);
                global.Parameters.SetIndividualFromGlobal();
                var solver = new GlobalSolver
                {
                    Model = global,
                    SolverAlgorithm = algorithm,
                    ErrorEstimationMethod = ErrorEstimationMethod.None,
                    MaxOptimizerIterations = 10,
                    Silent = true,
                };

                var convergence = solver.Solve();

                Assert.True(convergence.Failed);
                Assert.Equal(SolverTermination.InvalidValues, convergence.Termination);
            }
            finally
            {
                AppSettings.ParameterLimitSetting = previous;
            }
        }

        [Fact]
        public void InvalidFinalCandidateRestoresValidInitialParameters()
        {
            var model = CreateProbe(initialOffset: -1);
            var solver = new CandidateProbeSolver { Model = model };
            var initial = model.Parameters.GetFittedParameterArray();
            solver.Prepare(model, initial);

            solver.SelectFinal(model, initial, new[] { 1.0 });

            Assert.Equal(-1, model.Parameters.Table[ParameterType.Offset].Value);
            Assert.True(model.HasFiniteIncludedPredictions());
        }

        [Fact]
        public void GlobalOptimizerContinuesAfterInvalidMemberTrials()
        {
            var guarded = CreateProbe(initialOffset: -1, invalidAbove: -0.5);
            var unguarded = CreateProbe(initialOffset: -1, invalidAbove: double.MaxValue);
            var global = new GlobalModel();
            global.AddModel(guarded);
            global.AddModel(unguarded);
            global.Parameters.AddIndivdualParameter(guarded.Parameters);
            global.Parameters.AddIndivdualParameter(unguarded.Parameters);
            var solver = new GlobalSolver
            {
                Model = global,
                SolverAlgorithm = SolverAlgorithm.NelderMead,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                MaxOptimizerIterations = 100,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(solver.RejectedTrialEvaluationCount > 0);
            Assert.False(convergence.Failed, convergence.Message);
            Assert.True(guarded.HasFiniteIncludedPredictions());
        }

        [Fact]
        public void UnexpectedEvaluationExceptionStillPropagates()
        {
            var model = CreateProbe(initialOffset: -1, throwUnexpectedly: true);
            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = SolverAlgorithm.NelderMead,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                MaxOptimizerIterations = 20,
                Silent = true,
            };

            Assert.Throws<InvalidOperationException>(() => solver.Solve());
        }

        [Fact]
        public void InvalidInitialPointReturnsExplicitInvalidValuesConvergence()
        {
            var model = CreateProbe(initialOffset: 1);
            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = SolverAlgorithm.NelderMead,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                MaxOptimizerIterations = 20,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Failed);
            Assert.Equal(SolverTermination.InvalidValues, convergence.Termination);
            Assert.NotNull(model.Solution);
        }

        static ProbeModel CreateProbe(
            double initialOffset,
            bool throwUnexpectedly = false,
            double invalidAbove = 0,
            double temperature = 25)
        {
            var data = new ExperimentData("candidate-policy.itc")
            {
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = temperature,
                TargetTemperature = temperature,
            };

            for (var index = 0; index < 3; index++)
            {
                var injection = new InjectionData(data, index, 2e-6, 2e-10, include: true)
                {
                    ActualCellConcentration = 10e-6,
                    ActualTitrantConcentration = index * 2e-6,
                };
                injection.SetPeakArea(new FloatWithError(0, 1));
                data.Injections.Add(injection);
            }

            var model = new ProbeModel(data, throwUnexpectedly, invalidAbove);
            model.InitializeProbe(initialOffset);
            data.Model = model;
            return model;
        }

        sealed class ProbeModel : Model
        {
            readonly bool throwUnexpectedly;
            readonly double invalidAbove;

            internal ProbeModel(
                ExperimentData data,
                bool throwUnexpectedly,
                double invalidAbove) : base(data)
            {
                this.throwUnexpectedly = throwUnexpectedly;
                this.invalidAbove = invalidAbove;
            }

            internal void InitializeProbe(double offset)
            {
                InitializeParameters(Data);
                Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1, islocked: true);
                Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000, islocked: true);
                Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6, islocked: true);
                Parameters.AddOrUpdateParameter(ParameterType.Offset, offset);
            }

            public override double Evaluate(int injectionindex, bool withoffset = true)
            {
                if (throwUnexpectedly)
                    throw new InvalidOperationException("Unexpected probe failure.");

                var offset = Parameters.Table[ParameterType.Offset].Value;
                return offset > invalidAbove && injectionindex == 1
                    ? double.NaN
                    : offset;
            }
        }

        sealed class CandidateProbeSolver : Solver
        {
            internal double Prepare(Model model, double[] initial) =>
                PrepareCandidateEvaluations(model, initial, false, model.NumberOfPoints);

            internal double Candidate(Model model, double[] parameters) =>
                EvaluateCandidate(model, parameters, false);

            internal double[] CandidateResiduals(Model model, double[] parameters) =>
                EvaluateCandidateResiduals(model, parameters, false);

            internal double SelectFinal(Model model, double[] initial, double[] fitted) =>
                ApplyBestFittedParameters(
                    model,
                    initial,
                    fitted,
                    errorWeighted: false,
                    scope: "Test",
                    parameters: model.Parameters.GetFittedParameters()).UnweightedRmsd;
        }

        sealed class GlobalCandidateProbeSolver : GlobalSolver
        {
            internal double Prepare(GlobalModel model, double[] initial) =>
                PrepareCandidateEvaluations(model, initial, false, model.GetNumberOfPoints());

            internal double Candidate(GlobalModel model, double[] parameters) =>
                EvaluateCandidate(model, parameters, false);

            internal double[] CandidateResiduals(GlobalModel model, double[] parameters) =>
                EvaluateCandidateResiduals(model, parameters, false);
        }
    }
}
