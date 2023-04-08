// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;
using System.Collections.Generic;
using System.Linq;
using static AnalysisITC.Exporter;

namespace AnalysisITC
{
	public partial class ExportAccessoryView : NSView
	{
        ExportAccessoryViewController.ExportAccessoryViewSettings Settings { get; set; }


        public ExportAccessoryView (IntPtr handle) : base (handle)
		{
		}

		public void Setup(ExportAccessoryViewController.ExportAccessoryViewSettings settings)
		{
            Settings = settings;

            UnifyTimeAxisControl.State = Settings.UnifyTimeAxis ? NSCellStateValue.On : NSCellStateValue.Off;

            ExportCorrectedControl.Enabled = settings.BaselineCorrectionEnabled;
            ExportCorrectedControl.State = Settings.ExportBaselineCorrectDataPoints ? NSCellStateValue.On : NSCellStateValue.Off;
            BSLLabel.Enabled = settings.BaselineCorrectionEnabled;

            IncludeFittedPeaksControl.Enabled = settings.FittedPeakExportEnabled;
            IncludeFittedPeaksControl.State = settings.ExportFittedPeaks ? NSCellStateValue.On : NSCellStateValue.Off;
            FitPeakLabel.Enabled = settings.FittedPeakExportEnabled;

            TabView.SelectAt((int)Settings.Export);
        }

        partial void ExportTypeControlAction(NSSegmentedControl sender)
        {
			TabView.SelectAt(sender.SelectedSegment);

            Settings.Export = (ExportType)(int)sender.SelectedSegment;
        }

        partial void ExportSelectionControlAction(NSSegmentedControl sender)
        {
            Settings.Selection = (ExportDataSelection)(int)sender.SelectedSegment;
            Settings.SetData();

            Setup(Settings);
        }

        partial void UnifyTimeAxisControlAction(NSButton sender)
        {
            Settings.UnifyTimeAxis = sender.State == NSCellStateValue.On;
        }

        partial void BaselineCorrectControlAction(NSButton sender)
        {
            Settings.ExportBaselineCorrectDataPoints = sender.State == NSCellStateValue.On;
        }

        partial void FittedPeakControlAction(NSButton sender)
        {
            Settings.ExportFittedPeaks = sender.State == NSCellStateValue.On;
        }
    }
}