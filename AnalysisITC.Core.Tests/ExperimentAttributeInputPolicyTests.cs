using AnalysisITC.Core.Analysis;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class ExperimentAttributeInputPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7.4)]
    [InlineData(14)]
    public void AcceptsFinitePhIncludingBothBounds(double pH) =>
        Assert.True(ExperimentAttributeInputPolicy.IsValidBufferPh(pH));

    [Theory]
    [InlineData(-0.000001)]
    [InlineData(14.000001)]
    [InlineData(74)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsNonfiniteOrOutOfRangePh(double pH) =>
        Assert.False(ExperimentAttributeInputPolicy.IsValidBufferPh(pH));
}
