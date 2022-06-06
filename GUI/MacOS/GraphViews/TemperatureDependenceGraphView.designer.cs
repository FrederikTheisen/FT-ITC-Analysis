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
	[Register ("TemperatureDependenceGraphView")]
	partial class TemperatureDependenceGraphView
	{
		[Outlet]
		AppKit.NSTableView ResultTableView { get; set; }

		[Action ("CopyToClipboard:")]
		partial void CopyToClipboard (Foundation.NSObject sender);

		[Action ("EnergyControlClicked:")]
		partial void EnergyControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("TempUnitControlClick:")]
		partial void TempUnitControlClick (AppKit.NSSegmentedCell sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (ResultTableView != null) {
				ResultTableView.Dispose ();
				ResultTableView = null;
			}
		}
	}
}
