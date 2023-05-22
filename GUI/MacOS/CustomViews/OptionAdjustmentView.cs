using System;
using AnalysisITC.AppClasses.Analysis2;
using AnalysisITC.AppClasses.AnalysisClasses;
using AppKit;
using CoreGraphics;
//using static AnalysisITC.Analysis;
using Foundation;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
    public class OptionAdjustmentView : NSStackView
    {
        ModelOptions Option { get; set; }
        public ModelOptionKey Key => Option.Key;

        double tmpvalue;

        private NSTextField Label;
        private NSButton InputButton;
        private NSTextField InputField;
        private NSTextField InputErrorField;
        private CustomDrawingSegmentedControl ParameterOptionControl;

        public bool HasBeenAffectedFlag { get; private set; } = false;

        public override CGSize IntrinsicContentSize => new CGSize(Frame.Width, 14);
        public override nfloat Spacing { get => 0; set => base.Spacing = value; }
        private NSColor DefaultFieldColor;

        string InputString
        {
            get
            {
                string input = InputField.StringValue;

                input = input.Replace(',', '.');
                input = input.Replace(" ", "");

                return input;
            }
        }

        public void SetupDesignerLayout()
        {
            switch (Key)
            {
                case ModelOptionKey.PreboundLigandConc: InputButton.Hidden = true; break;
            }
        }

        public OptionAdjustmentView(IntPtr handle) : base(handle)
        {
        }

        [Export("initWithFrame:")]
        public OptionAdjustmentView(CGRect frameRect) : base(frameRect)
        {
            Frame = frameRect;
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            Distribution = NSStackViewDistribution.Fill;
            Alignment = NSLayoutAttribute.CenterY;
            SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);
            SetHuggingPriority(1000, NSLayoutConstraintOrientation.Vertical);

            Label = new NSTextField(new CGRect(0, 0, 150, 14))
            {
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                Bordered = false,
                Editable = false,
                StringValue = "init",
                TranslatesAutoresizingMaskIntoConstraints = false,
                HorizontalContentSizeConstraintActive = false,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
            };

            ParameterOptionControl = new CustomDrawingSegmentedControl(new CGRect(0, 0, 0, 14))
            {
                Cell = new CustomKdKaDrawingSegmentedCell(),
                SegmentStyle = NSSegmentStyle.Capsule,
                ControlSize = NSControlSize.Small,
                SegmentDistribution = NSSegmentDistribution.FillEqually,
                SegmentCount = 2,
                Font = NSFont.SystemFontOfSize(10),
                Hidden = true,
            };
            ParameterOptionControl.SetImageScaling(NSImageScaling.ProportionallyDown, 0);
            ParameterOptionControl.SetImageScaling(NSImageScaling.ProportionallyDown, 1);
            ParameterOptionControl.Activated += ParameterOptionControl_Activated;
            ParameterOptionControl.SizeToFit();

            InputField = new NSTextField(new CGRect(0, 0, 100, 14))
            {
                Bordered = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
                PlaceholderString = "Auto",
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                FocusRingType = NSFocusRingType.None,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                Alignment = NSTextAlignment.Right,
                LineBreakMode = NSLineBreakMode.TruncatingHead,
            };
            InputField.Changed += Input_Changed;
            InputField.AddConstraint(NSLayoutConstraint.Create(InputField, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 60));
            InputField.RefusesFirstResponder = true;

            AddArrangedSubview(Label);
            AddArrangedSubview(ParameterOptionControl);
            AddArrangedSubview(InputField);
        }

        public OptionAdjustmentView(CGRect frameRect, ModelOptions option) : base(frameRect)
        {
            Frame = frameRect;
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            Distribution = NSStackViewDistribution.Fill;
            Alignment = NSLayoutAttribute.CenterY;
            Option = option;

            switch (option.Key)
            {
                case ModelOptionKey.PeptideInCell:
                    SetupBoolOption(); break;
                case ModelOptionKey.PreboundLigandConc:
                case ModelOptionKey.PreboundLigandAffinity:
                case ModelOptionKey.PreboundLigandEnthalpy:
                    SetupLabel();
                    SetupParameterOptionLabel();
                    SetupInputErrorFields();
                    break;
                case ModelOptionKey.Buffer:
                    SetupLabel();
                    break;
            }
        }

        void SetupLabel()
        {
            Label = new NSTextField(new CGRect(0, 0, 200, 14))
            {
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                Bordered = false,
                Editable = false,
                StringValue = Option.OptionName,
                ToolTip = "Property Key: " + Option.Key.ToString(),
                TranslatesAutoresizingMaskIntoConstraints = false,
                HorizontalContentSizeConstraintActive = false,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
            };

            AddArrangedSubview(Label);
        }

        void SetupBoolOption()
        {
            InputButton = NSButton.CreateCheckbox(Option.OptionName, () => Method());
            InputButton.State = Option.BoolValue ? NSCellStateValue.On : NSCellStateValue.Off;
            InputButton.ToolTip = "Property Key: " + Option.Key.ToString();
            InputButton.ControlSize = NSControlSize.Small;
            InputButton.Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
            InputButton.ImagePosition = NSCellImagePosition.ImageTrailing;

            AddArrangedSubview(InputButton);
        }

        void SetupParameterOptionLabel()
        {
            if (Option.Key == ModelOptionKey.PreboundLigandAffinity)
            {
                Label.StringValue = Option.OptionName + $" ({AppSettings.DefaultConcentrationUnit.ToString()})";
            }
            else if (Option.Key == ModelOptionKey.PreboundLigandConc)
            {
                //Label.StringValue = Option.OptionName + $" ({AppSettings.DefaultConcentrationUnit.ToString()})";
            }
            else if (Option.Key == ModelOptionKey.PreboundLigandEnthalpy)
            {
                Label.StringValue += " (" + AppSettings.EnergyUnit.GetProperties().Unit + ")";
            }
        }

        void SetupInputErrorFields()
        {
            FloatWithError value = Option.ParameterValue;

            if (Option.Key == ModelOptionKey.PreboundLigandAffinity)
            {
                value = new FloatWithError(1.0) / value;
                value *= AppSettings.DefaultConcentrationUnit.GetProperties().Mod;
            }
            else if (Option.Key == ModelOptionKey.PreboundLigandConc)
            {
                value *= 1000000;

                InputButton = new NSButton();
                //InputButton = NSButton.CreateCheckbox("From Exp", () => Method());
                InputButton.SetButtonType(NSButtonType.OnOff);
                InputButton.Title = "FromExp";
                InputButton.ToolTip = $"When enabled, the parameter value will be taken from the matching experiment property ({Option.Key.ToString()}) rather than from the available input field";
                InputButton.State = Option.BoolValue ? NSCellStateValue.On : NSCellStateValue.Off;
                InputButton.BezelStyle = NSBezelStyle.Recessed;
                InputButton.ControlSize = NSControlSize.Mini;
                InputButton.Font = NSFont.SystemFontOfSize(9);
                //InputButton.ImagePosition = NSCellImagePosition.ImageTrailing;

                AddArrangedSubview(InputButton);
            }
            else if (Option.Key == ModelOptionKey.PreboundLigandEnthalpy)
            {
                value /= 1000;
            }

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
            InputField.Changed += Input_Changed;
            InputField.AddConstraint(NSLayoutConstraint.Create(InputField, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 45));
            InputField.RefusesFirstResponder = true;

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
            InputErrorField.Changed += Input_Changed;
            InputErrorField.AddConstraint(NSLayoutConstraint.Create(InputErrorField, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 25));
            InputErrorField.RefusesFirstResponder = true;

            AddArrangedSubview(InputField);
            //AddArrangedSubview(plusminuslabel);
            //AddArrangedSubview(InputErrorField);

            DefaultFieldColor = InputField.TextColor;
        }

        void Method()
        {
            Console.WriteLine($"InputButtonActivated [{Option.OptionName}]: " + InputButton.State.ToString());
        }

        private void ParameterOptionControl_Activated(object sender, EventArgs e)
        {
            if (Option.Key == ModelOptionKey.PreboundLigandAffinity)
            {
                AppSettings.InputAffinityAsDissociationConstant = (sender as NSSegmentedControl).SelectedSegment == 1;
                Label.StringValue = Option.OptionName + (AppSettings.InputAffinityAsDissociationConstant ? $" ({AppSettings.DefaultConcentrationUnit.ToString()})" : "");
            }

            //CheckInput();
        }

        private void Input_Changed(object sender, EventArgs e)
        {
            HasBeenAffectedFlag = true;

            CheckInput();
        }

        void CheckInput()
        {
            InputField.TextColor = NSColor.SystemRed;
            string input = InputString;

            if (string.IsNullOrEmpty(input)) InputField.TextColor = DefaultFieldColor;
            else if (double.TryParse(input, out double value))
            {
                InputField.TextColor = DefaultFieldColor;
            }
        }

        public void ApplyOptions()
        {
            switch (Option.Key)
            {
                case ModelOptionKey.PeptideInCell:
                    Option.BoolValue = InputButton.State == NSCellStateValue.On;
                    break;
                case ModelOptionKey.PreboundLigandConc:
                    {
                        Option.BoolValue = InputButton.State == NSCellStateValue.On;
                        if (string.IsNullOrEmpty(InputField.StringValue)) return;
                        var val = InputField.DoubleValue / 1000000;
                        var err = InputErrorField.DoubleValue / 1000000;

                        var value = new FloatWithError(val, err);

                        Option.ParameterValue = value;
                        Option.BoolValue = InputButton.State == NSCellStateValue.On;
                        break;
                    }

                case ModelOptionKey.PreboundLigandAffinity:
                    {
                        if (string.IsNullOrEmpty(InputField.StringValue)) return;
                        if (InputField.DoubleValue == 0) return;
                        var val = InputField.DoubleValue / AppSettings.DefaultConcentrationUnit.GetProperties().Mod;
                        var err = InputErrorField.DoubleValue / AppSettings.DefaultConcentrationUnit.GetProperties().Mod;

                        var k = 1 / val;
                        var k_err = err / val * k;

                        var value = new FloatWithError(k, k_err);

                        Option.ParameterValue = value;
                        break;
                    }

                case ModelOptionKey.PreboundLigandEnthalpy:
                    {
                        if (string.IsNullOrEmpty(InputField.StringValue)) return;
                        var val = InputField.DoubleValue;
                        var err = InputErrorField.DoubleValue;

                        var value = new Energy(new FloatWithError(val, err), AppSettings.EnergyUnit);

                        Option.ParameterValue = value.FloatWithError;
                        break;
                    }
            }
        }
    }
}