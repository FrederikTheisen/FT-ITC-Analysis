using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using SkiaSharp;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisReportStatusBadgeRenderingTests
{
    public static IEnumerable<object[]> Cases() =>
        from font in new[] { PublicationFont.Native, PublicationFont.Inter, PublicationFont.LiberationSans }
        from health in Enum.GetValues<AnalysisResultHealth>()
        from landscape in new[] { false, true }
        select new object[] { font, health, landscape };

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void CompleteTitlesWrapClearOfEveryStampInPreviewAndPdf(PublicationFont font, AnalysisResultHealth health, bool landscape)
    {
        var previous = AppSettings.PublicationFigureFont;
        try
        {
            AppSettings.PublicationFigureFont = font;
            var renderer = new SkiaAnalysisReportRenderer();
            var measurer = (IAnalysisReportTextMeasurer)typeof(SkiaAnalysisReportRenderer)
                .GetField("measurer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
            foreach (var scenario in new[] { "long", "short", "unbroken", "unicode" })
            {
                var title = scenario switch
                {
                    "long" => "Result 1. " + string.Join(" ", Enumerable.Repeat("Long result title with ΔG", 5)),
                    "short" => "Result 1. Binding",
                    "unbroken" => new string('W', 120),
                    _ => "Result 1. " + string.Concat(Enumerable.Repeat("ΔH e\u0301 中文 🧪 ", 14)).Trim(),
                };
                var document = Document(title, health, landscape);
                var plan = renderer.CreatePlan(document);
                var reference = Document("", health, landscape);
                var referencePlan = renderer.CreatePlan(reference);
                using var pdf = new MemoryStream();
                renderer.WritePdf(document, plan, pdf);
                Assert.True(pdf.Length > 2000);
                for (var index = 0; index < 2; index++)
                {
                    var page = plan.Pages[index];
                    var heading = page.Fragments.First();
                    using var actual = renderer.RenderPageBitmap(document, plan, index, 1500);
                    using var baseline = renderer.RenderPageBitmap(reference, referencePlan, index, 1500);
                    WriteEvidence(font, health, landscape, scenario, index, actual, pdf);
                    var stampText = index == 0 ? document.StatusBadgeText : health switch
                    {
                        AnalysisResultHealth.Valid => "ANALYSIS VALID",
                        AnalysisResultHealth.Warning => "REVIEW WARNINGS",
                        AnalysisResultHealth.PartialInvalid => "PARTIAL / STALE",
                        AnalysisResultHealth.Invalid => "INVALID / STALE",
                        _ => "STATUS UNKNOWN",
                    };
                    var stampWidth = measurer.Measure(stampText, new AnalysisReportTextStyle(6.5, true)).Width + 16;
                    var stampX = heading.Bounds.Right - stampWidth;
                    // Comparing the gap and stamp with an empty-title page also catches
                    // a renderer that keeps drawing the original unwrapped title.
                    var scale = actual.Width / page.Width;
                    for (var y = (int)Math.Floor(heading.Bounds.Y * scale); y < Math.Ceiling((heading.Bounds.Y + 21) * scale); y++)
                        for (var x = (int)Math.Floor((stampX - 6) * scale); x < Math.Ceiling((heading.Bounds.Right + 1) * scale); x++)
                            Assert.True(baseline.GetPixel(x, y) == actual.GetPixel(x, y),
                                scenario + ": title changed the stamp or its surrounding gap.");
                    Assert.All(heading.Lines, line => Assert.True(measurer.Measure(line, new AnalysisReportTextStyle(17, true)).Width
                        <= stampX - heading.Bounds.X - 8 + .001));
                    Assert.Equal(title.Replace(" ", ""), string.Concat(heading.Lines).Replace(" ", ""));
                    if (scenario == "short") Assert.Single(heading.Lines);
                    else Assert.True(heading.Lines.Count > 1);
                    var layout = heading.HeadingLayout!;
                    for (var line = 0; line < heading.Lines.Count; line++)
                    {
                        var top = heading.Bounds.Y + line * layout.TitleLineAdvance;
                        var ink = 0;
                        for (var y = (int)Math.Ceiling(top * scale); y < Math.Floor((top + layout.TitleLineAdvance) * scale); y++)
                            for (var x = (int)Math.Ceiling(heading.Bounds.X * scale); x < Math.Floor(stampX * scale - 8 * scale); x++)
                            {
                                var pixel = actual.GetPixel(x, y);
                                if (pixel.Red < 100 && pixel.Green < 100 && pixel.Blue < 100) ink++;
                            }
                        Assert.True(ink > 20, "A wrapped title line was omitted from the preview.");
                    }
                }
            }
        }
        finally { AppSettings.PublicationFigureFont = previous; }
    }

    static AnalysisReportDocument Document(string title, AnalysisResultHealth health, bool landscape)
    {
        var document = new AnalysisReportDocument { Title = "Stamp layout QA", ResultHealth = health,
            GeneratedAtUtc = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc) };
        if (landscape)
        {
            document.PageSettings.WidthCentimeters = 29.7;
            document.PageSettings.HeightCentimeters = 21;
        }
        foreach (var kind in new[] { AnalysisReportSectionKind.Cover, AnalysisReportSectionKind.ResultOverview })
        {
            var section = new AnalysisReportSection(kind, kind.ToString(), title, AnalysisReportLayoutPolicy.StartOnNewPage,
                statusBadgeHealth: health);
            section.Add(new AnalysisReportTextBlock("Analysis", "Content below the complete title", AnalysisReportLayoutPolicy.None));
            document.AddSection(section);
        }
        return document;
    }

    static void WriteEvidence(PublicationFont font, AnalysisResultHealth health, bool landscape, string scenario,
        int page, SKBitmap bitmap, MemoryStream pdf)
    {
        var directory = Environment.GetEnvironmentVariable("FTITC_REPORT_STAMP_QA_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory) || scenario != "long") return;
        Directory.CreateDirectory(directory);
        var name = "avalonia-stamp-" + font + "-" + health + "-" + (landscape ? "landscape" : "portrait");
        if (page == 0) File.WriteAllBytes(Path.Combine(directory, name + ".pdf"), pdf.ToArray());
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, name + "-" + page + ".png"), png.ToArray());
    }
}
