// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

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
            
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            Analysis.AnalysisFinished += GlobalAnalyzer_AnalysisFinished;
            Analysis.AnalysisIterationFinished += Analysis_AnalysisIterationFinished;
            Analysis.BootstrapIterationFinished += Analysis_BootstrapIterationFinished;
            DataManager.SelectionDidChange += DataManager_SelectionDidChange;
            DataManager.DataDidChange += DataManager_DataDidChange;
            Invalidate += Analysis_AnalysisIterationFinished;

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
        }

        private void GlobalAnalyzer_AnalysisFinished(object sender, SolverConvergence e)
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

        private void DataManager_DataDidChange(object sender, ExperimentData e)
        {
            bool enableglobal = DataManager.Data.Count > 1;

            AnalysisModeControl.SetEnabled(enableglobal, 1);

            if (!enableglobal) AnalysisModeControl.SelectSegment(0);
        }

        private void DataManager_SelectionDidChange(object sender, ExperimentData e)
        {
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

        partial void AnalysisModeClicked(NSSegmentedControl sender)
        {
            GlobalAffinityStyle.Hidden = sender.SelectedSegment == 0;
            GlobalEnthalpyStyle.Hidden = sender.SelectedSegment == 0;
            GlobalNView.Hidden = sender.SelectedSegment == 0;
        }

        partial void FitSimplex(NSObject sender)
        {
            Analysis.Algorithm = Analysis.SolverAlgorithm.NelderMead;

            Fit();
        }

        partial void FitLM(NSObject sender)
        {
            Analysis.Algorithm = Analysis.SolverAlgorithm.LevenbergMarquardt;

            Fit();
        }

        void Fit()
        {
            ToggleFitButtons(false);
            StatusBarManager.StartInderminateProgress();
            StatusBarManager.SetStatus("Fitting data...", 0);

            switch (AnalysisModeControl.SelectedSegment)
            {
                case 0: SingleAnalysis(); break;
                default: GlobalAnalysis(); break;
            }
        }

        void SingleAnalysis()
        {
            Analysis.Analyzer.InitializeAnalyzer(DataManager.Current);

            Analysis.Analyzer.Solve(SelectedAnalysisModel);
        }

        void GlobalAnalysis()
        {
            var estyle = (Analysis.VariableConstraint)(int)EnthalpyStyleSegControl.SelectedSegment;
            var astyle = (Analysis.VariableConstraint)(int)AffinityStyleSegControl.SelectedSegment;
            var nstyle = NStyleSegControl.SelectedSegment == 0 ? Analysis.VariableConstraint.None : Analysis.VariableConstraint.SameForAll;

            Analysis.GlobalAnalyzer.InitializeAnalyzer(estyle, astyle, nstyle);
            Analysis.GlobalAnalyzer.Solve(SelectedAnalysisModel);

        }

        void ToggleFitButtons(bool enable)
        {
            FitSimplexButton.Enabled = enable;
            FitLMButton.Enabled = enable;
        }
    }
}
