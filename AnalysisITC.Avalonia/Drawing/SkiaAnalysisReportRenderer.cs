using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using SkiaSharp;

using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Drawing;

public sealed class SkiaAnalysisReportRenderer
{
    static readonly SKColor Ink = new SKColor(30, 34, 38);
    static readonly SKColor Muted = new SKColor(92, 98, 104);
    static readonly SKColor Rule = new SKColor(205, 209, 213);
    static readonly SKColor TableHeader = new SKColor(235, 237, 239);
    static readonly SKColor WarningFill = new SKColor(255, 244, 214);
    static readonly SKColor ErrorFill = new SKColor(255, 228, 228);
    static readonly SKColor InformationFill = new SKColor(232, 241, 248);
    static readonly SKColor PlotBlue = new SKColor(33, 92, 145);
    static readonly SKColor PlotBand = new SKColor(80, 130, 170, 55);

    readonly SkiaFigureRenderer figureRenderer = new SkiaFigureRenderer();
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
        foreach (var fragment in page.Fragments) DrawFragment(canvas, fragment);
        DrawFooter(canvas, document, plan, page);
    }

    void DrawFragment(SKCanvas canvas, AnalysisReportLayoutFragment fragment)
    {
        var rect = Rect(fragment.Bounds);
        switch (fragment.Kind)
        {
            case AnalysisReportFragmentKind.SectionTitle:
                DrawLines(canvas, fragment.Lines, rect, 17, Ink, true);
                break;
            case AnalysisReportFragmentKind.Heading:
                var heading = (AnalysisReportHeadingBlock)fragment.Block;
                DrawLines(canvas, fragment.Lines, rect, heading.Level == 1 ? 17 : 12, Ink, true);
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
            case AnalysisReportFragmentKind.ContactSheet:
                DrawContactSheet(canvas, (AnalysisReportContactSheetBlock)fragment.Block, rect);
                break;
            case AnalysisReportFragmentKind.CartesianPlot:
                DrawPlot(canvas, (AnalysisReportPlotBlock)fragment.Block, rect);
                break;
            case AnalysisReportFragmentKind.CorrelationMatrix:
                DrawCorrelation(canvas, (AnalysisReportCorrelationMatrixBlock)fragment.Block, rect);
                break;
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
        DrawLines(canvas, fragment.Lines, new SKRect(rect.Left, y, rect.Right, rect.Bottom), 9, Ink);
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
            var labels = Wrap(item.Label, labelWidth - 6, 9, false);
            var values = Wrap(item.Value, valueWidth - 6, 9, false);
            var height = Math.Max(labels.Count, values.Count) * 12 + 6;
            DrawLines(canvas, labels, new SKRect(rect.Left + 3, y + 3, rect.Left + labelWidth - 3, y + height), 9, Muted, true);
            DrawLines(canvas, values, new SKRect(rect.Left + labelWidth + 3, y + 3, rect.Right - 3, y + height), 9, Ink);
            Line(canvas, rect.Left, y + height, rect.Right, y + height, Rule, .45f);
            y += height;
        }
    }

    void DrawTable(SKCanvas canvas, AnalysisReportTableBlock table,
        AnalysisReportLayoutFragment fragment, SKRect rect)
    {
        var scale = (float)fragment.Scale;
        var fontSize = 7.5f * scale;
        var lineHeight = 10f * scale;
        var padding = 3f * scale;
        var y = rect.Top;
        if (!string.IsNullOrWhiteSpace(table.Title))
        {
            DrawText(canvas, table.Title, rect.Left, y, 12 * scale, Ink, true);
            y += 18 * scale;
        }
        var columns = Math.Max(1, table.Columns.Count);
        var cellWidth = rect.Width / columns;
        var headerLines = table.Columns.Select(column => Wrap(column.Title, cellWidth - 2 * padding, fontSize, true)).ToList();
        var headerHeight = Math.Max(1, headerLines.Select(lines => lines.Count).DefaultIfEmpty(1).Max()) * lineHeight + 2 * padding;
        Fill(canvas, new SKRect(rect.Left, y, rect.Right, y + headerHeight), TableHeader);
        for (var column = 0; column < table.Columns.Count; column++)
            DrawLines(canvas, headerLines[column], new SKRect(rect.Left + column * cellWidth + padding, y + padding,
                rect.Left + (column + 1) * cellWidth - padding, y + headerHeight), fontSize, Ink, true);
        y += headerHeight;

        foreach (var row in table.Rows.Skip(fragment.FirstItem).Take(fragment.ItemCount))
        {
            var wrapped = Enumerable.Range(0, table.Columns.Count).Select(column =>
                Wrap(column < row.Cells.Count ? row.Cells[column] : "", cellWidth - 2 * padding, fontSize, false)).ToList();
            var height = Math.Max(1, wrapped.Select(lines => lines.Count).DefaultIfEmpty(1).Max()) * lineHeight + 2 * padding;
            for (var column = 0; column < table.Columns.Count; column++)
                DrawLines(canvas, wrapped[column], new SKRect(rect.Left + column * cellWidth + padding, y + padding,
                    rect.Left + (column + 1) * cellWidth - padding, y + height), fontSize, Ink);
            Line(canvas, rect.Left, y + height, rect.Right, y + height, Rule, .45f);
            y += height;
        }
        Stroke(canvas, new SKRect(rect.Left, rect.Top + (string.IsNullOrWhiteSpace(table.Title) ? 0 : 18 * scale), rect.Right, Math.Min(rect.Bottom, y)), Rule, .55f);
        for (var column = 1; column < table.Columns.Count; column++)
            Line(canvas, rect.Left + column * cellWidth, rect.Top + (string.IsNullOrWhiteSpace(table.Title) ? 0 : 18 * scale),
                rect.Left + column * cellWidth, Math.Min(rect.Bottom, y), Rule, .4f);
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

    void DrawContactSheet(SKCanvas canvas, AnalysisReportContactSheetBlock block, SKRect rect)
    {
        var top = rect.Top;
        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            DrawText(canvas, block.Title, rect.Left, top, 12, Ink, true);
            top += 18;
        }
        var rows = Math.Max(1, block.Rows);
        var columns = Math.Max(1, block.Columns);
        const float gap = 5;
        var cellWidth = (rect.Width - gap * (columns - 1)) / columns;
        var cellHeight = (rect.Bottom - top - gap * (rows - 1)) / rows;
        foreach (var cell in block.Cells)
        {
            var x = rect.Left + cell.Column * (cellWidth + gap);
            var y = top + cell.Row * (cellHeight + gap);
            var cellRect = new SKRect(x, y, x + cellWidth, y + cellHeight);
            Stroke(canvas, cellRect, Rule, .5f);
            DrawText(canvas, cell.PanelLabel + ". " + cell.ExperimentName, x + 3, y + 2, 6.5f, Ink, true);
            DrawFigureDocument(canvas, cell.Figure, new SKRect(x + 2, y + 13, x + cellWidth - 2, y + cellHeight - 2));
        }
    }

    void DrawFigureDocument(SKCanvas canvas, PublicationFigureDocument figure, SKRect target)
    {
        if (figure == null || target.Width <= 1 || target.Height <= 1) return;
        var figureFonts = figureRenderer.ResolveFontSet(figure);
        var layout = PublicationFigureLayout.Create(figure, figureFonts);
        var scale = Math.Min(target.Width / layout.PageWidth, target.Height / layout.PageHeight);
        var x = target.Left + (target.Width - layout.PageWidth * scale) / 2;
        var y = target.Top + (target.Height - layout.PageHeight * scale) / 2;
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
        DrawText(canvas, minX.ToString("G4", CultureInfo.CurrentCulture), graph.Left, graph.Bottom + 1, 6.5f, Muted);
        var maxXText = maxX.ToString("G4", CultureInfo.CurrentCulture);
        DrawText(canvas, maxXText, graph.Right - Measure(maxXText, 6.5f, false).Width, graph.Bottom + 1, 6.5f, Muted);

        foreach (var series in plot.Series)
        {
            var seriesPoints = series.Points.Where(point => Finite(point.X) && Finite(point.Y)).OrderBy(point => point.X).ToList();
            if (seriesPoints.Count == 0) continue;
            if (seriesPoints.Any(point => point.Lower.HasValue && point.Upper.HasValue))
            {
                using var band = new SKPath();
                var bandPoints = seriesPoints.Where(point => point.Lower.HasValue && point.Upper.HasValue).ToList();
                if (bandPoints.Count > 1)
                {
                    band.MoveTo(MapX(bandPoints[0].X), MapY(bandPoints[0].Upper.GetValueOrDefault()));
                    foreach (var point in bandPoints.Skip(1)) band.LineTo(MapX(point.X), MapY(point.Upper.GetValueOrDefault()));
                    foreach (var point in bandPoints.AsEnumerable().Reverse()) band.LineTo(MapX(point.X), MapY(point.Lower.GetValueOrDefault()));
                    band.Close();
                    using var paint = new SKPaint { Color = PlotBand, Style = SKPaintStyle.Fill, IsAntialias = true };
                    canvas.DrawPath(band, paint);
                }
            }
            if (series.Kind == AnalysisReportPlotSeriesKind.Line && seriesPoints.Count > 1)
            {
                using var path = new SKPath();
                path.MoveTo(MapX(seriesPoints[0].X), MapY(seriesPoints[0].Y));
                foreach (var point in seriesPoints.Skip(1)) path.LineTo(MapX(point.X), MapY(point.Y));
                using var paint = new SKPaint { Color = PlotBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, IsAntialias = true };
                canvas.DrawPath(path, paint);
            }
            else
            {
                foreach (var point in seriesPoints)
                {
                    var x = MapX(point.X); var y = MapY(point.Y);
                    if (point.Lower.HasValue && point.Upper.HasValue)
                        Line(canvas, x, MapY(point.Lower.Value), x, MapY(point.Upper.Value), PlotBlue, .8f);
                    using var paint = new SKPaint { Color = PlotBlue, Style = SKPaintStyle.Fill, IsAntialias = true };
                    canvas.DrawCircle(x, y, 2.6f, paint);
                }
            }
        }

        float MapX(double value) => graph.Left + (float)((value - minX) / (maxX - minX)) * graph.Width;
        float MapY(double value) => graph.Bottom - (float)((value - minY) / (maxY - minY)) * graph.Height;
    }

    void DrawCorrelation(SKCanvas canvas, AnalysisReportCorrelationMatrixBlock matrix, SKRect rect)
    {
        DrawText(canvas, matrix.Title, rect.Left, rect.Top, 12, Ink, true);
        if (matrix.Matrix == null) return;
        var count = Math.Min(matrix.Labels.Count, Math.Min(matrix.Matrix.GetLength(0), matrix.Matrix.GetLength(1)));
        if (count == 0) return;
        var labelWidth = Math.Min(rect.Width * .35f,
            matrix.Labels.Select(label => Measure(label, 6.5f, false).Width).DefaultIfEmpty(52).Max() + 10);
        var available = Math.Min(rect.Width - labelWidth - 10, rect.Height - 60);
        var cell = Math.Max(8, available / count);
        var left = rect.Left + labelWidth;
        var top = rect.Top + 44;
        for (var index = 0; index < count; index++)
        {
            var label = matrix.Labels[index];
            DrawText(canvas, label, left + index * cell + 2, top - 14, Math.Min(7, cell * .22f), Ink);
            DrawText(canvas, label, rect.Left, top + index * cell + cell * .32f, Math.Min(6.5f, cell * .22f), Ink);
            for (var column = 0; column < count; column++)
            {
                var value = matrix.Matrix[index, column];
                var square = new SKRect(left + column * cell, top + index * cell,
                    left + (column + 1) * cell, top + (index + 1) * cell);
                Fill(canvas, square, CorrelationColor(value));
                Stroke(canvas, square, SKColors.White, .4f);
                var text = Finite(value) ? value.ToString("0.00", CultureInfo.InvariantCulture) : "-";
                var size = Math.Min(7, cell * .22f);
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

    void DrawText(SKCanvas canvas, string text, float x, float y, float size, SKColor color, bool bold = false)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        using var font = fonts.CreateFont(size, bold, false);
        canvas.DrawText(text ?? "", x, y - font.Metrics.Ascent, SKTextAlign.Left, font, paint);
    }

    SKSize Measure(string text, float size, bool bold) =>
        SkiaDrawingContext.MeasureTextValue(text, size, fonts, bold);

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
