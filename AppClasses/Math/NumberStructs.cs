using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AnalysisITC
{
    public struct FloatWithError : IComparable
    {
        const double asymmetry_threshold = 0.13;

        private double asymmscore = 0.0;
        private bool isnan = false;

        public double Value { get; private set; } = 0;
        public double SD { get; private set; } = 0;
        public double[] DistributionConfidence95 { get; private set; } = new double[] { 0, 0 };
        public bool IsAsymmetric { get; private set; } = false;

        public readonly double Lower => DistributionConfidence95?[0] ?? Value;
        public readonly double Upper => DistributionConfidence95?[1] ?? Value;
        public readonly double LowerWidth => Value - Lower;
        public readonly double UpperWidth => Upper - Value;

        public readonly double FractionSD
        {
            get
            {
                if (SD < double.Epsilon) return 0;
                else if (Math.Abs(Value) < double.Epsilon) return 0;
                else return Math.Abs(SD / Value);
            }
        }
        public readonly bool HasError
        {
            get
            {
                if (Math.Abs(Value) < float.Epsilon) return SD > 10E-8;
                else return SD / Math.Abs(Value) > 10E-8;
            }
        }

        public readonly Energy Energy => new Energy(this);

        public static FloatWithError NaN
        {
            get => new FloatWithError() { isnan = true };
        }

        public static bool IsNaN(FloatWithError v) => v.isnan;

        public FloatWithError()
        {

        }

        public FloatWithError(double value = 0, double error = 0)
        {
            Value = value;
            SD = Math.Abs(error);
            DistributionConfidence95 = new double[] { Value - 1.96 * SD, Value + 1.96 * SD };

            IsAsymmetric = false;
        }

        public FloatWithError(double value, double error, double lower, double upper)
        {
            Value = value;
            SD = Math.Abs(error);
            DistributionConfidence95 = new[] { lower, upper };

            IsAsymmetric = false;

            // Sets proper intervals and asymmetry
            SetConfidenceInterval(lower, upper);
        }

        public FloatWithError(IEnumerable<double> distribution, double? mean = null)
        {
            var list = distribution?.ToList();

            if (list != null && list.Any())
            {
                double error = 0;
                double average = list.Average();
                if (mean != null) average = (double)mean;
                double sum = list.Sum(d => Math.Pow(d - average, 2));
                error = Math.Sqrt((sum) / (list.Count() - 1));

                Value = average;
                SD = Math.Abs(error);
                DistributionConfidence95 = GetConfidenceInterval(list);
                IsAsymmetric = false;
                SetAsymmetricError();
            }
            else if (mean != null) this = new FloatWithError((double)mean, 0);
            else this = new FloatWithError(0, 0);
        }

        public FloatWithError(List<FloatWithError> distribution, double? mean = null)
        {
            if (distribution != null && distribution.Any())
            {
                var rng = new Random();
                double error = 0;
                double average = 0;
                int n = Math.Clamp(10000 / distribution.Count(), 10, 200);
                var samples = new List<double>(n * distribution.Count());

                foreach (var fwe in distribution)
                    for (int i = 0; i < n; i++)
                        samples.Add(fwe.Sample(rng));

                average = samples.Average();
                if (mean != null) average = (double)mean;
                double sum = samples.Sum(d => Math.Pow(d - average, 2));
                error = Math.Sqrt((sum) / (samples.Count() - 1));

                Value = average;
                SD = Math.Abs(error);
                DistributionConfidence95 = GetConfidenceInterval(samples);
                IsAsymmetric = false;

                SetAsymmetricError();
            }
            else if (mean != null) this = new FloatWithError((double)mean, 0);
            else this = new FloatWithError(0, 0);
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

            SetAsymmetricError();
        }

        static double[] GetConfidenceInterval(List<double> distribution)
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

        /// <summary>
        /// Set the confidence interval of the number. The lower value is set to index 0 and the higher index 1
        /// </summary>
        public void SetConfidenceInterval(double b1, double b2)
        {
            if (b1 > b2) DistributionConfidence95 = new[] { b2, b1 };
            else DistributionConfidence95 = new[] { b1, b2 };

            SetAsymmetricError();
        }

        void SetAsymmetricError()
        {
            asymmscore = GetConfidenceIntervalAsymmetryScore();

            if (double.IsNaN(asymmscore)) IsAsymmetric = false;
            else
            {
                IsAsymmetric = asymmscore >= asymmetry_threshold;
            }
        }

        private double GetConfidenceIntervalAsymmetryScore()
        {
            double a = Value - Lower;
            double b = Upper - Value;

            if (a <= 0 || b <= 0) return double.NaN;

            return Math.Abs(b - a) / (a + b);
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
                case ConfidenceLevel.Conf95: return DistributionConfidence95;
                case ConfidenceLevel.SD: return new double[] { Value - LowerWidth * 0.5, Value + UpperWidth * 0.5 };
                case ConfidenceLevel.Conf50: return new double[] { Value - LowerWidth * 0.25, Value + UpperWidth * 0.25 };
            }
        }

        public double Sample(Random rand = null)
        {
            return Distribution.Normal(this, rand);
        }

        #region operators

        private static double Quad(double a, double b)
        {
            return Math.Sqrt(a * a + b * b);
        }

        public static FloatWithError operator +(FloatWithError v1, FloatWithError v2)
        {
            var v = v1.Value + v2.Value;
            var sd = Math.Sqrt(v1.SD * v1.SD + v2.SD * v2.SD);

            double lowerWidth = Quad(v1.LowerWidth, v2.LowerWidth);
            double upperWidth = Quad(v1.UpperWidth, v2.UpperWidth);

            double lowerCI = v - lowerWidth;
            double upperCI = v + upperWidth;

            return new FloatWithError(v, sd, lowerCI, upperCI);
        }

        public static FloatWithError operator +(FloatWithError v1, double scalar)
        {
            var v = v1.Value + scalar;

            return new FloatWithError(v, v1.SD, v1.Lower + scalar, v1.Upper + scalar);
        }

        public static FloatWithError operator -(FloatWithError v1, FloatWithError v2)
        {
            var v = v1.Value - v2.Value;
            var sd = Math.Sqrt(v1.SD * v1.SD + v2.SD * v2.SD);

            double lowerWidth = Quad(v1.LowerWidth, v2.UpperWidth);
            double upperWidth = Quad(v1.UpperWidth, v2.LowerWidth);

            double lowerCI = v - lowerWidth;
            double upperCI = v + upperWidth;

            return new FloatWithError(v, sd, lowerCI, upperCI);
        }

        public static FloatWithError operator -(FloatWithError v1, double scalar)
        {
            var v = v1.Value - scalar;

            return new FloatWithError(v, v1.SD, v1.Lower - scalar, v1.Upper - scalar);
        }

        public static FloatWithError operator /(FloatWithError v1, FloatWithError v2)
        {
            return FWEMath.Divide(v1, v2);
        }

        public static FloatWithError operator /(FloatWithError v1, double scalar)
        {
            var v = v1.Value / scalar;

            return new FloatWithError(v, v1.FractionSD * v, v1.Lower / scalar, v1.Upper / scalar);
        }

        public static FloatWithError operator /(double scalar, FloatWithError v2)
        {
            var v = scalar / v2.Value;

            return new FloatWithError(v, v2.FractionSD * v, scalar / v2.Upper, scalar / v2.Lower);
        }

        public static FloatWithError operator *(FloatWithError v1, FloatWithError v2)
        {
            return FWEMath.Multiply(v1, v2);
        }

        public static FloatWithError operator *(FloatWithError v1, double scalar) => scalar * v1;

        public static FloatWithError operator *(double scalar, FloatWithError v1)
        {
            var v = v1.Value * scalar;

            return new FloatWithError(v, v1.FractionSD * v, scalar * v1.Lower, scalar * v1.Upper);
        }

        public static explicit operator FloatWithError(double v)
        {
            return new FloatWithError(v);
        }

        public static implicit operator double(FloatWithError v)
        {
            return v.Value;
        }

        #endregion

        #region tostring

        public override string ToString()
        {
            return ToString("G3") + " | " + (100 * FractionSD).ToString("F1") + "%";
        }

        public string ToString(string format = "F1")
        {
            if (!HasError) return Value.ToString(format);
            else return Value.ToString(format) + " ± " + SD.ToString(format);
        }

        public string AsNumber()
        {
            return WithMod(1, "", false, false);
        }

        public string AsConcentration(ConcentrationUnit unit, bool withunit = true)
        {
            var value = unit.GetMod() * this;

            return withunit ? value.ToString("G5") + " " + unit.GetName() : value.ToString("G5");
        }
        public string AsF1FormattedConcentration(bool withunit)
        {
            if (this.HasError) return AsFormattedConcentration(ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(this.Value), withunit);
            else
            {
                var _ = AppSettings.NumberPrecision;
                AppSettings.NumberPrecision = NumberPrecision.SingleDecimal;
                var s = AsFormattedConcentration(ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(this.Value), withunit);
                AppSettings.NumberPrecision = _;
                return s;
            }
        }
        public string AsFormattedConcentration(bool withunit, bool withci = false) => AsFormattedConcentration(ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(this.Value), withunit, withci);
        public string AsFormattedConcentration(ConcentrationUnit unit, bool withunit = true, bool withci = false) => WithMod(unit.GetMod(), unit.GetName(), withunit, withci);

        public string AsFormattedEnergy(EnergyUnit unit, string suffix, bool withunit = true, bool withci = false) => WithMod(unit.GetMod(), suffix, withunit, withci);

        #endregion

        string WithMod(double mod, string unit, bool withunit, bool withci)
        {
            var value = mod * this;
            double logerror;
            string s;
            switch (AppSettings.NumberPrecision)
            {
                case NumberPrecision.Standard when HasError:
                    logerror = Math.Log10(value.SD * 0.5); // Add digit for errors less than 2
                    break;
                case NumberPrecision.Strict when HasError:
                    logerror = Math.Log10(value.SD);
                    break;
                case NumberPrecision.SingleDecimal:
                    s = withunit ? value.ToString("F1") + " " + unit : value.ToString("F1");
                    if (withci) s += ConfidenceIntervalString(Lower.ToString("F1"), Upper.ToString("F1"));
                    return s;
                case NumberPrecision.AllDecimals:
                    s = withunit ? value.ToString("G5") + " " + unit : value.ToString("G5");
                    if (withci) s += ConfidenceIntervalString(Lower.ToString("G5"), Upper.ToString("G5"));
                    return s;
                default: return withunit ? value.Value.ToString("G5") + " " + unit : value.Value.ToString("G5");
            }

            double floor = Math.Floor(logerror);
            int digits = (int)floor;
            double scale = Math.Pow(10, digits);
            string format = $"F{Math.Max(0, -digits)}";
            double roundedNumber = FWEMath.RoundApproximate(value.Value / scale) * scale;
            double roundedError = FWEMath.RoundApproximate(value.SD / scale) * scale;
            var output = roundedNumber.ToString(format) + " ± " + roundedError.ToString(format);

            s = withunit ? output + " " + unit : output;

            if (withci) s += ConfidenceIntervalString(value.Lower.ToString(format),value. Upper.ToString(format));

            return s;
        }

        readonly string ConfidenceIntervalString(string lower, string upper) => $" [{lower},{upper}]";

        public int CompareTo(object obj)
        {
            if (obj == null) return 1;

            if (obj is FloatWithError) return Value.CompareTo(((FloatWithError)obj).Value);
            else throw new Exception("value not FWE");
        }

        public string ToSaveString()
        {
            if (DistributionConfidence95 != null || !HasError) return $"{Value},{SD},{DistributionConfidence95[0]},{DistributionConfidence95[1]}";
            else return $"{Value},{SD}";
        }

        public static FloatWithError FromSaveString(string s)
        {
            var pars = s.Split(',').Select(ss => double.Parse(ss)).ToList();

            var fwe = new FloatWithError()
            {
                Value = pars[0],
                SD = pars[1], 
            };

            if (pars.Count > 2) fwe.SetConfidenceInterval(pars[0], pars[1]);
            else fwe.SetAsymmetricError();

            return fwe;
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
