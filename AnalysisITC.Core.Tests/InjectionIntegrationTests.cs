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

    private static InjectionData Integrate(List<DataPoint> samples, float endTime)
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
        injection.SetIntegrationLengthByTime(endTime);

        injection.Integrate();

        return injection;
    }
}
