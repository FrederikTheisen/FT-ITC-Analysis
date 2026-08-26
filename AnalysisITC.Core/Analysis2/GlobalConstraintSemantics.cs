using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Analysis
{
    /// <summary>
    /// Describes one constraint control. Sequential analyses use one descriptor for
    /// every active affinity step and one for every active enthalpy step.
    /// </summary>
    public sealed class GlobalConstraintFamilyDescriptor
    {
        internal GlobalConstraintFamilyDescriptor(
            ParameterType key,
            IEnumerable<ParameterType> memberKeys,
            IEnumerable<VariableConstraint> options)
        {
            Key = key;
            MemberKeys = (memberKeys ?? throw new ArgumentNullException(nameof(memberKeys))).ToArray();
            Options = (options ?? throw new ArgumentNullException(nameof(options))).ToArray();
        }

        public ParameterType Key { get; }
        public IReadOnlyList<ParameterType> MemberKeys { get; }
        public IReadOnlyList<VariableConstraint> Options { get; }
        public bool IsFamily => MemberKeys.Count > 1;
    }

    /// <summary>
    /// Authoritative translation between a member parameter constraint and the
    /// coordinate fitted by a global analysis.
    /// </summary>
    internal static class GlobalConstraintSemantics
    {
        internal static bool IsSupportedThermodynamicMember(ParameterType key)
        {
            return ThermodynamicParameterSlots.TryResolve(key, out _, out var family)
                && (family == ThermodynamicParameterFamily.Affinity
                    || family == ThermodynamicParameterFamily.Enthalpy);
        }

        internal static IReadOnlyList<ParameterType> CoordinateKeys(
            ParameterType memberKey,
            VariableConstraint constraint)
        {
            if (!ThermodynamicParameterSlots.TryResolve(memberKey, out var slot, out var family))
                return Array.Empty<ParameterType>();

            switch (family)
            {
                case ThermodynamicParameterFamily.Affinity:
                    switch (constraint)
                    {
                        case VariableConstraint.SameForAll: return new[] { slot.Affinity };
                        case VariableConstraint.TemperatureDependent: return new[] { slot.Gibbs };
                        default: return Array.Empty<ParameterType>();
                    }

                case ThermodynamicParameterFamily.Enthalpy:
                    switch (constraint)
                    {
                        case VariableConstraint.SameForAll: return new[] { slot.Enthalpy };
                        // Preserve the established fitting-vector order.
                        case VariableConstraint.TemperatureDependent:
                            return new[] { slot.HeatCapacity, slot.Enthalpy };
                        default: return Array.Empty<ParameterType>();
                    }

                default:
                    return Array.Empty<ParameterType>();
            }
        }

        internal static double InitialCoordinateValue(
            IReadOnlyList<Model> models,
            ParameterType memberKey,
            ParameterType coordinateKey)
        {
            if (models == null || models.Count == 0)
                throw new ArgumentException("At least one member model is required.", nameof(models));
            if (!ThermodynamicParameterSlots.TryResolve(memberKey, out var memberSlot, out var memberFamily))
                throw new ArgumentException($"{memberKey} is not a thermodynamic slot parameter.", nameof(memberKey));
            if (!ThermodynamicParameterSlots.TryResolve(coordinateKey, out var coordinateSlot, out var coordinateFamily)
                || coordinateSlot.Index != memberSlot.Index)
                throw new ArgumentException($"{coordinateKey} does not describe the same thermodynamic step as {memberKey}.", nameof(coordinateKey));

            if (coordinateFamily == ThermodynamicParameterFamily.HeatCapacity)
                return -1000.0;

            if (memberFamily == ThermodynamicParameterFamily.Affinity
                && coordinateFamily == ThermodynamicParameterFamily.Gibbs)
            {
                return models.Average(model =>
                {
                    var log10Ka = model.Parameters.Table[memberKey].Value;
                    return GibbsFromLog10Affinity(log10Ka, model.Data.MeasuredTemperatureKelvin);
                });
            }

            return models.Average(model => model.Parameters.Table[memberKey].Value);
        }

        internal static bool TryEvaluateMemberValue(
            ParameterType memberKey,
            VariableConstraint constraint,
            IReadOnlyDictionary<ParameterType, Parameter> globalTable,
            double temperatureKelvin,
            double referenceTemperatureKelvin,
            out double value)
        {
            value = double.NaN;
            if (!ThermodynamicParameterSlots.TryResolve(memberKey, out var slot, out var family))
                return false;

            switch (family)
            {
                case ThermodynamicParameterFamily.Affinity when constraint == VariableConstraint.SameForAll:
                    return TryGetValue(globalTable, slot.Affinity, out value);

                case ThermodynamicParameterFamily.Affinity when constraint == VariableConstraint.TemperatureDependent:
                    if (!TryGetValue(globalTable, slot.Gibbs, out var gibbs)) return false;
                    value = Log10AffinityFromGibbs(gibbs, temperatureKelvin);
                    return true;

                case ThermodynamicParameterFamily.Enthalpy when constraint == VariableConstraint.SameForAll:
                    return TryGetValue(globalTable, slot.Enthalpy, out value);

                case ThermodynamicParameterFamily.Enthalpy when constraint == VariableConstraint.TemperatureDependent:
                    if (!TryGetValue(globalTable, slot.Enthalpy, out var referenceEnthalpy)
                        || !TryGetValue(globalTable, slot.HeatCapacity, out var heatCapacity))
                        return false;
                    value = referenceEnthalpy
                        + (temperatureKelvin - referenceTemperatureKelvin) * heatCapacity;
                    return true;

                default:
                    return false;
            }
        }

        internal static double GibbsFromLog10Affinity(double log10Ka, double temperatureKelvin) =>
            -Energy.R * temperatureKelvin * Math.Log(10.0) * log10Ka;

        internal static double Log10AffinityFromGibbs(double gibbs, double temperatureKelvin) =>
            -gibbs / (Energy.R * temperatureKelvin * Math.Log(10.0));

        static bool TryGetValue(
            IReadOnlyDictionary<ParameterType, Parameter> table,
            ParameterType key,
            out double value)
        {
            value = double.NaN;
            if (table == null || !table.TryGetValue(key, out var parameter)) return false;
            value = parameter.Value;
            return true;
        }
    }
}
