using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using AppKit;
using CoreGraphics;
using Foundation;

using AnalysisITC.Core.Analysis;

namespace AnalysisITC
{
    /// <summary>
    /// Native macOS presentation of the residual-bootstrap parameter correlation
    /// matrix. The control is intentionally independent from the result
    /// controller so it can be hosted in a fixed plotting viewport and printed
    /// as a normal graph document.
    /// </summary>
    public sealed class CorrelationGraphControl : NSView
    {
        /// <summary>
        /// Hover presentation is deliberately dormant for now.  Keeping the policy internal
        /// lets the layout decide when a future release should expose the cell details without
        /// making this an application setting.
        /// </summary>
        internal enum CorrelationHoverPolicy
        {
            Disabled,
            WhenValuesHidden,
            Always,
        }

        // Keep the geometry in one place.  This mirrors the Avalonia control: labels and
        // legend are part of the matrix composition, rather than being positioned relative
        // to the edge of an opaque plot canvas.
        static readonly nfloat MaximumCellSize = 120;
        static readonly nfloat OuterMargin = 10;
        static readonly nfloat LegendBlock = 28;

        readonly List<string> labels = new List<string>();
        readonly List<string> tooltipLabels = new List<string>();
        double[,] matrix = new double[0, 0];
        string emptyMessage = "No correlation data available.";
        NSTrackingArea trackingArea;
        int hoveredRow = -1;
        int hoveredColumn = -1;
        bool pointerCursorSet;
        CorrelationHoverPolicy hoverPolicy = CorrelationHoverPolicy.Disabled;
        bool printOnWhite;

        /// <summary>
        /// The graph is transparent in the application.  Printing can opt into a white paper
        /// background without changing the on-screen appearance.
        /// </summary>
        public bool PrintOnWhite
        {
            get => printOnWhite;
            set
            {
                if (printOnWhite == value) return;
                printOnWhite = value;
                if (value) ClearHoverPresentation();
                NeedsDisplay = true;
            }
        }

        public CorrelationGraphControl() : this(CGRect.Empty)
        {
        }

        public CorrelationGraphControl(CGRect frame) : base(frame)
        {
            WantsLayer = true;
            Layer.BackgroundColor = NSColor.Clear.CGColor;
            AccessibilityLabel = AccessibleText;
        }

        public override bool IsOpaque => false;

        public IReadOnlyList<string> Labels => labels;

        public int Count => matrix.GetLength(0);

        public double[,] CorrelationMatrix => (double[,])matrix.Clone();

        public bool HasPrintableData => Count > 0;

        public string Method { get; private set; } = string.Empty;

        public string Scope { get; private set; } = string.Empty;

        public string SelectedLabel { get; private set; } = "None";

        public int SelectedCount { get; private set; }

        public int UsedReplicateCount { get; private set; }

        public int OmittedParameterCount { get; private set; }

        public IReadOnlyList<string> OmittedParameterLabels { get; private set; } =
            Array.Empty<string>();

        public bool UnlockedParameters { get; private set; }

        public bool RankWarning { get; private set; }

        public string EmptyMessage => emptyMessage;

        /// <summary>
        /// Internal test/future-feature hook.  The shipped presentation remains Disabled.
        /// </summary>
        internal CorrelationHoverPolicy HoverPolicy
        {
            get => hoverPolicy;
            set
            {
                if (hoverPolicy == value) return;
                hoverPolicy = value;
                // Any policy transition can invalidate the current layout eligibility
                // (notably Always -> WhenValuesHidden), so never retain a tooltip, cursor,
                // or outline across it.
                ClearHoverPresentation();
                NeedsDisplay = true;
            }
        }

        internal CorrelationHoverPolicy HoverPolicyForTesting
        {
            get => HoverPolicy;
            set => HoverPolicy = value;
        }

        internal int HoveredRow => hoveredRow;

        internal int HoveredColumn => hoveredColumn;

        internal (int Row, int Column)? HoveredCellForTesting
        {
            get
            {
                if (hoveredRow < 0 || hoveredColumn < 0) return null;
                return (hoveredRow, hoveredColumn);
            }
        }

        internal string HoverToolTipForTesting => ToolTip;

        internal bool ShowValuesForTesting
            => Count > 0 && CalculateLayout(Bounds.Size).ShowValues;

