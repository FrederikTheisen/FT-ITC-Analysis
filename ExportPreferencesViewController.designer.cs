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
	[Register ("ExportPreferencesViewController")]
	partial class ExportPreferencesViewController
	{
		[Outlet]
		AppKit.NSSegmentedControl ExportSelectedControl { get; set; }

		[Outlet]
		AppKit.NSButton ExportSolutionPointsControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl UnifyAxesControl { get; set; }

		[Action ("ExportSelectionControlAction:")]
		partial void ExportSelectionControlAction (AppKit.NSSegmentedControl sender);

		[Action ("ExportSolutionPointsControlAction:")]
		partial void ExportSolutionPointsControlAction (AppKit.NSButton sender);

		[Action ("UnifyAxesControlAcition:")]
		partial void UnifyAxesControlAcition (AppKit.NSSegmentedControl sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (UnifyAxesControl != null) {
				UnifyAxesControl.Dispose ();
				UnifyAxesControl = null;
			}

			if (ExportSolutionPointsControl != null) {
				ExportSolutionPointsControl.Dispose ();
				ExportSolutionPointsControl = null;
			}

			if (ExportSelectedControl != null) {
				ExportSelectedControl.Dispose ();
				ExportSelectedControl = null;
			}
		}
	}
}
