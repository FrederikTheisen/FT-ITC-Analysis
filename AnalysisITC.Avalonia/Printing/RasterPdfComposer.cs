using System;
using System.IO;

using SkiaSharp;

using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Printing;

internal static class RasterPdfComposer
{
    const float DefaultLongEdgePoints = 720;

    public static byte[] CreateGraphPdf(SKBitmap bitmap, string title)
    {
        var landscape = bitmap.Width >= bitmap.Height;
        var width = landscape
            ? DefaultLongEdgePoints
            : DefaultLongEdgePoints * bitmap.Width / (float)bitmap.Height;
        var height = landscape
            ? DefaultLongEdgePoints * bitmap.Height / (float)bitmap.Width
            : DefaultLongEdgePoints;
        return CreatePagePdf(bitmap, title, new PrintSize(width, height), new PrintRect(0, 0, width, height));
    }

    public static byte[] CreatePagePdf(
        SKBitmap bitmap,
        string title,
        PrintSize pageSize,
        PrintRect imageableArea)
    {
        if (!pageSize.IsValid)
            throw new ArgumentException("The print page size is invalid.", nameof(pageSize));

        var target = PrintGeometry.Fit(new PrintSize(bitmap.Width, bitmap.Height), imageableArea);
        using var stream = new MemoryStream();
        var metadata = new SKDocumentPdfMetadata
        {
            Title = title,
            Author = MarkdownStrings.AppName,
            Creator = MarkdownStrings.AppName
        };
        using var document = SKDocument.CreatePdf(stream, metadata);
        var canvas = document.BeginPage((float)pageSize.Width, (float)pageSize.Height);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, new SKRect(
            (float)target.X,
            (float)target.Y,
            (float)target.Right,
            (float)target.Bottom));
        document.EndPage();
        document.Close();
        return stream.ToArray();
    }
}
