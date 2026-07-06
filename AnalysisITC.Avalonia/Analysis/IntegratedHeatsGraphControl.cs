using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Analysis
{
    public sealed class IntegratedHeatsGraphControl : Control
    {
        static readonly IBrush CanvasBrush = Solid("#F4F7FA");
        static readonly IBrush PlotBrush = Brushes.White;
        static readonly IBrush TextBrush = Solid("#202832");
        static readonly IBrush MutedTextBrush = Solid("#607080");
        static readonly IBrush PointBrush = Solid("#202832");
        static readonly IBrush ExcludedBrush = Solid("#7B8794");
        static readonly IBrush FitBrush = Solid("#111827");
        static readonly IBrush GuideBrush = Solid("#8A96A3");
        static readonly IBrush HoverBrush = Solid("#2563EB");
        static readonly IBrush HoverBackgroundBrush = Brushes.White;
        static readonly IBrush BandBrush = new SolidColorBrush(Color.FromArgb(62, 90, 100, 115));

        static readonly Pen FramePen = new Pen(Solid("#AEB8C2"), 1);
        static readonly Pen AxisPen = new Pen(Solid("#26323D"), 1);
        static readonly Pen MajorGridPen = new Pen(Solid("#D8DEE6"), 1);
        static readonly Pen MinorGridPen = new Pen(Solid("#EEF2F6"), 1);
        static readonly Pen PointPen = new Pen(PointBrush, 1.2);
        static readonly Pen ExcludedPen = new Pen(ExcludedBrush, 1.2);
        static readonly Pen FitPen = new Pen(FitBrush, 1.8);
        static readonly Pen GuidePen = new Pen(GuideBrush, 1) { DashStyle = DashStyle.Dash };
        static readonly Pen ZeroPen = new Pen(Solid("#6B7682"), 1);
        static readonly Pen HoverPen = new Pen(HoverBrush, 1);

        const double SymbolSize = 7;
        const double HitSize = 12;
        const double ResidualFraction = 0.22;
        const double ResidualGap = 8;

        ExperimentData? experiment;
        GraphViewport view;
        GraphViewport residualView;
        bool hasView;
        Point? hoverPoint;
        GraphPoint? hoverGraphPoint;

        public event EventHandler? GraphChanged;
        public event EventHandler<string>? StatusChanged;

        public IntegratedHeatsGraphControl()
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
                hoverPoint = null;
                hoverGraphPoint = null;
                FitToData();
            }
        }

        public bool ShowFit { get; set; } = true;
        public bool ShowResiduals { get; set; } = true;
        public bool ShowErrorBars { get; set; } = true;
        public bool ShowConfidenceBand { get; set; } = true;
        public bool ShowPointLabels { get; set; } = true;
        public bool ShowFitParameters { get; set; } = true;
        public bool ShowExcludedPoints { get; set; } = true;
        public bool ScaleToIncludedPoints { get; set; }
        public bool UnifiedXAxis { get; set; }
        public bool UnifiedYAxis { get; set; }
        public bool DrawWithOffset { get; set; } = true;

        EnergyDisplay Energy => EnergyDisplay.Current;

        bool HasResidualPanel => ShowResiduals && Experiment?.Solution != null;

        public void FitToData()
        {
            var displayPoints = PlotPoints(includeExcluded: ShowExcludedPoints).ToList();
            var yScalingPoints = PlotPoints(includeExcluded: !ScaleToIncludedPoints && ShowExcludedPoints).ToList();
            var fitPoints = FitPoints().ToList();

            if (displayPoints.Count == 0 && fitPoints.Count == 0)
            {
                hasView = false;
                InvalidateVisual();
                return;
            }

            var unifiedXPoints = UnifiedXAxis ? ScalingPoints(includeExcluded: ShowExcludedPoints).ToList() : new List<GraphPoint>();
            var unifiedYPoints = UnifiedYAxis ? ScalingPoints(includeExcluded: !ScaleToIncludedPoints && ShowExcludedPoints).ToList() : new List<GraphPoint>();
            if (unifiedXPoints.Count == 0)
                unifiedXPoints = displayPoints.Concat(fitPoints).ToList();
            if (unifiedYPoints.Count == 0)
                unifiedYPoints = yScalingPoints.Concat(fitPoints).ToList();

            var xSource = UnifiedXAxis ? unifiedXPoints : displayPoints.Concat(fitPoints).ToList();
            var ySource = UnifiedYAxis ? unifiedYPoints : yScalingPoints.Concat(fitPoints).ToList();

            var xValues = xSource.Select(point => point.X).ToList();
            var yValues = ySource.SelectMany(point => new[] { point.Y, point.LowerY, point.UpperY })
                .Concat(new[] { 0.0 })
                .Where(double.IsFinite)
                .ToList();

            if (xValues.Count == 0) xValues.AddRange(new[] { 0.0, 1.0 });
            if (yValues.Count == 0) yValues.AddRange(new[] { -1.0, 1.0 });

            view = GraphViewport.WithPadding(xValues.Min(), xValues.Max(), yValues.Min(), yValues.Max(), 0.06, 0.12, includeZeroY: true);
            residualView = BuildResidualView();
            hasView = true;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            context.DrawRectangle(CanvasBrush, null, bounds);

            if (bounds.Width < 160 || bounds.Height < 160)
                return;

            var layout = GraphLayout.Create(bounds, view, residualView, Energy, HasResidualPanel, XAxisTitle());
            context.DrawRectangle(PlotBrush, FramePen, layout.FitPlot);

            if (Experiment == null || !hasView)
            {
                DrawEmptyState(context, layout.FitPlot);
                return;
            }

            DrawFitPanel(context, layout);

            if (HasResidualPanel)
                DrawResidualPanel(context, layout);

            DrawHover(context, layout);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (!hasView)
            {
                hoverPoint = null;
                hoverGraphPoint = null;
                return;
            }

            var layout = GraphLayout.Create(Bounds, view, residualView, Energy, HasResidualPanel, XAxisTitle());
            var point = e.GetPosition(this);
            hoverPoint = point;
            hoverGraphPoint = HitTest(point, layout);
            Cursor = hoverGraphPoint.HasValue ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Cross);

            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (!hasView) return;

            var layout = GraphLayout.Create(Bounds, view, residualView, Energy, HasResidualPanel, XAxisTitle());
            var hit = HitTest(e.GetPosition(this), layout);
            if (!hit.HasValue) return;

            var injection = hit.Value.Injection;
            injection.ToggleDataPointActive();
            GraphChanged?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, injection.Include
                ? $"Included injection #{injection.ID + 1}"
                : $"Excluded injection #{injection.ID + 1}");

            FitToData();
            e.Handled = true;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);

            hoverPoint = null;
            hoverGraphPoint = null;
            Cursor = new Cursor(StandardCursorType.Cross);
            InvalidateVisual();
        }

        void DrawFitPanel(DrawingContext context, GraphLayout layout)
        {
            DrawGrid(context, layout.FitPlot, layout.FitTransform, layout.XTicks, layout.YTicks);
            DrawZeroLine(context, layout.FitPlot, layout.FitTransform);

            if (Experiment?.Solution != null && ShowConfidenceBand)
                DrawConfidenceBand(context, layout);

            if (Experiment?.Solution != null && ShowFit)
                DrawFitLine(context, layout);

            if (Experiment?.Solution != null && ShowFitParameters)
                DrawParameterGuides(context, layout);

            DrawPoints(context, layout);
            DrawAxes(context, layout.FitPlot, layout.FitTransform, layout.XTicks, layout.YTicks, layout.XAxisTitle, layout.YAxisTitle, hideXAxisLabels: HasResidualPanel);

            if (Experiment?.Solution != null && ShowFitParameters)
                DrawParameterBox(context, layout.FitPlot);
        }

        void DrawResidualPanel(DrawingContext context, GraphLayout layout)
        {
            context.DrawRectangle(PlotBrush, FramePen, layout.ResidualPlot);
            DrawGrid(context, layout.ResidualPlot, layout.ResidualTransform, layout.XTicks, layout.ResidualYTicks);
            DrawZeroLine(context, layout.ResidualPlot, layout.ResidualTransform);
            DrawResidualPoints(context, layout);
            DrawAxes(context, layout.ResidualPlot, layout.ResidualTransform, layout.XTicks, layout.ResidualYTicks, layout.XAxisTitle, "", hideXAxisLabels: false);
        }

        void DrawEmptyState(DrawingContext context, Rect plot)
        {
            DrawText(context, "No integrated heats selected", new Point(plot.Left + 18, plot.Top + 18), 14, FontWeight.SemiBold, MutedTextBrush);
            DrawText(context, "Process an experiment, then switch to Analyze Data to inspect injection heats.", new Point(plot.Left + 18, plot.Top + 43), 12, FontWeight.Normal, MutedTextBrush);
        }

        void DrawGrid(DrawingContext context, Rect plot, PlotTransform transform, AxisTicks xTicks, AxisTicks yTicks)
        {
            using (context.PushClip(plot))
            {
                foreach (var tick in xTicks.Minor)
                    context.DrawLine(MinorGridPen, new Point(Crisp(transform.X(tick)), plot.Top), new Point(Crisp(transform.X(tick)), plot.Bottom));

                foreach (var tick in yTicks.Minor)
                    context.DrawLine(MinorGridPen, new Point(plot.Left, Crisp(transform.Y(tick))), new Point(plot.Right, Crisp(transform.Y(tick))));

                foreach (var tick in xTicks.Major)
                    context.DrawLine(MajorGridPen, new Point(Crisp(transform.X(tick)), plot.Top), new Point(Crisp(transform.X(tick)), plot.Bottom));

                foreach (var tick in yTicks.Major)
                    context.DrawLine(MajorGridPen, new Point(plot.Left, Crisp(transform.Y(tick))), new Point(plot.Right, Crisp(transform.Y(tick))));
            }
        }

        void DrawAxes(DrawingContext context, Rect plot, PlotTransform transform, AxisTicks xTicks, AxisTicks yTicks, string xTitle, string yTitle, bool hideXAxisLabels)
        {
            context.DrawLine(AxisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
            context.DrawLine(AxisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));

            if (!hideXAxisLabels)
            {
                foreach (var tick in xTicks.Major)
                {
                    if (!view.ContainsX(tick)) continue;

                    var x = Crisp(transform.X(tick));
                    context.DrawLine(AxisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 5));
                    DrawCenteredText(context, xTicks.Format(tick), new Point(x, plot.Bottom + 9), 11, MutedTextBrush);
                }

                DrawCenteredText(context, xTitle, new Point(plot.Left + plot.Width / 2, plot.Bottom + 35), 12, TextBrush);
            }

            foreach (var tick in yTicks.Major)
            {
                if (!transform.View.ContainsY(tick)) continue;

                var y = Crisp(transform.Y(tick));
                context.DrawLine(AxisPen, new Point(plot.Left - 5, y), new Point(plot.Left, y));
                DrawRightAlignedText(context, yTicks.Format(tick), new Point(plot.Left - 9, y - 7), 11, MutedTextBrush);
            }

            if (!string.IsNullOrWhiteSpace(yTitle))
                DrawText(context, yTitle, new Point(plot.Left, plot.Top - 24), 12, FontWeight.SemiBold, TextBrush);
        }

        void DrawZeroLine(DrawingContext context, Rect plot, PlotTransform transform)
        {
            if (!transform.View.ContainsY(0)) return;

            var y = Crisp(transform.Y(0));
            using (context.PushClip(plot))
            {
                context.DrawLine(ZeroPen, new Point(plot.Left, y), new Point(plot.Right, y));
            }
        }

        void DrawPoints(DrawingContext context, GraphLayout layout)
        {
            foreach (var point in PlotPoints(includeExcluded: ShowExcludedPoints))
            {
                if (!view.ContainsX(point.X) || !view.ContainsY(point.Y)) continue;

                DrawErrorBar(context, layout.FitPlot, layout.FitTransform, point);
                DrawSymbol(context, layout.FitTransform.ToScreen(point.X, point.Y), point.Included);

                if (ShowPointLabels)
                {
                    var labelPoint = layout.FitTransform.ToScreen(point.X, Math.Max(point.Y, point.UpperY));
                    DrawCenteredText(context, (point.Injection.ID + 1).ToString(CultureInfo.CurrentCulture), new Point(labelPoint.X, labelPoint.Y - 19), 10, MutedTextBrush);
                }
            }
        }

        void DrawResidualPoints(DrawingContext context, GraphLayout layout)
        {
            foreach (var point in ResidualPoints(includeExcluded: ShowExcludedPoints))
            {
                if (!view.ContainsX(point.X) || !layout.ResidualTransform.View.ContainsY(point.Y)) continue;

                DrawErrorBar(context, layout.ResidualPlot, layout.ResidualTransform, point);
                DrawSymbol(context, layout.ResidualTransform.ToScreen(point.X, point.Y), point.Included);
            }
        }

        void DrawErrorBar(DrawingContext context, Rect plot, PlotTransform transform, GraphPoint point)
        {
            if (!ShowErrorBars || !point.Included && !ShowExcludedPoints) return;
            if (Math.Abs(point.UpperY - point.LowerY) < double.Epsilon) return;

            var center = transform.ToScreen(point.X, point.Y);
            var top = transform.ToScreen(point.X, point.UpperY);
            var bottom = transform.ToScreen(point.X, point.LowerY);
            var pen = point.Included ? PointPen : ExcludedPen;

            using (context.PushClip(plot))
            {
                context.DrawLine(pen, new Point(center.X, top.Y), new Point(center.X, center.Y - SymbolSize / 2));
                context.DrawLine(pen, new Point(center.X, center.Y + SymbolSize / 2), new Point(center.X, bottom.Y));
                context.DrawLine(pen, new Point(center.X - SymbolSize / 2, top.Y), new Point(center.X + SymbolSize / 2, top.Y));
                context.DrawLine(pen, new Point(center.X - SymbolSize / 2, bottom.Y), new Point(center.X + SymbolSize / 2, bottom.Y));
            }
        }

        void DrawSymbol(DrawingContext context, Point center, bool included)
        {
            var rect = new Rect(center.X - SymbolSize / 2, center.Y - SymbolSize / 2, SymbolSize, SymbolSize);
            var brush = included ? PointBrush : Brushes.White;
            var pen = included ? PointPen : ExcludedPen;

            context.DrawRectangle(brush, pen, rect);
        }

        void DrawFitLine(DrawingContext context, GraphLayout layout)
        {
            var points = FitPoints()
                .Where(point => view.ContainsX(point.X) && view.ContainsY(point.Y))
                .OrderBy(point => point.X)
                .ToList();
            if (points.Count < 2) return;

            DrawPolyline(context, layout.FitPlot, points.Select(point => layout.FitTransform.ToScreen(point.X, point.Y)).ToList(), FitPen);
        }

        void DrawConfidenceBand(DrawingContext context, GraphLayout layout)
        {
            var band = ConfidenceBand()
                .Where(point => view.ContainsX(point.X))
                .OrderBy(point => point.X)
                .ToList();
            if (band.Count < 2) return;

            var upper = band.Select(point => layout.FitTransform.ToScreen(point.X, point.UpperY)).ToList();
            var lower = band.Select(point => layout.FitTransform.ToScreen(point.X, point.LowerY)).Reverse().ToList();

            var geometry = new StreamGeometry();
            using (var stream = geometry.Open())
            {
                stream.BeginFigure(upper[0], true);
                foreach (var point in upper.Skip(1)) stream.LineTo(point);
                foreach (var point in lower) stream.LineTo(point);
                stream.EndFigure(true);
            }

            using (context.PushClip(layout.FitPlot))
            {
                context.DrawGeometry(BandBrush, null, geometry);
            }
        }

        void DrawParameterGuides(DrawingContext context, GraphLayout layout)
        {
            var data = Experiment;
            if (data?.Solution == null) return;

            using (context.PushClip(layout.FitPlot))
            {
                foreach (var n in data.Solution.GetCorrectedStoichiometryGuides())
                {
                    var x = XValue(n.Value);
                    if (!view.ContainsX(x)) continue;

                    var screenX = layout.FitTransform.X(x);
                    context.DrawLine(GuidePen, new Point(screenX, layout.FitPlot.Top), new Point(screenX, layout.FitPlot.Bottom));
                }

                foreach (var h in data.Solution.GetCorrectedEnthalpyGuides())
                {
                    var y = h.Value;
                    if (DrawWithOffset) y += data.Solution.Offset;
                    y *= Energy.Scale;
                    if (!view.ContainsY(y)) continue;

                    var screenY = layout.FitTransform.Y(y);
                    context.DrawLine(GuidePen, new Point(layout.FitPlot.Left, screenY), new Point(layout.FitPlot.Right, screenY));
                }
            }
        }

        void DrawParameterBox(DrawingContext context, Rect plot)
        {
            var data = Experiment;
            if (data?.Solution == null) return;

            var lines = data.Solution.UISolutionParameters(FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Fitted | FinalFigureDisplayParameters.Derived)
                .Select(parameter => $"{parameter.Item1} = {parameter.Item2}")
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(8)
                .ToList();

            if (lines.Count == 0) return;

            DrawInfoBox(context, lines, plot, new Point(plot.Right - 14, plot.Top + 14), alignRight: true);
        }

        void DrawHover(DrawingContext context, GraphLayout layout)
        {
            if (!hoverPoint.HasValue || !hoverGraphPoint.HasValue) return;

            var point = hoverGraphPoint.Value;
            var screen = layout.FitTransform.ToScreen(point.X, point.Y);

            using (context.PushClip(layout.FitPlot))
            {
                context.DrawLine(HoverPen, new Point(screen.X, layout.FitPlot.Top), new Point(screen.X, layout.FitPlot.Bottom));
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(55, 37, 99, 235)), null, new Rect(screen.X - 7, screen.Y - 7, 14, 14), 2);
            }

            var lines = new List<string>
            {
                $"Injection #{point.Injection.ID + 1}",
                $"{layout.XAxisTitle}: {point.X:G4}",
                $"Heat: {Energy.Format(point.Y)}",
                point.Included ? "Included" : "Excluded"
            };

            if (Experiment?.Solution != null)
                lines.Add($"Residual: {Energy.Format(point.Injection.ResidualEnthalpy * Energy.Scale)}");

            DrawInfoBox(context, lines, layout.FitPlot, screen, alignRight: false);
        }

        void DrawInfoBox(DrawingContext context, IReadOnlyList<string> lines, Rect plot, Point anchor, bool alignRight)
        {
            const double paddingX = 9;
            const double paddingY = 7;
            const double lineGap = 3;

            var texts = lines.Select(line => CreateText(line, 11, FontWeight.Normal, TextBrush)).ToArray();
            var width = texts.Max(text => text.Width) + paddingX * 2;
            var height = texts.Sum(text => text.Height) + lineGap * (texts.Length - 1) + paddingY * 2;
            var x = alignRight ? anchor.X - width : anchor.X + 12;
            var y = alignRight ? anchor.Y : anchor.Y - height - 10;

            if (x + width > plot.Right - 8) x = anchor.X - width - 12;
            if (x < plot.Left + 8) x = plot.Left + 8;
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

        GraphPoint? HitTest(Point point, GraphLayout layout)
        {
            foreach (var graphPoint in PlotPoints(includeExcluded: ShowExcludedPoints))
            {
                var screen = layout.FitTransform.ToScreen(graphPoint.X, graphPoint.Y);
                if (Math.Abs(screen.X - point.X) <= HitSize / 2 && Math.Abs(screen.Y - point.Y) <= HitSize / 2)
                    return graphPoint;
            }

            return null;
        }

        IEnumerable<GraphPoint> PlotPoints(bool includeExcluded)
        {
            return PlotPointsFor(Experiment, includeExcluded);
        }

        IEnumerable<GraphPoint> PlotPointsFor(ExperimentData? data, bool includeExcluded)
        {
            if (data?.Injections == null) yield break;

            foreach (var injection in data.Injections)
            {
                if (!injection.IsIntegrated) continue;
                if (!injection.Include && !includeExcluded) continue;

                var x = XValue(data, injection);
                var y = YValue(data, injection);
                var sd = Safe(injection.SD) ? injection.SD * Energy.Scale : 0;

                if (!Safe(x) || !Safe(y)) continue;

                yield return new GraphPoint(injection, x, y, y - sd, y + sd, injection.Include);
            }
        }

        IEnumerable<GraphPoint> ResidualPoints(bool includeExcluded)
        {
            var data = Experiment;
            if (data?.Solution == null) yield break;

            foreach (var injection in data.Injections)
            {
                if (!injection.IsIntegrated) continue;
                if (!injection.Include && !includeExcluded) continue;

                var x = XValue(injection);
                var y = injection.ResidualEnthalpy * Energy.Scale;
                var sd = Safe(injection.SD) ? injection.SD * Energy.Scale : 0;

                if (!Safe(x) || !Safe(y)) continue;

                yield return new GraphPoint(injection, x, y, y - sd, y + sd, injection.Include);
            }
        }

        IEnumerable<GraphPoint> FitPoints()
        {
            return FitPointsFor(Experiment);
        }

        IEnumerable<GraphPoint> FitPointsFor(ExperimentData? data)
        {
            if (data?.Solution == null || data.Model == null) yield break;

            foreach (var injection in data.Injections)
            {
                var x = XValue(data, injection);
                var y = data.Model.EvaluateEnthalpy(injection.ID, DrawWithOffset) * Energy.Scale;
                if (!Safe(x) || !Safe(y)) continue;

                yield return new GraphPoint(injection, x, y, y, y, true);
            }
        }

        IEnumerable<GraphPoint> ScalingPoints(bool includeExcluded)
        {
            foreach (var data in DataManager.IncludedData)
            {
                foreach (var point in PlotPointsFor(data, includeExcluded))
                    yield return point;

                foreach (var point in FitPointsFor(data))
                    yield return point;
            }
        }

        IEnumerable<GraphPoint> ConfidenceBand()
        {
            var data = Experiment;
            if (data?.Solution?.BootstrapSolutions == null || data.Solution.BootstrapSolutions.Count == 0 || data.Model == null) yield break;

            foreach (var injection in data.Injections)
            {
                var confidence = data.Model.EvaluateBootstrap(injection.ID, DrawWithOffset).WithConfidence();
                if (confidence == null || confidence.Length < 2) continue;

                var x = XValue(injection);
                var lower = confidence[0] * Energy.Scale;
                var upper = confidence[1] * Energy.Scale;
                if (!Safe(x) || !Safe(lower) || !Safe(upper)) continue;

                yield return new GraphPoint(injection, x, 0, lower, upper, true);
            }
        }

        double XValue(InjectionData injection)
        {
            return XValue(Experiment, injection);
        }

        static double XValue(ExperimentData? data, InjectionData injection)
        {
            return data?.AxisType switch
            {
                AnalysisXAxisType.TitrantConcentration => injection.ActualTitrantConcentration * 1_000_000,
                AnalysisXAxisType.ID => injection.ID + 1,
                _ => injection.Ratio
            };
        }

        double XValue(double stoichiometry)
        {
            return Experiment?.AxisType == AnalysisXAxisType.TitrantConcentration
                ? stoichiometry * 1_000_000
                : stoichiometry;
        }

        double YValue(InjectionData injection)
        {
            return YValue(Experiment, injection);
        }

        double YValue(ExperimentData? data, InjectionData injection)
        {
            return (DrawWithOffset ? injection.Enthalpy : injection.OffsetEnthalpy) * Energy.Scale;
        }

        string XAxisTitle()
        {
            return Experiment?.AxisType.GetEnumDescription() ?? "Molar Ratio";
        }

        GraphViewport BuildResidualView()
        {
            var points = ResidualPoints(includeExcluded: !ScaleToIncludedPoints && ShowExcludedPoints).ToList();
            var max = points.Count == 0
                ? 1
                : 1.5 * Math.Max(points.SelectMany(point => new[]
                {
                    Math.Abs(point.Y),
                    Math.Abs(point.LowerY),
                    Math.Abs(point.UpperY)
                }).Max(), 1E-3);

            return new GraphViewport(view.XMin, view.XMax, -max, max);
        }

        static bool Safe(double value) => double.IsFinite(value) && !double.IsNaN(value);

        static void DrawPolyline(DrawingContext context, Rect clip, IReadOnlyList<Point> points, Pen pen)
        {
            if (points.Count < 2) return;

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

        readonly struct EnergyDisplay
        {
            public double Scale { get; }
            public string UnitLabel { get; }

            EnergyDisplay(double scale, string unitLabel)
            {
                Scale = scale;
                UnitLabel = unitLabel;
            }

            public static EnergyDisplay Current => new EnergyDisplay(AnalysisITC.Core.Units.Energy.ScaleFactor(AppSettings.EnergyUnit), AppSettings.EnergyUnit.GetUnit() + "/mol");

            public string Format(double value) => $"{value:G4} {UnitLabel}";
        }

        readonly struct GraphPoint
        {
            public GraphPoint(InjectionData injection, double x, double y, double lowerY, double upperY, bool included)
            {
                Injection = injection;
                X = x;
                Y = y;
                LowerY = lowerY;
                UpperY = upperY;
                Included = included;
            }

            public InjectionData Injection { get; }
            public double X { get; }
            public double Y { get; }
            public double LowerY { get; }
            public double UpperY { get; }
            public bool Included { get; }
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

            public static GraphViewport WithPadding(double xMin, double xMax, double yMin, double yMax, double xPaddingFraction, double yPaddingFraction, bool includeZeroY)
            {
                if (includeZeroY)
                {
                    yMin = Math.Min(yMin, 0);
                    yMax = Math.Max(yMax, 0);
                }

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
            public Rect FitPlot { get; }
            public Rect ResidualPlot { get; }
            public PlotTransform FitTransform { get; }
            public PlotTransform ResidualTransform { get; }
            public AxisTicks XTicks { get; }
            public AxisTicks YTicks { get; }
            public AxisTicks ResidualYTicks { get; }
            public string XAxisTitle { get; }
            public string YAxisTitle { get; }

            GraphLayout(Rect fitPlot, Rect residualPlot, PlotTransform fitTransform, PlotTransform residualTransform, AxisTicks xTicks, AxisTicks yTicks, AxisTicks residualYTicks, string xAxisTitle, string yAxisTitle)
            {
                FitPlot = fitPlot;
                ResidualPlot = residualPlot;
                FitTransform = fitTransform;
                ResidualTransform = residualTransform;
                XTicks = xTicks;
                YTicks = yTicks;
                ResidualYTicks = residualYTicks;
                XAxisTitle = xAxisTitle;
                YAxisTitle = yAxisTitle;
            }

            public static GraphLayout Create(Rect bounds, GraphViewport view, GraphViewport residualView, EnergyDisplay energy, bool hasResidual, string xAxisTitle)
            {
                var xTicks = AxisTicks.Create(view.XMin, view.XMax, Math.Max(4, Math.Min(8, (int)(bounds.Width / 145))));
                var yTicks = AxisTicks.Create(view.YMin, view.YMax, Math.Max(4, Math.Min(7, (int)(bounds.Height / 105))));
                var yLabelWidth = yTicks.Major.Count == 0 ? 44 : yTicks.Major.Max(tick => MeasureText(yTicks.Format(tick), 11).Width);
                var left = Math.Max(78, yLabelWidth + 26);
                const double top = 38;
                const double right = 24;
                const double bottom = 58;
                var fullHeight = Math.Max(1, bounds.Height - top - bottom);
                var residualHeight = hasResidual ? Math.Max(70, fullHeight * ResidualFraction) : 0;
                var gap = hasResidual ? ResidualGap : 0;
                var fitHeight = Math.Max(1, fullHeight - residualHeight - gap);

                var fitPlot = new Rect(left, top, Math.Max(1, bounds.Width - left - right), fitHeight);
                var residualPlot = hasResidual
                    ? new Rect(left, top + fitHeight + gap, fitPlot.Width, residualHeight)
                    : default;
                return new GraphLayout(
                    fitPlot,
                    residualPlot,
                    new PlotTransform(fitPlot, view),
                    new PlotTransform(residualPlot, residualView),
                    xTicks,
                    yTicks,
                    AxisTicks.Create(residualView.YMin, residualView.YMax, 3),
                    xAxisTitle,
                    $"Heat ({energy.UnitLabel})");
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
            public GraphViewport View { get; }

            public PlotTransform(Rect plot, GraphViewport view)
            {
                this.plot = plot;
                View = view;
            }

            public Point ToScreen(double x, double y) => new Point(X(x), Y(y));

            public double X(double value) => plot.Left + (value - View.XMin) / Math.Max(double.Epsilon, View.XMax - View.XMin) * plot.Width;
            public double Y(double value) => plot.Bottom - (value - View.YMin) / Math.Max(double.Epsilon, View.YMax - View.YMin) * plot.Height;
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
    }
}
