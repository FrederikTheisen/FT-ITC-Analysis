using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SkiaSharp;

using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Drawing;

public sealed class SkiaFigureRenderer
{
    static readonly SKColor White = SKColors.White;
    static readonly SKColor Black = SKColors.Black;
    static readonly SKColor Gray = new SKColor(120, 120, 120);
    static readonly SKColor LightGray = new SKColor(205, 205, 205);
    static readonly SKColor BandGray = new SKColor(190, 190, 190, 85);
    static readonly SKColor AnnotationBackground = new SKColor(255, 255, 255, 238);
    static readonly SKColor AnnotationBorder = new SKColor(210, 210, 210);

    const float AxisStrokeWidth = 1.35f;
    const float DataStrokeWidth = 1.15f;
    const float FitStrokeWidth = 1.8f;
    const float TickLength = 5.5f;
    const float MinorTickLength = 3f;
    const float TickTextSize = 10.5f;
    const float AxisTitleSize = 12.5f;
    const float AnnotationTextSize = 10.5f;

    public SKSize GetPageSize(PublicationFigureDocument document)
    {
        var layout = PublicationFigureLayout.Create(document);
        return new SKSize(layout.PageWidth, layout.PageHeight);
    }

    public SKBitmap RenderBitmap(PublicationFigureDocument document, int maxPixelWidth, int maxPixelHeight = 0)
    {
        var layout = PublicationFigureLayout.Create(document);
        var width = Math.Max(320, maxPixelWidth);
        var height = maxPixelHeight > 0
            ? Math.Max(320, maxPixelHeight)
            : (int)Math.Round(width * layout.PageHeight / layout.PageWidth);

        var targetAspect = layout.PageWidth / layout.PageHeight;
        var actualAspect = width / (float)height;

        if (actualAspect > targetAspect)
        {
            width = (int)Math.Round(height * targetAspect);
        }
        else
        {
            height = (int)Math.Round(width / targetAspect);
        }

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(White);
        canvas.Scale(width / layout.PageWidth, height / layout.PageHeight);
        DrawDocument(canvas, document, layout);
        canvas.Flush();

        return bitmap;
    }

    public void WritePdf(PublicationFigureDocument document, string path)
    {
        var layout = PublicationFigureLayout.Create(document);
        var metadata = new SKDocumentPdfMetadata
        {
            Title = document.Title,
            Author = MarkdownStrings.AppName,
            Creator = document.Creator,
            Subject = document.Subject,
            Keywords = string.Join(", ", document.MetadataKeywords)
        };

        using var pdf = SKDocument.CreatePdf(path, metadata);
        var canvas = pdf.BeginPage(layout.PageWidth, layout.PageHeight);
        DrawDocument(canvas, document, layout);
        pdf.EndPage();
        pdf.Close();
    }

    void DrawDocument(SKCanvas canvas, PublicationFigureDocument document, PublicationFigureLayout layout)
    {
        var drawing = new SkiaDrawingContext(canvas);

        drawing.FillRect(new SKRect(0, 0, layout.PageWidth, layout.PageHeight), White);

        if (document.ThermogramPanel != null)
        {
            DrawPanel(drawing, document, document.ThermogramPanel, layout.ThermogramRect, drawXAxisLabels: true);
        }

        if (document.FitPanel != null)
        {
            DrawPanel(drawing, document, document.FitPanel, layout.FitRect, drawXAxisLabels: document.ResidualPanel == null);
        }

        if (document.ResidualPanel != null)
        {
            DrawPanel(drawing, document, document.ResidualPanel, layout.ResidualRect, drawXAxisLabels: true);
        }
    }

