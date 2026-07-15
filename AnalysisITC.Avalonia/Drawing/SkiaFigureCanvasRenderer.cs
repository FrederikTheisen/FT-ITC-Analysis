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
    public float CanvasWidth { get; init; }
    public float CanvasHeight { get; init; }

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

    readonly SkiaFigureRenderer figureRenderer = new SkiaFigureRenderer();

    public SkiaFigureCanvasRenderPlan CreatePlan(PublicationFigureCanvasDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (!document.IsValid) return InvalidPlan(document, document.ValidationError);

        var figures = document.Cells
            .Select(cell => PublicationFigureBuilder.Build(cell.Source, document.FigureOptions))
            .ToList();
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
                figures[index], settings[index].ShowYAxisTickLabels, settings[index].ShowYAxisTitle, fontSize));
            rightMargins[column] = indices.Max(index => PublicationFigureLayout.RequiredRightMargin(figures[index]));
        }

        var topMargins = new float[activeRows];
        var bottomMargins = new float[activeRows];
        for (var row = 0; row < activeRows; row++)
        {
            var indices = CellIndices(document, cell => cell.Row == row);
            topMargins[row] = indices.Max(index => PublicationFigureLayout.RequiredTopMargin(
                figures[index], settings[index].ShowTopXAxisTickLabels, settings[index].ShowTopXAxisTitle, fontSize));
            bottomMargins[row] = indices.Max(index => PublicationFigureLayout.RequiredBottomMargin(
                figures[index], settings[index].ShowBottomXAxisTickLabels, settings[index].ShowBottomXAxisTitle, fontSize));
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
                    columnOffsets[cell.Column],
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
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight
        };
    }

    public SKBitmap RenderBitmap(SkiaFigureCanvasRenderPlan plan, int pixelWidth)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (!plan.IsValid) throw new InvalidOperationException(plan.ValidationError);

        var width = Math.Max(320, pixelWidth);
        var height = Math.Max(320, (int)Math.Round(width * plan.CanvasHeight / plan.CanvasWidth));
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Scale(width / plan.CanvasWidth, height / plan.CanvasHeight);
        Draw(canvas, plan);
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
        var canvas = pdf.BeginPage(plan.CanvasWidth, plan.CanvasHeight);
        Draw(canvas, plan);
        pdf.EndPage();
        pdf.Close();
    }

    void Draw(SKCanvas canvas, SkiaFigureCanvasRenderPlan plan)
    {
        canvas.Clear(SKColors.White);
        foreach (var cell in plan.Cells)
        {
            figureRenderer.DrawDocument(canvas, cell.Figure, cell.Layout, cell.RenderSettings);
            if (string.IsNullOrWhiteSpace(cell.Cell.PanelLabel)) continue;

            var figureBounds = cell.Layout.PageRect;
            var drawing = new SkiaDrawingContext(canvas);
            drawing.DrawText(
                cell.Cell.PanelLabel,
                new SKPoint(figureBounds.Left + PanelLabelInset, figureBounds.Top + PanelLabelInset),
                PanelLabelSize,
                SKColors.Black,
                bold: true);
        }
    }

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
