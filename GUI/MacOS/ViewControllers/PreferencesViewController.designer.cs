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
	[Register ("PreferencesViewController")]
	partial class PreferencesViewController
	{
		[Outlet]
		AppKit.NSTextField DefaultBootstrapIterationLabel { get; set; }

		[Outlet]
		AppKit.NSSlider DefaultBootstrapIterationSlider { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl DefaultErrorMethodControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl EnergyUnitControl { get; set; }

		[Outlet]
		AppKit.NSTextField FuncToleranceLabel { get; set; }

		[Outlet]
		AppKit.NSSlider FuncToleranceSlider { get; set; }

		[Outlet]
		AppKit.NSButton IncludeConcVarianceCheck { get; set; }

		[Outlet]
		AppKit.NSSlider MaxOptimizerIterationsSlider { get; set; }

		[Outlet]
		AppKit.NSSlider MinTempSpanSlider { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl PeakFitAlgorithmControl { get; set; }

		[Outlet]
		AppKit.NSTextField RefTempField { get; set; }

		[Outlet]
		AppKit.NSTextField TempSpanLabel { get; set; }

		[Action ("Apply:")]
		partial void Apply (Foundation.NSObject sender);

		[Action ("Close:")]
		partial void Close (Foundation.NSObject sender);

		[Action ("DefaultBootstrapIterationAction:")]
		partial void DefaultBootstrapIterationAction (AppKit.NSSlider sender);

		[Action ("FuncToleranceAction:")]
		partial void FuncToleranceAction (AppKit.NSSlider sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (EnergyUnitControl != null) {
				EnergyUnitControl.Dispose ();
				EnergyUnitControl = null;
			}

			if (MinTempSpanSlider != null) {
				MinTempSpanSlider.Dispose ();
				MinTempSpanSlider = null;
			}

			if (PeakFitAlgorithmControl != null) {
				PeakFitAlgorithmControl.Dispose ();
				PeakFitAlgorithmControl = null;
			}

			if (RefTempField != null) {
				RefTempField.Dispose ();
				RefTempField = null;
			}

			if (DefaultBootstrapIterationSlider != null) {
				DefaultBootstrapIterationSlider.Dispose ();
				DefaultBootstrapIterationSlider = null;
			}

			if (FuncToleranceLabel != null) {
				FuncToleranceLabel.Dispose ();
				FuncToleranceLabel = null;
			}

			if (TempSpanLabel != null) {
				TempSpanLabel.Dispose ();
				TempSpanLabel = null;
			}

			if (DefaultErrorMethodControl != null) {
				DefaultErrorMethodControl.Dispose ();
				DefaultErrorMethodControl = null;
			}

			if (DefaultBootstrapIterationLabel != null) {
				DefaultBootstrapIterationLabel.Dispose ();
				DefaultBootstrapIterationLabel = null;
			}

			if (IncludeConcVarianceCheck != null) {
				IncludeConcVarianceCheck.Dispose ();
				IncludeConcVarianceCheck = null;
			}

			if (MaxOptimizerIterationsSlider != null) {
				MaxOptimizerIterationsSlider.Dispose ();
				MaxOptimizerIterationsSlider = null;
			}

			if (FuncToleranceSlider != null) {
				FuncToleranceSlider.Dispose ();
				FuncToleranceSlider = null;
			}
		}
	}
}
