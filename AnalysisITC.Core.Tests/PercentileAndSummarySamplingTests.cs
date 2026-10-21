using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class PercentileAndSummarySamplingTests
{
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(39, 0, 38)]
    [InlineData(40, 0, 39)]
    [InlineData(41, 1, 39)]
    [InlineData(99, 2, 96)]
    [InlineData(100, 2, 97)]
    [InlineData(101, 2, 98)]
    [InlineData(199, 4, 194)]
    [InlineData(200, 4, 195)]
    [InlineData(201, 5, 195)]
    [InlineData(999, 24, 974)]
    [InlineData(1000, 24, 975)]
    [InlineData(1001, 25, 975)]
    [InlineData(1999, 49, 1949)]
    [InlineData(2000, 49, 1950)]
    [InlineData(2001, 50, 1950)]
    [InlineData(4999, 124, 4874)]
    [InlineData(5000, 124, 4875)]
    [InlineData(5001, 125, 4875)]
    [InlineData(9999, 249, 9749)]
    [InlineData(10000, 249, 9750)]
    [InlineData(10001, 250, 9750)]
    public void OrderedDistributionUsesOutwardBounds(int count, int lower, int upper)
    {
        var result = new FloatWithError(Enumerable.Range(0, count).Select(i => (double)i), -123);
        Assert.Equal(-123, result.Value);
        Assert.Equal(lower, result.Lower);
        Assert.Equal(upper, result.Upper);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleMemberPreservesCompleteUncertainty(bool supplyCenter)
    {
        var member = new FloatWithError(10, 0.01, 8, 50);
        var result = new FloatWithError(new List<FloatWithError> { member }, supplyCenter ? 10 : null);
        Equal(member, result);
    }

    [Fact]
    public void SingleMemberWithDifferentCenterStillUsesReferenceRms()
    {
        var result = new FloatWithError(new List<FloatWithError> { new(2, 0) }, 10);
        Equal(new FloatWithError(10, 8, 2, 2), result);
    }

    [Theory]
    [InlineData(2, 400)]
    [InlineData(100, 10000)]
    [InlineData(1200, 12000)]
    public void ZeroSdMembersVerifySamplingAndDrawCount(int count, int expectedDraws)
    {
        var members = Enumerable.Range(0, count).Select(i => new FloatWithError(i % 2 * 2, 0)).ToList();
        var random = new Random(59);
        foreach (var member in members)
            Assert.Equal(member.Value, member.Sample(random));

        // Half the draws are 0 and half 2: sum of squared deviations from 1 is M.
        // The omitted-center denominator M-1 makes the draw count observable.
        var result = new FloatWithError(members);
        var expectedSd = Math.Sqrt((double)expectedDraws / (expectedDraws - 1));
        Assert.Equal(1, result.Value);
        Assert.InRange(Math.Abs(result.SD - expectedSd) / expectedSd, 0, 1e-12);
        Assert.Equal(0, result.Lower);
        Assert.Equal(2, result.Upper);

        // With center 10, average squared deviation is (100 + 64)/2 regardless of M.
        var suppliedCenter = new FloatWithError(members, 10);
        Equal(new FloatWithError(10, Math.Sqrt(82), 0, 2), suppliedCenter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UncertainMembersAreRepeatable(bool supplyCenter)
    {
        var members = new List<FloatWithError> { new(10, 1, 8, 15), new(30, 2, 27, 35) };
        double? center = supplyCenter ? 20 : null;
        var first = new FloatWithError(members, center);
        if (supplyCenter) Assert.Equal(20, first.Value);
        for (var i = 0; i < 3; i++) Equal(first, new FloatWithError(members, center));
    }

    [Fact]
    public void IntersectionIsRepeatableAndRetainsExactCenter()
    {
        var fit = new LinearFitWithError(new FloatWithError(2, 0.1), new FloatWithError(-8, 0.3), 25);
        var first = fit.GetXAxisIntersect();
        Assert.Equal(29, first.Value); // 2 * (x - 25) - 8 = 0.
        Assert.True(first.SD > 0);
        for (var i = 0; i < 3; i++) Equal(first, fit.GetXAxisIntersect());
    }

    [Fact]
    public void ExactAndFixedZeroIntersectionsHaveNoUncertainty()
    {
        Equal(new FloatWithError(29), new LinearFitWithError(2, -8, 25).GetXAxisIntersect());
        Equal(new FloatWithError(29), LinearFitWithError.WithFixedZero(new FloatWithError(2, 0.1), 29, 25).GetXAxisIntersect());
    }

    static void Equal(FloatWithError expected, FloatWithError actual)
    {
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.SD, actual.SD);
        Assert.Equal(expected.Lower, actual.Lower);
        Assert.Equal(expected.Upper, actual.Upper);
        Assert.Equal(expected.IsAsymmetric, actual.IsAsymmetric);
    }
}
