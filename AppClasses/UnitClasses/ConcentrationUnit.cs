using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnalysisITC
{
    public static partial class Extensions
    {
        public static ConcentrationUnitAttribute GetProperties(this ConcentrationUnit value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(ConcentrationUnitAttribute), false).FirstOrDefault() as ConcentrationUnitAttribute;

            return attribute;
        }

        public static string GetName(this ConcentrationUnit value)
        {
            return value.GetProperties().Name;
        }

        /// <summary>
        /// Factor to from Molar to the current unit (eg. 1 for 'M' and 1000 for 'mM')
        /// </summary>
        public static double GetMod(this ConcentrationUnit value)
        {
            return value.GetProperties().Mod;
        }
    }

    public class ConcentrationUnitAttribute : Attribute
    {
        public string Name { get; set; }
        /// <summary>
        /// Factor to from Molar to the current unit (eg. 1 for 'M' and 1000 for 'mM')
        /// </summary>
        public double Mod { get; set; }

        public ConcentrationUnitAttribute(string name, double mod)
        {
            Name = name;
            Mod = mod;
        }

        public static ConcentrationUnit FromMag(double mag)
        {
            return mag switch
            {
                > 0 => ConcentrationUnit.M,
                > -3 => ConcentrationUnit.mM,
                > -6 => ConcentrationUnit.µM,
                > -9 => ConcentrationUnit.nM,
                > -12 => ConcentrationUnit.pM,
                _ => ConcentrationUnit.pM
            };
        }

        public static ConcentrationUnit FromConc(double conc)
        {
            var mag = Math.Log10(conc);

            return mag switch
            {
                > 0 => ConcentrationUnit.M,
                > -3 => ConcentrationUnit.mM,
                > -6 => ConcentrationUnit.µM,
                > -9 => ConcentrationUnit.nM,
                > -12 => ConcentrationUnit.pM,
                _ => ConcentrationUnit.pM
            };
        }

        public static ConcentrationUnit? FromString(string s)
        {
            return s.ToLower() switch
            {
                "," => ConcentrationUnit.M,
                "mm" => ConcentrationUnit.mM,
                "um" => ConcentrationUnit.µM,
                "nm" => ConcentrationUnit.nM,
                "pm" => ConcentrationUnit.pM,
                _ => null
            };
        }

        static bool TryParseUnit(string s, out ConcentrationUnit unit)
        {
            unit = ConcentrationUnit.mM;
            if (string.IsNullOrWhiteSpace(s)) return false;

            var t = s.Trim().ToLowerInvariant();
            t = t.Replace("μ", "µ"); // normalize mu

            // Accept common spellings
            switch (t)
            {
                case "m": unit = ConcentrationUnit.M; return true;
                case "mm":
                case "mmol":
                case "mmolar":
                case "mmol/l":
                case "mmol/l.": unit = ConcentrationUnit.mM; return true;

                case "um":
                case "µm":
                case "μm":
                case "umol":
                case "µmol":
                case "umol/l":
                case "µmol/l": unit = ConcentrationUnit.µM; return true;

                case "nm": unit = ConcentrationUnit.nM; return true;
                case "pm": unit = ConcentrationUnit.pM; return true;

                default: return false;
            }
        }

        public static bool TryExtractConcentrationM(string text, string name, out double concM)
        {
            concM = 0;

            // After:  "NaCl 150 mM" / "NaCl:150mM" / "NaCl=150 mM"
            var esc = Regex.Escape(name);
            var rxAfter = new Regex(
                $@"(?<![A-Za-z0-9]){esc}(?![A-Za-z0-9])\s*[:=]?\s*([+-]?\d+(?:[.,]\d+)?)\s*([A-Za-zµμ]+)?",
                RegexOptions.IgnoreCase);

            var m = rxAfter.Match(text);
            if (m.Success)
            {
                if (!TryParseNumber(m.Groups[1].Value, out var v)) return false;

                var unit = ConcentrationUnit.mM; // default if omitted
                if (TryParseUnit(m.Groups[2].Value, out var u)) unit = u;

                concM = v / unit.GetMod();
                return true;
            }

            // Before: "150 mM NaCl"
            var rxBefore = new Regex(
                $@"([+-]?\d+(?:[.,]\d+)?)\s*([A-Za-zµμ]+)?\s*(?<![A-Za-z0-9]){esc}(?![A-Za-z0-9])",
                RegexOptions.IgnoreCase);

            m = rxBefore.Match(text);
            if (m.Success)
            {
                if (!TryParseNumber(m.Groups[1].Value, out var v)) return false;

                var unit = ConcentrationUnit.mM; // default if omitted
                if (TryParseUnit(m.Groups[2].Value, out var u)) unit = u;

                concM = v / unit.GetMod();
                return true;
            }

            return false;

            static bool TryParseNumber(string s, out double v)
            {
                v = 0;
                if (string.IsNullOrWhiteSpace(s)) return false;

                // allow both "7.4" and "7,4"
                var t = s.Trim();
                if (t.Count(c => c == ',') == 1 && !t.Contains('.')) t = t.Replace(',', '.');

                return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            }
        }
    }

    public enum ConcentrationUnit
    {
        [ConcentrationUnit("M", 1)]
        M,
        [ConcentrationUnit("mM", 1000)]
        mM,
        [ConcentrationUnit("µM", 1000000)]
        µM,
        [ConcentrationUnit("nM", 1000000000)]
        nM,
        [ConcentrationUnit("pM", 1000000000000)]
        pM
    }
}
