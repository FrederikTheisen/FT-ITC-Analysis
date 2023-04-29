using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AnalysisITC
{
    public class TimeUnitAttribute : Attribute
    {
        public string Name { get; set; }
        public string Short { get; set; }
        public string Letter { get; set; }
        public double Mod { get; set; }

        public TimeUnitAttribute(string name, string shortname, string letter, double mod)
        {
            Name = name;
            Short = shortname;
            Letter = letter;
            Mod = mod;
        }
    }

    public enum TimeUnit
    {
        [TimeUnit("Seconds", "sec", "s", 1)]
        Second,
        [TimeUnit("Minutes", "min", "m", 1/60.0)]
        Minute,
        [TimeUnit("Hours", "hour", "h", 1/3600.0)]
        Hour
    }

    public struct FloatWithError : IComparable
    {
        public double Value { get; private set; }
        public double SD { get; private set; }
        public double FractionSD
        {
            get
            {
                if (SD < double.Epsilon) return 0;
                else if (Math.Abs(Value) < double.Epsilon) return 0;
                else return Math.Abs(SD / Value);
            }
        }
        public bool HasError => SD > double.Epsilon;

        public Energy Energy => new Energy(this);

        public FloatWithError(double value = 0, double error = 0)
        {
            Value = value;
            SD = Math.Abs(error);
        }

        public FloatWithError(IEnumerable<double> distribution, double? mean = null)
        {
            double result = 0;
            double average = 0;

            if (distribution.Any())
            {
                average = distribution.Average();
                if (mean != null) average = (double)mean;
                double sum = distribution.Sum(d => Math.Pow(d - average, 2));
                result = Math.Sqrt((sum) / (distribution.Count() - 1));
            }

            this = new FloatWithError(average, result);
        }

        public FloatWithError(IEnumerable<FloatWithError> distribution, double? mean = null)
        {
            double result = 0;
            double average = 0;
            int n = 100;
            var dist = distribution.ToList();

            if (distribution.Any())
            {
                var samples = new List<double>();
                foreach (var fwe in dist) for (int i = 0; i < n; i++) samples.Add(fwe.Sample());
                average = samples.Average();
                if (mean != null) average = (double)mean;
                double sum = samples.Sum(d => Math.Pow(d - average, 2));
                result = Math.Sqrt((sum) / (samples.Count() - 1));
            }

            this = new FloatWithError(average, result);
        }

        public void SetError(IEnumerable<double> distribution)
        {
            double result = 0;
            double average = 0;

            if (distribution.Any())
            {
                average = distribution.Average();
                double sum = distribution.Sum(d => Math.Pow(d - average, 2));
                result = Math.Sqrt((sum) / distribution.Count());
            }

            SD = Math.Abs(result);
        }
        public enum ConfidenceLevel
        {
            Conf95,
            SD,
            Conf50
        }
        public double[] WithConfidence(ConfidenceLevel conf = ConfidenceLevel.Conf95)
        {
            switch (conf)
            {
                default:
                case ConfidenceLevel.Conf95: return new double[] { Value + 2 * SD, Value - 2 * SD };
                case ConfidenceLevel.SD: return new double[] { Value + SD, Value - SD };
                case ConfidenceLevel.Conf50: return new double[] { Value + 0.5 * SD, Value - 0.5 * SD };
            }
        }

        public double Sample(Random rand = null)
        {
            return Distribution.Normal(this, rand);
        }

        public static FloatWithError operator +(FloatWithError v1, FloatWithError v2)
        {
            var v = v1.Value + v2.Value;
            var sd = Math.Sqrt(v1.SD * v1.SD + v2.SD * v2.SD);

            return new FloatWithError(v, sd);
        }

        public static FloatWithError operator -(FloatWithError v1, FloatWithError v2)
        {
            var v = v1.Value - v2.Value;
            var sd = Math.Sqrt(v1.SD * v1.SD + v2.SD * v2.SD);

            return new FloatWithError(v, sd);
        }

        public static FloatWithError operator /(FloatWithError v1, FloatWithError v2)
        {
            var v = v1.Value / v2.Value;

            if (v1.SD + v2.SD < double.Epsilon) return new FloatWithError(v, 0);
            if (v1.SD < double.Epsilon) return new FloatWithError(v, v2.FractionSD * v);
            if (v2.SD < double.Epsilon) return new FloatWithError(v, v1.FractionSD * v);

            var fv1 = v1.FractionSD;
            var fv2 = v2.FractionSD;

            var sd = v * Math.Sqrt(fv1 * fv1 + fv2 * fv2);

            return new FloatWithError(v, sd);
        }

        public static FloatWithError operator /(FloatWithError v1, double v2)
        {
            var v = v1.Value / v2;

            return new FloatWithError(v, v1.FractionSD * v);
        }

        public static FloatWithError operator *(FloatWithError v1, FloatWithError v2)
        {
            var v = v1.Value * v2.Value;

            if (v1.SD + v2.SD < double.Epsilon) return new FloatWithError(v, 0);
            if (v1.SD < double.Epsilon) return new FloatWithError(v, v2.FractionSD * v);
            if (v2.SD < double.Epsilon) return new FloatWithError(v, v1.FractionSD * v);

            var fv1 = v1.FractionSD;
            var fv2 = v2.FractionSD;

            var sd = v * Math.Sqrt(fv1 * fv1 + fv2 * fv2);

            return new FloatWithError(v, sd);
        }

        public static FloatWithError operator *(FloatWithError v1, double v2) => v2 * v1;

        public static FloatWithError operator *(double v2, FloatWithError v1)
        {
            var v = v1.Value * v2;

            return new FloatWithError(v, v1.FractionSD * v);
        }

        public static explicit operator FloatWithError(double v)
        {
            return new FloatWithError(v);
        }

        public static implicit operator double(FloatWithError v)
        {
            return v.Value;
        }

        public override string ToString()
        {
            return ToString("F3") + " | " + (100 * FractionSD).ToString("F1") + "%";
        }

        public string ToString(string format = "F1")
        {
            if (SD < double.Epsilon) return Value.ToString(format);
            else return Value.ToString(format) + " ± " + SD.ToString(format);
        }

        public string AsDissociationConstant(ConcentrationUnit unit, bool withunit = true)
        {
            var value = (unit.GetMod() * this).ToString();

            return withunit ? value + " " + unit.GetName() : value;
        }

        public string AsDissociationConstant(double mag = 0, bool withunit = true)
        {
            if (mag == 0) mag = Math.Log10(Value);

            if (withunit) return mag switch
            {
                > 0 => ToString() + " M",
                > -3 => (1000 * this).ToString() + " mM",
                > -6 => (1000000 * this).ToString() + " µM",
                > -9 => (1000000000 * this).ToString() + " nM",
                > -12 => (1000000000000 * this).ToString() + " pM",
                _ => ToString() + " M"
            };
            else return mag switch
            {
                > 0 => ToString(),
                > -3 => (1000 * this).ToString(),
                > -6 => (1000000 * this).ToString(),
                > -9 => (1000000000 * this).ToString(),
                > -12 => (1000000000000 * this).ToString(),
                _ => ToString()
            };
        }

        public int CompareTo(object obj)
        {
            if (obj == null) return 1;

            if (obj is FloatWithError) return Value.CompareTo(((FloatWithError)obj).Value);
            else throw new Exception("value not FWE");
        }
    }

    public class EnthalpyTuple : Tuple<Energy, Energy, Energy>
    {
        public Energy ReferenceEnthalpy => Item1;
        public Energy StandardEnthalpy => Item2;
        public Energy HeatCapacity => Item3;

        public EnthalpyTuple(Energy reference, Energy standard, Energy heatcapacity) : base(reference, standard, heatcapacity) { }

        public EnthalpyTuple(double reference, double standard, double heatcapacity) : base(new(reference), new Energy(standard), new(heatcapacity)) { }
    }

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

            if (slope == 0) return intercept.ToString(energyUnit, withunit: false);
            else return slope.ToString(energyUnit, withunit: false) + " · ∆T + " + intercept.ToString(energyUnit, withunit: false);
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

            var result = Kd0 * Math.Exp(-0.51 * Charges * Math.Sqrt(x) / (1 + Math.Sqrt(x)));

            if (iterations < 2) return result;

            //kd0 * Math.Exp(-0.51 * z * Math.Sqrt(x) / (1 + Math.Sqrt(x)))
            for (int i = 0; i < iterations; i++)
            {
                var _kd0 = Kd0.Sample(rand);
                var _z = Charges.Sample(rand);

                var f = _kd0 * Math.Exp(-0.51 * _z * Math.Sqrt(x) / (1 + Math.Sqrt(x)));

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

    public class SingleExponentialDecayFit : ElectrostaticsFit
    {
        public override FloatWithError Plateau => Parameters[1];
        public FloatWithError K => Parameters[2];

        public SingleExponentialDecayFit(FloatWithError kd0, FloatWithError plateau, FloatWithError k, double referencex = 0) : base(new FloatWithError[] {kd0,plateau,k}, referencex)
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
