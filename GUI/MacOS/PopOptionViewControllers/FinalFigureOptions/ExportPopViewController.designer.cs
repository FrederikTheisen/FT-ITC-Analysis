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
	[Register ("ExportPopViewController")]
	partial class ExportPopViewController
	{
		[Outlet]
		AppKit.NSButton ExportAllCheckBox { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ExportSelectionControl { get; set; }

		[Action ("Export:")]
		partial void Export (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (ExportAllCheckBox != null) {
				ExportAllCheckBox.Dispose ();
				ExportAllCheckBox = null;
			}

			if (ExportSelectionControl != null) {
				ExportSelectionControl.Dispose ();
				ExportSelectionControl = null;
			}
		}
	}
}
