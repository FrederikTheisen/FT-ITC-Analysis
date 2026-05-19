using System;
using System.Collections.Generic;
using System.Linq;
using AppKit;
using CoreGraphics;
using CoreText;
using Utilities;

namespace AnalysisITC
{
    public class BufferSubtractionGraph : CGGraph
    {
        const float TargetSymbolSize = 5;
        const float BufferSymbolSize = 8;
        const float HighlightSize = 14;

        static readonly NSColor[] TargetColors = new[]
        {
            NSColor.SystemBlue,
            NSColor.SystemGray,
            NSColor.Red,
            NSColor.Gray,
            NSColor.LightGray,
        };

        readonly List<FeatureBoundingBox> bufferPointBoxes = new();
        int mouseDownInjectionID = -1;
        int mouseOverInjectionID = -1;

        public event EventHandler BufferPointIncludeChanged;

        public List<ExperimentData> TargetExperiments { get; private set; } = new();
        public BufferSubtractionModel SubtractionModel { get; private set; }

        public bool ShowGrid { get; set; } = true;
        public bool ShowZero { get; set; } = true;
        public bool ShowErrorBars { get; set; } = true;
        public bool ShowAverageLine { get; set; } = false;
        public bool FocusYAxisOnBufferData { get; set; } = false;
        public double AverageLineSlope { get; set; } = 0;
        public double AverageLineIntercept { get; set; } = 0;
        public SymbolShape BufferSymbolShape { get; set; } = SymbolShape.Square;
        public SymbolShape TargetSymbolShape { get; set; } = SymbolShape.Circle;

        public ExperimentData BufferExperiment
        {
            get => ExperimentData;
            private set => ExperimentData = value;
        }

        double HeatScaleFactor => DataManager.Unit.IsSI()
            ? 1000000
            : 1000000 * Energy.JouleToCalFactor;

        string HeatUnitLabel => DataManager.Unit.IsSI() ? "uJ" : "ucal";

        public BufferSubtractionGraph(ExperimentData bufferExperiment, IEnumerable<ExperimentData> targetExperiments, BufferSubtractionModel subtractionModel, NSView view, bool focusYAxisOnBufferData = false)
            : base(bufferExperiment, view)
        {
            FocusYAxisOnBufferData = focusYAxisOnBufferData;

            XAxis = new GraphAxis(this, 0.5, 1.5)
            {
                UseNiceAxis = false,
                HideUnwantedTicks = false,
                DecimalPoints = 0,
                LegendTitle = "Injection number",
                ValueFactor = 1,
                MirrorTicks = true,
            };
            XAxis.TickScale.SetMaxTicks(8);

            YAxis = new GraphAxis(this, -1E-6, 1E-6)
            {
                UseNiceAxis = false,
                HideUnwantedTicks = false,
                LegendTitle = "Integrated heat (" + HeatUnitLabel + ")",
                ValueFactor = HeatScaleFactor,
                MirrorTicks = true,
            };
            YAxis.TickScale.SetMaxTicks(5);

            UpdateData(bufferExperiment, targetExperiments, subtractionModel);
        }

        public void UpdateData(ExperimentData bufferExperiment, IEnumerable<ExperimentData> targetExperiments, BufferSubtractionModel subtractionModel)
        {
            BufferExperiment = bufferExperiment;
            TargetExperiments = targetExperiments?
                .Where(exp => exp != null && exp != bufferExperiment)
                .ToList() ?? new List<ExperimentData>();
            SubtractionModel = subtractionModel;

            SetupAxes();
        }

        public override void PrepareDraw(CGContext gc, CGPoint center)
        {
            SetupAxes();
            base.PrepareDraw(gc, center);
        }

        void SetupAxes()
        {
            XAxis.LegendTitle = "Injection number";
            XAxis.ValueFactor = 1;
            XAxis.DecimalPoints = 0;

            YAxis.LegendTitle = "Integrated heat (" + HeatUnitLabel + ")";
            YAxis.ValueFactor = HeatScaleFactor;

            var allData = AllExperiments().ToList();
            var maxInjectionCount = allData.Count == 0 ? 1 : allData.Max(exp => Math.Max(exp.InjectionCount, 1));

            if (maxInjectionCount <= 1) XAxis.Set(0.5, 1.5);
            else XAxis.SetWithBuffer(1, maxInjectionCount, 0.05);
            XAxis.DecimalPoints = 0;

            var heatValues = new List<double>();
            IEnumerable<ExperimentData> yAxisData = FocusYAxisOnBufferData && BufferExperiment != null
                ? new[] { BufferExperiment }
                : allData;

            foreach (var exp in yAxisData)
            {
                heatValues.AddRange(IntegratedInjections(exp).Select(inj => inj.RawPeakArea.Value));
            }

            if (ShowAverageLine)
            {
                heatValues.Add(EvaluateAverageLine(XAxis.Min));
                heatValues.Add(EvaluateAverageLine(XAxis.Max));
            }

            if (SubtractionModel?.CanDrawLine == true)
            {
                heatValues.AddRange(SubtractionModel.GetLinePoints(XAxis.Min, XAxis.Max).Select(p => p.Heat));
            }

            if (heatValues.Count == 0)
            {
                YAxis.SetWithBuffer(-1E-6, 1E-6, 0.1);
            }
            else
            {
                var min = Math.Min(0, heatValues.Min());
                var max = Math.Max(0, heatValues.Max());
                YAxis.SetWithBuffer(min, max, 0.1);
            }
        }

