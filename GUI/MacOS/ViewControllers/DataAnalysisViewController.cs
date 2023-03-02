// This file has been autogenerated from a class added in the UI designer.

using System;
using Foundation;
using AppKit;
using AnalysisITC.AppClasses.Analysis2;
//using JavaScriptCore;
//using System.Security.Cryptography;
using System.Linq;

namespace AnalysisITC
{
	public partial class DataAnalysisViewController : NSViewController
	{
        public static event EventHandler Invalidate;
        public static void InvalidateGraph() => Invalidate.Invoke(null, null);

        AnalysisModel SelectedAnalysisModel => (AnalysisModel)(int)ModelTypeControl.SelectedSegment;

        bool ShowPeakInfo => PeakInfoScopeButton.State == NSCellStateValue.On;
        bool ShowParameters => ParametersScopeButton.State == NSCellStateValue.On;
        bool SameAxes => AxesScopeButton.State == NSCellStateValue.On;

        public DataAnalysisViewController (IntPtr handle) : base (handle)
		{
            AnalysisGlobalModeOptionsView2.ParameterContraintUpdated += AnalysisGlobalModeOptionsView2_ParameterContraintUpdated;
            SolverInterface.AnalysisFinished += AnalysisFinished;
            SolverInterface.BootstrapIterationFinished += Analysis_BootstrapIterationFinished;
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            SolverInterface.AnalysisFinished += AnalysisFinished;
            SolverInterface.AnalysisStepFinished += Analysis_AnalysisIterationFinished;
            SolverInterface.BootstrapIterationFinished += Analysis_BootstrapIterationFinished;
            DataManager.SelectionDidChange += DataManager_SelectionDidChange;
            DataManager.DataDidChange += DataManager_DataDidChange;
            Invalidate += Analysis_AnalysisIterationFinished;
            SolverInterface.SolverUpdated += SolverInterface_SolverUpdated;

            GlobalAffinityStyle.Hidden = true;
            GlobalEnthalpyStyle.Hidden = true;
            GlobalNView.Hidden = true;
        }

        public override void ViewWillAppear()
        {
            base.ViewWillAppear();

            PeakInfoScopeButton.State = AnalysisGraphView.ShowPeakInfo ? NSCellStateValue.On : NSCellStateValue.Off;
            ParametersScopeButton.State = AnalysisGraphView.ShowFitParameters ? NSCellStateValue.On : NSCellStateValue.Off;
            AxesScopeButton.State = AnalysisGraphView.UseUnifiedAxes ? NSCellStateValue.On : NSCellStateValue.Off;
            GraphView.Initialize(DataManager.Current);

            SetEnableGlobalAnalysis();

            if (ModelFactory.Factory == null) InitializeFactory();
        }

        private void AnalysisFinished(object sender, SolverConvergence e)
        {
            StatusBarManager.StopIndeterminateProgress();
            StatusBarManager.ClearAppStatus();

            if (e != null)
            {
                StatusBarManager.SetStatus(e.Iterations + " iterations, RMSD = " + e.Loss.ToString("G2"), 11000);
                StatusBarManager.SetStatus(e.Message + " | " + e.Time.TotalMilliseconds + "ms", 6000);
                StatusBarManager.SetStatus("Completed", 1500);
            }

            GraphView.Invalidate();

            ToggleFitButtons(true);
        }

        private void Analysis_AnalysisIterationFinished(object sender, EventArgs e)
        {
            GraphView.Invalidate();
        }

        private void Analysis_BootstrapIterationFinished(object sender, Tuple<int, int, float> e)
        {
            StatusBarManager.Progress = e.Item3;
            StatusBarManager.SetStatus("Bootstrapping...", 0);
            StatusBarManager.SetSecondaryStatus(e.Item1 + "/" + e.Item2, 0);
        }

        private void SolverInterface_SolverUpdated(object sender, SolverUpdate e)
        {
            e.SendToStatusBar();
        }

        private void DataManager_DataDidChange(object sender, ExperimentData e)
        {
            SetEnableGlobalAnalysis();
        }

        void SetEnableGlobalAnalysis()
        {
            bool enableglobal = DataManager.Data.Where(d => d.Include).Count() > 1;

            AnalysisModeControl.SetEnabled(enableglobal, 1);

            if (!enableglobal) AnalysisModeControl.SelectSegment(0);

            if (ModelFactory.Factory is GlobalModelFactory)
                if (!enableglobal) InitializeFactory();
                else ModelFactory.Factory.UpdateData();

            SetExposedFittingOptions();
        }

