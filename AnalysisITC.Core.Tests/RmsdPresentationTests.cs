using System.Globalization;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class RmsdPresentationTests
{
    [Theory]
    [InlineData(4.184, EnergyUnitFamily.Joules, "4.184 µJ")]
    [InlineData(4.184, EnergyUnitFamily.Calories, "1 µcal")]
    [InlineData(0, EnergyUnitFamily.Joules, "0 µJ")]
    [InlineData(0, EnergyUnitFamily.Calories, "0 µcal")]
    [InlineData(1.23456, EnergyUnitFamily.Joules, "1.235 µJ")]
    [InlineData(0.000000123456, EnergyUnitFamily.Joules, "1.235E-07 µJ")]
    [InlineData(double.NaN, EnergyUnitFamily.Joules, "Unavailable")]
    [InlineData(double.PositiveInfinity, EnergyUnitFamily.Calories, "Unavailable")]
    [InlineData(double.NegativeInfinity, EnergyUnitFamily.Joules, "Unavailable")]
    public void FormatsHeatWithIndependentExpectedValues(double value, EnergyUnitFamily family, string expected)
        => Assert.Equal(expected, RmsdPresentation.FormatHeat(value, family, CultureInfo.InvariantCulture));

    [Fact]
    public void MissingValueHasNoUnit()
        => Assert.Equal("Unavailable", RmsdPresentation.FormatHeat(null, EnergyUnitFamily.Joules));

    [Fact]
    public void UsesExplicitOrCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
            Assert.Equal("4,184 µJ", RmsdPresentation.FormatHeat(4.184, EnergyUnitFamily.Joules));
            Assert.Equal("4.184 µJ", RmsdPresentation.FormatHeat(4.184, EnergyUnitFamily.Joules, CultureInfo.InvariantCulture));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
