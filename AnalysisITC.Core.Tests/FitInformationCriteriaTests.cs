using System;
using System.Collections.Generic;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class FitInformationCriteriaTests
    {
        [Fact]
        public void UnweightedCriteriaIncludeEstimatedVarianceInK()
        {
            var solution = CreateSolution(10, 2, weighted: false, residual: 1e-6);
            var criteria = FitInformationCriteriaCalculator.Calculate(solution);
            var rss = 10e-12;
            var minusTwoLogLikelihood = 10 * (Math.Log(2 * Math.PI * rss / 10) + 1);
            var aic = minusTwoLogLikelihood + 2 * 3;

            Assert.Equal(10, criteria.ObservationCount);
            Assert.Equal(2, criteria.FittedParameterCount);
            Assert.Equal(3, criteria.LikelihoodParameterCount);
            Assert.False(criteria.UsesKnownObservationSigmas);
            Assert.Equal(minusTwoLogLikelihood, criteria.MinusTwoLogLikelihood.Value, 12);
            Assert.Equal(aic, criteria.Aic.Value, 12);
            Assert.Equal(aic + 2 * 3 * 4.0 / 6, criteria.Aicc.Value, 12);
        }

        [Fact]
        public void WeightedCriteriaDoNotCountKnownSigmasInK()
        {
            var solution = CreateSolution(10, 2, weighted: true, residual: 2e-6);
            var criteria = FitInformationCriteriaCalculator.Calculate(solution);
            var chiSquare = 10 * 4;
            var logSigmaSquaredSum = 10 * 2 * Math.Log(1e-6);
            var minusTwoLogLikelihood = chiSquare + 10 * Math.Log(2 * Math.PI) + logSigmaSquaredSum;

            Assert.Equal(2, criteria.LikelihoodParameterCount);
            Assert.True(criteria.UsesKnownObservationSigmas);
            Assert.Equal(minusTwoLogLikelihood, criteria.MinusTwoLogLikelihood.Value, 12);
            Assert.Equal(minusTwoLogLikelihood + 4, criteria.Aic.Value, 12);
        }

        [Fact]
        public void FixedParametersAreExcludedAndAicRemainsAvailableWhenAiccDoesNot()
        {
            var solution = CreateSolution(4, 2, weighted: false, residual: 1e-6);
            var criteria = FitInformationCriteriaCalculator.Calculate(solution);

            Assert.Equal(2, criteria.FittedParameterCount);
            Assert.Equal(3, criteria.LikelihoodParameterCount);
            Assert.True(criteria.IsAicAvailable);
            Assert.False(criteria.IsAiccAvailable);
            Assert.Equal(FitInformationCriteriaCalculator.AiccSampleSizeReason, criteria.AiccUnavailableReason);
            Assert.NotNull(criteria.Aic);
            Assert.Null(criteria.Aicc);
        }

        [Fact]
        public void LikelihoodFailureReasonIsSharedByAicAndAicc()
        {
            var criteria = FitInformationCriteriaCalculator.Calculate(
                CreateSolution(5, 0, weighted: false, residual: 0));

            Assert.False(criteria.IsAicAvailable);
            Assert.False(criteria.IsAiccAvailable);
            Assert.Null(criteria.MinusTwoLogLikelihood);
            Assert.Null(criteria.Aic);
            Assert.Null(criteria.Aicc);
            Assert.Equal(GaussianLikelihoodEvaluator.ZeroResidualVarianceReason, criteria.AicUnavailableReason);
            Assert.Equal(criteria.AicUnavailableReason, criteria.AiccUnavailableReason);
        }

        [Fact]
        public void SharedCoordinatesCountOnceAndMemberCoordinatesCountPerMember()
        {
            var first = CreateSolution(4, 1, weighted: false, residual: 1e-6).Model.Models[0];
            var second = CreateSolution(4, 1, weighted: false, residual: 2e-6).Model.Models[0];
            var global = new GlobalModel(new List<Model> { first, second });
            global.Parameters.AddIndivdualParameter(first.Parameters);
            global.Parameters.AddIndivdualParameter(second.Parameters);
            global.Parameters.AddorUpdateGlobalParameter(
                ParameterType.Affinity1,
                6,
                islocked: false);
            var solution = new GlobalSolution(
                new GlobalSolver { Model = global },
                Convergence());

            var criteria = FitInformationCriteriaCalculator.Calculate(solution);

            Assert.Equal(3, criteria.FittedParameterCount);
            Assert.Equal(4, criteria.LikelihoodParameterCount);
        }

        [Fact]
        public void AnalysisResultSnapshotIsStableUntilSolutionIsReplaced()
        {
            var initial = CreateSolution(5, 0, weighted: false, residual: 1e-6);
            var result = new AnalysisResult(initial);
            var initialAic = result.InformationCriteria.Aic;
            var initialAicc = result.InformationCriteria.Aicc;

            initial.Model.Models[0].Data.Injections[0].SetPeakArea(new FloatWithError(100e-6, 1e-6));
            Assert.Equal(initialAic, result.InformationCriteria.Aic);
            Assert.Equal(initialAicc, result.InformationCriteria.Aicc);

            var replacement = CreateSolution(5, 0, weighted: false, residual: 2e-6);
            result.UpdateSolution(replacement);
            Assert.NotEqual(initialAic, result.InformationCriteria.Aic);
        }

        static GlobalSolution CreateSolution(int observations, int fittedParameters, bool weighted, double residual)
        {
            var data = new ExperimentData("aic.itc")
            {
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };
            var predictions = new Dictionary<int, double>();
            for (var index = 0; index < observations; index++)
            {
                var injection = new InjectionData(data, index, 2e-6, 2e-10, true)
                {
                    ActualCellConcentration = 10e-6,
                    ActualTitrantConcentration = index * 2e-6,
                };
                injection.SetPeakArea(new FloatWithError(residual, 1e-6));
                data.Injections.Add(injection);
                predictions[index] = 0;
            }

            var model = new ProbeModel(data, predictions);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1, islocked: fittedParameters < 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000, islocked: fittedParameters < 2);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6, islocked: true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0, islocked: true);
            data.Model = model;

            var global = new GlobalModel(new List<Model> { model });
            global.Parameters.AddIndivdualParameter(model.Parameters);
            var solver = new GlobalSolver
            {
                Model = global,
                UseErrorWeightedFitting = weighted,
            };
            var solution = new GlobalSolution(solver, Convergence());
            global.Solution = solution;
            return solution;
        }

        static SolverConvergence Convergence()
        {
            return SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Algorithm = SolverAlgorithm.LevenbergMarquardt,
                Termination = SolverTermination.Converged,
            });
        }

        sealed class ProbeModel : Model
        {
            readonly IReadOnlyDictionary<int, double> predictions;

            public ProbeModel(ExperimentData data, IReadOnlyDictionary<int, double> predictions)
                : base(data) => this.predictions = predictions;

            public override double Evaluate(int injectionindex, bool withoffset = true) => predictions[injectionindex];
        }
    }
}