    void DrawPanel(SkiaDrawingContext drawing, PublicationFigureDocument document, PublicationFigurePanel panel, SKRect rect, bool drawXAxisLabels)
    {
        drawing.FillRect(rect, White);

        drawing.Save();
        drawing.Clip(rect);

        foreach (var band in panel.Bands)
        {
            DrawBand(drawing, panel, rect, band);
        }

        if (panel.DrawZeroLine && panel.YAxis.Minimum < 0 && panel.YAxis.Maximum > 0)
        {
            var zero = Transform(panel, rect, 0, 0);
            drawing.DrawLine(new SKPoint(rect.Left, zero.Y), new SKPoint(rect.Right, zero.Y), Gray, 0.9f);
        }

        foreach (var marker in panel.Markers)
        {
            var x = TransformX(panel, rect, marker.X);
            drawing.DrawLine(new SKPoint(x, rect.Top), new SKPoint(x, rect.Bottom), new SKColor(150, 150, 150, 120), 0.55f);
        }

        foreach (var series in panel.Series)
        {
            DrawSeries(drawing, panel, rect, series);
        }

        foreach (var point in panel.Points)
        {
            DrawErrorPoint(drawing, document.Options, panel, rect, point);
        }

        drawing.Restore();

        drawing.DrawRect(rect, Black, AxisStrokeWidth);
        DrawAxes(drawing, document, panel, rect, drawXAxisLabels);

        foreach (var box in panel.AnnotationBoxes)
        {
            DrawAnnotationBox(drawing, panel, rect, box);
        }
    }

    void DrawSeries(SkiaDrawingContext drawing, PublicationFigurePanel panel, SKRect rect, PublicationSeries series)
    {
        if (series.Points.Count < 2) return;

        var points = series.Points
            .Where(point => IsFinite(point.X) && IsFinite(point.Y))
            .Select(point => Transform(panel, rect, point.X, point.Y))
            .ToList();

        if (points.Count < 2) return;

        var color = series.Role == PublicationSeriesRole.Fit ? Black : Black;
        var width = series.Role == PublicationSeriesRole.Fit ? FitStrokeWidth : DataStrokeWidth;
        var smooth = series.Role == PublicationSeriesRole.Fit;

        drawing.DrawPolyline(points, color, width, smooth);
    }

    void DrawBand(SkiaDrawingContext drawing, PublicationFigurePanel panel, SKRect rect, PublicationBand band)
    {
        if (band.Upper.Count < 2 || band.Lower.Count < 2) return;

        var path = new SKPath();
        var upper = band.Upper.Select(point => Transform(panel, rect, point.X, point.Y)).ToList();
        var lower = band.Lower.Select(point => Transform(panel, rect, point.X, point.Y)).Reverse().ToList();

        if (upper.Count == 0 || lower.Count == 0) return;

        path.MoveTo(upper[0]);
        foreach (var point in upper.Skip(1)) path.LineTo(point);
        foreach (var point in lower) path.LineTo(point);
        path.Close();

        drawing.FillPath(path, BandGray);
    }

    void DrawErrorPoint(SkiaDrawingContext drawing, PublicationFigureOptions options, PublicationFigurePanel panel, SKRect rect, PublicationErrorPoint point)
    {
        var center = Transform(panel, rect, point.X, point.Y);
        var top = Transform(panel, rect, point.X, point.UpperY);
        var bottom = Transform(panel, rect, point.X, point.LowerY);
        var symbolSize = (float)Math.Max(3, options.SymbolSize);

        if (Math.Abs(top.Y - bottom.Y) > symbolSize * 0.7f)
        {
            var cap = symbolSize * 0.45f;
            drawing.DrawLine(new SKPoint(center.X, top.Y), new SKPoint(center.X, center.Y - symbolSize * 0.5f), Black, 0.9f);
            drawing.DrawLine(new SKPoint(center.X, center.Y + symbolSize * 0.5f), new SKPoint(center.X, bottom.Y), Black, 0.9f);
            drawing.DrawLine(new SKPoint(center.X - cap, top.Y), new SKPoint(center.X + cap, top.Y), Black, 0.9f);
            drawing.DrawLine(new SKPoint(center.X - cap, bottom.Y), new SKPoint(center.X + cap, bottom.Y), Black, 0.9f);
        }

        DrawSymbol(drawing, center, symbolSize, options.SymbolShape, point.Included);
    }

    void DrawSymbol(SkiaDrawingContext drawing, SKPoint center, float size, PublicationSymbolShape shape, bool filled)
    {
        var half = size * 0.5f;
        var rect = new SKRect(center.X - half, center.Y - half, center.X + half, center.Y + half);
        var fill = filled ? Black : White;

        if (shape == PublicationSymbolShape.Circle)
        {
            drawing.FillOval(rect, fill);
            drawing.DrawOval(rect, Black, 1);
            return;
        }

        drawing.FillRect(rect, fill);
        drawing.DrawRect(rect, Black, 1);
    }

