using System;
using AppKit;
using CoreGraphics;
using Foundation;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
	public class CustomDrawingSegmentedControl : NSSegmentedControl
	{
        [Export("initWithFrame:")]
        public CustomDrawingSegmentedControl(CGRect frameRect) : base(frameRect)
        {
		}

        public override void DrawRect(CGRect dirtyRect)
        {
            base.DrawRect(dirtyRect);
        }
    }
}

