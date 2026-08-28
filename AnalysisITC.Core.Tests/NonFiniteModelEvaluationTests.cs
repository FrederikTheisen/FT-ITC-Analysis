using System;
using System.Threading;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class NonFiniteModelEvaluationTests
    {
        [Theory]
        [InlineData(SolverAlgorithm.NelderMead)]
        [InlineData(SolverAlgorithm.LevenbergMarquardt)]
        public void TransientInvalidCandidateDoesNotAbortOptimizer(SolverAlgorithm algorithm)
        {
            var data = CreateExperiment(targetOffset: -1000);
            var model = new CandidateGuardProbeModel(data, invalidAbove: -100);
            model.InitializeProbe(offset: -100);
            data.Model = model;
            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = algorithm,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                MaxOptimizerIterations = 500,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(model.InvalidEvaluationCount > 0);
            Assert.False(convergence.Failed, convergence.Message);
            Assert.True(model.HasFiniteIncludedPredictions());
            Assert.True(model.Parameters.Table[ParameterType.Offset].Value <= -100);
        }

        [Fact]
        public void BootstrapContinuesAndCountsOnlyInvalidFinalReplicates()
        {
            var sequence = new CloneSequence();
            var data = CreateExperiment(targetOffset: 0);
            var model = new AlternatingBootstrapProbeModel(data, sequence, alwaysInvalid: false);
            model.InitializeProbe(offset: 0);
            model.ModelCloneOptions = new ModelCloneOptions
            {
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
            };
            data.Model = model;
            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                BootstrapIterations = 4,
                MaxOptimizerIterations = 300,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success, convergence.Message);
            Assert.Equal(ErrorEstimationOutcome.PartialFailure, convergence.ErrorEstimationOutcome);
            Assert.Equal(4, sequence.Generated);
            Assert.Equal(2, model.Solution.BootstrapSolutions.Count);
            Assert.Contains("succeeded=2", convergence.ErrorEstimationSummary);
            Assert.Contains("failed=2", convergence.ErrorEstimationSummary);
            Assert.Contains("total=4", convergence.ErrorEstimationSummary);
        }

        [Fact]
        public void InvalidPrimaryFinalIsMarkedAndSkipsBootstrap()
        {
            var sequence = new CloneSequence();
            var data = CreateExperiment(targetOffset: 0);
            var model = new AlternatingBootstrapProbeModel(data, sequence, alwaysInvalid: true);
            model.InitializeProbe(offset: 0);
            model.ModelCloneOptions = new ModelCloneOptions
            {
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
            };
            data.Model = model;
            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = SolverAlgorithm.NelderMead,
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                BootstrapIterations = 4,
                MaxOptimizerIterations = 50,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Failed);
            Assert.Equal(SolverTermination.InvalidValues, convergence.Termination);
            Assert.Equal(ErrorEstimationOutcome.None, convergence.ErrorEstimationOutcome);
            Assert.Equal(0, sequence.Generated);
            Assert.Empty(model.Solution.BootstrapSolutions);
        }

        [Fact]
        public void InvalidGlobalMemberMarksWholeFinalAndSkipsBootstrap()
        {
            var sequence = new CloneSequence();
            var validData = CreateExperiment(targetOffset: 0);
            var invalidData = CreateExperiment(targetOffset: 0);
            var valid = new AlternatingBootstrapProbeModel(validData, sequence, alwaysInvalid: false);
            var invalid = new AlternatingBootstrapProbeModel(invalidData, sequence, alwaysInvalid: true);
            valid.InitializeProbe(offset: 0);
            invalid.InitializeProbe(offset: 0);
            validData.Model = valid;
            invalidData.Model = invalid;

            var global = new GlobalModel
            {
                ModelCloneOptions = new ModelCloneOptions
                {
                    ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                    IsGlobalClone = true,
                },
            };
            global.AddModel(valid);
            global.AddModel(invalid);
            global.Parameters.AddIndivdualParameter(valid.Parameters);
            global.Parameters.AddIndivdualParameter(invalid.Parameters);
            var solver = new GlobalSolver
            {
                Model = global,
                SolverAlgorithm = SolverAlgorithm.NelderMead,
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                BootstrapIterations = 4,
                MaxOptimizerIterations = 50,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Failed);
            Assert.Equal(SolverTermination.InvalidValues, convergence.Termination);
            Assert.Equal(ErrorEstimationOutcome.None, convergence.ErrorEstimationOutcome);
            Assert.Equal(0, sequence.Generated);
            Assert.Empty(global.Solution.BootstrapSolutions);
        }

        static ExperimentData CreateExperiment(double targetOffset)
        {
            var data = new ExperimentData("non-finite-probe.itc")
            {
                CellConcentration = new FloatWithError(20e-6),
                SyringeConcentration = new FloatWithError(200e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };

            for (var id = 0; id < 4; id++)
            {
                var include = id != 0;
                var injection = new InjectionData(
                    data, id, 2e-6, data.SyringeConcentration * 2e-6, include)
                {
                    ActualCellConcentration = 20e-6 * (1 - id * 0.01),
                    ActualTitrantConcentration = id * 3e-6,
                    Ratio = id * 0.15,
                };
                injection.SetPeakArea(new FloatWithError(Baseline(id) + targetOffset, 1));
                data.Injections.Add(injection);
            }

            return data;
        }

        static double Baseline(int injectionIndex) => 100 + 10 * injectionIndex;

        sealed class CandidateGuardProbeModel : Model
        {
            readonly double invalidAbove;
            int invalidEvaluationCount;

            public int InvalidEvaluationCount => invalidEvaluationCount;

            public CandidateGuardProbeModel(ExperimentData data, double invalidAbove) : base(data)
            {
                this.invalidAbove = invalidAbove;
            }

            public void InitializeProbe(double offset)
            {
                InitializeParameters(Data);
                Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1, true);
                Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000, true);
                Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6, true);
                Parameters.AddOrUpdateParameter(ParameterType.Offset, offset);
            }

            public override double Evaluate(int injectionindex, bool withoffset = true)
            {
                var offset = Parameters.Table[ParameterType.Offset].Value;
                if (offset > invalidAbove)
                {
                    Interlocked.Increment(ref invalidEvaluationCount);
                    return double.NaN;
                }

                return Baseline(injectionindex) + offset;
            }
        }

        sealed class AlternatingBootstrapProbeModel : Model
        {
            readonly CloneSequence sequence;
            readonly bool alwaysInvalid;

            public AlternatingBootstrapProbeModel(
                ExperimentData data,
                CloneSequence sequence,
                bool alwaysInvalid) : base(data)
            {
                this.sequence = sequence;
                this.alwaysInvalid = alwaysInvalid;
            }

            public void InitializeProbe(double offset)
            {
                InitializeParameters(Data);
                Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1, true);
                Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000, true);
                Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6, true);
                Parameters.AddOrUpdateParameter(ParameterType.Offset, offset);
            }

            public override double Evaluate(int injectionindex, bool withoffset = true)
            {
                if (alwaysInvalid) return double.NaN;
                return Baseline(injectionindex) + Parameters.Table[ParameterType.Offset].Value;
            }

            internal override Model GenerateSyntheticModel(Random random)
            {
                var cloneNumber = Interlocked.Increment(ref sequence.Generated);
                var synthetic = new AlternatingBootstrapProbeModel(
                    Data.GetSynthClone(ModelCloneOptions, random),
                    sequence,
                    alwaysInvalid: cloneNumber % 2 == 0);
                SetSynthModelParameters(synthetic, random);
                return synthetic;
            }
        }

        sealed class CloneSequence
        {
            public int Generated;
        }
    }
}
