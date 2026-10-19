using System;
using System.Linq;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisReportPaginationTests
{
    [Theory]
    [InlineData(-1e-10)]
    [InlineData(0)]
    [InlineData(1e-10)]
    public void NearOneLineRemainderNeverCreatesAnEmptyNoticeFragment(double adjustment)
    {
        var lines = Enumerable.Range(1, 8).Select(index => "Warning " + index).ToArray();
        var preceding = new AnalysisReportTextBlock("", "Before", AnalysisReportLayoutPolicy.KeepTogether);
        var notice = new AnalysisReportNoticeBlock("Warnings", string.Join("\n", lines), AnalysisReportNoticeLevel.Warning);
        var document = Document(preceding, notice);
        // Section + preceding text + their spacing = 55.2 pt; one notice line needs 37.06 pt.
        document.PageSettings.HeightCentimeters = (55.2 + 37.06 + adjustment + 18) / AnalysisReportLayoutEngine.PointsPerCentimeter
            + document.PageSettings.MarginTopCentimeters + document.PageSettings.MarginBottomCentimeters;
        var plan = AnalysisReportLayoutEngine.Paginate(document, new Measurer());
        var fragments = plan.Pages.SelectMany(page => page.Fragments).Where(fragment => fragment.Block == notice).ToList();
        Assert.All(fragments, fragment => Assert.NotEmpty(fragment.Lines));
        Assert.Equal(lines, fragments.SelectMany(fragment => fragment.Lines));
        Assert.Equal("Warnings", fragments.First().Title);
        Assert.All(fragments.Skip(1), fragment => Assert.Equal("Warnings – continued", fragment.Title));
        AssertSafeContent(plan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactFreshPageHeightDoesNotDropTheFinalLine(bool keyValue)
    {
        var lines = Enumerable.Range(1, 60).Select(index => "Line " + index).ToArray();
        AnalysisReportBlock block = keyValue
            ? new AnalysisReportKeyValueBlock("Details", new[] { new AnalysisReportKeyValueItem("Label", string.Join("\n", lines)) })
            : new AnalysisReportNoticeBlock("Warnings", string.Join("\n", lines), AnalysisReportNoticeLevel.Warning);
        var document = Document(block);
        var height = keyValue ? 18 + 60 * 12 + 6 : 25 + 60 * 12.06;
        document.PageSettings.HeightCentimeters = (height + 18) / AnalysisReportLayoutEngine.PointsPerCentimeter
            + document.PageSettings.MarginTopCentimeters + document.PageSettings.MarginBottomCentimeters;
        var plan = AnalysisReportLayoutEngine.Paginate(document, new Measurer());
        var fragment = Assert.Single(plan.Pages.SelectMany(page => page.Fragments), item => item.Block == block);
        Assert.Equal(lines, keyValue ? fragment.KeyValueLayout.Rows.Single().ValueLines : fragment.Lines);
        AssertSafeContent(plan);
    }

    [Fact]
    public void OversizedNoticeKeepsEveryLineAndContinuesBeforeFollowingContent()
    {
        var lines = Enumerable.Range(1, 160).Select(index => "Warning " + index + ": ΔH e\u0301 🧪").ToArray();
        var notice = new AnalysisReportNoticeBlock("Report warnings", string.Join("\n", lines), AnalysisReportNoticeLevel.Warning);
        var following = new AnalysisReportKeyValueBlock("After warnings", new[] { new AnalysisReportKeyValueItem("Final", "Visible") });
        var document = Document(notice, following);
        var plan = AnalysisReportLayoutEngine.Paginate(document, new Measurer());
        var fragments = plan.Pages.SelectMany(page => page.Fragments).Where(fragment => fragment.Block == notice).ToList();
        Assert.True(fragments.Count >= 3);
        Assert.Equal(lines, fragments.SelectMany(fragment => fragment.Lines));
        var offset = 0;
        foreach (var fragment in fragments)
        {
            Assert.Equal(offset, fragment.FirstItem);
            Assert.Equal(offset == 0 ? "Report warnings" : "Report warnings – continued", fragment.Title);
            Assert.Equal(25 + fragment.Lines.Count * 12.06, fragment.Bounds.Height, 6);
            offset += fragment.ItemCount;
        }
        AssertSafeContent(plan);
    }

    [Fact]
    public void OversizedFirstKeyValueRowUsesCompleteLineSlicesAndContinuationState()
    {
        var labels = Enumerable.Range(1, 100).Select(index => "Label " + index).ToArray();
        var values = Enumerable.Range(1, 180).Select(index => "Value " + index + " ΔG e\u0301 🧪").ToArray();
        var block = new AnalysisReportKeyValueBlock("Long metadata", new[]
        {
            new AnalysisReportKeyValueItem(string.Join("\n", labels), string.Join("\n", values)),
            new AnalysisReportKeyValueItem("Following row", "Last value"),
        });
        var plan = AnalysisReportLayoutEngine.Paginate(Document(block), new Measurer());
        var fragments = plan.Pages.SelectMany(page => page.Fragments).Where(fragment => fragment.Block == block).ToList();
        var slices = fragments.SelectMany(fragment => fragment.KeyValueLayout.Rows).Where(row => row.ItemIndex == 0).ToList();
        Assert.True(slices.Count > 2);
        Assert.Equal(labels, slices.SelectMany(row => row.LabelLines));
        Assert.Equal(values, slices.SelectMany(row => row.ValueLines));
        Assert.Equal(0, fragments[1].FirstItem);
        Assert.True(fragments[1].IsContinuation);
        Assert.Equal("Long metadata – continued", fragments[1].Title);
        Assert.Equal("Last value", fragments.Last().KeyValueLayout.Rows.Last().ValueLines.Single());
        foreach (var fragment in fragments)
            Assert.Equal(18 + fragment.KeyValueLayout.Rows.Sum(row => row.Height), fragment.Bounds.Height, 6);
        AssertSafeContent(plan);
    }

    [Fact]
    public void OrdinaryRowAndNoticeMoveIntactWhenTheyFitAFreshPage()
    {
        var preceding = new AnalysisReportTextBlock("Earlier content", string.Join("\n", Enumerable.Repeat("Text", 55)),
            AnalysisReportLayoutPolicy.AllowContinuation);
        var notice = new AnalysisReportNoticeBlock("Short notice", string.Join("\n", Enumerable.Repeat("Warning", 10)), AnalysisReportNoticeLevel.Warning);
        var block = new AnalysisReportKeyValueBlock("Details", new[]
        { new AnalysisReportKeyValueItem("Label", string.Join("\n", Enumerable.Repeat("Value", 50))) });
        var plan = AnalysisReportLayoutEngine.Paginate(Document(preceding, notice, block), new Measurer());
        var noticePage = plan.Pages.Single(page => page.Fragments.Any(fragment => fragment.Block == notice));
        Assert.Equal(plan.MarginTop, noticePage.Fragments.First(fragment => fragment.Block == notice).Bounds.Y, 6);
        Assert.Single(plan.Pages.SelectMany(page => page.Fragments), fragment => fragment.Block == block);
        AssertSafeContent(plan);
    }

    [Fact]
    public void KeyValueMeasurementUsesDrawnColumnsAndBoldLabelWidth()
    {
        var block = new AnalysisReportKeyValueBlock("Geometry", new[]
        { new AnalysisReportKeyValueItem(new string('W', 90), new string('V', 200), 1) });
        var measurer = new Measurer();
        var plan = AnalysisReportLayoutEngine.Paginate(Document(block), measurer);
        var fragment = plan.Pages.SelectMany(page => page.Fragments).Single(item => item.Block == block);
        var layout = fragment.KeyValueLayout;
        Assert.Equal(fragment.Bounds.Width * .30, layout.LabelWidth, 6);
        var row = layout.Rows.Single();
        Assert.All(row.LabelLines, line => Assert.True(measurer.Measure(line, new AnalysisReportTextStyle(9, true)).Width <= layout.LabelWidth - 6 - 14));
        Assert.All(row.ValueLines, line => Assert.True(measurer.Measure(line, new AnalysisReportTextStyle(9)).Width <= fragment.Bounds.Width * .70 - 6));
        Assert.Equal(new string('V', 200), string.Concat(row.ValueLines));
        AssertSafeContent(plan);
    }

    static AnalysisReportDocument Document(params AnalysisReportBlock[] blocks)
    {
        var document = new AnalysisReportDocument { Title = "Pagination" };
        var section = new AnalysisReportSection(AnalysisReportSectionKind.Appendix, "appendix", "Appendix", AnalysisReportLayoutPolicy.AllowContinuation);
        foreach (var block in blocks) section.Add(block);
        document.AddSection(section);
        return document;
    }

    static void AssertSafeContent(AnalysisReportLayoutPlan plan)
    {
        foreach (var page in plan.Pages)
        {
            var lastBottom = plan.MarginTop;
            foreach (var fragment in page.Fragments)
            {
                Assert.True(fragment.Bounds.Y >= lastBottom - .001);
                Assert.True(fragment.Bounds.Bottom <= page.Height - plan.MarginBottom - 18 + .001);
                lastBottom = fragment.Bounds.Bottom;
            }
        }
    }

    sealed class Measurer : IAnalysisReportTextMeasurer
    {
        public AnalysisReportSize Measure(string text, AnalysisReportTextStyle style) =>
            new AnalysisReportSize(text.Length * (style.Bold ? 5.2 : 4.5), style.FontSize);
    }
}
