using System;

using AppKit;
using Foundation;

namespace AnalysisITC
{
    [Register("ResultWorkspaceSplitView")]
    public sealed class ResultWorkspaceSplitView : NSSplitView
    {
        static readonly nfloat MinimumPaneHeight = 120;

        readonly ResultWorkspaceSplitViewDelegate splitViewDelegate;

        public ResultWorkspaceSplitView(IntPtr handle) : base(handle)
        {
            splitViewDelegate = new ResultWorkspaceSplitViewDelegate();
            Delegate = splitViewDelegate;
        }

        public override void AwakeFromNib()
        {
            base.AwakeFromNib();

            if (Subviews.Length < 2) return;

            // Keep the lower table pane stable when the window changes size;
            // the upper graph/inspector pane receives the additional height.
            SetHoldingPriority(249, 0);
            SetHoldingPriority(1000, 1);
        }

        sealed class ResultWorkspaceSplitViewDelegate : NSSplitViewDelegate
        {
            public override nfloat ConstrainSplitPosition(
                NSSplitView splitView,
                nfloat proposedPosition,
                nint subviewDividerIndex)
            {
                if (subviewDividerIndex != 0) return proposedPosition;

                var maximumPosition =
                    splitView.Bounds.Height - MinimumPaneHeight;
                if (maximumPosition < MinimumPaneHeight)
                    maximumPosition = MinimumPaneHeight;

                if (proposedPosition < MinimumPaneHeight)
                    return MinimumPaneHeight;
                if (proposedPosition > maximumPosition)
                    return maximumPosition;
                return proposedPosition;
            }

            public override nfloat SetMinCoordinateOfSubview(
                NSSplitView splitView,
                nfloat proposedMinimumPosition,
                nint subviewDividerIndex) =>
                subviewDividerIndex == 0
                    ? MinimumPaneHeight
                    : proposedMinimumPosition;

            public override nfloat SetMaxCoordinateOfSubview(
                NSSplitView splitView,
                nfloat proposedMaximumPosition,
                nint subviewDividerIndex)
            {
                if (subviewDividerIndex != 0)
                    return proposedMaximumPosition;

                var maximumPosition =
                    splitView.Bounds.Height - MinimumPaneHeight;
                return maximumPosition < MinimumPaneHeight
                    ? MinimumPaneHeight
                    : maximumPosition;
            }

            public override bool CanCollapse(
                NSSplitView splitView,
                NSView subview) => false;

            public override bool ShouldAdjustSize(
                NSSplitView splitView,
                NSView view)
            {
                var subviews = splitView.Subviews;
                return subviews.Length < 2 || view != subviews[1];
            }
        }
    }
}
