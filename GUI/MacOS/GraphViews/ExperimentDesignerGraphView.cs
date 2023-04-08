// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;
using CoreText;

namespace AnalysisITC
{
    public partial class ExperimentDesignerGraphView : NSGraph
    {
        public DataFittingGraph DataFittingGraph => Graph as DataFittingGraph;

        public ExperimentDesignerGraphView(IntPtr handle) : base(handle)
        {
            State = ProgramState.AlwaysActive;
        }

        public override void UpdateTrackingArea()
        {

        }

        public override void Invalidate()
        {
            if (Graph == null) return;
            if (StateManager.CurrentState != State && State != ProgramState.AlwaysActive) return;

            DataFittingGraph.ShowPeakInfo = true;
            DataFittingGraph.ShowFitParameters = true;
            DataFittingGraph.UseMolarRatioAxis = false;
            DataFittingGraph.UseUnifiedEnthalpyAxis = false;
            DataFittingGraph.ParameterFontSize = 12;
            DataFittingGraph.AnalysisDisplayParameters = AppClasses.Analysis2.Models.SolutionInterface.FinalFigureDisplayParameters.Fitted;

            base.Invalidate();
        }

        public void Test()
        {

        }

        public void Initialize(ExperimentData experiment)
        {
            experiment.SolutionChanged -= Experiment_SolutionChanged;

            if (experiment != null)
            {
                Graph = new DataFittingGraph(experiment, this);
                experiment.SolutionChanged += Experiment_SolutionChanged;
            }
            else Graph = null;

            Invalidate();
        }

        private void Experiment_SolutionChanged(object sender, EventArgs e)
        {
            InvokeOnMainThread(() => Invalidate());
        }
    }
}