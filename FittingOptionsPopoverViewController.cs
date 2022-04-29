// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

namespace AnalysisITC
{
	public partial class FittingOptionsPopoverViewController : NSViewController
	{
		public FittingOptionsPopoverViewController (IntPtr handle) : base (handle)
		{
		}

        public override void ViewWillAppear()
        {
            base.ViewWillAppear();

            ErrorIterationsControl.DoubleValue = Math.Log10(Analysis.BootstrapIterations);
            ErrorIterationLabel.IntValue = (int)Math.Pow(10, ErrorIterationsControl.DoubleValue);

            OStep.PlaceholderString = Analysis.Ostep.ToString();
            HStep.PlaceholderString = Analysis.Hstep.ToString();
            GStep.PlaceholderString = Analysis.Gstep.ToString();
            CStep.PlaceholderString = Analysis.Cstep.ToString();
            NStep.PlaceholderString = Analysis.Nstep.ToString();

            InitOffset.StringValue = double.IsNaN(Analysis.Oinit) ? "" : Analysis.Oinit.ToString();
            InitH.StringValue = double.IsNaN(Analysis.Hinit) ? "" : Analysis.Hinit.ToString();
            InitN.StringValue = double.IsNaN(Analysis.Ninit) ? "" : Analysis.Ninit.ToString();
            InitG.StringValue = double.IsNaN(Analysis.Ginit) ? "" : Analysis.Ginit.ToString();
            InitCp.StringValue = double.IsNaN(Analysis.Cinit) ? "" : Analysis.Cinit.ToString();
        }

        partial void ErrorIterationSliderChanged(NSSlider sender)
        {
            ErrorIterationLabel.IntValue = (int)Math.Pow(10, ErrorIterationsControl.DoubleValue);
        }

        partial void ApplyOptions(NSObject sender)
        {
            Analysis.BootstrapIterations = (int)Math.Pow(10, ErrorIterationsControl.DoubleValue);

            if (OStep.DoubleValue > 0) Analysis.Ostep = OStep.DoubleValue;
            if (HStep.DoubleValue > 0) Analysis.Hstep = HStep.DoubleValue;
            if (GStep.DoubleValue > 0) Analysis.Gstep = GStep.DoubleValue;
            if (CStep.DoubleValue > 0) Analysis.Cstep = CStep.DoubleValue;
            if (NStep.DoubleValue > 0) Analysis.Nstep = NStep.DoubleValue;

            if (!string.IsNullOrEmpty(InitOffset.StringValue.Trim())) Analysis.Oinit = InitOffset.DoubleValue; else Analysis.Oinit = double.NaN;
            if (!string.IsNullOrEmpty(InitH.StringValue.Trim())) Analysis.Hinit = InitH.DoubleValue; else Analysis.Hinit = double.NaN;
            if (!string.IsNullOrEmpty(InitG.StringValue.Trim())) Analysis.Ginit = InitG.DoubleValue; else Analysis.Ginit = double.NaN;
            if (!string.IsNullOrEmpty(InitCp.StringValue.Trim())) Analysis.Cinit = InitCp.DoubleValue; else Analysis.Cinit = double.NaN;
            if (!string.IsNullOrEmpty(InitN.StringValue.Trim())) Analysis.Ninit = InitN.DoubleValue; else Analysis.Ninit = double.NaN;

            DismissViewController(this);
        }
    }
}
