// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;
using AnalysisITC.AppClasses.Analysis2;
using System.Collections.Generic;
//using static AnalysisITC.Analysis;
using CoreGraphics;

namespace AnalysisITC
{
	public partial class AnalysisGlobalModeOptionsView2 : NSStackView
	{
        public static event EventHandler ParameterContraintUpdated;

        GlobalModelParameters modelparameters;
        ParameterTypes key;
        List<VariableConstraint> options;

        private NSTextField Label;
        private NSSegmentedControl Control;

        public AnalysisGlobalModeOptionsView2(IntPtr handle) : base (handle)
		{
		}

        [Export("initWithFrame:")]
        public AnalysisGlobalModeOptionsView2(CGRect frameRect) : base(frameRect)
        {
            Frame = frameRect;
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            Distribution = NSStackViewDistribution.Fill;
            Alignment = NSLayoutAttribute.CenterY;
            //AutoresizingMask = NSViewResizingMask.WidthSizable;
            SetHuggingPriority(1000, NSLayoutConstraintOrientation.Horizontal);
            SetClippingResistancePriority(750, NSLayoutConstraintOrientation.Horizontal);
            SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            Control = new NSSegmentedControl()
            {
                SegmentCount = 3,
                SegmentStyle = NSSegmentStyle.Capsule,
                TranslatesAutoresizingMaskIntoConstraints = false,
                SelectedSegment = 0,
            };
            Control.Activated += SegmentedControlChanged;

            Label = new NSTextField();
            Label.BezelStyle = NSTextFieldBezelStyle.Rounded;
            Label.Bordered = false;
            Label.Editable = false;
            Label.StringValue = "init";
            Label.TranslatesAutoresizingMaskIntoConstraints = false;
            Label.HorizontalContentSizeConstraintActive = false;

            AddArrangedSubview(Label);
            AddArrangedSubview(Control);

            TranslatesAutoresizingMaskIntoConstraints = false;

            Label.SetContentHuggingPriorityForOrientation(249, NSLayoutConstraintOrientation.Horizontal);

            //AddConstraints(NSLayoutConstraint.FromVisualFormat("H:|-[stackView]-|", NSLayoutFormatOptions.AlignAllCenterY, null, new NSDictionary("stackView", this)));
            //AddConstraints(NSLayoutConstraint.FromVisualFormat("V:|-[stackView]-|", NSLayoutFormatOptions.AlignAllCenterX, null, new NSDictionary("stackView", this)));
        }

        public override CGSize IntrinsicContentSize => new CGSize(150, 20);

        public void Setup(ParameterTypes type, List<VariableConstraint> options, GlobalModelParameters modelparameters)
        {
            this.modelparameters = modelparameters;
            this.key = type;
            this.options = options;
            var labels = new List<string>();

            var selected = modelparameters.GetConstraintForParameter(key);
            Control.SegmentCount = options.Count;

            for (int i = 0; i < options.Count; i++)
            {
                VariableConstraint opt = options[i];
                Control.SetLabel(opt.GetEnumDescription(), i);

                if (selected == opt) Control.SelectedSegment = i;
            }

            Label.StringValue = key.GetEnumDescription() + " variable constraint";

            Layout();
        }

        private void SegmentedControlChanged(object sender, EventArgs e)
        {
            var selectedIndex = Control.SelectedSegment;
            Console.WriteLine("Selected index: " + options[(int)Control.SelectedSegment].ToString());
            modelparameters.SetConstraintForParameter(key, options[(int)Control.SelectedSegment]);

            ParameterContraintUpdated?.Invoke(null, null);
        }
    }
}