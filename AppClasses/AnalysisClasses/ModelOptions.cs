using System;
using AnalysisITC.AppClasses.Analysis2;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
	public class ModelOption
	{
		public string OptionName { get; set; }

		public ModelOptionType Type { get; private set; }

        public bool BoolValue { get; set; }
		public int IntValue { get; set; }
		public double DoubleValue { get; set; }
		public FloatWithError ParameterValue { get; set; }
        public Enum EnumValue { get; set; }
		public int EnumOptionCount { get; private set; }

		public ModelOption()
		{
		}

		public static ModelOption Bool(string name, bool value)
		{
			return new ModelOption()
			{
				OptionName = name,
				BoolValue = value,
				Type = ModelOptionType.Bool,
			};
		}

        public static ModelOption Int(string name, int value)
        {
            return new ModelOption()
            {
                OptionName = name,
                IntValue = value,
                Type = ModelOptionType.Int,
            };
        }

        public static ModelOption Double(string name, double value)
		{
			return new ModelOption()
			{
				OptionName = name,
				DoubleValue = value,
				Type = ModelOptionType.Double,
			};
		}

        public static ModelOption Enum(string name, Enum value, int optioncount)
        {
            return new ModelOption()
            {
                OptionName = name,
                EnumValue = value,
                Type = ModelOptionType.Enum,
				EnumOptionCount = optioncount,
            };
        }

		public static ModelOption Parameter(string name, ParameterType type, FloatWithError value)
		{
			return new ModelOption()
			{
				Type = ModelOptionType.Parameter,
				OptionName = name,
				EnumValue = type,
				ParameterValue = value,
            };
		}

        public enum ModelOptionType
		{
			Bool,
			Int,
			Double,
			Enum,
			Parameter
		}
	}
}

