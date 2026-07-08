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

namespace AnalysisITC.Avalonia.Tools
{
    public sealed class BufferSubtractionGraphControl : Control
    {
        readonly List<(InjectionData Injection, Point Point)> bufferPointPositions = new();

        ExperimentData? reference;
        IReadOnlyList<ExperimentData> targets = Array.Empty<ExperimentData>();
        BufferSubtractionModel? model;
        bool focusYAxisOnBufferData;

        public event EventHandler? BufferPointIncludeChanged;

        public BufferSubtractionGraphControl()
        {
            ClipToBounds = true;
            PointerPressed += OnPointerPressed;
        }

        public void SetData(
            ExperimentData? referenceExperiment,
            IReadOnlyList<ExperimentData> targetExperiments,
            BufferSubtractionModel? subtractionModel,
            bool focusYAxisOnBuffer)
        {
            reference = referenceExperiment;
            targets = targetExperiments ?? Array.Empty<ExperimentData>();
            model = subtractionModel;
            focusYAxisOnBufferData = focusYAxisOnBuffer;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            bufferPointPositions.Clear();

            var theme = AvaloniaGraphSettings.Current;
            var bounds = Bounds;
            context.FillRectangle(theme.CanvasBrush, bounds);

            if (reference == null)
            {
                DrawEmpty(context, "Select a buffer/reference experiment.");
                return;
            }

            var points = BuildPoints();
            if (points.Count == 0)
            {
                DrawEmpty(context, "No integrated buffer points are available.");
                return;
            }

            var plot = PlotRect(bounds);
            context.FillRectangle(theme.PlotBrush, plot);
            context.DrawRectangle(null, theme.FramePen, plot);

            var range = BuildDataRange(points, plot);
            DrawAxes(context, plot, range);
            DrawTargetPoints(context, plot, range);
            DrawModelLine(context, plot, range);
            DrawReferencePoints(context, plot, range);
        }

        List<GraphPoint> BuildPoints()
        {
            var points = new List<GraphPoint>();
            foreach (var injection in reference?.Injections ?? Enumerable.Empty<InjectionData>())
            {
                if (!injection.IsIntegrated) continue;
                points.Add(new GraphPoint(injection.ID + 1, ToDisplayHeat(injection.RawPeakArea.Value), true));
            }

            if (!focusYAxisOnBufferData)
            {
                foreach (var target in targets)
                {
                    foreach (var injection in target.Injections)
                    {
                        if (!injection.IsIntegrated) continue;
                        points.Add(new GraphPoint(injection.ID + 1, ToDisplayHeat(injection.RawPeakArea.Value), false));
                    }
                }
            }

            if (model?.CanDrawLine == true)
            {
                var minX = points.Count == 0 ? 1 : points.Min(point => point.X);
                var maxX = points.Count == 0 ? 2 : points.Max(point => point.X);
                points.AddRange(model.GetLinePoints(minX, maxX)
                    .Select(point => new GraphPoint(point.InjectionNumber, ToDisplayHeat(point.Heat), true)));
            }

            return points;
        }

        DataRange BuildDataRange(List<GraphPoint> points, Rect plot)
        {
            var minX = points.Min(point => point.X);
            var maxX = points.Max(point => point.X);
            var minY = points.Min(point => point.Y);
            var maxY = points.Max(point => point.Y);

            if (Math.Abs(maxX - minX) < 1e-9)
            {
                minX -= 1;
                maxX += 1;
            }

            if (Math.Abs(maxY - minY) < 1e-9)
            {
                minY -= 1;
                maxY += 1;
            }

            var xPad = Math.Max(0.5, 0.04 * (maxX - minX));
            var yPad = Math.Max(1e-9, 0.12 * (maxY - minY));
            return new DataRange(minX - xPad, maxX + xPad, minY - yPad, maxY + yPad);
        }

