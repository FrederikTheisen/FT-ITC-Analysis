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
	[Register ("FinalFigureViewController")]
	partial class FinalFigureViewController
	{
		[Outlet]
		AppKit.NSView BoxView { get; set; }

		[Outlet]
		AnalysisITC.FinalFigureGraphView FinalFigureGraph { get; set; }

		[Action ("ExportGraphButtonClick:")]
		partial void ExportGraphButtonClick (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (FinalFigureGraph != null) {
				FinalFigureGraph.Dispose ();
				FinalFigureGraph = null;
			}

			if (BoxView != null) {
				BoxView.Dispose ();
				BoxView = null;
			}
		}
	}
}
