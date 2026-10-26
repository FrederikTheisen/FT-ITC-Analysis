using System;
using System.Collections.Generic;
using System.Globalization;
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
        public AnalysisReportTextStyle(double fontSize, bool bold = false, bool italic = false, bool light = false)
        {
            FontSize = fontSize;
            Bold = bold;
            Italic = italic;
            Light = light;
        }

        public double FontSize { get; }
        public bool Bold { get; }
        public bool Italic { get; }
        public bool Light { get; }
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
        PublicationFigurePair,
        FigureCanvas,
        CartesianPlot,
        ThermodynamicSummary,
        CorrelationMatrix,
        TableOfContents,
        SignOff,
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
            IEnumerable<string> lines = null,
            AnalysisReportSection section = null,
            AnalysisReportTableLayout tableLayout = null,
            AnalysisReportSignOffLayout signOffLayout = null,
            AnalysisReportHeadingLayout headingLayout = null,
            AnalysisReportKeyValueLayout keyValueLayout = null, bool isContinuation = false)
        {
            Kind = kind;
            Block = block;
            Bounds = bounds;
            Scale = scale;
            FirstItem = firstItem;
            ItemCount = itemCount;
            RepeatTableHeader = repeatTableHeader;
            Lines = (lines ?? Enumerable.Empty<string>()).ToList();
            Section = section;
            TableLayout = tableLayout;
            SignOffLayout = signOffLayout;
            HeadingLayout = headingLayout;
            KeyValueLayout = keyValueLayout;
            IsContinuation = isContinuation || firstItem > 0;
            // A key-value continuation can also start partway through item zero.
            Title = string.IsNullOrWhiteSpace(block?.Title) ? ""
                : IsContinuation ? block.Title + ContinuedSuffix : block.Title;
        }

        public const string ContinuedSuffix = " – continued";

        public AnalysisReportFragmentKind Kind { get; }
        /// <summary>The block title, marked as continued on fragments after a page break.</summary>
        public string Title { get; }
        public AnalysisReportBlock Block { get; }
        public AnalysisReportRect Bounds { get; }
        public double Scale { get; }
        public int FirstItem { get; }
        public int ItemCount { get; }
        public bool RepeatTableHeader { get; }
        public IReadOnlyList<string> Lines { get; }
        public AnalysisReportSection Section { get; }
        public AnalysisReportTableLayout TableLayout { get; }
        public AnalysisReportSignOffLayout SignOffLayout { get; }
        public AnalysisReportHeadingLayout HeadingLayout { get; }
        public AnalysisReportKeyValueLayout KeyValueLayout { get; }
        public bool IsContinuation { get; }
    }

    public sealed class AnalysisReportKeyValueRowLayout
    {
        internal AnalysisReportKeyValueRowLayout(int itemIndex, int indentLevel, int firstLine,
            IEnumerable<string> labels, IEnumerable<string> values, int lineCount)
        {
            ItemIndex = itemIndex;
            IndentLevel = indentLevel;
            FirstLine = firstLine;
            LabelLines = labels.ToList();
            ValueLines = values.ToList();
            Height = lineCount * AnalysisReportKeyValueLayout.LineHeight + 2 * AnalysisReportKeyValueLayout.RowPadding;
        }
        public int ItemIndex { get; }
        public int IndentLevel { get; }
        public int FirstLine { get; }
        public IReadOnlyList<string> LabelLines { get; }
        public IReadOnlyList<string> ValueLines { get; }
        public double Height { get; }
    }

    public sealed class AnalysisReportKeyValueLayout
    {
        public const double LineHeight = 12;
        public const double RowPadding = 2;
        public const double HeadingHeight = 18;
        internal AnalysisReportKeyValueLayout(double labelWidth, IEnumerable<AnalysisReportKeyValueRowLayout> rows)
        {
            LabelWidth = labelWidth;
            Rows = rows.ToList();
        }
        public double LabelWidth { get; }
        public IReadOnlyList<AnalysisReportKeyValueRowLayout> Rows { get; }
    }

    public sealed class AnalysisReportTableRowLayout
    {
        internal AnalysisReportTableRowLayout(IEnumerable<IReadOnlyList<string>> cellLines, double height)
        {
            CellLines = cellLines.ToList();
            Height = height;
        }
        public IReadOnlyList<IReadOnlyList<string>> CellLines { get; }
        public double Height { get; }
    }

    public sealed class AnalysisReportTableLayout
    {
        internal AnalysisReportTableLayout(double[] widths, IEnumerable<IReadOnlyList<string>> headerLines,
            double headerHeight, IEnumerable<AnalysisReportTableRowLayout> rows)
        {
            ColumnWidths = widths;
            HeaderCellLines = headerLines.ToList();
            HeaderHeight = headerHeight;
            Rows = rows.ToList();
        }
        public IReadOnlyList<double> ColumnWidths { get; }
        public IReadOnlyList<IReadOnlyList<string>> HeaderCellLines { get; }
        public double HeaderHeight { get; }
        public IReadOnlyList<AnalysisReportTableRowLayout> Rows { get; }
    }

    public sealed class AnalysisReportPagePlan
    {
        readonly List<AnalysisReportLayoutFragment> fragments = new List<AnalysisReportLayoutFragment>();

        internal AnalysisReportPagePlan(int pageNumber, double width, double height,
            string reportTitle, string resultName = "", string experimentLabel = "", string experimentName = "")
        {
            PageNumber = pageNumber;
            Width = width;
            Height = height;
            ReportTitle = reportTitle ?? "";
            ResultName = resultName ?? "";
            ExperimentLabel = experimentLabel ?? "";
            ExperimentName = experimentName ?? "";
        }

        public int PageNumber { get; }
        public double Width { get; }
        public double Height { get; }
        public string ReportTitle { get; }
        public string ResultName { get; }
        public string ExperimentLabel { get; }
        public string ExperimentName { get; }
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
        static readonly AnalysisReportTextStyle SubheadingStyle = new AnalysisReportTextStyle(10.5, true);
        static readonly AnalysisReportTextStyle BodyStyle = new AnalysisReportTextStyle(9);

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

            var renderedIds = new HashSet<string>(document.Sections
                .Where(section => section.Blocks.Any(AnalysisReportSection.HasContent)).Select(section => section.Id));
            foreach (var contents in document.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportTableOfContentsBlock>())
                contents.PruneEntries(renderedIds);

            foreach (var section in document.Sections.Where(section => section.Blocks.Any(AnalysisReportSection.HasContent)))
            {
                state.BeginSection(section);
                if (pages.Count == 0
                    || section.Layout.HasFlag(AnalysisReportLayoutPolicy.StartOnNewPage))
                    state.NewPage();

                state.ReserveCoverSignOff(section);
                state.PlaceSectionTitle(section);
                foreach (var block in section.Blocks.Where(AnalysisReportSection.HasContent))
                    state.Place(block, section.Kind == AnalysisReportSectionKind.Cover);
                state.PlaceCoverSignOff();
            }

            state.ResolveTableOfContents();

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
            AnalysisReportSection currentSection;
            AnalysisReportPagePlan signOffPage;
            AnalysisReportSignOffBlock signOffBlock;
            AnalysisReportSignOffLayout signOffLayout;
            readonly Dictionary<string, int> sectionPages = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly List<AnalysisReportTableOfContentsBlock> contentsBlocks = new List<AnalysisReportTableOfContentsBlock>();

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
            double ContentBottom => pageHeight - bottom
                - (signOffLayout != null && page == signOffPage ? signOffLayout.Height + BlockSpacing : 0);
            double Remaining => ContentBottom - y;

            public void NewPage()
            {
                page = new AnalysisReportPagePlan(pages.Count + 1, pageWidth, pageHeight,
                    document.Title, currentSection?.ResultName,
                    currentSection?.ExperimentLabel, currentSection?.ExperimentName);
                pages.Add(page);
                y = top;
            }

            public void BeginSection(AnalysisReportSection section) => currentSection = section;

            public void ReserveCoverSignOff(AnalysisReportSection section)
            {
                if (section.Kind != AnalysisReportSectionKind.Cover) return;
                signOffBlock = section.Blocks.OfType<AnalysisReportSignOffBlock>().SingleOrDefault();
                if (signOffBlock == null) return;
                signOffLayout = AnalysisReportSignOffLayout.Create(signOffBlock, ContentWidth,
                    (text, width) => Wrap(text, width, BodyStyle));
                signOffPage = page;
            }

            public void PlaceCoverSignOff()
            {
                if (signOffBlock == null) return;
                var bounds = new AnalysisReportRect(left, pageHeight - bottom - signOffLayout.Height,
                    ContentWidth, signOffLayout.Height);
                signOffPage.Add(new AnalysisReportLayoutFragment(AnalysisReportFragmentKind.SignOff,
                    signOffBlock, bounds, signOffLayout: signOffLayout));
                if (page == signOffPage) y = bounds.Bottom + BlockSpacing;
                signOffPage = null;
                signOffBlock = null;
                signOffLayout = null;
            }

            public void PlaceSectionTitle(AnalysisReportSection section)
            {
                var health = section.Kind == AnalysisReportSectionKind.Cover ? document.ResultHealth : section.StatusBadgeHealth;
                var badge = health.HasValue ? AnalysisReportStatusBadgeLayout.Create(health.Value,
                    section.Kind == AnalysisReportSectionKind.Cover ? document.StatusBadgeText
                        : AnalysisReportStatusBadgeLayout.TextFor(health.Value), ContentWidth, measurer) : null;
                if (badge != null || !string.IsNullOrWhiteSpace(section.Subtitle))
                {
                    var layout = AnalysisReportHeadingLayout.Create(section.Title, section.Subtitle, ContentWidth,
                        SectionStyle, measurer, Wrap, badge);
                    Ensure(layout.Height + BlockSpacing);
                    if (!string.IsNullOrWhiteSpace(section.Id)) sectionPages[section.Id] = page.PageNumber;
                    Add(AnalysisReportFragmentKind.SectionTitle, null, layout.Height,
                        lines: layout.TitleLines, section: section, headingLayout: layout);
                    return;
                }
                var lines = Wrap(section.Title, ContentWidth, SectionStyle);
                var height = Math.Max(LineHeight(SectionStyle), lines.Count * LineHeight(SectionStyle));
                Ensure(height + BlockSpacing);
                if (!string.IsNullOrWhiteSpace(section.Id)) sectionPages[section.Id] = page.PageNumber;
                Add(AnalysisReportFragmentKind.SectionTitle, null, height,
                    lines: lines, section: section);
            }

            public void ResolveTableOfContents()
            {
                foreach (var block in contentsBlocks)
                    foreach (var entry in block.Entries)
                        entry.PageNumber = sectionPages.TryGetValue(entry.TargetSectionId, out var number)
                            ? number : 0;
            }

            public void Place(AnalysisReportBlock block, bool cover)
            {
                if (block == null) return;
                if (block is AnalysisReportSignOffBlock) return; // Placed once, in the cover's reserved bottom area.
                if (block is AnalysisReportHeadingBlock heading) { PlaceHeading(heading); return; }
                if (block is AnalysisReportTextBlock text) { PlaceText(text); return; }
                if (block is AnalysisReportNoticeBlock notice) { PlaceNotice(notice); return; }
                if (block is AnalysisReportKeyValueBlock keyValues) { PlaceKeyValues(keyValues); return; }
                if (block is AnalysisReportTableBlock table) { PlaceTable(table); return; }
                if (block is AnalysisReportFigureBlock figure) { PlaceFigure(figure); return; }
                if (block is AnalysisReportFigurePairBlock pair) { PlaceFigurePair(pair); return; }
                if (block is AnalysisReportFigureCanvasBlock canvas) { PlaceFigureCanvas(canvas, cover); return; }
                if (block is AnalysisReportPlotBlock plot) { PlacePlot(plot); return; }
                if (block is AnalysisReportThermodynamicSummaryBlock summary) { PlaceThermodynamicSummary(summary); return; }
                if (block is AnalysisReportCorrelationMatrixBlock matrix) { PlaceCorrelation(matrix); return; }
                if (block is AnalysisReportTableOfContentsBlock contents) PlaceTableOfContents(contents);
            }

            void PlaceTableOfContents(AnalysisReportTableOfContentsBlock block)
            {
                contentsBlocks.Add(block);
                var heights = block.Entries.Select(entry =>
                    Math.Max(1, Wrap(entry.Title, ContentWidth - 42 - entry.Indent, BodyStyle).Count)
                    * LineHeight(BodyStyle) + 5).ToList();
                var first = 0;
                while (first < block.Entries.Count)
                {
                    var titleHeight = TitleHeight(block.Title);
                    if (Remaining < titleHeight + heights[first]) NewPage();
                    var available = Remaining - titleHeight;
                    var count = 0;
                    var height = titleHeight;
                    while (first + count < heights.Count && heights[first + count] <= available)
                    {
                        height += heights[first + count];
                        available -= heights[first + count];
                        count++;
                    }
                    if (count == 0) count = 1;
                    Add(AnalysisReportFragmentKind.TableOfContents, block, height,
                        first: first, count: count);
                    first += count;
                    if (first < block.Entries.Count) NewPage();
                }
            }

            void PlaceHeading(AnalysisReportHeadingBlock block)
            {
                var style = block.Level == 1 ? SectionStyle
                    : block.Level == 3 ? SubheadingStyle : HeadingStyle;
                if (!string.IsNullOrWhiteSpace(block.Subtitle))
                {
                    var layout = CreateHeadingLayout(block.Text, block.Subtitle, style);
                    Ensure(layout.Height + BlockSpacing);
                    Add(AnalysisReportFragmentKind.Heading, block, layout.Height,
                        lines: layout.TitleLines, headingLayout: layout);
                    return;
                }
                var lines = Wrap(block.Text, ContentWidth, style);
                var height = lines.Count * LineHeight(style);
                Ensure(height + BlockSpacing);
                Add(AnalysisReportFragmentKind.Heading, block, height, lines: lines);
            }

            AnalysisReportHeadingLayout CreateHeadingLayout(string title, string subtitle, AnalysisReportTextStyle style) =>
                AnalysisReportHeadingLayout.Create(title, subtitle, ContentWidth, style, measurer, Wrap);

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
                var lines = Wrap(block.Message, ContentWidth - 2 * AnalysisReportNoticeLayout.HorizontalInset,
                    AnalysisReportNoticeLayout.BodyStyle);
                var decorationHeight = 2 * AnalysisReportNoticeLayout.VerticalInset + AnalysisReportNoticeLayout.HeadingHeight(block.Title);
                var needed = AnalysisReportNoticeLayout.Height(block.Title, lines.Count);
                if (needed <= UsableHeight())
                {
                    Ensure(needed + BlockSpacing);
                    Add(AnalysisReportFragmentKind.Notice, block, needed, count: lines.Count, lines: lines);
                    return;
                }
                var first = 0;
                while (first < lines.Count)
                {
                    if (Remaining < AnalysisReportNoticeLayout.Height(block.Title, 1)) NewPage();
                    var room = (int)Math.Floor((Remaining - decorationHeight) / AnalysisReportNoticeLayout.LineHeight + 1e-9);
                    if (room < 1) { NewPage(); continue; }
                    var count = Math.Min(room, lines.Count - first);
                    Add(AnalysisReportFragmentKind.Notice, block, AnalysisReportNoticeLayout.Height(block.Title, count),
                        first: first, count: count, lines: lines.Skip(first).Take(count));
                    first += count;
                    if (first < lines.Count) NewPage();
                }
            }

            void PlaceKeyValues(AnalysisReportKeyValueBlock block)
            {
                var labelWidth = ContentWidth * .30;
                var rowLabels = block.Items.Select(item => Wrap(item.Label,
                    labelWidth - 6 - item.IndentLevel * 14, new AnalysisReportTextStyle(9, true))).ToList();
                var rowValues = block.Items.Select(item => Wrap(item.Value,
                    ContentWidth - labelWidth - 6, BodyStyle)).ToList();
                var lineCounts = block.Items.Select((item, index) =>
                    Math.Max(1, Math.Max(rowLabels[index].Count, rowValues[index].Count))).ToList();
                var titleHeight = string.IsNullOrWhiteSpace(block.Title) ? 0 : AnalysisReportKeyValueLayout.HeadingHeight;
                var total = titleHeight + lineCounts.Sum(count => count * AnalysisReportKeyValueLayout.LineHeight + 2 * AnalysisReportKeyValueLayout.RowPadding);
                if (block.Layout.HasFlag(AnalysisReportLayoutPolicy.KeepTogether) && total <= UsableHeight())
                    Ensure(total + BlockSpacing);

                var first = 0;
                var firstLine = 0;
                while (first < block.Items.Count)
                {
                    var rows = new List<AnalysisReportKeyValueRowLayout>();
                    var fragmentFirst = first;
                    var continuedRow = firstLine > 0;
                    var height = titleHeight;
                    while (first < block.Items.Count)
                    {
                        var count = lineCounts[first] - firstLine;
                        var fullHeight = count * AnalysisReportKeyValueLayout.LineHeight + 2 * AnalysisReportKeyValueLayout.RowPadding;
                        var available = Remaining - height;
                        if (fullHeight > available)
                        {
                            // Ordinary rows move intact. Only a row taller than a fresh page is sliced.
                            if (firstLine == 0 && fullHeight <= UsableHeight() - titleHeight) break;
                            count = Math.Min(count, (int)Math.Floor((available - 2 * AnalysisReportKeyValueLayout.RowPadding) / AnalysisReportKeyValueLayout.LineHeight));
                            if (count <= 0) break;
                        }
                        var row = new AnalysisReportKeyValueRowLayout(first, block.Items[first].IndentLevel, firstLine,
                            rowLabels[first].Skip(firstLine).Take(count), rowValues[first].Skip(firstLine).Take(count), count);
                        rows.Add(row);
                        height += row.Height;
                        firstLine += count;
                        if (firstLine < lineCounts[first]) break;
                        first++;
                        firstLine = 0;
                    }
                    if (rows.Count > 0)
                        Add(AnalysisReportFragmentKind.KeyValueRows, block, height, first: fragmentFirst, count: rows.Count,
                            keyValueLayout: new AnalysisReportKeyValueLayout(labelWidth, rows), isContinuation: continuedRow);
                    if (first < block.Items.Count) NewPage();
                }
            }

            void PlaceTable(AnalysisReportTableBlock block)
            {
                if (block.Layout.HasFlag(AnalysisReportLayoutPolicy.StartOnNewPage)
                    && y > top)
                    NewPage();

                var scale = 1.0;
                var tableLayout = CreateTableLayout(block, scale);
                var fullHeight = TableHeight(block, tableLayout, scale, 0, block.Rows.Count);
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
                    for (var attempt = 0; fullHeight > available && attempt < 80; attempt++)
                    {
                        scale = Math.Max(.001, scale * .9);
                        tableLayout = CreateTableLayout(block, scale);
                        fullHeight = TableHeight(block, tableLayout, scale, 0, block.Rows.Count);
                    }
                    Add(AnalysisReportFragmentKind.TableRows, block,
                        Math.Min(available, fullHeight),
                        scale, 0, block.Rows.Count, true, tableLayout: tableLayout);
                    return;
                }

                var rowHeights = tableLayout.Rows.Select(row => row.Height).ToList();
                var first = 0;
                do
                {
                    var fixedHeight = TableTitleHeight(block.Title, scale) + tableLayout.HeaderHeight;
                    if (Remaining < fixedHeight + (rowHeights.Count > 0 ? rowHeights[first] : 0)) NewPage();
                    if (rowHeights.Count > 0 && Remaining < fixedHeight + rowHeights[first])
                    {
                        for (var attempt = 0; Remaining < fixedHeight + rowHeights[first] && attempt < 80; attempt++)
                        {
                            scale = Math.Max(.001, scale * .9);
                            tableLayout = CreateTableLayout(block, scale);
                            rowHeights = tableLayout.Rows.Select(row => row.Height).ToList();
                            fixedHeight = TableTitleHeight(block.Title, scale) + tableLayout.HeaderHeight;
                        }
                    }
                    var available = Remaining - fixedHeight;
                    var count = 0;
                    var height = fixedHeight;
                    while (first + count < rowHeights.Count && rowHeights[first + count] <= available)
                    {
                        height += rowHeights[first + count];
                        available -= rowHeights[first + count];
                        count++;
                    }
                    if (rowHeights.Count > 0 && count == 0)
                        throw new InvalidOperationException("A report table row cannot fit on a page even at the minimum font scale.");
                    Add(AnalysisReportFragmentKind.TableRows, block, height, scale, first, count, true,
                        tableLayout: tableLayout);
                    first += count;
                    if (first < rowHeights.Count) NewPage();
                } while (first < rowHeights.Count);
            }

            void PlaceFigure(AnalysisReportFigureBlock block)
            {
                var height = Math.Min(UsableHeight() * .48,
                    12.2 * 72 / 2.54 + TitleHeight(block.Title));
                Ensure(height + BlockSpacing);
                if (height > Remaining) height = Remaining;
                Add(AnalysisReportFragmentKind.PublicationFigure, block, height);
            }

            void PlaceFigurePair(AnalysisReportFigurePairBlock block)
            {
                var height = Math.Min(UsableHeight() * .52,
                    12.5 * 72 / 2.54 + TitleHeight(block.Title));
                Ensure(height + BlockSpacing);
                Add(AnalysisReportFragmentKind.PublicationFigurePair, block, Math.Min(height, Remaining));
            }

            void PlaceFigureCanvas(AnalysisReportFigureCanvasBlock block, bool cover)
            {
                var height = cover ? Math.Min(Remaining, UsableHeight() * .50) : Math.Min(Remaining, UsableHeight() * .55);
                if (height < 100 && !cover) { NewPage(); height = Math.Min(Remaining, UsableHeight() * .75); }
                Add(AnalysisReportFragmentKind.FigureCanvas, block, Math.Max(1, height),
                    Math.Min(1, Math.Max(.1, height / (ContentWidth * 0.8))));
            }

            void PlacePlot(AnalysisReportPlotBlock block)
            {
                var height = Math.Min(UsableHeight() * .40, ContentWidth * .42);
                Ensure(height + BlockSpacing);
                Add(AnalysisReportFragmentKind.CartesianPlot, block, height);
            }

            void PlaceThermodynamicSummary(AnalysisReportThermodynamicSummaryBlock block)
            {
                var legend = AnalysisReportThermodynamicSummaryLayout.LegendPositions(
                    block.Series, ContentWidth,
                    label => measurer.Measure(label, new AnalysisReportTextStyle(6)).Width);
                var rows = legend.Count == 0 ? 0 : legend[legend.Count - 1].Row + 1;
                var height = 6.5 * 72 / 2.54 + TitleHeight(block.Title) + 28
                    + Math.Max(0, rows - 1) * AnalysisReportThermodynamicSummaryLayout.LegendRowHeight;
                Ensure(height + BlockSpacing);
                Add(AnalysisReportFragmentKind.ThermodynamicSummary, block, Math.Min(height, Remaining));
            }

            void PlaceCorrelation(AnalysisReportCorrelationMatrixBlock block)
            {
                var height = block.PreferredSizeCentimeters * 72 / 2.54 + 68;
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
                IEnumerable<string> lines = null, AnalysisReportSection section = null,
                AnalysisReportTableLayout tableLayout = null,
                AnalysisReportHeadingLayout headingLayout = null,
                AnalysisReportKeyValueLayout keyValueLayout = null, bool isContinuation = false)
            {
                if (kind == AnalysisReportFragmentKind.Notice || kind == AnalysisReportFragmentKind.KeyValueRows)
                {
                    if (height > Remaining + .001)
                        throw new InvalidOperationException("Text fragment exceeds its allocated page space.");
                }
                else height = Math.Max(1, Math.Min(height, Remaining));
                page.Add(new AnalysisReportLayoutFragment(kind, block,
                    new AnalysisReportRect(left, y, ContentWidth, height), scale,
                    first, count, repeatHeader, lines, section, tableLayout, headingLayout: headingLayout,
                    keyValueLayout: keyValueLayout, isContinuation: isContinuation));
                y += height + BlockSpacing;
            }

            double TitleHeight(string title, double scale = 1) => string.IsNullOrWhiteSpace(title)
                ? 0 : LineHeight(new AnalysisReportTextStyle(HeadingStyle.FontSize * scale, true)) + 3;

            double TableTitleHeight(string title, double scale = 1) =>
                string.IsNullOrWhiteSpace(title) ? 0 : 18 * scale;

            AnalysisReportTableLayout CreateTableLayout(AnalysisReportTableBlock table, double scale)
            {
                var style = new AnalysisReportTextStyle(table.FontSize * scale);
                var widths = TableColumnWidths(table);
                var lineHeight = style.FontSize * 4 / 3;
                var headerStyle = new AnalysisReportTextStyle(table.FontSize * scale, true);
                var headers = table.Columns.Select((column, index) => (IReadOnlyList<string>)Wrap(column.Title,
                    Math.Max(1, widths[index] - 2 * CellPadding * scale), headerStyle)).ToList();
                var headerHeight = Math.Max(1, headers.Select(lines => lines.Count).DefaultIfEmpty(1).Max())
                    * lineHeight + 2 * table.VerticalCellPadding * scale;
                var rows = table.Rows.Select(row =>
                {
                    var cellLines = Enumerable.Range(0, table.Columns.Count)
                        .Select(column => (IReadOnlyList<string>)Wrap(column < row.Cells.Count ? row.Cells[column] : "",
                            Math.Max(1, widths[column] - 2 * CellPadding * scale), style)).ToList();
                    var height = Math.Max(1, cellLines.Select(lines => lines.Count).DefaultIfEmpty(1).Max())
                        * lineHeight + 2 * table.VerticalCellPadding * scale;
                    return new AnalysisReportTableRowLayout(cellLines, height);
                }).ToList();
                return new AnalysisReportTableLayout(widths, headers, headerHeight, rows);
            }

            double[] TableColumnWidths(AnalysisReportTableBlock table)
            {
                var totalWeight = table.Columns.Sum(column => column.WidthWeight);
                if (totalWeight <= 0) return Enumerable.Repeat(ContentWidth / Math.Max(1, table.Columns.Count), table.Columns.Count).ToArray();
                return table.Columns.Select(column => ContentWidth * column.WidthWeight / totalWeight).ToArray();
            }

            double TableHeight(AnalysisReportTableBlock table, AnalysisReportTableLayout tableLayout,
                double scale, int first, int count) =>
                TableTitleHeight(table.Title, scale) + tableLayout.HeaderHeight
                + tableLayout.Rows.Skip(first).Take(count).Sum(row => row.Height);

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
                            // Break between text elements so surrogate pairs and combining marks stay whole.
                            var elements = StringInfo.GetTextElementEnumerator(word);
                            while (elements.MoveNext())
                            {
                                var character = elements.GetTextElement();
                                var candidatePart = part + character;
                                if (part.Length > 0 && measurer.Measure(candidatePart, style).Width > width)
                                {
                                    output.Add(part);
                                    part = character;
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
