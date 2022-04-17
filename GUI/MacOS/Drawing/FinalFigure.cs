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
        CGEdgeMargin Margin
        {
            get
            {
                var max_margin_left = (float)Math.Max(DataGraph.YAxis.EstimateLabelMargin(), IntegrationGraph.YAxis.EstimateLabelMargin());

                return new CGEdgeMargin(max_margin_left, 0.1f * CGGraph.PPcm, (float)DataGraph.XAxis.EstimateLabelMargin(), (float)IntegrationGraph.XAxis.EstimateLabelMargin());
            }
        }
        CGRect PlotBox => new CGRect(IntegrationGraph.Origin, PlotDimensions.ScaleBy(CGGraph.PPcm));
        CGPoint UnadjustedGraphOrigin => new CGPoint(DataGraph.Center.X - DataGraph.PlotSize.Width * 0.5f, DataGraph.Center.Y - DataGraph.PlotSize.Height * 0.5f);

        public bool ShowErrorBars
        {
            get => IntegrationGraph.ShowErrorBars;
            set => IntegrationGraph.ShowErrorBars = value;
        }

        public bool HideBadDataErrorBars
        {
            get => IntegrationGraph.HideBadDataErrorBars;
            set => IntegrationGraph.HideBadDataErrorBars = value;
        }

        public bool UseUnifiedAnalysisAxes
        {
            get => IntegrationGraph.UseUnifiedAxes;
            set => IntegrationGraph.UseUnifiedAxes = value;
        }

        public bool UseUnifiedDataAxes
        {
            get => DataGraph.UseUnifiedAxes;
            set => DataGraph.UseUnifiedAxes = value;
        }

        public FinalFigure(ExperimentData experiment, NSView view)
        {
            DataGraph = new DataGraph(experiment, view)
            {
                DrawOnWhite = true,
                ShowBaselineCorrected = true
            };
            DataGraph.YAxis.Buffer = .05f;
            DataGraph.YAxis.MirrorTicks = true;
            DataGraph.XAxis.Buffer = .1f;
            DataGraph.XAxis.ValueFactor = 1.0 / 60;
            DataGraph.XAxis.LegendTitle = "Time (min)";

            IntegrationGraph = new DataFittingGraph(experiment, view)
            {
                DrawOnWhite = true,
                ShowGrid = false,
                ShowErrorBars = true,
                HideBadDataErrorBars = true,
            };
            IntegrationGraph.YAxis.MirrorTicks = true;
            IntegrationGraph.XAxis.MirrorTicks = true;
        }

        public void SetupFrames(nfloat width, nfloat height)
        {
            var halfheight = height / 2;

            var x = DataGraph.Center.X - PlotBox.Width * 0.5f + Margin.Left * 0.5f - Margin.Right * 0.5f;

            //DataGraph.AutoSetFrame((float)width, (float)halfheight);
            DataGraph.PlotSize = new CGSize(width * CGGraph.PPcm, halfheight * CGGraph.PPcm);
            DataGraph.Origin = new CGPoint(DataGraph.Center.X - DataGraph.PlotSize.Width * 0.5f, DataGraph.Center.Y - DataGraph.PlotSize.Height * 0.5f);
            DataGraph.Origin.Y += DataGraph.Frame.Height / 2;
            DataGraph.Origin.X = x;
            DataGraph.XAxis.Position = AxisPosition.Top;

            //IntegrationGraph.AutoSetFrame((float)width, (float)halfheight);
            IntegrationGraph.PlotSize = new CGSize(width * CGGraph.PPcm, halfheight * CGGraph.PPcm);
            IntegrationGraph.Origin = new CGPoint(IntegrationGraph.Center.X - IntegrationGraph.PlotSize.Width * 0.5f, DataGraph.Center.Y - DataGraph.PlotSize.Height * 0.5f);
            IntegrationGraph.Origin.Y -= IntegrationGraph.Frame.Height / 2;
            IntegrationGraph.Origin.X = x;
        }

        public void Draw(CGContext gc, CGPoint center)
        {
            DataGraph.Center = center;
            IntegrationGraph.Center = center;

            SetupFrames(PlotDimensions.Width, PlotDimensions.Height);

            gc.SetFillColor(NSColor.White.CGColor);
            gc.FillRect(PlotBox.WithMargin(Margin));

            DataGraph.SetupAxisScalingUnits();
            IntegrationGraph.SetupAxisScalingUnits();

            DataGraph.Draw(gc);
            IntegrationGraph.Draw(gc);

            DataGraph.DrawFrame(gc);
            IntegrationGraph.DrawFrame(gc);
        }
    }
}
