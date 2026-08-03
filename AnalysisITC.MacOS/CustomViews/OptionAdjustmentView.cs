using System;
using System.Collections.Generic;
using AnalysisITC.Core.Analysis;
using AppKit;
using CoreGraphics;
using Foundation;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.UI.MacOS.CustomViews
{
    public class OptionAdjustmentView : NSStackView, IDesignerAdjustmentView
    {
        public static event EventHandler RefreshLists;

        public ExperimentAttribute Option { get; private set; }
        public AttributeKey Key => Option.Key;

        bool tmpbool;

        private NSTextField Label;
        private NSButton InputButton;
        private NSButton InputSwitch;
        private NSTextField InputField;
        private ValueWithErrorTextField InputValueWithErrorField;
        private NSSlider Slider;
        private bool IsSyncingControls;
        private NSStackView HeaderRow;
        private NSStackView EditorRow;
        private NSStackView AuxiliaryRow;
        private NSTextField UnitLabel;
        private bool ShowsDivider;
        private bool IsCompactAnalysis;
        public NSPopUpButton StoichiometryPopup { get; set; }

        public event EventHandler ValueChanged;

        public bool HasBeenAffectedFlag { get; private set; } = false;
        public bool HasValidInput => InputValueWithErrorField == null || InputValueWithErrorField.HasValidInput;

        public override CGSize IntrinsicContentSize =>
            new CGSize(NSView.NoIntrinsicMetric,
                Mode == AdjustmentViewMode.Analysis
                    ? (IsCompactAnalysis ? (ShowsDivider ? 34 : 26) : (ShowsDivider ? 60 : 52))
                    : 16);
        public override nfloat Spacing { get => base.Spacing; set => base.Spacing = value; }
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
            AdjustmentViewMode mode = AdjustmentViewMode.Analysis,
            bool showsDivider = false) : base(frameRect)
        {
            Frame = frameRect;
            Option = option;
            Mode = mode;
            ShowsDivider = showsDivider;
            IsCompactAnalysis = Mode == AdjustmentViewMode.Analysis
                && Option.Key.GetProperties().Type == ExperimentAttribute.AttributeType.Bool;
            Orientation = Mode == AdjustmentViewMode.Analysis
                ? NSUserInterfaceLayoutOrientation.Vertical
                : NSUserInterfaceLayoutOrientation.Horizontal;
            Distribution = NSStackViewDistribution.Fill;
            Alignment = Mode == AdjustmentViewMode.Analysis
                ? NSLayoutAttribute.Width
                : NSLayoutAttribute.CenterY;
            Spacing = Mode == AdjustmentViewMode.Analysis ? 3 : 1;

            if (Mode == AdjustmentViewMode.Analysis) SetupAnalysisRows();

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

            if (Mode == AdjustmentViewMode.Analysis && ShowsDivider) SetupDivider();

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
                    UpdateAnalysisLabelEnabled(enable);
                    StoichiometryPopup.Enabled = enable;
                    break;
                case AttributeKey.NumberOfSites2:
                    enable = (attributes[AttributeKey.UseSyringeActiveFraction]?.BoolValue ?? false) && (!attributes[AttributeKey.LockDuplicateParameter]?.BoolValue ?? true);
                    UpdateAnalysisLabelEnabled(enable);
                    StoichiometryPopup.Enabled = enable;
                    break;
            }
        }

        void SetupLabel()
        {
            var font = Mode == AdjustmentViewMode.Analysis
                ? NSFont.SystemFontOfSize(NSFont.SystemFontSize, NSFontWeight.Semibold)
                : NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
            Label = new NSTextField(new CGRect(0, 0, 200, 16))
            {
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                Bordered = false,
                Editable = false,
                AttributedStringValue = Mode == AdjustmentViewMode.Analysis
                    ? AnalysisITC.UI.MacOS.MacStrings.AnalysisItemTitle(Option.GetDisplayName(), null, (float)NSFont.SystemFontSize)
                    : AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(Option.GetDisplayName(), font),
                //StringValue = Option.OptionName,
                ToolTip = Option.Key.GetProperties().ToolTip,
                TranslatesAutoresizingMaskIntoConstraints = false,
                HorizontalContentSizeConstraintActive = false,
                ControlSize = Mode == AdjustmentViewMode.Analysis ? NSControlSize.Regular : NSControlSize.Small,
                Font = font,
            };

            AddLabelSubview(Label);

            if (Mode == AdjustmentViewMode.Analysis)
                Label.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            if (ShowsSlider)
            {
                Label.AddConstraint(NSLayoutConstraint.Create(Label, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 145));
                Label.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Horizontal);
            }
        }

        void SetupBoolOption()
        {
            InputSwitch = new NSButton
            {
                Title = Option.GetDisplayName(),
                State = Option.BoolValue ? NSCellStateValue.On : NSCellStateValue.Off,
                ControlSize = Mode == AdjustmentViewMode.Analysis ? NSControlSize.Regular : NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(
                    Mode == AdjustmentViewMode.Analysis ? NSFont.SystemFontSize : NSFont.SmallSystemFontSize),
                ToolTip = Option.Key.GetProperties().ToolTip,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            InputSwitch.SetButtonType(NSButtonType.Switch);
            InputSwitch.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            InputSwitch.Activated += (s, e) => Method();

            AddLabelSubview(InputSwitch);
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

            AddEditorSubview(InputField);
        }

        void SetupParameterOptionLabel()
        {
            var font = NSFont.SystemFontOfSize(
                Mode == AdjustmentViewMode.Analysis ? NSFont.SystemFontSize : NSFont.SmallSystemFontSize,
                Mode == AdjustmentViewMode.Analysis ? NSFontWeight.Semibold : NSFontWeight.Regular);

            if (Mode == AdjustmentViewMode.Analysis) return;

            if (Option.Key == AttributeKey.PreboundLigandAffinity)
            {
                Label.AttributedStringValue = AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(
                    Option.OptionName + $" ({MarkdownStrings.DissociationConstant}, {AppSettings.DefaultConcentrationUnit})",
                    font);
            }
            else if (Option.Key == AttributeKey.PreboundLigandConc)
            {
                Label.AttributedStringValue = AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(
                    Option.OptionName + $" ({AppSettings.DefaultConcentrationUnit})",
                    font);
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
                    InputButton.SetButtonType(Mode == AdjustmentViewMode.Analysis
                        ? NSButtonType.Switch
                        : NSButtonType.PushOnPushOff);
                    InputButton.Title = "From attributes";
                    InputButton.ToolTip = $"Enable to retrieve value from experiment attributes";
                    InputButton.State = Option.BoolValue ? NSCellStateValue.On : NSCellStateValue.Off;
                    if (Mode != AdjustmentViewMode.Analysis)
                        InputButton.BezelStyle = NSBezelStyle.Recessed;
                    InputButton.ControlSize = Mode == AdjustmentViewMode.Analysis ? NSControlSize.Regular : NSControlSize.Mini;
                    InputButton.Font = NSFont.SystemFontOfSize(Mode == AdjustmentViewMode.Analysis ? NSFont.SystemFontSize : NSFont.SmallSystemFontSize);
                    InputButton.Activated += InputButton_Activated;
                    InputButton.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

                    if (Mode == AdjustmentViewMode.Analysis)
                        AuxiliaryRow.AddArrangedSubview(InputButton);
                    else
                        AddEditorSubview(InputButton);
                    break;
            }

            if (ShowsSlider) SetupSlider(value.Value);

            var inputWidth = Mode == AdjustmentViewMode.Analysis ? 132 : 80;
            var inputHeight = Mode == AdjustmentViewMode.Analysis ? 22 : 19;
            InputValueWithErrorField = new ValueWithErrorTextField(new CGRect(0, 0, inputWidth, inputHeight))
            {
                ToolTip = "Value and optional uncertainty. Press space to enter uncertainty. " + Option.Key.GetProperties().ToolTip,
                ControlSize = Mode == AdjustmentViewMode.Analysis ? NSControlSize.Regular : NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(Mode == AdjustmentViewMode.Analysis ? NSFont.SystemFontSize : NSFont.SmallSystemFontSize),
            };

            InputValueWithErrorField.SetValue(value.Value, value.SD);
            InputValueWithErrorField.Changed += ParameterInputChanged;
            InputValueWithErrorField.AddConstraint(NSLayoutConstraint.Create(InputValueWithErrorField, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, inputWidth));
            InputValueWithErrorField.AddConstraint(NSLayoutConstraint.Create(InputValueWithErrorField, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, inputHeight));

            AddEditorSubview(InputValueWithErrorField);
            SetupAnalysisUnitLabel();

            if (InputButton != null)
                InputValueWithErrorField.Enabled = InputButton.State != NSCellStateValue.On;

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

            AddEditorSubview(Slider);
        }

        void SetupStoichiometryOption()
        {
            StoichiometryPopup = new NSPopUpButton(CGRect.Empty, pullsDown: false)
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                Font = NSFont.SystemFontOfSize(Mode == AdjustmentViewMode.Analysis ? NSFont.SystemFontSize : NSFont.SmallSystemFontSize),
                BezelStyle = NSBezelStyle.Recessed,
                ControlSize = Mode == AdjustmentViewMode.Analysis ? NSControlSize.Regular : NSControlSize.Small,
                ToolTip = Option.Key.GetProperties().ToolTip
            };

            StoichiometryPopupBuilder.Populate(StoichiometryPopup);
            StoichiometryPopupBuilder.Select(StoichiometryPopup, Option.DoubleValue);

            StoichiometryPopup.Activated += StoichiometryPopup_Activated;
            StoichiometryPopup.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            AddEditorSubview(StoichiometryPopup);
        }

        void SetupAnalysisRows()
        {
            HeaderRow = new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Distribution = NSStackViewDistribution.Fill,
                Alignment = NSLayoutAttribute.CenterY,
                Spacing = 6,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            EditorRow = new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Distribution = NSStackViewDistribution.Fill,
                Alignment = NSLayoutAttribute.CenterY,
                Spacing = 6,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            AuxiliaryRow = new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Distribution = NSStackViewDistribution.Fill,
                Alignment = NSLayoutAttribute.CenterY,
                Spacing = 6,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };

            AddArrangedSubview(HeaderRow);
            AddArrangedSubview(AuxiliaryRow);
            AddArrangedSubview(EditorRow);
        }

        void SetupAnalysisUnitLabel()
        {
            if (Mode != AdjustmentViewMode.Analysis) return;

            string unit = null;
            switch (Option.Key)
            {
                case AttributeKey.PreboundLigandConc:
                case AttributeKey.PreboundLigandAffinity:
                    unit = AppSettings.DefaultConcentrationUnit.GetProperties().Name;
                    break;
                case AttributeKey.PreboundLigandEnthalpy:
                    unit = AppSettings.EnergyUnit.GetProperties().Unit + "/mol";
                    break;
                case AttributeKey.Percentage:
                    unit = "%";
                    break;
            }

            UnitLabel = new NSTextField
            {
                StringValue = unit ?? string.Empty,
                Bordered = false,
                Editable = false,
                DrawsBackground = false,
                TextColor = NSColor.SecondaryLabel,
                Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                ControlSize = NSControlSize.Regular,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            UnitLabel.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);
            EditorRow.AddArrangedSubview(UnitLabel);
        }

        void SetupDivider()
        {
            var container = new NSView
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            container.AddConstraint(NSLayoutConstraint.Create(
                container, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 5));

            var divider = new NSBox
            {
                BoxType = NSBoxType.NSBoxSeparator,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            container.AddSubview(divider);
            container.AddConstraints(new[]
            {
                NSLayoutConstraint.Create(divider, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, container, NSLayoutAttribute.Leading, 1, 8),
                NSLayoutConstraint.Create(divider, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, container, NSLayoutAttribute.Trailing, 1, -8),
                NSLayoutConstraint.Create(divider, NSLayoutAttribute.CenterY, NSLayoutRelation.Equal, container, NSLayoutAttribute.CenterY, 1, 0),
            });
            AddArrangedSubview(container);
        }

        void UpdateAnalysisLabelEnabled(bool enabled)
        {
            if (Label == null || Mode != AdjustmentViewMode.Analysis) return;
            Label.AttributedStringValue = AnalysisITC.UI.MacOS.MacStrings.AnalysisItemTitle(
                Option.GetDisplayName(), null, (float)NSFont.SystemFontSize, enabled);
        }

        void AddLabelSubview(NSView view)
        {
            if (Mode == AdjustmentViewMode.Analysis) HeaderRow.AddArrangedSubview(view);
            else AddArrangedSubview(view);
        }

        void AddEditorSubview(NSView view)
        {
            if (Mode == AdjustmentViewMode.Analysis) EditorRow.AddArrangedSubview(view);
            else AddArrangedSubview(view);
        }

        void Method()
        {
            Console.WriteLine($"InputButtonActivated [{Option.OptionName}]: " + InputSwitch.State.ToString());

            Option.BoolValue = InputSwitch.State == NSCellStateValue.On;
            HasBeenAffectedFlag = true;

            RefreshLists?.Invoke(this, null);
            RaiseValueChanged();
        }

        void InputButton_Activated(object sender, EventArgs e)
        {
            Option.BoolValue = InputButton.State == NSCellStateValue.On;
            if (InputValueWithErrorField != null)
                InputValueWithErrorField.Enabled = !Option.BoolValue;
            HasBeenAffectedFlag = true;
            RefreshLists?.Invoke(this, null);
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
                Option.BoolValue = InputSwitch.State == NSCellStateValue.On;
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

        public void FocusInput()
        {
            InputValueWithErrorField?.Window?.MakeFirstResponder(InputValueWithErrorField);
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
