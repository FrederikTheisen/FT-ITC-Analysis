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
	[Register ("ExperimentDesignerViewController2")]
	partial class ExperimentDesignerViewController2
	{
		[Outlet]
		AppKit.NSButton ApplyModelButton { get; set; }

		[Outlet]
		AppKit.NSTextField CellConcErrorField { get; set; }

		[Outlet]
		AppKit.NSTextField CellConcField { get; set; }

		[Outlet]
		AppKit.NSStepper InjectionCoundStepper { get; set; }

		[Outlet]
		AppKit.NSTextField InjectionCountField { get; set; }

		[Outlet]
		AppKit.NSStepper InjectionCountStepper { get; set; }

		[Outlet]
		AppKit.NSTextField InjectionInfoField { get; set; }

		[Outlet]
		AppKit.NSTextField InstrumentDescriptionField { get; set; }

		[Outlet]
		AppKit.NSMenu InstrumentMenu { get; set; }

		[Outlet]
		AppKit.NSPopUpButton ModelControl { get; set; }

		[Outlet]
		AppKit.NSMenu ModelMenu { get; set; }

		[Outlet]
		AppKit.NSStackView ModelOptionsStackView { get; set; }

		[Outlet]
		AnalysisITC.ExperimentDesignerGraphView SimGraphView { get; set; }

		[Outlet]
		AppKit.NSButton SimulateNoiseControl { get; set; }

		[Outlet]
		AppKit.NSButton SmallInitialInjControl { get; set; }

		[Outlet]
		AppKit.NSTextField SyringeConcErrorField { get; set; }

		[Outlet]
		AppKit.NSTextField SyringeConcField { get; set; }

		[Action ("ApplyModelSettings:")]
		partial void ApplyModelSettings (AppKit.NSButton sender);

		[Action ("InjectionInputChanged:")]
		partial void InjectionInputChanged (Foundation.NSObject sender);

		[Action ("InstrumentControlAction:")]
		partial void InstrumentControlAction (AppKit.NSPopUpButton sender);

		[Action ("ModelControlAction:")]
		partial void ModelControlAction (AppKit.NSPopUpButton sender);

		[Action ("SimulateNoiseControlAction:")]
		partial void SimulateNoiseControlAction (Foundation.NSObject sender);

		[Action ("SyringeCellAction:")]
		partial void SyringeCellAction (Foundation.NSObject sender);
		
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

			if (InjectionCoundStepper != null) {
				InjectionCoundStepper.Dispose ();
				InjectionCoundStepper = null;
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

			if (InstrumentDescriptionField != null) {
				InstrumentDescriptionField.Dispose ();
				InstrumentDescriptionField = null;
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

			if (SimGraphView != null) {
				SimGraphView.Dispose ();
				SimGraphView = null;
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

			if (SimulateNoiseControl != null) {
				SimulateNoiseControl.Dispose ();
				SimulateNoiseControl = null;
			}

			if (ApplyModelButton != null) {
				ApplyModelButton.Dispose ();
				ApplyModelButton = null;
			}
		}
	}
}
