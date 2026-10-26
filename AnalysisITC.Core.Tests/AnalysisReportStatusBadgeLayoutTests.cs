using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisReportStatusBadgeLayoutTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var health in Enum.GetValues<AnalysisResultHealth>())
            foreach (var cover in new[] { false, true })
                foreach (var subtitle in new[] { "", "Conditions beneath the complete heading" })
                    yield return new object[] { health, cover, subtitle };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TitlesReserveTheMeasuredStampAndKeepEveryLine(AnalysisResultHealth health, bool cover, string subtitle)
    {
        var measurer = new Measurer();
        var title = "Result 1. " + string.Join(" ", Enumerable.Repeat("Long result title", 8));
        var document = new AnalysisReportDocument { Title = title, ResultHealth = health };
        var section = new AnalysisReportSection(cover ? AnalysisReportSectionKind.Cover : AnalysisReportSectionKind.ResultOverview,
            "heading", title, AnalysisReportLayoutPolicy.None, statusBadgeHealth: health, subtitle: subtitle);
        section.Add(new AnalysisReportTextBlock("", "Following content", AnalysisReportLayoutPolicy.None));
        document.AddSection(section);
        var plan = AnalysisReportLayoutEngine.Paginate(document, measurer);
        var fragment = plan.Pages.SelectMany(page => page.Fragments).Single(item => item.Kind == AnalysisReportFragmentKind.SectionTitle);
        var badgeText = health switch
        {
            AnalysisResultHealth.Valid => "ANALYSIS VALID",
            AnalysisResultHealth.Warning => "REVIEW WARNINGS",
            AnalysisResultHealth.PartialInvalid => "PARTIAL / STALE",
            AnalysisResultHealth.Invalid => "INVALID / STALE",
            _ => "STATUS UNKNOWN",
        };
        var titleStyle = new AnalysisReportTextStyle(17, true);
        var available = fragment.Bounds.Width - measurer.Measure(badgeText, new AnalysisReportTextStyle(6.5, true)).Width - 16 - 8;
        Assert.All(fragment.Lines, line => Assert.True(measurer.Measure(line, titleStyle).Width <= available,
            "A title line enters the reserved stamp area."));
        Assert.Equal(title, string.Join(" ", fragment.Lines));
        Assert.True(fragment.Bounds.Height >= fragment.Lines.Count * 17 * 1.34,
            "The heading must allocate the line advance used to draw every title line.");
        if (subtitle.Length > 0)
        {
            Assert.Equal(fragment.Bounds.Width, fragment.HeadingLayout.SubtitleBounds.Width);
            Assert.Equal(subtitle, string.Join(" ", fragment.HeadingLayout.SubtitleLines));
        }
        var following = plan.Pages.SelectMany(page => page.Fragments).Single(item => item.Block != null);
        Assert.True(following.Bounds.Y >= fragment.Bounds.Bottom + 9);
    }

    [Fact]
    public void HeadingsWithoutAStampRetainTheFullWidth()
    {
        var document = new AnalysisReportDocument();
        var section = new AnalysisReportSection(AnalysisReportSectionKind.Experiment, "experiment", "Experiment heading",
            AnalysisReportLayoutPolicy.None, subtitle: "Conditions");
        section.Add(new AnalysisReportTextBlock("", "Content", AnalysisReportLayoutPolicy.None));
        document.AddSection(section);
        var plan = AnalysisReportLayoutEngine.Paginate(document, new Measurer());
        var heading = plan.Pages.Single().Fragments.First();
        Assert.Equal(heading.Bounds.Width, heading.HeadingLayout.TitleBounds.Width);
    }

    [Fact]
    public void ValidMultiResultCoverMeasuresThePluralStamp()
    {
        var document = new AnalysisReportDocument();
        foreach (var id in new[] { "first", "second" })
            document.AddResult(new AnalysisReportResultReference(id, id, id, DateTime.MinValue, "", 1, AnalysisResultHealth.Valid));
        var section = new AnalysisReportSection(AnalysisReportSectionKind.Cover, "cover", "Combined analyses", AnalysisReportLayoutPolicy.None);
        section.Add(new AnalysisReportTextBlock("", "Content", AnalysisReportLayoutPolicy.None));
        document.AddSection(section);
        var heading = AnalysisReportLayoutEngine.Paginate(document, new Measurer()).Pages.Single().Fragments.First();
        var stamp = heading.HeadingLayout.StatusBadge;
        Assert.Equal("ANALYSES VALID", stamp.Text);
        Assert.Equal(new Measurer().Measure("ANALYSES VALID", new AnalysisReportTextStyle(6.5, true)).Width + 16, stamp.Bounds.Width);
        Assert.Equal(8, stamp.Bounds.X - heading.HeadingLayout.TitleBounds.Right, 6);
    }

    [Fact]
    public void WrappedTitleAndStampMoveTogetherAtThePageBoundary()
    {
        var document = new AnalysisReportDocument();
        document.PageSettings.HeightCentimeters = 8;
        var preceding = new AnalysisReportSection(AnalysisReportSectionKind.Appendix, "before", "Earlier content", AnalysisReportLayoutPolicy.None);
        preceding.Add(new AnalysisReportTextBlock("", "Filler\nFiller\nFiller\nFiller", AnalysisReportLayoutPolicy.None));
        document.AddSection(preceding);
        var title = new AnalysisReportSection(AnalysisReportSectionKind.ResultOverview, "result",
            string.Join(" ", Enumerable.Repeat("Long result title", 7)), AnalysisReportLayoutPolicy.None,
            statusBadgeHealth: AnalysisResultHealth.Warning);
        title.Add(new AnalysisReportTextBlock("", "Following content", AnalysisReportLayoutPolicy.None));
        document.AddSection(title);
        var plan = AnalysisReportLayoutEngine.Paginate(document, new Measurer());
        var page = plan.Pages.Single(item => item.Fragments.Any(fragment => fragment.Section == title));
        var heading = page.Fragments.Single(item => item.Section == title);
        Assert.True(page.PageNumber > 1);
        Assert.Equal(plan.MarginTop, heading.Bounds.Y);
        Assert.Equal(heading.HeadingLayout.Height, heading.Bounds.Height);
        Assert.True(heading.Bounds.Bottom <= page.Height - plan.MarginBottom - 18);
        Assert.True(heading.HeadingLayout.StatusBadge.Bounds.Bottom <= heading.Bounds.Height);
    }

    sealed class Measurer : IAnalysisReportTextMeasurer
    {
        public AnalysisReportSize Measure(string text, AnalysisReportTextStyle style) => new(text.Length * style.FontSize / 2, style.FontSize);
    }
}
