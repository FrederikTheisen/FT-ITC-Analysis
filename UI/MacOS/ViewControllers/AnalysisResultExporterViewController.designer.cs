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
	partial class AnalysisResultExporterViewController
	{
		[Outlet]
		AppKit.NSSegmentedControl ErrorTypeControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ExportFormatControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ExportTypeControl { get; set; }

		[Outlet]
		AnalysisITC.ToggleSelectTableView ListView { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl UncertaintyStyleControl { get; set; }

		[Action ("CopyToClipboard:")]
		partial void CopyToClipboard (Foundation.NSObject sender);

		[Action ("ExportToFile:")]
		partial void ExportToFile (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (ExportTypeControl != null) {
				ExportTypeControl.Dispose ();
				ExportTypeControl = null;
			}

			if (ErrorTypeControl != null) {
				ErrorTypeControl.Dispose ();
				ErrorTypeControl = null;
			}

			if (ExportFormatControl != null) {
				ExportFormatControl.Dispose ();
				ExportFormatControl = null;
			}

			if (ListView != null) {
				ListView.Dispose ();
				ListView = null;
			}

			if (UncertaintyStyleControl != null) {
				UncertaintyStyleControl.Dispose ();
				UncertaintyStyleControl = null;
			}
		}
	}
}
