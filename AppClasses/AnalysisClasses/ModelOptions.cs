using System;
using AnalysisITC.AppClasses.Analysis2;
using System.Collections.Generic;
using AnalysisITC.Utils;
using System.Linq;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
	public class ModelOptionKeyAttribute : Attribute
	{
		public ModelOptions.ModelOptionType Type { get; set; }

		public bool AllowMultiple { get; set; } = false;

        public ModelOptionKeyAttribute(ModelOptions.ModelOptionType type, bool allowmultipleattributes = false)
		{
			Type = type;
			AllowMultiple = allowmultipleattributes;
		}
    }

	public enum ModelOptionKey
	{
		Null,
		[ModelOptionKey(ModelOptions.ModelOptionType.ParameterConcentration)]
		PreboundLigandConc,
		[ModelOptionKey(ModelOptions.ModelOptionType.ParameterAffinity)]
		PreboundLigandAffinity,
		[ModelOptionKey(ModelOptions.ModelOptionType.Parameter)]
		PreboundLigandEnthalpy,
		[ModelOptionKey(ModelOptions.ModelOptionType.Bool)]
        PeptideInCell,
		[ModelOptionKey(ModelOptions.ModelOptionType.Enum, true)]
		Buffer,
        [ModelOptionKey(ModelOptions.ModelOptionType.Enum, true)]
        Salt,
        [ModelOptionKey(ModelOptions.ModelOptionType.Double)]
        IonicStrength
    }

	public class ModelOptions
	{
		public string OptionName { get; set; }
		public ModelOptionKey Key { get; private set; } = ModelOptionKey.Null;

		public bool BoolValue { get; set; }
		public int IntValue { get; set; }
		public double DoubleValue { get; set; }
		public FloatWithError ParameterValue { get; set; }

        public int EnumOptionCount => EnumOptions.Count();
        public KeyValuePair<ModelOptionKey, ModelOptions> DictionaryEntry => new KeyValuePair<ModelOptionKey, ModelOptions>(Key, this);

		public IEnumerable<Tuple<int,string>> EnumOptions
		{
			get
			{
				switch (Key)
				{
					case ModelOptionKey.Buffer: return BufferAttribute.GetUIBuffers().Select(b => new Tuple<int, string>((int)b, b.ToString()));
					case ModelOptionKey.Salt: return SaltAttribute.GetSalts().Select(b => new Tuple<int, string>((int)b, b.GetProperties().Name));
                    default: return new List<Tuple<int,string>>();
                }
			}
		}

        public ModelOptions()
		{
			
		}

		public static ModelOptions FromKey(ModelOptionKey key)
		{
			switch (key)
			{
				case ModelOptionKey.PreboundLigandConc: return Concentration(key, "", new(0));
				case ModelOptionKey.PeptideInCell: return Bool(key, "", false);
				default: return Parameter(key, "", new(0));
			}
		}

		public static ModelOptions Bool(ModelOptionKey key, string name, bool value)
		{
			return new ModelOptions()
			{
                Key = key,
                OptionName = name,
				BoolValue = value,
			};
		}

		public static ModelOptions Int(ModelOptionKey key, string name, int value)
		{
			return new ModelOptions()
			{
                Key = key,
                OptionName = name,
				IntValue = value,
			};
		}

		public static ModelOptions Double(ModelOptionKey key, string name, double value)
		{
			return new ModelOptions()
			{
                Key = key,
                OptionName = name,
				DoubleValue = value,
			};
		}

		public static ModelOptions Enum(ModelOptionKey key, string name, List<string> options, int initial = 0)
		{
			return new ModelOptions()
			{
				Key = key,
				OptionName = name,
				IntValue = options.Count,
			};
		}

		public static ModelOptions Parameter(ModelOptionKey key, string name, FloatWithError value)
		{
			return new ModelOptions()
			{
				Key = key,
				OptionName = name,
				ParameterValue = value,
			};
		}

		public static ModelOptions Affinity(ModelOptionKey key, string name, FloatWithError value)
		{
			return new ModelOptions()
			{
                Key = key,
				OptionName = name,
				ParameterValue = value,
			};
		}

		public static ModelOptions Concentration(ModelOptionKey key, string name, FloatWithError value)
		{
			return new ModelOptions()
			{
                Key = key,
				OptionName = name,
				ParameterValue = value,
			};
		}

		public void UpdateOptionKey(ModelOptionKey key)
		{
			Key = key;

			if (Key == ModelOptionKey.Buffer || Key == ModelOptionKey.Salt)
			{
				IntValue = -1;
			}
		}

		public ModelOptions Copy()
		{
			return new ModelOptions()
			{
				OptionName = OptionName,
				Key = Key,
				IntValue = IntValue,
				BoolValue = BoolValue,
				ParameterValue = ParameterValue,
				DoubleValue = DoubleValue,
			};
		}

		public static List<ModelOptionKey> AvailableExperimentAttributes
		{
			get
			{
				return new List<ModelOptionKey>
				{
					 ModelOptionKey.PreboundLigandConc,
					 ModelOptionKey.PeptideInCell,
					 ModelOptionKey.Buffer,
					 ModelOptionKey.Salt,
					 ModelOptionKey.IonicStrength,
				};
			}
		}

		public enum ModelOptionType
		{
			Bool,
			Int,
			Double,
			Enum,
			Parameter,
			ParameterAffinity,
			ParameterConcentration
		}
	}
}

