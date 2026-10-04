using System;
using System.Collections.Generic;
using System.Linq;

using AppKit;
using Foundation;

namespace AnalysisITC.UI.MacOS.CustomViews
{
    [Register("AnalysisFitSummaryView")]
    internal sealed class AnalysisFitSummaryView : NSStackView
    {
        static readonly nfloat ParameterLabelWidth = 72;

        public AnalysisFitSummaryView(IntPtr handle) : base(handle)
        {
        }

        public override void AwakeFromNib()
        {
            base.AwakeFromNib();

            Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            Distribution = NSStackViewDistribution.Fill;
            Alignment = NSLayoutAttribute.CenterX;
            Spacing = 4;
            DetachesHiddenViews = true;
        }

        public void Display(
            IReadOnlyList<AnalysisParameterSummaryRow> rows,
            bool hasSolution)
        {
            ClearContent();

            if (rows == null || rows.Count == 0)
            {
                AddFullWidth(CreateLabel(
                    hasSolution
                        ? "No fit information selected for display."
                        : "No fit result for the selected experiment.",
                    NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                    NSColor.SecondaryLabel,
                    NSTextAlignment.Left,
                    249,
                    250));
                AddFlexibleSpacer();
                return;
            }

            IReadOnlyList<AnalysisParameterSummaryRow> parameterRows = rows;
            if (rows[0].IsModelHeader)
            {
                AddModelHeader(rows[0]);
                var gridRows = rows.Skip(1).ToList();
                if (!string.IsNullOrWhiteSpace(rows[0].Value))
                    gridRows.Insert(0, new AnalysisParameterSummaryRow(
                        "RMSD",
                        rows[0].Value,
                        false));
                parameterRows = gridRows;
            }

            if (parameterRows.Count > 0)
                AddParameterGrid(parameterRows);

            AddFlexibleSpacer();
        }

