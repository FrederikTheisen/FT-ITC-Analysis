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
        public static event EventHandler ParameterContraintUpdated;

        Parameter parameter { get; set; }

        double? tmpvalue;

        private NSTextField Label;
        private NSTextField Input;
        private NSColor DefaultFieldColor;
        private NSButton Lock;
        private CustomDrawingSegmentedControl ParameterOptionControl;

        public bool HasBeenAffectedFlag { get; private set; } = false;

        public override nfloat Spacing { get => 0; set => base.Spacing = value; }

        public ParameterTypes Key => parameter.Key;
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

                if (Input.StringValue.Length > 0) try
                    {
                        var value = double.Parse(input);

                        if (Key.GetProperties().ParentType == ParameterTypes.Affinity1)
                        {
                            if (AppSettings.InputAffinityAsDissociationConstant) return AppSettings.DefaultConcentrationUnit.GetProperties().Mod / value;
                            else return value;
                        }
                        else return value;
                    }
                    catch (Exception ex)
                    {
                        AppEventHandler.DisplayHandledException(ex);
                    }

                return parameter.Value;
            }
        }
        public bool Locked => Lock.State == NSCellStateValue.On;

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

            Label = new NSTextField(new CGRect(0,0,150,14))
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
                SegmentStyle = NSSegmentStyle.Capsule,
                ControlSize = NSControlSize.Small,
                SegmentDistribution = NSSegmentDistribution.Fit,
                SegmentCount = 2,
                Font = NSFont.SystemFontOfSize(10),
                Hidden = true,
            };
            ParameterOptionControl.Activated += ParameterOptionControl_Activated;
            ParameterOptionControl.SizeToFit();
            //ParameterOptionControl.SetWidth(0, 0);
            //ParameterOptionControl.SetWidth(0, 1);
            //ParameterOptionControl.AddConstraint(NSLayoutConstraint.Create(ParameterOptionControl, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 50));
            //ParameterOptionControl.Layout();

            Input = new NSTextField(new CGRect(0, 0, 100, 14))
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
            Input.Changed += Input_Changed;
            Input.AddConstraint(NSLayoutConstraint.Create(Input, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 60));
            Input.RefusesFirstResponder = true;

            Lock = new NSButton(new CGRect(0, 0, 23, 14))
            {
                BezelStyle = NSBezelStyle.Rounded,
                FocusRingType = NSFocusRingType.None,
                Bordered = false,
                Image = NSImage.GetSystemSymbol("lock.open.fill", null),
                AlternateImage = NSImage.GetSystemSymbol("lock.fill", null),
                ControlSize = NSControlSize.Small,
                ImageScaling = NSImageScale.ProportionallyDown,
                Title = "",
                AlternateTitle = "",
                ImagePosition = NSCellImagePosition.ImageRight,
            };
            Lock.SetButtonType(NSButtonType.Switch);
            Lock.Activated += Lock_Activated;
            Lock.ControlSize = NSControlSize.Small;
            Lock.ImageScaling = NSImageScale.ProportionallyDown;
            Lock.Cell.ImageScale = NSImageScale.ProportionallyDown;
            Lock.Cell.ControlSize = NSControlSize.Small;
            //Lock.ImagePosition = NSCellImagePosition.ImageOnly;
            Lock.AddConstraint(NSLayoutConstraint.Create(Lock, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 23));
            Lock.ImagePosition = NSCellImagePosition.ImageRight;
            Lock.Layout();
            
            AddArrangedSubview(Label);
            AddArrangedSubview(ParameterOptionControl);
            AddArrangedSubview(Input);
            AddArrangedSubview(Lock);
        }

        private void ParameterOptionControl_Activated(object sender, EventArgs e)
        {
            AppSettings.InputAffinityAsDissociationConstant = (sender as NSSegmentedControl).SelectedSegment == 1;

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
            //Input.TextColor = NSColor.SystemRed;
            Input.TextColor = NSColor.SystemRed;
            string input = InputString;

            if (string.IsNullOrEmpty(input))
                Input.TextColor = DefaultFieldColor;
            if (double.TryParse(input, out double value))
            {
                Console.WriteLine("Input field value changed: " + value.ToString());
                tmpvalue = value;

                if (Value >= parameter.Limits[0] && Value <= parameter.Limits[1])
                    Input.TextColor = DefaultFieldColor;
            }
        }

        public override CGSize IntrinsicContentSize => new CGSize(75, 14);

        public void Setup(Parameter par)
        {
            this.parameter = par;

            Label.StringValue = par.Key.GetProperties().Description;
            Lock.State = par.IsLocked ? NSCellStateValue.On : NSCellStateValue.Off;

            if (tmpvalue == null)
                tmpvalue = par.Value;

            if (par.Key.GetProperties().ParentType == ParameterTypes.Affinity1)
            {
                Label.StringValue += " (" + AppSettings.DefaultConcentrationUnit.ToString() + ")";

                ParameterOptionControl.Hidden = false;
                ParameterOptionControl.SegmentCount = 2;
                ParameterOptionControl.SetLabel("Ka", 0);
                ParameterOptionControl.SetLabel("Kd", 1);
                ParameterOptionControl.SizeToFit();
                ParameterOptionControl.SelectedSegment = AppSettings.InputAffinityAsDissociationConstant ? 1 : 0;
            }

            SetInputField();

            Layout();

            DefaultFieldColor = Label.TextColor;
        }

        void SetInputField()
        {
            if (parameter.Key.GetProperties().ParentType == ParameterTypes.Affinity1)
            {
                if (AppSettings.InputAffinityAsDissociationConstant) Input.PlaceholderString = (AppSettings.DefaultConcentrationUnit.GetProperties().Mod / (double)tmpvalue).ToString("####0.###");
                else Input.PlaceholderString = ((double)tmpvalue).ToString("G2");
            }
            else
            {
                Input.PlaceholderString = ((double)tmpvalue).ToString("G3");
            }
        }
    }
}

