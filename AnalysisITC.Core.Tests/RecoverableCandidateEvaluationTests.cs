using System;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests
{
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
            double invalidAbove = 0)
        {
            var data = new ExperimentData("candidate-policy.itc")
            {
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
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
                    parameters: model.Parameters.GetFittedParameters());
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