    void DrawAxes(SkiaDrawingContext drawing, PublicationFigureDocument document, PublicationFigurePanel panel, SKRect rect, bool drawXAxisLabels)
    {
        DrawXAxis(drawing, document, panel, rect, drawXAxisLabels);
        DrawYAxis(drawing, document, panel, rect);
    }

    void DrawXAxis(SkiaDrawingContext drawing, PublicationFigureDocument document, PublicationFigurePanel panel, SKRect rect, bool drawLabels)
    {
        var labelsAtTop = panel.XAxis.Placement == PublicationAxisPlacement.Top;
        var labelY = labelsAtTop ? rect.Top - 8 : rect.Bottom + 8;
        var tickStart = labelsAtTop ? rect.Top : rect.Bottom;
        var tickDirection = labelsAtTop ? 1 : -1;

        foreach (var tick in panel.XAxis.MinorTicks)
        {
            var x = TransformX(panel, rect, tick);
            drawing.DrawLine(new SKPoint(x, tickStart), new SKPoint(x, tickStart + tickDirection * MinorTickLength), Black, 0.75f);
            drawing.DrawLine(new SKPoint(x, labelsAtTop ? rect.Bottom : rect.Top), new SKPoint(x, (labelsAtTop ? rect.Bottom : rect.Top) - tickDirection * MinorTickLength), Black, 0.75f);
        }

        foreach (var tick in panel.XAxis.MajorTicks)
        {
            var x = TransformX(panel, rect, tick);
            drawing.DrawLine(new SKPoint(x, tickStart), new SKPoint(x, tickStart + tickDirection * TickLength), Black, 0.95f);
            drawing.DrawLine(new SKPoint(x, labelsAtTop ? rect.Bottom : rect.Top), new SKPoint(x, (labelsAtTop ? rect.Bottom : rect.Top) - tickDirection * TickLength), Black, 0.95f);

            if (drawLabels)
            {
                var text = panel.XAxis.FormatTick(tick);
                if (labelsAtTop)
                {
                    drawing.DrawTextCentered(text, new SKPoint(x, labelY - TickTextSize), TickTextSize, Black);
                }
                else
                {
                    drawing.DrawTextCentered(text, new SKPoint(x, labelY + 1), TickTextSize, Black);
                }
            }
        }

        if (document.Options.ShowAxisTitles && drawLabels && !string.IsNullOrWhiteSpace(panel.XAxis.Title))
        {
            var y = labelsAtTop ? rect.Top - 28 : rect.Bottom + 31;
            drawing.DrawTextCentered(panel.XAxis.Title, new SKPoint(rect.MidX, y), AxisTitleSize, Black);
        }
    }

    void DrawYAxis(SkiaDrawingContext drawing, PublicationFigureDocument document, PublicationFigurePanel panel, SKRect rect)
    {
        foreach (var tick in panel.YAxis.MinorTicks)
        {
            var y = TransformY(panel, rect, tick);
            drawing.DrawLine(new SKPoint(rect.Left, y), new SKPoint(rect.Left + MinorTickLength, y), Black, 0.75f);
            drawing.DrawLine(new SKPoint(rect.Right, y), new SKPoint(rect.Right - MinorTickLength, y), Black, 0.75f);
        }

        foreach (var tick in panel.YAxis.MajorTicks)
        {
            var y = TransformY(panel, rect, tick);
            drawing.DrawLine(new SKPoint(rect.Left, y), new SKPoint(rect.Left + TickLength, y), Black, 0.95f);
            drawing.DrawLine(new SKPoint(rect.Right, y), new SKPoint(rect.Right - TickLength, y), Black, 0.95f);
            drawing.DrawTextRight(panel.YAxis.FormatTick(tick), new SKPoint(rect.Left - 8, y + TickTextSize * 0.35f), TickTextSize, Black);
        }

        if (document.Options.ShowAxisTitles && !string.IsNullOrWhiteSpace(panel.YAxis.Title))
        {
            drawing.DrawTextRotated(panel.YAxis.Title, new SKPoint(rect.Left - 50, rect.MidY), AxisTitleSize, Black, -90);
        }
    }

