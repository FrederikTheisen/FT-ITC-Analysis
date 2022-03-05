// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

namespace AnalysisITC
{
	public partial class DataProcessingViewControllet : NSViewController
	{
        ExperimentData Data => DataManager.Current();
        DataProcessor Processor => Data.Processor;

		public DataProcessingViewControllet (IntPtr handle) : base (handle)
		{
		}

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            DataManager.SelectionDidChange += OnSelectionChanged;
            DataProcessor.InterpolationCompleted += OnInterpolationCompleted;
        }

        

        public override void ViewDidAppear()
        {
            base.ViewDidAppear();

            UpdateUI();

            BaselineGraphView.Initialize(DataManager.Current());
        }

        void UpdateUI()
        {
            switch ((BaselineInterpolatorTypes)(int)InterpolatorTypeControl.SelectedSegment)
            {
                case BaselineInterpolatorTypes.Spline:
                    SplineAlgorithmView.Hidden = false;
                    SplineAlgoControl.Hidden = false;
                    SplineHandleModeView.Hidden = false;
                    break;
                case BaselineInterpolatorTypes.ASL:
                    SplineAlgorithmView.Hidden = true;
                    SplineAlgoControl.Hidden = true;
                    SplineHandleModeView.Hidden = true;
                    break;
            }
        }

        partial void InterplolatorClicked(NSSegmentedControl sender)
        {
            Processor.InitializeBaseline((BaselineInterpolatorTypes)(int)sender.SelectedSegment);

            UpdateUI();

            UpdateProcessing();
        }

        partial void SplineAlgoClicked(NSSegmentedControl sender)
        {
            (Processor.Interpolator as SplineInterpolator).Algorithm = (SplineInterpolator.SplineInterpolatorAlgorithm)(int)sender.SelectedSegment;

            UpdateProcessing();
        }

        partial void SplineBaselineFractionChanged(NSTextField sender)
        {
            (Processor.Interpolator as SplineInterpolator).FractionBaseline = sender.FloatValue;

            SplineBaselineFractionControl.FloatValue = sender.FloatValue;

            UpdateProcessing();
        }

        partial void SplineBaselineFractionSliderChanged(NSSlider sender)
        {
            (Processor.Interpolator as SplineInterpolator).FractionBaseline = sender.FloatValue;

            UpdateProcessing();
        }

        partial void SplineHandleClicked(NSSegmentedControl sender)
        {
            (Processor.Interpolator as SplineInterpolator).HandleMode = (SplineInterpolator.SplineHandleMode)(int)sender.SelectedSegment;

            UpdateProcessing();
        }

        void UpdateProcessing()
        {
            Data.Processor.InterpolateBaseline();
        }

        private void OnSelectionChanged(object sender, ExperimentData e)
        {
            BaselineGraphView.Initialize(DataManager.Current());
        }

        private void OnInterpolationCompleted(object sender, EventArgs e)
        {
            BaselineGraphView.Invalidate();
        }
    }
}
