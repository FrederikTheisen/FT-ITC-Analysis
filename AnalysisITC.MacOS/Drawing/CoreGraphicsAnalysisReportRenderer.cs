using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using AppKit;
using CoreGraphics;
using CoreText;
using Foundation;

using AnalysisITC.Core.Presentation;

namespace AnalysisITC.UI.MacOS.Drawing
{
    sealed class CoreGraphicsAnalysisReportRenderer
    {
        static readonly CGColor Ink = NSColor.FromRgb(30, 34, 38).CGColor;
        static readonly CGColor Muted = NSColor.FromRgb(92, 98, 104).CGColor;
        static readonly CGColor Rule = NSColor.FromRgb(205, 209, 213).CGColor;
        static readonly CGColor Header = NSColor.FromRgb(235, 237, 239).CGColor;
        static readonly CGColor Warning = NSColor.FromRgb(255, 244, 214).CGColor;
        static readonly CGColor Error = NSColor.FromRgb(255, 228, 228).CGColor;
        static readonly CGColor Information = NSColor.FromRgb(232, 241, 248).CGColor;
        static readonly CGColor PlotBlue = NSColor.FromRgb(33, 92, 145).CGColor;
        static readonly CGColor PlotBand = NSColor.FromRgba(112, 161, 203, 72).CGColor;
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
            foreach (var fragment in page.Fragments) DrawFragment(context, page.Height, fragment);
            DrawFooter(context, document, plan, page);
        }

        void DrawFragment(CGContext context, double pageHeight, AnalysisReportLayoutFragment fragment)
        {
            var rect = PdfRect(pageHeight, fragment.Bounds);
            switch (fragment.Kind)
            {
                case AnalysisReportFragmentKind.SectionTitle:
                    DrawLines(context, pageHeight, fragment.Lines, fragment.Bounds, 17, Ink, true); break;
                case AnalysisReportFragmentKind.Heading:
                    var heading = (AnalysisReportHeadingBlock)fragment.Block;
                    DrawLines(context, pageHeight, fragment.Lines, fragment.Bounds, heading.Level == 1 ? 17 : 12, Ink, true); break;
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
                case AnalysisReportFragmentKind.ContactSheet:
                    DrawContactSheet(context, pageHeight, (AnalysisReportContactSheetBlock)fragment.Block, fragment.Bounds); break;
                case AnalysisReportFragmentKind.CartesianPlot:
                    DrawPlot(context, pageHeight, (AnalysisReportPlotBlock)fragment.Block, fragment.Bounds); break;
                case AnalysisReportFragmentKind.CorrelationMatrix:
                    DrawCorrelation(context, pageHeight, (AnalysisReportCorrelationMatrixBlock)fragment.Block, fragment.Bounds); break;
            }
        }

