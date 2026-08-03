using System;
using AppKit;
using CoreGraphics;
using System.Collections.Generic;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Core.Analysis;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

namespace AnalysisITC.UI.MacOS.CustomViews
{
    public class ParameterValueAdjustmentView : NSStackView, IDesignerAdjustmentView
    {
        public Parameter Parameter { get; set; }

        double? tmpvalue;

        private NSTextField Label;
        private NSTextField Input;
        private NSColor DefaultFieldColor;
        private NSButton Lock;
        private NSSlider Slider;
        private bool IsSyncingControls;
        private NSStackView HeaderRow;
        private NSStackView EditorRow;
        private NSTextField UnitLabel;
        private NSView DividerContainer;
        private bool ShowSiteIndex;
        private bool ShowsDivider;

        public event EventHandler ValueChanged;

        public bool HasBeenAffectedFlag { get; private set; } = false;
        public bool ShouldResetParameter => string.IsNullOrEmpty(InputString);
        public override CGSize IntrinsicContentSize =>
            new CGSize(NSView.NoIntrinsicMetric,
                Mode == AdjustmentViewMode.Analysis ? (ShowsDivider ? 60 : 52) : 16);

        public override nfloat Spacing { get => 1; set => base.Spacing = value; }

        public ParameterType Key => Parameter.Key;
        string InputString
        {
            get
            {
                string input = Input.StringValue;

                input = input.Replace(',', '.');
                input = input.Replace(" ", "");

                return input;
            }
        }
        public double Value
        {
            get
            {
                var input = InputString;

                if (Input.StringValue.Length > 0)
                    try
                    {
                        if (double.TryParse(input, out var value))
                        {
                            if (Key.GetProperties().ParentType == ParameterType.Affinity1)
                            {
                                if (AppSettings.InputAffinityAsDissociationConstant) return Math.Log10(AppSettings.DefaultConcentrationUnit.GetProperties().Mod / value);
                                else return value;
                            }
                            else if (UsesEnergyScale(Key)) return Energy.ConvertToJoule(value, AppSettings.EnergyUnit);
                            else return value;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppEventHandler.DisplayHandledException(ex);
                    }

                return Parameter.Value;
            }
        }
        public bool Locked => Lock?.State == NSCellStateValue.On;
        public bool HasValidInput
        {
            get
            {
                if (string.IsNullOrWhiteSpace(InputString)) return true;
                if (!double.TryParse(InputString, out _)) return false;
                var value = Value;
                return !double.IsNaN(value) && !double.IsInfinity(value) && IsWithinLimits(value);
            }
        }
        public AdjustmentViewMode Mode { get; private set; } = AdjustmentViewMode.Analysis;
        private bool ShowsLock => Mode == AdjustmentViewMode.Analysis;
        private bool ShowsSlider => Mode == AdjustmentViewMode.Designer;

        public ParameterValueAdjustmentView(IntPtr handle) : base(handle)
        {
        }

        public ParameterValueAdjustmentView(
            CGRect frameRect,
            Parameter par,
            AdjustmentViewMode mode = AdjustmentViewMode.Analysis,
            bool showSiteIndex = false,
            bool showsDivider = false) : base(frameRect)
        {
            Frame = frameRect;
            Parameter = par;
            Mode = mode;
            ShowSiteIndex = showSiteIndex;
            ShowsDivider = showsDivider;
            Orientation = Mode == AdjustmentViewMode.Analysis
                ? NSUserInterfaceLayoutOrientation.Vertical
                : NSUserInterfaceLayoutOrientation.Horizontal;
            Distribution = NSStackViewDistribution.Fill;
            Alignment = Mode == AdjustmentViewMode.Analysis
                ? NSLayoutAttribute.Width
                : NSLayoutAttribute.CenterY;
            Spacing = Mode == AdjustmentViewMode.Analysis ? 3 : 1;

            if (Mode == AdjustmentViewMode.Analysis) SetupAnalysisRows();

            if (tmpvalue == null)
                tmpvalue = Parameter.Value;

            SetupLabel();
            if (ShowsSlider) SetupSlider();
            SetInputField();
            if (ShowsLock) SetupLockBtn();
            if (Mode == AdjustmentViewMode.Analysis) SetupAnalysisUnitLabel();
            if (Mode == AdjustmentViewMode.Analysis && ShowsDivider) SetupDivider();
            SyncSliderFromValue();

            SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Vertical);
        }

