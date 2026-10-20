using System;
using System.Collections.Generic;
using System.Globalization;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class FloatWithErrorMissingFormattingTests : IDisposable
{
    readonly NumberPrecision originalPrecision = AppSettings.NumberPrecision;
    readonly UncertaintyDisplayStyle originalStyle = AppSettings.UncertaintyDisplayStyle;
    readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;

    public static IEnumerable<object[]> DisplayCases()
    {
        foreach (var precision in Enum.GetValues<NumberPrecision>())
        foreach (var style in Enum.GetValues<UncertaintyDisplayStyle>())
        foreach (var culture in new[] { "en-US", "fr-FR" })
            yield return new object[] { precision, style, culture };
    }

    [Theory]
    [MemberData(nameof(DisplayCases))]
    public void MissingTextHasNoNumericOrUnitSuffix(NumberPrecision precision, UncertaintyDisplayStyle style, string culture)
    {
        Configure(precision, style, culture);
        var missing = FloatWithError.NaN;

        Assert.Equal("Unavailable", missing.ToString());
        Assert.Equal("Unavailable", missing.ToString("F2", style, CultureInfo.InvariantCulture));
        Assert.Equal("Unavailable", missing.AsNumber());
        foreach (var withUnit in new[] { false, true })
        {
            Assert.Equal("Unavailable", missing.AsConcentration(ConcentrationUnit.µM, withUnit));
            Assert.Equal("Unavailable", missing.AsFormattedConcentration(withUnit, withci: true));
            Assert.Equal("Unavailable", missing.AsF1FormattedConcentration(withUnit));
            foreach (var unit in new[] { ConcentrationUnit.M, ConcentrationUnit.µM, ConcentrationUnit.pM })
                Assert.Equal("Unavailable", missing.AsFormattedConcentration(unit, withUnit, withci: true, style: style));
            foreach (var unit in Enum.GetValues<EnergyUnit>())
            {
                Assert.Equal("Unavailable", missing.AsFormattedEnergy(unit, "energy/mol·K", withUnit, withci: true, style: style));
                var energy = new Energy(missing);
                Assert.Equal("Unavailable", energy.ToString());
                Assert.Equal("Unavailable", energy.ToFormattedString(unit, withUnit, permole: true, perK: true, withci: true, style: style));
                Assert.Equal("Unavailable", energy.ToString(unit, "F2", withUnit, permole: true, perK: true, style: style));
            }
        }
        Assert.True(FloatWithError.IsNaN(missing));
        Assert.Equal(precision, AppSettings.NumberPrecision);
    }

    [Theory]
    [MemberData(nameof(DisplayCases))]
    public void LegitimateZerosKeepTheirNumericFormat(NumberPrecision precision, UncertaintyDisplayStyle style, string culture)
    {
        Configure(precision, style, culture);
        var zero = new FloatWithError(0);
        var expected = precision == NumberPrecision.SingleDecimal ? 0.0.ToString("F1") : "0";

        Assert.Equal(expected, zero.AsNumber());
        Assert.Equal(expected, default(FloatWithError).AsNumber());
        Assert.Equal("0 µM", zero.AsConcentration(ConcentrationUnit.µM));
        Assert.Equal(expected + " µM", zero.AsFormattedConcentration(ConcentrationUnit.µM));
        Assert.Equal(expected + " kJ/mol", new Energy(zero).ToFormattedString(EnergyUnit.KiloJoule, permole: true));
        Assert.Equal(0.0.ToString("F2") + " kcal/mol·K", new Energy(zero).ToString(EnergyUnit.KCal, "F2", permole: true, perK: true));
        Assert.False(FloatWithError.IsNaN(zero));
    }

    [Theory]
    [InlineData("en-US", "0.0 ± 1.0 [-2.0, 3.0]")]
    [InlineData("fr-FR", "0,0 ± 1,0 [-2,0, 3,0]")]
    public void ZeroCenteredAsymmetricUncertaintyIsPreserved(string culture, string expected)
    {
        Configure(NumberPrecision.SingleDecimal, UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval, culture);
        var value = new FloatWithError(0, 1, -2, 3);

        Assert.Equal(expected, value.AsNumber());
        Assert.Equal(expected + " µM", new FloatWithError(0, 1e-6, -2e-6, 3e-6).AsFormattedConcentration(ConcentrationUnit.µM));
        Assert.Equal(expected + " kJ/mol", new Energy(new FloatWithError(0, 1000, -2000, 3000)).ToFormattedString(EnergyUnit.KiloJoule, permole: true));
        Assert.Equal(-2, value.Lower);
        Assert.Equal(3, value.Upper);
        Assert.False(FloatWithError.IsNaN(value));
    }

    [Theory]
    [InlineData(-1000.0)]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(1000.0)]
    public void ScalarMultiplicationPreservesMissingFlag(double scalar)
    {
        Assert.True(FloatWithError.IsNaN(scalar * FloatWithError.NaN));
        Assert.True(FloatWithError.IsNaN(FloatWithError.NaN * scalar));
        Assert.False(FloatWithError.IsNaN(scalar * new FloatWithError(0)));
    }

    [Theory]
    [InlineData(-1000.0)]
    [InlineData(-1.0)]
    [InlineData(1.0)]
    [InlineData(1000.0)]
    public void ScalarDivisionPreservesMissingFlag(double scalar)
    {
        Assert.True(FloatWithError.IsNaN(FloatWithError.NaN / scalar));
        Assert.False(FloatWithError.IsNaN(new FloatWithError(0) / scalar));
    }

    [Fact]
    public void UnitAwareEnergyConstructionPreservesMissingFlag()
    {
        foreach (var unit in Enum.GetValues<EnergyUnit>())
        {
            Assert.True(FloatWithError.IsNaN(new Energy(FloatWithError.NaN, unit).FloatWithError));
            Assert.False(FloatWithError.IsNaN(new Energy(new FloatWithError(0), unit).FloatWithError));
        }
    }

    void Configure(NumberPrecision precision, UncertaintyDisplayStyle style, string culture)
    {
        AppSettings.NumberPrecision = precision;
        AppSettings.UncertaintyDisplayStyle = style;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
    }

    public void Dispose()
    {
        AppSettings.NumberPrecision = originalPrecision;
        AppSettings.UncertaintyDisplayStyle = originalStyle;
        CultureInfo.CurrentCulture = originalCulture;
    }
}
