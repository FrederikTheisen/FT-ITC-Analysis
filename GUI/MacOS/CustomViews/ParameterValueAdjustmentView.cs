using System;
using AnalysisITC.AppClasses.Analysis2;
using AppKit;
using CoreGraphics;
using PrintCore;
using static AnalysisITC.Analysis;
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
        private NSButton Lock;

        public bool HasBeenAffectedFlag { get; private set; } = false;
        
        public ParameterTypes Key => parameter.Key;
        public double Value
        {
            get
            {
                if (Input.StringValue.Length > 0) return Input.FloatValue;
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
            ////AutoresizingMask = NSViewResizingMask.WidthSizable;
            //SetHuggingPriority(1000, NSLayoutConstraintOrientation.Horizontal);
            //SetClippingResistancePriority(1000, NSLayoutConstraintOrientation.Horizontal);
            //SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

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
            //Label.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);
            //Label.SetContentCompressionResistancePriority(750, NSLayoutConstraintOrientation.Horizontal);

            Input = new HuggingTextView(new CGRect(0, 0, 54, 14))
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
            //Input.SetContentHuggingPriorityForOrientation(251, NSLayoutConstraintOrientation.Horizontal);
            //Input.SetContentCompressionResistancePriority(750, NSLayoutConstraintOrientation.Horizontal);
            Input.AddConstraint(NSLayoutConstraint.Create(Input, NSLayoutAttribute.Width, NSLayoutRelation.Equal, 1, 54));
            Input.RefusesFirstResponder = true;

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

            //TranslatesAutoresizingMaskIntoConstraints = false;
        }

        private void Lock_Activated(object sender, EventArgs e)
        {
            HasBeenAffectedFlag = true;
        }

        private void Input_Changed(object sender, EventArgs e)
        {
            HasBeenAffectedFlag = true;
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

