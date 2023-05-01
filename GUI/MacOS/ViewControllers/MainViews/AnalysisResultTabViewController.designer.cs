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
		AppKit.NSSegmentedControl ElectrostaticAnalysisModel { get; set; }

		[Outlet]
		AppKit.NSTextField ElectrostaticAnalysisOutput { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl EnergyControl { get; set; }

		[Outlet]
		AppKit.NSTextField EvaluateionTemperatureTextField { get; set; }

		[Outlet]
		AppKit.NSTextField EvaluationOutputLabel { get; set; }

		[Outlet]
		AppKit.NSButton ExperimentListButton { get; set; }

		[Outlet]
		AnalysisITC.ResultGraphView Graph { get; set; }

		[Outlet]
		AppKit.NSTextField ResultEvalTempUnitLabel { get; set; }

		[Outlet]
		AppKit.NSTableView ResultsTableView { get; set; }

		[Outlet]
		AppKit.NSTextField ResultSummaryLabel { get; set; }

		[Outlet]
		AppKit.NSButton SRFitButton { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl SRFoldedDegreeSegControl { get; set; }

		[Outlet]
		AppKit.NSTextField SRResultTextField { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl SRTemperatureModeSegControl { get; set; }

		[Outlet]
		AppKit.NSTabView TabView { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl TempControl { get; set; }

		[Outlet]
		AppKit.NSTextField TemperatureDependenceLabel { get; set; }

		[Action ("CopyToClipboard:")]
		partial void CopyToClipboard (Foundation.NSObject sender);

		[Action ("CopyToClipboatd:")]
		partial void CopyToClipboatd (Foundation.NSObject sender);

		[Action ("EnergyControlClicked:")]
		partial void EnergyControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("EvaluateParameters:")]
		partial void EvaluateParameters (Foundation.NSObject sender);

		[Action ("PeformIonicStrengthAnalysis:")]
		partial void PeformIonicStrengthAnalysis (AppKit.NSButton sender);

		[Action ("PerformProtonationAnalysis:")]
		partial void PerformProtonationAnalysis (AppKit.NSButton sender);

		[Action ("PerformSRAnalysis:")]
		partial void PerformSRAnalysis (Foundation.NSObject sender);

		[Action ("TempControlClicked:")]
		partial void TempControlClicked (AppKit.NSSegmentedControl sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (EnergyControl != null) {
				EnergyControl.Dispose ();
				EnergyControl = null;
			}

			if (EvaluateionTemperatureTextField != null) {
				EvaluateionTemperatureTextField.Dispose ();
				EvaluateionTemperatureTextField = null;
			}

			if (EvaluationOutputLabel != null) {
				EvaluationOutputLabel.Dispose ();
				EvaluationOutputLabel = null;
			}

			if (ExperimentListButton != null) {
				ExperimentListButton.Dispose ();
				ExperimentListButton = null;
			}

			if (Graph != null) {
				Graph.Dispose ();
				Graph = null;
			}

			if (ResultEvalTempUnitLabel != null) {
				ResultEvalTempUnitLabel.Dispose ();
				ResultEvalTempUnitLabel = null;
			}

			if (ResultsTableView != null) {
				ResultsTableView.Dispose ();
				ResultsTableView = null;
			}

			if (ResultSummaryLabel != null) {
				ResultSummaryLabel.Dispose ();
				ResultSummaryLabel = null;
			}

			if (SRFitButton != null) {
				SRFitButton.Dispose ();
				SRFitButton = null;
			}

			if (SRFoldedDegreeSegControl != null) {
				SRFoldedDegreeSegControl.Dispose ();
				SRFoldedDegreeSegControl = null;
			}

			if (SRResultTextField != null) {
				SRResultTextField.Dispose ();
				SRResultTextField = null;
			}

			if (SRTemperatureModeSegControl != null) {
				SRTemperatureModeSegControl.Dispose ();
				SRTemperatureModeSegControl = null;
			}

			if (TabView != null) {
				TabView.Dispose ();
				TabView = null;
			}

			if (TempControl != null) {
				TempControl.Dispose ();
				TempControl = null;
			}

			if (TemperatureDependenceLabel != null) {
				TemperatureDependenceLabel.Dispose ();
				TemperatureDependenceLabel = null;
			}

			if (ElectrostaticAnalysisOutput != null) {
				ElectrostaticAnalysisOutput.Dispose ();
				ElectrostaticAnalysisOutput = null;
			}

			if (ElectrostaticAnalysisModel != null) {
				ElectrostaticAnalysisModel.Dispose ();
				ElectrostaticAnalysisModel = null;
			}
		}
	}
}
