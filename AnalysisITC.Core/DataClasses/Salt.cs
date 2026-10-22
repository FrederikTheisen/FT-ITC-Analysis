using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Analysis;

namespace AnalysisITC.Core.Data
{
	public enum Salt
	{
        [Salt("NaCl", 1)]
        NaCl,
        [Salt("NaF", 1)]
        NaF,
        [Salt("Na{2}SO{4}", 3)]
        Na2SO4,
        [Salt("K{2}SO{4}", 3)]
        K2SO4,
        [Salt("MgSO{4}", 4)]
        MgSO4,
        [Salt("KCl", 1)]
        KCl,
        [Salt("MgCl{2}", 3)]
        MgCl2,
        [Salt("KI", 1)]
        KI,
        [Salt("CaCl{2}", 3)]
        CaCl2
    }

    public static partial class Extensions
    {
        public static SaltAttribute GetProperties(this Salt value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(SaltAttribute), false).FirstOrDefault() as SaltAttribute;

            return attribute;
        }
    }

    public class SaltAttribute : Attribute
    {
        public string Name { get; set; } = "";
        public string AttributedName { get; private set; } = "";
        public int IonicStrength { get; private set; } = 1;

        public SaltAttribute(string name = "", int ionicstrength = 1)
        {
            Name = name.Replace("{", "").Replace("}", "");
            AttributedName = name;
            IonicStrength = ionicstrength;
        }

        public static List<Salt> GetSalts()
        {
            return (from Salt salt in Enum.GetValues(typeof(Salt)) select salt).ToList();
        }

        // Concentrations are recorded formula-unit molarities, not ion activities.
        public static bool TryGetSingleSaltConcentration(ExperimentData data, out Salt? identity, out double concentration)
        {
            identity = null;
            concentration = 0;
            var salts = data.Attributes.Where(attribute => attribute.Key == AttributeKey.Salt).ToList();
            if (salts.Count == 0) return false;
            foreach (var entry in salts)
            {
                var value = entry.ParameterValue.Value;
                if (!Enum.IsDefined(typeof(Salt), entry.IntValue) || !FWEMath.IsFinite(value) || value < 0)
                    return false;
                if (value == 0) continue;
                var salt = (Salt)entry.IntValue;
                if (identity.HasValue && identity.Value != salt) return false;
                identity = salt;
                concentration += value;
            }
            return FWEMath.IsFinite(concentration);
        }
    }
}
