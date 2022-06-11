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
	[Register ("AnalysisResultTabViewController")]
	partial class AnalysisResultTabViewController
	{
		[Outlet]
		AppKit.NSSegmentedControl EnergyControl { get; set; }

		[Outlet]
		AnalysisITC.TemperatureDependenceGraphView Graph { get; set; }

		[Outlet]
		AppKit.NSTableView ResultsTableView { get; set; }

		[Outlet]
		AppKit.NSButton SRFitButton { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl SRFoldedDegreeSegControl { get; set; }

		[Outlet]
		AppKit.NSTextField SRResultTextField { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl SRTemperatureModeSegControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl TempControl { get; set; }

		[Action ("CopyToClipboard:")]
		partial void CopyToClipboard (Foundation.NSObject sender);

		[Action ("CopyToClipboatd:")]
		partial void CopyToClipboatd (Foundation.NSObject sender);

		[Action ("EnergyControlClicked:")]
		partial void EnergyControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("PerformSRAnalysis:")]
		partial void PerformSRAnalysis (Foundation.NSObject sender);

		[Action ("TempControlClicked:")]
		partial void TempControlClicked (AppKit.NSSegmentedControl sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (SRResultTextField != null) {
				SRResultTextField.Dispose ();
				SRResultTextField = null;
			}

			if (SRTemperatureModeSegControl != null) {
				SRTemperatureModeSegControl.Dispose ();
				SRTemperatureModeSegControl = null;
			}

			if (SRFoldedDegreeSegControl != null) {
				SRFoldedDegreeSegControl.Dispose ();
				SRFoldedDegreeSegControl = null;
			}

			if (SRFitButton != null) {
				SRFitButton.Dispose ();
				SRFitButton = null;
			}

			if (EnergyControl != null) {
				EnergyControl.Dispose ();
				EnergyControl = null;
			}

			if (Graph != null) {
				Graph.Dispose ();
				Graph = null;
			}

			if (ResultsTableView != null) {
				ResultsTableView.Dispose ();
				ResultsTableView = null;
			}

			if (TempControl != null) {
				TempControl.Dispose ();
				TempControl = null;
			}
		}
	}
}
