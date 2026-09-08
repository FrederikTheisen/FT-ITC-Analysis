using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using AppKit;
using CoreGraphics;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using Buffer = AnalysisITC.Core.Data.Buffer;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

namespace AnalysisITC.UI.MacOS.CustomViews
{
	public partial class ExperimentAttributeView : AppKit.NSStackView
	{
        public static double LastValue_PH { get; set; } = 7.4; // Default guess for ph
        public static double LastValue_HighConcentration { get; set; } = 0.1; // Default guess for concentration (mM range)

        public event EventHandler Remove;
        public event EventHandler KeyChanged;
        public event EventHandler<Tuple<AttributeKey,int>> SpecialAttributeSelected;

        public ExperimentAttribute Option { get; private set; }
        readonly bool spacious;
        NSView trailingSpacer;
        NSTextField unitLabel, phLabel;

        public override nfloat Spacing { get => spacious ? 4 : 1; set => base.Spacing = value; }

        NSPopUpButton KeySelectionControl { get; set; }
        NSButton BoolControl { get; set; }
        NSTextField DoubleField { get; set; }
        //NSTextField ParameterField { get; set; }
        //NSTextField ParameterErrorField { get; set; }
        ValueWithErrorTextField CombinedParameterField { get; set; }
        NSColor DefaultFieldColor { get; set; }
        NSPopUpButton EnumPopUpControl { get; set; }
        NSPopUpButton BufferSubtractionMethodControl { get; set; }
        NSSegmentedControl EnumSegControl { get; set; }
        NSTextField StringField { get; set; }

        #region Constructors

        // Called when created from unmanaged code
        public ExperimentAttributeView(IntPtr handle) : base(handle)
		{
			Initialize();
		}

		// Called when created directly from a XIB file
		[Export("initWithCoder:")]
		public ExperimentAttributeView(NSCoder coder) : base(coder)
		{
			Initialize();
		}

        public ExperimentAttributeView(CGRect frameRect, ExperimentAttribute option, bool spacious = false) : base(frameRect)
		{
			Frame = frameRect;
			Option = option;
            this.spacious = spacious;

            Initialize(spacious);
		}

		// Shared initialization code
        void Initialize(bool spacious = false)
		{
			Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
			Distribution = NSStackViewDistribution.Fill;
            Alignment = NSLayoutAttribute.CenterY;
            base.Spacing = spacious ? 4 : 1;
            AddConstraint(NSLayoutConstraint.Create(this, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, spacious ? 22 : 16));
            SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);
            SetHuggingPriority(1000, NSLayoutConstraintOrientation.Vertical);

			var rmbtn = new NSButton(new CGRect(0, 0, spacious ? 24 : 15, spacious ? 22 : Frame.Height))
            {
                BezelStyle = NSBezelStyle.Recessed,
				ControlSize = spacious ? NSControlSize.Regular : NSControlSize.Small,
                Image = NSImage.GetSystemSymbol("minus", null),
                Bordered = true,
                TranslatesAutoresizingMaskIntoConstraints = false,
                ShowsBorderOnlyWhileMouseInside = spacious,
            };
            if (spacious)
            {
                rmbtn.AddConstraint(NSLayoutConstraint.Create(rmbtn, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 24));
                rmbtn.AddConstraint(NSLayoutConstraint.Create(rmbtn, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 22));
                rmbtn.ToolTip = "Remove attribute";
            }
			rmbtn.SetButtonType(NSButtonType.MomentaryPushIn);
			rmbtn.Activated += (o,e) => Remove?.Invoke(this, null);

			AddArrangedSubview(rmbtn);

			SetupParameterSelectionMenu();

			SetupOption();
            ConfigureSpaciousControls();
        }

