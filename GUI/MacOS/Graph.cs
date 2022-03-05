using System;
using System.Collections.Generic;
using AppKit;
using CoreGraphics;
using Foundation;
using System.Linq;
using CoreText;

namespace AnalysisITC
{
    public class Graph
    {
        private CGContext Context { get; set; }
        public const float PPcm = 250/2.54f;

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

        protected float PlotWidthCM = 7f;
        protected float PlotPixelWidth => PlotWidthCM * PPcm;

        protected float PlotHeightCM = 5f;
        protected float PlotPixelHeight => PlotHeightCM * PPcm;

        internal CGSize PointsPerUnit;
        internal CGPoint Center;
        internal CGSize PlotSize;
        internal CGPoint Origin;
        internal CGRect Frame;
        internal NSView View;
        internal bool ScaleToView = true;


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
                PlotWidthCM = (float)View.Frame.Size.Width / PPcm;
                PlotHeightCM = (float)View.Frame.Size.Height / PPcm;
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

        internal virtual void DrawAxes()
        {

        }

        void DrawAxisLabel(string label, AxisPosition axisPosition = AxisPosition.Bottom)
        {

        }

        protected void DrawText(CGContext context, string text, nfloat textHeight, nfloat x, nfloat y)
        {
            y = PlotPixelHeight - y - textHeight;
            context.SetFillColor(StrokeColor.CGColor);

            var attributedString = new NSAttributedString(text,
                new CTStringAttributes
                {
                    ForegroundColorFromContext = true,
                    Font = DefaultFont
                });

            var textLine = new CTLine(attributedString);

            context.TextPosition = new CGPoint(x, y);
            textLine.Draw(context);

            textLine.Dispose();
        }

        internal virtual CGPoint GetRelativePosition(Tuple<float, float> point)
        {
            var relx = (point.Item1 - XAxis.Min) * PointsPerUnit.Width;
            var rely = (point.Item2 - YAxis.Min) * PointsPerUnit.Height;

            return new CGPoint(relx, rely);
        }

        internal virtual CGPoint GetRelativePosition(float x, float y)
        {
            var relx = (x - XAxis.Min) * PointsPerUnit.Width;
            var rely = (y - YAxis.Min) * PointsPerUnit.Height;

            return new CGPoint(relx, rely);
        }

        internal class GraphAxis
        {
            internal AxisPosition Position;

            public string Legend { get; set; }

            float min = 0;
            public float Min
            {
                get { if (UseNiceAxis) return TickScale.NiceMin; else return min; }
                set { min = value; TickScale.SetMinMaxPoints(min, max); }
            }

            float max = 1;
            public float Max
            {
                get { if (UseNiceAxis) return TickScale.NiceMax; else return max; }
                set { max = value; TickScale.SetMinMaxPoints(min, max); }
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

            public GraphAxis(float min, float max)
            {
                var delta = max - min;

                this.min = min - delta * buffer;
                this.max = max + delta * buffer;

                TickScale = new Utilities.NiceScale(this.min, this.max);
            }

            void UpdateAutoScale(float old)
            {
                var delta = max - min;
                var old_delta = delta / (2 * old + 1);

                var old_min = min + old_delta * old;
                var old_max = max - old_delta * old;

                this.min = old_min - old_delta * buffer;
                this.max = old_max + old_delta * buffer;

                TickScale = new Utilities.NiceScale(this.min, this.max);
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
            YAxis = new GraphAxis(experiment.DataPoints.Min(dp => dp.Power), experiment.DataPoints.Max(dp => dp.Power));

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
            return GetRelativePosition(new Tuple<float, float>(dp.Time, dp.Power));
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

            PlotSize = new CGSize(PlotPixelWidth-2, PlotPixelHeight - infoheight-2);
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
        public BaselineFittingGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {
            
        }

        public void SetInjectionView(int? i)
        {
            if (i == null)
            {
                XAxis = new GraphAxis(ExperimentData.DataPoints.Min(dp => dp.Time), ExperimentData.DataPoints.Max(dp => dp.Time))
                {
                    UseNiceAxis = false
                };
                YAxis = new GraphAxis(ExperimentData.DataPoints.Min(dp => dp.Power), ExperimentData.DataPoints.Max(dp => dp.Power));
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
                YAxis = new GraphAxis(ExperimentData.DataPoints.Where(dp => dp.Time > s && dp.Time < e).Min(dp => dp.Power), ExperimentData.DataPoints.Where(dp => dp.Time > s && dp.Time < e).Max(dp => dp.Power));
            }
        }

        internal override void Draw(CGContext cg)
        {
            base.Draw(cg);

            if (ExperimentData.Processor.Interpolator.Finished)
            {
                DrawBaseline(cg);

                if (ExperimentData.Processor.Interpolator is SplineInterpolator) DrawSplineHandles(cg);
            }
        }

        void DrawBaseline(CGContext cg)
        {
            CGLayer layer = CGLayer.Create(cg, Frame.Size);

            var path = new CGPath();

            var points = new List<CGPoint>();

            for (int i = 0; i < ExperimentData.DataPoints.Count; i++)
            {
                DataPoint p = ExperimentData.DataPoints[i];
                float b = ExperimentData.Processor.Interpolator.Baseline[i];

                if (p.Time > XAxis.Min && p.Time < XAxis.Max)
                    points.Add(GetRelativePosition(new Tuple<float, float>(p.Time, b)));
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
                var m = GetRelativePosition((float)sp.Item1, (float)sp.Item2);

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
    }

    public class IntegrationGraph : Graph
    {
        public IntegrationGraph(ExperimentData experiment, NSView view) : base(experiment, view)
        {

        }
    }

    public class FinalFigure
    {
        DataGraph DataGraph;
        IntegrationGraph IntegrationGraph;

        public FinalFigure(ExperimentData experiment, NSView view)
        {
            DataGraph = new DataGraph(experiment, view);
            DataGraph.DrawOnWhite = true;
            //setup frame
            //Setup axes


            IntegrationGraph = new IntegrationGraph(experiment, view);
            IntegrationGraph.DrawOnWhite = true;
            //setup frame
            //setup axes
        }
    }
}
