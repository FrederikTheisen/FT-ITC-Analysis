using System;
using System.Collections.Generic;
using AppKit;
using CoreGraphics;
using Foundation;

namespace AnalysisITC
{
    [Register("GraphView")]
    public class GraphView : NSView
    {
        public float PlotWidth = 2.5f;
        public float PlotPixelWidth => PlotWidth * 200;

        public float PlotHeight = 1.5f;
        public float PlotPixelHeight => PlotHeight * 200;

        public float XAxisMin = 0;
        public float XAxisMax = 3500;

        public float YAxisMin = 39;
        public float YAxisMax = 42;

        public CGPoint Center => new CGPoint(Frame.Width / 2, Frame.Height / 2);

        public void Invalidate() => this.NeedsDisplay = true;

        public Graph Graph;

        public GraphView(IntPtr handle) : base(handle)
        {
            
        }

        public void Initialize(ExperimentData experiment)
        {
            Graph = new DataGraph(experiment);
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            var cg = NSGraphicsContext.CurrentContext.CGContext;

            if (Graph != null) Graph.PrepareDraw(cg, new CGPoint(dirtyRect.GetMidX(), dirtyRect.GetMidY()));

            base.DrawRect(dirtyRect);
        }

    }
}
