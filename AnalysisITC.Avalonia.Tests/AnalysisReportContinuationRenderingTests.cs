using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Core.Presentation;
using SkiaSharp;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisReportContinuationRenderingTests
{
    [AvaloniaFact]
    public void OversizedNoticesAndRowsRenderEverySliceAndContinuationTitle()
    {
        var document = AnalysisReportBuilder.BuildInterpretationPreview("Prelude");
        var section = document.Sections.First();
        var notice = new AnalysisReportNoticeBlock("Report warnings", string.Join("\n", Enumerable.Range(1, 150)
            .Select(index => "Warning " + index.ToString("D3") + ": preserved evidence ΔH e\u0301")), AnalysisReportNoticeLevel.Warning);
        var metadata = new AnalysisReportKeyValueBlock("Long metadata", new[]
        { new AnalysisReportKeyValueItem("Provenance", string.Join("\n", Enumerable.Range(1, 150)
            .Select(index => "Evidence " + index.ToString("D3") + ": saved value ± SD"))) });
        section.Add(notice);
        section.Add(metadata);
        section.Add(new AnalysisReportKeyValueBlock("Result identifiers", Enumerable.Range(1, 70)
            .Select(index => new AnalysisReportKeyValueItem("Result " + index, "00000000-0000-0000-0000-" + index.ToString("D12"))),
            AnalysisReportLayoutPolicy.AllowContinuation));
        var renderer = new SkiaAnalysisReportRenderer();
        var plan = renderer.CreatePlan(document);
        var notices = plan.Pages.SelectMany(page => page.Fragments).Where(fragment => fragment.Block == notice).ToList();
        Assert.True(notices.Count > 1);
        Assert.Equal(150, notices.Sum(fragment => fragment.Lines.Count));
        Assert.Equal(150, plan.Pages.SelectMany(page => page.Fragments).Where(fragment => fragment.Block == metadata)
            .SelectMany(fragment => fragment.KeyValueLayout.Rows).Sum(row => row.ValueLines.Count));

        // Compare the actual drawing with/without the continued title, at identical bounds.
        var continued = notices[1];
        var plain = new AnalysisReportLayoutFragment(AnalysisReportFragmentKind.Notice, notice,
            continued.Bounds, lines: continued.Lines);
        using var continuedBitmap = DrawNotice(renderer, notice, continued);
        using var plainBitmap = DrawNotice(renderer, notice, plain);
        Assert.False(continuedBitmap.Bytes.SequenceEqual(plainBitmap.Bytes));

        // A layout line count alone does not prove DrawLines actually painted its final line.
        foreach (var fragment in notices)
        {
            Assert.NotEmpty(fragment.Lines);
            var withoutLastLine = new AnalysisReportLayoutFragment(AnalysisReportFragmentKind.Notice, notice,
                fragment.Bounds, firstItem: fragment.FirstItem, lines: fragment.Lines.Take(fragment.Lines.Count - 1));
            using var full = DrawNotice(renderer, notice, fragment);
            using var shortened = DrawNotice(renderer, notice, withoutLastLine);
            Assert.False(full.Bytes.SequenceEqual(shortened.Bytes), $"Notice fragment at line {fragment.FirstItem} did not draw its last line.");
        }

        var output = Environment.GetEnvironmentVariable("FTITC_REPORT_CONTINUATION_QA_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            renderer.WritePdf(document, plan, Path.Combine(output, "avalonia-continuation.pdf"));
        }
        for (var index = 0; index < plan.Pages.Count; index++)
        {
            var page = plan.Pages[index];
            Assert.All(page.Fragments, fragment => Assert.True(fragment.Bounds.Bottom <= page.Height - plan.MarginBottom - 18 + .001));
            using var bitmap = renderer.RenderPageBitmap(document, plan, index, 1000);
            if (string.IsNullOrWhiteSpace(output)) continue;
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(Path.Combine(output, "avalonia-continuation-" + (index + 1) + ".png"));
            data.SaveTo(stream);
        }
    }

    static SKBitmap DrawNotice(SkiaAnalysisReportRenderer renderer, AnalysisReportNoticeBlock block, AnalysisReportLayoutFragment fragment)
    {
        var bitmap = new SKBitmap(600, 900);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        var bounds = fragment.Bounds;
        typeof(SkiaAnalysisReportRenderer).GetMethod("DrawNotice", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(renderer, new object[] { canvas, block, fragment,
                new SKRect((float)bounds.X, (float)bounds.Y, (float)bounds.Right, (float)bounds.Bottom) });
        canvas.Flush();
        return bitmap;
    }
}
