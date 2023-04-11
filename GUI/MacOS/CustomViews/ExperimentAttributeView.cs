using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using AppKit;
using CoreGraphics;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
	public partial class ExperimentAttributeView : AppKit.NSStackView
	{
		public event EventHandler Remove;
        public event EventHandler KeyChanged;

		public ModelOptions Option { get; private set; }

        public override nfloat Spacing { get => 1; set => base.Spacing = value; }

        NSPopUpButton KeySelectionControl { get; set; }
        NSButton BoolControl { get; set; }
        NSTextField InputField { get; set; }
        NSTextField InputErrorField { get; set; }
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
                if (ExperimentDetailsPopoverController.AllAddedOptions.Contains(att) && Option.Key != att) continue;
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
                case ModelOptionKey.PreboundLigandAffinity:
                case ModelOptionKey.PreboundLigandConc:
					SetupConcentration();
					break;
                case ModelOptionKey.Buffer:
                    SetupEnum();
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

		void SetupConcentration()
		{
            SetupParameter();

            NSTextField lbl = new NSTextField(new CGRect(0, 0, 20, 14))
            {
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                Bordered = false,
                Editable = false,
                StringValue = "µM",
                TranslatesAutoresizingMaskIntoConstraints = false,
                HorizontalContentSizeConstraintActive = false,
                ControlSize = NSControlSize.Small,
                Alignment = NSTextAlignment.Right,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
            };
            lbl.AddConstraint(NSLayoutConstraint.Create(lbl, NSLayoutAttribute.Width, NSLayoutRelation.LessThanOrEqual, 1, 20));

            AddArrangedSubview(lbl);
        }

		void SetupParameter()
		{
            FloatWithError value = Option.ParameterValue;

            if (Option.Key == ModelOptionKey.PreboundLigandConc) value *= 1000000;

            InputField = new NSTextField(new CGRect(0, 0, 45, 14))
            {
                Bordered = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
                PlaceholderString = value.Value.ToString("F1"),
                ToolTip = "Value for the given property",
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                FocusRingType = NSFocusRingType.None,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                Alignment = NSTextAlignment.Right,
                LineBreakMode = NSLineBreakMode.TruncatingHead,
            };
            InputField.Changed += (o, e) => Input_Changed(InputField, null);
            InputField.RefusesFirstResponder = true;
            InputField.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);
            

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

            InputErrorField = new NSTextField(new CGRect(0, 0, 25, 14))
            {
                Bordered = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
                PlaceholderString = value.SD.ToString("F1"),
                ToolTip = "Error value for the given property",
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                FocusRingType = NSFocusRingType.None,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                Alignment = NSTextAlignment.Left,
                LineBreakMode = NSLineBreakMode.TruncatingHead,
            };
            InputErrorField.Changed += (o,e) => Input_Changed(InputErrorField, null);
            InputErrorField.AddConstraint(NSLayoutConstraint.Create(InputErrorField, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 25));
            InputErrorField.RefusesFirstResponder = true;

            InputField.NextKeyView = InputErrorField;
            InputErrorField.NextKeyView = InputField;

            AddArrangedSubview(InputField);
            AddArrangedSubview(plusminuslabel);
            AddArrangedSubview(InputErrorField);

            DefaultFieldColor = InputField.TextColor;
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

                for (int i = 0; i < Option.EnumOptionCount; i++)
                {
                    string opt = Option.EnumOptions.ToList()[i];
                    EnumPopUpControl.Menu.AddItem(new NSMenuItem("")
                    {
                        Tag = i,
                        AttributedTitle = new NSAttributedString(opt, NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize))
                    });
                }

                AddArrangedSubview(EnumPopUpControl);

                if (Option.IntValue != -1) { EnumPopUpControl.SelectItemWithTag(Option.IntValue); EnumPopUpControl_Activated(null, null); }
            }
            else
            {

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

        private void EnumPopUpControl_Activated(object sender, EventArgs e)
        {
            EnumPopUpControl.Menu.ItemAt(0).AttributedTitle = new NSAttributedString(Option.EnumOptions.ToList()[(int)EnumPopUpControl.SelectedTag], NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize));
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
                field.TextColor = DefaultFieldColor;
            }
        }

        public void ApplyOption(ExperimentData experiment)
        {
            if (Option.Key == ModelOptionKey.Null) return;

            if (!experiment.ExperimentOptions.ContainsKey(Option.Key))
            {
                experiment.ExperimentOptions.Add(Option.DictionaryEntry);
            }

            switch (Option.Key)
            {
                case ModelOptionKey.PeptideInCell: Option.BoolValue = BoolControl.State == NSCellStateValue.On; break;
                case ModelOptionKey.PreboundLigandConc:
                    {
                        var val = InputField.DoubleValue / 1000000;
                        var err = InputErrorField.DoubleValue / 1000000;

                        var value = new FloatWithError(val, err);

                        Option.ParameterValue = value;
                        break;
                    }

                case ModelOptionKey.PreboundLigandAffinity:
                    {
                        var val = InputField.DoubleValue / AppSettings.DefaultConcentrationUnit.GetProperties().Mod;
                        var err = InputErrorField.DoubleValue / AppSettings.DefaultConcentrationUnit.GetProperties().Mod;

                        var value = new FloatWithError(val, err);

                        Option.ParameterValue = value;
                        break;
                    }

                case ModelOptionKey.PreboundLigandEnthalpy:
                    {
                        var val = InputField.DoubleValue;
                        var err = InputErrorField.DoubleValue;

                        var value = new Energy(new FloatWithError(val, err), AppSettings.EnergyUnit);

                        Option.ParameterValue = value.FloatWithError;
                        break;
                    }
                case ModelOptionKey.Buffer: Option.IntValue = (int)EnumPopUpControl.IndexOfSelectedItem - 1; break;
            }
        }
    }
}