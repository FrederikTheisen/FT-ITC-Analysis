using System.Linq;

using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class PublicationFigureCanvasPanelTitleTests
    {
        static double Monospace(string text) => text.Length;

        // 'W' is twice as wide as every other character, standing in for a proportional font.
        static double Proportional(string text) => text.Sum(character => character == 'W' ? 2.0 : 1.0);

        [Fact]
        public void TitleThatFitsIsUnchanged()
        {
            Assert.Equal("Lysozyme run03", PublicationFigureCanvasBuilder.FitPanelTitle("Lysozyme run03", 14, Monospace));
        }

        [Fact]
        public void LongTitleKeepsBothEndsAndUsesTheFullWidth()
        {
            // 31 characters into 25: 24 kept, 12 from each end around the ellipsis.
            Assert.Equal("20250126_Lys…AG_25C_run03",
                PublicationFigureCanvasBuilder.FitPanelTitle("20250126_Lysozyme_NAG_25C_run03", 25, Monospace));
        }

        [Fact]
        public void ProportionalWidthsChooseTheLongestFittingTitle()
        {
            // Keeping 9 characters gives 5 W (10) + ellipsis (1) + 4 a (4) = 15; keeping 10 gives 16.
            Assert.Equal("WWWWW…aaaa",
                PublicationFigureCanvasBuilder.FitPanelTitle("WWWWWWWWWWaaaaaaaaaa", 15, Proportional));
        }

        [Fact]
        public void TitleCollapsesToEllipsisThenEmptyWhenNoSpaceRemains()
        {
            Assert.Equal("…", PublicationFigureCanvasBuilder.FitPanelTitle("Lysozyme", 1, Monospace));
            Assert.Equal("", PublicationFigureCanvasBuilder.FitPanelTitle("Lysozyme", 0.5, Monospace));
        }

        [Fact]
        public void CanvasCellsKeepTheFullExperimentName()
        {
            const string name = "20250126_Lysozyme_NAG_25C_run03_with_a_very_long_suffix";
            var document = PublicationFigureCanvasBuilder.Build(
                new[] { (Data.ITCDataContainer)new Data.ExperimentData(name + ".itc") },
                new PublicationFigureOptions(),
                new PublicationFigureCanvasOptions { ShowPanelTitles = true });

            Assert.True(document.IsValid, document.ValidationError);
            Assert.Equal(name, document.Cells.Single().PanelTitle);
        }
    }
}
