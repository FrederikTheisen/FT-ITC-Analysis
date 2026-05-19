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
	[Register ("FinalFigureViewController")]
	partial class FinalFigureViewController
	{
		[Outlet]
		AppKit.NSView BoxView { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedAddGapToResidualPlot { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedBadDataErrorBars { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedConcentrationDetailControl { get; set; }

		[Outlet]
		AppKit.NSTextField EmbeddedDataXTickLabel { get; set; }

		[Outlet]
		AppKit.NSStepper EmbeddedDataXTickStepper { get; set; }

		[Outlet]
		AppKit.NSTextField EmbeddedDataYTickLabel { get; set; }

		[Outlet]
		AppKit.NSStepper EmbeddedDataYTickStepper { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedDrawBaseline { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedDrawConfidence { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedDrawCorrected { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedDrawErrorBars { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedDrawZeroLine { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl EmbeddedEnergyUnitControl { get; set; }

		[Outlet]
		AppKit.NSTextField EmbeddedEnthalpyAxisTitleLabel { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl EmbeddedExportSelectionControl { get; set; }

		[Outlet]
		AppKit.NSTextField EmbeddedFitXTickLabel { get; set; }

		[Outlet]
		AppKit.NSStepper EmbeddedFitXTickStepper { get; set; }

		[Outlet]
		AppKit.NSTextField EmbeddedFitYTickLabel { get; set; }

		[Outlet]
		AppKit.NSStepper EmbeddedFitYTickStepper { get; set; }

		[Outlet]
		AppKit.NSMenu EmbeddedAttributeDisplayOptionsControl { get; set; }

		[Outlet]
		AppKit.NSTextField EmbeddedHeightLabel { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl EmbeddedInformationBoxPositionControl { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedHideBadData { get; set; }

		[Outlet]
		AppKit.NSTextField EmbeddedMolarRatioAxisTitleLabel { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedModelInfoControl { get; set; }

		[Outlet]
		AppKit.NSMenu EmbeddedParameterDisplayOptionsControl { get; set; }

		[Outlet]
		AppKit.NSTextField EmbeddedPowerAxisTitleLabel { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedSanitizeTicks { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedShowDataGraphControl { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedShowParametersControl { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedShowResiduals { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl EmbeddedSplineInterpolationControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl EmbeddedSymbolControl { get; set; }

		[Outlet]
		AppKit.NSTextField EmbeddedSymbolSizeLabel { get; set; }

		[Outlet]
		AppKit.NSStepper EmbeddedSymbolSizeStepper { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedTemperatureDetailControl { get; set; }

		[Outlet]
		AppKit.NSTextField EmbeddedTimeAxisTitleLabel { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl EmbeddedTimeUnitControl { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedUnifiedHeatAxis { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedUnifiedMolarRatioAxis { get; set; }

		[Outlet]
		AppKit.NSSwitch EmbeddedUnifiedPowerAxis { get; set; }

		[Outlet]
		AppKit.NSTextField EmbeddedWidthLabel { get; set; }

		[Outlet]
		AnalysisITC.FinalFigureGraphView FinalFigureGraph { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl PublishOptionsTabControl { get; set; }

		[Outlet]
		AppKit.NSTabView PublishOptionsTabView { get; set; }

		[Action ("ControlChanged:")]
		partial void ControlChanged (Foundation.NSObject sender);

		[Action ("ControlClicked:")]
		partial void ControlClicked (Foundation.NSObject sender);

		[Action ("ExportGraphButtonClick:")]
		partial void ExportGraphButtonClick (Foundation.NSObject sender);

		[Action ("AttributeOptionAction:")]
		partial void AttributeOptionAction (Foundation.NSObject sender);

		[Action ("ParameterOptionAction:")]
		partial void ParameterOptionAction (Foundation.NSObject sender);

		[Action ("PublishOptionsTabControlChanged:")]
		partial void PublishOptionsTabControlChanged (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (BoxView != null) {
				BoxView.Dispose ();
				BoxView = null;
			}

			if (EmbeddedAddGapToResidualPlot != null) {
				EmbeddedAddGapToResidualPlot.Dispose ();
				EmbeddedAddGapToResidualPlot = null;
			}

			if (EmbeddedBadDataErrorBars != null) {
				EmbeddedBadDataErrorBars.Dispose ();
				EmbeddedBadDataErrorBars = null;
			}

			if (EmbeddedConcentrationDetailControl != null) {
				EmbeddedConcentrationDetailControl.Dispose ();
				EmbeddedConcentrationDetailControl = null;
			}

			if (EmbeddedDataXTickLabel != null) {
				EmbeddedDataXTickLabel.Dispose ();
				EmbeddedDataXTickLabel = null;
			}

			if (EmbeddedDataXTickStepper != null) {
				EmbeddedDataXTickStepper.Dispose ();
				EmbeddedDataXTickStepper = null;
			}

			if (EmbeddedDataYTickLabel != null) {
				EmbeddedDataYTickLabel.Dispose ();
				EmbeddedDataYTickLabel = null;
			}

			if (EmbeddedDataYTickStepper != null) {
				EmbeddedDataYTickStepper.Dispose ();
				EmbeddedDataYTickStepper = null;
			}

			if (EmbeddedDrawBaseline != null) {
				EmbeddedDrawBaseline.Dispose ();
				EmbeddedDrawBaseline = null;
			}

			if (EmbeddedDrawConfidence != null) {
				EmbeddedDrawConfidence.Dispose ();
				EmbeddedDrawConfidence = null;
			}

			if (EmbeddedDrawCorrected != null) {
				EmbeddedDrawCorrected.Dispose ();
				EmbeddedDrawCorrected = null;
			}

			if (EmbeddedDrawErrorBars != null) {
				EmbeddedDrawErrorBars.Dispose ();
				EmbeddedDrawErrorBars = null;
			}

			if (EmbeddedDrawZeroLine != null) {
				EmbeddedDrawZeroLine.Dispose ();
				EmbeddedDrawZeroLine = null;
			}

			if (EmbeddedEnergyUnitControl != null) {
				EmbeddedEnergyUnitControl.Dispose ();
				EmbeddedEnergyUnitControl = null;
			}

			if (EmbeddedEnthalpyAxisTitleLabel != null) {
				EmbeddedEnthalpyAxisTitleLabel.Dispose ();
				EmbeddedEnthalpyAxisTitleLabel = null;
			}

			if (EmbeddedExportSelectionControl != null) {
				EmbeddedExportSelectionControl.Dispose ();
				EmbeddedExportSelectionControl = null;
			}

			if (EmbeddedFitXTickLabel != null) {
				EmbeddedFitXTickLabel.Dispose ();
				EmbeddedFitXTickLabel = null;
			}

			if (EmbeddedFitXTickStepper != null) {
				EmbeddedFitXTickStepper.Dispose ();
				EmbeddedFitXTickStepper = null;
			}

			if (EmbeddedFitYTickLabel != null) {
				EmbeddedFitYTickLabel.Dispose ();
				EmbeddedFitYTickLabel = null;
			}

			if (EmbeddedFitYTickStepper != null) {
				EmbeddedFitYTickStepper.Dispose ();
				EmbeddedFitYTickStepper = null;
			}

			if (EmbeddedAttributeDisplayOptionsControl != null) {
				EmbeddedAttributeDisplayOptionsControl.Dispose ();
				EmbeddedAttributeDisplayOptionsControl = null;
			}

			if (EmbeddedHeightLabel != null) {
				EmbeddedHeightLabel.Dispose ();
				EmbeddedHeightLabel = null;
			}

			if (EmbeddedInformationBoxPositionControl != null) {
				EmbeddedInformationBoxPositionControl.Dispose ();
				EmbeddedInformationBoxPositionControl = null;
			}

			if (EmbeddedHideBadData != null) {
				EmbeddedHideBadData.Dispose ();
				EmbeddedHideBadData = null;
			}

			if (EmbeddedMolarRatioAxisTitleLabel != null) {
				EmbeddedMolarRatioAxisTitleLabel.Dispose ();
				EmbeddedMolarRatioAxisTitleLabel = null;
			}

			if (EmbeddedModelInfoControl != null) {
				EmbeddedModelInfoControl.Dispose ();
				EmbeddedModelInfoControl = null;
			}

			if (EmbeddedParameterDisplayOptionsControl != null) {
				EmbeddedParameterDisplayOptionsControl.Dispose ();
				EmbeddedParameterDisplayOptionsControl = null;
			}

			if (EmbeddedPowerAxisTitleLabel != null) {
				EmbeddedPowerAxisTitleLabel.Dispose ();
				EmbeddedPowerAxisTitleLabel = null;
			}

			if (EmbeddedSanitizeTicks != null) {
				EmbeddedSanitizeTicks.Dispose ();
				EmbeddedSanitizeTicks = null;
			}

			if (EmbeddedShowDataGraphControl != null) {
				EmbeddedShowDataGraphControl.Dispose ();
				EmbeddedShowDataGraphControl = null;
			}

			if (EmbeddedShowParametersControl != null) {
				EmbeddedShowParametersControl.Dispose ();
				EmbeddedShowParametersControl = null;
			}

			if (EmbeddedShowResiduals != null) {
				EmbeddedShowResiduals.Dispose ();
				EmbeddedShowResiduals = null;
			}

			if (EmbeddedSplineInterpolationControl != null) {
				EmbeddedSplineInterpolationControl.Dispose ();
				EmbeddedSplineInterpolationControl = null;
			}

			if (EmbeddedSymbolControl != null) {
				EmbeddedSymbolControl.Dispose ();
				EmbeddedSymbolControl = null;
			}

			if (EmbeddedSymbolSizeLabel != null) {
				EmbeddedSymbolSizeLabel.Dispose ();
				EmbeddedSymbolSizeLabel = null;
			}

			if (EmbeddedSymbolSizeStepper != null) {
				EmbeddedSymbolSizeStepper.Dispose ();
				EmbeddedSymbolSizeStepper = null;
			}

			if (EmbeddedTemperatureDetailControl != null) {
				EmbeddedTemperatureDetailControl.Dispose ();
				EmbeddedTemperatureDetailControl = null;
			}

			if (EmbeddedTimeAxisTitleLabel != null) {
				EmbeddedTimeAxisTitleLabel.Dispose ();
				EmbeddedTimeAxisTitleLabel = null;
			}

			if (EmbeddedTimeUnitControl != null) {
				EmbeddedTimeUnitControl.Dispose ();
				EmbeddedTimeUnitControl = null;
			}

			if (EmbeddedUnifiedHeatAxis != null) {
				EmbeddedUnifiedHeatAxis.Dispose ();
				EmbeddedUnifiedHeatAxis = null;
			}

			if (EmbeddedUnifiedMolarRatioAxis != null) {
				EmbeddedUnifiedMolarRatioAxis.Dispose ();
				EmbeddedUnifiedMolarRatioAxis = null;
			}

			if (EmbeddedUnifiedPowerAxis != null) {
				EmbeddedUnifiedPowerAxis.Dispose ();
				EmbeddedUnifiedPowerAxis = null;
			}

			if (EmbeddedWidthLabel != null) {
				EmbeddedWidthLabel.Dispose ();
				EmbeddedWidthLabel = null;
			}

			if (FinalFigureGraph != null) {
				FinalFigureGraph.Dispose ();
				FinalFigureGraph = null;
			}

			if (PublishOptionsTabControl != null) {
				PublishOptionsTabControl.Dispose ();
				PublishOptionsTabControl = null;
			}

			if (PublishOptionsTabView != null) {
				PublishOptionsTabView.Dispose ();
				PublishOptionsTabView = null;
			}
		}
	}
}
