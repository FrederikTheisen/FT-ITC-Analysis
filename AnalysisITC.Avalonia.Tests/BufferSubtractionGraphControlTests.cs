using AnalysisITC.Avalonia.Tools;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

public sealed class BufferSubtractionGraphControlTests
{
    [Fact]
    public void ScreenshotRangesUseRoundTicksInsideThePlot()
    {
        Assert.Equal(new double[] { 20, 40 },
            BufferSubtractionGraphControl.BuildAxisTicks(-1.2, 58.2, injectionAxis: true));
        Assert.Equal(new double[] { -100, -75, -50, -25, 0 },
            BufferSubtractionGraphControl.BuildAxisTicks(-111, 12.9, injectionAxis: false));
    }

    [Theory]
    [InlineData(0.5, 1.5, 1)]
    [InlineData(0.5, 2.5, 2)]
    [InlineData(0.5, 3.5, 3)]
    public void ShortExperimentsHaveDistinctPositiveWholeInjectionTicks(double minimum, double maximum, int count)
    {
        var ticks = BufferSubtractionGraphControl.BuildAxisTicks(minimum, maximum, injectionAxis: true);
        Assert.Equal(count, ticks.Count);
        for (var i = 0; i < count; i++)
            Assert.Equal(i + 1, ticks[i]);
    }

    [Fact]
    public void SmallHeatRangesKeepFractionalTickSteps()
    {
        var ticks = BufferSubtractionGraphControl.BuildAxisTicks(-0.0111, 0.00129, injectionAxis: false);
        Assert.Equal(5, ticks.Count);
        Assert.Equal(-0.01, ticks[0], 12);
        Assert.Equal(-0.0075, ticks[1], 12);
        Assert.Equal(-0.005, ticks[2], 12);
        Assert.Equal(-0.0025, ticks[3], 12);
        Assert.Equal(0, ticks[4], 12);
    }

    [Fact]
    public void PositiveHeatRangeDoesNotAddTicksOutsideTheViewport()
    {
        Assert.Equal(new double[] { 25, 50, 75, 100, 125 },
            BufferSubtractionGraphControl.BuildAxisTicks(12.9, 137, injectionAxis: false));
    }

    [Fact]
    public void FractionalPreferredInjectionSpacingBecomesAWholeNiceStep()
    {
        Assert.Equal(new double[] { 5, 10 },
            BufferSubtractionGraphControl.BuildAxisTicks(0.5, 13.5, injectionAxis: true));
    }
}
