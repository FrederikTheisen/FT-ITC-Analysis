using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Presentation
{
    public enum AnalysisReportSectionKind
    {
        Cover,
        AnalysisSummary,
        Interpretation,
        Experiment,
        AdvancedAnalysis,
        Appendix,
    }

    [Flags]
    public enum AnalysisReportLayoutPolicy
    {
        None = 0,
        StartOnNewPage = 1,
        KeepTogether = 2,
        AllowContinuation = 4,
        ShrinkToSinglePage = 8,
    }

    public enum AnalysisReportNoticeLevel
    {
        Information,
        Warning,
        Error,
    }

    public enum AnalysisReportDiagnosticSeverity
    {
        Warning,
        Error,
    }

    public enum AnalysisReportAdvancedSectionKind
    {
        TemperatureDependence,
        SpolarRecord,
        AffinityVersusSalt,
        DebyeHuckel,
        CounterIonRelease,
        Protonation,
        Correlation,
    }

    public enum AnalysisReportPlotSeriesKind
    {
        Points,
        Line,
    }

    public sealed class AnalysisReportOptions
    {
        public string DocumentLabel { get; set; } = "";
        public string Title { get; set; } = "";
        public EnergyUnitFamily EnergyUnitFamily { get; set; } = AppSettings.EnergyUnitFamily;
        public EnergyUnit? EnergyUnitOverride { get; set; }
        public bool UseKelvin { get; set; }
        public UncertaintyDisplayStyle UncertaintyDisplayStyle { get; set; } = AppSettings.UncertaintyDisplayStyle;
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
        public string ApplicationVersion { get; set; } = AppVersion.FullVersionString;
        public IList<AnalysisReportAdvancedSectionRequest> AdvancedSections { get; } =
            new List<AnalysisReportAdvancedSectionRequest>();
    }

    public sealed class AnalysisReportAdvancedSectionRequest
    {
        public AnalysisReportAdvancedSectionRequest(
            AnalysisReportAdvancedSectionKind kind,
            int? correlationMemberIndex = null)
        {
            Kind = kind;
            CorrelationMemberIndex = correlationMemberIndex;
        }

        public AnalysisReportAdvancedSectionKind Kind { get; }

        /// <summary>
        /// Null selects the single/shared correlation scope. A non-negative index
        /// selects the shared-plus-local scope for that result member.
        /// </summary>
        public int? CorrelationMemberIndex { get; }

        public string Key => Kind == AnalysisReportAdvancedSectionKind.Correlation
            ? "correlation:" + (CorrelationMemberIndex.HasValue
                ? "member-" + CorrelationMemberIndex.Value
                : "shared")
            : Kind.ToString();
    }

    public sealed class AnalysisReportAdvancedSectionDescriptor
    {
        internal AnalysisReportAdvancedSectionDescriptor(
            AnalysisReportAdvancedSectionRequest request,
            string title,
            string description)
        {
            Request = request;
            Title = title ?? "";
            Description = description ?? "";
        }

        public AnalysisReportAdvancedSectionRequest Request { get; }
        public string Title { get; }
        public string Description { get; }
    }

    public sealed class AnalysisReportPageSettings
    {
        public const double A4WidthCentimeters = 21.0;
        public const double A4HeightCentimeters = 29.7;
        public const double DefaultMarginCentimeters = 1.5;

        public double WidthCentimeters { get; internal set; } = A4WidthCentimeters;
        public double HeightCentimeters { get; internal set; } = A4HeightCentimeters;
        public double MarginTopCentimeters { get; internal set; } = DefaultMarginCentimeters;
        public double MarginRightCentimeters { get; internal set; } = DefaultMarginCentimeters;
        public double MarginBottomCentimeters { get; internal set; } = DefaultMarginCentimeters;
        public double MarginLeftCentimeters { get; internal set; } = DefaultMarginCentimeters;
        public bool IsLandscape => WidthCentimeters > HeightCentimeters;
    }

    public sealed class AnalysisReportAppearance
    {
        public string StyleId { get; internal set; } = "neutral-scientific";
        public bool MonochromeFriendly { get; internal set; } = true;
        public bool ProminentBranding { get; internal set; }
    }

    public sealed class AnalysisReportDiagnostic
    {
        internal AnalysisReportDiagnostic(
            AnalysisReportDiagnosticSeverity severity,
            string code,
            string message)
        {
            Severity = severity;
            Code = code ?? "";
            Message = message ?? "";
        }

        public AnalysisReportDiagnosticSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
    }

    public sealed class AnalysisReportValidationResult
    {
        readonly List<AnalysisReportDiagnostic> diagnostics;

        internal AnalysisReportValidationResult(IEnumerable<AnalysisReportDiagnostic> diagnostics)
        {
            this.diagnostics = (diagnostics ?? Enumerable.Empty<AnalysisReportDiagnostic>()).ToList();
        }

        public IReadOnlyList<AnalysisReportDiagnostic> Diagnostics => diagnostics;
        public IReadOnlyList<string> Errors => diagnostics
            .Where(item => item.Severity == AnalysisReportDiagnosticSeverity.Error)
            .Select(item => item.Message)
            .ToList();
        public bool IsValid => Errors.Count == 0;
    }

    public sealed class AnalysisReportDocument
    {
        readonly List<AnalysisReportSection> sections = new List<AnalysisReportSection>();
        readonly List<AnalysisReportDiagnostic> diagnostics = new List<AnalysisReportDiagnostic>();

        internal AnalysisReportDocument()
        {
        }

        public AnalysisReportPageSettings PageSettings { get; } = new AnalysisReportPageSettings();
        public AnalysisReportAppearance Appearance { get; } = new AnalysisReportAppearance();
        public string DocumentLabel { get; internal set; } = "";
        public string Title { get; internal set; } = "";
        public string ResultName { get; internal set; } = "";
        public string ResultId { get; internal set; } = "";
        public DateTime ResultDate { get; internal set; }
        public DateTime GeneratedAtUtc { get; internal set; }
        public string Creator { get; internal set; } = "";
        public string ApplicationVersion { get; internal set; } = "";
        public IReadOnlyList<AnalysisReportSection> Sections => sections;
        public IReadOnlyList<AnalysisReportDiagnostic> Diagnostics => diagnostics;
        public IReadOnlyList<string> Warnings => diagnostics
            .Where(item => item.Severity == AnalysisReportDiagnosticSeverity.Warning)
            .Select(item => item.Message)
            .ToList();
        public IReadOnlyList<string> ValidationErrors => diagnostics
            .Where(item => item.Severity == AnalysisReportDiagnosticSeverity.Error)
            .Select(item => item.Message)
            .ToList();
        public bool IsValid => ValidationErrors.Count == 0;

        internal void AddSection(AnalysisReportSection section)
        {
            if (section != null) sections.Add(section);
        }

        internal void InsertSection(int index, AnalysisReportSection section)
        {
            if (section == null) return;
            sections.Insert(Math.Max(0, Math.Min(index, sections.Count)), section);
        }

        internal void AddDiagnostic(
            AnalysisReportDiagnosticSeverity severity,
            string code,
            string message)
        {
            diagnostics.Add(new AnalysisReportDiagnostic(severity, code, message));
        }
    }

    public sealed class AnalysisReportSection
    {
        readonly List<AnalysisReportBlock> blocks = new List<AnalysisReportBlock>();

        internal AnalysisReportSection(
            AnalysisReportSectionKind kind,
            string id,
            string title,
            AnalysisReportLayoutPolicy layout)
        {
            Kind = kind;
            Id = id ?? "";
            Title = title ?? "";
            Layout = layout;
        }

        public AnalysisReportSectionKind Kind { get; }
        public string Id { get; }
        public string Title { get; }
        public AnalysisReportLayoutPolicy Layout { get; }
        public IReadOnlyList<AnalysisReportBlock> Blocks => blocks;

        internal void Add(AnalysisReportBlock block)
        {
            if (block != null) blocks.Add(block);
        }
    }

    public abstract class AnalysisReportBlock
    {
        protected AnalysisReportBlock(string title, AnalysisReportLayoutPolicy layout)
        {
            Title = title ?? "";
            Layout = layout;
        }

        public string Title { get; }
        public AnalysisReportLayoutPolicy Layout { get; }
    }

    public sealed class AnalysisReportHeadingBlock : AnalysisReportBlock
    {
        internal AnalysisReportHeadingBlock(string text, int level)
            : base("", AnalysisReportLayoutPolicy.KeepTogether)
        {
            Text = text ?? "";
            Level = Math.Max(1, Math.Min(3, level));
        }

        public string Text { get; }
        public int Level { get; }
    }

    public sealed class AnalysisReportTextBlock : AnalysisReportBlock
    {
        internal AnalysisReportTextBlock(string title, string text, AnalysisReportLayoutPolicy layout)
            : base(title, layout)
        {
            Text = text ?? "";
        }

        public string Text { get; }
    }

    public sealed class AnalysisReportNoticeBlock : AnalysisReportBlock
    {
        internal AnalysisReportNoticeBlock(
            string title,
            string message,
            AnalysisReportNoticeLevel level)
            : base(title, AnalysisReportLayoutPolicy.KeepTogether)
        {
            Message = message ?? "";
            Level = level;
        }

        public string Message { get; }
        public AnalysisReportNoticeLevel Level { get; }
    }

    public sealed class AnalysisReportKeyValueItem
    {
        public AnalysisReportKeyValueItem(string label, string value)
        {
            Label = label ?? "";
            Value = value ?? "";
        }

        public string Label { get; }
        public string Value { get; }
    }

    public sealed class AnalysisReportKeyValueBlock : AnalysisReportBlock
    {
        internal AnalysisReportKeyValueBlock(
            string title,
            IEnumerable<AnalysisReportKeyValueItem> items,
            AnalysisReportLayoutPolicy layout = AnalysisReportLayoutPolicy.KeepTogether)
            : base(title, layout)
        {
            Items = (items ?? Enumerable.Empty<AnalysisReportKeyValueItem>()).ToList();
        }

        public IReadOnlyList<AnalysisReportKeyValueItem> Items { get; }
    }

    public sealed class AnalysisReportTableColumn
    {
        public AnalysisReportTableColumn(
            string id,
            string title,
            AnalysisResultColumnAlignment alignment = AnalysisResultColumnAlignment.Left)
        {
            Id = id ?? "";
            Title = title ?? "";
            Alignment = alignment;
        }

        public string Id { get; }
        public string Title { get; }
        public AnalysisResultColumnAlignment Alignment { get; }
    }

    public sealed class AnalysisReportTableRow
    {
        public AnalysisReportTableRow(IEnumerable<string> cells)
        {
            Cells = (cells ?? Enumerable.Empty<string>()).Select(value => value ?? "").ToList();
        }

        public IReadOnlyList<string> Cells { get; }
    }

    public sealed class AnalysisReportTableBlock : AnalysisReportBlock
    {
        internal AnalysisReportTableBlock(
            string title,
            IEnumerable<AnalysisReportTableColumn> columns,
            IEnumerable<AnalysisReportTableRow> rows,
            AnalysisReportLayoutPolicy layout)
            : base(title, layout)
        {
            Columns = (columns ?? Enumerable.Empty<AnalysisReportTableColumn>()).ToList();
            Rows = (rows ?? Enumerable.Empty<AnalysisReportTableRow>()).ToList();
        }

        public IReadOnlyList<AnalysisReportTableColumn> Columns { get; }
        public IReadOnlyList<AnalysisReportTableRow> Rows { get; }
    }

    public sealed class AnalysisReportFigureBlock : AnalysisReportBlock
    {
        internal AnalysisReportFigureBlock(
            string title,
            string panelLabel,
            PublicationFigureDocument figure,
            AnalysisReportLayoutPolicy layout)
            : base(title, layout)
        {
            PanelLabel = panelLabel ?? "";
            Figure = figure;
        }

        public string PanelLabel { get; }
        public PublicationFigureDocument Figure { get; }
    }

    public sealed class AnalysisReportContactSheetCell
    {
        internal AnalysisReportContactSheetCell(
            int row,
            int column,
            string panelLabel,
            string experimentName,
            PublicationFigureDocument figure)
        {
            Row = row;
            Column = column;
            PanelLabel = panelLabel ?? "";
            ExperimentName = experimentName ?? "";
            Figure = figure;
        }

        public int Row { get; }
        public int Column { get; }
        public string PanelLabel { get; }
        public string ExperimentName { get; }
        public PublicationFigureDocument Figure { get; }
    }

    public sealed class AnalysisReportContactSheetBlock : AnalysisReportBlock
    {
        internal AnalysisReportContactSheetBlock(
            string title,
            int rows,
            int columns,
            IEnumerable<AnalysisReportContactSheetCell> cells)
            : base(title, AnalysisReportLayoutPolicy.KeepTogether | AnalysisReportLayoutPolicy.ShrinkToSinglePage)
        {
            Rows = rows;
            Columns = columns;
            Cells = (cells ?? Enumerable.Empty<AnalysisReportContactSheetCell>()).ToList();
        }

        public int Rows { get; }
        public int Columns { get; }
        public IReadOnlyList<AnalysisReportContactSheetCell> Cells { get; }
    }

    public sealed class AnalysisReportPlotPoint
    {
        public AnalysisReportPlotPoint(
            double x,
            double y,
            double? lower = null,
            double? upper = null,
            string label = "")
        {
            X = x;
            Y = y;
            Lower = lower;
            Upper = upper;
            Label = label ?? "";
        }

        public double X { get; }
        public double Y { get; }
        public double? Lower { get; }
        public double? Upper { get; }
        public string Label { get; }
    }

    public sealed class AnalysisReportPlotSeries
    {
        public AnalysisReportPlotSeries(
            string label,
            AnalysisReportPlotSeriesKind kind,
            IEnumerable<AnalysisReportPlotPoint> points,
            string group = "")
        {
            Label = label ?? "";
            Kind = kind;
            Points = (points ?? Enumerable.Empty<AnalysisReportPlotPoint>()).ToList();
            Group = group ?? "";
        }

        public string Label { get; }
        public AnalysisReportPlotSeriesKind Kind { get; }
        public IReadOnlyList<AnalysisReportPlotPoint> Points { get; }
        public string Group { get; }
    }

    public sealed class AnalysisReportPlotBlock : AnalysisReportBlock
    {
        internal AnalysisReportPlotBlock(
            string title,
            string xAxisTitle,
            string yAxisTitle,
            IEnumerable<AnalysisReportPlotSeries> series)
            : base(title, AnalysisReportLayoutPolicy.KeepTogether)
        {
            XAxisTitle = xAxisTitle ?? "";
            YAxisTitle = yAxisTitle ?? "";
            Series = (series ?? Enumerable.Empty<AnalysisReportPlotSeries>()).ToList();
        }

        public string XAxisTitle { get; }
        public string YAxisTitle { get; }
        public IReadOnlyList<AnalysisReportPlotSeries> Series { get; }
    }

    public sealed class AnalysisReportCorrelationMatrixBlock : AnalysisReportBlock
    {
        internal AnalysisReportCorrelationMatrixBlock(
            string title,
            IEnumerable<string> labels,
            double[,] matrix,
            IEnumerable<string> notes)
            : base(title, AnalysisReportLayoutPolicy.KeepTogether | AnalysisReportLayoutPolicy.ShrinkToSinglePage)
        {
            Labels = (labels ?? Enumerable.Empty<string>()).ToList();
            Matrix = matrix == null ? null : (double[,])matrix.Clone();
            Notes = (notes ?? Enumerable.Empty<string>()).ToList();
        }

        public IReadOnlyList<string> Labels { get; }
        public double[,] Matrix { get; }
        public IReadOnlyList<string> Notes { get; }
    }
}
