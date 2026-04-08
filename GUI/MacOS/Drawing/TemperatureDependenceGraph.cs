using System;
using AppKit;
using CoreGraphics;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.Analysis2;
using Utilities;
using AnalysisITC.GUI.MacOS;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC
{
    public class TemperatureDependenceGraph : GraphBase
    {
        AnalysisResult Result { get; set; }

        List<FeatureBoundingBox> FeatureBoundingBoxes = new List<FeatureBoundingBox>();

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

        public override void PrepareDraw(CGContext gc, CGPoint center)
        {
            this.Center = center;

            FeatureBoundingBoxes.Clear();

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

        void DrawDependency(CGContext gc, ParameterType key)
        {
            var line = Result.Solution.TemperatureDependence[key];
            SymbolShape symbol = SymbolShape.Square;
            bool fill = true;

            switch (key)
            {
                case ParameterType.Enthalpy1: symbol = SymbolShape.Square; fill = true; break;
                case ParameterType.Enthalpy2: symbol = SymbolShape.Square; fill = false; break;
                case ParameterType.EntropyContribution1: symbol = SymbolShape.Circle; fill = true; break;
                case ParameterType.EntropyContribution2: symbol = SymbolShape.Circle; fill = false; break;
                case ParameterType.Gibbs1: symbol = SymbolShape.Diamond; fill = true; break;
                case ParameterType.Gibbs2: symbol = SymbolShape.Diamond; fill = false; break;
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

        void DrawDataPoints(CGContext gc, ParameterType key, SymbolShape symbol, bool fill)
        {
            const float size = 10;
            CGSize barwidth = new(size / 2, 0);

            var layer = CGLayer.Create(gc, PlotSize);
            var points = new List<CGPoint>();
            var selectedpoint = new List<CGPoint>();
            var bars = new CGPath();

            for (int i = 0; i < Result.Solution.Solutions.Count; i++)
            {
                var sol = Result.Solution.Solutions[i];
                var y = sol.ReportParameters[key];
                var x = sol.Temp;
                var dp = GetRelativePosition(x, y);

                FeatureBoundingBoxes.Add(new FeatureBoundingBox(MouseOverFeatureEvent.FeatureType.DataPoint, dp, size * 0.66f, i, Frame.Location));

                points.Add(dp);

                AddErrorBar(bars, x, y, barwidth);

                if (sol == DataManager.SelectedResultSolution)
                    selectedpoint.Add(dp);
            }

            layer.Context.SetStrokeColor(StrokeColor);
            layer.Context.AddPath(bars);
            layer.Context.StrokePath();

            DrawSymbolsAtPositions(layer, points.ToArray(), size, symbol, fill, 1, null, 0);

            if (selectedpoint.Count > 0)
            {
                var color = NSColor.ControlAccent.CGColor;
                var edge = MacColors.Adjust(color, -40);
                DrawSymbolsAtPositions(layer, selectedpoint.ToArray(), size * 1.2f, symbol, true, 1, color, 0);
                DrawSymbolsAtPositions(layer, selectedpoint.ToArray(), size * 1.2f, symbol, false, 0.5f, edge, 0);
            }

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

            for (var x = XAxis.Min; x < XAxis.Max; x += 0.5f) { xpoints.Add(x); }
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

        public override MouseOverFeatureEvent CursorFeatureFromPos(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            foreach (var feature in FeatureBoundingBoxes)
            {
                if (feature.CursorInBox(cursorpos))
                    return new MouseOverFeatureEvent(feature);
            }

            return new MouseOverFeatureEvent();
        }
    }
}
