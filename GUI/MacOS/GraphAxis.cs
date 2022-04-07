using System;
using System.Collections.Generic;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using CoreText;
using Foundation;
using Utilities;

namespace AnalysisITC
{
    public class GraphAxis
    {
        public static CTFont TickFont { get; set; } = new CTFont("Arial", 14);
        public static CTFont TitleFont { get; set; } = new CTFont("Arial", 16);

        CGGraph cggraph;
        CAGraph cagraph;

        public AxisPosition Position;
        public bool IsHorizontal => Position == AxisPosition.Bottom || Position == AxisPosition.Top;

        public string LegendTitle { get; set; } = "";

        public float ActualMin { get; private set; } = 0;
        public float Min
        {
            get { if (false) return TickScale.NiceMin; else return ActualMin; }
            set { ActualMin = value; TickScale.SetMinMaxPoints(ActualMin, ActualMax); }
        }

        public float ActualMax { get; private set; } = 1;
        public float Max
        {
            get { if (false) return TickScale.NiceMax; else return ActualMax; }
            set { ActualMax = value; TickScale.SetMinMaxPoints(ActualMin, ActualMax); }
        }

        float buffer = 0.035f;
        public float Buffer
        {
            get => buffer;
            set
            {
                var _buffer = buffer;

                buffer = value;

                UpdateAutoScale(_buffer);
            }
        }
        public Utilities.NiceScale TickScale;

        public bool UseNiceAxis { get; set; } = false;

        public int DecimalPoints { get; set; } = 1;
        double valueFactor = 1;
        public double ValueFactor { get => valueFactor; set { valueFactor = value; SetTickScale(); } }
        string formatter => "#####0." + new string('0', DecimalPoints);


        TextAlignment HorizontalTickLabelAlignment => Position switch
        {
            AxisPosition.Left => TextAlignment.Right,
            AxisPosition.Right => TextAlignment.Left,
            _ => TextAlignment.Center,
        };

        TextAlignment VerticalTickLabelAlignment => Position switch
        {
            AxisPosition.Top => TextAlignment.Bottom,
            AxisPosition.Bottom => TextAlignment.Top,
            _ => TextAlignment.Center,
        };

        nfloat TickLineLength = 5;

        CGSize TickLine => Position switch
        {
            AxisPosition.Bottom => new CGSize(0, TickLineLength),
            AxisPosition.Left => new CGSize(TickLineLength, 0),
            AxisPosition.Right => new CGSize(-TickLineLength, 0),
            _ => new CGSize(0, -TickLineLength),
        };
        CGSize FrameOffset => new CGSize(cggraph.Origin);//new CGSize(cggraph.Origin.X, cggraph.Origin.Y);
        nfloat labeloffset = 7;
        CGSize LabelOffset => Position switch
        {
            AxisPosition.Bottom => new CGSize(0, -labeloffset),
            AxisPosition.Left => new CGSize(-labeloffset, 0),
            AxisPosition.Right => new CGSize(labeloffset, 0),
            _ => new CGSize(0, labeloffset),
        };
        CGSize TickLabelSize;

        public GraphAxis(CAGraph graph, double min, double max, AxisPosition position = AxisPosition.Unknown)
        {
            this.cagraph = graph;
            Initialize(min, max, position);
        }

        public GraphAxis(CGGraph graph, double min, double max, AxisPosition position = AxisPosition.Unknown)
        {
            this.cggraph = graph;

            Initialize(min, max, position);
        }

        void Initialize(double min, double max, AxisPosition position)
        {
            this.ActualMin = (float)min;
            this.ActualMax = (float)max;

            SetTickScale();

            Position = position;
        }

        public static GraphAxis WithBuffer(object obj, double min, double max, double buffer = 0.035, AxisPosition position = AxisPosition.Unknown) => obj switch
        {
            CGGraph graph => new GraphAxis(graph, min - (max - min) * buffer, max + (max - min) * buffer, position),
            CAGraph graph => new GraphAxis(graph, min - (max - min) * buffer, max + (max - min) * buffer, position),
            _ => null,
        };

        public void SetWithBuffer(double min, double max, double buffer = 0.035)
        {
            var delta = max - min;

            ActualMin = (float)(min - delta * buffer);
            ActualMax = (float)(max + delta * buffer);

            SetTickScale();
        }

        void UpdateAutoScale(float old)
        {
            var delta = ActualMax - ActualMin;
            var old_delta = delta / (2 * old + 1);

            var old_min = ActualMin + old_delta * old;
            var old_max = ActualMax - old_delta * old;

            this.ActualMin = old_min - old_delta * buffer;
            this.ActualMax = old_max + old_delta * buffer;

            SetTickScale();
        }

