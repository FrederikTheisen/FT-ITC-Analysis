using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;

namespace AnalysisITC
{
    public partial class CALayerDrawingView : AppKit.NSView
    {
        public const float TwoPi = 2 * (float)Math.PI;
        public const float PiHalf = (float)Math.PI / 2;
        public const float PointsPerCM = 250 / 2.54f;
        public static readonly CGFont HelveticaLight = CGFont.CreateWithFontName("Helvetica Neue Light");
        public static readonly CGFont HelveticaBold = CGFont.CreateWithFontName("Helvetica Neue Bold");
        public static readonly CGFont AndaleMone = CGFont.CreateWithFontName("Andale Mono");

        public event EventHandler Paint;
        public event EventHandler Resized;

        DateTime _drawTime;
        Timer _frameTimer;
        public CGPoint CursorPositionInView { get; private set; } = new CGPoint(0, 0);

        public bool SaveScreen { get; set; } = false;

        public int Left
        {
            get => (int)Frame.Left;
            set
            {
                var location = new CGPoint(value, Frame.Y);
                Frame = new CGRect(location, Frame.Size);
            }
        }
        public int Right
        {
            get => (int)Frame.Right;
            set
            {
                var size = Frame;
                size.Width = size.X - value;
                Frame = size;
            }
        }
        public int Top
        {
            get => (int)Frame.Top;
            set
            {
                var location = new CGPoint(Frame.X, value);
                Frame = new CGRect(location, Frame.Size);
            }
        }
        public int Bottom
        {
            get => (int)Frame.Bottom;
            set
            {
                var frame = Frame;
                frame.Height = frame.Y - value;
                Frame = frame;
            }
        }
        public int Width
        {
            get => (int)Frame.Width;
            set
            {
                var frame = Frame;
                frame.Width = value;
                Frame = frame;
            }
        }
        public int Height
        {
            get => (int)Frame.Height;
            set
            {
                var frame = Frame;
                frame.Height = value;
                Frame = frame;
            }
        }
        public bool LiveResizeRedraw { get; set; } = true;
        public override bool IsFlipped => true;
        public static float ContentScale { get; private set; }
        public TimeSpan ResizeRefreshInterval { get; set; } = TimeSpan.FromMilliseconds(25);
        public CGColor BackColor => DrawOnWhite ? NSColor.White.CGColor : NSColor.WindowBackground.CGColor;
        public CGFont DefaultFont { get; set; } = CGFont.CreateWithFontName("Helvetica Neue Light");
        NSTrackingArea TrackingArea { get; set; }
        public bool IsMouseDown { get; set; } = false;
        public bool DrawOnWhite { get; set; }
        public CGColor DefaultStrokeColor => DrawOnWhite ? NSColor.Black.CGColor : NSColor.LabelColor.CGColor;

        #region Constructors

        // Called when created from unmanaged code
        public CALayerDrawingView(IntPtr handle) : base(handle)
        {
            Initialize();
        }

        // Called when created directly from a XIB file
        [Export("initWithCoder:")]
        public CALayerDrawingView(NSCoder coder) : base(coder)
        {
            Initialize();
        }

        public CALayerDrawingView()
        {
            Initialize();
        }

        // Shared initialization code
        void Initialize()
        {
            Layer = SetupBackgroundLayer();
            ContentScale = (float)NSScreen.MainScreen.BackingScaleFactor;

            UpdateTrackingArea();
        }

        void OnScreenChanged(object sender, NSScreen e)
        {
            ContentScale = (float)e.BackingScaleFactor;

            ScreenChanged();

            Invalidate();
        }

        /// <summary>
        /// Method for handling screen change.
        /// </summary>
		public virtual void ScreenChanged()
        {

        }

        public override void AwakeFromNib()
        {
            //WantsLayer = true;

            TrackingArea = new NSTrackingArea(Frame, NSTrackingAreaOptions.ActiveInKeyWindow | NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.MouseMoved
                , this, null);
            AddTrackingArea(TrackingArea);
        }

