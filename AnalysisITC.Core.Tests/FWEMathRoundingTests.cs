using System;
using System.Globalization;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class FWEMathRoundingTests
{
    [Theory]
    [InlineData(2.4, 2)]
    [InlineData(2.5, 3)]
    [InlineData(2.6, 3)]
    [InlineData(-2.4, -2)]
    [InlineData(-2.5, -3)]
    [InlineData(-2.6, -3)]
    [InlineData(0.5, 1)]
    [InlineData(-0.5, -1)]
    [InlineData(2.5 - 0.0000001, 3)]
    [InlineData(2.5 - 0.000001, 2)]
    [InlineData(-2.5 + 0.0000001, -3)]
    [InlineData(-2.5 + 0.000001, -2)]
    [InlineData(0.0, 0)]
    [InlineData(4.0, 4)]
    public void OneArgumentOverloadRoundsPositiveAndNegativeValuesSymmetrically(double input, double expected)
    {
        Assert.Equal(expected, FWEMath.RoundApproximate(input));
    }

    [Theory]
    [InlineData(2.4, 0, 2)]
    [InlineData(2.5, 0, 3)]
    [InlineData(2.6, 0, 3)]
    [InlineData(-2.4, 0, -2)]
    [InlineData(-2.5, 0, -3)]
    [InlineData(-2.6, 0, -3)]
    [InlineData(1.25, 1, 1.3)]
    [InlineData(-1.25, 1, -1.3)]
    [InlineData(256, -1, 260)]
    [InlineData(-256, -1, -260)]
    [InlineData(2.0, 3, 2.0)]
    public void DigitsOverloadRoundsBothSignsAndSupportsNegativeDigits(double input, int digits, double expected)
    {
        Assert.Equal(expected, FWEMath.RoundApproximate(input, digits));
    }

    [Fact]
    public void OverloadsKeepTheirDistinctDefaultMargins()
    {
        Assert.Equal(2, FWEMath.RoundApproximate(2.499999));
        Assert.Equal(3, FWEMath.RoundApproximate(2.499999, digits: 0));
        Assert.Equal(-2, FWEMath.RoundApproximate(-2.499999));
        Assert.Equal(-3, FWEMath.RoundApproximate(-2.499999, digits: 0));
    }

    [Theory]
    [InlineData(1.25 - 0.00000005, 1.3)]
    [InlineData(1.25 - 0.00000015, 1.2)]
    [InlineData(-1.25 + 0.00000005, -1.3)]
    [InlineData(-1.25 + 0.00000015, -1.2)]
    public void DigitsOverloadUsesSymmetricToleranceBasedOnOriginalValue(double input, double expected)
    {
        Assert.Equal(expected, FWEMath.RoundApproximate(input, digits: 1, margin: 8e-7));
    }

    [Theory]
    [InlineData(2.5, 0, MidpointRounding.AwayFromZero, 3)]
    [InlineData(-2.5, 0, MidpointRounding.AwayFromZero, -3)]
    [InlineData(2.5, 0, MidpointRounding.ToEven, 2)]
    [InlineData(-2.5, 0, MidpointRounding.ToEven, -2)]
    [InlineData(3.5, 0, MidpointRounding.ToEven, 4)]
    [InlineData(-3.5, 0, MidpointRounding.ToEven, -4)]
    [InlineData(2.5000001, 0, MidpointRounding.ToEven, 2)]
    [InlineData(-2.5000001, 0, MidpointRounding.ToEven, -2)]
    [InlineData(3.5 - 0.0000001, 0, MidpointRounding.ToEven, 4)]
    [InlineData(3.5 - 0.00001, 0, MidpointRounding.ToEven, 3)]
    [InlineData(-3.5 + 0.0000001, 0, MidpointRounding.ToEven, -4)]
    [InlineData(-3.5 + 0.00001, 0, MidpointRounding.ToEven, -3)]
    public void DigitsOverloadHonorsRequestedMidpointMode(double input, int digits, MidpointRounding mode, double expected)
    {
        Assert.Equal(expected, FWEMath.RoundApproximate(input, digits, mode: mode));
    }

    [Fact]
    public void DigitsOverloadSupportsCustomAndZeroMargins()
    {
        Assert.Equal(3, FWEMath.RoundApproximate(2.4999, margin: 0.001));
        Assert.Equal(2, FWEMath.RoundApproximate(2.4999, margin: 0));
        Assert.Equal(-3, FWEMath.RoundApproximate(-2.4999, margin: 0.001));
        Assert.Equal(-2, FWEMath.RoundApproximate(-2.4999, margin: 0));
    }

    [Fact]
    public void BothOverloadsPreserveIntegersAndNonFiniteValues()
    {
        Assert.Equal(12, FWEMath.RoundApproximate(12));
        Assert.Equal(-12, FWEMath.RoundApproximate(-12));
        Assert.Equal(12, FWEMath.RoundApproximate(12, digits: 2));
        Assert.Equal(-12, FWEMath.RoundApproximate(-12, digits: 2));
        Assert.True(double.IsNaN(FWEMath.RoundApproximate(double.NaN)));
        Assert.Equal(double.PositiveInfinity, FWEMath.RoundApproximate(double.PositiveInfinity, digits: 2));
        Assert.Equal(double.NegativeInfinity, FWEMath.RoundApproximate(double.NegativeInfinity, digits: 2));
    }

    [Theory]
    [InlineData(NumberPrecision.Standard)]
    [InlineData(NumberPrecision.Strict)]
    public void StandardAndStrictFormattingRoundMirroredValuesAndIntervalEndpoints(NumberPrecision precision)
    {
        var originalPrecision = AppSettings.NumberPrecision;
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            AppSettings.NumberPrecision = precision;

            var positive = new FloatWithError(2.5, 2, 0.5, 4.5);
            var negative = new FloatWithError(-2.5, 2, -4.5, -0.5);
            var crossingZero = new FloatWithError(0.5, 2, -1.5, 2.5);
            var scaledPositive = new FloatWithError(25, 20, 5, 45);
            var scaledNegative = new FloatWithError(-25, 20, -45, -5);
            var fractionalPositive = new FloatWithError(0.25, 0.2, 0.05, 0.45);
            var fractionalNegative = new FloatWithError(-0.25, 0.2, -0.45, -0.05);

            Assert.Equal("3 ± 2 [1, 5]", positive.AsNumber(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval));
            Assert.Equal("-3 ± 2 [-5, -1]", negative.AsNumber(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval));
            Assert.Equal("1 ± 2 [-2, 3]", crossingZero.AsNumber(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval));
            Assert.Equal("30 ± 20 [10, 50]", scaledPositive.AsNumber(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval));
            Assert.Equal("-30 ± 20 [-50, -10]", scaledNegative.AsNumber(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval));
            Assert.Equal("0.3 ± 0.2 [0.1, 0.5]", fractionalPositive.AsNumber(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval));
            Assert.Equal("-0.3 ± 0.2 [-0.5, -0.1]", fractionalNegative.AsNumber(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval));

            Assert.Equal(2.5, positive.Value);
            Assert.Equal(2, positive.SD);
            Assert.Equal(0.5, positive.Lower);
            Assert.Equal(4.5, positive.Upper);
            Assert.Equal(-2.5, negative.Value);
            Assert.Equal(-4.5, negative.Lower);
            Assert.Equal(-0.5, negative.Upper);
        }
        finally
        {
            AppSettings.NumberPrecision = originalPrecision;
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
