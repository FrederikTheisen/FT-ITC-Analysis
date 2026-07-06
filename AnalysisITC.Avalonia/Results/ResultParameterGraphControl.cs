using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Results
{
    public sealed class ResultParameterGraphControl : Control
    {
        static readonly IBrush CanvasBrush = Solid("#F4F7FA");
        static readonly IBrush PlotBrush = Brushes.White;
        static readonly IBrush TextBrush = Solid("#202832");
        static readonly IBrush MutedTextBrush = Solid("#607080");
        static readonly IBrush SelectedBrush = new SolidColorBrush(Color.FromArgb(36, 37, 99, 235));
        static readonly IBrush PointBrush = Solid("#202832");
        static readonly IBrush LineBrush = Solid("#334155");
        static readonly Pen FramePen = new Pen(Solid("#AEB8C2"), 1);
        static readonly Pen GridPen = new Pen(Solid("#E3E7EC"), 1);
        static readonly Pen LinePen = new Pen(LineBrush, 1.4);
        static readonly Pen PointPen = new Pen(PointBrush, 1.1);

        AnalysisResult? result;

        public ResultParameterGraphControl()
        {
            Focusable = true;
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            DataManager.ResultSolutionSelectionDidChange += OnResultSolutionSelectionChanged;
        }

        public AnalysisResult? Result
        {
            get => result;
            set
            {
                if (ReferenceEquals(result, value)) return;
                result = value;
                InvalidateVisual();
            }
        }

        public void FitToData()
        {
            InvalidateVisual();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            DataManager.ResultSolutionSelectionDidChange -= OnResultSolutionSelectionChanged;
            base.OnDetachedFromVisualTree(e);
        }

        void OnResultSolutionSelectionChanged(object? sender, SolutionInterface? e)
        {
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            context.DrawRectangle(CanvasBrush, null, bounds);

            var plot = new Rect(56, 26, Math.Max(1, bounds.Width - 76), Math.Max(1, bounds.Height - 58));
            context.DrawRectangle(PlotBrush, FramePen, plot);

            var solutions = result?.Solution?.Solutions ?? new List<SolutionInterface>();
            var parameters = result?.Solution?.IndividualModelReportParameters ?? new List<ParameterType>();

            if (solutions.Count == 0 || parameters.Count == 0 || plot.Width < 80 || plot.Height < 80)
            {
                DrawText(context, "No analysis result selected", new Point(plot.Left + 16, plot.Top + 16), 14, FontWeight.SemiBold, MutedTextBrush);
                return;
            }

            var selected = DataManager.SelectedResultSolution;
            var selectedIndex = solutions.FindIndex(solution => ReferenceEquals(solution, selected));
            if (selectedIndex >= 0)
            {
                var x = XForSolution(plot, solutions.Count, selectedIndex);
                var left = solutions.Count == 1 ? plot.Left : Math.Max(plot.Left, x - plot.Width / Math.Max(1, solutions.Count - 1) * 0.35);
                var right = solutions.Count == 1 ? plot.Right : Math.Min(plot.Right, x + plot.Width / Math.Max(1, solutions.Count - 1) * 0.35);
                context.DrawRectangle(SelectedBrush, null, new Rect(left, plot.Top, right - left, plot.Height));
            }

            var bandHeight = plot.Height / parameters.Count;
            for (int parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
            {
                var parameter = parameters[parameterIndex];
                var band = new Rect(plot.Left, plot.Top + parameterIndex * bandHeight, plot.Width, bandHeight);
                DrawParameterBand(context, band, solutions, parameter);
            }

            for (int i = 0; i < solutions.Count; i++)
            {
                var x = XForSolution(plot, solutions.Count, i);
                context.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                DrawCenteredText(context, (i + 1).ToString(CultureInfo.CurrentCulture), new Point(x, plot.Bottom + 7), 10, MutedTextBrush);
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            var solutions = result?.Solution?.Solutions ?? new List<SolutionInterface>();
            if (solutions.Count == 0)
            {
                DataManager.ClearResultSolutionSelection();
                return;
            }

            var bounds = Bounds;
            var plot = new Rect(56, 26, Math.Max(1, bounds.Width - 76), Math.Max(1, bounds.Height - 58));
            var point = e.GetPosition(this);

            if (!plot.Contains(point))
            {
                DataManager.ClearResultSolutionSelection();
                e.Handled = true;
                return;
            }

            var index = SolutionIndexForX(plot, solutions.Count, point.X);
            if (index >= 0 && index < solutions.Count)
                DataManager.SelectResultSolution(solutions[index]);

            e.Handled = true;
        }

        void DrawParameterBand(DrawingContext context, Rect band, IReadOnlyList<SolutionInterface> solutions, ParameterType parameter)
        {
            var values = solutions
                .Select(solution => ParameterValue(solution, parameter))
                .ToList();
            var finite = values.Where(IsFinite).ToList();
            if (finite.Count == 0) return;

            var min = finite.Min();
            var max = finite.Max();
            var delta = Math.Abs(max - min);
            if (delta < double.Epsilon) delta = Math.Max(1, Math.Abs(max));
            min -= delta * 0.12;
            max += delta * 0.12;

            var centerY = band.Top + band.Height * 0.5;
            context.DrawLine(GridPen, new Point(band.Left, centerY), new Point(band.Right, centerY));
            context.DrawLine(GridPen, new Point(band.Left, band.Bottom), new Point(band.Right, band.Bottom));

            var label = ParameterLabel(parameter);
            DrawText(context, label, new Point(8, band.Top + 6), 11, FontWeight.SemiBold, TextBrush);

            var points = new List<Point>();
            for (int i = 0; i < solutions.Count; i++)
            {
                if (!IsFinite(values[i])) continue;

                var x = XForSolution(new Rect(band.Left, band.Top, band.Width, band.Height), solutions.Count, i);
                var y = band.Bottom - (values[i] - min) / (max - min) * band.Height;
                points.Add(new Point(x, y));
            }

            DrawPolyline(context, points, LinePen);

            foreach (var point in points)
            {
                var rect = new Rect(point.X - 3.5, point.Y - 3.5, 7, 7);
                context.DrawEllipse(PointBrush, PointPen, rect);
            }

            DrawRightAlignedText(context, FormatAxisValue(parameter, max), new Point(band.Right - 4, band.Top + 3), 9.5, MutedTextBrush);
            DrawRightAlignedText(context, FormatAxisValue(parameter, min), new Point(band.Right - 4, band.Bottom - 14), 9.5, MutedTextBrush);
        }

        double ParameterValue(SolutionInterface solution, ParameterType parameter)
        {
            if (solution?.ReportParameters == null || !solution.ReportParameters.TryGetValue(parameter, out var value))
                return double.NaN;

            return parameter.GetProperties().ParentType switch
            {
                ParameterType.Affinity1 => value.Value * ResolveAffinityUnit().GetMod(),
                ParameterType.Enthalpy1 => value.Value * Energy.ScaleFactor(AppSettings.EnergyUnit),
                ParameterType.Gibbs1 => value.Value * Energy.ScaleFactor(AppSettings.EnergyUnit),
                ParameterType.EntropyContribution1 => value.Value * Energy.ScaleFactor(AppSettings.EnergyUnit),
                _ => value.Value
            };
        }

        string FormatAxisValue(ParameterType parameter, double value)
        {
            if (!IsFinite(value)) return "";

            return value.ToString(Math.Abs(value) < 10 ? "G3" : "G4", CultureInfo.CurrentCulture);
        }

        ConcentrationUnit ResolveAffinityUnit()
        {
            try
            {
                return result == null ? ConcentrationUnit.µM : result.AppropriateAffinityUnit;
            }
            catch
            {
                return AppSettings.DefaultConcentrationUnit;
            }
        }

        string ParameterLabel(ParameterType parameter)
        {
            var options = result?.Solution?.Solutions?.FirstOrDefault()?.ModelOptions ?? new Dictionary<AttributeKey, ExperimentAttribute>();
            var multiple = result?.Solution?.Solutions?.FirstOrDefault()?.ParametersConformingToKey(parameter).Count > 1;
            return ParameterTypeAttribute.TableHeaderTitle(options, parameter, multiple == true);
        }

        static double XForSolution(Rect plot, int count, int index)
        {
            if (count <= 1) return plot.Left + plot.Width * 0.5;

            return plot.Left + plot.Width * index / (count - 1);
        }

        static int SolutionIndexForX(Rect plot, int count, double x)
        {
            if (count <= 1) return 0;

            var position = (x - plot.Left) / Math.Max(1, plot.Width);
            return (int)Math.Round(position * (count - 1));
        }

        static void DrawPolyline(DrawingContext context, IReadOnlyList<Point> points, Pen pen)
        {
            if (points.Count < 2) return;

            var geometry = new StreamGeometry();
            using (var stream = geometry.Open())
            {
                stream.BeginFigure(points[0], false);
                for (int i = 1; i < points.Count; i++) stream.LineTo(points[i]);
            }

            context.DrawGeometry(null, pen, geometry);
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));

        static FormattedText CreateText(string text, double size, FontWeight weight, IBrush brush)
        {
            return new FormattedText(
                text ?? "",
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
    }
}
