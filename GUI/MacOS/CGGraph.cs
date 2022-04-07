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
    public class CGGraph
    {
        public const float PiHalf = (float)Math.PI / 2;

        public ExperimentData ExperimentData;

        private CGContext Context { get; set; }
        public const float PPcm = 0.5f * 227 / 2.54f;

        public bool DrawOnWhite = false;
        public CGColor StrokeColor
        {
            get
            {
                if (DrawOnWhite) return NSColor.Black.CGColor;
                else return NSColor.LabelColor.CGColor;
            }
        }
        internal static CTFont DefaultFont = new CTFont("Helvetica", 12);
        internal static nfloat DefaultFontHeight => DefaultFont.CapHeightMetric + 5;
        internal static CGColor HighlightColor => NSColor.LabelColor.ColorWithAlphaComponent(0.2f).CGColor;
        internal static CGColor ActivatedHighlightColor => NSColor.LabelColor.ColorWithAlphaComponent(0.35f).CGColor;
        internal static float SquareSize = 8;

        public nfloat PlotWidthCM = 7.0f;
        public nfloat PlotPixelWidth { get => PlotWidthCM * PPcm; set => PlotWidthCM = value / PPcm; }

        public nfloat PlotHeightCM = 5.0f;
        public nfloat PlotPixelHeight { get => PlotHeightCM * PPcm; set => PlotHeightCM = value / PPcm; }

        internal CGSize PointsPerUnit;
        public CGPoint Center;
        internal CGSize PlotSize;
        internal CGPoint Origin;
        internal CGRect Frame => new CGRect(Origin, PlotSize);
        internal NSView View;
        internal bool ScaleToView = true;
        internal CGLayer CGBuffer;

        GraphAxis xaxis;
        GraphAxis yaxis;

        public GraphAxis XAxis
        {
            get => xaxis;
            set
            {
                if (value.Position == AxisPosition.Unknown)
                {
                    value.Position = AxisPosition.Bottom;
                    value.DecimalPoints = 0;
                    value.ValueFactor = 1;
                    value.LegendTitle = "Time (s)";
                }

                xaxis = value;
            }
        }
        public GraphAxis YAxis
        {
            get => yaxis;
            set
            {
                if (value.Position == AxisPosition.Unknown)
                {
                    value.Position = AxisPosition.Left;
                    value.ValueFactor = 1000000;
                    value.DecimalPoints = 2;
                    value.LegendTitle = "Differential Power (µW)";
                }

                yaxis = value;
            }
        }

        public void SetXAxisRange(double min, double max, bool buffer = false) => SetAxisRange(XAxis, min, max, buffer);
        public void SetYAxisRange(double min, double max, bool buffer = false) => SetAxisRange(YAxis, min, max, buffer);

        public bool IsMouseDown { get; set; } = false;

        public CGGraph(ExperimentData experiment, NSView view)
        {
            ExperimentData = experiment;
            View = view;
        }

        public void PrepareDraw(CGContext gc, CGPoint center)
        {
            this.Context = gc;
            this.Center = center;

            SetupFrame();

            SetupAxisScalingUnits();

            Draw(gc);

            DrawFrame(gc);

            DrawAxes();
        }

        public virtual void SetupFrame(float width = 0, float height = 0)
        {
            if (width == 0) PlotWidthCM = View.Frame.Size.Width / PPcm - 1.5f;
            else PlotWidthCM = width;

            if (height == 0) PlotHeightCM = View.Frame.Size.Height / PPcm - 0.9f;
            else PlotHeightCM = height;

            PlotSize = new CGSize(PlotPixelWidth - 2, PlotPixelHeight - 2);
            Origin = new CGPoint(Center.X - PlotSize.Width * 0.5f, Center.Y - PlotSize.Height * 0.5f);
            //Origin.X += 0.65f * PPcm; //Frame positioning should not be done here
            //Origin.Y += 0.45f * PPcm;
            //Frame = new CGRect(Origin, PlotSize);
        }

        public void SetupAxisScalingUnits()
        {
            if (Frame.Size.Width * Frame.Size.Height < 0) return;

            var pppw = PlotSize.Width / (XAxis.Max - XAxis.Min);
            var ppph = PlotSize.Height / (YAxis.Max - YAxis.Min);

            PointsPerUnit = new CGSize(pppw, ppph);
        }

        internal virtual void Draw(CGContext cg)
        {

        }

        public void DrawFrame(CGContext gc)
        {
            gc.SetStrokeColor(StrokeColor);
            gc.StrokeRectWithWidth(Frame, 1);
        }

        #region Drawing

        #region Drawing Methods

        public void DrawDataSeries(CGContext gc, List<CGPoint> points, float linewidth = 1, CGColor color = null)
        {
            var layer = CGLayer.Create(gc, Frame.Size);

            if (points.Count == 0) return;
            if (points.Count == 1) { DrawCircle(gc, points[0], 2, true, color); return; }

            var path = new CGPath();

            path.MoveToPoint(points[0]);

            for (int i = 1; i < points.Count; i++)
            {
                CGPoint p = points[i];
                path.AddLineToPoint(p);
            }

            layer.Context.AddPath(path);
            layer.Context.SetStrokeColor(color);
            layer.Context.SetLineWidth(linewidth);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Origin);
        }

        public void DrawRectsAtPositions(CGLayer layer, CGPoint[] points, float size, bool circle = false, bool fill = false, float width = 1, CGColor color = null, float roundedradius = 0)
        {
            foreach (var p in points)
            {
                if (circle) AddCircleAtPosition(layer, p, size);
                else AddRectAtPosition(layer, p, size, roundedradius);
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

        public void DrawCircle(CGContext gc, CGPoint position, float radius, bool fill = false, CGColor color = null)
        {
            gc.SetStrokeColor(color);
            gc.SetFillColor(color);

            if (fill) gc.FillEllipseInRect(GetRectAtPosition(position, radius));
            else gc.StrokeEllipseInRect(GetRectAtPosition(position, radius));
        }

        public void DrawSpline(CGContext gc, CGPoint[] points, float linewidth, CGColor color = null)
        {
            var path = new CGPath();
            int pointCount = points.Length;

            if (pointCount > 0)
            {
                CGPoint p0 = points.First();
                path.MoveToPoint(p0);

                if (pointCount == 1) //draw dot
                {
                    CGRect pointRect = new CGRect(p0.X - linewidth / 2.0, p0.Y - linewidth / 2.0, linewidth, linewidth);
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
                        CGPoint midPoint = new CGPoint((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
                        path.AddQuadCurveToPoint(p1.X, p1.Y, midPoint.X, midPoint.Y);
                        p1 = p2;
                    }
                    path.AddLineToPoint(points.Last());
                }
            }

            DrawPath(gc, path, linewidth, color);
        }

        void DrawPath(CGContext gc, CGPath path, float linewidth = 1, CGColor color = null)
        {
            var layer = CGLayer.Create(gc, Frame.Size);

            DrawPathToLayer(layer, path, linewidth, color);

            gc.DrawLayer(layer, Frame.Location);
        }

        #endregion

        #region Add shapes to existing layer functions

        void DrawPathToLayer(CGLayer layer, CGPath path, float linewidth = 1, CGColor color = null)
        {
            layer.Context.AddPath(path);
            layer.Context.SetStrokeColor(color);
            layer.Context.SetLineWidth(linewidth);
            layer.Context.StrokePath();
        }

        internal void AddRectAtPosition(CGLayer layer, CGPoint p, double size, float roundedradius)
        {
            var rect = GetRectAtPosition(p, size);

            if (roundedradius > 0) layer.Context.AddPath(CGPath.FromRoundedRect(rect, roundedradius, roundedradius));
            else layer.Context.AddRect(rect);
        }

        internal void AddCircleAtPosition(CGLayer layer, CGPoint p, double size)
        {
            var rect = GetRectAtPosition(p, size);

            layer.Context.AddPath(CGPath.EllipseFromRect(rect));
        }

        #endregion

        CGRect GetRectAtPosition(CGPoint point, double size)
        {
            return new CGRect(point.X - size / 2, point.Y - size / 2, size, size);
        }

        #endregion

        internal virtual void DrawAxes()
        {

        }

        public CGSize DrawString(CGLayer layer, string s, CGPoint position, CTFont font, CTStringAttributes attr = null, TextAlignment horizontalignment = TextAlignment.Center, TextAlignment verticalalignment = TextAlignment.Center, CGColor textcolor = null, float rotation = 0)
        {
            if (textcolor == null) textcolor = StrokeColor;
            if (attr == null) attr = new CTStringAttributes
                {
                    ForegroundColorFromContext = true,
                    Font = font,
                    StrokeColor = textcolor,
                };

            var attributedString = new NSAttributedString(s, attr);

            var size = attributedString.Size;
            size.Height = font.CapHeightMetric;
            var textLine = new CTLine(attributedString);

            CGPoint ctm = new CGPoint(0, 0);

            switch (horizontalignment)
            {
                case TextAlignment.Right:
                    ctm.X -= size.Width;
                    break;
                case TextAlignment.Center:
                    ctm.X -= size.Width / 2;
                    break;
            }

            switch (verticalalignment)
            {
                case TextAlignment.Top:
                    ctm.Y -= size.Height;
                    break;
                case TextAlignment.Center:
                    ctm.Y -= size.Height / 2;
                    break;
            }

            layer.Context.SaveState();
            layer.Context.TranslateCTM(position.X, position.Y);
            layer.Context.RotateCTM(rotation);
            layer.Context.TranslateCTM(ctm.X, ctm.Y);
            layer.Context.TextPosition = new CGPoint(0, 0);// position;
            textLine.Draw(layer.Context);
            layer.Context.RestoreState();
            textLine.Dispose();

            return size;
        }

        public void DrawText(CGContext context, string text, nfloat textHeight, nfloat x, nfloat y, CGColor textcolor = null)
        {
            if (textcolor == null) textcolor = NSColor.LabelColor.CGColor;


            y = (nfloat)PlotPixelHeight - y - textHeight;
            context.SetFillColor(StrokeColor);

            var attributedString = new NSAttributedString(text,
                new CTStringAttributes
                {
                    ForegroundColorFromContext = true,
                    Font = DefaultFont,
                    StrokeColor = textcolor,
                });

            var textLine = new CTLine(attributedString);

            context.TextPosition = new CGPoint(x, y);
            textLine.Draw(context);

            textLine.Dispose();
        }

        internal CGPoint GetRelativePosition(DataPoint dp, GraphAxis axis = null)
        {
            return GetRelativePosition(dp.Time, dp.Power, axis);
        }

        internal virtual CGPoint GetRelativePosition(double x, double y, GraphAxis axis = null)
        {
            switch (axis)
            {
                case null:
                    return new CGPoint((x - XAxis.Min) * PointsPerUnit.Width, (y - YAxis.Min) * PointsPerUnit.Height);
                default:
                    if (axis.IsHorizontal)
                    {
                        var rely = (y - YAxis.Min) * PointsPerUnit.Height;
                        var pppw = PlotSize.Width / (axis.Max - axis.Min);
                        return new CGPoint((x - axis.Min) * pppw, rely);
                    }
                    else
                    {
                        var relx = (x - XAxis.Min) * PointsPerUnit.Width;
                        var ppph = PlotSize.Height / (axis.Max - axis.Min);
                        return new CGPoint(relx, (y - axis.Min) * ppph);
                    }
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

        public virtual bool IsCursorOnFeature(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            return false;
        }
    }

    public class DataGraph : CGGraph
    {
        bool showBaselineCorrected = false;
        public bool ShowBaselineCorrected
        {
            get => showBaselineCorrected;
            set
            {
                showBaselineCorrected = value;

                SetYAxisRange(DataPoints.Min(dp => dp.Power), DataPoints.Max(dp => dp.Power), buffer: true);
            }
        }

        public List<DataPoint> DataPoints => ShowBaselineCorrected && ExperimentData.BaseLineCorrectedDataPoints != null
                    ? ExperimentData.BaseLineCorrectedDataPoints
                    : ExperimentData.DataPoints;

        public DataGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
            XAxis = new GraphAxis(this, 0, DataPoints.Max(dp => dp.Time))
            {
                UseNiceAxis = false,
            };
            YAxis = new GraphAxis(this, DataPoints.Min(dp => dp.Power), DataPoints.Max(dp => dp.Power));
        }

        internal override void Draw(CGContext gc)
        {
            var points = new List<CGPoint>();

            foreach (var p in DataPoints) { if (p.Time > XAxis.Min && p.Time < XAxis.Max) points.Add(GetRelativePosition(p)); }

            gc.SetStrokeColor(StrokeColor);
            DrawDataSeries(gc, points);

            XAxis.Draw(gc);
            YAxis.Draw(gc);
        }
    }

    public class FileInfoGraph : DataGraph
    {
        List<string> info;

        GraphAxis TemperatureAxis { get; set; }

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

            float tamid = experiment.TargetTemperature;
            float delta = Math.Max(Math.Abs(experiment.DataPoints.Min(dp => Math.Min(dp.Temperature, dp.ShieldT)) - tamid), Math.Abs(experiment.DataPoints.Max(dp => Math.Max(dp.Temperature, dp.ShieldT)) - tamid));
            TemperatureAxis = GraphAxis.WithBuffer(this, tamid - delta, tamid + delta, position: AxisPosition.Right);
            TemperatureAxis.Position = AxisPosition.Right;
            TemperatureAxis.TickScale.SetMaxTicks(5);
            TemperatureAxis.LegendTitle = "Temperature (°C)";
        }

        public override void SetupFrame(float w = 0, float h = 0)
        {
            base.SetupFrame(w,h);

            PlotWidthCM = View.Frame.Size.Width / PPcm - 3;

            var infoheight = (DefaultFontHeight + 5) * info.Count + 10;

            PlotSize = new CGSize(PlotPixelWidth - 2, PlotPixelHeight - infoheight - 2);
            Origin = new CGPoint(Center.X - PlotSize.Width * 0.5f, Center.Y - (PlotSize.Height * 0.5f));
            //Frame = new CGRect(Origin, PlotSize);
        }

        internal override void Draw(CGContext gc)
        {
            DrawInfo(gc);

            DrawTemperature(gc);

            TemperatureAxis.Draw(gc);

            base.Draw(gc);
        }

        void DrawTemperature(CGContext gc)
        {
            var temperature = new List<CGPoint>();
            var jacket = new List<CGPoint>();

            for (int i = 0; i < DataPoints.Count - 1; i += 10)
            {
                DataPoint p = DataPoints[i];
                if (p.Time > XAxis.Min && p.Time < XAxis.Max)
                {
                    temperature.Add(GetRelativePosition(p.Time, p.Temperature, TemperatureAxis));
                    jacket.Add(GetRelativePosition(p.Time, p.ShieldT, TemperatureAxis));
                }
            }

            temperature.Add(GetRelativePosition(DataPoints.Last().Time, DataPoints.Last().Temperature, TemperatureAxis));
            jacket.Add(GetRelativePosition(DataPoints.Last().Time, DataPoints.Last().ShieldT, TemperatureAxis));

            DrawDataSeries(gc, temperature, 1, NSColor.SystemRedColor.CGColor);
            DrawDataSeries(gc, jacket, 1, NSColor.SystemRedColor.CGColor);
        }

        public void DrawInfo(CGContext gc)
        {
            var layer = CGLayer.Create(gc, View.Frame.Size);

            var pos = new CGPoint(5, View.Frame.Height);

            foreach (var l in info)
            {
                var s = DrawString(layer, l, pos, DefaultFont, null, TextAlignment.Left, TextAlignment.Top);

                pos.Y -= (s.Height + 5);
            }

            gc.DrawLayer(layer, new CGPoint(0, 0));
        }
    }

    public class BaselineFittingGraph : DataGraph
    {
        public static bool ShowBaseline { get; set; } = true;
        public static bool ShowInjections { get; set; } = true;

        public BaselineFittingGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
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

        internal override void Draw(CGContext gc)
        {
            base.Draw(gc);

            if (ShowBaseline && ExperimentData.Processor.Interpolator != null && ExperimentData.Processor.Interpolator.Finished)
            {
                DrawBaseline(gc);

                if (ExperimentData.Processor.Interpolator is SplineInterpolator) DrawSplineHandles(gc);
            }

            if (ShowInjections) DrawIntegrationMarkers(gc);
        }

        void DrawBaseline(CGContext gc)
        {
            CGLayer layer = CGLayer.Create(gc, Frame.Size);

            var path = new CGPath();

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

            path.AddLines(points.ToArray());

            layer.Context.AddPath(path);
            layer.Context.SetStrokeColor(NSColor.Red.CGColor);
            layer.Context.SetLineWidth(2);
            layer.Context.StrokePath();
            layer.Context.SetLineWidth(1);

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawSplineHandles(CGContext gc)
        {
            CGLayer layer = CGLayer.Create(gc, Frame.Size);

            List<CGRect> points = new List<CGRect>();

            foreach (var sp in (ExperimentData.Processor.Interpolator as SplineInterpolator).SplinePoints)
            {
                var m = GetRelativePosition((float)sp.Time, (float)sp.Power);

                if (ShowBaselineCorrected) m = GetRelativePosition((float)sp.Time, 0);

                var r = new CGRect(m.X - 4, m.Y - 4, 8, 8);

                points.Add(r);
            }

            layer.Context.SetFillColor(NSColor.Red.CGColor);

            foreach (var r in points)
            {
                layer.Context.FillRect(r);
            }


            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawIntegrationMarkers(CGContext gc)
        {
            CGLayer layer = CGLayer.Create(gc, Frame.Size);

            CGPath path = new CGPath();

            foreach (var inj in ExperimentData.Injections)
            {
                var s = GetRelativePosition(inj.IntegrationStartTime, DataPoints.Last(dp => dp.Time < inj.IntegrationStartTime).Power);
                var e = GetRelativePosition(inj.IntegrationEndTime, DataPoints.Last(dp => dp.Time < inj.IntegrationEndTime).Power);

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
                        path.AddArcToPoint(s.X, maxy + delta, e.X - deltax / 2, maxy + delta, radius);
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

            gc.DrawLayer(layer, Frame.Location);
        }

        public override bool IsCursorOnFeature(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            if (ShowBaseline && ExperimentData.Processor.Interpolator is SplineInterpolator)
                foreach (var sp in (ExperimentData.Processor.Interpolator as SplineInterpolator).SplinePoints)
                {
                    var handle_screen_pos = GetRelativePosition(sp.Time, sp.Power) + new CGSize(Origin);

                    if (ShowBaselineCorrected) handle_screen_pos = GetRelativePosition(sp.Time, 0) + new CGSize(Origin);

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

    public class DataFittingGraph : CGGraph
    {
        public static bool UnifiedAxes { get; set; } = false;
        public static bool DrawPeakInfo { get; set; } = false;
        public static bool DrawFitParameters { get; set; } = false;

        static CGSize ErrorBarEndWidth = new CGSize(SquareSize / 2, 0);

        public DataFittingGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
            if (UnifiedAxes)
            {
                var xmin = 0;
                var xmax = DataManager.IncludedData.Max(d => d.Injections.Last().Ratio);

                var ymax = Math.Max(DataManager.IncludedData.Max(d => d.Injections.Max(inj => (float)inj.OffsetEnthalpy)), Math.Max(DataManager.IncludedData.Max(d => (float)d.Solution.Enthalpy), 0));
                var ymin = Math.Min(DataManager.IncludedData.Max(d => d.Injections.Max(inj => (float)inj.OffsetEnthalpy)), Math.Min(DataManager.IncludedData.Min(d => (float)d.Solution.Enthalpy), 0));

                XAxis = GraphAxis.WithBuffer(this, xmin, xmax, 0.05, AxisPosition.Bottom);
                YAxis = GraphAxis.WithBuffer(this, ymin, ymax, 0.1, AxisPosition.Left);
            }
            else
            {
                XAxis = GraphAxis.WithBuffer(this, 0, experiment.Injections.Last().Ratio, 0.05, AxisPosition.Bottom);

                var solutionenthalpy = ExperimentData.Solution != null ? (float)ExperimentData.Solution.Enthalpy : 0;
                var minenthalpy = Math.Min(experiment.Injections.Min(inj => (float)inj.OffsetEnthalpy), Math.Min(solutionenthalpy, 0));
                var maxenthalpy = Math.Max(experiment.Injections.Max(inj => (float)inj.OffsetEnthalpy), Math.Max(solutionenthalpy, 0));

                YAxis = GraphAxis.WithBuffer(this, minenthalpy, maxenthalpy, 0.1, AxisPosition.Left);
            }

            XAxis.SetWithBuffer(0, Math.Max(Math.Floor(XAxis.Max + 0.33f), XAxis.Max), 0.05);
            XAxis.UseNiceAxis = false;
            XAxis.LegendTitle = "Molar Ratio";
            XAxis.DecimalPoints = 1;
            XAxis.TickScale.SetMaxTicks(7);
            YAxis.UseNiceAxis = false;
            YAxis.LegendTitle = "Heat per Injectant (kJ/mol)";
            YAxis.ValueFactor = 0.001f;

        }

        internal override void Draw(CGContext gc)
        {
            base.Draw(gc);

            DrawGrid(gc);

            if (ExperimentData.Solution != null) DrawFit(gc);

            if (ExperimentData.Processor.IntegrationCompleted) DrawInjectionsPoints(gc);

            if (DrawFitParameters && ExperimentData.Solution != null) DrawParameters(gc);

            XAxis.Draw(gc);
            YAxis.Draw(gc);
        }

        void DrawInjectionsPoints(CGContext gc)
        {
            var layer = CGLayer.Create(gc, Frame.Size);
            var points = new List<CGPoint>();
            var inv_points = new List<CGPoint>();

            var bars = new CGPath();

            foreach (var inj in ExperimentData.Injections)
            {
                var p = GetRelativePosition(inj.Ratio, inj.OffsetEnthalpy);

                if (DrawPeakInfo)
                {
                    var sd = Math.Abs(inj.SD / inj.PeakArea);
                    var etop = GetRelativePosition(inj.Ratio, inj.OffsetEnthalpy - inj.Enthalpy * sd);
                    var ebottom = GetRelativePosition(inj.Ratio, inj.OffsetEnthalpy + inj.Enthalpy * sd);

                    if (etop.Y - p.Y > SquareSize / 2)
                    {
                        bars.MoveToPoint(etop);
                        bars.AddLineToPoint(CGPoint.Add(p, new CGSize(0, SquareSize / 2)));

                        bars.MoveToPoint(ebottom);
                        bars.AddLineToPoint(CGPoint.Subtract(p, new CGSize(0, SquareSize / 2)));

                        bars.MoveToPoint(CGPoint.Subtract(etop, ErrorBarEndWidth));
                        bars.AddLineToPoint(CGPoint.Add(etop, ErrorBarEndWidth));

                        bars.MoveToPoint(CGPoint.Subtract(ebottom, ErrorBarEndWidth));
                        bars.AddLineToPoint(CGPoint.Add(ebottom, ErrorBarEndWidth));
                    }
                }

                if (inj.ID == moverfeature) DrawRectsAtPositions(
                    layer, new CGPoint[] { p },
                    size: 14, circle: false, fill: true, width: 0, roundedradius: 4,
                    color: IsMouseDown ? ActivatedHighlightColor : HighlightColor);

                if (inj.Include) points.Add(p);
                else inv_points.Add(p);
            }

            layer.Context.SetFillColor(NSColor.ControlText.CGColor);
            layer.Context.SetStrokeColor(NSColor.ControlText.CGColor);
            layer.Context.SetLineWidth(1);

            layer.Context.AddPath(bars);
            layer.Context.StrokePath();
            DrawRectsAtPositions(layer, points.ToArray(), SquareSize, false, true);
            DrawRectsAtPositions(layer, inv_points.ToArray(), SquareSize, false, false);

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawFit(CGContext gc)
        {
            
            var points = new List<CGPoint>();

            foreach (var inj in ExperimentData.Injections)
            {
                var x = inj.Ratio;
                var y = ExperimentData.Solution.Evaluate(inj.ID, withoffset: false);

                points.Add(GetRelativePosition(x, y));
            }

            DrawSpline(gc, points.ToArray(), 2, StrokeColor);

            //DrawRectsAtPositions(layer, points.ToArray(), 8, true, false, color: NSColor.PlaceholderTextColor.CGColor);
        }

        void DrawGrid(CGContext gc, bool onlyzero = false)
        {
            CGLayer layer = CGLayer.Create(gc, Frame.Size);
            layer.Context.SetStrokeColor(NSColor.LabelColor.ColorWithAlphaComponent(0.4f).CGColor);
            layer.Context.SetLineWidth(1);

            var zero = new CGPath();
            zero.MoveToPoint(GetRelativePosition(XAxis.Min, 0));
            zero.AddLineToPoint(GetRelativePosition(XAxis.Max, 0));
            layer.Context.AddPath(zero);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawParameters(CGContext gc)
        {
            CGLayer layer = CGLayer.Create(gc, Frame.Size);
            layer.Context.SetStrokeColor(NSColor.LabelColor.ColorWithAlphaComponent(0.4f).CGColor);
            layer.Context.SetLineWidth(1);

            var H = ExperimentData.Solution.Enthalpy;
            var e1 = GetRelativePosition(XAxis.Min, H);
            var e2 = GetRelativePosition(XAxis.Max, H);
            var enthalpy = new CGPath();
            enthalpy.MoveToPoint(e1);
            enthalpy.AddLineToPoint(e2);
            layer.Context.AddPath(enthalpy);

            var N = ExperimentData.Solution.N;
            var n1 = GetRelativePosition(N, YAxis.Min);
            var n2 = GetRelativePosition(N, YAxis.Max);

            var n = new CGPath();
            n.MoveToPoint(n1);
            n.AddLineToPoint(n2);
            layer.Context.AddPath(n);

            layer.Context.SetLineDash(3, new nfloat[] { 3 });
            layer.Context.StrokePath();

            var points = new List<CGPoint>();

            foreach (var inj in ExperimentData.Injections)
            {
                var x = inj.Ratio;
                var y = ExperimentData.Solution.Evaluate(inj.ID, withoffset: false);

                points.Add(GetRelativePosition(x, y));
            }

            DrawRectsAtPositions(layer, points.ToArray(), 8, true, false, color: NSColor.PlaceholderTextColor.CGColor);

            gc.DrawLayer(layer, Frame.Location);
        }

        int mdownid = -1;
        int moverfeature = -1;

        public override bool IsCursorOnFeature(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            foreach (var inj in ExperimentData.Injections)
            {
                var handle_screen_pos = GetRelativePosition(inj.Ratio, inj.OffsetEnthalpy) + new CGSize(Origin);

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