        internal bool IsHoverPresentationEnabled
        {
            get
            {
                if (hoverPolicy == CorrelationHoverPolicy.Disabled || Count == 0 || printOnWhite)
                    return false;
                if (hoverPolicy == CorrelationHoverPolicy.Always)
                    return true;
                return !CalculateLayout(Bounds.Size).ShowValues;
            }
        }

        /// <summary>
        /// The matrix remains discoverable to assistive technology. Hover presentation is
        /// retained for a future policy change but disabled in the shipped view.
        /// </summary>
        public string AccessibleText => Count == 0
            ? "Parameter correlation matrix is empty."
            : $"Parameter correlation matrix with {Count} parameters ({string.Join(", ", labels)}). Values range from minus one to one.";

        public double GetValue(int row, int column)
        {
            return row >= 0 && row < Count && column >= 0 && column < Count
                ? matrix[row, column]
                : double.NaN;
        }

        public void SetMatrix(
            IEnumerable<string> parameterLabels,
            double[,] values,
            string method = null,
            string scope = null)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var n = values.GetLength(0);
            if (values.GetLength(1) != n)
                throw new ArgumentException(
                    "Correlation matrix must be square.",
                    nameof(values));

            ClearHoverPresentation();

            labels.Clear();
            tooltipLabels.Clear();
            labels.AddRange((parameterLabels ?? Enumerable.Empty<string>())
                .Take(n)
                .Select((label, index) => string.IsNullOrWhiteSpace(label)
                    ? $"Parameter {index + 1}"
                    : CompactLabel(label)));
            while (labels.Count < n)
                labels.Add($"Parameter {labels.Count + 1}");

            tooltipLabels.AddRange(labels);

            matrix = (double[,])values.Clone();
            Method = method ?? string.Empty;
            Scope = scope ?? string.Empty;
            emptyMessage = string.Empty;
            AccessibilityLabel = AccessibleText;
            NeedsDisplay = true;
        }

        public void SetCorrelationResult(
            BootstrapCorrelationResult result,
            int selectedCount,
            string selectedLabel,
            bool isGlobalResult,
            string unavailableMessage = null)
        {
            ClearHoverPresentation();
            SelectedCount = selectedCount;
            SelectedLabel = string.IsNullOrWhiteSpace(selectedLabel)
                ? "None"
                : selectedLabel;
            Method = "Residual bootstrap (Pearson)";
            UsedReplicateCount = result?.UsedReplicateCount ?? 0;
            OmittedParameterCount = result?.OmittedParameterCount ?? 0;
            OmittedParameterLabels = result?.OmittedParameters?
                .Select(parameter => parameter.Label)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToArray()
                ?? Array.Empty<string>();
            UnlockedParameters = result?.Parameters?.Any(
                parameter => parameter.IncludedBecauseBootstrapUnlock) == true;
            RankWarning = result?.IsRankLimited == true;
            Scope = ScopeTitle(result, isGlobalResult, selectedCount);

            if (result == null || !result.IsAvailable || result.CorrelationMatrix == null)
            {
                Clear(unavailableMessage
                    ?? result?.Availability?.Reason
                    ?? "Correlation is unavailable for this result.",
                    preserveMetadata: true);
                return;
            }

            var displayLabels = result.Parameters
                .Select(FormatParameterLabel)
                .ToArray();
            var completeLabels = result.Parameters
                .Select(parameter => FormatTooltipParameterLabel(parameter, SelectedLabel))
                .ToArray();
            SetMatrix(displayLabels, result.CorrelationMatrix, Method, Scope);
            tooltipLabels.Clear();
            tooltipLabels.AddRange(completeLabels);
        }

