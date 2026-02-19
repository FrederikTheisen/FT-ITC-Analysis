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
		AppKit.NSSegmentedControl ConcentrationUnitControl { get; set; }

		[Outlet]
		AppKit.NSButton DiscardIntegrationRegionForBaseline { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl EnergyUnitControl { get; set; }

		[Outlet]
		AppKit.NSTextField FinalFigHeightField { get; set; }

		[Outlet]
		AppKit.NSTextField FinalFigWidthField { get; set; }

		[Outlet]
		AppKit.NSButton IncludeBufferInIonicStrength { get; set; }

		[Outlet]
		AppKit.NSTextField MinSaltSpanField { get; set; }

		[Outlet]
		AppKit.NSSlider MinSaltSpanSlider { get; set; }

		[Outlet]
		AppKit.NSTextField MinTempSpanField { get; set; }

		[Outlet]
		AppKit.NSSlider MinTempSpanSlider { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ParameterRoundingSettingsControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl PeakFitAlgorithmControl { get; set; }

		[Outlet]
		AppKit.NSTextField RefTempField { get; set; }

		[Action ("Apply:")]
		partial void Apply (Foundation.NSObject sender);

		[Action ("Close:")]
		partial void Close (Foundation.NSObject sender);

		[Action ("ColorGradientControlAction:")]
		partial void ColorGradientControlAction (AppKit.NSSegmentedControl sender);

		[Action ("EnergyUnitControlAction:")]
		partial void EnergyUnitControlAction (AppKit.NSSegmentedControl sender);

		[Action ("RefTempAction:")]
		partial void RefTempAction (AppKit.NSTextField sender);

		[Action ("Reset:")]
		partial void Reset (Foundation.NSObject sender);

		[Action ("TempSpanSlicerAction:")]
		partial void TempSpanSlicerAction (AppKit.NSSlider sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (DiscardIntegrationRegionForBaseline != null) {
				DiscardIntegrationRegionForBaseline.Dispose ();
				DiscardIntegrationRegionForBaseline = null;
			}

			if (ColorGradientControl != null) {
				ColorGradientControl.Dispose ();
				ColorGradientControl = null;
			}

			if (ColorMenu != null) {
				ColorMenu.Dispose ();
				ColorMenu = null;
			}

			if (ColorThemeMenu != null) {
				ColorThemeMenu.Dispose ();
				ColorThemeMenu = null;
			}

			if (ConcentrationUnitControl != null) {
				ConcentrationUnitControl.Dispose ();
				ConcentrationUnitControl = null;
			}

			if (EnergyUnitControl != null) {
				EnergyUnitControl.Dispose ();
				EnergyUnitControl = null;
			}

			if (FinalFigHeightField != null) {
				FinalFigHeightField.Dispose ();
				FinalFigHeightField = null;
			}

			if (FinalFigWidthField != null) {
				FinalFigWidthField.Dispose ();
				FinalFigWidthField = null;
			}

			if (IncludeBufferInIonicStrength != null) {
				IncludeBufferInIonicStrength.Dispose ();
				IncludeBufferInIonicStrength = null;
			}

			if (MinSaltSpanField != null) {
				MinSaltSpanField.Dispose ();
				MinSaltSpanField = null;
			}

			if (MinSaltSpanSlider != null) {
				MinSaltSpanSlider.Dispose ();
				MinSaltSpanSlider = null;
			}

			if (MinTempSpanField != null) {
				MinTempSpanField.Dispose ();
				MinTempSpanField = null;
			}

			if (MinTempSpanSlider != null) {
				MinTempSpanSlider.Dispose ();
				MinTempSpanSlider = null;
			}

			if (ParameterRoundingSettingsControl != null) {
				ParameterRoundingSettingsControl.Dispose ();
				ParameterRoundingSettingsControl = null;
			}

			if (PeakFitAlgorithmControl != null) {
				PeakFitAlgorithmControl.Dispose ();
				PeakFitAlgorithmControl = null;
			}

			if (RefTempField != null) {
				RefTempField.Dispose ();
				RefTempField = null;
			}
		}
	}
}