        void SetupSlider()
        {
            Slider = new NSSlider(new CGRect(0, 0, 120, 16))
            {
                MinValue = 0,
                MaxValue = 1,
                DoubleValue = InternalValueToSlider((double)tmpvalue),
                Continuous = true,
                ControlSize = NSControlSize.Mini,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            Slider.AddConstraint(NSLayoutConstraint.Create(Slider, NSLayoutAttribute.Width, NSLayoutRelation.GreaterThanOrEqual, 1, 100));
            Slider.Activated += Slider_Activated;

            AddArrangedSubview(Slider);
        }

        void SetupLockBtn()
        {
            if (Mode == AdjustmentViewMode.Analysis)
            {
                Lock = new NSButton(new CGRect(0, 0, 24, 22))
                {
                    Title = "",
                    AlternateTitle = "",
                    ControlSize = NSControlSize.Regular,
                    BezelStyle = NSBezelStyle.Recessed,
                    FocusRingType = NSFocusRingType.None,
                    Image = ResizeTemplateSymbol(NSImage.GetSystemSymbol("lock.open.fill", null)),
                    AlternateImage = ResizeTemplateSymbol(NSImage.GetSystemSymbol("lock.fill", null)),
                    ImagePosition = NSCellImagePosition.ImageOnly,
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                Lock.SetButtonType(NSButtonType.Toggle);
                Lock.State = Parameter.IsLocked ? NSCellStateValue.On : NSCellStateValue.Off;
                UpdateLockToolTip();
                Lock.Activated += Lock_Activated;
                Lock.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Horizontal);
                Lock.AddConstraint(NSLayoutConstraint.Create(Lock, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 24));
                Lock.AddConstraint(NSLayoutConstraint.Create(Lock, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 22));
                HeaderRow.AddArrangedSubview(Lock);
                return;
            }

            var targetImage1 = NewMethod(NSImage.GetSystemSymbol("lock.open.fill", null));
            var targetImage2 = NewMethod(NSImage.GetSystemSymbol("lock.fill", null));

            Lock = new NSButton(new CGRect(0, 0, 13, 16))
            {
                BezelStyle = NSBezelStyle.Recessed,
                FocusRingType = NSFocusRingType.None,
                //Bordered = false,
                Image = targetImage1,
                AlternateImage = targetImage2,
                ControlSize = NSControlSize.Small,
                Title = "",
                AlternateTitle = "",
            };
            Lock.SetButtonType(NSButtonType.Toggle);
            Lock.Activated += Lock_Activated;
            Lock.ControlSize = NSControlSize.Small;
            Lock.AddConstraint(NSLayoutConstraint.Create(Lock, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 20));
            Lock.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Vertical);
            Lock.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);
            Lock.ImagePosition = NSCellImagePosition.ImageOnly;
            Lock.Layout();

            Lock.State = Parameter.IsLocked ? NSCellStateValue.On : NSCellStateValue.Off;

            AddArrangedSubview(Lock);

            static NSImage NewMethod(NSImage img)
            {
                var targetFrame = new CGRect(0, 0, 13, 13);
                var targetImage = new NSImage(targetFrame.Size);
                targetImage.LockFocus();
                targetImage.Template = true;
                img.Draw(targetFrame, new CGRect(CGPoint.Empty, img.Size), NSCompositingOperation.SourceOver, 1f);
                targetImage.UnlockFocus();

                return targetImage;
            }
        }

