// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

namespace AnalysisITC
{
    public partial class AnalysisGraphView : NSGraph
    {
        static event EventHandler UpdateViewParameters;

        static bool showPeakInfo = true;
        static bool showFitParameters = true;
        static bool useUnifiedAxes = false;

        public static bool ShowPeakInfo
        {
            get => showPeakInfo;
            set { showPeakInfo = value; UpdateViewParameters?.Invoke(null, null); }
        }
        public static bool ShowFitParameters
        {
            get => showFitParameters;
            set { showFitParameters = value; UpdateViewParameters?.Invoke(null, null); }
        }
        public static bool UseUnifiedAxes
        {
            get => useUnifiedAxes;
            set { useUnifiedAxes = value; UpdateViewParameters?.Invoke(null, null); }
        }
        public DataFittingGraph DataFittingGraph => Graph as DataFittingGraph;

        public AnalysisGraphView(IntPtr handle) : base(handle)
        {
            State = ProgramState.Analyze;

            UpdateViewParameters += AnalysisGraphView_UpdateViewParameters;
        }

        private void AnalysisGraphView_UpdateViewParameters(object sender, EventArgs e)
        {
            if (Graph == null) return;

            DataFittingGraph.ShowPeakInfo = ShowPeakInfo;
            DataFittingGraph.ShowFitParameters = ShowFitParameters;
            DataFittingGraph.UseMolarRatioAxis = UseUnifiedAxes;

            Invalidate();
        }

        public override void Invalidate()
        {
            if (Graph == null) return;
            if (StateManager.CurrentState != ProgramState.Analyze) return;

            base.Invalidate();
        }

        public void Initialize(ExperimentData experiment)
        {
            if (experiment != null)
            {
                Graph = new DataFittingGraph(experiment, this);
            }
            else Graph = null;

            Invalidate();
        }

        private void Experiment_SolutionChanged(object sender, EventArgs e)
        {
            Invalidate();
        }

        public override void MouseMoved(NSEvent theEvent)
        {
            base.MouseMoved(theEvent);

            if (Graph == null) return;

            var b = (Graph as DataFittingGraph).IsCursorOnFeature(CursorPositionInView);

            if (b.IsMouseOverFeature)
            {
                NSCursor.PointingHandCursor.Set();

                ToolTip = b.ToolTip;

                Invalidate();
            }
            else
            {
                NSCursor.ArrowCursor.Set();

                ToolTip = null;
            }
        }

        public override void MouseDown(NSEvent theEvent)
        {
            base.MouseDown(theEvent);

            if (Graph == null) return;

            var b = Graph.IsCursorOnFeature(CursorPositionInView, isclick: true);

            if (b.IsMouseOverFeature) NSCursor.PointingHandCursor.Set();
            else NSCursor.ArrowCursor.Set();

            Graph.IsMouseDown = true;

            Invalidate();
        }

        public override void MouseUp(NSEvent theEvent)
        {
            base.MouseUp(theEvent);

            if (Graph == null) return;

            var b = Graph.IsCursorOnFeature(CursorPositionInView, ismouseup: true);

            if (b.IsMouseOverFeature) NSCursor.PointingHandCursor.Set();
            else NSCursor.ArrowCursor.Set();

            Graph.IsMouseDown = false;

            Invalidate();
        }
    }
}
