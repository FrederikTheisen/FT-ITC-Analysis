using System;
using System.Globalization;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class ConcentrationUnitMagnitudeTests : IDisposable
{
    readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;
    readonly NumberPrecision originalPrecision = AppSettings.NumberPrecision;
    readonly ConcentrationUnit originalDefaultUnit = AppSettings.DefaultConcentrationUnit;

    public ConcentrationUnitMagnitudeTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        AppSettings.NumberPrecision = NumberPrecision.Standard;
        AppSettings.DefaultConcentrationUnit = ConcentrationUnit.µM;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = originalCulture;
        AppSettings.NumberPrecision = originalPrecision;
        AppSettings.DefaultConcentrationUnit = originalDefaultUnit;
    }

    [Theory]
    [InlineData(1, "1 M")]
    [InlineData(1e-3, "1 mM")]
    [InlineData(1e-6, "1 µM")]
    [InlineData(1e-9, "1 nM")]
    [InlineData(1e-12, "1 pM")]
    public void ExactPowersUseTheLargerUnit(double molar, string expected)
    {
        Assert.Equal(expected, new FloatWithError(molar).AsFormattedConcentration(true));
    }

    [Theory]
    [InlineData(1000, "1000 M")]
    [InlineData(999e-6, "999 µM")]
    [InlineData(1.5e-3, "1.5 mM")]
    [InlineData(0.5e-9, "500 pM")]
    [InlineData(1e-15, "0.001 pM")]
    [InlineData(-5e-6, "-5 µM")]
    [InlineData(-2e-3, "-2 mM")]
    public void AdjacentAndNegativeMagnitudes(double molar, string expected)
    {
        Assert.Equal(expected, new FloatWithError(molar).AsFormattedConcentration(true));
    }

    [Fact]
    public void ValuesWithinOneFloatingPointStepOfABoundaryUseTheLargerUnit()
    {
        Assert.Equal(ConcentrationUnit.µM, ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(Math.BitIncrement(1e-6)));
        Assert.Equal(ConcentrationUnit.µM, ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(Math.BitDecrement(1e-6)));
        Assert.Equal(ConcentrationUnit.µM, ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(1000 * 1e-9));
        Assert.Equal(ConcentrationUnit.mM, ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(1000 * 1e-6));
        Assert.Equal("1 µM", new FloatWithError(1000 * 1e-9).AsFormattedConcentration(true));
    }

    [Theory]
    [InlineData(ConcentrationUnit.nM, "0 nM")]
    [InlineData(ConcentrationUnit.mM, "0 mM")]
    public void ZeroUsesTheDefaultConcentrationUnit(ConcentrationUnit defaultUnit, string expected)
    {
        AppSettings.DefaultConcentrationUnit = defaultUnit;

        Assert.Equal(defaultUnit, ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(0));
        Assert.Equal(expected, new FloatWithError(0).AsFormattedConcentration(true));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteValuesSelectTheDefaultUnit(double molar)
    {
        AppSettings.DefaultConcentrationUnit = ConcentrationUnit.nM;

        Assert.Equal(ConcentrationUnit.nM, ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(molar));
    }

    [Theory]
    [InlineData(double.PositiveInfinity, "∞")]
    [InlineData(double.NegativeInfinity, "−∞")]
    public void InfiniteValuesShowNoUnit(double molar, string expected)
    {
        var value = new FloatWithError(molar);

        Assert.Equal(expected, value.AsFormattedConcentration(true));
        Assert.Equal(expected, value.AsF1FormattedConcentration(true));
    }

    [Fact]
    public void NaNValuesShowNoUnit()
    {
        Assert.Equal("Unavailable", FloatWithError.NaN.AsFormattedConcentration(true));
        Assert.Equal("Unavailable", FloatWithError.NaN.AsF1FormattedConcentration(true));

        var value = new FloatWithError(double.NaN);
        Assert.DoesNotMatch("[mµnp]?M$", value.AsFormattedConcentration(true));
        Assert.DoesNotMatch("[mµnp]?M$", value.AsF1FormattedConcentration(true));
    }
}