        void DrawAxes(DrawingContext context, Rect plot, DataRange range)
        {
            var theme = AvaloniaGraphSettings.Current;
            for (var i = 0; i <= 4; i++)
            {
                var fraction = i / 4.0;
                var x = plot.Left + fraction * plot.Width;
                var y = plot.Bottom - fraction * plot.Height;
                context.DrawLine(theme.MajorGridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                context.DrawLine(theme.MajorGridPen, new Point(plot.Left, y), new Point(plot.Right, y));

                DrawCenteredText(context, Format(range.MinX + fraction * (range.MaxX - range.MinX)), new Point(x, plot.Bottom + 8), 11, theme.TextBrush);
                DrawRightText(context, Format(range.MinY + fraction * (range.MaxY - range.MinY)), new Point(plot.Left - 7, y - 7), 11, theme.TextBrush);
            }

            DrawCenteredText(context, "Injection", new Point(plot.Left + plot.Width / 2, plot.Bottom + 32), 12, theme.TextBrush);
            DrawText(context, $"Heat ({AppSettings.EnergyUnit.GetUnit()})", new Point(plot.Left, plot.Top - 26), 12, theme.TextBrush);
        }

        void DrawTargetPoints(DrawingContext context, Rect plot, DataRange range)
        {
            var theme = AvaloniaGraphSettings.Current;
            foreach (var target in targets)
            {
                foreach (var injection in target.Injections.Where(injection => injection.IsIntegrated))
                {
                    var point = ToPlot(injection.ID + 1, ToDisplayHeat(injection.RawPeakArea.Value), plot, range);
                    context.DrawEllipse(theme.MutedRegionBrush, null, point, 3.5, 3.5);
                }
            }
        }

        void DrawModelLine(DrawingContext context, Rect plot, DataRange range)
        {
            if (model?.CanDrawLine != true) return;

            var linePoints = model.GetLinePoints(range.MinX, range.MaxX)
                .Select(point => ToPlot(point.InjectionNumber, ToDisplayHeat(point.Heat), plot, range))
                .ToList();
            if (linePoints.Count < 2) return;

            var geometry = new StreamGeometry();
            using (var stream = geometry.Open())
            {
                stream.BeginFigure(linePoints[0], false);
                for (var i = 1; i < linePoints.Count; i++)
                    stream.LineTo(linePoints[i]);
            }

            using (context.PushClip(plot))
                context.DrawGeometry(null, AvaloniaGraphSettings.Current.FitPen, geometry);
        }

        void DrawReferencePoints(DrawingContext context, Rect plot, DataRange range)
        {
            var theme = AvaloniaGraphSettings.Current;
            foreach (var injection in reference?.Injections.Where(injection => injection.IsIntegrated) ?? Enumerable.Empty<InjectionData>())
            {
                var point = ToPlot(injection.ID + 1, ToDisplayHeat(injection.RawPeakArea.Value), plot, range);
                bufferPointPositions.Add((injection, point));

                if (injection.Include)
                {
                    context.DrawEllipse(theme.PointBrush, theme.PointPen, point, 5, 5);
                }
                else
                {
                    context.DrawEllipse(null, theme.ExcludedPen, point, 5, 5);
                }
            }
        }

        void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var position = e.GetPosition(this);
            var nearest = bufferPointPositions
                .Select(item => new { item.Injection, Distance = Distance(position, item.Point) })
                .Where(item => item.Distance <= 9)
                .OrderBy(item => item.Distance)
                .FirstOrDefault();

            if (nearest == null) return;

            nearest.Injection.Include = !nearest.Injection.Include;
            BufferPointIncludeChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
        }

        static Rect PlotRect(Rect bounds)
        {
            return new Rect(
                bounds.Left + 58,
                bounds.Top + 42,
                Math.Max(20, bounds.Width - 82),
                Math.Max(20, bounds.Height - 100));
        }

        static Point ToPlot(double x, double y, Rect plot, DataRange range)
        {
            var px = plot.Left + (x - range.MinX) / (range.MaxX - range.MinX) * plot.Width;
            var py = plot.Bottom - (y - range.MinY) / (range.MaxY - range.MinY) * plot.Height;
            return new Point(px, py);
        }

        static double ToDisplayHeat(double joule)
        {
            return Energy.ConvertFromJoule(joule, AppSettings.EnergyUnit);
        }

        static double Distance(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        static string Format(double value) => value.ToString("G3", CultureInfo.CurrentCulture);

        static void DrawEmpty(DrawingContext context, string message)
        {
            var theme = AvaloniaGraphSettings.Current;
            DrawText(context, message, new Point(AvaloniaGraphSettings.EmptyStateXOffset, AvaloniaGraphSettings.EmptyStateBodyYOffset), 13, theme.MutedTextBrush);
        }

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

        static void DrawText(DrawingContext context, string text, Point point, double size, IBrush brush)
        {
            context.DrawText(CreateText(text, size, FontWeight.Normal, brush), point);
        }

        static void DrawCenteredText(DrawingContext context, string text, Point point, double size, IBrush brush)
        {
            var formatted = CreateText(text, size, FontWeight.Normal, brush);
            context.DrawText(formatted, new Point(point.X - formatted.Width / 2, point.Y));
        }

        static void DrawRightText(DrawingContext context, string text, Point point, double size, IBrush brush)
        {
            var formatted = CreateText(text, size, FontWeight.Normal, brush);
            context.DrawText(formatted, new Point(point.X - formatted.Width, point.Y));
        }

        readonly struct GraphPoint
        {
            public GraphPoint(double x, double y, bool isReference)
            {
                X = x;
                Y = y;
                IsReference = isReference;
            }

            public double X { get; }
            public double Y { get; }
            public bool IsReference { get; }
        }

        readonly struct DataRange
        {
            public DataRange(double minX, double maxX, double minY, double maxY)
            {
                MinX = minX;
                MaxX = maxX;
                MinY = minY;
                MaxY = maxY;
            }

            public double MinX { get; }
            public double MaxX { get; }
            public double MinY { get; }
            public double MaxY { get; }
        }
    }
}
