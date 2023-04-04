﻿using System;
using AppKit;

namespace AnalysisITC.GUI.MacOS.GraphViews
{
	public class ExperimentDesignerGraphView : NSGraph
    {
        public DataFittingGraph DataFittingGraph => Graph as DataFittingGraph;

        public ExperimentDesignerGraphView(IntPtr handle) : base(handle)
        {
            State = ProgramState.AlwaysActive;
        }

        public override void UpdateTrackingArea()
        {
            
        }

        public override void ViewDidEndLiveResize()
        {
            base.ViewDidEndLiveResize();
        }

        public override void MouseDown(NSEvent theEvent)
        {

        }

        public override void MouseMoved(NSEvent theEvent)
        {

        }

        public override void MouseDragged(NSEvent theEvent)
        {

        }

        public override void MouseExited(NSEvent theEvent)
        {

        }

        public override void Invalidate()
        {
            //if (Graph == null) return;
            //if (StateManager.CurrentState != State && State != ProgramState.AlwaysActive) return;

            //DataFittingGraph.ShowPeakInfo = true;
            //DataFittingGraph.ShowFitParameters = false;
            //DataFittingGraph.UseMolarRatioAxis = false;
            //DataFittingGraph.UseUnifiedEnthalpyAxis = false;

            //base.Invalidate();
        }

        public void Test()
        {

        }

        public void Initialize(ExperimentData experiment)
        {
            Console.WriteLine("init Exp designer graph");

            if (experiment != null)
            {
                Graph = null; // new DataFittingGraph(experiment, this);
            }
            else Graph = null;

            //Invalidate();
        }

        private void Experiment_SolutionChanged(object sender, EventArgs e)
        {
            Invalidate();
        }
    }
}

