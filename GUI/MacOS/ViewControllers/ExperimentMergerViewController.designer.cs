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
	[Register ("ExperimentMergerViewController")]
	partial class ExperimentMergerViewController
	{
		[Outlet]
		AppKit.NSTextField BackMixFracLabel { get; set; }

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
		AppKit.NSTableView MergeTableView { get; set; }

		[Outlet]
		AppKit.NSButton RemovedTitratedAfterExperimentControl { get; set; }

		[Action ("CreateNewMergedExperimentAction:")]
		partial void CreateNewMergedExperimentAction (Foundation.NSObject sender);

		[Action ("MergeMethodControlAction:")]
		partial void MergeMethodControlAction (AppKit.NSSegmentedControl sender);
		
		void ReleaseDesignerOutlets ()
		{
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

			if (MergeTableView != null) {
				MergeTableView.Dispose ();
				MergeTableView = null;
			}

			if (RemovedTitratedAfterExperimentControl != null) {
				RemovedTitratedAfterExperimentControl.Dispose ();
				RemovedTitratedAfterExperimentControl = null;
			}
		}
	}
}
