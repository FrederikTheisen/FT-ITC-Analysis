using System;
using AppKit;
using CoreGraphics;
using Foundation;

namespace AnalysisITC
{
    /// <summary>
    /// A lightweight tab selector that keeps the standard segmented-control
    /// selection, action, keyboard, and accessibility behaviour.
    /// </summary>
    [Register("WorkspaceTabControl")]
    public class WorkspaceTabControl : NSSegmentedControl
    {
        static readonly nfloat PreferredHeight = 32;
        static readonly nfloat CellAlignmentInset = 4;
        static readonly nfloat CornerRadius = 5;
        static readonly nfloat DividerInset = 5;
        static readonly CGColor ControlBorderColor = NSColor.QuaternaryLabel.CGColor;

        bool drawBorder { get; set; } = false;

        NSTrackingArea trackingArea;
        nint hoveredSegment = -1;
        nint pressedSegment = -1;

        public WorkspaceTabControl(IntPtr handle) : base(handle)
        {
        }

        [Export("initWithFrame:")]
        public WorkspaceTabControl(CGRect frameRect) : base(frameRect)
        {
        }

        public override CGSize IntrinsicContentSize =>
            new CGSize(NSView.NoIntrinsicMetric, PreferredHeight);

        public override void DrawRect(CGRect dirtyRect)
        {
            if (SegmentCount <= 0) return;

            var context = NSGraphicsContext.CurrentContext.CGContext;
            var controlFrame = ControlFrame;
            var segmentWidth = controlFrame.Width / SegmentCount;

            DrawControlBackground(context, controlFrame);

            for (nint segment = 0; segment < SegmentCount; segment++)
            {
                var segmentFrame = new CGRect(
                    controlFrame.X + segment * segmentWidth,
                    controlFrame.Y,
                    segmentWidth,
                    controlFrame.Height);
                var selected = segment == SelectedSegment;
                var hovered = segment == hoveredSegment && !selected;
                var pressed = segment == pressedSegment;

                DrawSegmentBackground(context, segmentFrame, controlFrame, selected, hovered, pressed);
            }

            DrawSeparators(context, controlFrame, segmentWidth);
            if (drawBorder) DrawControlBorder(context, controlFrame);

            //DrawControlBorder(context, Bounds);
            //DrawControlBorder(context, ContentBounds);

            for (nint segment = 0; segment < SegmentCount; segment++)
            {
                var segmentFrame = new CGRect(
                    controlFrame.X + segment * segmentWidth,
                    controlFrame.Y,
                    segmentWidth,
                    controlFrame.Height);
                DrawTitle(segment, segmentFrame, segment == SelectedSegment);
            }
        }

        public override void UpdateTrackingAreas()
        {
            base.UpdateTrackingAreas();

            if (trackingArea != null) RemoveTrackingArea(trackingArea);

            trackingArea = new NSTrackingArea(
                Bounds,
                NSTrackingAreaOptions.ActiveInKeyWindow
                    | NSTrackingAreaOptions.InVisibleRect
                    | NSTrackingAreaOptions.MouseEnteredAndExited
                    | NSTrackingAreaOptions.MouseMoved,
                this,
                null);
            AddTrackingArea(trackingArea);
        }

        public override void MouseMoved(NSEvent theEvent)
        {
            base.MouseMoved(theEvent);

            var point = ConvertPointFromView(theEvent.LocationInWindow, null);
            SetHoveredSegment(SegmentAtPoint(point));
        }

        public override void MouseExited(NSEvent theEvent)
        {
            base.MouseExited(theEvent);
            SetHoveredSegment(-1);
        }

        public override void MouseDown(NSEvent theEvent)
        {
            if (!Enabled) return;

            var segment = SegmentAtPoint(ConvertPointFromView(theEvent.LocationInWindow, null));
            if (segment < 0) return;

            if (Window == null)
            {
                ActivateSegment(segment);
                return;
            }

            SetPressedSegment(segment);
            var pointerSegment = segment;

            while (true)
            {
                var trackingEvent = Window.NextEventMatchingMask(
                    NSEventMask.LeftMouseDragged | NSEventMask.LeftMouseUp);
                if (trackingEvent == null) break;

                pointerSegment = SegmentAtPoint(
                    ConvertPointFromView(trackingEvent.LocationInWindow, null));
                SetPressedSegment(pointerSegment == segment ? segment : -1);

                if (trackingEvent.Type == NSEventType.LeftMouseUp) break;
            }

            var shouldActivate = pressedSegment == segment;
            SetPressedSegment(-1);
            SetHoveredSegment(pointerSegment);

            if (shouldActivate) ActivateSegment(segment);
        }

        void ActivateSegment(nint segment)
        {
            SelectedSegment = segment;
            NeedsDisplay = true;
            SendAction(Action, Target);
        }

