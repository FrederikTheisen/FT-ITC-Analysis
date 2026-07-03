// WARNING
//
// This file has been generated automatically by Visual Studio to store outlets and
// actions made in the UI designer. If it is removed, they will be lost.
// Manual changes to this file may not be handled correctly.
//
using Foundation;
using System.CodeDom.Compiler;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

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

		[Action ("CopyToAllAction:")]
		partial void CopyToAllAction (Foundation.NSObject sender);

		[Action ("CopyToNonProcessed:")]
		partial void CopyToNonProcessed (Foundation.NSObject sender);

		[Action ("LockAction:")]
		partial void LockAction (Foundation.NSObject sender);

		[Action ("SplineAction:")]
		partial void SplineAction (Foundation.NSObject sender);

		[Action ("SplinePointsSliderChanged:")]
		partial void SplinePointsSliderChanged (AppKit.NSSlider sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (LockButton != null) {
				LockButton.Dispose ();
				LockButton = null;
			}

			if (SplinePointsSlider != null) {
				SplinePointsSlider.Dispose ();
				SplinePointsSlider = null;
			}

			if (ToSplineButton != null) {
				ToSplineButton.Dispose ();
				ToSplineButton = null;
			}
		}
	}
}
