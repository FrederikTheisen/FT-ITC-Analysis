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
public sealed class AnalysisReportConditionsRenderingTests
{
    const string Conditions = "100 µM ▸ 10 µM | 25 °C | 50 mM Hepes pH 7.4 | 150 mM NaCl";

    [AvaloniaTheory]
    [InlineData(PublicationFont.Native)]
    [InlineData(PublicationFont.Inter)]
    [InlineData(PublicationFont.LiberationSans)]
    public void ConditionsUseLightFacesAndEveryWrappedLineIsRendered(PublicationFont selection)
    {
        var previous = AppSettings.PublicationFigureFont;
        try
        {
            AppSettings.PublicationFigureFont = selection;
            var renderer = new SkiaAnalysisReportRenderer();
            var light = (SkiaPublicationFontSet)typeof(SkiaAnalysisReportRenderer)
                .GetField("subtitleFonts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
            Assert.Equal(300, light.Regular.FontWeight);
            Assert.NotEqual((ushort)0, light.TextRuns("▸", bold: false, italic: false).Single().Typeface.GetGlyph('▸'));
            Assert.NotEqual((ushort)0, light.Regular.GetGlyph('|'));
            Assert.NotEqual((ushort)0, light.Regular.GetGlyph('µ'));
            foreach (var scenario in new[] { "short", "wrapped", "supporting" })
            {
                var document = Document(scenario);
                var plan = renderer.CreatePlan(document);
                var page = plan.Pages.Single(item => item.Fragments.Any(fragment => fragment.HeadingLayout?.SubtitleLines.Count > 0));
                var fragment = page.Fragments.Single(item => item.HeadingLayout?.SubtitleLines.Count > 0);
                var layout = fragment.HeadingLayout!;
                Assert.Equal(layout.Height, fragment.Bounds.Height);
                if (scenario != "short") Assert.True(layout.SubtitleLines.Count > 1);
                if (scenario == "wrapped") Assert.True(layout.TitleLines.Count > 1);
                if (scenario == "supporting") Assert.True(page.PageNumber > plan.Pages.Single(item => item.Fragments.Any(
                    part => part.Block is AnalysisReportTextBlock text && text.Text.StartsWith("Filler"))).PageNumber);
                using var bitmap = renderer.RenderPageBitmap(document, plan, page.PageNumber - 1, 1800);
                var scale = bitmap.Width / page.Width;
                for (var index = 0; index < layout.SubtitleLines.Count; index++)
                {
                    var top = fragment.Bounds.Y + layout.SubtitleBounds.Y + index * layout.SubtitleLineAdvance;
                    Assert.True(InkPixels(bitmap, fragment.Bounds.X * scale, top * scale,
                        fragment.Bounds.Width * scale, layout.SubtitleLineAdvance * scale) > 30,
                        "A wrapped conditions line was clipped or omitted.");
                }
                using var stream = new MemoryStream();
                renderer.WritePdf(document, plan, stream);
                Assert.True(stream.Length > 2000);
                var output = Environment.GetEnvironmentVariable("FTITC_REPORT_CONDITIONS_QA_DIRECTORY");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    File.WriteAllBytes(Path.Combine(output, "avalonia-conditions-" + selection + "-" + scenario + ".pdf"), stream.ToArray());
                }
            }
        }
        finally { AppSettings.PublicationFigureFont = previous; }
    }

    static AnalysisReportDocument Document(string scenario)
    {
        var document = new AnalysisReportDocument { Title = "Conditions QA",
            GeneratedAtUtc = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc) };
        var cover = new AnalysisReportSection(AnalysisReportSectionKind.Cover, "cover", document.Title,
            AnalysisReportLayoutPolicy.None);
        cover.Add(new AnalysisReportTextBlock("", "Report rendering QA", AnalysisReportLayoutPolicy.None));
        document.AddSection(cover);
        var subtitle = scenario == "short" ? Conditions : string.Join(" | ", Enumerable.Repeat(Conditions, 4));
        var title = scenario == "wrapped" ? "1A. " + string.Join(" ", Enumerable.Repeat("Long experiment name", 8))
            : scenario == "supporting" ? "Supporting experiments" : "1A. Experiment name";
        var section = new AnalysisReportSection(scenario == "supporting" ? AnalysisReportSectionKind.SupportingData
            : AnalysisReportSectionKind.Experiment, "conditions", title, AnalysisReportLayoutPolicy.StartOnNewPage,
            subtitle: scenario == "supporting" ? "" : subtitle);
        if (scenario == "supporting")
        {
            document.PageSettings.HeightCentimeters = 10;
            section.Add(new AnalysisReportTextBlock("", string.Join("\n", Enumerable.Repeat("Filler", 6)),
                AnalysisReportLayoutPolicy.KeepTogether));
            section.Add(new AnalysisReportHeadingBlock("S1. Supporting blank", 2, subtitle));
        }
        section.Add(new AnalysisReportTextBlock("Experiment details", "Metadata begins here", AnalysisReportLayoutPolicy.None));
        document.AddSection(section);
        return document;
    }

    static int InkPixels(SKBitmap bitmap, double x, double y, double width, double height)
    {
        var count = 0;
        for (var row = Math.Max(0, (int)y); row < Math.Min(bitmap.Height, (int)Math.Ceiling(y + height)); row++)
            for (var column = Math.Max(0, (int)x); column < Math.Min(bitmap.Width, (int)Math.Ceiling(x + width)); column++)
            {
                var color = bitmap.GetPixel(column, row);
                if (color.Red < 210 && color.Green < 210 && color.Blue < 210) count++;
            }
        return count;
    }
}
