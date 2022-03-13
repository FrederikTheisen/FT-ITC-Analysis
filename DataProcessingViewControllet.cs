// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;
using System.Linq;

namespace AnalysisITC
{
	public partial class DataProcessingViewControllet : NSViewController
	{
        ExperimentData Data => DataManager.Current;
        DataProcessor Processor => Data?.Processor;

		public DataProcessingViewControllet (IntPtr handle) : base (handle)
		{
		}

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            DataManager.SelectionDidChange += OnSelectionChanged;
            DataProcessor.BaselineInterpolationCompleted += OnInterpolationCompleted;
        }


        public override void ViewWillAppear()
        {
            base.ViewWillAppear();

            UpdateUI();

            UpdateSliderLabels();

            BaselineGraphView.Initialize(DataManager.Current);
        }

        void UpdateUI()
        {
            if (Data == null || Processor.BaselineType == BaselineInterpolatorTypes.None)
            {
                InterpolatorTypeControl.SetSelected(false, 0);
                InterpolatorTypeControl.SetSelected(false, 1);
                InterpolatorTypeControl.SetSelected(false, 2);

                SplineAlgorithmView.Hidden = true;
                SplineBaselineFractionView.Hidden = true;
                SplineHandleModeView.Hidden = true;
                PolynomialDegreeView.Hidden = true;
                ZLimitView.Hidden = true;
            }
            else
            {
                InterpolatorTypeControl.SelectSegment((int)Processor.BaselineType);

                switch (Processor.BaselineType)
                {
                    case BaselineInterpolatorTypes.Spline:
                        SplineAlgorithmView.Hidden = false;
                        SplineHandleModeView.Hidden = false;
                        SplineBaselineFractionView.Hidden = false;
                        PolynomialDegreeView.Hidden = true;
                        ZLimitView.Hidden = true;
                        break;
                    case BaselineInterpolatorTypes.ASL:
                        SplineAlgorithmView.Hidden = true;
                        SplineHandleModeView.Hidden = true;
                        SplineBaselineFractionView.Hidden = true;
                        PolynomialDegreeView.Hidden = true;
                        ZLimitView.Hidden = true;
                        break;
                    case BaselineInterpolatorTypes.Polynomial:
                        SplineAlgorithmView.Hidden = true;
                        SplineHandleModeView.Hidden = true;
                        SplineBaselineFractionView.Hidden = true;
                        PolynomialDegreeView.Hidden = false;
                        ZLimitView.Hidden = false;
                        break;
                }

                IntegrationLengthControl.MaxValue = Data.Injections.Max(inj => inj.Delay);
                if (Data.Injections.Count > 0)
                {
                    IntegrationDelayControl.FloatValue = Data.Injections.Last().IntegrationStartTime - Data.Injections.Last().Time;
                    IntegrationLengthControl.FloatValue = Data.Injections.Last().IntegrationEndTime - Data.Injections.Last().IntegrationStartTime;
                }

                InjectionViewSegControl.Enabled = Data.Injections.Count > 0;
                DataZoomSegControl.Enabled = true;

                if (Data.Processor.Interpolator is SplineInterpolator)
                {
                    SplineFractionSliderControl.FloatValue = (Data.Processor.Interpolator as SplineInterpolator).FractionBaseline;
                }

                InjectionViewSegControl.SetLabel((BaselineGraphView.SelectedPeak + 1).ToString("##0"), 1);
            }

            UpdateSliderLabels();

            ConfirmProcessingButton.Enabled = DataManager.DataIsProcessed;
        }

        #region Processing Baseline

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

        partial void SplineHandleModeControlClicked(NSSegmentedControl sender)
        {
            (Processor.Interpolator as SplineInterpolator).HandleMode = (SplineInterpolator.SplineHandleMode)(int)sender.SelectedSegment;

            UpdateProcessing();
        }

        partial void SplineBaselineFractionSliderChanged(NSSlider sender)
        {
            (Processor.Interpolator as SplineInterpolator).FractionBaseline = sender.FloatValue;

            UpdateSliderLabels();

            UpdateProcessing();
        }

        partial void PolynomialDegreeChanged(NSSlider sender)
        {
            (Processor.Interpolator as PolynomialLeastSquaresInterpolator).Degree = sender.IntValue;

            UpdateSliderLabels();

            UpdateProcessing();
        }

        partial void ZLimitChanged(NSSlider sender)
        {
            (Processor.Interpolator as PolynomialLeastSquaresInterpolator).ZLimit = sender.FloatValue;

            UpdateSliderLabels();

            UpdateProcessing();
        }

        #endregion

        #region Processing Injections

        partial void IntegrationStartTimeSliderChanged(NSSlider sender)
        {
            UpdateSliderLabels();

            Data.SetCustomIntegrationTimes(IntegrationDelayControl.FloatValue, IntegrationLengthControl.FloatValue);

            BaselineGraphView.Invalidate();
        }

        partial void IntegrationLengthSliderChanged(NSSlider sender)
        {
            UpdateSliderLabels();

            Data.SetCustomIntegrationTimes(IntegrationDelayControl.FloatValue, IntegrationLengthControl.FloatValue);

            BaselineGraphView.Invalidate();
        }

        #endregion

        partial void ZoomSegControlClicked(NSSegmentedControl sender)
        {
            switch (sender.SelectedSegment)
            {
                case 0: BaselineGraphView.ShowAllVertical(); break;
                case 1: BaselineGraphView.ZoomBaseline(); break;
                case 2: BaselineGraphView.FocusPeak(); break;
                case 3: BaselineGraphView.UnfocusPeak();  break;
            }
        }

        partial void InjectionViewControlClicked(NSSegmentedControl sender)
        {
            switch (sender.SelectedSegment)
            {
                case 0: BaselineGraphView.SelectedPeak--; break;
                case 2: BaselineGraphView.SelectedPeak++; break;
            }

            sender.SetLabel((BaselineGraphView.SelectedPeak + 1).ToString("##0"), 1);
        }

        partial void DrawFeatureControlClicked(NSSegmentedControl sender)
        {
            BaselineGraphView.SetFeatureVisibility(sender.IsSelectedForSegment(0), sender.IsSelectedForSegment(1));
        }

        partial void ConfirmProcessingButtonClicked(NSObject sender)
        {
            DataManager.SetProgramState(2);
        }

        void UpdateSliderLabels()
        {
            IntegrationStartDelayLabel.StringValue = (IntegrationDelayControl.FloatValue).ToString("#0.0") + "s";
            IntegrationLengthLabel.StringValue = (IntegrationLengthControl.FloatValue).ToString("#0.0") + "s";
            SplineBaselineFractionControl.StringValue = (SplineFractionSliderControl.FloatValue * 100).ToString("##0") + " %";
            PolynomialDegreeLabel.StringValue = PolynomialDegreeSlider.IntValue.ToString();
            ZLimitLabel.StringValue = ZLimitSlider.FloatValue.ToString();
        }

        void UpdateProcessing()
        {
            Data.Processor.InterpolateBaseline();
        }

        private void OnSelectionChanged(object sender, ExperimentData e)
        {
            var current = DataManager.Current;

            BaselineGraphView.Initialize(current);

            UpdateUI();
        }

        private void OnInterpolationCompleted(object sender, EventArgs e)
        {
            if (Processor.BaselineCompleted) BaselineGraphView.Invalidate();

            ConfirmProcessingButton.Enabled = DataManager.DataIsProcessed;
        }
    }
}
