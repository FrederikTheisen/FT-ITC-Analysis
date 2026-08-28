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
	[Register ("ExperimentMergerViewController")]
	partial class ExperimentMergerViewController
	{
		[Outlet]
		AppKit.NSTextField BackMixFracLabel { get; set; }

		[Outlet]
		AppKit.NSButton IndividualMixingControl { get; set; }

		[Outlet]
		AppKit.NSSlider BackMixingSliderControl { get; set; }

		[Outlet]
		AppKit.NSTextField BackMixLabel { get; set; }

		[Outlet]
		AppKit.NSTextField DeadVolLabel { get; set; }

		[Outlet]
		AppKit.NSTextField DeadVolumeTextField { get; set; }

		[Outlet]
		AppKit.NSScrollView ExperimentListView { get; set; }

		[Outlet]
		AppKit.NSButton MergeButtonControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl MergeMethodControl { get; set; }

		[Outlet]
		AppKit.NSTableView MergeTableView { get; set; }

		[Outlet]
		AppKit.NSButton RemovedTitratedAfterExperimentControl { get; set; }

		[Outlet]
		AppKit.NSTextField SecondBackMixFracLabel { get; set; }

		[Outlet]
		AppKit.NSTextField SecondBackMixLabel { get; set; }

		[Outlet]
		AppKit.NSSlider SecondBackMixingSliderControl { get; set; }

		[Outlet]
		AppKit.NSStackView SecondMixingRow { get; set; }

		[Outlet]
		AppKit.NSTextField ThirdBackMixFracLabel { get; set; }

		[Outlet]
		AppKit.NSTextField ThirdBackMixLabel { get; set; }

		[Outlet]
		AppKit.NSSlider ThirdBackMixingSliderControl { get; set; }

		[Outlet]
		AppKit.NSStackView ThirdMixingRow { get; set; }

		[Action ("CreateNewMergedExperimentAction:")]
		partial void CreateNewMergedExperimentAction (Foundation.NSObject sender);

		[Action ("IndividualMixingControlAction:")]
		partial void IndividualMixingControlAction (Foundation.NSObject sender);

		[Action ("MergeMethodControlAction:")]
		partial void MergeMethodControlAction (AppKit.NSSegmentedControl sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (IndividualMixingControl != null) {
				IndividualMixingControl.Dispose ();
				IndividualMixingControl = null;
			}

			if (BackMixLabel != null) {
				BackMixLabel.Dispose ();
				BackMixLabel = null;
			}

			if (DeadVolLabel != null) {
				DeadVolLabel.Dispose ();
				DeadVolLabel = null;
			}

			if (BackMixFracLabel != null) {
				BackMixFracLabel.Dispose ();
				BackMixFracLabel = null;
			}

			if (BackMixingSliderControl != null) {
				BackMixingSliderControl.Dispose ();
				BackMixingSliderControl = null;
			}

			if (DeadVolumeTextField != null) {
				DeadVolumeTextField.Dispose ();
				DeadVolumeTextField = null;
			}

			if (ExperimentListView != null) {
				ExperimentListView.Dispose ();
				ExperimentListView = null;
			}

			if (MergeButtonControl != null) {
				MergeButtonControl.Dispose ();
				MergeButtonControl = null;
			}

			if (MergeMethodControl != null) {
				MergeMethodControl.Dispose ();
				MergeMethodControl = null;
			}

			if (MergeTableView != null) {
				MergeTableView.Dispose ();
				MergeTableView = null;
			}

			if (RemovedTitratedAfterExperimentControl != null) {
				RemovedTitratedAfterExperimentControl.Dispose ();
				RemovedTitratedAfterExperimentControl = null;
			}

			if (SecondBackMixFracLabel != null) {
				SecondBackMixFracLabel.Dispose ();
				SecondBackMixFracLabel = null;
			}

			if (SecondBackMixLabel != null) {
				SecondBackMixLabel.Dispose ();
				SecondBackMixLabel = null;
			}

			if (SecondBackMixingSliderControl != null) {
				SecondBackMixingSliderControl.Dispose ();
				SecondBackMixingSliderControl = null;
			}

			if (SecondMixingRow != null) {
				SecondMixingRow.Dispose ();
				SecondMixingRow = null;
			}

			if (ThirdBackMixFracLabel != null) {
				ThirdBackMixFracLabel.Dispose ();
				ThirdBackMixFracLabel = null;
			}

			if (ThirdBackMixLabel != null) {
				ThirdBackMixLabel.Dispose ();
				ThirdBackMixLabel = null;
			}

			if (ThirdBackMixingSliderControl != null) {
				ThirdBackMixingSliderControl.Dispose ();
				ThirdBackMixingSliderControl = null;
			}

			if (ThirdMixingRow != null) {
				ThirdMixingRow.Dispose ();
				ThirdMixingRow = null;
			}
		}
	}
}
