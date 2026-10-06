using System;
using System.Collections.Generic;
using System.Linq;

using SkiaSharp;

using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Drawing;

sealed class SkiaFigureCanvasCellPlan
{
    public PublicationFigureCanvasCell Cell { get; init; } = null!;
    public PublicationFigureDocument Figure { get; init; } = null!;
    public PublicationFigureLayout Layout { get; init; } = null!;
    public PublicationFigureRenderSettings RenderSettings { get; init; } = null!;
}

sealed class SkiaFigureCanvasRenderPlan
{
    public PublicationFigureCanvasDocument Document { get; init; } = null!;
    public PublicationFigureCanvasLayoutResult LayoutResult { get; init; } = null!;
    public IReadOnlyList<SkiaFigureCanvasCellPlan> Cells { get; init; } = Array.Empty<SkiaFigureCanvasCellPlan>();
    public SkiaPublicationFontSet Fonts { get; init; } = null!;
    public float CanvasWidth { get; init; }
    public float CanvasHeight { get; init; }
    public IReadOnlyList<SkiaFigureCanvasRenderPlan> Pages { get; init; } = Array.Empty<SkiaFigureCanvasRenderPlan>();
    public int PageCount => Pages.Count == 0 && IsValid ? 1 : Pages.Count;

    public IReadOnlyList<PublicationFigureDocument> Figures => Cells.Select(cell => cell.Figure).ToList();
    public bool IsValid => Document.IsValid && LayoutResult.IsValid;
    public string ValidationError => Document.IsValid ? LayoutResult.ValidationError : Document.ValidationError;
}

sealed class SkiaFigureCanvasRenderer
{
    internal const float PdfPointsPerCentimeter = 72f / 2.54f;
    const float GapCentimeters = 0.08f;
    const float PanelLabelSize = 10f;
    const float PanelLabelInset = 3f;
    const float PanelTitleHeight = 14f;

    readonly SkiaFigureRenderer figureRenderer = new SkiaFigureRenderer();

    public SkiaFigureCanvasRenderPlan CreatePlan(PublicationFigureCanvasDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (!document.IsValid) return InvalidPlan(document, document.ValidationError);
        if (document.PageCount <= 1) return CreateSinglePagePlan(document);

        var pages = Enumerable.Range(0, document.PageCount)
            .Select(index => CreateSinglePagePlan(document.CreatePageDocument(index)))
            .ToList();
        var width = pages.Max(page => page.CanvasWidth);
        var height = pages.Max(page => page.CanvasHeight);
        var first = pages[0].LayoutResult;
        return new SkiaFigureCanvasRenderPlan
        {
            Document = document,
            LayoutResult = new PublicationFigureCanvasLayoutResult(first.PlotWidthCentimeters,
                first.PlotHeightCentimeters, width / PdfPointsPerCentimeter, height / PdfPointsPerCentimeter, ""),
            Cells = pages.SelectMany(page => page.Cells).ToList(),
            Fonts = pages[0].Fonts,
            CanvasWidth = width,
            CanvasHeight = height,
            Pages = pages
        };
    }

