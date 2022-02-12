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
	[Register ("ViewController")]
	partial class ViewController
	{
		[Outlet]
		AnalysisITC.GraphView GVC { get; set; }

		[Action ("ButtonClick:")]
		partial void ButtonClick (AppKit.NSButton sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (GVC != null) {
				GVC.Dispose ();
				GVC = null;
			}
		}
	}
}
