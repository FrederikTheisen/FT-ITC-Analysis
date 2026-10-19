using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Analysis
{
    public static class ElectrostaticsAnalysisAvailability
    {
        public const string MinimumSpanToolTip = "Requires three ionic strengths separated by over half this span.";
        public const string Kd0ToolTip = "Model-estimated Kd extrapolated to zero ionic strength.";
        public const string CounterIonRequirement = "Requires three distinct positive ion activities with usable affinities.";

        public static string GetMissingSaltReason(IEnumerable<SolutionInterface> solutions)
        {
            var missing = solutions.Where(solution => !solution.Data.Attributes.Any(attribute => attribute.Key == AttributeKey.Salt))
                .Select(solution => solution.Data.Name).ToList();
            return missing.Count == 0 ? string.Empty
                : "Electrostatics analysis requires salt metadata for every experiment. Missing for: " + string.Join("; ", missing) + ".";
        }

        public static string GetUnavailableReason(IEnumerable<SolutionInterface> solutions, double minimumSpan, bool includeBuffer)
        {
            var members = solutions.ToList();
            var missing = GetMissingSaltReason(members);
            if (missing.Length > 0) return missing;

            var strengths = members.Where(HasUsableAffinity)
                .Select(solution => BufferAttribute.GetIonicStrength(solution.Data, includeBuffer))
                .Where(value => FWEMath.IsFinite(value) && value >= 0).OrderBy(value => value);
            var accepted = 0;
            var last = 0.0;
            foreach (var strength in strengths)
            {
                if (accepted != 0 && strength - last <= minimumSpan / 2) continue;
                last = strength;
                if (++accepted == 3) return string.Empty;
            }
            return IonicStrengthRequirement(minimumSpan);
        }

        public static string IonicStrengthRequirement(double minimumSpan)
            => "Requires three ionic strengths separated by more than "
                + (minimumSpan * 500).ToString("G6") + " mM, with usable affinities.";

        internal static bool HasUsableAffinity(SolutionInterface solution)
            => solution.ReportParameters.TryGetValue(ParameterType.Affinity1, out var affinity)
                && IsUsable(affinity) && affinity.Value > 0;

        internal static bool IsUsable(FloatWithError value)
            => !FloatWithError.IsNaN(value) && FWEMath.IsFinite(value.Value);
    }
}
