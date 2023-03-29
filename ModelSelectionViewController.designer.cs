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
	[Register ("ModelSelectionViewController")]
	partial class ModelSelectionViewController
	{
		[Outlet]
		AppKit.NSButton CompetitiveBinding { get; set; }

		[Outlet]
		AppKit.NSButton Dissociation { get; set; }

		[Outlet]
		AppKit.NSButton OneSites { get; set; }

		[Outlet]
		AppKit.NSButton ProlineIsomerBinding { get; set; }

		[Outlet]
		AppKit.NSButton SeqBinding { get; set; }

		[Outlet]
		AppKit.NSStackView StackView { get; set; }

		[Outlet]
		AppKit.NSButton TwoSites { get; set; }

		[Action ("BtnAction:")]
		partial void BtnAction (AppKit.NSButton sender);

		[Action ("CompBindAction:")]
		partial void CompBindAction (Foundation.NSObject sender);

		[Action ("DissocAction:")]
		partial void DissocAction (Foundation.NSObject sender);

		[Action ("OneSitesAction:")]
		partial void OneSitesAction (Foundation.NSObject sender);

		[Action ("ProIsoAction:")]
		partial void ProIsoAction (Foundation.NSObject sender);

		[Action ("SeqBindAction:")]
		partial void SeqBindAction (Foundation.NSObject sender);

		[Action ("TwoSitesAction:")]
		partial void TwoSitesAction (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (CompetitiveBinding != null) {
				CompetitiveBinding.Dispose ();
				CompetitiveBinding = null;
			}

			if (Dissociation != null) {
				Dissociation.Dispose ();
				Dissociation = null;
			}

			if (OneSites != null) {
				OneSites.Dispose ();
				OneSites = null;
			}

			if (ProlineIsomerBinding != null) {
				ProlineIsomerBinding.Dispose ();
				ProlineIsomerBinding = null;
			}

			if (SeqBinding != null) {
				SeqBinding.Dispose ();
				SeqBinding = null;
			}

			if (TwoSites != null) {
				TwoSites.Dispose ();
				TwoSites = null;
			}

			if (StackView != null) {
				StackView.Dispose ();
				StackView = null;
			}
		}
	}
}
