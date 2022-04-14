using System;
using AppKit;
using System.Collections.Generic;

namespace Utilities
{
    public class NiceScale
    {
        private float factor = 1;
        private double minPoint;
        private double maxPoint;
        private double maxTicks = 10;
        private double tickSpacing;
        private double range;
        private float niceMin;
        private float niceMax;

        public float NiceMin { get => niceMin; }
        public float NiceMax { get => niceMax; }
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
            this.factor = (float)factor;
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
            this.niceMin =
              (float)(Math.Floor(minPoint / TickSpacing) * TickSpacing);
            this.niceMax =
              (float)(Math.Ceiling(maxPoint / TickSpacing) * TickSpacing);
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
                ticks.Add(t);
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

    public class MouseOverFeatureEvent
    {
        public FeatureType Type { get; private set; } = FeatureType.Unknown;

        public int FeatureID { get; set; } = -1;

        List<string> tooltiplines = new List<string>();

        public string ToolTip
        {
            get
            {
                if (tooltiplines.Count == 0) return null;
                else return string.Join(Environment.NewLine, tooltiplines);
            }
        }

        public bool IsMouseOverFeature => FeatureID != -1;

        public MouseOverFeatureEvent(FeatureType type = FeatureType.Unknown)
        {
            Type = type;
        }

        public MouseOverFeatureEvent(AnalysisITC.InjectionData inj)
        {
            Type = FeatureType.IntegratedInjectionPoint;
            FeatureID = inj.ID;

            tooltiplines.Add("Inj #" + inj.ID);
            tooltiplines.Add("Time: " + inj.Time.ToString("F1") + "s");
            tooltiplines.Add("Ratio: " + inj.Ratio.ToString("F2"));
            tooltiplines.Add("Area: " + (inj.OffsetEnthalpy/1000).ToString("F1") + " kJ/mol");
            if (inj.Experiment.Solution != null) tooltiplines.Add("Residual: " + (inj.Enthalpy - inj.Experiment.Solution.Evaluate(inj.ID, false)).ToString("G2") + " kJ/mol");
        }

        public enum FeatureType
        {
            Unknown,
            IntegratedInjectionPoint,
            IntegrationRangeMarker,
            BaselineSplinePoint,
            BaselineSplineHandle
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
