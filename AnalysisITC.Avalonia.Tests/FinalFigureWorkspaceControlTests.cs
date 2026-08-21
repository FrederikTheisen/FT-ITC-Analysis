using AnalysisITC.Avalonia.FinalFigure;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class FinalFigureWorkspaceControlTests
{
    public FinalFigureWorkspaceControlTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 7)]
    [InlineData(2, 14)]
    public void TickDensityMapsToItsConfiguredTarget(int densityIndex, int expectedTickCount)
    {
        Assert.Equal(expectedTickCount, FinalFigureWorkspaceControl.TickCountForDensity((FinalFigureTickDensity)densityIndex));
    }

    [Fact]
    public void NewWorkspaceUsesNormalDensityForAllConfigurableAxes()
    {
        var workspace = new FinalFigureWorkspaceControl();
        var selectors = workspace.TickDensitySelectors;
        var options = workspace.GetOptionsSnapshot();

        Assert.Equal(4, selectors.Count);
        Assert.All(selectors, selector =>
        {
            Assert.Equal(new[] { "Sparse", "Normal", "Dense" }, selector.Options);
            Assert.Equal((int)FinalFigureTickDensity.Normal, selector.SelectedIndex);
        });
        Assert.Equal(7, options.DataXTickCount);
        Assert.Equal(7, options.DataYTickCount);
        Assert.Equal(7, options.FitXTickCount);
        Assert.Equal(7, options.FitYTickCount);
        Assert.True(options.SanitizeTicks);
    }

    [Fact]
    public void TickDensitySelectorsRemainIndependentInTheOptionsSnapshot()
    {
        var workspace = new FinalFigureWorkspaceControl();
        var selectors = workspace.TickDensitySelectors;

        selectors[0].SelectedIndex = (int)FinalFigureTickDensity.Sparse;
        selectors[1].SelectedIndex = (int)FinalFigureTickDensity.Normal;
        selectors[2].SelectedIndex = (int)FinalFigureTickDensity.Dense;
        selectors[3].SelectedIndex = (int)FinalFigureTickDensity.Sparse;

        var options = workspace.GetOptionsSnapshot();

        Assert.Equal(3, options.DataXTickCount);
        Assert.Equal(7, options.DataYTickCount);
        Assert.Equal(14, options.FitXTickCount);
        Assert.Equal(3, options.FitYTickCount);
    }
}