        public void Clear(string message = "", bool preserveMetadata = false)
        {
            ClearHoverPresentation();
            labels.Clear();
            tooltipLabels.Clear();
            matrix = new double[0, 0];
            emptyMessage = string.IsNullOrWhiteSpace(message)
                ? "No correlation data available."
                : message;
            if (!preserveMetadata)
            {
                Method = string.Empty;
                Scope = string.Empty;
                SelectedLabel = "None";
                SelectedCount = 0;
                UsedReplicateCount = 0;
                OmittedParameterCount = 0;
                OmittedParameterLabels = Array.Empty<string>();
                UnlockedParameters = false;
                RankWarning = false;
            }

            AccessibilityLabel = string.IsNullOrWhiteSpace(message)
                ? AccessibleText
                : message;
            NeedsDisplay = true;
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            base.DrawRect(dirtyRect);

            var context = NSGraphicsContext.CurrentContext?.CGContext;
            if (context == null) return;

            // Do not paint the document view or its margins.  The result workspace supplies
            // the surrounding surface, and transparency is important when this graph is used
            // in the dark themed workspace.  A paper background is only requested by Print().
            if (PrintOnWhite)
            {
                context.SetFillColor(NSColor.White.CGColor);
                context.FillRect(Bounds);
            }

            if (Count == 0)
            {
                DrawEmptyState(context);
                return;
            }

            var layout = CalculateLayout(Bounds.Size);
            var matrixRect = layout.Matrix;
            var cell = layout.Cell;

            // A very quiet frame makes the matrix legible without recreating the old bezel.
            context.SetStrokeColor(FrameColor.CGColor);
            context.SetLineWidth(1);
            context.StrokeRect(matrixRect.Inset((nfloat)0.5, (nfloat)0.5));

            for (var row = 0; row < Count; row++)
            {
                for (var column = 0; column < Count; column++)
                {
                    var value = row == column ? 1 : Clamp(matrix[row, column]);
                    var rect = new CGRect(
                        matrixRect.X + column * cell,
                        matrixRect.Y + (Count - row - 1) * cell,
                        cell,
                        cell);

                    context.SetFillColor(ColorFor(value).CGColor);
                    // A one point gutter is enough to separate cells while retaining the
                    // continuous heatmap feel of the Avalonia renderer.
                    var gutter = (nfloat)Math.Min(1.0, (double)cell * .025);
                    context.FillRect(rect.Inset(gutter, gutter));

                    if (layout.ShowValues)
                        DrawCentered(
                            context,
                            value.ToString("0.00", CultureInfo.InvariantCulture),
                            rect,
                            TextColorFor(value),
                            (nfloat)(cell < 44 ? 10 : 11));

                }
            }

            DrawAxisLabels(context, layout);
            DrawLegend(context, matrixRect.X, layout.LegendY, matrixRect.Width);
            DrawHoverOutline(context, layout);
        }

        public override void SetFrameSize(CGSize newSize)
        {
            base.SetFrameSize(newSize);
            // A resize changes the cell under a pointer. Do not leave a stale outline or
            // tooltip attached to the old geometry until the next mouse event arrives.
            ClearHoverPresentation();
        }

        public override void UpdateTrackingAreas()
        {
            base.UpdateTrackingAreas();

            if (trackingArea != null)
                RemoveTrackingArea(trackingArea);

            trackingArea = new NSTrackingArea(
                Bounds,
                NSTrackingAreaOptions.ActiveInKeyWindow
                    | NSTrackingAreaOptions.InVisibleRect
                    | NSTrackingAreaOptions.MouseEnteredAndExited
                    | NSTrackingAreaOptions.MouseMoved,
                this,
                null);
            AddTrackingArea(trackingArea);
        }

        public override void MouseMoved(NSEvent theEvent)
        {
            base.MouseMoved(theEvent);

            if (!IsHoverPresentationEnabled)
                return;

            var point = ConvertPointFromView(theEvent.LocationInWindow, null);
            var cell = CellAtPoint(point, CalculateLayout(Bounds.Size));
            SetHoveredCell(cell.Row, cell.Column);
        }

        public override void MouseExited(NSEvent theEvent)
        {
            base.MouseExited(theEvent);
            ClearHoverPresentation();
        }

        internal void ClearHoverPresentation()
        {
            var hadHover = hoveredRow >= 0 || hoveredColumn >= 0 || ToolTip != null;
            hoveredRow = -1;
            hoveredColumn = -1;
            ToolTip = null;
            if (pointerCursorSet)
            {
                NSCursor.ArrowCursor.Set();
                pointerCursorSet = false;
            }
            if (hadHover)
                NeedsDisplay = true;
        }

        internal void ClearHoverState() => ClearHoverPresentation();

