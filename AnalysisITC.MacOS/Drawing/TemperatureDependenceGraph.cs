using System;
using AppKit;
using CoreGraphics;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.UI.MacOS.Drawing;
using AnalysisITC.UI.MacOS;
using AnalysisITC.Core.Analysis;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.UI.MacOS.Drawing
{
    public class TemperatureDependenceGraph : GraphBase
    {
        AnalysisResult Result { get; set; }
        readonly double TemperatureOffset;

        List<FeatureBoundingBox> FeatureBoundingBoxes = new List<FeatureBoundingBox>();

        public TemperatureDependenceGraph(AnalysisResult analysis, NSView view)
            : this(analysis, view, useKelvin: false)
        {
        }

        public TemperatureDependenceGraph(
            AnalysisResult analysis,
            NSView view,
            bool useKelvin)
        {
            View = view;
            Result = analysis;
            TemperatureOffset = useKelvin ? 273.15 : 0;

            XAxis = GraphAxis.WithBuffer(
                this,
                DisplayTemperature(analysis.GetMinimumTemperature()),
                DisplayTemperature(analysis.GetMaximumTemperature()),
                buffer: .1,
                position: AxisPosition.Bottom);
            XAxis.HideUnwantedTicks = false;
            XAxis.LegendTitle =
                "Temperature (" + (useKelvin ? "K" : "°C") + ")";

            YAxis = GraphAxis.WithBuffer(this, analysis.GetMinimumParameter(), analysis.GetMaximumParameter(), buffer: .1, position: AxisPosition.Left);
            YAxis.HideUnwantedTicks = false;
            var values = new List<double>();
            var dependences = analysis.Solution?.TemperatureDependence;
            if (dependences != null)
            {
                foreach (var item in dependences)
                {
                    values.AddRange(analysis.Solution.Solutions
                        .Where(solution => solution.ReportParameters.ContainsKey(item.Key))
                        .Select(solution => solution.ReportParameters[item.Key].Value));
                    values.Add(item.Value.Evaluate(analysis.GetMinimumTemperature()).Value);
                    values.Add(item.Value.Evaluate(analysis.GetMaximumTemperature()).Value);
                }
            }
            var energyUnit = EnergyUnitResolver.Resolve(
                AppSettings.EnergyUnitFamily,
                values);
            YAxis.ValueFactor = Energy.ScaleFactor(energyUnit);
            YAxis.MirrorTicks = true;
            YAxis.LegendTitle = "Thermodynamic parameter (" + energyUnit.GetUnit() + "/mol)";
        }

        public override void PrepareDraw(CGContext gc, CGPoint center)
        {
            this.Center = center;

            FeatureBoundingBoxes.Clear();

            AutoSetFrame();

            SetupAxisScalingUnits();

            DrawFrameBackground(gc);

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
            var slotIndex = 1;

            if (ThermodynamicParameterSlots.TryResolve(key, out var slot, out var family))
            {
                slotIndex = slot.Index;
                fill = slot.Index % 2 == 1;
                symbol = family switch
                {
                    ThermodynamicParameterFamily.Enthalpy => SymbolShape.Square,
                    ThermodynamicParameterFamily.EntropyContribution => SymbolShape.Circle,
                    ThermodynamicParameterFamily.Gibbs => SymbolShape.Diamond,
                    _ => SymbolShape.Square,
                };
            }

            var envelope = BuildFitEnvelope(key, line);
            DrawConfidenceBand(gc, envelope);
            DrawLinFit(gc, envelope);

            DrawDataPoints(gc, key, symbol, fill, slotIndex);
        }

        IReadOnlyList<LinearFitEnvelopePoint> BuildFitEnvelope(
            ParameterType key,
            LinearFitWithError fit)
        {
            var bootstrapFits = (Result.Solution.BootstrapSolutions ?? new List<GlobalSolution>())
                .Where(solution => solution?.TemperatureDependence?.ContainsKey(key) == true)
                .Select(solution => solution.TemperatureDependence[key])
                .ToList();
            var samples = LinearFitEnvelopeBuilder.SampleDomain(
                CelsiusTemperature(XAxis.Min),
                CelsiusTemperature(XAxis.Max));

            return LinearFitEnvelopeBuilder.Build(fit, bootstrapFits, samples);
        }

        void DrawLinFit(CGContext gc, IReadOnlyList<LinearFitEnvelopePoint> envelope)
        {
            if (envelope == null || envelope.Count < 2) return;

            var path = new CGPath();
            path.MoveToPoint(GetRelativePosition(DisplayTemperature(envelope[0].X), envelope[0].Center));
            for (var index = 1; index < envelope.Count; index++)
                path.AddLineToPoint(GetRelativePosition(DisplayTemperature(envelope[index].X), envelope[index].Center));

            var layer = CGLayer.Create(gc, PlotSize);
            layer.Context.SetStrokeColor(StrokeColor);
            layer.Context.AddPath(path);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawDataPoints(CGContext gc, ParameterType key, SymbolShape symbol, bool fill, int slotIndex)
        {
            var size = 8 + 2 * Math.Max(1, Math.Min(4, slotIndex));
            CGSize barwidth = new(size / 2, 0);

            var layer = CGLayer.Create(gc, PlotSize);
            var points = new List<CGPoint>();
            var selectedpoint = new List<CGPoint>();
            var bars = new CGPath();

            for (int i = 0; i < Result.Solution.Solutions.Count; i++)
            {
                var sol = Result.Solution.Solutions[i];
                var y = sol.ReportParameters[key];
                var x = DisplayTemperature(sol.Temp);
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

        void DrawConfidenceBand(
            CGContext gc,
            IReadOnlyList<LinearFitEnvelopePoint> envelope)
        {
            var points = envelope?.Where(point => point.HasBand).ToList();
            if (points == null || points.Count < 2) return;

            var path = new CGPath();
            path.MoveToPoint(GetRelativePosition(DisplayTemperature(points[0].X), points[0].Upper));
            for (var index = 1; index < points.Count; index++)
                path.AddLineToPoint(GetRelativePosition(DisplayTemperature(points[index].X), points[index].Upper));
            for (var index = points.Count - 1; index >= 0; index--)
                path.AddLineToPoint(GetRelativePosition(DisplayTemperature(points[index].X), points[index].Lower));
            path.CloseSubpath();

            var layer = CGLayer.Create(gc, PlotSize);
            layer.Context.SetFillColor(new CGColor(StrokeColor, .25f));
            layer.Context.AddPath(path);
            layer.Context.FillPath();

            gc.DrawLayer(layer, Frame.Location);
        }

        double DisplayTemperature(double celsius) =>
            celsius + TemperatureOffset;

        double CelsiusTemperature(double displayed) =>
            displayed - TemperatureOffset;

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
