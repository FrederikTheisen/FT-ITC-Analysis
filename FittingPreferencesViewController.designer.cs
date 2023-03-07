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
	[Register ("FittingPreferencesViewController")]
	partial class FittingPreferencesViewController
	{
		[Outlet]
		AppKit.NSTextField AutoConcField { get; set; }

		[Outlet]
		AppKit.NSSlider AutoConcVarianceSlider { get; set; }

		[Outlet]
		AppKit.NSTextField BootstrapIterField { get; set; }

		[Outlet]
		AppKit.NSSlider DefaultBootstrapIterationSlider { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl DefaultErrorMethodControl { get; set; }

		[Outlet]
		AppKit.NSTextField FuncToleranceField { get; set; }

		[Outlet]
		AppKit.NSSlider FuncToleranceSlider { get; set; }

		[Outlet]
		AppKit.NSButton IncludeConcVarianceCheck { get; set; }

		[Outlet]
		AppKit.NSSlider MaxOptimizerIterationsSlider { get; set; }

		[Outlet]
		AppKit.NSTextField MaxOptimizerIterField { get; set; }

		[Action ("Apply:")]
		partial void Apply (Foundation.NSObject sender);

		[Action ("AutoConcSliderAction:")]
		partial void AutoConcSliderAction (AppKit.NSSlider sender);

		[Action ("BootstrapIterSliderAction:")]
		partial void BootstrapIterSliderAction (AppKit.NSSlider sender);

		[Action ("Close:")]
		partial void Close (Foundation.NSObject sender);

		[Action ("FuncToleranceSliderAction:")]
		partial void FuncToleranceSliderAction (AppKit.NSSlider sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (AutoConcField != null) {
				AutoConcField.Dispose ();
				AutoConcField = null;
			}

			if (AutoConcVarianceSlider != null) {
				AutoConcVarianceSlider.Dispose ();
				AutoConcVarianceSlider = null;
			}

			if (MaxOptimizerIterField != null) {
				MaxOptimizerIterField.Dispose ();
				MaxOptimizerIterField = null;
			}

			if (BootstrapIterField != null) {
				BootstrapIterField.Dispose ();
				BootstrapIterField = null;
			}

			if (DefaultBootstrapIterationSlider != null) {
				DefaultBootstrapIterationSlider.Dispose ();
				DefaultBootstrapIterationSlider = null;
			}

			if (DefaultErrorMethodControl != null) {
				DefaultErrorMethodControl.Dispose ();
				DefaultErrorMethodControl = null;
			}

			if (FuncToleranceField != null) {
				FuncToleranceField.Dispose ();
				FuncToleranceField = null;
			}

			if (FuncToleranceSlider != null) {
				FuncToleranceSlider.Dispose ();
				FuncToleranceSlider = null;
			}

			if (IncludeConcVarianceCheck != null) {
				IncludeConcVarianceCheck.Dispose ();
				IncludeConcVarianceCheck = null;
			}

			if (MaxOptimizerIterationsSlider != null) {
				MaxOptimizerIterationsSlider.Dispose ();
				MaxOptimizerIterationsSlider = null;
			}
		}
	}
}
