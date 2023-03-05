using System;
using AppKit;
using CoreGraphics;

namespace AnalysisITC
{
    public class NSGraph : NSView
    {
        public virtual void Invalidate() => this.NeedsDisplay = true;

        public ExperimentData Data => Graph?.ExperimentData;

        private CGGraph graph;
        private NSTrackingArea trackingArea;
        public CGPoint CursorPositionInView { get; private set; } = new CGPoint(0, 0);
        public virtual CGGraph Graph { get => graph; set => graph = value; }
        internal ProgramState State = ProgramState.Load;
        public bool MouseDidDrag { get; private set; } = false;

        CGRect TrackingFrame => Graph != null && Graph.Frame.Width != 0 ? Graph.Frame : new CGRect(0, 0, Frame.Width, Frame.Height);

        public NSGraph(IntPtr handle) : base(handle)
        {
            trackingArea = new NSTrackingArea(Frame, NSTrackingAreaOptions.ActiveAlways | NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.MouseMoved, this, null);
            AddTrackingArea(trackingArea);
        }

        public override void Layout()
        {
            base.Layout();

            if (graph != null) graph.AutoSetFrame();

            UpdateTrackingArea();
        }

        public override void ViewDidEndLiveResize()
        {
            base.ViewDidEndLiveResize();

            UpdateTrackingArea();
        }

        public void UpdateTrackingArea()
        {
            RemoveTrackingArea(trackingArea);

            trackingArea = new NSTrackingArea(TrackingFrame, NSTrackingAreaOptions.ActiveAlways | NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.MouseMoved, this, null);

            AddTrackingArea(trackingArea);
        }

        public override void MouseDown(NSEvent theEvent)
        {
            base.MouseDown(theEvent);

            MouseDidDrag = false;
        }

        public override void MouseMoved(NSEvent theEvent)
        {
            base.MouseMoved(theEvent);

            CursorPositionInView = ConvertPointFromView(theEvent.LocationInWindow, null);
        }

        public override void MouseDragged(NSEvent theEvent)
        {
            base.MouseDragged(theEvent);

            MouseDidDrag = true;

            CursorPositionInView = ConvertPointFromView(theEvent.LocationInWindow, null);
        }

        public override void MouseExited(NSEvent theEvent)
        {
            base.MouseExited(theEvent);

            NSCursor.ArrowCursor.Set();
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            if (StateManager.CurrentState != State) return;

            var cg = NSGraphicsContext.CurrentContext.CGContext;

            var center = new CGPoint(dirtyRect.GetMidX(), dirtyRect.GetMidY());
            switch (State)
            {
                case ProgramState.Load: center += new CGSize(0, 0); break;
                case ProgramState.Process: center += new CGSize(.8 * CGGraph.PPcm, 0.5 * CGGraph.PPcm); break;
                case ProgramState.Analyze: center += new CGSize(.8*CGGraph.PPcm, .5 * CGGraph.PPcm); break;
            }

            if (Graph != null)
            {
                Graph.PrepareDraw(cg, center);

            }

            base.DrawRect(dirtyRect);
        }

        public void Print()
        {
            var _dow = Graph.DrawOnWhite;
            Graph.DrawOnWhite = true;

            Invalidate();

            var op = NSPrintOperation.FromView(this);
            op.PrintInfo.PaperSize = this.Frame.Size;
            op.PrintInfo.BottomMargin = 0;
            op.PrintInfo.TopMargin = 0;
            op.PrintInfo.LeftMargin = 0;
            op.PrintInfo.RightMargin = 0;
            op.PrintInfo.ScalingFactor = 1;
            op.RunOperation();

            Graph.DrawOnWhite = _dow;
        }
    }
}