        private void ParameterOptionControl_Activated(object sender, EventArgs e)
        {
            if (Key.GetProperties().ParentType == ParameterType.Affinity1) AppSettings.InputAffinityAsDissociationConstant = (sender as NSSegmentedControl).SelectedSegment == 1;

            SetInputField();

            CheckInput();
        }

        private void Lock_Activated(object sender, EventArgs e)
        {
            HasBeenAffectedFlag = true;
            UpdateLockToolTip();
        }

        void UpdateLockToolTip()
        {
            if (Lock == null) return;
            Lock.ToolTip = Lock.State == NSCellStateValue.On ? "Unlock parameter" : "Lock parameter";
        }

        private void Input_Changed(object sender, EventArgs e)
        {
            HasBeenAffectedFlag = true;

            CheckInput();
            if (!IsSyncingControls) SyncSliderFromValue();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Slider_Activated(object sender, EventArgs e)
        {
            if (IsSyncingControls) return;

            IsSyncingControls = true;

            var value = SliderToInternalValue(Slider.DoubleValue);
            tmpvalue = value;
            SetInputField(FormatInternalValue(value), forceInput: true);
            Input.TextColor = IsWithinLimits(value) ? DefaultFieldColor : NSColor.SystemRed;

            IsSyncingControls = false;

            HasBeenAffectedFlag = true;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        void CheckInput()
        {
            Input.TextColor = NSColor.SystemRed;
            string input = InputString;

            if (string.IsNullOrEmpty(input))
                Input.TextColor = DefaultFieldColor;
            if (double.TryParse(input, out double value))
            {
                Console.WriteLine("Input field value changed: " + value.ToString());
                var internalValue = Value;
                tmpvalue = internalValue;

                if (IsWithinLimits(internalValue))
                    Input.TextColor = DefaultFieldColor;
            }
        }

        private bool IsWithinLimits(double value) => value >= Parameter.Limits[0] && value <= Parameter.Limits[1];

        private AdjustmentSliderRange GetDesignerSliderRange()
        {
            switch (Key.GetProperties().ParentType)
            {
                case ParameterType.Nvalue1: return new AdjustmentSliderRange(0.1, 10.0);
                case ParameterType.Affinity1: return new AdjustmentSliderRange(3.0, 9.0); // 1 mM to 1 nM Kd, stored as log10(Ka).
                case ParameterType.Offset: return new AdjustmentSliderRange(-30000.0, 30000.0);
                case ParameterType.Enthalpy1: return new AdjustmentSliderRange(-100000.0, 100000.0);
                default: return new AdjustmentSliderRange(Parameter.Limits[0], Parameter.Limits[1]);
            }
        }

        private double SliderToInternalValue(double sliderValue)
        {
            return AdjustmentSliderHelper.FromSliderValue(sliderValue, GetDesignerSliderRange());
        }

        private double InternalValueToSlider(double value)
        {
            return AdjustmentSliderHelper.ToSliderValue(value, GetDesignerSliderRange());
        }

        private void SyncSliderFromValue()
        {
            if (Slider == null) return;

            IsSyncingControls = true;
            Slider.DoubleValue = InternalValueToSlider(Value);
            IsSyncingControls = false;
        }

        /// <summary>
        /// Disable input parameters depending on the attribute state of the model.
        /// </summary>
        /// <param name="attributes"></param>
        public void UpdateState(IDictionary<AttributeKey, ExperimentAttribute> attributes)
        {
            switch (this.Parameter.Key)
            {
                case ParameterType.Nvalue1 when attributes.ContainsKey(AttributeKey.UseSyringeActiveFraction):
                    bool syrfactor = attributes[AttributeKey.UseSyringeActiveFraction]?.BoolValue ?? false;

                    UpdateAnalysisTitle(syrfactor);
                    break;
                case ParameterType.Nvalue2: // If shared N value or if using syringe correction, disable second N-value field
                    bool disable = (attributes[AttributeKey.LockDuplicateParameter]?.BoolValue ?? false) || (attributes[AttributeKey.UseSyringeActiveFraction]?.BoolValue ?? false);
                    Input.Enabled = !disable;
                    if (Slider != null) Slider.Enabled = !disable;
                    if (Lock != null) Lock.Enabled = !disable;
                    UpdateAnalysisTitle(false, !disable);
                    break;
            }
            //Layout();
        }

        void SetupLabel()
        {
            // Create the parameter name label
            Label = new NSTextField(new CGRect(0, 0, 150, 16))
            {
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                Bordered = false,
                Editable = false,
                StringValue = Parameter.Key.GetProperties().Description,
                TranslatesAutoresizingMaskIntoConstraints = false,
                HorizontalContentSizeConstraintActive = false,
                ControlSize = Mode == AdjustmentViewMode.Analysis ? NSControlSize.Regular : NSControlSize.Small,
                Font = Mode == AdjustmentViewMode.Analysis
                    ? NSFont.SystemFontOfSize(NSFont.SystemFontSize, NSFontWeight.Semibold)
                    : NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
            };

            // Set unit info in label
            if (Mode != AdjustmentViewMode.Analysis && Parameter.Key.GetProperties().ParentType == ParameterType.Affinity1 && AppSettings.InputAffinityAsDissociationConstant)
                Label.AttributedStringValue = AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString($"{Label.StringValue} ({MarkdownStrings.DissociationConstant}, {AppSettings.DefaultConcentrationUnit})", Label.Font);
            else if (Mode != AdjustmentViewMode.Analysis && ParameterTypeAttribute.IsEnergyUnitParameter(Parameter.Key))
                Label.StringValue += " (" + AppSettings.EnergyUnit.GetProperties().Unit + "/mol)";

            if (ShowsSlider)
            {
                Label.AddConstraint(NSLayoutConstraint.Create(Label, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 145));
                Label.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Horizontal);
            }

            // Store the default color (not sure if relevant, why not just use NSColor.Label?
            DefaultFieldColor = Label.TextColor;

            // Add to stack
            if (Mode == AdjustmentViewMode.Analysis)
            {
                Label.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);
                HeaderRow.AddArrangedSubview(Label);
                UpdateAnalysisTitle(false);
            }
            else
            {
                AddArrangedSubview(Label);
            }
        }