    void DrawAnnotationBox(SkiaDrawingContext drawing, PublicationFigurePanel panel, SKRect rect, PublicationAnnotationBox box)
    {
        if (box.Lines.Count == 0) return;

        const float paddingX = 6;
        const float paddingY = 5;
        const float lineGap = 2;

        var widths = box.Lines.Select(line => drawing.MeasureRichText(line, AnnotationTextSize).Width).ToList();
        var lineHeight = AnnotationTextSize * 1.25f;
        var width = widths.Max() + paddingX * 2;
        var height = box.Lines.Count * lineHeight + (box.Lines.Count - 1) * lineGap + paddingY * 2;
        var upper = ResolveBoxUpperPlacement(panel, box);
        var x = rect.Right - width - 8;
        var y = upper ? rect.Top + 8 : rect.Bottom - height - 8;
        var boxRect = new SKRect(x, y, x + width, y + height);

        drawing.FillRect(boxRect, AnnotationBackground);
        drawing.DrawRect(boxRect, AnnotationBorder, 0.75f);

        var textY = y + paddingY;
        foreach (var line in box.Lines)
        {
            drawing.DrawRichText(line, new SKPoint(x + paddingX, textY), AnnotationTextSize, Black);
            textY += lineHeight + lineGap;
        }
    }

    static bool ResolveBoxUpperPlacement(PublicationFigurePanel panel, PublicationAnnotationBox box)
    {
        if (box.Placement == PublicationInfoBoxPlacement.Upper) return true;
        if (box.Placement == PublicationInfoBoxPlacement.Lower) return false;

        if (panel.Kind == PublicationPanelKind.Fit)
        {
            var fit = panel.Series.FirstOrDefault(series => series.Role == PublicationSeriesRole.Fit);
            if (fit?.Points.Count > 1)
            {
                return fit.Points.First().Y > fit.Points.Last().Y;
            }
        }

        if (panel.Kind == PublicationPanelKind.Thermogram)
        {
            return Math.Abs(panel.YAxis.Maximum) >= Math.Abs(panel.YAxis.Minimum);
        }

        return true;
    }

    static SKPoint Transform(PublicationFigurePanel panel, SKRect rect, double x, double y)
    {
        return new SKPoint(
            TransformX(panel, rect, x),
            TransformY(panel, rect, y));
    }

    static float TransformX(PublicationFigurePanel panel, SKRect rect, double value)
    {
        var span = panel.XAxis.Maximum - panel.XAxis.Minimum;
        if (Math.Abs(span) < double.Epsilon) span = 1;

        return rect.Left + (float)((value - panel.XAxis.Minimum) / span * rect.Width);
    }

    static float TransformY(PublicationFigurePanel panel, SKRect rect, double value)
    {
        var span = panel.YAxis.Maximum - panel.YAxis.Minimum;
        if (Math.Abs(span) < double.Epsilon) span = 1;

        return rect.Bottom - (float)((value - panel.YAxis.Minimum) / span * rect.Height);
    }

    static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

sealed class PublicationFigureLayout
{
    public float PageWidth { get; private set; }
    public float PageHeight { get; private set; }
    public SKRect ThermogramRect { get; private set; }
    public SKRect FitRect { get; private set; }
    public SKRect ResidualRect { get; private set; }

