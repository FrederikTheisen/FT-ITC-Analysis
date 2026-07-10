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
    public sealed class ResultDependenceGraphControl : Control
    {
        static AvaloniaGraphTheme GraphTheme => AvaloniaGraphSettings.Current;

        AnalysisResult? result;
        ResultAnalysisViewMode mode = ResultAnalysisViewMode.Temperature;
        ElectrostaticsAnalysis.DissocFitMode saltMode = ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel;

        IReadOnlyList<GraphSeries> cachedSeries = Array.Empty<GraphSeries>();
        string xLabel = "";
        string yLabel = "";

        public ResultDependenceGraphControl()
        {
            Focusable = true;
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        public AnalysisResult? Result
        {
            get => result;
            set
            {
                if (ReferenceEquals(result, value)) return;
                result = value;
                Rebuild();
            }
        }

        public ResultAnalysisViewMode Mode
        {
            get => mode;
            set
            {
                if (mode == value) return;
                mode = value;
                Rebuild();
            }
        }

        public ElectrostaticsAnalysis.DissocFitMode SaltMode
        {
            get => saltMode;
            set
            {
                if (saltMode == value) return;
                saltMode = value;
                Rebuild();
            }
        }

        public void FitToData()
        {
            InvalidateVisual();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            DataManager.ResultSolutionSelectionDidChange += OnResultSolutionSelectionChanged;
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

        public void Rebuild()
        {
            cachedSeries = BuildSeries();
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            context.DrawRectangle(GraphTheme.CanvasBrush, null, bounds);

            var range = BuildRange(cachedSeries);
            var yTicks = BuildTicks(range.YMin, range.YMax);
            var yLabelWidth = yTicks.Count == 0
                ? AvaloniaGraphSettings.YLabelFallbackWidth
                : yTicks.Max(tick => MeasureText(FormatAxisValue(tick), AvaloniaGraphSettings.TickLabelFontSize).Width);

            var left = Math.Max(AvaloniaGraphSettings.GraphMarginLeftMinimum, yLabelWidth + AvaloniaGraphSettings.GraphMarginLeftTickBuffer);
            var plot = new Rect(
                left,
                AvaloniaGraphSettings.GraphMarginTop,
                Math.Max(1, bounds.Width - left - AvaloniaGraphSettings.GraphMarginRight),
                Math.Max(1, bounds.Height - AvaloniaGraphSettings.GraphMarginTop - AvaloniaGraphSettings.GraphMarginBottom));

            context.DrawRectangle(GraphTheme.PlotBrush, GraphTheme.FramePen, plot);

            if (cachedSeries.Count == 0 || cachedSeries.All(series => series.Points.Count == 0) || plot.Width < 80 || plot.Height < 80)
            {
                DrawText(
                    context,
                    EmptyMessage(),
                    new Point(plot.Left + AvaloniaGraphSettings.EmptyStateXOffset, plot.Top + AvaloniaGraphSettings.EmptyStateTitleYOffset),
                    AvaloniaGraphSettings.EmptyTitleFontSize,
                    FontWeight.SemiBold,
                    GraphTheme.MutedTextBrush);
                return;
            }

            DrawAxes(context, plot, range, yTicks);

            foreach (var series in cachedSeries)
            {
                DrawFit(context, plot, range, series);
                DrawPoints(context, plot, range, series);
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            var hit = FindPoint(e.GetPosition(this));
            if (hit.HasValue && hit.Value.Solution != null)
                DataManager.SelectResultSolution(hit.Value.Solution);
            else
                DataManager.ClearResultSolutionSelection();

            e.Handled = true;
        }

        GraphPoint? FindPoint(Point pointer)
        {
            var range = BuildRange(cachedSeries);
            var yTicks = BuildTicks(range.YMin, range.YMax);
            var yLabelWidth = yTicks.Count == 0
                ? AvaloniaGraphSettings.YLabelFallbackWidth
                : yTicks.Max(tick => MeasureText(FormatAxisValue(tick), AvaloniaGraphSettings.TickLabelFontSize).Width);
            var left = Math.Max(AvaloniaGraphSettings.GraphMarginLeftMinimum, yLabelWidth + AvaloniaGraphSettings.GraphMarginLeftTickBuffer);
            var plot = new Rect(
                left,
                AvaloniaGraphSettings.GraphMarginTop,
                Math.Max(1, Bounds.Width - left - AvaloniaGraphSettings.GraphMarginRight),
                Math.Max(1, Bounds.Height - AvaloniaGraphSettings.GraphMarginTop - AvaloniaGraphSettings.GraphMarginBottom));

            if (!plot.Contains(pointer)) return null;

            GraphPoint? closest = null;
            var closestDistance = double.MaxValue;
            foreach (var point in cachedSeries.SelectMany(series => series.Points))
            {
                var screen = ToScreen(plot, range, point.X, point.Y);
                var distance = Math.Sqrt(Math.Pow(screen.X - pointer.X, 2) + Math.Pow(screen.Y - pointer.Y, 2));
                if (distance < closestDistance && distance <= AvaloniaGraphSettings.AnalysisHitSize)
                {
                    closest = point;
                    closestDistance = distance;
                }
            }

            return closest;
        }

        void DrawAxes(DrawingContext context, Rect plot, GraphRange range, IReadOnlyList<double> yTicks)
        {
            foreach (var tick in yTicks)
            {
                var y = ToScreen(plot, range, range.XMin, tick).Y;
                context.DrawLine(GraphTheme.MajorGridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                DrawRightAlignedText(
                    context,
                    FormatAxisValue(tick),
                    new Point(plot.Left - AvaloniaGraphSettings.TickLabelOffset, y - AvaloniaGraphSettings.YTickLabelYOffset),
                    AvaloniaGraphSettings.TickLabelFontSize,
                    GraphTheme.MutedTextBrush);
            }

            foreach (var tick in BuildTicks(range.XMin, range.XMax))
            {
                var x = ToScreen(plot, range, tick, range.YMin).X;
                context.DrawLine(GraphTheme.MinorGridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                DrawCenteredText(
                    context,
                    FormatAxisValue(tick),
                    new Point(x, plot.Bottom + AvaloniaGraphSettings.TickLabelOffset),
                    AvaloniaGraphSettings.TickLabelFontSize,
                    GraphTheme.MutedTextBrush);
            }

            if (range.YMin < 0 && range.YMax > 0)
            {
                var zero = ToScreen(plot, range, range.XMin, 0).Y;
                context.DrawLine(GraphTheme.ZeroPen, new Point(plot.Left, zero), new Point(plot.Right, zero));
            }

            DrawText(context, yLabel, new Point(plot.Left, plot.Top - AvaloniaGraphSettings.AxisTitleOffset), AvaloniaGraphSettings.AxisTitleFontSize, FontWeight.SemiBold, GraphTheme.TextBrush);
            DrawCenteredText(context, xLabel, new Point(plot.Left + plot.Width / 2, plot.Bottom + AvaloniaGraphSettings.XAxisTitleOffset), AvaloniaGraphSettings.AxisTitleFontSize, GraphTheme.TextBrush);
        }

        void DrawFit(DrawingContext context, Rect plot, GraphRange range, GraphSeries series)
        {
            if (series.Fit == null) return;

            var fitDomain = FitDomain(series);
            if (fitDomain == null) return;

            var line = new List<Point>();
            var upper = new List<Point>();
            var lower = new List<Point>();
            var segmentCount = 96;

            for (int i = 0; i <= segmentCount; i++)
            {
                var x = fitDomain.Value.Min + (fitDomain.Value.Max - fitDomain.Value.Min) * i / segmentCount;
                var y = series.Fit(x);
                if (!IsFinite(y.Value)) continue;

                line.Add(ToScreen(plot, range, x, y.Value));
                if (IsFinite(y.Lower) && IsFinite(y.Upper) && Math.Abs(y.Upper - y.Lower) > 1E-12)
                {
                    upper.Add(ToScreen(plot, range, x, y.Upper));
                    lower.Add(ToScreen(plot, range, x, y.Lower));
                }
            }

            if (upper.Count > 1 && lower.Count > 1)
            {
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(upper[0], true);
                    foreach (var point in upper.Skip(1)) ctx.LineTo(point);
                    foreach (var point in lower.AsEnumerable().Reverse()) ctx.LineTo(point);
                    ctx.EndFigure(true);
                }
                context.DrawGeometry(GraphTheme.ConfidenceBandBrush, null, geometry);
            }

            if (line.Count < 2) return;
            var path = new StreamGeometry();
            using (var ctx = path.Open())
            {
                ctx.BeginFigure(line[0], false);
                foreach (var point in line.Skip(1)) ctx.LineTo(point);
            }
            context.DrawGeometry(null, GraphTheme.FitPen, path);
        }

        void DrawPoints(DrawingContext context, Rect plot, GraphRange range, GraphSeries series)
        {
            var selected = DataManager.SelectedResultSolution;
            foreach (var point in series.Points)
            {
                var screen = ToScreen(plot, range, point.X, point.Y);
                var selectedPoint = ReferenceEquals(point.Solution, selected);
                var brush = selectedPoint ? GraphTheme.DataBrush : GraphTheme.PointBrush;
                var pen = selectedPoint ? GraphTheme.DataPen : GraphTheme.PointPen;
                var radius = selectedPoint ? AvaloniaGraphSettings.AnalysisSymbolSize * 0.75 : AvaloniaGraphSettings.AnalysisSymbolSize * 0.58;

                if (IsFinite(point.LowerY) && IsFinite(point.UpperY))
                {
                    var upper = ToScreen(plot, range, point.X, point.UpperY);
                    var lower = ToScreen(plot, range, point.X, point.LowerY);
                    var cap = 4;
                    context.DrawLine(GraphTheme.PointPen, upper, lower);
                    context.DrawLine(GraphTheme.PointPen, new Point(upper.X - cap, upper.Y), new Point(upper.X + cap, upper.Y));
                    context.DrawLine(GraphTheme.PointPen, new Point(lower.X - cap, lower.Y), new Point(lower.X + cap, lower.Y));
                }

                DrawSymbol(context, screen, series.Symbol, radius, brush, pen);
            }
        }

        IReadOnlyList<GraphSeries> BuildSeries()
        {
            xLabel = "";
            yLabel = "";

            if (result == null) return Array.Empty<GraphSeries>();

            return mode switch
            {
                ResultAnalysisViewMode.Temperature => BuildTemperatureSeries(),
                ResultAnalysisViewMode.Salt => BuildSaltSeries(),
                ResultAnalysisViewMode.Protonation => BuildProtonationSeries(),
                _ => Array.Empty<GraphSeries>()
            };
        }

        IReadOnlyList<GraphSeries> BuildTemperatureSeries()
        {
            if (result?.Solution?.TemperatureDependence == null || result.Solution.TemperatureDependence.Count == 0)
                return Array.Empty<GraphSeries>();

            xLabel = "Temperature (°C)";
            yLabel = $"Thermodynamic parameter ({AppSettings.EnergyUnit.GetUnit()}/mol)";

            var parameters = new[]
            {
                ParameterType.Enthalpy1,
                ParameterType.EntropyContribution1,
                ParameterType.Gibbs1,
                ParameterType.Enthalpy2,
                ParameterType.EntropyContribution2,
                ParameterType.Gibbs2
            };

            return parameters
                .Where(parameter => result.Solution.TemperatureDependence.ContainsKey(parameter))
                .Select((parameter, index) =>
                {
                    var fit = result.Solution.TemperatureDependence[parameter];
                    return new GraphSeries(
                        parameter.GetProperties().Name,
                        result.Solution.Solutions
                            .Where(solution => solution.ReportParameters.ContainsKey(parameter))
                            .Select(solution => PointFrom(solution.Temp, solution.ReportParameters[parameter], solution, Energy.ScaleFactor(AppSettings.EnergyUnit)))
                            .Where(point => point.HasValue)
                            .OrderBy(point => point.X)
                            .ToList(),
                        x => ValueFrom(fit.Evaluate(x, 400), Energy.ScaleFactor(AppSettings.EnergyUnit)),
                        SymbolForSeries(index));
                })
                .Where(series => series.Points.Count > 0)
                .ToList();
        }

        IReadOnlyList<GraphSeries> BuildSaltSeries()
        {
            var analysis = result?.ElectrostaticsAnalysis;
            var solutions = result?.Solution?.Solutions ?? new List<SolutionInterface>();
            if (analysis == null || solutions.Count == 0) return Array.Empty<GraphSeries>();

            switch (saltMode)
            {
                case ElectrostaticsAnalysis.DissocFitMode.AffinityVsSalt:
                    {
                        xLabel = "[Salt] (mM)";
                        yLabel = $"Kd ({result!.AppropriateAffinityUnit.GetName()})";
                        var unit = result.AppropriateAffinityUnit;
                        var points = solutions
                            .Where(solution => solution.ReportParameters.ContainsKey(ParameterType.Affinity1))
                            .Select(solution =>
                            {
                                var salt = solution.Data.Attributes.Find(att => att.Key == AttributeKey.Salt)?.ParameterValue.Value ?? 0;
                                return PointFrom(1000 * salt, solution.ReportParameters[ParameterType.Affinity1], solution, unit.GetMod());
                            })
                            .Where(point => point.HasValue)
                            .OrderBy(point => point.X)
                            .ToList();
                        return new[] { new GraphSeries("Affinity vs Salt", JitterDuplicateX(points), null) };
                    }
                case ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease:
                    {
                        xLabel = "ln(a salt)";
                        yLabel = "ln(Kd)";
                        var points = solutions
                            .Where(solution => solution.ReportParameters.ContainsKey(ParameterType.Affinity1))
                            .Select(solution =>
                            {
                                var activity = SaltAttribute.GetIonActivity(solution.Data);
                                var affinity = solution.ReportParameters[ParameterType.Affinity1];
                                return activity > 0 && affinity.Value > 0
                                    ? PointFrom(Math.Log(activity), FWEMath.Log(affinity), solution, 1)
                                    : GraphPoint.None;
                            })
                            .Where(point => point.HasValue)
                            .OrderBy(point => point.X)
                            .ToList();
                        return new[]
                        {
                            new GraphSeries(
                                "Counter Ion Release",
                                points,
                                analysis.CounterIonReleaseFit == null ? null : x => ValueFrom(analysis.CounterIonReleaseFit.Evaluate(x, 400), 1))
                        };
                    }
                default:
                    {
                        xLabel = "sqrt(Ionic Strength / M)";
                        yLabel = "Log(Kd)";
                        var points = solutions
                            .Where(solution => solution.ReportParameters.ContainsKey(ParameterType.Affinity1))
                            .Select(solution => PointFrom(
                                Math.Sqrt(Math.Max(0, BufferAttribute.GetIonicStrength(solution.Data))),
                                FWEMath.Log10(solution.ReportParameters[ParameterType.Affinity1]),
                                solution,
                                1))
                            .Where(point => point.HasValue)
                            .OrderBy(point => point.X)
                            .ToList();
                        return new[]
                        {
                            new GraphSeries(
                                "Debye-Huckel",
                                points,
                                analysis.IonicStrengthDependenceFit == null ? null : x => ValueFrom(analysis.IonicStrengthDependenceFit.Evaluate(x * x), 1))
                        };
                    }
            }
        }

        IReadOnlyList<GraphSeries> BuildProtonationSeries()
        {
            var analysis = result?.ProtonationAnalysis;
            if (analysis == null) return Array.Empty<GraphSeries>();

            var scale = Energy.ScaleFactor(AppSettings.EnergyUnit);
            xLabel = $"Buffer protonation enthalpy ({AppSettings.EnergyUnit.GetUnit()}/mol)";
            yLabel = $"Observed enthalpy ({AppSettings.EnergyUnit.GetUnit()}/mol)";

            var points = result?.Solution?.Solutions
                .Where(solution => solution.Data.Attributes.Exists(att => att.Key == AttributeKey.Buffer))
                .Select(solution =>
                {
                    var buffer = (AnalysisITC.Core.Data.Buffer)solution.Data.Attributes.Find(att => att.Key == AttributeKey.Buffer)!.IntValue;
                    var x = buffer.GetProtonationEnthalpy(solution.Temp);
                    return PointFrom(x, solution.TotalEnthalpy, solution, scale);
                })
                .Where(point => point.HasValue)
                .OrderBy(point => point.X)
                .ToList() ?? new List<GraphPoint>();

            return new[]
            {
                new GraphSeries(
                    "Protonation",
                    points,
                    analysis.Fit == null ? null : x => ValueFrom(analysis.Fit.Evaluate(x / scale, 400), scale))
            };
        }

        static List<GraphPoint> JitterDuplicateX(IReadOnlyList<GraphPoint> points)
        {
            if (points.Count < 2) return points.ToList();
            var grouped = points.GroupBy(dp => dp.X).ToList();
            var range = Math.Max(1, points.Max(dp => dp.X) - points.Min(dp => dp.X));
            var jitter = range / 70;
            return grouped.SelectMany(group =>
            {
                var values = group.ToList();
                return values.Select((dp, i) => dp.WithX(dp.X + (values.Count == 1 ? 0.0 : (i - (values.Count - 1) / 2.0) * jitter)));
            }).ToList();
        }

        static GraphPoint PointFrom(double x, FloatWithError y, SolutionInterface? solution, double scale)
        {
            if (FloatWithError.IsNaN(y))
                return GraphPoint.None;

            return new GraphPoint(x, y.Value * scale, y.Lower * scale, y.Upper * scale, solution);
        }

        static GraphSymbol SymbolForSeries(int index)
        {
            var symbols = new[]
            {
                GraphSymbol.Circle,
                GraphSymbol.Square,
                GraphSymbol.Triangle,
                GraphSymbol.Diamond,
                GraphSymbol.Cross,
                GraphSymbol.Plus
            };
            return symbols[index % symbols.Length];
        }

        void DrawSymbol(DrawingContext context, Point center, GraphSymbol symbol, double radius, IBrush fill, Pen outline)
        {
            switch (symbol)
            {
                case GraphSymbol.Square:
                    var square = new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2);
                    context.DrawRectangle(fill, outline, square);
                    break;
                case GraphSymbol.Triangle:
                    var triangle = new StreamGeometry();
                    using (var ctx = triangle.Open())
                    {
                        ctx.BeginFigure(new Point(center.X, center.Y - radius), true);
                        ctx.LineTo(new Point(center.X - radius, center.Y + radius));
                        ctx.LineTo(new Point(center.X + radius, center.Y + radius));
                        ctx.EndFigure(true);
                    }
                    context.DrawGeometry(fill, outline, triangle);
                    break;
                case GraphSymbol.Diamond:
                    var diamond = new StreamGeometry();
                    using (var ctx = diamond.Open())
                    {
                        ctx.BeginFigure(new Point(center.X, center.Y - radius), true);
                        ctx.LineTo(new Point(center.X + radius, center.Y));
                        ctx.LineTo(new Point(center.X, center.Y + radius));
                        ctx.LineTo(new Point(center.X - radius, center.Y));
                        ctx.EndFigure(true);
                    }
                    context.DrawGeometry(fill, outline, diamond);
                    break;
                case GraphSymbol.Cross:
                    context.DrawLine(outline, new Point(center.X - radius, center.Y - radius), new Point(center.X + radius, center.Y + radius));
                    context.DrawLine(outline, new Point(center.X - radius, center.Y + radius), new Point(center.X + radius, center.Y - radius));
                    break;
                case GraphSymbol.Plus:
                    context.DrawLine(outline, new Point(center.X - radius, center.Y), new Point(center.X + radius, center.Y));
                    context.DrawLine(outline, new Point(center.X, center.Y - radius), new Point(center.X, center.Y + radius));
                    break;
                default:
                    context.DrawEllipse(fill, outline, center, radius, radius);
                    break;
            }
        }

        static GraphValue ValueFrom(FloatWithError value, double scale)
        {
            if (FloatWithError.IsNaN(value))
                return new GraphValue(double.NaN, double.NaN, double.NaN);

            return new GraphValue(value.Value * scale, value.Lower * scale, value.Upper * scale);
        }

        static (double Min, double Max)? FitDomain(GraphSeries series)
        {
            var xs = series.Points
                .Where(point => point.HasValue)
                .Select(point => point.X)
                .Where(IsFinite)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            if (xs.Count == 0) return null;
            if (xs.Count == 1)
            {
                var delta = Math.Max(Math.Abs(xs[0]) * 0.05, 1E-6);
                return (xs[0] - delta, xs[0] + delta);
            }

            return (xs.First(), xs.Last());
        }

        string EmptyMessage()
        {
            return mode switch
            {
                ResultAnalysisViewMode.Temperature => "Temperature dependence is not available.",
                ResultAnalysisViewMode.Salt => "Salt dependence is not available.",
                ResultAnalysisViewMode.Protonation => "Protonation analysis is not available.",
                _ => "No advanced analysis selected."
            };
        }

        static GraphRange BuildRange(IReadOnlyList<GraphSeries> series)
        {
            var xs = new List<double>();
            var ys = new List<double> { 0 };
            foreach (var point in series.SelectMany(s => s.Points))
            {
                xs.Add(point.X);
                ys.Add(point.Y);
                ys.Add(point.LowerY);
                ys.Add(point.UpperY);
            }

            foreach (var fit in series.Select(s => s.Fit).Where(fit => fit != null))
            {
                var finiteX = xs.Where(IsFinite).ToList();
                if (finiteX.Count == 0) continue;
                var min = finiteX.Min();
                var max = finiteX.Max();
                var span = Math.Max(1E-9, max - min);
                for (int i = 0; i <= 24; i++)
                {
                    var x = min + span * i / 24;
                    var y = fit!(x);
                    ys.Add(y.Value);
                    ys.Add(y.Lower);
                    ys.Add(y.Upper);
                }
            }

            var finiteXs = xs.Where(IsFinite).ToList();
            var finiteYs = ys.Where(IsFinite).ToList();
            if (finiteXs.Count == 0 || finiteYs.Count == 0)
                return new GraphRange(0, 1, -1, 1);

            var xMin = finiteXs.Min();
            var xMax = finiteXs.Max();
            var yMin = finiteYs.Min();
            var yMax = finiteYs.Max();
            var xDelta = xMax - xMin;
            var yDelta = yMax - yMin;
            if (Math.Abs(xDelta) < 1E-12) xDelta = Math.Max(1, Math.Abs(xMax));
            if (Math.Abs(yDelta) < 1E-12) yDelta = Math.Max(1, Math.Abs(yMax));
            return new GraphRange(
                xMin - xDelta * AvaloniaGraphSettings.AnalysisXPaddingFraction,
                xMax + xDelta * AvaloniaGraphSettings.AnalysisXPaddingFraction,
                yMin - yDelta * AvaloniaGraphSettings.AnalysisYPaddingFraction,
                yMax + yDelta * AvaloniaGraphSettings.AnalysisYPaddingFraction);
        }

        static Point ToScreen(Rect plot, GraphRange range, double x, double y)
        {
            var px = plot.Left + (x - range.XMin) / Math.Max(1E-12, range.XMax - range.XMin) * plot.Width;
            var py = plot.Bottom - (y - range.YMin) / Math.Max(1E-12, range.YMax - range.YMin) * plot.Height;
            return new Point(px, py);
        }

        static List<double> BuildTicks(double minimum, double maximum)
        {
            var ticks = new List<double>();
            var span = maximum - minimum;
            if (!IsFinite(span) || span <= 0) return ticks;

            var step = NiceNumber(span / 5, round: true);
            var start = Math.Ceiling(minimum / step) * step;
            for (var value = start; value <= maximum + step * 0.001 && ticks.Count < 20; value += step)
                ticks.Add(Math.Abs(value) < 1E-12 ? 0 : value);

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

        static string FormatAxisValue(double value)
        {
            if (!IsFinite(value)) return "";
            return value.ToString(Math.Abs(value) < 10 ? "G3" : "G4", CultureInfo.CurrentCulture);
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static Size MeasureText(string text, double size)
        {
            var formatted = CreateText(text, size, FontWeight.Normal, GraphTheme.TextBrush);
            return new Size(formatted.Width, formatted.Height);
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

        enum GraphSymbol
        {
            Circle,
            Square,
            Triangle,
            Diamond,
            Cross,
            Plus
        }

        sealed class GraphSeries
        {
            public GraphSeries(string name, IReadOnlyList<GraphPoint> points, Func<double, GraphValue>? fit, GraphSymbol symbol = GraphSymbol.Circle)
            {
                Name = name;
                Points = points;
                Fit = fit;
                Symbol = symbol;
            }

            public string Name { get; }
            public IReadOnlyList<GraphPoint> Points { get; }
            public Func<double, GraphValue>? Fit { get; }
            public GraphSymbol Symbol { get; }
        }

        readonly struct GraphPoint
        {
            public static GraphPoint None => new GraphPoint(double.NaN, double.NaN, double.NaN, double.NaN, null);

            public GraphPoint(double x, double y, double lowerY, double upperY, SolutionInterface? solution)
            {
                X = x;
                Y = y;
                LowerY = lowerY;
                UpperY = upperY;
                Solution = solution;
            }

            public double X { get; }
            public double Y { get; }
            public double LowerY { get; }
            public double UpperY { get; }
            public SolutionInterface? Solution { get; }
            public bool HasValue => IsFinite(X) && IsFinite(Y);

            public GraphPoint WithX(double x)
            {
                return new GraphPoint(x, Y, LowerY, UpperY, Solution);
            }
        }

        readonly struct GraphValue
        {
            public GraphValue(double value, double lower, double upper)
            {
                Value = value;
                Lower = lower;
                Upper = upper;
            }

            public double Value { get; }
            public double Lower { get; }
            public double Upper { get; }
        }

        readonly struct GraphRange
        {
            public GraphRange(double xMin, double xMax, double yMin, double yMax)
            {
                XMin = xMin;
                XMax = xMax;
                YMin = yMin;
                YMax = yMax;
            }

            public double XMin { get; }
            public double XMax { get; }
            public double YMin { get; }
            public double YMax { get; }
        }
    }
}
