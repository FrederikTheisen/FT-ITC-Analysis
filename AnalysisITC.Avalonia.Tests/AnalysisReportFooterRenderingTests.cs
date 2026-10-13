using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using SkiaSharp;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisReportFooterRenderingTests
{
    [AvaloniaTheory]
    [InlineData(PublicationFont.Native, false)]
    [InlineData(PublicationFont.Native, true)]
    [InlineData(PublicationFont.Inter, false)]
    [InlineData(PublicationFont.Inter, true)]
    [InlineData(PublicationFont.LiberationSans, false)]
    [InlineData(PublicationFont.LiberationSans, true)]
    public void LongTitlesLeaveProvenanceAndPageNumberPixelsUnchanged(PublicationFont font, bool landscape)
    {
        var previous = AppSettings.PublicationFigureFont;
        try
        {
            AppSettings.PublicationFigureFont = font;
            var renderer = new SkiaAnalysisReportRenderer();
            var measurer = (IAnalysisReportTextMeasurer)typeof(SkiaAnalysisReportRenderer)
                .GetField("measurer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
            var document = Document(landscape);
            var plan = renderer.CreatePlan(document);
            Assert.Equal(2, plan.Pages.Count);
            var page = plan.Pages[1];
            const int pixelWidth = 1600;
            using var reference = renderer.RenderPageBitmap(document, plan, 1, pixelWidth);
            using var coverReference = renderer.RenderPageBitmap(document, plan, 0, pixelWidth);
            var style = new AnalysisReportTextStyle(6.5);
            var provenanceWidth = measurer.Measure(document.Creator + " " + document.ApplicationVersion, style).Width;
            var numberWidth = measurer.Measure("Page 2 of 2", style).Width;
            foreach (var scenario in new[] { "ordinary", "long", "unicode-multiline" })
            {
                document.Title = scenario == "ordinary" ? "Binding analysis report"
                    : scenario == "long" ? new string('W', 240)
                    : string.Concat(Enumerable.Repeat("e\u0301 中文 😀\r\n\t", 90));
                using var actual = renderer.RenderPageBitmap(document, plan, 1, pixelWidth);
                using var cover = renderer.RenderPageBitmap(document, plan, 0, pixelWidth);
                using var pdf = new MemoryStream();
                renderer.WritePdf(document, plan, pdf);
                Assert.True(pdf.Length > 2000);
                WriteEvidence(font, landscape, scenario, actual, pdf);

                var scale = pixelWidth / page.Width;
                var top = page.Height - plan.MarginBottom + 4;
                var left = plan.MarginLeft;
                var right = page.Width - plan.MarginRight;
                // Compare against the same page with its title omitted. Unlike a geometry-only
                // assertion, this catches a renderer still drawing an unbounded document title.
                AssertPixelsEqual(reference, actual, left, top, provenanceWidth + 7, 12, scale,
                    scenario + ": report title overlaps software/version");
                AssertPixelsEqual(reference, actual, right - numberWidth - 7, top, numberWidth + 7, 12, scale,
                    scenario + ": report title overlaps page numbering");
                AssertPixelsEqual(coverReference, cover, left, top, right - left, 12, scale,
                    scenario + ": cover footer should omit the report title");
                Assert.True(ChangedPixels(reference, actual, left, top, right - left, 12, scale) > 0,
                    scenario + ": body footer should render its title");
            }
        }
        finally { AppSettings.PublicationFigureFont = previous; }
    }

    static AnalysisReportDocument Document(bool landscape)
    {
        var document = new AnalysisReportDocument
        {
            Creator = "FT-ITC Analysis", ApplicationVersion = "1.5.0",
            GeneratedAtUtc = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc),
        };
        if (landscape)
        {
            document.PageSettings.WidthCentimeters = AnalysisReportPageSettings.A4HeightCentimeters;
            document.PageSettings.HeightCentimeters = AnalysisReportPageSettings.A4WidthCentimeters;
        }
        foreach (var kind in new[] { AnalysisReportSectionKind.Cover, AnalysisReportSectionKind.Experiment })
        {
            var section = new AnalysisReportSection(kind, kind.ToString(), "Footer rendering QA",
                AnalysisReportLayoutPolicy.StartOnNewPage);
            section.Add(new AnalysisReportTextBlock("Report", "Footer test content", AnalysisReportLayoutPolicy.None));
            document.AddSection(section);
        }
        return document;
    }

    static void AssertPixelsEqual(SKBitmap expected, SKBitmap actual, double x, double y,
        double width, double height, double scale, string message)
    {
        var changed = ChangedPixels(expected, actual, x, y, width, height, scale);
        Assert.True(changed == 0, message + "; changed pixels: " + changed);
    }

    static int ChangedPixels(SKBitmap expected, SKBitmap actual, double x, double y,
        double width, double height, double scale)
    {
        var changed = 0;
        for (var row = (int)Math.Floor(y * scale); row < Math.Ceiling((y + height) * scale); row++)
            for (var column = (int)Math.Floor(x * scale); column < Math.Ceiling((x + width) * scale); column++)
                if (expected.GetPixel(column, row) != actual.GetPixel(column, row)) changed++;
        return changed;
    }

    static void WriteEvidence(PublicationFont font, bool landscape, string scenario, SKBitmap bitmap, MemoryStream pdf)
    {
        var directory = Environment.GetEnvironmentVariable("FTITC_REPORT_FOOTER_QA_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var name = "avalonia-footer-" + font + "-" + (landscape ? "landscape" : "portrait") + "-" + scenario;
        File.WriteAllBytes(Path.Combine(directory, name + ".pdf"), pdf.ToArray());
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), png.ToArray());
    }
}