        void SetInputField()
        {
            // Create the input text field
            var inputWidth = Mode == AdjustmentViewMode.Analysis ? 132 : 80;
            var inputHeight = Mode == AdjustmentViewMode.Analysis ? 22 : 19;
            Input = new NSTextField(new CGRect(0, 0, inputWidth, inputHeight))
            {
                Bordered = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
                PlaceholderString = "auto",
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                FocusRingType = NSFocusRingType.None,
                Bezeled = true,
                ControlSize = Mode == AdjustmentViewMode.Analysis ? NSControlSize.Regular : NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(Mode == AdjustmentViewMode.Analysis ? NSFont.SystemFontSize : NSFont.SmallSystemFontSize),
                Alignment = NSTextAlignment.Right,
                LineBreakMode = NSLineBreakMode.TruncatingHead,
            };
            Input.Changed += Input_Changed;
            Input.AddConstraint(NSLayoutConstraint.Create(Input, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, inputWidth));
            Input.AddConstraint(NSLayoutConstraint.Create(Input, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, inputHeight));

            SetInputField(FormatInternalValue((double)tmpvalue));

            // Add to stack
            if (Mode == AdjustmentViewMode.Analysis) EditorRow.AddArrangedSubview(Input);
            else AddArrangedSubview(Input);
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

            AddArrangedSubview(HeaderRow);
            AddArrangedSubview(EditorRow);
        }

