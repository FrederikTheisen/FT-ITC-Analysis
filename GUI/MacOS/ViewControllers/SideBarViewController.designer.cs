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
	[Register ("SideBarViewController")]
	partial class SideBarViewController
	{
		[Outlet]
		AppKit.NSImageCell IncludedImage { get; set; }

		[Outlet]
		AppKit.NSImageCell NotIncludedImage { get; set; }

		[Outlet]
		AppKit.NSImageCell NotProcessedImage { get; set; }

		[Outlet]
		AppKit.NSImageCell NSPlayFillImage { get; set; }

		[Outlet]
		AppKit.NSImageCell NSPlayImage { get; set; }

		[Outlet]
		AppKit.NSImageCell NSPlaySlashedFIllImage { get; set; }

		[Outlet]
		AppKit.NSTableView TableView { get; set; }
		
		void ReleaseDesignerOutlets ()
		{
			if (NSPlayFillImage != null) {
				NSPlayFillImage.Dispose ();
				NSPlayFillImage = null;
			}

			if (NSPlayImage != null) {
				NSPlayImage.Dispose ();
				NSPlayImage = null;
			}

			if (NSPlaySlashedFIllImage != null) {
				NSPlaySlashedFIllImage.Dispose ();
				NSPlaySlashedFIllImage = null;
			}

			if (TableView != null) {
				TableView.Dispose ();
				TableView = null;
			}

			if (NotProcessedImage != null) {
				NotProcessedImage.Dispose ();
				NotProcessedImage = null;
			}

			if (NotIncludedImage != null) {
				NotIncludedImage.Dispose ();
				NotIncludedImage = null;
			}

			if (IncludedImage != null) {
				IncludedImage.Dispose ();
				IncludedImage = null;
			}
		}
	}
}
