// This file has been autogenerated from a class added in the UI designer.

using System;
using System.Linq;
using AppKit;
using CoreGraphics;

namespace AnalysisITC
{
    public partial class DataProcessingGraphView : NSGraph
    {
        bool isBaselineZoomed = false;
        bool isInjectionZoomed = false;

        int selectedPeak = 0;

        bool ShowBaselineCorrected
        {
            get => Graph != null ? Graph.ShowBaselineCorrected : false;
            set { if (Graph != null) Graph.ShowBaselineCorrected = value; }
        }

        bool ShowBaseline
        {
            //get => Graph != null ? Graph.ShowBaseline : false;
            set { if (Graph != null) Graph.ShowBaseline = value; }
        }

        bool ShowInjections
        {
            //get => Graph != null ? Graph.ShowInjections : false;
            set { if (Graph != null) Graph.ShowInjections = value; }
        }

        public new BaselineFittingGraph Graph => base.Graph as BaselineFittingGraph;

        public DataProcessingGraphView(IntPtr handle) : base(handle)
        {
            State = ProgramState.Process;
        }

        public int SelectedPeak
        {
            get => selectedPeak;
            set
            {
                selectedPeak = value;

                if (selectedPeak < 0) selectedPeak = 0;
                else if (selectedPeak >= Data.InjectionCount) selectedPeak = Data.InjectionCount - 1;

                if (isInjectionZoomed) FocusPeak();
                if (!isBaselineZoomed) ShowAllVertical();
            }
        }

        public void Initialize(ExperimentData experiment)
        {
            if (experiment != null)
            {
                base.Graph = new BaselineFittingGraph(experiment, this);
            }
            else base.Graph = null;

            isBaselineZoomed = false;
        }

        public void ZoomBaseline() //TODO fix view scaling when baselinecorrected
        {
            if (Data == null) return;
            if (Data.Processor.Interpolator?.Baseline == null) return;

            var xmin = Graph.XAxis.Min;
            var xmax = Graph.XAxis.Max;

            var baselinemin = double.MaxValue;
            var baselinemax = double.MinValue;

            if (!ShowBaselineCorrected) for (int i = 0; i < Data.DataPoints.Count; i++)
                {
                    DataPoint dp = Graph.DataPoints[i];

                    if (dp.Time < xmin) continue;
                    else if (dp.Time > xmax) break;

                    var blp = Data.Processor.Interpolator.Baseline[i];

                    if (blp < baselinemin) baselinemin = (double)blp;
                    if (blp > baselinemax) baselinemax = (double)blp;
                }
            else
            {
                baselinemin = 0;
                baselinemax = 0;
            }

            var mean = Graph.DataPoints.Where(dp => dp.Time > xmin && dp.Time < xmax).Select(dp => dp.Power).Average();
            var ymin = Graph.DataPoints.Where(dp => dp.Time > xmin && dp.Time < xmax).Min(dp => dp.Power);
            var ymax = Graph.DataPoints.Where(dp => dp.Time > xmin && dp.Time < xmax).Max(dp => dp.Power);

            var delta = 0.0;

            var delta1 = mean - ymin;
            var delta2 = ymax - mean;

            if (delta1 < delta2) delta = delta1;
            else delta = delta2;

            Graph.SetYAxisRange(baselinemin - delta * .5f, baselinemax + delta * .5f);

            isBaselineZoomed = true;

            Invalidate();
        }

        public void ShowAllVertical()
        {
            if (Data == null) return;

            var xmin = Graph.XAxis.Min;
            var xmax = Graph.XAxis.Max;

            var dps = Graph.DataPoints;

            Graph.SetYAxisRange(dps.Where(dp => dp.Time > xmin).Min(dp => dp.Power), dps.Where(dp => dp.Time < xmax).Max(dp => dp.Power), buffer: true);

            isBaselineZoomed = false;

            Invalidate();
        }

        public void FocusPeak()
        {
            if (Data == null) return;

            var inj = Data.Injections[SelectedPeak];

            Graph.SetXAxisRange(inj.Time - inj.Delay * 0.2f, inj.Time + inj.Delay * 1.2f);

            isInjectionZoomed = true;

            if (isBaselineZoomed) ZoomBaseline();

            Invalidate();
        }

        public void UnfocusPeak()
        {
            if (Data == null) return;

            Graph.SetXAxisRange(Graph.DataPoints.Min(dp => dp.Time), Graph.DataPoints.Max(dp => dp.Time), buffer: false);

            isInjectionZoomed = false;

            if (isBaselineZoomed) ZoomBaseline();
            else ShowAllVertical();

            Invalidate();
        }

        public void SetFeatureVisibility(NSSegmentedControl sender)
        {
            SetFeatureVisibility(sender.IsSelectedForSegment(0), sender.IsSelectedForSegment(1), sender.IsSelectedForSegment(2));
        }

        public void SetFeatureVisibility(bool baseline, bool injections, bool corrected)
        {
            ShowBaseline = baseline;
            ShowInjections = injections;
            ShowBaselineCorrected = corrected;

            Invalidate();
        }

        public override void MouseMoved(NSEvent theEvent)
        {
            base.MouseMoved(theEvent);

            if (Data == null) return;

            var b = Graph.IsCursorOnFeature(CursorPositionInView);

            if (b.IsMouseOverFeature) NSCursor.PointingHandCursor.Set();
            else NSCursor.ArrowCursor.Set();
        }
    }
}
