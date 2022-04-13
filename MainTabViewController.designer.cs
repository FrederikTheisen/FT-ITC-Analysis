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
		AppKit.NSSegmentedControl TabviewSegControl { get; set; }

		[Action ("SegControlClicked:")]
		partial void SegControlClicked (AppKit.NSSegmentedControl sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (TabviewSegControl != null) {
				TabviewSegControl.Dispose ();
				TabviewSegControl = null;
			}
		}
	}
}
