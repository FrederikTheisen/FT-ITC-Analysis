using System;
using System.Collections.Generic;
using System.Globalization;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;

namespace AnalysisITC
{
    [Register("GraphView")]
    public class GraphView : NSGraph
    {
        public new ProgramState State = ProgramState.Load;

        public GraphView(IntPtr handle) : base(handle)
        {

        }

        public void Initialize(ExperimentData experiment)
        {
            if (experiment != null)
            {
                Graph = new FileInfoGraph(experiment, this);
                Graph.YAxis.Buffer = .05f;
            }
            else Graph = null;

            Invalidate();
        }

        public override void SetFrameSize(CGSize newSize)
        {
            base.SetFrameSize(newSize);

            Invalidate();
        }

        public override void MouseEntered(NSEvent theEvent)
        {
            if (Graph == null) return;

            base.MouseEntered(theEvent);
        }

        public override void MouseExited(NSEvent theEvent)
        {
            if (Graph == null) return;

            base.MouseExited(theEvent);

            Invalidate();
        }

        public override void MouseMoved(NSEvent theEvent)
        {
            if (Graph == null) return;

            base.MouseMoved(theEvent);

            var update = (Graph as FileInfoGraph).SetCursorInfo(CursorPositionInView);

            if (update)
            {
                NSCursor.CrosshairCursor.Set();

                Invalidate();
            }
            else NSCursor.ArrowCursor.Set();
        }
    }
}
