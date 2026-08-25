using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;

using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Core.Analysis;

namespace AnalysisITC.Avalonia.Results;

/// <summary>
/// A compact, print-safe correlation matrix.  The control intentionally owns only the
/// presentation model; the analyzer provides a matrix without taking a dependency on Avalonia.
/// </summary>
public sealed class ResultCorrelationGraphControl : Control
{
    readonly List<string> labels = new();
    readonly List<string> tooltipLabels = new();
    double[,] matrix = new double[0, 0];
    string emptyMessage = "";
    CorrelationHoverPolicy hoverPolicy = CorrelationHoverPolicy.Disabled;
    int hoveredRow = -1;
    int hoveredColumn = -1;
    string? hoverToolTip;

    internal enum CorrelationHoverPolicy
    {
        Disabled,
        WhenValuesHidden,
        Always
    }

    public ResultCorrelationGraphControl()
    {
        Focusable = true;
        ClipToBounds = true;
        AutomationProperties.SetName(this, AccessibleText);
    }

    public IReadOnlyList<string> Labels => labels;
    public int Count => matrix.GetLength(0);
    public double[,] CorrelationMatrix => (double[,])matrix.Clone();
    public bool HasPrintableData => Count > 0;
    public string Method { get; private set; } = "";
    public string Scope { get; private set; } = "";
    public int SelectedCount { get; private set; }
    public int UsedCount { get; private set; }
    public int OmittedCount { get; private set; }
    public string SelectedLabel { get; private set; } = "None";
    public bool UnlockedParameters { get; private set; }
    public bool RankWarning { get; private set; }
    public string AccessibleText => Count == 0
        ? "Parameter correlation matrix is empty."
        : $"Parameter correlation matrix with {Count} parameters ({string.Join(", ", labels)}). Values range from minus one to one.";
    public double GetValue(int row, int column)
        => row >= 0 && row < Count && column >= 0 && column < Count ? matrix[row, column] : double.NaN;

    /// <summary>
    /// Hover is intentionally disabled in the product UI for now. The other policies remain
    /// available to future presentation changes and focused control tests.
    /// </summary>
    internal CorrelationHoverPolicy HoverPolicy
    {
        get => hoverPolicy;
        set
        {
            if (hoverPolicy == value) return;
            hoverPolicy = value;
            ClearHoverState();
        }
    }

    internal CorrelationHoverPolicy HoverPolicyForTesting
    {
        get => HoverPolicy;
        set => HoverPolicy = value;
    }

    internal (int Row, int Column)? HoveredCellForTesting
        => hoveredRow < 0 || hoveredColumn < 0 ? null : (hoveredRow, hoveredColumn);
    internal string? HoverToolTipForTesting => hoverToolTip;
    internal bool ShowValuesForTesting
        => Count > 0 && CalculateLayout(Bounds.Size).ShowValues;

