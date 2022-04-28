// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

namespace AnalysisITC
{
	public partial class FitGraphOptionPopoverViewController : NSViewController
	{
		public FitGraphOptionPopoverViewController (IntPtr handle) : base (handle)
		{
		}

        public override void ViewWillAppear()
        {
            base.ViewWillAppear();

            UnifiedHeatAxis.State = FinalFigureGraphView.UseUnifiedHeatAxis ? NSCellStateValue.On : NSCellStateValue.Off;
            UnifiedMolarRatioAxis.State = FinalFigureGraphView.UseUnifiedMolarRatioAxis ? NSCellStateValue.On : NSCellStateValue.Off;
            DrawZeroLine.State = FinalFigureGraphView.DrawZeroLine ? NSCellStateValue.On : NSCellStateValue.Off;
            DrawErrorBars.State = FinalFigureGraphView.ShowErrorBars ? NSCellStateValue.On : NSCellStateValue.Off;
            BadDataErrorBars.State = FinalFigureGraphView.ShowBadDataErrorBars ? NSCellStateValue.On : NSCellStateValue.Off;
            DrawConfidence.State = FinalFigureGraphView.DrawConfidence ? NSCellStateValue.On : NSCellStateValue.Off;
            DrawFitParameters.State = FinalFigureGraphView.DrawFitParameters ? NSCellStateValue.On : NSCellStateValue.Off;
            HideBadData.State = FinalFigureGraphView.ShowBadData ? NSCellStateValue.On : NSCellStateValue.Off;

            if (FinalFigureGraphView.EnthalpyAxisTitleAxisTitleIsChanged) EnthalpyAxisTitleLabel.StringValue = FinalFigureGraphView.EnthalpyAxisTitle;
            if (FinalFigureGraphView.MolarRatioAxisTitleIsChanged) MolarRatioAxisTitleLabel.StringValue = FinalFigureGraphView.MolarRatioAxisTitle;

            XAxisTickStepper.IntValue = FinalFigureGraphView.FitXTickCount;
            YAxisTickStepper.IntValue = FinalFigureGraphView.FitYTickCount;

            UpdateTickLabels();
        }

        partial void ControlClicked(NSObject sender)
        {
            UpdateTickLabels();

            FinalFigureGraphView.UseUnifiedHeatAxis = UnifiedHeatAxis.State == NSCellStateValue.On;
            FinalFigureGraphView.UseUnifiedMolarRatioAxis = UnifiedMolarRatioAxis.State == NSCellStateValue.On;
            FinalFigureGraphView.DrawZeroLine = DrawZeroLine.State == NSCellStateValue.On;
            FinalFigureGraphView.ShowErrorBars = DrawErrorBars.State == NSCellStateValue.On;
            FinalFigureGraphView.ShowBadDataErrorBars = BadDataErrorBars.State == NSCellStateValue.On;
            FinalFigureGraphView.DrawConfidence = DrawConfidence.State == NSCellStateValue.On;
            FinalFigureGraphView.DrawFitParameters = DrawFitParameters.State == NSCellStateValue.On;
            FinalFigureGraphView.ShowBadData = HideBadData.State == NSCellStateValue.On;

            FinalFigureGraphView.EnthalpyAxisTitle = EnthalpyAxisTitleLabel.StringValue;
            FinalFigureGraphView.MolarRatioAxisTitle = MolarRatioAxisTitleLabel.StringValue;

            FinalFigureGraphView.FitXTickCount = XAxisTickStepper.IntValue;
            FinalFigureGraphView.FitYTickCount = YAxisTickStepper.IntValue;

            FinalFigureGraphView.Invalidate();
        }

        void UpdateTickLabels()
        {
            XTickLabel.IntValue = XAxisTickStepper.IntValue;
            YTickLabel.IntValue = YAxisTickStepper.IntValue;
        }
    }
}
