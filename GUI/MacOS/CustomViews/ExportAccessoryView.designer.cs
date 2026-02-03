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
	[Register ("ExportAccessoryView")]
	partial class ExportAccessoryView
	{
		[Outlet]
		AppKit.NSTextField BSLLabel { get; set; }

		[Outlet]
		AppKit.NSButton ExportCorrectedControl { get; set; }

		[Outlet]
		AppKit.NSButton ExportOffsetCorrectedControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ExportSelectionControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ExportTypeControl { get; set; }

		[Outlet]
		AppKit.NSTextField ExportTypeInfo { get; set; }

		[Outlet]
		AppKit.NSTextField FitPeakLabel { get; set; }

		[Outlet]
		AppKit.NSButton IncludeFittedPeaksControl { get; set; }

		[Outlet]
		AppKit.NSButton ITCsimExportOffsetCorrectedControl { get; set; }

		[Outlet]
		AppKit.NSTabView TabView { get; set; }

		[Outlet]
		AppKit.NSPopUpButton ThirdPartyFormatButton { get; set; }

		[Outlet]
		AppKit.NSButton UnifyTimeAxisControl { get; set; }

		[Action ("BaselineCorrectControlAction:")]
		partial void BaselineCorrectControlAction (AppKit.NSButton sender);

		[Action ("ExportSelectionControlAction:")]
		partial void ExportSelectionControlAction (AppKit.NSSegmentedControl sender);

		[Action ("ExportTypeControlAction:")]
		partial void ExportTypeControlAction (AppKit.NSSegmentedControl sender);

		[Action ("FittedPeakControlAction:")]
		partial void FittedPeakControlAction (AppKit.NSButton sender);

		[Action ("ITCsimExportOffsetCorrectedAction:")]
		partial void ITCsimExportOffsetCorrectedAction (AppKit.NSButton sender);

		[Action ("OffsetCorrectControlAction:")]
		partial void OffsetCorrectControlAction (AppKit.NSButton sender);

		[Action ("SelectThirdPartyAction:")]
		partial void SelectThirdPartyAction (AppKit.NSPopUpButton sender);

		[Action ("UnifyTimeAxisControlAction:")]
		partial void UnifyTimeAxisControlAction (AppKit.NSButton sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (BSLLabel != null) {
				BSLLabel.Dispose ();
				BSLLabel = null;
			}

			if (ExportCorrectedControl != null) {
				ExportCorrectedControl.Dispose ();
				ExportCorrectedControl = null;
			}

			if (ExportOffsetCorrectedControl != null) {
				ExportOffsetCorrectedControl.Dispose ();
				ExportOffsetCorrectedControl = null;
			}

			if (ExportSelectionControl != null) {
				ExportSelectionControl.Dispose ();
				ExportSelectionControl = null;
			}

			if (ExportTypeControl != null) {
				ExportTypeControl.Dispose ();
				ExportTypeControl = null;
			}

			if (FitPeakLabel != null) {
				FitPeakLabel.Dispose ();
				FitPeakLabel = null;
			}

			if (IncludeFittedPeaksControl != null) {
				IncludeFittedPeaksControl.Dispose ();
				IncludeFittedPeaksControl = null;
			}

			if (ITCsimExportOffsetCorrectedControl != null) {
				ITCsimExportOffsetCorrectedControl.Dispose ();
				ITCsimExportOffsetCorrectedControl = null;
			}

			if (TabView != null) {
				TabView.Dispose ();
				TabView = null;
			}

			if (UnifyTimeAxisControl != null) {
				UnifyTimeAxisControl.Dispose ();
				UnifyTimeAxisControl = null;
			}

			if (ThirdPartyFormatButton != null) {
				ThirdPartyFormatButton.Dispose ();
				ThirdPartyFormatButton = null;
			}

			if (ExportTypeInfo != null) {
				ExportTypeInfo.Dispose ();
				ExportTypeInfo = null;
			}
		}
	}
}
