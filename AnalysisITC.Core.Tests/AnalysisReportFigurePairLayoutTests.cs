using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisReportFigurePairLayoutTests
{
    static readonly AnalysisReportFigureMetrics Left = new(200, 300, 40, 10, 20, 5);
    static readonly AnalysisReportFigureMetrics Right = new(120, 300, 70, 10, 35, 25);

    [Fact]
    public void BothFiguresReserveTheLargerVerticalMarginsWithoutUpscaling()
    {
        var pair = AnalysisReportFigurePairLayout.Create(new(10, 20, 512, 400), Left, Right, true);
        Assert.Equal(1, pair.Scale);
        Assert.Equal(300, pair.PlotHeight);
        Assert.Equal(35, pair.TopMargin);
        Assert.Equal(25, pair.BottomMargin);
        Assert.Equal(360, pair.LeftBounds.Height);
        Assert.Equal(360, pair.RightBounds.Height);
        Assert.Equal(55, pair.LeftBounds.Y + pair.TopMargin * pair.Scale);
        Assert.Equal(355, pair.RightBounds.Y + (pair.TopMargin + pair.PlotHeight) * pair.Scale);
    }

    [Fact]
    public void NarrowerColumnLimitsBothFiguresToOneScale()
    {
        // 250 available points split 142.5 / 107.5; the right figure is 200 points wide.
        var pair = AnalysisReportFigurePairLayout.Create(new(10, 20, 262, 400), Left, Right, true);
        Assert.Equal(.5375, pair.Scale, 10);
        Assert.Equal(14.0625, pair.LeftBounds.X, 10);
        Assert.Equal(164.5, pair.RightBounds.X, 10);
        Assert.Equal(193.5, pair.LeftBounds.Height, 10);
        Assert.Equal(193.5, pair.RightBounds.Height, 10);
        Assert.Equal(38.8125, pair.LeftBounds.Y + pair.TopMargin * pair.Scale, 10);
        Assert.Equal(200.0625, pair.RightBounds.Y + (pair.TopMargin + pair.PlotHeight) * pair.Scale, 10);
        Assert.True(pair.LeftBounds.Right <= pair.LeftColumn.Right);
        Assert.True(pair.RightBounds.Right <= pair.RightColumn.Right);
    }

    [Fact]
    public void AvailableHeightReservesBottomAxisLabelsForBothFigures()
    {
        var pair = AnalysisReportFigurePairLayout.Create(new(10, 20, 512, 100), Left, Right, true);
        Assert.Equal(100.0 / 360, pair.Scale, 10);
        Assert.Equal(120, pair.LeftBounds.Bottom, 10);
        Assert.Equal(120, pair.RightBounds.Bottom, 10);
        Assert.Equal(113.0555555556,
            pair.RightBounds.Y + (pair.TopMargin + pair.PlotHeight) * pair.Scale, 8);
    }

    [Fact]
    public void OrdinaryPairsKeepEqualColumnWidthsAndShareTheSmallerConfiguredGraphHeight()
    {
        var right = new AnalysisReportFigureMetrics(120, 200, 70, 10, 35, 25);
        var pair = AnalysisReportFigurePairLayout.Create(new(10, 20, 512, 400), Left, right, false);
        Assert.Equal(250, pair.LeftColumn.Width);
        Assert.Equal(250, pair.RightColumn.Width);
        Assert.Equal(200, pair.PlotHeight);
        Assert.Equal(260, pair.LeftBounds.Height);
        Assert.Equal(260, pair.RightBounds.Height);
    }
}
