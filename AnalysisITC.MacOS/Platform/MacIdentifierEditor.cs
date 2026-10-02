using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC.Core.Data;

namespace AnalysisITC.UI.MacOS
{
    static class MacIdentifierEditor
    {
        public static Task ReviewImportedExperiments(IReadOnlyList<ExperimentData> experiments)
        {
            if (experiments == null || experiments.Count == 0) return Task.CompletedTask;
            return new Editor(experiments, reviewsImport: true).RunAsync();
        }

        public static Task EditExperiment(ExperimentData experiment)
        {
            if (experiment == null) return Task.CompletedTask;
            return new Editor(new[] { experiment }, reviewsImport: false).RunAsync();
        }

        internal static string ApplySharedValue(string current, string shared, bool replace)
        {
            var value = shared?.Trim() ?? "";
            if (value.Length == 0) return current ?? "";
            return replace || string.IsNullOrWhiteSpace(current) ? value : current;
        }

        sealed class Row
        {
            public ExperimentData Experiment;
            public NSButton Selected;
            public NSTextField ExternalId;
            public NSTextField CellId;
            public NSTextField SyringeId;

            // A single experiment has no selection column; shared values then apply to it.
            public bool IsSelected => Selected == null || Selected.State == NSCellStateValue.On;
        }

        sealed class Editor
        {
            const int ApplyResponse = 1000;
            const int SkipResponse = 1001;
            const float SelectionWidth = 18;
            const float NameWidth = 200;
            const float IdentifierWidth = 180;
            const float ColumnSpacing = 12;
            const float FormFieldWidth = 260;
            const float MaximumRowsHeight = 340;
            const float Inset = 20;
            const float PanelPadding = 12;
            // Room for a legacy (always visible) vertical scroller beside the last column.
            const float ScrollerAllowance = 16;

            readonly List<Row> rows = new List<Row>();
            readonly bool showsTable;
            readonly NSPanel panel;
            NSWindow host;
            NSButton selectAll;
            NSTextField sharedCellId;
            NSTextField sharedSyringeId;
            NSButton fillBlanks;
            NSButton replaceValues;
            TaskCompletionSource<bool> completion;

