using System;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisReportFooterLayoutTests
{
    [Fact]
    public void OrdinaryTitleKeepsItsOriginalContentMidpointAndSideFields()
    {
        var footer = Create("Short title", 120);
        Assert.Equal("FT-ITC 1", footer.ProvenanceText);
        Assert.Equal("Page 2 of 2", footer.PageNumberText);
        Assert.Equal("Short title", footer.TitleText);
        Assert.Equal(10, footer.ProvenanceX);
        Assert.Equal(89, footer.PageNumberX);
        Assert.Equal(55 - 11 / 2.0, footer.TitleX);
    }

    [Theory]
    [InlineData("FT-ITC", 52)]
    [InlineData("A much longer software name", 16)]
    public void TitleReservesBothSideFieldsAndStaysCentered(string creator, int availableWidth)
    {
        var footer = Create(new string('W', 200), 120, creator: creator);
        Assert.Equal(new string('W', availableWidth - 1) + "…", footer.TitleText);
        Assert.Equal(55 - availableWidth / 2.0, footer.TitleX);
        Assert.True(footer.TitleX >= footer.ProvenanceX + footer.ProvenanceText.Length + 8);
        Assert.True(footer.TitleX + footer.TitleText.Length <= footer.PageNumberX - 8);
    }

    [Theory]
    [InlineData("e\u0301e\u0301e\u0301", "e\u0301…")]
    [InlineData("😀😀😀", "😀…")]
    public void TruncationDoesNotSplitCombiningCharactersOrSurrogatePairs(string title, string expected)
    {
        // Four available UTF-16 units would allow a naive cut inside the second element.
        var footer = Create(title, 72);
        Assert.Equal(expected, footer.TitleText);
        Assert.Equal(31 - expected.Length / 2.0, footer.TitleX);
    }

    [Theory]
    [InlineData(68, "")]
    [InlineData(68.5, "")]
    [InlineData(69, "…")]
    public void TitleIsOmittedWhenEvenAnEllipsisCannotFit(double pageWidth, string expected)
    {
        Assert.Equal(expected, Create("Long title", pageWidth).TitleText);
    }

    [Fact]
    public void TitleIsOmittedIfProvenanceOccupiesTheCenter()
    {
        var footer = Create("Long title", 120, creator: new string('W', 80));
        Assert.Equal("", footer.TitleText);
        Assert.Equal("Page 2 of 2", footer.PageNumberText);
        Assert.Equal(89, footer.PageNumberX);
    }

    [Theory]
    [InlineData("  First\r\nsecond\tthird\rfourth\nfifth  ", "First second third fourth fifth")]
    [InlineData("\r\n\t  ", "")]
    [InlineData(null, "")]
    public void TitleIsNormalizedToOneLine(string title, string expected)
    {
        Assert.Equal(expected, Create(title, 200).TitleText);
    }

    [Fact]
    public void CoverOmitsTitleAndBlankVersionAddsNoTrailingSpace()
    {
        var footer = Create("A cover title", 120, cover: true, version: " ");
        Assert.Equal("FT-ITC", footer.ProvenanceText);
        Assert.Equal("Page 1 of 2", footer.PageNumberText);
        Assert.Equal("", footer.TitleText);
    }

    static AnalysisReportFooterLayout Create(string title, double pageWidth,
        string creator = "FT-ITC", string version = "1", bool cover = false)
    {
        var document = new AnalysisReportDocument { Title = title, Creator = creator, ApplicationVersion = version };
        var coverPage = new AnalysisReportPagePlan(1, pageWidth, 842, "Cover", "", "", "");
        var bodyPage = new AnalysisReportPagePlan(2, pageWidth, 842, "Body", "", "", "");
        var plan = new AnalysisReportLayoutPlan(pageWidth, 842, 10, 20, 20, 20, new[] { coverPage, bodyPage });
        return AnalysisReportFooterLayout.Create(document, plan, cover ? coverPage : bodyPage, new UnitTextMeasurer());
    }

    sealed class UnitTextMeasurer : IAnalysisReportTextMeasurer
    {
        public AnalysisReportSize Measure(string text, AnalysisReportTextStyle style) => new(text.Length, style.FontSize);
    }
}
