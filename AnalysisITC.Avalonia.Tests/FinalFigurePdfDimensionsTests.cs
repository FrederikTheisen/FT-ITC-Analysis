using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Avalonia.FinalFigure;
using AnalysisITC.Avalonia.Printing;
using AnalysisITC.Core.Presentation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class FinalFigurePdfDimensionsTests
{
    const double GeometryTolerance = .01;

    [AvaloniaTheory]
    [InlineData("complete", false, 283.464567)]
    [InlineData("complete", true, 283.464567)]
    [InlineData("thermogram", false, 283.464567)]
    [InlineData("thermogram", true, 283.464567)]
    [InlineData("fit", false, 141.732283)]
    [InlineData("fit", true, 141.732283)]
    public void ExportedPlotHasRequestedPhysicalDimensions(string panels, bool fileOutput, double expectedHeight)
    {
        var document = Figure(panels);
        var renderer = new SkiaFigureRenderer();
        byte[] bytes;
        if (fileOutput)
        {
            var path = Path.Combine(Path.GetTempPath(), $"cr031-{Guid.NewGuid():N}.pdf");
            try
            {
                renderer.WritePdf(document, path);
                bytes = File.ReadAllBytes(path);
            }
            finally { File.Delete(path); }
        }
        else
        {
            using var stream = new MemoryStream();
            renderer.WritePdf(document, stream);
            Assert.True(stream.CanWrite);
            bytes = stream.ToArray();
        }

        AssertGeometry(bytes, document, expectedHeight);
    }

    [AvaloniaFact]
    public void InternallyReachableNondefaultLayoutDensityStillExportsSixCentimeters()
    {
        // The UI never changes this density; exercise the existing internal document option.
        var document = Figure("complete", density: 96.0 / 2.54);
        using var stream = new MemoryStream();
        new SkiaFigureRenderer().WritePdf(document, stream);
        AssertGeometry(stream.ToArray(), document, 283.464567);
    }

    [AvaloniaTheory]
    [InlineData(3)]
    [InlineData(6)]
    public async Task PrintCaptureKeepsBitmapResolutionAndRecordsPdfDimensions(double width)
    {
        var document = Figure("complete", width: width);
        var renderer = new SkiaFigureRenderer();
        var previewSize = renderer.GetPageSize(document);
        var expectedPixelWidth = Math.Min(6000, Math.Max(1200, (int)Math.Ceiling(previewSize.Width / 72f * 300f)));
        using var expectedBitmap = renderer.RenderBitmap(document, expectedPixelWidth);
        using var payload = await GraphPrintTarget.FromPublicationFigure("Dimensions", () => document, renderer).CaptureAsync();

        Assert.True(payload.PreservePdf);
        Assert.Equal(expectedBitmap.Width, payload.Bitmap.Width);
        Assert.Equal(expectedBitmap.Height, payload.Bitmap.Height);
        Assert.Equal(expectedBitmap.Bytes, payload.Bitmap.Bytes);
        Assert.Equal(previewSize, renderer.GetPageSize(document));
        var scale = (72.0 / 2.54) / document.Options.PointsPerCentimeter;
        AssertClose(previewSize.Width * scale, payload.PdfPageSize.Width);
        AssertClose(previewSize.Height * scale, payload.PdfPageSize.Height);
        AssertPageSize(payload.Pdf, payload.PdfPageSize.Width, payload.PdfPageSize.Height);
    }

    [AvaloniaTheory]
    [InlineData("Portrait")]
    [InlineData("Landscape")]
    public async Task LinuxCompositionPreservesCorrectedVectorPdf(string orientation)
    {
        var document = Figure("complete");
        using var payload = await GraphPrintTarget.FromPublicationFigure("Dimensions", () => document, new SkiaFigureRenderer()).CaptureAsync();
        var media = new CupsMedia("A4", 21000, 29700, 500, 500, 500, 500);
        var printer = new CupsPrinter("Test", "Test", true, new[] { media }, "A4", Array.Empty<CupsColorMode>(), null);
        var options = new LinuxPrintOptions(printer, media, Enum.Parse<LinuxPrintOrientation>(orientation), 1, null);
        var pdf = (byte[])typeof(CupsGraphPrintBackend).GetMethod("ComposePdf", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { payload, options })!;

        Assert.Same(payload.Pdf, pdf);
        AssertGeometry(pdf, document, 283.464567);
    }

    [AvaloniaFact]
    public void InspectorUsesRelativeTypographyWordingAndSharedLineWidthTooltip()
    {
        var workspace = new FinalFigureWorkspaceControl();
        var labels = workspace.GetLogicalDescendants().OfType<TextBlock>().Select(label => label.Text).ToList();
        Assert.Contains("Base font", labels);
        Assert.Contains("Ticks use the base size; axis titles use base + 1; parameter boxes use 12.", labels);
        Assert.Equal(FinalFigureModelPresentation.LineWidthTooltip, ToolTip.GetTip(workspace.LineWidthForTesting));
    }

    [Fact]
    public void FrameReaderHandlesConcatenationRestoreAndEquivalentLinePaths()
    {
        var frames = StrokedBounds("2 0 0 2 10 20 cm q 1 0 0 1 3 4 cm 0 0 6 10 re S Q 0 0 m 6 0 l 6 10 l 0 10 l h S");
        Assert.Equal(new Bounds(16, 28, 28, 48), frames[0]);
        Assert.Equal(new Bounds(10, 20, 22, 40), frames[1]);
    }

    static PublicationFigureDocument Figure(string panels, double width = 6, double? density = null)
    {
        var options = new PublicationFigureOptions
        {
            PlotWidthCentimeters = width, PlotHeightCentimeters = 10,
            PointsPerCentimeter = density ?? PublicationFigureOptions.DefaultPointsPerCentimeter,
            Font = PublicationFont.LiberationSans, FontSize = 14,
            ShowExperimentDetails = false, ShowFitParameters = false
        };
        PublicationFigurePanel Panel(PublicationPanelKind kind) => new()
        {
            Kind = kind,
            XAxis = new PublicationAxis("X", kind == PublicationPanelKind.Thermogram ? PublicationAxisPlacement.Top : PublicationAxisPlacement.Bottom, 0, 2, 3),
            YAxis = new PublicationAxis("Y", PublicationAxisPlacement.Left, -1, 1, 3)
        };
        return new PublicationFigureDocument(options)
        {
            Title = "CR-031 dimensions", Creator = "CR-031 creator", Subject = "CR-031 subject",
            PlotWidth = width * options.PointsPerCentimeter, PlotHeight = 10 * options.PointsPerCentimeter,
            ThermogramPanel = panels != "fit" ? Panel(PublicationPanelKind.Thermogram) : null,
            FitPanel = panels != "thermogram" ? Panel(PublicationPanelKind.Fit) : null,
            ResidualPanel = panels == "complete" ? Panel(PublicationPanelKind.Residual) : null
        };
    }

    static void AssertGeometry(byte[] pdf, PublicationFigureDocument document, double height)
    {
        var renderer = new SkiaFigureRenderer();
        var previewSize = renderer.GetPageSize(document);
        var scale = (72.0 / 2.54) / document.Options.PointsPerCentimeter;
        AssertPageSize(pdf, previewSize.Width * scale, previewSize.Height * scale);
        var text = Encoding.Latin1.GetString(pdf);
        Assert.Contains("CR-031 dimensions", text);
        Assert.Contains("CR-031 creator", text);
        Assert.Contains("CR-031 subject", text);
        Assert.Contains("/FontFile2", text);
        Assert.DoesNotContain("/Subtype /Image", text);

        // In this fixture, panel frames are the only stroked paths with height and width.
        var frames = StrokedBounds(PageContent(pdf)).Where(frame => frame.Width > 20 && frame.Height > 20).ToList();
        Assert.Equal(document.Panels.Count(), frames.Count);
        foreach (var frame in frames) AssertClose(170.078740, frame.Width);
        AssertClose(height, frames.Max(frame => frame.Bottom) - frames.Min(frame => frame.Top));
    }

    static void AssertPageSize(byte[] pdf, double width, double height)
    {
        var match = Regex.Match(Encoding.Latin1.GetString(pdf), @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]");
        Assert.True(match.Success);
        AssertClose(width, double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), 1);
        AssertClose(height, double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), 1);
    }

    static void AssertClose(double expected, double actual, double tolerance = GeometryTolerance)
        => Assert.InRange(actual, expected - tolerance, expected + tolerance);

    static string PageContent(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var reference = Regex.Match(text, @"/Contents\s+(\d+)\s+0\s+R");
        Assert.True(reference.Success);
        var content = Regex.Match(text, @"(?m)^" + reference.Groups[1].Value + @" 0 obj\b(?<dictionary>[\s\S]*?)stream\r?\n");
        Assert.True(content.Success);
        var dictionary = content.Groups["dictionary"].Value;
        Assert.Contains("/FlateDecode", dictionary);
        var length = int.Parse(Regex.Match(dictionary, @"/Length\s+(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        using var source = new MemoryStream(pdf, content.Index + content.Length, length);
        using var inflated = new ZLibStream(source, CompressionMode.Decompress);
        using var reader = new StreamReader(inflated, Encoding.ASCII);
        return reader.ReadToEnd();
    }

    // A reader for the graphics-state and frame-path operators in these controlled fixtures,
    // not a general PDF parser. Text strings/names are skipped as whole tokens.
    static List<Bounds> StrokedBounds(string content)
    {
        var matrix = new Matrix(1, 0, 0, 1, 0, 0);
        var saved = new Stack<Matrix>();
        var operands = new List<double>();
        var path = new List<(double X, double Y)>();
        var result = new List<Bounds>();
        foreach (Match token in Regex.Matches(content, @"<[^>]*>|\((?:\\.|[^\\)])*\)|/[^\s<>\[\]()]+|[-+]?(?:\d*\.?\d+)(?:[Ee][-+]?\d+)?|[A-Za-z][A-Za-z*]*"))
        {
            var value = token.Value;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                operands.Add(number);
                continue;
            }
            if (value[0] is '/' or '<' or '(') continue;
            switch (value)
            {
                case "q": saved.Push(matrix); break;
                case "Q": matrix = saved.Pop(); break;
                case "cm": matrix = matrix.Concat(new Matrix(operands[0], operands[1], operands[2], operands[3], operands[4], operands[5])); break;
                case "m":
                case "l": path.Add(matrix.Transform(operands[0], operands[1])); break;
                case "re":
                    var x = operands[0]; var y = operands[1]; var w = operands[2]; var h = operands[3];
                    path.Add(matrix.Transform(x, y)); path.Add(matrix.Transform(x + w, y));
                    path.Add(matrix.Transform(x + w, y + h)); path.Add(matrix.Transform(x, y + h));
                    break;
                case "S": case "s": case "B": case "B*": case "b": case "b*":
                    if (path.Count > 0) result.Add(new Bounds(path.Min(p => p.X), path.Min(p => p.Y), path.Max(p => p.X), path.Max(p => p.Y)));
                    path.Clear();
                    break;
                case "n": case "f": case "f*": path.Clear(); break;
            }
            operands.Clear();
        }
        Assert.Empty(saved);
        return result;
    }

    readonly record struct Bounds(double Left, double Top, double Right, double Bottom)
    {
        public double Width => Right - Left;
        public double Height => Bottom - Top;
    }

    readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public (double X, double Y) Transform(double x, double y) => (A * x + C * y + E, B * x + D * y + F);
        public Matrix Concat(Matrix m) => new(A * m.A + C * m.B, B * m.A + D * m.B,
            A * m.C + C * m.D, B * m.C + D * m.D, A * m.E + C * m.F + E, B * m.E + D * m.F + F);
    }
}
