using System;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class FixedZeroLinearFitTests
{
    [Theory]
    [InlineData(0, 20, 5, 30)]
    [InlineData(20, -20, -30, -5)]
    [InlineData(30, -40, -60, -10)]
    public void DirectSummaryAndEnvelopeRetainOneAsymmetricSource(double x, double center, double lower, double upper)
    {
        var fit = LinearFitWithError.WithFixedZero(new FloatWithError(-2, .5, -3, -.5), 10, 30);
        var direct = fit.Evaluate(x);
        var summary = SummaryUncertainty.Model(fit);
        Assert.Single(summary.Contributions);
        var evaluated = summary.Evaluate(x);
        var envelope = Assert.Single(FitEnvelopeBuilder.Build(fit, null, new[] { x }));
        foreach (var value in new[] { direct, evaluated })
        {
            Assert.Equal(center, value.Value, 10);
            Assert.Equal(lower, value.Lower, 10);
            Assert.Equal(upper, value.Upper, 10);
            Assert.Equal(Math.Abs(x - 10) * .5, value.SD, 10);
        }
        Assert.Equal(center, envelope.Center, 10);
        Assert.Equal(lower, envelope.Lower, 10);
        Assert.Equal(upper, envelope.Upper, 10);
        Assert.Equal(new FloatWithError(10), fit.GetXAxisIntersect());
    }

    [Fact]
    public void BootstrapEnvelopeEvaluatesEachFixedZeroLineAtTheRequestedCoordinate()
    {
        var primary = LinearFitWithError.WithFixedZero(new FloatWithError(-2), 10, 30);
        var replicates = new[]
        {
            LinearFitWithError.WithFixedZero(new FloatWithError(-3), 10, 20),
            LinearFitWithError.WithFixedZero(new FloatWithError(-2), 10, 30),
            LinearFitWithError.WithFixedZero(new FloatWithError(-1), 10, 40),
        };
        var point = Assert.Single(FitEnvelopeBuilder.Build(primary, replicates, new[] { 0d }));
        Assert.Equal(20, point.Center);
        Assert.Equal(10.5, point.Lower, 10);
        Assert.Equal(29.5, point.Upper, 10);
    }

    [Fact]
    public void FixedZeroIgnoresUnavailableSlopeUncertaintyAtExactZero()
    {
        var fit = LinearFitWithError.WithFixedZero(new FloatWithError(-2, double.NaN, double.NaN, double.NaN), 10, 30);
        Assert.Equal(new FloatWithError(0), fit.Evaluate(10));
        Assert.Equal(new FloatWithError(0), SummaryUncertainty.Model(fit).Evaluate(10));
        Assert.True(double.IsNaN(SummaryUncertainty.Model(fit).Evaluate(20).SD));
    }

    [Fact]
    public void LinearAbsoluteZeroReferenceDoesNotEvaluateNonlinearBasis()
    {
        var fit = new LinearFitWithError(-100, 0, -273.15);
        Assert.Equal(-30000, SummaryUncertainty.Model(fit).Evaluate(26.85).Value, 10);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidReferenceStillInvalidatesRequiredNonlinearTerm(bool central)
    {
        var summary = new SummaryDependence { ReferenceTemperature = -273.15, Intercept = 1, HeatCapacityTerm = central ? 1 : 0 };
        summary.Contributions.Add(new SummaryErrorContribution(1, 0, 1, 1, 1, central ? 0 : 1));
        var value = summary.Evaluate(25);
        if (central) Assert.True(double.IsNaN(value.Value));
        else { Assert.Equal(1, value.Value); Assert.True(double.IsNaN(value.SD)); }
    }
}
