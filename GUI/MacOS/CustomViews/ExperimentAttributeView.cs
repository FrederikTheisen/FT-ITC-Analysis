using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using AppKit;
using CoreGraphics;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.Utils;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
	public partial class ExperimentAttributeView : AppKit.NSStackView
	{
		public event EventHandler Remove;
        public event EventHandler KeyChanged;
        public event EventHandler<Tuple<ModelOptionKey,int>> SpecialAttributeSelected;

        public ModelOptions Option { get; private set; }

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

		public ExperimentAttributeView(CGRect frameRect, ModelOptions option) : base(frameRect)
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

            foreach (var att in ModelOptions.AvailableExperimentAttributes)
            {
                if (!att.GetProperties().AllowMultiple && ExperimentDetailsPopoverController.AllAddedOptions.Contains(att) && Option.Key != att) continue;
                KeySelectionControl.Menu.AddItem(new NSMenuItem("")
                {
                    Tag = (int)att,
                    AttributedTitle = new NSAttributedString(att.ToString(), NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize))
                });
            }

            if (Option.Key != ModelOptionKey.Null)
            {
                KeySelectionControl.SelectItemWithTag((int)Option.Key);
                KeySelectionControl.SynchronizeTitleAndSelectedItem();
                KeySelectionControl.Title = KeySelectionControl.TitleOfSelectedItem;
            }
        }

        void SetupOption()
		{
			if (Option.Key == ModelOptionKey.Null) return;

			KeySelectionControl.SynchronizeTitleAndSelectedItem();
			KeySelectionControl.Title = KeySelectionControl.TitleOfSelectedItem;

			switch (Option.Key)
			{
                case ModelOptionKey.PeptideInCell:
					SetupBool();
					break;
                case ModelOptionKey.IonicStrength:
                    SetupConcentration(ConcentrationUnit.mM, false);
                    break;
                case ModelOptionKey.PreboundLigandAffinity:
                case ModelOptionKey.PreboundLigandConc:
					SetupConcentration(ConcentrationUnit.µM);
					break;
                case ModelOptionKey.Salt:
                    SetupEnum();
                    SetupConcentration(ConcentrationUnit.mM, false);
                    break;
                case ModelOptionKey.Buffer:
                    SetupEnum();
                    SetupDouble("  pH   ");
                    SetupConcentration(ConcentrationUnit.mM, false);
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
                case ModelOptionKey.PreboundLigandConc:
                    value *= 1000000;
                    break;
                case ModelOptionKey.IonicStrength:
                case ModelOptionKey.Salt:
                case ModelOptionKey.Buffer:
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

        void SetupEnum()
        {
            var spacer = new NSBox() { TitlePosition = NSTitlePosition.NoTitle, BoxType = NSBoxType.NSBoxCustom, BorderType = NSBorderType.NoBorder };
            spacer.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);
            AddArrangedSubview(spacer);

            if (Option.EnumOptionCount > 3)
            {
                EnumPopUpControl = new NSPopUpButton(new CGRect(0, 0, Frame.Width / 2, Frame.Height), true);
                EnumPopUpControl.BezelStyle = NSBezelStyle.Recessed;
                EnumPopUpControl.Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
                EnumPopUpControl.ControlSize = NSControlSize.Small;
                EnumPopUpControl.Activated += EnumPopUpControl_Activated;

                EnumPopUpControl.Menu = new NSMenu();
                EnumPopUpControl.Menu.AddItem(new NSMenuItem("Select"));

                var opts = Option.EnumOptions.ToList();

                for (int i = 0; i < Option.EnumOptionCount; i++)
                {
                    var opt = opts[i];
                    if (opt.Item1 != -1) EnumPopUpControl.Menu.AddItem(new NSMenuItem("")
                    {
                        Tag = opt.Item1,
                        AttributedTitle = MacStrings.FromMarkDownString(opt.Item2, NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)),
                        ToolTip = MacStrings.FromMarkDownString(opt.Item3, NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)).Value,
                    });
                    else EnumPopUpControl.Menu.AddItem(NSMenuItem.SeparatorItem);
                }

                AddArrangedSubview(EnumPopUpControl);

                if (Option.IntValue != -1) { EnumPopUpControl.SelectItemWithTag(Option.IntValue); EnumPopUpControl_Activated(null, null); }
            }
            else
            {
                AppEventHandler.DisplayHandledException(new NotImplementedException("Attribute with less than 3 options. Please fix."));
            }
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

            Option.UpdateOptionKey((ModelOptionKey)(int)KeySelectionControl.SelectedItem.Tag);

            SetupOption();

            KeyChanged?.Invoke(this, null);
        }

        private void EnumPopUpControl_Activated(object sender, EventArgs args)
        {
            switch (Option.Key)
            {
                case ModelOptionKey.Buffer:
                    switch ((Buffer)(int)EnumPopUpControl.SelectedTag)
                    {
                        case Buffer.PBS:
                        case Buffer.TBS:
                            Remove?.Invoke(this, null);
                            SpecialAttributeSelected?.Invoke(this, new(Option.Key, (int)EnumPopUpControl.SelectedTag));
                            return;
                    }
                    break;
            }
            EnumPopUpControl.Menu.ItemAt(0).AttributedTitle = MacStrings.FromMarkDownString(Option.EnumOptions.Single(e => e.Item1 == (int)EnumPopUpControl.SelectedTag).Item2, NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize));
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
                if (Option.Key == ModelOptionKey.Buffer && field == DoubleField && value > 14) return;
                field.TextColor = DefaultFieldColor;
            }
        }

        public void ApplyOption(ExperimentData experiment)
        {
            if (Option.Key == ModelOptionKey.Null) return;

            //if (!experiment.Attributes.Exists(opt => opt.Key == Option.Key))
            //{
                experiment.Attributes.Add(Option);
            //}

            switch (Option.Key)
            {
                case ModelOptionKey.PeptideInCell: Option.BoolValue = BoolControl.State == NSCellStateValue.On; break;
                case ModelOptionKey.PreboundLigandConc:
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
                case ModelOptionKey.PreboundLigandAffinity:
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

                case ModelOptionKey.PreboundLigandEnthalpy:
                    {
                        var val = ParameterField.DoubleValue;
                        var err = ParameterErrorField.DoubleValue;

                        var value = new Energy(new FloatWithError(val, err), AppSettings.EnergyUnit);

                        Option.ParameterValue = value.FloatWithError;
                        break;
                    }
                case ModelOptionKey.Salt:
                    {
                        Option.IntValue = (int)EnumPopUpControl.SelectedTag;
                        if (!string.IsNullOrEmpty(ParameterField.StringValue)) Option.ParameterValue = new(ParameterField.DoubleValue / 1000, 0);
                        break;
                    }
                case ModelOptionKey.Buffer:
                    {
                        Option.IntValue = (int)EnumPopUpControl.SelectedTag;
                        if (!string.IsNullOrEmpty(DoubleField.StringValue)) Option.DoubleValue = DoubleField.DoubleValue;
                        if (!string.IsNullOrEmpty(ParameterField.StringValue)) Option.ParameterValue = new(ParameterField.DoubleValue / 1000, 0);
                        break;
                    }
                case ModelOptionKey.IonicStrength:
                    {
                        if (!string.IsNullOrEmpty(ParameterField.StringValue)) Option.ParameterValue = new(ParameterField.DoubleValue / 1000, 0);
                        break;
                    }
            }
        }
    }
}