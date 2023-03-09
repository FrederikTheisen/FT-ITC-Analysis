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
		AppKit.NSTextField FinalFigHeightField { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl FinalFigParameterDisplayOptions { get; set; }

		[Outlet]
		AppKit.NSTextField FinalFigWidthField { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl UnifyAxesControl { get; set; }

		[Action ("Apply:")]
		partial void Apply (Foundation.NSObject sender);

		[Action ("Close:")]
		partial void Close (Foundation.NSObject sender);

		[Action ("ExportSelectionControlAction:")]
		partial void ExportSelectionControlAction (AppKit.NSSegmentedControl sender);

		[Action ("ExportSolutionPointsControlAction:")]
		partial void ExportSolutionPointsControlAction (AppKit.NSButton sender);

		[Action ("UnifyAxesControlAcition:")]
		partial void UnifyAxesControlAcition (AppKit.NSSegmentedControl sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (FinalFigHeightField != null) {
				FinalFigHeightField.Dispose ();
				FinalFigHeightField = null;
			}

			if (FinalFigWidthField != null) {
				FinalFigWidthField.Dispose ();
				FinalFigWidthField = null;
			}

			if (ExportSelectedControl != null) {
				ExportSelectedControl.Dispose ();
				ExportSelectedControl = null;
			}

			if (ExportSolutionPointsControl != null) {
				ExportSolutionPointsControl.Dispose ();
				ExportSolutionPointsControl = null;
			}

			if (UnifyAxesControl != null) {
				UnifyAxesControl.Dispose ();
				UnifyAxesControl = null;
			}

			if (FinalFigParameterDisplayOptions != null) {
				FinalFigParameterDisplayOptions.Dispose ();
				FinalFigParameterDisplayOptions = null;
			}
		}
	}
}
