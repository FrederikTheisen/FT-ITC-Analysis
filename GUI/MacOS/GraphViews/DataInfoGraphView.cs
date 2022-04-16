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

    }
}
