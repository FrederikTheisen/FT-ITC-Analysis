using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

public sealed class FigureCanvasPanelHeadingTests
{
    const float Size = 9f;
    static readonly SkiaPublicationFontSet Fonts = SkiaPublicationFontResolver.Shared.Resolve(PublicationFont.Inter);
    static readonly PublicationFigureCanvasOptions Options = new() { ShowPanelLetters = true, ShowPanelTitles = true };

    static PublicationFigureCanvasCell Cell(string title) => new(null, 0, 0, 0, 0, "1A", title);

    static float HeadingWidth((string Label, string Title) heading) =>
        SkiaDrawingContext.MeasureTextValue(heading.Label, Size, Fonts, bold: true).Width
        + SkiaDrawingContext.MeasureTextValue(" ", Size, Fonts).Width
        + SkiaDrawingContext.MeasureTextValue(heading.Title, Size, Fonts).Width;

    [Fact]
    public void ShortTitleIsDrawnInFull()
    {
        var heading = SkiaFigureCanvasRenderer.PanelHeading(Cell("C1 run03"), Options, 200, Size, Fonts);

        Assert.Equal(("1A", "C1 run03"), heading);
    }

    [Fact]
    public void LongTitleIsMiddleShortenedToFitThePanelWidth()
    {
        const string name = "C1_20250126_Lysozyme_NAG_25C_titration_run03";
        const float width = 130;

        var heading = SkiaFigureCanvasRenderer.PanelHeading(Cell(name), Options, width, Size, Fonts);

        Assert.Equal("1A", heading.Label);
        Assert.Contains("…", heading.Title);
        Assert.StartsWith("C1_", heading.Title);
        Assert.EndsWith("run03", heading.Title);
        Assert.True(HeadingWidth(heading) <= width);
    }

    [Fact]
    public void WiderPanelsKeepMoreOfTheTitle()
    {
        const string name = "C1_20250126_Lysozyme_NAG_25C_titration_run03";

        var narrow = SkiaFigureCanvasRenderer.PanelHeading(Cell(name), Options, 130, Size, Fonts);
        var wide = SkiaFigureCanvasRenderer.PanelHeading(Cell(name), Options, 210, Size, Fonts);

        Assert.True(wide.Title.Length > narrow.Title.Length);
    }
}
