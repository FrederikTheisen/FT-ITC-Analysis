// This file has been autogenerated from a class added in the UI designer.

using System;
using System.Linq;

using Foundation;
using AppKit;
using CoreGraphics;
using Utilities;

namespace AnalysisITC
{
	public partial class DataProcessingGraphView : NSGraph
    {
        ExperimentData data;

        bool isBaselineZoomed = false;
        bool isInjectionZoomed = false;

        int selectedPeak = 0;
        public int SelectedPeak
        {
            get => selectedPeak;
            set
            {
                selectedPeak = value;

                if (selectedPeak < 0) selectedPeak = 0;
                else if (selectedPeak >= data.InjectionCount) selectedPeak = data.InjectionCount - 1;

                if (isInjectionZoomed) FocusPeak();
                if (!isBaselineZoomed) ShowAllVertical();
            }
        }

		public DataProcessingGraphView (IntPtr handle) : base (handle)
		{
		}

        public void Initialize(ExperimentData experiment)
        {
            data = experiment;

            if (experiment != null)
            {
                Graph = new BaselineFittingGraph(experiment, this);
            }
            else Graph = null;

            isBaselineZoomed = false;

            Invalidate();
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            var cg = NSGraphicsContext.CurrentContext.CGContext;

            if (Graph != null)
            {
                Graph.PrepareDraw(cg, new CGPoint(dirtyRect.GetMidX(), dirtyRect.GetMidY()));
            }

            base.DrawRect(dirtyRect);
        }

        public override void SetFrameSize(CGSize newSize)
        {
            base.SetFrameSize(newSize);

            Invalidate();
        }

        public void ZoomBaseline()
        {
            if (data == null) return;
            if (data.Processor.Interpolator?.Baseline == null) return;
            if (Graph == null) return;

            var xmin = Graph.XAxis.Min;
            var xmax = Graph.XAxis.Max;

            var baselinemin = double.MaxValue;
            var baselinemax = double.MinValue;

            for (int i = 0; i < data.DataPoints.Count; i++)
            {
                DataPoint dp = data.DataPoints[i];

                if (dp.Time < xmin) continue;
                else if (dp.Time > xmax) break;

                var blp = data.Processor.Interpolator.Baseline[i];

                if (blp < baselinemin) baselinemin = (double)blp;
                if (blp > baselinemax) baselinemax = (double)blp;
            }

            var mean = data.DataPoints.Where(dp => dp.Time > xmin && dp.Time < xmax).Select(dp => dp.Power).Average();
            var ymin = data.DataPoints.Where(dp => dp.Time > xmin && dp.Time < xmax).Min(dp => dp.Power);
            var ymax = data.DataPoints.Where(dp => dp.Time > xmin && dp.Time < xmax).Max(dp => dp.Power);

            var delta = 0.0;

            var delta1 = mean - ymin;
            var delta2 = ymax - mean;

            if (delta1 < delta2) delta = delta1.Value;
            else delta = delta2.Value;

            Graph.SetYAxisRange(baselinemin - delta * .5f, baselinemax + delta * .5f);

            isBaselineZoomed = true;

            Invalidate();
        }

        public void ShowAllVertical()
        {
            if (data == null) return;
            if (Graph == null) return;

            var xmin = Graph.XAxis.Min;
            var xmax = Graph.XAxis.Max;

            Graph.SetYAxisRange(data.DataPoints.Where(dp => dp.Time > xmin).Min(dp => dp.Power).Value, data.DataPoints.Where(dp => dp.Time < xmax).Max(dp => dp.Power).Value, buffer: true);

            isBaselineZoomed = false;

            Invalidate();
        }

        public void FocusPeak()
        {
            if (data == null) return;
            if (Graph == null) return;

            var inj = data.Injections[SelectedPeak];

            Graph.SetXAxisRange(inj.Time - inj.Delay * 0.2f, inj.Time + inj.Delay * 1.2f);

            isInjectionZoomed = true;

            if (isBaselineZoomed) ZoomBaseline();

            Invalidate();
        }

        public void UnfocusPeak()
        {
            if (Graph == null) return;

            Graph.SetXAxisRange(data.DataPoints.Min(dp => dp.Time), data.DataPoints.Max(dp => dp.Time), buffer: false);

            isInjectionZoomed = false;

            if (isBaselineZoomed) ZoomBaseline();
            else ShowAllVertical();

            Invalidate();
        }

        public void SetFeatureVisibility(bool baseline, bool injections)
        {
            BaselineFittingGraph.RenderBaseline = baseline;
            BaselineFittingGraph.RenderInjections = injections;

            Invalidate();
        }

        public override void MouseMoved(NSEvent theEvent)
        {
            base.MouseMoved(theEvent);

            if (Graph == null) return;

            var b = (Graph as BaselineFittingGraph).IsCursorOnFeature(CursorPositionInView);

            if (b) NSCursor.PointingHandCursor.Set();
            else NSCursor.ArrowCursor.Set();
        }
    }
}
