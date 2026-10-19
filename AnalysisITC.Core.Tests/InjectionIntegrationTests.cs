using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;

using Xunit;

namespace AnalysisITC.Core.Tests;

public class InjectionIntegrationTests
{
    [Theory]
    [InlineData(1.0, 9.0, PeakHeatDirection.Endothermal)]
    [InlineData(-1.0, -9.0, PeakHeatDirection.Exothermal)]
    public void Integrate_UsesRightEndpointsAndPreservesSignalSign(
        double sign,
        double expectedHeat,
        PeakHeatDirection expectedDirection)
    {
        var samples = Enumerable.Range(0, 21)
            .Select(time => new DataPoint(time, (float)(sign * (time switch
            {
                0 => 100,
                1 => 2,
                2 => 3,
                3 => 4,
                _ => 0,
            }))))
            .ToList();

        var injection = Integrate(samples, endTime: 3);

        Assert.Equal(expectedHeat, injection.RawPeakArea.Value, precision: 12);
        Assert.Equal(expectedDirection, injection.HeatDirection);
    }

    [Fact]
    public void Integrate_UsesActualTimestampDifferencesForIrregularSamples()
    {
        var samples = new List<DataPoint>
        {
            new(0, 100),
            new(1, 2),
            new(3, 4),
            new(6, 5),
        };
        samples.AddRange(Enumerable.Range(7, 14).Select(time => new DataPoint(time, 0)));

        var injection = Integrate(samples, endTime: 6);

        Assert.Equal(25, injection.RawPeakArea.Value, precision: 12);
    }

    [Fact]
    public void Integrate_IncludingZeroValuedEndSampleDoesNotChangePeakHeat()
    {
        var samples = Enumerable.Range(0, 21)
            .Select(time => new DataPoint(time, time switch
            {
                0 => 100,
                1 => 2,
                2 => 3,
                _ => 0,
            }))
            .ToList();

        var injection = Integrate(samples, endTime: 3);

        Assert.Equal(5, injection.RawPeakArea.Value, precision: 12);
    }

    [Theory]
    [InlineData(2.0, 5.0)]
    [InlineData(-2.0, -5.0)]
    public void Integrate_IncludesPartialFinalSamplePeriod(double power, double expectedHeat)
    {
        var samples = Enumerable.Range(0, 21)
            .Select(time => new DataPoint(time, (float)power))
            .ToList();

        var injection = Integrate(samples, endTime: 2.5f);

        Assert.Equal(expectedHeat, injection.RawPeakArea.Value, precision: 12);
    }

    [Fact]
    public void Integrate_UsesFollowingRightEndpointForNonconstantPartialFinalPeriod()
    {
        var samples = Enumerable.Range(0, 21)
            .Select(time => new DataPoint(time, time switch
            {
                0 => 100,
                1 => 2,
                2 => 3,
                3 => 8,
                _ => 0,
            }))
            .ToList();

        var injection = Integrate(samples, endTime: 2.5f);

        Assert.Equal(9, injection.RawPeakArea.Value, precision: 12);
    }

    [Fact]
    public void Integrate_UsesFollowingRightEndpointForIrregularPartialPeriod()
    {
        var samples = new List<DataPoint>
        {
            new(0, 100),
            new(1, 2),
            new(3, 4),
            new(6, 8),
        };

        var injection = Integrate(samples, endTime: 4);

        Assert.Equal(18, injection.RawPeakArea.Value, precision: 12);
    }

    [Fact]
    public void Integrate_HandlesPartialStartAndDoesNotExtrapolatePastData()
    {
        var samples = Enumerable.Range(-2, 23)
            .Select(time => new DataPoint(time, time switch
            {
                -1 or 0 => 100,
                1 => 2,
                2 => 3,
                3 => 4,
                _ => 0,
            }))
            .ToList();

        var injection = Integrate(samples, endTime: 2.5f, startDelay: 0.5f);

        Assert.Equal(6, injection.RawPeakArea.Value, precision: 12);
    }

    [Fact]
    public void Integrate_ExactEndPointDoesNotAddAnotherPeriod()
    {
        var samples = Enumerable.Range(0, 4)
            .Select(time => new DataPoint(time, time + 1))
            .ToList();

        var injection = Integrate(samples, endTime: 2);

        Assert.Equal(5, injection.RawPeakArea.Value, precision: 12);
    }

    [Fact]
    public void Integrate_DoesNotExtrapolatePastLastDataPoint()
    {
        var samples = new List<DataPoint>
        {
            new(0, 100),
            new(1, 2),
            new(2, 3),
        };

        var injection = Integrate(samples, endTime: 3);

        Assert.Equal(5, injection.RawPeakArea.Value, precision: 12);
    }