    SkiaFigureCanvasRenderPlan CreateSinglePagePlan(PublicationFigureCanvasDocument document)
    {

        var figures = document.Cells
            .Select(cell => PublicationFigureBuilder.Build(cell.Source, document.FigureOptions))
            .ToList();
        var fonts = figureRenderer.ResolveFontSet(document.FigureOptions);
        var activeColumns = Math.Min(document.Options.Columns, document.Cells.Count);
        var activeRows = (document.Cells.Count + document.Options.Columns - 1) / document.Options.Columns;
        var plotWidthCentimeters = Math.Min(document.Options.PlotWidthCentimeters, document.FigureOptions.PlotWidthCentimeters);
        var plotHeightCentimeters = Math.Min(document.Options.PlotHeightCentimeters, document.FigureOptions.PlotHeightCentimeters);
        var plotWidth = (float)plotWidthCentimeters * PdfPointsPerCentimeter;
        var plotHeight = (float)plotHeightCentimeters * PdfPointsPerCentimeter;
        var fontSize = (float)document.Options.FontSize;
        var symbolSize = (float)document.Options.SymbolSize;
        var strokeWidth = (float)document.Options.StrokeWidth;
        var tickLength = SkiaFigureRenderer.TickLength * (strokeWidth <= 0.5f ? 0.5f : 1f);

        var settings = document.Cells
            .Select(cell => new PublicationFigureRenderSettings
            {
                FontSize = fontSize,
                AnnotationFontSize = 6f,
                SymbolSize = symbolSize,
                StrokeWidth = strokeWidth,
                MajorTickLength = tickLength,
                MinorTickLength = tickLength * 0.5f,
                ShowAnnotationBoxes = document.Options.ShowInformationBoxes,
                ShowTopXAxisTickLabels = true,
                ShowBottomXAxisTickLabels = true,
                ShowYAxisTickLabels = true,
                ShowTopXAxisTitle = cell.Row == 0,
                ShowBottomXAxisTitle = cell.Row == activeRows - 1,
                ShowYAxisTitle = cell.Column == 0
            })
            .ToList();

        var leftMargins = new float[activeColumns];
        var rightMargins = new float[activeColumns];
        for (var column = 0; column < activeColumns; column++)
        {
            var indices = CellIndices(document, cell => cell.Column == column);
            leftMargins[column] = indices.Max(index => PublicationFigureLayout.RequiredLeftMargin(
                figures[index], settings[index].ShowYAxisTickLabels, settings[index].ShowYAxisTitle, fontSize, fonts));
            rightMargins[column] = indices.Max(index => PublicationFigureLayout.RequiredRightMargin(figures[index]));
        }

        var topMargins = new float[activeRows];
        var bottomMargins = new float[activeRows];
        for (var row = 0; row < activeRows; row++)
        {
            var indices = CellIndices(document, cell => cell.Row == row);
            topMargins[row] = indices.Max(index => PublicationFigureLayout.RequiredTopMargin(
                figures[index], settings[index].ShowTopXAxisTickLabels, settings[index].ShowTopXAxisTitle, fontSize, fonts))
                + (document.Options.ShowPanelTitles ? PanelTitleHeight : 0);
            bottomMargins[row] = indices.Max(index => PublicationFigureLayout.RequiredBottomMargin(
                figures[index], settings[index].ShowBottomXAxisTickLabels, settings[index].ShowBottomXAxisTitle, fontSize, fonts));
        }

        var gap = GapCentimeters * PdfPointsPerCentimeter;
        var columnWidths = Enumerable.Range(0, activeColumns)
            .Select(column => leftMargins[column] + plotWidth + rightMargins[column])
            .ToArray();
        var rowHeights = Enumerable.Range(0, activeRows)
            .Select(row => topMargins[row] + plotHeight + bottomMargins[row])
            .ToArray();
        var canvasWidth = columnWidths.Sum() + gap * Math.Max(0, activeColumns - 1);
        var canvasHeight = rowHeights.Sum() + gap * Math.Max(0, activeRows - 1);
        var columnOffsets = Offsets(columnWidths, gap);
        var rowOffsets = Offsets(rowHeights, gap);

        var cellPlans = new List<SkiaFigureCanvasCellPlan>();
        for (var index = 0; index < document.Cells.Count; index++)
        {
            var cell = document.Cells[index];
            var cellsInRow = document.Cells.Count(item => item.Row == cell.Row);
            var rowWidth = columnWidths.Take(cellsInRow).Sum() + gap * Math.Max(0, cellsInRow - 1);
            cellPlans.Add(new SkiaFigureCanvasCellPlan
            {
                Cell = cell,
                Figure = figures[index],
                RenderSettings = settings[index],
                Layout = PublicationFigureLayout.CreateAligned(
                    figures[index],
                    plotWidth,
                    plotHeight,
                    leftMargins[cell.Column],
                    rightMargins[cell.Column],
                    topMargins[cell.Row],
                    bottomMargins[cell.Row],
                    (canvasWidth - rowWidth) * .5f + columnOffsets[cell.Column],
                    rowOffsets[cell.Row])
            });
        }

        return new SkiaFigureCanvasRenderPlan
        {
            Document = document,
            LayoutResult = new PublicationFigureCanvasLayoutResult(
                plotWidthCentimeters,
                plotHeightCentimeters,
                canvasWidth / PdfPointsPerCentimeter,
                canvasHeight / PdfPointsPerCentimeter,
                ""),
            Cells = cellPlans,
            Fonts = fonts,
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            Pages = Array.Empty<SkiaFigureCanvasRenderPlan>()
        };
    }

    public SKBitmap RenderBitmap(SkiaFigureCanvasRenderPlan plan, int pixelWidth)
        => RenderPageBitmap(plan, 0, pixelWidth);

    public SKBitmap RenderPageBitmap(SkiaFigureCanvasRenderPlan plan, int pageIndex, int pixelWidth)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (!plan.IsValid) throw new InvalidOperationException(plan.ValidationError);
        var page = plan.Pages.Count == 0 ? plan : plan.Pages[Math.Clamp(pageIndex, 0, plan.Pages.Count - 1)];

