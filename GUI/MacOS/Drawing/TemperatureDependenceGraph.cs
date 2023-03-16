using System;
using AppKit;
using CoreGraphics;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.Analysis2;

namespace AnalysisITC
{
    public class TemperatureDependenceGraph : GraphBase
    {
        AnalysisResult Result { get; set; }

        public TemperatureDependenceGraph(AnalysisResult analysis, NSView view)
        {
            View = view;
            Result = analysis;

            XAxis = GraphAxis.WithBuffer(this, analysis.GetMinimumTemperature(), analysis.GetMaximumTemperature(), buffer: .1, position: AxisPosition.Bottom);
            XAxis.HideUnwantedTicks = false;
            XAxis.LegendTitle = "Temperature (°C)";

            YAxis = GraphAxis.WithBuffer(this, analysis.GetMinimumParameter(), analysis.GetMaximumParameter(), buffer: .1, position: AxisPosition.Left);
            YAxis.HideUnwantedTicks = false;
            YAxis.ValueFactor = Energy.ScaleFactor(AppSettings.EnergyUnit);
            YAxis.MirrorTicks = true;
            YAxis.LegendTitle = "Thermodynamic parameter (" + AppSettings.EnergyUnit.GetUnit() + "/mol)";
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
            DrawDependencies(gc);

            DrawZeroLine(gc);
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

        void DrawDependencies(CGContext gc)
        {
            foreach (var dep in Result.Solution.TemperatureDependence)
            {
                DrawDependency(gc, dep.Key);
            }
        }

        void DrawDependency(CGContext gc, ParameterTypes key)
        {
            var line = Result.Solution.TemperatureDependence[key];
            SymbolShape symbol = SymbolShape.Square;
            bool fill = true;

            switch (key)
            {
                case ParameterTypes.Enthalpy1: symbol = SymbolShape.Square; fill = true; break;
                case ParameterTypes.Enthalpy2: symbol = SymbolShape.Square; fill = false; break;
                case ParameterTypes.EntropyContribution1: symbol = SymbolShape.Circle; fill = true; break;
                case ParameterTypes.EntropyContribution2: symbol = SymbolShape.Circle; fill = false; break;
                case ParameterTypes.Gibbs1: symbol = SymbolShape.Diamond; fill = true; break;
                case ParameterTypes.Gibbs2: symbol = SymbolShape.Diamond; fill = false; break;
            }

            DrawPredictionInterval(gc, line, Result.Solution.Solutions.Select(sol => sol.ReportParameters[key]));

            DrawLinFit(gc, line);

            DrawDataPoints(gc, key, symbol, fill);
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

        void DrawDataPoints(CGContext gc, ParameterTypes key, SymbolShape symbol, bool fill)
        {
            var layer = CGLayer.Create(gc, PlotSize);
            var points = new List<CGPoint>();

            foreach (var sol in Result.Solution.Solutions) points.Add(GetRelativePosition(sol.Temp, sol.ReportParameters[key]));

            DrawSymbolsAtPositions(layer, points.ToArray(), 10, symbol, fill, 1, null, 0);

            gc.DrawLayer(layer, Origin);
        }

        double ComputeConfidenceBand(double dx, IEnumerable<FloatWithError> var)
        {
            var n = Result.Solution.Solutions.Count;
            var dx2 = dx * dx;

            var sy = Math.Sqrt(var.Select(s =>
            {
                var v = s.SD;
                return v * v;
            }).Sum() / (n - 2));

            var sx = Result.Solution.Solutions.Select(s => Math.Pow(s.Temp - Result.Solution.Model.MeanTemperature, 2)).Sum() / (n - 2);
            var t = 1.96;

            return t * sy * Math.Sqrt(1 + 1 / n + dx2 / sx);
        }

        double ComputeConfidenceBand2(double dx, IEnumerable<FloatWithError> var)
        {
            var n = Result.Solution.Solutions.Count;
            var dx2 = dx * dx;

            var sy = Math.Sqrt(Result.Solution.Solutions.Select(s =>
            {
                var v = s.Loss;
                return v;
            }).Sum() / (n - 2));

            var sx = Math.Sqrt(Result.Solution.Solutions.Select(s => Math.Pow(s.Temp - Result.Solution.Model.MeanTemperature, 2)).Sum() / (n - 2));
            var t = 1.96;

            return t * sy * Math.Sqrt(1 + 1 / n + dx2 / (sx * sx));
        }

        void DrawPredictionInterval(CGContext gc, LinearFitWithError line, IEnumerable<FloatWithError> values)
        {
            var top = new List<CGPoint>();
            var bottom = new List<CGPoint>();
            var xrange = XAxis.Max - XAxis.Min;
            var xpoints = new List<double>();

            for (var x = XAxis.Min; x <= XAxis.Max; x += xrange / 10) { xpoints.Add(x); }
            xpoints.Add(XAxis.Max); 

            foreach (var x in xpoints)
            {
                var dx = x - Result.Solution.Model.MeanTemperature;
                var e = ComputeConfidenceBand(dx, values);
                var val = line.Slope * dx + line.Intercept;
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
