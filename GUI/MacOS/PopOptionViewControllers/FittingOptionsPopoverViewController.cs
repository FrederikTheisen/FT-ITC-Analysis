// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;
using AnalysisITC.GUI.MacOS.CustomViews;
using AnalysisITC.AppClasses.Analysis2;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
	public partial class FittingOptionsPopoverViewController : NSViewController
	{
        List<ParameterValueAdjustmentView> ParameterControls = new List<ParameterValueAdjustmentView>();
        List<OptionAdjustmentView> OptionControls = new List<OptionAdjustmentView>();

        partial void ErrorIterationSliderChanged(NSSlider sender) => ErrorIterationLabel.IntValue = (int)Math.Pow(10, ErrorIterationsControl.DoubleValue);

        public FittingOptionsPopoverViewController (IntPtr handle) : base (handle)
		{
		}

        public override void ViewWillAppear()
        {
            base.ViewWillAppear();

            ErrorIterationsControl.DoubleValue = Math.Log10(FittingOptionsController.BootstrapIterations);
            ErrorIterationLabel.IntValue = (int)Math.Pow(10, ErrorIterationsControl.DoubleValue);
            if (!FittingOptionsController.IncludeConcentrationVariance) IncludeConcErrorControl.SelectedSegment = 0;
            else
            {
                if (FittingOptionsController.EnableAutoConcentrationVariance) IncludeConcErrorControl.SelectedSegment = 2;
                else IncludeConcErrorControl.SelectedSegment = 1;
            }
            

            ErrorMethodControl.SelectedSegment = (int)FittingOptionsController.ErrorEstimationMethod;

            if (ModelFactory.Factory == null) return;

            foreach (var opt in ModelFactory.Factory.GetExposedModelOptions().Reverse())
            {
                var sv = new OptionAdjustmentView(new CoreGraphics.CGRect(0, 0, StackView.Frame.Width, 20), opt.Value);

                OptionControls.Add(sv);

                StackView.InsertArrangedSubview(sv, 10);
            }

            foreach (var par in ModelFactory.Factory.GetExposedParameters().Reverse())
            {
                var sv = new ParameterValueAdjustmentView(new CoreGraphics.CGRect(0, 0, StackView.Frame.Width, 20));

                sv.Setup(par);

                ParameterControls.Add(sv);

                StackView.InsertArrangedSubview(sv, 8);
            }

            if (ModelFactory.Factory.GetExposedParameters() == null || ModelFactory.Factory.GetExposedParameters().Count() == 0)
            {
                InitialValuesHeader.Hidden = true;
                InitialValuesLine.Hidden = true;
            }

            if (ModelFactory.Factory.GetExposedModelOptions() == null || ModelFactory.Factory.GetExposedModelOptions().Count() == 0)
            {
                ModelOptionsHeader.Hidden = true;
                ModelOptionsLine.Hidden = true;
            }
        }

        public override void ViewDidDisappear()
        {
            foreach (var sv in ParameterControls) sv.Dispose();

            base.ViewDidDisappear();
        }

        partial void ApplyOptions(NSObject sender)
        {
            try
            {
                FittingOptionsController.ErrorEstimationMethod = (ErrorEstimationMethod)(int)ErrorMethodControl.SelectedSegment;
                FittingOptionsController.BootstrapIterations = (int)Math.Pow(10, ErrorIterationsControl.DoubleValue);
                FittingOptionsController.IncludeConcentrationVariance = IncludeConcErrorControl.SelectedSegment > 0;
                FittingOptionsController.EnableAutoConcentrationVariance = IncludeConcErrorControl.SelectedSegment == 2;

                foreach (var sv in ParameterControls)
                {
                    if (sv.ShouldReInitializeParameter)
                    {
                        ModelFactory.Factory.ReinitializeParameter(sv.Parameter);
                    }
                    else if (sv.HasBeenAffectedFlag)
                    {
                        ModelFactory.Factory.SetCustomParameter(sv.Key, sv.Value, sv.Locked);
                    }
                }
                foreach (var sv in OptionControls)
                {
                    sv.ApplyOptions();
                }

                DismissViewController(this);
            }
            catch (Exception ex)
            {
                //DismissViewController(this);

                AppEventHandler.DisplayHandledException(ex);
            }
        }
    }
}
