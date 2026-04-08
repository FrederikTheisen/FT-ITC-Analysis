using System;
using System.Collections.Generic;
using AnalysisITC.AppClasses.AnalysisClasses.Models;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
	public sealed class AnalysisSessionState
	{
        public static AnalysisSessionState Current { get; private set; } = CreateDefault();

        public static AnalysisSessionState CreateDefault() => new AnalysisSessionState();

        public static void Reset() => Current = CreateDefault();

        public static void Replace(AnalysisSessionState state)
        {
            Current = state ?? throw new ArgumentNullException(nameof(state));
        }

        private AnalysisSessionState() { }

        public AnalysisModel ModelType { get; set; } = AnalysisModel.OneSetOfSites;
        public bool IsGlobal { get; set; }
        public AnalysisState Single { get; } = new();
        public AnalysisState Global { get; } = new();

        public AnalysisState Active => IsGlobal ? Global : Single;
    }

    public sealed class AnalysisState
    {
        public Dictionary<AttributeKey, ExperimentAttribute> ModelOptions { get; } = new();
        public Dictionary<ParameterOverrideKey, ParameterOverride> ParameterOverrides { get; } = new();
        public Dictionary<ParameterType, VariableConstraint> Constraints { get; } = new();
    }

    public readonly struct ParameterOverrideKey : IEquatable<ParameterOverrideKey>
    {
        public AnalysisModel Model { get; }
        public ParameterType Key { get; }

        public ParameterOverrideKey(AnalysisModel model, ParameterType key)
        {
            Model = model;
            Key = key;
        }

        public bool Equals(ParameterOverrideKey other)
        {
            return Model == other.Model && Key == other.Key;
        }

        public override bool Equals(object obj)
        {
            return obj is ParameterOverrideKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Model * 397) ^ (int)Key;
            }
        }
    }

    public sealed class ParameterOverride
    {
        public double Value { get; set; }
        public bool IsLocked { get; set; }
    }
}

