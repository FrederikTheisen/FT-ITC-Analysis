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
	[Register ("FitGraphOptionPopoverViewController")]
	partial class FitGraphOptionPopoverViewController
	{
		[Outlet]
		AppKit.NSSwitch AddGapToResidualPlot { get; set; }

		[Outlet]
		AppKit.NSSwitch BadDataErrorBars { get; set; }

		[Outlet]
		AppKit.NSSwitch DrawConfidence { get; set; }

		[Outlet]
		AppKit.NSSwitch DrawErrorBars { get; set; }

		[Outlet]
		AppKit.NSSwitch DrawFitParameters { get; set; }

		[Outlet]
		AppKit.NSSwitch DrawZeroLine { get; set; }

		[Outlet]
		AppKit.NSTextField EnthalpyAxisTitleLabel { get; set; }

		[Outlet]
		AppKit.NSSwitch HideBadData { get; set; }

		[Outlet]
		AppKit.NSTextField MolarRatioAxisTitleLabel { get; set; }

		[Outlet]
		AppKit.NSSwitch ShowResiduals { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl SplineInterpolationControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl SymbolControl { get; set; }

		[Outlet]
		AppKit.NSTextField SymbolSizeLabel { get; set; }

		[Outlet]
		AppKit.NSStepper SymbolSizeStepper { get; set; }

		[Outlet]
		AppKit.NSSwitch UnifiedHeatAxis { get; set; }

		[Outlet]
		AppKit.NSSwitch UnifiedMolarRatioAxis { get; set; }

		[Outlet]
		AppKit.NSStepper XAxisTickStepper { get; set; }

		[Outlet]
		AppKit.NSTextField XTickLabel { get; set; }

		[Outlet]
		AppKit.NSStepper YAxisTickStepper { get; set; }

		[Outlet]
		AppKit.NSTextField YTickLabel { get; set; }

		[Action ("ControlClicked:")]
		partial void ControlClicked (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (AddGapToResidualPlot != null) {
				AddGapToResidualPlot.Dispose ();
				AddGapToResidualPlot = null;
			}

			if (SplineInterpolationControl != null) {
				SplineInterpolationControl.Dispose ();
				SplineInterpolationControl = null;
			}

			if (BadDataErrorBars != null) {
				BadDataErrorBars.Dispose ();
				BadDataErrorBars = null;
			}

			if (DrawConfidence != null) {
				DrawConfidence.Dispose ();
				DrawConfidence = null;
			}

			if (DrawErrorBars != null) {
				DrawErrorBars.Dispose ();
				DrawErrorBars = null;
			}

			if (DrawFitParameters != null) {
				DrawFitParameters.Dispose ();
				DrawFitParameters = null;
			}

			if (DrawZeroLine != null) {
				DrawZeroLine.Dispose ();
				DrawZeroLine = null;
			}

			if (EnthalpyAxisTitleLabel != null) {
				EnthalpyAxisTitleLabel.Dispose ();
				EnthalpyAxisTitleLabel = null;
			}

			if (HideBadData != null) {
				HideBadData.Dispose ();
				HideBadData = null;
			}

			if (MolarRatioAxisTitleLabel != null) {
				MolarRatioAxisTitleLabel.Dispose ();
				MolarRatioAxisTitleLabel = null;
			}

			if (ShowResiduals != null) {
				ShowResiduals.Dispose ();
				ShowResiduals = null;
			}

			if (SymbolControl != null) {
				SymbolControl.Dispose ();
				SymbolControl = null;
			}

			if (SymbolSizeLabel != null) {
				SymbolSizeLabel.Dispose ();
				SymbolSizeLabel = null;
			}

			if (SymbolSizeStepper != null) {
				SymbolSizeStepper.Dispose ();
				SymbolSizeStepper = null;
			}

			if (UnifiedHeatAxis != null) {
				UnifiedHeatAxis.Dispose ();
				UnifiedHeatAxis = null;
			}

			if (UnifiedMolarRatioAxis != null) {
				UnifiedMolarRatioAxis.Dispose ();
				UnifiedMolarRatioAxis = null;
			}

			if (XAxisTickStepper != null) {
				XAxisTickStepper.Dispose ();
				XAxisTickStepper = null;
			}

			if (XTickLabel != null) {
				XTickLabel.Dispose ();
				XTickLabel = null;
			}

			if (YAxisTickStepper != null) {
				YAxisTickStepper.Dispose ();
				YAxisTickStepper = null;
			}

			if (YTickLabel != null) {
				YTickLabel.Dispose ();
				YTickLabel = null;
			}
		}
	}
}
