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
		AppKit.NSButton ApplyButton { get; set; }

		[Outlet]
		AppKit.NSScrollView ExperimentListView { get; set; }

		[Outlet]
		AppKit.NSButton FocusBufferYAxisControl { get; set; }

		[Outlet]
		AnalysisITC.BufferSubtractionGraphView GraphView { get; set; }

		[Outlet]
		AppKit.NSTextField ReferenceExperimentInfoLabel { get; set; }

		[Outlet]
		AppKit.NSPopUpButton ReferenceExperimentSelection { get; set; }

		[Outlet]
		AppKit.NSTableView SelectListView { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl SubtractionMethodControl { get; set; }

		[Action ("Apply:")]
		partial void Apply (Foundation.NSObject sender);

		[Action ("FocusYAxisChanged:")]
		partial void FocusYAxisChanged (AppKit.NSButton sender);

		[Action ("MethodSelectionChanged:")]
		partial void MethodSelectionChanged (AppKit.NSSegmentedControl sender);

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

			if (FocusBufferYAxisControl != null) {
				FocusBufferYAxisControl.Dispose ();
				FocusBufferYAxisControl = null;
			}

			if (GraphView != null) {
				GraphView.Dispose ();
				GraphView = null;
			}

			if (ReferenceExperimentSelection != null) {
				ReferenceExperimentSelection.Dispose ();
				ReferenceExperimentSelection = null;
			}

			if (ReferenceExperimentInfoLabel != null) {
				ReferenceExperimentInfoLabel.Dispose ();
				ReferenceExperimentInfoLabel = null;
			}

			if (ApplyButton != null) {
				ApplyButton.Dispose ();
				ApplyButton = null;
			}

			if (SubtractionMethodControl != null) {
				SubtractionMethodControl.Dispose ();
				SubtractionMethodControl = null;
			}
		}
	}
}
