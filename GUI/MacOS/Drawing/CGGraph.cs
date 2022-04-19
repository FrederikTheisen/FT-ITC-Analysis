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
        public CGColor StrokeColor => DrawOnWhite ? NSColor.Black.CGColor : NSColor.Label.CGColor;
        public CGColor SecondaryLineColor => DrawOnWhite ? NSColor.Black.ColorWithAlphaComponent(0.5f).CGColor : NSColor.TertiaryLabel.CGColor;

        internal static CTFont DefaultFont = new CTFont("Helvetica", 12);
        internal static nfloat DefaultFontHeight => DefaultFont.CapHeightMetric + 5;
        internal static CGColor HighlightColor => NSColor.Label.ColorWithAlphaComponent(0.2f).CGColor;
        internal static CGColor ActivatedHighlightColor => NSColor.Label.ColorWithAlphaComponent(0.35f).CGColor;
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

            AutoSetFrame();

            SetupAxisScalingUnits();

            Draw(gc);

            DrawFrame(gc);

            DrawAxes();
        }

        public virtual void AutoSetFrame()
        {
            var ymargin = YAxis.EstimateLabelMargin();
            var xmargin = XAxis.EstimateLabelMargin();

            PlotSize = new CGSize(View.Frame.Width - ymargin - 1, View.Frame.Height - xmargin - 1);
            Origin = new CGPoint(ymargin, xmargin);
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

        public void DrawDataSeries(CGContext gc, List<CGPoint> points, float linewidth, CGColor color)
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
            if (color == null) color = StrokeColor;

            foreach (var p in points)
            {
                if (circle) AddCircleAtPosition(layer, p, size);
                else AddRectAtPosition(layer, p, size, roundedradius);
            }

            if (fill) layer.Context.SetFillColor(color);
            else layer.Context.SetStrokeColor(color);

            layer.Context.SetLineWidth(width);
            if (fill) layer.Context.FillPath();
            else layer.Context.StrokePath();
        }

        public void DrawCircle(CGContext gc, CGPoint position, float radius, bool fill = false, CGColor color = null)
        {
            if (color == null) color = StrokeColor;

            gc.SetStrokeColor(color);
            gc.SetFillColor(color);

            if (fill) gc.FillEllipseInRect(GetRectAtPosition(position, radius));
            else gc.StrokeEllipseInRect(GetRectAtPosition(position, radius));
        }

        public void DrawSpline(CGContext gc, CGPoint[] points, float linewidth, CGColor color)
        {
            var path = GetSplineFrommPoints(points);

            DrawPath(gc, path, linewidth, color);
        }

        void DrawPath(CGContext gc, CGPath path, float linewidth, CGColor color)
        {
            var layer = CGLayer.Create(gc, Frame.Size);

            DrawPathToLayer(layer, path, linewidth, color);

            gc.DrawLayer(layer, Frame.Location);
        }

        public void FillPathShape(CGContext gc, CGPath path, CGColor color, float alpha = -1f)
        {
            if (alpha > 0) color = new CGColor(color, alpha);

            var layer = CGLayer.Create(gc, Frame.Size);
            layer.Context.SetFillColor(color);
            layer.Context.AddPath(path);
            layer.Context.FillPath();

            gc.DrawLayer(layer, Frame.Location);
        }

        #endregion

        #region Add shapes to existing layer functions

        void DrawPathToLayer(CGLayer layer, CGPath path, float linewidth, CGColor color)
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

        public CGPath GetSplineFrommPoints(CGPoint[] points, CGPath path = null, float linewidth = 1)
        {
            bool continued = path != null;
            if (path == null) path = new CGPath();

            int pointCount = points.Length;

            if (pointCount > 0)
            {
                CGPoint p0 = points.First();
                if (!continued) path.MoveToPoint(p0);
                else path.AddLineToPoint(p0);

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

            return path;
        }

        #endregion

        internal virtual void DrawAxes()
        {

        }

        public static CGSize MeasureString(string s, CTFont font, CTStringAttributes attr = null, AxisPosition position = AxisPosition.Bottom, bool ignoreoptical = true)
        {
            if (attr == null) attr = new CTStringAttributes
            {
                ForegroundColorFromContext = true,
                Font = font,
            };

            var attributedString = new NSAttributedString(s, attr);

            var size = attributedString.Size;
            //size.Height = font.CapHeightMetric;

            //size = attributedString.BoundingRectWithSize(new CGSize(nfloat.MaxValue, nfloat.MaxValue), NSStringDrawingOptions.UsesDeviceMetrics).Size;
            var textLine = new CTLine(attributedString);
            
            if (!ignoreoptical && (position == AxisPosition.Bottom || position == AxisPosition.Right)) size = textLine.GetBounds(CTLineBoundsOptions.UseOpticalBounds).Size;
            else size = textLine.GetBounds(CTLineBoundsOptions.UseGlyphPathBounds).Size;

            textLine.Dispose();

            return size;
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

            //var _size = attributedString.Size;
            //_size.Height = font.CapHeightMetric;

            var textLine = new CTLine(attributedString);

            var boxsize = textLine.GetBounds(CTLineBoundsOptions.UseOpticalBounds).Size;
            var size = textLine.GetBounds(CTLineBoundsOptions.UseGlyphPathBounds).Size;

            CGPoint ctm = new CGPoint(0, 0);

            switch (horizontalignment)
            {
                case TextAlignment.Right: ctm.X -= boxsize.Width; break;
                case TextAlignment.Center: ctm.X -= boxsize.Width / 2; break;
            }

            switch (verticalalignment)
            {
                case TextAlignment.Top: ctm.Y -= size.Height; break;
                case TextAlignment.Center: ctm.Y -= size.Height / 2; break;
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

            if (buffer) axis.SetWithBuffer((double)min, (double)max);
            else axis.Set((float)min, (float)max);
        }

        public virtual MouseOverFeatureEvent IsCursorOnFeature(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            return new MouseOverFeatureEvent();
        }
    }

    public class DataGraph : CGGraph
    {
        private bool _useUnifiedAxes = false;
        private bool _showBaselineCorrected = false;

        public bool ShowBaselineCorrected
        {
            get => _showBaselineCorrected;
            set
            {
                bool _value = _showBaselineCorrected;
                _showBaselineCorrected = value;

                if (value != _value) SetYAxisRange(DataPoints.Min(dp => dp.Power), DataPoints.Max(dp => dp.Power), buffer: true);
            }
        }

        public bool UseUnifiedAxes { get => _useUnifiedAxes; set { _useUnifiedAxes = value; SetupYAxes(); } }

        public List<DataPoint> DataPoints => ShowBaselineCorrected && ExperimentData.BaseLineCorrectedDataPoints != null
                    ? ExperimentData.BaseLineCorrectedDataPoints
                    : ExperimentData.DataPoints;

        public DataGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
            XAxis = new GraphAxis(this, 0, DataPoints.Max(dp => dp.Time))
            {
                UseNiceAxis = false,
            };

            SetupYAxes();
        }

        void SetupYAxes()
        {
            if (UseUnifiedAxes)
            {
                var ymin = DataManager.IncludedData.Min(d => d.BaseLineCorrectedDataPoints.Min(dp => dp.Power));
                var ymax = DataManager.IncludedData.Max(d => d.BaseLineCorrectedDataPoints.Max(dp => dp.Power));

                YAxis = new GraphAxis(this, ymin, ymax);
            }
            else YAxis = new GraphAxis(this, DataPoints.Min(dp => dp.Power), DataPoints.Max(dp => dp.Power));
        }

        internal override void Draw(CGContext gc)
        {
            var points = new List<CGPoint>();

            foreach (var p in DataPoints) { if (p.Time > XAxis.Min && p.Time < XAxis.Max) points.Add(GetRelativePosition(p)); }

            gc.SetStrokeColor(StrokeColor);
            DrawDataSeries(gc, points, 1, StrokeColor);

            XAxis.Draw(gc);
            YAxis.Draw(gc);
        }
    }

    public class FileInfoGraph : DataGraph
    {
        GraphAxis TemperatureAxis { get; set; }
        public List<string> Info { get; private set; }

        public FileInfoGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
            Info = new List<string>()
                {
                    "Filename: " + experiment.FileName,
                    "Date: " + experiment.Date.ToLocalTime().ToLongDateString() + " " + experiment.Date.ToLocalTime().ToString("HH:mm"),
                    "Temperature | Target: " + experiment.TargetTemperature.ToString() + " °C | Measured: " + experiment.MeasuredTemperature.ToString("G4") + " °C",
                    "Injections: " + experiment.InjectionCount.ToString(),
                    "Concentrations | Cell: " + (experiment.CellConcentration*1000000).ToString("G3") + " µM | Syringe: " + (experiment.SyringeConcentration*1000000).ToString("G3") + " µM",
                };

            var tamid = experiment.TargetTemperature;
            var delta = Math.Max(Math.Abs(experiment.DataPoints.Min(dp => Math.Min(dp.Temperature, dp.ShieldT)) - tamid), Math.Abs(experiment.DataPoints.Max(dp => Math.Max(dp.Temperature, dp.ShieldT)) - tamid));
            TemperatureAxis = GraphAxis.WithBuffer(this, tamid - delta, tamid + delta, position: AxisPosition.Right);
            TemperatureAxis.Position = AxisPosition.Right;
            TemperatureAxis.TickScale.SetMaxTicks(5);
            TemperatureAxis.LegendTitle = "Temperature (°C)";
        }

        public override void AutoSetFrame()
        {
            base.AutoSetFrame();

            //var infoheight = (DefaultFontHeight + 5) * info.Count + 10;

            //PlotSize.Height -= infoheight;// = new CGSize(View.Frame.Width - YAxis.EstimateLabelMargin() - TemperatureAxis.EstimateLabelMargin(), View.Frame.Height - XAxis.EstimateLabelMargin() - infoheight);
            PlotSize.Width -= TemperatureAxis.EstimateLabelMargin();
            //Origin = new CGPoint(Center.X - PlotSize.Width * 0.5f, Center.Y - (PlotSize.Height + infoheight) * 0.5f + XAxis.EstimateLabelMargin());
        }

        internal override void Draw(CGContext gc)
        {
            //DrawInfo(gc);

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

            DrawDataSeries(gc, temperature, 1, NSColor.SystemRed.CGColor);
            DrawDataSeries(gc, jacket, 1, NSColor.SystemRed.CGColor);
        }

        public void DrawInfo(CGContext gc)
        {
            var layer = CGLayer.Create(gc, View.Frame.Size);

            var pos = new CGPoint(5, View.Frame.Height);

            foreach (var l in Info)
            {
                var s = DrawString(layer, l, pos, DefaultFont, null, TextAlignment.Left, TextAlignment.Top);

                pos.Y -= (s.Height + 5);
            }

            gc.DrawLayer(layer, new CGPoint(0, 0));
        }
    }

    public class BaselineFittingGraph : DataGraph
    {
        public bool ShowBaseline { get; set; } = true;
        public bool ShowInjections { get; set; } = true;

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
                        points.Add(GetRelativePosition(p.Time, b.FloatWithError));
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

        public override MouseOverFeatureEvent IsCursorOnFeature(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            int id = 0;

            if (ShowBaseline && ExperimentData.Processor.Interpolator is SplineInterpolator)
                foreach (var sp in (ExperimentData.Processor.Interpolator as SplineInterpolator).SplinePoints)
                {
                    var handle_screen_pos = GetRelativePosition(sp.Time, sp.Power) + new CGSize(Origin);

                    if (ShowBaselineCorrected) handle_screen_pos = GetRelativePosition(sp.Time, 0) + new CGSize(Origin);

                    if (Math.Abs(cursorpos.X - 2 - handle_screen_pos.X) < 5)
                    {
                        if (Math.Abs(cursorpos.Y - handle_screen_pos.Y) < 5)
                        {
                            return new MouseOverFeatureEvent()
                            {
                                FeatureID = id
                            };
                        }
                    }

                    id++;
                }

            return new MouseOverFeatureEvent();
        }
    }

    public class DataFittingGraph : CGGraph
    {
        private bool _useUnifiedAxes = false;

        public bool UseUnifiedAxes { get => _useUnifiedAxes; set { _useUnifiedAxes = value; SetupAxes(); } }
        public bool ShowPeakInfo { get; set; } = false;
        public bool ShowErrorBars { get; set; } = false;
        public bool ShowFitParameters { get; set; } = false;
        public bool ShowGrid { get; set; } = true;
        public bool ShowZero { get; set; } = true;
        public bool HideBadData { get; set; } = false;
        public bool HideBadDataErrorBars { get; set; } = false;

        static CGSize ErrorBarEndWidth = new CGSize(SquareSize / 2, 0);

        public DataFittingGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
            SetupAxes();
        }

        void SetupAxes()
        {
            if (UseUnifiedAxes)
            {
                var xmin = 0;
                var xmax = DataManager.IncludedData.Max(d => d.Injections.Last().Ratio);

                var ymax = Math.Max(DataManager.IncludedData.Max(d => d.Injections.Max(inj => (float)inj.OffsetEnthalpy)), 0);
                var ymin = Math.Min(DataManager.IncludedData.Max(d => d.Injections.Max(inj => (float)inj.OffsetEnthalpy)), 0);

                if (DataManager.AnyDataIsAnalyzed)
                {
                    ymax = Math.Max(ymax, DataManager.IncludedData.Where(d => d.Solution != null).Max(d => (float)d.Solution.Enthalpy));
                    ymin = Math.Min(ymin, DataManager.IncludedData.Where(d => d.Solution != null).Min(d => (float)d.Solution.Enthalpy));
                }

                XAxis = GraphAxis.WithBuffer(this, xmin, xmax, 0.05, AxisPosition.Bottom);
                YAxis = GraphAxis.WithBuffer(this, ymin, ymax, 0.1, AxisPosition.Left);
            }
            else
            {
                XAxis = GraphAxis.WithBuffer(this, 0, ExperimentData.Injections.Last().Ratio, 0.05, AxisPosition.Bottom);

                var solutionenthalpy = ExperimentData.Solution != null ? (float)ExperimentData.Solution.Enthalpy : 0;
                var minenthalpy = Math.Min(ExperimentData.Injections.Min(inj => (float)inj.OffsetEnthalpy), Math.Min(solutionenthalpy, 0));
                var maxenthalpy = Math.Max(ExperimentData.Injections.Max(inj => (float)inj.OffsetEnthalpy), Math.Max(solutionenthalpy, 0));

                YAxis = GraphAxis.WithBuffer(this, minenthalpy, maxenthalpy, 0.1, AxisPosition.Left);
            }

            XAxis.SetWithBuffer(0, Math.Max(Math.Floor(XAxis.Max + 0.33f), XAxis.Max), 0.05);
            XAxis.UseNiceAxis = false;
            XAxis.LegendTitle = "Molar Ratio";
            XAxis.DecimalPoints = 1;
            XAxis.TickScale.SetMaxTicks(7);
            YAxis.UseNiceAxis = false;
            YAxis.LegendTitle = "kJ mol⁻¹ of injectant";
            YAxis.ValueFactor = 0.001f;
        }

        internal override void Draw(CGContext gc)
        {
            base.Draw(gc);

            if (ShowGrid) DrawGrid(gc);

            DrawZero(gc);

            if (ExperimentData.Solution != null)
            {
                DrawConfidenceInterval(gc);
                DrawFit(gc);
            }

            if (ExperimentData.Processor.IntegrationCompleted) DrawInjectionsPoints(gc);

            if (ShowFitParameters && ExperimentData.Solution != null) DrawParameters(gc);

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
                if (HideBadData && !inj.Include) continue;

                var p = GetRelativePosition(inj.Ratio, inj.OffsetEnthalpy);

                if ((ShowPeakInfo || ShowErrorBars) && !(HideBadDataErrorBars && !inj.Include))
                {
                    var sd = Math.Abs(inj.SD / inj.PeakArea);
                    var etop = GetRelativePosition(inj.Ratio, inj.OffsetEnthalpy - inj.OffsetEnthalpy * sd);
                    var ebottom = GetRelativePosition(inj.Ratio, inj.OffsetEnthalpy + inj.OffsetEnthalpy * sd);

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

                if (inj.ID == mOverFeature) DrawRectsAtPositions(
                    layer, new CGPoint[] { p },
                    size: 14, circle: false, fill: true, width: 0, roundedradius: 4,
                    color: IsMouseDown ? ActivatedHighlightColor : HighlightColor);

                if (inj.Include) points.Add(p);
                else inv_points.Add(p);
            }

            layer.Context.SetFillColor(StrokeColor);
            layer.Context.SetStrokeColor(StrokeColor);
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

        void DrawZero(CGContext gc)
        {
            CGLayer layer = CGLayer.Create(gc, Frame.Size);

            var zero = new CGPath();
            zero.MoveToPoint(GetRelativePosition(XAxis.Min, 0));
            zero.AddLineToPoint(GetRelativePosition(XAxis.Max, 0));
            layer.Context.AddPath(zero);
            layer.Context.SetStrokeColor(SecondaryLineColor);
            layer.Context.SetLineWidth(1);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawGrid(CGContext gc)
        {
            var yticks = YAxis.GetValidTicks(false);
            var xticks = XAxis.GetValidTicks(false);

            var grid = new CGPath();

            foreach (var t in yticks.Where(v => v != 0))
            {
                var y = GetRelativePosition(0, t / YAxis.ValueFactor).Y;

                grid.MoveToPoint(0, y);
                grid.AddLineToPoint(PlotSize.Width, y);
            }

            foreach (var t in xticks)
            {
                var x = GetRelativePosition(t / XAxis.ValueFactor, 0).X;

                grid.MoveToPoint(x, 0);
                grid.AddLineToPoint(x, PlotSize.Height);
            }

            CGLayer layer = CGLayer.Create(gc, Frame.Size);
            layer.Context.SetLineWidth(1);
            layer.Context.SetStrokeColor(SecondaryLineColor);
            layer.Context.SetLineDash(3, new nfloat[] { 10 });
            layer.Context.AddPath(grid);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawParameters(CGContext gc)
        {
            CGLayer layer = CGLayer.Create(gc, Frame.Size);
            layer.Context.SetStrokeColor(NSColor.Label.ColorWithAlphaComponent(0.4f).CGColor);
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

            DrawRectsAtPositions(layer, points.ToArray(), 8, true, false, color: SecondaryLineColor);

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawConfidenceInterval(CGContext gc)
        {
            if (ExperimentData.Solution == null) return;
            if (ExperimentData.Solution.BootstrapSolutions == null) return;

            var top = new List<CGPoint>();
            var bottom = new List<CGPoint>();

            foreach (var inj in ExperimentData.Injections)
            {
                var x = inj.Ratio;
                var y = ExperimentData.Solution.EvaluateBootstrap(inj.ID).WithConfidence();

                top.Add(GetRelativePosition(x, y[0]));
                bottom.Add(GetRelativePosition(x, y[1]));
            }

            bottom.Reverse();

            CGPath path = GetSplineFrommPoints(top.ToArray());
            GetSplineFrommPoints(bottom.ToArray(), path);

            //path.MoveToPoint(top[0]);
            //foreach (var p in top.Skip(1)) path.AddLineToPoint(p);

            FillPathShape(gc, path, StrokeColor, .25f);
        }

        int mDownID = -1;
        int mOverFeature = -1;

        public override MouseOverFeatureEvent IsCursorOnFeature(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            foreach (var inj in ExperimentData.Injections)
            {
                var handle_screen_pos = GetRelativePosition(inj.Ratio, inj.OffsetEnthalpy) + new CGSize(Origin);

                if (Math.Abs(cursorpos.X - 2 - handle_screen_pos.X) < 5)
                {
                    if (Math.Abs(cursorpos.Y - handle_screen_pos.Y) < 5)
                    {
                        if (isclick) mDownID = inj.ID;
                        else if (ismouseup && mDownID == inj.ID) { inj.Include = !inj.Include; mOverFeature = -1; }
                        mOverFeature = inj.ID;
                        return new MouseOverFeatureEvent(inj);
                    }
                }
            }

            if (isclick) mDownID = -1;
            if (mOverFeature != -1)
            {
                var e = new MouseOverFeatureEvent()
                {
                    FeatureID = mOverFeature
                };
                mOverFeature = -1;
                return e;
            }
            return new MouseOverFeatureEvent();
        }
    }
}