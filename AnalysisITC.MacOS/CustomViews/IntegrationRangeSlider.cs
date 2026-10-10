using System;
using AppKit;
using CoreGraphics;
using Foundation;

namespace AnalysisITC
{
    [Register("IntegrationRangeSlider")]
    public class IntegrationRangeSlider : NSSlider
    {
        public bool IsTrackingRange { get; private set; }
        public event EventHandler TrackingStarted;
        public event EventHandler TrackingCompleted;

        public IntegrationRangeSlider(IntPtr handle) : base(handle) { }

        [Export("initWithFrame:")]
        public IntegrationRangeSlider(CGRect frame) : base(frame) { }

        public override void MouseDown(NSEvent theEvent)
        {
            if (!Enabled) return;

            // AppKit sends the cell's continuous actions inside MouseDown. Its
            // application-wide CurrentEvent need not identify those callbacks.
            IsTrackingRange = true;
            try
            {
                TrackingStarted?.Invoke(this, EventArgs.Empty);
                base.MouseDown(theEvent);
            }
            finally
            {
                IsTrackingRange = false;
                TrackingCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
