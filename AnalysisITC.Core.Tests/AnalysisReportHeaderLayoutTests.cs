using System;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisReportHeaderLayoutTests
{
    [Theory]
    [InlineData("Alpha", "1A", "Experiment", "FT-ITC Analysis · Alpha · 1A. Experiment")]
    [InlineData("Alpha", "1A", "", "FT-ITC Analysis · Alpha · 1A.")]
    [InlineData("", "1A", "Experiment", "FT-ITC Analysis · 1A. Experiment")]
    [InlineData("Alpha", "", "", "FT-ITC Analysis · Alpha")]
    [InlineData(" ", "", "", "FT-ITC Analysis")]
    public void ShortAndEmptyNamesProduceCleanHeaders(string result, string label, string name, string expected)
    {
        Assert.Equal(expected, Create(result, label, name, 500).ContextText);
    }

    [Fact]
    public void ExperimentNameUsesSpaceAfterTheCompleteResultName()
    {
        const string expected = "FT-ITC Analysis · Alpha · 1A. Exper…";
        var header = Create("Alpha", "1A", "Experiment with a long name", expected.Length);
        Assert.Equal(expected, header.ContextText);
        AssertDateAndGap(header);
    }

    [Fact]
    public void ResultNameIsTruncatedOnlyEnoughToRetainTheExperimentLabel()
    {
        const string expected = "FT-ITC Analysis · Long res… · 1A.";
        var header = Create("Long result name that consumes the header", "1A", "Long experiment name", expected.Length);
        Assert.Equal(expected, header.ContextText);
        AssertDateAndGap(header);
    }

    [Fact]
    public void ExperimentNameIsOmittedWhenOnlyAnEllipsisFits()
    {
        const string expected = "FT-ITC Analysis · Alpha · 1A.";
        var header = Create("Alpha", "1A", "Experiment", expected.Length + 2);
        Assert.Equal(expected, header.ContextText);
        AssertDateAndGap(header);
    }

    [Fact]
    public void LongResultOnANonExperimentPageStillLeavesTheDateVisible()
    {
        const string expected = "FT-ITC Analysis · Long res…";
        var header = Create("Long result name that consumes the header", "", "", expected.Length);
        Assert.Equal(expected, header.ContextText);
        AssertDateAndGap(header);
    }

    [Theory]
    [InlineData("e\u0301e\u0301e\u0301", "e\u0301…")]
    [InlineData("😀😀😀", "😀…")]
    public void TruncationDoesNotSplitUnicodeTextElements(string name, string shortened)
    {
        var experimentExpected = "FT-ITC Analysis · Alpha · 1A. " + shortened;
        // One spare UTF-16 code unit would permit cutting the next text element in half.
        var experiment = Create("Alpha", "1A", name, experimentExpected.Length + 1);
        Assert.Equal(experimentExpected, experiment.ContextText);
        AssertDateAndGap(experiment);

        var resultExpected = "FT-ITC Analysis · " + shortened + " · 1A.";
        var result = Create(name, "1A", "Experiment", resultExpected.Length + 1);
        Assert.Equal(resultExpected, result.ContextText);
        AssertDateAndGap(result);
    }

    [Fact]
    public void CoverKeepsItsExistingBrandAndTypography()
    {
        var document = Document();
        var page = new AnalysisReportPagePlan(1, 595, 842, document.Title, "Alpha", "1A", "Experiment");
        var header = AnalysisReportHeaderLayout.Create(document, page, 0, 500, new UnitTextMeasurer());
        Assert.Equal("FT-ITC ANALYSIS REPORT", header.ContextText);
        Assert.Equal(7.5, header.ContextStyle.FontSize);
        Assert.True(header.ContextStyle.Bold);
        AssertDateAndGap(header);
    }

    static AnalysisReportHeaderLayout Create(string result, string label, string name, double contextWidth)
    {
        var document = Document();
        var page = new AnalysisReportPagePlan(2, 595, 842, document.Title, result, label, name);
        return AnalysisReportHeaderLayout.Create(document, page, 0,
            contextWidth + 8 + document.ExportDateText.Length, new UnitTextMeasurer());
    }

    static AnalysisReportDocument Document() => new()
    {
        Title = "Header tests",
        GeneratedAtUtc = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc),
    };

    static void AssertDateAndGap(AnalysisReportHeaderLayout header)
    {
        Assert.Equal(Document().ExportDateText, header.ExportDateText);
        Assert.True(header.ContextText.Length + 8 <= header.ExportDateX);
    }

    sealed class UnitTextMeasurer : IAnalysisReportTextMeasurer
    {
        public AnalysisReportSize Measure(string text, AnalysisReportTextStyle style) => new(text.Length, style.FontSize);
    }
}
