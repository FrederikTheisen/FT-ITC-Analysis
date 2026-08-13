using System;

using AppKit;
using Foundation;

namespace AnalysisITC.UI.MacOS.CustomViews
{
    /// <summary>
    /// Maintains hover state for a view whose position can change beneath a
    /// stationary pointer, such as a row in a scroll view.
    /// </summary>
    internal sealed class ScrollAwareHoverTracker : IDisposable
    {
        readonly NSView owner;
        readonly Action hoverChanged;

        NSTrackingArea trackingArea;
        NSClipView observedClipView;
        NSWindow observedWindow;
        NSObject clipBoundsObserver;
        NSObject ownerFrameObserver;
        NSObject windowDidBecomeKeyObserver;
        NSObject windowDidResignKeyObserver;
        bool disposed;

        public bool IsHovered { get; private set; }

        public ScrollAwareHoverTracker(NSView owner, Action hoverChanged)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.hoverChanged = hoverChanged;

            owner.PostsFrameChangedNotifications = true;
            ownerFrameObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                NSView.FrameChangedNotification,
                _ => Reconcile(),
                owner);
        }

        public void UpdateTrackingArea()
        {
            if (disposed) return;

            RemoveTrackingArea();
            UpdateObservers();

            if (owner.Window != null)
            {
                trackingArea = new NSTrackingArea(
                    owner.Bounds,
                    NSTrackingAreaOptions.ActiveInKeyWindow
                        | NSTrackingAreaOptions.InVisibleRect
                        | NSTrackingAreaOptions.MouseEnteredAndExited,
                    owner,
                    null);
                owner.AddTrackingArea(trackingArea);
            }

            Reconcile();
        }

        public void Reconcile()
        {
            if (disposed) return;

            var window = owner.Window;
            var visibleRect = owner.VisibleRect();
            var hovered = window != null
                && window.IsKeyWindow
                && !owner.IsHiddenOrHasHiddenAncestor
                && visibleRect.Width > 0
                && visibleRect.Height > 0
                && visibleRect.Contains(owner.ConvertPointFromView(
                    window.MouseLocationOutsideOfEventStream,
                    null));

            SetHovered(hovered);
        }

        void UpdateObservers()
        {
            var clipView = FindEnclosingClipView();
            if (!ReferenceEquals(clipView, observedClipView))
            {
                RemoveClipViewObserver();
                observedClipView = clipView;

                if (observedClipView != null)
                {
                    observedClipView.PostsBoundsChangedNotifications = true;
                    clipBoundsObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                        NSView.BoundsChangedNotification,
                        _ => Reconcile(),
                        observedClipView);
                }
            }

            var window = owner.Window;
            if (ReferenceEquals(window, observedWindow)) return;

            RemoveWindowObservers();
            observedWindow = window;
            if (observedWindow == null) return;

            windowDidBecomeKeyObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                NSWindow.DidBecomeKeyNotification,
                _ => Reconcile(),
                observedWindow);
            windowDidResignKeyObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                NSWindow.DidResignKeyNotification,
                _ => Reconcile(),
                observedWindow);
        }

        NSClipView FindEnclosingClipView()
        {
            for (var view = owner.Superview; view != null; view = view.Superview)
                if (view is NSClipView clipView)
                    return clipView;

            return null;
        }

        void SetHovered(bool hovered)
        {
            if (IsHovered == hovered) return;

            IsHovered = hovered;
            hoverChanged?.Invoke();
        }

        void RemoveTrackingArea()
        {
            if (trackingArea == null) return;

            owner.RemoveTrackingArea(trackingArea);
            trackingArea.Dispose();
            trackingArea = null;
        }

        void RemoveClipViewObserver()
        {
            if (clipBoundsObserver != null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(clipBoundsObserver);
                clipBoundsObserver.Dispose();
                clipBoundsObserver = null;
            }

            observedClipView = null;
        }

        void RemoveWindowObservers()
        {
            if (windowDidBecomeKeyObserver != null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(windowDidBecomeKeyObserver);
                windowDidBecomeKeyObserver.Dispose();
                windowDidBecomeKeyObserver = null;
            }

            if (windowDidResignKeyObserver != null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(windowDidResignKeyObserver);
                windowDidResignKeyObserver.Dispose();
                windowDidResignKeyObserver = null;
            }

            observedWindow = null;
        }

        public void Dispose()
        {
            if (disposed) return;

            RemoveTrackingArea();
            RemoveClipViewObserver();
            RemoveWindowObservers();
            if (ownerFrameObserver != null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(ownerFrameObserver);
                ownerFrameObserver.Dispose();
                ownerFrameObserver = null;
            }
            disposed = true;
            SetHovered(false);
        }
    }
}
