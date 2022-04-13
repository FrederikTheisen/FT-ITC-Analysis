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
		AppKit.NSSwitch ApplyToAllSwitch { get; set; }

		[Outlet]
		AnalysisITC.DataProcessingGraphView BaselineGraphView { get; set; }

		[Outlet]
		AppKit.NSButton ConfirmProcessingButton { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl DataZoomSegControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl DrawFeatureSegControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl InjectionViewSegControl { get; set; }

		[Outlet]
		AppKit.NSSlider IntegrationDelayControl { get; set; }

		[Outlet]
		AppKit.NSSlider IntegrationLengthControl { get; set; }

		[Outlet]
		AppKit.NSTextField IntegrationLengthLabel { get; set; }

		[Outlet]
		AppKit.NSTextField IntegrationStartDelayLabel { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl InterpolatorTypeControl { get; set; }

		[Outlet]
		AppKit.NSTextField PolynomialDegreeLabel { get; set; }

		[Outlet]
		AppKit.NSSlider PolynomialDegreeSlider { get; set; }

		[Outlet]
		AppKit.NSStackView PolynomialDegreeView { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl SplineAlgoControl { get; set; }

		[Outlet]
		AppKit.NSStackView SplineAlgorithmView { get; set; }

		[Outlet]
		AppKit.NSTextField SplineBaselineFractionControl { get; set; }

		[Outlet]
		AppKit.NSStackView SplineBaselineFractionView { get; set; }

		[Outlet]
		AppKit.NSSlider SplineFractionSliderControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl SplineHandleModeControl { get; set; }

		[Outlet]
		AppKit.NSStackView SplineHandleModeView { get; set; }

		[Outlet]
		AppKit.NSSwitch UseFactorSwitch { get; set; }

		[Outlet]
		AppKit.NSButton UseIntegrationFactorLengthControl { get; set; }

		[Outlet]
		AppKit.NSTextField ZLimitLabel { get; set; }

		[Outlet]
		AppKit.NSSlider ZLimitSlider { get; set; }

		[Outlet]
		AppKit.NSStackView ZLimitView { get; set; }

		[Action ("ApplyToAllSwitchToggled:")]
		partial void ApplyToAllSwitchToggled (AppKit.NSSwitch sender);

		[Action ("ConfirmProcessingButtonClicked:")]
		partial void ConfirmProcessingButtonClicked (Foundation.NSObject sender);

		[Action ("DrawFeatureControlClicked:")]
		partial void DrawFeatureControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("InjectionViewControlClicked:")]
		partial void InjectionViewControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("IntegrationLengthSliderChanged:")]
		partial void IntegrationLengthSliderChanged (AppKit.NSSlider sender);

		[Action ("IntegrationStartTimeSliderChanged:")]
		partial void IntegrationStartTimeSliderChanged (AppKit.NSSlider sender);

		[Action ("InterplolatorClicked:")]
		partial void InterplolatorClicked (AppKit.NSSegmentedControl sender);

		[Action ("PolynomialDegreeChanged:")]
		partial void PolynomialDegreeChanged (AppKit.NSSlider sender);

		[Action ("SplineAlgoClicked:")]
		partial void SplineAlgoClicked (AppKit.NSSegmentedControl sender);

		[Action ("SplineBaselineFractionSliderChanged:")]
		partial void SplineBaselineFractionSliderChanged (AppKit.NSSlider sender);

		[Action ("SplineHandleModeControlClicked:")]
		partial void SplineHandleModeControlClicked (AppKit.NSSegmentedControl sender);

		[Action ("ToggleUseIntegrationFactor:")]
		partial void ToggleUseIntegrationFactor (AppKit.NSButton sender);

		[Action ("UseFactorToggled:")]
		partial void UseFactorToggled (Foundation.NSObject sender);

		[Action ("ZLimitChanged:")]
		partial void ZLimitChanged (AppKit.NSSlider sender);

		[Action ("ZoomSegControlClicked:")]
		partial void ZoomSegControlClicked (AppKit.NSSegmentedControl sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (BaselineGraphView != null) {
				BaselineGraphView.Dispose ();
				BaselineGraphView = null;
			}

			if (ConfirmProcessingButton != null) {
				ConfirmProcessingButton.Dispose ();
				ConfirmProcessingButton = null;
			}

			if (DataZoomSegControl != null) {
				DataZoomSegControl.Dispose ();
				DataZoomSegControl = null;
			}

			if (DrawFeatureSegControl != null) {
				DrawFeatureSegControl.Dispose ();
				DrawFeatureSegControl = null;
			}

			if (InjectionViewSegControl != null) {
				InjectionViewSegControl.Dispose ();
				InjectionViewSegControl = null;
			}

			if (IntegrationDelayControl != null) {
				IntegrationDelayControl.Dispose ();
				IntegrationDelayControl = null;
			}

			if (IntegrationLengthControl != null) {
				IntegrationLengthControl.Dispose ();
				IntegrationLengthControl = null;
			}

			if (IntegrationLengthLabel != null) {
				IntegrationLengthLabel.Dispose ();
				IntegrationLengthLabel = null;
			}

			if (IntegrationStartDelayLabel != null) {
				IntegrationStartDelayLabel.Dispose ();
				IntegrationStartDelayLabel = null;
			}

			if (InterpolatorTypeControl != null) {
				InterpolatorTypeControl.Dispose ();
				InterpolatorTypeControl = null;
			}

			if (PolynomialDegreeLabel != null) {
				PolynomialDegreeLabel.Dispose ();
				PolynomialDegreeLabel = null;
			}

			if (PolynomialDegreeSlider != null) {
				PolynomialDegreeSlider.Dispose ();
				PolynomialDegreeSlider = null;
			}

			if (PolynomialDegreeView != null) {
				PolynomialDegreeView.Dispose ();
				PolynomialDegreeView = null;
			}

			if (SplineAlgoControl != null) {
				SplineAlgoControl.Dispose ();
				SplineAlgoControl = null;
			}

			if (SplineAlgorithmView != null) {
				SplineAlgorithmView.Dispose ();
				SplineAlgorithmView = null;
			}

			if (SplineBaselineFractionControl != null) {
				SplineBaselineFractionControl.Dispose ();
				SplineBaselineFractionControl = null;
			}

			if (SplineBaselineFractionView != null) {
				SplineBaselineFractionView.Dispose ();
				SplineBaselineFractionView = null;
			}

			if (SplineFractionSliderControl != null) {
				SplineFractionSliderControl.Dispose ();
				SplineFractionSliderControl = null;
			}

			if (SplineHandleModeControl != null) {
				SplineHandleModeControl.Dispose ();
				SplineHandleModeControl = null;
			}

			if (SplineHandleModeView != null) {
				SplineHandleModeView.Dispose ();
				SplineHandleModeView = null;
			}

			if (UseIntegrationFactorLengthControl != null) {
				UseIntegrationFactorLengthControl.Dispose ();
				UseIntegrationFactorLengthControl = null;
			}

			if (ZLimitLabel != null) {
				ZLimitLabel.Dispose ();
				ZLimitLabel = null;
			}

			if (ZLimitSlider != null) {
				ZLimitSlider.Dispose ();
				ZLimitSlider = null;
			}

			if (ZLimitView != null) {
				ZLimitView.Dispose ();
				ZLimitView = null;
			}

			if (ApplyToAllSwitch != null) {
				ApplyToAllSwitch.Dispose ();
				ApplyToAllSwitch = null;
			}

			if (UseFactorSwitch != null) {
				UseFactorSwitch.Dispose ();
				UseFactorSwitch = null;
			}
		}
	}
}
