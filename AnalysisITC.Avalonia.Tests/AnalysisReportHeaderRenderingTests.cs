using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

public sealed class AnalysisReportHeaderRenderingTests
{
    [Theory]
    [InlineData("short")]
    [InlineData("long-experiment")]
    [InlineData("long-result")]
    public void ExperimentAndContinuationHeadersKeepTheDateClear(string scenario)
    {
        var result = scenario == "long-result" ? new string('W', 200) : "Result name";
        var name = scenario == "short" ? "Experiment name" : new string('W', 200);
        var document = new AnalysisReportDocument
        {
            Title = "Experiment header QA",
            GeneratedAtUtc = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc),
        };
        var cover = new AnalysisReportSection(AnalysisReportSectionKind.Cover, "cover", document.Title,
            AnalysisReportLayoutPolicy.StartOnNewPage);
        cover.Add(new AnalysisReportTextBlock("Report", "Header QA", AnalysisReportLayoutPolicy.None));
        document.AddSection(cover);
        var experiment = new AnalysisReportSection(AnalysisReportSectionKind.Experiment, "experiment", "1A. " + name,
            AnalysisReportLayoutPolicy.StartOnNewPage | AnalysisReportLayoutPolicy.AllowContinuation,
            result, experimentLabel: "1A", experimentName: name);
        experiment.Add(new AnalysisReportTableBlock("Injection observations",
            new[] { new AnalysisReportTableColumn("row", "Injection") },
            Enumerable.Range(1, 100).Select(index => new AnalysisReportTableRow(new[] { index.ToString() })),
            AnalysisReportLayoutPolicy.AllowContinuation));
        document.AddSection(experiment);
        var renderer = new SkiaAnalysisReportRenderer();
        var plan = renderer.CreatePlan(document);
        var measurer = (IAnalysisReportTextMeasurer)typeof(SkiaAnalysisReportRenderer)
            .GetField("measurer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        Assert.True(plan.Pages.Count >= 3);
        const int pixelWidth = 1800;
        using var coverBitmap = renderer.RenderPageBitmap(document, plan, 0, pixelWidth);
        for (var index = 1; index < plan.Pages.Count; index++)
        {
            var page = plan.Pages[index];
            Assert.Equal("1A", page.ExperimentLabel);
            var header = AnalysisReportHeaderLayout.Create(document, page, plan.MarginLeft,
                page.Width - plan.MarginRight, measurer);
            Assert.Contains(" · 1A.", header.ContextText);
            if (scenario == "long-result") Assert.EndsWith(" · 1A.", header.ContextText);
            if (scenario == "long-experiment") Assert.EndsWith("…", header.ContextText);
            Assert.True(plan.MarginLeft + measurer.Measure(header.ContextText, header.ContextStyle).Width
                <= header.ExportDateX - 8);
            using var bitmap = renderer.RenderPageBitmap(document, plan, index, pixelWidth);
            // Compare date pixels, including the gap, against the unobstructed cover date.
            // This catches a renderer still drawing the original unbounded names.
            var scale = pixelWidth / page.Width;
            var startX = (int)Math.Ceiling((header.ExportDateX - 7) * scale);
            var endX = (int)Math.Floor((page.Width - plan.MarginRight) * scale);
            var startY = (int)Math.Floor(12 * scale);
            var endY = (int)Math.Ceiling(26 * scale);
            for (var y = startY; y < endY; y++)
                for (var x = startX; x < endX; x++)
                    Assert.Equal(coverBitmap.GetPixel(x, y), bitmap.GetPixel(x, y));
        }
        using var stream = new MemoryStream();
        renderer.WritePdf(document, plan, stream);
        Assert.True(stream.Length > 2000);
        var output = Environment.GetEnvironmentVariable("FTITC_REPORT_HEADER_QA_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "avalonia-" + scenario + ".pdf"), stream.ToArray());
        }
    }
}
