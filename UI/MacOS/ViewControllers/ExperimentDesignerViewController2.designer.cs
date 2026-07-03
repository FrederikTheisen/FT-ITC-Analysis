// WARNING
//
// This file has been generated automatically by Visual Studio to store outlets and
// actions made in the UI designer. If it is removed, they will be lost.
// Manual changes to this file may not be handled correctly.
//
using Foundation;
using System.CodeDom.Compiler;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC
{
	[Register ("ExperimentDesignerViewController2")]
	partial class ExperimentDesignerViewController2
	{
		[Outlet]
		AppKit.NSButton ApplyModelButton { get; set; }

		[Outlet]
		AppKit.NSButton AutoVolume { get; set; }

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
		AppKit.NSTextField InjectionVolumeField { get; set; }

		[Outlet]
		AppKit.NSStepper InjectionVolumeStepper { get; set; }

		[Outlet]
		AppKit.NSTextField InstrumentDescriptionField { get; set; }

		[Outlet]
		AppKit.NSPopUpButton InstrumentControl { get; set; }

		[Outlet]
		AppKit.NSMenu InstrumentMenu { get; set; }

		[Outlet]
		AppKit.NSPopUpButton ModelControl { get; set; }

		[Outlet]
		AppKit.NSMenu ModelMenu { get; set; }

		[Outlet]
		AppKit.NSTextField ModelOptionsLabel { get; set; }

		[Outlet]
		AppKit.NSBox ModelOptionsLine { get; set; }

		[Outlet]
		AppKit.NSStackView ModelOptionsStackView { get; set; }

		[Outlet]
		AppKit.NSStackView ParameterStackView { get; set; }

		[Outlet]
		AnalysisITC.ExperimentDesignerGraphView SimGraphView { get; set; }

		[Outlet]
		AppKit.NSButton SimulateNoiseControl { get; set; }

		[Outlet]
		AppKit.NSButton SmallInitialInjCheckmark { get; set; }

		[Outlet]
		AppKit.NSButton SmallInitialInjControl { get; set; }

		[Outlet]
		AppKit.NSTextField SyringeConcErrorField { get; set; }

		[Outlet]
		AppKit.NSTextField SyringeConcField { get; set; }

		[Outlet]
		AppKit.NSButton TandemExperimentControl { get; set; }

		[Outlet]
		AppKit.NSTextField TandemSegmentCountField { get; set; }

		[Outlet]
		AppKit.NSTextField TandemSegmentCountLabel { get; set; }

		[Outlet]
		AppKit.NSStepper TandemSegmentCountStepper { get; set; }

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

		[Action ("SmallInitialInjCheckAction:")]
		partial void SmallInitialInjCheckAction (AppKit.NSButton sender);

		[Action ("SyringeCellAction:")]
		partial void SyringeCellAction (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (ApplyModelButton != null) {
				ApplyModelButton.Dispose ();
				ApplyModelButton = null;
			}

			if (AutoVolume != null) {
				AutoVolume.Dispose ();
				AutoVolume = null;
			}

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

			if (InjectionVolumeField != null) {
				InjectionVolumeField.Dispose ();
				InjectionVolumeField = null;
			}

			if (InjectionVolumeStepper != null) {
				InjectionVolumeStepper.Dispose ();
				InjectionVolumeStepper = null;
			}

			if (InstrumentDescriptionField != null) {
				InstrumentDescriptionField.Dispose ();
				InstrumentDescriptionField = null;
			}

			if (InstrumentControl != null) {
				InstrumentControl.Dispose ();
				InstrumentControl = null;
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

			if (ModelOptionsLabel != null) {
				ModelOptionsLabel.Dispose ();
				ModelOptionsLabel = null;
			}

			if (ModelOptionsLine != null) {
				ModelOptionsLine.Dispose ();
				ModelOptionsLine = null;
			}

			if (ModelOptionsStackView != null) {
				ModelOptionsStackView.Dispose ();
				ModelOptionsStackView = null;
			}

			if (ParameterStackView != null) {
				ParameterStackView.Dispose ();
				ParameterStackView = null;
			}

			if (SimGraphView != null) {
				SimGraphView.Dispose ();
				SimGraphView = null;
			}

			if (SimulateNoiseControl != null) {
				SimulateNoiseControl.Dispose ();
				SimulateNoiseControl = null;
			}

			if (SmallInitialInjCheckmark != null) {
				SmallInitialInjCheckmark.Dispose ();
				SmallInitialInjCheckmark = null;
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

			if (TandemExperimentControl != null) {
				TandemExperimentControl.Dispose ();
				TandemExperimentControl = null;
			}

			if (TandemSegmentCountField != null) {
				TandemSegmentCountField.Dispose ();
				TandemSegmentCountField = null;
			}

			if (TandemSegmentCountLabel != null) {
				TandemSegmentCountLabel.Dispose ();
				TandemSegmentCountLabel = null;
			}

			if (TandemSegmentCountStepper != null) {
				TandemSegmentCountStepper.Dispose ();
				TandemSegmentCountStepper = null;
			}
		}
	}
}