        public override void ViewDidMoveToWindow()
        {
            base.ViewDidMoveToWindow();

            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(50);
                InvokeOnMainThread(() => Resized?.Invoke(this, null));
            });
        }

        #endregion

        #region Drawing

        CALayer SetupBackgroundLayer()
        {
            var _layer = new CALayer();
            _layer.BackgroundColor = BackColor;
            _layer.Opaque = true;

            return _layer;
        }

        #region Draw String

        public void DrawStringWithSpacing(string text, CGPoint position, CGFont font, nfloat fontsize, CGColor color, nfloat spacing)
        {
            var layer = new CATextLayer();
            var _color = NSColor.FromCGColor(color);

            spacing -= (fontsize / 2f + 1.4f);

            var myText = new NSAttributedString(text,
                   new NSStringAttributes()
                   {
                       Font = NSFont.FromFontName(font.FullName, fontsize),
                       KerningAdjustment = (float)spacing,
                       BaselineOffset = 0,
                       ForegroundColor = _color
                   });

            var s = new NSString(text);
            var att = new NSStringAttributes
            {
                Font = NSFont.FromFontName(font.FullName, fontsize),
                KerningAdjustment = (float)spacing,
                BaselineOffset = 0
            };

            var size = s.StringSize(att);
            var rect = s.BoundingRectWithSize(size, NSStringDrawingOptions.UsesLineFragmentOrigin, att.Dictionary);

            var frame = new CGRect(position, size);

            frame.X -= (fontsize / 2f - 2.5f);
            frame.Y -= rect.Height / 2f - 2;

            layer.Frame = frame;

            layer.AttributedString = myText;
            layer.ForegroundColor = color;
            layer.SetFont(font);
            layer.FontSize = fontsize;

            DrawLayer(layer, false);
        }

        public void DrawString(string text, CGRect rectangle, CGFont font, nfloat fontsize, CGColor color, NSTextAlignment horizontal = NSTextAlignment.Center, NSTextBlockVerticalAlignment vertical = NSTextBlockVerticalAlignment.Middle)
        {
            var center = new CGPoint(rectangle.X, rectangle.Y) + new CGSize(rectangle.Width / 2f, rectangle.Height / 2f);
            var frame = AdjustStringFrame(text, font, fontsize, center, horizontal, vertical);

            if (frame.Width > rectangle.Width - 2)
            {
                frame.X = rectangle.X;
                frame.Width = rectangle.Width - 2;
            }

            DrawStringInternal(text, frame, font, fontsize, color);
        }

        public void DrawString(string text, CGPoint position, CGFont font, nfloat fontsize, CGColor color, NSTextAlignment horizontal = NSTextAlignment.Center, NSTextBlockVerticalAlignment vertical = NSTextBlockVerticalAlignment.Middle)
        {
            CGRect frame = AdjustStringFrame(text, font, fontsize, position, horizontal, vertical);

            DrawStringInternal(text, frame, font, fontsize, color);
        }

        void DrawStringInternal(string text, CGRect frame, CGFont font, nfloat size, CGColor color)
        {
            var layer = new CATextLayer()
            {
                Frame = frame,
                String = text,
                FontSize = size,
                ForegroundColor = color
            };

            layer.SetFont(font);

            DrawLayer(layer, false, false);
        }

        static CGRect AdjustStringFrame(string text, CGFont font, nfloat fontsize, CGPoint position, NSTextAlignment horizontal = NSTextAlignment.Center, NSTextBlockVerticalAlignment vertical = NSTextBlockVerticalAlignment.Middle)
        {
            NSString s = new NSString(text);

            var att = new NSStringAttributes
            {
                Font = NSFont.FromFontName(font.FullName, fontsize),
            };

            var size = s.StringSize(att);
            var rect = s.BoundingRectWithSize(size, NSStringDrawingOptions.UsesLineFragmentOrigin, att.Dictionary);

            CGRect frame = new CGRect(position, size);

            switch (vertical)
            {
                case NSTextBlockVerticalAlignment.Middle:
                    frame.Y -= rect.Height / 2f - 2;
                    break;
                case NSTextBlockVerticalAlignment.Top:
                    break;
                case NSTextBlockVerticalAlignment.Bottom:
                    frame.Y -= rect.Height;
                    break;
                default:
                    break;
            }

            switch (horizontal)
            {
                case NSTextAlignment.Center:
                    frame.X -= rect.Width / 2f;
                    break;
                case NSTextAlignment.Left:
                    break;
                case NSTextAlignment.Right:
                    frame.X -= rect.Width;
                    break;
                default:
                    break;
            }

            return frame;
        }

        public CGSize MeasureString(string text, CGFont font, nfloat fontsize)
        {
            NSString s = new NSString(text);

            var att = new NSStringAttributes
            {
                Font = NSFont.FromFontName(font.FullName, fontsize),
            };

            return s.StringSize(att);
        }

        #endregion

        #region Draw Rectangle

        public void FillRectangle(CGPoint position, float size, CGColor color, float border = 0, float radius = 0)
        {
            FillRectangle(new CGRect(position - new CGSize(size / 2, size / 2), new CGSize(size, size)), color, border, radius);
        }

        public void FillRectangle(CGRect rect, CGColor color, float border = 0, float radius = 0)
        {
            CAShapeLayer r = new CAShapeLayer()
            {
                Frame = rect,
                CornerRadius = radius,
            };
        }
        
        public void FillRectangles(CGRect[] rect, CGColor color, float border = 0)
        {

        }

        public void DrawRectangle(float x, float y, float w, float h, float linewidth, CGColor color)
        {
            DrawRectangle(new CGRect(x, y, w, h), linewidth, color);
        }

        public void DrawRectangle(CGRect rect, float linewidth, CGColor color)
        {
            DrawLayer(new CAShapeLayer
            {
                Frame = rect,
                BorderWidth = linewidth,
                BorderColor = color
            });
        }

        void AddRectToLayer(CALayer layer, CGRect rect, CGColor color, float border = 0)
        {
            
        }

        #endregion

        #region Fill Rectangle

        public void FillRectangle(CALayer gc, nfloat x, nfloat y, nfloat w, nfloat h, CGColor color)
        {
            FillRectangle(gc, new CGRect(x, y, w, h), color);
        }

        public void FillRectangle(CALayer gc, CGRect rect, CGColor color, bool opaque = true)
        {
            var layer = new CAShapeLayer();

            layer.Frame = rect;
            layer.BackgroundColor = color;

            DrawLayer(layer, opaque);
        }

        #endregion

        #region Draw Line

        public void DrawLine(nfloat x1, nfloat y1, nfloat x2, nfloat y2, nfloat width, CGColor color)
        {
            DrawLine(new CGPoint(x1, y1), new CGPoint(x2, y2), width, color, CGLineCap.Butt);
        }

        public void DrawLine(nfloat x1, nfloat y1, nfloat x2, nfloat y2, nfloat width, CGColor color, CGLineCap cap = CGLineCap.Butt, NSNumber[] dashpattern = null)
        {
            DrawLine(new CGPoint(x1, y1), new CGPoint(x2, y2), width, color, cap, dashpattern);
        }

        public void DrawLine(CGPoint point1, CGPoint point2, nfloat width, CGColor color, CGLineCap cap = CGLineCap.Butt, NSNumber[] dashpattern = null)
        {
            var layer = new CAShapeLayer();

            CGPath path = new CGPath();

            path.MoveToPoint(point1);
            path.AddLineToPoint(point2);

            layer.Path = path;
            layer.LineWidth = width;
            layer.StrokeColor = color;
            layer.FillColor = null;
            layer.LineDashPattern = dashpattern;

            DrawLayer(layer);
        }

        public void DrawLine(CGPoint[] line, nfloat width, CGColor color)
        {
            var layer = new CAShapeLayer();

            CGPath path = new CGPath();

            path.MoveToPoint(line[0]);

            foreach (var point in line.Skip(1))
            {
                path.AddLineToPoint(point);
            }

            layer.Path = path;
            layer.LineWidth = width;
            layer.StrokeColor = color;
            layer.FillColor = null;

            DrawLayer(layer);
        }

        public void DrawLine(CGPoint[] line, float width, CGColor color, NSNumber[] dashpattern)
        {
            CGPath path = new CGPath();

            path.MoveToPoint(line[0]);

            foreach (var point in line.Skip(1))
            {
                path.AddLineToPoint(point);
            }

            DrawPath(path, null, color, width, dashpattern);
        }

        public void DrawLines(CGPoint[][] lines, nfloat width, CGColor color, CGLineCap cap = CGLineCap.Butt, NSNumber[] dashpattern = null)
        {
            var layer = new CAShapeLayer();

            CGPath path = new CGPath();

            foreach (var line in lines)
            {
                path.MoveToPoint(line[0]);

                foreach (var point in line.Skip(1))
                {
                    path.AddLineToPoint(point);
                }
            }

            layer.Path = path;
            layer.LineWidth = width;
            layer.StrokeColor = color;
            layer.LineCap = LineCap(cap);
            if (dashpattern != null)
                layer.LineDashPattern = dashpattern;

            DrawLayer(layer);
        }

        public void DrawLines(List<CGPoint[]> lines, nfloat width, CGColor color, CGLineCap cap = CGLineCap.Butt, NSNumber[] dashpattern = null)
        {
            var layer = new CAShapeLayer();

            CGPath path = new CGPath();

            foreach (var line in lines)
            {
                path.MoveToPoint(line[0]);

                foreach (var point in line.Skip(1))
                {
                    path.AddLineToPoint(point);
                }
            }

            layer.Path = path;
            layer.LineWidth = width;
            layer.StrokeColor = color;
            layer.LineCap = LineCap(cap);
            if (dashpattern != null)
                layer.LineDashPattern = dashpattern;

            DrawLayer(layer);
        }

        public void DrawPath(CGPath path, CGColor fillcolor, CGColor strokecolor = null, float borderwidth = 0, NSNumber[] dashpattern = null)
        {
            var layer = new CAShapeLayer()
            {
                Path = path,
                LineWidth = borderwidth,
                StrokeColor = strokecolor,
                FillColor = fillcolor
            };

            if (dashpattern != null)
                layer.LineDashPattern = dashpattern;

            DrawLayer(layer);
        }

        public void StrokePath(CGPath path, CGColor color, float linewidth = 1)
        {
            if (color == null) color = DefaultStrokeColor;

            var layer = new CAShapeLayer()
            {
                Path = path,
                LineWidth = linewidth,
                StrokeColor = color,
                FillColor = null
            };

            DrawLayer(layer);
        }

        private static NSString LineCap(CGLineCap cap)
        {
            switch (cap)
            {
                case CGLineCap.Round:
                    return CAShapeLayer.CapRound;
                case CGLineCap.Square:
                    return CAShapeLayer.CapSquare;
                default:
                    return CAShapeLayer.CapButt;
            }
        }

        #endregion

        #region Draw Arc

        public void DrawArcArrow(CGPoint center, nfloat radius, nfloat start, nfloat sweep, nfloat width, CGColor color)
        {
            var _tip = .05f;
            if (sweep < 0) _tip = -_tip;

            var endvecx = (nfloat)Math.Cos(start + sweep);
            var endvecy = (nfloat)Math.Sin(start + sweep);
            var tipvecx = (nfloat)Math.Cos(start + sweep + _tip);
            var tipvecy = (nfloat)Math.Sin(start + sweep + _tip);

            var centerend = new CGPoint(center.X + radius * endvecx, center.Y + radius * endvecy);
            var centertip = new CGPoint(center.X + radius * tipvecx, center.Y + radius * tipvecy);

            var innerpointend = centerend - new CGSize(width / 2 * endvecx, width / 2 * endvecy);
            var outerpointend = centerend + new CGSize(width / 2 * endvecx, width / 2 * endvecy);

            var path = new CGPath();

            path.AddArc(center.X, center.Y, radius + width / 2, start + sweep, start, sweep > 0);
            path.AddArc(center.X, center.Y, radius - width / 2, start, start + sweep, sweep < 0);
            path.CloseSubpath();
            path.MoveToPoint(innerpointend);
            path.AddLineToPoint(centertip);
            path.AddLineToPoint(outerpointend);

            DrawPath(path, color);
        }

        public void DrawCircle(CGPoint center, nfloat radius, nfloat thickness, CGColor color = null)
        {
            if (color == null)
                color = NSColor.Black.CGColor;

            var layer = new CAShapeLayer();

            CGPath _path = new CGPath();
            _path.AddArc(center.X, center.Y, radius, 0, 2 * (float)Math.PI, true);

            layer.Path = _path;
            layer.LineWidth = thickness;
            layer.FillColor = null;
            layer.StrokeColor = color;

            DrawLayer(layer, true, true);
        }

        public void DrawArc(CGPoint center, nfloat radius, nfloat startangle, nfloat sweepangle, float thickness = 1f, CGColor color = null)
        {
            if (color == null)
                color = NSColor.Black.CGColor;

            var layer = new CAShapeLayer();

            CGPath _path = new CGPath();
            _path.AddArc(center.X, center.Y, radius, startangle, startangle + sweepangle, sweepangle < 0);

            layer.Path = _path;
            layer.LineWidth = thickness;
            layer.FillColor = null;
            layer.StrokeColor = color;
            //_layer.BorderColor = color;

            DrawLayer(layer, true, true);
        }

        #endregion

        public void DrawLayer(CALayer _layer, bool opaque = true, bool ras = true)
        {
            _layer.ContentsScale = ContentScale;
            _layer.Opaque = opaque;
            _layer.ShouldRasterize = ras;
            _layer.RasterizationScale = ContentScale;
            _layer.DrawsAsynchronously = true;

            Layer.AddSublayer(_layer);
        }

        #endregion

        public void Invalidate()
        {
            Layer = SetupBackgroundLayer();

            OnPaint(Layer);

            _drawTime = DateTime.Now;
        }

        public virtual void OnPaint(CALayer gc)
        {
            DrawString("CALayer Base View, Override the 'OnPaint' Method", new CGPoint(Frame.Width / 2, Frame.Height / 2 + 20), DefaultFont, 25f, NSColor.Black.CGColor);
        }

        public override void ViewDidEndLiveResize()
        {
            base.ViewDidEndLiveResize();

            Invalidate();
        }

        public override void ResizeWithOldSuperviewSize(CGSize oldSize)
        {
            base.ResizeWithOldSuperviewSize(oldSize);

            if (LiveResizeRedraw)
                LiveResized();
        }

        public virtual void LiveResized()
        {
            Resized?.Invoke(null, null);

            if (DateTime.Now - _drawTime > ResizeRefreshInterval) Invalidate();
        }

        public override void Layout()
        {
            base.Layout();

            UpdateTrackingArea();
        }

        void UpdateTrackingArea()
        {
            if (TrackingArea != null) RemoveTrackingArea(TrackingArea);

            TrackingArea = new NSTrackingArea(Frame, NSTrackingAreaOptions.ActiveAlways | NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.MouseMoved | NSTrackingAreaOptions.EnabledDuringMouseDrag, this, null);

            AddTrackingArea(TrackingArea);
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
    }
}
