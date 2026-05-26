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
	[Register ("MainWindowController")]
	partial class MainWindowController
	{
		[Outlet]
		AppKit.NSSegmentedControl AnalysisSegControl { get; set; }

		[Outlet]
		AppKit.NSButton ContextButton { get; set; }

		[Outlet]
		AppKit.NSPopUpButton ContextToolbarMenuButton { get; set; }

		[Outlet]
		AppKit.NSPopUpButton FileToolbarMenuButton { get; set; }

		[Outlet]
		AppKit.NSPopUpButton WorkflowToolbarMenuButton { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl DataLoadSegControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl NavigationArrowControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ProcessSegControl { get; set; }

		[Outlet]
		AppKit.NSTextField StatusbarPrimaryLabel { get; set; }

		[Outlet]
		AppKit.NSProgressIndicator StatusbarProgressIndicator { get; set; }

		[Outlet]
		AppKit.NSTextField StatusbarSecondaryLabel { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl SharedToolbarControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl StepControl { get; set; }

		[Outlet]
		AppKit.NSButton StopProcessButton { get; set; }

		[Action ("AnalysisSegControlClicked:")]
		partial void AnalysisSegControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("ContextButtonClick:")]
		partial void ContextButtonClick (Foundation.NSObject sender);

		[Action ("DataLoadSegControlClick:")]
		partial void DataLoadSegControlClick (AppKit.NSSegmentedControl sender);

		[Action ("NavigationArrowControlClicked:")]
		partial void NavigationArrowControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("ProcessSegControlClick:")]
		partial void ProcessSegControlClick (AppKit.NSSegmentedControl sender);

		[Action ("SharedToolbarControlClicked:")]
		partial void SharedToolbarControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("StepControlClick:")]
		partial void StepControlClick (AppKit.NSSegmentedControl sender);

		[Action ("StopButtonClick:")]
		partial void StopButtonClick (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (AnalysisSegControl != null) {
				AnalysisSegControl.Dispose ();
				AnalysisSegControl = null;
			}

			if (ContextButton != null) {
				ContextButton.Dispose ();
				ContextButton = null;
			}

			if (ContextToolbarMenuButton != null) {
				ContextToolbarMenuButton.Dispose ();
				ContextToolbarMenuButton = null;
			}

			if (FileToolbarMenuButton != null) {
				FileToolbarMenuButton.Dispose ();
				FileToolbarMenuButton = null;
			}

			if (WorkflowToolbarMenuButton != null) {
				WorkflowToolbarMenuButton.Dispose ();
				WorkflowToolbarMenuButton = null;
			}

			if (DataLoadSegControl != null) {
				DataLoadSegControl.Dispose ();
				DataLoadSegControl = null;
			}

			if (NavigationArrowControl != null) {
				NavigationArrowControl.Dispose ();
				NavigationArrowControl = null;
			}

			if (ProcessSegControl != null) {
				ProcessSegControl.Dispose ();
				ProcessSegControl = null;
			}

			if (StatusbarPrimaryLabel != null) {
				StatusbarPrimaryLabel.Dispose ();
				StatusbarPrimaryLabel = null;
			}

			if (StatusbarProgressIndicator != null) {
				StatusbarProgressIndicator.Dispose ();
				StatusbarProgressIndicator = null;
			}

			if (StatusbarSecondaryLabel != null) {
				StatusbarSecondaryLabel.Dispose ();
				StatusbarSecondaryLabel = null;
			}

			if (SharedToolbarControl != null) {
				SharedToolbarControl.Dispose ();
				SharedToolbarControl = null;
			}

			if (StepControl != null) {
				StepControl.Dispose ();
				StepControl = null;
			}

			if (StopProcessButton != null) {
				StopProcessButton.Dispose ();
				StopProcessButton = null;
			}
		}
	}
}
