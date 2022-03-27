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
	[Register ("DataAnalysisViewController")]
	partial class DataAnalysisViewController
	{
		[Outlet]
		AppKit.NSSegmentedControl AnalysisModeControl { get; set; }

		[Outlet]
		AppKit.NSButton ApplyToAllExperimentsControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl GlobalVariablesControl { get; set; }

		[Outlet]
		AppKit.NSStackView GlobalVariablesView { get; set; }

		[Outlet]
		AnalysisITC.AnalysisGraphView GraphView { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ModelTypeControl { get; set; }

		[Action ("AnalysisModeClicked:")]
		partial void AnalysisModeClicked (AppKit.NSSegmentedControl sender);

		[Action ("FitSimplex:")]
		partial void FitSimplex (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (ApplyToAllExperimentsControl != null) {
				ApplyToAllExperimentsControl.Dispose ();
				ApplyToAllExperimentsControl = null;
			}

			if (AnalysisModeControl != null) {
				AnalysisModeControl.Dispose ();
				AnalysisModeControl = null;
			}

			if (ModelTypeControl != null) {
				ModelTypeControl.Dispose ();
				ModelTypeControl = null;
			}

			if (GlobalVariablesControl != null) {
				GlobalVariablesControl.Dispose ();
				GlobalVariablesControl = null;
			}

			if (GraphView != null) {
				GraphView.Dispose ();
				GraphView = null;
			}

			if (GlobalVariablesView != null) {
				GlobalVariablesView.Dispose ();
				GlobalVariablesView = null;
			}
		}
	}
}
