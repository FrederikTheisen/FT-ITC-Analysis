using System;
using System.Collections.Generic;

namespace AnalysisITC
{
    public struct Energy : IComparable
    {
        public const double CalToJouleFactor = 4.184;
        public const double JouleToCalFactor = 1 / 4.184;
        private const double MicroFactor = 0.000001;
        public static readonly Energy R = new Energy(8.3145);

        public FloatWithError FloatWithError { get; set; }
        public double Value => FloatWithError.Value;
        public double SD => FloatWithError.SD;

        public Energy(FloatWithError v)
        {
            FloatWithError = v;
        }

        public Energy(double v)
        {
            FloatWithError = new(v);
        }

        public Energy(double v, double e)
        {
            FloatWithError = new(v, e);
        }

        public Energy(FloatWithError value, EnergyUnit unit)
        {
            FloatWithError = value / ScaleFactor(unit);
        }

        public static Energy FromDistribution(IEnumerable<double> dist, double? mean = null) => new Energy(new FloatWithError(dist, mean));

        public Energy ToUnit(EnergyUnit to)
        {
            switch (to)
            {
                case EnergyUnit.MicroCal: return JouleToCalFactor * 1000000 * this;
                case EnergyUnit.Cal: return this * JouleToCalFactor;
                case EnergyUnit.KiloJoule: return this / 1000;
                case EnergyUnit.KCal: return this.ToUnit(EnergyUnit.Cal) / 1000;
                case EnergyUnit.Joule:
                default: return this;
            }
        }

        public static double ConvertToJoule(double value, EnergyUnit from)
        {
            switch (from)
            {
                case EnergyUnit.MicroCal: return MicroFactor * CalToJouleFactor * value;
                case EnergyUnit.Cal: return CalToJouleFactor * value;
                case EnergyUnit.KiloJoule: return 1000 * value;
                case EnergyUnit.KCal: return ConvertToJoule(1000 * value, EnergyUnit.Cal);
                case EnergyUnit.Joule:
                default: return value;
            }
        }

        public static double ConvertFromJoule(double value, EnergyUnit to)
        {
            switch (to)
            {
                case EnergyUnit.MicroCal: return JouleToCalFactor * 1000000 * value;
                case EnergyUnit.Cal: return value * JouleToCalFactor;
                case EnergyUnit.KiloJoule: return value / 1000;
                case EnergyUnit.KCal: return ConvertFromJoule(value / 1000, EnergyUnit.Cal);
                case EnergyUnit.Joule:
                default: return value;
            }
        }

        public static double ScaleFactor(EnergyUnit unit)
        {
            switch (unit)
            {
                default:
                case EnergyUnit.Joule: return 1;
                case EnergyUnit.KiloJoule: return 1 / 1000.0;
                case EnergyUnit.Cal: return 1 / CalToJouleFactor;
                case EnergyUnit.KCal: return 1 / (1000 * CalToJouleFactor);
            }
        }

        public static Energy operator +(Energy e1, Energy e2)
        {
            var v = e1.FloatWithError + e2.FloatWithError;

            return new Energy(v);
        }

        public static Energy operator -(Energy e1, Energy e2)
        {
            var v = e1.FloatWithError - e2.FloatWithError;

            return new Energy(v);
        }

        public static Energy operator /(Energy e1, Energy e2) => new Energy(e1.FloatWithError / e2.FloatWithError);

        public static Energy operator /(Energy e1, double val) => new Energy(e1.FloatWithError / val);

        public static Energy operator *(Energy e1, Energy e2) => new Energy(e1.FloatWithError * e2.FloatWithError);

        public static Energy operator *(Energy e1, double val) => new Energy(e1.FloatWithError * val);

        public static Energy operator *(double val, Energy e) => new Energy(e.FloatWithError * val);

        public static implicit operator double(Energy e) => e.FloatWithError.Value;

        //TODO add unit to print
        public override string ToString()
        {
            return FloatWithError.ToString();
        }

        public string Suffix(bool permole = false, bool perK = false)
        {
            string suffix = "";

            if (permole) suffix += "/mol";
            if (perK) suffix += "·K";

            return suffix;
        }

        public string ToFormattedString(EnergyUnit unit, bool withunit = true, bool permole = false, bool perK = false)
        {
            var suffix = withunit ? unit.GetUnit() : "";
            suffix += Suffix(permole, perK);

            return FloatWithError.AsFormattedEnergy(unit, suffix, withunit);
        }

        public string ToString(EnergyUnit unit, string formatter, bool withunit = true, bool permole = false, bool perK = false)
        {
            var suffix = withunit ? unit.GetUnit() : "";
            suffix += Suffix(permole, perK);

            return unit switch
            {
                EnergyUnit.Joule => FloatWithError.ToString(formatter) + " " + suffix,
                EnergyUnit.MicroCal => (1000000 * JouleToCalFactor * FloatWithError).ToString(formatter) + " " + suffix,
                EnergyUnit.Cal => (JouleToCalFactor * FloatWithError).ToString(formatter) + " " + suffix,
                EnergyUnit.KiloJoule => (FloatWithError / 1000).ToString(formatter) + " " + suffix,
                EnergyUnit.KCal => (JouleToCalFactor * FloatWithError / 1000).ToString(formatter) + " " + suffix,
                _ => FloatWithError.ToString(formatter) + " " + suffix,
            };
        }

        public int CompareTo(object obj)
        {
            if (obj == null) return 1;

            Energy otherEnergy = (Energy)obj;

            if (obj is Energy) return Value.CompareTo(otherEnergy.Value);
            else throw new Exception("value not Energy");
        }
    }

    public static partial class Extensions
    {
        public static bool IsSI(this EnergyUnit unit) => unit switch
        {
            EnergyUnit.KiloJoule => true,
            EnergyUnit.Joule => true,
            EnergyUnit.MicroCal => false,
            EnergyUnit.Cal => false,
            EnergyUnit.KCal => false,
            _ => true,
        };

        public static string GetUnit(this EnergyUnit value) => value.GetProperties().Unit;

        public static string GetName(this EnergyUnit value)
        {
            return value.GetProperties().LongName;
        }

        /// <summary>
        /// Factor to from Molar to the current unit (eg. 1 for 'J' and 0.001 for 'kJ')
        /// </summary>
        public static double GetMod(this EnergyUnit value)
        {
            return Energy.ScaleFactor(value);
        }
    }

        public class EnergyUnitAttribute : Attribute
    {
        public string Unit { get; set; }
        public string LongName { get; set; }

        public EnergyUnitAttribute(string name, string unit)
        {
            LongName = name;
            Unit = unit;
        }
    }

    //TODO add attribute with unit names and stuff
    public enum EnergyUnit
    {
        [EnergyUnit("kiloJoule", "kJ")]
        KiloJoule,
        [EnergyUnit("Joule", "J")]
        Joule,
        [EnergyUnit("microcalorie", "µcal")]
        MicroCal,
        [EnergyUnit("calorie", "cal")]
        Cal,
        [EnergyUnit("kilocalorie", "kcal")]
        KCal
    }
}
