using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Data
{
    public static class BufferSubtractionPolicy
    {
        public const string TandemTargetReason = "Buffer subtraction is not supported for tandem experiments.";
        public const string CorrectedSourceReason = "Remove buffer subtraction from source experiments before merging or scanning.";
        public const string MeanRequiresVolumesReason = "Mean subtraction needs reference injection volumes. Load the reference experiment to use Mean.";

        public static bool CanApplyTo(ExperimentData experiment) => experiment != null && !experiment.IsTandemExperiment;

        public static bool HasCorrectedSource(IEnumerable<ExperimentData> sources) =>
            sources.Any(source => source?.BufferSubtractionSettings != null);

        public static bool CanUseStoredMethod(BufferSubtractionReferenceSnapshot snapshot, BufferSubtractionMethod? currentMethod,
            BufferSubtractionMethod requestedMethod) =>
            requestedMethod != BufferSubtractionMethod.Mean || currentMethod == requestedMethod || snapshot == null
            || snapshot.Points.Any(point => point.Volume > 0 && FWEMath.IsFinite(point.Volume.Value));

        public static void ValidateStoredMethod(BufferSubtractionReferenceSnapshot snapshot, BufferSubtractionMethod? currentMethod,
            BufferSubtractionMethod requestedMethod)
        {
            if (!CanUseStoredMethod(snapshot, currentMethod, requestedMethod))
                throw new HandledException(HandledException.Severity.Warning, "Buffer Subtraction Error", MeanRequiresVolumesReason);
        }

        public static void ValidateTarget(ExperimentData experiment)
        {
            if (!CanApplyTo(experiment))
                throw new HandledException(HandledException.Severity.Warning, "Buffer Subtraction Error", TandemTargetReason);
        }

        public static void ValidateTandemSources(IEnumerable<ExperimentData> sources)
        {
            if (HasCorrectedSource(sources))
                throw new ArgumentException(CorrectedSourceReason, nameof(sources));
        }
    }
}
