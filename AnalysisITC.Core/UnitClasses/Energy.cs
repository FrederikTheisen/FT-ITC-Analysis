using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Units
{
    /// <summary>
    /// The energy family used for automatic presentation.  This is deliberately
    /// separate from <see cref="EnergyUnit"/>: import and interchange formats
    /// still use the exact unit enum, while the application preference chooses
    /// between base and kilo prefixes.
    /// </summary>
    public enum EnergyUnitFamily
    {
        Joules = 0,
        Calories = 1,
    }

    /// <summary>
    /// Resolves a display unit from central values stored internally in joules.
    /// Error/uncertainty values are intentionally not accepted by this API, so
    /// they cannot change a unit selected from the plotted or tabulated values.
    /// </summary>
    public static class EnergyUnitResolver
    {
        public const double JouleThreshold = 100.0;
        public const double CalorieThresholdJoules = 418.4;

        public static EnergyUnit Resolve(EnergyUnitFamily family, IEnumerable<double> centralValues)
        {
            var largest = LargestFiniteNonZeroMagnitude(centralValues);
            var normalizedFamily = NormalizeFamily(family);

            if (!largest.HasValue)
                return normalizedFamily == EnergyUnitFamily.Calories ? EnergyUnit.KCal : EnergyUnit.KiloJoule;

            if (normalizedFamily == EnergyUnitFamily.Calories)
            {
                return largest.Value < CalorieThresholdJoules
                    ? EnergyUnit.Cal
                    : EnergyUnit.KCal;
            }

            return largest.Value < JouleThreshold ? EnergyUnit.Joule : EnergyUnit.KiloJoule;
        }

        public static EnergyUnit Resolve(EnergyUnitFamily family, params double[] centralValues)
        {
            return Resolve(family, (IEnumerable<double>)centralValues);
        }

        public static EnergyUnit Resolve(EnergyUnitFamily family, double centralValue)
        {
            return Resolve(family, new[] { centralValue });
        }

        public static EnergyUnit ResolveAutomatic(EnergyUnitFamily family, IEnumerable<double> centralValues)
        {
            return Resolve(family, centralValues);
        }

        public static EnergyUnit ResolveAutomatic(EnergyUnitFamily family, params double[] centralValues)
        {
            return Resolve(family, centralValues);
        }

        public static EnergyUnit Resolve(EnergyUnitFamily family, IEnumerable<FloatWithError> values)
        {
            return Resolve(family, values == null ? null : values.Select(value => value.Value));
        }

        public static EnergyUnit Resolve(EnergyUnitFamily family, EnergyUnit? energyUnitOverride, IEnumerable<double> centralValues)
        {
            if (energyUnitOverride.HasValue)
            {
                ValidateOverride(energyUnitOverride.Value);
                return energyUnitOverride.Value;
            }

            return Resolve(family, centralValues);
        }

        public static EnergyUnit Resolve(EnergyUnitFamily family, EnergyUnit? energyUnitOverride, IEnumerable<FloatWithError> values)
        {
            return Resolve(
                family,
                energyUnitOverride,
                values == null ? null : values.Select(value => value.Value));
        }

        public static bool IsValidOverride(EnergyUnit? energyUnitOverride)
        {
            if (!energyUnitOverride.HasValue) return true;

            return energyUnitOverride.Value == EnergyUnit.Joule
                || energyUnitOverride.Value == EnergyUnit.KiloJoule
                || energyUnitOverride.Value == EnergyUnit.Cal
                || energyUnitOverride.Value == EnergyUnit.KCal;
        }

        public static void ValidateOverride(EnergyUnit energyUnitOverride)
        {
            if (energyUnitOverride == EnergyUnit.MicroCal)
                throw new ArgumentException("Microcalories are reserved for thermogram power and integrated heat and cannot be used as a figure or result-export override.", nameof(energyUnitOverride));

            if (energyUnitOverride != EnergyUnit.Joule
                && energyUnitOverride != EnergyUnit.KiloJoule
                && energyUnitOverride != EnergyUnit.Cal
                && energyUnitOverride != EnergyUnit.KCal)
                throw new ArgumentOutOfRangeException(nameof(energyUnitOverride), energyUnitOverride, "Unknown energy-unit override.");
        }

        public static EnergyUnitFamily FamilyOf(EnergyUnit unit)
        {
            return unit == EnergyUnit.MicroCal || unit == EnergyUnit.Cal || unit == EnergyUnit.KCal
                ? EnergyUnitFamily.Calories
                : EnergyUnitFamily.Joules;
        }

        public static EnergyUnit DefaultUnit(EnergyUnitFamily family)
        {
            return NormalizeFamily(family) == EnergyUnitFamily.Calories ? EnergyUnit.KCal : EnergyUnit.KiloJoule;
        }

        static EnergyUnitFamily NormalizeFamily(EnergyUnitFamily family)
        {
            return Enum.IsDefined(typeof(EnergyUnitFamily), family) ? family : EnergyUnitFamily.Joules;
        }

        static double? LargestFiniteNonZeroMagnitude(IEnumerable<double> values)
        {
            if (values == null) return null;

            double largest = 0;
            var found = false;
            foreach (var value in values)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                var magnitude = Math.Abs(value);
                if (magnitude <= 0) continue;
                if (!found || magnitude > largest) largest = magnitude;
                found = true;
            }

            return found ? largest : (double?)null;
        }
    }

    /// <summary>Names and scales for the two non-molar thermogram quantities.</summary>
    public static class ThermogramUnits
    {
        public static string DifferentialPowerUnit(EnergyUnitFamily family)
        {
            return NormalizeFamily(family) == EnergyUnitFamily.Calories ? "µcal/s" : "µW";
        }

        public static string IntegratedHeatUnit(EnergyUnitFamily family)
        {
            return NormalizeFamily(family) == EnergyUnitFamily.Calories ? "µcal" : "µJ";
        }

        // Aliases keep the helper pleasant to consume from UI and exporter code.
        public static string GetDifferentialPowerUnit(EnergyUnitFamily family) => DifferentialPowerUnit(family);
        public static string GetIntegratedHeatUnit(EnergyUnitFamily family) => IntegratedHeatUnit(family);
        public static string GetThermogramPowerUnit(EnergyUnitFamily family) => DifferentialPowerUnit(family);
        public static string GetThermogramHeatUnit(EnergyUnitFamily family) => IntegratedHeatUnit(family);

        /// <summary>Converts internal joules per second to the family power label.</summary>
        public static double DifferentialPowerScale(EnergyUnitFamily family)
        {
            return NormalizeFamily(family) == EnergyUnitFamily.Calories
                ? 1_000_000.0 / Energy.CalToJouleFactor
                : 1_000_000.0;
        }

        /// <summary>Converts internal joules to the family integrated-heat label.</summary>
        public static double IntegratedHeatScale(EnergyUnitFamily family)
        {
            return DifferentialPowerScale(family);
        }

        public static double GetDifferentialPowerScale(EnergyUnitFamily family) => DifferentialPowerScale(family);
        public static double GetIntegratedHeatScale(EnergyUnitFamily family) => IntegratedHeatScale(family);

        static EnergyUnitFamily NormalizeFamily(EnergyUnitFamily family)
        {
            return Enum.IsDefined(typeof(EnergyUnitFamily), family) ? family : EnergyUnitFamily.Joules;
        }
    }

    public static class EnergyUnitFamilyExtensions
    {
        public static EnergyUnitFamily GetFamily(this EnergyUnit unit) => EnergyUnitResolver.FamilyOf(unit);
        public static EnergyUnit GetDefaultUnit(this EnergyUnitFamily family) => EnergyUnitResolver.DefaultUnit(family);
        public static string GetDifferentialPowerUnit(this EnergyUnitFamily family) => ThermogramUnits.DifferentialPowerUnit(family);
        public static string GetIntegratedHeatUnit(this EnergyUnitFamily family) => ThermogramUnits.IntegratedHeatUnit(family);
    }

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

        public string ToFormattedString(EnergyUnit unit, bool withunit = true, bool permole = false, bool perK = false, bool withci = false, UncertaintyDisplayStyle? style = null)
        {
            var suffix = withunit ? unit.GetUnit() : "";
            suffix += Suffix(permole, perK);

            return FloatWithError.AsFormattedEnergy(unit, suffix, withunit, withci, style);
        }

        public string ToString(EnergyUnit unit, string formatter, bool withunit = true, bool permole = false, bool perK = false, UncertaintyDisplayStyle? style = null)
        {
            var suffix = withunit ? unit.GetUnit() : "";
            suffix += Suffix(permole, perK);

            var output = unit switch
            {
                EnergyUnit.Joule => FloatWithError.ToString(formatter, style),
                EnergyUnit.MicroCal => (1000000 * JouleToCalFactor * FloatWithError).ToString(formatter, style),
                EnergyUnit.Cal => (JouleToCalFactor * FloatWithError).ToString(formatter, style),
                EnergyUnit.KiloJoule => (FloatWithError / 1000).ToString(formatter, style),
                EnergyUnit.KCal => (JouleToCalFactor * FloatWithError / 1000).ToString(formatter, style),
                _ => FloatWithError.ToString(formatter, style),
            };

            return withunit && !string.IsNullOrWhiteSpace(suffix) ? output + " " + suffix : output;
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

        public static List<EnergyUnit> GetSelectableUnits()
        {
            return new List<EnergyUnit>()
            {
                EnergyUnit.Joule,
                EnergyUnit.KiloJoule,
                EnergyUnit.MicroCal,
                EnergyUnit.Cal,
                EnergyUnit.KCal
            };
        }
    }

    public enum EnergyUnit
    {
        [EnergyUnit("kilojoule", "kJ")]
        KiloJoule,
        [EnergyUnit("joule", "J")]
        Joule,
        [EnergyUnit("microcalorie", "µcal")]
        MicroCal,
        [EnergyUnit("calorie", "cal")]
        Cal,
        [EnergyUnit("kilocalorie", "kcal")]
        KCal
    }
}
