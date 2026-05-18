using System;
using System.Collections.Generic;
using AppKit;
using CoreGraphics;
using Foundation;

namespace AnalysisITC
{
    [Register("BufferSubtractionGraphView")]
    public partial class BufferSubtractionGraphView : NSGraph
    {
        public BufferSubtractionGraph BufferSubtractionGraph => Graph as BufferSubtractionGraph;

        public BufferSubtractionGraphView(IntPtr handle) : base(handle)
        {
            State = ProgramState.AlwaysActive;
        }

        public void Initialize(ExperimentData bufferExperiment, IEnumerable<ExperimentData> targetExperiments)
        {
            if (Graph is BufferSubtractionGraph graph)
            {
                graph.UpdateData(bufferExperiment, targetExperiments);
            }
            else
            {
                Graph = new BufferSubtractionGraph(bufferExperiment, targetExperiments, this);
            }

            Invalidate();
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            if (Graph == null) return;

            base.DrawRect(dirtyRect);
        }

        public override void MouseMoved(NSEvent theEvent)
        {
            base.MouseMoved(theEvent);

            if (Graph == null) return;

            var feature = Graph.CursorFeatureFromPos(CursorPositionInView);
            if (feature.IsMouseOverFeature)
            {
                NSCursor.PointingHandCursor.Set();
                ToolTip = BufferSubtractionGraph?.TooltipForFeature(feature);
            }
            else
            {
                NSCursor.ArrowCursor.Set();
                ToolTip = null;
            }

            Invalidate();
        }

        public override void MouseDown(NSEvent theEvent)
        {
            base.MouseDown(theEvent);

            if (Graph == null) return;

            var feature = Graph.CursorFeatureFromPos(CursorPositionInView, isclick: true);
            if (feature.IsMouseOverFeature) NSCursor.PointingHandCursor.Set();
            else NSCursor.ArrowCursor.Set();

            Graph.IsMouseDown = true;
            Invalidate();
        }

        public override void MouseUp(NSEvent theEvent)
        {
            base.MouseUp(theEvent);

            if (Graph == null) return;

            var feature = Graph.CursorFeatureFromPos(CursorPositionInView, ismouseup: true);
            if (feature.IsMouseOverFeature) NSCursor.PointingHandCursor.Set();
            else NSCursor.ArrowCursor.Set();

            Graph.IsMouseDown = false;
            Invalidate();
        }
    }
}
