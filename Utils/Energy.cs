using System;
using System.Collections.Generic;

namespace AnalysisITC
{
    public struct Energy : IComparable
    {
        public const double CalToJouleFactor = 4.184;
        public const double JouleToCalFactor = 1 / 4.184;
        const double MicroFactor = 0.000001;
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

        public static Energy FromDistribution(IEnumerable<double> dist, double? mean = null) => new Energy(new FloatWithError(dist, mean));

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

        public string ToString(string formatter)
        {
            return FloatWithError.ToString(formatter);
        }

        public static string Suffix(bool permole = false, bool perK = false)
        {
            string suffix = "";

            if (permole) suffix += "/mol";
            if (perK) suffix += "·K";

            return suffix;
        }

        public string ToString(EnergyUnit unit, string formatter = "F1", bool withunit = true, bool permole = false, bool perK = false)
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

    public class EnergyUnitAttribute : Attribute
    {
        public string Unit { get; set; }
        public string LongName { get; set; }

        public EnergyUnitAttribute(string shortname, string longname)
        {
            LongName = shortname;
            Unit = longname;
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
