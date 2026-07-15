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
    static readonly SKColor BaselineRed = new SKColor(220, 35, 35);
    static readonly SKColor AnnotationBackground = SKColors.White;
    static readonly SKColor AnnotationBorder = SKColors.Black;

    internal const float PointsPerCentimeter = 0.5f * 227f / 2.54f;
    internal const float PageInset = 0.1f * PointsPerCentimeter;
    internal const float AxisStrokeWidth = 1f;
    internal const float DataStrokeWidth = 1f;
    internal const float FitStrokeWidth = 2f;
    internal const float TickStrokeWidth = AxisStrokeWidth;
    internal const float TickLength = 6f;
    internal const float MinorTickLength = 3f;
    internal const float AxisLabelHorizontalOffset = 6f;
    internal const float AxisLabelVerticalOffset = 2f;
    internal const float HorizontalAxisTitleOffset = 0f;
    internal const float VerticalAxisTitleMinimumOffset = 4f;
    internal const float VerticalAxisTitleOffsetFontFraction = 0.3f;
    internal const float AnnotationInset = 8f;
    internal const float AnnotationPaddingX = 6f;
    internal const float AnnotationPaddingY = 2f;
    internal const float AnnotationLineSpacingFactor = 0.15f;
    internal const float ResidualGraphGap = 5f;

    internal static float VerticalAxisTitleOffset(float fontSize)
        => Math.Max(VerticalAxisTitleMinimumOffset, fontSize * VerticalAxisTitleOffsetFontFraction);

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
        DrawDocument(canvas, document, layout, PublicationFigureRenderSettings.Default);
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
        DrawDocument(canvas, document, layout, PublicationFigureRenderSettings.Default);
        pdf.EndPage();
        pdf.Close();
    }

    internal void DrawDocument(SKCanvas canvas, PublicationFigureDocument document, PublicationFigureLayout layout, PublicationFigureRenderSettings settings)
    {
        var drawing = new SkiaDrawingContext(canvas);

        drawing.FillRect(layout.PageRect, White);

        if (document.ThermogramPanel != null)
        {
            DrawPanel(drawing, document, document.ThermogramPanel, layout.ThermogramRect, drawXAxisLabels: true, settings, layout.PageLeft);
        }

        if (document.FitPanel != null)
        {
            DrawPanel(drawing, document, document.FitPanel, layout.FitRect, drawXAxisLabels: document.ResidualPanel == null, settings, layout.PageLeft);
        }

        if (document.ResidualPanel != null)
        {
            DrawPanel(drawing, document, document.ResidualPanel, layout.ResidualRect, drawXAxisLabels: true, settings, layout.PageLeft);
        }
    }

    void DrawPanel(SkiaDrawingContext drawing, PublicationFigureDocument document, PublicationFigurePanel panel, SKRect rect, bool drawXAxisLabels, PublicationFigureRenderSettings settings, float pageLeft)
    {
        var fontSize = settings.FontSize ?? (float)document.Options.FontSize;
        drawing.FillRect(rect, White);

        drawing.Save();
        drawing.Clip(rect);

        foreach (var band in panel.Bands)
        {
            DrawBand(drawing, panel, rect, band);
        }

        if (document.Options.IntegrationRegionStyle == PublicationIntegrationRegionStyle.Fill)
            foreach (var region in panel.IntegrationRegions) DrawIntegrationRegionFill(drawing, panel, rect, region);

        if (panel.DrawZeroLine && panel.YAxis.Minimum < 0 && panel.YAxis.Maximum > 0)
        {
            var zero = Transform(panel, rect, 0, 0);
            drawing.DrawLine(new SKPoint(rect.Left, zero.Y), new SKPoint(rect.Right, zero.Y), Gray, AxisStrokeWidth);
        }

        foreach (var marker in panel.Markers)
        {
            var x = TransformX(panel, rect, marker.X);
            drawing.DrawLine(new SKPoint(x, rect.Top), new SKPoint(x, rect.Bottom), new SKColor(150, 150, 150, 120), 0.55f);
        }

        foreach (var series in panel.Series)
        {
            DrawSeries(drawing, document.Options, panel, rect, series);
        }

        if (document.Options.IntegrationRegionStyle != PublicationIntegrationRegionStyle.Fill)
            foreach (var region in panel.IntegrationRegions) DrawIntegrationRegionMarker(drawing, document.Options, panel, rect, region);

        foreach (var point in panel.Points)
        {
            DrawErrorPoint(drawing, document.Options, panel, rect, point, settings.SymbolSize ?? (float)document.Options.SymbolSize);
        }

        drawing.Restore();

        drawing.DrawRect(rect, Black, AxisStrokeWidth);
        DrawAxes(drawing, document, panel, rect, drawXAxisLabels, settings, pageLeft);

        if (settings.ShowAnnotationBoxes)
            foreach (var box in panel.AnnotationBoxes)
                DrawAnnotationBox(drawing, panel, rect, box, settings.AnnotationFontSize ?? 12f);
    }

    void DrawSeries(SkiaDrawingContext drawing, PublicationFigureOptions options, PublicationFigurePanel panel, SKRect rect, PublicationSeries series)
    {
        if (series.Points.Count < 2) return;

        var points = series.Points
            .Where(point => IsFinite(point.X) && IsFinite(point.Y))
            .Select(point => Transform(panel, rect, point.X, point.Y))
            .ToList();

        if (points.Count < 2) return;

        var color = series.Role == PublicationSeriesRole.Baseline ? BaselineRed : Black;
        var width = series.Role switch
        {
            PublicationSeriesRole.Fit => (float)Math.Max(0.25, options.FitLineWidth),
            PublicationSeriesRole.Baseline => (float)Math.Max(0.25, options.BaselineWidth),
            _ => DataStrokeWidth
        };
        var smooth = series.Role == PublicationSeriesRole.Fit && options.FitLineSmoothness != LineSmoothness.Linear;

        drawing.DrawPolyline(points, color, width, smooth,
            series.Role == PublicationSeriesRole.Baseline && options.BaselineStyle == PublicationBaselineStyle.Dashed);
    }

    void DrawIntegrationRegionFill(SkiaDrawingContext drawing, PublicationFigurePanel panel, SKRect rect, PublicationIntegrationRegion region)
    {
        if (region.Data.Count < 2 || region.Baseline.Count < 2) return;
        using var path = new SKPath();
        var data = region.Data.Select(point => Transform(panel, rect, point.X, point.Y)).ToList();
        var baseline = region.Baseline.Select(point => Transform(panel, rect, point.X, point.Y)).Reverse().ToList();
        path.MoveTo(data[0]);
        foreach (var point in data.Skip(1)) path.LineTo(point);
        foreach (var point in baseline) path.LineTo(point);
        path.Close();
        drawing.FillPath(path, Black);
    }

    void DrawIntegrationRegionMarker(SkiaDrawingContext drawing, PublicationFigureOptions options, PublicationFigurePanel panel, SKRect rect, PublicationIntegrationRegion region)
    {
        if (region.Baseline.Count < 2) return;
        var color = new SKColor(120, 120, 120, 128);
        if (options.IntegrationRegionStyle == PublicationIntegrationRegionStyle.Bar)
        {
            var left = TransformX(panel, rect, region.Baseline.First().X);
            var right = TransformX(panel, rect, region.Baseline.Last().X);
            var top = region.BarAtTop ? rect.Top + 5 : rect.Bottom - 8;
            drawing.FillRect(new SKRect(Math.Min(left, right), top, Math.Max(left, right), top + 3), color);
            return;
        }

        foreach (var endpoint in new[] { region.Baseline.First(), region.Baseline.Last() })
        {
            var center = Transform(panel, rect, endpoint.X, endpoint.Y);
            drawing.DrawLine(new SKPoint(center.X, center.Y - 4), new SKPoint(center.X, center.Y + 4), color, 1);
        }
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

    void DrawErrorPoint(SkiaDrawingContext drawing, PublicationFigureOptions options, PublicationFigurePanel panel, SKRect rect, PublicationErrorPoint point, float symbolSize)
    {
        var center = Transform(panel, rect, point.X, point.Y);
        var top = Transform(panel, rect, point.X, point.UpperY);
        var bottom = Transform(panel, rect, point.X, point.LowerY);
        symbolSize = Math.Max(3, symbolSize);

        if (Math.Abs(top.Y - bottom.Y) > symbolSize * 0.7f)
        {
            var cap = symbolSize * 0.45f;
            drawing.DrawLine(new SKPoint(center.X, top.Y), new SKPoint(center.X, center.Y - symbolSize * 0.5f), Black, AxisStrokeWidth);
            drawing.DrawLine(new SKPoint(center.X, center.Y + symbolSize * 0.5f), new SKPoint(center.X, bottom.Y), Black, AxisStrokeWidth);
            drawing.DrawLine(new SKPoint(center.X - cap, top.Y), new SKPoint(center.X + cap, top.Y), Black, AxisStrokeWidth);
            drawing.DrawLine(new SKPoint(center.X - cap, bottom.Y), new SKPoint(center.X + cap, bottom.Y), Black, AxisStrokeWidth);
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

    void DrawAxes(SkiaDrawingContext drawing, PublicationFigureDocument document, PublicationFigurePanel panel, SKRect rect, bool drawXAxisLabels, PublicationFigureRenderSettings settings, float pageLeft)
    {
        var isTopAxis = panel.XAxis.Placement == PublicationAxisPlacement.Top;
        var showXAxisLabels = drawXAxisLabels && (isTopAxis ? settings.ShowTopXAxisTickLabels : settings.ShowBottomXAxisTickLabels);
        var showXAxisTitle = isTopAxis ? settings.ShowTopXAxisTitle : settings.ShowBottomXAxisTitle;
        var fontSize = settings.FontSize ?? (float)document.Options.FontSize;
        DrawXAxis(drawing, document, panel, rect, showXAxisLabels, drawXAxisLabels && showXAxisTitle, fontSize);
        DrawYAxis(drawing, document, panel, rect, settings.ShowYAxisTickLabels, settings.ShowYAxisTitle, pageLeft, fontSize);
    }

    void DrawXAxis(SkiaDrawingContext drawing, PublicationFigureDocument document, PublicationFigurePanel panel, SKRect rect, bool drawLabels, bool drawTitle, float fontSize)
    {
        var labelsAtTop = panel.XAxis.Placement == PublicationAxisPlacement.Top;
        var tickStart = labelsAtTop ? rect.Top : rect.Bottom;
        var tickDirection = labelsAtTop ? 1 : -1;
        var tickLabelHeight = drawLabels ? MaxTickLabelHeight(panel.XAxis, fontSize) : 0;

        foreach (var tick in panel.XAxis.MinorTicks)
        {
            var x = TransformX(panel, rect, tick);
            drawing.DrawLine(new SKPoint(x, tickStart), new SKPoint(x, tickStart + tickDirection * MinorTickLength), Black, TickStrokeWidth);
            drawing.DrawLine(new SKPoint(x, labelsAtTop ? rect.Bottom : rect.Top), new SKPoint(x, (labelsAtTop ? rect.Bottom : rect.Top) - tickDirection * MinorTickLength), Black, TickStrokeWidth);
        }

        foreach (var tick in panel.XAxis.MajorTicks)
        {
            var x = TransformX(panel, rect, tick);
            drawing.DrawLine(new SKPoint(x, tickStart), new SKPoint(x, tickStart + tickDirection * TickLength), Black, TickStrokeWidth);
            drawing.DrawLine(new SKPoint(x, labelsAtTop ? rect.Bottom : rect.Top), new SKPoint(x, (labelsAtTop ? rect.Bottom : rect.Top) - tickDirection * TickLength), Black, TickStrokeWidth);

            if (drawLabels)
            {
                var text = panel.XAxis.FormatTick(tick);
                if (labelsAtTop)
                {
                    drawing.DrawTextCenteredBottom(text, new SKPoint(x, rect.Top - AxisLabelVerticalOffset), fontSize, Black);
                }
                else
                {
                    drawing.DrawTextCenteredTop(text, new SKPoint(x, rect.Bottom + AxisLabelVerticalOffset), fontSize, Black);
                }
            }
        }

        if (document.Options.ShowAxisTitles && drawTitle && !string.IsNullOrWhiteSpace(panel.XAxis.Title))
        {
            if (labelsAtTop)
            {
                var bottom = rect.Top - AxisLabelVerticalOffset - tickLabelHeight - HorizontalAxisTitleOffset;
                drawing.DrawTextCenteredBottom(panel.XAxis.Title, new SKPoint(rect.MidX, bottom), fontSize + 1, Black);
            }
            else
            {
                var top = rect.Bottom + AxisLabelVerticalOffset + tickLabelHeight + HorizontalAxisTitleOffset;
                drawing.DrawTextCenteredTop(panel.XAxis.Title, new SKPoint(rect.MidX, top), fontSize + 1, Black);
            }
        }
    }

    void DrawYAxis(SkiaDrawingContext drawing, PublicationFigureDocument document, PublicationFigurePanel panel, SKRect rect, bool drawLabels, bool drawTitle, float pageLeft, float fontSize)
    {
        foreach (var tick in panel.YAxis.MinorTicks)
        {
            var y = TransformY(panel, rect, tick);
            drawing.DrawLine(new SKPoint(rect.Left, y), new SKPoint(rect.Left + MinorTickLength, y), Black, TickStrokeWidth);
            drawing.DrawLine(new SKPoint(rect.Right, y), new SKPoint(rect.Right - MinorTickLength, y), Black, TickStrokeWidth);
        }

        foreach (var tick in panel.YAxis.MajorTicks)
        {
            var y = TransformY(panel, rect, tick);
            drawing.DrawLine(new SKPoint(rect.Left, y), new SKPoint(rect.Left + TickLength, y), Black, TickStrokeWidth);
            drawing.DrawLine(new SKPoint(rect.Right, y), new SKPoint(rect.Right - TickLength, y), Black, TickStrokeWidth);
            if (drawLabels)
            {
                var point = new SKPoint(rect.Left - AxisLabelHorizontalOffset * fontSize / 12f, y);
                drawing.DrawTextRightMiddle(panel.YAxis.FormatTick(tick), point, fontSize, Black);
            }
        }

        if (document.Options.ShowAxisTitles && drawTitle && !string.IsNullOrWhiteSpace(panel.YAxis.Title))
        {
            var titleHeight = drawing.MeasureText(panel.YAxis.Title, fontSize + 1).Height;
            var titleCenterX = pageLeft + PageInset + titleHeight * 0.5f;
            drawing.DrawTextRotated(panel.YAxis.Title, new SKPoint(titleCenterX, rect.MidY), fontSize + 1, Black, -90);
        }
    }

    void DrawAnnotationBox(SkiaDrawingContext drawing, PublicationFigurePanel panel, SKRect rect, PublicationAnnotationBox box, float fontSize)
    {
        if (box.Lines.Count == 0) return;

        var widths = box.Lines.Select(line => drawing.MeasureRichText(line, fontSize).Width).ToList();
        var lineHeight = fontSize;
        var lineGap = fontSize * AnnotationLineSpacingFactor;
        var paddingx = AnnotationPaddingX * fontSize / 12f;
        var width = widths.Max() + paddingx * 2;
        var height = box.Lines.Count * lineHeight + (box.Lines.Count - 1) * lineGap + AnnotationPaddingY * 2;
        var upper = ResolveBoxUpperPlacement(panel, box);
        var x = rect.Right - width - AnnotationInset;
        var y = upper ? rect.Top + AnnotationInset : rect.Bottom - height - AnnotationInset;
        var boxRect = new SKRect(x, y, x + width, y + height);

        drawing.FillRect(boxRect, AnnotationBackground);
        drawing.DrawRect(boxRect, AnnotationBorder, AxisStrokeWidth);

        var textY = y;// + AnnotationPaddingY;
        foreach (var line in box.Lines)
        {
            drawing.DrawRichText(line, new SKPoint(x + paddingx, textY), fontSize, Black);
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

    internal static float MaxTickLabelWidth(PublicationAxis axis, float fontSize)
    {
        return axis.MajorTicks.Count == 0
            ? 0
            : axis.MajorTicks.Max(tick => SkiaDrawingContext.MeasureTextValue(axis.FormatTick(tick), fontSize).Width);
    }

    internal static float MaxTickLabelHeight(PublicationAxis axis, float fontSize)
    {
        return axis.MajorTicks.Count == 0
            ? 0
            : axis.MajorTicks.Max(tick => SkiaDrawingContext.MeasureTextValue(axis.FormatTick(tick), fontSize).Height);
    }
}

sealed class PublicationFigureRenderSettings
{
    public static readonly PublicationFigureRenderSettings Default = new PublicationFigureRenderSettings();

    public float? FontSize { get; set; }
    public float? AnnotationFontSize { get; set; }
    public float? SymbolSize { get; set; }
    public bool ShowAnnotationBoxes { get; set; } = true;
    public bool ShowTopXAxisTickLabels { get; set; } = true;
    public bool ShowBottomXAxisTickLabels { get; set; } = true;
    public bool ShowYAxisTickLabels { get; set; } = true;
    public bool ShowTopXAxisTitle { get; set; } = true;
    public bool ShowBottomXAxisTitle { get; set; } = true;
    public bool ShowYAxisTitle { get; set; } = true;
}

sealed class PublicationFigureLayout
{
    public float PageLeft { get; private set; }
    public float PageTop { get; private set; }
    public float PageWidth { get; private set; }
    public float PageHeight { get; private set; }
    public float LeftMargin { get; private set; }
    public float RightMargin { get; private set; }
    public float TopMargin { get; private set; }
    public float BottomMargin { get; private set; }
    public SKRect PageRect => new SKRect(PageLeft, PageTop, PageLeft + PageWidth, PageTop + PageHeight);
    public SKRect ThermogramRect { get; private set; }
    public SKRect FitRect { get; private set; }
    public SKRect ResidualRect { get; private set; }

    public static PublicationFigureLayout Create(PublicationFigureDocument document)
    {
        var plotWidth = (float)Math.Max(120, document.PlotWidth);
        var configuredPlotHeight = (float)Math.Max(160, document.PlotHeight);
        var leftMargin = RequiredLeftMargin(document);
        var rightMargin = RequiredRightMargin(document);
        var topMargin = RequiredTopMargin(document);
        var bottomMargin = RequiredBottomMargin(document);
        return Create(document, plotWidth, configuredPlotHeight, leftMargin, rightMargin, topMargin, bottomMargin, 0, 0, preserveFitOnlyHalfHeight: true);
    }

    public static PublicationFigureLayout CreateAligned(
        PublicationFigureDocument document,
        float plotWidth,
        float plotHeight,
        float leftMargin,
        float rightMargin,
        float topMargin,
        float bottomMargin,
        float pageLeft = 0,
        float pageTop = 0)
    {
        return Create(document, plotWidth, plotHeight, leftMargin, rightMargin, topMargin, bottomMargin, pageLeft, pageTop, preserveFitOnlyHalfHeight: false);
    }

    static PublicationFigureLayout Create(
        PublicationFigureDocument document,
        float plotWidth,
        float configuredPlotHeight,
        float leftMargin,
        float rightMargin,
        float topMargin,
        float bottomMargin,
        float pageLeft,
        float pageTop,
        bool preserveFitOnlyHalfHeight)
    {
        plotWidth = Math.Max(1, plotWidth);
        configuredPlotHeight = Math.Max(1, configuredPlotHeight);
        var hasThermogram = document.ThermogramPanel != null;
        var hasResidual = document.ResidualPanel != null;
        var plotHeight = preserveFitOnlyHalfHeight && !hasThermogram ? configuredPlotHeight * 0.5f : configuredPlotHeight;
        var pageWidth = leftMargin + plotWidth + rightMargin;
        var pageHeight = topMargin + plotHeight + bottomMargin;
        var plotLeft = pageLeft + leftMargin;
        var plotTop = pageTop + topMargin;
        var thermogramHeight = hasThermogram ? plotHeight * 0.5f : 0;
        var fitCompositeTop = plotTop + thermogramHeight;
        var fitCompositeHeight = hasThermogram ? plotHeight - thermogramHeight : plotHeight;
        var residualFraction = (float)Math.Max(0.05, Math.Min(0.5, document.Options.ResidualPanelFraction));
        var fitHeight = fitCompositeHeight;
        var residualHeight = 0f;
        var gap = 0f;

        if (hasResidual)
        {
            gap = document.Options.IncludeResidualGraphGap ? SkiaFigureRenderer.ResidualGraphGap : 0;
            residualHeight = preserveFitOnlyHalfHeight
                ? Math.Max(35, fitCompositeHeight * residualFraction)
                : Math.Max(1, fitCompositeHeight * residualFraction);
            fitHeight = preserveFitOnlyHalfHeight
                ? Math.Max(60, fitCompositeHeight - residualHeight - gap)
                : Math.Max(1, fitCompositeHeight - residualHeight - gap);
        }

        return new PublicationFigureLayout
        {
            PageLeft = pageLeft,
            PageTop = pageTop,
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            LeftMargin = leftMargin,
            RightMargin = rightMargin,
            TopMargin = topMargin,
            BottomMargin = bottomMargin,
            ThermogramRect = hasThermogram
                ? new SKRect(plotLeft, plotTop, plotLeft + plotWidth, plotTop + thermogramHeight)
                : SKRect.Empty,
            FitRect = new SKRect(plotLeft, fitCompositeTop, plotLeft + plotWidth, fitCompositeTop + fitHeight),
            ResidualRect = hasResidual
                ? new SKRect(plotLeft, fitCompositeTop + fitHeight + gap, plotLeft + plotWidth, fitCompositeTop + fitHeight + gap + residualHeight)
                : SKRect.Empty
        };
    }

    public static float RequiredLeftMargin(PublicationFigureDocument document)
        => RequiredLeftMargin(document, true, document.Options.ShowAxisTitles, (float)document.Options.FontSize);

    public static float RequiredLeftMargin(PublicationFigureDocument document, bool showTickLabels, bool showAxisTitle, float fontSize)
    {
        return document.Panels
            .Select(panel => EstimateVerticalAxisMargin(panel.YAxis, showTickLabels, showAxisTitle && document.Options.ShowAxisTitles, fontSize))
            .DefaultIfEmpty(SkiaFigureRenderer.PageInset)
            .Max();
    }

    public static float RequiredRightMargin(PublicationFigureDocument document) => SkiaFigureRenderer.PageInset;
    public static float RequiredTopMargin(PublicationFigureDocument document)
        => RequiredTopMargin(document, true, document.Options.ShowAxisTitles, (float)document.Options.FontSize);

    public static float RequiredTopMargin(PublicationFigureDocument document, bool showTickLabels, bool showAxisTitle, float fontSize)
    {
        return document.ThermogramPanel != null
            ? EstimateHorizontalAxisMargin(document.ThermogramPanel.XAxis, showTickLabels, showAxisTitle && document.Options.ShowAxisTitles, fontSize)
            : SkiaFigureRenderer.PageInset;
    }

    public static float RequiredBottomMargin(PublicationFigureDocument document)
        => RequiredBottomMargin(document, true, document.Options.ShowAxisTitles, (float)document.Options.FontSize);

    public static float RequiredBottomMargin(PublicationFigureDocument document, bool showTickLabels, bool showAxisTitle, float fontSize)
    {
        var axis = document.ResidualPanel?.XAxis ?? document.FitPanel?.XAxis;
        return axis != null
            ? EstimateHorizontalAxisMargin(axis, showTickLabels, showAxisTitle && document.Options.ShowAxisTitles, fontSize)
            : SkiaFigureRenderer.PageInset;
    }

    static float EstimateHorizontalAxisMargin(PublicationAxis axis, bool showTickLabels, bool showAxisTitle, float fontSize)
    {
        var tickMargin = showTickLabels
            ? SkiaFigureRenderer.AxisLabelVerticalOffset + SkiaFigureRenderer.MaxTickLabelHeight(axis, fontSize)
            : 0;
        var titleMargin = showAxisTitle && !string.IsNullOrWhiteSpace(axis.Title)
            ? SkiaFigureRenderer.HorizontalAxisTitleOffset + SkiaDrawingContext.MeasureTextValue(axis.Title, fontSize + 1).Height
            : 0;
        return Math.Max(SkiaFigureRenderer.PageInset, tickMargin + titleMargin);
    }

    static float EstimateVerticalAxisMargin(PublicationAxis axis, bool showTickLabels, bool showAxisTitle, float fontSize)
    {
        var tickMargin = showTickLabels
            ? SkiaFigureRenderer.AxisLabelHorizontalOffset * fontSize / 12f + SkiaFigureRenderer.MaxTickLabelWidth(axis, fontSize)
            : 0;
        var titleMargin = showAxisTitle && !string.IsNullOrWhiteSpace(axis.Title)
            ? SkiaFigureRenderer.PageInset + SkiaFigureRenderer.VerticalAxisTitleOffset(fontSize) + SkiaDrawingContext.MeasureTextValue(axis.Title, fontSize + 1).Height
            : 0;
        return Math.Max(SkiaFigureRenderer.PageInset, tickMargin + titleMargin);
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

    public void DrawPolyline(IReadOnlyList<SKPoint> points, SKColor color, float width, bool smooth, bool dashed = false)
    {
        if (points.Count < 2) return;

        using var path = smooth ? SmoothedPath(points) : LinearPath(points);
        using var paint = StrokePaint(color, width);
        if (dashed) paint.PathEffect = SKPathEffect.CreateDash(new[] { 2 * width, 2 * width }, 0);
        canvas.DrawPath(path, paint);
    }

    public void DrawTextCenteredBaseline(string text, SKPoint baseline, float size, SKColor color)
    {
        using var paint = TextPaint(color);
        using var font = TextFont(size, bold: false, italic: false);
        var width = font.MeasureText(text ?? "", paint);
        canvas.DrawText(text ?? "", baseline.X - width / 2, baseline.Y, SKTextAlign.Left, font, paint);
    }

    public void DrawTextRightBaseline(string text, SKPoint baseline, float size, SKColor color)
    {
        using var paint = TextPaint(color);
        using var font = TextFont(size, bold: false, italic: false);
        var width = font.MeasureText(text ?? "", paint);
        canvas.DrawText(text ?? "", baseline.X - width, baseline.Y, SKTextAlign.Left, font, paint);
    }

    public void DrawTextCenteredTop(string text, SKPoint topCenter, float size, SKColor color)
    {
        using var paint = TextPaint(color);
        using var font = TextFont(size, bold: false, italic: false);
        var value = text ?? "";
        var width = font.MeasureText(value, paint);
        var metrics = font.Metrics;
        var baseline = topCenter.Y - metrics.Ascent;
        canvas.DrawText(value, topCenter.X - width / 2, baseline, SKTextAlign.Left, font, paint);
    }

    public void DrawTextCenteredBottom(string text, SKPoint bottomCenter, float size, SKColor color)
    {
        using var paint = TextPaint(color);
        using var font = TextFont(size, bold: false, italic: false);
        var value = text ?? "";
        var width = font.MeasureText(value, paint);
        var metrics = font.Metrics;
        var baseline = bottomCenter.Y - metrics.Descent;
        canvas.DrawText(value, bottomCenter.X - width / 2, baseline, SKTextAlign.Left, font, paint);
    }

    public void DrawTextRightMiddle(string text, SKPoint rightCenter, float size, SKColor color)
    {
        using var paint = TextPaint(color);
        using var font = TextFont(size, bold: false, italic: false);
        var value = text ?? "";
        var width = font.MeasureText(value, paint);
        var metrics = font.Metrics;
        var baseline = rightCenter.Y - (metrics.Ascent + metrics.Descent) * 0.5f;
        canvas.DrawText(value, rightCenter.X - width, baseline, SKTextAlign.Left, font, paint);
    }

    public void DrawTextRotated(string text, SKPoint point, float size, SKColor color, float degrees)
    {
        using var paint = TextPaint(color);
        using var font = TextFont(size, bold: false, italic: false);
        var width = font.MeasureText(text ?? "", paint);
        var metrics = font.Metrics;
        var baseline = -(metrics.Ascent + metrics.Descent) * 0.5f;

        canvas.Save();
        canvas.Translate(point);
        canvas.RotateDegrees(degrees);
        canvas.DrawText(text ?? "", -width / 2, baseline, SKTextAlign.Left, font, paint);
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
        return MeasureTextValue(text, size, bold, italic);
    }

    public static SKSize MeasureTextValue(string text, float size, bool bold = false, bool italic = false)
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
        return MeasureRichTextValue(text, size);
    }

    public static SKSize MeasureRichTextValue(string text, float size)
    {
        var width = 0f;
        var height = size * 1.25f;

        foreach (var part in RichTextParts(text, size))
        {
            width += MeasureTextValue(part.Text, part.Size, part.Bold, part.Italic).Width;
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
        var font = new SKFont(TextTypeface, size)
        {
            Embolden = bold,
            SkewX = italic ? -0.25f : 0
        };

        return font;
    }

    static readonly SKTypeface TextTypeface = CreateTextTypeface();

    static SKTypeface CreateTextTypeface()
    {
        return SKTypeface.FromFamilyName("Helvetica Neue", SKFontStyleWeight.Light, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.FromFamilyName("Helvetica Neue")
            ?? SKTypeface.Default;
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
