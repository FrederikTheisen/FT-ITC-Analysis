using System;
using AnalysisITC.AppClasses.Analysis2;
using AppKit;
using CoreGraphics;
using PrintCore;
//using static AnalysisITC.Analysis;
using System.Collections.Generic;
using Foundation;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
    public class ParameterValueAdjustmentView : NSStackView
    {
        public Parameter Parameter { get; set; }

        double? tmpvalue;

        private NSTextField Label;
        private NSTextField Input;
        private NSColor DefaultFieldColor;
        private NSButton Lock;
        private CustomDrawingSegmentedControl ParameterOptionControl;

        public bool HasBeenAffectedFlag { get; private set; } = false;
        public bool ShouldReInitializeParameter => string.IsNullOrEmpty(InputString);
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
                        var value = double.Parse(input);

                        if (Key.GetProperties().ParentType == ParameterType.Affinity1)
                        {
                            if (AppSettings.InputAffinityAsDissociationConstant) return AppSettings.DefaultConcentrationUnit.GetProperties().Mod / value;
                            else return value;
                        }
                        else if (ParameterTypeAttribute.IsEnergyUnitParameter(Key)) return Energy.ConvertToJoule(value, AppSettings.EnergyUnit);
                        else return value;
                    }
                    catch (Exception ex)
                    {
                        AppEventHandler.DisplayHandledException(ex);
                    }

                return Parameter.Value;
            }
        }
        public bool Locked => Lock.State == NSCellStateValue.On;
        public bool EnableLock { get; set; } = true;

        public ParameterValueAdjustmentView(IntPtr handle) : base(handle)
        {
        }

        [Export("initWithFrame:")]
        public ParameterValueAdjustmentView(CGRect frameRect) : base(frameRect)
        {
            Frame = frameRect;
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            Distribution = NSStackViewDistribution.Fill;
            Alignment = NSLayoutAttribute.CenterY;
            //SetContentHuggingPriorityForOrientation(999, NSLayoutConstraintOrientation.Vertical);
            //SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Vertical);
            //SetHuggingPriority(1000, NSLayoutConstraintOrientation.Vertical);

            Label = new NSTextField(new CGRect(0, 0, 150, 16))
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

            ParameterOptionControl = new CustomDrawingSegmentedControl(new CGRect(0, 0, 0, 16))
            {
                Cell = new CustomKdKaDrawingSegmentedCell(),
                SegmentStyle = NSSegmentStyle.Rounded,
                ControlSize = NSControlSize.Small,
                SegmentDistribution = NSSegmentDistribution.FillEqually,
                SegmentCount = 2,
                Font = NSFont.SystemFontOfSize(9),
                Hidden = true,
            };
            ParameterOptionControl.SetImageScaling(NSImageScaling.ProportionallyDown, 0);
            ParameterOptionControl.SetImageScaling(NSImageScaling.ProportionallyDown, 1);
            ParameterOptionControl.Activated += ParameterOptionControl_Activated;
            ParameterOptionControl.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Vertical);
            ParameterOptionControl.SizeToFit();

            Input = new NSTextField(new CGRect(0, 0, 80, 18))
            {
                Bordered = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
                PlaceholderString = "Auto",
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
            Input.RefusesFirstResponder = true;


            AddArrangedSubview(Label);
            AddArrangedSubview(ParameterOptionControl);
            AddArrangedSubview(Input);

            SetupLockBtn();

            //SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);
            //SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Vertical);
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

            static NSImage MakeSymbolImage(string symbolName, nfloat size)
            {
                var img = NSImage.GetSystemSymbol(symbolName, null);
                var frame = new CGRect(0, 0, size, size);
                var output = new NSImage(frame.Size);

                output.LockFocus();
                output.Template = true;
                img.Draw(frame, new CGRect(CGPoint.Empty, img.Size), NSCompositingOperation.SourceOver, 1f);
                output.UnlockFocus();

                return output;
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
                tmpvalue = value;

                if (Value >= Parameter.Limits[0] && Value <= Parameter.Limits[1])
                    Input.TextColor = DefaultFieldColor;
            }
        }

        public void Setup(Parameter par)
        {
            this.Parameter = par;

            Lock.State = par.IsLocked ? NSCellStateValue.On : NSCellStateValue.Off;
            if (!EnableLock) Lock.Hidden = true;

            if (tmpvalue == null)
                tmpvalue = par.Value;

            if (par.Key.GetProperties().ParentType == ParameterType.Affinity1)
            {
                ParameterOptionControl.Hidden = false;
                ParameterOptionControl.SegmentCount = 2;
                ParameterOptionControl.SetLabel("  ", 0);
                ParameterOptionControl.SetLabel("  ", 1);
                ParameterOptionControl.SizeToFit();
                ParameterOptionControl.SelectedSegment = AppSettings.InputAffinityAsDissociationConstant ? 1 : 0;
            }

            SetupLabel();
            SetInputField();

            Layout();

            DefaultFieldColor = Label.TextColor;
        }

        void SetupLabel()
        {
            Label.StringValue = Parameter.Key.GetProperties().Description;

            if (Parameter.Key.GetProperties().ParentType == ParameterType.Affinity1 && AppSettings.InputAffinityAsDissociationConstant)
                Label.StringValue += " (" + AppSettings.DefaultConcentrationUnit.ToString() + ")";
            else if (ParameterTypeAttribute.IsEnergyUnitParameter(Parameter.Key))
                Label.StringValue += " (" + AppSettings.EnergyUnit.GetProperties().Unit + ")";
        }

        void SetInputField()
        {
            if (Parameter.Key.GetProperties().ParentType == ParameterType.Affinity1)
            {
                if (AppSettings.InputAffinityAsDissociationConstant)
                {
                    var number = (AppSettings.DefaultConcentrationUnit.GetProperties().Mod / (double)tmpvalue);
                    SetInputField(Convert.ToDouble(String.Format("{0:G3}", number)).ToString());
                }
                else SetInputField(((double)tmpvalue).ToString("G2"));
            }
            else if (ParameterTypeAttribute.IsEnergyUnitParameter(Parameter.Key)) SetInputField(new Energy((double)tmpvalue).ToString(AppSettings.EnergyUnit, "G3", withunit: false));
            else SetInputField(((double)tmpvalue).ToString("F3"));
        }

        void SetInputField(string s)
        {
            if (Parameter.ChangedByUser) Input.StringValue = s;
            else Input.PlaceholderString = s;
        }
    }
}

