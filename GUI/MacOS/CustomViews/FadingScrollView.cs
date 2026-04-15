using System;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
    public partial class FadingScrollView : NSScrollView
    {
        public nfloat FadeHeight { get; set; } = 30f;

        private readonly CAGradientLayer _maskLayer = new CAGradientLayer();
        private NSObject _boundsObserver;

        public FadingScrollView(IntPtr handle) : base(handle)
        {
            InitializeFadeMask();
        }

        public FadingScrollView(CGRect frame) : base(frame)
        {
            InitializeFadeMask();
        }

        private void InitializeFadeMask()
        {
            WantsLayer = true;

            if (ContentView != null)
            {
                ContentView.WantsLayer = true;
                ContentView.PostsBoundsChangedNotifications = true;
            }

            _maskLayer.NeedsDisplayOnBoundsChange = true;

            // Observe scrolling through the clip view’s bounds changes.
            _boundsObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                NSView.BoundsChangedNotification,
                ClipViewBoundsChanged,
                ContentView);
        }

        public override void AwakeFromNib()
        {
            base.AwakeFromNib();
            UpdateFadeMask();
        }

        public override void Layout()
        {
            base.Layout();
            UpdateFadeMask();
        }

        public override void ViewDidMoveToWindow()
        {
            base.ViewDidMoveToWindow();
            UpdateFadeMask();
        }

        public override void ViewDidChangeEffectiveAppearance()
        {
            base.ViewDidChangeEffectiveAppearance();
            UpdateFadeMask();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _boundsObserver != null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(_boundsObserver);
                _boundsObserver.Dispose();
                _boundsObserver = null;
            }

            base.Dispose(disposing);
        }

        private void ClipViewBoundsChanged(NSNotification notification)
        {
            UpdateFadeMask();
        }

        public void RefreshFadeMask()
        {
            UpdateFadeMask();
        }

        private void UpdateFadeMask()
        {
            var clipView = ContentView;
            var documentView = DocumentView as NSView;

            if (clipView?.Layer == null || documentView == null)
                return;

            var bounds = clipView.Bounds;
            var visibleRect = clipView.DocumentVisibleRect();
            var docHeight = documentView.Bounds.Height;
            var visibleHeight = visibleRect.Height;

            CATransaction.Begin();
            CATransaction.DisableActions = true;

            try
            {
                if (bounds.Height <= 0 || docHeight <= 0 || visibleHeight <= 0)
                {
                    clipView.Layer.Mask = null;
                    return;
                }

                bool canScroll = docHeight > visibleHeight + 0.5f;
                if (!canScroll)
                {
                    clipView.Layer.Mask = null;
                    return;
                }

                bool atTop = visibleRect.Y <= 0.5f;
                bool atBottom = visibleRect.GetMaxY() >= docHeight - 0.5f;

                nfloat fadeFraction = FadeHeight / bounds.Height;
                fadeFraction = (nfloat)Math.Max(0f, Math.Min(0.5f, fadeFraction));

                nfloat topSolidEnd = fadeFraction;
                nfloat bottomSolidStart = 1f - fadeFraction;

                _maskLayer.Frame = bounds;

                var clear = NSColor.Clear.CGColor;
                var opaque = NSColor.Black.CGColor;

                if (atTop)
                {
                    _maskLayer.Colors = new CGColor[] { opaque, opaque, clear };
                    _maskLayer.Locations = new NSNumber[]
                    {
                NSNumber.FromFloat(0f),
                NSNumber.FromNFloat(bottomSolidStart),
                NSNumber.FromFloat(1f)
                    };
                }
                else if (atBottom)
                {
                    _maskLayer.Colors = new CGColor[] { clear, opaque, opaque };
                    _maskLayer.Locations = new NSNumber[]
                    {
                NSNumber.FromFloat(0f),
                NSNumber.FromNFloat(topSolidEnd),
                NSNumber.FromFloat(1f)
                    };
                }
                else
                {
                    _maskLayer.Colors = new CGColor[] { clear, opaque, opaque, clear };
                    _maskLayer.Locations = new NSNumber[]
                    {
                NSNumber.FromFloat(0f),
                NSNumber.FromNFloat(topSolidEnd),
                NSNumber.FromNFloat(bottomSolidStart),
                NSNumber.FromFloat(1f)
                    };
                }

                _maskLayer.StartPoint = new CGPoint(0.5, 0.0);
                _maskLayer.EndPoint = new CGPoint(0.5, 1.0);

                clipView.Layer.Mask = _maskLayer;
            }
            finally
            {
                CATransaction.Commit();
            }
        }
    }
}
