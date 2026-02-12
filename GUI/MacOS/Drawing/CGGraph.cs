using System;
using System.Collections.Generic;
using AppKit;
using CoreGraphics;
using Foundation;
using System.Linq;
using CoreText;
using Utilities;
using static AnalysisITC.AppClasses.Analysis2.Models.SolutionInterface;

namespace AnalysisITC
{
    public class GraphBase
    {
        public const float PiHalf = (float)Math.PI / 2;
        public const float PPcm = 0.5f * 227 / 2.54f;

        public nfloat PlotWidthCM = 7.0f;
        public nfloat PlotPixelWidth { get => PlotWidthCM * PPcm; set => PlotWidthCM = value / PPcm; }

        private nfloat PlotHeightCM = 5.0f;
        private nfloat PlotPixelHeight { get => PlotHeightCM * PPcm; set => PlotHeightCM = value / PPcm; }

        internal CGSize PointsPerUnit;
        public CGPoint Center;
        public CGSize PlotSize;
        public CGPoint Origin;

        /// <summary>
        /// The graph frame defining the plotting area. Derived from Origin and PlotSize.
        /// </summary>
        public CGRect Frame { get { if (PlotSize.Width == 0) AutoSetFrame(); return new CGRect(Origin, PlotSize); } }
        public NSView View;

        public bool DrawOnWhite = false;
        public CGColor StrokeColor => DrawOnWhite ? NSColor.Black.CGColor : NSColor.Label.CGColor;
        public CGColor SecondaryLineColor => DrawOnWhite ? NSColor.Gray.CGColor : NSColor.SecondaryLabel.CGColor;
        public CGColor TertiaryLineColor => DrawOnWhite ? NSColor.LightGray.CGColor : NSColor.TertiaryLabel.CGColor;

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

        public virtual void PrepareDraw(CGContext gc, CGPoint center)
        {

        }

        /// <summary>
        /// Returns the frame that should be used for cursor tracking. By default this is the same as <see cref="Frame"/>.
        /// Derived graph types that contain multiple plotting regions can override this to return the bounding rectangle
        /// encompassing all interactive plot areas.
        /// </summary>
        /// <returns>A rectangle in view coordinates representing the interactive region.</returns>
        public virtual CGRect GetTrackingFrame()
        {
            return Frame;
        }

        internal CGPoint GetRelativePosition(DataPoint dp, GraphAxis axis = null)
        {
            return GetRelativePosition(dp.Time, dp.Power, axis);
        }

        public virtual CGPoint GetRelativePosition(double x, double y, GraphAxis axis = null)
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

        /// <summary>
        /// Draws string to a given layer. Return size of string. Provide layer as null to get size.
        /// </summary>
        /// <returns></returns>
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
            var textLine = new CTLine(attributedString);
            var boxsize = textLine.GetBounds(CTLineBoundsOptions.UseOpticalBounds).Size;
            var size = textLine.GetBounds(CTLineBoundsOptions.UseGlyphPathBounds).Size;

            if (layer != null)
            {
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
            }

            return size;
        }

