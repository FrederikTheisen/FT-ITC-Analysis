using System;
using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

public sealed class FigureCanvasPanelHeadingTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    public void RenderedPanelLetterKeepsItsSizeWhenBaseFontChanges(double baseSize)
    {
        var expected = RenderedLetterHeight(10);
        var actual = RenderedLetterHeight(baseSize);

        Assert.True(actual > 0);
        Assert.InRange(Math.Abs(actual - expected), 0, 0.3);
    }

    static double RenderedLetterHeight(double baseSize)
    {
        var experiment = new ExperimentData("letter.itc") { Name = "" };
        var document = PublicationFigureCanvasBuilder.Build(new[] { experiment },
            new PublicationFigureOptions { ShowThermogram = false, ShowAxisTitles = false },
            new PublicationFigureCanvasOptions
            {
                Columns = 1, Rows = 1, FontSize = baseSize, ShowPanelTitles = true,
                ShowInformationBoxes = false
            });
        var renderer = new SkiaFigureCanvasRenderer();
        var plan = renderer.CreatePlan(document);
        using var bitmap = renderer.RenderBitmap(plan, (int)Math.Ceiling(plan.CanvasWidth * 4));
        var scale = bitmap.Height / (double)plan.CanvasHeight;
        var horizontalScale = bitmap.Width / (double)plan.CanvasWidth;
        var first = bitmap.Height;
        var last = -1;
        // Stay above the upper tick label, which extends into the heading strip at large base sizes.
        for (var y = 0; y < (int)(12 * scale); y++)
            for (var x = 0; x < (int)(12 * horizontalScale); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 100 || pixel.Green > 100 || pixel.Blue > 100) continue;
                first = Math.Min(first, y);
                last = Math.Max(last, y);
            }
        return last < first ? 0 : (last - first + 1) / scale;
    }

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
