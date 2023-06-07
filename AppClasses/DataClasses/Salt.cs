using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC
{
	public enum Salt
	{
        [Salt("NaCl", 1, 2)]
        NaCl,
        [Salt("NaF", 1, 2)]
        NaF,
        [Salt("Na{2}SO{4}", 3, 3)]
        Na2SO4,
        [Salt("K{2}SO{4}", 3, 3)]
        K2SO4,
        [Salt("MgSO{4}", 4, 2)]
        MgSO4,
        [Salt("KCl", 1, 2)]
        KCl,
        [Salt("MgCl{2}", 3, 3)]
        MgCl2,
        [Salt("KI", 1, 2)]
        KI,
        [Salt("CaCl{2}", 1, 3)]
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
        public string Name { get; private set; } = "";
        public int IonicStrength { get; private set; } = 1;
        public int Activity { get; private set; } = 2;

        public SaltAttribute(string name = "", int ionicstrength = 1)
        {
            Name = name;
            IonicStrength = ionicstrength;
        }

        public SaltAttribute(string name = "", int ionicstrength = 1, int activity = 2)
        {
            Name = name;
            IonicStrength = ionicstrength;
            Activity = activity;
        }

        public static List<Salt> GetSalts()
        {
            return (from Salt salt in Enum.GetValues(typeof(Salt)) select salt).ToList();
        }

        public static double GetIonActivity(ExperimentData data)
        {
            var salts = data.Attributes.Where(opt => opt.Key == ModelOptionKey.Salt);
            var a = 0.0;

            foreach (var salt in salts)
            {
                a += Math.Pow(salt.ParameterValue, ((Salt)salt.IntValue).GetProperties().Activity);
            }

            return a;
        }
    }
}

