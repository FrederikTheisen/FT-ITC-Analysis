using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class RelativeUncertaintyAvailabilityTests
{
    [Theory]
    [InlineData(1e-9, 0.5e-9, 2e-9)]
    [InlineData(1e100, 0.5e100, 2e100)]
    [InlineData(-1e-9, -2e-9, -0.5e-9)]
    public void SdFreeAsymmetricIntervalsRemainAvailableAcrossScales(double value, double lower, double upper)
    {
        var estimate = new FloatWithError(value, 0, lower, upper);
        Assert.False(estimate.HasError);
        Assert.True(estimate.HasConfidenceInterval);
        Assert.Equal(lower, estimate.Lower);
        Assert.Equal(upper, estimate.Upper);
    }

    [Theory]
    [InlineData(1, 2e-7, true)]
    [InlineData(1, 5e-8, false)]
    [InlineData(1e-9, 2e-16, true)]
    [InlineData(1e-9, 5e-17, false)]
    [InlineData(0, 2e-7, true)]
    [InlineData(0, 5e-8, false)]
    public void SdAndConfidenceIntervalShareRelativeThresholdWithZeroFallback(double value, double width, bool available)
    {
        var estimate = new FloatWithError(value, width, value - width, value + width);
        Assert.Equal(1e-7, FloatWithError.UncertaintyAvailabilityThreshold);
        Assert.Equal(available, estimate.HasError);
        Assert.Equal(available, estimate.HasConfidenceInterval);
    }

    [Fact]
    public void PointEstimateHasNoUncertainty()
    {
        var estimate = new FloatWithError(1e-9);
        Assert.False(estimate.HasError);
        Assert.False(estimate.HasConfidenceInterval);
    }
}
