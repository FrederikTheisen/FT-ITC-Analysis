using System;
using AppKit;
using CoreGraphics;

namespace AnalysisITC
{
    public class NSGraph : NSView
    {
        public void Invalidate() => this.NeedsDisplay = true;

        public ExperimentData Data => Graph.ExperimentData;

        private CGGraph graph;
        private NSTrackingArea trackingArea;
        public CGPoint CursorPositionInView { get; private set; } = new CGPoint(0, 0);
        public virtual CGGraph Graph { get => graph; set => graph = value; }
        internal ProgramState State = ProgramState.Load;

        public NSGraph(IntPtr handle) : base(handle)
        {
            trackingArea = new NSTrackingArea(Frame, NSTrackingAreaOptions.ActiveAlways | NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.MouseMoved, this, null);
            AddTrackingArea(trackingArea);
        }

        public override void Layout()
        {
            base.Layout();

            UpdateTrackingArea();
        }

        public override void AwakeFromNib()
        {
            base.AwakeFromNib();

            UpdateTrackingArea();
        }

        public override void ViewDidEndLiveResize()
        {
            base.ViewDidEndLiveResize();

            UpdateTrackingArea();
        }

        void UpdateTrackingArea()
        {
            RemoveTrackingArea(trackingArea);

            trackingArea = new NSTrackingArea(Frame, NSTrackingAreaOptions.ActiveAlways | NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.MouseMoved, this, null);

            AddTrackingArea(trackingArea);
        }

        public override void MouseMoved(NSEvent theEvent)
        {
            base.MouseMoved(theEvent);

            CursorPositionInView = ConvertPointFromView(theEvent.LocationInWindow, null);
        }

        public override void MouseDragged(NSEvent theEvent)
        {
            base.MouseDragged(theEvent);

            CursorPositionInView = ConvertPointFromView(theEvent.LocationInWindow, null);
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
    }
}
