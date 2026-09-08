using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using SkiaSharp;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Drawing;

public sealed class SkiaAnalysisReportRenderer
{
    static readonly SKColor Ink = new SKColor(23, 53, 64);
    static readonly SKColor Muted = new SKColor(89, 112, 120);
    static readonly SKColor Rule = new SKColor(213, 222, 218);
    static readonly SKColor TableHeader = new SKColor(228, 241, 234);
    static readonly SKColor WarningFill = new SKColor(255, 248, 231);
    static readonly SKColor ErrorFill = new SKColor(255, 240, 239);
    static readonly SKColor InformationFill = new SKColor(228, 241, 234);
    static readonly SKColor PlotBlue = new SKColor(23, 53, 64);
    static readonly SKColor PlotBand = new SKColor(80, 130, 170, 55);
    static readonly SKColor Mint = new SKColor(121, 205, 177);
    static readonly SKColor Coral = new SKColor(241, 125, 117);
    static readonly SKColor Sun = new SKColor(245, 189, 120);

    readonly SkiaFigureRenderer figureRenderer = new SkiaFigureRenderer();
    readonly SkiaFigureCanvasRenderer canvasRenderer = new SkiaFigureCanvasRenderer();
    readonly SkiaPublicationFontSet fonts;
    readonly SkiaTextMeasurer measurer;

    public SkiaAnalysisReportRenderer()
    {
        fonts = figureRenderer.ResolveFontSet(new PublicationFigureOptions());
        measurer = new SkiaTextMeasurer(fonts);
    }

    public AnalysisReportLayoutPlan CreatePlan(AnalysisReportDocument document) =>
        AnalysisReportLayoutEngine.Paginate(document, measurer);

    public void WritePdf(AnalysisReportDocument document, string path)
    {
        WritePdf(document, CreatePlan(document), path);
    }

    public void WritePdf(AnalysisReportDocument document, AnalysisReportLayoutPlan plan, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A destination path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("The destination directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                WritePdf(document, plan, stream);
            if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
            else File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void WritePdf(AnalysisReportDocument document, Stream stream)
    {
        WritePdf(document, CreatePlan(document), stream);
    }

    public void WritePdf(AnalysisReportDocument document, AnalysisReportLayoutPlan plan, Stream stream)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        var metadata = new SKDocumentPdfMetadata
        {
            Title = document.Title,
            Author = document.Creator,
            Creator = document.Creator + " " + document.ApplicationVersion,
            Subject = "ITC analysis report",
            Keywords = "ITC, analysis, report, thermogram, fit"
        };
        using var pdf = SKDocument.CreatePdf(stream, metadata);
        foreach (var page in plan.Pages)
        {
            var canvas = pdf.BeginPage((float)page.Width, (float)page.Height);
            DrawPage(canvas, document, plan, page);
            pdf.EndPage();
        }
        pdf.Close();
    }

    public SKBitmap RenderPageBitmap(
        AnalysisReportDocument document,
        AnalysisReportLayoutPlan plan,
        int pageIndex,
        int pixelWidth)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (pageIndex < 0 || pageIndex >= plan.Pages.Count) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        var page = plan.Pages[pageIndex];
        var width = Math.Max(320, pixelWidth);
        var height = (int)Math.Round(width * page.Height / page.Width);
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(width / (float)page.Width, height / (float)page.Height);
        DrawPage(canvas, document, plan, page);
        canvas.Flush();
        return bitmap;
    }

    void DrawPage(SKCanvas canvas, AnalysisReportDocument document,
        AnalysisReportLayoutPlan plan, AnalysisReportPagePlan page)
    {
        canvas.Clear(SKColors.White);
        DrawHeader(canvas, document, (float)plan.MarginLeft,
            (float)(page.Width - plan.MarginRight), page.IsCover, page.ResultName);
        foreach (var fragment in page.Fragments)
            DrawFragment(canvas, document, fragment, page.IsCover);
        DrawFooter(canvas, document, plan, page);
    }

