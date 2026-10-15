using System.Linq;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class HelpContentTests
    {
        [Theory]
        [InlineData("\n")]
        [InlineData("\r\n")]
        public void DisplayEquationsPreserveSurroundingTextAndScientificMarkup(string newline)
        {
            var before = "## Section" + newline + "Text with $5 inline." + newline;
            var equation = @"\sigma{q,i}^2^ = *x*" + newline + "*y* = 2";
            var after = newline + "# Next heading" + newline + "Following text.";
            var input = before + "$$" + newline + equation + newline + "$$" + newline + after;

            var blocks = HelpContent.Parse(input);

            Assert.Equal(3, blocks.Count);
            Assert.False(blocks[0].IsEquation);
            Assert.Equal(before, blocks[0].Text);
            Assert.True(blocks[1].IsEquation);
            Assert.Equal(equation, blocks[1].Text);
            Assert.False(blocks[2].IsEquation);
            Assert.Equal(after, blocks[2].Text);
        }

        [Fact]
        public void SeparateEquationsAndInlineDollarSignsAreNotMerged()
        {
            var blocks = HelpContent.Parse("Inline $$ stays text.\n$$\na = 1\n$$\n$$\nb = 2\n$$");

            Assert.Equal(new[] { "a = 1", "b = 2" }, blocks.Where(block => block.IsEquation).Select(block => block.Text));
            Assert.Equal("Inline $$ stays text.\n", blocks[0].Text);
        }

        [Fact]
        public void UnclosedFenceDoesNotLoseText()
        {
            const string text = "Text\n$$\nx = 1";
            var block = Assert.Single(HelpContent.Parse(text));
            Assert.False(block.IsEquation);
            Assert.Equal(text, block.Text);
            Assert.Empty(HelpContent.Parse(null));
        }
    }
}
