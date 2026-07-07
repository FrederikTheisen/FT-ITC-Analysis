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
        static readonly Pen FramePen = new Pen(Solid("#AEB8C2"), 1);
        static readonly Pen GridPen = new Pen(Solid("#E3E7EC"), 1);
        static readonly Pen ZeroPen = new Pen(Solid("#334155"), 1.2);
        static readonly Pen ErrorPen = new Pen(Solid("#202832"), 1.1);
        static readonly Pen SelectedPen = new Pen(Solid("#2563EB"), 2);
        static readonly IBrush BarBrush = Solid("#64748B");
        static readonly Pen BarPen = new Pen(Solid("#475569"), 1);
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

            var bounds = Bounds;
            context.DrawRectangle(CanvasBrush, null, bounds);

            var plot = new Rect(64, 22, Math.Max(1, bounds.Width - 84), Math.Max(1, bounds.Height - 70));
            context.DrawRectangle(PlotBrush, FramePen, plot);

            var solutions = result?.Solution?.Solutions ?? new List<SolutionInterface>();
            var parameters = AvailableThermodynamicParameters(solutions);

            if (solutions.Count == 0 || parameters.Count == 0 || plot.Width < 80 || plot.Height < 80)
            {
                DrawText(context, "No thermodynamic result parameters", new Point(plot.Left + 16, plot.Top + 16), 14, FontWeight.SemiBold, MutedTextBrush);
                return;
            }

            var selected = DataManager.SelectedResultSolution;
            var yRange = BuildValueRange(solutions, parameters);
            var ticks = BuildTicks(yRange.Minimum, yRange.Maximum);

            foreach (var tick in ticks)
            {
                var y = YForValue(plot, yRange.Minimum, yRange.Maximum, tick);
                context.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                DrawRightAlignedText(context, FormatAxisValue(tick), new Point(plot.Left - 8, y - 7), 10, MutedTextBrush);
            }

            var zeroY = YForValue(plot, yRange.Minimum, yRange.Maximum, 0);
            context.DrawLine(ZeroPen, new Point(plot.Left, zeroY), new Point(plot.Right, zeroY));

            DrawText(context, $"Energy ({AppSettings.EnergyUnit.GetUnit()}/mol)", new Point(8, plot.Top + 4), 11, FontWeight.SemiBold, TextBrush);

            var categoryWidth = plot.Width / parameters.Count;
            for (int parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
            {
                var parameter = parameters[parameterIndex];
                var categoryLeft = plot.Left + parameterIndex * categoryWidth;
                var categoryCenter = categoryLeft + categoryWidth * 0.5;

                if (parameterIndex > 0)
                    context.DrawLine(GridPen, new Point(categoryLeft, plot.Top), new Point(categoryLeft, plot.Bottom));

                DrawParameterBars(context, plot, yRange, solutions, selected, parameter, categoryLeft, categoryWidth);
                DrawCenteredText(context, ParameterLabel(parameter), new Point(categoryCenter, plot.Bottom + 8), 11, TextBrush);
            }

            DrawSolutionLegend(context, plot, solutions, selected);
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
            var plot = new Rect(64, 22, Math.Max(1, bounds.Width - 84), Math.Max(1, bounds.Height - 70));
            var point = e.GetPosition(this);

            if (!plot.Contains(point))
            {
                DataManager.ClearResultSolutionSelection();
                e.Handled = true;
                return;
            }

            var parameters = AvailableThermodynamicParameters(solutions);
            var index = SolutionIndexForPoint(plot, parameters.Count, solutions.Count, point.X);
            if (index >= 0 && index < solutions.Count)
                DataManager.SelectResultSolution(solutions[index]);

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
                var pen = selectedBar ? SelectedPen : BarPen;

                context.DrawRectangle(BarBrush, pen, rect);

                if (value.Lower.HasValue && value.Upper.HasValue)
                {
                    var lowerY = YForValue(plot, yRange.Minimum, yRange.Maximum, value.Lower.Value);
                    var upperY = YForValue(plot, yRange.Minimum, yRange.Maximum, value.Upper.Value);
                    var centerX = rect.Left + rect.Width * 0.5;
                    var cap = Math.Max(3, rect.Width * 0.35);
                    context.DrawLine(ErrorPen, new Point(centerX, upperY), new Point(centerX, lowerY));
                    context.DrawLine(ErrorPen, new Point(centerX - cap, upperY), new Point(centerX + cap, upperY));
                    context.DrawLine(ErrorPen, new Point(centerX - cap, lowerY), new Point(centerX + cap, lowerY));
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
                context.DrawRectangle(BarBrush, ReferenceEquals(solutions[i], selected) ? SelectedPen : BarPen, rect);
                DrawText(context, (i + 1).ToString(CultureInfo.CurrentCulture), new Point(x + 14, y - 1), 10, FontWeight.Normal, MutedTextBrush);
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
