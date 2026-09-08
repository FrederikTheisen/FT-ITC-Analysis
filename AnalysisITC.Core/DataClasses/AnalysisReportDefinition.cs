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
        readonly List<string> supportingExperimentIds = new List<string>();
        AnalysisStudyContext studyContext = new AnalysisStudyContext();
        AnalysisInterpretationOptions interpretationSettings = AnalysisInterpretationOptions.Default();
        AnalysisInterpretationRecord approvedInterpretation;

        public AnalysisReport()
        {
            Date = DateTime.UtcNow;
            Name = "Analysis report";
        }

        public IReadOnlyList<string> ResultIds => resultIds.AsReadOnly();
        public IReadOnlyList<string> SupportingExperimentIds => supportingExperimentIds.AsReadOnly();
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
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (resultIds.SequenceEqual(next, StringComparer.Ordinal)) return;
            resultIds.Clear();
            resultIds.AddRange(next);
            MarkModified();
        }

        public void SetSupportingExperimentIds(IEnumerable<string> ids)
        {
            var next = (ids ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (supportingExperimentIds.SequenceEqual(next, StringComparer.Ordinal)) return;
            supportingExperimentIds.Clear();
            supportingExperimentIds.AddRange(next);
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
            approvedInterpretation.Origin = AnalysisInterpretationOrigin.AiGenerated;
            approvedInterpretation.InterpretationMarkdown = AnalysisInterpretationResponseParser.Parse(
                approvedInterpretation.InterpretationMarkdown);
            approvedInterpretation.ApprovedAtUtc = DateTime.UtcNow;
            MarkModified();
        }

        public void SetManualInterpretation(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                ClearApprovedInterpretation();
                return;
            }
            approvedInterpretation = new AnalysisInterpretationRecord
            {
                Origin = AnalysisInterpretationOrigin.Manual,
                InterpretationMarkdown = AnalysisInterpretationResponseParser.ParseManual(markdown),
                ApprovedAtUtc = DateTime.UtcNow,
            };
            MarkModified();
        }

        public void UpdateApprovedInterpretationText(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                ClearApprovedInterpretation();
                return;
            }
            if (approvedInterpretation == null || approvedInterpretation.Origin == AnalysisInterpretationOrigin.Manual)
            {
                SetManualInterpretation(markdown);
                return;
            }
            approvedInterpretation.InterpretationMarkdown = ParseEditorMarkdown(markdown);
            approvedInterpretation.UserEdited = true;
            approvedInterpretation.ApprovedAtUtc = DateTime.UtcNow;
            MarkModified();
        }

        static string ParseEditorMarkdown(string markdown)
        {
            var value = (markdown ?? "").Trim();
            if (!value.StartsWith("## ", StringComparison.Ordinal))
                value = "## Overall interpretation\n" + value;
            return AnalysisInterpretationResponseParser.Parse(value);
        }

        public void ClearApprovedInterpretation()
        {
            if (approvedInterpretation == null) return;
            approvedInterpretation = null;
            MarkModified();
        }

        internal void Restore(
            IEnumerable<string> ids,
            IEnumerable<string> experimentIds,
            AnalysisStudyContext context,
            AnalysisInterpretationOptions settings,
            AnalysisInterpretationRecord approved)
        {
            resultIds.Clear();
            resultIds.AddRange((ids ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id)));
            supportingExperimentIds.Clear();
            supportingExperimentIds.AddRange((experimentIds ?? Enumerable.Empty<string>())
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
