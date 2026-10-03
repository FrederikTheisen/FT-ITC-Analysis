using System;
using System.Globalization;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class FloatWithErrorCompactFormattingTests
{
    const UncertaintyDisplayStyle Full = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;

    [Theory]
    [InlineData(NumberPrecision.Strict, "100000 ± 2000 [96000, 104000]")]
    [InlineData(NumberPrecision.Standard, "100000 ± 2000 [96000, 104000]")]
    [InlineData(NumberPrecision.SingleDecimal, "100000.0 ± 2000.0 [96000.0, 104000.0]")]
    [InlineData(NumberPrecision.AllDecimals, "1E+05 ± 2000 [96000, 1.04E+05]")]
    public void OrdinaryValuesKeepTheirExistingFormat(NumberPrecision precision, string expected)
    {
        WithSettings(precision, () =>
        {
            var value = new FloatWithError(100000, 2000, 96000, 104000);

            Assert.Equal(expected, value.AsNumber(Full));
        });
    }

    [Theory]
    [InlineData(NumberPrecision.Strict, "1.2346E+05")]
    [InlineData(NumberPrecision.Standard, "1.2346E+05")]
    [InlineData(NumberPrecision.SingleDecimal, "123456.0")]
    [InlineData(NumberPrecision.AllDecimals, "1.2346E+05")]
    public void OrdinaryEstimateWithoutUncertaintyKeepsItsExistingFormat(NumberPrecision precision, string expected)
    {
        WithSettings(precision, () => Assert.Equal(expected, new FloatWithError(123456).AsNumber(Full)));
    }

    [Theory]
    [InlineData(9999999999.0, "9999999999.0")]
    [InlineData(-9999999999.0, "-9999999999.0")]
    [InlineData(1e10, "1E+10")]
    [InlineData(-1e10, "-1E+10")]
    [InlineData(12345678901.0, "1.23457E+10")]
    [InlineData(-12345678901.0, "-1.23457E+10")]
    public void EstimatesSwitchToScientificNotationAtTheThreshold(double estimate, string expected)
    {
        WithSettings(NumberPrecision.SingleDecimal, () => Assert.Equal(expected, new FloatWithError(estimate).AsNumber(Full)));
    }

    [Theory]
    [InlineData(NumberPrecision.Strict, "1.23457E+10")]
    [InlineData(NumberPrecision.Standard, "1.23457E+10")]
    [InlineData(NumberPrecision.AllDecimals, "1.23457E+10")]
    public void EstimateOnlyFallbackUsesTheThreshold(NumberPrecision precision, string expected)
    {
        WithSettings(precision, () => Assert.Equal(expected, new FloatWithError(12345678901.0).AsNumber(Full)));
    }

    [Theory]
    [InlineData(NumberPrecision.Strict, "5 ± 1 [3, 1.5E+300]")]
    [InlineData(NumberPrecision.Standard, "5.0 ± 1.0 [3.0, 1.5E+300]")]
    [InlineData(NumberPrecision.SingleDecimal, "5.0 ± 1.0 [3.0, 1.5E+300]")]
    [InlineData(NumberPrecision.AllDecimals, "5 ± 1 [3, 1.5E+300]")]
    public void IntervalComponentsAreFormattedIndependently(NumberPrecision precision, string expected)
    {
        WithSettings(precision, () => Assert.Equal(expected, new FloatWithError(5, 1, 3, 1.5e300).AsNumber(Full)));
    }

    [Fact]
    public void LargeStandardDeviationIsFormattedIndependently()
    {
        WithSettings(NumberPrecision.SingleDecimal, () =>
            Assert.Equal("5.0 ± 2E+10", new FloatWithError(5, 2e10).AsNumber(UncertaintyDisplayStyle.StandardDeviation)));
    }

    [Fact]
    public void UnitConversionCanMoveComponentsAcrossTheThreshold()
    {
        WithSettings(NumberPrecision.SingleDecimal, () =>
        {
            var value = new FloatWithError(0.005, 0.001, 0.003, 0.02);

            Assert.Equal("5.0 ± 1.0 [3.0, 20.0] mM", value.AsFormattedConcentration(ConcentrationUnit.mM, withci: true, style: Full));
            Assert.Equal("5000000000.0 ± 1000000000.0 [3000000000.0, 2E+10] pM", value.AsFormattedConcentration(ConcentrationUnit.pM, withci: true, style: Full));
            Assert.Equal("5E+10 pM", new FloatWithError(0.05).AsFormattedConcentration(ConcentrationUnit.pM, style: Full));
        });
    }

    [Theory]
    [InlineData(NumberPrecision.Strict, "5 ± 1 [−∞, ∞]")]
    [InlineData(NumberPrecision.Standard, "5.0 ± 1.0 [−∞, ∞]")]
    [InlineData(NumberPrecision.SingleDecimal, "5.0 ± 1.0 [−∞, ∞]")]
    [InlineData(NumberPrecision.AllDecimals, "5 ± 1 [−∞, ∞]")]
    public void InfiniteBoundsUseInfinitySymbols(NumberPrecision precision, string expected)
    {
        WithSettings(precision, () =>
            Assert.Equal(expected, new FloatWithError(5, 1, double.NegativeInfinity, double.PositiveInfinity).AsNumber(Full)));
    }

    [Theory]
    [InlineData(NumberPrecision.Strict, "5 ± 1 [-1.79769E+308, 1.79769E+308]")]
    [InlineData(NumberPrecision.Standard, "5.0 ± 1.0 [-1.79769E+308, 1.79769E+308]")]
    [InlineData(NumberPrecision.SingleDecimal, "5.0 ± 1.0 [-1.79769E+308, 1.79769E+308]")]
    [InlineData(NumberPrecision.AllDecimals, "5 ± 1 [-1.79769E+308, 1.79769E+308]")]
    public void ExtremeFiniteBoundsRemainNumeric(NumberPrecision precision, string expected)
    {
        WithSettings(precision, () =>
            Assert.Equal(expected, new FloatWithError(5, 1, double.MinValue, double.MaxValue).AsNumber(Full)));
    }

    [Theory]
    [InlineData(NumberPrecision.Strict, "1.00000 ± 0.00001 [0.99998, 1.79769E+308]")]
    [InlineData(NumberPrecision.Standard, "1.000000 ± 0.000010 [0.999980, 1.79769E+308]")]
    public void ExtremeFiniteBoundRemainsNumericWhenDisplayRoundingOverflows(NumberPrecision precision, string expected)
    {
        WithSettings(precision, () =>
        {
            // Rounding the upper bound to the SD's decimal place exceeds double.MaxValue.
            var value = new FloatWithError(1, 0.00001, 0.99998, double.MaxValue);

            Assert.Equal(expected, value.AsNumber(Full));
        });
    }

    [Fact]
    public void NaNFormattingIsPreserved()
    {
        WithSettings(NumberPrecision.SingleDecimal, () => Assert.Equal("NaN", new FloatWithError(double.NaN).AsNumber(Full)));
    }

    [Fact]
    public void FormattingLeavesStoredValuesUnchanged()
    {
        var value = new FloatWithError(12345678901.0, 2e10, double.NegativeInfinity, double.MaxValue);

        foreach (NumberPrecision precision in Enum.GetValues(typeof(NumberPrecision)))
            WithSettings(precision, () =>
            {
                value.AsNumber(Full);
                value.AsFormattedConcentration(ConcentrationUnit.pM, withci: true, style: Full);
            });

        Assert.Equal(12345678901.0, value.Value);
        Assert.Equal(2e10, value.SD);
        Assert.Equal(double.NegativeInfinity, value.Lower);
        Assert.Equal(double.MaxValue, value.Upper);
    }

    static void WithSettings(NumberPrecision precision, Action action)
    {
        var originalPrecision = AppSettings.NumberPrecision;
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            AppSettings.NumberPrecision = precision;

            action();
        }
        finally
        {
            AppSettings.NumberPrecision = originalPrecision;
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