        void SetHoveredCell(int row, int column)
        {
            if (!IsHoverPresentationEnabled)
            {
                ClearHoverPresentation();
                return;
            }

            if (row < 0 || column < 0 || row >= Count || column >= Count)
            {
                ClearHoverPresentation();
                return;
            }

            if (hoveredRow == row && hoveredColumn == column)
                return;

            hoveredRow = row;
            hoveredColumn = column;
            ToolTip = BuildTooltip(row, column);
            NSCursor.PointingHandCursor.Set();
            pointerCursorSet = true;
            NeedsDisplay = true;
        }

        string BuildTooltip(int row, int column)
        {
            var value = row == column ? 1 : Clamp(matrix[row, column]);
            var rowLabel = row < tooltipLabels.Count ? tooltipLabels[row] : labels[row];
            var columnLabel = column < tooltipLabels.Count ? tooltipLabels[column] : labels[column];
            var lines = new List<string>
            {
                $"{rowLabel} vs {columnLabel}",
            };

            lines.Add($"Pearson r: {value.ToString("0.00", CultureInfo.InvariantCulture)}");
            lines.Add($"Replicates: {UsedReplicateCount.ToString(CultureInfo.InvariantCulture)}");
            return string.Join(Environment.NewLine, lines);
        }

        void DrawHoverOutline(CGContext context, CorrelationLayout layout)
        {
            if (!IsHoverPresentationEnabled
                || hoveredRow < 0
                || hoveredColumn < 0
                || hoveredRow >= Count
                || hoveredColumn >= Count)
                return;

            var cell = layout.Cell;
            var rect = new CGRect(
                layout.Matrix.X + hoveredColumn * cell,
                layout.Matrix.Y + (Count - hoveredRow - 1) * cell,
                cell,
                cell);
            context.SaveState();
            context.SetStrokeColor((PrintOnWhite ? NSColor.Black : NSColor.White).CGColor);
            context.SetLineWidth((nfloat)Math.Min(2, Math.Max(1, (double)cell * .06)));
            context.StrokeRect(rect.Inset((nfloat).75, (nfloat).75));
            context.RestoreState();
        }

        static (int Row, int Column) CellAtPoint(CGPoint point, CorrelationLayout layout)
        {
            if (!layout.Matrix.Contains(point) || layout.Cell <= 0)
                return (-1, -1);

            var column = (int)Math.Floor((point.X - layout.Matrix.X) / layout.Cell);
            var drawnRow = (int)Math.Floor((point.Y - layout.Matrix.Y) / layout.Cell);
            var row = layout.Matrix.Height <= 0 ? -1 : (int)Math.Floor(layout.Matrix.Height / layout.Cell) - 1 - drawnRow;
            return (row, column);
        }

        void DrawEmptyState(CGContext context)
        {
            var width = Math.Max(180, Bounds.Width - 48);
            var message = new NSAttributedString(
                emptyMessage,
                new NSStringAttributes
                {
                    Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                    ForegroundColor = LabelColor,
                    ParagraphStyle = new NSMutableParagraphStyle
                    {
                        Alignment = NSTextAlignment.Center,
                        LineBreakMode = NSLineBreakMode.ByWordWrapping,
                    },
                });
            var size = message.BoundingRectWithSize(
                new CGSize(width, nfloat.MaxValue),
                NSStringDrawingOptions.UsesLineFragmentOrigin
                    | NSStringDrawingOptions.UsesFontLeading);
            message.DrawString(new CGRect(
                (Bounds.Width - width) / 2,
                Math.Max(16, (Bounds.Height - size.Height) / 2),
                width,
                size.Height));
            message.Dispose();
        }

        void DrawAxisLabels(CGContext context, CorrelationLayout layout)
        {
            var matrixRect = layout.Matrix;
            var cell = layout.Cell;
            var font = NSFont.SystemFontOfSize(cell < 38 ? 9 : 10);
            for (var index = 0; index < Count; index++)
            {
                var displayLabel = FitLabelToWidth(
                    CompactLabel(labels[index]),
                    font,
                    layout.AxisLabelWidth);
                var rowY = matrixRect.Y + (Count - index - 0.5f) * cell;
                DrawLabel(
                    context,
                    displayLabel,
                    new CGPoint(matrixRect.X - 10, rowY),
                    font,
                    LabelColor,
                    NSTextAlignment.Right);

                if (layout.ShowColumnLabels)
                    DrawAngledLabel(
                        context,
                        displayLabel,
                        new CGPoint(matrixRect.X + (index + 0.5f) * cell, matrixRect.GetMaxY() + 8),
                        font,
                        LabelColor);
            }
        }

