using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    public class FitWithError
    {
        internal static Random Rand = new Random();

        internal FloatWithError[] Parameters { get; set; } = null;

        public double ReferenceX { get; set; } = 0;

        public FitWithError()
        {

        }

        public FitWithError(FloatWithError[] parameters, double referencex = 0)
        {
            Parameters = parameters;
            ReferenceX = referencex;
        }

        public virtual FloatWithError Evaluate(double x, int iterations = 5000)
        {
            return new(0);
        }

        public virtual double[] MinMax(double x)
        {
            return new double[] { 0, 0 };
        }
    }

    public class LinearFit : Tuple<double, double, double>
    {
        public double Slope => Item1;
        public double Intercept => Item2;
        public double ReferenceT => Item3;

        public LinearFit(double slope, double intercept, double referencex) : base(slope, intercept, referencex)
        {
        }

        public double Evaluate(double x) => (x - ReferenceT) * Slope + Intercept;
    }

    public class LinearFitWithError : FitWithError
    {
        public FloatWithError Slope => Parameters[0];
        public FloatWithError Intercept => Parameters[1];
        public double ReferenceT => ReferenceX;

        public LinearFitWithError(FloatWithError slope, FloatWithError intercept, double referencex) : base(new[] { slope, intercept }, referencex)
        {
        }

        public LinearFitWithError(double slope, double intercept, double referencex) : base(new[] { new FloatWithError(slope), new FloatWithError(intercept) }, referencex)
        {
        }

        public override FloatWithError Evaluate(double x, int iterations = 5000)
        {
            var rand = new Random();
            var results = new List<double>();

            for (int i = 0; i < iterations; i++)
            {
                var f = new LinearFit(Slope.Sample(rand), Intercept.Sample(rand), ReferenceT);

                results.Add(f.Evaluate(x));
            }

            return new FloatWithError(results);
        }

        public FloatWithError GetXAxisIntersect()
        {
            var rand = new Random();
            var results = new List<double>();

            var exact = -Intercept.Value / Slope.Value + ReferenceT;

            for (int i = 0; i < 3000; i++)
            {
                var f = new LinearFit(Slope.Sample(rand), Intercept.Sample(rand), ReferenceT);

                results.Add(-f.Intercept / f.Slope + ReferenceT);
            }

            return new FloatWithError(results, exact);
        }

        public string ToString(EnergyUnit energyUnit)
        {
            var slope = new Energy(Slope);
            var intercept = new Energy(Intercept);

            string s = intercept.ToFormattedString(energyUnit, withunit: false);

            if (Math.Abs(slope) > 1E-6) // If there is any meaningful slope
            {
                // Determine correct sign for display
                string sign = slope < 0 ? "-" : "+";

                var printslope = new Energy(Math.Abs(slope.Value), slope.SD);

                // (value ± sd) + (slope ± sd) · ∆T
                s = "(" + s + ") " + sign + " (" + printslope.ToFormattedString(energyUnit, withunit: false) + ") · ∆T";
            }

            return s;
        }
    }

    public class ElectrostaticsFit : FitWithError
    {
        public FloatWithError Kd0 => Parameters[0];
        public virtual FloatWithError Plateau
        {
            get
            {
                return new(0);
            }
        }

        public ElectrostaticsFit()
        {

        }

        public ElectrostaticsFit(FloatWithError[] parameters, double refx = 0) : base(parameters, refx)
        {

        }

        public ElectrostaticsFit(FloatWithError kd0, FloatWithError zz, double referencex = 0) : base(new[] { kd0, zz }, referencex)
        {
        }
    }

    public class DebyeHuckelFit : ElectrostaticsFit
    {
        public FloatWithError Charges => Parameters[1];
        public override FloatWithError Plateau
        {
            get
            {
                return Kd0 * Math.Exp(-0.51 * Charges);
            }
        }

        public DebyeHuckelFit(FloatWithError kd0, FloatWithError zz, double referencex = 0) : base(kd0, zz, referencex)
        {
        }

        public override FloatWithError Evaluate(double x, int iterations = 2000)
        {
            if (x < 0) throw new ArgumentOutOfRangeException("X value cannot be negative for this function");

            var rand = new Random();
            var results = new List<double>();
            var sqrtx = Math.Sqrt(x);

            var result = Kd0 * Math.Exp(-0.51 * Charges * sqrtx / (1 + sqrtx));

            if (iterations < 2) return result;

            //kd0 * Math.Exp(-0.51 * z * Math.Sqrt(x) / (1 + Math.Sqrt(x)))
            for (int i = 0; i < iterations; i++)
            {
                var _kd0 = Kd0.Sample(rand);
                var _z = Charges.Sample(rand);

                var f = _kd0 * Math.Exp(-0.51 * _z * sqrtx / (1 + sqrtx));

                results.Add(f);
            }

            return new FloatWithError(results, result);
        }

        public override double[] MinMax(double x)
        {
            var r00 = Kd0.WithConfidence(FloatWithError.ConfidenceLevel.SD)[0] * Math.Exp(-0.51 * Charges.WithConfidence(FloatWithError.ConfidenceLevel.SD)[0] * Math.Sqrt(x) / (1 + Math.Sqrt(x)));
            var r01 = Kd0.WithConfidence(FloatWithError.ConfidenceLevel.SD)[0] * Math.Exp(-0.51 * Charges.WithConfidence(FloatWithError.ConfidenceLevel.SD)[1] * Math.Sqrt(x) / (1 + Math.Sqrt(x)));
            var r10 = Kd0.WithConfidence(FloatWithError.ConfidenceLevel.SD)[1] * Math.Exp(-0.51 * Charges.WithConfidence(FloatWithError.ConfidenceLevel.SD)[0] * Math.Sqrt(x) / (1 + Math.Sqrt(x)));
            var r11 = Kd0.WithConfidence(FloatWithError.ConfidenceLevel.SD)[1] * Math.Exp(-0.51 * Charges.WithConfidence(FloatWithError.ConfidenceLevel.SD)[1] * Math.Sqrt(x) / (1 + Math.Sqrt(x)));

            var vals = new double[] { r00, r01, r10, r11 };

            return new double[] { vals.Min(), vals.Max() };
        }
    }

    public class CounterIonReleaseFit : ElectrostaticsFit
    {
        LinearFitWithError Fit;

        public CounterIonReleaseFit(FloatWithError slope, FloatWithError intercept, double referencex = 0)
        {
            Fit = new LinearFitWithError(slope, intercept, referencex);
        }

        public override FloatWithError Evaluate(double x, int iterations = 5000)
        {
            return Fit.Evaluate(x, iterations);
        }

        public override double[] MinMax(double x)
        {
            return base.MinMax(x);
        }
    }

    public class SingleExponentialDecayFit : ElectrostaticsFit
    {
        public override FloatWithError Plateau => Parameters[1];
        public FloatWithError K => Parameters[2];

        public SingleExponentialDecayFit(FloatWithError kd0, FloatWithError plateau, FloatWithError k, double referencex = 0) : base(new FloatWithError[] { kd0, plateau, k }, referencex)
        {

        }

        double Function(double kd0, double p, double k, double dx)
        {
            return (kd0 - p) * Math.Exp(-k * dx) + p;
        }

        public override FloatWithError Evaluate(double x, int iterations = 2000)
        {
            var rand = new Random();
            var results = new List<double>();

            var result = new FloatWithError(Function(Kd0, Plateau, K, x));

            if (iterations < 2) return result;

            for (int i = 0; i < iterations; i++)
            {
                var _kd0 = Kd0.Sample(rand);
                var _p = Plateau.Sample(rand);
                var _k = K.Sample(rand);

                results.Add(Function(_kd0, _p, _k, x));
            }

            return new FloatWithError(results, result);
        }

        public override double[] MinMax(double x)
        {
            var r000 = Function(
                Kd0.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[0],
                Plateau.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[0],
                K.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[0], x);

            var r001 = Function(
                Kd0.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[0],
                Plateau.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[0],
                K.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[1], x);

            var r010 = Function(
                Kd0.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[0],
                Plateau.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[1],
                K.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[0], x);

            var r100 = Function(
                Kd0.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[1],
                Plateau.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[0],
                K.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[0], x);

            var r011 = Function(
                Kd0.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[0],
                Plateau.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[1],
                K.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[1], x);

            var r110 = Function(
                Kd0.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[1],
                Plateau.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[1],
                K.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[0], x);

            var r101 = Function(
                Kd0.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[1],
                Plateau.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[0],
                K.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[1], x);

            var r111 = Function(
                Kd0.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[1],
                Plateau.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[1],
                K.WithConfidence(FloatWithError.ConfidenceLevel.Conf50)[1], x);

            var vals = new double[] { r000, r001, r010, r100, r101, r011, r110, r111 };

            var val = new FloatWithError(vals, Evaluate(x, 0));
            return val.WithConfidence(FloatWithError.ConfidenceLevel.Conf50);
            return new double[] { vals.Min(), vals.Max() };
        }
    }
}

