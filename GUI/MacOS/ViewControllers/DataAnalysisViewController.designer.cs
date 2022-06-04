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
		AppKit.NSSegmentedControl AffinityStyleSegControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl AnalysisModeControl { get; set; }

		[Outlet]
		AppKit.NSButton ApplyToAllExperimentsControl { get; set; }

		[Outlet]
		AppKit.NSButton AxesScopeButton { get; set; }

		[Outlet]
		AppKit.NSTextField CstepTextField { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl EnthalpyStyleSegControl { get; set; }

		[Outlet]
		AppKit.NSButton FitLMButton { get; set; }

		[Outlet]
		AppKit.NSButton FitSimplexButton { get; set; }

		[Outlet]
		AppKit.NSStackView GlobalAffinityStyle { get; set; }

		[Outlet]
		AppKit.NSStackView GlobalEnthalpyStyle { get; set; }

		[Outlet]
		AppKit.NSStackView GlobalNView { get; set; }

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
		AppKit.NSSegmentedControl NStyleSegControl { get; set; }

		[Outlet]
		AppKit.NSTextField OstepTextField { get; set; }

		[Outlet]
		AppKit.NSButton ParametersScopeButton { get; set; }

		[Outlet]
		AppKit.NSButton PeakInfoScopeButton { get; set; }

		[Outlet]
		AppKit.NSStackView SolverStepSizeView { get; set; }

		[Action ("AnalysisModeClicked:")]
		partial void AnalysisModeClicked (AppKit.NSSegmentedControl sender);

		[Action ("CopySettingsToAll:")]
		partial void CopySettingsToAll (Foundation.NSObject sender);

		[Action ("FeatureDrawControlClicked:")]
		partial void FeatureDrawControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("FitSimplex:")]
		partial void FitSimplex (Foundation.NSObject sender);

		[Action ("ScopeButtonClicked:")]
		partial void ScopeButtonClicked (AppKit.NSButton sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (AffinityStyleSegControl != null) {
				AffinityStyleSegControl.Dispose ();
				AffinityStyleSegControl = null;
			}

			if (AnalysisModeControl != null) {
				AnalysisModeControl.Dispose ();
				AnalysisModeControl = null;
			}

			if (ApplyToAllExperimentsControl != null) {
				ApplyToAllExperimentsControl.Dispose ();
				ApplyToAllExperimentsControl = null;
			}

			if (AxesScopeButton != null) {
				AxesScopeButton.Dispose ();
				AxesScopeButton = null;
			}

			if (CstepTextField != null) {
				CstepTextField.Dispose ();
				CstepTextField = null;
			}

			if (EnthalpyStyleSegControl != null) {
				EnthalpyStyleSegControl.Dispose ();
				EnthalpyStyleSegControl = null;
			}

			if (FitLMButton != null) {
				FitLMButton.Dispose ();
				FitLMButton = null;
			}

			if (FitSimplexButton != null) {
				FitSimplexButton.Dispose ();
				FitSimplexButton = null;
			}

			if (GlobalAffinityStyle != null) {
				GlobalAffinityStyle.Dispose ();
				GlobalAffinityStyle = null;
			}

			if (GlobalEnthalpyStyle != null) {
				GlobalEnthalpyStyle.Dispose ();
				GlobalEnthalpyStyle = null;
			}

			if (GlobalNView != null) {
				GlobalNView.Dispose ();
				GlobalNView = null;
			}

			if (NStyleSegControl != null) {
				NStyleSegControl.Dispose ();
				NStyleSegControl = null;
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

			if (GstepTextField != null) {
				GstepTextField.Dispose ();
				GstepTextField = null;
			}

			if (HstepTextField != null) {
				HstepTextField.Dispose ();
				HstepTextField = null;
			}

			if (ModelTypeControl != null) {
				ModelTypeControl.Dispose ();
				ModelTypeControl = null;
			}

			if (NstepTextField != null) {
				NstepTextField.Dispose ();
				NstepTextField = null;
			}

			if (OstepTextField != null) {
				OstepTextField.Dispose ();
				OstepTextField = null;
			}

			if (ParametersScopeButton != null) {
				ParametersScopeButton.Dispose ();
				ParametersScopeButton = null;
			}

			if (PeakInfoScopeButton != null) {
				PeakInfoScopeButton.Dispose ();
				PeakInfoScopeButton = null;
			}

			if (SolverStepSizeView != null) {
				SolverStepSizeView.Dispose ();
				SolverStepSizeView = null;
			}
		}
	}
}
