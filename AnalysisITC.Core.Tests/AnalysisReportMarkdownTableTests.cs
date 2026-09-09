using System.Linq;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public class AnalysisReportMarkdownTableTests
{
    [Fact]
    public void ParsesHeaderAlignmentEscapedPipesAndShortRowsWithoutLosingFollowingText()
    {
        var document = AnalysisReportBuilder.BuildInterpretationPreview(
            "## Overall interpretation\nBefore.\n\n| Experiment | Kd | Assessment |\n| :--- | ---: | :---: |\n| **Experiment 1A** | 4.1 | left \\| right |\n| 1B | 4.0 |\n\nAfter.");
        var blocks = document.Sections.SelectMany(s => s.Blocks).ToList();
        var table = Assert.Single(blocks.OfType<AnalysisReportTableBlock>());
        Assert.True(table.InlineMarkdown);
        Assert.Equal(new[] { AnalysisResultColumnAlignment.Left, AnalysisResultColumnAlignment.Right, AnalysisResultColumnAlignment.Center }, table.Columns.Select(c => c.Alignment));
        Assert.Equal(new[] { "**Experiment 1A**", "4.1", "left | right" }, table.Rows[0].Cells);
        Assert.Equal(new[] { "1B", "4.0", "" }, table.Rows[1].Cells);
        Assert.Contains(blocks.OfType<AnalysisReportTextBlock>(), b => b.Text == "Before.");
        Assert.Contains(blocks.OfType<AnalysisReportTextBlock>(), b => b.Text == "After.");
    }

    [Theory]
    [InlineData("A | B\n--- | ---\n1 | 2")]
    [InlineData("| A | B |\n| --- | --- |\n| 1 | 2 |")]
    public void OuterPipesAreOptional(string markdown)
    {
        var doc = AnalysisReportBuilder.BuildInterpretationPreview(markdown);
        var table = Assert.Single(doc.Sections.SelectMany(s => s.Blocks).OfType<AnalysisReportTableBlock>());
        Assert.Equal(new[] { "1", "2" }, Assert.Single(table.Rows).Cells);
    }

    [Fact]
    public void MalformedTablesAndExtraCellsRemainVisibleText()
    {
        var doc = AnalysisReportBuilder.BuildInterpretationPreview("A | B\n--- | wrong\n1 | 2\n\nA | B\n--- | ---\n1 | 2 | retain me");
        var text = string.Join("\n", doc.Sections.SelectMany(s => s.Blocks).OfType<AnalysisReportTextBlock>().Select(b => b.Text));
        Assert.Contains("--- | wrong", text);
        Assert.Contains("retain me", text);
    }
}
