using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
	public class AttributeKeyAttribute : Attribute
	{
		public string Name { get; set; }
		public string ToolTip { get; set; } = "";
		public ExperimentAttribute.AttributeType Type { get; set; }

		public bool AllowMultiple { get; set; } = false;

        public AttributeKeyAttribute(ExperimentAttribute.AttributeType type, bool allowmultipleattributes = false)
		{
			Name = type.ToString();
			Type = type;
			AllowMultiple = allowmultipleattributes;

			ToolTip = type.ToString();
		}

        public AttributeKeyAttribute(string name, ExperimentAttribute.AttributeType type, bool allowmultipleattributes = false)
        {
			Name = name;
            Type = type;
            AllowMultiple = allowmultipleattributes;

            ToolTip = type.ToString();
        }

        public AttributeKeyAttribute(string name, string tooltip, ExperimentAttribute.AttributeType type, bool allowmultipleattributes = false)
        {
            Name = name;
            Type = type;
            AllowMultiple = allowmultipleattributes;

			ToolTip = tooltip;
        }
    }

	public enum AttributeKey
	{
		Null,
		[AttributeKey("[Ligand]", "Concentration of ligand already bound before titration starts. This reduces the initially available binding capacity. Can be set directly or read from an experiment attribute.", ExperimentAttribute.AttributeType.ParameterConcentration)]
		PreboundLigandConc,
		[AttributeKey("Ligand Affinity", "Previously determined affinity of the prebound ligand for the binding site. This determines how strongly the prebound species occupies the site before titration.", ExperimentAttribute.AttributeType.ParameterAffinity)]
		PreboundLigandAffinity,
		[AttributeKey("Ligand Enthalpy", "Previously determined binding enthalpy of the prebound ligand. This is used when calculating heat changes associated with ligand displacement.", ExperimentAttribute.AttributeType.Parameter)]
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
        [AttributeKey("Shared N-Values", "Force duplicated site parameters to share the same fitted value. Useful for symmetric or equivalent two-site models.", ExperimentAttribute.AttributeType.Bool)]
        LockDuplicateParameter,
        [AttributeKey("Buffer Subtraction", ExperimentAttribute.AttributeType.ReferenceExperiment)]
        BufferSubtraction,
        [AttributeKey("Stoichiometry", "Fixed stoichiometric site ratio used by the model. This controls how many binding sites are represented on the cell side.", ExperimentAttribute.AttributeType.Int)]
        NumberOfSites1,
        [AttributeKey("Use Syringe Correction", "Use a syringe concentration correction factor instead of fitting an apparent N - value.Useful when the active titrant concentration is uncertain.This changes the fitted affinity and enthalpy.", ExperimentAttribute.AttributeType.Bool)]
        UseSyringeActiveFraction,
        [AttributeKey("Stoichiometry", "Fixed stoichiometric site ratio used by the model. This controls how many binding sites are represented on the cell side.", ExperimentAttribute.AttributeType.Int)]
        NumberOfSites2,
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
					case AttributeKey.Buffer: return BufferAttribute.GetUIBuffers().Select(b => new Tuple<int, string, string>((int)b, b.GetProperties().AttributedName, b.GetTooltip()));
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
				return DataManager.Data.Select(d => new Tuple<int, string, string, string>(i++, d.Name, d.Date.ToString(), d.UniqueID));
			}
		}

        public ExperimentAttribute()
		{
			ParameterValue = new();
		}

		public static ExperimentAttribute FromKey(AttributeKey key)
		{
			switch (key)
			{
				case AttributeKey.NumberOfSites1:
				case AttributeKey.NumberOfSites2: return Int(key, "", 1);
				case AttributeKey.PreboundLigandConc: return Concentration(key, "", new(0));
				case AttributeKey.UseSyringeActiveFraction:
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
				ParameterValue = new(),
			};
		}

        public static ExperimentAttribute Int(AttributeKey key, string name, int value)
        {
            return new ExperimentAttribute()
            {
                Key = key,
                OptionName = name,
                IntValue = value,
                ParameterValue = new(),
            };
        }

        public static ExperimentAttribute Double(AttributeKey key, string name, double value)
        {
            return new ExperimentAttribute()
            {
                Key = key,
                OptionName = name,
                DoubleValue = value,
                ParameterValue = new(),
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
                ParameterValue = new(),
            };
        }

        public void UpdateOptionKey(AttributeKey key, double dd = 0.0, double dp = 0.0)
		{
			Key = key;

			switch(key)
			{
				case AttributeKey.Buffer:
					DoubleValue = dd;
					ParameterValue = new(dp);
                    IntValue = -1;
                    break;
                case AttributeKey.Salt:
                    ParameterValue = new(dp);
                    IntValue = -1;
                    break;
				default:
					break;
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

		override public string ToString()
		{
            switch (Key)
            {
                case AttributeKey.Buffer:
                    return $"{ParameterValue.Value} mM {((Buffer)IntValue).GetProperties().ListName} pH {DoubleValue}";
                case AttributeKey.Salt:
                    return $"{ParameterValue.Value} mM {((Salt)IntValue).GetProperties().Name}";
                case AttributeKey.NumberOfSites1:
                case AttributeKey.NumberOfSites2:
                    return StoichiometryOptions.GetClosest(DoubleValue).Title;
            }

            switch (Key.GetProperties().Type)
			{
				case AttributeType.Bool: return $"{BoolValue}";
				case AttributeType.Enum:
				case AttributeType.Int: return $"{IntValue}";
				case AttributeType.Double: return $"{DoubleValue}";
				case AttributeType.ParameterAffinity:
				case AttributeType.ParameterConcentration:
				case AttributeType.Parameter: return $"{ParameterValue}";
			}

			return $"{Key} {StringValue} {IntValue} {BoolValue} {DoubleValue} {ParameterValue}";
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

