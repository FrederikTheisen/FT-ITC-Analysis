using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AnalysisITC
{
    public enum ConcentrationUnit
    {
        M,
        mM,
        µM,
        nM,
        pM
    }

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

        public double[] WithConfidence()
        {
            return new double[] { Value + 2 * SD, Value - 2 * SD };
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

    public class LinearFit : Tuple<double, double, double>
    {
        public double Slope => Item1;
        public double Intercept => Item2;
        public double ReferenceT => Item3;

        public LinearFit(double slope, double intercept, double referencex) : base(slope, intercept, referencex)
        {
        }

        public static LinearFit FitData(double[] x, double[] y, double refx)
        {
            var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(x.Select(v => v - refx).ToArray(), y);

            return new LinearFit(reg.B, reg.A, refx);
        }

        public double Evaluate(double x) => (x - ReferenceT) * Slope + Intercept;
    }

    public class LinearFitWithError : Tuple<FloatWithError, FloatWithError, double>
    {
        static Random Random = new Random();

        public FloatWithError Slope => Item1;
        public FloatWithError Intercept => Item2;
        public double ReferenceT => Item3;

        public LinearFitWithError(FloatWithError slope, FloatWithError intercept, double referencex) : base(slope, intercept, referencex)
        {
        }

        public LinearFitWithError(double slope, double intercept, double referencex) : base(new(slope), new(intercept), referencex)
        {
        }

        static async Task<LinearFitWithError> FitData(double[] x, double[] y, double refx) //TODO not used, remove code (22-06-05)
        {
            var fit = LinearFit.FitData(x, y, refx);
            var fits = new List<LinearFit>();

            var residuals = x.Select((v, i) => fit.Evaluate(v) - y[i]).ToList();

            await Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    var newys = y.Select(v => v + residuals[Random.Next(residuals.Count())]).ToArray();

                    fits.Add(LinearFit.FitData(x, newys, refx));
                }
            });

            var dist_slope = fits.Select(f => f.Slope);
            var dist_intercept = fits.Select(f => f.Intercept);

            return new LinearFitWithError(new FloatWithError(dist_slope, fit.Slope), new FloatWithError(dist_intercept, fit.Intercept), refx);
        }

        public FloatWithError Evaluate(double x, int iterations = 5000)
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

        public string ToString(string formatter = "F0")
        {
            return "(" + Slope.ToString(formatter) + ") · ∆T + (" + Intercept.ToString(formatter) + ")";
        }

        public string ToString(EnergyUnit energyUnit)
        {
            var slope = new Energy(Slope);
            var intercept = new Energy(Intercept);

            return slope.ToString(energyUnit, withunit: false) + " · ∆T + " + intercept.ToString(energyUnit, withunit: false);
        }
    }
}