        void SetupAnalysisUnitLabel()
        {
            string unit = null;
            if (Parameter.Key.GetProperties().ParentType == ParameterType.Affinity1 && AppSettings.InputAffinityAsDissociationConstant)
                unit = AppSettings.DefaultConcentrationUnit.GetProperties().Name;
            else if (Parameter.Key.GetProperties().ParentType == ParameterType.HeatCapacity1
                || Parameter.Key.GetProperties().ParentType == ParameterType.Entropy1)
                unit = AppSettings.EnergyUnit.GetProperties().Unit + "/mol/K";
            else if (UsesEnergyScale(Parameter.Key))
                unit = AppSettings.EnergyUnit.GetProperties().Unit + "/mol";
            else if (Parameter.Key == ParameterType.IsomerizationRate)
                unit = "s⁻¹";
            else if (Parameter.Key == ParameterType.CisIsomerPopulationPercentage)
                unit = "%";

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
            DividerContainer = new NSView
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            DividerContainer.AddConstraint(NSLayoutConstraint.Create(
                DividerContainer, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 5));

            var divider = new NSBox
            {
                BoxType = NSBoxType.NSBoxSeparator,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            DividerContainer.AddSubview(divider);
            DividerContainer.AddConstraints(new[]
            {
                NSLayoutConstraint.Create(divider, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, DividerContainer, NSLayoutAttribute.Leading, 1, 8),
                NSLayoutConstraint.Create(divider, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, DividerContainer, NSLayoutAttribute.Trailing, 1, -8),
                NSLayoutConstraint.Create(divider, NSLayoutAttribute.CenterY, NSLayoutRelation.Equal, DividerContainer, NSLayoutAttribute.CenterY, 1, 0),
            });
            AddArrangedSubview(DividerContainer);
        }

        void UpdateAnalysisTitle(bool correctionFactor, bool enabled = true)
        {
            if (Label == null || Mode != AdjustmentViewMode.Analysis) return;

            var title = correctionFactor ? "Correction Factor" : Parameter.Key.GetProperties().Description;
            var symbol = AnalysisITC.UI.MacOS.MacStrings.ParameterSymbol(
                Parameter.Key, ShowSiteIndex, correctionFactor);
            Label.AttributedStringValue = AnalysisITC.UI.MacOS.MacStrings.AnalysisItemTitle(
                title, symbol, (float)NSFont.SystemFontSize, enabled);
        }

        static bool UsesEnergyScale(ParameterType key) =>
            ParameterTypeAttribute.IsEnergyUnitParameter(key)
            || key.GetProperties().ParentType == ParameterType.Entropy1;

        static NSImage ResizeTemplateSymbol(NSImage image)
        {
            if (image == null) return null;

            var targetFrame = new CGRect(0, 0, 13, 13);
            var targetImage = new NSImage(targetFrame.Size) { Template = true };
            targetImage.LockFocus();
            image.Draw(targetFrame, new CGRect(CGPoint.Empty, image.Size),
                NSCompositingOperation.SourceOver, 1f);
            targetImage.UnlockFocus();
            return targetImage;
        }

        public void FocusInput()
        {
            Input?.Window?.MakeFirstResponder(Input);
        }

        private string FormatInternalValue(double value)
        {
            if (Parameter.Key.GetProperties().ParentType == ParameterType.Affinity1)
            {
                if (AppSettings.InputAffinityAsDissociationConstant)
                {
                    var number = AppSettings.DefaultConcentrationUnit.GetProperties().Mod / Math.Pow(10, value);
                    return Convert.ToDouble(String.Format("{0:G3}", number)).ToString();
                }
                return value.ToString("G2");
            }

            if (UsesEnergyScale(Parameter.Key))
                return new Energy(value).ToString(AppSettings.EnergyUnit, "G3", withunit: false);

            return value.ToString("F3");
        }

        void SetInputField(string s, bool forceInput = false)
        {
            if (forceInput || Parameter.ChangedByUser) Input.StringValue = s;
            else Input.PlaceholderString = s;
        }
    }
}
