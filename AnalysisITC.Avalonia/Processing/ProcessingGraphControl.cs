using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Processing
{
    public sealed class ProcessingGraphControl : Control
    {
        static readonly IBrush CanvasBrush = Solid("#F4F7FA");
        static readonly IBrush PlotBrush = Brushes.White;
        static readonly IBrush TextBrush = Solid("#202832");
        static readonly IBrush MutedTextBrush = Solid("#607080");
        static readonly IBrush DataBrush = Solid("#1E5F84");
        static readonly IBrush CorrectedDataBrush = Solid("#365D41");
        static readonly IBrush BaselineBrush = Solid("#BE3A34");
        static readonly IBrush SplinePointBrush = Solid("#BE3A34");
        static readonly IBrush RegionBrush = Solid("#2563EB");
        static readonly IBrush RegionMutedBrush = Solid("#7B8794");
        static readonly IBrush HoverBrush = Solid("#26323D");
        static readonly IBrush HoverBackgroundBrush = Solid("#FFFFFF");
        static readonly IBrush ZoomBrush = new SolidColorBrush(Color.FromArgb(50, 37, 99, 235));

        static readonly Pen FramePen = new Pen(Solid("#AEB8C2"), 1);
        static readonly Pen AxisPen = new Pen(Solid("#26323D"), 1);
        static readonly Pen MajorGridPen = new Pen(Solid("#D8DEE6"), 1);
        static readonly Pen MinorGridPen = new Pen(Solid("#EEF2F6"), 1);
        static readonly Pen DataPen = new Pen(DataBrush, 1.2);
        static readonly Pen CorrectedDataPen = new Pen(CorrectedDataBrush, 1.2);
        static readonly Pen BaselinePen = new Pen(BaselineBrush, 1.8);
        static readonly Pen HoverPen = new Pen(HoverBrush, 1);
        static readonly Pen ZoomPen = new Pen(Solid("#2563EB"), 1);

        const double MarkerHitWidth = 8;
        const double DragThreshold = 5;
        const double SplinePointRadius = 4.5;

        ExperimentData? experiment;
        GraphViewport view;
        bool hasView;
        bool isPointerCaptured;
        bool isZoomDragging;
        HitTarget dragTarget;
        Point dragStart;
        Point dragCurrent;
        Point? hoverPoint;
        DataPoint? hoverData;
        int pressedClickCount;
        int selectedInjectionIndex = -1;

        public event EventHandler<int>? SelectedInjectionChanged;
        public event EventHandler? IntegrationEdited;
        public event EventHandler? IntegrationEditCompleted;
        public event EventHandler? CopySelectedIntegrationToNextRequested;

        public enum VerticalZoomMode
        {
            None,
            AllData,
            Baseline
        }

        public enum HorizontalZoomMode
        {
            None,
            AllPeaks,
            SelectedPeak
        }

        public ProcessingGraphControl()
        {
            Focusable = true;
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Cross);
        }

        public ExperimentData? Experiment
        {
            get => experiment;
            set
            {
                if (ReferenceEquals(experiment, value)) return;

                experiment = value;
                selectedInjectionIndex = -1;
                CurrentVerticalZoomMode = VerticalZoomMode.AllData;
                CurrentHorizontalZoomMode = HorizontalZoomMode.AllPeaks;
                FitToData();
            }
        }

        public int SelectedInjectionIndex
        {
            get => selectedInjectionIndex < 0 ? -1 : ClampInjectionIndex(selectedInjectionIndex);
            set
            {
                var next = value < 0 ? -1 : ClampInjectionIndex(value);
                if (selectedInjectionIndex == next) return;

                selectedInjectionIndex = next;
                SelectedInjectionChanged?.Invoke(this, SelectedInjectionIndex);
                InvalidateVisual();
            }
        }

        public int PeakZoomWidth { get; set; } = 1;
        public bool ShowBaseline { get; set; } = true;
        public bool ShowIntegrationRegions { get; set; } = true;
        public bool ShowBaselineCorrected { get; set; }
        public bool ShowCursorInfo { get; set; } = true;
        public VerticalZoomMode CurrentVerticalZoomMode { get; private set; } = VerticalZoomMode.AllData;
        public HorizontalZoomMode CurrentHorizontalZoomMode { get; private set; } = HorizontalZoomMode.AllPeaks;
        public bool IsInjectionFocused => CurrentHorizontalZoomMode == HorizontalZoomMode.SelectedPeak;

        PowerDisplay Power => PowerDisplay.Current;

        public void SetFeatureVisibility(bool baseline, bool integrationRegions, bool corrected, bool cursorInfo)
        {
            ShowBaseline = baseline;
            ShowIntegrationRegions = integrationRegions;
            ShowBaselineCorrected = corrected;
            ShowCursorInfo = cursorInfo;

            ApplyVerticalZoomMode(CurrentVerticalZoomMode);
            InvalidateVisual();
        }

        public void FitToData()
        {
            var points = DisplayDataPoints();
            if (points.Count < 2)
            {
                hasView = false;
                hoverPoint = null;
                hoverData = null;
                InvalidateVisual();
                return;
            }

            var power = Power;
            var xMin = points.Min(point => (double)point.Time);
            var xMax = points.Max(point => (double)point.Time);
            var yMin = points.Min(point => power.Convert(point.Power));
            var yMax = points.Max(point => power.Convert(point.Power));

            view = GraphViewport.WithPadding(xMin, xMax, yMin, yMax, xPaddingFraction: 0.015, yPaddingFraction: 0.08);
            hasView = true;
            hoverPoint = null;
            hoverData = null;
            CurrentVerticalZoomMode = VerticalZoomMode.AllData;
            CurrentHorizontalZoomMode = HorizontalZoomMode.AllPeaks;

            InvalidateVisual();
        }

        public void ShowAllInjections()
        {
            var points = DisplayDataPoints();
            if (points.Count < 2)
            {
                FitToData();
                return;
            }

            view = new GraphViewport(points.Min(point => point.Time), points.Max(point => point.Time), view.YMin, view.YMax);
            CurrentHorizontalZoomMode = HorizontalZoomMode.AllPeaks;
            ApplyVerticalZoomMode(CurrentVerticalZoomMode);
            InvalidateVisual();
        }

        public void FocusSelectedInjection()
        {
            var data = Experiment;
            if (data == null || data.InjectionCount == 0 || SelectedInjectionIndex < 0)
                return;

            var firstIndex = Math.Max(0, SelectedInjectionIndex - Math.Max(0, PeakZoomWidth));
            var lastIndex = Math.Min(data.InjectionCount - 1, SelectedInjectionIndex + Math.Max(0, PeakZoomWidth));
            var first = data.Injections[firstIndex];
            var last = data.Injections[lastIndex];
            var xMin = firstIndex == 0 ? 0 : first.Time - first.Delay * 0.2;
            var xMax = last.Time + last.Delay * 1.2;

            view = new GraphViewport(xMin, xMax, view.YMin, view.YMax);
            CurrentHorizontalZoomMode = HorizontalZoomMode.SelectedPeak;
            ApplyVerticalZoomMode(CurrentVerticalZoomMode);
            InvalidateVisual();
        }

        public void ShowAllVertical()
        {
            if (!hasView) return;

            var points = DisplayDataPoints()
                .Where(point => point.Time >= view.XMin && point.Time <= view.XMax)
                .ToList();

            if (points.Count < 2) return;

            var power = Power;
            view = GraphViewport.WithPadding(
                view.XMin,
                view.XMax,
                points.Min(point => power.Convert(point.Power)),
                points.Max(point => power.Convert(point.Power)),
                xPaddingFraction: 0,
                yPaddingFraction: 0.08);

            CurrentVerticalZoomMode = VerticalZoomMode.AllData;
            InvalidateVisual();
        }

        public void ZoomBaseline()
        {
            var data = Experiment;
            if (!hasView || data?.Processor?.Interpolator?.Baseline == null || data.Processor.Interpolator.Baseline.Count == 0)
                return;

            if (ShowBaselineCorrected)
            {
                var corrected = DisplayDataPoints()
                    .Where(point => point.Time >= view.XMin && point.Time <= view.XMax)
                    .ToList();

                if (corrected.Count < 2) return;

                var power = Power;
                var yMin = corrected.Min(point => power.Convert(point.Power));
                var yMax = corrected.Max(point => power.Convert(point.Power));
                var span = Math.Max(Math.Abs(yMin), Math.Abs(yMax));
                if (span < double.Epsilon) span = 1;

                view = new GraphViewport(view.XMin, view.XMax, -span * 1.15, span * 1.15);
            }
            else
            {
                var baseline = BaselinePoints()
                    .Where(point => point.Time >= view.XMin && point.Time <= view.XMax)
                    .ToList();

                if (baseline.Count < 2) return;

                var dataPoints = DisplayDataPoints()
                    .Where(point => point.Time >= view.XMin && point.Time <= view.XMax)
                    .ToList();

                var power = Power;
                var baselineMin = baseline.Min(point => power.Convert(point.Power));
                var baselineMax = baseline.Max(point => power.Convert(point.Power));
                var mean = dataPoints.Count > 0 ? dataPoints.Average(point => power.Convert(point.Power)) : (baselineMin + baselineMax) / 2;
                var dataMin = dataPoints.Count > 0 ? dataPoints.Min(point => power.Convert(point.Power)) : baselineMin;
                var dataMax = dataPoints.Count > 0 ? dataPoints.Max(point => power.Convert(point.Power)) : baselineMax;
                var delta = Math.Min(Math.Abs(mean - dataMin), Math.Abs(dataMax - mean));
                if (delta < double.Epsilon) delta = Math.Max(1, Math.Abs(baselineMax - baselineMin));

                view = new GraphViewport(view.XMin, view.XMax, baselineMin - delta * 3, baselineMax + delta * 3);
            }

            CurrentVerticalZoomMode = VerticalZoomMode.Baseline;
            InvalidateVisual();
        }

        public void ApplyVerticalZoomMode(VerticalZoomMode mode)
        {
            switch (mode)
            {
                case VerticalZoomMode.Baseline:
                    ZoomBaseline();
                    break;
                case VerticalZoomMode.AllData:
                    ShowAllVertical();
                    break;
                case VerticalZoomMode.None:
                    InvalidateVisual();
                    break;
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            context.DrawRectangle(CanvasBrush, null, bounds);

            if (bounds.Width < 140 || bounds.Height < 140)
                return;

            var graph = GraphLayout.Create(bounds, view, Power);
            context.DrawRectangle(PlotBrush, FramePen, graph.Plot);

            if (Experiment?.HasThermogram != true || !hasView)
            {
                DrawEmptyState(context, graph.Plot);
                return;
            }

            DrawGrid(context, graph);
            DrawIntegrationRegions(context, graph);
            DrawData(context, graph);
            DrawBaseline(context, graph);
            DrawSplinePoints(context, graph);
            DrawAxes(context, graph);
            DrawZoomSelection(context);
            DrawHover(context, graph);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (!hasView) return;

            var graph = GraphLayout.Create(Bounds, view, Power);
            var point = e.GetPosition(this);
            if (!graph.Plot.Contains(point)) return;

            Focus();
            pressedClickCount = e.ClickCount;
            dragStart = point;
            dragCurrent = point;
            dragTarget = HitTest(point, graph);
            isPointerCaptured = true;
            e.Pointer.Capture(this);

            if (dragTarget.Kind == HitKind.IntegrationStart || dragTarget.Kind == HitKind.IntegrationEnd)
            {
                Cursor = new Cursor(StandardCursorType.SizeWestEast);
            }
            else
            {
                isZoomDragging = true;
                Cursor = new Cursor(StandardCursorType.Cross);
            }

            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (!hasView)
            {
                hoverPoint = null;
                hoverData = null;
                return;
            }

            var graph = GraphLayout.Create(Bounds, view, Power);
            var point = e.GetPosition(this);

            if (isPointerCaptured)
            {
                dragCurrent = point;

                if (dragTarget.Kind == HitKind.IntegrationStart || dragTarget.Kind == HitKind.IntegrationEnd)
                {
                    UpdateIntegrationMarker(point, graph);
                }

                InvalidateVisual();
                e.Handled = true;
                return;
            }

            UpdateHover(point, graph);
            var hit = HitTest(point, graph);
            Cursor = hit.Kind == HitKind.IntegrationStart || hit.Kind == HitKind.IntegrationEnd
                ? new Cursor(StandardCursorType.SizeWestEast)
                : new Cursor(StandardCursorType.Cross);

            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (!isPointerCaptured)
                return;

            var graph = GraphLayout.Create(Bounds, view, Power);
            var point = e.GetPosition(this);
            var moved = Distance(dragStart, point) > DragThreshold;
            var wasIntegrationDrag = dragTarget.Kind == HitKind.IntegrationStart || dragTarget.Kind == HitKind.IntegrationEnd;

            isPointerCaptured = false;
            isZoomDragging = false;
            e.Pointer.Capture(null);

            if (wasIntegrationDrag)
            {
                IntegrationEditCompleted?.Invoke(this, EventArgs.Empty);
            }
            else if (moved)
            {
                ZoomRegion(dragStart, point, graph);
            }
            else
            {
                var hit = HitTest(point, graph);
                if (hit.InjectionIndex >= 0)
                {
                    SelectedInjectionIndex = hit.InjectionIndex;
                    if (pressedClickCount > 1)
                        FocusSelectedInjection();
                }
            }

            Cursor = new Cursor(StandardCursorType.Cross);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);

            if (isPointerCaptured) return;

            hoverPoint = null;
            hoverData = null;
            Cursor = new Cursor(StandardCursorType.Cross);
            InvalidateVisual();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (Experiment == null) return;

            switch (e.Key)
            {
                case Key.Left:
                    SelectedInjectionIndex = SelectedInjectionIndex <= 0 ? 0 : SelectedInjectionIndex - 1;
                    FocusSelectedInjection();
                    e.Handled = true;
                    break;
                case Key.Right:
                    SelectedInjectionIndex = SelectedInjectionIndex < 0 ? 0 : SelectedInjectionIndex + 1;
                    FocusSelectedInjection();
                    e.Handled = true;
                    break;
                case Key.Space:
                    CopySelectedIntegrationToNextRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
            }
        }

        void DrawEmptyState(DrawingContext context, Rect plot)
        {
            DrawText(context, "No thermogram selected", new Point(plot.Left + 18, plot.Top + 18), 14, FontWeight.SemiBold, MutedTextBrush);
            DrawText(context, "Open an ITC file and select an experiment to process baseline and injections.", new Point(plot.Left + 18, plot.Top + 43), 12, FontWeight.Normal, MutedTextBrush);
        }

        void DrawGrid(DrawingContext context, GraphLayout graph)
        {
            using (context.PushClip(graph.Plot))
            {
                foreach (var tick in graph.XTicks.Minor)
                {
                    var x = Crisp(graph.Transform.X(tick));
                    context.DrawLine(MinorGridPen, new Point(x, graph.Plot.Top), new Point(x, graph.Plot.Bottom));
                }

                foreach (var tick in graph.YTicks.Minor)
                {
                    var y = Crisp(graph.Transform.Y(tick));
                    context.DrawLine(MinorGridPen, new Point(graph.Plot.Left, y), new Point(graph.Plot.Right, y));
                }

                foreach (var tick in graph.XTicks.Major)
                {
                    var x = Crisp(graph.Transform.X(tick));
                    context.DrawLine(MajorGridPen, new Point(x, graph.Plot.Top), new Point(x, graph.Plot.Bottom));
                }

                foreach (var tick in graph.YTicks.Major)
                {
                    var y = Crisp(graph.Transform.Y(tick));
                    context.DrawLine(MajorGridPen, new Point(graph.Plot.Left, y), new Point(graph.Plot.Right, y));
                }
            }
        }

        void DrawAxes(DrawingContext context, GraphLayout graph)
        {
            context.DrawLine(AxisPen, new Point(graph.Plot.Left, graph.Plot.Bottom), new Point(graph.Plot.Right, graph.Plot.Bottom));
            context.DrawLine(AxisPen, new Point(graph.Plot.Left, graph.Plot.Top), new Point(graph.Plot.Left, graph.Plot.Bottom));

            foreach (var tick in graph.XTicks.Major)
            {
                if (!view.ContainsX(tick)) continue;

                var x = Crisp(graph.Transform.X(tick));
                context.DrawLine(AxisPen, new Point(x, graph.Plot.Bottom), new Point(x, graph.Plot.Bottom + 5));
                DrawCenteredText(context, graph.XTicks.Format(tick), new Point(x, graph.Plot.Bottom + 9), 11, MutedTextBrush);
            }

            foreach (var tick in graph.YTicks.Major)
            {
                if (!view.ContainsY(tick)) continue;

                var y = Crisp(graph.Transform.Y(tick));
                context.DrawLine(AxisPen, new Point(graph.Plot.Left - 5, y), new Point(graph.Plot.Left, y));
                DrawRightAlignedText(context, graph.YTicks.Format(tick), new Point(graph.Plot.Left - 9, y - 7), 11, MutedTextBrush);
            }

            DrawCenteredText(context, "Time (s)", new Point(graph.Plot.Left + graph.Plot.Width / 2, graph.Plot.Bottom + 35), 12, TextBrush);
            DrawText(context, $"Power ({Power.UnitLabel})", new Point(graph.Plot.Left, graph.Plot.Top - 24), 12, FontWeight.SemiBold, TextBrush);
        }

        void DrawData(DrawingContext context, GraphLayout graph)
        {
            var points = BuildDisplayPoints(DisplayDataPoints(), graph);
            if (points.Count < 2) return;

            DrawPolyline(context, graph.Plot, points, ShowBaselineCorrected ? CorrectedDataPen : DataPen);
        }

        void DrawBaseline(DrawingContext context, GraphLayout graph)
        {
            if (!ShowBaseline) return;
            if (Experiment?.Processor?.Interpolator?.Finished != true) return;

            var points = BuildDisplayPoints(BaselinePoints(), graph);
            if (points.Count < 2) return;

            DrawPolyline(context, graph.Plot, points, BaselinePen);
        }

        void DrawSplinePoints(DrawingContext context, GraphLayout graph)
        {
            if (!ShowBaseline) return;
            if (Experiment?.Processor?.Interpolator is not SplineInterpolator spline) return;
            if (spline.SplinePoints.Count == 0) return;

            using (context.PushClip(graph.Plot))
            {
                foreach (var point in spline.SplinePoints)
                {
                    if (!view.ContainsX(point.Time)) continue;

                    var y = ShowBaselineCorrected ? 0 : Power.Convert(point.Power);
                    if (!view.ContainsY(y)) continue;

                    var screen = graph.Transform.ToScreen(point.Time, y);
                    context.DrawEllipse(Brushes.White, new Pen(SplinePointBrush, 1.2), screen, SplinePointRadius, SplinePointRadius);
                    context.DrawEllipse(SplinePointBrush, null, screen, 1.7, 1.7);
                }
            }
        }

        void DrawIntegrationRegions(DrawingContext context, GraphLayout graph)
        {
            if (!ShowIntegrationRegions || Experiment?.Injections == null) return;

            using (context.PushClip(graph.Plot))
            {
                foreach (var injection in Experiment.Injections)
                {
                    if (injection.IntegrationEndTime < view.XMin || injection.IntegrationStartTime > view.XMax) continue;

                    var startX = graph.Transform.X(injection.IntegrationStartTime);
                    var endX = graph.Transform.X(injection.IntegrationEndTime);
                    var left = Math.Max(graph.Plot.Left, Math.Min(startX, endX));
                    var right = Math.Min(graph.Plot.Right, Math.Max(startX, endX));
                    if (right <= left) continue;

                    var selected = SelectedInjectionIndex == injection.ID;
                    var baseBrush = selected ? RegionBrush : RegionMutedBrush;
                    var fill = new SolidColorBrush(((SolidColorBrush)baseBrush).Color, selected ? 0.16 : 0.08);
                    var line = new Pen(new SolidColorBrush(((SolidColorBrush)baseBrush).Color, selected ? 0.9 : 0.45), selected ? 1.25 : 1);

                    context.DrawRectangle(fill, null, new Rect(left, graph.Plot.Top, right - left, graph.Plot.Height));
                    context.DrawLine(line, new Point(Crisp(startX), graph.Plot.Top), new Point(Crisp(startX), graph.Plot.Bottom));
                    context.DrawLine(line, new Point(Crisp(endX), graph.Plot.Top), new Point(Crisp(endX), graph.Plot.Bottom));
                }
            }
        }

        void DrawZoomSelection(DrawingContext context)
        {
            if (!isZoomDragging || Distance(dragStart, dragCurrent) <= DragThreshold) return;

            var rect = RectFromPoints(dragStart, dragCurrent);
            context.DrawRectangle(ZoomBrush, ZoomPen, rect);
        }

        void DrawHover(DrawingContext context, GraphLayout graph)
        {
            if (!ShowCursorInfo || !hoverPoint.HasValue || !hoverData.HasValue) return;

            var data = hoverData.Value;
            if (!view.ContainsX(data.Time)) return;

            var screenY = Power.Convert(ShowBaselineCorrected && Experiment?.BaseLineCorrectedDataPoints != null
                ? FindNearestDataPoint(Experiment.BaseLineCorrectedDataPoints, data.Time)?.Power ?? data.Power
                : data.Power);
            var screen = graph.Transform.ToScreen(data.Time, screenY);
            var x = Crisp(screen.X);

            using (context.PushClip(graph.Plot))
            {
                context.DrawLine(HoverPen, new Point(x, graph.Plot.Top), new Point(x, graph.Plot.Bottom));
                context.DrawEllipse(Brushes.White, HoverPen, screen, 4, 4);
            }

            var lines = BuildHoverLines(data).ToArray();
            DrawInfoBox(context, lines, graph.Plot, screen);
        }

        IEnumerable<string> BuildHoverLines(DataPoint data)
        {
            var injection = Experiment?.Injections?.FirstOrDefault(inj => data.Time >= inj.IntegrationStartTime && data.Time <= inj.IntegrationEndTime);
            if (injection != null)
            {
                var heat = double.IsFinite(injection.Enthalpy)
                    ? injection.Enthalpy2.ToFormattedString(AppSettings.EnergyUnit, withunit: true, permole: true)
                    : Power.FormatEnergy(injection.PeakArea);

                yield return $"Inj #{injection.ID + 1}: {heat}";
                yield return $"Heat: {injection.HeatDirection.GetEnumDescription()}";
            }

            yield return $"Time: {data.Time:F1} s";
            yield return $"Power: {Power.Format(Power.Convert(data.Power))}";

            if (Experiment?.BaseLineCorrectedDataPoints != null && Experiment.BaseLineCorrectedDataPoints.Count > 0)
            {
                var corrected = FindNearestDataPoint(Experiment.BaseLineCorrectedDataPoints, data.Time);
                if (corrected.HasValue)
                    yield return $"Delta power: {Power.Format(Power.Convert(corrected.Value.Power))}";
            }
        }

        void DrawInfoBox(DrawingContext context, IReadOnlyList<string> lines, Rect plot, Point anchor)
        {
            if (lines.Count == 0) return;

            const double paddingX = 9;
            const double paddingY = 7;
            const double lineGap = 3;

            var texts = lines.Select(line => CreateText(line, 11, FontWeight.Normal, TextBrush)).ToArray();
            var width = texts.Max(text => text.Width) + paddingX * 2;
            var height = texts.Sum(text => text.Height) + lineGap * (texts.Length - 1) + paddingY * 2;

            var x = anchor.X + 12;
            var y = anchor.Y - height - 10;

            if (x + width > plot.Right - 8) x = anchor.X - width - 12;
            if (y < plot.Top + 8) y = anchor.Y + 12;
            if (y + height > plot.Bottom - 8) y = plot.Bottom - height - 8;

            var rect = new Rect(x, y, width, height);
            context.DrawRectangle(HoverBackgroundBrush, new Pen(Solid("#B6C0CA"), 1), rect, 4);

            var lineY = y + paddingY;
            foreach (var text in texts)
            {
                context.DrawText(text, new Point(x + paddingX, lineY));
                lineY += text.Height + lineGap;
            }
        }

        void UpdateHover(Point point, GraphLayout graph)
        {
            if (!graph.Plot.Contains(point))
            {
                hoverPoint = null;
                hoverData = null;
                return;
            }

            hoverPoint = point;
            hoverData = FindNearestDataPoint(Experiment?.DataPoints, graph.Transform.ToData(point).X);
        }

        void UpdateIntegrationMarker(Point point, GraphLayout graph)
        {
            var data = Experiment;
            if (data == null || dragTarget.InjectionIndex < 0 || dragTarget.InjectionIndex >= data.InjectionCount) return;

            var injection = data.Injections[dragTarget.InjectionIndex];
            var time = graph.Transform.ToData(point).X;

            data.Processor.IntegrationLengthMode = InjectionData.IntegrationLengthMode.Time;

            if (dragTarget.Kind == HitKind.IntegrationStart)
                injection.SetIntegrationStartTime((float)(time - injection.Time));
            else if (dragTarget.Kind == HitKind.IntegrationEnd)
                injection.SetIntegrationLengthByTime((float)(time - injection.Time));

            selectedInjectionIndex = injection.ID;
            SelectedInjectionChanged?.Invoke(this, SelectedInjectionIndex);
            data.Processor.IntegratePeaks(false);
            IntegrationEdited?.Invoke(this, EventArgs.Empty);
        }

        void ZoomRegion(Point start, Point end, GraphLayout graph)
        {
            var rect = RectFromPoints(start, end);
            if (rect.Width < 10 || rect.Height < 10) return;

            var topLeft = graph.Transform.ToData(new Point(rect.Left, rect.Top));
            var bottomRight = graph.Transform.ToData(new Point(rect.Right, rect.Bottom));

            view = new GraphViewport(topLeft.X, bottomRight.X, bottomRight.Y, topLeft.Y);
            CurrentVerticalZoomMode = VerticalZoomMode.None;
            CurrentHorizontalZoomMode = HorizontalZoomMode.None;
        }

        HitTarget HitTest(Point point, GraphLayout graph)
        {
            var data = Experiment;
            if (data?.Injections == null || !graph.Plot.Contains(point)) return HitTarget.None;

            foreach (var injection in data.Injections)
            {
                var startX = graph.Transform.X(injection.IntegrationStartTime);
                var endX = graph.Transform.X(injection.IntegrationEndTime);

                if (Math.Abs(point.X - startX) <= MarkerHitWidth / 2)
                    return new HitTarget(HitKind.IntegrationStart, injection.ID);

                if (Math.Abs(point.X - endX) <= MarkerHitWidth / 2)
                    return new HitTarget(HitKind.IntegrationEnd, injection.ID);
            }

            foreach (var injection in data.Injections)
            {
                var startX = graph.Transform.X(injection.IntegrationStartTime);
                var endX = graph.Transform.X(injection.IntegrationEndTime);
                var left = Math.Min(startX, endX);
                var right = Math.Max(startX, endX);

                if (point.X >= left && point.X <= right)
                    return new HitTarget(HitKind.IntegrationRegion, injection.ID);
            }

            return new HitTarget(HitKind.Plot, -1);
        }

        IReadOnlyList<DataPoint> DisplayDataPoints()
        {
            if (ShowBaselineCorrected && Experiment?.BaseLineCorrectedDataPoints != null && Experiment.BaseLineCorrectedDataPoints.Count > 1)
                return Experiment.BaseLineCorrectedDataPoints;

            if (Experiment?.DataPoints != null)
                return Experiment.DataPoints;

            return Array.Empty<DataPoint>();
        }

        IReadOnlyList<DataPoint> BaselinePoints()
        {
            var data = Experiment;
            var baseline = data?.Processor?.Interpolator?.Baseline;

            if (data == null || baseline == null || baseline.Count != data.DataPoints.Count)
                return Array.Empty<DataPoint>();

            if (ShowBaselineCorrected)
            {
                return new[]
                {
                    new DataPoint(data.DataPoints.First().Time, 0),
                    new DataPoint(data.DataPoints.Last().Time, 0)
                };
            }

            var points = new List<DataPoint>(baseline.Count);
            for (int i = 0; i < data.DataPoints.Count; i++)
                points.Add(new DataPoint(data.DataPoints[i].Time, (float)baseline[i].Value));

            return points;
        }

        List<Point> BuildDisplayPoints(IReadOnlyList<DataPoint> data, GraphLayout graph)
        {
            if (data.Count == 0) return new List<Point>();

            var visible = data
                .Where(point => view.ContainsX(point.Time))
                .ToList();

            if (visible.Count == 0) return new List<Point>();

            if (visible.Count <= graph.Plot.Width * 2)
            {
                return visible
                    .Select(point => graph.Transform.ToScreen(point.Time, Power.Convert(point.Power)))
                    .ToList();
            }

            var result = new List<Point>((int)graph.Plot.Width * 3);
            PixelBucket? bucket = null;

            foreach (var point in visible)
            {
                var screen = graph.Transform.ToScreen(point.Time, Power.Convert(point.Power));
                var pixel = (int)Math.Round(screen.X);

                if (bucket == null)
                {
                    bucket = new PixelBucket(pixel, screen.Y);
                    continue;
                }

                if (bucket.Value.Pixel == pixel)
                {
                    bucket = bucket.Value.Add(screen.Y);
                    continue;
                }

                bucket.Value.AppendTo(result);
                bucket = new PixelBucket(pixel, screen.Y);
            }

            bucket?.AppendTo(result);

            return result;
        }

        static void DrawPolyline(DrawingContext context, Rect clip, IReadOnlyList<Point> points, Pen pen)
        {
            var geometry = new StreamGeometry();
            using (var stream = geometry.Open())
            {
                stream.BeginFigure(points[0], false);
                for (int i = 1; i < points.Count; i++) stream.LineTo(points[i]);
            }

            using (context.PushClip(clip))
            {
                context.DrawGeometry(null, pen, geometry);
            }
        }

        DataPoint? FindNearestDataPoint(IReadOnlyList<DataPoint>? data, double time)
        {
            if (data == null || data.Count == 0) return null;

            var low = 0;
            var high = data.Count - 1;

            while (low < high)
            {
                var mid = (low + high) / 2;
                if (data[mid].Time < time) low = mid + 1;
                else high = mid;
            }

            if (low == 0) return data[0];
            if (low >= data.Count) return data[data.Count - 1];

            var before = data[low - 1];
            var after = data[low];

            return Math.Abs(before.Time - time) <= Math.Abs(after.Time - time) ? before : after;
        }

        int ClampInjectionIndex(int index)
        {
            if (Experiment?.Injections == null || Experiment.InjectionCount == 0) return -1;
            if (index < 0) return -1;
            return Math.Min(Experiment.InjectionCount - 1, Math.Max(0, index));
        }

        static Rect RectFromPoints(Point a, Point b)
        {
            return new Rect(
                Math.Min(a.X, b.X),
                Math.Min(a.Y, b.Y),
                Math.Abs(a.X - b.X),
                Math.Abs(a.Y - b.Y));
        }

        static double Distance(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        static double Crisp(double value) => Math.Round(value) + 0.5;

        static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));

        static FormattedText CreateText(string text, double size, FontWeight weight, IBrush brush)
        {
            return new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, weight),
                size,
                brush);
        }

        static void DrawText(DrawingContext context, string text, Point point, double size, FontWeight weight, IBrush brush)
        {
            context.DrawText(CreateText(text, size, weight, brush), point);
        }

        static void DrawCenteredText(DrawingContext context, string text, Point point, double size, IBrush brush)
        {
            var formatted = CreateText(text, size, FontWeight.Normal, brush);
            context.DrawText(formatted, new Point(point.X - formatted.Width / 2, point.Y));
        }

        static void DrawRightAlignedText(DrawingContext context, string text, Point point, double size, IBrush brush)
        {
            var formatted = CreateText(text, size, FontWeight.Normal, brush);
            context.DrawText(formatted, new Point(point.X - formatted.Width, point.Y));
        }

        readonly struct PowerDisplay
        {
            public double Scale { get; }
            public string UnitLabel { get; }

            PowerDisplay(double scale, string unitLabel)
            {
                Scale = scale;
                UnitLabel = unitLabel;
            }

            public static PowerDisplay Current => AppSettings.EnergyUnit.IsSI()
                ? new PowerDisplay(1_000_000, "uW")
                : new PowerDisplay(1_000_000 * Energy.JouleToCalFactor, "ucal/s");

            public double Convert(double power) => power * Scale;

            public string Format(double value) => $"{value:G4} {UnitLabel}";

            public string FormatEnergy(double value)
            {
                var scaled = AppSettings.EnergyUnit.IsSI()
                    ? value * 1_000_000
                    : value * 1_000_000 * Energy.JouleToCalFactor;

                return $"{scaled:G4} {(AppSettings.EnergyUnit.IsSI() ? "uJ" : "ucal")}";
            }
        }

        readonly struct GraphViewport
        {
            public double XMin { get; }
            public double XMax { get; }
            public double YMin { get; }
            public double YMax { get; }

            public GraphViewport(double xMin, double xMax, double yMin, double yMax)
            {
                XMin = Math.Min(xMin, xMax);
                XMax = Math.Max(xMin, xMax);
                YMin = Math.Min(yMin, yMax);
                YMax = Math.Max(yMin, yMax);
            }

            public static GraphViewport WithPadding(double xMin, double xMax, double yMin, double yMax, double xPaddingFraction, double yPaddingFraction)
            {
                var xDelta = EnsureDelta(xMin, xMax);
                var yDelta = EnsureDelta(yMin, yMax);

                return new GraphViewport(
                    xMin - xDelta * xPaddingFraction,
                    xMax + xDelta * xPaddingFraction,
                    yMin - yDelta * yPaddingFraction,
                    yMax + yDelta * yPaddingFraction);
            }

            public bool ContainsX(double value) => value >= XMin && value <= XMax;

            public bool ContainsY(double value) => value >= YMin && value <= YMax;

            static double EnsureDelta(double min, double max)
            {
                var delta = max - min;
                if (!double.IsFinite(delta) || Math.Abs(delta) < double.Epsilon) return 1;

                return delta;
            }
        }

        readonly struct GraphLayout
        {
            public Rect Plot { get; }
            public PlotTransform Transform { get; }
            public AxisTicks XTicks { get; }
            public AxisTicks YTicks { get; }

            GraphLayout(Rect plot, PlotTransform transform, AxisTicks xTicks, AxisTicks yTicks)
            {
                Plot = plot;
                Transform = transform;
                XTicks = xTicks;
                YTicks = yTicks;
            }

            public static GraphLayout Create(Rect bounds, GraphViewport view, PowerDisplay power)
            {
                var xTicks = AxisTicks.Create(view.XMin, view.XMax, Math.Max(4, Math.Min(9, (int)(bounds.Width / 135))));
                var yTicks = AxisTicks.Create(view.YMin, view.YMax, Math.Max(4, Math.Min(8, (int)(bounds.Height / 85))));

                var yLabelWidth = yTicks.Major.Count == 0
                    ? 44
                    : yTicks.Major.Max(tick => MeasureText(yTicks.Format(tick), 11).Width);

                var left = Math.Max(74, yLabelWidth + 24);
                const double top = 38;
                const double right = 22;
                const double bottom = 58;

                var plot = new Rect(
                    left,
                    top,
                    Math.Max(1, bounds.Width - left - right),
                    Math.Max(1, bounds.Height - top - bottom));

                return new GraphLayout(plot, new PlotTransform(plot, view), xTicks, yTicks);
            }

            static Size MeasureText(string text, double size)
            {
                var formatted = CreateText(text, size, FontWeight.Normal, Brushes.Black);
                return new Size(formatted.Width, formatted.Height);
            }
        }

        readonly struct PlotTransform
        {
            readonly Rect plot;
            readonly GraphViewport view;

            public PlotTransform(Rect plot, GraphViewport view)
            {
                this.plot = plot;
                this.view = view;
            }

            public Point ToScreen(double x, double y) => new Point(X(x), Y(y));

            public Point ToData(Point point)
            {
                var x = view.XMin + (point.X - plot.Left) / Math.Max(1, plot.Width) * (view.XMax - view.XMin);
                var y = view.YMax - (point.Y - plot.Top) / Math.Max(1, plot.Height) * (view.YMax - view.YMin);

                return new Point(x, y);
            }

            public double X(double value) => plot.Left + (value - view.XMin) / Math.Max(double.Epsilon, view.XMax - view.XMin) * plot.Width;

            public double Y(double value) => plot.Bottom - (value - view.YMin) / Math.Max(double.Epsilon, view.YMax - view.YMin) * plot.Height;
        }

        readonly struct AxisTicks
        {
            public IReadOnlyList<double> Major { get; }
            public IReadOnlyList<double> Minor { get; }
            readonly double step;

            AxisTicks(IReadOnlyList<double> major, IReadOnlyList<double> minor, double step)
            {
                Major = major;
                Minor = minor;
                this.step = step;
            }

            public static AxisTicks Create(double min, double max, int maxTicks)
            {
                var range = max - min;
                if (!double.IsFinite(range) || Math.Abs(range) < double.Epsilon)
                {
                    range = 1;
                    min -= 0.5;
                    max += 0.5;
                }

                var step = NiceNumber(range / Math.Max(1, maxTicks), round: true);
                if (!double.IsFinite(step) || step <= 0) step = 1;

                var first = Math.Floor(min / step) * step;
                var last = Math.Ceiling(max / step) * step;
                var major = new List<double>();
                var minor = new List<double>();
                var guard = 0;

                for (var value = first; value <= last + step * 0.5 && guard++ < 1000; value += step)
                {
                    if (value >= min - step * 0.001 && value <= max + step * 0.001)
                        major.Add(NormalizeZero(value));

                    var half = value + step / 2;
                    if (half >= min && half <= max)
                        minor.Add(NormalizeZero(half));
                }

                return new AxisTicks(major, minor, step);
            }

            public string Format(double value)
            {
                var absStep = Math.Abs(step);

                if (absStep >= 1000) return value.ToString("G4", CultureInfo.CurrentCulture);
                if (absStep >= 1) return value.ToString("0.#", CultureInfo.CurrentCulture);

                var decimals = Math.Min(6, Math.Max(1, (int)Math.Ceiling(-Math.Log10(absStep)) + 1));
                return value.ToString("0." + new string('#', decimals), CultureInfo.CurrentCulture);
            }

            static double NiceNumber(double value, bool round)
            {
                if (!double.IsFinite(value) || value <= 0) return 1;

                var exponent = Math.Floor(Math.Log10(value));
                var fraction = value / Math.Pow(10, exponent);
                double niceFraction;

                if (round)
                {
                    if (fraction < 1.5) niceFraction = 1;
                    else if (fraction < 3) niceFraction = 2;
                    else if (fraction < 7) niceFraction = 5;
                    else niceFraction = 10;
                }
                else
                {
                    if (fraction <= 1) niceFraction = 1;
                    else if (fraction <= 2) niceFraction = 2;
                    else if (fraction <= 5) niceFraction = 5;
                    else niceFraction = 10;
                }

                return niceFraction * Math.Pow(10, exponent);
            }

            static double NormalizeZero(double value) => Math.Abs(value) < 1E-12 ? 0 : value;
        }

        readonly struct PixelBucket
        {
            public int Pixel { get; }
            readonly double firstY;
            readonly double lastY;
            readonly double minY;
            readonly double maxY;
            readonly int minOrder;
            readonly int maxOrder;
            readonly int count;

            public PixelBucket(int pixel, double y)
            {
                Pixel = pixel;
                firstY = y;
                lastY = y;
                minY = y;
                maxY = y;
                minOrder = 0;
                maxOrder = 0;
                count = 1;
            }

            PixelBucket(int pixel, double firstY, double lastY, double minY, double maxY, int minOrder, int maxOrder, int count)
            {
                Pixel = pixel;
                this.firstY = firstY;
                this.lastY = lastY;
                this.minY = minY;
                this.maxY = maxY;
                this.minOrder = minOrder;
                this.maxOrder = maxOrder;
                this.count = count;
            }

            public PixelBucket Add(double y)
            {
                var nextMinY = minY;
                var nextMaxY = maxY;
                var nextMinOrder = minOrder;
                var nextMaxOrder = maxOrder;

                if (y < minY)
                {
                    nextMinY = y;
                    nextMinOrder = count;
                }

                if (y > maxY)
                {
                    nextMaxY = y;
                    nextMaxOrder = count;
                }

                return new PixelBucket(Pixel, firstY, y, nextMinY, nextMaxY, nextMinOrder, nextMaxOrder, count + 1);
            }

            public void AppendTo(List<Point> points)
            {
                var x = Pixel + 0.5;

                if (count <= 1 || Math.Abs(maxY - minY) < 0.5)
                {
                    points.Add(new Point(x, lastY));
                    return;
                }

                points.Add(new Point(x, firstY));

                if (minOrder <= maxOrder)
                {
                    points.Add(new Point(x, minY));
                    points.Add(new Point(x, maxY));
                }
                else
                {
                    points.Add(new Point(x, maxY));
                    points.Add(new Point(x, minY));
                }

                points.Add(new Point(x, lastY));
            }
        }

        readonly struct HitTarget
        {
            public static readonly HitTarget None = new HitTarget(HitKind.None, -1);

            public HitTarget(HitKind kind, int injectionIndex)
            {
                Kind = kind;
                InjectionIndex = injectionIndex;
            }

            public HitKind Kind { get; }
            public int InjectionIndex { get; }
        }

        enum HitKind
        {
            None,
            Plot,
            IntegrationRegion,
            IntegrationStart,
            IntegrationEnd
        }
    }
}