    internal void ClearHoverState()
    {
        hoveredRow = -1;
        hoveredColumn = -1;
        hoverToolTip = null;
        ToolTip.SetTip(this, null);
        Cursor = null;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Correlation is a fixed plotting surface. It consumes the viewport offered by
        // graphHost rather than advertising a larger desired size which would force scrolling.
        const double fallbackWidth = 640;
        const double fallbackHeight = 520;
        var width = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width) : fallbackWidth;
        var height = double.IsFinite(availableSize.Height) ? Math.Max(1, availableSize.Height) : fallbackHeight;
        return new Size(width, height);
    }

    public void SetMatrix(IEnumerable<string> parameterLabels, double[,] values, string? method = null, string? scope = null)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        var n = values.GetLength(0);
        if (values.GetLength(1) != n) throw new ArgumentException("Correlation matrix must be square.", nameof(values));

        ClearHoverState();
        labels.Clear();
        tooltipLabels.Clear();
        labels.AddRange((parameterLabels ?? Array.Empty<string>()).Take(n).Select((s, i) => string.IsNullOrWhiteSpace(s) ? $"Parameter {i + 1}" : s));
        while (labels.Count < n) labels.Add($"Parameter {labels.Count + 1}");
        // Generic matrices have no separate experiment metadata, but their tooltip labels
        // should still use the same scientific symbols and scope wording as the axes.
        // SetCorrelationResult replaces these with the complete member-aware labels below.
        tooltipLabels.AddRange(labels.Select(CompactLabel));
        matrix = (double[,])values.Clone();
        Method = method ?? "";
        Scope = scope ?? "";
        emptyMessage = "";
        AutomationProperties.SetName(this, AccessibleText);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void Clear(string message = "")
    {
        ClearHoverState();
        labels.Clear();
        tooltipLabels.Clear();
        matrix = new double[0, 0];
        emptyMessage = message;
        Method = "";
        Scope = "";
        SelectedCount = 0;
        UsedCount = 0;
        OmittedCount = 0;
        SelectedLabel = "None";
        UnlockedParameters = false;
        RankWarning = false;
        AutomationProperties.SetName(this, string.IsNullOrWhiteSpace(message) ? AccessibleText : message);
        InvalidateVisual();
    }

    public void SetCorrelationResult(
        BootstrapCorrelationResult correlation,
        int selectedCount,
        string? selectedLabel,
        bool isGlobalResult = false)
    {
        if (correlation == null) { Clear("Correlation is unavailable for this result."); return; }
        SelectedCount = selectedCount;
        SelectedLabel = string.IsNullOrWhiteSpace(selectedLabel) ? "None" : selectedLabel;
        UsedCount = correlation.UsedReplicateCount;
        OmittedCount = correlation.OmittedParameterCount;
        UnlockedParameters = correlation.Parameters.Any(parameter => parameter.IncludedBecauseBootstrapUnlock);
        RankWarning = correlation.IsRankLimited;
        Method = "Residual bootstrap (Pearson)";
        Scope = correlation.Parameters.Any(parameter => parameter.IsMember)
            ? "Global + selected experiment"
            : isGlobalResult || correlation.Parameters.Any(parameter => parameter.IsShared) ? "Global parameters" : "Single experiment";
        if (!correlation.IsAvailable || correlation.CorrelationMatrix == null)
        {
            var reason = correlation.Availability?.Reason ?? "Correlation is unavailable for this result.";
            Clear(reason);
            SelectedCount = selectedCount;
            SelectedLabel = string.IsNullOrWhiteSpace(selectedLabel) ? "None" : selectedLabel;
            UsedCount = correlation.UsedReplicateCount;
            OmittedCount = correlation.OmittedParameterCount;
            Method = "Residual bootstrap (Pearson)";
            Scope = correlation.Parameters.Any(parameter => parameter.IsMember)
                ? "Global + selected experiment"
                : isGlobalResult || correlation.Parameters.Any(parameter => parameter.IsShared) ? "Global parameters" : "Single experiment";
            UnlockedParameters = correlation.Parameters.Any(parameter => parameter.IncludedBecauseBootstrapUnlock);
            RankWarning = correlation.IsRankLimited;
            return;
        }
        var displayLabels = correlation.Parameters.Select(parameter =>
        {
            var label = NormalizeParameterLabel(parameter.Label) + (parameter.IncludedBecauseBootstrapUnlock ? "*" : string.Empty);
            if (parameter.IsShared) return "Global · " + label;
            if (parameter.IsMember) return "Experiment · " + label;
            return label;
        }).ToArray();
        var completeLabels = correlation.Parameters.Select(parameter =>
        {
            var label = NormalizeParameterLabel(parameter.Label) + (parameter.IncludedBecauseBootstrapUnlock ? "*" : string.Empty);
            if (parameter.IsShared) return "Global · " + label;
            if (parameter.IsMember)
            {
                var member = string.IsNullOrWhiteSpace(parameter.MemberName) ? SelectedLabel : parameter.MemberName;
                return string.IsNullOrWhiteSpace(member) || member == "None"
                    ? "Experiment · " + label
                    : "Experiment · " + member + " · " + label;
            }
            return label;
        }).ToArray();
        SetMatrix(displayLabels, correlation.CorrelationMatrix, Method, Scope);
        tooltipLabels.Clear();
        tooltipLabels.AddRange(completeLabels);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdateHover(e.GetPosition(this));
        var layout = Count == 0 ? default : CalculateLayout(Bounds.Size);
        // WhenValuesHidden remains fully inert while values are legible, including event
        // routing; this keeps the dormant policy indistinguishable from Disabled in that case.
        e.Handled = Count > 0 && HoverEnabled(layout);
    }

    internal void UpdateHoverAtForTesting(Point point) => UpdateHover(point);

    void UpdateHover(Point point)
    {
        var layout = Count == 0 ? default : CalculateLayout(Bounds.Size);
        if (Count == 0 || !HoverEnabled(layout))
        {
            // Disabled is deliberately inert: no hit-testing, highlight, tooltip, or cursor.
            if (hoveredRow >= 0) ClearHoverState();
            return;
        }

        if (!layout.Matrix.Contains(point))
        {
            ClearHoverState();
            return;
        }

        var column = (int)((point.X - layout.Matrix.Left) / layout.Cell);
        var row = (int)((point.Y - layout.Matrix.Top) / layout.Cell);
        if (row < 0 || row >= Count || column < 0 || column >= Count)
        {
            ClearHoverState();
            return;
        }

        hoveredRow = row;
        hoveredColumn = column;
        hoverToolTip = BuildToolTip(row, column);
        ToolTip.SetTip(this, hoverToolTip);
        Cursor = new Cursor(StandardCursorType.Hand);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearHoverState();
    }

    bool HoverEnabled(CorrelationLayout layout)
        => hoverPolicy switch
        {
            CorrelationHoverPolicy.Always => true,
            CorrelationHoverPolicy.WhenValuesHidden => !layout.ShowValues,
            _ => false
        };

    string BuildToolTip(int row, int column)
    {
        var rowLabel = row < tooltipLabels.Count ? tooltipLabels[row] : CompactLabel(labels[row]);
        var columnLabel = column < tooltipLabels.Count ? tooltipLabels[column] : CompactLabel(labels[column]);
        return $"{rowLabel} vs {columnLabel}\nPearson r: {Clamp(matrix[row, column]).ToString("0.00", CultureInfo.InvariantCulture)}\nReplicates: {UsedCount.ToString(CultureInfo.CurrentCulture)}";
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        var theme = AvaloniaGraphSettings.CurrentForRender;
        context.DrawRectangle(theme.CanvasBrush, null, bounds);

        if (Count == 0)
        {
            AvaloniaGraphText.DrawWrappedText(context, string.IsNullOrWhiteSpace(emptyMessage) ? "No correlation data available." : emptyMessage,
                new Point(24, Math.Max(24, bounds.Height / 2 - 16)), Math.Max(60, bounds.Width - 48), 14, FontWeight.SemiBold, theme.MutedTextBrush);
            return;
        }

        var layout = CalculateLayout(bounds.Size);
        var origin = layout.Matrix.TopLeft;
        var cell = layout.Cell;
        context.DrawRectangle(theme.PlotBrush, theme.FramePen, layout.Matrix);

        for (var r = 0; r < Count; r++)
        {
            var displayLabel = CompactLabel(labels[r]);
            DrawLabel(context, displayLabel, new Point(origin.X - 10, origin.Y + r * cell + cell / 2), right: true, theme.MutedTextBrush);
            if (layout.ShowColumnLabels)
                DrawColumnLabel(context, displayLabel, new Point(origin.X + r * cell + cell / 2, origin.Y - 8), theme.MutedTextBrush);

            for (var c = 0; c < Count; c++)
            {
                var value = c == r ? 1 : Clamp(matrix[r, c]);
                var rect = new Rect(origin.X + c * cell, origin.Y + r * cell, cell, cell);
                context.DrawRectangle(new SolidColorBrush(ColorFor(value)), null, rect.Deflate(0.5));
                if (layout.ShowValues)
                    DrawCentered(context, value.ToString("0.00", CultureInfo.InvariantCulture), rect.Center,
                        value is > -.45 and < .45 ? Brushes.Black : Brushes.White);
                if (hoveredRow == r && hoveredColumn == c && HoverEnabled(layout))
                    context.DrawRectangle(null, new Pen(Brushes.Black, 2), rect.Deflate(1));
            }
        }

        DrawLegend(context, origin, layout.Matrix.Width, layout.LegendY, theme.MutedTextBrush);
    }

    static double Clamp(double value) => double.IsFinite(value) ? Math.Max(-1, Math.Min(1, value)) : 0;

    static Color ColorFor(double value)
    {
        value = Clamp(value);
        if (value >= 0)
        {
            var t = (byte)Math.Round(255 - 150 * value);
            return Color.FromRgb(t, t, 255);
        }
        const byte red = 255;
        var blueGreen = (byte)Math.Round(255 - 150 * -value);
        return Color.FromRgb(red, blueGreen, blueGreen);
    }

    static void DrawLegend(DrawingContext context, Point origin, double width, double y, IBrush textBrush)
    {
        var steps = 24;
        var item = width / steps;
        for (var i = 0; i < steps; i++)
            context.DrawRectangle(new SolidColorBrush(ColorFor(-1 + 2.0 * i / (steps - 1))), null, new Rect(origin.X + i * item, y, item + 1, 8));
        DrawCentered(context, "−1", new Point(origin.X, y + 18), textBrush);
        DrawCentered(context, "0", new Point(origin.X + width / 2, y + 18), textBrush);
        DrawCentered(context, "+1", new Point(origin.X + width, y + 18), textBrush);
    }

    static void DrawLabel(DrawingContext context, string value, Point point, bool right, IBrush brush)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Inter"), 10, brush);
        using (context.PushTransform(Matrix.CreateTranslation(point.X, point.Y)))
            context.DrawText(text, new Point(right ? -text.Width : -text.Width / 2, -text.Height / 2));
    }

    static void DrawColumnLabel(DrawingContext context, string value, Point point, IBrush brush)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Inter"), 10, brush);
        using (context.PushTransform(Matrix.CreateTranslation(point.X, point.Y)))
        using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 4)))
            context.DrawText(text, new Point(0, -text.Height / 2));
    }

    static void DrawCentered(DrawingContext context, string value, Point point, IBrush brush)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Inter"), 10, brush);
        context.DrawText(text, new Point(point.X - text.Width / 2, point.Y - text.Height / 2));
    }

    CorrelationLayout CalculateLayout(Size size)
    {
        const double outer = 10;
        const double legendBlock = 28;
        var labelWidth = MaximumCompactLabelWidth();
        // Reserve a little room for explicit Global/Experiment scope labels while they remain
        // legible, then release the angled labels for large matrices.
        var rowLabels = Math.Clamp(labelWidth + 10, 68, 126);
        var columnOverhang = Math.Clamp(labelWidth * .68 + 8, 24, 92);
        var columnLabels = Math.Clamp(labelWidth * .68 + 12, 42, 100);
        var availableWidth = Math.Max(8, size.Width - 2 * outer - rowLabels - columnOverhang);
        var availableHeight = Math.Max(8, size.Height - 2 * outer - columnLabels - legendBlock);
        var cell = Math.Min(120, Math.Min(availableWidth / Count, availableHeight / Count));
        cell = Math.Max(8, cell);
        var showColumnLabels = cell >= 34;
        if (!showColumnLabels)
        {
            columnOverhang = 8;
            columnLabels = 8;
            availableWidth = Math.Max(8, size.Width - 2 * outer - rowLabels - columnOverhang);
            availableHeight = Math.Max(8, size.Height - 2 * outer - columnLabels - legendBlock);
            cell = Math.Max(8, Math.Min(120, Math.Min(availableWidth / Count, availableHeight / Count)));
        }
        var matrixSize = cell * Count;
        var contentWidth = rowLabels + matrixSize + columnOverhang;
        var contentHeight = columnLabels + matrixSize + legendBlock;
        var contentLeft = Math.Max(outer, (size.Width - contentWidth) / 2);
        var contentTop = Math.Max(outer, (size.Height - contentHeight) / 2);
        var matrix = new Rect(contentLeft + rowLabels, contentTop + columnLabels, matrixSize, matrixSize);
        return new CorrelationLayout(
            matrix,
            cell,
            matrix.Bottom + 14,
            showColumnLabels,
            cell >= 24);
    }

    double MaximumCompactLabelWidth()
    {
        if (labels.Count == 0) return 90;
        return labels
            .Select(label => new FormattedText(CompactLabel(label), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Inter"), 10, Brushes.Black).Width)
            .DefaultIfEmpty(90)
            .Max();
    }

    static string CompactLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "Parameter";
        if (label.StartsWith("Shared · ", StringComparison.Ordinal)) return "Global · " + NormalizeParameterLabel(label[9..]);
        if (label.StartsWith("S · ", StringComparison.Ordinal)) return "Global · " + NormalizeParameterLabel(label[4..]);
        if (label.StartsWith("Local · ", StringComparison.Ordinal)) return "Experiment · " + NormalizeParameterLabel(label[8..]);
        if (label.StartsWith("L · ", StringComparison.Ordinal)) return "Experiment · " + NormalizeParameterLabel(label[4..]);
        if (label.StartsWith("Local (", StringComparison.Ordinal))
        {
            var separator = label.IndexOf(") · ", StringComparison.Ordinal);
            return separator >= 0 ? "Experiment · " + NormalizeParameterLabel(label[(separator + 4)..]) : label;
        }
        if (label.StartsWith("Global · ", StringComparison.Ordinal)) return "Global · " + NormalizeParameterLabel(label[9..]);
        if (label.StartsWith("Experiment · ", StringComparison.Ordinal)) return "Experiment · " + NormalizeParameterLabel(label[13..]);
        return NormalizeParameterLabel(label);
    }

    static string NormalizeParameterLabel(string label)
        => label.Replace("log10 Ka", "log₁₀Ka", StringComparison.Ordinal)
            .Replace("dCp", "ΔCp", StringComparison.Ordinal)
            .Replace("dG", "ΔG", StringComparison.Ordinal)
            .Replace("dH", "ΔH", StringComparison.Ordinal);

    internal Rect MatrixBoundsForTesting => Count == 0 ? default : CalculateLayout(Bounds.Size).Matrix;
    internal double LegendYForTesting => Count == 0 ? 0 : CalculateLayout(Bounds.Size).LegendY;
    internal string CompactLabelForTesting(int index) => CompactLabel(labels[index]);

    readonly struct CorrelationLayout
    {
        public CorrelationLayout(
            Rect matrix,
            double cell,
            double legendY,
            bool showColumnLabels,
            bool showValues)
        {
            Matrix = matrix;
            Cell = cell;
            LegendY = legendY;
            ShowColumnLabels = showColumnLabels;
            ShowValues = showValues;
        }

        public Rect Matrix { get; }
        public double Cell { get; }
        public double LegendY { get; }
        public bool ShowColumnLabels { get; }
        public bool ShowValues { get; }
    }

}
