using System.Collections.Generic;
using System.Linq;

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
    public void Integrate_HandlesWindowInsideOneIrregularSamplePeriod()
    {
        var samples = Enumerable.Range(-2, 23)
            .Where(time => time is not 3 and not 4)
            .Select(time => new DataPoint(time, time == 5 ? 3 : 100))
            .ToList();

        var injection = Integrate(samples, endTime: 4.8f, startDelay: 2.5f);

        Assert.Equal(6.9, injection.RawPeakArea.Value, precision: 5);
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