        void DrawTextBlock(CGContext context, double pageHeight, AnalysisReportTextBlock block, AnalysisReportLayoutFragment fragment)
        {
            var bounds = fragment.Bounds; var y = bounds.Y;
            if (!string.IsNullOrWhiteSpace(block.Title)) { DrawTextTop(context, pageHeight, block.Title, bounds.X, y, 12, Ink, true); y += 18; }
            DrawLines(context, pageHeight, fragment.Lines, new AnalysisReportRect(bounds.X, y, bounds.Width, bounds.Bottom - y), 9, Ink);
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
                var labels = Wrap(item.Label, labelWidth - 6, 9, false);
                var values = Wrap(item.Value, bounds.Width - labelWidth - 6, 9, false);
                var height = Math.Max(labels.Count, values.Count) * 12 + 6;
                DrawLines(context, pageHeight, labels, new AnalysisReportRect(bounds.X + 3, y + 3, labelWidth - 6, height - 3), 9, Muted, true);
                DrawLines(context, pageHeight, values, new AnalysisReportRect(bounds.X + labelWidth + 3, y + 3, bounds.Width - labelWidth - 6, height - 3), 9, Ink);
                Line(context, bounds.X, pageHeight - y - height, bounds.Right, pageHeight - y - height, Rule, .45f);
                y += height;
            }
        }

        void DrawTable(CGContext context, double pageHeight, AnalysisReportTableBlock table, AnalysisReportLayoutFragment fragment)
        {
            var bounds = fragment.Bounds; var scale = fragment.Scale; var font = 7.5 * scale; var line = 10 * scale; var padding = 3 * scale; var y = bounds.Y;
            if (!string.IsNullOrWhiteSpace(table.Title)) { DrawTextTop(context, pageHeight, table.Title, bounds.X, y, 12 * scale, Ink, true); y += 18 * scale; }
            var columns = Math.Max(1, table.Columns.Count); var width = bounds.Width / columns;
            var headers = table.Columns.Select(column => Wrap(column.Title, width - 2 * padding, font, true)).ToList();
            var headerHeight = Math.Max(1, headers.Select(value => value.Count).DefaultIfEmpty(1).Max()) * line + 2 * padding;
            Fill(context, PdfRect(pageHeight, new AnalysisReportRect(bounds.X, y, bounds.Width, headerHeight)), Header);
            for (var column = 0; column < table.Columns.Count; column++)
                DrawLines(context, pageHeight, headers[column], new AnalysisReportRect(bounds.X + column * width + padding, y + padding, width - 2 * padding, headerHeight), font, Ink, true);
            y += headerHeight;
            foreach (var row in table.Rows.Skip(fragment.FirstItem).Take(fragment.ItemCount))
            {
                var cells = Enumerable.Range(0, table.Columns.Count).Select(column => Wrap(column < row.Cells.Count ? row.Cells[column] : "", width - 2 * padding, font, false)).ToList();
                var height = Math.Max(1, cells.Select(value => value.Count).DefaultIfEmpty(1).Max()) * line + 2 * padding;
                for (var column = 0; column < table.Columns.Count; column++)
                    DrawLines(context, pageHeight, cells[column], new AnalysisReportRect(bounds.X + column * width + padding, y + padding, width - 2 * padding, height), font, Ink);
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

        void DrawContactSheet(CGContext context, double pageHeight, AnalysisReportContactSheetBlock block, AnalysisReportRect bounds)
        {
            var top = bounds.Y;
            if (!string.IsNullOrWhiteSpace(block.Title)) { DrawTextTop(context, pageHeight, block.Title, bounds.X, top, 12, Ink, true); top += 18; }
            var rows = Math.Max(1, block.Rows); var columns = Math.Max(1, block.Columns); const double gap = 5;
            var width = (bounds.Width - gap * (columns - 1)) / columns;
            var height = (bounds.Bottom - top - gap * (rows - 1)) / rows;
            foreach (var cell in block.Cells)
            {
                var cellBounds = new AnalysisReportRect(bounds.X + cell.Column * (width + gap), top + cell.Row * (height + gap), width, height);
                Stroke(context, PdfRect(pageHeight, cellBounds), Rule, .5f);
                DrawTextTop(context, pageHeight, cell.PanelLabel + ". " + cell.ExperimentName, cellBounds.X + 3, cellBounds.Y + 2, 6.5, Ink, true);
                figureRenderer.DrawFigureInRect(context, cell.Figure,
                    PdfRect(pageHeight, new AnalysisReportRect(cellBounds.X + 2, cellBounds.Y + 13, cellBounds.Width - 4, cellBounds.Height - 15)), 5.5f);
            }
        }

        void DrawPlot(CGContext context, double pageHeight, AnalysisReportPlotBlock plot, AnalysisReportRect bounds)
        {
            DrawTextTop(context, pageHeight, plot.Title, bounds.X, bounds.Y, 12, Ink, true);
            var graphTop = bounds.Y + 28; var graphLeft = bounds.X + 48; var graphRight = bounds.Right - 12; var graphBottom = bounds.Bottom - 30;
            var graph = PdfRect(pageHeight, new AnalysisReportRect(graphLeft, graphTop, graphRight - graphLeft, graphBottom - graphTop));
            var points = plot.Series.SelectMany(series => series.Points).Where(point => Finite(point.X) && Finite(point.Y)).ToList();
            if (points.Count == 0) return;
            var minX = points.Min(point => point.X); var maxX = points.Max(point => point.X); var minY = points.Min(point => point.Lower ?? point.Y); var maxY = points.Max(point => point.Upper ?? point.Y);
            Expand(ref minX, ref maxX); Expand(ref minY, ref maxY); Stroke(context, graph, Ink, .8f);
            DrawTextTop(context, pageHeight, plot.XAxisTitle, graphLeft, graphBottom + 10, 8, Ink);
            DrawTextTop(context, pageHeight, plot.YAxisTitle, bounds.X, graphTop, 8, Ink);
            foreach (var series in plot.Series)
            {
                var values = series.Points.Where(point => Finite(point.X) && Finite(point.Y)).OrderBy(point => point.X).ToList();
                var bandValues = values.Where(point => point.Lower.HasValue && point.Upper.HasValue && Finite(point.Lower.Value) && Finite(point.Upper.Value)).ToList();
                if (bandValues.Count > 1)
                {
                    using (var band = new CGPath())
                    {
                        band.MoveToPoint(X(bandValues[0].X), Y(bandValues[0].Upper.Value));
                        foreach (var point in bandValues.Skip(1)) band.AddLineToPoint(X(point.X), Y(point.Upper.Value));
                        foreach (var point in bandValues.AsEnumerable().Reverse()) band.AddLineToPoint(X(point.X), Y(point.Lower.Value));
                        band.CloseSubpath();
                        context.SaveState(); context.SetFillColor(PlotBand); context.AddPath(band); context.FillPath(); context.RestoreState();
                    }
                }
                if (series.Kind == AnalysisReportPlotSeriesKind.Line && values.Count > 1)
                {
                    context.SaveState(); context.SetStrokeColor(PlotBlue); context.SetLineWidth(1.4f);
                    context.MoveTo(X(values[0].X), Y(values[0].Y)); foreach (var point in values.Skip(1)) context.AddLineToPoint(X(point.X), Y(point.Y)); context.StrokePath(); context.RestoreState();
                }
                else foreach (var point in values)
                {
                    if (point.Lower.HasValue && point.Upper.HasValue) Line(context, X(point.X), Y(point.Lower.Value), X(point.X), Y(point.Upper.Value), PlotBlue, .8f);
                    context.SetFillColor(PlotBlue); context.FillEllipseInRect(new CGRect(X(point.X) - 2.5, Y(point.Y) - 2.5, 5, 5));
                }
            }
            nfloat X(double value) => graph.X + (nfloat)((value - minX) / (maxX - minX)) * graph.Width;
            nfloat Y(double value) => graph.Y + (nfloat)((value - minY) / (maxY - minY)) * graph.Height;
        }

        void DrawCorrelation(CGContext context, double pageHeight, AnalysisReportCorrelationMatrixBlock matrix, AnalysisReportRect bounds)
        {
            DrawTextTop(context, pageHeight, matrix.Title, bounds.X, bounds.Y, 12, Ink, true);
            if (matrix.Matrix == null) return;
            var count = Math.Min(matrix.Labels.Count, Math.Min(matrix.Matrix.GetLength(0), matrix.Matrix.GetLength(1))); if (count == 0) return;
            var labelWidth = Math.Min(bounds.Width * .35, matrix.Labels.Select(label => (double)Measure(label, 6.5, false).Width).DefaultIfEmpty(52).Max() + 10);
            var cell = Math.Max(8, Math.Min(bounds.Width - labelWidth - 10, bounds.Height - 60) / count); var left = bounds.X + labelWidth; var top = bounds.Y + 44;
            for (var row = 0; row < count; row++)
            {
                DrawTextTop(context, pageHeight, matrix.Labels[row], bounds.X, top + row * cell + cell * .3, Math.Min(6.5, cell * .22), Ink);
                for (var column = 0; column < count; column++)
                {
                    var value = matrix.Matrix[row, column]; var square = new AnalysisReportRect(left + column * cell, top + row * cell, cell, cell);
                    Fill(context, PdfRect(pageHeight, square), CorrelationColor(value)); Stroke(context, PdfRect(pageHeight, square), NSColor.White.CGColor, .4f);
                    var text = Finite(value) ? value.ToString("0.00", CultureInfo.InvariantCulture) : "-";
                    DrawTextTop(context, pageHeight, text, square.X + 2, square.Y + cell * .3, Math.Min(7, cell * .22), Math.Abs(value) > .65 ? NSColor.White.CGColor : Ink);
                }
            }
        }

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

        static void DrawTextTop(CGContext context, double pageHeight, string text, double x, double top, double size, CGColor color, bool bold = false)
        {
            using (var font = new CTFont(bold ? "Helvetica Neue Medium" : "Helvetica Neue Light", (nfloat)size))
            using (var attributed = new NSAttributedString(text ?? "", new CTStringAttributes { Font = font, ForegroundColorFromContext = true }))
            using (var line = new CTLine(attributed))
            {
                context.SaveState(); context.SetFillColor(color); context.TextPosition = new CGPoint(x, pageHeight - top - size); line.Draw(context); context.RestoreState();
            }
        }

        static CGSize Measure(string text, double size, bool bold)
        {
            using (var font = new CTFont(bold ? "Helvetica Neue Medium" : "Helvetica Neue Light", (nfloat)size))
            using (var attributed = new NSAttributedString(text ?? "", new CTStringAttributes { Font = font }))
            using (var line = new CTLine(attributed))
                return new CGSize((nfloat)line.GetTypographicBounds(), size * 1.2);
        }

        static CGRect PdfRect(double pageHeight, AnalysisReportRect rect) => new CGRect(rect.X, pageHeight - rect.Bottom, rect.Width, rect.Height);
        static void Fill(CGContext context, CGRect rect, CGColor color) { context.SetFillColor(color); context.FillRect(rect); }
        static void Stroke(CGContext context, CGRect rect, CGColor color, nfloat width) { context.SaveState(); context.SetStrokeColor(color); context.SetLineWidth(width); context.StrokeRect(rect); context.RestoreState(); }
        static void Line(CGContext context, double x1, double y1, double x2, double y2, CGColor color, nfloat width) { context.SaveState(); context.SetStrokeColor(color); context.SetLineWidth(width); context.MoveTo((nfloat)x1, (nfloat)y1); context.AddLineToPoint((nfloat)x2, (nfloat)y2); context.StrokePath(); context.RestoreState(); }
        static CGColor CorrelationColor(double value) { if (!Finite(value)) return NSColor.FromRgb(230, 230, 230).CGColor; var amount = (int)Math.Round(225 - Math.Min(1, Math.Abs(value)) * 145); return value < 0 ? NSColor.FromRgb(amount, amount, 235).CGColor : NSColor.FromRgb(235, amount, amount).CGColor; }
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
