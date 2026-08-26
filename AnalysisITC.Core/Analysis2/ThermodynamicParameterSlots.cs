using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis.Models;

namespace AnalysisITC.Core.Analysis
{
    internal enum ThermodynamicParameterFamily
    {
        Affinity,
        Enthalpy,
        Gibbs,
        HeatCapacity,
        Entropy,
        EntropyContribution,
    }

    internal readonly struct ThermodynamicParameterSlot
    {
        internal ThermodynamicParameterSlot(
            int index,
            ParameterType affinity,
            ParameterType enthalpy,
            ParameterType gibbs,
            ParameterType heatCapacity,
            ParameterType entropy,
            ParameterType entropyContribution)
        {
            Index = index;
            Affinity = affinity;
            Enthalpy = enthalpy;
            Gibbs = gibbs;
            HeatCapacity = heatCapacity;
            Entropy = entropy;
            EntropyContribution = entropyContribution;
        }

        internal int Index { get; }
        internal ParameterType Affinity { get; }
        internal ParameterType Enthalpy { get; }
        internal ParameterType Gibbs { get; }
        internal ParameterType HeatCapacity { get; }
        internal ParameterType Entropy { get; }
        internal ParameterType EntropyContribution { get; }

        internal ParameterType Get(ThermodynamicParameterFamily family)
        {
            switch (family)
            {
                case ThermodynamicParameterFamily.Affinity: return Affinity;
                case ThermodynamicParameterFamily.Enthalpy: return Enthalpy;
                case ThermodynamicParameterFamily.Gibbs: return Gibbs;
                case ThermodynamicParameterFamily.HeatCapacity: return HeatCapacity;
                case ThermodynamicParameterFamily.Entropy: return Entropy;
                case ThermodynamicParameterFamily.EntropyContribution: return EntropyContribution;
                default: throw new ArgumentOutOfRangeException(nameof(family));
            }
        }
    }

    /// <summary>
    /// Authoritative mapping between a one-based thermodynamic step and every
    /// parameter family that can describe that step. Persistence identifiers remain
    /// explicit and deliberately do not derive from this table.
    /// </summary>
    internal static class ThermodynamicParameterSlots
    {
        internal const int MinimumSequentialCount = 2;
        internal const int MaximumSequentialCount = 4;

        static readonly IReadOnlyList<ThermodynamicParameterSlot> Slots = new[]
        {
            new ThermodynamicParameterSlot(1, ParameterType.Affinity1, ParameterType.Enthalpy1,
                ParameterType.Gibbs1, ParameterType.HeatCapacity1, ParameterType.Entropy1,
                ParameterType.EntropyContribution1),
            new ThermodynamicParameterSlot(2, ParameterType.Affinity2, ParameterType.Enthalpy2,
                ParameterType.Gibbs2, ParameterType.HeatCapacity2, ParameterType.Entropy2,
                ParameterType.EntropyContribution2),
            new ThermodynamicParameterSlot(3, ParameterType.Affinity3, ParameterType.Enthalpy3,
                ParameterType.Gibbs3, ParameterType.HeatCapacity3, ParameterType.Entropy3,
                ParameterType.EntropyContribution3),
            new ThermodynamicParameterSlot(4, ParameterType.Affinity4, ParameterType.Enthalpy4,
                ParameterType.Gibbs4, ParameterType.HeatCapacity4, ParameterType.Entropy4,
                ParameterType.EntropyContribution4),
        };

        internal static IReadOnlyList<ThermodynamicParameterSlot> All => Slots;

        internal static IEnumerable<ThermodynamicParameterSlot> Active(int count)
        {
            ValidateSequentialCount(count);
            return Slots.Take(count);
        }

        internal static IEnumerable<ThermodynamicParameterSlot> Active(Model model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (model.ModelType == AnalysisModel.SequentialBindingSites)
            {
                if (!model.ModelOptions.TryGetValue(AttributeKey.SequentialSiteCount, out var option))
                    throw new InvalidOperationException("Sequential model is missing its binding-step count.");
                return Active(option.IntValue);
            }

            return Slots.Where(slot => model.Parameters.Table.ContainsKey(slot.Affinity)
                || model.Parameters.Table.ContainsKey(slot.Enthalpy));
        }

        internal static ThermodynamicParameterSlot ForStep(int oneBasedIndex)
        {
            if (oneBasedIndex < 1 || oneBasedIndex > Slots.Count)
                throw new ArgumentOutOfRangeException(nameof(oneBasedIndex), "Thermodynamic step must be from 1 to 4.");
            return Slots[oneBasedIndex - 1];
        }

        internal static ParameterType Get(int oneBasedIndex, ThermodynamicParameterFamily family) =>
            ForStep(oneBasedIndex).Get(family);

        internal static bool TryResolve(
            ParameterType key,
            out ThermodynamicParameterSlot slot,
            out ThermodynamicParameterFamily family)
        {
            foreach (var candidate in Slots)
            {
                foreach (ThermodynamicParameterFamily candidateFamily in Enum.GetValues(typeof(ThermodynamicParameterFamily)))
                {
                    if (candidate.Get(candidateFamily) != key) continue;
                    slot = candidate;
                    family = candidateFamily;
                    return true;
                }
            }

            slot = default;
            family = default;
            return false;
        }

        internal static ParameterType Sibling(ParameterType key, ThermodynamicParameterFamily targetFamily)
        {
            if (!TryResolve(key, out var slot, out _))
                throw new ArgumentException($"{key} is not a thermodynamic slot parameter.", nameof(key));
            return slot.Get(targetFamily);
        }

        internal static IEnumerable<ParameterType> OrderedKeys(
            IEnumerable<ParameterType> keys,
            params ThermodynamicParameterFamily[] familyOrder)
        {
            var set = new HashSet<ParameterType>(keys ?? Enumerable.Empty<ParameterType>());
            var families = familyOrder == null || familyOrder.Length == 0
                ? (ThermodynamicParameterFamily[])Enum.GetValues(typeof(ThermodynamicParameterFamily))
                : familyOrder;
            return Slots.SelectMany(slot => families.Select(slot.Get)).Where(set.Contains);
        }

        internal static int FamilyMemberCount(IEnumerable<ParameterType> keys, ParameterType key)
        {
            if (!TryResolve(key, out _, out var family)) return 0;
            var set = new HashSet<ParameterType>(keys ?? Enumerable.Empty<ParameterType>());
            return Slots.Count(slot => set.Contains(slot.Get(family)));
        }

        internal static void ValidateSequentialCount(int count)
        {
            if (count < MinimumSequentialCount || count > MaximumSequentialCount)
                throw new ArgumentOutOfRangeException(nameof(count), count,
                    $"Sequential binding-step count must be from {MinimumSequentialCount} to {MaximumSequentialCount}.");
        }
    }
}
