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
	[Register ("FittingOptionsPopoverViewController")]
	partial class FittingOptionsPopoverViewController
	{
		[Outlet]
		AppKit.NSTextField CStep { get; set; }

		[Outlet]
		AppKit.NSTextField ErrorIterationLabel { get; set; }

		[Outlet]
		AppKit.NSSlider ErrorIterationsControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ErrorMethodControl { get; set; }

		[Outlet]
		AppKit.NSTextField GStep { get; set; }

		[Outlet]
		AppKit.NSTextField HStep { get; set; }

		[Outlet]
		AppKit.NSTextField InitCp { get; set; }

		[Outlet]
		AppKit.NSButton InitCpLock { get; set; }

		[Outlet]
		AppKit.NSTextField InitG { get; set; }

		[Outlet]
		AppKit.NSButton InitGLock { get; set; }

		[Outlet]
		AppKit.NSTextField InitH { get; set; }

		[Outlet]
		AppKit.NSButton InitHLock { get; set; }

		[Outlet]
		AppKit.NSTextField InitN { get; set; }

		[Outlet]
		AppKit.NSButton InitNLock { get; set; }

		[Outlet]
		AppKit.NSTextField InitOffset { get; set; }

		[Outlet]
		AppKit.NSButton InitOffsetLock { get; set; }

		[Outlet]
		AppKit.NSTextField NStep { get; set; }

		[Outlet]
		AppKit.NSTextField OStep { get; set; }

		[Action ("ApplyOptions:")]
		partial void ApplyOptions (Foundation.NSObject sender);

		[Action ("ErrorIterationSliderChanged:")]
		partial void ErrorIterationSliderChanged (AppKit.NSSlider sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (ErrorMethodControl != null) {
				ErrorMethodControl.Dispose ();
				ErrorMethodControl = null;
			}

			if (ErrorIterationsControl != null) {
				ErrorIterationsControl.Dispose ();
				ErrorIterationsControl = null;
			}

			if (InitH != null) {
				InitH.Dispose ();
				InitH = null;
			}

			if (InitHLock != null) {
				InitHLock.Dispose ();
				InitHLock = null;
			}

			if (InitG != null) {
				InitG.Dispose ();
				InitG = null;
			}

			if (InitGLock != null) {
				InitGLock.Dispose ();
				InitGLock = null;
			}

			if (InitCp != null) {
				InitCp.Dispose ();
				InitCp = null;
			}

			if (InitCpLock != null) {
				InitCpLock.Dispose ();
				InitCpLock = null;
			}

			if (InitN != null) {
				InitN.Dispose ();
				InitN = null;
			}

			if (InitNLock != null) {
				InitNLock.Dispose ();
				InitNLock = null;
			}

			if (InitOffset != null) {
				InitOffset.Dispose ();
				InitOffset = null;
			}

			if (InitOffsetLock != null) {
				InitOffsetLock.Dispose ();
				InitOffsetLock = null;
			}

			if (OStep != null) {
				OStep.Dispose ();
				OStep = null;
			}

			if (CStep != null) {
				CStep.Dispose ();
				CStep = null;
			}

			if (GStep != null) {
				GStep.Dispose ();
				GStep = null;
			}

			if (HStep != null) {
				HStep.Dispose ();
				HStep = null;
			}

			if (NStep != null) {
				NStep.Dispose ();
				NStep = null;
			}

			if (ErrorIterationLabel != null) {
				ErrorIterationLabel.Dispose ();
				ErrorIterationLabel = null;
			}
		}
	}
}
