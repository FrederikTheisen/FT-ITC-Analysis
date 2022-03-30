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
		AppKit.NSTextField CstepTextField { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl GlobalVariablesControl { get; set; }

		[Outlet]
		AppKit.NSStackView GlobalVariablesView { get; set; }

		[Outlet]
		AnalysisITC.AnalysisGraphView GraphView { get; set; }

		[Outlet]
		AppKit.NSTextField GstepTextField { get; set; }

		[Outlet]
		AppKit.NSTextField HstepTextField { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ModelTypeControl { get; set; }

		[Outlet]
		AppKit.NSTextField NstepTextField { get; set; }

		[Outlet]
		AppKit.NSTextField OstepTextField { get; set; }

		[Outlet]
		AppKit.NSStackView SolverStepSizeView { get; set; }

		[Action ("AnalysisModeClicked:")]
		partial void AnalysisModeClicked (AppKit.NSSegmentedControl sender);

		[Action ("FeatureDrawControlClicked:")]
		partial void FeatureDrawControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("FitSimplex:")]
		partial void FitSimplex (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (CstepTextField != null) {
				CstepTextField.Dispose ();
				CstepTextField = null;
			}

			if (HstepTextField != null) {
				HstepTextField.Dispose ();
				HstepTextField = null;
			}

			if (GstepTextField != null) {
				GstepTextField.Dispose ();
				GstepTextField = null;
			}

			if (NstepTextField != null) {
				NstepTextField.Dispose ();
				NstepTextField = null;
			}

			if (OstepTextField != null) {
				OstepTextField.Dispose ();
				OstepTextField = null;
			}

			if (AnalysisModeControl != null) {
				AnalysisModeControl.Dispose ();
				AnalysisModeControl = null;
			}

			if (ApplyToAllExperimentsControl != null) {
				ApplyToAllExperimentsControl.Dispose ();
				ApplyToAllExperimentsControl = null;
			}

			if (GlobalVariablesControl != null) {
				GlobalVariablesControl.Dispose ();
				GlobalVariablesControl = null;
			}

			if (GlobalVariablesView != null) {
				GlobalVariablesView.Dispose ();
				GlobalVariablesView = null;
			}

			if (GraphView != null) {
				GraphView.Dispose ();
				GraphView = null;
			}

			if (ModelTypeControl != null) {
				ModelTypeControl.Dispose ();
				ModelTypeControl = null;
			}

			if (SolverStepSizeView != null) {
				SolverStepSizeView.Dispose ();
				SolverStepSizeView = null;
			}
		}
	}
}
