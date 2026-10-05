using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Presentation
{
    public static class AnalysisReportExperimentReferences
    {
        public const string ResultTooltip = "Reports this fit and its experiments";
        public const string SupportingExperimentTooltip = "Adds saved data as a supporting experiment";
        public const string CoveredExperimentTooltip = "Already reported with a selected result";
        public const string BufferReferenceTooltip = "Buffer run subtracted from a selected result";
        public const string TandemSourceTooltip = "Run merged into a selected tandem result";
        public const string BufferAndTandemSourceTooltip = "Buffer and tandem source for selected results";

        public static IReadOnlyList<ExperimentData> Candidates(
            IEnumerable<AnalysisResult> results, IEnumerable<ExperimentData> availableExperiments)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var data in Members(results))
            {
                var bufferId = data.BufferSubtractionSettings?.ReferenceExperimentId;
                if (!string.IsNullOrWhiteSpace(bufferId)) ids.Add(bufferId);
                foreach (var id in data.TandemSourceExperimentIds) ids.Add(id);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            return (availableExperiments ?? Enumerable.Empty<ExperimentData>())
                .Where(data => data != null && ids.Contains(data.UniqueID) && seen.Add(data.UniqueID))
                .ToList();
        }

        public static string PickerRole(ExperimentData experiment, IEnumerable<AnalysisResult> results)
        {
            var (coveringIndex, isBufferReference, isTandemSource) = Relation(experiment, results);
            if (coveringIndex >= 0)
                return "Included through Result " + AnalysisReportReferenceLabels.Result(coveringIndex);

            var roles = new List<string>();
            if (isBufferReference) roles.Add("Buffer reference");
            if (isTandemSource) roles.Add("Tandem source");
            return string.Join("; ", roles);
        }

        public static string PickerTooltip(ExperimentData experiment, IEnumerable<AnalysisResult> results)
        {
            var (coveringIndex, isBufferReference, isTandemSource) = Relation(experiment, results);
            if (coveringIndex >= 0) return CoveredExperimentTooltip;
            if (isBufferReference && isTandemSource) return BufferAndTandemSourceTooltip;
            if (isBufferReference) return BufferReferenceTooltip;
            if (isTandemSource) return TandemSourceTooltip;
            return SupportingExperimentTooltip;
        }

        static (int CoveringIndex, bool IsBufferReference, bool IsTandemSource) Relation(
            ExperimentData experiment, IEnumerable<AnalysisResult> results)
        {
            if (experiment == null || string.IsNullOrWhiteSpace(experiment.UniqueID)) return (-1, false, false);
            var selected = (results ?? Enumerable.Empty<AnalysisResult>()).ToList();
            for (var index = 0; index < selected.Count; index++)
                if (Members(new[] { selected[index] }).Any(data => data.UniqueID == experiment.UniqueID))
                    return (index, false, false);

            var members = Members(selected).ToList();
            return (-1,
                members.Any(data => data.BufferSubtractionSettings?.ReferenceExperimentId == experiment.UniqueID),
                members.Any(data => data.TandemSourceExperimentIds.Contains(experiment.UniqueID)));
        }

        static IEnumerable<ExperimentData> Members(IEnumerable<AnalysisResult> results) =>
            (results ?? Enumerable.Empty<AnalysisResult>())
                .SelectMany(result => result?.Solution?.Solutions ?? new List<SolutionInterface>())
                .Select(solution => solution?.Data).Where(data => data != null);
    }
}
