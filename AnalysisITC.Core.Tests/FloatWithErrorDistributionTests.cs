using System;
using System.Collections.Generic;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class FloatWithErrorDistributionTests
    {
        [Fact]
        public void NumericDistributionWithoutReferenceUsesSampleStandardDeviation()
        {
            var result = new FloatWithError(new[] { 1.0, 2.0, 4.0 });

            Assert.Equal(7.0 / 3.0, result.Value, 12);
            Assert.Equal(Math.Sqrt(7.0 / 3.0), result.SD, 12);
            Assert.Equal(1.0, result.Lower);
            Assert.Equal(4.0, result.Upper);
        }

        [Fact]
        public void NumericDistributionWithReferenceUsesReferenceRmsDeviation()
        {
            var result = new FloatWithError(new[] { 0.0, 2.0, 10.0 }, 100.0);

            Assert.Equal(100.0, result.Value);
            Assert.Equal(Math.Sqrt(27704.0 / 3.0), result.SD, 12);
            Assert.Equal(0.0, result.Lower);
            Assert.Equal(10.0, result.Upper);
        }

        [Fact]
        public void EmptyAndSingleNumericDistributionsHaveZeroStandardDeviation()
        {
            var emptyWithoutReference = new FloatWithError(Array.Empty<double>());
            var singleWithoutReference = new FloatWithError(new[] { 3.0 });
            var empty = new FloatWithError(Array.Empty<double>(), 100.0);
            var single = new FloatWithError(new[] { 3.0 }, 100.0);

            Assert.Equal(0.0, emptyWithoutReference.Value);
            Assert.Equal(0.0, emptyWithoutReference.SD);
            Assert.Equal(3.0, singleWithoutReference.Value);
            Assert.Equal(0.0, singleWithoutReference.SD);
            Assert.Equal(100.0, empty.Value);
            Assert.Equal(0.0, empty.SD);
            Assert.Equal(100.0, single.Value);
            Assert.Equal(0.0, single.SD);
            Assert.Equal(3.0, single.Lower);
            Assert.Equal(3.0, single.Upper);
        }

        [Fact]
        public void MonteCarloDistributionUsesGeneratedSampleCountForBothDenominators()
        {
            var distribution = new List<FloatWithError>
            {
                new(0.0),
                new(2.0),
            };

            var sampleStandardDeviation = new FloatWithError(distribution);
            var fixedReferenceRms = new FloatWithError(distribution, 10.0);

            Assert.Equal(1.0, sampleStandardDeviation.Value, 12);
            Assert.Equal(Math.Sqrt(400.0 / 399.0), sampleStandardDeviation.SD, 12);
            Assert.Equal(10.0, fixedReferenceRms.Value);
            Assert.Equal(Math.Sqrt(32800.0 / 400.0), fixedReferenceRms.SD, 12);
        }

        [Fact]
        public void PercentilesAndAutomaticFormattingRemainBasedOnTheDistribution()
        {
            var result = new FloatWithError(new[] { 8.0, 9.0, 10.0, 11.0, 50.0 }, 10.0);

            Assert.Equal(8.0, result.Lower);
            Assert.Equal(50.0, result.Upper);
            Assert.True(result.IsAsymmetric);
            Assert.Contains("[", result.AsNumber(UncertaintyDisplayStyle.Automatic));
            Assert.DoesNotContain("±", result.AsNumber(UncertaintyDisplayStyle.Automatic));
        }

        [Fact]
        public void StronglyAsymmetricCiUsesStoredBoundsAsEqualTailLimits()
        {
            var value = new FloatWithError(10.0, 0.01, 8.0, 50.0);
            var random = new Random(841);
            const int sampleCount = 200_000;
            var belowLower = 0;
            var aboveUpper = 0;

            for (var index = 0; index < sampleCount; index++)
            {
                var sample = value.Sample(random);
                if (sample <= value.Lower) belowLower++;
                if (sample >= value.Upper) aboveUpper++;
            }

            Assert.InRange((double)belowLower / sampleCount, 0.023, 0.027);
            Assert.InRange((double)aboveUpper / sampleCount, 0.023, 0.027);
        }

        [Fact]
        public void BelowDisplayThresholdCiStillControlsSampling()
        {
            var value = new FloatWithError(10.0, 0.01, 8.0, 12.5);
            var random = new Random(1729);
            var farFromSdRange = false;

            Assert.False(value.IsAsymmetric);

            for (var index = 0; index < 100; index++)
                farFromSdRange |= Math.Abs(value.Sample(random) - value.Value) > 0.1;

            Assert.True(farFromSdRange);
        }

        [Fact]
        public void SymmetricDefaultCiIsNormalCompatible()
        {
            var value = new FloatWithError(5.0, 2.0);
            var expectedRandom = new Random(1729);
            var actualRandom = new Random(1729);

            for (var index = 0; index < 1_000; index++)
                Assert.Equal(
                    Distribution.Normal(value.Value, value.SD, expectedRandom),
                    value.Sample(actualRandom));
        }

        [Theory]
        [InlineData(10.0, 8.0, 18.0)]
        [InlineData(10.0, 2.0, 12.0)]
        [InlineData(0.1, -1.5, 0.8)]
        public void AsymmetricCiHasContinuousDensityAtValue(
            double valueNumber, double lower, double upper)
        {
            var value = new FloatWithError(valueNumber, 0.01, lower, upper);
            var random = new Random(841);
            var nearModeWidth = Math.Min(value.LowerWidth, value.UpperWidth) * 0.005;
            const int sampleCount = 300_000;
            var immediatelyBelow = 0;
            var immediatelyAbove = 0;

            for (var index = 0; index < sampleCount; index++)
            {
                var sample = value.Sample(random);
                if (sample >= value.Value - nearModeWidth && sample < value.Value)
                    immediatelyBelow++;
                if (sample > value.Value && sample <= value.Value + nearModeWidth)
                    immediatelyAbove++;
            }

            Assert.InRange((double)immediatelyBelow / immediatelyAbove, 0.85, 1.15);
        }

        [Fact]
        public void InvalidCiFallsBackToSdBasedNormalSampling()
        {
            var absent = default(FloatWithError);
            var invalid = new FloatWithError(10.0, 2.0, 10.0, 16.0);
            var nonEnclosing = new FloatWithError(10.0, 2.0, 12.0, 16.0);
            var nonFinite = new FloatWithError(10.0, 2.0, double.NaN, 16.0);

            Assert.False(invalid.IsAsymmetric);
            Assert.False(nonEnclosing.IsAsymmetric);
            Assert.False(nonFinite.IsAsymmetric);

            const int seed = 1729;
            Assert.Equal(
                Distribution.Normal(absent.Value, absent.SD, new Random(seed)),
                absent.Sample(new Random(seed)));
            Assert.Equal(
                Distribution.Normal(invalid.Value, invalid.SD, new Random(seed)),
                invalid.Sample(new Random(seed)));
            Assert.Equal(
                Distribution.Normal(nonEnclosing.Value, nonEnclosing.SD, new Random(seed)),
                nonEnclosing.Sample(new Random(seed)));
            Assert.Equal(
                Distribution.Normal(nonFinite.Value, nonFinite.SD, new Random(seed)),
                nonFinite.Sample(new Random(seed)));
        }

        [Fact]
        public void SamplingDoesNotMutateValue()
        {
            var value = new FloatWithError(10.0, 0.01, 8.0, 50.0);
            var originalValue = value.Value;
            var random = new Random(331);

            for (var index = 0; index < 100; index++)
                _ = value.Sample(random);

            Assert.Equal(originalValue, value.Value);
        }
    }
}
