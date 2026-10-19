using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests;

public class MinVolatilityBaselineTests
{
    [Theory]
    [InlineData(1.25e-5f)]
    [InlineData(-1.25e-5f)]
    [InlineData(0f)]
    public void SingletonAverageReturnsSamplePower(float power)
    {
        Assert.Equal((double)power, DataPoint.VolatilityWeightedAverage(new List<DataPoint> { new(174, power) }));
    }

    [Fact]
    public void MultipleSamplesRetainVolatilityWeighting()
    {
        // Successive differences are 1 and 2: weights are .01/1.005 and .01/2.005.
        // Their weighted mean of powers 2 and 4 is 8.03/3.01.
        var points = new List<DataPoint> { new(0, 1), new(1, 2), new(2, 4) };
        Assert.Equal(8.03 / 3.01, DataPoint.VolatilityWeightedAverage(points), 12);
    }

    [Fact]
    public void EmptyAverageRemainsUndefined()
    {
        Assert.True(double.IsNaN(DataPoint.VolatilityWeightedAverage(new List<DataPoint>())));
    }

    [Theory]
    [InlineData(SplineInterpolator.SplineInterpolatorAlgorithm.Linear)]
    [InlineData(SplineInterpolator.SplineInterpolatorAlgorithm.Smooth)]
    public async Task ExtendingIntegrationToFullDelayPreservesFiniteProcessing(SplineInterpolator.SplineInterpolatorAlgorithm algorithm)
    {
        const float baseline = 1e-5f;
        var experiment = new ExperimentData("min-volatility.itc") { InitialDelay = 40 };
        for (int time = 0; time <= 220; time++)
            experiment.DataPoints.Add(new DataPoint(time, baseline + (time >= 45 && time <= 49 ? 2e-6f : 0), 25));

        var injection = InjectionData.FromPEAQFile(experiment, 0, include: true, time: 40,
            volume: 2e-6, delay: 180, duration: 2, temperature: 25);
        experiment.Injections.Add(injection);
        injection.SetIntegrationLengthByTime(60);
        experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Spline);
        experiment.Processor.DiscardIntegratedPoints = true;
        var spline = Assert.IsType<SplineInterpolator>(experiment.Processor.Interpolator);
        spline.Algorithm = algorithm;
        spline.PointDensity = SplineInterpolator.SplinePointDensity.Balanced;
        spline.HandleMode = SplineInterpolator.SplineHandleMode.MinVolatility;
        Assert.False(experiment.Processor.IsLocked);

        await experiment.Processor.ProcessData(showProgress: false);
        Assert.True(experiment.Processor.IntegrationCompleted);
        Assert.All(spline.Baseline, value => Assert.True(double.IsFinite(value.Value)));

        injection.SetIntegrationLengthByTime(180);
        Assert.Equal(220f, injection.IntegrationEndTime);
        // The final window is [214, 215], entirely inside the integration region.
        // Existing fallback deliberately uses the raw sample at 214.
        Assert.Empty(spline.GetInterpolatedDataPoints(214, 215));
        var fallback = spline.GetInitialPoints().Last();
        Assert.Equal(214.5, fallback.Time);
        Assert.Equal((double)baseline, fallback.Power);

        await experiment.Processor.ProcessData(showProgress: false);
        Assert.True(experiment.Processor.BaselineCompleted);
        Assert.Equal(214.5, spline.SplinePoints.Last().Time);
        Assert.Equal((double)baseline, spline.SplinePoints.Last().Power);
        Assert.All(spline.SplinePoints, point => Assert.True(double.IsFinite(point.Power)));
        Assert.Equal(experiment.DataPoints.Count, spline.Baseline.Count);
        Assert.All(spline.Baseline, value => Assert.True(double.IsFinite(value.Value)));
        Assert.Equal(experiment.DataPoints.Count, experiment.BaseLineCorrectedDataPoints.Count);
        Assert.All(experiment.BaseLineCorrectedDataPoints, point => Assert.True(float.IsFinite(point.Power)));
        Assert.True(experiment.Processor.IntegrationCompleted);
        Assert.True(double.IsFinite(injection.RawPeakArea.Value));
        // Five one-second intervals carry an extra 2 microjoules/second each.
        Assert.InRange(injection.RawPeakArea.Value, 1e-5 - 1e-10, 1e-5 + 1e-10);
    }
}
