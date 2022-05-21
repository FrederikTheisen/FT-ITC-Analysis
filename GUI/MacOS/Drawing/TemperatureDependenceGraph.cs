using System;
using AppKit;
using CoreGraphics;
using System.Collections.Generic;
using System.Linq;

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
            YAxis.ValueFactor = 0.001;
            YAxis.MirrorTicks = true;
            YAxis.LegendTitle = "Thermodynamc parameter (kJ/mol)";
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
            DrawDataPoints(gc);
        }

        void DrawDataPoints(CGContext gc)
        {
            var layer = CGLayer.Create(gc, PlotSize);

            var entropies = new List<CGPoint>();
            var enthalpies = new List<CGPoint>();
            var gibbs = new List<CGPoint>();

            foreach (var sol in Result.Solution.Solutions)
            {
                entropies.Add(GetRelativePosition(sol.T, sol.TdS));
                enthalpies.Add(GetRelativePosition(sol.T, sol.Enthalpy));
                gibbs.Add(GetRelativePosition(sol.T, sol.GibbsFreeEnergy));
            }

            DrawSymbolsAtPositions(layer, entropies.ToArray(), 10, SymbolShape.Circle, true, 1, null, 0);
            DrawSymbolsAtPositions(layer, enthalpies.ToArray(), 10, SymbolShape.Square, true, 1, null, 0);
            DrawSymbolsAtPositions(layer, gibbs.ToArray(), 10, SymbolShape.Diamond, true, 1, null, 0);

            gc.DrawLayer(layer, Origin);
        }

        void DrawFrame(CGContext gc)
        {
            gc.SetStrokeColor(StrokeColor);
            gc.StrokeRectWithWidth(Frame, 1);
        }
    }
}
