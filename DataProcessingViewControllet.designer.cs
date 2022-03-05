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
	[Register ("DataProcessingViewControllet")]
	partial class DataProcessingViewControllet
	{
		[Outlet]
		AppKit.NSSegmentedControl InterpolatorTypeControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl SplineAlgoControl { get; set; }

		[Outlet]
		AppKit.NSStackView SplineAlgorithmView { get; set; }

		[Outlet]
		AppKit.NSTextField SplineBaselineFractionControl { get; set; }

		[Outlet]
		AppKit.NSStackView SplineBaselineFractionView { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl SplineHandleModeControl { get; set; }

		[Outlet]
		AppKit.NSStackView SplineHandleModeView { get; set; }

		[Action ("InterplolatorClicked:")]
		partial void InterplolatorClicked (AppKit.NSSegmentedControl sender);

		[Action ("SplineAlgoClicked:")]
		partial void SplineAlgoClicked (AppKit.NSSegmentedControl sender);

		[Action ("SplineBaselineFractionChanged:")]
		partial void SplineBaselineFractionChanged (AppKit.NSTextField sender);

		[Action ("SplineBaselineFractionSliderChanged:")]
		partial void SplineBaselineFractionSliderChanged (AppKit.NSSlider sender);

		[Action ("SplineHandleClicked:")]
		partial void SplineHandleClicked (AppKit.NSSegmentedControl sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (SplineAlgorithmView != null) {
				SplineAlgorithmView.Dispose ();
				SplineAlgorithmView = null;
			}

			if (SplineHandleModeView != null) {
				SplineHandleModeView.Dispose ();
				SplineHandleModeView = null;
			}

			if (SplineBaselineFractionView != null) {
				SplineBaselineFractionView.Dispose ();
				SplineBaselineFractionView = null;
			}

			if (InterpolatorTypeControl != null) {
				InterpolatorTypeControl.Dispose ();
				InterpolatorTypeControl = null;
			}

			if (SplineAlgoControl != null) {
				SplineAlgoControl.Dispose ();
				SplineAlgoControl = null;
			}

			if (SplineHandleModeControl != null) {
				SplineHandleModeControl.Dispose ();
				SplineHandleModeControl = null;
			}

			if (SplineBaselineFractionControl != null) {
				SplineBaselineFractionControl.Dispose ();
				SplineBaselineFractionControl = null;
			}
		}
	}
}