    void DrawFragment(SKCanvas canvas, AnalysisReportDocument document,
        AnalysisReportLayoutFragment fragment, bool isCover)
    {
        var rect = Rect(fragment.Bounds);
        switch (fragment.Kind)
        {
            case AnalysisReportFragmentKind.SectionTitle:
                DrawLines(canvas, fragment.Lines, rect, 17, Ink, true);
                if (isCover) DrawStatusBadge(canvas, document.ResultHealth, document.StatusBadgeText, rect);
                else if (fragment.Section?.StatusBadgeHealth is AnalysisResultHealth sectionHealth)
                    DrawStatusBadge(canvas, sectionHealth, StatusBadgeText(sectionHealth), rect);
                break;
            case AnalysisReportFragmentKind.Heading:
                var heading = (AnalysisReportHeadingBlock)fragment.Block;
                DrawLines(canvas, fragment.Lines, rect,
                    heading.Level == 1 ? 17 : heading.Level == 3 ? 10.5f : 12, Ink, true);
                break;
            case AnalysisReportFragmentKind.Text:
                DrawTextBlock(canvas, (AnalysisReportTextBlock)fragment.Block, fragment, rect);
                break;
            case AnalysisReportFragmentKind.Notice:
                DrawNotice(canvas, (AnalysisReportNoticeBlock)fragment.Block, fragment, rect);
                break;
            case AnalysisReportFragmentKind.KeyValueRows:
                DrawKeyValues(canvas, (AnalysisReportKeyValueBlock)fragment.Block, fragment, rect);
                break;
            case AnalysisReportFragmentKind.TableRows:
                DrawTable(canvas, (AnalysisReportTableBlock)fragment.Block, fragment, rect);
                break;
            case AnalysisReportFragmentKind.PublicationFigure:
                DrawFigure(canvas, (AnalysisReportFigureBlock)fragment.Block, rect);
                break;
            case AnalysisReportFragmentKind.PublicationFigurePair:
                DrawFigurePair(canvas, (AnalysisReportFigurePairBlock)fragment.Block, rect);
                break;
            case AnalysisReportFragmentKind.FigureCanvas:
                DrawFigureCanvas(canvas, (AnalysisReportFigureCanvasBlock)fragment.Block, rect);
                break;
            case AnalysisReportFragmentKind.CartesianPlot:
                DrawPlot(canvas, (AnalysisReportPlotBlock)fragment.Block, rect);
                break;
            case AnalysisReportFragmentKind.ThermodynamicSummary:
                DrawThermodynamicSummary(canvas, (AnalysisReportThermodynamicSummaryBlock)fragment.Block, rect);
                break;
            case AnalysisReportFragmentKind.CorrelationMatrix:
                DrawCorrelation(canvas, (AnalysisReportCorrelationMatrixBlock)fragment.Block, rect);
                break;
            case AnalysisReportFragmentKind.TableOfContents:
                DrawTableOfContents(canvas, (AnalysisReportTableOfContentsBlock)fragment.Block, fragment, rect);
                break;
        }
    }