        void DrawLegend(CGContext context, nfloat left, nfloat y, nfloat width)
        {
            var steps = 32;
            var itemWidth = width / steps;
            for (var i = 0; i < steps; i++)
            {
                var value = -1 + 2.0 * i / (steps - 1);
                context.SetFillColor(ColorFor(value).CGColor);
                context.FillRect(new CGRect(left + i * itemWidth, y, itemWidth + 1, 9));
            }

            var font = NSFont.SystemFontOfSize(10);
            DrawLabel(context, "−1", new CGPoint(left, y - 8), font, LabelColor, NSTextAlignment.Left);
            DrawLabel(context, "0", new CGPoint(left + width / 2, y - 8), font, LabelColor, NSTextAlignment.Center);
            DrawLabel(context, "+1", new CGPoint(left + width, y - 8), font, LabelColor, NSTextAlignment.Right);
        }

        static void DrawLabel(
            CGContext context,
            string value,
            CGPoint center,
            NSFont font,
            NSColor color,
            NSTextAlignment alignment)
        {
            var paragraph = new NSMutableParagraphStyle
            {
                Alignment = alignment,
                LineBreakMode = NSLineBreakMode.TruncatingTail,
            };
            var attributed = new NSAttributedString(
                value,
                new NSStringAttributes
                {
                    Font = font,
                    ForegroundColor = color,
                    ParagraphStyle = paragraph,
                });
            var size = attributed.GetSize();
            var x = alignment == NSTextAlignment.Left
                ? center.X
                : alignment == NSTextAlignment.Right
                    ? center.X - size.Width
                    : center.X - size.Width / 2;
            attributed.DrawString(new CGPoint(x, center.Y - size.Height / 2));
            attributed.Dispose();
        }

        static void DrawAngledLabel(
            CGContext context,
            string value,
            CGPoint anchor,
            NSFont font,
            NSColor color)
        {
            context.SaveState();
            context.TranslateCTM(anchor.X, anchor.Y);
            // AppKit's regular NSView coordinates have +Y upward.  Avalonia's -45°
            // top-down label therefore maps to +45° here.
            context.RotateCTM((nfloat)(Math.PI / 4));
            using (var attributed = new NSAttributedString(
                       value,
                       new NSStringAttributes
                       {
                           Font = font,
                           ForegroundColor = color,
                       }))
            {
                var size = attributed.GetSize();
                attributed.DrawString(new CGPoint(0, -size.Height / 2));
            }
            context.RestoreState();
        }

        static void DrawCentered(
            CGContext context,
            string value,
            CGRect rect,
            NSColor color,
            nfloat fontSize)
        {
            var attributed = new NSAttributedString(
                value,
                new NSStringAttributes
                {
                    Font = NSFont.SystemFontOfSize(fontSize),
                    ForegroundColor = color,
                    ParagraphStyle = new NSMutableParagraphStyle
                    {
                        Alignment = NSTextAlignment.Center,
                    },
                });
            var size = attributed.GetSize();
            attributed.DrawString(new CGPoint(
                rect.GetMidX() - size.Width / 2,
                rect.GetMidY() - size.Height / 2));
            attributed.Dispose();
        }

