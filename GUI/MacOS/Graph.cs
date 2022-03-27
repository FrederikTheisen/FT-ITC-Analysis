using System;
using System.Collections.Generic;
using AppKit;
using CoreGraphics;
using Foundation;
using System.Linq;
using CoreText;
using Utilities;

namespace AnalysisITC
{
    public class NSGraph : NSView
    {
        public void Invalidate() => this.NeedsDisplay = true;

        public Graph Graph;
        private NSTrackingArea trackingArea;
        public CGPoint CursorPositionInView { get; private set; } = new CGPoint(0, 0);

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
            var cg = NSGraphicsContext.CurrentContext.CGContext;

            if (Graph != null)
            {
                Graph.PrepareDraw(cg, new CGPoint(dirtyRect.GetMidX(), dirtyRect.GetMidY()));
            }

            base.DrawRect(dirtyRect);
        }
    }

    public class Graph
    {
        private CGContext Context { get; set; }
        public const float PPcm = 250 / 2.54f;

        public bool DrawOnWhite = false;
        public NSColor StrokeColor
        {
            get
            {
                if (DrawOnWhite) return NSColor.Black;
                else return NSColor.LabelColor;
            }
        }
        internal CTFont DefaultFont = new CTFont("Helvetica", 12);
        internal nfloat DefaultFontHeight => DefaultFont.CapHeightMetric + 5;

        protected nfloat PlotWidthCM = 7.0f;
        protected nfloat PlotPixelWidth => PlotWidthCM * PPcm;

        protected nfloat PlotHeightCM = 5.0f;
        protected nfloat PlotPixelHeight => PlotHeightCM * PPcm;

        internal CGSize PointsPerUnit;
        internal CGPoint Center;
        internal CGSize PlotSize;
        internal CGPoint Origin;
        internal CGRect Frame;
        internal NSView View;
        internal bool ScaleToView = true;
        internal CGLayer CGBuffer;

        internal GraphAxis XAxis;
        internal GraphAxis YAxis;

        public ExperimentData ExperimentData;

        public Graph(ExperimentData experiment, NSView view)
        {
            ExperimentData = experiment;
            View = view;
        }

        public void PrepareDraw(CGContext cg, CGPoint center)
        {
            this.Context = cg;
            this.Center = center;

            SetupFrame();

            if (Frame.Size.Width * Frame.Size.Height < 0) return;

            var pppw = PlotSize.Width / (XAxis.Max - XAxis.Min);
            var ppph = PlotSize.Height / (YAxis.Max - YAxis.Min);

            PointsPerUnit = new CGSize(pppw, ppph);

            Draw(cg);

            DrawFrame();

            DrawAxes();
        }

        internal virtual void SetupFrame()
        {
            if (ScaleToView)
            {
                PlotWidthCM = View.Frame.Size.Width / PPcm;
                PlotHeightCM = View.Frame.Size.Height / PPcm;
            }

            PlotSize = new CGSize(PlotPixelWidth - 2, PlotPixelHeight - 2);
            Origin = new CGPoint(Center.X - PlotSize.Width * 0.5f, Center.Y - PlotSize.Height * 0.5f);
            Frame = new CGRect(Origin, PlotSize);
        }

        internal virtual void Draw(CGContext cg)
        {

        }

        protected void DrawData()
        {

        }

        void DrawSpline(CGPoint[] points)
        {
            CGLayer layer = CGLayer.Create(Context, Frame.Size);



            Context.DrawLayer(layer, Frame.Location);
        }

        void DrawPoints(CGPoint[] points)
        {
            CGLayer layer = CGLayer.Create(Context, Frame.Size);

            Context.DrawLayer(layer, Frame.Location);
        }

        void DrawFrame()
        {
            Context.SetStrokeColor(StrokeColor.CGColor);
            Context.StrokeRectWithWidth(Frame, 1);
        }

        internal void DrawRectsAtPositions(CGLayer layer, CGPoint[] points, double size, bool rounded = false, bool fill = false, float width = 1, CGColor color = null)
        {
            foreach (var p in points)
            {
                AddRectAtPosition(layer, p, size, rounded);
            }

            if (color != null)
            {
                if (fill) layer.Context.SetFillColor(color);
                else layer.Context.SetStrokeColor(color);
            }

            layer.Context.SetLineWidth(width);
            if (fill) layer.Context.FillPath();
            else layer.Context.StrokePath();
        }

        internal void AddRectAtPosition(CGLayer layer, CGPoint p, double size, bool rounded)
        {
            var rect = GetRectAtPosition(p, size);

            if (rounded) layer.Context.AddEllipseInRect(rect);
            else layer.Context.AddRect(rect);
        }

        CGRect GetRectAtPosition(CGPoint point, double size)
        {
            return new CGRect(point.X - size / 2, point.Y - size / 2, size, size);
        }

        internal virtual void DrawAxes()
        {

        }

        void DrawAxisLabel(string label, AxisPosition axisPosition = AxisPosition.Bottom)
        {

        }

        protected void DrawText(CGContext context, string text, nfloat textHeight, nfloat x, nfloat y, CGColor textcolor = null)
        {
            if (textcolor == null) textcolor = NSColor.LabelColor.CGColor;

            y = (nfloat)PlotPixelHeight - y - textHeight;
            context.SetFillColor(StrokeColor.CGColor);

            var attributedString = new NSAttributedString(text,
                new CTStringAttributes
                {
                    ForegroundColorFromContext = true,
                    Font = DefaultFont,
                    StrokeColor = textcolor
                }) ;

            var textLine = new CTLine(attributedString);

            context.TextPosition = new CGPoint(x, y);
            textLine.Draw(context);

            textLine.Dispose();
        }

        internal virtual CGPoint GetRelativePosition(Tuple<double, double> point)
        {
            var relx = (point.Item1 - XAxis.Min) * PointsPerUnit.Width;
            var rely = (point.Item2 - YAxis.Min) * PointsPerUnit.Height;

            return new CGPoint(relx, rely);
        }

        internal virtual CGPoint GetRelativePosition(double x, double y)
        {
            var relx = (x - XAxis.Min) * PointsPerUnit.Width;
            var rely = (y - YAxis.Min) * PointsPerUnit.Height;

            return new CGPoint(relx, rely);
        }

        internal CGPoint PointToAxisPosition(nfloat relx, nfloat rely)
        {
            var x = relx / PointsPerUnit.Width + XAxis.Min;
            var y = rely / PointsPerUnit.Height + YAxis.Min;

            return new CGPoint(x, y);
        }

        void SetAxisRange(GraphAxis axis, double? min, double? max, bool buffer = false)
        {
            if (min == null) min = axis.ActualMin;
            if (max == null) max = axis.ActualMax;

            if (min >= max)
            {
                var _min = min;

                min = max;
                max = _min;
            }

            if (buffer)
            {
                axis.SetWithBuffer((double)min, (double)max);
            }
            else
            {
                axis.Min = (float)min;
                axis.Max = (float)max;
            }
        }

        public void SetXAxisRange(double min, double max, bool buffer = false)
        {
            SetAxisRange(XAxis, min, max, buffer);
        }

        public void SetYAxisRange(double min, double max, bool buffer = false)
        {
            SetAxisRange(YAxis, min, max, buffer);
        }

        internal class GraphAxis
        {
            internal AxisPosition Position;

            public string Legend { get; set; }

            public float ActualMin { get; private set; } = 0;
            public float Min
            {
                get { if (UseNiceAxis) return TickScale.NiceMin; else return ActualMin; }
                set { ActualMin = value; TickScale.SetMinMaxPoints(ActualMin, ActualMax); }
            }

            public float ActualMax { get; private set; } = 1;
            public float Max
            {
                get { if (UseNiceAxis) return TickScale.NiceMax; else return ActualMax; }
                set { ActualMax = value; TickScale.SetMinMaxPoints(ActualMin, ActualMax); }
            }

            float buffer = 0.035f;
            public float Buffer
            {
                get => buffer;
                set
                {
                    var _buffer = buffer;

                    buffer = value;

                    UpdateAutoScale(_buffer);
                }
            }
            Utilities.NiceScale TickScale = new Utilities.NiceScale(0, 1);

            public bool UseNiceAxis { get; set; } = true;

            public int DecimalPoints { get; set; } = 1;

            public GraphAxis(double min, double max)
            {
                this.ActualMin = (float)min;
                this.ActualMax = (float)max;

                TickScale = new Utilities.NiceScale(this.ActualMin, this.ActualMax);
            }

            public static GraphAxis WithBuffer(double min, double max, double? buffer = null)
            {
                if (buffer == null) buffer = 0.035f;

                var delta = max - min;

                var _min = min - delta * buffer;
                var _max = max + delta * buffer;

                var axis = new GraphAxis((float)_min, (float)_max);

                return axis;
            }

            public void SetWithBuffer(double min, double max, float? buffer = null)
            {
                if (buffer == null) buffer = 0.035f;

                var delta = max - min;

                ActualMin = (float)(min - delta * buffer);
                ActualMax = (float)(max + delta * buffer);

                TickScale = new Utilities.NiceScale(this.ActualMin, this.ActualMax);
            }

            void UpdateAutoScale(float old)
            {
                var delta = ActualMax - ActualMin;
                var old_delta = delta / (2 * old + 1);

                var old_min = ActualMin + old_delta * old;
                var old_max = ActualMax - old_delta * old;

                this.ActualMin = old_min - old_delta * buffer;
                this.ActualMax = old_max + old_delta * buffer;

                TickScale = new Utilities.NiceScale(this.ActualMin, this.ActualMax);
            }

            public void Draw(CGContext cg, CGRect frame)
            {

            }
        }

        internal enum AxisPosition
        {
            Top,
            Bottom
        }

    }

    public class DataGraph : Graph
    {
        public bool ShowBaselineCorrected { get; set; } = false;

        public DataGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
            XAxis = new GraphAxis(experiment.DataPoints.Min(dp => dp.Time), experiment.DataPoints.Max(dp => dp.Time))
            {
                UseNiceAxis = false
            };
            YAxis = new GraphAxis(experiment.DataPoints.Min(dp => dp.Power).Value, experiment.DataPoints.Max(dp => dp.Power).Value);

        }

        internal override void Draw(CGContext cg)
        {
            CGLayer layer = CGLayer.Create(cg, Frame.Size);

            var path = new CGPath();

            var points = new List<CGPoint>();

            if (!ShowBaselineCorrected) foreach (var p in ExperimentData.DataPoints) { if (p.Time > XAxis.Min && p.Time < XAxis.Max) points.Add(GetRelativePosition(p)); }
            else foreach (var p in ExperimentData.BaseLineCorrectedDataPoints) { if (p.Time > XAxis.Min && p.Time < XAxis.Max) points.Add(GetRelativePosition(p)); }

            path.AddLines(points.ToArray());

            layer.Context.AddPath(path);
            layer.Context.SetStrokeColor(StrokeColor.CGColor);
            layer.Context.StrokePath();

            cg.DrawLayer(layer, Frame.Location);
        }

        CGPoint GetRelativePosition(DataPoint dp)
        {
            return GetRelativePosition(new Tuple<double, double>(dp.Time, dp.Power.Value));
        }
    }

    public class FileInfoGraph : DataGraph
    {
        List<string> info;

        public FileInfoGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
            info = new List<string>()
                {
                    "Filename: " + experiment.FileName,
                    "Date: " + experiment.Date.ToLocalTime().ToLongDateString() + " " + experiment.Date.ToLocalTime().ToString("HH:mm"),
                    "Temperature | Target: " + experiment.TargetTemperature.ToString() + " °C | Measured: " + experiment.MeasuredTemperature + " °C",
                    "Injections: " + experiment.InjectionCount.ToString(),
                    "Concentrations | Cell: " + (experiment.CellConcentration*1000000).ToString() + " µM | Syringe: " + (experiment.SyringeConcentration*1000000).ToString() + " µM",
                };
        }

        internal override void SetupFrame()
        {
            base.SetupFrame();

            var infoheight = DefaultFontHeight * info.Count + 10;

            PlotSize = new CGSize(PlotPixelWidth - 2, PlotPixelHeight - infoheight - 2);
            Origin = new CGPoint(Center.X - PlotSize.Width * 0.5f, Center.Y - (PlotSize.Height + infoheight) * 0.5f);
            Frame = new CGRect(Origin, PlotSize);
        }

        internal override void Draw(CGContext cg)
        {
            base.Draw(cg);

            DrawInfo(cg);
        }

        public void DrawInfo(CGContext cg)
        {
            int i = 0;

            foreach (var l in info)
            {
                DrawText(cg, l, DefaultFontHeight, 5, 3 + DefaultFontHeight * i);

                i++;
            }
        }
    }

    public class BaselineFittingGraph : DataGraph
    {
        public static bool RenderBaseline { get; set; } = true;
        public static bool RenderInjections { get; set; } = true;

        public BaselineFittingGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
            SetYAxisRange(experiment.DataPoints.Min(dp => dp.Power).Value, experiment.DataPoints.Max(dp => dp.Power).Value, buffer: true);
        }

        public void SetInjectionView(int? i)
        {
            if (i == null)
            {
                XAxis = new GraphAxis(ExperimentData.DataPoints.Min(dp => dp.Time), ExperimentData.DataPoints.Max(dp => dp.Time))
                {
                    UseNiceAxis = false
                };
                YAxis = new GraphAxis(ExperimentData.DataPoints.Min(dp => dp.Power).Value, ExperimentData.DataPoints.Max(dp => dp.Power).Value);
            }
            else
            {
                var inj = ExperimentData.Injections[(int)i];
                var s = inj.Time - 10;
                var e = inj.Time + inj.Delay + 10;

                XAxis = new GraphAxis(s, e)
                {
                    UseNiceAxis = false
                };
                YAxis = new GraphAxis(ExperimentData.DataPoints.Where(dp => dp.Time > s && dp.Time < e).Min(dp => dp.Power).Value, ExperimentData.DataPoints.Where(dp => dp.Time > s && dp.Time < e).Max(dp => dp.Power).Value);
            }
        }

        internal override void Draw(CGContext cg)
        {
            base.Draw(cg);

            if (RenderBaseline && ExperimentData.Processor.Interpolator != null && ExperimentData.Processor.Interpolator.Finished)
            {
                DrawBaseline(cg);

                if (ExperimentData.Processor.Interpolator is SplineInterpolator) DrawSplineHandles(cg);
            }

            if (RenderInjections) DrawIntegrationMarkers(cg);
        }

        void DrawBaseline(CGContext cg)
        {
            CGLayer layer = CGLayer.Create(cg, Frame.Size);

            var path = new CGPath();

            var points = new List<CGPoint>();

            for (int i = 0; i < ExperimentData.DataPoints.Count; i++)
            {
                DataPoint p = ExperimentData.DataPoints[i];
                Energy b = ExperimentData.Processor.Interpolator.Baseline[i];

                if (p.Time > XAxis.Min && p.Time < XAxis.Max)
                    points.Add(GetRelativePosition(new Tuple<double, double>(p.Time, b.Value)));
            }

            path.AddLines(points.ToArray());

            layer.Context.AddPath(path);
            layer.Context.SetStrokeColor(NSColor.Red.CGColor);
            layer.Context.SetLineWidth(2);
            layer.Context.StrokePath();
            layer.Context.SetLineWidth(1);

            cg.DrawLayer(layer, Frame.Location);
        }

        void DrawSplineHandles(CGContext cg)
        {
            CGLayer layer = CGLayer.Create(cg, Frame.Size);

            List<CGRect> points = new List<CGRect>();

            foreach (var sp in (ExperimentData.Processor.Interpolator as SplineInterpolator).SplinePoints)
            {
                var m = GetRelativePosition((float)sp.Time, (float)sp.Power);

                var r = new CGRect(m.X - 4, m.Y - 4, 8, 8);

                points.Add(r);
            }

            layer.Context.SetFillColor(NSColor.Red.CGColor);

            foreach (var r in points)
            {
                layer.Context.FillRect(r);
            }


            cg.DrawLayer(layer, Frame.Location);
        }

        void DrawIntegrationMarkers(CGContext cg)
        {
            CGLayer layer = CGLayer.Create(cg, Frame.Size);

            CGPath path = new CGPath();

            foreach (var inj in ExperimentData.Injections)
            {
                var s = GetRelativePosition(inj.IntegrationStartTime, ExperimentData.DataPoints.Last(dp => dp.Time < inj.IntegrationStartTime).Power.Value);
                var e = GetRelativePosition(inj.IntegrationEndTime, ExperimentData.DataPoints.Last(dp => dp.Time < inj.IntegrationEndTime).Power.Value);

                var radius = 4f;
                var delta = 0.04f * (Frame.Height);
                var deltax = e.X - s.X;

                var maxy = (float)Math.Max(s.Y, e.Y);
                var miny = (float)Math.Min(s.Y, e.Y);

                switch (inj.HeatDirection)
                {
                    default:
                    case PeakHeatDirection.Exothermal:
                        path.MoveToPoint(s.X, miny - delta);
                        path.AddArcToPoint(s.X, maxy + delta, e.X - deltax/2, maxy + delta, radius);
                        path.AddArcToPoint(e.X, maxy + delta, e.X, miny - delta, radius);
                        //path.AddLineToPoint(s.X, maxy + delta);
                        //path.AddLineToPoint(e.X, maxy + delta);
                        path.AddLineToPoint(e.X, miny - delta);
                        DrawText(layer.Context, (inj.ID + 1).ToString("inj #0"), DefaultFontHeight, s.X + 3, PlotPixelHeight - (maxy + delta + 20), NSColor.SystemBlueColor.CGColor);
                        break;
                    case PeakHeatDirection.Endothermal:
                        path.MoveToPoint(s.X, maxy + delta);
                        path.AddLineToPoint(s.X, miny - delta);
                        path.AddLineToPoint(e.X, miny - delta);
                        path.AddLineToPoint(e.X, maxy + delta);
                        DrawText(layer.Context, (inj.ID + 1).ToString("inj #0"), DefaultFontHeight, s.X + 3, PlotPixelHeight - (miny - delta), NSColor.SystemBlueColor.CGColor);
                        break;
                }
            }

            layer.Context.SetStrokeColor(NSColor.SystemBlueColor.CGColor);

            layer.Context.AddPath(path);
            layer.Context.StrokePath();

            cg.DrawLayer(layer, Frame.Location);
        }

        public bool IsCursorOnFeature(CGPoint cursorpos)
        {
            if (RenderBaseline && ExperimentData.Processor.Interpolator is SplineInterpolator)
                foreach (var sp in (ExperimentData.Processor.Interpolator as SplineInterpolator).SplinePoints)
                {
                    var handle_screen_pos = GetRelativePosition(sp.Time, sp.Power.Value);

                    if (Math.Abs(cursorpos.X - 2 - handle_screen_pos.X) < 5)
                    {
                        if (Math.Abs(cursorpos.Y - handle_screen_pos.Y) < 5)
                        {
                            return true;
                        }
                    }
                }

            return false;
        }
    }

    public class DataFittingGraph : Graph
    {
        public static bool UnifiedAxes { get; set; } = false;

        public DataFittingGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
            XAxis = GraphAxis.WithBuffer(
                0,
                experiment.Injections.Last().Ratio,
                0.05);

            YAxis = GraphAxis.WithBuffer(
                Math.Min(experiment.Injections.Min(inj => inj.OffsetEnthalpy.Value.Value), Math.Min((float)ExperimentData.Solution.Enthalpy.Value, 0)),
                Math.Max(experiment.Injections.Max(inj => inj.OffsetEnthalpy.Value.Value), Math.Max((float)ExperimentData.Solution.Enthalpy.Value, 0)),
                0.1);


            XAxis.UseNiceAxis = false;
        }

        internal override void Draw(CGContext cg)
        {
            base.Draw(cg);

            if (ExperimentData.Solution != null) DrawFit(cg);

            if (ExperimentData.Processor.IntegrationCompleted) DrawInjectionsPoints(cg);
        }

        void DrawInjectionsPoints(CGContext cg)
        {
            CGLayer layer = CGLayer.Create(cg, Frame.Size);

            List<CGPoint> points = new List<CGPoint>();
            List<CGPoint> inv_points = new List<CGPoint>();

            foreach (var inj in ExperimentData.Injections)
            {
                var p = GetRelativePosition(inj.Ratio, inj.OffsetEnthalpy);

                if (inj.ID == moverfeature) DrawRectsAtPositions(layer, new CGPoint[] { p }, 9, false, false, width: 2, color: NSColor.Red.CGColor);

                if (inj.Include) points.Add(p);
                else inv_points.Add(p);
            }

            layer.Context.SetFillColor(NSColor.LabelColor.CGColor);
            layer.Context.SetStrokeColor(NSColor.LabelColor.CGColor);

            DrawRectsAtPositions(layer, points.ToArray(), 8, false, true);
            DrawRectsAtPositions(layer, inv_points.ToArray(), 8, false, false);

            cg.DrawLayer(layer, Frame.Location);
        }

        void DrawFit(CGContext cg)
        {
            CGLayer layer = CGLayer.Create(cg, Frame.Size);

            List<CGPoint> points = new List<CGPoint>();

            foreach (var inj in ExperimentData.Injections)
            {
                var x = inj.Ratio;
                var y = ExperimentData.Solution.Evaluate(inj.ID, withoffset: false);

                points.Add(GetRelativePosition(x, y));
            }

            CGPath path = newPathFromPoints(points.ToArray());

            DrawRectsAtPositions(layer, points.ToArray(), 8, true, false, color: NSColor.PlaceholderTextColor.CGColor);

            layer.Context.SetLineWidth(2);
            layer.Context.AddPath(path);
            layer.Context.StrokePath();

            cg.DrawLayer(layer, Frame.Location);
        }

        CGPoint MidPoint(CGPoint p1, CGPoint p2)
        {
            return new CGPoint((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
        }

        CGPath newPathFromPoints(CGPoint[] points)
        {
            var path = new CGPath();

            int pointCount = points.Length;

            if (pointCount > 0)
            {
                CGPoint p0 = points.First();
                path.MoveToPoint(p0);

                if (pointCount == 1) //draw dot
                {
                    var strokeWidth = 1;
                    CGRect pointRect = new CGRect(p0.X - strokeWidth / 2.0, p0.Y - strokeWidth / 2.0, strokeWidth, strokeWidth);
                    path.AddPath(CGPath.EllipseFromRect(pointRect));
                }
                else if (pointCount == 2) //draw line
                {
                    CGPoint p1 = points[1];
                    path.AddLineToPoint(p1);
                }
                else //draw spline
                {
                    CGPoint p1 = p0;
                    CGPoint p2;
                    for (int i = 0; i < pointCount - 1; i++)
                    {
                        p2 = points[i + 1];
                        CGPoint midPoint = MidPoint(p1, p2);
                        path.AddQuadCurveToPoint(p1.X, p1.Y, midPoint.X, midPoint.Y);
                        p1 = p2;
                    }
                    path.AddLineToPoint(points.Last());
                }
            }

            return path;
        }

        int mdownid = -1;
        int moverfeature = -1;

        public bool IsCursorOnFeature(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            foreach (var inj in ExperimentData.Injections)
            {
                var handle_screen_pos = GetRelativePosition(inj.Ratio, inj.OffsetEnthalpy);

                if (Math.Abs(cursorpos.X - 2 - handle_screen_pos.X) < 5)
                {
                    if (Math.Abs(cursorpos.Y - handle_screen_pos.Y) < 5)
                    {
                        if (isclick) mdownid = inj.ID;
                        else if (ismouseup && mdownid == inj.ID) { inj.Include = !inj.Include; moverfeature = -1; }
                        moverfeature = inj.ID;
                        return true;
                    }
                }
            }

            if (isclick) mdownid = -1;
            moverfeature = -1;
            return false;
        }
    }

    public class FinalFigure
    {
        DataGraph DataGraph;
        DataFittingGraph IntegrationGraph;

        public FinalFigure(ExperimentData experiment, NSView view)
        {
            DataGraph = new DataGraph(experiment, view);
            DataGraph.DrawOnWhite = true;
            //setup frame
            //Setup axes


            IntegrationGraph = new DataFittingGraph(experiment, view);
            IntegrationGraph.DrawOnWhite = true;
            //setup frame
            //setup axes
        }
    }
}
