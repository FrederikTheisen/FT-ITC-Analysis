﻿using System;
namespace Utilities
{
    public class NiceScale
    {
        private double minPoint;
        private double maxPoint;
        private double maxTicks = 10;
        private double tickSpacing;
        private double range;
        private float niceMin;
        private float niceMax;

        public float NiceMin { get => niceMin; }
        public float NiceMax { get => niceMax; }

        /**
         * Instantiates a new instance of the NiceScale class.
         *
         * @param min the minimum data point on the axis
         * @param max the maximum data point on the axis
         */
        public NiceScale(double min, double max)
        {
            this.minPoint = min;
            this.maxPoint = max;
            Calculate();
        }

        /**
         * Calculate and update values for tick spacing and nice
         * minimum and maximum data points on the axis.
         */
        private void Calculate()
        {
            this.range = NiceNum(maxPoint - minPoint, false);
            this.tickSpacing = NiceNum(range / (maxTicks - 1), true);
            this.niceMin =
              (float)(Math.Floor(minPoint / tickSpacing) * tickSpacing);
            this.niceMax =
              (float)(Math.Ceiling(maxPoint / tickSpacing) * tickSpacing);
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
}