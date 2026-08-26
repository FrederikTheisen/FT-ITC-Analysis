using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis.Models;


namespace AnalysisITC.Core.Analysis
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

        public void SetSequentialConstraintFamily(
            ParameterType familyKey,
            VariableConstraint constraint,
            int activeStepCount)
        {
            ThermodynamicParameterSlots.ValidateSequentialCount(activeStepCount);
            if (!ThermodynamicParameterSlots.TryResolve(familyKey, out _, out var family)
                || (family != ThermodynamicParameterFamily.Affinity
                    && family != ThermodynamicParameterFamily.Enthalpy))
                throw new ArgumentException("Sequential constraints can be set only for affinity or enthalpy families.", nameof(familyKey));

            foreach (var slot in ThermodynamicParameterSlots.All)
            {
                var key = slot.Get(family);
                if (slot.Index <= activeStepCount) Constraints[key] = constraint;
                else Constraints.Remove(key);
            }
        }

        public void TrimSequentialSteps(int activeStepCount)
        {
            ThermodynamicParameterSlots.ValidateSequentialCount(activeStepCount);

            foreach (var key in Constraints.Keys.ToList())
            {
                if (ThermodynamicParameterSlots.TryResolve(key, out var slot, out _)
                    && slot.Index > activeStepCount)
                    Constraints.Remove(key);
            }

            foreach (var key in ParameterOverrides.Keys.ToList())
            {
                if (key.Model == AnalysisModel.SequentialBindingSites
                    && ThermodynamicParameterSlots.TryResolve(key.Key, out var slot, out _)
                    && slot.Index > activeStepCount)
                    ParameterOverrides.Remove(key);
            }
        }

        public void ResizeSequentialSteps(int previousStepCount, int activeStepCount)
        {
            ThermodynamicParameterSlots.ValidateSequentialCount(previousStepCount);
            ThermodynamicParameterSlots.ValidateSequentialCount(activeStepCount);

            var affinityStyle = ConsistentFamilyStyle(
                ThermodynamicParameterFamily.Affinity,
                previousStepCount);
            var enthalpyStyle = ConsistentFamilyStyle(
                ThermodynamicParameterFamily.Enthalpy,
                previousStepCount);

            TrimSequentialSteps(activeStepCount);

            if (activeStepCount <= previousStepCount) return;
            if (affinityStyle.HasValue)
                SetSequentialConstraintFamily(
                    ParameterType.Affinity1,
                    affinityStyle.Value,
                    activeStepCount);
            if (enthalpyStyle.HasValue)
                SetSequentialConstraintFamily(
                    ParameterType.Enthalpy1,
                    enthalpyStyle.Value,
                    activeStepCount);
        }

        VariableConstraint? ConsistentFamilyStyle(
            ThermodynamicParameterFamily family,
            int activeStepCount)
        {
            VariableConstraint? style = null;
            foreach (var slot in ThermodynamicParameterSlots.Active(activeStepCount))
            {
                if (!Constraints.TryGetValue(slot.Get(family), out var candidate))
                    return null;
                if (style.HasValue && style.Value != candidate)
                    return null;
                style = candidate;
            }
            return style;
        }
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
