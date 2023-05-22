// This file has been autogenerated from a class added in the UI designer.

using System;
using System.Linq;
using AppKit;
using CoreGraphics;

namespace AnalysisITC
{
    public partial class DataProcessingGraphView : NSGraph
    {
        public event EventHandler<int> InjectionSelected;

        public new BaselineFittingGraph Graph => base.Graph as BaselineFittingGraph;
        Utilities.MouseOverFeatureEvent SelectedFeature { get; set; } = null;
        public int PeakZoomWidth { get; set; } = 1;
        bool isBaselineZoomed = false;
        bool isInjectionZoomed = false;
        int selectedPeak = -1;

        NSBox ZoomSelectionBox = new NSBox() { BorderType = NSBorderType.LineBorder, Hidden = true, TitlePosition = NSTitlePosition.NoTitle, BoxType = NSBoxType.NSBoxCustom };

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
            get => Graph != null ? Graph.ShowInjections : false;
            set { if (Graph != null) Graph.ShowInjections = value; }
        }

        bool DrawCursorPositionInfo
        {
            set { if (Graph != null) Graph.DrawCursorPositionInfo = value; }
        }

        public int SelectedPeak
        {
            get => selectedPeak >= Data.InjectionCount ? Data.InjectionCount - 1 : selectedPeak;
            set
            {
                if (value == -1) selectedPeak = value;
                else
                {
                    selectedPeak = value;

                    if (selectedPeak < 0) selectedPeak = 0;
                    else if (selectedPeak >= Data.InjectionCount) selectedPeak = Data.InjectionCount - 1;
                }

                Graph.SetFocusedInjection(selectedPeak);

                if (isInjectionZoomed) FocusPeak();
                if (!isBaselineZoomed) ShowAllVertical();
            }
        }

        public DataProcessingGraphView(IntPtr handle) : base(handle)
        {
            State = ProgramState.Process;

            NSEvent.AddLocalMonitorForEventsMatchingMask(NSEventMask.KeyDown, (NSEvent theEvent) => KeyDownEventHandler(theEvent));

            this.AddSubview(ZoomSelectionBox);
        }

        NSEvent KeyDownEventHandler(NSEvent theEvent)
        {
            if (StateManager.CurrentState != State) return theEvent;

            if (theEvent.KeyCode == (int)NSKey.Space)
                if (SelectedPeak != -1 && isInjectionZoomed)
                {
                    var length = Data.Injections[SelectedPeak].IntegrationLength;
                    SelectedPeak++;
                    Data.Injections[SelectedPeak].SetCustomIntegrationTimes(null, length, true);
                    FocusPeak();
                }

            return theEvent;
        }

