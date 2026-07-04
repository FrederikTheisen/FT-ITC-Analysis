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
using AnalysisITC.Core.Units;

namespace AnalysisITC.Avalonia
{
    public sealed class ThermogramGraphControl : Control
    {
        public static readonly StyledProperty<ExperimentData?> ExperimentProperty =
            AvaloniaProperty.Register<ThermogramGraphControl, ExperimentData?>(nameof(Experiment));

        static readonly IBrush CanvasBrush = Solid("#F8FAFC");
        static readonly IBrush PlotBrush = Brushes.White;
        static readonly IBrush TextBrush = Solid("#1F2933");
        static readonly IBrush MutedTextBrush = Solid("#64717D");
        static readonly IBrush DataBrush = Solid("#145A8D");
        static readonly IBrush InjectionBrush = Solid("#A46A2A");
        static readonly IBrush HoverBrush = Solid("#2E3944");
        static readonly IBrush HoverBackgroundBrush = Solid("#FFFFFF");

        static readonly Pen FramePen = new Pen(Solid("#AEB8C2"), 1);
        static readonly Pen AxisPen = new Pen(Solid("#2F3B46"), 1);
        static readonly Pen MajorGridPen = new Pen(Solid("#D8DEE6"), 1);
        static readonly Pen MinorGridPen = new Pen(Solid("#EEF2F6"), 1);
        static readonly Pen DataPen = new Pen(DataBrush, 1.25);
        static readonly Pen InjectionPen = new Pen(InjectionBrush, 0.8);
        static readonly Pen HoverPen = new Pen(HoverBrush, 1);

        GraphViewport view;
        bool hasView;
        bool isPanning;
        Point lastPanPoint;
        Point? hoverPoint;
        DataPoint? hoverData;

        public ThermogramGraphControl()
        {
            Focusable = true;
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Cross);
        }

        public ExperimentData? Experiment
        {
            get => GetValue(ExperimentProperty);
            set => SetValue(ExperimentProperty, value);
        }

        PowerDisplay Power => PowerDisplay.Current;

        public void FitToData()
        {
            var points = Experiment?.DataPoints;
            if (points == null || points.Count < 2)
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

            InvalidateVisual();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == ExperimentProperty) FitToData();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            context.DrawRectangle(CanvasBrush, null, bounds);

            if (bounds.Width < 120 || bounds.Height < 120)
            {
                return;
            }

            var graph = GraphLayout.Create(context, bounds, view, Power);

            context.DrawRectangle(PlotBrush, FramePen, graph.Plot);

            if (Experiment?.HasThermogram != true || !hasView)
            {
                DrawEmptyState(context, graph.Plot);
                return;
            }

