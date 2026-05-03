using System;
using AppKit;
using CoreGraphics;
using System.Collections.Generic;
using AnalysisITC.Utilities;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
    public enum ParameterValueAdjustmentViewMode
    {
        Analysis,
        Designer
    }

    public class ParameterValueAdjustmentView : NSStackView
    {
        public Parameter Parameter { get; set; }

        double? tmpvalue;

        private NSTextField Label;
        private NSTextField Input;
        private NSColor DefaultFieldColor;
        private NSButton Lock;
        private NSSlider Slider;
        private bool IsSyncingControls;

        public event EventHandler ValueChanged;

        public bool HasBeenAffectedFlag { get; private set; } = false;
        public bool ShouldResetParameter => string.IsNullOrEmpty(InputString);
        public override CGSize IntrinsicContentSize => new CGSize(NSView.NoIntrinsicMetric, 16);

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
                            else if (ParameterTypeAttribute.IsEnergyUnitParameter(Key)) return Energy.ConvertToJoule(value, AppSettings.EnergyUnit);
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
        public ParameterValueAdjustmentViewMode Mode { get; private set; } = ParameterValueAdjustmentViewMode.Analysis;
        private bool ShowsLock => Mode == ParameterValueAdjustmentViewMode.Analysis;
        private bool ShowsSlider => Mode == ParameterValueAdjustmentViewMode.Designer;

        public ParameterValueAdjustmentView(IntPtr handle) : base(handle)
        {
        }

        public ParameterValueAdjustmentView(
            CGRect frameRect,
            Parameter par,
            ParameterValueAdjustmentViewMode mode = ParameterValueAdjustmentViewMode.Analysis) : base(frameRect)
        {
            Frame = frameRect;
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            Distribution = NSStackViewDistribution.Fill;
            Alignment = NSLayoutAttribute.CenterY;
            Parameter = par;
            Mode = mode;

            if (tmpvalue == null)
                tmpvalue = Parameter.Value;

            SetupLabel();
            if (ShowsSlider) SetupSlider();
            SetInputField();
            if (ShowsLock) SetupLockBtn();
            SyncSliderFromValue();
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

        private (double Min, double Max) GetDesignerSliderRange()
        {
            switch (Key.GetProperties().ParentType)
            {
                case ParameterType.Nvalue1: return (0.1, 10.0);
                case ParameterType.Affinity1: return (3.0, 9.0); // 1 mM to 1 nM Kd, stored as log10(Ka).
                case ParameterType.Offset: return (-30000.0, 30000.0);
                case ParameterType.Enthalpy1: return (-100000.0, 100000.0);
                default: return (Parameter.Limits[0], Parameter.Limits[1]);
            }
        }

        private double SliderToInternalValue(double sliderValue)
        {
            var range = GetDesignerSliderRange();
            var slider = Clamp(sliderValue, 0, 1);

            return range.Min + slider * (range.Max - range.Min);
        }

        private double InternalValueToSlider(double value)
        {
            var range = GetDesignerSliderRange();
            if (range.Max <= range.Min) return 0;

            return Clamp((value - range.Min) / (range.Max - range.Min), 0, 1);
        }

        private void SyncSliderFromValue()
        {
            if (Slider == null) return;

            IsSyncingControls = true;
            Slider.DoubleValue = InternalValueToSlider(Value);
            IsSyncingControls = false;
        }

        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

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

                    if (syrfactor) Label.StringValue = "Correction Factor";
                    else Label.StringValue = Parameter.Key.GetProperties().Description;
                    break;
                case ParameterType.Nvalue2: // If shared N value or if using syringe correction, disable second N-value field
                    bool disable = (attributes[AttributeKey.LockDuplicateParameter]?.BoolValue ?? false) || (attributes[AttributeKey.UseSyringeActiveFraction]?.BoolValue ?? false);
                    Input.Enabled = !disable;
                    if (Slider != null) Slider.Enabled = !disable;
                    if (Lock != null) Lock.Enabled = !disable;
                    Label.TextColor = disable ? NSColor.DisabledControlText : NSColor.Label;
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
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
            };

            // Set unit info in label
            if (Parameter.Key.GetProperties().ParentType == ParameterType.Affinity1 && AppSettings.InputAffinityAsDissociationConstant)
                Label.AttributedStringValue = MacStrings.FromMarkDownString($"{Label.StringValue} ({MarkdownStrings.DissociationConstant}, {AppSettings.DefaultConcentrationUnit})", Label.Font);
            else if (ParameterTypeAttribute.IsEnergyUnitParameter(Parameter.Key))
                Label.StringValue += " (" + AppSettings.EnergyUnit.GetProperties().Unit + "/mol)";

            if (ShowsSlider)
            {
                Label.AddConstraint(NSLayoutConstraint.Create(Label, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 145));
                Label.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Horizontal);
            }

            // Store the default color (not sure if relevant, why not just use NSColor.Label?
            DefaultFieldColor = Label.TextColor;

            // Add to stack
            AddArrangedSubview(Label);
        }

        void SetInputField()
        {
            // Create the input text field
            Input = new NSTextField(new CGRect(0, 0, 80, 18))
            {
                Bordered = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
                PlaceholderString = "auto",
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                FocusRingType = NSFocusRingType.None,
                Bezeled = true,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                Alignment = NSTextAlignment.Right,
                LineBreakMode = NSLineBreakMode.TruncatingHead,
            };
            Input.Changed += Input_Changed;
            Input.AddConstraint(NSLayoutConstraint.Create(Input, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 80));
            Input.AddConstraint(NSLayoutConstraint.Create(Input, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 19));

            SetInputField(FormatInternalValue((double)tmpvalue));

            // Add to stack
            AddArrangedSubview(Input);
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

            if (ParameterTypeAttribute.IsEnergyUnitParameter(Parameter.Key))
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
