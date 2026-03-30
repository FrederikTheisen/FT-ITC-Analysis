using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    public static class FWEMath
    {
        const double ln10 = 2.302585093;

        #region Math methods

        /// <summary>
        /// ln(number)
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        public static FloatWithError Log(FloatWithError number)
        {
            var fwe = new FloatWithError(Math.Log(number.Value), number.SD / number.Value);
            fwe.SetConfidenceInterval(Math.Log(number.Lower), Math.Log(number.Upper));
            return fwe;
        }

        /// <summary>
        /// log10(number)
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        public static FloatWithError Log10(FloatWithError number)
        {
            var fwe = new FloatWithError(Math.Log10(number.Value), number.SD / (ln10 * number.Value));
            fwe.SetConfidenceInterval(Math.Log10(number.Lower), Math.Log10(number.Upper));

            return fwe;
        }

        /// <summary>
        /// e^number
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        public static FloatWithError Exp(FloatWithError number)
        {
            var f = Math.Exp(number.Value);
            var sd = Math.Abs(number.SD);

            var fwe = new FloatWithError(f, f * sd);

            fwe.SetConfidenceInterval(Math.Exp(number.Lower), Math.Exp(number.Upper));

            return fwe;
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

            var fwe = new FloatWithError(value, sd);
            fwe.SetConfidenceInterval(Math.Pow(a, number.Lower), Math.Pow(a, number.Upper));

            return fwe;
        }

        #endregion

        #region divide and multiply

        private static double Quad(double a, double b)
        {
            return Math.Sqrt(a * a + b * b);
        }

        private static bool CrossesZero(double lower, double upper)
        {
            return lower <= 0.0 && upper >= 0.0;
        }

        private static void GetMagnitudeInterval(
            double value, double lowerCI, double upperCI,
            out double magValue, out double magLower, out double magUpper)
        {
            if (value == 0.0)
                throw new InvalidOperationException("Central value cannot be zero.");

            if (CrossesZero(lowerCI, upperCI))
                throw new InvalidOperationException("Confidence interval crosses zero.");

            magValue = Math.Abs(value);

            if (value > 0.0)
            {
                magLower = lowerCI;
                magUpper = upperCI;
            }
            else
            {
                magLower = -upperCI;
                magUpper = -lowerCI;
            }

            if (magLower <= 0.0 || magUpper <= 0.0)
                throw new InvalidOperationException("Magnitude interval must be strictly positive.");
        }

        public static FloatWithError Divide(FloatWithError v1, FloatWithError v2)
        {
            if (v2.Value == 0.0)
                throw new DivideByZeroException();

            var value = v1.Value / v2.Value;
            var fv1 = v1.FractionSD;
            var fv2 = v2.FractionSD;
            var sd = Math.Abs(value) * Math.Sqrt(fv1 * fv1 + fv2 * fv2);

            // We can't propagate intervals the cross zero for division
            if (Math.Abs(v1.Value) < double.Epsilon ||
                Math.Abs(v2.Value) < double.Epsilon ||
                CrossesZero(v1.Lower, v1.Upper) ||
                CrossesZero(v2.Lower, v2.Upper))
            {
                return new FloatWithError(value, sd);
            }

            GetMagnitudeInterval(v1.Value, v1.Lower, v1.Upper, out double aMag, out double aMagLower, out double aMagUpper);
            GetMagnitudeInterval(v2.Value, v2.Lower, v2.Upper, out double bMag, out double bMagLower, out double bMagUpper);


            double magValue = aMag / bMag;

            double lowerLogWidth = Quad(
                Math.Log(aMag / aMagLower),
                Math.Log(bMagUpper / bMag));

            double upperLogWidth = Quad(
                Math.Log(aMagUpper / aMag),
                Math.Log(bMag / bMagLower));

            double magLowerCI = magValue * Math.Exp(-lowerLogWidth);
            double magUpperCI = magValue * Math.Exp(+upperLogWidth);

            // Order the CI and magnitude
            if (value >= 0.0) return new FloatWithError(value, sd, magLowerCI, magUpperCI);
            else return new FloatWithError(value, sd, -magUpperCI, -magLowerCI);
        }

        public static FloatWithError Multiply(FloatWithError v1, FloatWithError v2)
        {
            double value = v1.Value * v2.Value;
            double fv1 = v1.FractionSD;
            double fv2 = v2.FractionSD;
            double sd = Math.Abs(value) * Math.Sqrt(fv1 * fv1 + fv2 * fv2);

            // CI propagation only makes sense here if both intervals stay on one side of zero
            if (Math.Abs(v1.Value) < double.Epsilon ||
                Math.Abs(v2.Value) < double.Epsilon ||
                CrossesZero(v1.Lower, v1.Upper) ||
                CrossesZero(v2.Lower, v2.Upper))
            {
                return new FloatWithError(value, sd);
            }

            GetMagnitudeInterval(v1.Value, v1.Lower, v1.Upper, out double aMag, out double aMagLower, out double aMagUpper);
            GetMagnitudeInterval(v2.Value, v2.Lower, v2.Upper, out double bMag, out double bMagLower, out double bMagUpper);

            double magValue = aMag * bMag;

            double lowerLogWidth = Quad(
                Math.Log(aMag / aMagLower),
                Math.Log(bMag / bMagLower));

            double upperLogWidth = Quad(
                Math.Log(aMagUpper / aMag),
                Math.Log(bMagUpper / bMag));

            double magLowerCI = magValue * Math.Exp(-lowerLogWidth);
            double magUpperCI = magValue * Math.Exp(+upperLogWidth);

            if (value >= 0.0) return new FloatWithError(value, sd, magLowerCI, magUpperCI);
            else return new FloatWithError(value, sd, -magUpperCI, -magLowerCI);
        }

        #endregion

        public static FloatWithError Average(FloatWithError a, FloatWithError b)
        {
            var value = 0.5 * (a.Value + b.Value);

            // SD of average of two independent values
            var sd = 0.5 * Math.Sqrt(a.SD * a.SD + b.SD * b.SD);

            return new FloatWithError(value, sd);
        }

        public static FloatWithError Average(List<FloatWithError> values)
        {
            if (values.Count == 0) return FloatWithError.NaN;

            var sum = values[0];
            int count = values.Count;

            foreach (var v in values.Skip(1))
            {
                sum += v;
            }

            return sum / count;
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
