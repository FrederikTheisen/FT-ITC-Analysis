using System;
using AnalysisITC.AppClasses.Analysis2;
using System.Collections.Generic;
using AnalysisITC.Utilities;
using System.Linq;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
	public class AttributeKeyAttribute : Attribute
	{
		public string Name { get; set; }
		public ExperimentAttribute.AttributeType Type { get; set; }

		public bool AllowMultiple { get; set; } = false;

        public AttributeKeyAttribute(ExperimentAttribute.AttributeType type, bool allowmultipleattributes = false)
		{
			Name = type.ToString();
			Type = type;
			AllowMultiple = allowmultipleattributes;
		}

        public AttributeKeyAttribute(string name, ExperimentAttribute.AttributeType type, bool allowmultipleattributes = false)
        {
			Name = name;
            Type = type;
            AllowMultiple = allowmultipleattributes;
        }
    }

	public enum AttributeKey
	{
		Null,
		[AttributeKey("Prebound Ligand", ExperimentAttribute.AttributeType.ParameterConcentration)]
		PreboundLigandConc,
		[AttributeKey(ExperimentAttribute.AttributeType.ParameterAffinity)]
		PreboundLigandAffinity,
		[AttributeKey(ExperimentAttribute.AttributeType.Parameter)]
		PreboundLigandEnthalpy,
		[AttributeKey(ExperimentAttribute.AttributeType.Bool)]
        PeptideInCell,
		[AttributeKey("Buffer", ExperimentAttribute.AttributeType.Enum, true)]
		Buffer,
        [AttributeKey("Salt", ExperimentAttribute.AttributeType.Enum, true)]
        Salt,
        [AttributeKey("Ionic Strength", ExperimentAttribute.AttributeType.Double)]
        IonicStrength,
        [AttributeKey(ExperimentAttribute.AttributeType.ParameterConcentration)]
        EquilibriumConstant,
        [AttributeKey(ExperimentAttribute.AttributeType.Parameter)]
        Percentage,
        [AttributeKey(ExperimentAttribute.AttributeType.Bool)]
        LockDuplicateParameter,
        [AttributeKey("Buffer Subtraction", ExperimentAttribute.AttributeType.ReferenceExperiment)]
        BufferSubtraction,
    }

	public class ExperimentAttribute
	{
		public string OptionName { get; set; }
		public AttributeKey Key { get; private set; } = AttributeKey.Null;

		public bool BoolValue { get; set; }
		public int IntValue { get; set; }
		public double DoubleValue { get; set; }
		public string StringValue { get; set; }
		public FloatWithError ParameterValue { get; set; }

        public int EnumOptionCount => EnumOptions.Count();
        public KeyValuePair<AttributeKey, ExperimentAttribute> DictionaryEntry => new KeyValuePair<AttributeKey, ExperimentAttribute>(Key, this);

		/// <summary>
		/// Return list of available options as tuples with an ID, title and tooltip.
		/// </summary>
		public IEnumerable<Tuple<int,string, string>> EnumOptions
		{
			get
			{
				switch (Key)
				{
					case AttributeKey.Buffer: return BufferAttribute.GetUIBuffers().Select(b => new Tuple<int, string, string>((int)b, b.ToString(), b.GetTooltip()));
					case AttributeKey.Salt: return SaltAttribute.GetSalts().Select(b => new Tuple<int, string, string>((int)b, b.GetProperties().AttributedName, ""));
                    default: throw new Exception("Attribute Configuration Error");
                }
			}
		}

		public IEnumerable<Tuple<int, string, string, string>> ExperimentReferenceOptions
		{
			get
			{
				int i = 0;
				return DataManager.Data.Select(d => new Tuple<int, string, string, string>(i++, d.FileName, d.Date.ToString(), d.UniqueID));
			}
		}

        public ExperimentAttribute()
		{
			
		}

		public static ExperimentAttribute FromKey(AttributeKey key)
		{
			switch (key)
			{
				case AttributeKey.PreboundLigandConc: return Concentration(key, "", new(0));
				case AttributeKey.PeptideInCell: return Bool(key, "", false);
				default: return Parameter(key, "", new(0));
			}
		}

		public static ExperimentAttribute Bool(AttributeKey key, string name, bool value)
		{
			return new ExperimentAttribute()
			{
                Key = key,
                OptionName = name,
				BoolValue = value,
			};
		}

		public static ExperimentAttribute Int(AttributeKey key, string name, int value)
		{
			return new ExperimentAttribute()
			{
                Key = key,
                OptionName = name,
				IntValue = value,
			};
		}

		public static ExperimentAttribute Double(AttributeKey key, string name, double value)
		{
			return new ExperimentAttribute()
			{
                Key = key,
                OptionName = name,
				DoubleValue = value,
			};
		}

		public static ExperimentAttribute Enum(AttributeKey key, string name, List<string> options, int initial = 0)
		{
			return new ExperimentAttribute()
			{
				Key = key,
				OptionName = name,
				IntValue = options.Count,
			};
		}

		public static ExperimentAttribute Parameter(AttributeKey key, string name, FloatWithError value)
		{
			return new ExperimentAttribute()
			{
				Key = key,
				OptionName = name,
				ParameterValue = value,
			};
		}

		public static ExperimentAttribute Affinity(AttributeKey key, string name, FloatWithError value)
		{
			return new ExperimentAttribute()
			{
                Key = key,
				OptionName = name,
				ParameterValue = value,
			};
		}

		public static ExperimentAttribute Concentration(AttributeKey key, string name, FloatWithError value)
		{
			return new ExperimentAttribute()
			{
                Key = key,
				OptionName = name,
				ParameterValue = value,
			};
		}

        public static ExperimentAttribute ExperimentReference(string name, string uniqueid)
        {
            return new ExperimentAttribute()
            {
                Key = AttributeKey.BufferSubtraction,
                OptionName = name,
                StringValue = uniqueid,
            };
        }

        public void UpdateOptionKey(AttributeKey key)
		{
			Key = key;

			if (Key == AttributeKey.Buffer || Key == AttributeKey.Salt)
			{
				IntValue = -1;
			}
		}

		public ExperimentAttribute Copy()
		{
			return new ExperimentAttribute()
			{
				OptionName = OptionName,
				Key = Key,
				IntValue = IntValue,
				BoolValue = BoolValue,
				ParameterValue = ParameterValue,
				DoubleValue = DoubleValue,
				StringValue = StringValue,
			};
		}

		public static List<AttributeKey> AvailableExperimentAttributes
		{
			get
			{
				return new List<AttributeKey>
				{
					 AttributeKey.PreboundLigandConc,
					 //ModelOptionKey.PeptideInCell,
					 AttributeKey.Buffer,
					 AttributeKey.Salt,
					 AttributeKey.IonicStrength,
					 AttributeKey.BufferSubtraction
				};
			}
		}

		public enum AttributeType
		{
			Bool,
			Int,
			Double,
			Enum,
			Parameter,
			ParameterAffinity,
			ParameterConcentration,
            ReferenceExperiment,
        }
	}
}

