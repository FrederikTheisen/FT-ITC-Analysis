using System;
using System.Collections.Generic;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class BaselineAutoCorrelationTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(1000)]
    public void TwoSegmentsNormalizeOnlyEligiblePairs(float secondStart)
    {
        var points = new List<DataPoint>
        {
            new(0, 1), new(1, 2), new(2, 1),
            new(secondStart, -2), new(secondStart + 1, -1), new(secondStart + 2, -2),
        };

        // Four eligible products: 2 + 2 + 2 + 2 = 8.
        // Their destination squares: 4 + 1 + 1 + 4 = 10.
        // The cross-gap destination square (4) must not enter either sum.
        Assert.Equal(0.8, Statistics.EstimateAutoCorrelation(points, 2), 12);
    }

    [Fact]
    public void ContiguousPairsIncludeTheTimeGapThreshold()
    {
        var points = new List<DataPoint> { new(0, 1), new(2, 2), new(4, 1) };

        Assert.Equal(4.0 / 5.0, Statistics.EstimateAutoCorrelation(points, 2), 12);
    }

    [Fact]
    public void NoEligiblePowerReturnsZero()
    {
        Assert.Equal(0, Statistics.EstimateAutoCorrelation(new List<DataPoint>(), 2));
        Assert.Equal(0, Statistics.EstimateAutoCorrelation(new List<DataPoint> { new(0, 1) }, 2));
        Assert.Equal(0, Statistics.EstimateAutoCorrelation(new List<DataPoint> { new(0, 1), new(10, 2) }, 2));
        Assert.Equal(0, Statistics.EstimateAutoCorrelation(new List<DataPoint> { new(0, 0), new(1, 0) }, 2));
    }

    [Theory]
    [InlineData(1, 1, 0.85)]
    [InlineData(1, -1, -0.999)]
    [InlineData(1, -2, -0.5)]
    public void ExistingCorrelationLimitsArePreserved(float first, float second, double expected)
    {
        var points = new List<DataPoint> { new(0, first), new(1, second) };

        Assert.Equal(expected, Statistics.EstimateAutoCorrelation(points, 2), 12);
    }

    [Fact]
    public void IntegratedHeatSdUsesWithinSegmentCorrelationAndSuppliesFitWeight()
    {
        var points = new List<DataPoint>
        {
            new(0, 1), new(10, 2),
            new(20, 3), new(30, 3), new(40, 3),
            new(50, -1), new(60, -2),
        };
        var experiment = new ExperimentData("two-segment-baseline")
        {
            DataPoints = points,
            BaseLineCorrectedDataPoints = points,
        };
        var injection = new InjectionData(experiment, volume: 1e-6f, delay: 50, filter: 0, duration: 1)
        {
            Time = 10,
        };
        experiment.Injections.Add(injection);
        injection.SetIntegrationLengthByTime(40);

        injection.Integrate();

        // Baseline samples [1, 2] and [-1, -2] give r = 4/8 = 1/2,
        // power variance = (1 + 4 + 1 + 4)/(4 - 1) = 10/3, with no clipping.
        // Independently summing the AR(1) covariance matrices gives:
        // three interior samples: 3 + 2*(1/2 + 1/2 + 1/4) = 5.5;
        // each two-sample baseline segment: 2 + 2*(1/2) = 3.
        // dt = 10, integration length = 40, baseline sample count = 4.
        var expectedSd = Math.Sqrt((10.0 / 3.0) * (100 * 5.5 + 1600.0 * 6 / 16));
        Assert.Equal(80.0, injection.RawPeakArea.Value, 12);
        Assert.Equal(expectedSd, injection.RawPeakArea.SD, 12);
        Assert.Equal(expectedSd, injection.PeakAreaError, 12);
        Assert.Equal(expectedSd, Model.GetSigmaForWeighting(injection, experiment.Injections), 12);
    }
}