        CGRect ContentBounds => new CGRect(
            Bounds.X,
            Bounds.Y + CellAlignmentInset,
            Bounds.Width,
            Math.Max(0, Bounds.Height - CellAlignmentInset));

        CGRect ContentBounds2 => new CGRect(
            Bounds.X,
            Bounds.Y,
            Bounds.Width,
            Bounds.Height);

        CGRect ControlFrame
        {
            get
            {
                var bounds = ContentBounds;
                return new CGRect(
                    bounds.X + 2.5,
                    bounds.Y + 2.5,
                    Math.Max(0, bounds.Width - 5),
                    Math.Max(0, bounds.Height - 5));
            }
        }

        CGRect ControlFrame2
        {
            get
            {
                var bounds = ContentBounds;
                return new CGRect(
                    bounds.X,
                    bounds.Y,
                    bounds.Width,
                    bounds.Height);
            }
        }

        void DrawControlBackground(CGContext context, CGRect frame)
        {
            using (var path = CGPath.FromRoundedRect(frame, CornerRadius, CornerRadius))
            {
                context.AddPath(path);
                context.SetFillColor(NSColor.Label.ColorWithAlphaComponent(0.045f).CGColor);
                context.FillPath();
            }
        }

        void DrawSegmentBackground(
            CGContext context,
            CGRect segmentFrame,
            CGRect controlFrame,
            bool selected,
            bool hovered,
            bool pressed)
        {
            NSColor fillColor;
            if (pressed)
                fillColor = NSColor.ControlAccent.ColorWithAlphaComponent(0.28f);
            else if (selected)
                fillColor = NSColor.ControlAccent.ColorWithAlphaComponent(0.16f);
            else if (hovered)
                fillColor = NSColor.Label.ColorWithAlphaComponent(0.085f);
            else
                return;

            context.SaveState();
            using (var clipPath = CGPath.FromRoundedRect(controlFrame, CornerRadius, CornerRadius))
            {
                context.AddPath(clipPath);
                context.Clip();
                context.SetFillColor(fillColor.CGColor);
                context.FillRect(segmentFrame);
            }
            context.RestoreState();
        }

        void DrawSeparators(CGContext context, CGRect frame, nfloat segmentWidth)
        {
            if (SegmentCount < 2) return;

            context.SetFillColor(ControlBorderColor);
            for (nint segment = 1; segment < SegmentCount; segment++)
            {
                var x = Math.Floor(frame.X + segment * segmentWidth);
                context.FillRect(new CGRect(x, frame.Y + DividerInset, 1, Math.Max(0, frame.Height - 2 * DividerInset)));
            }
        }

        void DrawControlBorder(CGContext context, CGRect frame)
        {
            using (var path = CGPath.FromRoundedRect(frame, CornerRadius, CornerRadius))
            {
                context.AddPath(path);
                context.SetStrokeColor(ControlBorderColor);
                context.SetLineWidth(1);
                context.StrokePath();
            }
        }

        nint SegmentAtPoint(CGPoint point)
        {
            var bounds = ControlFrame;
            if (SegmentCount <= 0
                || point.X < bounds.GetMinX()
                || point.X > bounds.GetMaxX()
                || point.Y < bounds.GetMinY()
                || point.Y > bounds.GetMaxY()) return -1;

            var segmentWidth = bounds.Width / SegmentCount;
            return (nint)Math.Min(
                SegmentCount - 1,
                Math.Floor((point.X - bounds.X) / segmentWidth));
        }

        void SetHoveredSegment(nint segment)
        {
            if (hoveredSegment == segment) return;

            hoveredSegment = segment;
            NeedsDisplay = true;
        }

        void SetPressedSegment(nint segment)
        {
            if (pressedSegment == segment) return;

            pressedSegment = segment;
            NeedsDisplay = true;
            DisplayIfNeeded();
        }

        void DrawTitle(nint segment, CGRect frame, bool selected)
        {
            var font = selected
                ? NSFont.SystemFontOfSize(NSFont.SystemFontSize, NSFontWeight.Semibold)
                : NSFont.SystemFontOfSize(NSFont.SystemFontSize);
            var color = !Enabled
                ? NSColor.DisabledControlText
                : selected ? NSColor.Label : NSColor.SecondaryLabel;
            using (var title = new NSAttributedString(
                       GetLabel(segment) ?? string.Empty,
                       new NSStringAttributes
                       {
                           Font = font,
                           ForegroundColor = color,
                       }))
            {
                var titleSize = title.GetSize();
                var titlePoint = new CGPoint(
                    Math.Floor(frame.GetMidX() - titleSize.Width / 2),
                    Math.Floor(frame.GetMidY() - titleSize.Height / 2));

                title.DrawString(titlePoint);
            }
        }
    }
}