    [Fact]
    public void Integrate_CompletesWithNoBaselineSamples()
    {
        var samples = new List<DataPoint>
        {
            new(3, 3),
            new(4, 4),
        };

        var injection = Integrate(samples, endTime: 5);

        Assert.True(injection.IsIntegrated);
        Assert.Equal(4, injection.RawPeakArea.Value, precision: 12);
        Assert.Equal(0, injection.RawPeakArea.SD);
    }

    [Fact]
    public void Integrate_CompletesWithOneBaselineSample()
    {
        var samples = new List<DataPoint>
        {
            new(0, 100),
            new(1, 3),
            new(2, 4),
        };

        var injection = Integrate(samples, endTime: 5);

        Assert.True(injection.IsIntegrated);
        Assert.Equal(7, injection.RawPeakArea.Value, precision: 12);
        Assert.Equal(0, injection.RawPeakArea.SD);
    }

    [Fact]
    public void Integrate_HandlesWindowInsideOneIrregularSamplePeriod()
    {
        var samples = Enumerable.Range(-2, 23)
            .Where(time => time is not 3 and not 4)
            .Select(time => new DataPoint(time, time == 5 ? 3 : 100))
            .ToList();

        var injection = Integrate(samples, endTime: 4.8f, startDelay: 2.5f);

        Assert.Equal(6.9, injection.RawPeakArea.Value, precision: 5);
    }

    [Fact]
    public void Integrate_PreservesFloatIntervalProductsBitForBit()
    {
        var samples = Enumerable.Range(0, 21)
            .Select(time => new DataPoint(time, time switch
            {
                1 => 0.1f,
                2 => 0.2f,
                3 => 0.3f,
                _ => 0,
            }))
            .ToList();

        var injection = Integrate(samples, endTime: 2.3f);

        // The pre-change float products accumulate to 0.38999999314546585.
        // Widening the final duration before multiplication instead gives
        // 0.38999999374151173, a different double bit pattern.
        Assert.Equal(0x3fd8f5c288000000L, BitConverter.DoubleToInt64Bits(injection.RawPeakArea.Value));
    }

    [Theory]
    [InlineData(0, 40, 80, 825)]
    [InlineData(5, 35, 70, 506.25)]
    public void Integrate_PropagatesNoiseThroughAlignedAndFractionalIntervals(
        float startDelay, float endOffset, double expectedHeat, double integrationVarianceFactor)
    {
        var samples = new List<DataPoint>
        {
            new(-10, 0),
            new(0, 1), new(10, 2),
            new(20, 3), new(30, 3), new(40, 3),
            new(50, -1), new(60, -2),
        };

        var injection = IntegrateWithKnownBaseline(samples, startDelay, endOffset);

        // Baseline [1, 2] and [-1, -2]: variance 10/3, r = 1/2,
        // baseline covariance sum 3 + 3 = 6. Integration durations are
        // [10, 10, 10, 10] or [5, 10, 10, 5], respectively.
        var length = endOffset - startDelay;
        var expectedSd = Math.Sqrt((10.0 / 3) * (integrationVarianceFactor + length * length * 6.0 / 16));

        Assert.Equal(expectedHeat, injection.RawPeakArea.Value, 12);
        AssertHeatSdAndFitWeight(injection, expectedSd);
    }

    [Fact]
    public void Integrate_PropagatesNoiseUsingIrregularIntervalDurations()
    {
        var samples = new List<DataPoint>
        {
            new(0, 1), new(10, 2),
            new(20, 3), new(35, 3),
            new(50, -1), new(60, -2),
        };

        var injection = IntegrateWithKnownBaseline(samples, startDelay: 0, endOffset: 40);

        // Durations [10, 15, 15] and r = 1/2 give
        // 100 + 225 + 225 + 2*(75 + 37.5 + 112.5) = 1000.
        var expectedSd = Math.Sqrt((10.0 / 3) * (1000 + 1600.0 * 6 / 16));
        Assert.Equal(60, injection.RawPeakArea.Value, 12);
        AssertHeatSdAndFitWeight(injection, expectedSd);
    }

