using System;

namespace AnalysisITC
{
    public static class FWEMath
    {
        const double ln10 = 2.302585093;

        /// <summary>
        /// ln(number)
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        public static FloatWithError Log(FloatWithError number)
        {
            return new FloatWithError(Math.Log(number.Value), number.SD / number.Value);
        }

        /// <summary>
        /// log10(number)
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        public static FloatWithError Log10(FloatWithError number)
        {
            return new FloatWithError(Math.Log10(number.Value), number.SD / (ln10 * number.Value));
        }

        /// <summary>
        /// e^number
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        public static FloatWithError Exp(FloatWithError number)
        {
            var f = Math.Abs(Math.Exp(number.Value));
            var sd = Math.Abs(number.SD);

            return new FloatWithError(f, f * sd);
        }

        /// <summary>
        /// a^number
        /// </summary>
        /// <param name="a"></param>
        /// <param name="number"></param>
        /// <returns></returns>
        public static FloatWithError Pow(double a, FloatWithError number)
        {
            var value = Math.Pow(a, number.Value);
            var sd = Math.Log(a) * value * number.SD;

            return new FloatWithError(value, sd);
        }

        public static FloatWithError Average(FloatWithError a, FloatWithError b)
        {
            var value = 0.5 * (a.Value + b.Value);

            // SD of average of two independent values
            var sd = 0.5 * Math.Sqrt(a.SD * a.SD + b.SD * b.SD);

            return new FloatWithError(value, sd);
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
