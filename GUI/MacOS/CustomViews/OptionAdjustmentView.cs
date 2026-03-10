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
        public ExperimentAttribute Option { get; private set; }
        public AttributeKey Key => Option.Key;

        double tmpvalue;

        private NSTextField Label;
        private NSButton InputButton;
        private NSTextField InputField;
        private NSTextField InputErrorField;
        private ValueWithErrorTextField InputValueWithErrorField;
        private CustomDrawingSegmentedControl ParameterOptionControl;

        public bool HasBeenAffectedFlag { get; private set; } = false;

        public override CGSize IntrinsicContentSize => new CGSize(NSView.NoIntrinsicMetric, 16);
        public override nfloat Spacing { get => 1; set => base.Spacing = value; }
        private NSColor DefaultFieldColor;

        public void SetupDesignerLayout()
        {
            switch (Key)
            {
                case AttributeKey.PreboundLigandConc: InputButton.Hidden = true; break;
            }
        }

        public OptionAdjustmentView(IntPtr handle) : base(handle)
        {
        }

        public OptionAdjustmentView(CGRect frameRect, ExperimentAttribute option) : base(frameRect)
        {
            Frame = frameRect;
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            Distribution = NSStackViewDistribution.Fill;
            Alignment = NSLayoutAttribute.CenterY;
            Option = option;

            switch (option.Key)
            {
                case AttributeKey.LockDuplicateParameter:
                case AttributeKey.PeptideInCell:
                    SetupBoolOption(); break;
                case AttributeKey.Percentage:
                case AttributeKey.EquilibriumConstant:
                case AttributeKey.PreboundLigandConc:
                case AttributeKey.PreboundLigandAffinity:
                case AttributeKey.PreboundLigandEnthalpy:
                    SetupLabel();
                    SetupParameterOptionLabel();
                    SetupInputErrorFields();
                    break;
                case AttributeKey.Buffer:
                    SetupLabel();
                    break;
            }

            SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);
            SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Vertical);
        }

        void SetupLabel()
        {
            Label = new NSTextField(new CGRect(0, 0, 200, 16))
            {
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                Bordered = false,
                Editable = false,
                AttributedStringValue = Utilities.MacStrings.FromMarkDownString(Option.OptionName, NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize)),
                //StringValue = Option.OptionName,
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
            if (Option.Key == AttributeKey.PreboundLigandAffinity)
            {
                Label.AttributedStringValue = Utilities.MacStrings.FromMarkDownString(
                    Option.OptionName + $" ({Utilities.MarkdownStrings.DissociationConstant}, {AppSettings.DefaultConcentrationUnit})",
                    NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize));
            }
            else if (Option.Key == AttributeKey.PreboundLigandConc)
            {
                Label.AttributedStringValue = Utilities.MacStrings.FromMarkDownString(
                    Option.OptionName + $" ({AppSettings.DefaultConcentrationUnit})",
                    NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize));
            }
            else if (Option.Key == AttributeKey.PreboundLigandEnthalpy)
            {
                Label.StringValue += " (" + AppSettings.EnergyUnit.GetProperties().Unit + "/mol)";
            }
        }

        void SetupInputErrorFields()
        {
            FloatWithError value = Option.ParameterValue;

            switch (Option.Key)
            {
                case AttributeKey.PreboundLigandAffinity:
                    value = new FloatWithError(1.0) / value;
                    value *= AppSettings.DefaultConcentrationUnit.GetProperties().Mod;
                    break;
                case AttributeKey.PreboundLigandConc:
                    value *= 1000000;
                    break;
                case AttributeKey.PreboundLigandEnthalpy:
                    value /= 1000;
                    break;
                case AttributeKey.Percentage:
                    value *= 100;
                    break;
            }

            switch (Option.Key)
            {
                case AttributeKey.Percentage:
                case AttributeKey.PreboundLigandConc:
                case AttributeKey.EquilibriumConstant:
                    InputButton = new NSButton(new CGRect(0, 0, 50, 16));
                    InputButton.SetButtonType(NSButtonType.PushOnPushOff);
                    InputButton.Title = "From Attributes";
                    InputButton.ToolTip = $"Enable to retrieve value from experiment attributes";
                    InputButton.State = Option.BoolValue ? NSCellStateValue.On : NSCellStateValue.Off;
                    InputButton.BezelStyle = NSBezelStyle.Recessed;
                    InputButton.ControlSize = NSControlSize.Mini;
                    //InputButton.Font = NSFont.SystemFontOfSize(10);

                    AddArrangedSubview(InputButton);
                    break;
            }

            InputValueWithErrorField = new ValueWithErrorTextField(new CGRect(0, 0, 80, 19))
            {
                ToolTip = "Value and optional uncertainty. Press space to enter uncertainty.",
            };

            InputValueWithErrorField.SetValue(value.Value, value.SD);
            InputValueWithErrorField.Changed += Input_Changed;
            InputValueWithErrorField.AddConstraint(NSLayoutConstraint.Create(InputValueWithErrorField, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 80));
            InputValueWithErrorField.AddConstraint(NSLayoutConstraint.Create(InputValueWithErrorField, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 19));

            AddArrangedSubview(InputValueWithErrorField);

            DefaultFieldColor = InputValueWithErrorField.TextColor;

            return;
        }

        void Method()
        {
            Console.WriteLine($"InputButtonActivated [{Option.OptionName}]: " + InputButton.State.ToString());
        }

        private void ParameterOptionControl_Activated(object sender, EventArgs e)
        {
            if (Option.Key == AttributeKey.PreboundLigandAffinity)
            {
                AppSettings.InputAffinityAsDissociationConstant = (sender as NSSegmentedControl).SelectedSegment == 1;
                Label.StringValue = Option.OptionName + (AppSettings.InputAffinityAsDissociationConstant ? $" ({AppSettings.DefaultConcentrationUnit})" : "");
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
            InputValueWithErrorField.TextColor = NSColor.SystemRed;

            if (string.IsNullOrWhiteSpace(InputValueWithErrorField.ValueText))
            {
                InputValueWithErrorField.TextColor = DefaultFieldColor;
                return;
            }

            if (InputValueWithErrorField.HasValidInput)
                InputValueWithErrorField.TextColor = DefaultFieldColor;
        }

        public void ApplyOptions()
        {
            if (InputButton != null)
            {
                Option.BoolValue = InputButton.State == NSCellStateValue.On;
            }

            if (InputValueWithErrorField != null)
            {
                if (!InputValueWithErrorField.TryGetValue(out double val, out double err))
                    return;

                switch (Option.Key)
                {
                    case AttributeKey.Percentage:
                        {
                            val /= 100;
                            err /= 100;

                            var value = new FloatWithError(val, err);

                            Option.ParameterValue = value;
                            break;
                        }
                    case AttributeKey.EquilibriumConstant:
                        {
                            var value = new FloatWithError(val, err);

                            Option.ParameterValue = value;
                            break;
                        }
                    case AttributeKey.PreboundLigandConc:
                        {
                            val /= 1000000;
                            err /= 1000000;

                            var value = new FloatWithError(val, err);

                            Option.ParameterValue = value;
                            break;
                        }
                    case AttributeKey.PreboundLigandAffinity:
                        {
                            val /= AppSettings.DefaultConcentrationUnit.GetProperties().Mod;
                            err /= AppSettings.DefaultConcentrationUnit.GetProperties().Mod;

                            var k = 1 / val;
                            var k_err = err / val * k;

                            var value = new FloatWithError(k, k_err);

                            Option.ParameterValue = value;
                            break;
                        }
                    case AttributeKey.PreboundLigandEnthalpy:
                        {
                            var value = new Energy(new FloatWithError(val, err), AppSettings.EnergyUnit);
                            Option.ParameterValue = value.FloatWithError;
                            break;
                        }  
                }
            }

            // Store in array of options
            ModelFactory.StorePreviousAttribute(Option);
        }

        public override string ToString()
        {
            return Key.ToString() + ": " + Option.OptionName;
        }
    }
}