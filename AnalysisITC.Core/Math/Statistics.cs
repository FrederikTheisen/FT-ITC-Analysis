using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Numerics
{
	public static class Statistics
	{
        public static double Ar1SumVarFactor(int n, double r)
        {
            if (n <= 1) return n;

            if (Math.Abs(r) < 1e-8) return n;

            // m = n-1
            int m = n - 1;

            double oneMinusR = 1.0 - r;
            double rPowM = Math.Pow(r, m);
            double rPowN = rPowM * r;

            // A = sum_{k=1..m} r^k
            double A = r * (1.0 - rPowM) / oneMinusR;

            // B = sum_{k=1..m} k r^k
            double denom = oneMinusR * oneMinusR;
            double B = r * (1.0 - (m + 1) * rPowM + m * rPowN) / denom;

            // S = sum_{k=1..m} (n-k) r^k = nA - B
            double S = n * A - B;

            return n + 2.0 * S;
        }

        /// <summary>
        /// Variance multiplier for an AR(1) weighted sum. Integration weights are
        /// contributing durations in seconds; correlation follows sample-order
        /// separation, including when those durations differ.
        /// </summary>
        internal static double Ar1WeightedSumVarFactor(IReadOnlyList<double> weights, double r)
        {
            double carry = 0;
            double variance = 0;

            // Express the sum in independent AR(1) innovations, accumulating
            // squares rather than cancelling positive and negative covariances.
            for (int i = weights.Count - 1; i >= 0; i--)
            {
                carry = weights[i] + r * carry;
                variance += (i == 0 ? 1 : 1 - r * r) * carry * carry;
            }

            return variance;
        }

        public static double EstimateAutoCorrelation(List<DataPoint> pts, double dtMax)
        {
            // Computes r1 = sum(x_i x_{i-1}) / sum(x_i^2) over the same
            // eligible pairs in both sums. Excluded integration intervals must
            // not add an unpaired destination square to the normalization.
            double num = 0;
            double den = 0;

            for (int i = 1; i < pts.Count; i++)
            {
                double x0 = pts[i - 1].Power;
                double x1 = pts[i].Power;

                double dt = pts[i].Time - pts[i - 1].Time;
                if (dt <= dtMax)
                {
                    num += x1 * x0;
                    den += x1 * x1;
                }
            }

            if (den <= 0) return 0;

            double r = num / den;
            if (r > 0.999) r = 0.999;
            if (r < -0.999) r = -0.999;
            return Math.Min(r, 0.85);
        }

        public static float Median(List<float> list)
        {
            int count = list.Count;

            if (count % 2 == 0)
                return list.OrderBy(o => o).Skip((count / 2) - 1).Take(2).Average();
            else
                return list.OrderBy(x => x).ElementAt(count / 2);
        }
    }
}