        internal override void Draw(CGContext gc)
        {
            bufferPointBoxes.Clear();

            if (ShowGrid) DrawGrid(gc);
            if (ShowZero) DrawZero(gc);

            DrawTargetData(gc);
            DrawSubtractionModelLine(gc);
            DrawAverageLine(gc);
            DrawBufferData(gc);

            XAxis.Draw(gc);
            YAxis.Draw(gc);

            if (!AllExperiments().Any(exp => IntegratedInjections(exp).Any()))
            {
                DrawTextBoxConsistent(gc, new List<string>
                {
                    BufferExperiment == null ? "Select a buffer experiment." : "No integrated heats available."
                }, new CTFont(DefaultFont.DisplayName, 12), NSRectAlignment.BottomTrailing);
            }
        }

        void DrawTargetData(CGContext gc)
        {
            for (int i = 0; i < TargetExperiments.Count; i++)
            {
                var color = TargetColors[i % TargetColors.Length].ColorWithAlphaComponent(0.55f).CGColor;
                DrawExperimentData(gc, TargetExperiments[i], color, TargetSymbolShape, TargetSymbolSize, drawInactiveSeparately: false);
            }
        }

        void DrawBufferData(CGContext gc)
        {
            if (BufferExperiment == null) return;

            DrawExperimentData(gc, BufferExperiment, StrokeColor, BufferSymbolShape, BufferSymbolSize, drawInactiveSeparately: true);
        }

