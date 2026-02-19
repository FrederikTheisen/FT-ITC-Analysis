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
	[Register ("BufferSubtractionTool")]
	partial class BufferSubtractionTool
	{
		[Outlet]
		AppKit.NSScrollView ExperimentListView { get; set; }

		[Outlet]
		AppKit.NSTextField ReferenceExperimentInfoLabel { get; set; }

		[Outlet]
		AppKit.NSPopUpButton ReferenceExperimentSelection { get; set; }

		[Outlet]
		AppKit.NSTableView SelectListView { get; set; }

		[Action ("Apply:")]
		partial void Apply (Foundation.NSObject sender);

		[Action ("ReferenceSelectionChanged:")]
		partial void ReferenceSelectionChanged (AppKit.NSPopUpButton sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (ExperimentListView != null) {
				ExperimentListView.Dispose ();
				ExperimentListView = null;
			}

			if (SelectListView != null) {
				SelectListView.Dispose ();
				SelectListView = null;
			}

			if (ReferenceExperimentSelection != null) {
				ReferenceExperimentSelection.Dispose ();
				ReferenceExperimentSelection = null;
			}

			if (ReferenceExperimentInfoLabel != null) {
				ReferenceExperimentInfoLabel.Dispose ();
				ReferenceExperimentInfoLabel = null;
			}
		}
	}
}
