// Run with run-analysis-null-graph-tests.sh AnalysisReportFigurePairTests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC;
using AnalysisITC.Core.Presentation;

static class AnalysisReportFigurePairTests
{
    static int Main()
    {
        NSApplication.Init();
        try
        {
            CheckFrames(true, true, false, 700, 450);
            CheckFrames(true, true, true, 700, 450);
            CheckFrames(true, false, false, 700, 450);
            CheckFrames(false, false, true, 700, 450);
            CheckFrames(true, true, true, 500, 180);
            Console.WriteLine("PASS: five native report figure pairs share their rendered top and bottom graph frames");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
    }

    static void CheckFrames(bool thermogram, bool residual, bool wideTicks, int width, int height)
    {
        var pair = CreatePair(thermogram, residual, wideTicks);
        var rendererType = typeof(MainWindowController).Assembly.GetType("AnalysisITC.UI.MacOS.Drawing.CoreGraphicsAnalysisReportRenderer");
        var renderer = Activator.CreateInstance(rendererType, true);
        const int renderScale = 3;
        var pixelWidth = (width + 40) * renderScale;
        var pixelHeight = (height + 40) * renderScale;
        using (var space = CGColorSpace.CreateDeviceRGB())
        using (var bitmap = new CGBitmapContext(IntPtr.Zero, pixelWidth, pixelHeight, 8, pixelWidth * 4,
            space, CGImageAlphaInfo.PremultipliedLast))
        {
            bitmap.ScaleCTM(renderScale, renderScale);
            bitmap.SetFillColor(NSColor.White.CGColor);
            bitmap.FillRect(new CGRect(0, 0, width + 40, height + 40));
            rendererType.GetMethod("DrawFigurePair", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(renderer, new object[] { bitmap, (double)(height + 40), pair, new AnalysisReportRect(20, 20, width, height) });
            var pixels = new byte[pixelWidth * pixelHeight * 4];
            Marshal.Copy(bitmap.Data, pixels, 0, pixels.Length);
            var split = (20 + (int)Math.Round((width - 12) * .57)) * renderScale;
            var left = FrameRows(pixels, pixelWidth, pixelHeight, 20 * renderScale, split);
            var right = FrameRows(pixels, pixelWidth, pixelHeight, split + 12 * renderScale, (width + 20) * renderScale);
            if (left.Count == 0 || right.Count == 0) throw new Exception("Missing graph frame in native pair");
            if (Math.Abs(left[0] - right[0]) > 1 || Math.Abs(left[left.Count - 1] - right[right.Count - 1]) > 1)
                throw new Exception("Native frames differ: " + thermogram + "/" + residual + "/" + wideTicks
                    + ": left " + left[0] + "–" + left[left.Count - 1] + ", right " + right[0] + "–" + right[right.Count - 1]);
            var output = Environment.GetEnvironmentVariable("FTITC_REPORT_ALIGNMENT_QA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                using (var image = bitmap.ToImage())
                using (var representation = new NSBitmapImageRep(image))
                using (var png = representation.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary()))
                    File.WriteAllBytes(Path.Combine(output, "macos-" + thermogram + "-" + residual + "-" + wideTicks + "-" + width + "-" + height + ".png"), png.ToArray());
            }
        }
    }

    static List<int> FrameRows(byte[] pixels, int width, int height, int left, int right)
    {
        var rows = new List<int>();
        for (var y = 0; y < height; y++)
        {
            var run = 0;
            for (var x = left; x < right; x++)
            {
                var offset = (y * width + x) * 4;
                run = pixels[offset] < 160 && pixels[offset + 1] < 160 && pixels[offset + 2] < 160 ? run + 1 : 0;
                if (run >= Math.Max(40, (right - left) / 4)) { rows.Add(y); break; }
            }
        }
        return rows;
    }

    static PublicationFigurePanel Panel(PublicationPanelKind kind, double min, double max, string y)
    {
        return new PublicationFigurePanel
        {
            Kind = kind,
            XAxis = new PublicationAxis(kind == PublicationPanelKind.Thermogram ? "Time (min)" : "Molar ratio",
                kind == PublicationPanelKind.Thermogram ? PublicationAxisPlacement.Top : PublicationAxisPlacement.Bottom,
                0, kind == PublicationPanelKind.Thermogram ? 50 : 6, 5, sanitizeTicks: true),
            YAxis = new PublicationAxis(y, PublicationAxisPlacement.Left, min, max, 3, preferCenteredTicks: kind == PublicationPanelKind.Residual, sanitizeTicks: true),
        };
    }

    static PublicationFigureDocument Figure(double width)
    {
        return new PublicationFigureDocument(new PublicationFigureOptions
        {
            PlotWidthCentimeters = width, PlotHeightCentimeters = 10,
            Font = PublicationFont.LiberationSans, FontSize = 14,
            ShowFitParameters = false, ShowExperimentDetails = false,
            FocusThermogramOnBaseline = width == 8.5,
        });
    }

    static AnalysisReportFigurePairBlock CreatePair(bool thermogram, bool residual, bool wideTicks)
    {
        var left = Figure(8.5);
        left.ThermogramPanel = Panel(PublicationPanelKind.Thermogram, 40.4, 40.8, "Differential Power (µW)");
        var right = Figure(6);
        if (thermogram) right.ThermogramPanel = Panel(PublicationPanelKind.Thermogram, -6, 0, "Differential Power (µW)");
        right.FitPanel = Panel(PublicationPanelKind.Fit, -40, 0, "kJ/mol of injectant");
        if (residual) right.ResidualPanel = Panel(PublicationPanelKind.Residual, -.05, .05, "");
        if (wideTicks) right.FitPanel.YAxis = new PublicationAxis("kJ/mol of injectant", PublicationAxisPlacement.Left, -1234567, 0, 3, sanitizeTicks: true);
        return (AnalysisReportFigurePairBlock)Activator.CreateInstance(typeof(AnalysisReportFigurePairBlock),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { "", "Processing", left, "Final fit", right }, null);
    }
}
