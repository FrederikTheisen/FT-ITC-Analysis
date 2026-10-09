using System;
using System.Globalization;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class StructuringPresentationTests
{
    [Theory]
    [InlineData(false, UncertaintyDisplayStyle.Automatic, "25 [23, 29] °C")]
    [InlineData(true, UncertaintyDisplayStyle.Automatic, "298.15 [296.15, 302.15] K")]
    [InlineData(false, UncertaintyDisplayStyle.StandardDeviation, "25 ± 1 °C")]
    [InlineData(true, UncertaintyDisplayStyle.StandardDeviation, "298.15 ± 1 K")]
    [InlineData(false, UncertaintyDisplayStyle.ConfidenceInterval, "25 [23, 29] °C")]
    [InlineData(true, UncertaintyDisplayStyle.ConfidenceInterval, "298.15 [296.15, 302.15] K")]
    [InlineData(false, UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval, "25 ± 1 [23, 29] °C")]
    [InlineData(true, UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval, "298.15 ± 1 [296.15, 302.15] K")]
    [InlineData(false, UncertaintyDisplayStyle.None, "25 °C")]
    [InlineData(true, UncertaintyDisplayStyle.None, "298.15 K")]
    public void SavedUncertaintyUsesSelectedStyleAndTemperatureUnits(
        bool useKelvin, UncertaintyDisplayStyle style, string expected)
    {
        WithSettings(() =>
        {
            var saved = new FloatWithError(25, 1, 23, 29);
            Assert.Equal(expected, StructuringPresentation.FormatEvaluationTemperature(saved, useKelvin, style));
            Assert.Equal(25, saved.Value);
            Assert.Equal(1, saved.SD);
            Assert.Equal(new[] { 23.0, 29.0 }, saved.DistributionConfidence95);
        });
    }

    [Theory]
    [InlineData(false, "25 °C")]
    [InlineData(true, "298.15 K")]
    public void TemperatureWithoutUncertaintyStaysEstimateOnly(bool useKelvin, string expected)
    {
        WithSettings(() =>
        {
            foreach (UncertaintyDisplayStyle style in Enum.GetValues(typeof(UncertaintyDisplayStyle)))
                Assert.Equal(expected, StructuringPresentation.FormatEvaluationTemperature(new FloatWithError(25), useKelvin, style));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingAndNonFiniteTemperaturesAreUnavailableWithoutUnits(bool useKelvin)
    {
        foreach (var value in new[] { FloatWithError.NaN, new FloatWithError(double.NaN),
            new FloatWithError(double.PositiveInfinity), new FloatWithError(double.NegativeInfinity) })
            Assert.Equal("Unavailable", StructuringPresentation.FormatEvaluationTemperature(
                value, useKelvin, UncertaintyDisplayStyle.Automatic));
    }

    [Fact]
    public void NumericFormattingFollowsExistingPrecisionAndCulture()
    {
        WithSettings(() =>
        {
            AppSettings.NumberPrecision = NumberPrecision.SingleDecimal;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
            Assert.Equal("298,3 ± 1,0 K", StructuringPresentation.FormatEvaluationTemperature(
                new FloatWithError(25.16, 1), true, UncertaintyDisplayStyle.StandardDeviation));
        });
    }

    static void WithSettings(Action check)
    {
        var previousPrecision = AppSettings.NumberPrecision;
        var previousStyle = AppSettings.UncertaintyDisplayStyle;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            AppSettings.NumberPrecision = NumberPrecision.AllDecimals;
            // Explicit style arguments must take precedence over the global preference.
            AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.None;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            check();
        }
        finally
        {
            AppSettings.NumberPrecision = previousPrecision;
            AppSettings.UncertaintyDisplayStyle = previousStyle;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