        var width = Math.Max(320, pixelWidth);
        var height = Math.Max(320, (int)Math.Round(width * page.CanvasHeight / page.CanvasWidth));
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Scale(width / page.CanvasWidth, height / page.CanvasHeight);
        Draw(canvas, page);
        canvas.Flush();
        return bitmap;
    }

    public void WritePdf(SkiaFigureCanvasRenderPlan plan, string path)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (!plan.IsValid) throw new InvalidOperationException(plan.ValidationError);

        var metadata = new SKDocumentPdfMetadata
        {
            Title = "Supporting Figure",
            Author = MarkdownStrings.AppName,
            Creator = MarkdownStrings.AppName,
            Subject = "ITC supporting figures",
            Keywords = string.Join(", ", plan.Figures.Select(figure => figure.Title).Where(title => !string.IsNullOrWhiteSpace(title)))
        };

        using var pdf = SKDocument.CreatePdf(path, metadata);
        foreach (var page in plan.Pages.Count == 0 ? new[] { plan } : plan.Pages)
        {
            var canvas = pdf.BeginPage(page.CanvasWidth, page.CanvasHeight);
            Draw(canvas, page);
            pdf.EndPage();
        }
        pdf.Close();
    }

    internal void DrawInRect(SKCanvas canvas, SkiaFigureCanvasRenderPlan plan, SKRect target, bool allowUpscale = false)
    {
        if (canvas == null || plan == null || !plan.IsValid || target.Width <= 1 || target.Height <= 1) return;
        var page = plan.Pages.Count == 0 ? plan : plan.Pages[0];
        var scale = Math.Min(target.Width / page.CanvasWidth, target.Height / page.CanvasHeight);
        if (!allowUpscale) scale = Math.Min(1, scale);
        var x = target.Left + (target.Width - page.CanvasWidth * scale) * .5f;
        var y = target.Top + (target.Height - page.CanvasHeight * scale) * .5f;
        canvas.Save();
        canvas.Translate(x, y);
        canvas.Scale(scale);
        Draw(canvas, page);
        canvas.Restore();
    }

    void Draw(SKCanvas canvas, SkiaFigureCanvasRenderPlan plan)
    {
        using (var background = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill })
            canvas.DrawRect(new SKRect(0, 0, plan.CanvasWidth, plan.CanvasHeight), background);
        foreach (var cell in plan.Cells)
        {
            figureRenderer.DrawDocument(canvas, cell.Figure, cell.Layout, cell.RenderSettings, plan.Fonts);
            var figureBounds = cell.Layout.PageRect;
            var size = Math.Min(PanelLabelSize, (float)plan.Document.Options.FontSize);
            var heading = PanelHeading(cell.Cell, plan.Document.Options,
                figureBounds.Width - 2 * PanelLabelInset, size, plan.Fonts);
            if (heading.Label.Length == 0 && heading.Title.Length == 0) continue;

            var drawing = new SkiaDrawingContext(canvas, plan.Fonts);
            drawing.DrawBoldLeadText(
                heading.Label,
                PanelHeadingGap(size, plan.Fonts),
                heading.Title,
                new SKPoint(figureBounds.Left + PanelLabelInset, figureBounds.Top + PanelLabelInset),
                size,
                SKColors.Black);
        }
    }

    /// <summary>Bold panel label and the regular-weight title, middle-shortened to fit the available width.</summary>
    internal static (string Label, string Title) PanelHeading(
        PublicationFigureCanvasCell cell,
        PublicationFigureCanvasOptions options,
        float availableWidth,
        float size,
        SkiaPublicationFontSet fonts)
    {
        var label = options.ShowPanelLetters && !string.IsNullOrWhiteSpace(cell.PanelLabel) ? cell.PanelLabel : "";
        var title = options.ShowPanelTitles && !string.IsNullOrWhiteSpace(cell.PanelTitle) ? cell.PanelTitle : "";
        if (title.Length == 0) return (label, "");

        var reserved = label.Length == 0
            ? 0
            : SkiaDrawingContext.MeasureTextValue(label, size, fonts, bold: true).Width + PanelHeadingGap(size, fonts);
        return (label, PublicationFigureCanvasBuilder.FitPanelTitle(title, availableWidth - reserved,
            text => SkiaDrawingContext.MeasureTextValue(text, size, fonts).Width));
    }

    static float PanelHeadingGap(float size, SkiaPublicationFontSet fonts)
        => SkiaDrawingContext.MeasureTextValue(" ", size, fonts).Width;

    static List<int> CellIndices(PublicationFigureCanvasDocument document, Func<PublicationFigureCanvasCell, bool> predicate)
    {
        return document.Cells
            .Select((cell, index) => new { cell, index })
            .Where(item => predicate(item.cell))
            .Select(item => item.index)
            .ToList();
    }

    static float[] Offsets(IReadOnlyList<float> sizes, float gap)
    {
        var offsets = new float[sizes.Count];
        for (var index = 1; index < sizes.Count; index++)
            offsets[index] = offsets[index - 1] + sizes[index - 1] + gap;
        return offsets;
    }

    static SkiaFigureCanvasRenderPlan InvalidPlan(PublicationFigureCanvasDocument document, string error)
    {
        return new SkiaFigureCanvasRenderPlan
        {
            Document = document,
            LayoutResult = new PublicationFigureCanvasLayoutResult(0, 0, 0, 0, error)
        };
    }
}
