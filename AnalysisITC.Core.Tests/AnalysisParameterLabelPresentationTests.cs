using System.Collections.Generic;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisParameterLabelPresentationTests
{
    [Theory]
    [InlineData(AnalysisModel.OneSetOfSites)]
    [InlineData(AnalysisModel.TwoSetsOfSites)]
    [InlineData(AnalysisModel.CompetitiveBinding)]
    public void FirstParameterChangesMeaningWithSyringeCorrection(AnalysisModel model)
    {
        var options = ModelOptionCatalog.CreateOptions(model);
        foreach (var enabled in new[] { false, true, false })
        {
            options[AttributeKey.UseSyringeActiveFraction].BoolValue = enabled;
            var label = AnalysisParameterLabelPresentation.Create(ParameterType.Nvalue1, options);
            Assert.Equal(enabled ? "Correction factor" : "N-value", label.Title);
            Assert.Equal(enabled ? "α" : "N", label.Symbol);
            Assert.Equal(enabled
                ? "Scales delivered titrant concentration during model evaluation."
                : "Apparent binding sites per macromolecule in the cell.", label.ToolTip);
            Assert.Equal(1, options[AttributeKey.NumberOfSites1].DoubleValue);
        }
    }

    [Fact]
    public void MissingOrDisabledCorrectionKeepsStoichiometryAndSiteIndices()
    {
        foreach (var options in new IDictionary<AttributeKey, ExperimentAttribute>[]
        {
            null,
            new Dictionary<AttributeKey, ExperimentAttribute>(),
            new Dictionary<AttributeKey, ExperimentAttribute> { [AttributeKey.UseSyringeActiveFraction] = null },
        })
        {
            var label = AnalysisParameterLabelPresentation.Create(ParameterType.Nvalue1, options, true);
            Assert.Equal("N-value", label.Title);
            Assert.Equal("N{1}", label.Symbol);
        }
    }

    [Fact]
    public void CorrectionDoesNotRenameOtherParametersOrTheSecondSite()
    {
        var options = ModelOptionCatalog.CreateOptions(AnalysisModel.TwoSetsOfSites);
        options[AttributeKey.UseSyringeActiveFraction].BoolValue = true;
        var second = AnalysisParameterLabelPresentation.Create(ParameterType.Nvalue2, options, true);
        Assert.Equal("N-value 2", second.Title);
        Assert.Equal("N{2}", second.Symbol);
        Assert.Equal("Apparent cell stoichiometry for the second site class.", second.ToolTip);
        var enthalpy = AnalysisParameterLabelPresentation.Create(ParameterType.Enthalpy1, options);
        Assert.Equal("Enthalpy", enthalpy.Title);
        Assert.Null(enthalpy.ToolTip);
    }
}
