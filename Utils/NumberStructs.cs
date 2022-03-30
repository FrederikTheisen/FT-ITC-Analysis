using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    public struct Energy
    {
        const double CalToJouleFactor = 4.184;
        public static readonly Energy R = new Energy(8.3145, EnergyUnit.Joule);

        private double value;
        readonly EnergyUnit unit;

        private FloatWithError Joule;
        private FloatWithError Cal;

        public Energy(FloatWithError v, EnergyUnit unit)
        {
            this.unit = unit;
            value = v.Value;
            Joule = v;
            Cal = v;
            Value = v;
            
        }

        public Energy(double v, EnergyUnit unit)
        {
            this.unit = unit;
            value = v;
            Joule = new();
            Cal = new();
            Value = new(v);

        }

        public Energy(FloatWithError v)
        {
            unit = DataManager.Unit;
            value = v.Value;
            Joule = v;
            Cal = v;
            Value = v;
        }

        public Energy(double v = 0)
        {
            unit = DataManager.Unit;
            value = v;
            Joule = new();
            Cal = new();
            Value = new(v);
        }

        public FloatWithError Value
        {
            private set
            {
                switch (unit)
                {
                    case EnergyUnit.Joule:
                        Joule = value;
                        Cal = value / CalToJouleFactor;
                        break;
                    case EnergyUnit.Cal:
                        Joule = value * CalToJouleFactor;
                        Cal = value;
                        break;
                }
            }
            get
            {
                switch (DataManager.Unit)
                {
                    default:
                    case EnergyUnit.Joule: return Joule;
                    case EnergyUnit.Cal: return Cal;
                }
            }
        }

        public double SD
        {
            get
            {
                switch (DataManager.Unit)
                {
                    default:
                    case EnergyUnit.Joule: return Joule.SD;
                    case EnergyUnit.Cal: return Cal.SD;
                }
            }
        }

        public static Energy operator +(Energy e1, Energy e2)
        {
            var v = e1.Value + e2.Value;

            return new Energy(v, ExperimentData.Unit);
        }

        public static Energy operator -(Energy e1, Energy e2)
        {
            var v = e1.Value - e2.Value;

            return new Energy(v, ExperimentData.Unit);
        }

        public static Energy operator /(Energy e1, Energy e2) => new Energy(e1.Value / e2.Value);

        public static Energy operator /(Energy e1, double val) => new Energy(e1.Value / val);

        public static Energy operator *(Energy e1, Energy e2) => new Energy(e1.Value * e2.Value);

        public static Energy operator *(Energy e1, double val) => new Energy(e1.Value * val);

        public static Energy operator *(double val, Energy e) => new Energy(e.Value * val);

        public static bool operator <(Energy v1, Energy v2) => v1.Value.Value < v2.Value.Value;

        public static bool operator >(Energy v1, Energy v2) => v1.Value.Value > v2.Value.Value;

        public static bool operator <(Energy v1, double v2) => v1.Value.Value < v2;

        public static bool operator >(Energy v1, double v2) => v1.Value.Value > v2;

        public static implicit operator double(Energy e) => e.Value.Value;

        //TODO add unit to print
        public override string ToString()
        {
            return Value.ToString();
        }
    }

    //TODO add attribute with unit names and stuff
    public enum EnergyUnit
    {
        Joule,
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

        public static FloatWithError operator *(FloatWithError v1, double v2)
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
            if (SD < double.Epsilon) return Value.ToString();
            else return Value.ToString() + " +- " + SD.ToString();
        }
    }
}
