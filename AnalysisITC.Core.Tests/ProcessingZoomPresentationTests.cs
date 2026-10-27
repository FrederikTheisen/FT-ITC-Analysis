using System;
using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class ProcessingZoomPresentationTests
{
    [Theory]
    [InlineData(0.5, false, -3, 23)]
    [InlineData(1, false, 3, 17)]
    [InlineData(2, false, 6, 14)]
    [InlineData(4, false, 7.5, 12.5)]
    [InlineData(0.5, true, -12, 12)]
    [InlineData(1, true, -6, 6)]
    [InlineData(2, true, -3, 3)]
    [InlineData(4, true, -1.5, 1.5)]
    public void ZoomScalesMarginsAndPreservesBaselineDrift(double level, bool corrected, double minimum, double maximum)
    {
        var data = CreateExperiment();
        Assert.True(ProcessingZoomPresentation.TryGetBaselineRange(data, 0, 7, corrected, level, out var min, out var max));
        // Eligible powers 8,10,10,12,10 have mean 10 and nearest extreme 2 µW.
        // The baseline spans 9–11 µW. Default margins are 6 µW on each side.
        Assert.Equal(minimum * 1e-6, min, 11);
        Assert.Equal(maximum * 1e-6, max, 11);
    }

    [Fact]
    public void RangeRespectsVisibleTimesAndDiscardSetting()
    {
        var data = CreateExperiment();
        // Times 5–7 contain powers 10,12,10: mean 10⅔, nearest extreme ⅔ µW.
        Assert.True(ProcessingZoomPresentation.TryGetBaselineRange(data, 5, 7, false, 1, out var min, out var max));
        Assert.Equal(9e-6, min, 11);
        Assert.Equal(13e-6, max, 11);
        data.Processor.DiscardIntegratedPoints = false;
        Assert.True(ProcessingZoomPresentation.TryGetBaselineRange(data, 0, 7, false, 1, out min, out max));
        // All powers average 43.75 µW; the nearest extreme is 8 µW.
        Assert.Equal(-98.25e-6, min, 11);
        Assert.Equal(118.25e-6, max, 11);
    }

    [Fact]
    public void UnavailableOrFlatRangesLeaveTheCallerViewUnchanged()
    {
        Assert.False(ProcessingZoomPresentation.TryGetBaselineRange(null, 0, 7, false, 1, out _, out _));
        var data = CreateExperiment();
        Assert.False(ProcessingZoomPresentation.TryGetBaselineRange(data, 20, 30, false, 1, out _, out _));
        Assert.False(ProcessingZoomPresentation.TryGetBaselineRange(data, 2, 4, false, 1, out _, out _));
        data.Processor.Interpolator.Baseline.RemoveAt(0);
        Assert.False(ProcessingZoomPresentation.TryGetBaselineRange(data, 0, 7, false, 1, out _, out _));
        data = CreateExperiment();
        for (var i = 0; i < data.DataPoints.Count; i++) data.DataPoints[i] = new DataPoint(i, 10e-6f);
        Assert.False(ProcessingZoomPresentation.TryGetBaselineRange(data, 0, 7, true, 1, out _, out _));
    }

    static ExperimentData CreateExperiment()
    {
        var data = new ExperimentData("zoom.itc");
        var powers = new[] { 8, 10, 100, 100, 100, 10, 12, 10 };
        for (var i = 0; i < powers.Length; i++) data.DataPoints.Add(new DataPoint(i, powers[i] * 1e-6f));
        var injection = InjectionData.FromPEAQFile(data, 0, true, 2, 1e-6, 5, 1, 25);
        data.Injections.Add(injection);
        injection.SetIntegrationLengthByTime(2);
        data.Processor.InitializeBaseline(BaselineInterpolatorTypes.Spline);
        data.Processor.DiscardIntegratedPoints = true;
        data.Processor.Interpolator.Baseline = new[] { 9, 9, 9, 10, 10, 11, 11, 11 }
            .Select(value => new Energy(value * 1e-6)).ToList();
        return data;
    }
}
