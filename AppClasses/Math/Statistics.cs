using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
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

        public static double EstimateAutoCorrelation(List<DataPoint> pts, double dtMax)
        {
            // Computes r1 = sum(x_i x_{i-1}) / sum(x_i^2) but only for adjacent samples in time
            double num = 0;
            double den = 0;

            for (int i = 1; i < pts.Count; i++)
            {
                double x0 = pts[i - 1].Power;
                double x1 = pts[i].Power;

                den += x1 * x1;

                double dt = pts[i].Time - pts[i - 1].Time;
                if (dt <= dtMax)
                    num += x1 * x0;
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

