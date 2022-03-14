using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    public struct Energy
    {
        const double CalToJouleFactor = 4.184;

        readonly double value;
        readonly EnergyUnit unit;

        private FloatWithError Joule;
        private FloatWithError Cal;

        public Energy(FloatWithError v, EnergyUnit unit)
        {
            this.unit = unit;
            value = v.Value;
            Joule = 0;
            Cal = 0;
            Value = v;
            
        }

        public Energy(double v, EnergyUnit unit)
        {
            this.unit = unit;
            value = v;
            Joule = 0;
            Cal = 0;
            Value = v;

        }

        public Energy(FloatWithError v)
        {
            unit = DataManager.Unit;
            value = v.Value;
            Joule = 0;
            Cal = 0;
            Value = v;
        }

        public Energy(double v)
        {
            unit = DataManager.Unit;
            value = v;
            Joule = 0;
            Cal = 0;
            Value = v;
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

        public static Energy operator /(Energy e1, Energy e2)
        {
            var v = e1.Value / e2.Value;

            return new Energy(v, ExperimentData.Unit);
        }

        public static Energy operator /(Energy e1, double val)
        {
            var v = e1.Value / val;

            return new Energy(v, ExperimentData.Unit);
        }

        public static Energy operator *(Energy e1, Energy e2)
        {
            var v = e1.Value * e2.Value;

            return new Energy(v, ExperimentData.Unit);
        }

        public static Energy operator *(Energy e1, double val)
        {
            var v = e1.Value * val;

            return new Energy(v, ExperimentData.Unit);
        }

        public static Energy operator *(double val, Energy e)
        {
            var v = e.Value * val;

            return new Energy(v, ExperimentData.Unit);
        }

        public static implicit operator double(Energy e)
        {
            return e.Value.Value;
        }
    }

    public enum EnergyUnit
    {
        Joule,
        Cal
    }

    public struct FloatWithError
    {
        public double Value { get; set; }
        public double SD { get; set; }

        public FloatWithError(double value, double error = 0)
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

            var fv1 = v1.SD / v1.Value;
            var fv2 = v2.SD / v2.Value;

            var sd = Math.Sqrt(fv1 * fv1 + fv2 * fv2);

            return new FloatWithError(v, sd);
        }

        public static FloatWithError operator *(FloatWithError v1, FloatWithError v2)
        {
            var v = v1.Value * v2.Value;

            var fv1 = v1.SD / v1.Value;
            var fv2 = v2.SD / v2.Value;

            var sd = Math.Sqrt(fv1 * fv1 + fv2 * fv2);

            return new FloatWithError(v, sd);
        }

        public static implicit operator FloatWithError(double v)
        {
            return new FloatWithError(v);
        }

        public static explicit operator double(FloatWithError v)
        {
            return v.Value;
        }
    }
}
