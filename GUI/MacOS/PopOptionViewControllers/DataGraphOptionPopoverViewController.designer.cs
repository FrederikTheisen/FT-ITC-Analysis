// WARNING
//
// This file has been generated automatically by Visual Studio to store outlets and
// actions made in the UI designer. If it is removed, they will be lost.
// Manual changes to this file may not be handled correctly.
//
using Foundation;
using System.CodeDom.Compiler;

namespace AnalysisITC
{
	[Register ("DataGraphOptionPopoverViewController")]
	partial class DataGraphOptionPopoverViewController
	{
		[Outlet]
		AppKit.NSButton DrawBaseline { get; set; }

		[Outlet]
		AppKit.NSButton DrawCorrected { get; set; }

		[Outlet]
		AppKit.NSTextField PowerAxisTitleLabel { get; set; }

		[Outlet]
		AppKit.NSTextField TimeAxisTitleLabel { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl TimeUnitControl { get; set; }

		[Outlet]
		AppKit.NSButton UnifiedPowerAxis { get; set; }

		[Outlet]
		AppKit.NSTextField XTickLabel { get; set; }

		[Outlet]
		AppKit.NSStepper XTickStepper { get; set; }

		[Outlet]
		AppKit.NSTextField YTickLabel { get; set; }

		[Outlet]
		AppKit.NSStepper YTickStepper { get; set; }

		[Action ("ControlChanged:")]
		partial void ControlChanged (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (DrawBaseline != null) {
				DrawBaseline.Dispose ();
				DrawBaseline = null;
			}

			if (DrawCorrected != null) {
				DrawCorrected.Dispose ();
				DrawCorrected = null;
			}

			if (PowerAxisTitleLabel != null) {
				PowerAxisTitleLabel.Dispose ();
				PowerAxisTitleLabel = null;
			}

			if (TimeAxisTitleLabel != null) {
				TimeAxisTitleLabel.Dispose ();
				TimeAxisTitleLabel = null;
			}

			if (TimeUnitControl != null) {
				TimeUnitControl.Dispose ();
				TimeUnitControl = null;
			}

			if (UnifiedPowerAxis != null) {
				UnifiedPowerAxis.Dispose ();
				UnifiedPowerAxis = null;
			}

			if (XTickLabel != null) {
				XTickLabel.Dispose ();
				XTickLabel = null;
			}

			if (XTickStepper != null) {
				XTickStepper.Dispose ();
				XTickStepper = null;
			}

			if (YTickLabel != null) {
				YTickLabel.Dispose ();
				YTickLabel = null;
			}

			if (YTickStepper != null) {
				YTickStepper.Dispose ();
				YTickStepper = null;
			}
		}
	}
}
