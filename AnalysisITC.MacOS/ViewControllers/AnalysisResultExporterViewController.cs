// This file provides the storyboard-backed controller for exporting analysis results.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppKit;
using AnalysisITC.Core.Analysis.Models;
using CoreGraphics;
using Foundation;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC
{
    [Register("AnalysisResultExporterViewController")]
    public partial class AnalysisResultExporterViewController : NSViewController
    {
        AnalysisResultExporterDataSource dataSource;
        AnalysisResultExporterDelegate tableDelegate;
        readonly NSPopUpButton outputPurposePopup = new NSPopUpButton();

        public AnalysisResultExporterViewController(IntPtr handle) : base(handle)
        {
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            dataSource = new AnalysisResultExporterDataSource();
            tableDelegate = new AnalysisResultExporterDelegate(dataSource);

            ListView.AllowsMultipleSelection = true;
            ListView.DataSource = dataSource;
            ListView.Delegate = tableDelegate;
            ListView.ReloadData();

            SetupOutputPurposeControl();

            SetupToolTips();
            SelectDefaultRows();
        }

        public override void ViewWillAppear()
        {
            base.ViewWillAppear();

            StatusBarManager.SetStatus(StateManager.ProgramSubStateString(ProgramSubState.ResultExporter), -1);
        }

        public override void ViewWillDisappear()
        {
            base.ViewWillDisappear();

            StatusBarManager.ClearAppStatus();
        }

        void SelectDefaultRows()
        {
            var selectedResult = DataManager.SelectedResult;
            var indexes = new NSMutableIndexSet();

            if (selectedResult != null)
            {
                var index = dataSource.Items.IndexOf(selectedResult);
                if (index >= 0) indexes.Add((nuint)index);
            }
            else
            {
                for (int i = 0; i < dataSource.Items.Count; i++) indexes.Add((nuint)i);
            }

            ListView.SelectRows(indexes, byExtendingSelection: false);
        }

        List<AnalysisResult> SelectedResults()
        {
            var results = new List<AnalysisResult>();

            ListView.SelectedRows.EnumerateIndexes((nuint index, ref bool stop) =>
            {
                if (index < (nuint)dataSource.Items.Count)
                    results.Add(dataSource.Items[(int)index]);
            });

            return results;
        }

        AnalysisResultExportOptions CurrentOptions()
        {
            return new AnalysisResultExportOptions
            {
                RowMode = ExportTypeControl.SelectedSegment == 1 ? AnalysisResultExportRowMode.AllRows : AnalysisResultExportRowMode.Summary,
                ErrorStyle = ErrorTypeControl.SelectedSegment == 1 ? AnalysisResultExportErrorStyle.SeparateColumns : AnalysisResultExportErrorStyle.ValueWithError,
                FileFormat = ExportFormatControl.SelectedSegment == 1 ? AnalysisResultExportFileFormat.TSV : AnalysisResultExportFileFormat.CSV,
                UncertaintyDisplayStyle = ExportUncertaintyStyle(),
                EnergyUnitFamily = AppSettings.EnergyUnitFamily,
                EnergyUnitOverride = ExportEnergyUnitOverride(),
                UseKelvin = false,
                OutputPurpose = outputPurposePopup.IndexOfSelectedItem == 1
                    ? ResultOutputPurpose.Diagnostic : ResultOutputPurpose.Standard,
            };
        }

        void SetupOutputPurposeControl()
        {
            outputPurposePopup.AddItems(new[] { "Standard export", "Diagnostic export" });
            outputPurposePopup.SelectItem(0);
            outputPurposePopup.ToolTip = "Diagnostic exports include binding-model parameters for results assessed as no binding detected.";
            var label = new NSTextField { StringValue = "Result output", Editable = false, Bordered = false,
                DrawsBackground = false, Font = NSFont.SystemFontOfSize(12) };
            label.SetContentHuggingPriorityForOrientation(999, NSLayoutConstraintOrientation.Horizontal);
            var row = new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Alignment = NSLayoutAttribute.Top,
                Distribution = NSStackViewDistribution.Fill,
                Spacing = 4,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            row.AddArrangedSubview(label);
            row.AddArrangedSubview(outputPurposePopup);
            outputPurposePopup.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);
            var root = FindPurposeHost(View);
            if (root == null) return;
            root.InsertArrangedSubview(row, Math.Min(2, (int)root.ArrangedSubviews.Length));
            row.WidthAnchor.ConstraintEqualToAnchor(root.WidthAnchor, -36).Active = true;
        }

        NSStackView FindPurposeHost(NSView view)
        {
            if (view is NSStackView stack && stack.Orientation == NSUserInterfaceLayoutOrientation.Vertical
                && stack.ArrangedSubviews.Any(child => child is NSStackView nested
                    && ContainsControl(nested, ExportTypeControl))) return stack;
            foreach (var child in view.Subviews ?? Array.Empty<NSView>())
                if (FindPurposeHost(child) is NSStackView found) return found;
            return null;
        }

        static bool ContainsControl(NSView view, NSView target) => ReferenceEquals(view, target)
            || (view.Subviews ?? Array.Empty<NSView>()).Any(child => ContainsControl(child, target));

        EnergyUnit? ExportEnergyUnitOverride()
        {
            return (int)(EnergyUnitControl?.SelectedSegment ?? 0) switch
            {
                1 => EnergyUnit.Joule,
                2 => EnergyUnit.KiloJoule,
                3 => EnergyUnit.Cal,
                4 => EnergyUnit.KCal,
                _ => (EnergyUnit?)null,
            };
        }

        UncertaintyDisplayStyle ExportUncertaintyStyle()
        {
            return (int)UncertaintyStyleControl.SelectedSegment switch
            {
                1 => UncertaintyDisplayStyle.ConfidenceInterval,
                2 => UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
                _ => UncertaintyDisplayStyle.StandardDeviation,
            };
        }

        void SetupToolTips()
        {
            ExportTypeControl.ToolTip = "Summary thermodynamic values use Parameter Evaluation at its default temperature; replicate rows retain their fitted values.";
            ErrorTypeControl.ToolTip = "Choose inline publication-style values or data-style value and uncertainty columns.";
            UncertaintyStyleControl.ToolTip = "Choose standard deviation, interval bounds, or both. Local summaries use Combined SD and approximate propagated intervals; model-estimated intervals remain CI95.";
            ExportFormatControl.ToolTip = "Choose comma-separated CSV or tab-separated TSV output.";
            EnergyUnitControl.ToolTip = "Choose Automatic or a fixed energy unit for all exported energy columns.";
            ListView.ToolTip = "Select the analysis results to include in the export.";

            SetButtonToolTip("Cancel", "Close the exporter without copying or writing a file.");
            SetButtonToolTip("Copy to Clipboard", "Copy the configured analysis result table to the clipboard.");
            SetButtonToolTip("Export to File...", "Write the configured analysis result table to a CSV or TSV file.");
        }

        void SetButtonToolTip(string title, string toolTip)
        {
            foreach (var button in FindSubviews<NSButton>(View))
            {
                if (button.Title == title) button.ToolTip = toolTip;
            }
        }

        IEnumerable<T> FindSubviews<T>(NSView view) where T : NSView
        {
            foreach (var subview in view.Subviews)
            {
                if (subview is T typed) yield return typed;

                foreach (var match in FindSubviews<T>(subview))
                    yield return match;
            }
        }

        string BuildOutput()
        {
            var selected = SelectedResults();

            if (selected.Count == 0)
            {
                AppEventHandler.DisplayHandledException(new HandledException(
                    HandledException.Severity.Warning,
                    "No Analysis Results Selected",
                    "Select one or more analysis results to export."));

                return null;
            }

            return AnalysisResultTableExporter.Build(selected, CurrentOptions());
        }

        partial void CopyToClipboard(NSObject sender)
        {
            try
            {
                var output = BuildOutput();
                if (string.IsNullOrEmpty(output)) return;

                NSPasteboard.GeneralPasteboard.ClearContents();
                NSPasteboard.GeneralPasteboard.SetStringForType(output, "NSStringPboardType");

                StatusBarManager.SetStatus("Analysis result table copied to clipboard", 3000);
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        partial void ExportToFile(NSObject sender)
        {
            try
            {
                var output = BuildOutput();
                if (string.IsNullOrEmpty(output)) return;

                var options = CurrentOptions();
                var panel = NSSavePanel.SavePanel;
                panel.Title = "Export Analysis Results";
                panel.NameFieldStringValue = "analysis_results." + options.FileExtension;
                panel.AllowedFileTypes = new[] { options.FileExtension };
                panel.CanCreateDirectories = true;

                panel.BeginSheet(View.Window, result =>
                {
                    if (result != (int)NSModalResponse.OK || panel.Url == null) return;

                    try
                    {
                        File.WriteAllText(panel.Url.Path, output);
                        StatusBarManager.SetStatus("Analysis result table exported", 3000);
                    }
                    catch (Exception ex)
                    {
                        AppEventHandler.DisplayHandledException(ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
        }
    }

    class AnalysisResultExporterDataSource : NSTableViewDataSource
    {
        public List<AnalysisResult> Items { get; } = DataManager.Results;

        public override nint GetRowCount(NSTableView tableView) => Items.Count;
    }

    class AnalysisResultExporterDelegate : NSTableViewDelegate
    {
        readonly AnalysisResultExporterDataSource source;

        public AnalysisResultExporterDelegate(AnalysisResultExporterDataSource source)
        {
            this.source = source;
        }

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            return new AnalysisResultExporterCell(source.Items[(int)row], tableColumn.Width);
        }

        [Export("tableView:heightOfRow:")]
        public override nfloat GetRowHeight(NSTableView tableView, nint row) => 62;

        [Export("tableView:rowViewForRow:")]
        public override NSTableRowView CoreGetRowView(NSTableView tableView, nint row)
        {
            return new ExperimentMergerRowView();
        }
    }

    class AnalysisResultExporterCell : NSView
    {
        const double RowHeight = 62;

        public AnalysisResultExporterCell(AnalysisResult result, nfloat width) : base(new CGRect(0, 0, width, RowHeight))
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable;

            var model = GetModelName(result);
            var count = result.Solution.Solutions.Count;
            var details = $"{model} | {count} experiment" + (count == 1 ? "" : "s");

            AddSubview(MakeLabel(result.Name, 8, 38, width - 16, 17, NSFont.BoldSystemFontOfSize(13)));
            AddSubview(MakeLabel("Date: " + result.Date.ToString("g") + " • Analysis operator: "
                + (string.IsNullOrWhiteSpace(result.OperatorName) ? "Not recorded" : result.OperatorName),
                8, 22, width - 16, 14, NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)));
            AddSubview(MakeLabel(details, 8, 6, width - 16, 14, NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)));
        }

        static NSTextField MakeLabel(string text, nfloat x, nfloat y, nfloat width, nfloat height, NSFont font)
        {
            return new NSTextField(new CGRect(x, y, width, height))
            {
                StringValue = text ?? "",
                Font = font,
                Bezeled = false,
                Bordered = false,
                DrawsBackground = false,
                Editable = false,
                Selectable = false,
                LineBreakMode = NSLineBreakMode.TruncatingTail,
                AutoresizingMask = NSViewResizingMask.WidthSizable,
            };
        }

        static string GetModelName(AnalysisResult result)
        {
            try
            {
                return result.Model.ModelType.GetProperties().Name;
            }
            catch
            {
                return result.Model.ModelType.ToString();
            }
        }
    }
}
