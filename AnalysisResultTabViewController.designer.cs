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
		AppKit.NSSegmentedControl TempControl { get; set; }

		[Action ("CopyToClipboard:")]
		partial void CopyToClipboard (Foundation.NSObject sender);

		[Action ("CopyToClipboatd:")]
		partial void CopyToClipboatd (Foundation.NSObject sender);

		[Action ("EnergyControlClicked:")]
		partial void EnergyControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("TempControlClicked:")]
		partial void TempControlClicked (AppKit.NSSegmentedControl sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (EnergyControl != null) {
				EnergyControl.Dispose ();
				EnergyControl = null;
			}

			if (TempControl != null) {
				TempControl.Dispose ();
				TempControl = null;
			}

			if (ResultsTableView != null) {
				ResultsTableView.Dispose ();
				ResultsTableView = null;
			}

			if (Graph != null) {
				Graph.Dispose ();
				Graph = null;
			}
		}
	}
}