        void AddModelHeader(AnalysisParameterSummaryRow row)
        {
            var modelText = string.IsNullOrWhiteSpace(row.ModelName)
                ? row.Label
                : row.ModelName;
            var modelLabel = CreateLabel(
                modelText,
                NSFont.SystemFontOfSize(
                    NSFont.SmallSystemFontSize,
                    NSFontWeight.Medium),
                NSColor.Label,
                NSTextAlignment.Left,
                249,
                250);
            modelLabel.ToolTip = PlainText(modelText);
            modelLabel.HorizontalContentSizeConstraintActive = true;
            modelLabel.AddConstraint(NSLayoutConstraint.Create(
                modelLabel,
                NSLayoutAttribute.Width,
                NSLayoutRelation.GreaterThanOrEqual,
                1,
                60));

            var scopeLabel = CreateLabel(
                row.Scope,
                NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                NSColor.SecondaryLabel,
                NSTextAlignment.Right,
                1000,
                1000);
            scopeLabel.ToolTip = PlainText(row.Scope);
            scopeLabel.HorizontalContentSizeConstraintActive = true;
            scopeLabel.LineBreakMode = NSLineBreakMode.Clipping;
            scopeLabel.MaximumNumberOfLines = 1;
            scopeLabel.Cell.Wraps = false;
            scopeLabel.Cell.UsesSingleLineMode = true;

            var header = new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Distribution = NSStackViewDistribution.Fill,
                Alignment = NSLayoutAttribute.FirstBaseline,
                Spacing = 8,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            header.AddArrangedSubview(modelLabel);
            header.AddArrangedSubview(scopeLabel);
            AddFullWidth(header);

            var separator = new NSBox
            {
                BoxType = NSBoxType.NSBoxSeparator,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            separator.AddConstraint(NSLayoutConstraint.Create(
                separator,
                NSLayoutAttribute.Height,
                NSLayoutRelation.Equal,
                1,
                5));
            AddFullWidth(separator);
        }

        void AddParameterGrid(
            IReadOnlyList<AnalysisParameterSummaryRow> rows)
        {
            var views = rows
                .Select(row => new NSView[]
                {
                    CreateParameterLabel(row.Label),
                    CreateValueLabel(row.Value),
                })
                .ToArray();
            var grid = NSGridView.Create(views);
            grid.ColumnSpacing = 8;
            grid.RowSpacing = 2;
            grid.RowAlignment = NSGridRowAlignment.FirstBaseline;
            grid.X = NSGridCellPlacement.Fill;
            grid.Y = NSGridCellPlacement.Center;
            grid.GetColumn(0).Width = ParameterLabelWidth;
            grid.GetColumn(0).X = NSGridCellPlacement.Leading;
            grid.GetColumn(1).X = NSGridCellPlacement.Fill;
            for (var index = 0; index < rows.Count; index++)
                grid.GetCell(1, index).X = NSGridCellPlacement.Trailing;
            grid.TranslatesAutoresizingMaskIntoConstraints = false;
            AddFullWidth(grid);
        }

        NSTextField CreateParameterLabel(string text)
        {
            var label = CreateLabel(
                text,
                NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                NSColor.SecondaryLabel,
                NSTextAlignment.Left,
                750,
                1000);
            label.ToolTip = PlainText(text);
            return label;
        }

        NSTextField CreateValueLabel(string text)
        {
            var label = CreateLabel(
                text,
                NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                NSColor.Label,
                NSTextAlignment.Right,
                249,
                250);
            label.ToolTip = PlainText(text);
            return label;
        }

        static NSTextField CreateLabel(
            string markdown,
            NSFont font,
            NSColor color,
            NSTextAlignment alignment,
            float horizontalHugging,
            float horizontalCompressionResistance)
        {
            var attributed = MacStrings.FromMarkDownString(
                markdown ?? string.Empty,
                font);
            attributed.AddAttribute(
                NSStringAttributeKey.ForegroundColor,
                color,
                new NSRange(0, attributed.Length));

            var label = new NSTextField
            {
                AttributedStringValue = attributed,
                Bordered = false,
                Editable = false,
                Selectable = false,
                DrawsBackground = false,
                FocusRingType = NSFocusRingType.None,
                Alignment = alignment,
                LineBreakMode = NSLineBreakMode.ByWordWrapping,
                MaximumNumberOfLines = 0,
                HorizontalContentSizeConstraintActive = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            label.Cell.Wraps = true;
            label.Cell.Scrollable = false;
            label.Cell.UsesSingleLineMode = false;
            label.SetContentHuggingPriorityForOrientation(
                horizontalHugging,
                NSLayoutConstraintOrientation.Horizontal);
            label.SetContentCompressionResistancePriority(
                horizontalCompressionResistance,
                NSLayoutConstraintOrientation.Horizontal);
            return label;
        }

        void AddFullWidth(NSView view)
        {
            view.TranslatesAutoresizingMaskIntoConstraints = false;
            AddArrangedSubview(view);
            AddConstraint(NSLayoutConstraint.Create(
                view,
                NSLayoutAttribute.Width,
                NSLayoutRelation.Equal,
                this,
                NSLayoutAttribute.Width,
                1,
                0));
        }

        void AddFlexibleSpacer()
        {
            var spacer = new NSView
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            spacer.SetContentHuggingPriorityForOrientation(
                1,
                NSLayoutConstraintOrientation.Vertical);
            spacer.SetContentCompressionResistancePriority(
                1,
                NSLayoutConstraintOrientation.Vertical);
            AddFullWidth(spacer);
        }

        void ClearContent()
        {
            foreach (var view in ArrangedSubviews.ToArray())
            {
                RemoveArrangedSubview(view);
                view.RemoveFromSuperview();
                view.Dispose();
            }
        }

        static string PlainText(string markdown)
        {
            return string.Concat(
                Core.Utilities.MarkdownProcessor
                    .GetSegments(markdown ?? string.Empty)
                    .Select(segment => segment.Text));
        }
    }
}
