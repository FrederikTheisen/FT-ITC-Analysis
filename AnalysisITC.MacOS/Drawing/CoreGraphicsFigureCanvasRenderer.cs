using System;
using System.Collections.Generic;
using System.Linq;

using AppKit;
using CoreGraphics;
using CoreText;
using Foundation;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.UI.MacOS.Drawing
{
    sealed class CoreGraphicsFigureRenderSettings
    {
        public float FontSize { get; set; }
        public float AnnotationFontSize { get; set; } = 6;
        public float SymbolSize { get; set; }
        public float StrokeWidth { get; set; } = 1;
        public float MajorTickLength { get; set; } = 6;
        public float MinorTickLength { get; set; } = 3;
        public bool ShowAnnotationBoxes { get; set; } = true;
        public bool ShowTopXAxisTitle { get; set; } = true;
        public bool ShowBottomXAxisTitle { get; set; } = true;
        public bool ShowYAxisTitle { get; set; } = true;
    }

    sealed class CoreGraphicsFigureLayout
    {
        public CGRect PageRect { get; set; }
        public CGRect ThermogramRect { get; set; }
        public CGRect FitRect { get; set; }
        public CGRect ResidualRect { get; set; }
    }

    sealed class CoreGraphicsFigureCanvasCellPlan
    {
        public PublicationFigureCanvasCell Cell { get; set; }
        public PublicationFigureDocument Figure { get; set; }
        public CoreGraphicsFigureLayout Layout { get; set; }
        public CoreGraphicsFigureRenderSettings Settings { get; set; }
    }

    sealed class CoreGraphicsFigureCanvasRenderPlan
    {
        public PublicationFigureCanvasDocument Document { get; set; }
        public PublicationFigureCanvasLayoutResult LayoutResult { get; set; }
        public IReadOnlyList<CoreGraphicsFigureCanvasCellPlan> Cells { get; set; } = Array.Empty<CoreGraphicsFigureCanvasCellPlan>();
        public float CanvasWidth { get; set; }
        public float CanvasHeight { get; set; }
        public bool IsValid => Document != null && Document.IsValid && LayoutResult != null && LayoutResult.IsValid;
        public string ValidationError => Document != null && !Document.IsValid
            ? Document.ValidationError
            : LayoutResult?.ValidationError ?? "Could not create the supporting figure.";
    }

    sealed class CoreGraphicsFigureCanvasRenderer
    {
        internal const float PdfPointsPerCentimeter = 72f / 2.54f;
        const float PageInset = 0.1f * PdfPointsPerCentimeter;
        const float Gap = 0.08f * PdfPointsPerCentimeter;
        const float StandardTickLength = 6;
        const float AxisLabelHorizontalOffset = 6;
        const float AxisLabelVerticalOffset = 2;
        const float HorizontalAxisTitleOffset = 2;
        const float VerticalAxisTitleMinimumOffset = 4;
        const float VerticalAxisTitleOffsetFontFraction = 0.3f;
        const float AnnotationInset = 8;
        const float AnnotationPaddingX = 6;
        const float AnnotationPaddingY = 2;
        const float AnnotationLineSpacingFactor = 0.15f;
        const float ResidualGraphGap = 5;
        const float PanelLabelSize = 10;
        const float PanelLabelInset = 3;

        static readonly CGColor Black = NSColor.Black.CGColor;
        static readonly CGColor White = NSColor.White.CGColor;
        static readonly CGColor Gray = NSColor.FromRgb(120, 120, 120).CGColor;
        static readonly CGColor BandGray = NSColor.FromRgba(190, 190, 190, 85).CGColor;
        static readonly CGColor BaselineRed = NSColor.FromRgb(220, 35, 35).CGColor;

        public CoreGraphicsFigureCanvasRenderPlan CreatePlan(PublicationFigureCanvasDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (!document.IsValid) return InvalidPlan(document, document.ValidationError);

            var figures = document.Cells
                .Select(cell => PublicationFigureBuilder.Build(cell.Source, document.FigureOptions))
                .ToList();
            var activeColumns = Math.Min(document.Options.Columns, document.Cells.Count);
            var activeRows = (document.Cells.Count + document.Options.Columns - 1) / document.Options.Columns;
            var plotWidthCentimeters = document.Options.PlotWidthCentimeters;
            var plotHeightCentimeters = document.Options.PlotHeightCentimeters;
            var plotWidth = (float)plotWidthCentimeters * PdfPointsPerCentimeter;
            var plotHeight = (float)plotHeightCentimeters * PdfPointsPerCentimeter;
            var fontSize = (float)document.Options.FontSize;
            var strokeWidth = (float)document.Options.StrokeWidth;
            var tickLength = StandardTickLength * (strokeWidth <= 0.5f ? 0.5f : 1);

            var settings = document.Cells.Select(cell => new CoreGraphicsFigureRenderSettings
            {
                FontSize = fontSize,
                AnnotationFontSize = 6,
                SymbolSize = (float)document.Options.SymbolSize,
                StrokeWidth = strokeWidth,
                MajorTickLength = tickLength,
                MinorTickLength = tickLength * 0.5f,
                ShowAnnotationBoxes = document.Options.ShowInformationBoxes,
                ShowTopXAxisTitle = cell.Row == 0,
                ShowBottomXAxisTitle = cell.Row == activeRows - 1,
                ShowYAxisTitle = cell.Column == 0
            }).ToList();

            var leftMargins = new float[activeColumns];
            var rightMargins = new float[activeColumns];
            for (var column = 0; column < activeColumns; column++)
            {
                var indices = CellIndices(document, cell => cell.Column == column);
                leftMargins[column] = indices.Max(index => RequiredLeftMargin(figures[index], settings[index]));
                rightMargins[column] = PageInset;
            }

            var topMargins = new float[activeRows];
            var bottomMargins = new float[activeRows];
            for (var row = 0; row < activeRows; row++)
            {
                var indices = CellIndices(document, cell => cell.Row == row);
                topMargins[row] = indices.Max(index => RequiredTopMargin(figures[index], settings[index]));
                bottomMargins[row] = indices.Max(index => RequiredBottomMargin(figures[index], settings[index]));
            }

            var columnWidths = Enumerable.Range(0, activeColumns)
                .Select(column => leftMargins[column] + plotWidth + rightMargins[column])
                .ToArray();
            var rowHeights = Enumerable.Range(0, activeRows)
                .Select(row => topMargins[row] + plotHeight + bottomMargins[row])
                .ToArray();
            var canvasWidth = columnWidths.Sum() + Gap * Math.Max(0, activeColumns - 1);
            var canvasHeight = rowHeights.Sum() + Gap * Math.Max(0, activeRows - 1);
            var columnOffsets = Offsets(columnWidths, Gap);
            var rowOffsetsFromTop = Offsets(rowHeights, Gap);

            var cells = new List<CoreGraphicsFigureCanvasCellPlan>();
            for (var index = 0; index < document.Cells.Count; index++)
            {
                var cell = document.Cells[index];
                var pageX = columnOffsets[cell.Column];
                var pageY = canvasHeight - rowOffsetsFromTop[cell.Row] - rowHeights[cell.Row];
                cells.Add(new CoreGraphicsFigureCanvasCellPlan
                {
                    Cell = cell,
                    Figure = figures[index],
                    Settings = settings[index],
                    Layout = CreateLayout(
                        figures[index],
                        plotWidth,
                        plotHeight,
                        leftMargins[cell.Column],
                        rightMargins[cell.Column],
                        topMargins[cell.Row],
                        bottomMargins[cell.Row],
                        pageX,
                        pageY)
                });
            }

            return new CoreGraphicsFigureCanvasRenderPlan
            {
                Document = document,
                LayoutResult = new PublicationFigureCanvasLayoutResult(
                    plotWidthCentimeters,
                    plotHeightCentimeters,
                    canvasWidth / PdfPointsPerCentimeter,
                    canvasHeight / PdfPointsPerCentimeter,
                    ""),
                Cells = cells,
                CanvasWidth = canvasWidth,
                CanvasHeight = canvasHeight
            };
        }

        public NSData CreatePdfData(CoreGraphicsFigureCanvasRenderPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsValid) throw new InvalidOperationException(plan.ValidationError);

            var data = new NSMutableData();
            using (var consumer = new CGDataConsumer(data))
            using (var context = new CGContextPDF(consumer, new CGPDFInfo
            {
                Title = "Supporting Figure",
                Author = MarkdownStrings.AppName,
                Creator = $"{MarkdownStrings.AppName} v{AppVersion.FullVersionString}",
                Subject = "ITC supporting figures",
                Keywords = plan.Cells.Select(cell => cell.Figure.Title).Where(title => !string.IsNullOrWhiteSpace(title)).ToArray()
            }))
            {
                var mediaBox = new CGRect(0, 0, plan.CanvasWidth, plan.CanvasHeight);
                context.BeginPage(mediaBox);
                Draw(context, plan);
                context.EndPage();
                context.Close();
            }
            return data;
        }

        void Draw(CGContext context, CoreGraphicsFigureCanvasRenderPlan plan)
        {
            context.SetFillColor(White);
            context.FillRect(new CGRect(0, 0, plan.CanvasWidth, plan.CanvasHeight));

            foreach (var cell in plan.Cells)
            {
                DrawFigure(context, cell.Figure, cell.Layout, cell.Settings);
                if (string.IsNullOrWhiteSpace(cell.Cell.PanelLabel)) continue;

                DrawText(context,
                    cell.Cell.PanelLabel,
                    new CGPoint(cell.Layout.PageRect.X + PanelLabelInset, cell.Layout.PageRect.GetMaxY() - PanelLabelInset),
                    PanelLabelSize,
                    HorizontalAnchor.Left,
                    VerticalAnchor.Top,
                    bold: true);
            }
        }

        void DrawFigure(CGContext context, PublicationFigureDocument document, CoreGraphicsFigureLayout layout, CoreGraphicsFigureRenderSettings settings)
        {
            context.SetFillColor(White);
            context.FillRect(layout.PageRect);

            if (document.ThermogramPanel != null)
                DrawPanel(context, document, document.ThermogramPanel, layout.ThermogramRect, settings, true, settings.ShowTopXAxisTitle, layout.PageRect.X);
            if (document.FitPanel != null)
                DrawPanel(context, document, document.FitPanel, layout.FitRect, settings, document.ResidualPanel == null, document.ResidualPanel == null && settings.ShowBottomXAxisTitle, layout.PageRect.X);
            if (document.ResidualPanel != null)
                DrawPanel(context, document, document.ResidualPanel, layout.ResidualRect, settings, true, settings.ShowBottomXAxisTitle, layout.PageRect.X);
        }

        void DrawPanel(
            CGContext context,
            PublicationFigureDocument document,
            PublicationFigurePanel panel,
            CGRect rect,
            CoreGraphicsFigureRenderSettings settings,
            bool drawXAxisLabels,
            bool drawXAxisTitle,
            nfloat pageLeft)
        {
            context.SetFillColor(White);
            context.FillRect(rect);
            context.SaveState();
            context.ClipToRect(rect);

            foreach (var band in panel.Bands) DrawBand(context, panel, rect, band);
            if (document.Options.IntegrationRegionStyle == PublicationIntegrationRegionStyle.Fill)
                foreach (var region in panel.IntegrationRegions) DrawIntegrationRegionFill(context, panel, rect, region);

            if (panel.DrawZeroLine && panel.YAxis.Minimum < 0 && panel.YAxis.Maximum > 0)
            {
                var y = TransformY(panel, rect, 0);
                DrawLine(context, new CGPoint(rect.X, y), new CGPoint(rect.GetMaxX(), y), Gray, settings.StrokeWidth);
            }

            foreach (var marker in panel.Markers)
            {
                var x = TransformX(panel, rect, marker.X);
                DrawLine(context, new CGPoint(x, rect.Y), new CGPoint(x, rect.GetMaxY()), Gray, 0.55f);
            }

            foreach (var series in panel.Series) DrawSeries(context, document.Options, panel, rect, series, settings.StrokeWidth);
            if (document.Options.IntegrationRegionStyle != PublicationIntegrationRegionStyle.Fill)
                foreach (var region in panel.IntegrationRegions) DrawIntegrationRegionMarker(context, document.Options, panel, rect, region, settings.StrokeWidth);
            foreach (var point in panel.Points) DrawErrorPoint(context, document.Options, panel, rect, point, settings.SymbolSize, settings.StrokeWidth);

            context.RestoreState();
            StrokeRect(context, rect, Black, settings.StrokeWidth);
            DrawAxes(context, document, panel, rect, settings, drawXAxisLabels, drawXAxisTitle, pageLeft);

            if (settings.ShowAnnotationBoxes)
                foreach (var box in panel.AnnotationBoxes) DrawAnnotationBox(context, panel, rect, box, settings.AnnotationFontSize, settings.StrokeWidth);
        }

        void DrawSeries(CGContext context, PublicationFigureOptions options, PublicationFigurePanel panel, CGRect rect, PublicationSeries series, float strokeWidth)
        {
            var points = series.Points
                .Where(point => IsFinite(point.X) && IsFinite(point.Y))
                .Select(point => Transform(panel, rect, point.X, point.Y))
                .ToList();
            if (points.Count < 2) return;

            var color = series.Role == PublicationSeriesRole.Baseline ? BaselineRed : Black;
            var width = series.Role switch
            {
                PublicationSeriesRole.Fit => (float)Math.Max(0.25, options.FitLineWidth * strokeWidth),
                PublicationSeriesRole.Baseline => (float)Math.Max(0.25, options.BaselineWidth * strokeWidth),
                _ => strokeWidth
            };
            var smooth = series.Role == PublicationSeriesRole.Fit && options.FitLineSmoothness != LineSmoothness.Linear;
            DrawPolyline(context, points, color, width, smooth,
                series.Role == PublicationSeriesRole.Baseline && options.BaselineStyle == PublicationBaselineStyle.Dashed);
        }

        void DrawIntegrationRegionFill(CGContext context, PublicationFigurePanel panel, CGRect rect, PublicationIntegrationRegion region)
        {
            if (region.Data.Count < 2 || region.Baseline.Count < 2) return;
            using (var path = new CGPath())
            {
                var data = region.Data.Select(point => Transform(panel, rect, point.X, point.Y)).ToList();
                var baseline = region.Baseline.Select(point => Transform(panel, rect, point.X, point.Y)).Reverse().ToList();
                path.MoveToPoint(data[0]);
                foreach (var point in data.Skip(1)) path.AddLineToPoint(point);
                foreach (var point in baseline) path.AddLineToPoint(point);
                path.CloseSubpath();
                context.SetFillColor(Black);
                context.AddPath(path);
                context.FillPath();
            }
        }

        void DrawIntegrationRegionMarker(CGContext context, PublicationFigureOptions options, PublicationFigurePanel panel, CGRect rect, PublicationIntegrationRegion region, float strokeWidth)
        {
            if (region.Baseline.Count < 2) return;
            if (options.IntegrationRegionStyle == PublicationIntegrationRegionStyle.Bar)
            {
                var left = TransformX(panel, rect, region.Baseline.First().X);
                var right = TransformX(panel, rect, region.Baseline.Last().X);
                var y = region.BarAtTop ? rect.GetMaxY() - 8 : rect.Y + 5;
                context.SetFillColor(Gray);
                context.FillRect(new CGRect(Math.Min(left, right), y, Math.Abs(right - left), 3));
                return;
            }

            foreach (var endpoint in new[] { region.Baseline.First(), region.Baseline.Last() })
            {
                var center = Transform(panel, rect, endpoint.X, endpoint.Y);
                DrawLine(context, new CGPoint(center.X, center.Y - 4), new CGPoint(center.X, center.Y + 4), Gray, strokeWidth);
            }
        }

        void DrawBand(CGContext context, PublicationFigurePanel panel, CGRect rect, PublicationBand band)
        {
            if (band.Upper.Count < 2 || band.Lower.Count < 2) return;
            var upper = band.Upper.Select(point => Transform(panel, rect, point.X, point.Y)).ToList();
            var lower = band.Lower.Select(point => Transform(panel, rect, point.X, point.Y)).Reverse().ToList();
            using (var path = new CGPath())
            {
                path.MoveToPoint(upper[0]);
                foreach (var point in upper.Skip(1)) path.AddLineToPoint(point);
                foreach (var point in lower) path.AddLineToPoint(point);
                path.CloseSubpath();
                context.SetFillColor(BandGray);
                context.AddPath(path);
                context.FillPath();
            }
        }

        void DrawErrorPoint(CGContext context, PublicationFigureOptions options, PublicationFigurePanel panel, CGRect rect, PublicationErrorPoint point, float symbolSize, float strokeWidth)
        {
            var center = Transform(panel, rect, point.X, point.Y);
            var top = Transform(panel, rect, point.X, point.UpperY);
            var bottom = Transform(panel, rect, point.X, point.LowerY);
            symbolSize = Math.Max(3, symbolSize);

            if (Math.Abs(top.Y - bottom.Y) > symbolSize * 0.7f)
            {
                var cap = symbolSize * 0.45f;
                DrawLine(context, new CGPoint(center.X, top.Y), new CGPoint(center.X, center.Y + symbolSize * 0.5f), Black, strokeWidth);
                DrawLine(context, new CGPoint(center.X, center.Y - symbolSize * 0.5f), new CGPoint(center.X, bottom.Y), Black, strokeWidth);
                DrawLine(context, new CGPoint(center.X - cap, top.Y), new CGPoint(center.X + cap, top.Y), Black, strokeWidth);
                DrawLine(context, new CGPoint(center.X - cap, bottom.Y), new CGPoint(center.X + cap, bottom.Y), Black, strokeWidth);
            }

            var half = symbolSize * 0.5f;
            var symbolRect = new CGRect(center.X - half, center.Y - half, symbolSize, symbolSize);
            context.SetFillColor(point.Included ? Black : White);
            if (options.SymbolShape == PublicationSymbolShape.Circle) context.FillEllipseInRect(symbolRect);
            else context.FillRect(symbolRect);
            if (options.SymbolShape == PublicationSymbolShape.Circle) StrokeEllipse(context, symbolRect, Black, strokeWidth);
            else StrokeRect(context, symbolRect, Black, strokeWidth);
        }

        void DrawAxes(
            CGContext context,
            PublicationFigureDocument document,
            PublicationFigurePanel panel,
            CGRect rect,
            CoreGraphicsFigureRenderSettings settings,
            bool drawXAxisLabels,
            bool drawXAxisTitle,
            nfloat pageLeft)
        {
            DrawXAxis(context, document, panel, rect, drawXAxisLabels, drawXAxisTitle, settings);
            DrawYAxis(context, document, panel, rect, settings.ShowYAxisTitle, pageLeft, settings);
        }

        void DrawXAxis(CGContext context, PublicationFigureDocument document, PublicationFigurePanel panel, CGRect rect, bool drawLabels, bool drawTitle, CoreGraphicsFigureRenderSettings settings)
        {
            var fontSize = settings.FontSize;
            var top = panel.XAxis.Placement == PublicationAxisPlacement.Top;
            var axisY = top ? rect.GetMaxY() : rect.Y;
            var direction = top ? -1 : 1;

            foreach (var tick in panel.XAxis.MinorTicks)
            {
                var x = TransformX(panel, rect, tick);
                DrawLine(context, new CGPoint(x, axisY), new CGPoint(x, axisY + direction * settings.MinorTickLength), Black, settings.StrokeWidth);
                var opposite = top ? rect.Y : rect.GetMaxY();
                DrawLine(context, new CGPoint(x, opposite), new CGPoint(x, opposite - direction * settings.MinorTickLength), Black, settings.StrokeWidth);
            }

            foreach (var tick in panel.XAxis.MajorTicks)
            {
                var x = TransformX(panel, rect, tick);
                DrawLine(context, new CGPoint(x, axisY), new CGPoint(x, axisY + direction * settings.MajorTickLength), Black, settings.StrokeWidth);
                var opposite = top ? rect.Y : rect.GetMaxY();
                DrawLine(context, new CGPoint(x, opposite), new CGPoint(x, opposite - direction * settings.MajorTickLength), Black, settings.StrokeWidth);

                if (drawLabels)
                {
                    DrawText(context,
                        panel.XAxis.FormatTick(tick),
                        new CGPoint(x, top ? rect.GetMaxY() + AxisLabelVerticalOffset : rect.Y - AxisLabelVerticalOffset),
                        fontSize,
                        HorizontalAnchor.Center,
                        top ? VerticalAnchor.Bottom : VerticalAnchor.Top);
                }
            }

            if (!document.Options.ShowAxisTitles || !drawTitle || string.IsNullOrWhiteSpace(panel.XAxis.Title)) return;
            var labelHeight = drawLabels ? MaxTickLabelHeight(panel.XAxis, fontSize) : 0;
            DrawText(context,
                panel.XAxis.Title,
                new CGPoint(rect.GetMidX(), top
                    ? rect.GetMaxY() + AxisLabelVerticalOffset + labelHeight + HorizontalAxisTitleOffset
                    : rect.Y - AxisLabelVerticalOffset - labelHeight - HorizontalAxisTitleOffset),
                fontSize + 1,
                HorizontalAnchor.Center,
                top ? VerticalAnchor.Bottom : VerticalAnchor.Top,
                rich: true);
        }

        void DrawYAxis(CGContext context, PublicationFigureDocument document, PublicationFigurePanel panel, CGRect rect, bool drawTitle, nfloat pageLeft, CoreGraphicsFigureRenderSettings settings)
        {
            var fontSize = settings.FontSize;
            foreach (var tick in panel.YAxis.MinorTicks)
            {
                var y = TransformY(panel, rect, tick);
                DrawLine(context, new CGPoint(rect.X, y), new CGPoint(rect.X + settings.MinorTickLength, y), Black, settings.StrokeWidth);
                DrawLine(context, new CGPoint(rect.GetMaxX(), y), new CGPoint(rect.GetMaxX() - settings.MinorTickLength, y), Black, settings.StrokeWidth);
            }

            foreach (var tick in panel.YAxis.MajorTicks)
            {
                var y = TransformY(panel, rect, tick);
                DrawLine(context, new CGPoint(rect.X, y), new CGPoint(rect.X + settings.MajorTickLength, y), Black, settings.StrokeWidth);
                DrawLine(context, new CGPoint(rect.GetMaxX(), y), new CGPoint(rect.GetMaxX() - settings.MajorTickLength, y), Black, settings.StrokeWidth);
                DrawText(context,
                    panel.YAxis.FormatTick(tick),
                    new CGPoint(rect.X - AxisLabelHorizontalOffset * fontSize / 12f, y),
                    fontSize,
                    HorizontalAnchor.Right,
                    VerticalAnchor.Middle);
            }

            if (!document.Options.ShowAxisTitles || !drawTitle || string.IsNullOrWhiteSpace(panel.YAxis.Title)) return;
            var titleHeight = MeasureRichText(panel.YAxis.Title, fontSize + 1).Height;
            var x = pageLeft + PageInset + titleHeight * 0.5f;
            DrawText(context,
                panel.YAxis.Title,
                new CGPoint(x, rect.GetMidY()),
                fontSize + 1,
                HorizontalAnchor.Center,
                VerticalAnchor.Middle,
                rotation: (float)(Math.PI / 2),
                rich: true);
        }

        void DrawAnnotationBox(CGContext context, PublicationFigurePanel panel, CGRect rect, PublicationAnnotationBox box, float fontSize, float strokeWidth)
        {
            if (box.Lines.Count == 0) return;
            var sizes = box.Lines.Select(line => MeasureRichText(line, fontSize)).ToList();
            var lineHeight = fontSize;
            var lineGap = fontSize * AnnotationLineSpacingFactor;
            var paddingX = AnnotationPaddingX * fontSize / 12f;
            var width = sizes.Max(size => size.Width) + paddingX * 2;
            var height = box.Lines.Count * lineHeight + (box.Lines.Count - 1) * lineGap + AnnotationPaddingY * 2;
            var upper = ResolveBoxUpperPlacement(panel, box);
            var x = rect.GetMaxX() - width - AnnotationInset;
            var y = upper ? rect.GetMaxY() - height - AnnotationInset : rect.Y + AnnotationInset;
            var boxRect = new CGRect(x, y, width, height);

            context.SetFillColor(White);
            context.FillRect(boxRect);
            StrokeRect(context, boxRect, Black, strokeWidth);

            var textY = boxRect.GetMaxY() - AnnotationPaddingY;
            foreach (var line in box.Lines)
            {
                DrawText(context, line, new CGPoint(x + paddingX, textY), fontSize, HorizontalAnchor.Left, VerticalAnchor.Top, rich: true);
                textY -= lineHeight + lineGap;
            }
        }

        static bool ResolveBoxUpperPlacement(PublicationFigurePanel panel, PublicationAnnotationBox box)
        {
            if (box.Placement == PublicationInfoBoxPlacement.Upper) return true;
            if (box.Placement == PublicationInfoBoxPlacement.Lower) return false;
            if (panel.Kind == PublicationPanelKind.Fit)
            {
                var fit = panel.Series.FirstOrDefault(series => series.Role == PublicationSeriesRole.Fit);
                if (fit?.Points.Count > 1) return fit.Points.First().Y > fit.Points.Last().Y;
            }
            if (panel.Kind == PublicationPanelKind.Thermogram)
                return Math.Abs(panel.YAxis.Maximum) >= Math.Abs(panel.YAxis.Minimum);
            return true;
        }

        static CoreGraphicsFigureLayout CreateLayout(
            PublicationFigureDocument document,
            float plotWidth,
            float plotHeight,
            float leftMargin,
            float rightMargin,
            float topMargin,
            float bottomMargin,
            float pageX,
            float pageY)
        {
            var pageRect = new CGRect(pageX, pageY, leftMargin + plotWidth + rightMargin, topMargin + plotHeight + bottomMargin);
            var plotLeft = pageX + leftMargin;
            var plotBottom = pageY + bottomMargin;
            var hasThermogram = document.ThermogramPanel != null;
            var hasResidual = document.ResidualPanel != null;
            var thermogramHeight = hasThermogram ? plotHeight * 0.5f : 0;
            var fitCompositeHeight = hasThermogram ? plotHeight - thermogramHeight : plotHeight;
            var residualFraction = (float)Math.Max(0.05, Math.Min(0.5, document.Options.ResidualPanelFraction));
            var gap = hasResidual && document.Options.IncludeResidualGraphGap ? ResidualGraphGap : 0;
            var residualHeight = hasResidual ? Math.Max(1, fitCompositeHeight * residualFraction) : 0;
            var fitHeight = hasResidual ? Math.Max(1, fitCompositeHeight - residualHeight - gap) : fitCompositeHeight;
            var fitBottom = plotBottom + residualHeight + gap;

            return new CoreGraphicsFigureLayout
            {
                PageRect = pageRect,
                ThermogramRect = hasThermogram
                    ? new CGRect(plotLeft, plotBottom + fitCompositeHeight, plotWidth, thermogramHeight)
                    : CGRect.Empty,
                FitRect = new CGRect(plotLeft, fitBottom, plotWidth, fitHeight),
                ResidualRect = hasResidual
                    ? new CGRect(plotLeft, plotBottom, plotWidth, residualHeight)
                    : CGRect.Empty
            };
        }

        static float RequiredLeftMargin(PublicationFigureDocument figure, CoreGraphicsFigureRenderSettings settings)
        {
            return figure.Panels.Select(panel =>
            {
                var tick = AxisLabelHorizontalOffset * settings.FontSize / 12f + MaxTickLabelWidth(panel.YAxis, settings.FontSize);
                var title = settings.ShowYAxisTitle && figure.Options.ShowAxisTitles && !string.IsNullOrWhiteSpace(panel.YAxis.Title)
                    ? PageInset + Math.Max(VerticalAxisTitleMinimumOffset, settings.FontSize * VerticalAxisTitleOffsetFontFraction)
                        + (float)MeasureRichText(panel.YAxis.Title, settings.FontSize + 1).Height
                    : 0;
                return Math.Max(PageInset, tick + title);
            }).DefaultIfEmpty(PageInset).Max();
        }

        static float RequiredTopMargin(PublicationFigureDocument figure, CoreGraphicsFigureRenderSettings settings)
        {
            if (figure.ThermogramPanel == null) return PageInset;
            return HorizontalMargin(figure.ThermogramPanel.XAxis, figure.Options.ShowAxisTitles && settings.ShowTopXAxisTitle, settings.FontSize);
        }

        static float RequiredBottomMargin(PublicationFigureDocument figure, CoreGraphicsFigureRenderSettings settings)
        {
            var axis = figure.ResidualPanel?.XAxis ?? figure.FitPanel?.XAxis;
            return axis == null
                ? PageInset
                : HorizontalMargin(axis, figure.Options.ShowAxisTitles && settings.ShowBottomXAxisTitle, settings.FontSize);
        }

        static float HorizontalMargin(PublicationAxis axis, bool showTitle, float fontSize)
        {
            var tick = AxisLabelVerticalOffset + MaxTickLabelHeight(axis, fontSize);
            var title = showTitle && !string.IsNullOrWhiteSpace(axis.Title)
                ? HorizontalAxisTitleOffset + (float)MeasureRichText(axis.Title, fontSize + 1).Height
                : 0;
            // Keep the measured tick labels and title clear of the cropped PDF
            // edge. Without this outer inset, the title glyph bounds can end
            // exactly at the top or bottom of the supporting figure.
            return PageInset + tick + title;
        }

        static float MaxTickLabelWidth(PublicationAxis axis, float fontSize)
            => axis.MajorTicks.Count == 0 ? 0 : axis.MajorTicks.Max(tick => (float)MeasureText(axis.FormatTick(tick), fontSize).Width);

        static float MaxTickLabelHeight(PublicationAxis axis, float fontSize)
            => axis.MajorTicks.Count == 0 ? 0 : axis.MajorTicks.Max(tick => (float)MeasureText(axis.FormatTick(tick), fontSize).Height);

        static CGSize MeasureText(string text, float size, bool bold = false)
        {
            using (var font = Font(size, bold))
            using (var attributed = new NSAttributedString(text ?? "", new CTStringAttributes { Font = font }))
            using (var line = new CTLine(attributed))
            {
                var glyphBounds = line.GetBounds(CTLineBoundsOptions.UseGlyphPathBounds);
                return new CGSize((nfloat)line.GetTypographicBounds(), glyphBounds.Height);
            }
        }

        static CGSize MeasureRichText(string text, float size)
        {
            using (var font = Font(size))
            using (var attributed = AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(text ?? "", NSFont.FromCTFont(font), true))
            using (var line = new CTLine(attributed))
            {
                var glyphBounds = line.GetBounds(CTLineBoundsOptions.UseGlyphPathBounds);
                return new CGSize((nfloat)line.GetTypographicBounds(), glyphBounds.Height);
            }
        }

        static void DrawText(
            CGContext context,
            string text,
            CGPoint anchor,
            float size,
            HorizontalAnchor horizontal,
            VerticalAnchor vertical,
            bool bold = false,
            float rotation = 0,
            bool rich = false)
        {
            using (var font = Font(size, bold))
            using (var attributed = rich
                ? AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(text ?? "", NSFont.FromCTFont(font), true)
                : new NSAttributedString(text ?? "", new CTStringAttributes { Font = font, ForegroundColorFromContext = true }))
            using (var line = new CTLine(attributed))
            {
                var bounds = line.GetBounds(CTLineBoundsOptions.UseGlyphPathBounds);
                var typographicWidth = (nfloat)line.GetTypographicBounds();
                var x = horizontal switch
                {
                    HorizontalAnchor.Center => -typographicWidth * 0.5f,
                    HorizontalAnchor.Right => -typographicWidth,
                    _ => 0
                };
                var y = vertical switch
                {
                    VerticalAnchor.Top => -(bounds.Y + bounds.Height),
                    VerticalAnchor.Middle => -(bounds.Y + bounds.Height * 0.5f),
                    VerticalAnchor.Bottom => -bounds.Y,
                    _ => 0
                };

                context.SaveState();
                context.SetFillColor(Black);
                context.SetStrokeColor(Black);
                context.TranslateCTM(anchor.X, anchor.Y);
                if (Math.Abs(rotation) > float.Epsilon) context.RotateCTM(rotation);
                context.TextPosition = new CGPoint(x, y);
                line.Draw(context);
                context.RestoreState();
            }
        }

        static CTFont Font(float size, bool bold = false)
            => new CTFont(bold ? "Helvetica Neue Medium" : "Helvetica Neue Light", size);

        static CGPoint Transform(PublicationFigurePanel panel, CGRect rect, double x, double y)
            => new CGPoint(TransformX(panel, rect, x), TransformY(panel, rect, y));

        static nfloat TransformX(PublicationFigurePanel panel, CGRect rect, double value)
        {
            var span = panel.XAxis.Maximum - panel.XAxis.Minimum;
            if (Math.Abs(span) < double.Epsilon) span = 1;
            return (nfloat)(rect.X + (value - panel.XAxis.Minimum) / span * rect.Width);
        }

        static nfloat TransformY(PublicationFigurePanel panel, CGRect rect, double value)
        {
            var span = panel.YAxis.Maximum - panel.YAxis.Minimum;
            if (Math.Abs(span) < double.Epsilon) span = 1;
            return (nfloat)(rect.Y + (value - panel.YAxis.Minimum) / span * rect.Height);
        }

        static void DrawLine(CGContext context, CGPoint start, CGPoint end, CGColor color, nfloat width)
        {
            context.SaveState();
            context.SetStrokeColor(color);
            context.SetLineWidth(width);
            context.MoveTo(start.X, start.Y);
            context.AddLineToPoint(end.X, end.Y);
            context.StrokePath();
            context.RestoreState();
        }

        static void StrokeRect(CGContext context, CGRect rect, CGColor color, nfloat width)
        {
            context.SaveState();
            context.SetStrokeColor(color);
            context.SetLineWidth(width);
            context.StrokeRect(rect);
            context.RestoreState();
        }

        static void StrokeEllipse(CGContext context, CGRect rect, CGColor color, nfloat width)
        {
            context.SaveState();
            context.SetStrokeColor(color);
            context.SetLineWidth(width);
            context.StrokeEllipseInRect(rect);
            context.RestoreState();
        }

        static void DrawPolyline(CGContext context, IReadOnlyList<CGPoint> points, CGColor color, nfloat width, bool smooth, bool dashed)
        {
            using (var path = smooth ? SmoothedPath(points) : LinearPath(points))
            {
                context.SaveState();
                context.SetStrokeColor(color);
                context.SetLineWidth(width);
                context.SetLineJoin(CGLineJoin.Round);
                context.SetLineCap(CGLineCap.Round);
                if (dashed) context.SetLineDash(0, new[] { 2 * width, 2 * width });
                context.AddPath(path);
                context.StrokePath();
                context.RestoreState();
            }
        }

        static CGPath LinearPath(IReadOnlyList<CGPoint> points)
        {
            var path = new CGPath();
            path.MoveToPoint(points[0]);
            foreach (var point in points.Skip(1)) path.AddLineToPoint(point);
            return path;
        }

        static CGPath SmoothedPath(IReadOnlyList<CGPoint> points)
        {
            if (points.Count < 3) return LinearPath(points);
            var path = new CGPath();
            path.MoveToPoint(points[0]);
            for (var index = 0; index < points.Count - 1; index++)
            {
                var p0 = points[Math.Max(0, index - 1)];
                var p1 = points[index];
                var p2 = points[index + 1];
                var p3 = points[Math.Min(points.Count - 1, index + 2)];
                var c1 = new CGPoint(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                var c2 = new CGPoint(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                path.AddCurveToPoint(c1.X, c1.Y, c2.X, c2.Y, p2.X, p2.Y);
            }
            return path;
        }

        static List<int> CellIndices(PublicationFigureCanvasDocument document, Func<PublicationFigureCanvasCell, bool> predicate)
            => document.Cells.Select((cell, index) => new { cell, index }).Where(item => predicate(item.cell)).Select(item => item.index).ToList();

        static float[] Offsets(IReadOnlyList<float> sizes, float gap)
        {
            var offsets = new float[sizes.Count];
            for (var index = 1; index < sizes.Count; index++) offsets[index] = offsets[index - 1] + sizes[index - 1] + gap;
            return offsets;
        }

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        static CoreGraphicsFigureCanvasRenderPlan InvalidPlan(PublicationFigureCanvasDocument document, string error)
            => new CoreGraphicsFigureCanvasRenderPlan
            {
                Document = document,
                LayoutResult = new PublicationFigureCanvasLayoutResult(0, 0, 0, 0, error)
            };

        enum HorizontalAnchor { Left, Center, Right }
        enum VerticalAnchor { Baseline, Top, Middle, Bottom }
    }
}
