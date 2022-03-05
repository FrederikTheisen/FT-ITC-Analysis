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
		AppKit.NSButton ClearDataButton { get; set; }

		[Outlet]
		AppKit.NSButton ContinueButton { get; set; }

		[Outlet]
		AnalysisITC.GraphView GVC { get; set; }

		[Outlet]
		AppKit.NSTextField Label { get; set; }

		[Outlet]
		AppKit.NSTextField Label2 { get; set; }

		[Outlet]
		AppKit.NSTextField Label3 { get; set; }

		[Action ("ButtonClick:")]
		partial void ButtonClick (AppKit.NSButton sender);

		[Action ("ClearButtonClick:")]
		partial void ClearButtonClick (Foundation.NSObject sender);

		[Action ("ContinueClick:")]
		partial void ContinueClick (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (GVC != null) {
				GVC.Dispose ();
				GVC = null;
			}

			if (Label != null) {
				Label.Dispose ();
				Label = null;
			}

			if (Label2 != null) {
				Label2.Dispose ();
				Label2 = null;
			}

			if (Label3 != null) {
				Label3.Dispose ();
				Label3 = null;
			}

			if (ContinueButton != null) {
				ContinueButton.Dispose ();
				ContinueButton = null;
			}

			if (ClearDataButton != null) {
				ClearDataButton.Dispose ();
				ClearDataButton = null;
			}
		}
	}
}
