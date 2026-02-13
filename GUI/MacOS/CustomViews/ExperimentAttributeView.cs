using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using AppKit;
using CoreGraphics;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.Utilities;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
	public partial class ExperimentAttributeView : AppKit.NSStackView
	{
		public event EventHandler Remove;
        public event EventHandler KeyChanged;
        public event EventHandler<Tuple<AttributeKey,int>> SpecialAttributeSelected;

        public ExperimentAttribute Option { get; private set; }

        public override nfloat Spacing { get => 1; set => base.Spacing = value; }

        NSPopUpButton KeySelectionControl { get; set; }
        NSButton BoolControl { get; set; }
        NSTextField DoubleField { get; set; }
        NSTextField ParameterField { get; set; }
        NSTextField ParameterErrorField { get; set; }
        NSColor DefaultFieldColor { get; set; }
        NSPopUpButton EnumPopUpControl { get; set; }
        NSSegmentedControl EnumSegControl { get; set; }

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

		public ExperimentAttributeView(CGRect frameRect, ExperimentAttribute option) : base(frameRect)
		{
			Frame = frameRect;
			Option = option;

			Initialize();
		}

		// Shared initialization code
		void Initialize()
		{
			Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
			Distribution = NSStackViewDistribution.Fill;
			Alignment = NSLayoutAttribute.CenterY;
            SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);
            SetHuggingPriority(1000, NSLayoutConstraintOrientation.Vertical);

			var rmbtn = new NSButton(new CGRect(0, 0, 15, Frame.Height))
            {
				BezelStyle = NSBezelStyle.Recessed,
				ControlSize = NSControlSize.Small,
                Image = NSImage.GetSystemSymbol("minus", null),
                Bordered = false,
            };
			rmbtn.SetButtonType(NSButtonType.MomentaryPushIn);
			rmbtn.Activated += (o,e) => Remove?.Invoke(this, null);

			AddArrangedSubview(rmbtn);

			SetupParameterSelectionMenu();

			SetupOption();
        }

		void SetupParameterSelectionMenu()
		{
			KeySelectionControl = new NSPopUpButton(new CGRect(0, 0, Frame.Width / 2, Frame.Height), true);
			KeySelectionControl.BezelStyle = NSBezelStyle.Recessed;
            KeySelectionControl.Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
            KeySelectionControl.ControlSize = NSControlSize.Small;
            KeySelectionControl.Activated += ComboBox_Activated;

            SetupKeyMenu();

            AddArrangedSubview(KeySelectionControl);
        }

        void SetupKeyMenu()
        {
            KeySelectionControl.Menu = new NSMenu();
            KeySelectionControl.Menu.AddItem(new NSMenuItem("Select Attribute Key"));

            foreach (var att in ExperimentAttribute.AvailableExperimentAttributes)
            {
                if (!att.GetProperties().AllowMultiple && ExperimentDetailsPopoverController.AllAddedOptions.Contains(att) && Option.Key != att) continue;
                KeySelectionControl.Menu.AddItem(new NSMenuItem("")
                {
                    Tag = (int)att,
                    AttributedTitle = new NSAttributedString(att.GetProperties().Name, NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize))
                });
            }

            if (Option.Key != AttributeKey.Null)
            {
                KeySelectionControl.SelectItemWithTag((int)Option.Key);
                KeySelectionControl.SynchronizeTitleAndSelectedItem();
                KeySelectionControl.Title = KeySelectionControl.TitleOfSelectedItem;
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
					SetupConcentration(ConcentrationUnit.µM);
					break;
                case AttributeKey.Salt:
                    SetupEnum();
                    SetupConcentration(ConcentrationUnit.mM, false);
                    break;
                case AttributeKey.Buffer:
                    SetupEnum();
                    SetupDouble("  pH   ");
                    SetupConcentration(ConcentrationUnit.mM, false);
                    break;
                case AttributeKey.BufferSubtraction:
                    SetupReferenceExperiment();
                    break;
            }
        }

		void SetupBool()
		{
			BoolControl = NSButton.CreateCheckbox("", () => Option.BoolValue = BoolControl.State == NSCellStateValue.On);
            BoolControl.State = Option.BoolValue ? NSCellStateValue.On : NSCellStateValue.Off;
            BoolControl.ToolTip = "Property Key: " + Option.Key.ToString();
            BoolControl.ControlSize = NSControlSize.Small;
            BoolControl.Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
            BoolControl.ImagePosition = NSCellImagePosition.ImageTrailing;
            BoolControl.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            AddArrangedSubview(BoolControl);
        }

        void SetupDouble(string description = null)
        {
            if (!string.IsNullOrEmpty(description))
            {
                var lbl = NSTextField.CreateLabel(description);
                lbl.Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
                lbl.Alignment = NSTextAlignment.Center;

                AddArrangedSubview(lbl);
            }

            DoubleField = new NSTextField(new CGRect(0, 0, 30, 14))
            {
                Bordered = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
                PlaceholderString = Option.DoubleValue.ToString("F2"),
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                FocusRingType = NSFocusRingType.None,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                Alignment = NSTextAlignment.Left,
                LineBreakMode = NSLineBreakMode.TruncatingHead,
            };
            DoubleField.Changed += (o, e) => Input_Changed(DoubleField, null);
            DoubleField.RefusesFirstResponder = true;
            DoubleField.AddConstraint(NSLayoutConstraint.Create(DoubleField, NSLayoutAttribute.Width, NSLayoutRelation.GreaterThanOrEqual, 1, 30));
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
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
            };
            lbl.AddConstraint(NSLayoutConstraint.Create(lbl, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 22));

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

            ParameterField = new NSTextField(new CGRect(0, 0, 45, 14))
            {
                Bordered = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
                PlaceholderString = value.Value.ToString("F1"),
                //DoubleValue = value.Value,
                ToolTip = "Value for the given property",
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                FocusRingType = NSFocusRingType.None,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                Alignment = NSTextAlignment.Right,
                LineBreakMode = NSLineBreakMode.TruncatingHead,
            };
            ParameterField.Changed += (o, e) => Input_Changed(ParameterField, null);
            ParameterField.RefusesFirstResponder = true;
            ParameterField.AddConstraint(NSLayoutConstraint.Create(ParameterField, NSLayoutAttribute.Width, NSLayoutRelation.GreaterThanOrEqual, 1, 35));
            ParameterField.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            AddArrangedSubview(ParameterField);

            if (includeerror)
            {
                var plusminuslabel = new NSTextField(new CGRect(0, 0, 10, 14))
                {
                    BezelStyle = NSTextFieldBezelStyle.Rounded,
                    Bordered = false,
                    Editable = false,
                    StringValue = "±",
                    TranslatesAutoresizingMaskIntoConstraints = false,
                    HorizontalContentSizeConstraintActive = false,
                    ControlSize = NSControlSize.Small,
                    Alignment = NSTextAlignment.Center,
                    TextColor = NSColor.SecondaryLabel,
                    Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                };
                plusminuslabel.AddConstraint(NSLayoutConstraint.Create(plusminuslabel, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 10));

                ParameterErrorField = new NSTextField(new CGRect(0, 0, 25, 14))
                {
                    Bordered = false,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                    PlaceholderString = value.SD.ToString("F1"),
                    //DoubleValue = value.SD,
                    ToolTip = "Error value for the given property",
                    BezelStyle = NSTextFieldBezelStyle.Rounded,
                    FocusRingType = NSFocusRingType.None,
                    ControlSize = NSControlSize.Small,
                    Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                    Alignment = NSTextAlignment.Left,
                    LineBreakMode = NSLineBreakMode.TruncatingHead,
                };
                ParameterErrorField.Changed += (o, e) => Input_Changed(ParameterErrorField, null);
                ParameterErrorField.AddConstraint(NSLayoutConstraint.Create(ParameterErrorField, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 25));
                ParameterErrorField.RefusesFirstResponder = true;

                ParameterField.NextKeyView = ParameterErrorField;
                ParameterErrorField.NextKeyView = ParameterField;

                AddArrangedSubview(plusminuslabel);
                AddArrangedSubview(ParameterErrorField);
            }

            DefaultFieldColor = ParameterField.TextColor;
        }

        NSPopUpButton DropDownMenuButton()
        {
            var btn = new NSPopUpButton(new CGRect(0, 0, Frame.Width / 2, Frame.Height), true);
            btn.BezelStyle = NSBezelStyle.Recessed;
            btn.Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
            btn.ControlSize = NSControlSize.Small;
            btn.Activated += EnumPopUpControl_Activated;
            btn.AddConstraint(NSLayoutConstraint.Create(btn, NSLayoutAttribute.Width, NSLayoutRelation.LessThanOrEqual, 1, 150));
            btn.LineBreakMode = NSLineBreakMode.TruncatingMiddle;

            btn.Menu = new NSMenu();
            btn.Menu.AddItem(new NSMenuItem("Select"));

            return btn;
        }

        void SetupDropdownMenu()
        {
            var spacer = new NSBox() { TitlePosition = NSTitlePosition.NoTitle, BoxType = NSBoxType.NSBoxCustom, BorderType = NSBorderType.NoBorder };
            spacer.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);
            AddArrangedSubview(spacer);

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
                        AttributedTitle = MacStrings.FromMarkDownString(opt.Item2, NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)),
                        ToolTip = MacStrings.FromMarkDownString(opt.Item3, NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)).Value,
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
                    AttributedTitle = MacStrings.FromMarkDownString(opt.Item2, NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)),
                    ToolTip = MacStrings.FromMarkDownString(opt.Item3, NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)).Value,
                });
            }

            AddArrangedSubview(EnumPopUpControl);

            if (selectedID != -1) { EnumPopUpControl.SelectItemWithTag(selectedID); EnumPopUpControl_Activated(null, null); }
        }

        #endregion

        public void UpdateKeyMenu()
        {
            SetupKeyMenu();
        }

        private void ComboBox_Activated(object sender, EventArgs e)
        {
            while (Views.Count() > 2)
                RemoveView(Views[2]);

            Option.UpdateOptionKey((AttributeKey)(int)KeySelectionControl.SelectedItem.Tag);

            SetupOption();

            KeyChanged?.Invoke(this, null);
        }

        void SetPopUpButtonText(string text)
        {
            EnumPopUpControl.Menu.ItemAt(0).AttributedTitle = MacStrings.FromMarkDownString(text , NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize));
        }

        private void EnumPopUpControl_Activated(object sender, EventArgs args)
        {
            switch (Option.Key)
            {
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
            }
            
        }

        private void Input_Changed(object sender, EventArgs e)
        {
            CheckInput(sender as NSTextField);
        }

        void CheckInput(NSTextField field)
        {
            field.TextColor = NSColor.SystemRed;
            string input = field.StringValue;

            if (string.IsNullOrEmpty(input)) field.TextColor = DefaultFieldColor;
            else if (double.TryParse(input, out double value))
            {
                if (value < 0) return;
                if (Option.Key == AttributeKey.Buffer && field == DoubleField && value > 14) return;
                field.TextColor = DefaultFieldColor;
            }
        }

        public void ApplyOption(ExperimentData experiment)
        {
            if (Option.Key == AttributeKey.Null) return;

            //if (!experiment.Attributes.Exists(opt => opt.Key == Option.Key))
            //{
                experiment.Attributes.Add(Option);
            //}

            switch (Option.Key)
            {
                case AttributeKey.PeptideInCell: Option.BoolValue = BoolControl.State == NSCellStateValue.On; break;
                case AttributeKey.PreboundLigandConc:
                    {
                        if (!string.IsNullOrEmpty(ParameterField.StringValue))
                        {
                            var val = ParameterField.DoubleValue / 1000000;
                            var err = ParameterErrorField.DoubleValue / 1000000;

                            var value = new FloatWithError(val, err);

                            Option.ParameterValue = value;
                        }
                        break;
                    }
                case AttributeKey.PreboundLigandAffinity:
                    {
                        if (!string.IsNullOrEmpty(ParameterField.StringValue))
                        {
                            var val = ParameterField.DoubleValue / AppSettings.DefaultConcentrationUnit.GetProperties().Mod;
                            var err = ParameterErrorField.DoubleValue / AppSettings.DefaultConcentrationUnit.GetProperties().Mod;

                            var value = new FloatWithError(val, err);

                            Option.ParameterValue = value;
                        }
                        break;
                    }

                case AttributeKey.PreboundLigandEnthalpy:
                    {
                        var val = ParameterField.DoubleValue;
                        var err = ParameterErrorField.DoubleValue;

                        var value = new Energy(new FloatWithError(val, err), AppSettings.EnergyUnit);

                        Option.ParameterValue = value.FloatWithError;
                        break;
                    }
                case AttributeKey.Salt:
                    {
                        Option.IntValue = (int)EnumPopUpControl.SelectedTag;
                        if (!string.IsNullOrEmpty(ParameterField.StringValue)) Option.ParameterValue = new(ParameterField.DoubleValue / 1000, 0);
                        break;
                    }
                case AttributeKey.Buffer:
                    {
                        Option.IntValue = (int)EnumPopUpControl.SelectedTag;
                        if (!string.IsNullOrEmpty(DoubleField.StringValue)) Option.DoubleValue = DoubleField.DoubleValue;
                        if (!string.IsNullOrEmpty(ParameterField.StringValue)) Option.ParameterValue = new(ParameterField.DoubleValue / 1000, 0);
                        break;
                    }
                case AttributeKey.IonicStrength:
                    {
                        if (!string.IsNullOrEmpty(ParameterField.StringValue)) Option.ParameterValue = new(ParameterField.DoubleValue / 1000, 0);
                        break;
                    }
                case AttributeKey.BufferSubtraction:
                    {
                        var idx = (int)EnumPopUpControl.SelectedTag;
                        Option.StringValue = DataManager.Data[idx].UniqueID;
                        break;
                    }
            }
        }
    }
}