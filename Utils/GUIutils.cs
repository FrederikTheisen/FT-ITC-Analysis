using System;
using AppKit;
using System.Collections.Generic;
using CoreGraphics;
using AnalysisITC;
using System.Linq;

namespace Utilities
{
    public class NiceScale
    {
        private readonly double factor = 1;
        private double minPoint;
        private double maxPoint;
        private double maxTicks = 10;
        private double tickSpacing;
        private double range;
        private double niceMin;
        private double niceMax;

        public double NiceMin { get => niceMin; }
        public double NiceMax { get => niceMax; }
        public double TickSpacing { get => tickSpacing; }

        public double UITickSpacing => tickSpacing;
        public double UINiceMin => niceMin;
        public double UINiceMax => niceMax;

        /**
         * Instantiates a new instance of the NiceScale class.
         *
         * @param min the minimum data point on the axis
         * @param max the maximum data point on the axis
         */
        public NiceScale(double min, double max, double factor = 1)
        {
            this.minPoint = min * factor;
            this.maxPoint = max * factor;
            this.factor = factor;
            Calculate();
        }

        /**
         * Calculate and update values for tick spacing and nice
         * minimum and maximum data points on the axis.
         */
        private void Calculate()
        {
            this.range = NiceNum((maxPoint - minPoint), false);
            this.tickSpacing = NiceNum(range / (maxTicks + 1), true);
            this.niceMin = (Math.Floor(minPoint / TickSpacing) * TickSpacing);
            this.niceMax = (Math.Ceiling(maxPoint / TickSpacing) * TickSpacing);
        }

        /**
         * Returns a "nice" number approximately equal to range Rounds
         * the number if round = true Takes the ceiling if round = false.
         *
         * @param range the data range
         * @param round whether to round the result
         * @return a "nice" number to be used for the data range
         */
        private double NiceNum(double range, bool round)
        {
            double exponent; /** exponent of range */
            double fraction; /** fractional part of range */
            double niceFraction; /** nice, rounded fraction */

            exponent = Math.Floor(Math.Log10(range));
            fraction = range / Math.Pow(10, exponent);

            if (round)
            {
                if (fraction < 1.5)
                    niceFraction = 1;
                else if (fraction < 3)
                    niceFraction = 2;
                else if (fraction < 7)
                    niceFraction = 5;
                else
                    niceFraction = 10;
            }
            else
            {
                if (fraction <= 1)
                    niceFraction = 1;
                else if (fraction <= 2)
                    niceFraction = 2;
                else if (fraction <= 5)
                    niceFraction = 5;
                else
                    niceFraction = 10;
            }

            return niceFraction * Math.Pow(10, exponent);
        }

        public List<double> Ticks()
        {
            var ticks = new List<double>();

            for (double t = UINiceMin; t <= UINiceMax; t += UITickSpacing)
            {
                ticks.Add(Math.Round(t, 4));
            }

            return ticks;
        }

        /**
         * Sets the minimum and maximum data points for the axis.
         *
         * @param minPoint the minimum data point on the axis
         * @param maxPoint the maximum data point on the axis
         */
        public void SetMinMaxPoints(double minPoint, double maxPoint)
        {
            this.minPoint = minPoint;
            this.maxPoint = maxPoint;
            Calculate();
        }

        /**
         * Sets maximum number of tick marks we're comfortable with
         *
         * @param maxTicks the maximum number of tick marks for the axis
         */
        public void SetMaxTicks(double maxTicks)
        {
            this.maxTicks = maxTicks;
            Calculate();
        }
    }

    public class NSSliderEvent
    {
        public bool DidStartDragging;
        public bool IsDragging;
        public bool DidEndDragging;

        public NSSliderEvent()
        {
            var _event = NSApplication.SharedApplication.CurrentEvent;

            DidStartDragging = _event.Type == NSEventType.LeftMouseDown;
            IsDragging = _event.Type == NSEventType.LeftMouseDragged;
            DidEndDragging = _event.Type == NSEventType.LeftMouseUp;
        }
    }

    public class Increment
    {
        public static int PostIncrement(int index, int increment, out int i)
        {
            i = index + increment;

            return index;
        }
    }

    public class FeatureBoundingBox
    {
        public MouseOverFeatureEvent.FeatureType Type { get; private set; } = MouseOverFeatureEvent.FeatureType.Unknown;
        public CGRect Rect { get; set; } = new CGRect(0, 0, 0, 0);
        public CGPoint offset = new CGPoint();
        public int FeatureID { get; set; } = -1;
        public int SubID { get; set; } = -1;
        public double FeatureReferenceValue { get; set; } = 0;

        public FeatureBoundingBox(MouseOverFeatureEvent.FeatureType type, CGRect rect, int id, CGPoint boxoffset, int sid = -1)
        {
            Type = type;
            Rect = rect;
            FeatureID = id;
            SubID = sid;
            offset = boxoffset;
        }

        public bool Contains(CGPoint point) => Rect.Contains(point);

        public bool CursorInBox(CGPoint cursorpos) => Contains(cursorpos.Subtract(offset));

        public bool ProximityX(CGPoint cursorpos, float pixels_to_x_factor)
        {
            cursorpos = cursorpos.Subtract(offset);

            var dist = Rect.Left > cursorpos.X ? Rect.Left - cursorpos.X : cursorpos.X - Rect.Right;
            var tdist = dist * pixels_to_x_factor;

            switch (Type)
            {
                case MouseOverFeatureEvent.FeatureType.IntegrationRangeMarker: return tdist < 10;
                default: return false;
            }
        }
    }

