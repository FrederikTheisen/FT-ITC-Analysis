using System;
using System.Threading;
using System.Threading.Tasks;

using AppKit;
using CoreGraphics;
using Foundation;

using AnalysisITC.Core.Application;

namespace AnalysisITC
{
    [Register("FixedStatusBarSplitView")]
    public sealed class FixedStatusBarSplitView : NSSplitView
    {
        public FixedStatusBarSplitView(IntPtr handle) : base(handle)
        {
        }

        public override void ResetCursorRects()
        {
            base.ResetCursorRects();

            // The status-bar pane has a fixed height. NSSplitView still installs
            // a resize cursor for its divider, so remove only the cursor rectangles
            // owned by this outer split view.
            DiscardCursorRects();
        }

        public override void MouseDown(NSEvent theEvent)
        {
            // The only exposed surface of this split view is its fixed divider.
            // Do not let NSSplitView enter its divider-drag tracking loop.
            NSCursor.ArrowCursor.Set();
        }

        public override void MouseDragged(NSEvent theEvent)
        {
            NSCursor.ArrowCursor.Set();
        }

        public override void MouseUp(NSEvent theEvent)
        {
            NSCursor.ArrowCursor.Set();
        }
    }

    [Register("StatusBarView")]
    public sealed class StatusBarView : NSView
    {
        public StatusBarView(IntPtr handle) : base(handle)
        {
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            NSColor.WindowBackground.SetFill();
            NSBezierPath.FillRect(Bounds);
            base.DrawRect(dirtyRect);
        }
    }

    public partial class StatusBarViewController : NSViewController
    {
        int progressUpdateGeneration;
        bool subscribed;

        public StatusBarViewController(IntPtr handle) : base(handle)
        {
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            StatusBarManager.StatusUpdated += OnStatusUpdated;
            StatusBarManager.SecondaryStatusUpdated += OnSecondaryStatusUpdated;
            StatusBarManager.ProgressUpdate += OnProgressUpdated;
            subscribed = true;

            ProgressIndicator.Hidden = true;
            StatusBarManager.Invalidate();
        }

        void OnStatusUpdated(object sender, string status)
        {
            NSApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                if (StatusLabel != null)
                    StatusLabel.StringValue = status ?? string.Empty;
            });
        }

        void OnSecondaryStatusUpdated(object sender, string status)
        {
            NSApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                if (DocumentStatusLabel != null)
                    DocumentStatusLabel.StringValue = status ?? string.Empty;
            });
        }

        void OnProgressUpdated(object sender, ProgressIndicatorEventData update)
        {
            var generation = Interlocked.Increment(ref progressUpdateGeneration);
            NSApplication.SharedApplication.InvokeOnMainThread(
                () => ApplyProgressUpdate(update, generation));
        }

        async void ApplyProgressUpdate(
            ProgressIndicatorEventData update,
            int generation)
        {
            if (ProgressIndicator == null) return;

            if (update.HideProgressWheel)
            {
                await Task.Delay(100);
                if (generation != Volatile.Read(ref progressUpdateGeneration)) return;

                ProgressIndicator.StopAnimation(this);
                ProgressIndicator.Hidden = true;
                return;
            }

            if (update.Indeterminate)
            {
                ProgressIndicator.Indeterminate = true;
                ProgressIndicator.Hidden = false;
                ProgressIndicator.StartAnimation(this);
                return;
            }

            ProgressIndicator.StopAnimation(this);
            ProgressIndicator.Indeterminate = false;
            ProgressIndicator.DoubleValue = Math.Max(0, Math.Min(100, update.Progress * 100));
            ProgressIndicator.Hidden = false;

            if (!update.IsProgressFinished) return;

            await Task.Delay(500);
            if (generation != Volatile.Read(ref progressUpdateGeneration)) return;
            ProgressIndicator.Hidden = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && subscribed)
            {
                subscribed = false;
                Interlocked.Increment(ref progressUpdateGeneration);
                StatusBarManager.StatusUpdated -= OnStatusUpdated;
                StatusBarManager.SecondaryStatusUpdated -= OnSecondaryStatusUpdated;
                StatusBarManager.ProgressUpdate -= OnProgressUpdated;
            }

            base.Dispose(disposing);
        }
    }
}
