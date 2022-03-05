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
	[Register ("ExperimentDataViewCell")]
	partial class ExperimentDataViewCell
	{
		[Outlet]
		AppKit.NSBox Box { get; set; }

		[Outlet]
		AppKit.NSTextField ExpNameLabel { get; set; }

		[Outlet]
		AppKit.NSTextField Line2 { get; set; }

		[Outlet]
		AppKit.NSTextField Line3 { get; set; }

		[Outlet]
		AppKit.NSTextField Line4 { get; set; }

		[Action ("RemoveClick:")]
		partial void RemoveClick (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (Box != null) {
				Box.Dispose ();
				Box = null;
			}

			if (ExpNameLabel != null) {
				ExpNameLabel.Dispose ();
				ExpNameLabel = null;
			}

			if (Line2 != null) {
				Line2.Dispose ();
				Line2 = null;
			}

			if (Line3 != null) {
				Line3.Dispose ();
				Line3 = null;
			}

			if (Line4 != null) {
				Line4.Dispose ();
				Line4 = null;
			}
		}
	}
}
