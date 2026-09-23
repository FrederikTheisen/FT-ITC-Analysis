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
                        case VariableConstraint.ThermodynamicallyLinked: return new[] { slot.Gibbs };
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

        internal static double InitialCoordinateValue(
            IReadOnlyList<Model> models,
            ParameterType memberKey,
            ParameterType coordinateKey,
            double referenceTemperatureKelvin,
            Func<ParameterType, VariableConstraint> constraintFor)
        {
            if (constraintFor == null
                || !ThermodynamicParameterSlots.TryResolve(memberKey, out var slot, out var family)
                || family != ThermodynamicParameterFamily.Affinity
                || coordinateKey != slot.Gibbs
                || constraintFor(slot.Affinity) != VariableConstraint.ThermodynamicallyLinked)
                return InitialCoordinateValue(models, memberKey, coordinateKey);

            var memberParameters = models.Select(model => model.Parameters).ToList();
            var enthalpy = EnthalpyRelationship(
                memberParameters,
                null,
                constraintFor,
                slot.Enthalpy,
                referenceTemperatureKelvin);
            var transported = memberParameters.Select(parameters =>
            {
                var temperature = parameters.ExperimentTemperature;
                var gibbs = GibbsFromLog10Affinity(
                    parameters.Table[memberKey].Value,
                    temperature);
                var ratio = temperature / referenceTemperatureKelvin;
                var bracket = HeatCapacityTerm(temperature, referenceTemperatureKelvin);
                return (gibbs - (1.0 - ratio) * enthalpy.ReferenceEnthalpy
                    - enthalpy.HeatCapacity * bracket) / ratio;
            });
            return transported.Average();
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

                case ThermodynamicParameterFamily.Affinity when constraint == VariableConstraint.ThermodynamicallyLinked:
                    if (!TryGetValue(globalTable, slot.Gibbs, out var linkedGibbs)) return false;
                    value = Log10AffinityFromGibbs(linkedGibbs, temperatureKelvin);
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

        internal static bool TryEvaluateMemberValue(
            ParameterType memberKey,
            VariableConstraint constraint,
            IReadOnlyDictionary<ParameterType, Parameter> globalTable,
            IReadOnlyList<ModelParameters> members,
            IReadOnlyDictionary<ParameterType, VariableConstraint> constraints,
            double temperatureKelvin,
            double referenceTemperatureKelvin,
            out double value)
        {
            if (constraint == VariableConstraint.ThermodynamicallyLinked
                && ThermodynamicParameterSlots.TryResolve(memberKey, out var slot, out var family)
                && family == ThermodynamicParameterFamily.Affinity
                && TryGetValue(globalTable, slot.Gibbs, out var gibbs))
            {
                var relation = EnthalpyRelationship(
                    members,
                    globalTable,
                    key => constraints != null && constraints.TryGetValue(key, out var selected)
                        ? selected
                        : VariableConstraint.None,
                    slot.Enthalpy,
                    referenceTemperatureKelvin);
                value = Log10AffinityFromGibbs(
                    EvaluateLinkedGibbs(
                        gibbs,
                        relation.ReferenceEnthalpy,
                        relation.HeatCapacity,
                        temperatureKelvin,
                        referenceTemperatureKelvin),
                    temperatureKelvin);
                return true;
            }

            return TryEvaluateMemberValue(
                memberKey,
                constraint,
                globalTable,
                temperatureKelvin,
                referenceTemperatureKelvin,
                out value);
        }

        internal static double EvaluateLinkedGibbs(
            double referenceGibbs,
            double referenceEnthalpy,
            double heatCapacity,
            double temperatureKelvin,
            double referenceTemperatureKelvin)
        {
            var ratio = temperatureKelvin / referenceTemperatureKelvin;
            return ratio * referenceGibbs
                + (1.0 - ratio) * referenceEnthalpy
                + heatCapacity * HeatCapacityTerm(temperatureKelvin, referenceTemperatureKelvin);
        }

        internal static double HeatCapacityTerm(double temperatureKelvin, double referenceTemperatureKelvin)
        {
            if (temperatureKelvin <= 0 || referenceTemperatureKelvin <= 0)
                return double.NaN;

            var x = (temperatureKelvin - referenceTemperatureKelvin) / referenceTemperatureKelvin;
            // delta - T*log(T/Tr), evaluated as Tr * (x - (1+x)*log1p(x))
            // so that the cancellation near the reference temperature is small.
            return referenceTemperatureKelvin
                * (x - (1.0 + x) * Log1p(x));
        }

        static double Log1p(double value)
        {
            if (Math.Abs(value) > 1e-4) return Math.Log(1.0 + value);

            // A short alternating series avoids losing the leading digits when
            // the temperature is very close to the fit reference.
            var term = value;
            var sum = term;
            for (var n = 2; n <= 12; n++)
            {
                term *= -value * (n - 1.0) / n;
                sum += term;
            }
            return sum;
        }

        internal static (double ReferenceEnthalpy, double HeatCapacity) EnthalpyRelationship(
            IReadOnlyList<ModelParameters> members,
            IReadOnlyDictionary<ParameterType, Parameter> globalTable,
            Func<ParameterType, VariableConstraint> constraintFor,
            ParameterType enthalpyKey,
            double referenceTemperatureKelvin)
        {
            var constraint = constraintFor?.Invoke(enthalpyKey) ?? VariableConstraint.None;
            if (constraint == VariableConstraint.SameForAll
                && TryGetValue(globalTable, enthalpyKey, out var shared))
                return (shared, 0);

            if (constraint == VariableConstraint.TemperatureDependent
                && TryGetValue(globalTable, enthalpyKey, out var reference)
                && ThermodynamicParameterSlots.TryResolve(enthalpyKey, out var slot, out _)
                && TryGetValue(globalTable, slot.HeatCapacity, out var heatCapacity))
                return (reference, heatCapacity);

            var observations = (members ?? Array.Empty<ModelParameters>())
                .Where(parameters => parameters?.Table.ContainsKey(enthalpyKey) == true)
                .Select(parameters => (Temperature: parameters.ExperimentTemperature,
                    Enthalpy: parameters.Table[enthalpyKey].Value))
                .ToList();
            if (observations.Count == 0) return (double.NaN, double.NaN);

            var meanTemperature = observations.Average(item => item.Temperature);
            var meanEnthalpy = observations.Average(item => item.Enthalpy);
            var centered = observations.Select(item => item.Temperature - meanTemperature).ToArray();
            var denominator = centered.Sum(value => value * value);
            var slope = denominator > 0
                ? centered.Select((value, index) => value * (observations[index].Enthalpy - meanEnthalpy)).Sum() / denominator
                : 0;
            return (meanEnthalpy + slope * (referenceTemperatureKelvin - meanTemperature), slope);
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
