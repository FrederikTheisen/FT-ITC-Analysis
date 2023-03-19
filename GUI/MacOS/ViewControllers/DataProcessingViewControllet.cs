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

        bool ShowBaseline => BaselineScopeButton.State == NSCellStateValue.On;
        bool ShowIntegrationRange => IntegrationScopeButton.State == NSCellStateValue.On;
        bool Corrected => CorrectedScopeButton.State == NSCellStateValue.On;
        bool ShowCursorInfo => ShowCursorInfoButton.State == NSCellStateValue.On;

        public DataProcessingViewControllet (IntPtr handle) : base (handle)
		{
		}

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            AppDelegate.StartPrintOperation += AppDelegate_StartPrintOperation;
            DataManager.SelectionDidChange += OnSelectionChanged;
            DataProcessor.BaselineInterpolationCompleted += OnInterpolationCompleted;
            BaselineOptionsPopoverViewController.Updated += BaselineOptionsPopoverViewController_Updated;
            BaselineGraphView.InjectionSelected += BaselineGraphView_InjectionSelected;

            BaselineScopeButton.State = NSCellStateValue.On;
            IntegrationScopeButton.State = NSCellStateValue.On;
            ShowCursorInfoButton.State = NSCellStateValue.On;
        }

        private void AppDelegate_StartPrintOperation(object sender, EventArgs e)
        {
            if (StateManager.CurrentState != ProgramState.Process) return;

            BaselineGraphView.Print();
        }

        private void BaselineOptionsPopoverViewController_Updated(object sender, EventArgs e)
        {
            UpdateUI();
        }

        private void BaselineGraphView_InjectionSelected(object sender, int e)
        {
            UpdateInjectionSelectionUI();
        }

        public override void ViewWillAppear()
        {
            base.ViewWillAppear();

            UpdateUI();

            UpdateSliderLabels();

            //BaselineGraphView.Initialize(DataManager.Current);

            BaselineGraphView.SetFeatureVisibility(ShowBaseline, ShowIntegrationRange, Corrected, ShowCursorInfo);

            BaselineGraphView.UpdateTrackingArea();
        }

        void UpdateUI()
        {
            if (Data == null || Processor.BaselineType == BaselineInterpolatorTypes.None)
            {
                SetSelectedSegment(InterpolatorTypeControl, -1);

                SplineAlgorithmView.Hidden = true;
                SplineBaselineFractionView.Hidden = true;
                SplineHandleModeView.Hidden = true;
                PolynomialDegreeView.Hidden = true;
                ZLimitView.Hidden = true;

                BaselineHeader.StringValue = "Baseline Interpolator Options";
                IntegrationHeader.StringValue = "Peak Integration Options";
            }
            else
            {
                SetSelectedSegment(InterpolatorTypeControl, (int)Processor.BaselineType);

                switch (Processor.BaselineType)
                {
                    case BaselineInterpolatorTypes.Spline:
                        SplineAlgorithmView.Hidden = false;
                        SplineHandleModeView.Hidden = false;
                        SplineBaselineFractionView.Hidden = false;
                        PolynomialDegreeView.Hidden = true;
                        ZLimitView.Hidden = true;
                        SetSelectedSegment(SplineAlgoControl, (int)(Data.Processor.Interpolator as SplineInterpolator).Algorithm);
                        SetSelectedSegment(SplineHandleModeControl, (int)(Data.Processor.Interpolator as SplineInterpolator).HandleMode);
                        SplineFractionSliderControl.FloatValue = (Data.Processor.Interpolator as SplineInterpolator).FractionBaseline;
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
                        PolynomialDegreeSlider.IntValue = (Data.Processor.Interpolator as PolynomialLeastSquaresInterpolator).Degree;
                        ZLimitSlider.DoubleValue = (Data.Processor.Interpolator as PolynomialLeastSquaresInterpolator).ZLimit;
                        break;
                }

                
                if (Data.Injections.Count > 0)
                {
                    IntegrationLengthControl.MaxValue = Data.Injections.Max(inj => inj.Delay);
                    IntegrationDelayControl.FloatValue = Data.Injections.Last().IntegrationStartDelay;
                    if (Data.IntegrationLengthMode == InjectionData.IntegrationLengthMode.Factor) IntegrationLengthControl.FloatValue = FactorToSlider(Data.IntegrationLengthFactor);
                    else IntegrationLengthControl.FloatValue = Data.Injections.Last().IntegrationLength;
                }

                BaselineHeader.StringValue = "Baseline Interpolator Options" + (Data.Processor.IsLocked || Data.Processor.Interpolator.IsLocked ? " [LOCKED]" : "");
                IntegrationHeader.StringValue = "Peak Integration Options" + (Data.Processor.IsLocked ? " [LOCKED]" : "");

                InjectionViewSegControl.Enabled = Data.Injections.Count > 0;
                DataZoomSegControl.Enabled = true;

                InjectionViewSegControl.SetLabel((BaselineGraphView.SelectedPeak + 1).ToString(), 1);
            }

            UpdateSliderLabels();

            UpdateInjectionSelectionUI();

            ConfirmProcessingButton.Enabled = DataManager.AllDataIsBaselineProcessed;
        }

        void SetSelectedSegment(NSSegmentedControl control, int segment)
        {
            for (int i = 0; i < control.SegmentCount; i++) control.SetSelected(false, i);

            if (segment > -0.5) control.SetSelected(true, segment);
        }

        void UpdateSliderLabels()
        {
            IntegrationStartDelayLabel.FloatValue = IntegrationDelayControl.FloatValue;
            var lengthlabel = GetLengthSliderParameter();
            if (Data != null)
            {
                if (Data.IntegrationLengthMode == InjectionData.IntegrationLengthMode.Time) IntegrationLengthLabel.StringValue = lengthlabel.ToString("F1") + "s";
                else IntegrationLengthLabel.StringValue = lengthlabel.ToString("F1") + "x";
            }
            SplineBaselineFractionControl.StringValue = (SplineFractionSliderControl.FloatValue * 100).ToString("##0") + " %";
            PolynomialDegreeLabel.StringValue = PolynomialDegreeSlider.IntValue.ToString();
            ZLimitLabel.StringValue = ZLimitSlider.FloatValue.ToString("G3");
        }

        #region Processing Baseline

        partial void InterplolatorClicked(NSSegmentedControl sender)
        {
            if (Data == null) return;

            Processor.InitializeBaseline((BaselineInterpolatorTypes)(int)sender.SelectedSegment);

            UpdateUI();

            UpdateProcessing();
        }

        partial void SplineAlgoClicked(NSSegmentedControl sender)
        {
            if (Data == null) return;

            (Processor.Interpolator as SplineInterpolator).Algorithm = (SplineInterpolator.SplineInterpolatorAlgorithm)(int)sender.SelectedSegment;

            UpdateProcessing(false);
        }

        partial void SplineHandleModeControlClicked(NSSegmentedControl sender)
        {
            if (Data == null) return;

            (Processor.Interpolator as SplineInterpolator).HandleMode = (SplineInterpolator.SplineHandleMode)(int)sender.SelectedSegment;

            UpdateProcessing();
        }

        partial void SplineBaselineFractionSliderChanged(NSSlider sender)
        {
            if (Data == null) return;

            (Processor.Interpolator as SplineInterpolator).FractionBaseline = sender.FloatValue;

            UpdateSliderLabels();

            UpdateProcessing();
        }

        partial void PolynomialDegreeChanged(NSSlider sender)
        {
            if (Data == null) return;
            if (Processor.Interpolator is not PolynomialLeastSquaresInterpolator) return;

            (Processor.Interpolator as PolynomialLeastSquaresInterpolator).Degree = sender.IntValue;

            UpdateSliderLabels();

            UpdateProcessing();
        }

        partial void ZLimitChanged(NSSlider sender)
        {
            if (Data == null) return;

            (Processor.Interpolator as PolynomialLeastSquaresInterpolator).ZLimit = sender.FloatValue;

            UpdateSliderLabels();

            UpdateProcessing();
        }

        partial void ApplyToAllSwitchToggled(NSSwitch sender)
        {
            
        }

        partial void CopySettingsToAllClicked(NSObject sender)
        {
            DataManager.CopySelectedProcessToAll();
        }

        #endregion

        #region Processing Injections

        partial void IntegrationSegControlClicked(NSSegmentedControl sender)
        {
            if (Data == null) return;

            Data.IntegrationLengthMode = (InjectionData.IntegrationLengthMode)(int)sender.SelectedSegment;

            UpdateSliderLabels();

            UpdateIntegrationRange(length: GetLengthSliderParameter());
        }

        partial void IntegrationStartTimeSliderChanged(NSSlider sender)
        {
            UpdateSliderLabels();

            UpdateIntegrationRange(delay: IntegrationDelayControl.FloatValue);
        }

        partial void IntegrationLengthSliderChanged(NSSlider sender)
        {
            UpdateSliderLabels();

            UpdateIntegrationRange(length: GetLengthSliderParameter());
        }

        float GetLengthSliderParameter() => Data.IntegrationLengthMode switch
        {
            InjectionData.IntegrationLengthMode.Factor => (float)Math.Pow(10, 2 * IntegrationLengthControl.FloatValue / IntegrationLengthControl.MaxValue),
            InjectionData.IntegrationLengthMode.Fit => (float)Math.Pow(10, 2 * IntegrationLengthControl.FloatValue / IntegrationLengthControl.MaxValue),
            _ => IntegrationLengthControl.FloatValue,
        };


        float FactorToSlider(float value)
        {
            return (float)(Math.Log10(value) * IntegrationLengthControl.MaxValue / 2);
        }

        void UpdateIntegrationRange(float? delay = null, float? length = null)
        {
            if (Data == null) return;

            if (BaselineGraphView.SelectedPeak == -1) Data.SetCustomIntegrationTimes(delay, length);
            else Data.Injections[BaselineGraphView.SelectedPeak].SetCustomIntegrationTimes(delay, length);

            BaselineGraphView.Invalidate();

            Data.Processor.IntegratePeaks();
        }

        #endregion

        partial void ZoomSegControlClicked(NSSegmentedControl sender)
        {
            if (Data == null) return;

            switch (sender.SelectedSegment)
            {
                case 0: BaselineGraphView.ShowAllVertical(); break;
                case 1: BaselineGraphView.ZoomBaseline(); break;
                case 2: BaselineGraphView.FocusPeak(); break;
                case 3: BaselineGraphView.UnfocusPeak();  break;
            }
        }

        partial void PeakZoomWidthClicked(NSSegmentedControl sender)
        {
            BaselineGraphView.PeakZoomWidth = (int)sender.SelectedSegment;

            BaselineGraphView.SelectedPeak = BaselineGraphView.SelectedPeak;
        }

        partial void ViewPreviousInjection(NSButton sender)
        {
            BaselineGraphView.SelectedPeak--;

            UpdateInjectionSelectionUI();
        }

        partial void ViewNextInjection(NSButton sender)
        {
            BaselineGraphView.SelectedPeak++;

            UpdateInjectionSelectionUI();
        }

        partial void SelectAllInjections(NSButton sender)
        {
            BaselineGraphView.SelectedPeak = -1;

            UpdateInjectionSelectionUI();
        }

        partial void InjectionViewControlClicked(NSSegmentedControl sender)
        {
            if (Data == null) return;

            switch (sender.SelectedSegment)
            {
                case 0: BaselineGraphView.SelectedPeak--; break;
                case 2: BaselineGraphView.SelectedPeak++; break;
            }

            UpdateInjectionSelectionUI();
        }

        void UpdateInjectionSelectionUI()
        {
            if (BaselineGraphView.SelectedPeak != -1)
            {
                InjectionViewSegControl.SetLabel("injection #" + (BaselineGraphView.SelectedPeak + 1).ToString(), 1);
                IntegrationLengthControl.FloatValue = Data.Injections[BaselineGraphView.SelectedPeak].IntegrationLength;
                IntegrationLengthLabel.FloatValue = Data.Injections[BaselineGraphView.SelectedPeak].IntegrationLength;
                IntegrationStartDelayLabel.FloatValue = Data.Injections[BaselineGraphView.SelectedPeak].IntegrationStartDelay;
            }
            else
            {
                InjectionViewSegControl.SetLabel("all selected", 1);
            }

            ViewPreviousControl.Enabled = BaselineGraphView.SelectedPeak > 0;
            ViewNextControl.Enabled = BaselineGraphView.SelectedPeak < Data?.InjectionCount;
            BaselineGraphView.Invalidate();
        }

        partial void ScopeButtonClicked(NSObject sender) => BaselineGraphView.SetFeatureVisibility(ShowBaseline, ShowIntegrationRange, Corrected, ShowCursorInfo);
        partial void DrawFeatureControlClicked(NSSegmentedControl sender) => BaselineGraphView.SetFeatureVisibility(DrawFeatureSegControl);

        partial void ConfirmProcessingButtonClicked(NSObject sender)
        {
            
        }

        void UpdateProcessing(bool replace = true)
        {
            if (Data == null) return;

            Data.Processor.ProcessData(replace);
        }

        private void OnSelectionChanged(object sender, ExperimentData e)
        {
            var current = DataManager.Current;

            BaselineGraphView.Initialize(current);

            BaselineGraphView.SetFeatureVisibility(ShowBaseline, ShowIntegrationRange, Corrected, ShowCursorInfo);

            UpdateUI();
        }

        private void OnInterpolationCompleted(object sender, EventArgs e)
        {
            if (Processor.BaselineCompleted) BaselineGraphView.Invalidate();

            UpdateUI();
        }
    }
}