        CorrelationLayout CalculateLayout(CGSize size)
        {
            var labelWidth = MaximumCompactLabelWidth();
            var rowLabels = Math.Max(56, Math.Min(116, labelWidth + 10));
            var columnOverhang = Math.Max(30, Math.Min(88, labelWidth * .62));
            var columnLabels = Math.Max(38, Math.Min(78, labelWidth * .58 + 8));
            var count = Math.Max(1, Count);

            var availableWidth = Math.Max(
                1,
                (double)size.Width - 2 * (double)OuterMargin - rowLabels - columnOverhang);
            var availableHeight = Math.Max(
                1,
                (double)size.Height - 2 * (double)OuterMargin - columnLabels
                    - (double)LegendBlock);
            // The matrix is always fitted to this fixed viewport. Large matrices are allowed
            // to fall below the value-label legibility threshold instead of introducing a
            // document view or scroll bars.
            var cell = (nfloat)Math.Min(
                (double)MaximumCellSize,
                Math.Min(availableWidth / count, availableHeight / count));
            var showColumnLabels = cell >= 26;
            if (!showColumnLabels)
            {
                // Once angled labels are illegible, release their reserved top/right space
                // back to the fixed matrix instead of leaving an empty pseudo-scroll margin.
                columnOverhang = 8;
                columnLabels = 8;
                availableWidth = Math.Max(
                    1,
                    (double)size.Width - 2 * (double)OuterMargin - rowLabels - columnOverhang);
                availableHeight = Math.Max(
                    1,
                    (double)size.Height - 2 * (double)OuterMargin - columnLabels
                        - (double)LegendBlock);
                cell = (nfloat)Math.Min(
                    (double)MaximumCellSize,
                    Math.Min(availableWidth / count, availableHeight / count));
            }

            var matrixSize = cell * count;
            var contentWidth = rowLabels + matrixSize + columnOverhang;
            var contentHeight = columnLabels + matrixSize + LegendBlock;
            var contentLeft = Math.Max(
                0,
                (size.Width - contentWidth) / 2);
            var contentTop = Math.Max(
                0,
                (size.Height - contentHeight) / 2);

            // Layout is calculated top-down like Avalonia, then converted to AppKit's
            // bottom-up view coordinates exactly once.
            var matrixTop = contentTop + columnLabels;
            var matrixBottom = size.Height - matrixTop - matrixSize;
            var matrix = new CGRect(contentLeft + rowLabels, matrixBottom, matrixSize, matrixSize);
            var legendTop = matrixTop + matrixSize + 8;
            var legendY = size.Height - legendTop - 9;
            var axisLabelWidth = Math.Max(42, rowLabels - 8);

            return new CorrelationLayout(
                matrix,
                cell,
                (nfloat)legendY,
                (nfloat)axisLabelWidth,
                showColumnLabels,
                cell >= 29);
        }

        static string FormatParameterLabel(BootstrapCorrelationParameterDescriptor parameter)
        {
            var label = DisplayParameterLabel(parameter.Label)
                + (parameter.IncludedBecauseBootstrapUnlock ? "*" : string.Empty);
            if (parameter.IsShared) return "Global · " + label;
            if (parameter.IsMember) return "Experiment · " + label;
            // Two-site coordinates already carry stable slot suffixes such as N1 and N2.
            // Ordinary single-experiment labels need no scope prefix.
            return label;
        }

        static string FormatTooltipParameterLabel(
            BootstrapCorrelationParameterDescriptor parameter,
            string selectedLabel)
        {
            var label = DisplayParameterLabel(parameter.Label)
                + (parameter.IncludedBecauseBootstrapUnlock ? "*" : string.Empty);
            if (!parameter.IsMember)
                return parameter.IsShared ? "Global · " + label : label;

            var member = string.IsNullOrWhiteSpace(parameter.MemberName)
                ? selectedLabel
                : parameter.MemberName;
            return string.IsNullOrWhiteSpace(member)
                || member.Equals("None", StringComparison.OrdinalIgnoreCase)
                || member.StartsWith("None (", StringComparison.OrdinalIgnoreCase)
                ? "Experiment · " + label
                : "Experiment · " + member + " · " + label;
        }

