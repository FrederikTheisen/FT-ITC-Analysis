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
	[Register ("AnalysisGlobalModeOptionsView")]
	partial class AnalysisGlobalModeOptionsView
	{
		[Outlet]
		AppKit.NSSegmentedControl Control { get; set; }

		[Outlet]
		AppKit.NSTextField Label { get; set; }

		[Action ("ControlClicked:")]
		partial void ControlClicked (AppKit.NSSegmentedControl sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (Control != null) {
				Control.Dispose ();
				Control = null;
			}

			if (Label != null) {
				Label.Dispose ();
				Label = null;
			}
		}
	}
}
