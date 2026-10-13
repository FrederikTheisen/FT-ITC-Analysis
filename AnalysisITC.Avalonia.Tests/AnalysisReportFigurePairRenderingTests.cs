using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Core.Presentation;
using SkiaSharp;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisReportFigurePairRenderingTests
{
    [AvaloniaTheory]
    [InlineData(true, true, false, 700, 450)]
    [InlineData(true, true, true, 700, 450)]
    [InlineData(true, false, false, 700, 450)]
    [InlineData(false, false, true, 700, 450)]
    [InlineData(true, true, true, 500, 180)]
    public void RenderedGraphFramesShareTopAndBottom(bool thermogram, bool residual, bool wideTicks,
        int width, int height)
    {
        var pair = CreatePair(thermogram, residual, wideTicks);
        var renderer = new SkiaAnalysisReportRenderer();
        const int renderScale = 3;
        using var bitmap = new SKBitmap((width + 40) * renderScale, (height + 40) * renderScale);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Scale(renderScale);
        var target = new SKRect(20, 20, 20 + width, 20 + height);
        typeof(SkiaAnalysisReportRenderer).GetMethod("DrawFigurePair", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(renderer, new object[] { canvas, pair, target });
        canvas.Flush();
        var split = (20 + (int)Math.Round((width - 12) * .57)) * renderScale;
        var left = FrameRows(bitmap, 20 * renderScale, split);
        var right = FrameRows(bitmap, split + 12 * renderScale, (width + 20) * renderScale);
        Assert.NotEmpty(left);
        Assert.NotEmpty(right);
        Assert.InRange(Math.Abs(left[0] - right[0]), 0, 1);
        Assert.InRange(Math.Abs(left[^1] - right[^1]), 0, 1);
        Assert.True(left[^1] < target.Bottom * renderScale && right[^1] < target.Bottom * renderScale);

        var output = Environment.GetEnvironmentVariable("FTITC_REPORT_ALIGNMENT_QA_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(output, $"avalonia-{thermogram}-{residual}-{wideTicks}-{width}-{height}.png"));
            png.SaveTo(file);
        }
    }

    static List<int> FrameRows(SKBitmap bitmap, int left, int right)
    {
        var rows = new List<int>();
        for (var y = 0; y < bitmap.Height; y++)
        {
            var run = 0;
            for (var x = left; x < right; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                run = pixel.Red < 160 && pixel.Green < 160 && pixel.Blue < 160 ? run + 1 : 0;
                if (run >= Math.Max(40, (right - left) / 4)) { rows.Add(y); break; }
            }
        }
        return rows;
    }

    static AnalysisReportFigurePairBlock CreatePair(bool thermogram, bool residual, bool wideTicks)
    {
        PublicationFigureDocument Figure(double width) => new(new PublicationFigureOptions
        {
            PlotWidthCentimeters = width, PlotHeightCentimeters = 10,
            Font = PublicationFont.LiberationSans, FontSize = 14,
            ShowFitParameters = false, ShowExperimentDetails = false,
            FocusThermogramOnBaseline = width == 8.5,
        }) { PlotWidth = width * PublicationFigureOptions.DefaultPointsPerCentimeter,
             PlotHeight = 10 * PublicationFigureOptions.DefaultPointsPerCentimeter };
        PublicationFigurePanel Panel(PublicationPanelKind kind, double min, double max, string y) => new()
        {
            Kind = kind,
            XAxis = new PublicationAxis(kind == PublicationPanelKind.Thermogram ? "Time (min)" : "Molar ratio",
                kind == PublicationPanelKind.Thermogram ? PublicationAxisPlacement.Top : PublicationAxisPlacement.Bottom,
                0, kind == PublicationPanelKind.Thermogram ? 50 : 6, 5, sanitizeTicks: true),
            YAxis = new PublicationAxis(y, PublicationAxisPlacement.Left, min, max, 3, preferCenteredTicks: kind == PublicationPanelKind.Residual, sanitizeTicks: true),
        };
        var left = Figure(8.5);
        left.ThermogramPanel = Panel(PublicationPanelKind.Thermogram, 40.4, 40.8, "Differential Power (µW)");
        var right = Figure(6);
        if (thermogram) right.ThermogramPanel = Panel(PublicationPanelKind.Thermogram, -6, 0, "Differential Power (µW)");
        right.FitPanel = Panel(PublicationPanelKind.Fit, -40, 0, "kJ/mol of injectant");
        if (residual) right.ResidualPanel = Panel(PublicationPanelKind.Residual, -.05, .05, "");
        if (wideTicks) right.FitPanel.YAxis = new PublicationAxis("kJ/mol of injectant", PublicationAxisPlacement.Left, -1234567, 0, 3, sanitizeTicks: true);
        return new AnalysisReportFigurePairBlock("", "Processing", left, "Final fit", right);
    }
}
