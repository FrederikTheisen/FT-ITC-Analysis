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
    public sealed class GaussianLikelihoodTests
    {
        [Fact]
        public void EstimatedLikelihoodReportsExactPooledResidualStatistics()
        {
            var model = CreateProbe(
                new ResidualSpec(true, 1e-6, 1e-6),
                new ResidualSpec(true, -2e-6, 2e-6),
                new ResidualSpec(true, 3e-6, 3e-6));

            var evaluation = GaussianLikelihoodEvaluator.Evaluate(
                model,
                GaussianLikelihoodMode.EstimatedCommonVariance);
            var rss = 14e-12;

            Assert.Equal(3, evaluation.ObservationCount);
            Assert.True(evaluation.HasFiniteResidualStatistics);
            Assert.Equal(rss, evaluation.RawResidualSumOfSquares, 15);
            Assert.Equal(1e6 * Math.Sqrt(rss / 3), evaluation.RmsdMicrojoules, 12);
            Assert.True(evaluation.IsLikelihoodAvailable);
            Assert.Equal(3 * (Math.Log(2 * Math.PI * rss / 3) + 1), evaluation.MinusTwoLogLikelihood, 12);
        }

        [Fact]
        public void GlobalEvaluationAndCombinationUseOneCommonVariance()
        {
            var first = CreateProbe(new ResidualSpec(true, 1e-6, 1e-6));
            var second = CreateProbe(
                new ResidualSpec(true, 3e-6, 3e-6),
                new ResidualSpec(true, -3e-6, 3e-6),
                new ResidualSpec(true, 3e-6, 3e-6));
            var global = CreateGlobal(first, second);

            var firstEvaluation = GaussianLikelihoodEvaluator.Evaluate(first, GaussianLikelihoodMode.EstimatedCommonVariance);
            var secondEvaluation = GaussianLikelihoodEvaluator.Evaluate(second, GaussianLikelihoodMode.EstimatedCommonVariance);
            var combined = GaussianLikelihoodEvaluator.Combine(new[] { firstEvaluation, secondEvaluation });
            var direct = GaussianLikelihoodEvaluator.Evaluate(global, GaussianLikelihoodMode.EstimatedCommonVariance);

            Assert.Equal(4, direct.ObservationCount);
            Assert.Equal(28e-12, direct.RawResidualSumOfSquares, 15);
            Assert.Equal(combined.RawResidualSumOfSquares, direct.RawResidualSumOfSquares, 15);
            Assert.Equal(combined.MinusTwoLogLikelihood, direct.MinusTwoLogLikelihood, 12);
            Assert.Equal(1e6 * Math.Sqrt(28e-12 / 4), direct.RmsdMicrojoules, 12);
        }

        [Fact]
        public void PartitioningObservationsAcrossMembersPreservesCommonVarianceLikelihood()
        {
            var unpartitioned = GaussianLikelihoodEvaluator.Evaluate(
                CreateGlobal(CreateProbe(
                    new ResidualSpec(true, 1e-6, 1e-6),
                    new ResidualSpec(true, -2e-6, 1e-6),
                    new ResidualSpec(true, 4e-6, 1e-6))),
                GaussianLikelihoodMode.EstimatedCommonVariance);
            var partitioned = GaussianLikelihoodEvaluator.Evaluate(
                CreateGlobal(
                    CreateProbe(new ResidualSpec(true, 1e-6, 1e-6)),
                    CreateProbe(
                        new ResidualSpec(true, -2e-6, 1e-6),
                        new ResidualSpec(true, 4e-6, 1e-6))),
                GaussianLikelihoodMode.EstimatedCommonVariance);

            Assert.Equal(unpartitioned.ObservationCount, partitioned.ObservationCount);
            Assert.Equal(unpartitioned.RawResidualSumOfSquares, partitioned.RawResidualSumOfSquares, 15);
            Assert.Equal(unpartitioned.MinusTwoLogLikelihood, partitioned.MinusTwoLogLikelihood, 12);
        }

        [Fact]
        public void EmptyAndZeroResidualMembersAreNeutralDuringCombination()
        {
            var empty = CreateProbe(new ResidualSpec(false, 0, 0));
            var zero = CreateProbe(new ResidualSpec(true, 0, 1e-6));
            var positive = CreateProbe(new ResidualSpec(true, 2e-6, 1e-6));

            var emptyEvaluation = GaussianLikelihoodEvaluator.Evaluate(empty, GaussianLikelihoodMode.EstimatedCommonVariance);
            var zeroEvaluation = GaussianLikelihoodEvaluator.Evaluate(zero, GaussianLikelihoodMode.EstimatedCommonVariance);
            var positiveEvaluation = GaussianLikelihoodEvaluator.Evaluate(positive, GaussianLikelihoodMode.EstimatedCommonVariance);
            var combined = GaussianLikelihoodEvaluator.Combine(new[] { emptyEvaluation, zeroEvaluation, positiveEvaluation });

            Assert.False(emptyEvaluation.IsLikelihoodAvailable);
            Assert.False(zeroEvaluation.IsLikelihoodAvailable);
            Assert.True(zeroEvaluation.HasFiniteResidualStatistics);
            Assert.True(combined.IsLikelihoodAvailable);
            Assert.Equal(2, combined.ObservationCount);
            Assert.Equal(4e-12, combined.RawResidualSumOfSquares, 15);
        }

        [Fact]
        public void KnownSigmaLikelihoodUsesHeterogeneousSigmas()
        {
            var model = CreateProbe(
                new ResidualSpec(true, 2e-6, 1e-6),
                new ResidualSpec(true, -3e-6, 2e-6));
            var evaluation = GaussianLikelihoodEvaluator.Evaluate(
                model,
                GaussianLikelihoodMode.KnownObservationSigmas);

            var expectedChiSquare = 4 + 2.25;
            var expectedLogSigmaSquared = 2 * Math.Log(1e-6) + 2 * Math.Log(2e-6);
            Assert.True(evaluation.IsLikelihoodAvailable);
            Assert.Equal(expectedChiSquare, evaluation.StandardizedResidualSumOfSquares, 12);
            Assert.Equal(expectedLogSigmaSquared, evaluation.LogSigmaSquaredSum, 12);
            Assert.Equal(
                expectedChiSquare + 2 * Math.Log(2 * Math.PI) + expectedLogSigmaSquared,
                evaluation.MinusTwoLogLikelihood,
                12);
        }

        [Fact]
        public void KnownSigmaLikelihoodResolvesFallbackSeparatelyPerMember()
        {
            var evaluation = GaussianLikelihoodEvaluator.Evaluate(
                CreateGlobal(
                    CreateProbe(
                        new ResidualSpec(true, 2e-6, double.NaN),
                        new ResidualSpec(true, -4e-6, 2e-6)),
                    CreateProbe(
                        new ResidualSpec(true, 4e-6, double.NaN),
                        new ResidualSpec(true, -8e-6, 4e-6))),
                GaussianLikelihoodMode.KnownObservationSigmas);

            Assert.True(evaluation.IsLikelihoodAvailable);
            Assert.Equal(10, evaluation.StandardizedResidualSumOfSquares, 12);
            Assert.Equal(
                4 * Math.Log(2e-6) + 4 * Math.Log(4e-6),
                evaluation.LogSigmaSquaredSum,
                12);
        }

        [Fact]
        public void ZeroResidualKnownSigmaLikelihoodRemainsAvailable()
        {
            var evaluation = GaussianLikelihoodEvaluator.Evaluate(
                CreateProbe(new ResidualSpec(true, 0, 2e-6)),
                GaussianLikelihoodMode.KnownObservationSigmas);

            Assert.True(evaluation.HasFiniteResidualStatistics);
            Assert.Equal(0, evaluation.RawResidualSumOfSquares);
            Assert.Equal(0, evaluation.RmsdMicrojoules);
            Assert.True(evaluation.IsLikelihoodAvailable);
        }

        [Fact]
        public void InvalidResidualsAndNoObservationsHaveStableDiagnostics()
        {
            var empty = GaussianLikelihoodEvaluator.Evaluate(
                CreateProbe(new ResidualSpec(false, 0, 0)),
                GaussianLikelihoodMode.EstimatedCommonVariance);
            var invalid = GaussianLikelihoodEvaluator.Evaluate(
                CreateProbe(new ResidualSpec(true, double.NaN, 1e-6)),
                GaussianLikelihoodMode.EstimatedCommonVariance);

            Assert.False(empty.IsLikelihoodAvailable);
            Assert.Equal(GaussianLikelihoodEvaluator.NoObservationsReason, empty.UnavailableReason);
            Assert.False(invalid.HasFiniteResidualStatistics);
            Assert.Equal(GaussianLikelihoodEvaluator.NonFiniteResidualReason, invalid.UnavailableReason);
        }

        [Fact]
        public void ResidualOverflowHasStableDiagnostic()
        {
            var evaluation = GaussianLikelihoodEvaluator.Evaluate(
                CreateProbe(new ResidualSpec(true, double.MaxValue, 1e-6)),
                GaussianLikelihoodMode.EstimatedCommonVariance);

            Assert.False(evaluation.HasFiniteResidualStatistics);
            Assert.False(evaluation.IsLikelihoodAvailable);
            Assert.Equal(
                GaussianLikelihoodEvaluator.NonFiniteResidualStatisticsReason,
                evaluation.UnavailableReason);
        }

        [Fact]
        public void InvalidGlobalMemberPreservesTotalObservationCountAndFirstReason()
        {
            var evaluation = GaussianLikelihoodEvaluator.Evaluate(
                CreateGlobal(
                    CreateProbe(
                        new ResidualSpec(true, 1e-6, 1e-6),
                        new ResidualSpec(true, 2e-6, 1e-6)),
                    CreateProbe(new ResidualSpec(true, double.NaN, 1e-6))),
                GaussianLikelihoodMode.EstimatedCommonVariance);

            Assert.Equal(3, evaluation.ObservationCount);
            Assert.False(evaluation.IsLikelihoodAvailable);
            Assert.Equal(GaussianLikelihoodEvaluator.NonFiniteResidualReason, evaluation.UnavailableReason);
        }

        [Fact]
        public void CombineRejectsNullAndMixedModes()
        {
            var model = CreateProbe(new ResidualSpec(true, 1e-6, 1e-6));
            var estimated = GaussianLikelihoodEvaluator.Evaluate(model, GaussianLikelihoodMode.EstimatedCommonVariance);
            var known = GaussianLikelihoodEvaluator.Evaluate(model, GaussianLikelihoodMode.KnownObservationSigmas);

            Assert.Throws<ArgumentNullException>(() => GaussianLikelihoodEvaluator.Combine(null));
            Assert.Throws<ArgumentNullException>(() => GaussianLikelihoodEvaluator.Combine(new GaussianLikelihoodEvaluation[] { estimated, null }));
            Assert.Throws<ArgumentException>(() => GaussianLikelihoodEvaluator.Combine(new[] { estimated, known }));
        }

        [Fact]
        public void LossMethodsMatchSharedEvaluatorRmsd()
        {
            var first = CreateProbe(new ResidualSpec(true, 1e-6, 1e-6));
            var second = CreateProbe(new ResidualSpec(true, -3e-6, 2e-6));
            var global = CreateGlobal(first, second);

            Assert.Equal(
                GaussianLikelihoodEvaluator.Evaluate(first, GaussianLikelihoodMode.EstimatedCommonVariance).RmsdMicrojoules,
                first.Loss(),
                12);
            Assert.Equal(
                GaussianLikelihoodEvaluator.Evaluate(global, GaussianLikelihoodMode.EstimatedCommonVariance).RmsdMicrojoules,
                global.Loss(),
                12);
        }

        [Fact]
        public void EvaluationDoesNotChangeModelState()
        {
            var model = CreateProbe(new ResidualSpec(true, 1e-6, 1e-6));
            var parameter = model.Parameters.Table[ParameterType.Enthalpy1];
            var value = parameter.Value;
            var include = model.Data.Injections[0].Include;

            GaussianLikelihoodEvaluator.Evaluate(model, GaussianLikelihoodMode.KnownObservationSigmas);

            Assert.Equal(value, model.Parameters.Table[ParameterType.Enthalpy1].Value);
            Assert.Equal(include, model.Data.Injections[0].Include);
        }

        static GlobalModel CreateGlobal(params ProbeModel[] models)
        {
            var global = new GlobalModel();
            foreach (var model in models)
            {
                global.AddModel(model);
                global.Parameters.AddIndivdualParameter(model.Parameters);
            }
            return global;
        }

        static ProbeModel CreateProbe(params ResidualSpec[] residuals)
        {
            var data = new ExperimentData("gaussian-likelihood.itc")
            {
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };
            var predictions = new Dictionary<int, double>();
            for (var index = 0; index < residuals.Length; index++)
            {
                var spec = residuals[index];
                var injection = new InjectionData(data, index, 2e-6, 2e-10, spec.Include)
                {
                    ActualCellConcentration = 10e-6,
                    ActualTitrantConcentration = index * 2e-6,
                };
                injection.SetPeakArea(new FloatWithError(spec.Residual, spec.Sigma));
                data.Injections.Add(injection);
                predictions[index] = 0;
            }

            var model = new ProbeModel(data, predictions);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1, islocked: true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000, islocked: true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6, islocked: true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0, islocked: true);
            data.Model = model;
            return model;
        }

        readonly struct ResidualSpec
        {
            public bool Include { get; }
            public double Residual { get; }
            public double Sigma { get; }

            public ResidualSpec(bool include, double residual, double sigma)
            {
                Include = include;
                Residual = residual;
                Sigma = sigma;
            }
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