        void SetTickScale()
        {
            TickScale = new Utilities.NiceScale(this.ActualMin, this.ActualMax, ValueFactor);
        }

        public void Draw(CGContext gc)
        {
            var tickvalues = TickScale.Ticks();
            tickvalues.RemoveAll(v => v / ValueFactor < Min || v / ValueFactor > Max);

            var origin = cggraph.Frame.Location;

            bool horizontal = Position == AxisPosition.Bottom || Position == AxisPosition.Top;
            bool alt = Position == AxisPosition.Right || Position == AxisPosition.Top;
            var drawticks = new List<CGPoint>();
            var allticks = new List<CGPoint>();

            foreach (var _tick in tickvalues)
            {
                var tick = _tick / ValueFactor;

                var x = horizontal ? tick : !alt ? cggraph.XAxis.Min : cggraph.XAxis.Max;
                var y = !horizontal ? tick : !alt ? cggraph.YAxis.Min : cggraph.YAxis.Max;

                var p = cggraph.GetRelativePosition(x, y, this);

                if (tick != Min && tick != Max) drawticks.Add(p);
                allticks.Add(p);

            }

            CGLayer layer = CGLayer.Create(gc, cggraph.Frame.Size);
            CGPath ticklines = new();

            foreach (var tick in drawticks)
            {
                ticklines.MoveToPoint(tick);
                ticklines.AddLineToPoint(CGPoint.Add(tick, TickLine));
            }

            layer.Context.SetStrokeColor(cggraph.StrokeColor);
            layer.Context.SetFillColor(cggraph.StrokeColor);
            layer.Context.AddPath(ticklines);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, origin);

            DrawTicks(gc, allticks, tickvalues);

            DrawTitle(gc);
        }

        void DrawTicks(CGContext gc, List<CGPoint> ticks, List<double> tickvalues)
        {
            CGLayer layer = CGLayer.Create(gc, cggraph.View.Frame.Size);
            layer.Context.SetStrokeColor(cggraph.StrokeColor);
            layer.Context.SetFillColor(cggraph.StrokeColor);

            var maxsize = new CGSize(0, 0);

            for (int i = 0; i < ticks.Count; i++)
            {
                CGPoint tick = ticks[i];
                var point = tick + FrameOffset + LabelOffset;

                //var _size = cggraph.DrawString(layer, (tickvalues[i] * ValueFactor).ToString(formatter), point, TickFont, null, HorizontalTickLabelAlignment, VerticalTickLabelAlignment, null);
                var _size = cggraph.DrawString(layer, (tickvalues[i]).ToString(formatter), point, TickFont, null, HorizontalTickLabelAlignment, VerticalTickLabelAlignment, null);

                if (_size.Width > maxsize.Width) maxsize.Width = _size.Width;
                if (_size.Height > maxsize.Height) maxsize.Height = _size.Height;
            }

            TickLabelSize = maxsize;

            gc.DrawLayer(layer, new CGPoint(0, 0));
        }

        void DrawTitle(CGContext gc)
        {
            CGLayer layer = CGLayer.Create(gc, cggraph.View.Frame.Size);
            layer.Context.SetStrokeColor(cggraph.StrokeColor);
            layer.Context.SetFillColor(cggraph.StrokeColor);

            var ori = cggraph.Origin;
            var frame = cggraph.Frame;

            var point = Position switch
            {
                AxisPosition.Bottom => new CGPoint(ori.X + frame.Width / 2, ori.Y - labeloffset - TickLabelSize.Height - 5),
                AxisPosition.Top => new CGPoint(ori.X + frame.Width / 2, ori.Y + frame.Height + labeloffset + TickLabelSize.Height + 5),
                AxisPosition.Left => new CGPoint(ori.X - labeloffset - TickLabelSize.Width - 10, ori.Y + frame.Height / 2),
                AxisPosition.Right => new CGPoint(ori.X + frame.Width + labeloffset + TickLabelSize.Width + 5, ori.Y + frame.Height / 2),
                _ => cggraph.Center
            };

            var rot = Position switch
            {
                AxisPosition.Left => CGGraph.PiHalf,
                AxisPosition.Right => CGGraph.PiHalf,
                _ => 0
            };

            var aln = Position switch
            {
                AxisPosition.Bottom => TextAlignment.Top,
                AxisPosition.Top => TextAlignment.Bottom,
                AxisPosition.Left => TextAlignment.Bottom,
                AxisPosition.Right => TextAlignment.Top,
                _ => TextAlignment.Center,
            };

            cggraph.DrawString(layer, LegendTitle, point, TickFont, null, TextAlignment.Center, aln, null, rot);

            gc.DrawLayer(layer, new CGPoint(0, 0));
        }
    }

    public enum AxisPosition
    {
        Unknown,
        Top,
        Bottom,
        Left,
        Right
    }
}
