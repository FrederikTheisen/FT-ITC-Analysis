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
	[Register ("BindingAnalysisViewController")]
	partial class BindingAnalysisViewController
	{
		[Outlet]
		AppKit.NSTableView ResultsTableView { get; set; }

		[Action ("CloseButtonClicked:")]
		partial void CloseButtonClicked (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (ResultsTableView != null) {
				ResultsTableView.Dispose ();
				ResultsTableView = null;
			}
		}
	}
}
