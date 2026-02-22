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

        static public SymbolShape SymbolShape { get; set; } = SymbolShape.Square;
        static CGSize ErrorBarEndWidth => new CGSize(CGGraph.SymbolSize / 2, 0);

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

        public override void PrepareDraw(CGContext gc, CGPoint center)
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
            var bars = new CGPath();

            for (int i = 0; i < XValues.Length; i++)
            {
                var x = XValues[i];
                var y = YValues[i];
                var p = GetRelativePosition(x, y);
                points.Add(p);

                var sd = y.SD;
                var etop = GetRelativePosition(x, y + sd);
                var ebottom = GetRelativePosition(x, y - sd);

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
            }

            layer.Context.SetFillColor(StrokeColor);
            layer.Context.SetStrokeColor(StrokeColor);
            layer.Context.SetLineWidth(1);
            layer.Context.AddPath(bars);
            layer.Context.StrokePath();
            DrawSymbolsAtPositions(layer, points.ToArray(), CGGraph.SymbolSize, SymbolShape, true, 1, null, 0);

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
                case FitWithError f:
                    DrawFit(gc, f);
                    break;
                default: break;
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

        void DrawFit(CGContext gc, FitWithError fit)
        {
            var line = new List<CGPoint>();
            var top = new List<CGPoint>();
            var bottom = new List<CGPoint>();
            var xrange = Math.Max(1, XAxis.Max - XAxis.Min);
            var xpoints = new List<double>();
            var stepsize = xrange * 0.05f;

            // Protect against points with same x axis position
            for (var x = XAxis.Min; x < (XAxis.Max - stepsize); x += stepsize) { xpoints.Add(x); }
            xpoints.Add(XAxis.Max);

            foreach (var x in xpoints)
            {
                var dx = x - fit.ReferenceX;
                var y = fit.Evaluate(x,0);
                var e = fit.MinMax(x);
                var max = e[1];
                var min = e[0];

                line.Add(new CGPoint(GetRelativePosition(x, y)));
                top.Add(new CGPoint(GetRelativePosition(x, max)));
                bottom.Add(new CGPoint(GetRelativePosition(x, min)));
            }

            bottom.Reverse();

            CGPath path = GetSplineFromPoints(top.ToArray());
            GetSplineFromPoints(bottom.ToArray(), path);

            var errorlayer = CGLayer.Create(gc, PlotSize);
            errorlayer.Context.SetFillColor(new CGColor(StrokeColor, .25f));
            errorlayer.Context.AddPath(path);
            errorlayer.Context.FillPath();

            CGPath fitpath = GetSplineFromPoints(line.ToArray());

            var fitlayer = CGLayer.Create(gc, PlotSize);
            fitlayer.Context.SetStrokeColor(StrokeColor);
            fitlayer.Context.AddPath(fitpath);
            fitlayer.Context.StrokePath();

            gc.DrawLayer(errorlayer, Frame.Location);
            gc.DrawLayer(fitlayer, Frame.Location);
        }

        void DrawLinearPredictionInterval(CGContext gc, LinearFitWithError fit)
        {
            var top = new List<CGPoint>();
            var bottom = new List<CGPoint>();
            var xrange = XAxis.Max - XAxis.Min;
            var xpoints = new List<double>();

            // In case xrange is zero
            if (xrange < 1E-9f) xrange = 0.01f;

            for (var x = XAxis.Min; x < XAxis.Max; x += xrange / 10) { xpoints.Add(x); }
            xpoints.Add(XAxis.Max);

            foreach (var x in xpoints)
            {
                var dx = x - fit.ReferenceT;
                var e = ComputeConfidenceBand(x, XValues, YValues);
                var val = fit.Slope * dx + fit.Intercept;
                var max = val + e;
                var min = val - e;

                top.Add(new CGPoint(GetRelativePosition(x, max)));
                bottom.Add(new CGPoint(GetRelativePosition(x, min)));
            }

            bottom.Reverse();

            CGPath path = GetSplineFromPoints(top.ToArray(), smoothness: LineSmoothness.Linear);
            GetSplineFromPoints(bottom.ToArray(), path, smoothness: LineSmoothness.Linear);

            var layer = CGLayer.Create(gc, PlotSize);
            layer.Context.SetFillColor(new CGColor(StrokeColor, .25f));
            layer.Context.AddPath(path);
            layer.Context.FillPath();

            gc.DrawLayer(layer, Frame.Location);

            double ComputeConfidenceBand(double x, FloatWithError[] xs, FloatWithError[] ys)
            {
                var x0 = xs.Average(x => x.Value);
                var n = ys.Count();
                var dx = x - x0;
                var dx2 = dx * dx;

                var sy = Math.Sqrt(ys.Select(s =>
                {
                    var v = s.SD;
                    return v * v;
                }).Sum() / (n - 2));

                var sx = xs.Select(x => Math.Pow(x - x0, 2)).Sum() / (n - 2);
                var t = 1;

                return t * sy * Math.Sqrt(1 + 1 / n + dx2 / sx);
            }
        }
    }
}

