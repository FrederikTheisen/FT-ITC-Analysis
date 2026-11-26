using System;
using AppKit;
using CoreGraphics;
using CoreText;
using Foundation;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.UI.MacOS.Drawing;
using AnalysisITC.UI.MacOS;
using AnalysisITC.Core.Analysis;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.UI.MacOS.Drawing
{
    public class TemperatureDependenceGraph : GraphBase
    {
        readonly AnalysisResultPresentationData presentation;
        readonly Dictionary<ParameterType, IReadOnlyList<FitEnvelopePoint>> envelopes = new();
        readonly double TemperatureOffset;
        readonly bool showUncertainty;
        readonly string uncertaintyCaption = "";
        IReadOnlyList<string> captionLines = Array.Empty<string>();
        double captionWidth;
        const int CaptionLineHeight = 15;

        List<FeatureBoundingBox> FeatureBoundingBoxes = new List<FeatureBoundingBox>();

        public TemperatureDependenceGraph(AnalysisResult analysis, NSView view)
            : this(analysis, view, useKelvin: false)
        {
        }

        public TemperatureDependenceGraph(
            AnalysisResult analysis,
            NSView view,
            bool useKelvin)
        {
            View = view;
            presentation = analysis.PresentationData;
            TemperatureOffset = useKelvin ? 273.15 : 0;
            showUncertainty = AppSettings.UncertaintyDisplayStyle != UncertaintyDisplayStyle.None;

            XAxis = GraphAxis.WithBuffer(
                this,
                analysis.GetMinimumTemperature(),
                analysis.GetMaximumTemperature(),
                buffer: .1,
                position: AxisPosition.Bottom);
            // Keep sampling independent of display-unit rounding in GraphAxis.
            var minimumTemperatureCelsius = XAxis.Min;
            var maximumTemperatureCelsius = XAxis.Max;
            XAxis.Set(DisplayTemperature(minimumTemperatureCelsius), DisplayTemperature(maximumTemperatureCelsius));
            XAxis.HideUnwantedTicks = false;
            XAxis.LegendTitle =
                "Temperature (" + (useKelvin ? "K" : "°C") + ")";

            // Seed the axis before collecting prepared member/envelope values.
            // The old range helpers evaluate every linked uncertainty on each
            // graph recreation.
            YAxis = GraphAxis.WithBuffer(this, 0, 1, buffer: .1, position: AxisPosition.Left);
            YAxis.HideUnwantedTicks = false;
            var values = new List<double>();
            var centers = new List<double>();
            var bandKinds = new List<(string Label, TemperatureDependenceIntervalKind Kind)>();
            var hasPointIntervals = false;
            var dependences = analysis.Solution?.TemperatureDependence;
            if (dependences != null)
            {
                foreach (var item in dependences)
                {
                    foreach (var member in presentation.Members)
                    {
                        if (!member.Parameters.TryGetValue(item.Key, out var point)
                            || !IsFinite(point.Value) || !IsFinite(member.TemperatureCelsius)) continue;
                        centers.Add(point.Value);
                        if (showUncertainty && TemperatureDependenceUncertaintyCaption.TryGetIndividualInterval(
                                analysis, member.Solution, item.Key, point, out var lower, out var upper))
                        {
                            values.Add(lower);
                            values.Add(upper);
                            hasPointIntervals = true;
                        }
                    }

                    var envelope = presentation.GetTemperatureEnvelope(
                        item.Key, minimumTemperatureCelsius, maximumTemperatureCelsius,
                        out var intervalKind, mode: AppSettings.SummaryUncertaintyMode);
                    envelopes[item.Key] = envelope;
                    centers.AddRange(envelope.Select(point => point.Center));
                    if (!showUncertainty) continue;
                    var bandRuns = TemperatureDependenceUncertaintyCaption.UsableBandRuns(envelope).ToList();
                    if (bandRuns.Count == 0) continue;
                    bandKinds.Add((item.Key.GetProperties().Name, intervalKind));
                    foreach (var (start, length) in bandRuns)
                        for (var index = start; index < start + length; index++)
                        {
                            values.Add(envelope[index].Lower);
                            values.Add(envelope[index].Upper);
                        }
                }
            }
            uncertaintyCaption = TemperatureDependenceUncertaintyCaption.Build(hasPointIntervals, bandKinds);
            var energyUnit = EnergyUnitResolver.Resolve(AppSettings.EnergyUnitFamily, centers.Where(IsFinite));
            values.AddRange(centers);
            var finiteValues = values.Where(IsFinite).ToList();
            if (finiteValues.Count > 0)
                YAxis.SetWithBuffer(finiteValues.Min(), finiteValues.Max(), .1);
            YAxis.ValueFactor = Energy.ScaleFactor(energyUnit);
            YAxis.MirrorTicks = true;
            YAxis.LegendTitle = "Thermodynamic parameter (" + energyUnit.GetUnit() + "/mol)";
        }

        public override void AutoSetFrame()
        {
            base.AutoSetFrame();
            if (uncertaintyCaption.Length == 0 || PlotSize.Width <= 0) return;

            if (captionWidth != PlotSize.Width)
            {
                captionWidth = PlotSize.Width;
                captionLines = WrapCaption(uncertaintyCaption, captionWidth);
            }

            var captionHeight = captionLines.Count * CaptionLineHeight + 5;
            Origin.Y += captionHeight;
            PlotSize.Height -= captionHeight;
        }

        IReadOnlyList<string> WrapCaption(string caption, double width)
        {
            var lines = new List<string>();
            var line = "";
            var attributes = new CTStringAttributes { Font = GraphAxis.TickFont };
            foreach (var word in caption.Split(' '))
            {
                var combined = line.Length == 0 ? word : line + " " + word;
                using var text = new NSAttributedString(combined, attributes);
                using var measurement = new CTLine(text);
                if (line.Length > 0 && measurement.GetBounds(CTLineBoundsOptions.UseOpticalBounds).Width > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else line = combined;
            }
            if (line.Length > 0) lines.Add(line);
            return lines;
        }

        public override void PrepareDraw(CGContext gc, CGPoint center)
        {
            this.Center = center;

            FeatureBoundingBoxes.Clear();

            AutoSetFrame();
            if (PlotSize.Width <= 0 || PlotSize.Height <= 0) return;

            SetupAxisScalingUnits();

            DrawFrameBackground(gc);

            Draw(gc);

            DrawFrame(gc);

            XAxis.Draw(gc);
            YAxis.Draw(gc);
            if (captionLines.Count > 0)
            {
                using var layer = CGLayer.Create(gc, new CGSize(PlotSize.Width, captionLines.Count * CaptionLineHeight));
                for (var index = 0; index < captionLines.Count; index++)
                    DrawString(layer, captionLines[index],
                        new CGPoint(0, (captionLines.Count - index - .5) * CaptionLineHeight),
                        GraphAxis.TickFont, horizontalignment: TextAlignment.Left, textcolor: StrokeColor);
                gc.DrawLayer(layer, new CGPoint(Origin.X, 5));
            }
        }

        void SetupAxisScalingUnits()
        {
            if (Frame.Size.Width * Frame.Size.Height < 0) return;

            var pppw = PlotSize.Width / (XAxis.Max - XAxis.Min);
            var ppph = PlotSize.Height / (YAxis.Max - YAxis.Min);

            PointsPerUnit = new CGSize(pppw, ppph);
        }

        void Draw(CGContext gc)
        {
            DrawDependencies(gc);

            DrawZeroLine(gc);
        }

        private void DrawZeroLine(CGContext gc)
        {
            var path = new CGPath();
            path.MoveToPoint(GetRelativePosition(XAxis.Min, 0));
            path.AddLineToPoint(GetRelativePosition(XAxis.Max, 0));

            var layer = CGLayer.Create(gc, PlotSize);
            layer.Context.SetStrokeColor(StrokeColor);
            layer.Context.AddPath(path);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawDependencies(CGContext gc)
        {
            foreach (var entry in envelopes)
                DrawDependency(gc, entry.Key, entry.Value);
        }

        void DrawDependency(CGContext gc, ParameterType key, IReadOnlyList<FitEnvelopePoint> envelope)
        {
            SymbolShape symbol = SymbolShape.Square;
            bool fill = true;
            var slotIndex = 1;

            if (ThermodynamicParameterSlots.TryResolve(key, out var slot, out var family))
            {
                slotIndex = slot.Index;
                fill = slot.Index % 2 == 1;
                symbol = family switch
                {
                    ThermodynamicParameterFamily.Enthalpy => SymbolShape.Square,
                    ThermodynamicParameterFamily.EntropyContribution => SymbolShape.Circle,
                    ThermodynamicParameterFamily.Gibbs => SymbolShape.Diamond,
                    _ => SymbolShape.Square,
                };
            }

            if (showUncertainty) DrawConfidenceBand(gc, envelope);
            DrawLinFit(gc, envelope);

            DrawDataPoints(gc, key, symbol, fill, slotIndex);
        }

        void DrawLinFit(CGContext gc, IReadOnlyList<FitEnvelopePoint> envelope)
        {
            if (envelope == null || envelope.Count < 2) return;

            using var path = new CGPath();
            var hasSegment = false;
            foreach (var point in envelope)
            {
                if (!IsFinite(point.X) || !IsFinite(point.Center))
                {
                    hasSegment = false;
                    continue;
                }

                var position = GetRelativePosition(DisplayTemperature(point.X), point.Center);
                if (hasSegment) path.AddLineToPoint(position);
                else path.MoveToPoint(position);
                hasSegment = true;
            }

            var layer = CGLayer.Create(gc, PlotSize);
            layer.Context.SetStrokeColor(StrokeColor);
            layer.Context.AddPath(path);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawDataPoints(CGContext gc, ParameterType key, SymbolShape symbol, bool fill, int slotIndex)
        {
            var size = 8 + 2 * Math.Max(1, Math.Min(4, slotIndex));
            CGSize barwidth = new(size / 2, 0);

            var layer = CGLayer.Create(gc, PlotSize);
            var points = new List<CGPoint>();
            var selectedpoint = new List<CGPoint>();
            var bars = new CGPath();

            for (int i = 0; i < presentation.Members.Count; i++)
            {
                var member = presentation.Members[i];
                if (!member.Parameters.TryGetValue(key, out var y)
                    || !IsFinite(y.Value) || !IsFinite(member.TemperatureCelsius)) continue;
                var sol = member.Solution;
                var x = DisplayTemperature(member.TemperatureCelsius);
                var dp = GetRelativePosition(x, y);

                FeatureBoundingBoxes.Add(new FeatureBoundingBox(MouseOverFeatureEvent.FeatureType.DataPoint, dp, size * 0.66f, i, Frame.Location));

                points.Add(dp);

                if (showUncertainty && TemperatureDependenceUncertaintyCaption.TryGetIndividualInterval(
                        presentation.Result, sol, key, y, out var lower, out var upper))
                {
                    var top = GetRelativePosition(x, upper);
                    var bottom = GetRelativePosition(x, lower);
                    bars.MoveToPoint(top);
                    bars.AddLineToPoint(bottom);
                    bars.MoveToPoint(CGPoint.Subtract(top, barwidth));
                    bars.AddLineToPoint(CGPoint.Add(top, barwidth));
                    bars.MoveToPoint(CGPoint.Subtract(bottom, barwidth));
                    bars.AddLineToPoint(CGPoint.Add(bottom, barwidth));
                }

                if (sol == DataManager.SelectedResultSolution)
                    selectedpoint.Add(dp);
            }

            layer.Context.SetStrokeColor(StrokeColor);
            layer.Context.AddPath(bars);
            layer.Context.StrokePath();

            DrawSymbolsAtPositions(layer, points.ToArray(), size, symbol, fill, 1, null, 0);

            if (selectedpoint.Count > 0)
            {
                var color = NSColor.ControlAccent.CGColor;
                var edge = MacColors.Adjust(color, -40);
                DrawSymbolsAtPositions(layer, selectedpoint.ToArray(), size * 1.2f, symbol, true, 1, color, 0);
                DrawSymbolsAtPositions(layer, selectedpoint.ToArray(), size * 1.2f, symbol, false, 0.5f, edge, 0);
            }

            gc.DrawLayer(layer, Origin);
        }

        void DrawConfidenceBand(
            CGContext gc,
            IReadOnlyList<FitEnvelopePoint> envelope)
        {
            using var path = new CGPath();
            foreach (var (start, length) in TemperatureDependenceUncertaintyCaption.UsableBandRuns(envelope))
            {
                var end = start + length - 1;
                path.MoveToPoint(GetRelativePosition(DisplayTemperature(envelope[start].X), envelope[start].Upper));
                for (var index = start + 1; index <= end; index++)
                    path.AddLineToPoint(GetRelativePosition(DisplayTemperature(envelope[index].X), envelope[index].Upper));
                for (var index = end; index >= start; index--)
                    path.AddLineToPoint(GetRelativePosition(DisplayTemperature(envelope[index].X), envelope[index].Lower));
                path.CloseSubpath();
            }
            if (path.IsEmpty) return;

            var layer = CGLayer.Create(gc, PlotSize);
            layer.Context.SetFillColor(new CGColor(StrokeColor, .25f));
            layer.Context.AddPath(path);
            layer.Context.FillPath();

            gc.DrawLayer(layer, Frame.Location);
        }

        double DisplayTemperature(double celsius) =>
            celsius + TemperatureOffset;


        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public override MouseOverFeatureEvent CursorFeatureFromPos(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            foreach (var feature in FeatureBoundingBoxes)
            {
                if (feature.CursorInBox(cursorpos))
                    return new MouseOverFeatureEvent(feature);
            }

            return new MouseOverFeatureEvent();
        }
    }
}
