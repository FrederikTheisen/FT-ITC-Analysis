using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.Core.Presentation
{
    public readonly struct AnalysisReportSize
    {
        public AnalysisReportSize(double width, double height)
        {
            Width = width;
            Height = height;
        }

        public double Width { get; }
        public double Height { get; }
    }

    public readonly struct AnalysisReportRect
    {
        public AnalysisReportRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public double Right => X + Width;
        public double Bottom => Y + Height;
    }

    public readonly struct AnalysisReportTextStyle
    {
        public AnalysisReportTextStyle(double fontSize, bool bold = false, bool italic = false)
        {
            FontSize = fontSize;
            Bold = bold;
            Italic = italic;
        }

        public double FontSize { get; }
        public bool Bold { get; }
        public bool Italic { get; }
    }

    /// <summary>
    /// Platform text metrics used by the shared paginator. Coordinates and sizes are PDF points.
    /// </summary>
    public interface IAnalysisReportTextMeasurer
    {
        AnalysisReportSize Measure(string text, AnalysisReportTextStyle style);
    }

    public enum AnalysisReportFragmentKind
    {
        SectionTitle,
        Heading,
        Text,
        Notice,
        KeyValueRows,
        TableRows,
        PublicationFigure,
        ContactSheet,
        CartesianPlot,
        CorrelationMatrix,
    }

    public sealed class AnalysisReportLayoutFragment
    {
        internal AnalysisReportLayoutFragment(
            AnalysisReportFragmentKind kind,
            AnalysisReportBlock block,
            AnalysisReportRect bounds,
            double scale = 1,
            int firstItem = 0,
            int itemCount = 0,
            bool repeatTableHeader = false,
            IEnumerable<string> lines = null)
        {
            Kind = kind;
            Block = block;
            Bounds = bounds;
            Scale = scale;
            FirstItem = firstItem;
            ItemCount = itemCount;
            RepeatTableHeader = repeatTableHeader;
            Lines = (lines ?? Enumerable.Empty<string>()).ToList();
        }

        public AnalysisReportFragmentKind Kind { get; }
        public AnalysisReportBlock Block { get; }
        public AnalysisReportRect Bounds { get; }
        public double Scale { get; }
        public int FirstItem { get; }
        public int ItemCount { get; }
        public bool RepeatTableHeader { get; }
        public IReadOnlyList<string> Lines { get; }
    }

    public sealed class AnalysisReportPagePlan
    {
        readonly List<AnalysisReportLayoutFragment> fragments = new List<AnalysisReportLayoutFragment>();

        internal AnalysisReportPagePlan(int pageNumber, double width, double height, string reportTitle)
        {
            PageNumber = pageNumber;
            Width = width;
            Height = height;
            ReportTitle = reportTitle ?? "";
        }

        public int PageNumber { get; }
        public double Width { get; }
        public double Height { get; }
        public string ReportTitle { get; }
        public bool IsCover => PageNumber == 1;
        public IReadOnlyList<AnalysisReportLayoutFragment> Fragments => fragments;

        internal void Add(AnalysisReportLayoutFragment fragment) => fragments.Add(fragment);
    }

    public sealed class AnalysisReportLayoutPlan
    {
        internal AnalysisReportLayoutPlan(
            double pageWidth,
            double pageHeight,
            double marginLeft,
            double marginTop,
            double marginRight,
            double marginBottom,
            IEnumerable<AnalysisReportPagePlan> pages)
        {
            PageWidth = pageWidth;
            PageHeight = pageHeight;
            MarginLeft = marginLeft;
            MarginTop = marginTop;
            MarginRight = marginRight;
            MarginBottom = marginBottom;
            Pages = pages.ToList();
        }

        public double PageWidth { get; }
        public double PageHeight { get; }
        public double MarginLeft { get; }
        public double MarginTop { get; }
        public double MarginRight { get; }
        public double MarginBottom { get; }
        public IReadOnlyList<AnalysisReportPagePlan> Pages { get; }
    }

    /// <summary>
    /// Resolves report content into printable A4 pages without drawing platform graphics.
    /// </summary>
    public static class AnalysisReportLayoutEngine
    {
        public const double PointsPerCentimeter = 72.0 / 2.54;
        const double BlockSpacing = 9;
        const double FooterHeight = 18;
        const double CellPadding = 3;
        static readonly AnalysisReportTextStyle SectionStyle = new AnalysisReportTextStyle(17, true);
        static readonly AnalysisReportTextStyle HeadingStyle = new AnalysisReportTextStyle(12, true);
        static readonly AnalysisReportTextStyle BodyStyle = new AnalysisReportTextStyle(9);
        static readonly AnalysisReportTextStyle SmallStyle = new AnalysisReportTextStyle(7.5);
        static readonly AnalysisReportTextStyle SmallBoldStyle = new AnalysisReportTextStyle(7.5, true);

        public static AnalysisReportLayoutPlan Paginate(
            AnalysisReportDocument document,
            IAnalysisReportTextMeasurer measurer)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (measurer == null) throw new ArgumentNullException(nameof(measurer));
            if (!document.IsValid)
                throw new InvalidOperationException("A report containing validation errors cannot be paginated.");

            var settings = document.PageSettings;
            var pageWidth = settings.WidthCentimeters * PointsPerCentimeter;
            var pageHeight = settings.HeightCentimeters * PointsPerCentimeter;
            var left = settings.MarginLeftCentimeters * PointsPerCentimeter;
            var top = settings.MarginTopCentimeters * PointsPerCentimeter;
            var right = settings.MarginRightCentimeters * PointsPerCentimeter;
            var bottom = settings.MarginBottomCentimeters * PointsPerCentimeter;
            var pages = new List<AnalysisReportPagePlan>();
            var state = new State(document, measurer, pages, pageWidth, pageHeight,
                left, top, right, bottom + FooterHeight);

            foreach (var section in document.Sections)
            {
                if (pages.Count == 0
                    || section.Layout.HasFlag(AnalysisReportLayoutPolicy.StartOnNewPage))
                    state.NewPage();

                state.PlaceSectionTitle(section.Title);
                foreach (var block in section.Blocks)
                    state.Place(block, section.Kind == AnalysisReportSectionKind.Cover);
            }

            return new AnalysisReportLayoutPlan(pageWidth, pageHeight, left, top, right, bottom, pages);
        }

        sealed class State
        {
            readonly AnalysisReportDocument document;
            readonly IAnalysisReportTextMeasurer measurer;
            readonly List<AnalysisReportPagePlan> pages;
            readonly double pageWidth;
            readonly double pageHeight;
            readonly double left;
            readonly double top;
            readonly double right;
            readonly double bottom;
            AnalysisReportPagePlan page;
            double y;

            public State(AnalysisReportDocument document, IAnalysisReportTextMeasurer measurer,
                List<AnalysisReportPagePlan> pages, double pageWidth, double pageHeight,
                double left, double top, double right, double bottom)
            {
                this.document = document;
                this.measurer = measurer;
                this.pages = pages;
                this.pageWidth = pageWidth;
                this.pageHeight = pageHeight;
                this.left = left;
                this.top = top;
                this.right = right;
                this.bottom = bottom;
            }

            double ContentWidth => pageWidth - left - right;
            double ContentBottom => pageHeight - bottom;
            double Remaining => ContentBottom - y;

            public void NewPage()
            {
                page = new AnalysisReportPagePlan(pages.Count + 1, pageWidth, pageHeight, document.Title);
                pages.Add(page);
                y = top;
            }

            public void PlaceSectionTitle(string title)
            {
                var lines = Wrap(title, ContentWidth, SectionStyle);
                var height = Math.Max(LineHeight(SectionStyle), lines.Count * LineHeight(SectionStyle));
                Ensure(height + BlockSpacing);
                Add(AnalysisReportFragmentKind.SectionTitle, null, height, lines: lines);
            }

            public void Place(AnalysisReportBlock block, bool cover)
            {
                if (block == null) return;
                if (block is AnalysisReportHeadingBlock heading) { PlaceHeading(heading); return; }
                if (block is AnalysisReportTextBlock text) { PlaceText(text); return; }
                if (block is AnalysisReportNoticeBlock notice) { PlaceNotice(notice); return; }
                if (block is AnalysisReportKeyValueBlock keyValues) { PlaceKeyValues(keyValues); return; }
                if (block is AnalysisReportTableBlock table) { PlaceTable(table); return; }
                if (block is AnalysisReportFigureBlock figure) { PlaceFigure(figure); return; }
                if (block is AnalysisReportContactSheetBlock contact) { PlaceContactSheet(contact, cover); return; }
                if (block is AnalysisReportPlotBlock plot) { PlacePlot(plot); return; }
                if (block is AnalysisReportCorrelationMatrixBlock matrix) PlaceCorrelation(matrix);
            }

            void PlaceHeading(AnalysisReportHeadingBlock block)
            {
                var style = block.Level == 1 ? SectionStyle : HeadingStyle;
                var lines = Wrap(block.Text, ContentWidth, style);
                var height = lines.Count * LineHeight(style);
                Ensure(height + BlockSpacing);
                Add(AnalysisReportFragmentKind.Heading, block, height, lines: lines);
            }

            void PlaceText(AnalysisReportTextBlock block)
            {
                var titleHeight = TitleHeight(block.Title);
                var lines = Wrap(block.Text, ContentWidth, BodyStyle);
                var lineHeight = LineHeight(BodyStyle);
                var needed = titleHeight + Math.Max(1, lines.Count) * lineHeight + 2 * CellPadding;
                if (block.Layout.HasFlag(AnalysisReportLayoutPolicy.KeepTogether) && needed <= UsableHeight())
                {
                    Ensure(needed + BlockSpacing);
                    Add(AnalysisReportFragmentKind.Text, block, needed, lines: lines);
                    return;
                }

                var first = 0;
                while (first < lines.Count || (lines.Count == 0 && first == 0))
                {
                    if (Remaining < titleHeight + lineHeight + 2 * CellPadding) NewPage();
                    var room = Math.Max(1, (int)Math.Floor((Remaining - titleHeight - 2 * CellPadding) / lineHeight));
                    var count = lines.Count == 0 ? 0 : Math.Min(room, lines.Count - first);
                    var fragmentLines = lines.Skip(first).Take(count).ToList();
                    var height = titleHeight + Math.Max(1, count) * lineHeight + 2 * CellPadding;
                    Add(AnalysisReportFragmentKind.Text, block, height, first: first, count: count,
                        lines: fragmentLines);
                    if (lines.Count == 0) break;
                    first += count;
                    if (first < lines.Count) NewPage();
                }
            }

            void PlaceNotice(AnalysisReportNoticeBlock block)
            {
                var lines = Wrap(block.Message, ContentWidth - 2 * CellPadding, BodyStyle);
                var height = TitleHeight(block.Title) + Math.Max(1, lines.Count) * LineHeight(BodyStyle) + 4 * CellPadding;
                Ensure(height + BlockSpacing);
                Add(AnalysisReportFragmentKind.Notice, block, height, lines: lines);
            }

            void PlaceKeyValues(AnalysisReportKeyValueBlock block)
            {
                var rowHeights = block.Items.Select(item => KeyValueRowHeight(item, 1)).ToList();
                var total = TitleHeight(block.Title) + rowHeights.Sum();
                if (block.Layout.HasFlag(AnalysisReportLayoutPolicy.KeepTogether) && total <= UsableHeight())
                    Ensure(total + BlockSpacing);

                var first = 0;
                while (first < block.Items.Count)
                {
                    var titleHeight = TitleHeight(block.Title);
                    if (Remaining < titleHeight + rowHeights[first]) NewPage();
                    var available = Remaining - titleHeight;
                    var count = 0;
                    var height = titleHeight;
                    while (first + count < rowHeights.Count && rowHeights[first + count] <= available)
                    {
                        height += rowHeights[first + count];
                        available -= rowHeights[first + count];
                        count++;
                    }
                    if (count == 0) count = 1;
                    Add(AnalysisReportFragmentKind.KeyValueRows, block, height,
                        first: first, count: count);
                    first += count;
                    if (first < block.Items.Count) NewPage();
                }
            }

            void PlaceTable(AnalysisReportTableBlock block)
            {
                var scale = 1.0;
                var fullHeight = TableHeight(block, scale, 0, block.Rows.Count);
                if (block.Layout.HasFlag(AnalysisReportLayoutPolicy.ShrinkToSinglePage))
                {
                    var available = Math.Max(1, Remaining);
                    if (fullHeight > available && fullHeight <= UsableHeight())
                    {
                        NewPage();
                        available = Remaining;
                    }
                    // A shrink-to-single-page table must retain every row and column.
                    // Prefer legibility, but allow unusually large tables to scale far
                    // enough that they still honor the unsplittable layout contract.
                    if (fullHeight > available) scale = Math.Max(0.01, available / fullHeight);
                    Add(AnalysisReportFragmentKind.TableRows, block,
                        Math.Min(available, TableHeight(block, scale, 0, block.Rows.Count)),
                        scale, 0, block.Rows.Count, true);
                    return;
                }

                var rowHeights = TableRowHeights(block, scale);
                var first = 0;
                do
                {
                    var fixedHeight = TitleHeight(block.Title, scale) + TableHeaderHeight(block, scale);
                    if (Remaining < fixedHeight + (rowHeights.Count > 0 ? rowHeights[first] : 0)) NewPage();
                    var available = Remaining - fixedHeight;
                    var count = 0;
                    var height = fixedHeight;
                    while (first + count < rowHeights.Count && rowHeights[first + count] <= available)
                    {
                        height += rowHeights[first + count];
                        available -= rowHeights[first + count];
                        count++;
                    }
                    if (rowHeights.Count > 0 && count == 0) count = 1;
                    Add(AnalysisReportFragmentKind.TableRows, block, height, scale, first, count, true);
                    first += count;
                    if (first < rowHeights.Count) NewPage();
                } while (first < rowHeights.Count);
            }

            void PlaceFigure(AnalysisReportFigureBlock block)
            {
                var aspect = FigureAspect(block.Figure);
                var width = ContentWidth;
                var height = Math.Min(UsableHeight(), width / aspect + TitleHeight(block.Title));
                Ensure(height + BlockSpacing);
                if (height > Remaining) height = Remaining;
                Add(AnalysisReportFragmentKind.PublicationFigure, block, height);
            }

            void PlaceContactSheet(AnalysisReportContactSheetBlock block, bool cover)
            {
                var height = cover ? Remaining : Math.Min(Remaining, UsableHeight() * .75);
                if (height < 100 && !cover) { NewPage(); height = Math.Min(Remaining, UsableHeight() * .75); }
                Add(AnalysisReportFragmentKind.ContactSheet, block, Math.Max(1, height),
                    Math.Min(1, Math.Max(.1, height / (ContentWidth * 0.8))));
            }

            void PlacePlot(AnalysisReportPlotBlock block)
            {
                var height = Math.Min(UsableHeight() * .64, ContentWidth * .68);
                Ensure(height + BlockSpacing);
                Add(AnalysisReportFragmentKind.CartesianPlot, block, height);
            }

            void PlaceCorrelation(AnalysisReportCorrelationMatrixBlock block)
            {
                var height = Math.Min(UsableHeight() * .78, ContentWidth + TitleHeight(block.Title));
                Ensure(height + BlockSpacing);
                Add(AnalysisReportFragmentKind.CorrelationMatrix, block, Math.Min(height, Remaining));
            }

            void Ensure(double height)
            {
                if (page == null) NewPage();
                if (height > Remaining && y > top) NewPage();
            }

            void Add(AnalysisReportFragmentKind kind, AnalysisReportBlock block, double height,
                double scale = 1, int first = 0, int count = 0, bool repeatHeader = false,
                IEnumerable<string> lines = null)
            {
                height = Math.Max(1, Math.Min(height, Remaining));
                page.Add(new AnalysisReportLayoutFragment(kind, block,
                    new AnalysisReportRect(left, y, ContentWidth, height), scale,
                    first, count, repeatHeader, lines));
                y += height + BlockSpacing;
            }

            double TitleHeight(string title, double scale = 1) => string.IsNullOrWhiteSpace(title)
                ? 0 : LineHeight(new AnalysisReportTextStyle(HeadingStyle.FontSize * scale, true)) + 3;

            double KeyValueRowHeight(AnalysisReportKeyValueItem item, double scale)
            {
                var style = new AnalysisReportTextStyle(BodyStyle.FontSize * scale);
                var labelLines = Wrap(item.Label, ContentWidth * .30 - CellPadding, style).Count;
                var valueLines = Wrap(item.Value, ContentWidth * .68 - CellPadding, style).Count;
                return Math.Max(1, Math.Max(labelLines, valueLines)) * LineHeight(style) + 2 * CellPadding;
            }

            List<double> TableRowHeights(AnalysisReportTableBlock table, double scale)
            {
                var style = new AnalysisReportTextStyle(SmallStyle.FontSize * scale);
                var width = Math.Max(12, ContentWidth / Math.Max(1, table.Columns.Count) - 2 * CellPadding);
                return table.Rows.Select(row =>
                    Math.Max(1, row.Cells.Select(cell => Wrap(cell, width, style).Count).DefaultIfEmpty(1).Max())
                    * LineHeight(style) + 2 * CellPadding * scale).ToList();
            }

            double TableHeaderHeight(AnalysisReportTableBlock table, double scale)
            {
                var style = new AnalysisReportTextStyle(SmallBoldStyle.FontSize * scale, true);
                var width = Math.Max(12, ContentWidth / Math.Max(1, table.Columns.Count) - 2 * CellPadding);
                var lines = table.Columns.Select(column => Wrap(column.Title, width, style).Count).DefaultIfEmpty(1).Max();
                return Math.Max(1, lines) * LineHeight(style) + 2 * CellPadding * scale;
            }

            double TableHeight(AnalysisReportTableBlock table, double scale, int first, int count) =>
                TitleHeight(table.Title, scale) + TableHeaderHeight(table, scale)
                + TableRowHeights(table, scale).Skip(first).Take(count).Sum();

            double FigureAspect(PublicationFigureDocument figure)
            {
                if (figure == null || figure.PlotHeight <= 0) return .75;
                return Math.Max(.25, figure.PlotWidth / figure.PlotHeight);
            }

            double LineHeight(AnalysisReportTextStyle style)
            {
                var measured = measurer.Measure("Ag", style).Height;
                return Math.Max(style.FontSize * 1.2, measured * 1.2);
            }

            double UsableHeight() => pageHeight - top - bottom;

            List<string> Wrap(string text, double width, AnalysisReportTextStyle style)
            {
                var output = new List<string>();
                foreach (var paragraph in (text ?? "").Replace("\r\n", "\n").Split('\n'))
                {
                    if (paragraph.Length == 0) { output.Add(""); continue; }
                    var words = paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var current = "";
                    foreach (var word in words)
                    {
                        if (measurer.Measure(word, style).Width > width)
                        {
                            if (current.Length > 0) { output.Add(current); current = ""; }
                            var part = "";
                            foreach (var character in word)
                            {
                                var candidatePart = part + character;
                                if (part.Length > 0 && measurer.Measure(candidatePart, style).Width > width)
                                {
                                    output.Add(part);
                                    part = character.ToString();
                                }
                                else part = candidatePart;
                            }
                            current = part;
                            continue;
                        }
                        var candidate = current.Length == 0 ? word : current + " " + word;
                        if (current.Length > 0 && measurer.Measure(candidate, style).Width > width)
                        {
                            output.Add(current);
                            current = word;
                        }
                        else current = candidate;
                    }
                    output.Add(current);
                }
                return output;
            }
        }
    }
}
