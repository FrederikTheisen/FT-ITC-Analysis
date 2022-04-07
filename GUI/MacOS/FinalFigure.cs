using System;
using AppKit;
using CoreGraphics;

namespace AnalysisITC
{
    public class FinalFigure
    {
        DataGraph DataGraph;
        DataFittingGraph IntegrationGraph;

        public CGSize PlotDimensions { get; set; } = new CGSize(6, 10);
        CGEdgeMargin Margin = new CGEdgeMargin(1.5f * CGGraph.PPcm, 0.1f * CGGraph.PPcm, .8f * CGGraph.PPcm, .85f * CGGraph.PPcm);
        CGRect PlotBox => new CGRect(IntegrationGraph.Origin, PlotDimensions.ScaleBy(CGGraph.PPcm));

        public FinalFigure(ExperimentData experiment, NSView view)
        {
            DataGraph = new DataGraph(experiment, view);
            DataGraph.DrawOnWhite = true;
            DataGraph.ShowBaselineCorrected = true;
            DataGraph.XAxis.Buffer = .1f;
            DataGraph.YAxis.Buffer = .05f;
            DataGraph.XAxis.ValueFactor = 1.0 / 60;

            IntegrationGraph = new DataFittingGraph(experiment, view);
            IntegrationGraph.DrawOnWhite = true;
        }

        public void SetupFrames(nfloat width, nfloat height)
        {
            var halfheight = height / 2;

            DataGraph.SetupFrame((float)width, (float)halfheight);
            DataGraph.Origin.Y += DataGraph.Frame.Height / 2;
            DataGraph.XAxis.Position = AxisPosition.Top;

            IntegrationGraph.SetupFrame((float)width, (float)halfheight);
            IntegrationGraph.Origin.Y -= IntegrationGraph.Frame.Height / 2;
        }

        public void Draw(CGContext gc, CGPoint center)
        {
            DataGraph.Center = center;
            IntegrationGraph.Center = center;

            SetupFrames(PlotDimensions.Width, PlotDimensions.Height);

            gc.SetFillColor(NSColor.White.CGColor);
            gc.FillRect(PlotBox.WithMargin(Margin));
                //new CGRect
                //    (
                //    CGPoint.Subtract(IntegrationGraph.Origin, new CGSize(1.5 * CGGraph.PPcm, .8 * CGGraph.PPcm)),
                //    new CGSize(DataGraph.PlotPixelWidth + 2 * CGGraph.PPcm, 2 * DataGraph.PlotPixelHeight + 1.6 * CGGraph.PPcm))
                //    );

            DataGraph.SetupAxisScalingUnits();
            IntegrationGraph.SetupAxisScalingUnits();

            DataGraph.Draw(gc);
            IntegrationGraph.Draw(gc);

            DataGraph.DrawFrame(gc);
            IntegrationGraph.DrawFrame(gc);
        }
    }
}
