using System;
using System.Collections.Generic;
using AppKit;
using CoreGraphics;
using Foundation;
using System.Linq;
using CoreText;
using Utilities;
using CoreAnimation;

namespace AnalysisITC
{
    public partial class CAGraph : CALayerDrawingView
    {
        public ExperimentData ExperimentData;

        protected nfloat PlotWidthCM = 7.0f;
        protected nfloat PlotPixelWidth => PlotWidthCM * PointsPerCM;

        protected nfloat PlotHeightCM = 5.0f;
        protected nfloat PlotPixelHeight => PlotHeightCM * PointsPerCM;

        internal CGSize PointsPerUnit;
        internal CGPoint Center;
        internal CGSize PlotSize;
        internal CGPoint Origin;
        internal CGRect PlotFrame;
        internal bool ScaleToView = true;

        GraphAxis xaxis;
        GraphAxis yaxis;

        public GraphAxis XAxis
        {
            get => xaxis;
            set
            {
                if (value.Position == AxisPosition.Unknown)
                    value.Position = AxisPosition.Bottom;

                xaxis = value;
            }
        }
        public GraphAxis YAxis
        {
            get => yaxis;
            set
            {
                if (value.Position == AxisPosition.Unknown)
                    value.Position = AxisPosition.Left;

                yaxis = value;
            }
        }

        public void SetXAxisRange(double min, double max, bool buffer = false) => SetAxisRange(XAxis, min, max, buffer);
        public void SetYAxisRange(double min, double max, bool buffer = false) => SetAxisRange(YAxis, min, max, buffer);

        // Called when created from unmanaged code
        public CAGraph(IntPtr handle) : base(handle)
        {

        }

        // Called when created directly from a XIB file
        [Export("initWithCoder:")]
        public CAGraph(NSCoder coder) : base(coder)
        {

        }

        void Initialize()
        {
            PlotSize = Frame.Size;
        }

        public virtual void SetData(ExperimentData data)
        {
            ExperimentData = data;

            XAxis = new GraphAxis(this, data.DataPoints.Min(dp => dp.Time), data.DataPoints.Max(dp => dp.Time))
            {
                UseNiceAxis = false
            };
            YAxis = new GraphAxis(this, data.DataPoints.Min(dp => dp.Power), data.DataPoints.Max(dp => dp.Power));

            Invalidate();
        }

        public override void OnPaint(CALayer gc)
        {
            SetupFrame();

            if (PlotFrame.Size.Width * PlotFrame.Size.Height < 0) return;
            if (XAxis == null || YAxis == null) return;

            var pppw = PlotSize.Width / (XAxis.Max - XAxis.Min);
            var ppph = PlotSize.Height / (YAxis.Max - YAxis.Min);

            PointsPerUnit = new CGSize(pppw, ppph);

            Draw();

            DrawFrame();

            DrawAxes();
        }

        internal virtual void SetupFrame()
        {
            if (ScaleToView)
            {
                PlotWidthCM = Frame.Size.Width / PointsPerCM;
                PlotHeightCM = Frame.Size.Height / PointsPerCM;
            }

            PlotSize = new CGSize(PlotPixelWidth - 2, PlotPixelHeight - 2);
            Origin = new CGPoint(Center.X - PlotSize.Width * 0.5f, Center.Y - PlotSize.Height * 0.5f);
            PlotFrame = new CGRect(Origin, PlotSize);
        }

        /// <summary>
        /// 
        /// </summary>
        internal virtual void Draw()
        {
            DrawString("Graph Base Paint, Override the 'Draw' Method", new CGPoint(Frame.Width / 2, Frame.Height / 2 + 20), DefaultFont, 25f, NSColor.Black.CGColor);
        }

        void DrawFrame()
        {
            DrawRectangle(Frame, 1, DefaultStrokeColor);
        }

        internal virtual void DrawAxes()
        {

        }

        #region Drawing functions

        public void DrawDataGraph(List<CGPoint> points, float linewidth = 1, CGColor color = null)
        {
            if (points.Count == 0) return;
            if (points.Count == 1) { DrawCircle(points[0], 2, 0); return; }

            var path = new CGPath();

            path.MoveToPoint(points[0]);

            for (int i = 1; i < points.Count; i++)
            {
                CGPoint p = points[i];
                path.AddLineToPoint(p);
            }

            var layer = new CAShapeLayer()
            {
                Path = path,
                LineWidth = linewidth,
                StrokeColor = color,
                FillColor = null,
                FillMode = CAFillMode.Removed
            };

            DrawLayer(layer);
        }

        #endregion

