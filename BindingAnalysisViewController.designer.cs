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
	[Register ("BindingAnalysisViewController")]
	partial class BindingAnalysisViewController
	{
		[Outlet]
		AppKit.NSSegmentedControl EnergyUnitControl { get; set; }

		[Outlet]
		AnalysisITC.TemperatureDependenceGraphView Graph { get; set; }

		[Outlet]
		AppKit.NSTextField LabelLabel { get; set; }

		[Outlet]
		AppKit.NSTableView ResultsTableView { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl TemperatureUnitControl { get; set; }

		[Outlet]
		AppKit.NSTextField ValueLabel { get; set; }

		[Action ("CloseButtonClicked:")]
		partial void CloseButtonClicked (Foundation.NSObject sender);

		[Action ("CopyToClipboard:")]
		partial void CopyToClipboard (Foundation.NSObject sender);

		[Action ("EnergyUnitControlClicked:")]
		partial void EnergyUnitControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("TempUnitControlClicked:")]
		partial void TempUnitControlClicked (AppKit.NSSegmentedControl sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (EnergyUnitControl != null) {
				EnergyUnitControl.Dispose ();
				EnergyUnitControl = null;
			}

			if (ResultsTableView != null) {
				ResultsTableView.Dispose ();
				ResultsTableView = null;
			}

			if (TemperatureUnitControl != null) {
				TemperatureUnitControl.Dispose ();
				TemperatureUnitControl = null;
			}

			if (Graph != null) {
				Graph.Dispose ();
				Graph = null;
			}

			if (LabelLabel != null) {
				LabelLabel.Dispose ();
				LabelLabel = null;
			}

			if (ValueLabel != null) {
				ValueLabel.Dispose ();
				ValueLabel = null;
			}
		}
	}
}
