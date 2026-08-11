using System;
using System.Linq;

using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public class LinearFitEnvelopeTests
    {
        [Fact]
        public void UsesDeterministicCenterAndBootstrapPercentiles()
        {
            var fit = new LinearFitWithError(2, 10, 0);
            var bootstrap = new[]
            {
                new LinearFitWithError(2, 0, 0),
                new LinearFitWithError(2, 10, 0),
                new LinearFitWithError(2, 20, 0),
                new LinearFitWithError(2, 30, 0),
            };

            var point = Assert.Single(LinearFitEnvelopeBuilder.Build(fit, bootstrap, new[] { 2d }));

            Assert.Equal(14, point.Center, 10);
            Assert.Equal(4.75, point.Lower, 10);
            Assert.Equal(33.25, point.Upper, 10);
            Assert.True(point.HasBand);
        }

        [Fact]
        public void FallsBackToSlopeAndInterceptBounds()
        {
            var slope = new FloatWithError(2, 0.5, 1, 3);
            var intercept = new FloatWithError(10, 1, 8, 12);
            var fit = new LinearFitWithError(slope, intercept, 0);

            var point = Assert.Single(LinearFitEnvelopeBuilder.Build(fit, null, new[] { 2d }));

            Assert.Equal(14, point.Center, 10);
            Assert.Equal(10, point.Lower, 10);
            Assert.Equal(18, point.Upper, 10);
            Assert.True(point.HasBand);
        }

        [Fact]
        public void IgnoresInvalidBootstrapFitsBeforeChoosingFallback()
        {
            var fit = new LinearFitWithError(
                new FloatWithError(1, 0.5, 0.5, 1.5),
                new FloatWithError(3, 0.5, 2.5, 3.5),
                0);
            var bootstrap = new[]
            {
                new LinearFitWithError(1, 3, 0),
                new LinearFitWithError(double.NaN, 4, 0),
            };

            var point = Assert.Single(LinearFitEnvelopeBuilder.Build(fit, bootstrap, new[] { 2d }));

            Assert.Equal(3.5, point.Lower, 10);
            Assert.Equal(6.5, point.Upper, 10);
        }

        [Fact]
        public void OmitsZeroWidthBand()
        {
            var point = Assert.Single(LinearFitEnvelopeBuilder.Build(
                new LinearFitWithError(2, 10, 0),
                null,
                new[] { 2d }));

            Assert.Equal(14, point.Center, 10);
            Assert.False(point.HasBand);
            Assert.True(double.IsNaN(point.Lower));
            Assert.True(double.IsNaN(point.Upper));
        }

        [Fact]
        public void SamplesCompleteDomainIncludingBothEdges()
        {
            var samples = LinearFitEnvelopeBuilder.SampleDomain(-2, 8, 4).ToArray();

            Assert.Equal(new[] { -2d, 0.5, 3, 5.5, 8 }, samples);
        }

        [Fact]
        public void RejectsInvalidSampleCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LinearFitEnvelopeBuilder.SampleDomain(0, 1, 0));
        }
    }
}