    public static PublicationFigureLayout Create(PublicationFigureDocument document)
    {
        const float leftMargin = 70;
        const float rightMargin = 12;
        const float topMargin = 38;
        const float bottomMargin = 54;
        const float residualGap = 6;

        var plotWidth = (float)Math.Max(120, document.PlotWidth);
        var plotHeight = (float)Math.Max(160, document.PlotHeight);
        var pageWidth = leftMargin + plotWidth + rightMargin;
        var pageHeight = topMargin + plotHeight + bottomMargin;
        var plotLeft = leftMargin;
        var plotTop = topMargin;
        var hasThermogram = document.ThermogramPanel != null;
        var hasResidual = document.ResidualPanel != null;
        var thermogramHeight = hasThermogram ? plotHeight * 0.5f : 0;
        var fitCompositeTop = plotTop + thermogramHeight;
        var fitCompositeHeight = hasThermogram ? plotHeight - thermogramHeight : plotHeight;
        var residualFraction = (float)Math.Max(0.05, Math.Min(0.5, document.Options.ResidualPanelFraction));
        var fitHeight = fitCompositeHeight;
        var residualHeight = 0f;
        var gap = 0f;

        if (hasResidual)
        {
            gap = document.Options.IncludeResidualGraphGap ? residualGap : 0;
            residualHeight = Math.Max(35, fitCompositeHeight * residualFraction);
            fitHeight = Math.Max(60, fitCompositeHeight - residualHeight - gap);
        }

        return new PublicationFigureLayout
        {
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            ThermogramRect = hasThermogram
                ? new SKRect(plotLeft, plotTop, plotLeft + plotWidth, plotTop + thermogramHeight)
                : SKRect.Empty,
            FitRect = new SKRect(plotLeft, fitCompositeTop, plotLeft + plotWidth, fitCompositeTop + fitHeight),
            ResidualRect = hasResidual
                ? new SKRect(plotLeft, fitCompositeTop + fitHeight + gap, plotLeft + plotWidth, fitCompositeTop + fitHeight + gap + residualHeight)
                : SKRect.Empty
        };
    }
}

sealed class SkiaDrawingContext
{
    readonly SKCanvas canvas;

    public SkiaDrawingContext(SKCanvas canvas)
    {
        this.canvas = canvas;
    }

    public void Save() => canvas.Save();

    public void Restore() => canvas.Restore();

    public void Clip(SKRect rect) => canvas.ClipRect(rect, SKClipOperation.Intersect, true);

    public void FillRect(SKRect rect, SKColor color)
    {
        using var paint = FillPaint(color);
        canvas.DrawRect(rect, paint);
    }

    public void DrawRect(SKRect rect, SKColor color, float width)
    {
        using var paint = StrokePaint(color, width);
        canvas.DrawRect(rect, paint);
    }

    public void FillOval(SKRect rect, SKColor color)
    {
        using var paint = FillPaint(color);
        canvas.DrawOval(rect, paint);
    }

    public void DrawOval(SKRect rect, SKColor color, float width)
    {
        using var paint = StrokePaint(color, width);
        canvas.DrawOval(rect, paint);
    }

    public void FillPath(SKPath path, SKColor color)
    {
        using var paint = FillPaint(color);
        canvas.DrawPath(path, paint);
    }

    public void DrawLine(SKPoint start, SKPoint end, SKColor color, float width)
    {
        using var paint = StrokePaint(color, width);
        canvas.DrawLine(start, end, paint);
    }

    public void DrawPolyline(IReadOnlyList<SKPoint> points, SKColor color, float width, bool smooth)
    {
        if (points.Count < 2) return;

        using var path = smooth ? SmoothedPath(points) : LinearPath(points);
        using var paint = StrokePaint(color, width);
        canvas.DrawPath(path, paint);
    }

    public void DrawTextCentered(string text, SKPoint point, float size, SKColor color)
    {
        var metrics = MeasureText(text, size);
        DrawText(text, new SKPoint(point.X - metrics.Width / 2, point.Y + metrics.Height), size, color);
    }

    public void DrawTextRight(string text, SKPoint point, float size, SKColor color)
    {
        var metrics = MeasureText(text, size);
        DrawText(text, new SKPoint(point.X - metrics.Width, point.Y), size, color);
    }

    public void DrawTextRotated(string text, SKPoint point, float size, SKColor color, float degrees)
    {
        canvas.Save();
        canvas.Translate(point);
        canvas.RotateDegrees(degrees);
        DrawTextCentered(text, new SKPoint(0, -size), size, color);
        canvas.Restore();
    }

    public void DrawText(string text, SKPoint topLeft, float size, SKColor color, bool bold = false, bool italic = false)
    {
        using var paint = TextPaint(color);
        using var font = TextFont(size, bold, italic);
        var metrics = font.Metrics;
        var baseline = topLeft.Y - metrics.Ascent;
        canvas.DrawText(text ?? "", topLeft.X, baseline, SKTextAlign.Left, font, paint);
    }

