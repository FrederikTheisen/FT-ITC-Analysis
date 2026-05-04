using System;
using System.Collections.Generic;
using AnalysisITC.AppClasses.AnalysisClasses;
using AppKit;
using CoreGraphics;
using Foundation;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
    public class OptionAdjustmentView : NSStackView, IDesignerAdjustmentView
    {
        public static event EventHandler RefreshLists;

        public ExperimentAttribute Option { get; private set; }
        public AttributeKey Key => Option.Key;

        bool tmpbool;

        private NSTextField Label;
        private NSButton InputButton;
        private NSSwitch InputSwitch;
        private NSTextField InputField;
        private ValueWithErrorTextField InputValueWithErrorField;
        private NSSlider Slider;
        private bool IsSyncingControls;
        public NSPopUpButton StoichiometryPopup { get; set; }

        public event EventHandler ValueChanged;

        public bool HasBeenAffectedFlag { get; private set; } = false;

        public override CGSize IntrinsicContentSize => new CGSize(NSView.NoIntrinsicMetric, 16);
        public override nfloat Spacing { get => 1; set => base.Spacing = value; }
        private NSColor DefaultFieldColor;
        public AdjustmentViewMode Mode { get; private set; } = AdjustmentViewMode.Analysis;
        private bool ShowsSlider => Mode == AdjustmentViewMode.Designer && SupportsSlider;
        private bool SupportsSlider
        {
            get
            {
                switch (Key)
                {
                    case AttributeKey.Percentage:
                    case AttributeKey.EquilibriumConstant:
                    case AttributeKey.PreboundLigandConc:
                    case AttributeKey.PreboundLigandAffinity:
                    case AttributeKey.PreboundLigandEnthalpy:
                        return true;
                    default:
                        return false;
                }
            }
        }

        public void SetupDesignerLayout()
        {
            switch (Key)
            {
                case AttributeKey.PreboundLigandConc:
                    if (InputButton != null)
                    {
                        InputButton.Hidden = true;
                        InputButton.State = NSCellStateValue.Off;
                        Option.BoolValue = false;
                    }
                    break;
            }
        }

        public OptionAdjustmentView(IntPtr handle) : base(handle)
        {
        }

        public OptionAdjustmentView(
            CGRect frameRect,
            ExperimentAttribute option,
            AdjustmentViewMode mode = AdjustmentViewMode.Analysis) : base(frameRect)
        {
            Frame = frameRect;
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            Distribution = NSStackViewDistribution.Fill;
            Alignment = NSLayoutAttribute.CenterY;
            Option = option;
            Mode = mode;

            tmpbool = Option.BoolValue;

            switch (Option.Key)
            {
                case AttributeKey.NumberOfSites2:
                case AttributeKey.NumberOfSites1:
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
            bool enable;

            switch (Option.Key)
            {
                case AttributeKey.NumberOfSites1:
                    enable = attributes[AttributeKey.UseSyringeActiveFraction]?.BoolValue ?? false;
                    Label.TextColor = enable ? NSColor.Label : NSColor.DisabledControlText;
                    StoichiometryPopup.Enabled = enable;
                    break;
                case AttributeKey.NumberOfSites2:
                    enable = (attributes[AttributeKey.UseSyringeActiveFraction]?.BoolValue ?? false) && (!attributes[AttributeKey.LockDuplicateParameter]?.BoolValue ?? true);
                    Label.TextColor = enable ? NSColor.Label : NSColor.DisabledControlText;
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
                ToolTip = Option.Key.GetProperties().ToolTip,
                TranslatesAutoresizingMaskIntoConstraints = false,
                HorizontalContentSizeConstraintActive = false,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
            };

            AddArrangedSubview(Label);

            if (ShowsSlider)
            {
                Label.AddConstraint(NSLayoutConstraint.Create(Label, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 145));
                Label.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Horizontal);
            }
        }

        void SetupBoolOption()
        {
            Label = new NSTextField
            {
                StringValue = Option.OptionName,
                Editable = false,
                Bezeled = false,
                DrawsBackground = false,
                Selectable = false,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                ToolTip = Option.Key.GetProperties().ToolTip
            };

            Label.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            InputSwitch = new NSSwitch
            {
                State = Option.BoolValue ? (int)NSCellStateValue.On : (int)NSCellStateValue.Off,
                ControlSize = NSControlSize.Mini,
                ToolTip = Option.Key.GetProperties().ToolTip,
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
            InputField.Formatter = new NSNumberFormatter()
            {
                NumberStyle = NSNumberFormatterStyle.None,
                MaximumFractionDigits = 0,
                RoundingIncrement = 1
            };
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
                    value = 1.0 / FWEMath.Pow(10.0, value);
                    value *= AppSettings.DefaultConcentrationUnit.GetProperties().Mod;
                    break;
                case AttributeKey.PreboundLigandConc:
                    value *= AppSettings.DefaultConcentrationUnit.GetProperties().Mod;
                    break;
                case AttributeKey.PreboundLigandEnthalpy:
                    value = new Energy(value).ToUnit(AppSettings.EnergyUnit).FloatWithError;
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
                    InputButton.Activated += InputButton_Activated;

                    AddArrangedSubview(InputButton);
                    break;
            }

            if (ShowsSlider) SetupSlider(value.Value);

            InputValueWithErrorField = new ValueWithErrorTextField(new CGRect(0, 0, 80, 19))
            {
                ToolTip = "Value and optional uncertainty. Press space to enter uncertainty. " + Option.Key.GetProperties().ToolTip,
            };

            InputValueWithErrorField.SetValue(value.Value, value.SD);
            InputValueWithErrorField.Changed += ParameterInputChanged;
            InputValueWithErrorField.AddConstraint(NSLayoutConstraint.Create(InputValueWithErrorField, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 80));
            InputValueWithErrorField.AddConstraint(NSLayoutConstraint.Create(InputValueWithErrorField, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 19));

            AddArrangedSubview(InputValueWithErrorField);

            DefaultFieldColor = InputValueWithErrorField.TextColor;

            return;
        }

        void SetupSlider(double displayValue)
        {
            Slider = new NSSlider(new CGRect(0, 0, 120, 16))
            {
                MinValue = 0,
                MaxValue = 1,
                DoubleValue = DisplayValueToSlider(displayValue),
                Continuous = true,
                ControlSize = NSControlSize.Mini,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            Slider.AddConstraint(NSLayoutConstraint.Create(Slider, NSLayoutAttribute.Width, NSLayoutRelation.GreaterThanOrEqual, 1, 100));
            Slider.Activated += Slider_Activated;

            AddArrangedSubview(Slider);
        }

        void SetupStoichiometryOption()
        {
            StoichiometryPopup = new NSPopUpButton(CGRect.Empty, pullsDown: false)
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                BezelStyle = NSBezelStyle.Recessed,
                ControlSize = NSControlSize.Small,
                ToolTip = Option.Key.GetProperties().ToolTip
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
            HasBeenAffectedFlag = true;

            RefreshLists?.Invoke(this, null);
            RaiseValueChanged();
        }

        void InputButton_Activated(object sender, EventArgs e)
        {
            HasBeenAffectedFlag = true;
            RaiseValueChanged();
        }

        void ParameterInputChanged(object sender, EventArgs e)
        {
            HasBeenAffectedFlag = true;

            CheckParameterInput();
            if (!IsSyncingControls) SyncSliderFromValue();
            RaiseValueChanged();
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
            RaiseValueChanged();
        }

        void StoichiometryPopup_Activated(object sender, EventArgs e)
        {
            var selected = StoichiometryPopupBuilder.GetSelected(StoichiometryPopup);

            Console.WriteLine("Stoichiometry = " + selected);
            Option.DoubleValue = selected.Factor;
            HasBeenAffectedFlag = true;

            // Optional if you want to show a label somewhere else:
            // StoichiometryInfoLabel.StringValue = selected.Title;

            // Optional if your parameter list needs rebuilding after mode changes:
            // ReloadParameterList();
            RaiseValueChanged();
        }

        void Slider_Activated(object sender, EventArgs e)
        {
            if (IsSyncingControls || InputValueWithErrorField == null) return;

            IsSyncingControls = true;

            var value = SliderToDisplayValue(Slider.DoubleValue);
            InputValueWithErrorField.SetValue(value, InputValueWithErrorField.DoubleErrorPart);
            CheckParameterInput();

            IsSyncingControls = false;

            HasBeenAffectedFlag = true;
            RaiseValueChanged();
        }

        void SyncSliderFromValue()
        {
            if (Slider == null || InputValueWithErrorField == null) return;
            if (!InputValueWithErrorField.TryGetValue(out double value, out _)) return;

            IsSyncingControls = true;
            Slider.DoubleValue = DisplayValueToSlider(value);
            IsSyncingControls = false;
        }

        private AdjustmentSliderRange GetSliderRange()
        {
            switch (Key)
            {
                case AttributeKey.Percentage:
                    return new AdjustmentSliderRange(0.0, 1.0);
                case AttributeKey.EquilibriumConstant:
                    return new AdjustmentSliderRange(-6.0, 5.0);
                case AttributeKey.PreboundLigandConc:
                    return new AdjustmentSliderRange(0.0, 0.001);
                case AttributeKey.PreboundLigandAffinity:
                    return new AdjustmentSliderRange(3.0, 9.0);
                case AttributeKey.PreboundLigandEnthalpy:
                    return new AdjustmentSliderRange(-100000.0, 100000.0);
                default:
                    return new AdjustmentSliderRange(0.0, 1.0);
            }
        }

        private double SliderToDisplayValue(double sliderValue)
        {
            var value = AdjustmentSliderHelper.FromSliderValue(sliderValue, GetSliderRange());

            switch (Key)
            {
                case AttributeKey.Percentage:
                    return value * 100.0;
                case AttributeKey.EquilibriumConstant:
                    return Math.Pow(10.0, value);
                case AttributeKey.PreboundLigandConc:
                    return value * AppSettings.DefaultConcentrationUnit.GetProperties().Mod;
                case AttributeKey.PreboundLigandAffinity:
                    return AppSettings.DefaultConcentrationUnit.GetProperties().Mod / Math.Pow(10.0, value);
                case AttributeKey.PreboundLigandEnthalpy:
                    return Energy.ConvertFromJoule(value, AppSettings.EnergyUnit);
                default:
                    return value;
            }
        }

        private double DisplayValueToSlider(double displayValue)
        {
            var value = DisplayValueToSliderDomain(displayValue);
            if (double.IsNaN(value) || double.IsInfinity(value))
                value = GetSliderRange().Min;

            return AdjustmentSliderHelper.ToSliderValue(value, GetSliderRange());
        }

        private double DisplayValueToSliderDomain(double displayValue)
        {
            switch (Key)
            {
                case AttributeKey.Percentage:
                    return displayValue / 100.0;
                case AttributeKey.EquilibriumConstant:
                    return Math.Log10(Math.Max(displayValue, 0.000001));
                case AttributeKey.PreboundLigandConc:
                    return displayValue / AppSettings.DefaultConcentrationUnit.GetProperties().Mod;
                case AttributeKey.PreboundLigandAffinity:
                    return Math.Log10(AppSettings.DefaultConcentrationUnit.GetProperties().Mod / Math.Max(displayValue, double.Epsilon));
                case AttributeKey.PreboundLigandEnthalpy:
                    return Energy.ConvertToJoule(displayValue, AppSettings.EnergyUnit);
                default:
                    return displayValue;
            }
        }

        private void RaiseValueChanged()
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ApplyOptions()
        {
            if (InputButton != null)
            {
                Option.BoolValue = InputButton.State == NSCellStateValue.On;
            }
            else if (InputSwitch != null)
            {
                Option.BoolValue = InputSwitch.State == (int)NSCellStateValue.On;
            }

            if (StoichiometryPopup != null)
            {
                switch (Option.Key)
                {
                    case AttributeKey.NumberOfSites2:
                    case AttributeKey.NumberOfSites1:
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
                            var unitMod = AppSettings.DefaultConcentrationUnit.GetProperties().Mod;
                            val /= unitMod;
                            err /= unitMod;

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

                            Option.ParameterValue = FWEMath.Log10(value);
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
            // ModelFactory.StorePreviousAttribute(Option);
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