        public void Initialize(ExperimentData experiment)
        {
            selectedPeak = -1;

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
            if (SelectedPeak == -1) return;
            int idx1 = SelectedPeak - PeakZoomWidth;
            int idx2 = SelectedPeak + PeakZoomWidth;

            if (idx1 < 0) idx1 = 0;
            if (idx2 >= Data.InjectionCount) idx2 = Data.InjectionCount - 1;

            var inj_first = Data.Injections[idx1];
            var inj_last = Data.Injections[idx2];

            //If first injection is #0, then start draw at t = 0
            Graph.SetXAxisRange(idx1 == 0 ? 0 : inj_first.Time - inj_first.Delay * 0.2f, inj_last.Time + inj_last.Delay * 1.2f);

            isInjectionZoomed = true;

            if (isBaselineZoomed) ZoomBaseline();
            else ShowAllVertical();

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

        void ZoomRegion(CGRect region)
        {
            if (Data == null) return;

            Graph.SetXAxisRange(region.Left, region.Right);
            Graph.SetYAxisRange(region.Bottom, region.Top);

            Invalidate();
        }

        public void SetFeatureVisibility(NSSegmentedControl sender)
        {
            SetFeatureVisibility(sender.IsSelectedForSegment(0), sender.IsSelectedForSegment(1), sender.IsSelectedForSegment(2), false);
        }

        public void SetFeatureVisibility(bool baseline, bool injections, bool corrected, bool cursorinfo)
        {
            ShowBaseline = baseline;
            ShowInjections = injections;
            ShowBaselineCorrected = corrected;
            DrawCursorPositionInfo = cursorinfo;

            Invalidate();
        }

        public override void MouseMoved(NSEvent theEvent)
        {
            base.MouseMoved(theEvent);

            if (Data == null) return;

            var b = Graph.CursorFeatureFromPos(CursorPositionInView);
            var update = Graph.SetCursorInfo(CursorPositionInView);

            if (b.IsMouseOverFeature)
            {
                if (b.Type == Utilities.MouseOverFeatureEvent.FeatureType.BaselineSplinePoint) NSCursor.ResizeUpDownCursor.Set();
                else if (b.Type == Utilities.MouseOverFeatureEvent.FeatureType.BaselineSplineHandle) NSCursor.ResizeUpDownCursor.Set();
                else if (b.Type == Utilities.MouseOverFeatureEvent.FeatureType.IntegrationRangeMarker) NSCursor.ResizeLeftRightCursor.Set();
            }
            else if (update) NSCursor.CrosshairCursor.Set();
            else NSCursor.ArrowCursor.Set();

            if (update) Invalidate();
        }

        public override void MouseDown(NSEvent theEvent)
        {
            base.MouseDown(theEvent);

            if (Data == null) return;

            SelectedFeature = Graph.CursorFeatureFromPos(CursorPositionInView, true);
            //SelectedFeature.ClickCursorPosition = theEvent.LocationInWindow;
        }

        public override void RightMouseDown(NSEvent theEvent)
        {
            base.RightMouseDown(theEvent);

            if (Data == null) return;

            var feature = Graph.CursorFeatureFromPos(CursorPositionInView);

            if (feature.IsMouseOverFeature)
            {
                if (feature.Type == Utilities.MouseOverFeatureEvent.FeatureType.BaselineSplinePoint)
                {

                    NSMenu menu = new NSMenu("Spline Point Options");
                    menu.AddItem(new NSMenuItem("Remove", (s, e) => { Data.Processor.Interpolator.SplineInterpolator.RemoveSplinePoint(feature.FeatureID); }));
                    WillOpenMenu(menu, theEvent);

                    NSMenu.PopUpContextMenu(menu, theEvent, this);
                }
            }
            else if (Data.Processor.Interpolator is SplineInterpolator)
            {
                var xfraction = (CursorPositionInView.X - Graph.Frame.X) / Graph.Frame.Width;
                var time = xfraction * (Graph.XAxis.Max - Graph.XAxis.Min) + Graph.XAxis.Min;

                NSMenu menu = new NSMenu("New Spline Point");
                menu.AddItem(new NSMenuItem("New Spline Point..."));
                menu.AddItem(new NSMenuItem("at data", (s, e) => { (Data.Processor.Interpolator as SplineInterpolator).InsertSplinePoint(time, true); }) { IndentationLevel = 1 });
                menu.AddItem(new NSMenuItem("at baseline", (s, e) => { (Data.Processor.Interpolator as SplineInterpolator).InsertSplinePoint(time, false); }) { IndentationLevel = 1 });
                NSMenu.PopUpContextMenu(menu, theEvent, this);
            }
        }

        public override void MouseDragged(NSEvent theEvent)
        {
            base.MouseDragged(theEvent);

            var position = CursorPositionInView;// theEvent.LocationInWindow;

            //if (SelectedFeature.FeatureID == -1) return;
            switch (SelectedFeature.Type)
            {
                case Utilities.MouseOverFeatureEvent.FeatureType.BaselineSplinePoint:
                    {
                        var feature = (Data.Processor.Interpolator as SplineInterpolator).SplinePoints[SelectedFeature.FeatureID];
                        var adjust = 10E-10 * (position.Y - SelectedFeature.ClickCursorPosition.Y);

                        feature.Power = SelectedFeature.FeatureReferenceValue + adjust;
                        break;
                    }
                case Utilities.MouseOverFeatureEvent.FeatureType.BaselineSplineHandle:
                    {
                        bool invert = SelectedFeature.SubID == 0;

                        var feature = (Data.Processor.Interpolator as SplineInterpolator).SplinePoints[SelectedFeature.FeatureID];
                        var adjust = 10E-12 * (position.Y - SelectedFeature.ClickCursorPosition.Y);
                        if (invert) adjust = -adjust;

                        feature.Slope = SelectedFeature.FeatureReferenceValue + adjust;
                        break;
                    }
                case Utilities.MouseOverFeatureEvent.FeatureType.IntegrationRangeMarker:
                    {
                        Data.IntegrationLengthMode = InjectionData.IntegrationLengthMode.Time;
                        bool start = SelectedFeature.SubID == 0;

                        var xfraction = (CursorPositionInView.X - Graph.Frame.X) / Graph.Frame.Width;
                        var time = xfraction * (Graph.XAxis.Max - Graph.XAxis.Min) + Graph.XAxis.Min;
                        var inj = Data.Injections[SelectedFeature.FeatureID];

                        if (start) inj.SetCustomIntegrationTimes((float)time - inj.Time, inj.IntegrationLength, forcetime: true);
                        else inj.SetCustomIntegrationTimes(inj.IntegrationStartDelay, (float)time - inj.Time, forcetime: true);

                        InjectionSelected?.Invoke(null, SelectedFeature.FeatureID);

                        break;
                    }
                case Utilities.MouseOverFeatureEvent.FeatureType.DragZoom:
                    {
                        ZoomSelectionBox.Frame = SelectedFeature.GetZoomRect(Graph, position);
                        ZoomSelectionBox.Hidden = false;

                        break;
                    }
                    
            }

            Invalidate();
        }

        public override void MouseUp(NSEvent theEvent)
        {
            base.MouseUp(theEvent);

            if (MouseDidDrag)
            {
                switch (SelectedFeature.Type)
                {
                    case Utilities.MouseOverFeatureEvent.FeatureType.BaselineSplineHandle:
                    case Utilities.MouseOverFeatureEvent.FeatureType.BaselineSplinePoint: UpdateSplineHandle(); break;
                    case Utilities.MouseOverFeatureEvent.FeatureType.IntegrationRangeMarker: Data.Processor.IntegratePeaks(); break;
                    case Utilities.MouseOverFeatureEvent.FeatureType.DragZoom:
                        ZoomSelectionBox.Hidden = true;
                        ZoomRegion(SelectedFeature.GetZoomRegion(Graph, CursorPositionInView));
                        Console.WriteLine(SelectedFeature.GetZoomRegion(Graph, CursorPositionInView));
                        break;
                }

                if (Data == null) return;

                var b = Graph.CursorFeatureFromPos(CursorPositionInView);

                if (b.IsMouseOverFeature)
                {
                    if (b.Type == Utilities.MouseOverFeatureEvent.FeatureType.BaselineSplinePoint) NSCursor.ResizeUpDownCursor.Set();
                    else if (b.Type == Utilities.MouseOverFeatureEvent.FeatureType.BaselineSplineHandle) NSCursor.ResizeUpDownCursor.Set();
                    else if (b.Type == Utilities.MouseOverFeatureEvent.FeatureType.IntegrationRangeMarker) NSCursor.ResizeLeftRightCursor.Set();
                }
                else NSCursor.ArrowCursor.Set();
            }
            else if (ShowInjections)
            {
                var xfraction = (CursorPositionInView.X - Graph.Frame.X) / Graph.Frame.Width;
                var time = xfraction * (Graph.XAxis.Max - Graph.XAxis.Min) + Graph.XAxis.Min;

                var clickedinj = Data.Injections.Where(inj => inj.Time < time && inj.Time + inj.Delay > time);

                if (clickedinj.Count() != 0)
                {
                    SelectedPeak = clickedinj.First().ID;
                    InjectionSelected?.Invoke(clickedinj.First(), clickedinj.First().ID);
                }

                if (theEvent.ClickCount > 1 && SelectedPeak != -1) FocusPeak(); // Double click
            }
        }

        public override void MouseExited(NSEvent theEvent)
        {
            if (Graph == null) return;

            base.MouseExited(theEvent);

            Invalidate();
        }

        async void UpdateSplineHandle()
        {
            await Data.Processor.Interpolator.Interpolate(new System.Threading.CancellationToken(), false);

            Data.Processor.SubtractBaseline();

            Invalidate();

            Data.Processor.IntegratePeaks();
        }
    }
}
