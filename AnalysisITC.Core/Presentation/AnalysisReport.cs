using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Presentation
{
    public static class AnalysisReportReferenceLabels
    {
        public static string Result(int zeroBasedResultIndex) =>
            (Math.Max(0, zeroBasedResultIndex) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static string Experiment(int zeroBasedResultIndex, int zeroBasedExperimentIndex) =>
            Result(zeroBasedResultIndex) + PublicationFigureCanvasBuilder.PanelLabel(zeroBasedExperimentIndex);

        public static string SupportingExperiment(int zeroBasedSupportingIndex) =>
            "S" + (Math.Max(0, zeroBasedSupportingIndex) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public enum AnalysisReportSectionKind
    {
        Cover,
        ResultOverview,
        AnalysisSummary,
        Interpretation,
        Experiment,
        SupportingData,
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
        public bool IncludeInjectionTables { get; set; } = true;
        public bool CondenseRepeatedExperiments { get; set; } = true;
        public UncertaintyDisplayStyle UncertaintyDisplayStyle { get; set; } = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;
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
        readonly List<AnalysisReportResultReference> results = new List<AnalysisReportResultReference>();
        readonly List<AnalysisReportSupportingExperimentReference> supportingExperiments = new List<AnalysisReportSupportingExperimentReference>();

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
        public AnalysisResultHealth ResultHealth { get; internal set; } = AnalysisResultHealth.Valid;
        public IReadOnlyList<AnalysisReportResultReference> Results => results;
        public IReadOnlyList<AnalysisReportSupportingExperimentReference> SupportingExperiments => supportingExperiments;
        public bool IsMultiResult => results.Count > 1;
        public string ExportDateText => "Exported "
            + GeneratedAtUtc.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            + " UTC";
        public string StatusBadgeText => ResultHealth switch
        {
            AnalysisResultHealth.Valid => IsMultiResult ? "ANALYSES VALID" : "ANALYSIS VALID",
            AnalysisResultHealth.Warning => "REVIEW WARNINGS",
            AnalysisResultHealth.PartialInvalid => "PARTIAL / STALE",
            AnalysisResultHealth.Invalid => "INVALID / STALE",
            _ => "STATUS UNKNOWN",
        };
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

        internal void AddResult(AnalysisReportResultReference result)
        {
            if (result != null) results.Add(result);
        }

        internal void AddSupportingExperiment(AnalysisReportSupportingExperimentReference experiment)
        {
            if (experiment != null) supportingExperiments.Add(experiment);
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

    public sealed class AnalysisReportSupportingExperimentReference
    {
        internal AnalysisReportSupportingExperimentReference(string id, string label, string name)
        {
            Id = id ?? "";
            Label = label ?? "";
            Name = name ?? "";
        }

        public string Id { get; }
        public string Label { get; }
        public string Name { get; }
    }

    public sealed class AnalysisReportResultReference
    {
        internal AnalysisReportResultReference(
            string id, string label, string name, DateTime date, string model,
            int experimentCount, AnalysisResultHealth health)
        {
            Id = id ?? "";
            Label = label ?? "";
            Name = name ?? "";
            Date = date;
            Model = model ?? "";
            ExperimentCount = Math.Max(0, experimentCount);
            Health = health;
        }

        public string Id { get; }
        public string Label { get; }
        public string Name { get; }
        public DateTime Date { get; }
        public string Model { get; }
        public int ExperimentCount { get; }
        public AnalysisResultHealth Health { get; }
    }

    public sealed class AnalysisReportSection
    {
        readonly List<AnalysisReportBlock> blocks = new List<AnalysisReportBlock>();

        internal AnalysisReportSection(
            AnalysisReportSectionKind kind,
            string id,
            string title,
            AnalysisReportLayoutPolicy layout,
            string resultName = "",
            AnalysisResultHealth? statusBadgeHealth = null)
        {
            Kind = kind;
            Id = id ?? "";
            Title = title ?? "";
            Layout = layout;
            ResultName = resultName ?? "";
            StatusBadgeHealth = statusBadgeHealth;
        }

        public AnalysisReportSectionKind Kind { get; }
        public string Id { get; }
        public string Title { get; }
        public AnalysisReportLayoutPolicy Layout { get; }
        public string ResultName { get; }
        public AnalysisResultHealth? StatusBadgeHealth { get; }
        public IReadOnlyList<AnalysisReportBlock> Blocks => blocks;

        internal void Add(AnalysisReportBlock block)
        {
            if (block != null) blocks.Add(block);
        }
    }

    public sealed class AnalysisReportTableOfContentsEntry
    {
        internal AnalysisReportTableOfContentsEntry(string title, string targetSectionId)
        {
            Title = title ?? "";
            TargetSectionId = targetSectionId ?? "";
        }

        public string Title { get; }
        public string TargetSectionId { get; }
        public int PageNumber { get; internal set; }
    }

    public sealed class AnalysisReportTableOfContentsBlock : AnalysisReportBlock
    {
        internal AnalysisReportTableOfContentsBlock(
            string title,
            IEnumerable<AnalysisReportTableOfContentsEntry> entries)
            : base(title, AnalysisReportLayoutPolicy.AllowContinuation)
        {
            Entries = (entries ?? Enumerable.Empty<AnalysisReportTableOfContentsEntry>()).ToList();
        }

        public IReadOnlyList<AnalysisReportTableOfContentsEntry> Entries { get; }

        internal void AddEntry(AnalysisReportTableOfContentsEntry entry)
        {
            if (entry != null && Entries is List<AnalysisReportTableOfContentsEntry> values)
                values.Add(entry);
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
        internal AnalysisReportTextBlock(string title, string text, AnalysisReportLayoutPolicy layout,
            bool inlineMarkdown = false)
            : base(title, layout)
        {
            Text = text ?? "";
            InlineMarkdown = inlineMarkdown;
        }

        public string Text { get; }
        public bool InlineMarkdown { get; }
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
        public AnalysisReportKeyValueItem(string label, string value, int indentLevel = 0)
        {
            Label = label ?? "";
            Value = value ?? "";
            IndentLevel = Math.Max(0, Math.Min(1, indentLevel));
        }

        public string Label { get; }
        public string Value { get; }
        public int IndentLevel { get; }
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
            AnalysisResultColumnAlignment alignment = AnalysisResultColumnAlignment.Left,
            double widthWeight = 1)
        {
            Id = id ?? "";
            Title = title ?? "";
            Alignment = alignment;
            WidthWeight = widthWeight > 0 ? widthWeight : 1;
        }

        public string Id { get; }
        public string Title { get; }
        public AnalysisResultColumnAlignment Alignment { get; }
        public double WidthWeight { get; }
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
            AnalysisReportLayoutPolicy layout,
            double fontSize = 7.5,
            double verticalCellPadding = 3)
            : base(title, layout)
        {
            Columns = (columns ?? Enumerable.Empty<AnalysisReportTableColumn>()).ToList();
            Rows = (rows ?? Enumerable.Empty<AnalysisReportTableRow>()).ToList();
            FontSize = fontSize > 0 ? fontSize : 7.5;
            VerticalCellPadding = Math.Max(0, verticalCellPadding);
        }

        public IReadOnlyList<AnalysisReportTableColumn> Columns { get; }
        public IReadOnlyList<AnalysisReportTableRow> Rows { get; }
        public double FontSize { get; }
        public double VerticalCellPadding { get; }
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

    public sealed class AnalysisReportFigurePairBlock : AnalysisReportBlock
    {
        internal AnalysisReportFigurePairBlock(
            string title,
            string leftTitle,
            PublicationFigureDocument leftFigure,
            string rightTitle,
            PublicationFigureDocument rightFigure)
            : base(title, AnalysisReportLayoutPolicy.KeepTogether)
        {
            LeftTitle = leftTitle ?? "";
            LeftFigure = leftFigure;
            RightTitle = rightTitle ?? "";
            RightFigure = rightFigure;
        }

        public string LeftTitle { get; }
        public PublicationFigureDocument LeftFigure { get; }
        public string RightTitle { get; }
        public PublicationFigureDocument RightFigure { get; }
    }

    public sealed class AnalysisReportFigureCanvasBlock : AnalysisReportBlock
    {
        internal AnalysisReportFigureCanvasBlock(string title, PublicationFigureCanvasDocument canvas)
            : base(title, AnalysisReportLayoutPolicy.KeepTogether | AnalysisReportLayoutPolicy.ShrinkToSinglePage)
        {
            Canvas = canvas;
        }

        public PublicationFigureCanvasDocument Canvas { get; }
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
        public double? StandardDeviationLower { get; internal set; }
        public double? StandardDeviationUpper { get; internal set; }
        public double? ConfidenceLower { get; internal set; }
        public double? ConfidenceUpper { get; internal set; }
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
            IEnumerable<AnalysisReportPlotSeries> series,
            UncertaintyDisplayStyle uncertaintyStyle = UncertaintyDisplayStyle.Automatic)
            : base(title, AnalysisReportLayoutPolicy.KeepTogether)
        {
            XAxisTitle = xAxisTitle ?? "";
            YAxisTitle = yAxisTitle ?? "";
            Series = (series ?? Enumerable.Empty<AnalysisReportPlotSeries>()).ToList();
            UncertaintyStyle = uncertaintyStyle;
        }

        public string XAxisTitle { get; }
        public string YAxisTitle { get; }
        public IReadOnlyList<AnalysisReportPlotSeries> Series { get; }
        public UncertaintyDisplayStyle UncertaintyStyle { get; }
    }

    public sealed class AnalysisReportThermodynamicBar
    {
        internal AnalysisReportThermodynamicBar(
            string category,
            double value,
            double? standardDeviationLower,
            double? standardDeviationUpper,
            double? confidenceLower,
            double? confidenceUpper)
        {
            Category = category ?? "";
            Value = value;
            StandardDeviationLower = standardDeviationLower;
            StandardDeviationUpper = standardDeviationUpper;
            ConfidenceLower = confidenceLower;
            ConfidenceUpper = confidenceUpper;
        }

        public string Category { get; }
        public double Value { get; }
        public double? StandardDeviationLower { get; }
        public double? StandardDeviationUpper { get; }
        public double? ConfidenceLower { get; }
        public double? ConfidenceUpper { get; }
    }

    public sealed class AnalysisReportThermodynamicSeries
    {
        internal AnalysisReportThermodynamicSeries(string label, IEnumerable<AnalysisReportThermodynamicBar> bars)
        {
            Label = label ?? "";
            Bars = (bars ?? Enumerable.Empty<AnalysisReportThermodynamicBar>()).ToList();
        }

        public string Label { get; }
        public IReadOnlyList<AnalysisReportThermodynamicBar> Bars { get; }
    }

    public sealed class AnalysisReportThermodynamicSummaryBlock : AnalysisReportBlock
    {
        internal AnalysisReportThermodynamicSummaryBlock(
            string title,
            string yAxisTitle,
            UncertaintyDisplayStyle uncertaintyStyle,
            string uncertaintyNote,
            IEnumerable<string> categories,
            IEnumerable<AnalysisReportThermodynamicSeries> series)
            : base(title, AnalysisReportLayoutPolicy.KeepTogether)
        {
            YAxisTitle = yAxisTitle ?? "";
            UncertaintyStyle = uncertaintyStyle;
            UncertaintyNote = uncertaintyNote ?? "";
            Categories = (categories ?? Enumerable.Empty<string>()).ToList();
            Series = (series ?? Enumerable.Empty<AnalysisReportThermodynamicSeries>()).ToList();
        }

        public string YAxisTitle { get; }
        public UncertaintyDisplayStyle UncertaintyStyle { get; }
        public string UncertaintyNote { get; }
        public IReadOnlyList<string> Categories { get; }
        public IReadOnlyList<AnalysisReportThermodynamicSeries> Series { get; }
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
            ColumnLabels = Labels.Select(CompactColumnLabel).ToList();
            Matrix = matrix == null ? null : (double[,])matrix.Clone();
            Notes = (notes ?? Enumerable.Empty<string>()).ToList();
        }

        public IReadOnlyList<string> Labels { get; }
        public IReadOnlyList<string> ColumnLabels { get; }
        public double PreferredSizeCentimeters =>
            Math.Min(9, 6.5 + Math.Max(0, Labels.Count - 8) * .25);
        public double[,] Matrix { get; }
        public IReadOnlyList<string> Notes { get; }

        static string CompactColumnLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "";
            var separator = label.LastIndexOf('·');
            return separator >= 0 && separator + 1 < label.Length
                ? label.Substring(separator + 1).Trim()
                : label.Trim();
        }
    }
}
