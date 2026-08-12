using System;
using System.IO;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Skia.Helpers;

using SkiaSharp;

using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Printing;

internal sealed class GraphPrintTarget
{
    readonly Func<Task<GraphPrintPayload>> capture;

    GraphPrintTarget(string jobName, Func<Task<GraphPrintPayload>> capture)
    {
        JobName = string.IsNullOrWhiteSpace(jobName) ? "FT-ITC graph" : jobName;
        this.capture = capture;
    }

    public string JobName { get; }

    public Task<GraphPrintPayload> CaptureAsync() => capture();

    public static GraphPrintTarget FromVisual(string jobName, Control visual)
        => new(jobName, () => CaptureVisualAsync(jobName, visual));

    public static GraphPrintTarget FromPublicationFigure(
        string jobName,
        Func<PublicationFigureDocument> documentFactory,
        SkiaFigureRenderer renderer)
        => new(jobName, () => CapturePublicationFigureAsync(jobName, documentFactory, renderer));

    static Task<GraphPrintPayload> CaptureVisualAsync(string jobName, Control visual)
        => CaptureVisualCoreAsync(jobName, visual);

    static async Task<GraphPrintPayload> CaptureVisualCoreAsync(string jobName, Control visual)
    {
        var width = visual.Bounds.Width;
        var height = visual.Bounds.Height;
        if (width < 1 || height < 1)
            throw new InvalidOperationException("The active graph has no printable size.");

        var pixelSize = CalculatePixelSize(width, height, out var scale);
        var dpi = new Vector(96 * scale, 96 * scale);

        using var scope = GraphPrintRenderScope.Enter();
        var pdf = await CreateVectorPdfAsync(jobName, visual, width, height);
        using var rendered = new RenderTargetBitmap(pixelSize, dpi);
        rendered.Render(visual);
        using var pixels = new WriteableBitmap(pixelSize, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var framebuffer = pixels.Lock();
        rendered.CopyPixels(framebuffer);
        var bitmap = new SKBitmap(new SKImageInfo(pixelSize.Width, pixelSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var source = new SKPixmap(bitmap.Info, framebuffer.Address, framebuffer.RowBytes);
        if (!source.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes))
        {
            bitmap.Dispose();
            throw new InvalidOperationException("The active graph could not be rendered.");
        }
        return new GraphPrintPayload(
            jobName,
            bitmap,
            pdf,
            preservePdf: true,
            pdfPageSize: new PrintSize(width, height));
    }

    static async Task<byte[]> CreateVectorPdfAsync(
        string jobName,
        Control visual,
        double width,
        double height)
    {
        using var stream = new MemoryStream();
        var metadata = new SKDocumentPdfMetadata
        {
            Title = jobName,
            Author = MarkdownStrings.AppName,
            Creator = MarkdownStrings.AppName
        };
        using var document = SKDocument.CreatePdf(stream, metadata);
        var canvas = document.BeginPage((float)width, (float)height);
        canvas.Clear(SKColors.White);
        await DrawingContextHelper.RenderAsync(
            canvas,
            visual,
            new Rect(0, 0, width, height),
            new Vector(96, 96));
        document.EndPage();
        document.Close();
        return stream.ToArray();
    }

    internal static PixelSize CalculatePixelSize(double width, double height, out double scale)
    {
        const double targetDpi = 300;
        const int maximumDimension = 6000;
        scale = targetDpi / 96.0;
        scale = Math.Min(scale, maximumDimension / Math.Max(width, height));
        scale = Math.Max(double.Epsilon, scale);
        return new PixelSize(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    static Task<GraphPrintPayload> CapturePublicationFigureAsync(
        string jobName,
        Func<PublicationFigureDocument> documentFactory,
        SkiaFigureRenderer renderer)
    {
        const int maximumDimension = 6000;
        var document = documentFactory();
        var pageSize = renderer.GetPageSize(document);
        var pixelWidth = Math.Min(maximumDimension, Math.Max(1200, (int)Math.Ceiling(pageSize.Width / 72f * 300f)));
        var bitmap = renderer.RenderBitmap(document, pixelWidth);
        using var pdfStream = new MemoryStream();
        renderer.WritePdf(document, pdfStream);
        return Task.FromResult(new GraphPrintPayload(
            jobName,
            bitmap,
            pdfStream.ToArray(),
            preservePdf: true,
            pdfPageSize: new PrintSize(pageSize.Width, pageSize.Height)));
    }
}

internal sealed class GraphPrintPayload : IDisposable
{
    public GraphPrintPayload(
        string jobName,
        SKBitmap bitmap,
        byte[] pdf,
        bool preservePdf = false,
        PrintSize? pdfPageSize = null)
    {
        JobName = jobName;
        Bitmap = bitmap;
        Pdf = pdf;
        PreservePdf = preservePdf;
        PdfPageSize = pdfPageSize is { IsValid: true }
            ? pdfPageSize.Value
            : new PrintSize(bitmap.Width, bitmap.Height);
    }

    public string JobName { get; }
    public SKBitmap Bitmap { get; }
    public byte[] Pdf { get; }
    public bool PreservePdf { get; }
    public PrintSize PdfPageSize { get; }
    public PrintSize SourceSize => new(Bitmap.Width, Bitmap.Height);

    public void Dispose() => Bitmap.Dispose();
}
