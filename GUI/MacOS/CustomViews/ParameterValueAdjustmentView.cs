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

        private NSTextField Label;
        private HuggingTextView Input;
        private NSColor DefaultFieldColor;
        private NSButton Lock;

        public bool HasBeenAffectedFlag { get; private set; } = false;
        
        public ParameterTypes Key => parameter.Key;
        public string InputString
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

                if (Input.StringValue.Length > 0) return double.Parse(input);
                else return parameter.Value;
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

            Input = new HuggingTextView(new CGRect(0, 0, 100, 14))
            {
                Bordered = false,
                TranslatesAutoresizingMaskIntoConstraints = false,
                PlaceholderString = "Auto",
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                FocusRingType = NSFocusRingType.None,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                Alignment = NSTextAlignment.Right,
            };
            Input.Changed += Input_Changed;
            Input.AddConstraint(NSLayoutConstraint.Create(Input, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 54));
            Input.RefusesFirstResponder = true;
            DefaultFieldColor = Input.TextColor;

            Lock = new NSButton(new CGRect(0, 0, 14.5, 14))
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
                ImagePosition = NSCellImagePosition.ImageOnly,
            };
            Lock.SetButtonType(NSButtonType.Switch);
            Lock.Activated += Lock_Activated;
            Lock.ControlSize = NSControlSize.Small;
            Lock.ImageScaling = NSImageScale.ProportionallyDown;
            Lock.Cell.ImageScale = NSImageScale.ProportionallyDown;
            Lock.Cell.ControlSize = NSControlSize.Small;
            Lock.ImagePosition = NSCellImagePosition.ImageOnly;
            Lock.Layout();
            
            AddArrangedSubview(Label);
            AddArrangedSubview(Input);
            AddArrangedSubview(Lock);
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
            string input = InputString;

            if (double.TryParse(input, out double value))
            {
                Console.WriteLine("Input field value changed: " + value.ToString());

                Input.TextColor = DefaultFieldColor;
            }
            else Input.TextColor = NSColor.SystemRed;
        }

        public override CGSize IntrinsicContentSize => new CGSize(75, 14);

        public void Setup(Parameter par)
        {
            this.parameter = par;

            Label.StringValue = par.Key.GetProperties().Description;
            Input.PlaceholderString = par.Value.ToString();
            Lock.State = par.IsLocked ? NSCellStateValue.On : NSCellStateValue.Off;

            Layout();
        }
    }
}