    public class MouseOverFeatureEvent
    {
        public FeatureType Type { get; private set; } = FeatureType.Unknown;

        public int FeatureID { get; set; } = -1;
        public int SubID => Box.SubID;

        public FeatureBoundingBox Box { get; private set; }

        public CGPoint ClickCursorPosition { get; set; } = new CGPoint();
        public double FeatureReferenceValue { get; set; }
        public CGPoint CursorAxisClickPosition { get; set; }

        readonly List<string> tooltiplines = new List<string>();

        public string ToolTip
        {
            get
            {
                if (tooltiplines.Count == 0) return null;
                else return string.Join(Environment.NewLine, tooltiplines);
            }
        }

        public bool IsMouseOverFeature => FeatureID != -1;

        public CGRect GetZoomRegion(CGGraph graph, CGPoint cursorpos)
        {
            var curr = MouseDragZoom(graph, cursorpos);

            return CGRect.FromLTRB(
                (float)Math.Min(CursorAxisClickPosition.X, curr.CursorAxisClickPosition.X),
                (float)Math.Max(CursorAxisClickPosition.Y, curr.CursorAxisClickPosition.Y),
                (float)Math.Max(CursorAxisClickPosition.X, curr.CursorAxisClickPosition.X),
                (float)Math.Min(CursorAxisClickPosition.Y, curr.CursorAxisClickPosition.Y));
        }

        public CGRect GetZoomRect(CGGraph graph, CGPoint cursorpos)
        {
            return CGRect.FromLTRB(
                (float)Math.Min(ClickCursorPosition.X, cursorpos.X),
                (float)Math.Max(ClickCursorPosition.Y, cursorpos.Y),
                (float)Math.Max(ClickCursorPosition.X, cursorpos.X),
                (float)Math.Min(ClickCursorPosition.Y, cursorpos.Y));
        }

        public MouseOverFeatureEvent(FeatureType type = FeatureType.Unknown)
        {
            Type = type;
        }

        public MouseOverFeatureEvent(FeatureBoundingBox box)
        {
            Box = box;
            Type = box.Type;
            FeatureID = box.FeatureID;
            FeatureReferenceValue = box.FeatureReferenceValue;
        }

        public MouseOverFeatureEvent(AnalysisITC.InjectionData inj)
        {
            Type = FeatureType.IntegratedInjectionPoint;
            FeatureID = inj.ID;

            tooltiplines.Add("Inj #" + (inj.ID + 1));
            //tooltiplines.Add("Time: " + inj.Time.ToString("F1") + "s");
            tooltiplines.Add("Ratio: " + inj.Ratio.ToString("F2"));
            tooltiplines.Add("Area: " + (inj.OffsetEnthalpy/1000).ToString("F1") + " kJ/mol");
            tooltiplines.Add("Temperature: " + inj.Temperature.ToString("F2") + " °C");
            if (inj.Experiment.Solution != null) tooltiplines.Add("Residual: " + ((inj.Enthalpy - inj.Experiment.Model.EvaluateEnthalpy(inj.ID, true))/1000).ToString("G2") + " kJ/mol");

            int longest = tooltiplines.Max(l => l.Length);

            string timestring = inj.Time.ToString("F1") + "s";

            int spaces = 2 * (longest - timestring.Length) - tooltiplines[0].Length;
            if (spaces < 1) spaces = 1;

            tooltiplines[0] += new string(' ', spaces) + timestring;
        }

        public static MouseOverFeatureEvent BoundboxFeature(FeatureBoundingBox box, CGPoint cursorpos)
        {
            return new MouseOverFeatureEvent(box)
            {
                ClickCursorPosition = cursorpos,
            };
        }

        public static MouseOverFeatureEvent MouseDragZoom(CGGraph graph, CGPoint cursorpositioninview)
        {
            var relativepositioninframe = CGExtensions.RelativePositionInFrame(graph.Frame, cursorpositioninview);

            var inframepositon = new CGPoint(Math.Min(graph.Frame.Right, Math.Max(graph.Frame.Left, cursorpositioninview.X)), Math.Min(graph.Frame.Bottom, Math.Max(graph.Frame.Top, cursorpositioninview.Y)));

            var feature = new MouseOverFeatureEvent(FeatureType.DragZoom)
            {
                ClickCursorPosition = inframepositon,
                CursorAxisClickPosition = new CGPoint(graph.XAxis.GetValueFromRelativePosition(relativepositioninframe.X), graph.YAxis.GetValueFromRelativePosition(relativepositioninframe.Y))
            };

            return feature;
        }

        public enum FeatureType
        {
            Unknown,
            IntegratedInjectionPoint,
            IntegrationRangeMarker,
            BaselineSplinePoint,
            BaselineSplineHandle,
            DragZoom
        }
    }

    public static class CGExtensions
    {
        public static CGPoint RelativePositionInFrame(CGRect frame, CGPoint position)
        {
            var offsetpos = position.Subtract(frame.Location);

            return new CGPoint(offsetpos.X / frame.Width, offsetpos.Y / frame.Height);
        }

        public static CGPoint Subtract(this CGPoint p1, CGPoint p2)
        {
            return new CGPoint(p1.X - p2.X, p1.Y - p2.Y);
        }
    }

    public enum TextAlignment
    {
        Left,
        Center,
        Right,
        Top,
        Bottom
    }
}
