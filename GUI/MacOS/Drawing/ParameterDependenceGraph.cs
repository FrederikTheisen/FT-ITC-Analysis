using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.Analysis2;
using AppKit;
using CoreGraphics;

namespace AnalysisITC
{
	public class ParameterDependenceGraph : GraphBase
	{
        public string XLabel { get; set; } = "";
        public string YLabel { get; set; } = "";

        public FloatWithError[] XValues { get; set; }
        public FloatWithError[] YValues { get; set; }

        public double XScaleFactor { get; set; } = 1;
        public double YScaleFactor { get; set; } = 1;

        public object Fit { get; set; }

        public ParameterDependenceGraph(NSView view)
        {
            View = view;
        }

        public void Setup()
        {
            if (XValues == null || YValues == null) return;
            XAxis = GraphAxis.WithBuffer(this, XValues.Min(), XValues.Max(), buffer: .1, position: AxisPosition.Bottom);
            XAxis.HideUnwantedTicks = false;
            XAxis.ValueFactor = XScaleFactor;
            XAxis.LegendTitle = XLabel;

            YAxis = GraphAxis.WithBuffer(this, YValues.Min(), YValues.Max(), buffer: .1, position: AxisPosition.Left);
            YAxis.HideUnwantedTicks = false;
            YAxis.ValueFactor = YScaleFactor;
            YAxis.MirrorTicks = true;
            YAxis.LegendTitle = YLabel;
        }

        public void PrepareDraw(CGContext gc, CGPoint center)
        {
            this.Center = center;

            AutoSetFrame();

            SetupAxisScalingUnits();

            Draw(gc);

            DrawFrame(gc);

            XAxis.Draw(gc);
            YAxis.Draw(gc);
        }

        void SetupAxisScalingUnits()
        {
            if (Frame.Size.Width * Frame.Size.Height < 0) return;

            var pppw = PlotSize.Width / (XAxis.Max - XAxis.Min);
            var ppph = PlotSize.Height / (YAxis.Max - YAxis.Min);

            PointsPerUnit = new CGSize(pppw, ppph);
        }

        void Draw(CGContext gc)
        {
            if (XValues == null || YValues == null) return;
            if (Fit != null) DrawFit(gc, Fit);

            DrawData(gc);
        }

        private void DrawZeroLine(CGContext gc)
        {
            var path = new CGPath();
            path.MoveToPoint(GetRelativePosition(XAxis.Min, 0));
            path.AddLineToPoint(GetRelativePosition(XAxis.Max, 0));

            var layer = CGLayer.Create(gc, PlotSize);
            layer.Context.SetStrokeColor(StrokeColor);
            layer.Context.AddPath(path);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawData(CGContext gc)
        {
            var layer = CGLayer.Create(gc, PlotSize);
            var points = new List<CGPoint>();

            for (int i = 0; i < XValues.Length; i++)
            {
                var x = XValues[i];
                var y = YValues[i];

                points.Add(GetRelativePosition(x, y));
            }

            DrawSymbolsAtPositions(layer, points.ToArray(), 10, SymbolShape.Square, true, 1, null, 0);

            gc.DrawLayer(layer, Origin);
        }

        void DrawFit(CGContext gc, object fit)
        {
            switch (fit)
            {
                case LinearFit f:
                    DrawLinFit(gc, f);
                    break;
                case LinearFitWithError f:
                    DrawLinearPredictionInterval(gc, f);
                    DrawLinFit(gc, f);
                    break;
                default: AppEventHandler.DisplayHandledException(new Exception("Unknown fit")); break;
            }
        }

        void DrawLinFit(CGContext gc, LinearFit fit)
        {
            DrawLinFit(gc, new LinearFitWithError(fit.Slope, fit.Intercept, fit.ReferenceT));
        }

        void DrawLinFit(CGContext gc, LinearFitWithError fit)
        {
            var offset = fit.ReferenceT;
            var xmin = XAxis.Min - offset;
            var xmax = XAxis.Max - offset;
            var y0 = fit.Slope * xmin + fit.Intercept;
            var y1 = fit.Slope * xmax + fit.Intercept;

            var path = new CGPath();
            path.MoveToPoint(GetRelativePosition(XAxis.Min, y0));
            path.AddLineToPoint(GetRelativePosition(XAxis.Max, y1));

            var layer = CGLayer.Create(gc, PlotSize);
            layer.Context.SetStrokeColor(StrokeColor);
            layer.Context.AddPath(path);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawLinearPredictionInterval(CGContext gc, LinearFitWithError fit)
        {
            var top = new List<CGPoint>();
            var bottom = new List<CGPoint>();
            var xrange = XAxis.Max - XAxis.Min;
            var xpoints = new List<double>();

            for (var x = XAxis.Min; x <= XAxis.Max; x += xrange / 10) { xpoints.Add(x); }
            xpoints.Add(XAxis.Max);

            foreach (var x in xpoints)
            {
                var dx = x - fit.ReferenceT;
                var e = dx * dx * fit.Slope.SD + fit.Intercept.SD;
                var val = fit.Slope * dx + fit.Intercept;
                var max = val + e;
                var min = val - e;

                top.Add(new CGPoint(GetRelativePosition(x, max)));
                bottom.Add(new CGPoint(GetRelativePosition(x, min)));
            }

            bottom.Reverse();

            CGPath path = GetSplineFromPoints(top.ToArray());
            GetSplineFromPoints(bottom.ToArray(), path);

            var layer = CGLayer.Create(gc, PlotSize);
            layer.Context.SetFillColor(new CGColor(StrokeColor, .25f));
            layer.Context.AddPath(path);
            layer.Context.FillPath();

            gc.DrawLayer(layer, Frame.Location);
        }
    }
}

