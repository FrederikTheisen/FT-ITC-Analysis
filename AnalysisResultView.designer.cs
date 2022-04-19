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
	[Register ("AnalysisResultView")]
	partial class AnalysisResultView
	{
		[Outlet]
		AppKit.NSButton AnalysisResultIcon { get; set; }

		[Outlet]
		AppKit.NSTextField ResultContentLabel { get; set; }

		[Outlet]
		AppKit.NSTextField ResultTitleLabel { get; set; }

		[Action ("RemoveButtonClick:")]
		partial void RemoveButtonClick (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (AnalysisResultIcon != null) {
				AnalysisResultIcon.Dispose ();
				AnalysisResultIcon = null;
			}

			if (ResultTitleLabel != null) {
				ResultTitleLabel.Dispose ();
				ResultTitleLabel = null;
			}

			if (ResultContentLabel != null) {
				ResultContentLabel.Dispose ();
				ResultContentLabel = null;
			}
		}
	}
}
