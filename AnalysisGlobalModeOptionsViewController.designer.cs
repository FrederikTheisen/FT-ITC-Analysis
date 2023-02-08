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
	[Register ("AnalysisGlobalModeOptionsViewController")]
	partial class AnalysisGlobalModeOptionsViewController
	{
		[Outlet]
		AppKit.NSTextField ParameterLabel { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ParameterOptionControl { get; set; }

		[Action ("ParameterOptionControlClicked:")]
		partial void ParameterOptionControlClicked (AppKit.NSSegmentedControl sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (ParameterLabel != null) {
				ParameterLabel.Dispose ();
				ParameterLabel = null;
			}

			if (ParameterOptionControl != null) {
				ParameterOptionControl.Dispose ();
				ParameterOptionControl = null;
			}
		}
	}
}
