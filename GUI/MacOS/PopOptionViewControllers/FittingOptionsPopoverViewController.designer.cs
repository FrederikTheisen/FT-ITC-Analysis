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
		AppKit.NSSegmentedControl IncludeConcErrorControl { get; set; }

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
		AppKit.NSTextField InitialValuesHeader { get; set; }

		[Outlet]
		AppKit.NSBox InitialValuesLine { get; set; }

		[Outlet]
		AppKit.NSTextField InitN { get; set; }

		[Outlet]
		AppKit.NSButton InitNLock { get; set; }

		[Outlet]
		AppKit.NSTextField InitOffset { get; set; }

		[Outlet]
		AppKit.NSButton InitOffsetLock { get; set; }

		[Outlet]
		AppKit.NSTextField ModelOptionsHeader { get; set; }

		[Outlet]
		AppKit.NSBox ModelOptionsLine { get; set; }

		[Outlet]
		AppKit.NSTextField NStep { get; set; }

		[Outlet]
		AppKit.NSTextField OStep { get; set; }

		[Outlet]
		AppKit.NSStackView StackView { get; set; }

		[Action ("ApplyOptions:")]
		partial void ApplyOptions (Foundation.NSObject sender);

		[Action ("ErrorIterationSliderChanged:")]
		partial void ErrorIterationSliderChanged (AppKit.NSSlider sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (IncludeConcErrorControl != null) {
				IncludeConcErrorControl.Dispose ();
				IncludeConcErrorControl = null;
			}

			if (CStep != null) {
				CStep.Dispose ();
				CStep = null;
			}

			if (ErrorIterationLabel != null) {
				ErrorIterationLabel.Dispose ();
				ErrorIterationLabel = null;
			}

			if (ErrorIterationsControl != null) {
				ErrorIterationsControl.Dispose ();
				ErrorIterationsControl = null;
			}

			if (ErrorMethodControl != null) {
				ErrorMethodControl.Dispose ();
				ErrorMethodControl = null;
			}

			if (GStep != null) {
				GStep.Dispose ();
				GStep = null;
			}

			if (HStep != null) {
				HStep.Dispose ();
				HStep = null;
			}

			if (InitCp != null) {
				InitCp.Dispose ();
				InitCp = null;
			}

			if (InitCpLock != null) {
				InitCpLock.Dispose ();
				InitCpLock = null;
			}

			if (InitG != null) {
				InitG.Dispose ();
				InitG = null;
			}

			if (InitGLock != null) {
				InitGLock.Dispose ();
				InitGLock = null;
			}

			if (InitH != null) {
				InitH.Dispose ();
				InitH = null;
			}

			if (InitHLock != null) {
				InitHLock.Dispose ();
				InitHLock = null;
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

			if (NStep != null) {
				NStep.Dispose ();
				NStep = null;
			}

			if (OStep != null) {
				OStep.Dispose ();
				OStep = null;
			}

			if (StackView != null) {
				StackView.Dispose ();
				StackView = null;
			}

			if (ModelOptionsHeader != null) {
				ModelOptionsHeader.Dispose ();
				ModelOptionsHeader = null;
			}

			if (ModelOptionsLine != null) {
				ModelOptionsLine.Dispose ();
				ModelOptionsLine = null;
			}

			if (InitialValuesHeader != null) {
				InitialValuesHeader.Dispose ();
				InitialValuesHeader = null;
			}

			if (InitialValuesLine != null) {
				InitialValuesLine.Dispose ();
				InitialValuesLine = null;
			}
		}
	}
}
