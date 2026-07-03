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
	[Register ("ExportAccessoryViewController")]
	partial class ExportAccessoryViewController
	{
		[Outlet]
		AppKit.NSButton ExportBaselineCorrect { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ExportSelectionControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ExportTypeControl { get; set; }

		[Action ("ExportBaselineCorrectAction:")]
		partial void ExportBaselineCorrectAction (AppKit.NSButton sender);

		[Action ("ExportSelectionControlAction:")]
		partial void ExportSelectionControlAction (AppKit.NSSegmentedControl sender);

		[Action ("IncludedFittedPeaksControlAction:")]
		partial void IncludedFittedPeaksControlAction (AppKit.NSButton sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (ExportBaselineCorrect != null) {
				ExportBaselineCorrect.Dispose ();
				ExportBaselineCorrect = null;
			}

			if (ExportTypeControl != null) {
				ExportTypeControl.Dispose ();
				ExportTypeControl = null;
			}

			if (ExportSelectionControl != null) {
				ExportSelectionControl.Dispose ();
				ExportSelectionControl = null;
			}
		}
	}
}
