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
	[Register ("ExperimentMergerListViewCell")]
	partial class ExperimentMergerListViewCell
	{
		[Outlet]
		AppKit.NSButton MoveDownControl { get; set; }

		[Outlet]
		AppKit.NSButton MoveUpControl { get; set; }

		[Outlet]
		AppKit.NSTextField TitleLabel { get; set; }

		[Action ("MoveDownAction:")]
		partial void MoveDownAction (Foundation.NSObject sender);

		[Action ("MoveUpAction:")]
		partial void MoveUpAction (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (TitleLabel != null) {
				TitleLabel.Dispose ();
				TitleLabel = null;
			}

			if (MoveUpControl != null) {
				MoveUpControl.Dispose ();
				MoveUpControl = null;
			}

			if (MoveDownControl != null) {
				MoveDownControl.Dispose ();
				MoveDownControl = null;
			}
		}
	}
}