    [Fact]
    public void Integrate_EstimatesNoiseForWindowInsideOneSampleInterval()
    {
        // Earlier samples are outside the baseline window, but retain a
        // sampling interval compatible with this valid 30-second region.
        var samples = Enumerable.Range(-10, 10)
            .Select(index => new DataPoint(5 * index, 0))
            .ToList();
        samples.AddRange(new[]
        {
            new DataPoint(0, 1), new DataPoint(10, 2),
            new DataPoint(50, -1), new DataPoint(60, -2),
        });

        var injection = IntegrateWithKnownBaseline(samples, startDelay: 5, endOffset: 35);

        // One contributing sample has duration 30, so its variance multiplier
        // is 900 regardless of correlation. The baseline remains [1,2],[-1,-2].
        var expectedSd = Math.Sqrt((10.0 / 3) * (900 + 900.0 * 6 / 16));
        Assert.Equal(-30, injection.RawPeakArea.Value, 12);
        AssertHeatSdAndFitWeight(injection, expectedSd);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Integrate_ShortWindowSmallPeakHasNonzeroSdAndUnknownDirection(float sign)
    {
        const float noiseScale = 1f / 1048576;
        var samples = new List<DataPoint>
        {
            new(0, noiseScale), new(10, 2 * noiseScale),
            new(20, sign * noiseScale / 8), new(30, 0),
            new(40, -noiseScale), new(50, -2 * noiseScale),
        };

        var injection = IntegrateWithKnownBaseline(samples, startDelay: 0, endOffset: 20, delay: 40);

        // Baseline [1,2,0,-1,-2] times noiseScale gives variance 10/4,
        // r = (2+0+0+2)/(4+0+1+4) = 4/9, and segment counts 2 and 3.
        // The two integrated samples have durations [10,10]. Previously the
        // single strict-interior timestamp forced SD=0 and a signed direction.
        const double r = 4.0 / 9;
        var integrationFactor = 200 * (1 + r);
        var baselineFactor = 400.0 * (5 + 6 * r + 2 * r * r) / 25;
        var expectedSd = noiseScale * Math.Sqrt(2.5 * (integrationFactor + baselineFactor));

        Assert.Equal(sign * 1.25 * noiseScale, injection.RawPeakArea.Value, 12);
        AssertHeatSdAndFitWeight(injection, expectedSd);
        Assert.True(Math.Abs(injection.RawPeakArea.Value) < 3 * expectedSd);
        Assert.Equal(PeakHeatDirection.Unknown, injection.HeatDirection);
    }

    [Fact]
    public void Integrate_NoOverlappingIntervalsHasZeroHeatAndSd()
    {
        var samples = new List<DataPoint> { new(0, 1), new(1, 2) };

        var injection = Integrate(samples, endTime: 12, startDelay: 10);

        Assert.True(injection.IsIntegrated);
        Assert.Equal(0, injection.RawPeakArea.Value);
        Assert.Equal(0, injection.RawPeakArea.SD);
        Assert.Equal(PeakHeatDirection.Unknown, injection.HeatDirection);
    }

    private static InjectionData IntegrateWithKnownBaseline(
        List<DataPoint> samples, float startDelay, float endOffset, float delay = 50)
    {
        var experiment = new ExperimentData("integration-noise-test.itc")
        {
            DataPoints = samples,
            BaseLineCorrectedDataPoints = samples,
        };
        var injection = InjectionData.FromPEAQFile(
            experiment, id: 0, include: true, time: 10, volume: 1e-6,
            delay: delay, duration: 1, temperature: 25);
        experiment.Injections.Add(injection);
        injection.InitializeIntegrationTimes();
        if (startDelay != 0) injection.SetIntegrationStartTime(startDelay);
        injection.SetIntegrationLengthByTime(endOffset);
        injection.Integrate();
        return injection;
    }

    private static void AssertHeatSdAndFitWeight(InjectionData injection, double expectedSd)
    {
        Assert.True(double.IsFinite(injection.RawPeakArea.SD));
        Assert.True(injection.RawPeakArea.SD > 0);
        Assert.Equal(expectedSd, injection.RawPeakArea.SD, 12);
        Assert.Equal(expectedSd, injection.PeakAreaError, 12);
        Assert.Equal(expectedSd, Model.GetSigmaForWeighting(injection, injection.Experiment.Injections), 12);
    }

    private static InjectionData Integrate(List<DataPoint> samples, float endTime, float startDelay = 0)
    {
        var experiment = new ExperimentData("integration-test.itc")
        {
            DataPoints = samples,
            BaseLineCorrectedDataPoints = samples,
        };
        var injection = InjectionData.FromPEAQFile(
            experiment,
            id: 0,
            include: true,
            time: 0,
            volume: 1e-6f,
            delay: 20,
            duration: 1,
            temperature: 25);
        experiment.Injections.Add(injection);
        injection.InitializeIntegrationTimes();
        if (startDelay != 0) injection.SetIntegrationStartTime(startDelay);
        injection.SetIntegrationLengthByTime(endTime);

        injection.Integrate();

        return injection;
    }
}
