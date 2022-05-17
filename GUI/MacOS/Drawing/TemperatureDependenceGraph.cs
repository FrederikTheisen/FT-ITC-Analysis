using System;
using AppKit;
using CoreGraphics;

namespace AnalysisITC
{
    public class TemperatureDependenceGraph : GraphBase
    {
        public TemperatureDependenceGraph(AnalysisResult analysis, NSView view)
        {
            View = view;

            XAxis = new GraphAxis(this, analysis.GetMinimumTemperature(), analysis.GetMaximumTemperature(), AxisPosition.Bottom);
            YAxis = new GraphAxis(this, analysis.GetMinimumParameter(), analysis.GetMaximumParameter(), AxisPosition.Left);
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

        public void AutoSetFrame()
        {
            var ymargin = YAxis.EstimateLabelMargin();
            var xmargin = XAxis.EstimateLabelMargin();

            var size = Math.Min(View.Frame.Height - ymargin, View.Frame.Width - xmargin);

            PlotSize = new CGSize(size - 1, size - 1);
            Origin = new CGPoint(ymargin, xmargin)
            {
                X = Center.X - PlotSize.Width / 2
            };
        }

        public void SetupAxisScalingUnits()
        {
            if (Frame.Size.Width * Frame.Size.Height < 0) return;

            var pppw = PlotSize.Width / (XAxis.Max - XAxis.Min);
            var ppph = PlotSize.Height / (YAxis.Max - YAxis.Min);

            PointsPerUnit = new CGSize(pppw, ppph);
        }

        internal void Draw(CGContext cg)
        {


        }

        public void DrawFrame(CGContext gc)
        {
            gc.SetStrokeColor(StrokeColor);
            gc.StrokeRectWithWidth(Frame, 1);
        }
    }
}
