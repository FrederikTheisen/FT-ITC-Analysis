using System;
using System.Collections.Generic;
using AppKit;
using CoreGraphics;
using Foundation;
using System.Linq;

namespace AnalysisITC
{
    public class Graph
    {
        private CGContext Context { get; set; }
        public const float PPcm = 250/2.54f; 

        public float PlotWidthCM = 7f;
        public float PlotPixelWidth => PlotWidthCM * PPcm;

        public float PlotHeightCM = 5f;
        public float PlotPixelHeight => PlotHeightCM * PPcm;

        internal CGSize PointsPerUnit;
        internal CGPoint Center;
        internal CGSize PlotSize;
        internal CGPoint Origin;
        internal CGRect Frame;


        internal GraphAxis XAxis;
        internal GraphAxis YAxis;

        public ExperimentData ExperimentData;

        public Graph(ExperimentData experiment)
        {
            ExperimentData = experiment;
        }

        public void PrepareDraw(CGContext cg, CGPoint center)
        {
            this.Context = cg;
            this.Center = center;

            PlotSize = new CGSize(PlotPixelWidth, PlotPixelHeight);
            Origin = new CGPoint(Center.X - PlotSize.Width * 0.5f, Center.Y - PlotSize.Height * 0.5f);
            Frame = new CGRect(Origin, PlotSize);

            var pppw = PlotPixelWidth / (XAxis.Max - XAxis.Min);
            var ppph = PlotPixelHeight / (YAxis.Max - YAxis.Min);

            PointsPerUnit = new CGSize(pppw, ppph);

            Draw(cg);

            DrawFrame();

            DrawAxes();
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
            Context.SetLineWidth(0.5f);
            Context.StrokeRect(Frame);
        }

        internal virtual void DrawAxes()
        {

        }

        void DrawAxisLabel(string label, AxisPosition axisPosition = AxisPosition.Bottom)
        {

        }
        
        internal virtual CGPoint GetRelativePosition(Tuple<float, float> point)
        {
            var relx = (point.Item1 - XAxis.Min) * PointsPerUnit.Width;
            var rely = (point.Item2 - YAxis.Min) * PointsPerUnit.Height;

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

            Utilities.NiceScale TickScale = new Utilities.NiceScale(0, 1);

            public bool UseNiceAxis { get; set; } = true;

            public int DecimalPoints { get; set; } = 1;

            public GraphAxis(float min, float max)
            {
                var delta = max - min;

                this.min = min - delta * 0.035f;
                this.max = max + delta * 0.035f;

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

        public DataGraph(ExperimentData experiment) : base(experiment)
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
            layer.Context.SetStrokeColor(new CGColor(CGConstantColor.White));
            layer.Context.StrokePath();

            cg.DrawLayer(layer, Frame.Location);
        }

        CGPoint GetRelativePosition(DataPoint dp)
        {
            return GetRelativePosition(new Tuple<float, float>(dp.Time, dp.Power));
        }
    }

    public class BaselineFittingGraph : Graph
    {
        public BaselineFittingGraph(ExperimentData experiment) : base(experiment)
        {
            XAxis = new GraphAxis(experiment.DataPoints.Min(dp => dp.Time), experiment.DataPoints.Max(dp => dp.Time));
            YAxis = new GraphAxis(experiment.DataPoints.Min(dp => dp.Power), experiment.DataPoints.Max(dp => dp.Power));
        }
    }

    public class IntegrationGraph : Graph
    {
        public IntegrationGraph(ExperimentData experiment) : base(experiment)
        {

        }
    }

    public class FinalFigure : Graph
    {
        DataGraph DataGraph;
        IntegrationGraph IntegrationGraph;

        public FinalFigure(ExperimentData experiment) : base(experiment)
        {
            DataGraph = new DataGraph(experiment);
            //setup frame
            //Setup axes


            IntegrationGraph = new IntegrationGraph(experiment);
            //setup frame
            //setup axes
        }
    }
}