    void DrawTableOfContents(SKCanvas canvas, AnalysisReportTableOfContentsBlock block,
        AnalysisReportLayoutFragment fragment, SKRect rect)
    {
        var y = rect.Top;
        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            DrawText(canvas, block.Title, rect.Left, y, 12, Ink, true);
            y += 18;
        }
        foreach (var entry in block.Entries.Skip(fragment.FirstItem).Take(fragment.ItemCount))
        {
            var page = entry.PageNumber > 0 ? entry.PageNumber.ToString(CultureInfo.CurrentCulture) : "–";
            var pageWidth = Measure(page, 9, false).Width;
            var titleWidth = Math.Max(40, rect.Width - 42);
            var lines = Wrap(entry.Title, titleWidth, 9, false);
            var rowHeight = Math.Max(1, lines.Count) * 12 + 5;
            DrawLines(canvas, lines, new SKRect(rect.Left, y, rect.Left + titleWidth, y + rowHeight), 9, Ink);
            DrawText(canvas, page, rect.Right - pageWidth, y, 9, Ink);
            var leaderStart = rect.Left + Math.Min(titleWidth - 4, Measure(lines.LastOrDefault() ?? "", 9, false).Width + 7);
            var leaderEnd = rect.Right - pageWidth - 7;
            if (leaderEnd > leaderStart)
            {
                using var paint = new SKPaint { Color = Rule, StrokeWidth = .7f, PathEffect = SKPathEffect.CreateDash(new[] { 1f, 2f }, 0) };
                canvas.DrawLine(leaderStart, y + 8, leaderEnd, y + 8, paint);
            }
            y += rowHeight;
        }
    }

    void DrawTextBlock(SKCanvas canvas, AnalysisReportTextBlock block,
        AnalysisReportLayoutFragment fragment, SKRect rect)
    {
        var y = rect.Top;
        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            DrawText(canvas, block.Title, rect.Left, y, 12, Ink, true);
            y += 18;
        }
        var bodyRect = new SKRect(rect.Left, y, rect.Right, rect.Bottom);
        if (block.InlineMarkdown) DrawInlineMarkdownLines(canvas, fragment.Lines, bodyRect, 9, Ink);
        else DrawLines(canvas, fragment.Lines, bodyRect, 9, Ink);
    }

    void DrawNotice(SKCanvas canvas, AnalysisReportNoticeBlock block,
        AnalysisReportLayoutFragment fragment, SKRect rect)
    {
        var fill = block.Level == AnalysisReportNoticeLevel.Warning ? WarningFill
            : block.Level == AnalysisReportNoticeLevel.Error ? ErrorFill : InformationFill;
        Fill(canvas, rect, fill);
        Stroke(canvas, rect, Rule, .7f);
        var inset = SKRect.Inflate(rect, -6, -5);
        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            DrawText(canvas, block.Title, inset.Left, inset.Top, 10, Ink, true);
            inset.Top += 15;
        }
        DrawLines(canvas, fragment.Lines, inset, 9, Ink);
    }

    void DrawKeyValues(SKCanvas canvas, AnalysisReportKeyValueBlock block,
        AnalysisReportLayoutFragment fragment, SKRect rect)
    {
        var y = rect.Top;
        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            DrawText(canvas, block.Title, rect.Left, y, 12, Ink, true);
            y += 18;
        }
        var labelWidth = rect.Width * .30f;
        var valueWidth = rect.Width - labelWidth;
        foreach (var item in block.Items.Skip(fragment.FirstItem).Take(fragment.ItemCount))
        {
            var indent = item.IndentLevel * 14;
            var labels = Wrap(item.Label, labelWidth - 6 - indent, 9, false);
            var values = Wrap(item.Value, valueWidth - 6, 9, false);
            var height = Math.Max(labels.Count, values.Count) * 12 + 6;
            DrawLines(canvas, labels, new SKRect(rect.Left + 3 + indent, y + 3, rect.Left + labelWidth - 3, y + height), 9, Muted, true);
            DrawLines(canvas, values, new SKRect(rect.Left + labelWidth + 3, y + 3, rect.Right - 3, y + height), 9, Ink);
            Line(canvas, rect.Left, y + height, rect.Right, y + height, Rule, .45f);
            y += height;
        }
    }

    void DrawTable(SKCanvas canvas, AnalysisReportTableBlock table,
        AnalysisReportLayoutFragment fragment, SKRect rect)
    {
        var scale = (float)fragment.Scale;
        var fontSize = (float)table.FontSize * scale;
        var lineHeight = fontSize * 4 / 3;
        var horizontalPadding = 3f * scale;
        var verticalPadding = (float)table.VerticalCellPadding * scale;
        var y = rect.Top;
        if (!string.IsNullOrWhiteSpace(table.Title))
        {
            DrawText(canvas, table.Title, rect.Left, y, 12 * scale, Ink, true);
            y += 18 * scale;
        }
        var columns = Math.Max(1, table.Columns.Count);
        var weight = table.Columns.Sum(column => column.WidthWeight);
        var widths = table.Columns.Select(column => rect.Width * (float)(column.WidthWeight / weight)).ToArray();
        var offsets = new float[columns];
        for (var column = 1; column < columns; column++) offsets[column] = offsets[column - 1] + widths[column - 1];
        var headerLines = table.Columns.Select((column, index) => Wrap(column.Title, widths[index] - 2 * horizontalPadding, fontSize, true)).ToList();
        var headerHeight = Math.Max(1, headerLines.Select(lines => lines.Count).DefaultIfEmpty(1).Max()) * lineHeight + 2 * verticalPadding;
        Fill(canvas, new SKRect(rect.Left, y, rect.Right, y + headerHeight), TableHeader);
        for (var column = 0; column < table.Columns.Count; column++)
            DrawLines(canvas, headerLines[column], new SKRect(rect.Left + offsets[column] + horizontalPadding, y + verticalPadding,
                rect.Left + offsets[column] + widths[column] - horizontalPadding, y + headerHeight), fontSize, Ink, true);
        y += headerHeight;

        foreach (var row in table.Rows.Skip(fragment.FirstItem).Take(fragment.ItemCount))
        {
            var wrapped = Enumerable.Range(0, table.Columns.Count).Select(column =>
                Wrap(column < row.Cells.Count ? row.Cells[column] : "", widths[column] - 2 * horizontalPadding, fontSize, false)).ToList();
            var height = Math.Max(1, wrapped.Select(lines => lines.Count).DefaultIfEmpty(1).Max()) * lineHeight + 2 * verticalPadding;
            for (var column = 0; column < table.Columns.Count; column++)
                DrawLines(canvas, wrapped[column], new SKRect(rect.Left + offsets[column] + horizontalPadding, y + verticalPadding,
                    rect.Left + offsets[column] + widths[column] - horizontalPadding, y + height), fontSize, Ink);
            Line(canvas, rect.Left, y + height, rect.Right, y + height, Rule, .45f);
            y += height;
        }
        Stroke(canvas, new SKRect(rect.Left, rect.Top + (string.IsNullOrWhiteSpace(table.Title) ? 0 : 18 * scale), rect.Right, Math.Min(rect.Bottom, y)), Rule, .55f);
        for (var column = 1; column < table.Columns.Count; column++)
            Line(canvas, rect.Left + offsets[column], rect.Top + (string.IsNullOrWhiteSpace(table.Title) ? 0 : 18 * scale),
                rect.Left + offsets[column], Math.Min(rect.Bottom, y), Rule, .4f);
    }

    void DrawFigure(SKCanvas canvas, AnalysisReportFigureBlock block, SKRect rect)
    {
        var top = rect.Top;
        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            DrawText(canvas, block.Title, rect.Left, top, 12, Ink, true);
            top += 18;
        }
        if (!string.IsNullOrWhiteSpace(block.PanelLabel))
            DrawText(canvas, block.PanelLabel, rect.Left, top, 11, Ink, true);
        DrawFigureDocument(canvas, block.Figure, new SKRect(rect.Left, top, rect.Right, rect.Bottom));
    }

    void DrawFigurePair(SKCanvas canvas, AnalysisReportFigurePairBlock block, SKRect rect)
    {
        var top = rect.Top;
        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            DrawText(canvas, block.Title, rect.Left, top, 12, Ink, true);
            top += 18;
        }
        const float gap = 12;
        var leftFraction = block.LeftFigure?.Options?.FocusThermogramOnBaseline == true ? .57f : .5f;
        var leftWidth = (rect.Width - gap) * leftFraction;
        var rightWidth = rect.Width - gap - leftWidth;
        DrawText(canvas, block.LeftTitle, rect.Left, top, 9, Ink, true);
        DrawText(canvas, block.RightTitle, rect.Left + leftWidth + gap, top, 9, Ink, true);
        top += 14;
        DrawFigureDocument(canvas, block.LeftFigure, new SKRect(rect.Left, top, rect.Left + leftWidth, rect.Bottom), alignTop: true);
        DrawFigureDocument(canvas, block.RightFigure, new SKRect(rect.Left + leftWidth + gap, top, rect.Left + leftWidth + gap + rightWidth, rect.Bottom), alignTop: true);
    }

    void DrawFigureCanvas(SKCanvas canvas, AnalysisReportFigureCanvasBlock block, SKRect rect)
    {
        var top = rect.Top;
        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            DrawText(canvas, block.Title, rect.Left, top, 12, Ink, true);
            top += 18;
        }
        var plan = canvasRenderer.CreatePlan(block.Canvas);
        canvasRenderer.DrawInRect(canvas, plan, new SKRect(rect.Left, top, rect.Right, rect.Bottom));
    }

    void DrawFigureDocument(SKCanvas canvas, PublicationFigureDocument? figure, SKRect target, bool alignTop = false)
    {
        if (figure == null || target.Width <= 1 || target.Height <= 1) return;
        var figureFonts = figureRenderer.ResolveFontSet(figure);
        var layout = PublicationFigureLayout.Create(figure, figureFonts);
        var scale = Math.Min(target.Width / layout.PageWidth, target.Height / layout.PageHeight);
        scale = Math.Min(1, scale);
        var x = target.Left + (target.Width - layout.PageWidth * scale) / 2;
        var y = alignTop ? target.Top : target.Top + (target.Height - layout.PageHeight * scale) / 2;
        canvas.Save();
        canvas.Translate(x, y);
        canvas.Scale(scale);
        figureRenderer.DrawDocument(canvas, figure, layout, PublicationFigureRenderSettings.Default, figureFonts);
        canvas.Restore();
    }

    void DrawPlot(SKCanvas canvas, AnalysisReportPlotBlock plot, SKRect rect)
    {
        var top = rect.Top;
        DrawText(canvas, plot.Title, rect.Left, top, 12, Ink, true);
        top += 20;
        var legendEntries = plot.Series
            .Where(series => series.Kind == AnalysisReportPlotSeriesKind.Points)
            .GroupBy(series => string.IsNullOrWhiteSpace(series.Group) ? series.Label : series.Group)
            .Select(group => group.First()).ToList();
        if (legendEntries.Count > 1) top += 14;
        var graph = new SKRect(rect.Left + 48, top + 8, rect.Right - 12, rect.Bottom - 30);
        var points = plot.Series.SelectMany(series => series.Points)
            .Where(point => Finite(point.X) && Finite(point.Y)).ToList();
        if (points.Count == 0) return;
        var minX = points.Min(point => point.X); var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Lower ?? point.Y); var maxY = points.Max(point => point.Upper ?? point.Y);
        Expand(ref minX, ref maxX); Expand(ref minY, ref maxY);
        Stroke(canvas, graph, Ink, .8f);
        DrawText(canvas, plot.XAxisTitle, graph.MidX - Measure(plot.XAxisTitle, 8, false).Width / 2, graph.Bottom + 10, 8, Ink);
        DrawText(canvas, plot.YAxisTitle, rect.Left, graph.Top - 1, 8, Ink);
        for (var tick = 0; tick <= 4; tick++)
        {
            var value = minY + (maxY - minY) * tick / 4.0;
            var y = MapY(value);
            Line(canvas, graph.Left, y, graph.Right, y, Rule, .4f);
            var text = value.ToString("G3", CultureInfo.CurrentCulture);
            DrawText(canvas, text, graph.Left - Measure(text, 6, false).Width - 5, y - 4, 6, Muted);
        }
        DrawText(canvas, minX.ToString("G4", CultureInfo.CurrentCulture), graph.Left, graph.Bottom + 1, 6.5f, Muted);
        var maxXText = maxX.ToString("G4", CultureInfo.CurrentCulture);
        DrawText(canvas, maxXText, graph.Right - Measure(maxXText, 6.5f, false).Width, graph.Bottom + 1, 6.5f, Muted);

        var groupColors = new Dictionary<string, int>();
        var nextColor = 0;
        for (var seriesIndex = 0; seriesIndex < plot.Series.Count; seriesIndex++)
        {
            var series = plot.Series[seriesIndex];
            var group = string.IsNullOrWhiteSpace(series.Group) ? series.Label : series.Group;
            if (!groupColors.TryGetValue(group, out var colorIndex))
                groupColors[group] = colorIndex = nextColor++;
            var color = SeriesColor(colorIndex);
            var seriesPoints = series.Points.Where(point => Finite(point.X) && Finite(point.Y)).OrderBy(point => point.X).ToList();
            if (seriesPoints.Count == 0) continue;
            if (series.Kind == AnalysisReportPlotSeriesKind.Line
                && seriesPoints.Any(point => point.Lower.HasValue && point.Upper.HasValue))
            {
                using var band = new SKPath();
                var bandPoints = seriesPoints.Where(point => point.Lower.HasValue && point.Upper.HasValue).ToList();
                if (bandPoints.Count > 1)
                {
                    band.MoveTo(MapX(bandPoints[0].X), MapY(bandPoints[0].Upper.GetValueOrDefault()));
                    foreach (var point in bandPoints.Skip(1)) band.LineTo(MapX(point.X), MapY(point.Upper.GetValueOrDefault()));
                    foreach (var point in bandPoints.AsEnumerable().Reverse()) band.LineTo(MapX(point.X), MapY(point.Lower.GetValueOrDefault()));
                    band.Close();
                    using var paint = new SKPaint { Color = color.WithAlpha(42), Style = SKPaintStyle.Fill, IsAntialias = true };
                    canvas.DrawPath(band, paint);
                }
            }
            if (series.Kind == AnalysisReportPlotSeriesKind.Line && seriesPoints.Count > 1)
            {
                using var path = new SKPath();
                path.MoveTo(MapX(seriesPoints[0].X), MapY(seriesPoints[0].Y));
                foreach (var point in seriesPoints.Skip(1)) path.LineTo(MapX(point.X), MapY(point.Y));
                using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, IsAntialias = true };
                canvas.DrawPath(path, paint);
            }
            else
            {
                foreach (var point in seriesPoints)
                {
                    var x = MapX(point.X); var y = MapY(point.Y);
                    if (plot.UncertaintyStyle == AnalysisITC.Core.Application.UncertaintyDisplayStyle.ConfidenceInterval
                        || plot.UncertaintyStyle == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval)
                        DrawWhisker(x, point.ConfidenceLower, point.ConfidenceUpper, color, .7f, 3.5f);
                    if (plot.UncertaintyStyle == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviation
                        || plot.UncertaintyStyle == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval
                        || plot.UncertaintyStyle == AnalysisITC.Core.Application.UncertaintyDisplayStyle.Automatic)
                        DrawWhisker(x, point.StandardDeviationLower ?? point.Lower,
                            point.StandardDeviationUpper ?? point.Upper, color, 1.2f, 2.2f);
                    DrawMarker(x, y, color, colorIndex);
                }
            }
        }

        if (legendEntries.Count > 1)
        {
            var x = graph.Left;
            var y = rect.Top + 22;
            foreach (var entry in legendEntries)
            {
                var key = string.IsNullOrWhiteSpace(entry.Group) ? entry.Label : entry.Group;
                var index = groupColors.TryGetValue(key, out var value) ? value : 0;
                DrawMarker(x + 3, y + 4, SeriesColor(index), index);
                DrawText(canvas, entry.Label, x + 10, y, 7, Ink);
                x += Measure(entry.Label, 7, false).Width + 24;
            }
        }

        float MapX(double value) => graph.Left + (float)((value - minX) / (maxX - minX)) * graph.Width;
        float MapY(double value) => graph.Bottom - (float)((value - minY) / (maxY - minY)) * graph.Height;
        void DrawWhisker(float x, double? low, double? high, SKColor color, float width, float cap)
        {
            if (!low.HasValue || !high.HasValue || !Finite(low.Value) || !Finite(high.Value)) return;
            var lowY = MapY(low.Value); var highY = MapY(high.Value);
            Line(canvas, x, lowY, x, highY, color, width);
            Line(canvas, x - cap, lowY, x + cap, lowY, color, width);
            Line(canvas, x - cap, highY, x + cap, highY, color, width);
        }
        void DrawMarker(float x, float y, SKColor color, int index)
        {
            using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
            if (index % 3 == 1) canvas.DrawRect(x - 2.4f, y - 2.4f, 4.8f, 4.8f, paint);
            else if (index % 3 == 2)
            {
                using var path = new SKPath();
                path.MoveTo(x, y - 3); path.LineTo(x + 3, y + 2.5f); path.LineTo(x - 3, y + 2.5f); path.Close();
                canvas.DrawPath(path, paint);
            }
            else canvas.DrawCircle(x, y, 2.6f, paint);
        }
    }

    void DrawThermodynamicSummary(SKCanvas canvas, AnalysisReportThermodynamicSummaryBlock block, SKRect rect)
    {
        DrawText(canvas, block.Title, rect.Left, rect.Top, 12, Ink, true);
        var desiredWidth = Math.Min(rect.Width, Math.Max(280, 92 + block.Categories.Count * Math.Max(42, 13 * block.Series.Count)));
        var left = rect.Left + (rect.Width - desiredWidth) * .5f;
        var hasUncertaintyNote = !string.IsNullOrWhiteSpace(block.UncertaintyNote);
        var graph = new SKRect(left + 45, rect.Top + 26, left + desiredWidth - 8,
            rect.Bottom - (hasUncertaintyNote ? 54 : 38));
        var all = block.Series.SelectMany(series => series.Bars).ToList();
        if (all.Count == 0) return;
        var values = all.SelectMany(DisplayedValues).Where(Finite).ToList();
        var minimum = Math.Min(0, values.Min()); var maximum = Math.Max(0, values.Max()); Expand(ref minimum, ref maximum);
        Stroke(canvas, graph, Ink, .8f);
        DrawText(canvas, block.YAxisTitle, left, graph.Top - 1, 7.5f, Ink);
        for (var tick = 0; tick <= 4; tick++)
        {
            var value = minimum + (maximum - minimum) * tick / 4.0;
            var y = MapY(value);
            Line(canvas, graph.Left, y, graph.Right, y, Rule, .45f);
            var text = value.ToString("G3", CultureInfo.CurrentCulture);
            DrawText(canvas, text, graph.Left - Measure(text, 6, false).Width - 5, y - 4, 6, Muted);
        }
        var zero = MapY(0);
        Line(canvas, graph.Left, zero, graph.Right, zero, Rule, .7f);
        var categoryWidth = graph.Width / Math.Max(1, block.Categories.Count);
        var binWidth = categoryWidth * .76f;
        var barWidth = Math.Max(3, binWidth / Math.Max(1, block.Series.Count) - 2);
        for (var category = 0; category < block.Categories.Count; category++)
        {
            var label = block.Categories[category];
            var center = graph.Left + categoryWidth * (category + .5f);
            DrawText(canvas, label, center - Measure(label, 6.5f, false).Width * .5f, graph.Bottom + 3, 6.5f, Ink);
            for (var seriesIndex = 0; seriesIndex < block.Series.Count; seriesIndex++)
            {
                var bar = block.Series[seriesIndex].Bars.FirstOrDefault(item => item.Category == label);
                if (bar == null || !Finite(bar.Value)) continue;
                var x = center - binWidth * .5f + (seriesIndex + .5f) * binWidth / block.Series.Count;
                var y = MapY(bar.Value);
                using var paint = new SKPaint { Color = SeriesColor(seriesIndex), Style = SKPaintStyle.Fill, IsAntialias = true };
                canvas.DrawRect(new SKRect(x - barWidth * .5f, Math.Min(y, zero), x + barWidth * .5f, Math.Max(y, zero)), paint);
                Stroke(canvas, new SKRect(x - barWidth * .5f, Math.Min(y, zero), x + barWidth * .5f, Math.Max(y, zero)), Ink, .45f);
                DrawBarUncertainty(x, bar);
            }
        }
        var legendX = left + 45; var legendY = rect.Bottom - (hasUncertaintyNote ? 30 : 14);
        for (var index = 0; index < block.Series.Count; index++)
        {
            var label = block.Series[index].Label;
            Fill(canvas, new SKRect(legendX, legendY, legendX + 8, legendY + 8), SeriesColor(index));
            DrawText(canvas, label, legendX + 12, legendY - 1, 6.5f, Ink);
            legendX += 20 + Measure(label, 6.5f, false).Width;
        }
        if (hasUncertaintyNote)
            DrawText(canvas, block.UncertaintyNote, rect.Left, rect.Bottom - 10, 6.25f, Muted);

        float MapY(double value) => graph.Bottom - (float)((value - minimum) / (maximum - minimum)) * graph.Height;
        IEnumerable<double> DisplayedValues(AnalysisReportThermodynamicBar bar)
        {
            yield return bar.Value;
            var style = block.UncertaintyStyle;
            if (style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.ConfidenceInterval
                || style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval)
            {
                if (bar.ConfidenceLower.HasValue) yield return bar.ConfidenceLower.Value;
                if (bar.ConfidenceUpper.HasValue) yield return bar.ConfidenceUpper.Value;
            }
            if (style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviation
                || style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval
                || style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.Automatic)
            {
                if (bar.StandardDeviationLower.HasValue) yield return bar.StandardDeviationLower.Value;
                if (bar.StandardDeviationUpper.HasValue) yield return bar.StandardDeviationUpper.Value;
            }
        }
        void DrawBarUncertainty(float x, AnalysisReportThermodynamicBar bar)
        {
            var style = block.UncertaintyStyle;
            if (style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.ConfidenceInterval || style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval)
                DrawWhisker(x, bar.ConfidenceLower, bar.ConfidenceUpper, .7f, 4);
            if (style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviation || style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval || style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.Automatic)
                DrawWhisker(x, bar.StandardDeviationLower, bar.StandardDeviationUpper, 1.5f, 2.5f);
        }
        void DrawWhisker(float x, double? low, double? high, float width, float cap)
        {
            if (!low.HasValue || !high.HasValue || !Finite(low.Value) || !Finite(high.Value)) return;
            var y1 = MapY(low.Value); var y2 = MapY(high.Value);
            Line(canvas, x, y1, x, y2, Ink, width); Line(canvas, x - cap, y1, x + cap, y1, Ink, width); Line(canvas, x - cap, y2, x + cap, y2, Ink, width);
        }
    }

    void DrawCorrelation(SKCanvas canvas, AnalysisReportCorrelationMatrixBlock matrix, SKRect rect)
    {
        DrawText(canvas, matrix.Title, rect.Left, rect.Top, 12, Ink, true);
        if (matrix.Matrix == null) return;
        var count = Math.Min(matrix.Labels.Count, Math.Min(matrix.Matrix.GetLength(0), matrix.Matrix.GetLength(1)));
        if (count == 0) return;
        var desired = (float)(matrix.PreferredSizeCentimeters * 72 / 2.54);
        var labelWidth = Math.Min(120,
            matrix.Labels.Select(label => Measure(label, 6.5f, false).Width).DefaultIfEmpty(52).Max() + 10);
        var cell = Math.Max(8, Math.Min(desired, rect.Height - 68) / count);
        var totalWidth = labelWidth + cell * count;
        var start = rect.Left + Math.Max(0, (rect.Width - totalWidth) * .5f);
        var left = start + labelWidth;
        var top = rect.Top + 36;
        var labelSize = Math.Max(5, Math.Min(6.5f, cell * .22f));
        for (var column = 0; column < count; column++)
        {
            var label = matrix.ColumnLabels[column];
            var measured = Measure(label, labelSize, false);
            DrawText(canvas, label, left + column * cell + (cell - measured.Width) / 2,
                top - measured.Height - 4, labelSize, Ink);
        }
        for (var index = 0; index < count; index++)
        {
            var label = matrix.Labels[index];
            var measuredLabel = Measure(label, labelSize, false);
            DrawText(canvas, label, start, top + index * cell + (cell - measuredLabel.Height) / 2, labelSize, Ink);
            for (var column = 0; column < count; column++)
            {
                var value = matrix.Matrix[index, column];
                var square = new SKRect(left + column * cell, top + index * cell,
                    left + (column + 1) * cell, top + (index + 1) * cell);
                Fill(canvas, square, CorrelationColor(value));
                Stroke(canvas, square, SKColors.White, .4f);
                var text = Finite(value) ? value.ToString("0.00", CultureInfo.InvariantCulture) : "-";
                var size = Math.Max(5, Math.Min(7, cell * .22f));
                var measured = Measure(text, size, false);
                DrawText(canvas, text, square.MidX - measured.Width / 2, square.MidY - measured.Height / 2, size,
                    Math.Abs(value) > .65 ? SKColors.White : Ink);
            }
        }
        var noteY = top + count * cell + 8;
        foreach (var note in matrix.Notes.Take(3))
        {
            DrawText(canvas, note, rect.Left, noteY, 7.5f, Muted);
            noteY += 10;
        }
    }

    void DrawHeader(SKCanvas canvas, AnalysisReportDocument document,
        float left, float right, bool isCover, string resultName)
    {
        var brand = isCover ? "FT-ITC ANALYSIS REPORT" : "FT-ITC Analysis" +
            (string.IsNullOrWhiteSpace(resultName) ? "" : " · " + resultName);
        var brandSize = isCover ? 7.5f : 6.5f;
        DrawText(canvas, brand, left, 14, brandSize, Ink, isCover);
        var exportDate = document.ExportDateText;
        DrawText(canvas, exportDate, right - Measure(exportDate, 6.5f, false).Width,
            14, 6.5f, Muted);
        Line(canvas, left, 31, right, 31, isCover ? Mint : Rule, isCover ? 2 : .45f);
    }

    void DrawStatusBadge(SKCanvas canvas, AnalysisResultHealth health, string text,
        SKRect titleBounds)
    {
        var width = Measure(text, 6.5f, true).Width + 16;
        var x = titleBounds.Right - width;
        var y = titleBounds.Top + 1;
        var fill = health == AnalysisResultHealth.Valid ? InformationFill
            : health == AnalysisResultHealth.Warning ? WarningFill : ErrorFill;
        var stroke = health == AnalysisResultHealth.Valid ? Mint
            : health == AnalysisResultHealth.Warning ? Sun : Coral;
        var rect = new SKRect(x, y, x + width, y + 18);
        using (var fillPaint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true })
            canvas.DrawRoundRect(rect, 2.5f, 2.5f, fillPaint);
        using (var strokePaint = new SKPaint { Color = stroke, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true })
            canvas.DrawRoundRect(rect, 2.5f, 2.5f, strokePaint);
        DrawText(canvas, text, x + 8, y + 5, 6.5f, Ink, true);
    }

    static string StatusBadgeText(AnalysisResultHealth health) => health switch
    {
        AnalysisResultHealth.Valid => "ANALYSIS VALID",
        AnalysisResultHealth.Warning => "REVIEW WARNINGS",
        AnalysisResultHealth.PartialInvalid => "PARTIAL / STALE",
        AnalysisResultHealth.Invalid => "INVALID / STALE",
        _ => "STATUS UNKNOWN",
    };

    void DrawFooter(SKCanvas canvas, AnalysisReportDocument document,
        AnalysisReportLayoutPlan plan, AnalysisReportPagePlan page)
    {
        var y = (float)(page.Height - plan.MarginBottom + 5);
        var left = (float)plan.MarginLeft;
        var right = (float)(page.Width - plan.MarginRight);
        Line(canvas, left, y - 5, right, y - 5, Rule, .45f);
        var provenance = document.Creator + (string.IsNullOrWhiteSpace(document.ApplicationVersion) ? "" : " " + document.ApplicationVersion);
        DrawText(canvas, provenance, left, y, 6.5f, Muted);
        var number = "Page " + page.PageNumber.ToString(CultureInfo.CurrentCulture) + " of " + plan.Pages.Count.ToString(CultureInfo.CurrentCulture);
        DrawText(canvas, number, right - Measure(number, 6.5f, false).Width, y, 6.5f, Muted);
        if (!page.IsCover)
        {
            var titleWidth = Measure(document.Title, 6.5f, false).Width;
            DrawText(canvas, document.Title, (left + right - titleWidth) / 2, y, 6.5f, Muted);
        }
    }

    List<string> Wrap(string text, float width, float size, bool bold)
    {
        var lines = new List<string>();
        foreach (var paragraph in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            if (paragraph.Length == 0) { lines.Add(""); continue; }
            var current = "";
            foreach (var word in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Measure(word, size, bold).Width > width)
                {
                    if (current.Length > 0) { lines.Add(current); current = ""; }
                    var part = "";
                    foreach (var character in word)
                    {
                        var candidatePart = part + character;
                        if (part.Length > 0 && Measure(candidatePart, size, bold).Width > width)
                        {
                            lines.Add(part);
                            part = character.ToString();
                        }
                        else part = candidatePart;
                    }
                    current = part;
                    continue;
                }
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && Measure(candidate, size, bold).Width > width)
                {
                    lines.Add(current);
                    current = word;
                }
                else current = candidate;
            }
            lines.Add(current);
        }
        return lines;
    }

    void DrawLines(SKCanvas canvas, IReadOnlyList<string> lines, SKRect rect,
        float size, SKColor color, bool bold = false)
    {
        var y = rect.Top;
        var advance = size * 1.34f;
        foreach (var line in lines)
        {
            if (y + advance > rect.Bottom + .5f) break;
            DrawText(canvas, line, rect.Left, y, size, color, bold);
            y += advance;
        }
    }

    void DrawInlineMarkdownLines(SKCanvas canvas, IReadOnlyList<string> lines,
        SKRect rect, float size, SKColor color)
    {
        var y = rect.Top;
        var advance = size * 1.34f;
        foreach (var line in lines)
        {
            if (y + advance > rect.Bottom + .5f) break;
            var x = rect.Left; var index = 0; var bold = false; var italic = false;
            while (index < line.Length)
            {
                if (index + 1 < line.Length && line[index] == '*' && line[index + 1] == '*')
                {
                    bold = !bold; index += 2; continue;
                }
                if (line[index] == '*') { italic = !italic; index++; continue; }
                var next = line.IndexOf('*', index);
                if (next < 0) next = line.Length;
                var text = line.Substring(index, next - index);
                DrawText(canvas, text, x, y, size, color, bold, italic);
                x += Measure(text, size, bold, italic).Width;
                index = next;
            }
            y += advance;
        }
    }

    void DrawText(SKCanvas canvas, string text, float x, float y, float size, SKColor color,
        bool bold = false, bool italic = false)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        using var font = fonts.CreateFont(size, bold, italic);
        canvas.DrawText(text ?? "", x, y - font.Metrics.Ascent, SKTextAlign.Left, font, paint);
    }

    SKSize Measure(string text, float size, bool bold, bool italic = false) =>
        SkiaDrawingContext.MeasureTextValue(text, size, fonts, bold, italic);

    static SKRect Rect(AnalysisReportRect rect) => new SKRect(
        (float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);

    static void Fill(SKCanvas canvas, SKRect rect, SKColor color)
    {
        using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRect(rect, paint);
    }

    static void Stroke(SKCanvas canvas, SKRect rect, SKColor color, float width)
    {
        using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };
        canvas.DrawRect(rect, paint);
    }

    static void Line(SKCanvas canvas, float x1, float y1, float x2, float y2, SKColor color, float width)
    {
        using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    static SKColor CorrelationColor(double value)
    {
        if (!Finite(value)) return new SKColor(230, 230, 230);
        var amount = (byte)Math.Round(225 - Math.Min(1, Math.Abs(value)) * 145);
        return value < 0 ? new SKColor(amount, amount, 235) : new SKColor(235, amount, amount);
    }

    static SKColor SeriesColor(int index) => (index % 5) switch
    {
        0 => PlotBlue,
        1 => Mint,
        2 => Coral,
        3 => Sun,
        _ => new SKColor(89, 112, 120),
    };

    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    static void Expand(ref double minimum, ref double maximum)
    {
        if (minimum == maximum)
        {
            var delta = Math.Abs(minimum) > 0 ? Math.Abs(minimum) * .05 : 1;
            minimum -= delta; maximum += delta;
        }
        else
        {
            var padding = (maximum - minimum) * .06;
            minimum -= padding; maximum += padding;
        }
    }

    sealed class SkiaTextMeasurer : IAnalysisReportTextMeasurer
    {
        readonly SkiaPublicationFontSet fonts;

        public SkiaTextMeasurer(SkiaPublicationFontSet fonts) => this.fonts = fonts;

        public AnalysisReportSize Measure(string text, AnalysisReportTextStyle style)
        {
            var size = SkiaDrawingContext.MeasureTextValue(text, (float)style.FontSize,
                fonts, style.Bold, style.Italic);
            return new AnalysisReportSize(size.Width, size.Height);
        }
    }
}
