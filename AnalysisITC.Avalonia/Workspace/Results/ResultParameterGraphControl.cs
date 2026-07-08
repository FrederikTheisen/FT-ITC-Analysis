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
        static AvaloniaGraphTheme GraphTheme => AvaloniaGraphSettings.Current;

        static readonly ParameterType[] ThermodynamicParameters =
        {
            ParameterType.Enthalpy1,
            ParameterType.EntropyContribution1,
            ParameterType.Gibbs1,
            ParameterType.Enthalpy2,
            ParameterType.EntropyContribution2,
            ParameterType.Gibbs2
        };

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

            var solutions = result?.Solution?.Solutions ?? new List<SolutionInterface>();
            var parameters = AvailableThermodynamicParameters(solutions);
            var yRange = BuildValueRange(solutions, parameters);
            var ticks = BuildTicks(yRange.Minimum, yRange.Maximum);

            var bounds = Bounds;
            context.DrawRectangle(GraphTheme.CanvasBrush, null, bounds);

            var yLabelWidth = ticks.Count == 0
                    ? AvaloniaGraphSettings.YLabelFallbackWidth
                    : ticks.Max(tick => MeasureText(FormatAxisValue(tick), AvaloniaGraphSettings.TickLabelFontSize).Width);

            var left = Math.Max(AvaloniaGraphSettings.GraphMarginLeftMinimum, yLabelWidth + AvaloniaGraphSettings.GraphMarginLeftTickBuffer);
            double top = AvaloniaGraphSettings.GraphMarginTop;
            double right = AvaloniaGraphSettings.GraphMarginRight;
            double bottom = AvaloniaGraphSettings.GraphMarginBottom;

            var plot = new Rect(
                left,
                top,
                Math.Max(1, bounds.Width - left - right),
                Math.Max(1, bounds.Height - top - bottom + 20));

            context.DrawRectangle(GraphTheme.PlotBrush, GraphTheme.FramePen, plot);

            if (solutions.Count == 0 || parameters.Count == 0 || plot.Width < 80 || plot.Height < 80)
            {
                DrawText(
                    context,
                    "No thermodynamic result parameters",
                    new Point(plot.Left + AvaloniaGraphSettings.EmptyStateXOffset, plot.Top + AvaloniaGraphSettings.EmptyStateTitleYOffset),
                    AvaloniaGraphSettings.EmptyTitleFontSize,
                    FontWeight.SemiBold,
                    GraphTheme.MutedTextBrush);
                return;
            }

            var selected = DataManager.SelectedResultSolution;

            foreach (var tick in ticks)
            {
                var y = YForValue(plot, yRange.Minimum, yRange.Maximum, tick);
                context.DrawLine(GraphTheme.MajorGridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                DrawRightAlignedText(
                    context,
                    FormatAxisValue(tick),
                    new Point(plot.Left - AvaloniaGraphSettings.TickLabelOffset, y - AvaloniaGraphSettings.YTickLabelYOffset),
                    AvaloniaGraphSettings.TickLabelFontSize,
                    GraphTheme.MutedTextBrush);
            }

            var zeroY = YForValue(plot, yRange.Minimum, yRange.Maximum, 0);
            context.DrawLine(GraphTheme.ZeroPen, new Point(plot.Left, zeroY), new Point(plot.Right, zeroY));

            DrawText(context, $"Energy ({AppSettings.EnergyUnit.GetUnit()}/mol)", new Point(plot.Left, plot.Top - AvaloniaGraphSettings.AxisTitleOffset), AvaloniaGraphSettings.AxisTitleFontSize, FontWeight.SemiBold, GraphTheme.TextBrush);

            var categoryWidth = plot.Width / parameters.Count;
            for (int parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
            {
                var parameter = parameters[parameterIndex];
                var categoryLeft = plot.Left + parameterIndex * categoryWidth;
                var categoryCenter = categoryLeft + categoryWidth * 0.5;

                if (parameterIndex > 0)
                    context.DrawLine(GraphTheme.MinorGridPen, new Point(categoryLeft, plot.Top), new Point(categoryLeft, plot.Bottom));

                DrawParameterBars(context, plot, yRange, solutions, selected, parameter, categoryLeft, categoryWidth);
                DrawCenteredText(context, ParameterLabel(parameter), new Point(categoryCenter, plot.Bottom + AvaloniaGraphSettings.TickLabelOffset), AvaloniaGraphSettings.TickLabelFontSize, GraphTheme.TextBrush);
            }
        }

        static Size MeasureText(string text, double size)
        {
            var formatted = CreateText(text, size, FontWeight.Normal, GraphTheme.TextBrush);
            return new Size(formatted.Width, formatted.Height);
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
            var parameters = AvailableThermodynamicParameters(solutions);
            if (parameters.Count == 0)
            {
                DataManager.ClearResultSolutionSelection();
                return;
            }

            var yRange = BuildValueRange(solutions, parameters);
            var ticks = BuildTicks(yRange.Minimum, yRange.Maximum);

            var yLabelWidth = ticks.Count == 0
                    ? AvaloniaGraphSettings.YLabelFallbackWidth
                    : ticks.Max(tick => MeasureText(FormatAxisValue(tick), AvaloniaGraphSettings.TickLabelFontSize).Width);

            var left = Math.Max(AvaloniaGraphSettings.GraphMarginLeftMinimum, yLabelWidth + AvaloniaGraphSettings.GraphMarginLeftTickBuffer);
            double top = AvaloniaGraphSettings.GraphMarginTop;
            double right = AvaloniaGraphSettings.GraphMarginRight;
            double bottom = AvaloniaGraphSettings.GraphMarginBottom;

            var plot = new Rect(
                left,
                top,
                Math.Max(1, bounds.Width - left - right),
                Math.Max(1, bounds.Height - top - bottom + 20));

            var point = e.GetPosition(this);

            if (!plot.Contains(point))
            {
                DataManager.ClearResultSolutionSelection();
                e.Handled = true;
                return;
            }

            var found = false;
            var categoryWidth = plot.Width / Math.Max(1, parameters.Count);
            for (int parameterIndex = 0; parameterIndex < parameters.Count && !found; parameterIndex++)
            {
                var parameter = parameters[parameterIndex];
                var categoryLeft = plot.Left + parameterIndex * categoryWidth;
                var binWidth = categoryWidth * 0.8;
                var perSolutionWidth = binWidth / Math.Max(1, solutions.Count);
                var barWidth = Math.Max(3, perSolutionWidth - 2);

                for (int i = 0; i < solutions.Count; i++)
                {
                    var value = ParameterValue(solutions[i], parameter);
                    if (!value.HasValue) continue;

                    var x = categoryLeft + categoryWidth * 0.1 + i * perSolutionWidth + (perSolutionWidth - barWidth) * 0.5;
                    var y = YForValue(plot, yRange.Minimum, yRange.Maximum, value.Value);
                    var zeroY = YForValue(plot, yRange.Minimum, yRange.Maximum, 0);
                    var topRect = Math.Min(y, zeroY);
                    var height = Math.Abs(zeroY - y);
                    var rect = new Rect(x, topRect, barWidth, Math.Max(1, height));

                    if (rect.Contains(point))
                    {
                        DataManager.SelectResultSolution(solutions[i]);
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
                DataManager.ClearResultSolutionSelection();

            e.Handled = true;
        }

        void DrawParameterBars(
            DrawingContext context,
            Rect plot,
            ValueRange yRange,
            IReadOnlyList<SolutionInterface> solutions,
            SolutionInterface? selected,
            ParameterType parameter,
            double categoryLeft,
            double categoryWidth)
        {
            var binWidth = categoryWidth * 0.8;
            var barWidth = Math.Max(3, binWidth / Math.Max(1, solutions.Count) - 2);
            var zeroY = YForValue(plot, yRange.Minimum, yRange.Maximum, 0);

            for (int i = 0; i < solutions.Count; i++)
            {
                var value = ParameterValue(solutions[i], parameter);
                if (!value.HasValue) continue;

                var x = categoryLeft + categoryWidth * 0.1 + i * (binWidth / Math.Max(1, solutions.Count)) + (binWidth / Math.Max(1, solutions.Count) - barWidth) * 0.5;
                var y = YForValue(plot, yRange.Minimum, yRange.Maximum, value.Value);
                var top = Math.Min(y, zeroY);
                var height = Math.Abs(zeroY - y);
                var rect = new Rect(x, top, barWidth, Math.Max(1, height));
                var selectedBar = ReferenceEquals(solutions[i], selected);
                var pen = !selectedBar ? GraphTheme.FitPen : GraphTheme.DataPen;
                var brush = !selectedBar ? GraphTheme.FitBrush : GraphTheme.DataBrush;

                context.DrawRectangle(brush, pen, rect);

                if (value.Lower.HasValue && value.Upper.HasValue)
                {
                    var lowerY = YForValue(plot, yRange.Minimum, yRange.Maximum, value.Lower.Value);
                    var upperY = YForValue(plot, yRange.Minimum, yRange.Maximum, value.Upper.Value);
                    var centerX = rect.Left + rect.Width * 0.5;
                    var cap = Math.Max(3, rect.Width * 0.35);
                    context.DrawLine(GraphTheme.PointPen, new Point(centerX, upperY), new Point(centerX, lowerY));
                    context.DrawLine(GraphTheme.PointPen, new Point(centerX - cap, upperY), new Point(centerX + cap, upperY));
                    context.DrawLine(GraphTheme.PointPen, new Point(centerX - cap, lowerY), new Point(centerX + cap, lowerY));
                }
            }
        }

        ThermodynamicValue ParameterValue(SolutionInterface solution, ParameterType parameter)
        {
            if (solution?.ReportParameters == null || !solution.ReportParameters.TryGetValue(parameter, out var value))
                return ThermodynamicValue.None;

            var scale = Energy.ScaleFactor(AppSettings.EnergyUnit);
            return new ThermodynamicValue(
                value.Value * scale,
                value.Lower * scale,
                value.Upper * scale);
        }

        static ValueRange BuildValueRange(IReadOnlyList<SolutionInterface> solutions, IReadOnlyList<ParameterType> parameters)
        {
            var values = new List<double> { 0 };
            foreach (var solution in solutions)
            {
                foreach (var parameter in parameters)
                {
                    if (solution?.ReportParameters == null || !solution.ReportParameters.TryGetValue(parameter, out var value))
                        continue;

                    var scale = Energy.ScaleFactor(AppSettings.EnergyUnit);
                    values.Add(value.Value * scale);
                    values.Add(value.Lower * scale);
                    values.Add(value.Upper * scale);
                }
            }

            var finite = values.Where(IsFinite).ToList();
            var min = finite.Min();
            var max = finite.Max();
            var delta = max - min;
            if (Math.Abs(delta) < double.Epsilon) delta = Math.Max(1, Math.Abs(max));

            return new ValueRange(min - delta * 0.1, max + delta * 0.1);
        }

        string ParameterLabel(ParameterType parameter)
        {
            var options = result?.Solution?.Solutions?.FirstOrDefault()?.ModelOptions ?? new Dictionary<AttributeKey, ExperimentAttribute>();
            var multiple = result?.Solution?.Solutions?.FirstOrDefault()?.ParametersConformingToKey(parameter).Count > 1;
            return ParameterTypeAttribute.TableHeaderTitle(options, parameter, multiple == true);
        }

        static List<ParameterType> AvailableThermodynamicParameters(IReadOnlyList<SolutionInterface> solutions)
        {
            return ThermodynamicParameters
                .Where(parameter => solutions.Any(solution => solution.ReportParameters != null && solution.ReportParameters.ContainsKey(parameter)))
                .ToList();
        }

        static List<double> BuildTicks(double minimum, double maximum)
        {
            var ticks = new List<double>();
            var span = maximum - minimum;
            if (!IsFinite(span) || span <= 0) return ticks;

            var step = NiceNumber(span / 5, round: true);
            var start = Math.Ceiling(minimum / step) * step;
            for (var value = start; value <= maximum + step * 0.001 && ticks.Count < 20; value += step)
            {
                ticks.Add(Math.Abs(value) < 1E-12 ? 0 : value);
            }

            return ticks;
        }

        static double NiceNumber(double value, bool round)
        {
            if (!IsFinite(value) || value <= 0) return 1;

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

        static double YForValue(Rect plot, double minimum, double maximum, double value)
        {
            if (Math.Abs(maximum - minimum) < double.Epsilon) return plot.Bottom;

            return plot.Bottom - (value - minimum) / (maximum - minimum) * plot.Height;
        }

        static int SolutionIndexForPoint(Rect plot, int parameterCount, int solutionCount, double x)
        {
            if (solutionCount <= 0 || parameterCount <= 0) return -1;

            var categoryWidth = plot.Width / parameterCount;
            var categoryIndex = (int)Math.Floor((x - plot.Left) / Math.Max(1, categoryWidth));
            if (categoryIndex < 0 || categoryIndex >= parameterCount) return -1;

            var offset = x - (plot.Left + categoryIndex * categoryWidth + categoryWidth * 0.1);
            var binWidth = categoryWidth * 0.8;
            if (offset < 0 || offset > binWidth) return -1;

            var index = (int)Math.Floor(offset / Math.Max(1, binWidth / solutionCount));
            return Math.Max(0, Math.Min(solutionCount - 1, index));
        }

        void DrawSolutionLegend(DrawingContext context, Rect plot, IReadOnlyList<SolutionInterface> solutions, SolutionInterface? selected)
        {
            var x = plot.Left;
            var y = plot.Bottom + 32;
            for (int i = 0; i < solutions.Count; i++)
            {
                var rect = new Rect(x, y + 2, 10, 10);
                context.DrawRectangle(GraphTheme.DataBrush, ReferenceEquals(solutions[i], selected) ? GraphTheme.FitPen : GraphTheme.DataPen, rect);
                DrawText(context, (i + 1).ToString(CultureInfo.CurrentCulture), new Point(x + 14, y - 1), AvaloniaGraphSettings.TickLabelFontSize, FontWeight.Normal, GraphTheme.MutedTextBrush);
                x += 36;
                if (x > plot.Right - 28) break;
            }
        }

        static string FormatAxisValue(double value)
        {
            if (!IsFinite(value)) return "";

            return value.ToString(Math.Abs(value) < 10 ? "G3" : "G4", CultureInfo.CurrentCulture);
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

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

        readonly struct ValueRange
        {
            public ValueRange(double minimum, double maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            public double Minimum { get; }
            public double Maximum { get; }
        }

        readonly struct ThermodynamicValue
        {
            public static ThermodynamicValue None => new ThermodynamicValue(double.NaN, null, null);

            public ThermodynamicValue(double value, double? lower, double? upper)
            {
                Value = value;
                Lower = lower;
                Upper = upper;
            }

            public double Value { get; }
            public double? Lower { get; }
            public double? Upper { get; }
            public bool HasValue => IsFinite(Value);
        }
    }
}