    public SKSize MeasureText(string text, float size, bool bold = false, bool italic = false)
    {
        using var paint = TextPaint(SKColors.Black);
        using var font = TextFont(size, bold, italic);
        var width = font.MeasureText(text ?? "", paint);
        var metrics = font.Metrics;

        return new SKSize(width, metrics.Descent - metrics.Ascent);
    }

    public void DrawRichText(string text, SKPoint topLeft, float size, SKColor color)
    {
        var x = topLeft.X;
        var baseline = topLeft.Y + size;

        foreach (var part in RichTextParts(text, size))
        {
            using var paint = TextPaint(color);
            using var font = TextFont(part.Size, part.Bold, part.Italic);
            canvas.DrawText(part.Text, x, baseline + part.BaselineOffset, SKTextAlign.Left, font, paint);
            x += font.MeasureText(part.Text, paint);
        }
    }

    public SKSize MeasureRichText(string text, float size)
    {
        var width = 0f;
        var height = size * 1.25f;

        foreach (var part in RichTextParts(text, size))
        {
            width += MeasureText(part.Text, part.Size, part.Bold, part.Italic).Width;
        }

        return new SKSize(width, height);
    }

    static IEnumerable<RichTextPart> RichTextParts(string text, float size)
    {
        foreach (var segment in MarkdownProcessor.GetSegments(text ?? ""))
        {
            switch (segment.Property)
            {
                case MarkdownProperty.Cursive:
                    yield return new RichTextPart(segment.Text, size, false, true, 0);
                    break;
                case MarkdownProperty.Bold:
                    yield return new RichTextPart(segment.Text, size, true, false, 0);
                    break;
                case MarkdownProperty.Subscript:
                    yield return new RichTextPart(segment.Text, size * 0.72f, false, false, size * 0.28f);
                    break;
                case MarkdownProperty.Superscript:
                    yield return new RichTextPart(segment.Text, size * 0.72f, false, false, -size * 0.38f);
                    break;
                case MarkdownProperty.Small:
                    yield return new RichTextPart(segment.Text, size * 0.82f, false, false, 0);
                    break;
                default:
                    yield return new RichTextPart(segment.Text, size, false, false, 0);
                    break;
            }
        }
    }

    static SKPaint FillPaint(SKColor color)
    {
        return new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
    }

    static SKPaint StrokePaint(SKColor color, float width)
    {
        return new SKPaint
        {
            Color = color,
            IsAntialias = true,
            StrokeWidth = width,
            Style = SKPaintStyle.Stroke
        };
    }

    static SKPaint TextPaint(SKColor color)
    {
        return new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
    }

    static SKFont TextFont(float size, bool bold, bool italic)
    {
        return new SKFont
        {
            Size = size,
            Embolden = bold,
            SkewX = italic ? -0.25f : 0
        };
    }

    static SKPath LinearPath(IReadOnlyList<SKPoint> points)
    {
        var path = new SKPath();
        path.MoveTo(points[0]);
        for (int i = 1; i < points.Count; i++) path.LineTo(points[i]);
        return path;
    }

    static SKPath SmoothedPath(IReadOnlyList<SKPoint> points)
    {
        var path = new SKPath();
        path.MoveTo(points[0]);

        if (points.Count < 3)
        {
            path.LineTo(points[1]);
            return path;
        }

        for (int i = 0; i < points.Count - 1; i++)
        {
            var p0 = i == 0 ? points[i] : points[i - 1];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i + 2 < points.Count ? points[i + 2] : p2;
            var c1 = new SKPoint(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
            var c2 = new SKPoint(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);

            path.CubicTo(c1, c2, p2);
        }

        return path;
    }

    readonly struct RichTextPart
    {
        public RichTextPart(string text, float size, bool bold, bool italic, float baselineOffset)
        {
            Text = text ?? "";
            Size = size;
            Bold = bold;
            Italic = italic;
            BaselineOffset = baselineOffset;
        }

        public string Text { get; }
        public float Size { get; }
        public bool Bold { get; }
        public bool Italic { get; }
        public float BaselineOffset { get; }
    }
}
