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
	[Register ("AboutAppViewController")]
	partial class AboutAppViewController
	{
		[Outlet]
		AppKit.NSButton VersionButton { get; set; }

		[Outlet]
		AppKit.NSTextField VersionLabel { get; set; }

		[Action ("VersionButtonAction:")]
		partial void VersionButtonAction (Foundation.NSObject sender);

		[Action ("VersionLabelClick:")]
		partial void VersionLabelClick (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (VersionLabel != null) {
				VersionLabel.Dispose ();
				VersionLabel = null;
			}

			if (VersionButton != null) {
				VersionButton.Dispose ();
				VersionButton = null;
			}
		}
	}
}
