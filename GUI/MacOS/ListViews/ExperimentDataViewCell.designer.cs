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
	[Register ("ExperimentDataViewCell")]
	partial class ExperimentDataViewCell
	{
		[Outlet]
		AppKit.NSTextField AffinityLine { get; set; }

		[Outlet]
		AppKit.NSBox Box { get; set; }

		[Outlet]
		AppKit.NSTextField EnthalpyLine { get; set; }

		[Outlet]
		AppKit.NSTextField EntropyLine { get; set; }

		[Outlet]
		AppKit.NSTextField ExpNameLabel { get; set; }

		[Outlet]
		AppKit.NSButton IncludeDataButton { get; set; }

		[Outlet]
		AppKit.NSTextField Line2 { get; set; }

		[Outlet]
		AppKit.NSTextField Line3 { get; set; }

		[Outlet]
		AppKit.NSTextField Line4 { get; set; }

		[Outlet]
		AppKit.NSTextField ModelFitLine { get; set; }

		[Outlet]
		AppKit.NSTextField NvalueLine { get; set; }

		[Outlet]
		AnalysisITC.NSMarginButton ShowFitDataButton { get; set; }

		[Action ("RemoveClick:")]
		partial void RemoveClick (Foundation.NSObject sender);

		[Action ("ShowFitDataButtonClick:")]
		partial void ShowFitDataButtonClick (Foundation.NSObject sender);

		[Action ("ToggleDataGlobalInclude:")]
		partial void ToggleDataGlobalInclude (AppKit.NSButton sender);

		[Action ("ViewDetails:")]
		partial void ViewDetails (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (AffinityLine != null) {
				AffinityLine.Dispose ();
				AffinityLine = null;
			}

			if (Box != null) {
				Box.Dispose ();
				Box = null;
			}

			if (EnthalpyLine != null) {
				EnthalpyLine.Dispose ();
				EnthalpyLine = null;
			}

			if (EntropyLine != null) {
				EntropyLine.Dispose ();
				EntropyLine = null;
			}

			if (ExpNameLabel != null) {
				ExpNameLabel.Dispose ();
				ExpNameLabel = null;
			}

			if (IncludeDataButton != null) {
				IncludeDataButton.Dispose ();
				IncludeDataButton = null;
			}

			if (Line2 != null) {
				Line2.Dispose ();
				Line2 = null;
			}

			if (Line3 != null) {
				Line3.Dispose ();
				Line3 = null;
			}

			if (Line4 != null) {
				Line4.Dispose ();
				Line4 = null;
			}

			if (ModelFitLine != null) {
				ModelFitLine.Dispose ();
				ModelFitLine = null;
			}

			if (NvalueLine != null) {
				NvalueLine.Dispose ();
				NvalueLine = null;
			}

			if (ShowFitDataButton != null) {
				ShowFitDataButton.Dispose ();
				ShowFitDataButton = null;
			}
		}
	}
}