            public Editor(IReadOnlyList<ExperimentData> experiments, bool reviewsImport)
            {
                showsTable = experiments.Count > 1;

                var heading = Label(!reviewsImport ? "Experiment Identifiers"
                    : showsTable ? "Identify Imported Experiments" : "Identify Imported Experiment");
                heading.Font = NSFont.BoldSystemFontOfSize(NSFont.SystemFontSize + 2);
                var description = NSTextField.CreateWrappingLabel(reviewsImport
                    ? "Add laboratory identifiers for the imported data. All fields are optional and values may repeat. Skip keeps the imported data without identifiers."
                    : "All fields are optional and values may repeat between experiments.");
                description.TranslatesAutoresizingMaskIntoConstraints = false;
                description.TextColor = NSColor.SecondaryLabel;

                var sections = showsTable
                    ? new[] { Section("Experiments", BuildTable(experiments)), Section("Shared sample IDs", BuildSharedRows()) }
                    : new[] { Section("Identifiers", BuildForm(experiments[0])) };
                var contentWidth = (float)Math.Ceiling(sections.Max(section => section.FittingSize.Width));
                description.PreferredMaxLayoutWidth = contentWidth;

                var skip = Button(reviewsImport ? "Skip" : "Cancel");
                skip.KeyEquivalent = "\u001b";
                skip.Activated += (sender, e) => Finish(SkipResponse);
                var apply = Button("Apply");
                apply.KeyEquivalent = "\r";
                apply.Activated += (sender, e) => Finish(ApplyResponse);
                var footer = new NSStackView
                {
                    Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                    Spacing = 8,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                footer.AddView(skip, NSStackViewGravity.Trailing);
                footer.AddView(apply, NSStackViewGravity.Trailing);
                skip.WidthAnchor.ConstraintGreaterThanOrEqualToConstant(80).Active = true;
                apply.WidthAnchor.ConstraintEqualToAnchor(skip.WidthAnchor).Active = true;

                var content = new NSStackView
                {
                    Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                    Alignment = NSLayoutAttribute.Leading,
                    Spacing = 12,
                    EdgeInsets = new NSEdgeInsets(Inset, Inset, Inset, Inset),
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                content.AddArrangedSubview(heading);
                content.AddArrangedSubview(description);
                foreach (var section in sections) content.AddArrangedSubview(section);
                content.AddArrangedSubview(footer);
                content.SetCustomSpacing(4, heading);
                content.SetCustomSpacing(16, description);
                content.SetCustomSpacing(20, sections.Last());
                foreach (var view in new NSView[] { description, footer }.Concat(sections))
                    view.WidthAnchor.ConstraintEqualToConstant(contentWidth).Active = true;

                panel = new NSPanel(new CGRect(0, 0, contentWidth + 2 * Inset, 300),
                    NSWindowStyle.Titled, NSBackingStore.Buffered, false)
                {
                    Title = reviewsImport ? "Experiment Identifiers" : "Edit Experiment Identifiers",
                    ReleasedWhenClosed = false,
                };
                panel.ContentView = content;
                panel.SetContentSize(content.FittingSize);
                panel.DefaultButtonCell = apply.Cell;
                UpdateSharedActions();
            }

            public Task RunAsync()
            {
                completion = new TaskCompletionSource<bool>();
                host = NSApplication.SharedApplication.MainWindow ?? NSApplication.SharedApplication.KeyWindow;
                if (host != null)
                {
                    host.BeginSheet(panel, response => Complete(response == ApplyResponse));
                }
                else
                {
                    // No document window to attach to: fall back to an application-modal panel.
                    panel.Center();
                    var response = NSApplication.SharedApplication.RunModalForWindow(panel);
                    panel.OrderOut(null);
                    Complete(response == ApplyResponse);
                }
                return completion.Task;
            }

            void Finish(int response)
            {
                if (host != null) host.EndSheet(panel, response);
                else NSApplication.SharedApplication.StopModalWithCode(response);
            }

            void Complete(bool applied)
            {
                if (applied)
                {
                    foreach (var row in rows)
                    {
                        row.Experiment.ExternalExperimentId = row.ExternalId.StringValue;
                        row.Experiment.CellSampleId = row.CellId.StringValue;
                        row.Experiment.SyringeSampleId = row.SyringeId.StringValue;
                    }
                }
                completion.TrySetResult(applied);
            }

            NSView BuildForm(ExperimentData experiment)
            {
                var row = NewRow(experiment, selectable: false);
                var name = Label(experiment.Name);
                name.ToolTip = experiment.FileName;
                name.LineBreakMode = NSLineBreakMode.TruncatingTail;
                var grid = NSGridView.Create(new[]
                {
                    new NSView[] { FormLabel("Experiment"), name },
                    new NSView[] { FormLabel("Experiment ID"), row.ExternalId },
                    new NSView[] { FormLabel("Cell sample/batch ID"), row.CellId },
                    new NSView[] { FormLabel("Syringe sample/batch ID"), row.SyringeId },
                });
                grid.TranslatesAutoresizingMaskIntoConstraints = false;
                grid.RowAlignment = NSGridRowAlignment.FirstBaseline;
                grid.RowSpacing = 8;
                grid.ColumnSpacing = 12;
                grid.GetColumn(0).X = NSGridCellPlacement.Trailing;
                grid.GetColumn(1).Width = FormFieldWidth;
                grid.GetColumn(1).X = NSGridCellPlacement.Fill;
                return grid;
            }

            NSView BuildTable(IReadOnlyList<ExperimentData> experiments)
            {
                selectAll = Checkbox(true);
                selectAll.AllowsMixedState = true;
                selectAll.ToolTip = "Check or uncheck all experiments";
                selectAll.Activated += (sender, e) => SetAllSelected(!rows.All(row => row.IsSelected));

                var header = TableGrid(new[]
                {
                    new NSView[] { selectAll, Header("Experiment"), Header("Experiment ID"), Header("Cell sample/batch ID"), Header("Syringe sample/batch ID") },
                });

                var rowViews = new List<NSView[]>();
                foreach (var experiment in experiments)
                {
                    var row = NewRow(experiment, selectable: true);
                    var name = Label(experiment.Name);
                    name.ToolTip = experiment.FileName;
                    name.LineBreakMode = NSLineBreakMode.TruncatingTail;
                    name.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
                    rowViews.Add(new NSView[] { row.Selected, name, row.ExternalId, row.CellId, row.SyringeId });
                }
                var rowsGrid = TableGrid(rowViews.ToArray());
                rowsGrid.RowSpacing = 8;

                var document = new FlippedView { TranslatesAutoresizingMaskIntoConstraints = false };
                document.AddSubview(rowsGrid);
                rowsGrid.TopAnchor.ConstraintEqualToAnchor(document.TopAnchor, 8).Active = true;
                rowsGrid.LeadingAnchor.ConstraintEqualToAnchor(document.LeadingAnchor).Active = true;
                rowsGrid.BottomAnchor.ConstraintEqualToAnchor(document.BottomAnchor, -4).Active = true;
                var scroll = new NSScrollView
                {
                    HasVerticalScroller = true,
                    AutohidesScrollers = true,
                    BorderType = NSBorderType.NoBorder,
                    DrawsBackground = false,
                    DocumentView = document,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                document.LeadingAnchor.ConstraintEqualToAnchor(scroll.ContentView.LeadingAnchor).Active = true;
                document.TrailingAnchor.ConstraintEqualToAnchor(scroll.ContentView.TrailingAnchor).Active = true;
                document.TopAnchor.ConstraintEqualToAnchor(scroll.ContentView.TopAnchor).Active = true;
                var rowsHeight = rowsGrid.FittingSize.Height + 12;
                scroll.HeightAnchor.ConstraintEqualToConstant((nfloat)Math.Min(rowsHeight, MaximumRowsHeight)).Active = true;
                scroll.WidthAnchor.ConstraintEqualToConstant(TableWidth + ScrollerAllowance).Active = true;

                var separator = Separator();
                var stack = VerticalStack(0, header, separator, scroll);
                stack.SetCustomSpacing(8, header);
                separator.WidthAnchor.ConstraintEqualToAnchor(scroll.WidthAnchor).Active = true;
                return stack;
            }

            NSView BuildSharedRows()
            {
                sharedCellId = Field("Cell sample/batch ID", "");
                sharedSyringeId = Field("Syringe sample/batch ID", "");
                sharedCellId.Changed += (sender, e) => UpdateSharedActions();
                sharedSyringeId.Changed += (sender, e) => UpdateSharedActions();
                fillBlanks = Button("Fill Blanks");
                fillBlanks.ToolTip = "Enter the shared IDs only where checked experiments are blank.";
                fillBlanks.Activated += (sender, e) => ApplyShared(replace: false);
                replaceValues = Button("Replace Values");
                replaceValues.ToolTip = "Overwrite the cell or syringe IDs of all checked experiments.";
                replaceValues.Activated += (sender, e) => ApplyShared(replace: true);
                var buttons = new NSStackView
                {
                    Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                    Spacing = 8,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                buttons.AddArrangedSubview(fillBlanks);
                buttons.AddArrangedSubview(replaceValues);

                var label = NSTextField.CreateWrappingLabel("Enter a cell or syringe ID once and apply it to the checked experiments.");
                label.TranslatesAutoresizingMaskIntoConstraints = false;
                label.TextColor = NSColor.SecondaryLabel;
                label.PreferredMaxLayoutWidth = NameWidth + IdentifierWidth + ColumnSpacing;
                var empty = NSGridCell.EmptyContentView;
                var grid = TableGrid(new[]
                {
                    new NSView[] { empty, label, empty, sharedCellId, sharedSyringeId },
                    new NSView[] { empty, empty, empty, buttons, empty },
                });
                grid.RowSpacing = 8;
                grid.MergeCells(new NSRange(1, 2), new NSRange(0, 1));
                grid.MergeCells(new NSRange(3, 2), new NSRange(1, 1));
                grid.GetCell(3, 1).X = NSGridCellPlacement.Trailing;
                return grid;
            }

            static float TableWidth => SelectionWidth + NameWidth + 3 * IdentifierWidth + 4 * ColumnSpacing;

            static NSGridView TableGrid(NSView[][] views)
            {
                var grid = NSGridView.Create(views);
                grid.TranslatesAutoresizingMaskIntoConstraints = false;
                grid.RowAlignment = NSGridRowAlignment.FirstBaseline;
                grid.ColumnSpacing = ColumnSpacing;
                grid.GetColumn(0).Width = SelectionWidth;
                grid.GetColumn(1).Width = NameWidth;
                for (var column = 2; column < 5; column++)
                {
                    grid.GetColumn(column).Width = IdentifierWidth;
                    grid.GetColumn(column).X = NSGridCellPlacement.Fill;
                }
                return grid;
            }

            Row NewRow(ExperimentData experiment, bool selectable)
            {
                var row = new Row
                {
                    Experiment = experiment,
                    Selected = selectable ? Checkbox(true) : null,
                    ExternalId = Field("Optional", experiment.ExternalExperimentId),
                    CellId = Field("Optional", experiment.CellSampleId),
                    SyringeId = Field("Optional", experiment.SyringeSampleId),
                };
                if (row.Selected != null) row.Selected.Activated += (sender, e) => SelectionChanged();
                rows.Add(row);
                return row;
            }

            void SetAllSelected(bool selected)
            {
                foreach (var row in rows.Where(row => row.Selected != null))
                    row.Selected.State = selected ? NSCellStateValue.On : NSCellStateValue.Off;
                SelectionChanged();
            }

            void SelectionChanged()
            {
                var selected = rows.Count(row => row.IsSelected);
                if (selectAll != null)
                    selectAll.State = selected == rows.Count ? NSCellStateValue.On
                        : selected == 0 ? NSCellStateValue.Off : NSCellStateValue.Mixed;
                UpdateSharedActions();
            }

            void UpdateSharedActions()
            {
                if (fillBlanks == null) return;
                var hasValue = !string.IsNullOrWhiteSpace(sharedCellId.StringValue) || !string.IsNullOrWhiteSpace(sharedSyringeId.StringValue);
                var enabled = hasValue && rows.Any(row => row.IsSelected);
                fillBlanks.Enabled = enabled;
                replaceValues.Enabled = enabled;
            }

            void ApplyShared(bool replace)
            {
                foreach (var row in rows.Where(row => row.IsSelected))
                {
                    row.CellId.StringValue = ApplySharedValue(row.CellId.StringValue, sharedCellId.StringValue, replace);
                    row.SyringeId.StringValue = ApplySharedValue(row.SyringeId.StringValue, sharedSyringeId.StringValue, replace);
                }
            }

            // Matches the outlined sections of the Experiment Details popover.
            static NSView Section(string title, NSView body)
            {
                var heading = Label(title);
                heading.Font = NSFont.BoldSystemFontOfSize(13);
                var stack = VerticalStack(10, heading, body);
                var section = new SectionPanelView { TranslatesAutoresizingMaskIntoConstraints = false };
                section.AddSubview(stack);
                stack.TopAnchor.ConstraintEqualToAnchor(section.TopAnchor, PanelPadding).Active = true;
                stack.BottomAnchor.ConstraintEqualToAnchor(section.BottomAnchor, -PanelPadding).Active = true;
                stack.LeadingAnchor.ConstraintEqualToAnchor(section.LeadingAnchor, PanelPadding).Active = true;
                stack.TrailingAnchor.ConstraintLessThanOrEqualToAnchor(section.TrailingAnchor, -PanelPadding).Active = true;
                return section;
            }

            static NSStackView VerticalStack(float spacing, params NSView[] views)
            {
                var stack = new NSStackView
                {
                    Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                    Alignment = NSLayoutAttribute.Leading,
                    Spacing = spacing,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                foreach (var view in views) stack.AddArrangedSubview(view);
                return stack;
            }

            static NSTextField Label(string text)
            {
                var label = NSTextField.CreateLabel(text ?? "");
                label.TranslatesAutoresizingMaskIntoConstraints = false;
                return label;
            }

            static NSTextField FormLabel(string text)
            {
                var label = Label(text);
                label.TextColor = NSColor.SecondaryLabel;
                return label;
            }

            static NSTextField Header(string text)
            {
                var label = Label(text);
                label.Font = NSFont.BoldSystemFontOfSize(NSFont.SmallSystemFontSize);
                label.TextColor = NSColor.SecondaryLabel;
                label.LineBreakMode = NSLineBreakMode.TruncatingTail;
                return label;
            }

            static NSTextField Field(string placeholder, string value)
            {
                var field = new NSTextField
                {
                    StringValue = value ?? "",
                    PlaceholderString = placeholder,
                    Bezeled = true,
                    BezelStyle = NSTextFieldBezelStyle.Rounded,
                    DrawsBackground = true,
                    Editable = true,
                    Selectable = true,
                    Font = NSFont.SystemFontOfSize(13),
                    LineBreakMode = NSLineBreakMode.TruncatingTail,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                    AccessibilityLabel = placeholder,
                };
                field.Cell.Scrollable = true;
                return field;
            }

            static NSButton Checkbox(bool selected)
            {
                var checkbox = new NSButton { Title = "", TranslatesAutoresizingMaskIntoConstraints = false };
                checkbox.SetButtonType(NSButtonType.Switch);
                checkbox.State = selected ? NSCellStateValue.On : NSCellStateValue.Off;
                return checkbox;
            }

            static NSButton Button(string title) => new NSButton
            {
                Title = title,
                BezelStyle = NSBezelStyle.Rounded,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };

            static NSBox Separator() => new NSBox
            {
                BoxType = NSBoxType.NSBoxSeparator,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
        }

        sealed class SectionPanelView : NSView
        {
            public override void DrawRect(CGRect dirtyRect)
            {
                base.DrawRect(dirtyRect);
                var rect = new CGRect(0.5, 0.5, Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 1));
                using (var path = NSBezierPath.FromRoundedRect(rect, 4, 4))
                {
                    NSColor.ControlBackground.SetFill();
                    path.Fill();
                    NSColor.Separator.SetStroke();
                    path.LineWidth = 0.5f;
                    path.Stroke();
                }
            }
        }

        sealed class FlippedView : NSView
        {
            public override bool IsFlipped => true;
        }
    }
}
