using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class SummaryUncertaintyTests
{
    [Fact]
    public void AsymmetricExampleRetainsCentralAndPropagatesSeparateSides()
    {
        var values = new[] { new FloatWithError(10, 1, 8, 14), new FloatWithError(12, 3, 11, 15) };
        var summary = SummaryUncertainty.Mean(values, 11);
        var actual = summary.Evaluate(0);
        Assert.Equal(11, actual.Value);
        Assert.Equal(Math.Sqrt(7), actual.SD, 12); // sample variance 2 + mean individual variance 5
        Assert.Equal(11 - Math.Sqrt(5.0916), actual.Lower, 12);
        Assert.Equal(11 + Math.Sqrt(10.0916), actual.Upper, 12);
        Assert.Equal(actual, summary.Evaluate(0));
    }

    [Fact]
    public void NormalIntervalsReduceToCombinedStandardError()
    {
        var actual = SummaryUncertainty.Mean(new[] { new FloatWithError(10, 1), new FloatWithError(12, 3) }, 11).Evaluate(0);
        Assert.Equal(11 - 1.96 * Math.Sqrt(3.5), actual.Lower, 12);
        Assert.Equal(11 + 1.96 * Math.Sqrt(3.5), actual.Upper, 12);
    }

    [Fact]
    public void OpposingAsymmetriesCanCancel()
    {
        var actual = SummaryUncertainty.Mean(new[] { new FloatWithError(10, 1, 8, 14), new FloatWithError(10, 1, 6, 12) }, 10).Evaluate(0);
        Assert.Equal(Math.Sqrt(5), actual.LowerWidth, 12);
        Assert.Equal(actual.LowerWidth, actual.UpperWidth, 12);
    }

    [Theory]
    [InlineData(8, 14)]
    [InlineData(11, 14)]
    public void SingleObservationPreservesOrderedIntervalEvenOutsideBestFit(double lower, double upper)
    {
        var actual = SummaryUncertainty.Mean(new[] { new FloatWithError(10, 2, lower, upper) }, 10).Evaluate(0);
        Assert.Equal(10, actual.Value);
        Assert.Equal(2, actual.SD);
        Assert.Equal(lower, actual.Lower);
        Assert.Equal(upper, actual.Upper);
    }

    [Fact]
    public void MissingSdDoesNotDiscardValueOrAvailableInterval()
    {
        var actual = SummaryUncertainty.Mean(new[] { new FloatWithError(10, double.NaN, 8, 14), new FloatWithError(12, 0, 11, 15) }, 11).Evaluate(0);
        Assert.Equal(11, actual.Value);
        Assert.Equal(Math.Sqrt(2), actual.SD, 12);
        Assert.Equal(11 - Math.Sqrt(5.0916), actual.Lower, 12);
        Assert.Equal(0, SummaryUncertainty.Mean(new[] { new FloatWithError(10, 0) }, 10).Evaluate(0).SD);
    }

    [Theory]
    [InlineData(11, 14)]
    [InlineData(double.NaN, double.NaN)]
    public void InvalidMultiMemberIntervalDoesNotPreventSd(double lower, double upper)
    {
        var actual = SummaryUncertainty.Mean(new[] { new FloatWithError(10, 1, lower, upper), new FloatWithError(12, 3) }, 11).Evaluate(0);
        Assert.Equal(Math.Sqrt(7), actual.SD, 12);
        Assert.True(double.IsNaN(actual.Lower));
        Assert.True(double.IsNaN(actual.Upper));
    }

    [Fact]
    public void DefaultValueWithNoStoredIntervalIsNotTreatedAsKnownExactInterval()
    {
        var actual = SummaryUncertainty.Mean(new[] { default(FloatWithError), new FloatWithError(2, 1) }, 1).Evaluate(0);
        Assert.True(double.IsNaN(actual.Lower));
    }

    [Fact]
    public void TwoObservationTrendUsesIndividualErrorsAndSwapsSidesForNegativeWeights()
    {
        var summary = SummaryUncertainty.Trend(new LinearFitWithError(1, 10, 20),
            new[] { new FloatWithError(10, 1, 8, 14), new FloatWithError(20, 3, 19, 23) }, new[] { 20.0, 30.0 });
        var actual = summary.Evaluate(40); // weights -1 and 2; zero residual degrees of freedom
        Assert.Equal(30, actual.Value);
        Assert.Equal(Math.Sqrt(5), actual.SD, 12);
        Assert.Equal(30 - Math.Sqrt(20), actual.Lower, 12);
        Assert.Equal(30 + Math.Sqrt(40), actual.Upper, 12);
        Assert.Equal(Math.Sqrt(.1), summary.SlopeUncertainty.SD, 12);
        Assert.Equal(1 - Math.Sqrt(.17), summary.SlopeUncertainty.Lower, 12);
        Assert.Equal(1 + Math.Sqrt(.13), summary.SlopeUncertainty.Upper, 12);
    }

    [Fact]
    public void ReplicatesAtTwoTemperaturesRetainResidualDegreesOfFreedom()
    {
        // Fitted means 1 and 11, residuals -1,+1,-1,+1. SSE=4, df=2, Sxx=100.
        var summary = SummaryUncertainty.Trend(new LinearFitWithError(1, 6, 25),
            new[] { new FloatWithError(0, 1), new FloatWithError(2, 1), new FloatWithError(10, 1), new FloatWithError(12, 1) },
            new[] { 20.0, 20, 30, 30 });
        var actual = summary.Evaluate(25);
        Assert.Equal(Math.Sqrt(3), actual.SD, 12);
        Assert.Equal(6 - 1.96 * Math.Sqrt(.75), actual.Lower, 12);
        Assert.Equal(Math.Sqrt(.03), summary.SlopeUncertainty.SD, 12);
        Assert.InRange(Math.Abs(1 + 1.96 * Math.Sqrt(.03) - summary.SlopeUncertainty.Upper), 0, 1e-14);
    }

    [Fact]
    public void TrendUsesUnequalIndividualErrorsAndNonzeroResidualSpread()
    {
        // z=-1,0,1; y=0,0,3; residuals .5,-1,.5; residual variance=1.5.
        var summary = SummaryUncertainty.Trend(new LinearFitWithError(1.5, 1, 21),
            new[] { new FloatWithError(0, 1), new FloatWithError(0, 2), new FloatWithError(3, 3) }, new[] { 20.0, 21, 22 });
        Assert.Equal(Math.Sqrt(1.5 + 14.0 / 3), summary.Evaluate(21).SD, 12);
        Assert.Equal(Math.Sqrt(3.25), summary.SlopeUncertainty.SD, 12);
        Assert.Equal(1 + 1.96 * Math.Sqrt(.5 + 14.0 / 9), summary.Evaluate(21).Upper, 12);
    }

    [Fact]
    public void OneUsableTrendObservationRetainsIndividualErrorButCannotEstimateSlopeError()
    {
        var summary = SummaryUncertainty.Trend(new LinearFitWithError(1, 10, 20),
            new[] { new FloatWithError(10, 2, 8, 14) }, new[] { 20.0 });
        var actual = summary.Evaluate(20);
        Assert.Equal(10, actual.Value);
        Assert.Equal(2, actual.SD);
        Assert.Equal(8, actual.Lower);
        Assert.Equal(14, actual.Upper);
        Assert.True(double.IsNaN(summary.SlopeUncertainty.SD));
    }

    [Fact]
    public void DegenerateTemperatureTrendRetainsCentralButHasNoUncertainty()
    {
        var summary = SummaryUncertainty.Trend(new LinearFitWithError(1, 10, 20),
            new[] { new FloatWithError(10, 1), new FloatWithError(12, 3) }, new[] { 20.0, 20 });
        Assert.Equal(15, summary.Evaluate(25).Value);
        Assert.True(double.IsNaN(summary.Evaluate(25).SD));
        Assert.True(double.IsNaN(summary.SlopeUncertainty.SD));
    }

    [Fact]
    public void ModelDependenceRetainsStoredUncertaintyOnBothSidesOfReference()
    {
        var fit = new LinearFitWithError(new FloatWithError(2, .5, 1, 4), new FloatWithError(10, 2, 8, 15), 25);
        var summary = SummaryUncertainty.Model(fit);
        foreach (var t in new[] { 15.0, 25, 35 })
        {
            var expected = fit.Evaluate(t);
            var actual = summary.Evaluate(t);
            Assert.Equal(expected.Value, actual.Value);
            Assert.Equal(expected.SD, actual.SD, 12);
            Assert.Equal(expected.Lower, actual.Lower, 12);
            Assert.Equal(expected.Upper, actual.Upper, 12);
        }
    }

    [Fact]
    public void DuplicateExperimentReferencesAreDetectedWithoutComparingValues()
    {
        var first = new ExperimentData("first.itc");
        var second = new ExperimentData("second.itc");
        Assert.True(SummaryUncertainty.HasDuplicateExperiments(new[] { first, first }));
        Assert.False(SummaryUncertainty.HasDuplicateExperiments(new[] { first, second }));
    }
}