        internal void DrawRectsAtPositions(CGLayer layer, CGPoint[] points, float size, bool circle = false, bool fill = false, float width = 1, CGColor color = null, float radius = 0)
        {
            foreach (var p in points)
            {
                if (circle) AddCircleAtPosition(layer, p, size);
                else AddRectAtPosition(layer, p, size, radius);
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

        internal void AddCircleAtPosition(CGLayer layer, CGPoint p, double size)
        {
            var rect = GetRectAtPosition(p, size);

            layer.Context.AddEllipseInRect(rect);
        }

        internal void AddRectAtPosition(CGLayer layer, CGPoint p, double size, float radius)
        {
            var rect = GetRectAtPosition(p, size);

            if (radius > 0) layer.Context.AddPath(CGPath.FromRoundedRect(rect, radius, radius));
            else layer.Context.AddRect(rect);
        }

        CGRect GetRectAtPosition(CGPoint point, double size)
        {
            return new CGRect(point.X - size / 2, point.Y - size / 2, size, size);
        }



        //protected void DrawText(CGContext context, string text, nfloat textHeight, nfloat x, nfloat y, CGColor textcolor = null)
        //{
        //    if (textcolor == null) textcolor = NSColor.LabelColor.CGColor;

        //    y = (nfloat)PlotPixelHeight - y - textHeight;
        //    context.SetFillColor(DefaultStrokeColor);

        //    var attributedString = new NSAttributedString(text,
        //        new CTStringAttributes
        //        {
        //            ForegroundColorFromContext = true,
        //            Font = DefaultFont,
        //            StrokeColor = textcolor
        //        }) ;

        //    var textLine = new CTLine(attributedString);

        //    context.TextPosition = new CGPoint(x, y);
        //    textLine.Draw(context);

        //    textLine.Dispose();
        //}

        internal CGPoint GetRelativePosition(DataPoint dp, GraphAxis yaxis = null)
        {
            return GetRelativePosition(dp.Time, dp.Power, yaxis);
        }

        internal virtual CGPoint GetRelativePosition(double x, double y, GraphAxis yaxis = null)
        {
            var relx = (x - XAxis.Min) * PointsPerUnit.Width;

            switch (yaxis)
            {
                case null: return new CGPoint(relx, (y - YAxis.Min) * PointsPerUnit.Height);
                default:
                    var ppph = PlotSize.Height / (yaxis.Max - yaxis.Min);
                    return new CGPoint(relx, (y - yaxis.Min) * ppph);
            }
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
    }

    public class CADataGraph : CAGraph
    {
        bool showBaselineCorrected = false;

        public CADataGraph(IntPtr handle) : base(handle)
        {
        }

        public CADataGraph(NSCoder coder) : base(coder)
        {
        }

        public bool ShowBaselineCorrected
        {
            get => showBaselineCorrected;
            set
            {
                showBaselineCorrected = value;

                SetYAxisRange(DataPoints.Min(dp => dp.Power), DataPoints.Max(dp => dp.Power));
            }
        }

        public List<DataPoint> DataPoints => ShowBaselineCorrected && ExperimentData.BaseLineCorrectedDataPoints != null
                    ? ExperimentData.BaseLineCorrectedDataPoints
                    : ExperimentData.DataPoints;

        public override void SetData(ExperimentData experiment)
        {
            base.SetData(experiment);

            XAxis = new GraphAxis(this, DataPoints.Min(dp => dp.Time), DataPoints.Max(dp => dp.Time))
            {
                UseNiceAxis = false
            };
            YAxis = new GraphAxis(this, DataPoints.Min(dp => dp.Power), DataPoints.Max(dp => dp.Power));
        }

        internal override void Draw()
        {
            var points = new List<CGPoint>();

            foreach (var p in DataPoints) { if (p.Time > XAxis.Min && p.Time < XAxis.Max) points.Add(GetRelativePosition(p)); }

            DrawDataGraph(points, 1, DefaultStrokeColor);
        }
    }

    public class CAFileInfoGraph : CADataGraph
    {
        List<string> info = new List<string>();

        public CAFileInfoGraph(IntPtr handle) : base(handle)
        {
        }

        public CAFileInfoGraph(NSCoder coder) : base(coder)
        {
        }

        GraphAxis TemperatureAxis { get; set; }

        public override void SetData(ExperimentData experiment)
        {
            base.SetData(experiment);

            info = new List<string>()
                {
                    "Filename: " + experiment.FileName,
                    "Date: " + experiment.Date.ToLocalTime().ToLongDateString() + " " + experiment.Date.ToLocalTime().ToString("HH:mm"),
                    "Temperature | Target: " + experiment.TargetTemperature.ToString() + " °C | Measured: " + experiment.MeasuredTemperature + " °C",
                    "Injections: " + experiment.InjectionCount.ToString(),
                    "Concentrations | Cell: " + (experiment.CellConcentration*1000000).ToString() + " µM | Syringe: " + (experiment.SyringeConcentration*1000000).ToString() + " µM",
                };

            TemperatureAxis = GraphAxis.WithBuffer(this, experiment.DataPoints.Min(dp => Math.Min(dp.Temperature, dp.ShieldT)), experiment.DataPoints.Max(dp => Math.Max(dp.Temperature, dp.ShieldT)), position: AxisPosition.Right);
            TemperatureAxis.Position = AxisPosition.Right;
        }

        internal override void Draw()
        {
            base.Draw();

            //XAxis.Draw(Layer);
            //YAxis.Draw(Layer);
            //if (TemperatureAxis != null) TemperatureAxis.Draw(Layer);
        }

        void DrawTemperature()
        {
            var temperature = new List<CGPoint>();
            var jacket = new List<CGPoint>();

            for (int i = 0; i < DataPoints.Count; i+=5)
            {
                DataPoint p = DataPoints[i];
                if (p.Time > XAxis.Min && p.Time < XAxis.Max)
                {
                    temperature.Add(GetRelativePosition(p.Time, p.Temperature, TemperatureAxis));
                    jacket.Add(GetRelativePosition(p.Time, p.ShieldT, TemperatureAxis));
                }
            }

            DrawDataGraph(temperature, color: NSColor.Red.CGColor);
            DrawDataGraph(jacket, color: NSColor.Red.CGColor);
        }
    }

    public class CABaselineFittingGraph : CADataGraph
    {
        public CABaselineFittingGraph(IntPtr handle) : base(handle)
        {
        }

        public CABaselineFittingGraph(NSCoder coder) : base(coder)
        {
        }

        public static bool ShowBaseline { get; set; } = true;
        public static bool ShowInjections { get; set; } = true;

        public override void SetData(ExperimentData experiment)
        {
            base.SetData(experiment);

            SetYAxisRange(DataPoints.Min(dp => dp.Power), DataPoints.Max(dp => dp.Power), buffer: true);
        }

        public void SetInjectionView(int? i)
        {
            if (i == null)
            {
                XAxis = new GraphAxis(this, DataPoints.Min(dp => dp.Time), DataPoints.Max(dp => dp.Time))
                {
                    UseNiceAxis = false
                };
                YAxis = new GraphAxis(this, DataPoints.Min(dp => dp.Power), DataPoints.Max(dp => dp.Power));
            }
            else
            {
                var inj = ExperimentData.Injections[(int)i];
                var s = inj.Time - 10;
                var e = inj.Time + inj.Delay + 10;

                XAxis = new GraphAxis(this, s, e)
                {
                    UseNiceAxis = false
                };
                YAxis = new GraphAxis(this, ExperimentData.DataPoints.Where(dp => dp.Time > s && dp.Time < e).Min(dp => dp.Power), ExperimentData.DataPoints.Where(dp => dp.Time > s && dp.Time < e).Max(dp => dp.Power));
            }
        }

        internal override void Draw()
        {
            base.Draw();

            if (ShowBaseline && ExperimentData.Processor.Interpolator != null && ExperimentData.Processor.Interpolator.Finished)
            {
                DrawBaseline();

                if (ExperimentData.Processor.Interpolator is SplineInterpolator) DrawSplineHandles();
            }

            if (ShowInjections) DrawIntegrationMarkers();
        }

        void DrawBaseline()
        {
            var points = new List<CGPoint>();

            if (!ShowBaselineCorrected) for (int i = 0; i < ExperimentData.DataPoints.Count; i++)
                {
                    DataPoint p = ExperimentData.DataPoints[i];
                    Energy b = ExperimentData.Processor.Interpolator.Baseline[i];

                    if (p.Time > XAxis.Min && p.Time < XAxis.Max)
                        points.Add(GetRelativePosition(p.Time, b.Value));
                }
            else points = new List<CGPoint>
            {
                GetRelativePosition(ExperimentData.DataPoints.First().Time, 0),
                GetRelativePosition(ExperimentData.DataPoints.Last().Time, 0)
            };

            DrawDataGraph(points, 2, NSColor.SystemRedColor.CGColor);
        }

        void DrawSplineHandles()
        {
            var points = new List<CGRect>();

            foreach (var sp in (ExperimentData.Processor.Interpolator as SplineInterpolator).SplinePoints)
            {
                var m = GetRelativePosition((float)sp.Time, (float)sp.Power);

                if (ShowBaselineCorrected) m = GetRelativePosition((float)sp.Time, 0);

                var r = new CGRect(m.X - 4, m.Y - 4, 8, 8);

                points.Add(r);
            }

            foreach (var r in points)
            {
                DrawRectangle(r, 0, NSColor.SystemRedColor.CGColor);
            }
        }

        void DrawIntegrationMarkers()
        {
            CGPath path = new CGPath();

            foreach (var inj in ExperimentData.Injections)
            {
                var s = GetRelativePosition(inj.IntegrationStartTime, DataPoints.Last(dp => dp.Time < inj.IntegrationStartTime).Power);
                var e = GetRelativePosition(inj.IntegrationEndTime, DataPoints.Last(dp => dp.Time < inj.IntegrationEndTime).Power);

                var radius = 4f;
                var delta = 0.04f * (PlotFrame.Height);
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
                        //TODO DrawString((inj.ID + 1).ToString("inj #0"), DefaultFontHeight, s.X + 3, PlotPixelHeight - (maxy + delta + 20), NSColor.SystemBlueColor.CGColor);
                        break;
                    case PeakHeatDirection.Endothermal:
                        path.MoveToPoint(s.X, maxy + delta);
                        path.AddLineToPoint(s.X, miny - delta);
                        path.AddLineToPoint(e.X, miny - delta);
                        path.AddLineToPoint(e.X, maxy + delta);
                        //TODO DrawString((inj.ID + 1).ToString("inj #0"), DefaultFontHeight, s.X + 3, PlotPixelHeight - (miny - delta), NSColor.SystemBlueColor.CGColor);
                        break;
                }
            }

            StrokePath(path, NSColor.SystemBlueColor.CGColor, 1);
        }

        public bool IsCursorOnFeature(CGPoint cursorpos)
        {
            if (ShowBaseline && ExperimentData.Processor.Interpolator is SplineInterpolator)
                foreach (var sp in (ExperimentData.Processor.Interpolator as SplineInterpolator).SplinePoints)
                {
                    var handle_screen_pos = GetRelativePosition(sp.Time, sp.Power);

                    if (ShowBaselineCorrected) handle_screen_pos = GetRelativePosition(sp.Time, 0);

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

    public class CADataFittingGraph : CAGraph
    {
        public static bool UnifiedAxes { get; set; } = false;
        public static bool DrawPeakInfo { get; set; } = false;
        public static bool DrawFitParameters { get; set; } = false;

        static CGSize ErrorBarEndWidth = new CGSize(2, 0);

        public override void SetData(ExperimentData experiment)
        {
            base.SetData(experiment);

            try
            {
                if (UnifiedAxes)
                {
                    var xmin = 0;
                    var xmax = DataManager.IncludedData.Max(d => d.Injections.Last().Ratio);

                    var ymax = Math.Max(DataManager.IncludedData.Max(d => d.Injections.Max(inj => (float)inj.OffsetEnthalpy)), Math.Max(DataManager.IncludedData.Max(d => (float)d.Solution.Enthalpy), 0));
                    var ymin = Math.Min(DataManager.IncludedData.Max(d => d.Injections.Max(inj => (float)inj.OffsetEnthalpy)), Math.Min(DataManager.IncludedData.Min(d => (float)d.Solution.Enthalpy), 0));

                    XAxis = GraphAxis.WithBuffer(this, xmin, xmax, 0.05);
                    YAxis = GraphAxis.WithBuffer(this, ymin, ymax, 0.1);
                }
                else
                {
                    XAxis = GraphAxis.WithBuffer(this, 
                        0,
                        experiment.Injections.Last().Ratio,
                        0.05);

                    YAxis = GraphAxis.WithBuffer(this, 
                        Math.Min(experiment.Injections.Min(inj => (float)inj.OffsetEnthalpy), Math.Min((float)ExperimentData.Solution.Enthalpy, 0)),
                        Math.Max(experiment.Injections.Max(inj => (float)inj.OffsetEnthalpy), Math.Max((float)ExperimentData.Solution.Enthalpy, 0)),
                        0.1);
                }
            }
            catch 
            {
                try
                {
                    XAxis = GraphAxis.WithBuffer(this, 
                            0,
                            experiment.Injections.Last().Ratio,
                            0.05);

                    YAxis = GraphAxis.WithBuffer(this, experiment.Injections.Min(inj => (float)inj.OffsetEnthalpy), experiment.Injections.Max(inj => (float)inj.OffsetEnthalpy), 0.1);
                }
                catch
                {
                    XAxis = new GraphAxis(this, 0, 1);
                    YAxis = new GraphAxis(this, 0, 1);
                }
            }

            XAxis.UseNiceAxis = false;
            YAxis.UseNiceAxis = false;

        }

        internal override void Draw()
        {
            base.Draw();

            if (ExperimentData.Solution != null) DrawFit();

            if (ExperimentData.Processor.IntegrationCompleted) DrawInjectionsPoints();

            if (DrawFitParameters && ExperimentData.Solution != null) DrawParameters();
        }

        void DrawInjectionsPoints()
        {
            List<CGPoint> points = new List<CGPoint>();
            List<CGPoint> inv_points = new List<CGPoint>();
            List<CGPath> errorbars = new List<CGPath>();

            var bars = new CGPath();

            foreach (var inj in ExperimentData.Injections)
            {
                var p = GetRelativePosition(inj.Ratio, inj.OffsetEnthalpy);

                if (DrawPeakInfo)
                {
                    var sd = inj.SD / inj.InjectionMass;
                    var etop = GetRelativePosition(inj.Ratio, inj.OffsetEnthalpy + sd);
                    var ebottom = GetRelativePosition(inj.Ratio, inj.OffsetEnthalpy - sd);

                    bars.MoveToPoint(etop);
                    bars.AddLineToPoint(ebottom);

                    bars.MoveToPoint(CGPoint.Subtract(etop, ErrorBarEndWidth));
                    bars.AddLineToPoint(CGPoint.Add(etop, ErrorBarEndWidth));

                    bars.MoveToPoint(CGPoint.Subtract(ebottom, ErrorBarEndWidth));
                    bars.AddLineToPoint(CGPoint.Add(ebottom, ErrorBarEndWidth));
                }

                if (inj.ID == moverfeature) FillRectangle(
                    p,
                    size: 14, radius: 4,
                    color: IsMouseDown ? NSColor.ControlShadow.CGColor : NSColor.ControlDarkShadow.CGColor);

                if (inj.Include) points.Add(p);
                else inv_points.Add(p);
            }

            StrokePath(bars, DefaultStrokeColor, 2);

            //FillRectangles(points.ToArray(), DefaultStrokeColor);

            //DrawRectsAtPositions(layer, points.ToArray(), 8, false, true);
            //DrawRectsAtPositions(layer, inv_points.ToArray(), 8, false, false);
        }

        void DrawFit()
        {
            //CGLayer layer = CGLayer.Create(cg, PlotFrame.Size);

            List<CGPoint> points = new List<CGPoint>();

            foreach (var inj in ExperimentData.Injections)
            {
                var x = inj.Ratio;
                var y = ExperimentData.Solution.Evaluate(inj.ID, withoffset: false);

                points.Add(GetRelativePosition(x, y));
            }

            CGPath path = newPathFromPoints(points.ToArray());

            StrokePath(path, DefaultStrokeColor);

            //DrawRectsAtPositions(layer, points.ToArray(), 8, true, false, color: NSColor.PlaceholderTextColor.CGColor);

            //layer.Context.SetStrokeColor(NSColor.LabelColor.CGColor);
            //layer.Context.SetLineWidth(2);
            //layer.Context.AddPath(path);
            //layer.Context.StrokePath();

            //cg.DrawLayer(layer, PlotFrame.Location);
        }

        void DrawPeakDetails()
        {

        }

        void DrawParameters()
        {

            var zero = new CGPath();
            zero.MoveToPoint(GetRelativePosition(XAxis.Min, 0));
            zero.AddLineToPoint(GetRelativePosition(XAxis.Max, 0));

            var H = ExperimentData.Solution.Enthalpy;
            var e1 = GetRelativePosition(XAxis.Min, H);
            var e2 = GetRelativePosition(XAxis.Max, H);
            var enthalpy = new CGPath();
            enthalpy.MoveToPoint(e1);
            enthalpy.AddLineToPoint(e2);

            var N = ExperimentData.Solution.N;
            var n1 = GetRelativePosition(N, YAxis.Min);
            var n2 = GetRelativePosition(N, YAxis.Max);

            var n = new CGPath();
            n.MoveToPoint(n1);
            n.AddLineToPoint(n2);

            StrokePath(zero, DefaultStrokeColor);
            StrokePath(enthalpy, DefaultStrokeColor);
            StrokePath(n, DefaultStrokeColor);
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

        public CADataFittingGraph(IntPtr handle) : base(handle)
        {
        }

        public CADataFittingGraph(NSCoder coder) : base(coder)
        {
        }

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
            if (moverfeature != -1) { moverfeature = -1; return true; }
            return false;
        }
    }
}
