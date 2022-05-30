// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

namespace AnalysisITC
{
    public partial class AnalysisGraphView : NSGraph
    {
        public static bool ShowPeakInfo { get; set; } = true;
        public static bool ShowFitParameters { get; set; } = true;
        public static bool UseUnifiedAxes { get; set; } = false;

        public DataFittingGraph DataFittingGraph => Graph as DataFittingGraph;

        public AnalysisGraphView (IntPtr handle) : base (handle)
		{
            State = ProgramState.Analyze;
        }

        public override void Invalidate()
        {
            if (Graph == null) return;
            if (StateManager.CurrentState != ProgramState.Analyze) return;

            DataFittingGraph.ShowPeakInfo = ShowPeakInfo;
            DataFittingGraph.ShowFitParameters = ShowFitParameters;
            DataFittingGraph.UseUnifiedAxes = UseUnifiedAxes;

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
