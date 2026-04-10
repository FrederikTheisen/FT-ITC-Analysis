using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AnalysisITC
{
    public static class ConcentrationParser
    {
        private static readonly Regex NumberRegex = new Regex(
            @"[-+]?(?:\d+(?:[.,]\d+)?|[.,]\d+)(?:[eE][-+]?\d+)?",
            RegexOptions.Compiled);

        public static ConcentrationUnit FromString(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return AppSettings.DefaultConcentrationUnit;

            string unitToken = ExtractUnitToken(s);

            return unitToken switch
            {
                "m" => ConcentrationUnit.M,
                "mm" => ConcentrationUnit.mM,
                "um" => ConcentrationUnit.µM,
                "µm" => ConcentrationUnit.µM,
                "nm" => ConcentrationUnit.nM,
                "pm" => ConcentrationUnit.pM,
                _ => AppSettings.DefaultConcentrationUnit
            };
        }

        public static bool TryParseMolarConcentration(string s, out FloatWithError result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(s))
                return false;

            var matches = NumberRegex.Matches(s);
            if (matches.Count == 0 || matches.Count > 2)
                return false;

            if (!TryParseDouble(matches[0].Value, out double value))
                return false;

            double error = 0.0;
            if (matches.Count == 2 && !TryParseDouble(matches[1].Value, out error))
                return false;

            var unit = FromString(s);
            double factor = ToMolarFactor(unit);

            result = new FloatWithError(value * factor, Math.Abs(error) * factor);
            return true;
        }

        private static string ExtractUnitToken(string s)
        {
            // First remove numeric parts, including scientific notation.
            string withoutNumbers = NumberRegex.Replace(s, "");

            var sb = new StringBuilder(withoutNumbers.Length);

            foreach (char ch in withoutNumbers)
            {
                if (char.IsLetter(ch) || ch == 'µ' || ch == 'μ')
                {
                    char c = char.ToLowerInvariant(ch);

                    // Normalize micro symbols to plain 'u'
                    if (c == 'µ' || c == 'μ')
                        c = 'u';

                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static bool TryParseDouble(string s, out double value)
        {
            // Accept both dot and comma decimals in a simple way.
            string normalized = s.Replace(',', '.');

            return double.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static double ToMolarFactor(ConcentrationUnit unit)
        {
            return unit switch
            {
                ConcentrationUnit.M => 1.0,
                ConcentrationUnit.mM => 1e-3,
                ConcentrationUnit.µM => 1e-6,
                ConcentrationUnit.nM => 1e-9,
                ConcentrationUnit.pM => 1e-12,
                _ => 1.0
            };
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

        private static ConcentrationUnit FromMag(double mag)
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

        public static ConcentrationUnit GetMagnitudeUnitFromConcentration(double conc)
        {
            var mag = Math.Log10(conc);

            return FromMag(mag);
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
