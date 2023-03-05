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
	[Register ("GeneralSettingsViewController")]
	partial class GeneralSettingsViewController
	{
		[Outlet]
		AppKit.NSSegmentedControl ColorGradientControl { get; set; }

		[Outlet]
		AppKit.NSMenu ColorMenu { get; set; }

		[Outlet]
		AppKit.NSPopUpButton ColorThemeMenu { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl EnergyUnitControl { get; set; }

		[Outlet]
		AppKit.NSTextField FinalFigHeightField { get; set; }

		[Outlet]
		AppKit.NSTextField FinalFigWidthField { get; set; }

		[Outlet]
		AppKit.NSTextField MinTempSpanField { get; set; }

		[Outlet]
		AppKit.NSSlider MinTempSpanSlider { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl PeakFitAlgorithmControl { get; set; }

		[Outlet]
		AppKit.NSTextField RefTempField { get; set; }

		[Action ("ColorGradientControlAction:")]
		partial void ColorGradientControlAction (AppKit.NSSegmentedControl sender);

		[Action ("EnergyUnitControlAction:")]
		partial void EnergyUnitControlAction (AppKit.NSSegmentedControl sender);

		[Action ("RefTempAction:")]
		partial void RefTempAction (AppKit.NSTextField sender);

		[Action ("TempSpanSlicerAction:")]
		partial void TempSpanSlicerAction (AppKit.NSSlider sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (EnergyUnitControl != null) {
				EnergyUnitControl.Dispose ();
				EnergyUnitControl = null;
			}

			if (ColorGradientControl != null) {
				ColorGradientControl.Dispose ();
				ColorGradientControl = null;
			}

			if (ColorThemeMenu != null) {
				ColorThemeMenu.Dispose ();
				ColorThemeMenu = null;
			}

			if (MinTempSpanSlider != null) {
				MinTempSpanSlider.Dispose ();
				MinTempSpanSlider = null;
			}

			if (MinTempSpanField != null) {
				MinTempSpanField.Dispose ();
				MinTempSpanField = null;
			}

			if (RefTempField != null) {
				RefTempField.Dispose ();
				RefTempField = null;
			}

			if (FinalFigHeightField != null) {
				FinalFigHeightField.Dispose ();
				FinalFigHeightField = null;
			}

			if (FinalFigWidthField != null) {
				FinalFigWidthField.Dispose ();
				FinalFigWidthField = null;
			}

			if (PeakFitAlgorithmControl != null) {
				PeakFitAlgorithmControl.Dispose ();
				PeakFitAlgorithmControl = null;
			}

			if (ColorMenu != null) {
				ColorMenu.Dispose ();
				ColorMenu = null;
			}
		}
	}
}
