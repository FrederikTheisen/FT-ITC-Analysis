using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearRegression;

namespace AnalysisITC
{
    public class IonicStrengthDependence
    {
        public double Kd0 { get; private set; }
        public double SaltSensitivity { get; private set; }
        public double Curvature { get; private set; }
        public double SSE { get; private set; }
        public bool UsesCurvature { get; private set; }

        public IonicStrengthDependence(double kd0, double saltSensitivity, double curvature = 0.0, double sse = double.NaN, bool usesCurvature = false)
        {
            Kd0 = kd0;
            SaltSensitivity = saltSensitivity;
            Curvature = curvature;
            SSE = sse;
            UsesCurvature = usesCurvature;
        }

        public double Evaluate(double ionicStrength)
        {
            if (Kd0 <= 0) return double.NaN;
            if (double.IsNaN(ionicStrength) || double.IsInfinity(ionicStrength) || ionicStrength < 0) return double.NaN;

            var lnKd = Math.Log(Kd0) + SaltSensitivity * Math.Sqrt(ionicStrength) + Curvature * ionicStrength;
            return Math.Exp(lnKd);
        }

        public static IonicStrengthDependence FitIonicStrengthDependence(double[] ionicStrengths, double[] kds)
        {
            if (ionicStrengths == null || kds == null) return null;
            if (ionicStrengths.Length != kds.Length) return null;

            var xs = new List<double>();
            var ys = new List<double>();

            for (int i = 0; i < ionicStrengths.Length; i++)
            {
                var x = ionicStrengths[i];
                var y = kds[i];

                if (double.IsNaN(x) || double.IsInfinity(x) || x < 0) continue;
                if (double.IsNaN(y) || double.IsInfinity(y) || y <= 0) continue;

                xs.Add(x);
                ys.Add(Math.Log(y));
            }

            if (xs.Count < 2) return null;

            // Keep this simple:
            // - 2 parameters if the dataset is small
            // - 3 parameters only when there are enough points to support curvature
            if (false)
            {
                var curved = FitWithCurvature(xs.ToArray(), ys.ToArray());
                if (curved != null) return curved;
            }

            return FitWithoutCurvature(xs.ToArray(), ys.ToArray());
        }

        static IonicStrengthDependence FitWithoutCurvature(double[] ionicStrengths, double[] lnKds)
        {
            if (ionicStrengths.Length < 2) return null;

            var u = ionicStrengths.Select(x => Math.Sqrt(x)).ToArray();
            var p = MathNet.Numerics.Fit.Polynomial(u, lnKds, 1);
            if (p == null || p.Length < 2) return null;

            var lnKd0 = p[0];
            var saltSensitivity = p[1];
            var kd0 = Math.Exp(lnKd0);

            if (double.IsNaN(kd0) || double.IsInfinity(kd0) || kd0 <= 0) return null;

            double sse = 0.0;
            for (int i = 0; i < ionicStrengths.Length; i++)
            {
                var pred = lnKd0 + saltSensitivity * Math.Sqrt(ionicStrengths[i]);
                var resid = lnKds[i] - pred;
                sse += resid * resid;
            }

            return new IonicStrengthDependence(kd0, saltSensitivity, 0.0, sse, false);
        }

        static IonicStrengthDependence FitWithCurvature(double[] ionicStrengths, double[] lnKds)
        {
            if (ionicStrengths.Length < 5) return null;

            var u = ionicStrengths.Select(x => Math.Sqrt(x)).ToArray();
            var p = MathNet.Numerics.Fit.Polynomial(u, lnKds, 2);
            if (p == null || p.Length < 3) return null;

            var lnKd0 = p[0];
            var saltSensitivity = p[1];
            var curvature = p[2];
            var kd0 = Math.Exp(lnKd0);

            if (double.IsNaN(kd0) || double.IsInfinity(kd0) || kd0 <= 0) return null;
            if (double.IsNaN(curvature) || double.IsInfinity(curvature)) return null;

            double sse = 0.0;
            for (int i = 0; i < ionicStrengths.Length; i++)
            {
                var pred = lnKd0 + saltSensitivity * Math.Sqrt(ionicStrengths[i]) + curvature * ionicStrengths[i];
                var resid = lnKds[i] - pred;
                sse += resid * resid;
            }

            return new IonicStrengthDependence(kd0, saltSensitivity, curvature, sse, true);
        }
    }

    public class IonicStrengthDependenceFit : ElectrostaticsFit
    {
        public FloatWithError Kd0 { get; private set; }
        public FloatWithError SaltSensitivity { get; private set; }
        public FloatWithError Curvature { get; private set; }
        public bool UsesCurvature { get; private set; }

        public IonicStrengthDependenceFit(FloatWithError kd0, FloatWithError saltSensitivity, FloatWithError curvature, bool usesCurvature = false)
        {
            Kd0 = kd0;
            SaltSensitivity = saltSensitivity;
            Curvature = curvature;
            UsesCurvature = usesCurvature;
        }

        public override FloatWithError Evaluate(double ionicStrength)
        {
            if (Kd0.Value <= 0) return new();
            if (double.IsNaN(ionicStrength) || double.IsInfinity(ionicStrength) || ionicStrength < 0) return FloatWithError.NaN;

            var lnKd = FWEMath.Log(Kd0) + SaltSensitivity * ionicStrength;
            if (UsesCurvature) lnKd += Curvature * ionicStrength * ionicStrength;

            return lnKd / 2.303;
        }
    }
}

