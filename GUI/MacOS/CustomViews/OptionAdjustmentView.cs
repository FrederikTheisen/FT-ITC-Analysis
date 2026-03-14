using System;
using System.Collections.Generic;
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
        public static event EventHandler RefreshLists;

        public ExperimentAttribute Option { get; private set; }
        public AttributeKey Key => Option.Key;

        double tmpdouble;
        bool tmpbool;

        private NSTextField Label;
        private NSButton InputButton;
        private NSSwitch InputSwitch;
        private NSTextField InputField;
        private NSTextField InputErrorField;
        private ValueWithErrorTextField InputValueWithErrorField;
        private CustomDrawingSegmentedControl ParameterOptionControl;
        public NSPopUpButton StoichiometryPopup { get; set; }

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

            tmpbool = Option.BoolValue;

            switch (Option.Key)
            {
                case AttributeKey.NumberOfSites:
                    SetupLabel();
                    SetupStoichiometryOption();
                    break;
                case AttributeKey.LockDuplicateParameter:
                case AttributeKey.PeptideInCell:
                case AttributeKey.UseSyringeActiveFraction:
                    SetupBoolOption(); break;
                case AttributeKey.Percentage:
                case AttributeKey.EquilibriumConstant:
                case AttributeKey.PreboundLigandConc:
                case AttributeKey.PreboundLigandAffinity:
                case AttributeKey.PreboundLigandEnthalpy:
                    SetupLabel();
                    SetupParameterOptionLabel();
                    SetupParameterOption();
                    break;
                case AttributeKey.Buffer:
                    SetupLabel();
                    break;
            }

            SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);
            SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Vertical);
        }

        public void UpdateState(IDictionary<AttributeKey,ExperimentAttribute> attributes)
        {
            switch (Option.Key)
            {
                case AttributeKey.NumberOfSites:
                    bool enable = attributes[AttributeKey.UseSyringeActiveFraction].BoolValue;
                    Label.Enabled = enable;
                    StoichiometryPopup.Enabled = enable;
                    break;
            }
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

            InputButton.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            Label = new NSTextField
            {
                StringValue = Option.OptionName,
                Editable = false,
                Bezeled = false,
                DrawsBackground = false,
                Selectable = false,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                ToolTip = "Property Key: " + Option.Key.ToString()
            };

            Label.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            InputSwitch = new NSSwitch
            {
                State = Option.BoolValue ? (int)NSCellStateValue.On : (int)NSCellStateValue.Off,
                ControlSize = NSControlSize.Mini,
                ToolTip = "Property Key: " + Option.Key.ToString()
            };

            InputSwitch.Activated += (s, e) => Method();

            AddArrangedSubview(Label);
            AddArrangedSubview(InputSwitch);
        }

        void SetupIntegerOption()
        {
            InputField = new NSTextField(new CGRect(0, 0, 80, 19))
            {
                StringValue = Option.IntValue.ToString(),
                PlaceholderString = Option.IntValue.ToString(),
                TranslatesAutoresizingMaskIntoConstraints = false,
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                FocusRingType = NSFocusRingType.None,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                Alignment = NSTextAlignment.Right,
            };
            //InputField.Formatter = new NSNumberFormatter()
            //{
            //    NumberStyle = NSNumberFormatterStyle.None,
            //    Minimum = 1,
            //    Maximum = 10,
            //    MaximumFractionDigits = 0,
            //    MaximumIntegerDigits = 2,
            //    RoundingIncrement = 1
            //};
            InputField.AddConstraint(NSLayoutConstraint.Create(InputField, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 80));
            InputField.AddConstraint(NSLayoutConstraint.Create(InputField, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 19));

            InputField.Changed += InputChanged;

            DefaultFieldColor = InputField.TextColor;

            AddArrangedSubview(InputField);
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

        void SetupParameterOption()
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
            InputValueWithErrorField.Changed += ParameterInputChanged;
            InputValueWithErrorField.AddConstraint(NSLayoutConstraint.Create(InputValueWithErrorField, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 80));
            InputValueWithErrorField.AddConstraint(NSLayoutConstraint.Create(InputValueWithErrorField, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 19));

            AddArrangedSubview(InputValueWithErrorField);

            DefaultFieldColor = InputValueWithErrorField.TextColor;

            return;
        }

        void SetupStoichiometryOption()
        {
            StoichiometryPopup = new NSPopUpButton(CGRect.Empty, pullsDown: false)
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                BezelStyle = NSBezelStyle.Recessed,
                ControlSize = NSControlSize.Small
            };

            StoichiometryPopupBuilder.Populate(StoichiometryPopup);
            StoichiometryPopupBuilder.Select(StoichiometryPopup, Option.DoubleValue);

            StoichiometryPopup.Activated += StoichiometryPopup_Activated;

            AddArrangedSubview(StoichiometryPopup);
        }

        void Method()
        {
            Console.WriteLine($"InputButtonActivated [{Option.OptionName}]: " + InputSwitch.State.ToString());

            Option.BoolValue = InputSwitch.State == (int)NSCellStateValue.On;

            RefreshLists?.Invoke(this, null);
        }

        void ParameterInputChanged(object sender, EventArgs e)
        {
            HasBeenAffectedFlag = true;

            CheckParameterInput();
        }

        void CheckParameterInput()
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

        void InputChanged(object sender, EventArgs e)
        {
            HasBeenAffectedFlag = true;
        }

        void StoichiometryPopup_Activated(object sender, EventArgs e)
        {
            var selected = StoichiometryPopupBuilder.GetSelected(StoichiometryPopup);

            Console.WriteLine("Stoichiometry = " + selected);

            // Optional if you want to show a label somewhere else:
            // StoichiometryInfoLabel.StringValue = selected.Title;

            // Optional if your parameter list needs rebuilding after mode changes:
            // ReloadParameterList();
        }

        public void ApplyOptions()
        {
            if (InputButton != null)
            {
                Option.BoolValue = InputSwitch.State == (int)NSCellStateValue.On;
            }

            if (StoichiometryPopup != null)
            {
                switch (Option.Key)
                {
                    case AttributeKey.NumberOfSites:
                        var selected = StoichiometryPopupBuilder.GetSelected(StoichiometryPopup);
                        Option.DoubleValue = selected.Factor;
                        break;
                }
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

        public void Revert()
        {
            switch (Option.Key)
            {
                case AttributeKey.UseSyringeActiveFraction:
                    Option.BoolValue = tmpbool;
                    break;
            }
        }

        public override string ToString()
        {
            return Key.ToString() + ": " + Option.OptionName;
        }
    }
}