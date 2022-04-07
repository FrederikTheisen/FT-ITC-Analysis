using AppKit;
using CoreGraphics;

namespace AnalysisITC
{
    public class FinalFigure
    {
        DataGraph DataGraph;
        DataFittingGraph IntegrationGraph;

        public FinalFigure(ExperimentData experiment, NSView view)
        {
            DataGraph = new DataGraph(experiment, view);
            DataGraph.DrawOnWhite = true;
            //setup frame
            //Setup axes


            IntegrationGraph = new DataFittingGraph(experiment, view);
            IntegrationGraph.DrawOnWhite = true;
            //setup frame
            //setup axes
        }

        public void SetupFrames(float width, float height)
        {
            float halfheight = height / 2;

            DataGraph.SetupFrame(width, halfheight);
            DataGraph.Origin.Y += halfheight;
            DataGraph.XAxis.Position = AxisPosition.Top;

            IntegrationGraph.SetupFrame(width, halfheight);
            IntegrationGraph.Origin.Y -= halfheight;
        }

        public void Draw(CGContext gc, CGPoint center)
        {
            SetupFrames(4,8);

            DataGraph.Draw(gc);
            IntegrationGraph.Draw(gc);
        }
    }
}
