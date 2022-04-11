using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    public struct Energy
    {
        const double CalToJouleFactor = 4.184;
        const double JouleToCalFactor = 1 / 4.184;
        const double MicroFactor = 0.000001;
        public static readonly Energy R = new Energy(8.3145);

        public FloatWithError Value { get; set; }
        public double SD => Value.SD;

        public Energy(FloatWithError v)
        {
            Value = v;
        }

        public Energy(double v)
        {
            Value = new(v);
        }

        public static Energy FromDistribution(IEnumerable<double> dist, double? mean = null) => new Energy(new FloatWithError(dist, mean));

        public static double ConvertToJoule(double value, EnergyUnit from)
        {
            switch (from)
            {
                case EnergyUnit.MicroCal: return MicroFactor * CalToJouleFactor * value;
                case EnergyUnit.Cal: return CalToJouleFactor * value;
                case EnergyUnit.Joule:
                default: return value;
            }
        }

        public static Energy operator +(Energy e1, Energy e2)
        {
            var v = e1.Value + e2.Value;

            return new Energy(v);
        }

        public static Energy operator -(Energy e1, Energy e2)
        {
            var v = e1.Value - e2.Value;

            return new Energy(v);
        }

        public static Energy operator /(Energy e1, Energy e2) => new Energy(e1.Value / e2.Value);

        public static Energy operator /(Energy e1, double val) => new Energy(e1.Value / val);

        public static Energy operator *(Energy e1, Energy e2) => new Energy(e1.Value * e2.Value);

        public static Energy operator *(Energy e1, double val) => new Energy(e1.Value * val);

        public static Energy operator *(double val, Energy e) => new Energy(e.Value * val);

        //public static bool operator <(Energy v1, Energy v2) => v1.Value.Value < v2.Value.Value;

        //public static bool operator >(Energy v1, Energy v2) => v1.Value.Value > v2.Value.Value;

        //public static bool operator <(Energy v1, double v2) => v1.Value.Value < v2;

        //public static bool operator >(Energy v1, double v2) => v1.Value.Value > v2;

        public static implicit operator double(Energy e) => e.Value.Value;

        //TODO add unit to print
        public override string ToString()
        {
            return Value.ToString();
        }

        public string ToString(EnergyUnit unit)
        {
            switch (unit)
            {
                case EnergyUnit.Joule: return Value.ToString() + " J";
                case EnergyUnit.MicroCal: return (1000000 * JouleToCalFactor * Value).ToString() + " µcal";
                case EnergyUnit.Cal: return (JouleToCalFactor * Value).ToString() + " cal";
                case EnergyUnit.KiloJoule: return (Value/1000).ToString() + " kJ";
                default: return Value.ToString() + " J";
            }
        }
    }

    //TODO add attribute with unit names and stuff
    public enum EnergyUnit
    {
        KiloJoule,
        Joule,
        MicroCal,
        Cal
    }

    public struct FloatWithError
    {
        public double Value { get; set; }
        public double SD { get; set; }
        public double FractionSD
        {
            get
            {
                if (SD < double.Epsilon) return 0;
                else if (Math.Abs(Value) < double.Epsilon) return 0;
                else return SD / Value;
            }
        }

        public FloatWithError(double value = 0, double error = 0)
        {
            Value = value;
            SD = error;
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
                result = Math.Sqrt((sum) / distribution.Count());
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

            SD = result;
        }

        public double[] WithConfidence()
        {
            return new double[] { Value + 2 * SD, Value - 2 * SD };
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

        public string ToString(string format = "F1")
        {
            if (SD < double.Epsilon) return Value.ToString(format);
            else return Value.ToString(format) + " ± " + SD.ToString(format);
        }

        public string AsDissociationConstant()
        {
            var mag = Math.Log10(Value);

            if (mag > 0) return this.ToString() + " M";
            else if (mag > -3) return (1000 * this).ToString() + " mM";
            else if (mag > -6) return (1000000 * this).ToString() + " µM";
            else if (mag > -9) return (1000000000 * this).ToString() + " nM";
            else if (mag > -12) return (1000000000000 * this).ToString() + " pM";
            else return this.ToString() + " M";
        }
    }
}
