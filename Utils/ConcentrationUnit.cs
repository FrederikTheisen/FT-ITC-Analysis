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
    }

    public class ConcentrationUnitAttribute : Attribute
    {
        public string Name { get; set; }
        public double Mod { get; set; }

        public ConcentrationUnitAttribute(string name, double mod)
        {
            Name = name;
            Mod = mod;
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
