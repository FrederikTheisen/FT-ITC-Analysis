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
	[Register ("ExperimentDesignerViewController")]
	partial class ExperimentDesignerViewController
	{
		//[Outlet]
        //GUI.MacOS.GraphViews.ExperimentDesignerGraphView SimulationAnalysisGraphView { get; set; }

		[Outlet]
		AnalysisITC.AnalysisGraphView AnalysisGraphView { get; set; }

        [Outlet]
		AppKit.NSTextField CellConcErrorField { get; set; }

		[Outlet]
		AppKit.NSTextField CellConcField { get; set; }

		[Outlet]
		AppKit.NSTextField InjectionCountField { get; set; }

		[Outlet]
		AppKit.NSStepper InjectionCountStepper { get; set; }

		[Outlet]
		AppKit.NSTextField InjectionInfoField { get; set; }

		[Outlet]
		AppKit.NSTextField InstrumentInfoField { get; set; }

		[Outlet]
		AppKit.NSMenu InstrumentMenu { get; set; }

		[Outlet]
		AppKit.NSPopUpButton ModelControl { get; set; }

		[Outlet]
		AppKit.NSMenu ModelMenu { get; set; }

		[Outlet]
		AppKit.NSStackView ModelOptionsStackView { get; set; }

		[Outlet]
		AppKit.NSButton SmallInitialInjControl { get; set; }

		[Outlet]
		AppKit.NSTextField SyringeConcErrorField { get; set; }

		[Outlet]
		AppKit.NSTextField SyringeConcField { get; set; }

		[Action ("ApplyModelSettings:")]
		partial void ApplyModelSettings (Foundation.NSObject sender);

		[Action ("InjectionInputChanged:")]
		partial void InjectionInputChanged (Foundation.NSObject sender);

		[Action ("InstrumentControlAction:")]
		partial void InstrumentControlAction (AppKit.NSPopUpButton sender);

		[Action ("ModelControlAction:")]
		partial void ModelControlAction (AppKit.NSPopUpButton sender);

		[Action ("Test:")]
		partial void Test (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{

			if (CellConcErrorField != null) {
				CellConcErrorField.Dispose ();
				CellConcErrorField = null;
			}

			if (CellConcField != null) {
				CellConcField.Dispose ();
				CellConcField = null;
			}

			if (InjectionCountField != null) {
				InjectionCountField.Dispose ();
				InjectionCountField = null;
			}

			if (InjectionCountStepper != null) {
				InjectionCountStepper.Dispose ();
				InjectionCountStepper = null;
			}

			if (InjectionInfoField != null) {
				InjectionInfoField.Dispose ();
				InjectionInfoField = null;
			}

			if (InstrumentInfoField != null) {
				InstrumentInfoField.Dispose ();
				InstrumentInfoField = null;
			}

			if (InstrumentMenu != null) {
				InstrumentMenu.Dispose ();
				InstrumentMenu = null;
			}

			if (ModelControl != null) {
				ModelControl.Dispose ();
				ModelControl = null;
			}

			if (ModelMenu != null) {
				ModelMenu.Dispose ();
				ModelMenu = null;
			}

			if (ModelOptionsStackView != null) {
				ModelOptionsStackView.Dispose ();
				ModelOptionsStackView = null;
			}

			if (SmallInitialInjControl != null) {
				SmallInitialInjControl.Dispose ();
				SmallInitialInjControl = null;
			}

			if (SyringeConcErrorField != null) {
				SyringeConcErrorField.Dispose ();
				SyringeConcErrorField = null;
			}

			if (SyringeConcField != null) {
				SyringeConcField.Dispose ();
				SyringeConcField = null;
			}
		}
	}
}
