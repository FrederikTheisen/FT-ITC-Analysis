using System;

namespace AnalysisITC
{
    public static class FWEMath
    {
        public static FloatWithError Log(FloatWithError number)
        {
            return new FloatWithError(Math.Log(number.Value), number.SD / number.Value);
        }

        public static FloatWithError Exp(FloatWithError number)
        {
            var f = Math.Abs(Math.Exp(number.Value));
            var sd = Math.Abs(number.SD);

            return new FloatWithError(f, f * sd);
        }

        public static double RoundApproximate(double dbl)
        {
            double margin = 8e-8;
            double fraction = dbl;
            double value = Math.Truncate(fraction);
            fraction = fraction - value;
            if (fraction == 0) return dbl;

            double tolerance = margin * dbl;
            // Determine whether this is a midpoint value.
            if ((fraction >= .5 - tolerance) & (fraction <= .5 + tolerance)) return (value + 1);

            // Any remaining fractional value greater than .5 is not a midpoint value.
            if (fraction > .5) return (value + 1);
            else if (fraction < -0.5) return (value - 1);
            else return value;
        }

        public static double RoundApproximate(double dbl, int digits = 0, double margin = 8e-7, MidpointRounding mode = MidpointRounding.AwayFromZero)
        {
            double fraction = dbl * Math.Pow(10, digits);
            double value = Math.Truncate(fraction);
            fraction = fraction - value;
            if (fraction == 0) return dbl;

            double tolerance = margin * dbl;
            // Determine whether this is a midpoint value.
            if ((fraction >= .5 - tolerance) & (fraction <= .5 + tolerance))
            {
                if (mode == MidpointRounding.AwayFromZero) return (value + 1) / Math.Pow(10, digits);
                else
                   if (value % 2 != 0)
                    return (value + 1) / Math.Pow(10, digits);
                else
                    return value / Math.Pow(10, digits);
            }
            // Any remaining fractional value greater than .5 is not a midpoint value.
            if (fraction > .5) return (value + 1) / Math.Pow(10, digits);
            else return value / Math.Pow(10, digits);
        }
    }

}