            DrawGrid(context, graph);
            DrawSeries(context, graph);
            DrawInjections(context, graph);
            DrawAxes(context, graph);
            DrawHover(context, graph);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);

            if (!hasView) return;

            var graph = GraphLayout.Create(null, Bounds, view, Power);
            var point = e.GetPosition(this);
            if (!graph.Plot.Contains(point)) return;

            var factor = e.Delta.Y > 0 ? 0.82 : 1.22;
            var anchor = graph.Transform.ToData(point);

            view = view.Zoom(anchor.X, anchor.Y, factor);
            UpdateHover(point, graph);
            InvalidateVisual();

            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (!hasView) return;

            var graph = GraphLayout.Create(null, Bounds, view, Power);
            var point = e.GetPosition(this);
            if (!graph.Plot.Contains(point)) return;

            Focus();
            isPanning = true;
            lastPanPoint = point;
            e.Pointer.Capture(this);
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

            var graph = GraphLayout.Create(null, Bounds, view, Power);
            var point = e.GetPosition(this);

            if (isPanning)
            {
                view = view.Pan(lastPanPoint, point, graph.Transform);
                lastPanPoint = point;
                hoverPoint = null;
                hoverData = null;
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            UpdateHover(point, graph);
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);

            hoverPoint = null;
            hoverData = null;
            InvalidateVisual();
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
            hoverData = FindNearestDataPoint(graph.Transform.ToData(point).X);
        }

        void DrawEmptyState(DrawingContext context, Rect plot)
        {
            DrawText(context, "No thermogram selected", new Point(plot.Left + 18, plot.Top + 18), 14, FontWeight.SemiBold, MutedTextBrush);
            DrawText(context, "Open an ITC file and select an experiment to render raw differential power.", new Point(plot.Left + 18, plot.Top + 43), 12, FontWeight.Normal, MutedTextBrush);
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

        void DrawSeries(DrawingContext context, GraphLayout graph)
        {
            var displayPoints = BuildDisplayPoints(graph);
            if (displayPoints.Count == 0) return;

            var geometry = new StreamGeometry();
            using (var stream = geometry.Open())
            {
                stream.BeginFigure(displayPoints[0], false);
                for (int i = 1; i < displayPoints.Count; i++) stream.LineTo(displayPoints[i]);
            }

            using (context.PushClip(graph.Plot))
            {
                context.DrawGeometry(null, DataPen, geometry);
            }
        }

        List<Point> BuildDisplayPoints(GraphLayout graph)
        {
            var data = Experiment?.DataPoints;
            if (data == null || data.Count == 0) return new List<Point>();

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

        void DrawInjections(DrawingContext context, GraphLayout graph)
        {
            var injections = Experiment?.Injections;
            if (injections == null || injections.Count == 0) return;

            using (context.PushClip(graph.Plot))
            {
                foreach (var injection in injections)
                {
                    if (!view.ContainsX(injection.Time)) continue;

                    var x = Crisp(graph.Transform.X(injection.Time));
                    context.DrawLine(InjectionPen, new Point(x, graph.Plot.Top), new Point(x, graph.Plot.Bottom));
                    context.DrawRectangle(InjectionBrush, null, new Rect(x - 2, graph.Plot.Top, 4, 8));
                }
            }

            if (injections.Count > 80) return;

            var lastLabelX = double.NegativeInfinity;
            foreach (var injection in injections)
            {
                if (!view.ContainsX(injection.Time)) continue;

                var x = graph.Transform.X(injection.Time);
                if (x - lastLabelX < 22) continue;

                DrawCenteredText(context, injection.ID.ToString(CultureInfo.CurrentCulture), new Point(x, graph.Plot.Top + 10), 9, InjectionBrush);
                lastLabelX = x;
            }
        }

        void DrawHover(DrawingContext context, GraphLayout graph)
        {
            if (!hoverPoint.HasValue || !hoverData.HasValue) return;

            var data = hoverData.Value;
            if (!view.ContainsX(data.Time)) return;

            var screen = graph.Transform.ToScreen(data.Time, Power.Convert(data.Power));
            var x = Crisp(screen.X);

            using (context.PushClip(graph.Plot))
            {
                context.DrawLine(HoverPen, new Point(x, graph.Plot.Top), new Point(x, graph.Plot.Bottom));
                context.DrawEllipse(Brushes.White, HoverPen, screen, 4, 4);
            }

            var lines = new[]
            {
                $"Time: {data.Time:F1} s",
                $"Power: {Power.Format(Power.Convert(data.Power))}",
                $"Temp: {data.Temperature:F3} C"
            };

            DrawInfoBox(context, lines, graph.Plot, screen);
        }

        void DrawInfoBox(DrawingContext context, string[] lines, Rect plot, Point anchor)
        {
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

        DataPoint? FindNearestDataPoint(double time)
        {
            var data = Experiment?.DataPoints;
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
        }

        readonly struct GraphViewport
        {
            public double XMin { get; }
            public double XMax { get; }
            public double YMin { get; }
            public double YMax { get; }

            GraphViewport(double xMin, double xMax, double yMin, double yMax)
            {
                XMin = xMin;
                XMax = xMax;
                YMin = yMin;
                YMax = yMax;
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

            public GraphViewport Zoom(double xAnchor, double yAnchor, double factor)
            {
                factor = Math.Max(0.05, Math.Min(20, factor));

                return new GraphViewport(
                    xAnchor - (xAnchor - XMin) * factor,
                    xAnchor + (XMax - xAnchor) * factor,
                    yAnchor - (yAnchor - YMin) * factor,
                    yAnchor + (YMax - yAnchor) * factor);
            }

            public GraphViewport Pan(Point previous, Point current, PlotTransform transform)
            {
                var previousData = transform.ToData(previous);
                var currentData = transform.ToData(current);
                var dx = previousData.X - currentData.X;
                var dy = previousData.Y - currentData.Y;

                return new GraphViewport(XMin + dx, XMax + dx, YMin + dy, YMax + dy);
            }

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

            public static GraphLayout Create(DrawingContext? context, Rect bounds, GraphViewport view, PowerDisplay power)
            {
                var xTicks = AxisTicks.Create(view.XMin, view.XMax, Math.Max(4, Math.Min(9, (int)(bounds.Width / 135))));
                var yTicks = AxisTicks.Create(view.YMin, view.YMax, Math.Max(4, Math.Min(8, (int)(bounds.Height / 85))));

                var yLabelWidth = yTicks.Major.Count == 0
                    ? 44
                    : yTicks.Major.Max(tick => MeasureText(yTicks.Format(tick), 11).Width);

                var left = Math.Max(72, yLabelWidth + 22);
                const double top = 38;
                const double right = 20;
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
                    {
                        major.Add(NormalizeZero(value));
                    }

                    var half = value + step / 2;
                    if (half >= min && half <= max)
                    {
                        minor.Add(NormalizeZero(half));
                    }
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
    }
}
