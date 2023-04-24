using System;
using System.Linq;

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
                _ => ConcentrationUnit.M
            };
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