        private void DataManager_SelectionDidChange(object sender, ExperimentData e)
        {
            if (ModelFactory.Factory != null) ModelFactory.Factory.UpdateData();

            SetExposedFittingOptions();

            GraphView.Initialize(e);
        }

        partial void FeatureDrawControlClicked(NSSegmentedControl sender)
        {
            AnalysisGraphView.ShowPeakInfo = sender.IsSelectedForSegment(0);
            AnalysisGraphView.ShowFitParameters = sender.IsSelectedForSegment(1);
            AnalysisGraphView.UseUnifiedAxes = sender.IsSelectedForSegment(2);

            GraphView.Invalidate();
        }

        partial void ScopeButtonClicked(NSButton sender)
        {
            AnalysisGraphView.ShowPeakInfo = ShowPeakInfo;
            AnalysisGraphView.ShowFitParameters = ShowParameters;
            AnalysisGraphView.UseUnifiedAxes = SameAxes;

            GraphView.Invalidate();
        }

        partial void AnalysisModeClicked(NSSegmentedControl sender) => InitializeFactory();
        partial void AnalysisModelClicked(NSSegmentedControl sender) => InitializeFactory();
        void InitializeFactory()
        {
            if (!DataManager.DataIsLoaded) ModelFactory.Factory = null; //don't even try
            else
            {
                if (ModelFactory.Factory == null) ModelFactory.Factory = ModelFactory.InitializeFactory(SelectedAnalysisModel, AnalysisModeControl.SelectedSegment == 1);
                else //Determine if model changed and update if it did.
                {
                    bool global = AnalysisModeControl.SelectedSegment == 1;
                    var model = ModelFactory.Factory.ModelType;

                    if (ModelFactory.Factory.IsGlobalAnalysis != global || model != SelectedAnalysisModel) ModelFactory.Factory = ModelFactory.InitializeFactory(SelectedAnalysisModel, AnalysisModeControl.SelectedSegment == 1);
                }
            }

            SetExposedFittingOptions();
        }

        void SetExposedFittingOptions()
        {
            while (OptionsStackView.Views[2] is AnalysisGlobalModeOptionsView2)
            {
                var view = OptionsStackView.Views[2];
                OptionsStackView.RemoveView(view);
                view.Dispose();
            }

            if (ModelFactory.Factory != null && ModelFactory.Factory.IsGlobalAnalysis)
            {
                var options = (ModelFactory.Factory as GlobalModelFactory).GetExposedOptions();

                foreach (var opt in options)
                {
                    var control = new AnalysisGlobalModeOptionsView2(new CoreGraphics.CGRect(0, 0, OptionsStackView.Frame.Width, 20));
                    control.Setup(opt.Key, opt.Value, (ModelFactory.Factory as GlobalModelFactory).GlobalModelParameters);

                    OptionsStackView.InsertArrangedSubview(control, 2);
                }
            }

            OptionsStackView.Layout();
        }

        private void AnalysisGlobalModeOptionsView2_ParameterContraintUpdated(object sender, EventArgs e)
        {
            if (ModelFactory.Factory != null && ModelFactory.Factory.IsGlobalAnalysis)
            {
                (ModelFactory.Factory as GlobalModelFactory).InitializeGlobalParameters();
            }
        }

        partial void FitSimplex(NSObject sender) => Fit2(SolverAlgorithm.NelderMead);
        partial void FitLM(NSObject sender) => Fit2(SolverAlgorithm.LevenbergMarquardt);
        void Fit2(SolverAlgorithm algorithm)
        {
            ToggleFitButtons(false);

            if (ModelFactory.Factory == null) InitializeFactory();

            ModelFactory.Factory.BuildModel();

            var solver = SolverInterface.Initialize(ModelFactory.Factory);
            solver.SolverAlgorithm = algorithm;
            solver.ErrorEstimationMethod = FittingOptionsController.ErrorEstimationMethod;
            solver.BootstrapIterations = FittingOptionsController.BootstrapIterations;

            solver.Analyze();

            ModelFactory.Clear();

            InitializeFactory();
        }

        void ToggleFitButtons(bool enable)
        {
            FitSimplexButton.Enabled = enable;
            FitLMButton.Enabled = enable;
        }
    }
}