        void DrawExperimentData(CGContext gc, ExperimentData experiment, CGColor color, SymbolShape shape, float symbolSize, bool drawInactiveSeparately)
        {
            if (experiment == null) return;

            var layer = CGLayer.Create(gc, Frame.Size);
            var bars = new CGPath();
            var activePoints = new List<CGPoint>();
            var inactivePoints = new List<CGPoint>();

            foreach (var inj in IntegratedInjections(experiment))
            {
                var x = GetInjectionNumber(inj);
                var p = GetRelativePosition(x, inj.RawPeakArea.Value);

                if (ShowErrorBars)
                {
                    AddErrorBar(bars, x, inj.RawPeakArea, new CGSize(symbolSize / 2, 0));
                }

                if (experiment == BufferExperiment)
                {
                    bufferPointBoxes.Add(new FeatureBoundingBox(
                        MouseOverFeatureEvent.FeatureType.DataPoint,
                        p,
                        BufferSymbolSize,
                        inj.ID,
                        Frame.Location));

                    if (inj.ID == mouseOverInjectionID)
                    {
                        DrawSymbolsAtPositions(
                            layer,
                            new[] { p },
                            HighlightSize,
                            BufferSymbolShape,
                            fill: true,
                            width: 0,
                            color: IsMouseDown ? ActivatedHighlightColor : HighlightColor,
                            roundedradius: 4);
                    }
                }

                if (!drawInactiveSeparately || inj.Include) activePoints.Add(p);
                else inactivePoints.Add(p);
            }

            layer.Context.SetStrokeColor(color);
            layer.Context.SetFillColor(color);
            layer.Context.SetLineWidth(1);
            layer.Context.AddPath(bars);
            layer.Context.StrokePath();

            DrawSymbolsAtPositions(layer, activePoints.ToArray(), symbolSize, shape, fill: true, width: 1, color: color);
            DrawSymbolsAtPositions(layer, inactivePoints.ToArray(), symbolSize, shape, fill: false, width: 1, color: color);

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawAverageLine(CGContext gc)
        {
            if (!ShowAverageLine) return;

            var line = new CGPath();
            line.MoveToPoint(GetRelativePosition(XAxis.Min, EvaluateAverageLine(XAxis.Min)));
            line.AddLineToPoint(GetRelativePosition(XAxis.Max, EvaluateAverageLine(XAxis.Max)));

            var layer = CGLayer.Create(gc, Frame.Size);
            layer.Context.SetStrokeColor(NSColor.SystemRed.CGColor);
            layer.Context.SetLineWidth(1.5f);
            layer.Context.SetLineDash(0, new nfloat[] { 5, 3 });
            layer.Context.AddPath(line);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawSubtractionModelLine(CGContext gc)
        {
            var points = SubtractionModel?.GetLinePoints(XAxis.Min, XAxis.Max);
            if (points == null || points.Count < 2) return;

            var line = new CGPath();
            line.MoveToPoint(GetRelativePosition(points[0].InjectionNumber, points[0].Heat));

            for (int i = 1; i < points.Count; i++)
                line.AddLineToPoint(GetRelativePosition(points[i].InjectionNumber, points[i].Heat));

            var layer = CGLayer.Create(gc, Frame.Size);
            layer.Context.SetStrokeColor(NSColor.SystemRed.CGColor);
            layer.Context.SetLineWidth(1.5f);
            layer.Context.SetLineDash(0, new nfloat[] { 5, 3 });
            layer.Context.AddPath(line);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawZero(CGContext gc)
        {
            if (YAxis.Min > 0 || YAxis.Max < 0) return;

            var zero = new CGPath();
            zero.MoveToPoint(GetRelativePosition(XAxis.Min, 0));
            zero.AddLineToPoint(GetRelativePosition(XAxis.Max, 0));

            var layer = CGLayer.Create(gc, Frame.Size);
            layer.Context.SetStrokeColor(SecondaryLineColor);
            layer.Context.SetLineWidth(1);
            layer.Context.AddPath(zero);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawGrid(CGContext gc)
        {
            var grid = new CGPath();

            foreach (var t in YAxis.GetValidTicks(false).Item1.Where(v => v != 0))
            {
                var y = GetRelativePosition(0, t / YAxis.ValueFactor).Y;
                grid.MoveToPoint(0, y);
                grid.AddLineToPoint(PlotSize.Width, y);
            }

            foreach (var t in XAxis.GetValidTicks(false).Item1)
            {
                var x = GetRelativePosition(t / XAxis.ValueFactor, 0).X;
                grid.MoveToPoint(x, 0);
                grid.AddLineToPoint(x, PlotSize.Height);
            }

            var layer = CGLayer.Create(gc, Frame.Size);
            layer.Context.SetLineWidth(1);
            layer.Context.SetStrokeColor(TertiaryLineColor);
            layer.Context.SetLineDash(3, new nfloat[] { 10 });
            layer.Context.AddPath(grid);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }

        double EvaluateAverageLine(double injectionNumber)
        {
            return AverageLineSlope * injectionNumber + AverageLineIntercept;
        }

        IEnumerable<ExperimentData> AllExperiments()
        {
            if (BufferExperiment != null) yield return BufferExperiment;

            foreach (var target in TargetExperiments)
            {
                if (target != null) yield return target;
            }
        }

        IEnumerable<InjectionData> IntegratedInjections(ExperimentData experiment)
        {
            return experiment?.Injections?.Where(inj => inj.IsIntegrated) ?? Enumerable.Empty<InjectionData>();
        }

        static double GetInjectionNumber(InjectionData injection)
        {
            return injection.ID + 1;
        }

        public string TooltipForFeature(MouseOverFeatureEvent feature)
        {
            if (feature == null || !feature.IsMouseOverFeature || BufferExperiment == null) return null;
            var inj = BufferExperiment.Injections.FirstOrDefault(i => i.ID == feature.FeatureID);
            if (inj == null) return null;

            var heat = HeatScaleFactor * inj.RawPeakArea;
            return string.Join(Environment.NewLine, new[]
            {
                "Buffer inj #" + (inj.ID + 1),
                "Raw heat: " + heat.ToString("G3") + " " + HeatUnitLabel,
                inj.Include ? "Included" : "Excluded",
            });
        }

        public override MouseOverFeatureEvent CursorFeatureFromPos(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            foreach (var box in bufferPointBoxes)
            {
                if (!box.CursorInBox(cursorpos)) continue;

                if (isclick) mouseDownInjectionID = box.FeatureID;
                else if (ismouseup && mouseDownInjectionID == box.FeatureID)
                {
                    var inj = BufferExperiment?.Injections.FirstOrDefault(i => i.ID == box.FeatureID);
                    inj?.ToggleDataPointActive();
                    BufferPointIncludeChanged?.Invoke(this, EventArgs.Empty);
                    mouseDownInjectionID = -1;
                }

                mouseOverInjectionID = box.FeatureID;
                return MouseOverFeatureEvent.BoundboxFeature(box, cursorpos);
            }

            if (isclick || ismouseup) mouseDownInjectionID = -1;
            mouseOverInjectionID = -1;

            return new MouseOverFeatureEvent();
        }
    }
}
