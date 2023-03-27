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
	[Register ("BaselineOptionsPopoverViewController")]
	partial class BaselineOptionsPopoverViewController
	{
		[Outlet]
		AppKit.NSButton LockButton { get; set; }

		[Outlet]
		AppKit.NSSlider SplinePointsSlider { get; set; }

		[Outlet]
		AppKit.NSButton ToSplineButton { get; set; }

		[Action ("LockAction:")]
		partial void LockAction (Foundation.NSObject sender);

		[Action ("SplineAction:")]
		partial void SplineAction (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (LockButton != null) {
				LockButton.Dispose ();
				LockButton = null;
			}

			if (ToSplineButton != null) {
				ToSplineButton.Dispose ();
				ToSplineButton = null;
			}

			if (SplinePointsSlider != null) {
				SplinePointsSlider.Dispose ();
				SplinePointsSlider = null;
			}
		}
	}
}
