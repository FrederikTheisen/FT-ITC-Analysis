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
	[Register ("MainTabViewController")]
	partial class MainTabViewController
	{
		[Outlet]
		AppKit.NSView TabControllerView { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl TabviewSegControl { get; set; }

		[Outlet]
		AppKit.NSButton TCVAnalysisControl { get; set; }

		[Outlet]
		AppKit.NSButton TCVDataTabControl { get; set; }

		[Outlet]
		AppKit.NSButton TCVFigureControl { get; set; }

		[Outlet]
		AppKit.NSButton TCVProcessControl { get; set; }

		[Action ("SegControlClicked:")]
		partial void SegControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("TCVAnalysisClick:")]
		partial void TCVAnalysisClick (AppKit.NSButton sender);

		[Action ("TCVDataClick:")]
		partial void TCVDataClick (AppKit.NSButton sender);

		[Action ("TCVFigureClick:")]
		partial void TCVFigureClick (AppKit.NSButton sender);

		[Action ("TCVProcessClick:")]
		partial void TCVProcessClick (AppKit.NSButton sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (TabviewSegControl != null) {
				TabviewSegControl.Dispose ();
				TabviewSegControl = null;
			}

			if (TCVAnalysisControl != null) {
				TCVAnalysisControl.Dispose ();
				TCVAnalysisControl = null;
			}

			if (TCVProcessControl != null) {
				TCVProcessControl.Dispose ();
				TCVProcessControl = null;
			}

			if (TCVFigureControl != null) {
				TCVFigureControl.Dispose ();
				TCVFigureControl = null;
			}

			if (TabControllerView != null) {
				TabControllerView.Dispose ();
				TabControllerView = null;
			}

			if (TCVDataTabControl != null) {
				TCVDataTabControl.Dispose ();
				TCVDataTabControl = null;
			}
		}
	}
}
