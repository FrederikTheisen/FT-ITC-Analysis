using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class TandemMixingScannerTests
    {
        [Theory]
        [InlineData(2, 0.02)]
        [InlineData(3, 0.05)]
        [InlineData(4, 0.10)]
        [InlineData(5, 0.20)]
        public void AdaptiveBroadStepDependsOnExperimentCount(int experimentCount, double expectedStep)
        {
            Assert.Equal(expectedStep, TandemMixingScanner.AdaptiveBroadStepForExperimentCount(experimentCount), 12);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(6)]
        public void AdaptiveBroadStepRejectsUnsupportedExperimentCounts(int experimentCount)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                TandemMixingScanner.AdaptiveBroadStepForExperimentCount(experimentCount));
        }

        [Fact]
        public void TransitionGridEnumeratesFourDimensions()
        {
            var dimensions = Enumerable.Range(0, 4)
                .Select(_ => (IReadOnlyList<double>)new[] { 0.0, 0.5, 1.0 })
                .ToList();

            var points = TandemMixingScanner.EnumerateTransitionFractionGrid(dimensions).ToList();

            Assert.Equal(81, points.Count);
            Assert.Equal(new[] { 0.0, 0.0, 0.0, 0.0 }, points.First());
            Assert.Equal(new[] { 1.0, 1.0, 1.0, 1.0 }, points.Last());
            Assert.All(points, point => Assert.Equal(4, point.Count));
        }
    }
}