        public CGSize DrawString2(CGLayer layer, NSAttributedString str, CGPoint position, TextAlignment horizontalignment = TextAlignment.Center, TextAlignment verticalalignment = TextAlignment.Center, CGColor textcolor = null, float rotation = 0)
        {
            if (textcolor == null) textcolor = StrokeColor;

            var textLine = new CTLine(str);
            var boxsize = textLine.GetBounds(CTLineBoundsOptions.UseOpticalBounds).Size;
            var size = textLine.GetBounds(CTLineBoundsOptions.UseGlyphPathBounds).Size;

            if (layer != null)
            {
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
                layer.Context.SetFillColor(textcolor);
                textLine.Draw(layer.Context);
                layer.Context.RestoreState();
                textLine.Dispose();
            }

            return size;
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

        public void DrawDiamondsAtPositions(CGLayer layer, CGPoint[] points, float size, bool fill = false, float width = 1, CGColor color = null, float roundedradius = 0)
        {
            if (color == null) color = StrokeColor;

            foreach (var p in points)
            {
                var rect = GetRectAtPosition(new CGPoint(0, 0), size);

                layer.Context.TranslateCTM(p.X, p.Y);

                layer.Context.RotateCTM(PiHalf / 2f);
                if (roundedradius > 0) layer.Context.AddPath(CGPath.FromRoundedRect(rect, roundedradius, roundedradius));
                else layer.Context.AddPath(CGPath.FromRect(rect));
                layer.Context.RotateCTM(-PiHalf / 2f);

                layer.Context.TranslateCTM(-p.X, -p.Y);
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

        public CGRect GetRectAtPosition(CGPoint point, double size)
        {
            return new CGRect(point.X - size / 2, point.Y - size / 2, size, size);
        }

        public void DrawSymbolsAtPositions(CGLayer layer, CGPoint[] points, float size, SymbolShape shape, bool fill = false, float width = 1, CGColor color = null, float roundedradius = 0)
        {
            switch (shape)
            {
                case SymbolShape.Square: DrawRectsAtPositions(layer, points, size, false, fill, width, color, roundedradius); break;
                case SymbolShape.Circle: DrawRectsAtPositions(layer, points, size, true, fill, width, color, roundedradius); break;
                case SymbolShape.Diamond: DrawDiamondsAtPositions(layer, points, size, fill, width, color, roundedradius); break;
            }
        }

        internal void AddCircleAtPosition(CGLayer layer, CGPoint p, double size)
        {
            var rect = GetRectAtPosition(p, size);

            layer.Context.AddPath(CGPath.EllipseFromRect(rect));
        }

        internal void AddRectAtPosition(CGLayer layer, CGPoint p, double size, float roundedradius)
        {
            var rect = GetRectAtPosition(p, size);

            if (roundedradius > 0) layer.Context.AddPath(CGPath.FromRoundedRect(rect, roundedradius, roundedradius));
            else layer.Context.AddRect(rect);
        }

        public void FillPathShape(CGContext gc, CGPath path, CGColor color)
        {
            var layer = CGLayer.Create(gc, Frame.Size);
            layer.Context.SetFillColor(color);
            layer.Context.AddPath(path);
            layer.Context.FillPath();

            gc.DrawLayer(layer, Frame.Location);
        }

        public virtual void AutoSetFrame()
        {
            var ymargin = YAxis.EstimateLabelMargin();
            var xmargin = XAxis.EstimateLabelMargin();

            PlotSize = new CGSize(View.Frame.Width - ymargin - 5, View.Frame.Height - xmargin - 5);
            Origin = new CGPoint(ymargin, xmargin);
        }

        public CGPath GetSplineFromPoints(CGPoint[] points, CGPath path = null, float linewidth = 1)
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

        public void DrawFrame(CGContext gc)
        {
            gc.SetStrokeColor(StrokeColor);
            gc.StrokeRectWithWidth(Frame, 1);
        }

        public static CGSize MeasureString(string s, CTFont font, CTStringAttributes attr = null, AxisPosition position = AxisPosition.Bottom, bool ignoreoptical = true)
        {
            if (attr == null) attr = new CTStringAttributes
            {
                ForegroundColorFromContext = true,
                Font = font,
            };

            var attributedString = new NSAttributedString(s, attr);

            return MeasureString(attributedString, position, ignoreoptical);

            //var size = attributedString.Size;

            //var textLine = new CTLine(attributedString);
            
            //if (!ignoreoptical && (position == AxisPosition.Bottom || position == AxisPosition.Right)) size = textLine.GetBounds(CTLineBoundsOptions.UseOpticalBounds).Size;
            //else size = textLine.GetBounds(CTLineBoundsOptions.UseGlyphPathBounds).Size;

            //textLine.Dispose();

            //return size;
        }

        public static CGSize MeasureString(NSAttributedString attributedString, AxisPosition position = AxisPosition.Bottom, bool ignoreoptical = true)
        {
            var size = attributedString.Size;

            var textLine = new CTLine(attributedString);

            if (!ignoreoptical && (position == AxisPosition.Bottom || position == AxisPosition.Right)) size = textLine.GetBounds(CTLineBoundsOptions.UseOpticalBounds).Size;
            else size = textLine.GetBounds(CTLineBoundsOptions.UseGlyphPathBounds).Size;

            textLine.Dispose();

            return size;
        }

        public enum SymbolShape
        {
            Square,
            Circle,
            Diamond,
        }
    }

    public class CGGraph : GraphBase
    {
        public ExperimentData ExperimentData;

        public CTFont DefaultFont = new CTFont("Helvetica Neue Light", 12);
        internal nfloat DefaultFontHeight => DefaultFont.CapHeightMetric + 5;
        internal static CGColor HighlightColor => NSColor.Label.ColorWithAlphaComponent(0.2f).CGColor;
        internal static CGColor ActivatedHighlightColor => NSColor.Label.ColorWithAlphaComponent(0.35f).CGColor;
        public static float SymbolSize { get; set; } = 8;

        public bool IsMouseDown { get; set; } = false;

        public CGGraph(ExperimentData experiment, NSView view)
        {
            ExperimentData = experiment;
            View = view;
        }

        public override void PrepareDraw(CGContext gc, CGPoint center)
        {
            this.Center = center;

            AutoSetFrame();

            SetupAxisScalingUnits();

            Draw(gc);

            DrawFrame(gc);
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
            layer.Context.SetLineJoin(CGLineJoin.Round);
            layer.Context.SetLineCap(CGLineCap.Round);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Origin);
        }

        public void DrawSpline(CGContext gc, CGPoint[] points, float linewidth, CGColor color)
        {
            var path = GetSplineFromPoints(points);

            DrawPath(gc, path, linewidth, color);
        }

        void DrawPath(CGContext gc, CGPath path, float linewidth, CGColor color)
        {
            var layer = CGLayer.Create(gc, Frame.Size);

            DrawPathToLayer(layer, path, linewidth, color);

            gc.DrawLayer(layer, Frame.Location);
        }

        public void DrawTextBox(CGContext gc, List<string> lines, CTFont font = null, NSRectAlignment alignment = NSRectAlignment.BottomTrailing, CGColor textcolor = null)
        {
            if (font == null) font = DefaultFont;
            if (textcolor == null) textcolor = StrokeColor;
            if (lines.Count == 0) return;

            nfloat width = 0;
            nfloat height = 0;

            var attstr_lines = new List<NSAttributedString>();
            var nsfont = NSFont.FromCTFont(font);

            foreach (var line in lines)
            {
                var attstr = Utilities.MacStrings.FromMarkDownString(line, NSFont.FromCTFont(font), true);
                attstr_lines.Add(attstr);

                var size = DrawString2(null, attstr, new CGPoint(0, 0), horizontalignment: TextAlignment.Left);

                if (size.Width > width) width = size.Width;
                height += size.Height + font.Size * 0.4f;
            }

            var boxsize = new CGSize(width + 12, height + 6);
            var xpos = alignment switch
            {
                NSRectAlignment.Top or NSRectAlignment.None or NSRectAlignment.Bottom => Frame.Width / 2 - boxsize.Width / 2,
                NSRectAlignment.BottomTrailing or NSRectAlignment.Trailing or NSRectAlignment.TopTrailing => Frame.Width - boxsize.Width - 7,
                _ => 7,
            };
            var ypos = alignment switch
            {
                NSRectAlignment.Leading or NSRectAlignment.None or NSRectAlignment.Trailing => Frame.Height / 2 - boxsize.Height / 2,
                NSRectAlignment.Top or NSRectAlignment.TopLeading or NSRectAlignment.TopTrailing => Frame.Height - boxsize.Height - 7,
                _ => 7,
            };

            var pos = new CGPoint(xpos + Frame.X, ypos + Frame.Y);
            var tpos = new CGPoint(6, boxsize.Height - 0.75f * font.Size);
            var layer = CGLayer.Create(gc, boxsize);
            var textlayer = CGLayer.Create(gc, boxsize);

            foreach (var line in attstr_lines)
            {
                var size = DrawString2(textlayer, line, tpos, horizontalignment: TextAlignment.Left);

                tpos.Y -= size.Height + font.Size * 0.4f;
            }

            layer.Context.SetFillColor(DrawOnWhite ? NSColor.White.CGColor : NSColor.TextBackground.CGColor);
            layer.Context.FillRect(new CGRect(1, 1, boxsize.Width - 2, boxsize.Height - 2));
            layer.Context.StrokeRect(new CGRect(1, 1, boxsize.Width - 2, boxsize.Height - 2));

            gc.DrawLayer(layer, pos);
            gc.DrawLayer(textlayer, pos);
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

        #endregion
        #endregion

        public virtual MouseOverFeatureEvent CursorFeatureFromPos(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
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
            YAxis = GraphAxis.WithBuffer(this, DataPoints.Min(dp => dp.Power), DataPoints.Max(dp => dp.Power));

            SetupYAxes();
        }

        void SetupYAxes()
        {
            IEnumerable<ExperimentData> data;

            if (UseUnifiedAxes) data = DataManager.IncludedData;
            else data = new ExperimentData[] { ExperimentData };

            var minmax = Method(data);

            YAxis.SetWithBuffer(minmax[0], minmax[1], YAxis.Buffer);
        }

        double[] Method(IEnumerable<ExperimentData> data)
        {
            PeakHeatDirection direction;
            if (data.All(exp => exp.AverageHeatDirection == PeakHeatDirection.Exothermal)) direction = PeakHeatDirection.Exothermal;
            else if (data.All(exp => exp.AverageHeatDirection == PeakHeatDirection.Endothermal)) direction = PeakHeatDirection.Endothermal;
            else direction = PeakHeatDirection.Unknown;

            double max = 0;
            double min = 0;

            switch (direction)
            {
                case PeakHeatDirection.Exothermal when ShowBaselineCorrected: min = data.Min(exp => local_getdatapoints(exp).Min(dp => dp.Power)); break;
                case PeakHeatDirection.Endothermal when ShowBaselineCorrected: max = data.Max(exp => local_getdatapoints(exp).Max(dp => dp.Power)); break;
                default:
                    min = data.Min(exp => local_getdatapoints(exp).Min(dp => dp.Power));
                    max = data.Max(exp => local_getdatapoints(exp).Max(dp => dp.Power));
                    break;
            }

            return new double[] { min, max };

            List<DataPoint> local_getdatapoints(ExperimentData _exp)
            {
                if (ShowBaselineCorrected && _exp.BaseLineCorrectedDataPoints != null) return _exp.BaseLineCorrectedDataPoints;
                else return _exp.DataPoints;
            }
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

        public bool DrawCursorPositionInfo { get; set; } = false;
        double CursorPosition { get; set; }
        List<string> CursorInfo { get; set; } = new List<string>();

        public FileInfoGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
            string injdescription = "";

            for (int i = 0; i < experiment.Injections.Count; i++)
            {
                var curr = experiment.Injections[i];
                var next = i < experiment.InjectionCount - 1 ? experiment.Injections[i + 1] : null;
                var prev = i > 0 ? experiment.Injections[i - 1] : null;

                if (experiment.Injections.Where(inj => inj.ID > i).All(inj => inj.Volume == curr.Volume))
                {
                    injdescription += "#" + (i + 1).ToString() + "-" + experiment.Injections.Count.ToString() + ": " + (1000000 * curr.Volume).ToString("F1") + " µl, ";
                    break;
                }
                else if (next != null && curr.Volume != next.Volume)
                    injdescription += "#" + (i + 1).ToString() + ": " + (1000000 * curr.Volume).ToString("F1") + " µl, ";
                else if (prev != null && curr.Volume != prev.Volume)
                    injdescription += "#" + (i + 1).ToString() + ": " + (1000000 * curr.Volume).ToString("F1") + " µl, ";
            }

            injdescription = injdescription.Substring(0, injdescription.Length - 2);

            Info = new List<string>()
                {
                    "Filename: " + experiment.FileName + " | Instrument: " + experiment.Instrument.GetProperties().Name,
                    "Date: " + experiment.Date.ToLocalTime().ToLongDateString() + " " + experiment.Date.ToLocalTime().ToString("HH:mm"),
                    "Temperature | Target: " + experiment.TargetTemperature.ToString() + " °C [Measured: " + experiment.MeasuredTemperature.ToString("G4") + " °C] | Feedback Mode: " + experiment.FeedBackMode.GetProperties().Name + " | Stirring Speed: " + experiment.StirringSpeed.ToString() + " rpm",
                    "Injections: " + experiment.InjectionCount.ToString() + " [" + injdescription + "]",
                    "Concentrations | Cell: " + experiment.CellConcentration.AsConcentration(ConcentrationUnit.µM) + " | Syringe: " + experiment.SyringeConcentration.AsConcentration(ConcentrationUnit.µM),
                };

            if (!string.IsNullOrEmpty(experiment.Comments)) Info.Add("Comment: " + experiment.Comments);

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

            PlotSize.Width -= TemperatureAxis.EstimateLabelMargin();
        }

        internal override void Draw(CGContext gc)
        {
            DrawTemperature(gc);

            TemperatureAxis.Draw(gc);

            base.Draw(gc);

            if (DrawCursorPositionInfo) DrawInfo(gc);
        }

        void DrawTemperature(CGContext gc)
        {
            var temperature = new List<CGPoint>();
            var jacket = new List<CGPoint>();

            for (int i = 0; i < DataPoints.Count - 1; i += 10) //Skip some datapoints
            {
                DataPoint p = DataPoints[i];
                if (p.Time > XAxis.Min && p.Time < XAxis.Max)
                {
                    temperature.Add(GetRelativePosition(p.Time, p.Temperature, TemperatureAxis));
                    jacket.Add(GetRelativePosition(p.Time, p.ShieldT, TemperatureAxis));
                }
            }

            temperature.Add(GetRelativePosition(DataPoints.Last().Time, DataPoints.Last().Temperature, TemperatureAxis)); //add final datapoint
            jacket.Add(GetRelativePosition(DataPoints.Last().Time, DataPoints.Last().ShieldT, TemperatureAxis));

            DrawDataSeries(gc, temperature, 1, NSColor.SystemRed.CGColor);
            DrawDataSeries(gc, jacket, 1, NSColor.SystemRed.CGColor);
        }

        void DrawInfo(CGContext gc)
        {
            var layer = CGLayer.Create(gc, Frame.Size);
            var textlayer = CGLayer.Create(gc, Frame.Size);
            textlayer.Context.SetFillColor(StrokeColor);

            var datapoint = CursorPosition > ExperimentData.DataPoints.Last().Time ? ExperimentData.DataPoints.Last() : ExperimentData.DataPoints.First(dp => dp.Time > CursorPosition);

            var power = GetRelativePosition(CursorPosition, datapoint.Power);
            var max = new CGPoint(power.X, Frame.Height);

            layer.Context.MoveTo(power.X, 0);
            layer.Context.AddLineToPoint(power.X, Frame.Height);
            layer.Context.StrokePath();
            DrawRectsAtPositions(layer, new CGPoint[] { power }, 5, true, true);

            DrawTextBox(gc, CursorInfo, alignment: NSRectAlignment.TopLeading);

            gc.DrawLayer(layer, Frame.Location);
            gc.DrawLayer(textlayer, Frame.Location);
        }

        public bool SetCursorInfo(CGPoint cursorpos)
        {
            if (Frame.Contains(cursorpos))
            {
                DrawCursorPositionInfo = true;

                var xfraction = (cursorpos.X - Frame.X) / Frame.Width;

                CursorPosition = xfraction * (XAxis.Max - XAxis.Min);

                CursorInfo = new List<string>();

                var datapoint = CursorPosition > ExperimentData.DataPoints.Last().Time ? ExperimentData.DataPoints.Last() : ExperimentData.DataPoints.First(dp => dp.Time > CursorPosition);

                CursorInfo.Add("Time: " + datapoint.Time.ToString() + "s");
                CursorInfo.Add("DP: " + (DataManager.Unit.IsSI() ? (datapoint.Power * 1000000).ToString("F1") + " µW" : (datapoint.Power * 1000000 * Energy.JouleToCalFactor).ToString("F1") + " µCal"));
                CursorInfo.Add("Temperature: " + datapoint.Temperature.ToString("F3") + " °C");
                CursorInfo.Add("Jacket Temp: " + datapoint.ShieldT.ToString("F3") + " °C");
            }
            else DrawCursorPositionInfo = false;

            return DrawCursorPositionInfo;
        }
    }

    public class BaselineDataGraph : DataGraph
    {
        public static float BaselineThickness { get; set; } = 2;

        public bool ShowBaseline { get; set; } = true;
        public bool ShowExperimentDetails { get; set; } = false;
        public FinalFigureDisplayParameters FinalFigureDisplayParameters { get; set; } = FinalFigureDisplayParameters.None;

        public string SyringeName { get; set; } = "";
        public string CellName { get; set; } = "";

        public BaselineDataGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
        }

        internal override void Draw(CGContext gc)
        {
            base.Draw(gc);

            if (ShowBaseline && ExperimentData.Processor.Interpolator != null && ExperimentData.Processor.Interpolator.Finished) DrawBaseline(gc);

            if (ShowExperimentDetails) DrawExperimentDetails(gc);
        }

        internal void DrawBaseline(CGContext gc)
        {
            var layer = CGLayer.Create(gc, Frame.Size);
            var path = new CGPath();
            var points = new List<CGPoint>();

            if (!ShowBaselineCorrected) for (int i = 0; i < ExperimentData.DataPoints.Count; i++)
                {
                    var p = ExperimentData.DataPoints[i];
                    var b = ExperimentData.Processor.Interpolator.Baseline[i];

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
            layer.Context.SetLineWidth(BaselineThickness);
            layer.Context.StrokePath();
            layer.Context.SetLineWidth(1);

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawExperimentDetails(CGContext gc)
        {
            var lines = new List<string>();
            string syr = "";
            if (!string.IsNullOrEmpty(SyringeName)) syr = "[" + SyringeName + "] = ";
            else syr = "[Syringe] = ";
            syr += ExperimentData.SyringeConcentration.AsFormattedConcentration(true);

            string cell = "";
            if (!string.IsNullOrEmpty(SyringeName)) cell = "[" + SyringeName + "] = ";
            else cell = "[Cell] = ";
            cell += ExperimentData.CellConcentration.AsFormattedConcentration(true);

            if (FinalFigureDisplayParameters.HasFlag(FinalFigureDisplayParameters.Temperature)) lines.Add(ExperimentData.MeasuredTemperature.ToString("F1") + " °C");
            if (FinalFigureDisplayParameters.HasFlag(FinalFigureDisplayParameters.Concentrations)) lines.Add(syr);
            if (FinalFigureDisplayParameters.HasFlag(FinalFigureDisplayParameters.Concentrations)) lines.Add(cell);

            if (FinalFigureDisplayParameters.HasFlag(FinalFigureDisplayParameters.Attributes) && ExperimentData.Solution != null)
            {
                var attlines = ExperimentData.Model.Solution.UIExperimentModelAttributes(AppSettings.DisplayAttributeOptions);

                foreach (var att in attlines)
                {
                    var l = att.Item1;
                    if (!string.IsNullOrEmpty(att.Item2)) l += " = " + att.Item2;
                    lines.Add(l);
                }
            }

            var position = ExperimentData.AverageHeatDirection == PeakHeatDirection.Endothermal ? NSRectAlignment.TopTrailing : NSRectAlignment.BottomTrailing;

            DrawTextBox(gc, lines, DrawOnWhite ? new CTFont(DefaultFont.DisplayName, 12) : DefaultFont, position);
        }
    }

    public class BaselineFittingGraph : BaselineDataGraph
    {
        int focused = -1;

        public bool ShowInjections { get; set; } = true;
        List<FeatureBoundingBox> SplineHandlePoints { get; set; } = new List<FeatureBoundingBox>();
        List<FeatureBoundingBox> SplinePoints { get; set; } = new List<FeatureBoundingBox>();
        List<FeatureBoundingBox> IntegrationHandleBoxes { get; set; } = new List<FeatureBoundingBox>();

        public bool DrawCursorPositionInfo { get; set; } = false;
        double CursorPosition { get; set; }
        List<string> CursorInfo { get; set; } = new List<string>();

        public BaselineFittingGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
            SetYAxisRange(DataPoints.Min(dp => dp.Power), DataPoints.Max(dp => dp.Power), buffer: true);
        }

        public void SetFocusedInjection(int i = -1) => focused = i;

        internal override void Draw(CGContext gc)
        {
            base.Draw(gc);

            IntegrationHandleBoxes.Clear();
            SplineHandlePoints.Clear();
            SplinePoints.Clear();

            if (ShowBaseline && ExperimentData.Processor.Interpolator != null && ExperimentData.Processor.Interpolator.Finished)
            {
                if (ExperimentData.Processor.Interpolator is SplineInterpolator) DrawSplineHandles(gc);
            }

            if (ShowInjections) DrawIntegrationMarkers(gc);

            if (DrawCursorPositionInfo && CursorInfo.Count > 0) DrawTextBox(gc, CursorInfo, alignment: NSRectAlignment.BottomTrailing);
        }

        void DrawSplineHandles(CGContext gc)
        {
            var layer = CGLayer.Create(gc, Frame.Size);
            var points = new List<CGRect>();
            var handles = new List<CGPath>();
            var slopelines = new List<CGPath>();

            foreach (var sp in (ExperimentData.Processor.Interpolator as SplineInterpolator).SplinePoints.Where(sp => sp.Time > XAxis.Min && sp.Time < XAxis.Max))
            {
                var m = GetRelativePosition((float)sp.Time, (float)sp.Power);

                if (ShowBaselineCorrected) m = GetRelativePosition((float)sp.Time, 0);
                if ((ExperimentData.Processor.Interpolator as SplineInterpolator).Algorithm == SplineInterpolator.SplineInterpolatorAlgorithm.Handles)
                {
                    var handle = new CGPath();
                    var slopeline = new CGPath();

                    var handlelengthtime = ExperimentData.Injections.Average(inj => inj.Delay / 3);
                    var y = ShowBaselineCorrected ? 0 : sp.Power;
                    var yoffset = sp.Slope * handlelengthtime;

                    var h1 = GetRelativePosition((float)sp.Time - handlelengthtime, (float)y - yoffset);
                    var h2 = GetRelativePosition((float)sp.Time + handlelengthtime, (float)y + yoffset);

                    var slope = (h2.Y - h1.Y) / (h2.X - h1.X);
                    var theta = Math.Atan(slope);

                    var handlelengthpixels = m.X - h1.X;
                    var _xoffset = (float)Math.Cos(theta) * (float)handlelengthpixels;
                    var _yoffset = (float)Math.Sin(theta) * (float)handlelengthpixels;

                    h1 = m.Subtract(_xoffset, _yoffset);
                    h2 = m.Add(_xoffset, _yoffset);

                    slopeline.MoveToPoint(h1);
                    slopeline.AddLineToPoint(h2);
                    handle.AddEllipseInRect(GetRectAtPosition(h1, 8));
                    handle.AddEllipseInRect(GetRectAtPosition(h2, 8));

                    SplineHandlePoints.Add(new FeatureBoundingBox(MouseOverFeatureEvent.FeatureType.BaselineSplineHandle, GetRectAtPosition(h1, 8), sp.ID, boxoffset: Frame.Location, sid: 0) { FeatureReferenceValue = sp.Slope });
                    SplineHandlePoints.Add(new FeatureBoundingBox(MouseOverFeatureEvent.FeatureType.BaselineSplineHandle, GetRectAtPosition(h2, 8), sp.ID, boxoffset: Frame.Location, sid: 1) { FeatureReferenceValue = sp.Slope });

                    handles.Add(handle);
                    slopelines.Add(slopeline);
                }

                var r = new CGRect(m.X - 4, m.Y - 4, 8, 8);

                SplinePoints.Add(new FeatureBoundingBox(MouseOverFeatureEvent.FeatureType.BaselineSplinePoint, r, sp.ID, Frame.Location) { FeatureReferenceValue = sp.Power });

                points.Add(r);
            }

            //Red slope line
            layer.Context.SetStrokeColor(NSColor.Red.CGColor);
            layer.Context.SetLineWidth(3);
            foreach (var l in slopelines) layer.Context.AddPath(l);
            layer.Context.StrokePath();

            //Black slope line
            layer.Context.SetStrokeColor(NSColor.Black.CGColor);
            layer.Context.SetLineWidth(1);
            foreach (var l in slopelines) layer.Context.AddPath(l);
            layer.Context.StrokePath();

            //red handle
            layer.Context.SetFillColor(NSColor.Red.CGColor);
            foreach (var h in handles) layer.Context.AddPath(h);
            layer.Context.FillPath();

            //black handle outline
            foreach (var h in handles) layer.Context.AddPath(h);
            layer.Context.StrokePath();

            //Draw points
            layer.Context.SetFillColor(NSColor.Red.CGColor);
            foreach (var r in points) layer.Context.FillRect(r);

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawIntegrationMarkers(CGContext gc)
        {
            var layer = CGLayer.Create(gc, Frame.Size);
            var path = new CGPath();
            var thickness = 1.5f;

            foreach (var inj in ExperimentData.Injections)
            {
                if (inj.Time < XAxis.Min || inj.Time > XAxis.Max) continue;

                var s = GetRelativePosition(inj.IntegrationStartTime, DataPoints.Last(dp => dp.Time < inj.IntegrationStartTime).Power);
                var e = GetRelativePosition(inj.IntegrationEndTime, DataPoints.Last(dp => dp.Time < inj.IntegrationEndTime).Power);

                if (focused == -1 || focused == inj.ID) layer.Context.SetFillColor(NSColor.SystemBlue.ColorWithAlphaComponent(.5f).CGColor);
                else layer.Context.SetFillColor(NSColor.SystemGray.ColorWithAlphaComponent(.5f).CGColor);
                layer.Context.SetLineWidth(4);

                var b = new CGRect(s.X - thickness, 0, 2 * thickness, Frame.Height);
                var p = new CGPath();
                p.MoveToPoint(s.X - thickness, thickness);
                p.AddLineToPoint(s.X + thickness, thickness);
                p.AddLineToPoint(s.X + thickness, Frame.Height - 3 * thickness);
                p.AddLineToPoint(s.X + 10 + thickness, Frame.Height - 3 * thickness);
                p.AddLineToPoint(s.X + 15 + thickness, Frame.Height - thickness);
                p.AddLineToPoint(s.X - thickness, Frame.Height - thickness);
                p.CloseSubpath();
                layer.Context.AddPath(p);

                layer.Context.FillPath();

                IntegrationHandleBoxes.Add(new FeatureBoundingBox(MouseOverFeatureEvent.FeatureType.IntegrationRangeMarker, b, inj.ID, Frame.Location, 0));

                var b2 = new CGRect(e.X - thickness, 0, 2 * thickness, Frame.Height);
                var p2 = new CGPath();
                p2.MoveToPoint(e.X + thickness, Frame.Height - thickness);
                p2.AddLineToPoint(e.X - thickness, Frame.Height - thickness);
                p2.AddLineToPoint(e.X - thickness, 3 * thickness);
                p2.AddLineToPoint(e.X - 10 - thickness, 3 * thickness);
                p2.AddLineToPoint(e.X - 15 - thickness, thickness);
                p2.AddLineToPoint(e.X + thickness, thickness);
                p2.CloseSubpath();
                layer.Context.AddPath(p2);

                IntegrationHandleBoxes.Add(new FeatureBoundingBox(MouseOverFeatureEvent.FeatureType.IntegrationRangeMarker, b2, inj.ID, Frame.Location, 1));

                layer.Context.FillPath();
            }

            gc.DrawLayer(layer, Frame.Location);
        }

        public bool SetCursorInfo(CGPoint cursorpos)
        {
            if (Frame.Contains(cursorpos))
            {
                CursorInfo = new List<string>();

                var xfraction = (cursorpos.X - Frame.X) / Frame.Width;

                CursorPosition = xfraction * (XAxis.Max - XAxis.Min) + XAxis.Min;
                var datapoint = CursorPosition > ExperimentData.DataPoints.Last().Time ? ExperimentData.DataPoints.Last() : ExperimentData.DataPoints.First(dp => dp.Time > CursorPosition);
                var clickedinj = ExperimentData.Injections.Where(inj => inj.IntegrationStartTime < datapoint.Time && inj.IntegrationEndTime + 1 > datapoint.Time);

                if (clickedinj.Count() > 0)
                {
                    var inj = clickedinj.First().ID + 1;
                    CursorInfo.Add("Inj #" + inj.ToString() + " | " + new Energy(clickedinj.First().Enthalpy).ToFormattedString(AppSettings.EnergyUnit));
                }
                
                CursorInfo.Add("Time: " + datapoint.Time.ToString() + "s");
                CursorInfo.Add("DP: " + (DataManager.Unit.IsSI() ? (datapoint.Power * 1000000).ToString("F1") + " µW" : (datapoint.Power * 1000000 * Energy.JouleToCalFactor).ToString("F1") + " µCal"));

                if (ExperimentData.BaseLineCorrectedDataPoints != null && ExperimentData.BaseLineCorrectedDataPoints.Count > 0)
                {
                    var bldp = CursorPosition > ExperimentData.BaseLineCorrectedDataPoints.Last().Time ? ExperimentData.BaseLineCorrectedDataPoints.Last() : ExperimentData.BaseLineCorrectedDataPoints.First(dp => dp.Time > CursorPosition);

                    CursorInfo.Add("∆DP: " + (DataManager.Unit.IsSI() ? (bldp.Power * 1000000).ToString("F2") + " µW" : (bldp.Power * 1000000 * Energy.JouleToCalFactor).ToString("F2") + " µCal"));
                }
            }
            else return false;

            return true;
        }

        public override MouseOverFeatureEvent CursorFeatureFromPos(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            foreach (var handle in SplineHandlePoints)
                if (handle.CursorInBox(cursorpos)) return MouseOverFeatureEvent.BoundboxFeature(handle, cursorpos); //return new MouseOverFeatureEvent(handle);

            foreach (var point in SplinePoints)
                if (point.CursorInBox(cursorpos)) return MouseOverFeatureEvent.BoundboxFeature(point, cursorpos); //return new MouseOverFeatureEvent(point);

            foreach (var handle in IntegrationHandleBoxes)
                if (handle.CursorInBox(cursorpos)) return MouseOverFeatureEvent.BoundboxFeature(handle, cursorpos); //return new MouseOverFeatureEvent(handle);

            foreach (var handle in IntegrationHandleBoxes)
                if (handle.ProximityX(cursorpos, (XAxis.Max - XAxis.Min) / (float)PlotPixelWidth)) return MouseOverFeatureEvent.BoundboxFeature(handle, cursorpos); //return new MouseOverFeatureEvent(handle);

            if (isclick) return MouseOverFeatureEvent.MouseDragZoom(this, cursorpos);
            else return new MouseOverFeatureEvent();
        }
    }

    public class ThermogramGraph : CGGraph
    { 
        protected int mDownID = -1;
        protected int mOverFeature = -1;

        public static CGSize ErrorBarEndWidth => new CGSize(CGGraph.SymbolSize / 2, 0);

        public ThermogramGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
        }

        protected virtual void SetupAxes()
        {

        }

        protected void DrawZero(CGContext gc)
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

        protected void DrawGrid(CGContext gc)
        {
            var yticks = YAxis.GetValidTicks(false).Item1;
            var xticks = XAxis.GetValidTicks(false).Item1;

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
            layer.Context.SetStrokeColor(TertiaryLineColor);
            layer.Context.SetLineDash(3, new nfloat[] { 10 });
            layer.Context.AddPath(grid);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }
    }

    public class DataFittingGraph : ThermogramGraph
    {
        private bool _useUnifiedAxes = false;
        private bool _useUnifiedEnthalpyAxis = false;
        private bool _focusvaliddata = false;

        public bool DrawConfidenceBands { get; set; } = true;
        public bool ShowFitParameters { get; set; } = true;
        public int ParameterFontSize { get; set; } = 24;
        public bool DrawWithOffset { get; set; } = true;
        public bool ShowGrid { get; set; } = true;
        public bool ShowZero { get; set; } = true;
        public bool ShowPeakInfo { get; set; } = true;
        public bool ShowErrorBars { get; set; } = true;
        public bool HideBadData { get; set; } = false;
        public bool HideBadDataErrorBars { get; set; } = true;
        public SymbolShape InjectionSymbolShape { get; set; } = SymbolShape.Square;

        public FinalFigureDisplayParameters FinalFigureDisplayParameters { get; set; } = FinalFigureDisplayParameters.All;
        public FinalFigureDisplayParameters AnalysisDisplayParameters { get; set; } = FinalFigureDisplayParameters.AnalysisView;

        public ResidualGraph ResidualGraph { get; set; }
        public ResidualGraphOptions ResidualDisplayOptions { get; private set; }

        public bool UnifiedMolarRatioAxis { get => _useUnifiedAxes; set { _useUnifiedAxes = value; SetupAxes(); } }
        public bool UnifiedEnthalpyAxis { get => _useUnifiedEnthalpyAxis; set { _useUnifiedEnthalpyAxis = value; SetupAxes(); } }
        public bool FocusValidData { get => _focusvaliddata; set { _focusvaliddata = value; SetupAxes(); } }

        public CGPoint GetCompositeOrigin()
        {
            if (ResidualDisplayOptions.ShowResidualGraph) return ResidualGraph.Origin;
            else return base.Origin;
        }

        public DataFittingGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
            XAxis = new GraphAxis(this, 0, 1);
            XAxis.SetWithBuffer(0, Math.Max(Math.Floor(XAxis.Max + 0.33f), XAxis.Max), 0.05);
            XAxis.UseNiceAxis = false;
            XAxis.LegendTitle = GraphAxis.GetXAxisTitle(experiment);
            XAxis.DecimalPoints = 1;
            XAxis.TickScale.SetMaxTicks(7);
            XAxis.ValueFactor = GraphAxis.GetXAxisScaleFactor(experiment);

            YAxis = new GraphAxis(this, 0, 1);
            YAxis.UseNiceAxis = false;
            YAxis.HideUnwantedTicks = false;
            YAxis.LegendTitle = AppSettings.EnergyUnit.GetUnit() + "/mol of injectant";
            YAxis.ValueFactor = Energy.ScaleFactor(AppSettings.EnergyUnit);

            SetupAxes();

            ResidualGraph = new ResidualGraph(this);
            ResidualDisplayOptions = new ResidualGraphOptions();
        }

        public override CGRect GetTrackingFrame()
        {
            if (ResidualDisplayOptions != null && ResidualDisplayOptions.ShowResidualGraph && ResidualGraph != null)
            {
                // Compute the union of the fit graph frame and the residual graph frame.
                var fitFrame = base.Frame;
                var resFrame = ResidualGraph.Frame;
                return CGRect.Union(fitFrame, resFrame);
            }
            return base.Frame;
        }

        protected override void SetupAxes()
        {
            if (UnifiedMolarRatioAxis && DataManager.IncludedData.Count() > 0)
            {
                var xmax = DataManager.IncludedData.Max(d => d.Injections.Last().Ratio);
                XAxis.SetWithBuffer(0, xmax, 0.05);
            }
            else XAxis.SetWithBuffer(0, ExperimentData.Injections.Last().Ratio, 0.05);
            
            if (UnifiedEnthalpyAxis && DataManager.IncludedData.Count() > 0)
            {
                var minmax = GetMinMaxEnthalpy(DataManager.IncludedData);
                YAxis.SetWithBuffer(minmax[0], minmax[1], 0.1);
            }
            else
            {
                var minmax = GetMinMaxEnthalpy(new ExperimentData[] { ExperimentData });
                YAxis.SetWithBuffer(minmax[0], minmax[1], 0.1);
            }

            XAxis.SetWithBuffer(0, Math.Max(Math.Floor(XAxis.Max + 0.33f), XAxis.Max), 0.05);
        }

        double[] GetMinMaxEnthalpy(IEnumerable<ExperimentData> data)
        {
            var evals = data.Where(d => d.Solution != null).Select(d => d.Model.EvaluateEnthalpy(0, withoffset: DrawWithOffset));
            var maxpoints = data.Select(d => d.Injections.Where(inj => inj.Include || !FocusValidData).Max(inj => inj.Enthalpy));
            var minpoints = data.Select(d => d.Injections.Where(inj => inj.Include || !FocusValidData).Min(inj => inj.Enthalpy));

            if (evals.Count() == 0) evals = new double[] { 0 };
            if (maxpoints.Count() == 0) maxpoints = new double[] { 0 };
            if (minpoints.Count() == 0) minpoints = new double[] { 0 };

            var max = Math.Max(maxpoints.Max(), evals.Max());
            var min = Math.Min(minpoints.Min(), evals.Min());

            if (!DrawWithOffset)
            {
                if (evals.Min() < -1000) min = evals.Min();
                if (evals.Max() > 1000) max = evals.Max();
            }

            return new double[] { Math.Min(min, 0), Math.Max(max, 0) };
        }

        public override void AutoSetFrame()
        {
            var ymargin = YAxis.EstimateLabelMargin();
            var xmargin = XAxis.EstimateLabelMargin();

            var viable_height = View.Frame.Height - xmargin - 5;
            var viable_width = View.Frame.Width - ymargin - 5;

            SetFrame(new CGSize(viable_width, viable_height), new CGPoint(ymargin, xmargin));
        }

        public void SetFrame(CGSize viable_size, CGPoint origin)
        {
            var ymargin = origin.X;
            var xmargin = origin.Y;

            var viable_height = viable_size.Height;
            var viable_width = viable_size.Width;

            if (ResidualDisplayOptions.ShowResidualGraph)
            {
                var frac = ResidualDisplayOptions.ResidualFraction;
                var fit_plot_height = viable_height * (1 - frac);
                var res_plot_height = viable_height * (frac);
                var origin_x = ymargin;
                var origin_y_fit = xmargin + res_plot_height;
                var origin_y_res = xmargin;

                if (ResidualDisplayOptions.GapGraphs) res_plot_height -= ResidualDisplayOptions.GapSize;

                base.PlotSize = new CGSize(viable_width, fit_plot_height);
                base.Origin = new CGPoint(origin_x, origin_y_fit);

                ResidualGraph.PlotSize = new CGSize(viable_width, res_plot_height);
                ResidualGraph.Origin = new CGPoint(origin_x, origin_y_res);

                XAxis.HideLabels = true;
            }
            else
            {
                base.PlotSize = viable_size;
                base.Origin = origin;

                XAxis.HideLabels = false;
            }
        }

        internal override void Draw(CGContext gc)
        {
            base.Draw(gc);

            if (ShowGrid) DrawGrid(gc);

            if (ShowZero) DrawZero(gc);

            if (ExperimentData.Solution != null)
            {
                if (DrawConfidenceBands) DrawConfidenceInterval(gc);
                DrawFit(gc);
            }

            if (ExperimentData.Processor.IntegrationCompleted) DrawInjectionsPoints(gc);

            XAxis.Draw(gc);
            YAxis.Draw(gc);

            if (ShowFitParameters && ExperimentData.Solution != null) DrawParameters(gc);

            // Draw the residual graph after the fit graph, if enabled.
            if (ResidualDisplayOptions.ShowResidualGraph) ResidualGraph.PrepareDraw(gc, Center);
        }

        void DrawInjectionsPoints(CGContext gc)
        {
            var layer = CGLayer.Create(gc, Frame.Size);
            var points = new List<CGPoint>();
            var inv_points = new List<CGPoint>();
            var infolayer = CGLayer.Create(gc, Frame.Size);
            infolayer.Context.SetFillColor(StrokeColor);
            var bars = new CGPath();

            foreach (var inj in ExperimentData.Injections)
            {
                if (HideBadData && !inj.Include) continue; //Ignore datapoint if 'bad' and hidebaddata is true

                var enthalpy = DrawWithOffset ? inj.Enthalpy : inj.OffsetEnthalpy;

                var p = GetRelativePosition(inj.Ratio, enthalpy);
                var infop = p;

                if ((ShowPeakInfo || ShowErrorBars) && !(HideBadDataErrorBars && !inj.Include))
                {
                    var etop = GetRelativePosition(inj.Ratio, enthalpy - inj.SD);
                    var ebottom = GetRelativePosition(inj.Ratio, enthalpy + inj.SD);

                    if (Math.Abs(etop.Y - p.Y) > CGGraph.SymbolSize / 2)
                    {
                        bars.MoveToPoint(etop);
                        bars.AddLineToPoint(CGPoint.Add(p, new CGSize(0, CGGraph.SymbolSize / 2)));

                        bars.MoveToPoint(ebottom);
                        bars.AddLineToPoint(CGPoint.Subtract(p, new CGSize(0, CGGraph.SymbolSize / 2)));

                        bars.MoveToPoint(CGPoint.Subtract(etop, ErrorBarEndWidth));
                        bars.AddLineToPoint(CGPoint.Add(etop, ErrorBarEndWidth));

                        bars.MoveToPoint(CGPoint.Subtract(ebottom, ErrorBarEndWidth));
                        bars.AddLineToPoint(CGPoint.Add(ebottom, ErrorBarEndWidth));
                    }

                    infop = new CGPoint(ebottom.X, Math.Max(ebottom.Y, etop.Y));
                }

                if (ShowPeakInfo)
                {
                    DrawString(infolayer, "#" + (inj.ID + 1).ToString(), infop + new CGSize(0,10), DefaultFont, verticalalignment: TextAlignment.Bottom, textcolor: StrokeColor);
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
            DrawSymbolsAtPositions(layer, points.ToArray(), SymbolSize, InjectionSymbolShape, true);
            DrawSymbolsAtPositions(layer, inv_points.ToArray(), SymbolSize, InjectionSymbolShape, false);

            gc.DrawLayer(layer, Frame.Location);
            gc.DrawLayer(infolayer, Frame.Location);
        }

        void DrawFit(CGContext gc)
        {
            var points = new List<CGPoint>();

            foreach (var inj in ExperimentData.Injections)
            {
                var x = inj.Ratio;
                var y = ExperimentData.Model.EvaluateEnthalpy(inj.ID, withoffset: DrawWithOffset);

                points.Add(GetRelativePosition(x, y));
            }

            DrawSpline(gc, points.OrderBy(p => p.X).ToArray(), 2, StrokeColor);

            //DrawRectsAtPositions(layer, points.ToArray(), 8, true, false, color: NSColor.PlaceholderTextColor.CGColor);
        }

        void DrawParameters(CGContext gc)
        {
            CGLayer layer = CGLayer.Create(gc, Frame.Size);
            layer.Context.SetStrokeColor(TertiaryLineColor);
            layer.Context.SetLineWidth(1);

            var H = ExperimentData.Solution.TotalEnthalpy;
            if (DrawWithOffset) H += ExperimentData.Solution.Parameters[AppClasses.Analysis2.ParameterType.Offset];
            var e1 = GetRelativePosition(XAxis.Min, H);
            var e2 = GetRelativePosition(XAxis.Max, H);
            var enthalpy = new CGPath();
            enthalpy.MoveToPoint(e1);
            enthalpy.AddLineToPoint(e2);
            layer.Context.AddPath(enthalpy);

            //var N = ExperimentData.Solution.N;
            var Ns = ExperimentData.Solution.ParametersConformingToKey(AppClasses.Analysis2.ParameterType.Nvalue1);

            foreach (var N in Ns)
            {
                var n1 = GetRelativePosition(N, YAxis.Min);
                var n2 = GetRelativePosition(N, YAxis.Max);

                var n = new CGPath();
                n.MoveToPoint(n1);
                n.AddLineToPoint(n2);
                layer.Context.AddPath(n);

                layer.Context.SetLineDash(3, new nfloat[] { 3 });
                layer.Context.StrokePath();
            }

            gc.DrawLayer(layer, Frame.Location);

            var lines = new List<string>();

            foreach (var par in ExperimentData.Solution.UISolutionParameters(DrawOnWhite ? FinalFigureDisplayParameters : AnalysisDisplayParameters))
            {
                var line = par.Item1;
                if (par.Item2.Length > 0) line += " = " + par.Item2;
                lines.Add(line);
            }

            var position = ExperimentData.Solution.TotalEnthalpy > 0 ? NSRectAlignment.TopTrailing : NSRectAlignment.BottomTrailing;

            DrawTextBox(gc, lines, DrawOnWhite ? new CTFont(DefaultFont.DisplayName, 12) : new CTFont(DefaultFont.DisplayName, ParameterFontSize), position);
        }

        void DrawConfidenceInterval(CGContext gc)
        {
            if (ExperimentData.Solution == null) return;
            if (ExperimentData.Solution.BootstrapSolutions == null) return;
            if (ExperimentData.Solution.BootstrapSolutions.Count == 0) return;

            var top = new List<CGPoint>();
            var bottom = new List<CGPoint>();

            foreach (var inj in ExperimentData.Injections)
            {
                var x = inj.Ratio;
                var y = ExperimentData.Model.EvaluateBootstrap(inj.ID, DrawWithOffset).WithConfidence();

                top.Add(GetRelativePosition(x, y[0]));
                bottom.Add(GetRelativePosition(x, y[1]));
            }

            top = top.OrderBy(p => p.X).ToList();
            bottom = bottom.OrderBy(p => p.X).ToList();

            bottom.Reverse();

            CGPath path = GetSplineFromPoints(top.ToArray());
            GetSplineFromPoints(bottom.ToArray(), path);

            FillPathShape(gc, path, TertiaryLineColor);
        }

        public override CGPoint GetRelativePosition(double x, double y, GraphAxis axis = null)
        {
            var point = base.GetRelativePosition(x, y, axis);

            return point;
        }

        public override MouseOverFeatureEvent CursorFeatureFromPos(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            foreach (var inj in ExperimentData.Injections)
            {
                // Get injection point position
                var handle_screen_pos = GetRelativePosition(inj.Ratio, DrawWithOffset ? inj.Enthalpy : inj.OffsetEnthalpy) + new CGSize(Origin);

                if (Math.Abs(cursorpos.X - 2 - handle_screen_pos.X) < 5.5)
                {
                    if (Math.Abs(cursorpos.Y + 1 - handle_screen_pos.Y) < 5.5)
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

        public class ResidualGraphOptions
        {
            public bool ShowResidualGraph { get; set; } = true;
            public bool GapGraphs { get; set; } = true;
            public float ResidualFraction { get; set; } = 0.2f;
            public float GapSize { get; set; } = 5f;
        }
    }

    public class ResidualGraph : ThermogramGraph
    {
        DataFittingGraph DataFittingGraph { get; set; }

        public bool MirrorAxisUnification { get; set; } = true;

        GraphAxis ParentXAxis => DataFittingGraph.XAxis;
        GraphAxis ParentYAxis => DataFittingGraph.YAxis;

        public ResidualGraph(DataFittingGraph dataFittingGraph) : base(dataFittingGraph.ExperimentData, dataFittingGraph.View)
        {
            DataFittingGraph = dataFittingGraph;

            XAxis = new GraphAxis(this, 0, 1);
            XAxis.UseNiceAxis = ParentXAxis.UseNiceAxis;
            XAxis.LegendTitle = ParentXAxis.LegendTitle;
            XAxis.DecimalPoints = ParentXAxis.DecimalPoints;
            XAxis.TickScale.SetMaxTicks(7);
            XAxis.MirrorTicks = true;
            XAxis.ValueFactor = ParentXAxis.ValueFactor;

            YAxis = new GraphAxis(this, 0, 1);
            YAxis.UseNiceAxis = ParentYAxis.UseNiceAxis;
            YAxis.HideUnwantedTicks = ParentYAxis.HideUnwantedTicks;
            YAxis.LegendTitle = "";
            YAxis.ValueFactor = ParentYAxis.ValueFactor;
            YAxis.TickScale.SetMaxTicks(3);

            SetupAxes();
        }

        protected override void SetupAxes()
        {
            XAxis.Set(ParentXAxis.ActualMin, ParentXAxis.Max);

            var list = new List<ExperimentData>();

            if (DataFittingGraph.UnifiedEnthalpyAxis && MirrorAxisUnification) list.AddRange(DataManager.IncludedData);
            else list.Add(ExperimentData);

            var max = GetMaxResiduals(list);

            YAxis.Set(-max, max);
        }

        double GetMaxResiduals(IEnumerable<ExperimentData> data)
        {
            var res = new List<double>();

            foreach (var dat in data)
            {
                if (dat.Solution != null)
                {
                    foreach (var inj in dat.Injections.Where(inj => inj.Include))
                    {
                        var inj_v = Math.Abs(inj.ResidualEnthalpy);
                        var inj_min = Math.Abs(inj_v - inj.SD);
                        var inj_max = Math.Abs(inj_v + inj.SD);
                        if (DataFittingGraph.ShowErrorBars) res.Add(Math.Max(inj_v, Math.Min(inj_min, inj_max)));
                        else res.Add(inj_v);
                    }
                }
            }

            if (res.Count > 0) return 1.5 * Math.Max(res.Max(v => Math.Abs(v)), 1E-3);
            else return 1100;
        }

        public override void PrepareDraw(CGContext gc, CGPoint center)
        {
            SetupAxes();

            DrawOnWhite = DataFittingGraph.DrawOnWhite;
            base.SetupAxisScalingUnits();

            Draw(gc);

            DrawFrame(gc);
        }

        internal override void Draw(CGContext gc)
        {
            base.Draw(gc);

            if (DataFittingGraph.ShowGrid) DrawGrid(gc);

            DrawZero(gc);

            if (ExperimentData.Solution != null)
            {
                DrawResidual(gc);
            }

            XAxis.Draw(gc);
            YAxis.Draw(gc);
        }

        void DrawResidual(CGContext gc)
        {
            var layer = CGLayer.Create(gc, Frame.Size);
            var points = new List<CGPoint>();
            var inv_points = new List<CGPoint>();
            var infolayer = CGLayer.Create(gc, Frame.Size);
            infolayer.Context.SetFillColor(StrokeColor);
            var bars = new CGPath();

            foreach (var inj in ExperimentData.Injections)
            {
                var res = inj.ResidualEnthalpy;

                var p = GetRelativePosition(inj.Ratio, res);
                var infop = p;

                if ((DataFittingGraph.ShowPeakInfo || DataFittingGraph.ShowErrorBars) && !(DataFittingGraph.HideBadDataErrorBars && !inj.Include))
                {
                    var etop = GetRelativePosition(inj.Ratio, res - inj.SD);
                    var ebottom = GetRelativePosition(inj.Ratio, res + inj.SD);

                    if (Math.Abs(etop.Y - p.Y) > CGGraph.SymbolSize / 2)
                    {
                        bars.MoveToPoint(etop);
                        bars.AddLineToPoint(CGPoint.Add(p, new CGSize(0, CGGraph.SymbolSize / 2)));

                        bars.MoveToPoint(ebottom);
                        bars.AddLineToPoint(CGPoint.Subtract(p, new CGSize(0, CGGraph.SymbolSize / 2)));

                        bars.MoveToPoint(CGPoint.Subtract(etop, ErrorBarEndWidth));
                        bars.AddLineToPoint(CGPoint.Add(etop, ErrorBarEndWidth));

                        bars.MoveToPoint(CGPoint.Subtract(ebottom, ErrorBarEndWidth));
                        bars.AddLineToPoint(CGPoint.Add(ebottom, ErrorBarEndWidth));
                    }

                    infop = new CGPoint(ebottom.X, Math.Max(ebottom.Y, etop.Y));
                }

                if (DataFittingGraph.ShowPeakInfo)
                {
                    DrawString(infolayer, "#" + (inj.ID + 1).ToString(), infop + new CGSize(0, 10), DefaultFont, verticalalignment: TextAlignment.Bottom, textcolor: StrokeColor);
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
            DrawSymbolsAtPositions(layer, points.ToArray(), SymbolSize, DataFittingGraph.InjectionSymbolShape, true);
            DrawSymbolsAtPositions(layer, inv_points.ToArray(), SymbolSize, DataFittingGraph.InjectionSymbolShape, false);

            gc.DrawLayer(layer, Frame.Location);
            gc.DrawLayer(infolayer, Frame.Location);
        }
    }
}