		void SetupParameterSelectionMenu()
		{
			KeySelectionControl = new NSPopUpButton(new CGRect(0, 0, Frame.Width / 2, Frame.Height), true);
			KeySelectionControl.BezelStyle = NSBezelStyle.Recessed;
            KeySelectionControl.Font = AttributeFont;
            KeySelectionControl.ControlSize = NSControlSize.Small;
            KeySelectionControl.Activated += ComboBox_Activated;
            if (spacious)
            {
                KeySelectionControl.AddConstraint(NSLayoutConstraint.Create(KeySelectionControl, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 144));
                KeySelectionControl.AddConstraint(NSLayoutConstraint.Create(KeySelectionControl, NSLayoutAttribute.Width, NSLayoutRelation.LessThanOrEqual, 1, 150));
                KeySelectionControl.LineBreakMode = NSLineBreakMode.TruncatingTail;
            }

            SetupKeyMenu();

            AddArrangedSubview(KeySelectionControl);
        }

        void SetupKeyMenu()
        {
            KeySelectionControl.Menu = new NSMenu();
            KeySelectionControl.Menu.AddItem(new NSMenuItem("Select Attribute Key"));

            foreach (var att in ExperimentAttribute.AvailableExperimentAttributes)
            {
                if (!att.GetProperties().AllowMultiple && ExperimentDetailsPopoverController.AvailableAttributes.Contains(att) && Option.Key != att) continue;
                KeySelectionControl.Menu.AddItem(new NSMenuItem("")
                {
                    Tag = (int)att,
                    AttributedTitle = new NSAttributedString(att.GetProperties().Name, AttributeFont)
                });
            }

            if (Option.Key != AttributeKey.Null)
            {
                KeySelectionControl.SelectItemWithTag((int)Option.Key);
                KeySelectionControl.SynchronizeTitleAndSelectedItem();
                KeySelectionControl.Title = KeySelectionControl.TitleOfSelectedItem;
                KeySelectionControl.ToolTip = Option.Key.GetProperties().ToolTip;
            }
        }

        void SetupOption()
		{
			if (Option.Key == AttributeKey.Null) return;

			KeySelectionControl.SynchronizeTitleAndSelectedItem();
			KeySelectionControl.Title = KeySelectionControl.TitleOfSelectedItem;

			switch (Option.Key)
			{
                case AttributeKey.PeptideInCell:
					SetupBool();
					break;
                case AttributeKey.IonicStrength:
                    SetupConcentration(ConcentrationUnit.mM, false);
                    break;
                case AttributeKey.PreboundLigandAffinity:
                case AttributeKey.PreboundLigandConc:
					SetupConcentration(ConcentrationUnit.µM, true);
					break;
                case AttributeKey.Salt:
                    SetupEnum();
                    SetupConcentration(ConcentrationUnit.mM, false);
                    break;
                case AttributeKey.Buffer:
                    SetupEnum();
                    SetupDouble(spacious ? "pH" : "  pH   ");
                    SetupConcentration(ConcentrationUnit.mM, false);
                    break;
                case AttributeKey.BufferSubtraction:
                    SetupBufferSubtraction();
                    break;
                case AttributeKey.Species:
                    SetupSpecies();
                    break;
            }
        }

        NSFont AttributeFont => NSFont.SystemFontOfSize(spacious ? 13 : NSFont.SmallSystemFontSize);

