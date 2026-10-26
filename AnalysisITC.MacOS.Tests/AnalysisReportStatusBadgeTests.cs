// Run with run-analysis-null-graph-tests.sh AnalysisReportStatusBadgeTests.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;

static class AnalysisReportStatusBadgeTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const int Scale = 2;

    static int Main()
    {
        NSApplication.Init();
        try
        {
            var rendererType = typeof(MainWindowController).Assembly.GetType("AnalysisITC.UI.MacOS.Drawing.CoreGraphicsAnalysisReportRenderer");
            var renderer = Activator.CreateInstance(rendererType, true);
            var measurer = (IAnalysisReportTextMeasurer)rendererType.GetField("measurer", Private).GetValue(renderer);
            foreach (AnalysisResultHealth health in Enum.GetValues(typeof(AnalysisResultHealth)))
                foreach (var landscape in new[] { false, true })
                    foreach (var scenario in new[] { "long", "short", "unbroken", "unicode" })
                    {
                        var title = scenario == "long" ? "Result 1. " + string.Join(" ", Enumerable.Repeat("Long result title with ΔG", 5))
                            : scenario == "short" ? "Result 1. Binding" : scenario == "unbroken" ? new string('W', 120)
                            : "Result 1. " + string.Concat(Enumerable.Repeat("ΔH e\u0301 中文 🧪 ", 14)).Trim();
                        var document = Document(title, health, landscape);
                        var plan = (AnalysisReportLayoutPlan)rendererType.GetMethod("CreatePlan").Invoke(renderer, new object[] { document });
                        var baseline = Document("", health, landscape);
                        var baselinePlan = (AnalysisReportLayoutPlan)rendererType.GetMethod("CreatePlan").Invoke(renderer, new object[] { baseline });
                        var directory = Environment.GetEnvironmentVariable("FTITC_REPORT_STAMP_QA_DIRECTORY");
                        var name = "macos-stamp-" + health + "-" + (landscape ? "landscape" : "portrait");
                        using (var pdf = (NSData)rendererType.GetMethod("CreatePdfData").Invoke(renderer, new object[] { document, plan }))
                        {
                            Check(pdf.Length > 2000, "Native report PDF is empty");
                            if (!string.IsNullOrWhiteSpace(directory) && scenario == "long")
                            {
                                Directory.CreateDirectory(directory);
                                File.WriteAllBytes(Path.Combine(directory, name + ".pdf"), pdf.ToArray());
                            }
                        }
                        for (var index = 0; index < 2; index++)
                        {
                            var page = plan.Pages[index];
                            var heading = page.Fragments.First();
                            var badgeText = health == AnalysisResultHealth.Valid ? "ANALYSIS VALID"
                                : health == AnalysisResultHealth.Warning ? "REVIEW WARNINGS"
                                : health == AnalysisResultHealth.PartialInvalid ? "PARTIAL / STALE"
                                : health == AnalysisResultHealth.Invalid ? "INVALID / STALE" : "STATUS UNKNOWN";
                            var stampX = heading.Bounds.Right - measurer.Measure(badgeText, new AnalysisReportTextStyle(6.5, true)).Width - 16;
                            Check(heading.Lines.All(line => measurer.Measure(line, new AnalysisReportTextStyle(17, true)).Width
                                <= stampX - heading.Bounds.X - 8 + .001), "Native title enters stamp area");
                            Check(title.Replace(" ", "") == string.Concat(heading.Lines).Replace(" ", ""), "Native title lost text");
                            Check(scenario == "short" ? heading.Lines.Count == 1 : heading.Lines.Count > 1, "Unexpected native title wrapping");
                            Check(heading.Bounds.Height == heading.HeadingLayout.Height, "Native wrapped heading is clipped");
                            var path = !string.IsNullOrWhiteSpace(directory) && scenario == "long"
                                ? Path.Combine(directory, name + "-" + index + ".png") : null;
                            var actual = Render(renderer, document, plan, page, path);
                            var expected = Render(renderer, baseline, baselinePlan, baselinePlan.Pages[index], null);
                            var width = (int)Math.Ceiling(page.Width * Scale);
                            for (var row = 0; row < actual.Length / (width * 4); row++)
                                for (var column = (int)Math.Floor((stampX - 6) * Scale); column < Math.Ceiling((heading.Bounds.Right + 1) * Scale); column++)
                                    for (var channel = 0; channel < 4; channel++)
                                    {
                                        var offset = (row * width + column) * 4 + channel;
                                        Check(expected[offset] == actual[offset], "Native title changed the stamp or its surrounding gap");
                                    }
                        }
                    }
            Console.WriteLine("PASS: native cover and result titles preserve every name and all five stamps in portrait/landscape previews and PDFs (80 rendered headings)");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
    }

    static AnalysisReportDocument Document(string title, AnalysisResultHealth health, bool landscape)
    {
        var document = (AnalysisReportDocument)Activator.CreateInstance(typeof(AnalysisReportDocument), true);
        typeof(AnalysisReportDocument).GetProperty("Title").SetValue(document, "Stamp layout QA", null);
        typeof(AnalysisReportDocument).GetProperty("ResultHealth").SetValue(document, health, null);
        typeof(AnalysisReportDocument).GetProperty("GeneratedAtUtc").SetValue(document, new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc), null);
        if (landscape)
        {
            typeof(AnalysisReportPageSettings).GetProperty("WidthCentimeters").SetValue(document.PageSettings, 29.7, null);
            typeof(AnalysisReportPageSettings).GetProperty("HeightCentimeters").SetValue(document.PageSettings, 21.0, null);
        }
        foreach (var kind in new[] { AnalysisReportSectionKind.Cover, AnalysisReportSectionKind.ResultOverview })
        {
            var section = (AnalysisReportSection)Activator.CreateInstance(typeof(AnalysisReportSection), Private, null,
                new object[] { kind, kind.ToString(), title, AnalysisReportLayoutPolicy.StartOnNewPage, "", health, "", "", "" }, null);
            var content = (AnalysisReportTextBlock)Activator.CreateInstance(typeof(AnalysisReportTextBlock), Private, null,
                new object[] { "Analysis", "Content below the complete title", AnalysisReportLayoutPolicy.None, false }, null);
            typeof(AnalysisReportSection).GetMethod("Add", Private).Invoke(section, new object[] { content });
            typeof(AnalysisReportDocument).GetMethod("AddSection", Private).Invoke(document, new object[] { section });
        }
        return document;
    }

    static byte[] Render(object renderer, AnalysisReportDocument document, AnalysisReportLayoutPlan plan,
        AnalysisReportPagePlan page, string path)
    {
        var width = (int)Math.Ceiling(page.Width * Scale);
        var height = (int)Math.Ceiling(page.Height * Scale);
        using (var space = CGColorSpace.CreateDeviceRGB())
        using (var bitmap = new CGBitmapContext(IntPtr.Zero, width, height, 8, width * 4, space, CGImageAlphaInfo.PremultipliedLast))
        {
            bitmap.ScaleCTM(Scale, Scale);
            renderer.GetType().GetMethod("DrawPage", Private).Invoke(renderer, new object[] { bitmap, document, plan, page });
            var pixels = new byte[width * height * 4];
            Marshal.Copy(bitmap.Data, pixels, 0, pixels.Length);
            if (path != null)
                using (var image = bitmap.ToImage())
                using (var representation = new NSBitmapImageRep(image))
                using (var png = representation.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary()))
                    File.WriteAllBytes(path, png.ToArray());
            return pixels;
        }
    }

    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
