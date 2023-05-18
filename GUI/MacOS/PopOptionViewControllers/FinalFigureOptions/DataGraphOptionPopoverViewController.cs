// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

namespace AnalysisITC
{
	public partial class DataGraphOptionPopoverViewController : NSViewController
	{
		public DataGraphOptionPopoverViewController (IntPtr handle) : base (handle)
		{
		}

        public override void ViewWillAppear()
        {
            base.ViewWillAppear();

            UnifiedPowerAxis.State = FinalFigureGraphView.UnifiedPowerAxis ? NSCellStateValue.On : NSCellStateValue.Off;
            DrawBaseline.State = FinalFigureGraphView.DrawBaseline ? NSCellStateValue.On : NSCellStateValue.Off;
            DrawCorrected.State = FinalFigureGraphView.DrawBaselineCorrected ? NSCellStateValue.On : NSCellStateValue.Off;
            TimeUnitControl.SelectSegment((int)FinalFigureGraphView.TimeAxisUnit);

            if (FinalFigureGraphView.PowerAxisTitleIsChanged) PowerAxisTitleLabel.PlaceholderString = FinalFigureGraphView.PowerAxisTitle;
            if (FinalFigureGraphView.TimeAxisTitleIsChanged) TimeAxisTitleLabel.PlaceholderString = FinalFigureGraphView.TimeAxisTitle;

            XTickStepper.IntValue = FinalFigureGraphView.DataXTickCount;
            YTickStepper.IntValue = FinalFigureGraphView.DataYTickCount;

            UpdateTickLabels();
        }

        partial void ControlChanged(NSObject sender)
        {
            UpdateTickLabels();

            FinalFigureGraphView.UnifiedPowerAxis = UnifiedPowerAxis.State == NSCellStateValue.On;
            FinalFigureGraphView.DrawBaseline = DrawBaseline.State == NSCellStateValue.On;
            FinalFigureGraphView.DrawBaselineCorrected = DrawCorrected.State == NSCellStateValue.On;
            FinalFigureGraphView.TimeAxisUnit = (TimeUnit)(int)TimeUnitControl.SelectedSegment;

            if (PowerAxisTitleLabel.StringValue.Trim() != "") FinalFigureGraphView.PowerAxisTitle = PowerAxisTitleLabel.StringValue;
            if (TimeAxisTitleLabel.StringValue.Trim() != "") FinalFigureGraphView.TimeAxisTitle = TimeAxisTitleLabel.StringValue;

            FinalFigureGraphView.DataXTickCount = XTickStepper.IntValue;
            FinalFigureGraphView.DataYTickCount = YTickStepper.IntValue;

            FinalFigureGraphView.Invalidate();
        }

        void UpdateTickLabels()
        {
            XTickLabel.IntValue = XTickStepper.IntValue;
            YTickLabel.IntValue = YTickStepper.IntValue;
        }
    }
}