        void ConfigureSpaciousControls()
        {
            if (!spacious)
                return;

            var items = Views.ToArray();
            foreach (var view in items)
            {
                view.TranslatesAutoresizingMaskIntoConstraints = false;
                view.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
                view.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Vertical);
                if (view is NSControl control)
                    control.ControlSize = NSControlSize.Regular;
                if (view is NSTextField field)
                {
                    field.Cell.Wraps = false;
                    field.Cell.UsesSingleLineMode = true;
                    field.ControlSize = NSControlSize.Regular;
                    field.Font = AttributeFont;
                    field.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);
                    field.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Vertical);
                    if (field.Editable)
                    {
                        field.Bordered = false;
                        field.Bezeled = true;
                        field.DrawsBackground = true;
                        field.RefusesFirstResponder = false;
                    }
                    else
                    {
                        field.LineBreakMode = NSLineBreakMode.Clipping;
                    }
                }
                if (view is NSPopUpButton popup)
                {
                    popup.Font = AttributeFont;
                    popup.BezelStyle = NSBezelStyle.Recessed;
                    popup.ShowsBorderOnlyWhileMouseInside = true;
                    popup.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
                    if (!popup.Constraints.Any(c => c.FirstAttribute == NSLayoutAttribute.Height))
                        popup.AddConstraint(NSLayoutConstraint.Create(popup, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 22));
                }
            }

            trailingSpacer?.RemoveFromSuperview();
            trailingSpacer = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            trailingSpacer.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);

            var anchor = new NSView[] { phLabel, CombinedParameterField, StringField, BufferSubtractionMethodControl }
                .FirstOrDefault(view => view != null && items.Contains(view));
            NSStackView valueGroup = null;
            if (anchor != null)
            {
                var anchorIndex = Array.IndexOf(items, anchor);
                var valueViews = items.Skip(anchorIndex).ToArray();
                foreach (var view in valueViews)
                    RemoveView(view);

                valueGroup = new NSStackView(new CGRect(0, 0, 100, 22))
                {
                    Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                    Alignment = NSLayoutAttribute.FirstBaseline,
                    Distribution = NSStackViewDistribution.Fill,
                    Spacing = 4,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                valueGroup.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);
                valueGroup.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Vertical);

                foreach (var view in valueViews)
                    valueGroup.AddArrangedSubview(view);
            }

            AddArrangedSubview(trailingSpacer);
            if (valueGroup != null)
                AddArrangedSubview(valueGroup);
        }


		void SetupBool()
		{
			BoolControl = NSButton.CreateCheckbox("", () => Option.BoolValue = BoolControl.State == NSCellStateValue.On);
            BoolControl.State = Option.BoolValue ? NSCellStateValue.On : NSCellStateValue.Off;
            BoolControl.ToolTip = "Property Key: " + Option.Key.ToString();
            BoolControl.ControlSize = NSControlSize.Small;
            BoolControl.Font = AttributeFont;
            BoolControl.ImagePosition = NSCellImagePosition.ImageTrailing;
            BoolControl.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            AddArrangedSubview(BoolControl);
        }

        void SetupDouble(string description = null)
        {
            if (!string.IsNullOrEmpty(description))
            {
                var lbl = phLabel = NSTextField.CreateLabel(description);

                if (spacious)
                    lbl.AddConstraint(NSLayoutConstraint.Create(lbl, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 22));
                lbl.Font = AttributeFont;
                lbl.Alignment = NSTextAlignment.Center;

                AddArrangedSubview(lbl);
            }

            DoubleField = new NSTextField(new CGRect(0, 0, 30, 16))
            {
                Bordered = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
                StringValue = Option.DoubleValue.ToString("F1"),
                PlaceholderString = Option.DoubleValue.ToString("F1"),
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                FocusRingType = NSFocusRingType.None,
                ControlSize = NSControlSize.Small,
                Font = AttributeFont,
                Alignment = NSTextAlignment.Left,
                LineBreakMode = NSLineBreakMode.TruncatingHead,
            };
            DoubleField.Changed += (o, e) => Input_Changed(DoubleField, null);
            DoubleField.RefusesFirstResponder = true;
            DoubleField.AddConstraint(NSLayoutConstraint.Create(DoubleField, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, spacious ? 44 : 30));
            DoubleField.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            AddArrangedSubview(DoubleField);
        }

		void SetupConcentration(ConcentrationUnit concentrationUnit = ConcentrationUnit.µM, bool witherror = true)
		{
            SetupParameter(witherror);

            NSTextField lbl = new NSTextField(new CGRect(0, 0, 22, 14))
            {
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                Bordered = false,
                Editable = false,
                StringValue = concentrationUnit.GetProperties().Name,
                TranslatesAutoresizingMaskIntoConstraints = false,
                HorizontalContentSizeConstraintActive = false,
                ControlSize = NSControlSize.Small,
                Alignment = NSTextAlignment.Right,
                Font = AttributeFont,
            };
            unitLabel = lbl;
            lbl.AddConstraint(NSLayoutConstraint.Create(lbl, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, spacious ? 26 : 22));

            AddArrangedSubview(lbl);
        }

		void SetupParameter(bool includeerror = true)
		{
            FloatWithError value = Option.ParameterValue;

            switch (Option.Key)
            {
                case AttributeKey.PreboundLigandConc:
                    value *= 1000000;
                    break;
                case AttributeKey.IonicStrength:
                case AttributeKey.Salt:
                case AttributeKey.Buffer:
                    value *= 1000;
                    break;
            }

            CombinedParameterField = new ValueWithErrorTextField(
                new CGRect(0, 0, 80, 19))
            {
                ToolTip = includeerror ? "Value for the given property. Press space to enter uncertainty." : "Value for the given property",
                Alignment = NSTextAlignment.Right,
                Bezeled = false,
            };

            CombinedParameterField.SetValue(value.Value, value.SD);

            CombinedParameterField.Changed += (o, e) => Input_Changed(CombinedParameterField, null);
            CombinedParameterField.AddConstraint(NSLayoutConstraint.Create(CombinedParameterField, NSLayoutAttribute.Width, spacious ? NSLayoutRelation.Equal : NSLayoutRelation.GreaterThanOrEqual, 1, spacious && !includeerror ? 64 : 80));

            if (spacious)
                CombinedParameterField.AddConstraint(NSLayoutConstraint.Create(CombinedParameterField, NSLayoutAttribute.Width, NSLayoutRelation.LessThanOrEqual, 1, 120));
            CombinedParameterField.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            AddArrangedSubview(CombinedParameterField);

            DefaultFieldColor = CombinedParameterField.TextColor;
        }

        NSPopUpButton DropDownMenuButton(bool pullsDown = true)
        {
            var btn = new NSPopUpButton(new CGRect(0, 0, Frame.Width / 2, Frame.Height), pullsDown);
            // Attribute editors in the Details sheet use the same recessed native
            // pop-up treatment as the surrounding condition controls.
            btn.BezelStyle = NSBezelStyle.Recessed;
            btn.Font = AttributeFont;
            btn.ControlSize = NSControlSize.Small;
            btn.Activated += EnumPopUpControl_Activated;
            btn.AddConstraint(NSLayoutConstraint.Create(btn, NSLayoutAttribute.Width, NSLayoutRelation.LessThanOrEqual, 1, 150));
            if (spacious)
                btn.AddConstraint(NSLayoutConstraint.Create(btn, NSLayoutAttribute.Width, NSLayoutRelation.GreaterThanOrEqual, 1, 70));
            if (spacious && (Option.Key == AttributeKey.Buffer || Option.Key == AttributeKey.Salt))
            {
                var preferredWidth = NSLayoutConstraint.Create(btn, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 78);
                preferredWidth.Priority = 750;
                btn.AddConstraint(preferredWidth);
            }
            btn.LineBreakMode = NSLineBreakMode.TruncatingMiddle;

            btn.Menu = new NSMenu();
            btn.Menu.AddItem(new NSMenuItem("Select") { Tag = -1 });

            return btn;
        }

        void SetupDropdownMenu()
        {
            if (!spacious)
            {
                var spacer = new NSBox() { TitlePosition = NSTitlePosition.NoTitle, BoxType = NSBoxType.NSBoxCustom, BorderType = NSBorderType.NoBorder };
                spacer.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);
                AddArrangedSubview(spacer);
            }

            EnumPopUpControl = DropDownMenuButton();
        }

        void SetupEnum()
        {
            SetupDropdownMenu();

            var opts = Option.EnumOptions.ToList();

            for (int i = 0; i < Option.EnumOptionCount; i++)
            {
                var opt = opts[i];

                if (opt.Item1 != -1)
                {
                    var item = new NSMenuItem("")
                    {
                        Tag = opt.Item1,
                        AttributedTitle = AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(opt.Item2, AttributeFont),
                        ToolTip = AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(opt.Item3, AttributeFont).Value,
                    };
                    EnumPopUpControl.Menu.AddItem(item);
                }
                else EnumPopUpControl.Menu.AddItem(NSMenuItem.SeparatorItem);
            }

            AddArrangedSubview(EnumPopUpControl);

            if (Option.IntValue != -1) { EnumPopUpControl.SelectItemWithTag(Option.IntValue); EnumPopUpControl_Activated(null, null); }
        }

        void SetupReferenceExperiment()
        {
            SetupDropdownMenu();

            var opts = Option.ExperimentReferenceOptions.ToList();

            int selectedID = -1;

            for (int i = 0; i < opts.Count; i++)
            {
                var opt = opts[i];
                if (Option.StringValue == opt.Item4) selectedID = i;
                EnumPopUpControl.Menu.AddItem(new NSMenuItem("")
                {
                    Tag = i,
                    AttributedTitle = AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(opt.Item2, AttributeFont),
                    ToolTip = AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(opt.Item3, AttributeFont).Value,
                });
            }

            AddArrangedSubview(EnumPopUpControl);

            if (selectedID != -1) { EnumPopUpControl.SelectItemWithTag(selectedID); EnumPopUpControl_Activated(null, null); }
        }

        void SetupBufferSubtraction()
        {
            SetupReferenceExperiment();

            BufferSubtractionMethodControl = DropDownMenuButton(pullsDown: false);
            BufferSubtractionMethodControl.Activated -= EnumPopUpControl_Activated;
            BufferSubtractionMethodControl.Menu.RemoveAllItems();
            BufferSubtractionMethodControl.AddConstraint(NSLayoutConstraint.Create(BufferSubtractionMethodControl, NSLayoutAttribute.Width, NSLayoutRelation.LessThanOrEqual, 1, 90));
            BufferSubtractionMethodControl.Activated += (_, __) =>
            {
                BufferSubtractionMethodControl.SynchronizeTitleAndSelectedItem();
                BufferSubtractionMethodControl.Title = BufferSubtractionMethodControl.TitleOfSelectedItem;
            };

            BufferSubtractionMethodControl.Menu.AddItem(new NSMenuItem(BufferSubtractionMethod.MatchedInjection.GetDisplayName())
            {
                Tag = (int)BufferSubtractionMethod.MatchedInjection,
                ToolTip = "Subtract the matching buffer injection, with nearby valid injections used as fallback.",
            });
            BufferSubtractionMethodControl.Menu.AddItem(new NSMenuItem(BufferSubtractionMethod.Linear.GetDisplayName())
            {
                Tag = (int)BufferSubtractionMethod.Linear,
                ToolTip = "Subtract a straight-line fit through the included buffer injections.",
            });
            BufferSubtractionMethodControl.Menu.AddItem(new NSMenuItem(BufferSubtractionMethod.ExponentialDecay.GetDisplayName())
            {
                Tag = (int)BufferSubtractionMethod.ExponentialDecay,
                ToolTip = "Subtract a single exponential decay fit through the included buffer injections.",
            });

            var method = BufferSubtractionSettings.FromAttribute(Option)?.Method ?? BufferSubtractionMethod.MatchedInjection;
            BufferSubtractionMethodControl.SelectItemWithTag((int)method);
            BufferSubtractionMethodControl.SynchronizeTitleAndSelectedItem();
            BufferSubtractionMethodControl.Title = BufferSubtractionMethodControl.TitleOfSelectedItem;

            AddArrangedSubview(BufferSubtractionMethodControl);
        }

        void SetupSpecies()
        {
            SetupDropdownMenu();

            foreach (var opt in ExperimentAttribute.SpeciesLocationOptions)
            {
                EnumPopUpControl.Menu.AddItem(new NSMenuItem(opt.Item2)
                {
                    Tag = opt.Item1,
                    ToolTip = opt.Item3,
                });
            }

            AddArrangedSubview(EnumPopUpControl);

            var location = ExperimentAttribute.NormalizeSpeciesLocation(Option.IntValue);
            EnumPopUpControl.SelectItemWithTag((int)location);
            EnumPopUpControl_Activated(null, null);

            StringField = new NSTextField(new CGRect(0, 0, 120, 16))
            {
                Bordered = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
                StringValue = Option.StringValue ?? "",
                PlaceholderString = "Species name",
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                FocusRingType = NSFocusRingType.None,
                ControlSize = NSControlSize.Small,
                Font = AttributeFont,
                Alignment = NSTextAlignment.Right,
                LineBreakMode = NSLineBreakMode.TruncatingTail,
            };
            if (spacious)
                StringField.AddConstraint(NSLayoutConstraint.Create(StringField, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 180));
            else
                StringField.AddConstraint(NSLayoutConstraint.Create(StringField, NSLayoutAttribute.Width, NSLayoutRelation.GreaterThanOrEqual, 1, 120));
            StringField.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            AddArrangedSubview(StringField);
        }

        #endregion

        public void UpdateKeyMenu()
        {
            SetupKeyMenu();
        }

        private void ComboBox_Activated(object sender, EventArgs e)
        {
            trailingSpacer?.RemoveFromSuperview();
            while (Views.Count() > 2)
                RemoveView(Views[2]);

            Option.UpdateOptionKey((AttributeKey)(int)KeySelectionControl.SelectedItem.Tag, LastValue_PH, LastValue_HighConcentration);

            SetupOption();
            ConfigureSpaciousControls();

            KeyChanged?.Invoke(this, null);
        }

        /// <summary>
        /// Set the drop down menu text to the selected option title (or whatever you want)
        /// </summary>
        /// <param name="text"></param>
        void SetPopUpButtonText(string text)
        {
            EnumPopUpControl.Menu.ItemAt(0).AttributedTitle = AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(text , AttributeFont);
        }

        private void EnumPopUpControl_Activated(object sender, EventArgs args)
        {
            switch (Option.Key)
            {
                case AttributeKey.Salt:
                    // Set button text
                    SetPopUpButtonText(Option.EnumOptions.Single(e => e.Item1 == (int)EnumPopUpControl.SelectedTag).Item2);
                    break;
                case AttributeKey.Buffer:
                    switch ((Buffer)(int)EnumPopUpControl.SelectedTag)
                    {
                        case Buffer.PBS:
                        case Buffer.TBS:
                            Remove?.Invoke(this, null);
                            SpecialAttributeSelected?.Invoke(this, new(Option.Key, (int)EnumPopUpControl.SelectedTag));
                            return;
                    }
                    // Set button text
                    SetPopUpButtonText(Option.EnumOptions.Single(e => e.Item1 == (int)EnumPopUpControl.SelectedTag).Item2);
                    break;
                case AttributeKey.BufferSubtraction:
                    // Set button text
                    SetPopUpButtonText(Option.ExperimentReferenceOptions.Single(e => e.Item1 == (int)EnumPopUpControl.SelectedTag).Item2);
                    break;
                case AttributeKey.Species:
                    SetPopUpButtonText(ExperimentAttribute.GetSpeciesLocationDisplayName((int)EnumPopUpControl.SelectedTag));
                    break;
            }
            
        }

        private void Input_Changed(object sender, EventArgs e)
        {
            CheckInput(sender as NSTextField);
        }

        void CheckInput(NSTextField field)
        {
            if (field == null)
                return;

            field.TextColor = NSColor.SystemRed;

            if (field is ValueWithErrorTextField valueField)
            {
                if (string.IsNullOrWhiteSpace(valueField.ValueText))
                {
                    field.TextColor = DefaultFieldColor;
                    return;
                }

                if (valueField.HasValidInput)
                {
                    var value = valueField.DoubleValuePart;

                    if (value < 0) return;
                    field.TextColor = DefaultFieldColor;
                }

                return;
            }
            else
            {
                string input = field.StringValue;

                if (string.IsNullOrEmpty(input)) field.TextColor = DefaultFieldColor;
                else if (double.TryParse(input, out double value))
                {
                    if (value < 0) return;
                    if (Option.Key == AttributeKey.Buffer && field == DoubleField && value > 14) return;
                    field.TextColor = DefaultFieldColor;
                }
            }
        }

        public void ApplyOption(ExperimentData experiment)
        {
            if (Option.Key == AttributeKey.Null) return;



            switch (Option.Key)
            {
                case AttributeKey.PeptideInCell: Option.BoolValue = BoolControl.State == NSCellStateValue.On; break;
                case AttributeKey.PreboundLigandConc:
                    {
                        if (!CombinedParameterField.TryGetValue(out double val, out double err))
                            break;

                        val /= 1000000;
                        err /= 1000000;

                        var value = new FloatWithError(val, err);

                        Option.ParameterValue = value;
                        break;
                    }
                case AttributeKey.Salt:
                    {
                        Option.IntValue = (int)EnumPopUpControl.SelectedTag;
                        if (Option.IntValue == -1) return;

                        if (!CombinedParameterField.TryGetValue(out double val, out double err))
                            break;

                        Option.ParameterValue = new(val / 1000, err / 1000);
                        LastValue_HighConcentration = val / 1000;

                        break;
                    }
                case AttributeKey.Buffer:
                    {
                        Option.IntValue = (int)EnumPopUpControl.SelectedTag;
                        if (Option.IntValue == -1) return;
                        if (!string.IsNullOrEmpty(DoubleField.StringValue))
                        {
                            Option.DoubleValue = DoubleField.DoubleValue;
                            LastValue_PH = DoubleField.DoubleValue;
                        }


                        if (!CombinedParameterField.TryGetValue(out double val, out double err))
                            break;

                        Option.ParameterValue = new(val / 1000, err / 1000);
                        LastValue_HighConcentration = val / 1000;

                        break;
                    }
                case AttributeKey.IonicStrength:
                    {
                        if (!CombinedParameterField.TryGetValue(out double val, out double err))
                            break;

                        Option.ParameterValue = new(val / 1000, err / 1000);
                        break;
                    }
                case AttributeKey.BufferSubtraction:
                    {
                        var idx = (int)EnumPopUpControl.SelectedTag;
                        if (idx != -1)
                        {
                            var reference = DataManager.Data[idx];
                            var method = BufferSubtractionMethodControl == null
                                ? BufferSubtractionMethod.MatchedInjection
                                : (BufferSubtractionMethod)(int)BufferSubtractionMethodControl.SelectedTag;

                            experiment.SetBufferSubtraction(reference, method);

                            return;
                        }
                        break;
                    }
                case AttributeKey.Species:
                    {
                        Option.IntValue = (int)ExperimentAttribute.NormalizeSpeciesLocation((int)EnumPopUpControl.SelectedTag);
                        Option.StringValue = StringField.StringValue?.Trim() ?? "";

                        if (string.IsNullOrWhiteSpace(Option.StringValue)) return;

                        break;
                    }
            }

            experiment.AddOrUpdateAttribute(Option);
        }
    }
}
