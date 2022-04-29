﻿using System;
using System.Collections.Generic;
using System.Linq;
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

        public AxisPosition Position;
        public bool IsHorizontal => Position == AxisPosition.Bottom || Position == AxisPosition.Top;
        public bool MirrorTicks { get; set; } = false;

        public string LegendTitle { get; set; } = "";

        public float ActualMin { get; private set; } = 0;
        public float Min
        {
            get { if (false) return (float)TickScale.NiceMin; else return ActualMin; }
            set { ActualMin = value; TickScale.SetMinMaxPoints(ActualMin, ActualMax); }
        }

        public float ActualMax { get; private set; } = 1;
        public float Max
        {
            get { if (false) return (float)TickScale.NiceMax; else return ActualMax; }
            set { ActualMax = value; TickScale.SetMinMaxPoints(ActualMin, ActualMax); }
        }

        double buffer = 0.035f;
        public double Buffer
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
        public bool HideUnwantedTicks { get; set; } = true;

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

        nfloat TickLineLength = 6;

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
        nfloat titleoffset = 7;
        CGSize TitleOffset => Position switch
        {
            AxisPosition.Bottom => new CGSize(0, -titleoffset),
            AxisPosition.Left => new CGSize(-titleoffset, 0),
            AxisPosition.Right => new CGSize(titleoffset, 0),
            _ => new CGSize(0, titleoffset),
        };

        public GraphAxis(CAGraph graph, double min, double max, AxisPosition position = AxisPosition.Unknown)
        {
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
            CGGraph graph => new GraphAxis(graph, min - (max - min) * buffer, max + (max - min) * buffer, position) { Buffer = buffer },
            CAGraph graph => new GraphAxis(graph, min - (max - min) * buffer, max + (max - min) * buffer, position) { Buffer = buffer },
            _ => null,
        };

        public void Set(double min, double max)
        {
            ActualMin = (float)min;
            ActualMax = (float)max;

            SetTickScale();
        }

        public void SetWithBuffer(double min, double max, double buffer = 0.035)
        {
            var delta = max - min;

            ActualMin = (float)(min - delta * buffer);
            ActualMax = (float)(max + delta * buffer);

            SetTickScale();
        }

        void UpdateAutoScale(double old)
        {
            var delta = ActualMax - ActualMin;
            var old_delta = delta / (2 * old + 1);

            var old_min = ActualMin + old_delta * old;
            var old_max = ActualMax - old_delta * old;

            this.ActualMin = (float)(old_min - old_delta * buffer);
            this.ActualMax = (float)(old_max + old_delta * buffer);

            SetTickScale();
        }

        void SetTickScale()
        {
            TickScale = new Utilities.NiceScale(this.ActualMin, this.ActualMax, ValueFactor);

            SetDecimalDigits(TickScale.Ticks());
        }

        void SetDecimalDigits(List<double> ticks)
        {
            int maxdigits = 0;

            foreach (var tick in ticks)
            {
                string s = ((float)tick).ToString();
                if (!s.Contains('.')) continue;
                string result = s.Substring(s.LastIndexOf('.'));
                int num = result.Length - 1;
                maxdigits = num > maxdigits ? num : maxdigits;
            }

            DecimalPoints = maxdigits;
        }

        public void SetMaxTicks(int number)
        {
            TickScale.SetMaxTicks(number);

            SetDecimalDigits(TickScale.Ticks());
        }

        public (List<double>, List<double>) GetValidTicks(bool includeborderticks = true)
        {
            var tickvalues = TickScale.Ticks();

            if (HideUnwantedTicks)
            {
                bool hideabovezero = tickvalues.Count(v => v < 0) > tickvalues.Count(v => v > 0);

                if (hideabovezero) tickvalues.RemoveAll(v => v > 0);
                else tickvalues.RemoveAll(v => v < 0);
            }

            var minortickvalues = tickvalues.Select(t => t + 0.5 * (tickvalues[0] - tickvalues[1])).ToList();
            tickvalues.RemoveAll(v => v / ValueFactor < Min || v / ValueFactor > Max);
            if (!includeborderticks) tickvalues.RemoveAll(v => v == Min || v == Max);

            return (tickvalues, minortickvalues);
        }

        public void Draw(CGContext gc)
        {
            var ticks = GetValidTicks(false);

            List<double> tickvalues = ticks.Item1;
            List<double> minortickvalues = ticks.Item2;

            var origin = cggraph.Frame.Location;

            bool horizontal = Position == AxisPosition.Bottom || Position == AxisPosition.Top;
            bool alt = Position == AxisPosition.Right || Position == AxisPosition.Top;

            var drawticks = new List<CGPoint>();
            var halfticks = new List<CGPoint>();
            var allticks = new List<CGPoint>();

            GetTickScreenPositions(tickvalues, horizontal, alt, drawticks, allticks);
            GetTickScreenPositions(minortickvalues, horizontal, alt, halfticks);

            CGLayer layer = CGLayer.Create(gc, cggraph.Frame.Size);
            CGPath ticklines = new();

            AddTickLines(drawticks, halfticks, ticklines);

            if (MirrorTicks)
            {
                drawticks = new List<CGPoint>();
                halfticks = new List<CGPoint>();

                GetTickScreenPositions(tickvalues, horizontal, !alt, drawticks);
                GetTickScreenPositions(minortickvalues, horizontal, !alt, halfticks);

                AddTickLines(drawticks, halfticks, ticklines, true);
            }

            layer.Context.SetStrokeColor(cggraph.StrokeColor);
            layer.Context.SetFillColor(cggraph.StrokeColor);
            layer.Context.AddPath(ticklines);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, origin);

            DrawTickLabels(gc, allticks, tickvalues);

            DrawAxisTitle(gc);
        }

        private void AddTickLines(List<CGPoint> drawticks, List<CGPoint> halfticks, CGPath ticklines, bool mirror = false)
        {
            var tickline = TickLine;

            if (mirror) tickline = tickline.ScaleBy(-1);

            foreach (var tick in drawticks)
            {
                ticklines.MoveToPoint(tick);
                ticklines.AddLineToPoint(CGPoint.Add(tick, tickline));
            }
            foreach (var tick in halfticks)
            {
                ticklines.MoveToPoint(tick);
                ticklines.AddLineToPoint(CGPoint.Add(tick, tickline.ScaleBy(0.5f)));
            }
        }

        private void GetTickScreenPositions(List<double> tickvalues, bool ishorizontal, bool isalt, List<CGPoint> drawticks, List<CGPoint> allticks = null)
        {
            if (allticks == null) allticks = new List<CGPoint>();

            foreach (var _tick in tickvalues)
            {
                var tick = _tick / ValueFactor;

                var x = ishorizontal ? tick : !isalt ? cggraph.XAxis.Min : cggraph.XAxis.Max;
                var y = !ishorizontal ? tick : !isalt ? cggraph.YAxis.Min : cggraph.YAxis.Max;

                var p = cggraph.GetRelativePosition(x, y, this);

                if (tick != Min && tick != Max) drawticks.Add(p);
                allticks.Add(p);
            }
        }

        void DrawTickLabels(CGContext gc, List<CGPoint> ticks, List<double> tickvalues)
        {
            CGLayer layer = CGLayer.Create(gc, cggraph.View.Frame.Size);
            layer.Context.SetStrokeColor(cggraph.StrokeColor);
            layer.Context.SetFillColor(cggraph.StrokeColor);

            var maxsize = new CGSize(0, 0);

            for (int i = 0; i < ticks.Count; i++)
            {
                CGPoint tick = ticks[i];
                var point = tick + FrameOffset + LabelOffset;

                var _size = cggraph.DrawString(layer, (tickvalues[i]).ToString(formatter), point, TickFont, null, HorizontalTickLabelAlignment, VerticalTickLabelAlignment, null);

                if (_size.Width > maxsize.Width) maxsize.Width = _size.Width;
                if (_size.Height > maxsize.Height) maxsize.Height = _size.Height;
            }

            TickLabelSize = maxsize;

            gc.DrawLayer(layer, new CGPoint(0, 0));
        }

        void DrawAxisTitle(CGContext gc)
        {
            CGLayer layer = CGLayer.Create(gc, cggraph.View.Frame.Size);
            layer.Context.SetStrokeColor(cggraph.StrokeColor);
            layer.Context.SetFillColor(cggraph.StrokeColor);

            var ori = cggraph.Origin;
            var frame = cggraph.Frame;

            var point = Position switch
            {
                AxisPosition.Bottom => new CGPoint(ori.X + frame.Width / 2, ori.Y - TickLabelSize.Height),
                AxisPosition.Top => new CGPoint(ori.X + frame.Width / 2, ori.Y + frame.Height + TickLabelSize.Height),
                AxisPosition.Left => new CGPoint(ori.X - TickLabelSize.Width, ori.Y + frame.Height / 2),
                AxisPosition.Right => new CGPoint(ori.X + frame.Width + TickLabelSize.Width, ori.Y + frame.Height / 2),
                _ => cggraph.Center
            };

            point += LabelOffset + TitleOffset;

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

            var titlesize = cggraph.DrawString(layer, LegendTitle, point, TitleFont, null, TextAlignment.Center, aln, null, rot);

            gc.DrawLayer(layer, new CGPoint(0, 0));
        }

        public nfloat EstimateLabelMargin()
        {
            var tickvalues = TickScale.Ticks();
            tickvalues.RemoveAll(v => v / ValueFactor < Min || v / ValueFactor > Max);

            bool horizontal = Position == AxisPosition.Bottom || Position == AxisPosition.Top;

            var ticklabelsize = new CGSize(0, 0);

            for (int i = 0; i < tickvalues.Count; i++)
            {
                var _size = CGGraph.MeasureString((tickvalues[i]).ToString(formatter), TickFont, null);

                if (_size.Width > ticklabelsize.Width) ticklabelsize.Width = _size.Width;
                if (_size.Height > ticklabelsize.Height) ticklabelsize.Height = _size.Height;
            }

            var titlesize = CGGraph.MeasureString(LegendTitle, TitleFont, null, Position, false);

            var margin = LabelOffset + TitleOffset;
            margin = margin.AbsoluteValueSize();

            if (horizontal) return margin.Height + ticklabelsize.Height + titlesize.Height;
            else return margin.Width + ticklabelsize.Width + titlesize.Height;
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
