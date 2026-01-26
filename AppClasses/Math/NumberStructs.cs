using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AnalysisITC
{
    public struct FloatWithError : IComparable
    {
        public double Value { get; private set; }
        public double SD { get; private set; }
        public double[] DistributionConfidence95 { get; private set; }
        public double[] DistributionConfidence100 { get; private set; }

        public double FractionSD
        {
            get
            {
                if (SD < double.Epsilon) return 0;
                else if (Math.Abs(Value) < double.Epsilon) return 0;
                else return Math.Abs(SD / Value);
            }
        }
        public bool HasError
        {
            get
            {
                if (Math.Abs(Value) < float.Epsilon) return SD > 10E-8;
                else return SD / Math.Abs(Value) > 10E-8;
            }
        }

        public Energy Energy => new Energy(this);

        public FloatWithError(double value = 0, double error = 0)
        {
            Value = value;
            SD = Math.Abs(error);
            DistributionConfidence95 = new double[] { Value - 2 * SD, Value + 2 * SD };
            DistributionConfidence100 = new double[] { Value - 3 * SD, Value + 3 * SD };
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

                Value = average;
                SD = Math.Abs(result);
                DistributionConfidence95 = GetConfidenceInterval(distribution.ToList());
                DistributionConfidence100 = new double[] { distribution.Min(),distribution.Max() };
            }
            else this = new FloatWithError((double)mean, result);
        }

        public FloatWithError(IEnumerable<FloatWithError> distribution, double? mean = null)
        {
            double result = 0;
            double average = 0;
            
            if (distribution.Any())
            {
                int n = 100;
                var samples = new List<double>();
                foreach (var fwe in distribution) for (int i = 0; i < n; i++) samples.Add(fwe.Sample());
                average = samples.Average();
                if (mean != null) average = (double)mean;
                double sum = samples.Sum(d => Math.Pow(d - average, 2));
                result = Math.Sqrt((sum) / (samples.Count() - 1));

                Value = average;
                SD = Math.Abs(result);
                DistributionConfidence95 = GetConfidenceInterval(samples);
                DistributionConfidence100 = new double[] { samples.Min(), samples.Max() };
            }
            else this = new FloatWithError((double)mean, result);
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
            DistributionConfidence95 = GetConfidenceInterval(distribution.ToList());
            DistributionConfidence100 = new double[] { distribution.Min(), distribution.Max() };
        }

        public static double[] GetConfidenceInterval(List<double> distribution)
        {
            // Sort the data in ascending order
            distribution.Sort();

            // Set the desired confidence level (e.g., 95%)
            double confidenceLevel = 0.95;

            // Calculate lower and upper percentiles
            double lowerPercentile = (1 - confidenceLevel) / 2 * 100;
            double upperPercentile = (1 + confidenceLevel) / 2 * 100;

            // Calculate the indices corresponding to the percentiles
            int lowerIndex = (int)Math.Ceiling(lowerPercentile * distribution.Count / 100) - 1;
            int upperIndex = (int)Math.Floor(upperPercentile * distribution.Count / 100);

            // Retrieve the values at the calculated indices
            double lowerValue = distribution[lowerIndex];
            double upperValue = distribution[upperIndex];

            return new double[] { lowerValue, upperValue };
        }

        public enum ConfidenceLevel
        {
            Conf100,
            Conf95,
            SD,
            Conf50
        }
        public double[] WithConfidence(ConfidenceLevel conf = ConfidenceLevel.Conf95)
        {
            switch (conf)
            {
                default:
                case ConfidenceLevel.Conf95: return DistributionConfidence95;
                case ConfidenceLevel.SD: return new double[] { Value + SD, Value - SD };
                case ConfidenceLevel.Conf50: return new double[] { Value + 0.5 * SD, Value - 0.5 * SD };
                case ConfidenceLevel.Conf100: return DistributionConfidence100;
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

        public static FloatWithError operator +(FloatWithError v1, double v2)
        {
            var v = v1.Value + v2;

            return new FloatWithError(v, v1.SD);
        }

        public static FloatWithError operator -(FloatWithError v1, FloatWithError v2)
        {
            var v = v1.Value - v2.Value;
            var sd = Math.Sqrt(v1.SD * v1.SD + v2.SD * v2.SD);

            return new FloatWithError(v, sd);
        }

        public static FloatWithError operator -(FloatWithError v1, double v2)
        {
            var v = v1.Value - v2;

            return new FloatWithError(v, v1.SD);
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

        public static FloatWithError operator /(double v1, FloatWithError v2)
        {
            var v = v1 / v2.Value;

            return new FloatWithError(v, v2.FractionSD * v);
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
            return ToString("G3") + " | " + (100 * FractionSD).ToString("F1") + "%";
        }

        public string AsSaveString()
        {
            return Value.ToString("G5") + "," + SD.ToString("G5");
        }

        public string ToString(string format = "F1")
        {
            if (!HasError) return Value.ToString(format);
            else return Value.ToString(format) + " ± " + SD.ToString(format);
        }

        public string AsNumber()
        {
            return WithMod(1, "", false);
        }

        public string AsConcentration(ConcentrationUnit unit, bool withunit = true)
        {
            var value = unit.GetMod() * this;

            return withunit ? value.ToString("G5") + " " + unit.GetName() : value.ToString("G5");
        }
        public string AsF1FormattedConcentration(bool withunit)
        {
            if (this.HasError) return AsFormattedConcentration(ConcentrationUnitAttribute.FromConc(this.Value), withunit);
            else
            {
                var _ = AppSettings.NumberPrecision;
                AppSettings.NumberPrecision = NumberPrecision.SingleDecimal;
                var s = AsFormattedConcentration(ConcentrationUnitAttribute.FromConc(this.Value), withunit);
                AppSettings.NumberPrecision = _;
                return s;
            }
        }
        public string AsFormattedConcentration(bool withunit) => AsFormattedConcentration(ConcentrationUnitAttribute.FromConc(this.Value), withunit);
        public string AsFormattedConcentration(ConcentrationUnit unit, bool withunit = true) => WithMod(unit.GetMod(), unit.GetName(), withunit);

        public string AsFormattedEnergy(EnergyUnit unit, string suffix, bool withunit = true) => WithMod(unit.GetMod(), suffix, withunit);

        string WithMod(double mod, string unit, bool withunit)
        {
            var value = mod * this;
            double logerror;

            switch (AppSettings.NumberPrecision)
            {
                case NumberPrecision.Standard when HasError:
                    logerror = Math.Log10(value.SD * 0.5); // Add digit for errors less than 2
                    break;
                case NumberPrecision.Strict when HasError:
                    logerror = Math.Log10(value.SD);
                    break;
                case NumberPrecision.SingleDecimal:
                    return withunit ? value.ToString("F1") + " " + unit : value.ToString("F1");
                case NumberPrecision.AllDecimals:
                    return withunit ? value.ToString("G5") + " " + unit : value.ToString("G5");
                default: return withunit ? value.Value.ToString("G5") + " " + unit : value.Value.ToString("G5");
            }

            double floor = Math.Floor(logerror);
            int digits = (int)floor;
            double scale = Math.Pow(10, digits);
            string formatString = $"F{Math.Max(0, -digits)}";
            double roundedNumber = FWEMath.RoundApproximate(value.Value / scale) * scale;
            double roundedError = FWEMath.RoundApproximate(value.SD / scale) * scale;
            var output = roundedNumber.ToString(formatString) + " ± " + roundedError.ToString(formatString);

            return withunit ? output + " " + unit : output;
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
}
