using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.AppClasses.AnalysisClasses
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
        public string Name { get; private set; } = "";
        public int IonicStrength { get; private set; } = 1;

        public SaltAttribute(string name = "", int ionicstrength = 1)
        {
            Name = name;
            IonicStrength = ionicstrength;
        }

        public static List<Salt> GetSalts()
        {
            return (from Salt salt in Enum.GetValues(typeof(Salt)) select salt).ToList();
        }
    }
}

