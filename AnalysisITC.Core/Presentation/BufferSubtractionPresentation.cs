using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Presentation
{
    public static class BufferSubtractionPresentation
    {
        public static string InjectionVolumeWarning(ExperimentData reference, IReadOnlyList<ExperimentData> targets)
        {
            if (reference == null) return string.Empty;

            var mismatches = targets.Count(target => HasDifferentInjectionVolume(reference, target));
            if (mismatches == 0) return string.Empty;

            return targets.Count == 1
                ? "Injection volumes differ from the reference."
                : $"Injection volumes differ from the reference for {mismatches} of {targets.Count} selected targets.";
        }

        static bool HasDifferentInjectionVolume(ExperimentData reference, ExperimentData target)
        {
            var count = Math.Min(reference.Injections.Count, target.Injections.Count);
            for (int i = 0; i < count; i++)
            {
                var referenceVolume = reference.Injections[i]?.Volume ?? 0;
                var targetVolume = target.Injections[i]?.Volume ?? 0;
                if (referenceVolume <= 0 || targetVolume <= 0 ||
                    !FWEMath.IsFinite(referenceVolume) || !FWEMath.IsFinite(targetVolume)) continue;

                // Discard floating-point noise at the 0.1 µL boundary.
                var differenceMicrolitres = Math.Abs(referenceVolume - targetVolume) * 1e6;
                if (Math.Round(differenceMicrolitres, 9) > 0.1) return true;
            }

            return false;
        }
    }
}