        static string DisplayParameterLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "Parameter";
            if (label.StartsWith("log10 Ka", StringComparison.Ordinal))
                return "log₁₀Ka" + label.Substring("log10 Ka".Length);
            if (label.StartsWith("dCp", StringComparison.Ordinal))
                return "ΔCp" + label.Substring("dCp".Length);
            if (label.StartsWith("dG", StringComparison.Ordinal))
                return "ΔG" + label.Substring("dG".Length);
            if (label.StartsWith("dH", StringComparison.Ordinal))
                return "ΔH" + label.Substring("dH".Length);
            return label;
        }

        static string ScopeTitle(
            BootstrapCorrelationResult result,
            bool isGlobalResult,
            int selectedCount)
        {
            if (result?.Parameters?.Any(parameter => parameter.IsMember) == true)
                return "Global + selected experiment";
            if (result?.Parameters?.Any(parameter => parameter.IsShared) == true)
                return "Global parameters";
            if (isGlobalResult)
                return selectedCount == 1
                    ? "Global + selected experiment"
                    : "Global parameters";
            return "Single experiment";
        }

        static double Clamp(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? 0
                : Math.Max(-1, Math.Min(1, value));
        }

        static NSColor ColorFor(double value)
        {
            // Keep this exactly in step with ResultCorrelationGraphControl: negative
            // correlations are red, zero is neutral white, and positive correlations are blue.
            value = Clamp(value);
            if (value >= 0)
            {
                var channel = (nfloat)((255 - 150 * value) / 255.0);
                return NSColor.FromCalibratedRgb(channel, channel, 1);
            }

            var redChannel = (nfloat)((255 - 150 * -value) / 255.0);
            return NSColor.FromCalibratedRgb(1, redChannel, redChannel);
        }

        static NSColor TextColorFor(double value)
        {
            value = Clamp(value);
            return value > -.45 && value < .45 ? NSColor.Black : NSColor.White;
        }

        NSColor LabelColor => PrintOnWhite ? NSColor.DarkGray : NSColor.SecondaryLabel;

        NSColor FrameColor => PrintOnWhite
            ? NSColor.LightGray
            : NSColor.Separator.ColorWithAlphaComponent(.55f);

        nfloat MaximumCompactLabelWidth()
        {
            if (labels.Count == 0) return 56;
            var font = NSFont.SystemFontOfSize(10);
            nfloat maximum = 0;
            foreach (var label in labels)
            {
                using (var attributed = new NSAttributedString(
                    CompactLabel(label),
                    new NSStringAttributes { Font = font }))
                {
                    var width = attributed.GetSize().Width;
                    if (width > maximum) maximum = width;
                }
            }
            return maximum;
        }

        static string CompactLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "Parameter";
            if (label.StartsWith("Shared · ", StringComparison.Ordinal))
                return "Global · " + DisplayParameterLabel(label.Substring(9));
            if (label.StartsWith("S · ", StringComparison.Ordinal))
                return "Global · " + DisplayParameterLabel(label.Substring(4));
            if (label.StartsWith("Local · ", StringComparison.Ordinal))
                return "Experiment · " + DisplayParameterLabel(label.Substring(8));
            if (label.StartsWith("L · ", StringComparison.Ordinal))
                return "Experiment · " + DisplayParameterLabel(label.Substring(4));
            if (label.StartsWith("Local (", StringComparison.Ordinal))
            {
                var separator = label.IndexOf(") · ", StringComparison.Ordinal);
                if (separator >= 0)
                    return "Experiment · " + DisplayParameterLabel(label.Substring(separator + 4));
            }
            if (label.StartsWith("Global · ", StringComparison.Ordinal))
                return "Global · " + DisplayParameterLabel(label.Substring(9));
            if (label.StartsWith("Experiment · ", StringComparison.Ordinal))
                return "Experiment · " + DisplayParameterLabel(label.Substring(13));
            return DisplayParameterLabel(label);
        }

        static string FitLabelToWidth(string label, NSFont font, nfloat maximumWidth)
        {
            if (string.IsNullOrEmpty(label)) return string.Empty;
            using (var full = new NSAttributedString(
                label,
                new NSStringAttributes { Font = font }))
            {
                if (full.GetSize().Width <= maximumWidth) return label;
            }

            const string ellipsis = "…";
            for (var length = label.Length - 1; length > 0; length--)
            {
                var candidate = label.Substring(0, length).TrimEnd() + ellipsis;
                using (var attributed = new NSAttributedString(
                    candidate,
                    new NSStringAttributes { Font = font }))
                {
                    if (attributed.GetSize().Width <= maximumWidth)
                        return candidate;
                }
            }
            return ellipsis;
        }

        readonly struct CorrelationLayout
        {
            public CorrelationLayout(
                CGRect matrix,
                nfloat cell,
                nfloat legendY,
                nfloat axisLabelWidth,
                bool showColumnLabels,
                bool showValues)
            {
                Matrix = matrix;
                Cell = cell;
                LegendY = legendY;
                AxisLabelWidth = axisLabelWidth;
                ShowColumnLabels = showColumnLabels;
                ShowValues = showValues;
            }

            public CGRect Matrix { get; }
            public nfloat Cell { get; }
            public nfloat LegendY { get; }
            public nfloat AxisLabelWidth { get; }
            public bool ShowColumnLabels { get; }
            public bool ShowValues { get; }
        }
    }
}
