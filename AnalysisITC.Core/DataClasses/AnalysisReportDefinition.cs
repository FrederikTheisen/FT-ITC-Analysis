using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Interpretation;

namespace AnalysisITC.Core.Data
{
    /// <summary>
    /// Persistent definition of a report. Rendering produces a separate,
    /// transient <see cref="Presentation.AnalysisReportDocument"/>.
    /// </summary>
    public sealed class AnalysisReport : ITCDataContainer
    {
        readonly List<string> resultIds = new List<string>();
        AnalysisStudyContext studyContext = new AnalysisStudyContext();
        AnalysisInterpretationOptions interpretationSettings = AnalysisInterpretationOptions.Default();
        AnalysisInterpretationRecord approvedInterpretation;

        public AnalysisReport()
        {
            Date = DateTime.UtcNow;
            Name = "Analysis report";
        }

        public IReadOnlyList<string> ResultIds => resultIds.AsReadOnly();
        public string AuthorComments { get => Comments; set => Comments = value; }
        public AnalysisStudyContext StudyContext => studyContext.Copy();
        public AnalysisInterpretationOptions InterpretationSettings => interpretationSettings.Copy();
        public AnalysisInterpretationRecord ApprovedInterpretation => approvedInterpretation?.Copy();
        public AnalysisInterpretationFreshness InterpretationFreshness { get; private set; } = AnalysisInterpretationFreshness.Unverifiable;
        public string InterpretationFreshnessReason { get; private set; } = "The interpretation has not been evaluated.";

        public void SetResultIds(IEnumerable<string> ids)
        {
            var next = (ids ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToList();
            if (resultIds.SequenceEqual(next, StringComparer.Ordinal)) return;
            resultIds.Clear();
            resultIds.AddRange(next);
            MarkModified();
        }

        public void UpdateStudyContext(AnalysisStudyContext context)
        {
            studyContext = (context ?? new AnalysisStudyContext()).Copy();
            MarkModified();
        }

        public void UpdateInterpretationSettings(AnalysisInterpretationOptions settings)
        {
            interpretationSettings = (settings ?? AnalysisInterpretationOptions.Default()).Copy();
            MarkModified();
        }

        public void ApproveInterpretation(AnalysisInterpretationRecord interpretation)
        {
            if (interpretation == null) throw new ArgumentNullException(nameof(interpretation));
            approvedInterpretation = interpretation.Copy();
            approvedInterpretation.ApprovedAtUtc = DateTime.UtcNow;
            MarkModified();
        }

        public void ClearApprovedInterpretation()
        {
            if (approvedInterpretation == null) return;
            approvedInterpretation = null;
            MarkModified();
        }

        internal void Restore(
            IEnumerable<string> ids,
            AnalysisStudyContext context,
            AnalysisInterpretationOptions settings,
            AnalysisInterpretationRecord approved)
        {
            resultIds.Clear();
            resultIds.AddRange((ids ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id)));
            studyContext = (context ?? new AnalysisStudyContext()).Copy();
            interpretationSettings = (settings ?? AnalysisInterpretationOptions.Default()).Copy();
            approvedInterpretation = approved?.Copy();
        }

        internal void SetInterpretationFreshness(AnalysisInterpretationFreshnessResult freshness)
        {
            InterpretationFreshness = freshness?.Status ?? AnalysisInterpretationFreshness.Unverifiable;
            InterpretationFreshnessReason = freshness?.Reason ?? "The interpretation could not be evaluated.";
        }
    }
}
