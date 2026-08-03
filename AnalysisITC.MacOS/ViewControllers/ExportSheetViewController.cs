using System;
using System.Linq;

using AppKit;
using CoreGraphics;
using Foundation;

using AnalysisITC.Core.Export;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC
{
    sealed class ExportSheetViewController : NSViewController
    {
        static readonly ExportType[] Formats =
        {
            ExportType.InterchangeCsv,
            ExportType.Data,
            ExportType.Peaks,
            ExportType.CSV,
            ExportType.ITCsim,
            ExportType.PYTC,
            ExportType.MicroCal
        };

        readonly ExportAccessoryViewSettings settings;
        readonly Action<bool> completed;

        NSTextField outputNameField;
        NSPopUpButton formatPopup;
        NSSegmentedControl selectionControl;
        NSTextField descriptionLabel;
        NSTextField extensionLabel;
        NSButton unifyTimeAxisCheck;
        NSButton correctedDataCheck;
        NSButton fittedValuesCheck;
        NSButton offsetCorrectedCheck;
        NSTextField errorLabel;
        bool didComplete;

        public ExportSheetViewController(ExportAccessoryViewSettings settings, Action<bool> completed)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.completed = completed ?? throw new ArgumentNullException(nameof(completed));
        }

        public override void LoadView()
        {
            View = new NSView(new CGRect(0, 0, 560, 370));
            BuildView();
            RefreshControls();
        }

        void BuildView()
        {
            View.WantsLayer = true;

            var title = Label("Export Data", 20, bold: true);
            title.Frame = new CGRect(20, 330, 500, 25);
            View.AddSubview(title);

            AddFormLabel("Output name", 292);
            outputNameField = new NSTextField(new CGRect(145, 286, 380, 24))
            {
                StringValue = settings.OutputBaseName ?? "",
                PlaceholderString = "Output name"
            };
            View.AddSubview(outputNameField);

            AddFormLabel("Format", 252);
            formatPopup = new NSPopUpButton(new CGRect(145, 246, 260, 26), false);
            formatPopup.AddItems(Formats.Select(format => format.GetProperties().Name).ToArray());
            formatPopup.SelectItem(Array.IndexOf(Formats, settings.Export));
            formatPopup.Activated += (_, _) => RefreshControls();
            View.AddSubview(formatPopup);
            extensionLabel = Label("", 12, false);
            extensionLabel.TextColor = NSColor.SecondaryLabelColor;
            extensionLabel.Frame = new CGRect(415, 250, 110, 18);
            View.AddSubview(extensionLabel);

            AddFormLabel("Data", 212);
            selectionControl = new NSSegmentedControl(new CGRect(145, 206, 300, 26))
            {
                SegmentCount = 3
            };
            selectionControl.SetLabel("Selected", 0);
            selectionControl.SetLabel("Active", 1);
            selectionControl.SetLabel("All", 2);
            selectionControl.SelectSegment((int)settings.Selection);
            selectionControl.Activated += (_, _) => RefreshControls();
            View.AddSubview(selectionControl);

            descriptionLabel = Label("", 12, false);
            descriptionLabel.TextColor = NSColor.SecondaryLabelColor;
            descriptionLabel.UsesSingleLineMode = false;
            descriptionLabel.Cell.Wraps = true;
            descriptionLabel.LineBreakMode = NSLineBreakMode.ByWordWrapping;
            descriptionLabel.Frame = new CGRect(20, 150, 505, 42);
            View.AddSubview(descriptionLabel);

            unifyTimeAxisCheck = CheckBox("Unify time axis");
            unifyTimeAxisCheck.Frame = new CGRect(20, 115, 220, 22);
            View.AddSubview(unifyTimeAxisCheck);

            correctedDataCheck = CheckBox("Export baseline-corrected trace");
            correctedDataCheck.Frame = new CGRect(250, 115, 275, 22);
            View.AddSubview(correctedDataCheck);

            fittedValuesCheck = CheckBox("Include fitted model values");
            fittedValuesCheck.Frame = new CGRect(20, 88, 220, 22);
            View.AddSubview(fittedValuesCheck);

            offsetCorrectedCheck = CheckBox("Export offset-corrected peaks");
            offsetCorrectedCheck.Frame = new CGRect(250, 88, 275, 22);
            View.AddSubview(offsetCorrectedCheck);

            errorLabel = Label("", 12, false);
            errorLabel.TextColor = NSColor.SystemRedColor;
            errorLabel.Frame = new CGRect(20, 54, 400, 18);
            View.AddSubview(errorLabel);

            var cancel = new NSButton(new CGRect(355, 14, 82, 28)) { Title = "Cancel", BezelStyle = NSBezelStyle.Rounded };
            cancel.Activated += (_, _) => Complete(false);
            View.AddSubview(cancel);

            var chooseFolder = new NSButton(new CGRect(445, 14, 95, 28)) { Title = "Choose Folder...", BezelStyle = NSBezelStyle.Rounded };
            chooseFolder.Activated += (_, _) => ApplyAndChooseFolder();
            View.AddSubview(chooseFolder);
        }

        void AddFormLabel(string text, nfloat y)
        {
            var label = Label(text, 13, false);
            label.Alignment = NSTextAlignment.Right;
            label.Frame = new CGRect(20, y, 112, 20);
            View.AddSubview(label);
        }

        void RefreshControls()
        {
            var format = SelectedFormat;
            settings.Selection = (ExportDataSelection)selectionControl.SelectedSegment;
            settings.SetData();

            descriptionLabel.StringValue = format.GetProperties().Description;
            extensionLabel.StringValue = "." + format.GetProperties().Extension;

            var traceOptions = format == ExportType.Data;
            var peakOptions = format == ExportType.Peaks || format == ExportType.CSV;
            var offsetOptions = peakOptions || format == ExportType.ITCsim;

            unifyTimeAxisCheck.Hidden = !traceOptions;
            correctedDataCheck.Hidden = !traceOptions;
            fittedValuesCheck.Hidden = !peakOptions;
            offsetCorrectedCheck.Hidden = !offsetOptions;

            unifyTimeAxisCheck.State = settings.UnifyTimeAxis ? NSCellStateValue.On : NSCellStateValue.Off;
            correctedDataCheck.State = settings.ExportBaselineCorrectDataPoints ? NSCellStateValue.On : NSCellStateValue.Off;
            correctedDataCheck.Enabled = settings.BaselineCorrectionEnabled;
            fittedValuesCheck.State = settings.Columns.HasFlag(ExportColumns.Fit) ? NSCellStateValue.On : NSCellStateValue.Off;
            fittedValuesCheck.Enabled = settings.FittedPeakExportEnabled;
            offsetCorrectedCheck.State = settings.ExportOffsetCorrected ? NSCellStateValue.On : NSCellStateValue.Off;
            offsetCorrectedCheck.Enabled = settings.FittedPeakExportEnabled;
        }

        ExportType SelectedFormat => Formats[Math.Max(0, formatPopup.IndexOfSelectedItem)];

        void ApplyAndChooseFolder()
        {
            var outputName = outputNameField.StringValue?.Trim();
            if (string.IsNullOrWhiteSpace(outputName))
            {
                errorLabel.StringValue = "Enter an output name.";
                return;
            }

            settings.OutputBaseName = outputName;
            settings.Export = SelectedFormat;
            settings.Selection = (ExportDataSelection)selectionControl.SelectedSegment;
            settings.UnifyTimeAxis = unifyTimeAxisCheck.State == NSCellStateValue.On;
            settings.ExportBaselineCorrectDataPoints = correctedDataCheck.State == NSCellStateValue.On;
            settings.ExportOffsetCorrected = offsetCorrectedCheck.State == NSCellStateValue.On;
            settings.Columns = fittedValuesCheck.State == NSCellStateValue.On
                ? settings.Columns | ExportColumns.Fit
                : settings.Columns & ~ExportColumns.Fit;
            settings.ExportFittedPeaks = settings.Columns.HasFlag(ExportColumns.Fit);
            settings.SetData();
            Complete(true);
        }

        void Complete(bool accepted)
        {
            if (didComplete) return;
            didComplete = true;
            completed(accepted);
        }

        static NSTextField Label(string text, nfloat size, bool bold)
        {
            return new NSTextField
            {
                StringValue = text,
                Editable = false,
                Selectable = false,
                Bordered = false,
                DrawsBackground = false,
                Font = bold ? NSFont.BoldSystemFontOfSize(size) : NSFont.SystemFontOfSize(size)
            };
        }

        static NSButton CheckBox(string text)
        {
            var button = new NSButton { Title = text, ButtonType = NSButtonType.Switch };
            button.SetButtonType(NSButtonType.Switch);
            return button;
        }
    }
}
