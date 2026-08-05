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
        readonly ExportAccessoryViewSettings settings;
        readonly Action<bool> completed;

        NSTextField outputNameField;
        NSPopUpButton formatPopup;
        NSSegmentedControl selectionControl;
        NSStackView rootStack;
        NSTextField descriptionLabel;
        NSTextField unitsLabel;
        NSLayoutConstraint descriptionHeightConstraint;
        NSLayoutConstraint unitsHeightConstraint;
        NSButton correctedDataCheck;
        NSButton offsetCorrectedCheck;
        NSStackView optionsContent;
        NSTextField noOptionsLabel;
        NSTextField errorLabel;
        bool didComplete;

        public ExportSheetViewController(ExportAccessoryViewSettings settings, Action<bool> completed)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.completed = completed ?? throw new ArgumentNullException(nameof(completed));
        }

        public override void LoadView()
        {
            View = new NSView(new CGRect(0, 0, 470, 450));

            BuildView();
            RefreshControls();
        }

        public override void ViewWillAppear()
        {
            base.ViewWillAppear();
            this.View.Window.MinSize = new CGSize(470, 450);
            this.View.Window.MaxSize = new CGSize(470, 450);
        }

        void BuildView()
        {
            View.WantsLayer = true;
            rootStack = CreateVerticalStack(5);
            rootStack.Alignment = NSLayoutAttribute.Leading;
            rootStack.SetHuggingPriority(1000, NSLayoutConstraintOrientation.Vertical);
            rootStack.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);
            rootStack.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Vertical);
            View.AddSubview(rootStack);
            rootStack.LeadingAnchor.ConstraintEqualToAnchor(View.LeadingAnchor, 20).Active = true;
            rootStack.TopAnchor.ConstraintEqualToAnchor(View.TopAnchor, 20).Active = true;
            rootStack.BottomAnchor.ConstraintEqualToAnchor(View.BottomAnchor, -20).Active = true;
            rootStack.WidthAnchor.ConstraintEqualToConstant(430).Active = true;

            var title = Label("Export Data", NSFont.SystemFontSize, bold: true);
            title.Alignment = NSTextAlignment.Center;
            AddFullWidth(title);

            var introduction = WrappedLabel(
                "Choose the output name, format, and experiments to export. " +
                "After confirming these options, Finder will ask where the files should be written.");
            introduction.HeightAnchor.ConstraintEqualToConstant(
                MeasureWrappedHeight(introduction, 430, 32)).Active = true;
            introduction.Selectable = true;
            AddFullWidth(introduction);
            AddFullWidth(Separator());

            outputNameField = new NSTextField
            {
                StringValue = settings.OutputBaseName ?? "",
                PlaceholderString = "Output prefix",
                ControlSize = NSControlSize.Regular,
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                FocusRingType = NSFocusRingType.None,
                TranslatesAutoresizingMaskIntoConstraints = false,
                RefusesFirstResponder = true,
            };
            AddFormRow("Output prefix", outputNameField);

            formatPopup = new NSPopUpButton(CGRect.Empty, false)
            {
                ControlSize = NSControlSize.Regular,
                TranslatesAutoresizingMaskIntoConstraints = false
            };
            AddFormatMenuItem(ExportType.Data);
            AddFormatMenuItem(ExportType.Peaks);
            AddFormatMenuItem(ExportType.InterchangeCsv);
            formatPopup.Menu.AddItem(NSMenuItem.SeparatorItem);
            AddFormatMenuItem(ExportType.MicroCal, "MicroCal / SEDPHAT");
            AddFormatMenuItem(ExportType.PYTC, "pytc");
            AddFormatMenuItem(ExportType.ITCsim, "ITCsim");
            SelectFormat(settings.Export);
            formatPopup.Activated += (_, _) => RefreshControls();
            AddFormRow("Format", formatPopup);

            selectionControl = new NSSegmentedControl
            {
                SegmentCount = 3,
                ControlSize = NSControlSize.Regular,
                TranslatesAutoresizingMaskIntoConstraints = false
            };
            selectionControl.SetLabel("Selected", 0);
            selectionControl.SetLabel("Active", 1);
            selectionControl.SetLabel("All", 2);
            selectionControl.SelectSegment((int)settings.Selection);
            selectionControl.Activated += (_, _) => RefreshControls();
            AddFormRow("Data", selectionControl);

            correctedDataCheck = CheckBox("Baseline-corrected trace");
            offsetCorrectedCheck = CheckBox("Offset-corrected peaks");
            noOptionsLabel = Label("No additional options", NSFont.SmallSystemFontSize, false);
            noOptionsLabel.TextColor = NSColor.SecondaryLabel;
            optionsContent = CreateVerticalStack(5);
            optionsContent.AddArrangedSubview(correctedDataCheck);
            optionsContent.AddArrangedSubview(offsetCorrectedCheck);
            optionsContent.AddArrangedSubview(noOptionsLabel);
            AddFormRow("Options", optionsContent);

            AddFullWidth(Separator());

            var detailsTitle = Label("Format details", 13, true);
            AddFullWidth(detailsTitle);

            descriptionLabel = WrappedLabel("");
            descriptionLabel.TextColor = NSColor.SecondaryLabel;
            descriptionHeightConstraint = descriptionLabel.HeightAnchor.ConstraintEqualToConstant(36);
            descriptionHeightConstraint.Active = true;
            AddFullWidth(descriptionLabel);

            var unitsTitle = Label("Output units", 13, true);
            AddFullWidth(unitsTitle);

            unitsLabel = WrappedLabel("");
            unitsHeightConstraint = unitsLabel.HeightAnchor.ConstraintEqualToConstant(36);
            unitsHeightConstraint.Active = true;
            AddFullWidth(unitsLabel);

            errorLabel = Label("", 12, false);
            errorLabel.TextColor = NSColor.SystemRed;
            errorLabel.Hidden = true;
            AddFullWidth(errorLabel);

            var footerSpacer = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            footerSpacer.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Vertical);
            footerSpacer.SetContentCompressionResistancePriority(1, NSLayoutConstraintOrientation.Vertical);
            AddFullWidth(footerSpacer);

            AddFullWidth(Separator());

            var footer = CreateHorizontalStack(8);
            footer.Distribution = NSStackViewDistribution.FillEqually;

            var cancel = new NSButton { Title = "Cancel", BezelStyle = NSBezelStyle.Rounded, TranslatesAutoresizingMaskIntoConstraints = false, ControlSize = NSControlSize.Large };
            cancel.Activated += (_, _) => Complete(false);
            footer.AddArrangedSubview(cancel);

            var chooseFolder = new NSButton { Title = "Choose Folder...", BezelStyle = NSBezelStyle.Rounded, TranslatesAutoresizingMaskIntoConstraints = false, ControlSize = NSControlSize.Large };
            chooseFolder.Activated += (_, _) => ApplyAndChooseFolder();
            footer.AddArrangedSubview(chooseFolder);
            AddFullWidth(footer);
        }

        void RefreshControls()
        {
            var format = SelectedFormat;
            settings.Selection = (ExportDataSelection)(int)selectionControl.SelectedSegment;
            settings.SetData();

            descriptionLabel.StringValue = format.GetProperties().Description;
            unitsLabel.StringValue = ExportFormatDescription.GetOutputUnits(format, settings.Data);
            descriptionHeightConstraint.Constant = MeasureWrappedHeight(descriptionLabel, 430, 34);
            unitsHeightConstraint.Constant = MeasureWrappedHeight(unitsLabel, 430, 34);

            var traceOptions = format == ExportType.Data || format == ExportType.InterchangeCsv;
            var offsetOptions = format == ExportType.Peaks || format == ExportType.ITCsim || format == ExportType.InterchangeCsv;

            correctedDataCheck.Hidden = !traceOptions;
            offsetCorrectedCheck.Hidden = !offsetOptions;
            noOptionsLabel.Hidden = traceOptions || offsetOptions;

            correctedDataCheck.State = settings.ExportBaselineCorrectDataPoints ? NSCellStateValue.On : NSCellStateValue.Off;
            correctedDataCheck.Enabled = settings.BaselineCorrectionEnabled;
            offsetCorrectedCheck.State = settings.ExportOffsetCorrected ? NSCellStateValue.On : NSCellStateValue.Off;
            offsetCorrectedCheck.Enabled = settings.FittedPeakExportEnabled;
            SetError(null);
        }

        ExportType SelectedFormat => formatPopup.SelectedItem?.Tag is nint tag && Enum.IsDefined(typeof(ExportType), (int)tag)
            ? (ExportType)(int)tag
            : ExportType.InterchangeCsv;

        void ApplyAndChooseFolder()
        {
            var outputName = outputNameField.StringValue?.Trim();
            if (string.IsNullOrWhiteSpace(outputName))
            {
                SetError("Enter an output name.");
                return;
            }

            settings.OutputBaseName = outputName;
            settings.Export = SelectedFormat;
            settings.Selection = (ExportDataSelection)(int)selectionControl.SelectedSegment;
            settings.ExportBaselineCorrectDataPoints = correctedDataCheck.State == NSCellStateValue.On;
            settings.ExportOffsetCorrected = offsetCorrectedCheck.State == NSCellStateValue.On;
            settings.ExportFittedPeaks = settings.Columns.HasFlag(ExportColumns.Fit);
            settings.SetData();
            if (settings.Data.Count == 0)
            {
                SetError("Select an experiment or choose Active or All data.");
                return;
            }
            Complete(true);
        }

        void SetError(string message)
        {
            errorLabel.StringValue = message ?? "";
            errorLabel.Hidden = string.IsNullOrWhiteSpace(message);
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
                Font = bold ? NSFont.BoldSystemFontOfSize(size) : NSFont.SystemFontOfSize(size),
                TranslatesAutoresizingMaskIntoConstraints = false
            };
        }

        static NSTextField WrappedLabel(string text)
        {
            var label = Label(text, 12, false);
            label.UsesSingleLineMode = false;
            label.MaximumNumberOfLines = 0;
            label.Cell.Wraps = true;
            label.LineBreakMode = NSLineBreakMode.ByWordWrapping;
            return label;
        }

        static nfloat MeasureWrappedHeight(NSTextField label, nfloat width, nfloat minimumHeight)
        {
            var measured = (nfloat)Math.Ceiling(
                (double)label.Cell.CellSizeForBounds(new CGRect(0, 0, width, 10000)).Height) + 2;
            return measured < minimumHeight ? minimumHeight : measured;
        }

        static NSStackView CreateVerticalStack(nfloat spacing)
        {
            return new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                Distribution = NSStackViewDistribution.Fill,
                Alignment = NSLayoutAttribute.Width,
                Spacing = spacing,
                DetachesHiddenViews = true,
                TranslatesAutoresizingMaskIntoConstraints = false
            };
        }

        static NSStackView CreateHorizontalStack(nfloat spacing)
        {
            return new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Distribution = NSStackViewDistribution.Fill,
                Alignment = NSLayoutAttribute.CenterY,
                Spacing = spacing,
                DetachesHiddenViews = true,
                TranslatesAutoresizingMaskIntoConstraints = false
            };
        }

        void AddFormRow(string title, NSView control)
        {
            var row = CreateHorizontalStack(8);
            var label = Label(title, 13, false);
            label.Alignment = NSTextAlignment.Left;
            label.WidthAnchor.ConstraintEqualToConstant(172).Active = true;
            control.WidthAnchor.ConstraintEqualToConstant(250).Active = true;
            row.AddArrangedSubview(label);
            row.AddArrangedSubview(control);
            row.WidthAnchor.ConstraintEqualToConstant(430).Active = true;
            row.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);
            rootStack.AddArrangedSubview(row);
        }

        void AddFullWidth(NSView view)
        {
            rootStack.AddArrangedSubview(view);
            view.WidthAnchor.ConstraintEqualToConstant(430).Active = true;
        }

        static NSBox Separator()
        {
            var separator = new NSBox
            {
                BoxType = NSBoxType.NSBoxSeparator,
                TranslatesAutoresizingMaskIntoConstraints = false
            };
            separator.HeightAnchor.ConstraintEqualToConstant(1).Active = true;
            return separator;
        }

        static NSButton CheckBox(string text)
        {
            var button = new NSButton { Title = text, TranslatesAutoresizingMaskIntoConstraints = false };
            button.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
            button.SetButtonType(NSButtonType.Switch);
            return button;
        }

        void AddFormatMenuItem(ExportType format, string title = null)
        {
            var properties = format.GetProperties();
            formatPopup.Menu.AddItem(new NSMenuItem($"{title ?? properties.Name} (.{properties.Extension})")
            {
                Tag = (int)format
            });
        }

        void SelectFormat(ExportType format)
        {
            var item = formatPopup.Items().FirstOrDefault(candidate => candidate.Tag == (int)format)
                ?? formatPopup.Items().First(candidate => candidate.Tag == (int)ExportType.InterchangeCsv);
            formatPopup.SelectItem(item);
        }
    }
}
