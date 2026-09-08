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
            Assert.Equal(350e6, evaluation.MolarResidualSumOfSquares, 6);
            Assert.Equal(Math.Sqrt(350e6 / 3), evaluation.MolarRmsdJoulesPerMole.Value, 9);
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
        public void MolarRmsdNormalizesEachIncludedInjectionBeforePooling()
        {
            var first = CreateProbe(
                new ResidualSpec(true, 2e-6, 1e-6, 1e-9),
                new ResidualSpec(false, 100e-6, 1e-6, 1e-10));
            var second = CreateProbe(
                new ResidualSpec(true, -3e-6, 1e-6, 3e-9),
                new ResidualSpec(true, 8e-6, 1e-6, 2e-9));

            var evaluation = GaussianLikelihoodEvaluator.Evaluate(
                CreateGlobal(first, second),
                GaussianLikelihoodMode.EstimatedCommonVariance);

            var expectedMolarRss = 2000.0 * 2000.0
                + 1000.0 * 1000.0
                + 4000.0 * 4000.0;
            Assert.Equal(3, evaluation.ObservationCount);
            Assert.Equal(expectedMolarRss, evaluation.MolarResidualSumOfSquares, 6);
            Assert.Equal(
                Math.Sqrt(expectedMolarRss / 3),
                evaluation.MolarRmsdJoulesPerMole.Value,
                9);
        }

        [Theory]
        [InlineData(GaussianLikelihoodMode.EstimatedCommonVariance)]
        [InlineData(GaussianLikelihoodMode.EstimatedWeightedVariance)]
        public void PartitioningObservationsAcrossMembersPreservesCommonVarianceLikelihood(GaussianLikelihoodMode mode)
        {
            var unpartitioned = GaussianLikelihoodEvaluator.Evaluate(
                CreateGlobal(CreateProbe(
                    new ResidualSpec(true, 1e-6, 1e-6),
                    new ResidualSpec(true, -2e-6, 2e-6),
                    new ResidualSpec(true, 4e-6, 3e-6))),
                mode);
            var partitioned = GaussianLikelihoodEvaluator.Evaluate(
                CreateGlobal(
                    CreateProbe(new ResidualSpec(true, 1e-6, 1e-6)),
                    CreateProbe(
                        new ResidualSpec(true, -2e-6, 2e-6),
                        new ResidualSpec(true, 4e-6, 3e-6))),
                mode);

            Assert.Equal(unpartitioned.ObservationCount, partitioned.ObservationCount);
            Assert.Equal(unpartitioned.RawResidualSumOfSquares, partitioned.RawResidualSumOfSquares, 15);
            Assert.Equal(unpartitioned.MinusTwoLogLikelihood, partitioned.MinusTwoLogLikelihood, 12);
        }

        [Theory]
        [InlineData(GaussianLikelihoodMode.EstimatedCommonVariance)]
        [InlineData(GaussianLikelihoodMode.EstimatedWeightedVariance)]
        public void EmptyAndZeroResidualMembersAreNeutralDuringCombination(GaussianLikelihoodMode mode)
        {
            var empty = CreateProbe(new ResidualSpec(false, 0, 0));
            var zero = CreateProbe(new ResidualSpec(true, 0, 1e-6));
            var positive = CreateProbe(new ResidualSpec(true, 2e-6, 1e-6));

            var emptyEvaluation = GaussianLikelihoodEvaluator.Evaluate(empty, mode);
            var zeroEvaluation = GaussianLikelihoodEvaluator.Evaluate(zero, mode);
            var positiveEvaluation = GaussianLikelihoodEvaluator.Evaluate(positive, mode);
            var combined = GaussianLikelihoodEvaluator.Combine(new[] { emptyEvaluation, zeroEvaluation, positiveEvaluation });

            Assert.False(emptyEvaluation.IsLikelihoodAvailable);
            Assert.False(zeroEvaluation.IsLikelihoodAvailable);
            Assert.True(zeroEvaluation.HasFiniteResidualStatistics);
            Assert.True(combined.IsLikelihoodAvailable);
            Assert.Equal(2, combined.ObservationCount);
            Assert.Equal(4e-12, combined.RawResidualSumOfSquares, 15);
            Assert.Equal(2 * (Math.Log(2 * Math.PI * 2e-12) + 1), combined.MinusTwoLogLikelihood, 12);
        }

        [Fact]
        public void EstimatedWeightedLikelihoodMatchesProductOfGaussianDensitiesAtVarianceMaximum()
        {
            var model = CreateProbe(
                new ResidualSpec(true, 1e-6, 1e-6),
                new ResidualSpec(true, -4e-6, 2e-6),
                new ResidualSpec(true, 6e-6, 3e-6),
                new ResidualSpec(false, 100e-6, 1e-9));

            // Standardized residuals are 1, -2, 2. Their mean square is 3,
            // so the maximizing observation variances are 3, 12, 27 (µJ²).
            var densityProduct = GaussianDensity(1e-6, 3e-12)
                * GaussianDensity(-4e-6, 12e-12)
                * GaussianDensity(6e-6, 27e-12);
            var evaluation = GaussianLikelihoodEvaluator.Evaluate(model, GaussianLikelihoodMode.EstimatedWeightedVariance);

            Assert.True(evaluation.IsLikelihoodAvailable);
            Assert.Equal(3, evaluation.ObservationCount);
            Assert.Equal(9, evaluation.StandardizedResidualSumOfSquares, 12);
            Assert.Equal(-2 * Math.Log(densityProduct), evaluation.MinusTwoLogLikelihood, 12);
            Assert.Equal(Math.Sqrt(53.0 / 3), evaluation.RmsdMicrojoules, 12);
        }

        [Fact]
        public void EstimatedWeightedLikelihoodWithEqualSigmasMatchesUnweightedLikelihood()
        {
            var model = CreateProbe(
                new ResidualSpec(true, 1e-6, 7e-6),
                new ResidualSpec(true, -3e-6, 7e-6),
                new ResidualSpec(true, 5e-6, 7e-6));
            var unweighted = GaussianLikelihoodEvaluator.Evaluate(model, GaussianLikelihoodMode.EstimatedCommonVariance);
            var weighted = GaussianLikelihoodEvaluator.Evaluate(model, GaussianLikelihoodMode.EstimatedWeightedVariance);

            Assert.Equal(unweighted.MinusTwoLogLikelihood, weighted.MinusTwoLogLikelihood, 12);
            Assert.Equal(unweighted.RmsdMicrojoules, weighted.RmsdMicrojoules);
        }

        [Theory]
        [InlineData(0.01)]
        [InlineData(100.0)]
        public void EstimatedWeightedLikelihoodIsInvariantToCommonSigmaMultiplier(double multiplier)
        {
            var original = GaussianLikelihoodEvaluator.Evaluate(CreateProbe(
                new ResidualSpec(true, 1e-6, 1e-6),
                new ResidualSpec(true, -3e-6, 2e-6)), GaussianLikelihoodMode.EstimatedWeightedVariance);
            var rescaled = GaussianLikelihoodEvaluator.Evaluate(CreateProbe(
                new ResidualSpec(true, 1e-6, multiplier * 1e-6),
                new ResidualSpec(true, -3e-6, multiplier * 2e-6)), GaussianLikelihoodMode.EstimatedWeightedVariance);

            Assert.Equal(original.MinusTwoLogLikelihood, rescaled.MinusTwoLogLikelihood, 12);
            Assert.Equal(original.RmsdMicrojoules, rescaled.RmsdMicrojoules);
        }

        [Fact]
        public void EstimatedWeightedGlobalLikelihoodEstimatesVarianceOnceAfterPooling()
        {
            var first = CreateProbe(new ResidualSpec(true, 1e-6, 1e-6));
            var second = CreateProbe(new ResidualSpec(true, -6e-6, 2e-6));
            var firstEvaluation = GaussianLikelihoodEvaluator.Evaluate(first, GaussianLikelihoodMode.EstimatedWeightedVariance);
            var secondEvaluation = GaussianLikelihoodEvaluator.Evaluate(second, GaussianLikelihoodMode.EstimatedWeightedVariance);
            var evaluation = GaussianLikelihoodEvaluator.Evaluate(CreateGlobal(first, second), GaussianLikelihoodMode.EstimatedWeightedVariance);

            // Q = 1 + 9: one pooled multiplier of 5 gives variances 5 and 20 µJ².
            var densityProduct = GaussianDensity(1e-6, 5e-12) * GaussianDensity(-6e-6, 20e-12);
            Assert.Equal(-2 * Math.Log(densityProduct), evaluation.MinusTwoLogLikelihood, 12);
            Assert.NotEqual(firstEvaluation.MinusTwoLogLikelihood + secondEvaluation.MinusTwoLogLikelihood,
                evaluation.MinusTwoLogLikelihood);
        }

        [Fact]
        public void EstimatedWeightedStatisticsOverflowMakesLikelihoodUnavailable()
        {
            var evaluation = GaussianLikelihoodEvaluator.Evaluate(
                CreateProbe(new ResidualSpec(true, 1, 1e-200)), GaussianLikelihoodMode.EstimatedWeightedVariance);

            Assert.False(evaluation.IsLikelihoodAvailable);
            Assert.Equal(GaussianLikelihoodEvaluator.NonFiniteWeightedStatisticsReason, evaluation.UnavailableReason);
        }

        static double GaussianDensity(double residual, double variance)
            => Math.Exp(-residual * residual / (2 * variance)) / Math.Sqrt(2 * Math.PI * variance);

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

        [Theory]
        [InlineData(GaussianLikelihoodMode.EstimatedCommonVariance)]
        [InlineData(GaussianLikelihoodMode.EstimatedWeightedVariance)]
        public void InvalidResidualsAndNoObservationsHaveStableDiagnostics(GaussianLikelihoodMode mode)
        {
            var empty = GaussianLikelihoodEvaluator.Evaluate(
                CreateProbe(new ResidualSpec(false, 0, 0)),
                mode);
            var invalid = GaussianLikelihoodEvaluator.Evaluate(
                CreateProbe(new ResidualSpec(true, double.NaN, 1e-6)),
                mode);

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
            var estimatedWeighted = GaussianLikelihoodEvaluator.Evaluate(model, GaussianLikelihoodMode.EstimatedWeightedVariance);

            Assert.Throws<ArgumentNullException>(() => GaussianLikelihoodEvaluator.Combine(null));
            Assert.Throws<ArgumentNullException>(() => GaussianLikelihoodEvaluator.Combine(new GaussianLikelihoodEvaluation[] { estimated, null }));
            Assert.Throws<ArgumentException>(() => GaussianLikelihoodEvaluator.Combine(new[] { estimated, known }));
            Assert.Throws<ArgumentException>(() => GaussianLikelihoodEvaluator.Combine(new[] { estimatedWeighted, known }));
            Assert.Throws<ArgumentException>(() => GaussianLikelihoodEvaluator.Combine(new[] { estimatedWeighted, estimated }));
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

        [Theory]
        [InlineData(GaussianLikelihoodMode.KnownObservationSigmas)]
        [InlineData(GaussianLikelihoodMode.EstimatedWeightedVariance)]
        public void EvaluationDoesNotChangeModelState(GaussianLikelihoodMode mode)
        {
            var model = CreateProbe(new ResidualSpec(true, 1e-6, 1e-6));
            var parameter = model.Parameters.Table[ParameterType.Enthalpy1];
            var value = parameter.Value;
            var include = model.Data.Injections[0].Include;
            var sigma = model.Data.Injections[0].PeakArea.SD;

            GaussianLikelihoodEvaluator.Evaluate(model, mode);

            Assert.Equal(value, model.Parameters.Table[ParameterType.Enthalpy1].Value);
            Assert.Equal(include, model.Data.Injections[0].Include);
            Assert.Equal(sigma, model.Data.Injections[0].PeakArea.SD);
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
                var volume = spec.InjectionMass / data.SyringeConcentration.Value;
                var injection = new InjectionData(data, index, volume, spec.InjectionMass, spec.Include)
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
            public double InjectionMass { get; }

            public ResidualSpec(
                bool include,
                double residual,
                double sigma,
                double injectionMass = 2e-10)
            {
                Include = include;
                Residual = residual;
                Sigma = sigma;
                InjectionMass = injectionMass;
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
