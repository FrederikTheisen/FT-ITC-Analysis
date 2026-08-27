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
    }
}
