using System;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class AnalysisResultModelOptionPresentationTests : IDisposable
{
    readonly ConcentrationUnit originalUnit = AppSettings.DefaultConcentrationUnit;
    readonly EnergyUnitFamily originalFamily = AppSettings.EnergyUnitFamily;
    readonly NumberPrecision originalPrecision = AppSettings.NumberPrecision;
    readonly UncertaintyDisplayStyle originalStyle = AppSettings.UncertaintyDisplayStyle;
    readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;

    public AnalysisResultModelOptionPresentationTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        AppSettings.DefaultConcentrationUnit = ConcentrationUnit.µM;
        AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
        AppSettings.NumberPrecision = NumberPrecision.SingleDecimal;
        AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.ConfidenceInterval;
    }

    [Theory]
    [InlineData(UncertaintyDisplayStyle.Automatic, "1.0 [0.1, 10.0] µM")]
    [InlineData(UncertaintyDisplayStyle.ConfidenceInterval, "1.0 [0.1, 10.0] µM")]
    [InlineData(UncertaintyDisplayStyle.StandardDeviation, "1.0 ± 0.2 µM")]
    [InlineData(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval, "1.0 ± 0.2 [0.1, 10.0] µM")]
    [InlineData(UncertaintyDisplayStyle.None, "1.0 µM")]
    public void KdUsesTransformedEndpointsAndAsymmetry(UncertaintyDisplayStyle style, string expected)
    {
        AppSettings.UncertaintyDisplayStyle = style;
        // Independently: Kd = 10^-logKa; 10^-6 M = 1 µM and [10^-7, 10^-5] M = [0.1, 10] µM.
        Assert.Equal(expected, Format(AttributeKey.PreboundLigandAffinity, new FloatWithError(6, .1, 5, 7)));
    }

    [Theory]
    [InlineData(ConcentrationUnit.µM, "1 [0.1, 10] µM")]
    [InlineData(ConcentrationUnit.nM, "1000 [100, 10000] nM")]
    public void KdUsesSelectedConcentrationUnit(ConcentrationUnit unit, string expected)
    {
        AppSettings.DefaultConcentrationUnit = unit;
        AppSettings.NumberPrecision = NumberPrecision.AllDecimals;
        Assert.Equal(expected, Format(AttributeKey.PreboundLigandAffinity, new FloatWithError(6, .1, 5, 7)));
    }

    [Theory]
    [InlineData(NumberPrecision.Strict, "-32 [-35, -20] kJ/mol")]
    [InlineData(NumberPrecision.Standard, "-32 [-35, -20] kJ/mol")]
    [InlineData(NumberPrecision.SingleDecimal, "-32.0 [-35.0, -20.0] kJ/mol")]
    [InlineData(NumberPrecision.AllDecimals, "-32 [-35, -20] kJ/mol")]
    public void EnergyUsesSelectedPrecision(NumberPrecision precision, string expected)
    {
        AppSettings.NumberPrecision = precision;
        Assert.Equal(expected, Format(AttributeKey.PreboundLigandEnthalpy, new FloatWithError(-32000, 2000, -35000, -20000)));
    }

    [Theory]
    [InlineData(UncertaintyDisplayStyle.Automatic, "-32.0 [-35.0, -20.0] kJ/mol", "2.0 [1.0, 5.0] µM", "25.0 [22.0, 40.0] %")]
    [InlineData(UncertaintyDisplayStyle.ConfidenceInterval, "-32.0 [-35.0, -20.0] kJ/mol", "2.0 [1.0, 5.0] µM", "25.0 [22.0, 40.0] %")]
    [InlineData(UncertaintyDisplayStyle.StandardDeviation, "-32.0 ± 2.0 kJ/mol", "2.0 ± 0.2 µM", "25.0 ± 1.0 %")]
    [InlineData(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval, "-32.0 ± 2.0 [-35.0, -20.0] kJ/mol", "2.0 ± 0.2 [1.0, 5.0] µM", "25.0 ± 1.0 [22.0, 40.0] %")]
    [InlineData(UncertaintyDisplayStyle.None, "-32.0 kJ/mol", "2.0 µM", "25.0 %")]
    public void LinearConversionsPreserveSavedIntervals(UncertaintyDisplayStyle style, string energy, string concentration, string percentage)
    {
        AppSettings.UncertaintyDisplayStyle = style;
        Assert.Equal(energy, Format(AttributeKey.PreboundLigandEnthalpy, new FloatWithError(-32000, 2000, -35000, -20000)));
        Assert.Equal(concentration, Format(AttributeKey.PreboundLigandConc, new FloatWithError(2e-6, .2e-6, 1e-6, 5e-6)));
        Assert.Equal(percentage, Format(AttributeKey.Percentage, new FloatWithError(.25, .01, .22, .40)));
    }

    [Theory]
    [InlineData(EnergyUnitFamily.Joules, -32000, 2000, -35000, -20000, "-32.0 [-35.0, -20.0] kJ/mol")]
    [InlineData(EnergyUnitFamily.Joules, -32, 2, -35, -20, "-32.0 [-35.0, -20.0] J/mol")]
    [InlineData(EnergyUnitFamily.Calories, -41840, 4184, -50208, -20920, "-10.0 [-12.0, -5.0] kcal/mol")]
    [InlineData(EnergyUnitFamily.Calories, -41.84, 4.184, -50.208, -20.920, "-10.0 [-12.0, -5.0] cal/mol")]
    public void EnergyUsesSelectedFamilyAndCentralValueScale(EnergyUnitFamily family, double value, double sd, double lower, double upper, string expected)
    {
        AppSettings.EnergyUnitFamily = family;
        // Independent SI conversion: 1 kJ = 1000 J; 1 cal = 4.184 J.
        Assert.Equal(expected, Format(AttributeKey.PreboundLigandEnthalpy, new FloatWithError(value, sd, lower, upper)));
    }

    [Fact]
    public void ConcentrationUsesSelectedUnitRatherThanAutomaticMagnitude()
    {
        AppSettings.DefaultConcentrationUnit = ConcentrationUnit.nM;
        Assert.Equal("2000.0 [1000.0, 5000.0] nM",
            Format(AttributeKey.PreboundLigandConc, new FloatWithError(2e-6, .2e-6, 1e-6, 5e-6)));
    }

    [Fact]
    public void AutomaticKeepsSymmetricUncertaintyAsSd()
    {
        AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.Automatic;
        Assert.Equal("-32.0 ± 2.0 kJ/mol",
            Format(AttributeKey.PreboundLigandEnthalpy, new FloatWithError(-32000, 2000, -36000, -28000)));
    }

    [Theory]
    [InlineData(UncertaintyDisplayStyle.Automatic)]
    [InlineData(UncertaintyDisplayStyle.StandardDeviation)]
    [InlineData(UncertaintyDisplayStyle.ConfidenceInterval)]
    [InlineData(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval)]
    [InlineData(UncertaintyDisplayStyle.None)]
    public void ZeroUncertaintyDoesNotInventError(UncertaintyDisplayStyle style)
    {
        AppSettings.UncertaintyDisplayStyle = style;
        Assert.Equal("1.0 µM", Format(AttributeKey.PreboundLigandAffinity, new FloatWithError(6)));
        Assert.Equal("0.0 kJ/mol", Format(AttributeKey.PreboundLigandEnthalpy, new FloatWithError(0)));
    }

    [Theory]
    [InlineData(AttributeKey.PreboundLigandAffinity, 6, .1, 5, 7, "1.0 [0.1, 10.0] µM")]
    [InlineData(AttributeKey.PreboundLigandEnthalpy, -32000, 2000, -35000, -20000, "-32.0 [-35.0, -20.0] kJ/mol")]
    [InlineData(AttributeKey.PreboundLigandConc, 2e-6, .2e-6, 1e-6, 5e-6, "2.0 [1.0, 5.0] µM")]
    public void SourceIndicatorCanBeReplacedBySavedMemberValue(AttributeKey key, double value, double sd, double lower, double upper, string expected)
    {
        var option = Option(key, new FloatWithError(value, sd, lower, upper));
        option.BoolValue = true;
        Assert.True(AnalysisResultModelOptionPresentation.UsesExperimentAttribute(key, option));
        Assert.Equal("From experiment attribute", AnalysisResultModelOptionPresentation.FormatValue(key, option));
        Assert.Equal(expected, AnalysisResultModelOptionPresentation.FormatValue(key, option, showAttributeSource: false));
    }

    [Fact]
    public void FormattingPreservesEverySavedComponent()
    {
        var option = Option(AttributeKey.PreboundLigandAffinity, new FloatWithError(6, .123456789, 4.87654321, 6.987654321));
        option.BoolValue = true;
        var saved = option.ParameterValue;
        var endpoints = saved.DistributionConfidence95.ToArray();
        foreach (var style in Enum.GetValues<UncertaintyDisplayStyle>())
        {
            AppSettings.UncertaintyDisplayStyle = style;
            AnalysisResultModelOptionPresentation.FormatValue(option.Key, option, showAttributeSource: false);
        }
        Assert.Equal(saved.Value, option.ParameterValue.Value);
        Assert.Equal(saved.SD, option.ParameterValue.SD);
        Assert.Equal(endpoints, option.ParameterValue.DistributionConfidence95);
        Assert.Equal(saved.IsAsymmetric, option.ParameterValue.IsAsymmetric);
        Assert.True(option.BoolValue);
    }

    [Fact]
    public void NonNumericOptionsKeepTheirExistingDisplay()
    {
        var stoichiometry = ExperimentAttribute.FromKey(AttributeKey.NumberOfSites1);
        stoichiometry.DoubleValue = 1;
        Assert.Equal(StoichiometryOptions.FormatAsTitle(1), AnalysisResultModelOptionPresentation.FormatValue(stoichiometry.Key, stoichiometry));
        var flag = ExperimentAttribute.FromKey(AttributeKey.UseSyringeActiveFraction);
        flag.BoolValue = true;
        Assert.Equal("Yes", AnalysisResultModelOptionPresentation.FormatValue(flag.Key, flag));
        Assert.False(AnalysisResultModelOptionPresentation.UsesExperimentAttribute(flag.Key, flag));
        Assert.Equal("", AnalysisResultModelOptionPresentation.FormatValue(AttributeKey.PreboundLigandAffinity, null));
    }

    static string Format(AttributeKey key, FloatWithError value) => AnalysisResultModelOptionPresentation.FormatValue(key, Option(key, value));

    static ExperimentAttribute Option(AttributeKey key, FloatWithError value)
    {
        var option = ExperimentAttribute.FromKey(key);
        option.BoolValue = false;
        option.ParameterValue = value;
        return option;
    }

    public void Dispose()
    {
        AppSettings.DefaultConcentrationUnit = originalUnit;
        AppSettings.EnergyUnitFamily = originalFamily;
        AppSettings.NumberPrecision = originalPrecision;
        AppSettings.UncertaintyDisplayStyle = originalStyle;
        CultureInfo.CurrentCulture = originalCulture;
    }
}
