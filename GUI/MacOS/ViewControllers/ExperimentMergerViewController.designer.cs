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
		AppKit.NSSlider BackMixingSliderControl { get; set; }

		[Outlet]
		AppKit.NSTextField DeadVolumeTextField { get; set; }

		[Outlet]
		AppKit.NSScrollView ExperimentListView { get; set; }

		[Outlet]
		AppKit.NSButton RemovedTitratedAfterExperimentControl { get; set; }

		[Action ("CreateNewMergedExperimentAction:")]
		partial void CreateNewMergedExperimentAction (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (DeadVolumeTextField != null) {
				DeadVolumeTextField.Dispose ();
				DeadVolumeTextField = null;
			}

			if (BackMixingSliderControl != null) {
				BackMixingSliderControl.Dispose ();
				BackMixingSliderControl = null;
			}

			if (RemovedTitratedAfterExperimentControl != null) {
				RemovedTitratedAfterExperimentControl.Dispose ();
				RemovedTitratedAfterExperimentControl = null;
			}

			if (ExperimentListView != null) {
				ExperimentListView.Dispose ();
				ExperimentListView = null;
			}
		}
	}
}
