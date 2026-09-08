using Xunit;

using AnalysisITC.Avalonia.Drawing;

namespace AnalysisITC.Avalonia.Tests;

public sealed class PublicationFigureIntegrationMarkerTests
{
    [Theory]
    [InlineData(100f, 3f)]
    [InlineData(250f, 7.5f)]
    public void LineMarkerHeightIsThreePercentOfThePlotPanel(float panelHeight, float expectedHeight)
    {
        var markerHeight = SkiaFigureRenderer.IntegrationLineMarkerHeight(panelHeight);

        Assert.Equal(expectedHeight, markerHeight, precision: 4);
    }
}
