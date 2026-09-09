using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using AppKit;
using CoreGraphics;
using CoreText;
using Foundation;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.UI.MacOS.Drawing
{
    sealed class CoreGraphicsAnalysisReportRenderer
    {
        static readonly CGColor Ink = NSColor.FromRgb(23, 53, 64).CGColor;
        static readonly CGColor Muted = NSColor.FromRgb(89, 112, 120).CGColor;
        static readonly CGColor Rule = NSColor.FromRgb(213, 222, 218).CGColor;
        static readonly CGColor Header = NSColor.FromRgb(228, 241, 234).CGColor;
        static readonly CGColor Warning = NSColor.FromRgb(255, 248, 231).CGColor;
        static readonly CGColor Error = NSColor.FromRgb(255, 240, 239).CGColor;
        static readonly CGColor Information = NSColor.FromRgb(228, 241, 234).CGColor;
        static readonly CGColor PlotBlue = NSColor.FromRgb(23, 53, 64).CGColor;
        static readonly CGColor PlotBand = NSColor.FromRgba(112, 161, 203, 72).CGColor;
        static readonly CGColor Mint = NSColor.FromRgb(121, 205, 177).CGColor;
        static readonly CGColor Coral = NSColor.FromRgb(241, 125, 117).CGColor;
        static readonly CGColor Sun = NSColor.FromRgb(245, 189, 120).CGColor;
        readonly CoreGraphicsFigureCanvasRenderer figureRenderer = new CoreGraphicsFigureCanvasRenderer();
        readonly TextMeasurer measurer = new TextMeasurer();

        public AnalysisReportLayoutPlan CreatePlan(AnalysisReportDocument document) =>
            AnalysisReportLayoutEngine.Paginate(document, measurer);

        public NSData CreatePdfData(AnalysisReportDocument document, AnalysisReportLayoutPlan plan = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            plan = plan ?? CreatePlan(document);
            var data = new NSMutableData();
            using (var consumer = new CGDataConsumer(data))
            using (var context = new CGContextPDF(consumer, new CGPDFInfo
            {
                Title = document.Title,
                Author = document.Creator,
                Creator = document.Creator + " " + document.ApplicationVersion,
                Subject = "ITC analysis report",
                Keywords = new[] { "ITC", "analysis", "report", "thermogram", "fit" }
            }))
            {
                foreach (var page in plan.Pages)
                {
                    context.BeginPage(new CGRect(0, 0, page.Width, page.Height));
                    DrawPage(context, document, plan, page);
                    context.EndPage();
                }
                context.Close();
            }
            return data;
        }

        public void WritePdf(AnalysisReportDocument document, AnalysisReportLayoutPlan plan, string path)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("The destination directory is unavailable.");
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var data = CreatePdfData(document, plan)) File.WriteAllBytes(temporary, data.ToArray());
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        void DrawPage(CGContext context, AnalysisReportDocument document,
            AnalysisReportLayoutPlan plan, AnalysisReportPagePlan page)
        {
            context.SetFillColor(NSColor.White.CGColor);
            context.FillRect(new CGRect(0, 0, page.Width, page.Height));
            DrawHeader(context, document, page.Height, plan.MarginLeft,
                page.Width - plan.MarginRight, page.IsCover, page.ResultName);
            foreach (var fragment in page.Fragments)
                DrawFragment(context, document, page.Height, fragment, page.IsCover);
            DrawFooter(context, document, plan, page);
        }

        void DrawFragment(CGContext context, AnalysisReportDocument document, double pageHeight,
            AnalysisReportLayoutFragment fragment, bool isCover)
        {
            var rect = PdfRect(pageHeight, fragment.Bounds);
            switch (fragment.Kind)
            {
                case AnalysisReportFragmentKind.SectionTitle:
                    DrawLines(context, pageHeight, fragment.Lines, fragment.Bounds, 17, Ink, true);
                    if (isCover) DrawStatusBadge(context, document.ResultHealth, document.StatusBadgeText, pageHeight, fragment.Bounds);
                    else if (fragment.Section?.StatusBadgeHealth is AnalysisResultHealth sectionHealth)
                        DrawStatusBadge(context, sectionHealth, StatusBadgeText(sectionHealth), pageHeight, fragment.Bounds);
                    break;
                case AnalysisReportFragmentKind.Heading:
                    var heading = (AnalysisReportHeadingBlock)fragment.Block;
                    DrawLines(context, pageHeight, fragment.Lines, fragment.Bounds,
                        heading.Level == 1 ? 17 : heading.Level == 3 ? 10.5 : 12, Ink, true); break;
                case AnalysisReportFragmentKind.Text:
                    DrawTextBlock(context, pageHeight, (AnalysisReportTextBlock)fragment.Block, fragment); break;
                case AnalysisReportFragmentKind.Notice:
                    DrawNotice(context, pageHeight, (AnalysisReportNoticeBlock)fragment.Block, fragment, rect); break;
                case AnalysisReportFragmentKind.KeyValueRows:
                    DrawKeyValues(context, pageHeight, (AnalysisReportKeyValueBlock)fragment.Block, fragment); break;
                case AnalysisReportFragmentKind.TableRows:
                    DrawTable(context, pageHeight, (AnalysisReportTableBlock)fragment.Block, fragment); break;
                case AnalysisReportFragmentKind.PublicationFigure:
                    DrawFigure(context, pageHeight, (AnalysisReportFigureBlock)fragment.Block, fragment.Bounds); break;
                case AnalysisReportFragmentKind.PublicationFigurePair:
                    DrawFigurePair(context, pageHeight, (AnalysisReportFigurePairBlock)fragment.Block, fragment.Bounds); break;
                case AnalysisReportFragmentKind.FigureCanvas:
                    DrawFigureCanvas(context, pageHeight, (AnalysisReportFigureCanvasBlock)fragment.Block, fragment.Bounds); break;
                case AnalysisReportFragmentKind.CartesianPlot:
                    DrawPlot(context, pageHeight, (AnalysisReportPlotBlock)fragment.Block, fragment.Bounds); break;
                case AnalysisReportFragmentKind.ThermodynamicSummary:
                    DrawThermodynamicSummary(context, pageHeight, (AnalysisReportThermodynamicSummaryBlock)fragment.Block, fragment.Bounds); break;
                case AnalysisReportFragmentKind.CorrelationMatrix:
                    DrawCorrelation(context, pageHeight, (AnalysisReportCorrelationMatrixBlock)fragment.Block, fragment.Bounds); break;
                case AnalysisReportFragmentKind.TableOfContents:
                    DrawTableOfContents(context, pageHeight, (AnalysisReportTableOfContentsBlock)fragment.Block, fragment); break;
            }
        }

        void DrawTableOfContents(CGContext context, double pageHeight,
            AnalysisReportTableOfContentsBlock block, AnalysisReportLayoutFragment fragment)
        {
            var bounds = fragment.Bounds; var y = bounds.Y;
            if (!string.IsNullOrWhiteSpace(block.Title))
            {
                DrawTextTop(context, pageHeight, block.Title, bounds.X, y, 12, Ink, true);
                y += 18;
            }
            foreach (var entry in block.Entries.Skip(fragment.FirstItem).Take(fragment.ItemCount))
            {
                var page = entry.PageNumber > 0 ? entry.PageNumber.ToString(CultureInfo.CurrentCulture) : "–";
                var pageWidth = Measure(page, 9, false).Width;
                var titleWidth = Math.Max(40, bounds.Width - 42);
                var lines = Wrap(entry.Title, titleWidth, 9, false);
                var rowHeight = Math.Max(1, lines.Count) * 12 + 5;
                DrawLines(context, pageHeight, lines,
                    new AnalysisReportRect(bounds.X, y, titleWidth, rowHeight), 9, Ink);
                DrawTextTop(context, pageHeight, page, bounds.Right - pageWidth, y, 9, Ink);
                var leaderStart = bounds.X + Math.Min(titleWidth - 4,
                    Measure(lines.LastOrDefault() ?? "", 9, false).Width + 7);
                var leaderEnd = bounds.Right - pageWidth - 7;
                if (leaderEnd > leaderStart)
                    Line(context, leaderStart, pageHeight - y - 8, leaderEnd, pageHeight - y - 8, Rule, .7f);
                y += rowHeight;
            }
        }

        void DrawTextBlock(CGContext context, double pageHeight, AnalysisReportTextBlock block, AnalysisReportLayoutFragment fragment)
        {
            var bounds = fragment.Bounds; var y = bounds.Y;
            if (!string.IsNullOrWhiteSpace(block.Title)) { DrawTextTop(context, pageHeight, block.Title, bounds.X, y, 12, Ink, true); y += 18; }
            var bodyBounds = new AnalysisReportRect(bounds.X, y, bounds.Width, bounds.Bottom - y);
            if (block.InlineMarkdown) DrawInlineMarkdownLines(context, pageHeight, fragment.Lines, bodyBounds, 9, Ink);
            else DrawLines(context, pageHeight, fragment.Lines, bodyBounds, 9, Ink);
        }

        void DrawNotice(CGContext context, double pageHeight, AnalysisReportNoticeBlock block,
            AnalysisReportLayoutFragment fragment, CGRect rect)
        {
            Fill(context, rect, block.Level == AnalysisReportNoticeLevel.Warning ? Warning : block.Level == AnalysisReportNoticeLevel.Error ? Error : Information);
            Stroke(context, rect, Rule, .7f);
            var top = fragment.Bounds.Y + 5;
            if (!string.IsNullOrWhiteSpace(block.Title)) { DrawTextTop(context, pageHeight, block.Title, fragment.Bounds.X + 6, top, 10, Ink, true); top += 15; }
            DrawLines(context, pageHeight, fragment.Lines,
                new AnalysisReportRect(fragment.Bounds.X + 6, top, fragment.Bounds.Width - 12, fragment.Bounds.Bottom - top - 4), 9, Ink);
        }

        void DrawKeyValues(CGContext context, double pageHeight, AnalysisReportKeyValueBlock block, AnalysisReportLayoutFragment fragment)
        {
            var bounds = fragment.Bounds; var y = bounds.Y;
            if (!string.IsNullOrWhiteSpace(block.Title)) { DrawTextTop(context, pageHeight, block.Title, bounds.X, y, 12, Ink, true); y += 18; }
            var labelWidth = bounds.Width * .30;
            foreach (var item in block.Items.Skip(fragment.FirstItem).Take(fragment.ItemCount))
            {
                var indent = item.IndentLevel * 14;
                var labels = Wrap(item.Label, labelWidth - 6 - indent, 9, false);
                var values = Wrap(item.Value, bounds.Width - labelWidth - 6, 9, false);
                var height = Math.Max(labels.Count, values.Count) * 12 + 6;
                DrawLines(context, pageHeight, labels, new AnalysisReportRect(bounds.X + 3 + indent, y + 3, labelWidth - 6 - indent, height - 3), 9, Muted, true);
                DrawLines(context, pageHeight, values, new AnalysisReportRect(bounds.X + labelWidth + 3, y + 3, bounds.Width - labelWidth - 6, height - 3), 9, Ink);
                Line(context, bounds.X, pageHeight - y - height, bounds.Right, pageHeight - y - height, Rule, .45f);
                y += height;
            }
        }

        void DrawTable(CGContext context, double pageHeight, AnalysisReportTableBlock table, AnalysisReportLayoutFragment fragment)
        {
            var bounds = fragment.Bounds; var scale = fragment.Scale; var font = table.FontSize * scale; var line = font * 4 / 3;
            var horizontalPadding = 3 * scale; var verticalPadding = table.VerticalCellPadding * scale; var y = bounds.Y;
            if (!string.IsNullOrWhiteSpace(table.Title)) { DrawTextTop(context, pageHeight, table.Title, bounds.X, y, 12 * scale, Ink, true); y += 18 * scale; }
            var columns = Math.Max(1, table.Columns.Count);
            var weight = table.Columns.Sum(column => column.WidthWeight);
            var widths = table.Columns.Select(column => bounds.Width * column.WidthWeight / weight).ToArray();
            var offsets = new double[columns];
            for (var column = 1; column < columns; column++) offsets[column] = offsets[column - 1] + widths[column - 1];
            var headers = table.Columns.Select((column, index) => Wrap(column.Title, widths[index] - 2 * horizontalPadding, font, true)).ToList();
            var headerHeight = Math.Max(1, headers.Select(value => value.Count).DefaultIfEmpty(1).Max()) * line + 2 * verticalPadding;
            Fill(context, PdfRect(pageHeight, new AnalysisReportRect(bounds.X, y, bounds.Width, headerHeight)), Header);
            for (var column = 0; column < table.Columns.Count; column++)
            {
                var headerBounds = new AnalysisReportRect(bounds.X + offsets[column] + horizontalPadding, y + verticalPadding, widths[column] - 2 * horizontalPadding, headerHeight);
                if (table.InlineMarkdown) DrawInlineMarkdownLines(context, pageHeight, headers[column].Select(text => "**" + text + "**").ToList(), headerBounds, font, Ink, table.Columns[column].Alignment);
                else DrawLines(context, pageHeight, headers[column], headerBounds, font, Ink, true);
            }
            y += headerHeight;
            foreach (var row in table.Rows.Skip(fragment.FirstItem).Take(fragment.ItemCount))
            {
                var cells = Enumerable.Range(0, table.Columns.Count).Select(column => Wrap(column < row.Cells.Count ? row.Cells[column] : "", widths[column] - 2 * horizontalPadding, font, false)).ToList();
                var height = Math.Max(1, cells.Select(value => value.Count).DefaultIfEmpty(1).Max()) * line + 2 * verticalPadding;
                for (var column = 0; column < table.Columns.Count; column++)
                {
                    var cellBounds = new AnalysisReportRect(bounds.X + offsets[column] + horizontalPadding, y + verticalPadding, widths[column] - 2 * horizontalPadding, height);
                    if (table.InlineMarkdown) DrawInlineMarkdownLines(context, pageHeight, cells[column], cellBounds, font, Ink, table.Columns[column].Alignment);
                    else DrawLines(context, pageHeight, cells[column], cellBounds, font, Ink);
                }
                Line(context, bounds.X, pageHeight - y - height, bounds.Right, pageHeight - y - height, Rule, .45f);
                y += height;
            }
        }

        void DrawFigure(CGContext context, double pageHeight, AnalysisReportFigureBlock block, AnalysisReportRect bounds)
        {
            var top = bounds.Y;
            if (!string.IsNullOrWhiteSpace(block.Title)) { DrawTextTop(context, pageHeight, block.Title, bounds.X, top, 12, Ink, true); top += 18; }
            figureRenderer.DrawFigureInRect(context, block.Figure,
                PdfRect(pageHeight, new AnalysisReportRect(bounds.X, top, bounds.Width, bounds.Bottom - top)), 8);
        }

        void DrawFigurePair(CGContext context, double pageHeight, AnalysisReportFigurePairBlock block, AnalysisReportRect bounds)
        {
            var top = bounds.Y;
            if (!string.IsNullOrWhiteSpace(block.Title)) { DrawTextTop(context, pageHeight, block.Title, bounds.X, top, 12, Ink, true); top += 18; }
            const double gap = 12;
            var leftFraction = block.LeftFigure?.Options?.FocusThermogramOnBaseline == true ? .57 : .5;
            var leftWidth = (bounds.Width - gap) * leftFraction;
            var rightWidth = bounds.Width - gap - leftWidth;
            DrawTextTop(context, pageHeight, block.LeftTitle, bounds.X, top, 9, Ink, true);
            DrawTextTop(context, pageHeight, block.RightTitle, bounds.X + leftWidth + gap, top, 9, Ink, true);
            top += 14;
            figureRenderer.DrawFigureInRect(context, block.LeftFigure,
                PdfRect(pageHeight, new AnalysisReportRect(bounds.X, top, leftWidth, bounds.Bottom - top)), 8, alignTop: true);
            figureRenderer.DrawFigureInRect(context, block.RightFigure,
                PdfRect(pageHeight, new AnalysisReportRect(bounds.X + leftWidth + gap, top, rightWidth, bounds.Bottom - top)), 8, alignTop: true);
        }

        void DrawFigureCanvas(CGContext context, double pageHeight, AnalysisReportFigureCanvasBlock block, AnalysisReportRect bounds)
        {
            var top = bounds.Y;
            if (!string.IsNullOrWhiteSpace(block.Title)) { DrawTextTop(context, pageHeight, block.Title, bounds.X, top, 12, Ink, true); top += 18; }
            var plan = figureRenderer.CreatePlan(block.Canvas);
            figureRenderer.DrawInRect(context, plan,
                PdfRect(pageHeight, new AnalysisReportRect(bounds.X, top, bounds.Width, bounds.Bottom - top)));
        }

        void DrawPlot(CGContext context, double pageHeight, AnalysisReportPlotBlock plot, AnalysisReportRect bounds)
        {
            DrawTextTop(context, pageHeight, plot.Title, bounds.X, bounds.Y, 12, Ink, true);
            var legendEntries = plot.Series.Where(series => series.Kind == AnalysisReportPlotSeriesKind.Points)
                .GroupBy(series => string.IsNullOrWhiteSpace(series.Group) ? series.Label : series.Group)
                .Select(group => group.First()).ToList();
            var graphTop = bounds.Y + 28 + (legendEntries.Count > 1 ? 14 : 0); var graphLeft = bounds.X + 48; var graphRight = bounds.Right - 12; var graphBottom = bounds.Bottom - 30;
            var graph = PdfRect(pageHeight, new AnalysisReportRect(graphLeft, graphTop, graphRight - graphLeft, graphBottom - graphTop));
            var points = plot.Series.SelectMany(series => series.Points).Where(point => Finite(point.X) && Finite(point.Y)).ToList();
            if (points.Count == 0) return;
            var minX = points.Min(point => point.X); var maxX = points.Max(point => point.X); var minY = points.Min(point => point.Lower ?? point.Y); var maxY = points.Max(point => point.Upper ?? point.Y);
            Expand(ref minX, ref maxX); Expand(ref minY, ref maxY); Stroke(context, graph, Ink, .8f);
            DrawTextTop(context, pageHeight, plot.XAxisTitle, graphLeft, graphBottom + 10, 8, Ink);
            DrawTextTop(context, pageHeight, plot.YAxisTitle, bounds.X, graphTop, 8, Ink);
            for (var tick = 0; tick <= 4; tick++)
            {
                var value = minY + (maxY - minY) * tick / 4.0;
                var y = Y(value);
                Line(context, graph.X, y, graph.GetMaxX(), y, Rule, .4f);
                var text = value.ToString("G3", CultureInfo.CurrentCulture);
                DrawTextTop(context, pageHeight, text,
                    graphLeft - Measure(text, 6, false).Width - 5,
                    pageHeight - y - 4, 6, Muted);
            }
            var groupColors = new Dictionary<string, int>(); var nextColor = 0;
            foreach (var series in plot.Series)
            {
                var group = string.IsNullOrWhiteSpace(series.Group) ? series.Label : series.Group;
                if (!groupColors.TryGetValue(group, out var colorIndex)) groupColors[group] = colorIndex = nextColor++;
                var color = SeriesColor(colorIndex);
                var values = series.Points.Where(point => Finite(point.X) && Finite(point.Y)).OrderBy(point => point.X).ToList();
                var bandValues = values.Where(point => point.Lower.HasValue && point.Upper.HasValue && Finite(point.Lower.Value) && Finite(point.Upper.Value)).ToList();
                if (series.Kind == AnalysisReportPlotSeriesKind.Line && bandValues.Count > 1)
                {
                    using (var band = new CGPath())
                    {
                        band.MoveToPoint(X(bandValues[0].X), Y(bandValues[0].Upper.Value));
                        foreach (var point in bandValues.Skip(1)) band.AddLineToPoint(X(point.X), Y(point.Upper.Value));
                        foreach (var point in bandValues.AsEnumerable().Reverse()) band.AddLineToPoint(X(point.X), Y(point.Lower.Value));
                        band.CloseSubpath();
                        context.SaveState(); context.SetFillColor(new CGColor(color, .16f)); context.AddPath(band); context.FillPath(); context.RestoreState();
                    }
                }
                if (series.Kind == AnalysisReportPlotSeriesKind.Line && values.Count > 1)
                {
                    context.SaveState(); context.SetStrokeColor(color); context.SetLineWidth(1.4f);
                    context.MoveTo(X(values[0].X), Y(values[0].Y)); foreach (var point in values.Skip(1)) context.AddLineToPoint(X(point.X), Y(point.Y)); context.StrokePath(); context.RestoreState();
                }
                else foreach (var point in values)
                {
                    var x = X(point.X); var y = Y(point.Y);
                    if (plot.UncertaintyStyle == AnalysisITC.Core.Application.UncertaintyDisplayStyle.ConfidenceInterval || plot.UncertaintyStyle == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval)
                        DrawWhisker(x, point.ConfidenceLower, point.ConfidenceUpper, color, .7f, 3.5f);
                    if (plot.UncertaintyStyle == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviation || plot.UncertaintyStyle == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval || plot.UncertaintyStyle == AnalysisITC.Core.Application.UncertaintyDisplayStyle.Automatic)
                        DrawWhisker(x, point.StandardDeviationLower ?? point.Lower, point.StandardDeviationUpper ?? point.Upper, color, 1.2f, 2.2f);
                    DrawMarker(x, y, color, colorIndex);
                }
            }
            if (legendEntries.Count > 1)
            {
                var legendX = graphLeft; var legendTop = bounds.Y + 22;
                foreach (var entry in legendEntries)
                {
                    var key = string.IsNullOrWhiteSpace(entry.Group) ? entry.Label : entry.Group;
                    var index = groupColors.TryGetValue(key, out var value) ? value : 0;
                    DrawMarker((nfloat)(legendX + 3), (nfloat)(pageHeight - legendTop - 4), SeriesColor(index), index);
                    DrawTextTop(context, pageHeight, entry.Label, legendX + 10, legendTop, 7, Ink);
                    legendX += Measure(entry.Label, 7, false).Width + 24;
                }
            }
            nfloat X(double value) => graph.X + (nfloat)((value - minX) / (maxX - minX)) * graph.Width;
            nfloat Y(double value) => graph.Y + (nfloat)((value - minY) / (maxY - minY)) * graph.Height;
            void DrawWhisker(nfloat x, double? low, double? high, CGColor color, nfloat width, nfloat cap)
            {
                if (!low.HasValue || !high.HasValue || !Finite(low.Value) || !Finite(high.Value)) return;
                var y1 = Y(low.Value); var y2 = Y(high.Value);
                Line(context, x, y1, x, y2, color, width); Line(context, x - cap, y1, x + cap, y1, color, width); Line(context, x - cap, y2, x + cap, y2, color, width);
            }
            void DrawMarker(nfloat x, nfloat y, CGColor color, int index)
            {
                context.SaveState(); context.SetFillColor(color);
                if (index % 3 == 1) context.FillRect(new CGRect(x - 2.4, y - 2.4, 4.8, 4.8));
                else if (index % 3 == 2)
                {
                    context.MoveTo(x, y + (nfloat)3); context.AddLineToPoint(x + (nfloat)3, y - (nfloat)2.5); context.AddLineToPoint(x - (nfloat)3, y - (nfloat)2.5); context.ClosePath(); context.FillPath();
                }
                else context.FillEllipseInRect(new CGRect(x - 2.6, y - 2.6, 5.2, 5.2));
                context.RestoreState();
            }
        }

        void DrawThermodynamicSummary(CGContext context, double pageHeight, AnalysisReportThermodynamicSummaryBlock block, AnalysisReportRect bounds)
        {
            DrawTextTop(context, pageHeight, block.Title, bounds.X, bounds.Y, 12, Ink, true);
            var desiredWidth = Math.Min(bounds.Width, Math.Max(280, 92 + block.Categories.Count * Math.Max(42, 13 * block.Series.Count)));
            var left = bounds.X + (bounds.Width - desiredWidth) * .5;
            var graphTop = bounds.Y + 26;
            var hasUncertaintyNote = !string.IsNullOrWhiteSpace(block.UncertaintyNote);
            var graphBottom = bounds.Bottom - (hasUncertaintyNote ? 54 : 38);
            var graphLeft = left + 45;
            var graphRight = left + desiredWidth - 8;
            var all = block.Series.SelectMany(series => series.Bars).ToList();
            if (all.Count == 0) return;
            var values = all.SelectMany(DisplayedValues).Where(Finite).ToList();
            var min = Math.Min(0, values.Min()); var max = Math.Max(0, values.Max()); Expand(ref min, ref max);
            var graph = PdfRect(pageHeight, new AnalysisReportRect(graphLeft, graphTop, graphRight - graphLeft, graphBottom - graphTop));
            Stroke(context, graph, Ink, .8f);
            DrawTextTop(context, pageHeight, block.YAxisTitle, left, graphTop, 7.5, Ink);
            for (var tick = 0; tick <= 4; tick++)
            {
                var value = min + (max - min) * tick / 4.0;
                var y = Y(value);
                Line(context, graph.X, y, graph.GetMaxX(), y, Rule, .45f);
                var text = value.ToString("G3", CultureInfo.CurrentCulture);
                DrawTextTop(context, pageHeight, text,
                    graphLeft - Measure(text, 6, false).Width - 5,
                    pageHeight - y - 4, 6, Muted);
            }
            var zero = Y(0);
            Line(context, graph.X, zero, graph.GetMaxX(), zero, Rule, .7f);
            var categoryWidth = graph.Width / Math.Max(1, block.Categories.Count);
            var binWidth = categoryWidth * .76;
            var barWidth = Math.Max(3, binWidth / Math.Max(1, block.Series.Count) - 2);
            for (var category = 0; category < block.Categories.Count; category++)
            {
                var center = graph.X + categoryWidth * (category + .5);
                var label = block.Categories[category];
                DrawTextTop(context, pageHeight, label, center - Measure(label, 6.5, false).Width * .5, graphBottom + 3, 6.5, Ink);
                for (var seriesIndex = 0; seriesIndex < block.Series.Count; seriesIndex++)
                {
                    var bar = block.Series[seriesIndex].Bars.FirstOrDefault(item => item.Category == label);
                    if (bar == null || !Finite(bar.Value)) continue;
                    var x = center - binWidth * .5 + (seriesIndex + .5) * binWidth / block.Series.Count;
                    var y = Y(bar.Value);
                    var color = SeriesColor(seriesIndex);
                    context.SaveState(); context.SetFillColor(color); context.SetStrokeColor(Ink); context.SetLineWidth(.45f);
                    context.AddRect(new CGRect(x - barWidth * .5, Math.Min(y, zero), barWidth, Math.Abs(y - zero))); context.DrawPath(CGPathDrawingMode.FillStroke); context.RestoreState();
                    DrawBarUncertainty((nfloat)x, bar);
                }
            }
            var legendX = left + 45; var legendTop = bounds.Bottom - (hasUncertaintyNote ? 30 : 14);
            for (var index = 0; index < block.Series.Count; index++)
            {
                var label = block.Series[index].Label;
                Fill(context, PdfRect(pageHeight, new AnalysisReportRect(legendX, legendTop, 8, 8)), SeriesColor(index));
                DrawTextTop(context, pageHeight, label, legendX + 12, legendTop - 1, 6.5, Ink);
                legendX += 20 + Measure(label, 6.5, false).Width;
            }
            if (hasUncertaintyNote)
                DrawTextTop(context, pageHeight, block.UncertaintyNote,
                    bounds.X, bounds.Bottom - 10, 6.25, Muted);

            nfloat Y(double value) => graph.Y + (nfloat)((value - min) / (max - min)) * graph.Height;
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
            void DrawBarUncertainty(nfloat x, AnalysisReportThermodynamicBar bar)
            {
                var style = block.UncertaintyStyle;
                if (style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.ConfidenceInterval || style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval)
                    DrawWhisker(x, bar.ConfidenceLower, bar.ConfidenceUpper, .7f, 4);
                if (style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviation || style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval || style == AnalysisITC.Core.Application.UncertaintyDisplayStyle.Automatic)
                    DrawWhisker(x, bar.StandardDeviationLower, bar.StandardDeviationUpper, 1.5f, 2.5f);
            }
            void DrawWhisker(nfloat x, double? low, double? high, nfloat width, nfloat cap)
            {
                if (!low.HasValue || !high.HasValue || !Finite(low.Value) || !Finite(high.Value)) return;
                var y1 = Y(low.Value); var y2 = Y(high.Value);
                Line(context, x, y1, x, y2, Ink, width); Line(context, x - cap, y1, x + cap, y1, Ink, width); Line(context, x - cap, y2, x + cap, y2, Ink, width);
            }
        }

        void DrawCorrelation(CGContext context, double pageHeight, AnalysisReportCorrelationMatrixBlock matrix, AnalysisReportRect bounds)
        {
            DrawTextTop(context, pageHeight, matrix.Title, bounds.X, bounds.Y, 12, Ink, true);
            if (matrix.Matrix == null) return;
            var count = Math.Min(matrix.Labels.Count, Math.Min(matrix.Matrix.GetLength(0), matrix.Matrix.GetLength(1))); if (count == 0) return;
            var desired = matrix.PreferredSizeCentimeters * 72 / 2.54;
            var labelWidth = Math.Min(120, matrix.Labels.Select(label => (double)Measure(label, 6.5, false).Width).DefaultIfEmpty(52).Max() + 10);
            var cell = Math.Max(8, Math.Min(desired, bounds.Height - 68) / count);
            var totalWidth = labelWidth + cell * count;
            var start = bounds.X + Math.Max(0, (bounds.Width - totalWidth) * .5);
            var left = start + labelWidth;
            var top = bounds.Y + 36;
            var labelFontSize = Math.Max(5, Math.Min(6.5, cell * .22));
            for (var column = 0; column < count; column++)
            {
                var label = matrix.ColumnLabels[column];
                var measured = Measure(label, labelFontSize, false);
                DrawTextTop(context, pageHeight, label,
                    left + column * cell + (cell - measured.Width) * .5,
                    top - measured.Height - 4,
                    labelFontSize, Ink);
            }
            for (var row = 0; row < count; row++)
            {
                var rowLabel = matrix.Labels[row];
                var rowLabelSize = Measure(rowLabel, labelFontSize, false);
                DrawTextTop(context, pageHeight, rowLabel, start,
                    top + row * cell + (cell - rowLabelSize.Height) * .5,
                    labelFontSize, Ink);
                for (var column = 0; column < count; column++)
                {
                    var value = matrix.Matrix[row, column]; var square = new AnalysisReportRect(left + column * cell, top + row * cell, cell, cell);
                    Fill(context, PdfRect(pageHeight, square), CorrelationColor(value)); Stroke(context, PdfRect(pageHeight, square), NSColor.White.CGColor, .4f);
                    var text = Finite(value) ? value.ToString("0.00", CultureInfo.InvariantCulture) : "-";
                    var textSize = Math.Max(5, Math.Min(7, cell * .22));
                    var measured = Measure(text, textSize, false);
                    DrawTextTop(context, pageHeight, text,
                        square.X + (cell - measured.Width) * .5,
                        square.Y + (cell - measured.Height) * .5,
                        textSize, Math.Abs(value) > .65 ? NSColor.White.CGColor : Ink);
                }
            }
            var noteTop = top + count * cell + 8;
            foreach (var note in matrix.Notes.Take(3))
            {
                DrawTextTop(context, pageHeight, note, start, noteTop, 7.5, Muted);
                noteTop += 10;
            }
        }

        void DrawHeader(CGContext context, AnalysisReportDocument document,
            double pageHeight, double left, double right, bool isCover, string resultName)
        {
            var brand = isCover ? "FT-ITC ANALYSIS REPORT" : "FT-ITC Analysis" +
                (string.IsNullOrWhiteSpace(resultName) ? "" : " · " + resultName);
            var brandSize = isCover ? 7.5 : 6.5;
            DrawTextTop(context, pageHeight, brand, left, 14, brandSize, Ink, isCover);
            var exportDate = document.ExportDateText;
            DrawTextTop(context, pageHeight, exportDate,
                right - Measure(exportDate, 6.5, false).Width, 14, 6.5, Muted);
            Line(context, left, pageHeight - 31, right, pageHeight - 31,
                isCover ? Mint : Rule, isCover ? 2 : .45f);
        }

        void DrawStatusBadge(CGContext context, AnalysisResultHealth health, string text,
            double pageHeight, AnalysisReportRect titleBounds)
        {
            var textWidth = Measure(text, 6.5, true).Width;
            var width = textWidth + 16;
            var x = titleBounds.Right - width;
            var y = titleBounds.Y + 1;
            var fill = health == AnalysisResultHealth.Valid ? Information
                : health == AnalysisResultHealth.Warning ? Warning : Error;
            var stroke = health == AnalysisResultHealth.Valid ? Mint
                : health == AnalysisResultHealth.Warning ? Sun : Coral;
            var rect = PdfRect(pageHeight, new AnalysisReportRect(x, y, width, 18));
            using (var path = CGPath.FromRoundedRect(rect, 2.5f, 2.5f))
            {
                context.AddPath(path); context.SetFillColor(fill); context.FillPath();
                context.AddPath(path); context.SetStrokeColor(stroke); context.SetLineWidth(1.2f); context.StrokePath();
            }
            DrawTextTop(context, pageHeight, text, x + 8, y + 5, 6.5, Ink, true);
        }

        static string StatusBadgeText(AnalysisResultHealth health) => health switch
        {
            AnalysisResultHealth.Valid => "ANALYSIS VALID",
            AnalysisResultHealth.Warning => "REVIEW WARNINGS",
            AnalysisResultHealth.PartialInvalid => "PARTIAL / STALE",
            AnalysisResultHealth.Invalid => "INVALID / STALE",
            _ => "STATUS UNKNOWN",
        };

        void DrawFooter(CGContext context, AnalysisReportDocument document, AnalysisReportLayoutPlan plan, AnalysisReportPagePlan page)
        {
            var top = page.Height - plan.MarginBottom + 5; var left = plan.MarginLeft; var right = page.Width - plan.MarginRight;
            Line(context, left, page.Height - top + 5, right, page.Height - top + 5, Rule, .45f);
            DrawTextTop(context, page.Height, document.Creator + " " + document.ApplicationVersion, left, top, 6.5, Muted);
            var number = "Page " + page.PageNumber + " of " + plan.Pages.Count;
            DrawTextTop(context, page.Height, number, right - Measure(number, 6.5, false).Width, top, 6.5, Muted);
            if (!page.IsCover) DrawTextTop(context, page.Height, document.Title, left + (right - left - Measure(document.Title, 6.5, false).Width) * .5, top, 6.5, Muted);
        }

        void DrawLines(CGContext context, double pageHeight, IReadOnlyList<string> lines, AnalysisReportRect bounds, double size, CGColor color, bool bold = false)
        {
            var y = bounds.Y; var advance = size * 1.34;
            foreach (var line in lines) { if (y + advance > bounds.Bottom + .5) break; DrawTextTop(context, pageHeight, line, bounds.X, y, size, color, bold); y += advance; }
        }

        void DrawInlineMarkdownLines(CGContext context, double pageHeight,
            IReadOnlyList<string> lines, AnalysisReportRect bounds, double size, CGColor color, AnalysisResultColumnAlignment alignment = AnalysisResultColumnAlignment.Left)
        {
            var y = bounds.Y; var advance = size * 4 / 3;
            var bold = false; var italic = false;
            foreach (var line in lines)
            {
                if (y + advance > bounds.Bottom + .5) break;
                var measureBold = bold; var measureItalic = italic;
                var visibleWidth = 0.0;
                foreach (var run in System.Text.RegularExpressions.Regex.Split(line, @"(\*\*|\*)"))
                {
                    if (run == "**") measureBold = !measureBold;
                    else if (run == "*") measureItalic = !measureItalic;
                    else visibleWidth += Measure(run, size, measureBold, measureItalic).Width;
                }
                var shift = alignment == AnalysisResultColumnAlignment.Right ? bounds.Width - visibleWidth
                    : alignment == AnalysisResultColumnAlignment.Center ? (bounds.Width - visibleWidth) / 2 : 0;
                var x = bounds.X + Math.Max(0, shift); var index = 0;
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
                    DrawTextTop(context, pageHeight, text, x, y, size, color, bold, italic);
                    x += Measure(text, size, bold, italic).Width;
                    index = next;
                }
                y += advance;
            }
        }

        List<string> Wrap(string text, double width, double size, bool bold)
        {
            var output = new List<string>();
            foreach (var paragraph in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (paragraph.Length == 0) { output.Add(""); continue; }
                var current = "";
                foreach (var word in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Measure(word, size, bold).Width > width)
                    {
                        if (current.Length > 0) { output.Add(current); current = ""; }
                        var part = "";
                        foreach (var character in word)
                        {
                            var candidatePart = part + character;
                            if (part.Length > 0 && Measure(candidatePart, size, bold).Width > width) { output.Add(part); part = character.ToString(); }
                            else part = candidatePart;
                        }
                        current = part;
                        continue;
                    }
                    var candidate = current.Length == 0 ? word : current + " " + word;
                    if (current.Length > 0 && Measure(candidate, size, bold).Width > width) { output.Add(current); current = word; }
                    else current = candidate;
                }
                output.Add(current);
            }
            return output;
        }

        static void DrawTextTop(CGContext context, double pageHeight, string text, double x,
            double top, double size, CGColor color, bool bold = false, bool italic = false)
        {
            var fontName = bold && italic ? "Helvetica Neue Medium Italic"
                : bold ? "Helvetica Neue Medium" : italic ? "Helvetica Neue Italic" : "Helvetica Neue Light";
            using (var font = new CTFont(fontName, (nfloat)size))
            using (var attributed = new NSAttributedString(text ?? "", new CTStringAttributes { Font = font, ForegroundColorFromContext = true }))
            using (var line = new CTLine(attributed))
            {
                context.SaveState(); context.SetFillColor(color); context.TextPosition = new CGPoint(x, pageHeight - top - size); line.Draw(context); context.RestoreState();
            }
        }

        static CGSize Measure(string text, double size, bool bold, bool italic = false)
        {
            var fontName = bold && italic ? "Helvetica Neue Medium Italic"
                : bold ? "Helvetica Neue Medium" : italic ? "Helvetica Neue Italic" : "Helvetica Neue Light";
            using (var font = new CTFont(fontName, (nfloat)size))
            using (var attributed = new NSAttributedString(text ?? "", new CTStringAttributes { Font = font }))
            using (var line = new CTLine(attributed))
                return new CGSize((nfloat)line.GetTypographicBounds(), size * 1.2);
        }

        static CGRect PdfRect(double pageHeight, AnalysisReportRect rect) => new CGRect(rect.X, pageHeight - rect.Bottom, rect.Width, rect.Height);
        static void Fill(CGContext context, CGRect rect, CGColor color) { context.SetFillColor(color); context.FillRect(rect); }
        static void Stroke(CGContext context, CGRect rect, CGColor color, nfloat width) { context.SaveState(); context.SetStrokeColor(color); context.SetLineWidth(width); context.StrokeRect(rect); context.RestoreState(); }
        static void Line(CGContext context, double x1, double y1, double x2, double y2, CGColor color, nfloat width) { context.SaveState(); context.SetStrokeColor(color); context.SetLineWidth(width); context.MoveTo((nfloat)x1, (nfloat)y1); context.AddLineToPoint((nfloat)x2, (nfloat)y2); context.StrokePath(); context.RestoreState(); }
        static CGColor CorrelationColor(double value) { if (!Finite(value)) return NSColor.FromRgb(230, 230, 230).CGColor; var amount = (int)Math.Round(225 - Math.Min(1, Math.Abs(value)) * 145); return value < 0 ? NSColor.FromRgb(amount, amount, 235).CGColor : NSColor.FromRgb(235, amount, amount).CGColor; }
        static CGColor SeriesColor(int index) => (index % 5) switch
        {
            0 => PlotBlue,
            1 => Mint,
            2 => Coral,
            3 => Sun,
            _ => NSColor.FromRgb(89, 112, 120).CGColor,
        };
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static void Expand(ref double minimum, ref double maximum) { if (minimum == maximum) { var delta = Math.Abs(minimum) > 0 ? Math.Abs(minimum) * .05 : 1; minimum -= delta; maximum += delta; } else { var padding = (maximum - minimum) * .06; minimum -= padding; maximum += padding; } }

        sealed class TextMeasurer : IAnalysisReportTextMeasurer
        {
            public AnalysisReportSize Measure(string text, AnalysisReportTextStyle style)
            {
                var size = CoreGraphicsAnalysisReportRenderer.Measure(text, style.FontSize, style.Bold);
                return new AnalysisReportSize(size.Width, size.Height);
            }
        }
    }
}
