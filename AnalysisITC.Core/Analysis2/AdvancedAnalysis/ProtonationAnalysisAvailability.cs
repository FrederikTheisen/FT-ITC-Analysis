using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Analysis.Models;
using Buffer = AnalysisITC.Core.Data.Buffer;

namespace AnalysisITC.Core.Analysis
{
    public static class ProtonationAnalysisAvailability
    {
        public static string GetUnavailableReason(IEnumerable<SolutionInterface> solutions)
            => GetUnavailableReason(solutions, out _);

        internal static string GetUnavailableReason(IEnumerable<SolutionInterface> solutions, out string tooltip)
        {
            var enthalpies = new List<double>();
            var unavailable = new List<string>();
            var missingExperiments = new List<string>();
            foreach (var solution in solutions ?? Enumerable.Empty<SolutionInterface>())
            {
                if (BufferAttribute.TryGetIonizationEnthalpy(solution?.Data, solution?.Temp ?? double.NaN, out var enthalpy))
                {
                    enthalpies.Add(enthalpy.Value);
                    continue;
                }

                var attribute = solution?.Data?.Attributes?.Find(item => item.Key == AttributeKey.Buffer);
                var bufferName = attribute == null ? "No buffer metadata"
                    : ((Buffer)attribute.IntValue).GetProperties()?.ListName ?? "Unknown buffer";
                var name = solution?.Data?.Name ?? "Unnamed experiment";
                unavailable.Add(bufferName + " (" + name + ")");
                missingExperiments.Add(name);
            }

            if (unavailable.Count > 0)
            {
                tooltip = enthalpies.Count == 0
                    ? "Requires buffer attributes with known ionization enthalpies."
                    : "Missing buffer ionization enthalpy: " + string.Join("; ", missingExperiments) + ".";
                return "Protonation analysis requires usable buffer ionization enthalpies. Unavailable for: "
                    + string.Join("; ", unavailable) + ".";
            }
            if (enthalpies.Distinct().Count() < 2)
            {
                tooltip = "Requires at least two distinct buffer ionization enthalpies.";
                return "Protonation analysis requires at least two distinct, finite buffer ionization enthalpies.";
            }
            tooltip = null;
            return string.Empty;
        }
    }
}
