using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Presentation;

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
        AnalysisReportOptions presentationSettings = new AnalysisReportOptions();
        bool hasPresentationSettings;
        AnalysisInterpretationRecord approvedInterpretation;

        public AnalysisReport()
        {
            Date = DateTime.UtcNow;
            LastEditedUtc = Date;
        }

        public DateTime? LastEditedUtc { get; private set; }
        public override string Name
        {
            get => base.Name;
            set
            {
                var next = value ?? "";
                if (base.Name == next) return;
                if (!DocumentDirtyTracker.IsRestoringDocument && approvedInterpretation != null
                    && approvedInterpretation.EvidenceFingerprintScheme == AnalysisInterpretationPromptBuilder.LegacyEvidenceFingerprintScheme
                    && approvedInterpretation.ReportNameForFingerprint == null)
                    approvedInterpretation.ReportNameForFingerprint = base.Name;
                base.Name = next;
            }
        }

        public override void MarkModified()
        {
            if (DocumentDirtyTracker.IsRestoringDocument) return;
            var now = DateTime.UtcNow;
            LastEditedUtc = LastEditedUtc.HasValue && now <= LastEditedUtc.Value
                ? LastEditedUtc.Value.AddTicks(1) : now;
            base.MarkModified();
        }

        public IReadOnlyList<string> ResultIds => resultIds.AsReadOnly();
        public IReadOnlyList<string> SupportingExperimentIds => supportingExperimentIds.AsReadOnly();
        public string AuthorComments { get => Comments; set => Comments = value; }
        public AnalysisStudyContext StudyContext => studyContext.Copy();
        public AnalysisInterpretationOptions InterpretationSettings => interpretationSettings.Copy();
        public AnalysisReportOptions PresentationSettings => presentationSettings.Copy(includeGenerationMetadata: false);
        public bool HasPresentationSettings => hasPresentationSettings;
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
            var next = (context ?? new AnalysisStudyContext()).Copy();
            if (ValuesEqual(studyContext, next)) return;
            studyContext = next;
            MarkModified();
        }

        public void UpdateInterpretationSettings(AnalysisInterpretationOptions settings)
        {
            var next = (settings ?? AnalysisInterpretationOptions.Default()).Copy();
            if (ValuesEqual(interpretationSettings, next)) return;
            interpretationSettings = next;
            MarkModified();
        }

        public void UpdatePresentationSettings(AnalysisReportOptions settings)
        {
            if (hasPresentationSettings && PresentationSettingsEqual(settings)) return;
            presentationSettings = (settings ?? new AnalysisReportOptions()).Copy(includeGenerationMetadata: false);
            hasPresentationSettings = true;
            MarkModified();
        }

        internal void InitializePresentationSettings(AnalysisReportOptions settings)
        {
            presentationSettings = (settings ?? new AnalysisReportOptions()).Copy(includeGenerationMetadata: false);
            hasPresentationSettings = true;
        }

        public bool PresentationSettingsEqual(AnalysisReportOptions settings)
        {
            var left = presentationSettings;
            var right = settings ?? new AnalysisReportOptions();
            return left.DocumentLabel == right.DocumentLabel
                && ((left.AutomaticTitle && right.AutomaticTitle) || left.Title == right.Title)
                && left.AutomaticTitle == right.AutomaticTitle && left.EnergyUnitFamily == right.EnergyUnitFamily
                && left.EnergyUnitOverride == right.EnergyUnitOverride && left.UseKelvin == right.UseKelvin
                && left.UncertaintyDisplayStyle == right.UncertaintyDisplayStyle
                && left.IncludeInjectionTables == right.IncludeInjectionTables
                && left.CondenseRepeatedExperiments == right.CondenseRepeatedExperiments
                && left.ExpandedExplanations == right.ExpandedExplanations
                && left.ExtraTraceability == right.ExtraTraceability
                && left.ReportId == (right.ReportId?.Trim() ?? "")
                && left.AdvancedSections.Select(item => item.Key).SequenceEqual(right.AdvancedSections.Select(item => item.Key), StringComparer.Ordinal);
        }

        /// <summary>
        /// Creates a detached report definition for transient operations such as
        /// previewing or exporting interpretation input. The copy retains the
        /// report identity, metadata, selections, context, settings, and saved
        /// interpretation without sharing mutable state with this report.
        /// </summary>
        public AnalysisReport CreateDetachedCopy(
            AnalysisStudyContext context = null,
            AnalysisInterpretationOptions settings = null)
        {
            var copy = new AnalysisReport();
            copy.SetID(UniqueID);
            copy.SetFileName(FileName);
            copy.SetDate(Date);
            copy.DateSource = DateSource;
            copy.Name = Name;
            copy.Comments = Comments;
            copy.Restore(
                ResultIds,
                SupportingExperimentIds,
                context ?? StudyContext,
                settings ?? InterpretationSettings,
                approvedInterpretation,
                HasPresentationSettings ? PresentationSettings : null,
                LastEditedUtc);
            copy.SetInterpretationFreshness(new AnalysisInterpretationFreshnessResult
            {
                Status = InterpretationFreshness,
                Reason = InterpretationFreshnessReason,
            });
            copy.MarkClean();
            return copy;
        }

        public void ApproveInterpretation(AnalysisInterpretationRecord interpretation)
        {
            if (interpretation == null) throw new ArgumentNullException(nameof(interpretation));
            var next = interpretation.Copy();
            next.Origin = AnalysisInterpretationOrigin.AiGenerated;
            if (next.EvidenceFingerprintScheme == AnalysisInterpretationPromptBuilder.LegacyEvidenceFingerprintScheme
                && next.ReportNameForFingerprint == null)
                next.ReportNameForFingerprint = approvedInterpretation?.EvidenceFingerprintScheme == next.EvidenceFingerprintScheme
                    && approvedInterpretation.InputFingerprint == next.InputFingerprint
                    ? approvedInterpretation.ReportNameForFingerprint ?? Name : Name;
            next.InterpretationMarkdown = AnalysisInterpretationResponseParser.Parse(next.InterpretationMarkdown);
            next.ApprovedAtUtc = approvedInterpretation?.ApprovedAtUtc ?? default;
            if (ValuesEqual(approvedInterpretation, next)) return;
            next.ApprovedAtUtc = DateTime.UtcNow;
            approvedInterpretation = next;
            MarkModified();
        }

        public void SetManualInterpretation(string markdown, string assessmentContextFingerprint = null)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                ClearApprovedInterpretation();
                return;
            }
            var text = AnalysisInterpretationResponseParser.ParseManual(markdown);
            var fingerprint = assessmentContextFingerprint ?? "";
            if (approvedInterpretation?.Origin == AnalysisInterpretationOrigin.Manual
                && approvedInterpretation.InterpretationMarkdown == text
                && approvedInterpretation.AssessmentContextFingerprint == fingerprint) return;
            approvedInterpretation = new AnalysisInterpretationRecord
            {
                Origin = AnalysisInterpretationOrigin.Manual,
                InterpretationMarkdown = text,
                ApprovedAtUtc = DateTime.UtcNow,
                AssessmentContextFingerprint = fingerprint,
            };
            MarkModified();
        }

        public void UpdateApprovedInterpretationText(string markdown, string assessmentContextFingerprint = null)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                ClearApprovedInterpretation();
                return;
            }
            if (approvedInterpretation == null || approvedInterpretation.Origin == AnalysisInterpretationOrigin.Manual)
            {
                SetManualInterpretation(markdown, assessmentContextFingerprint);
                return;
            }
            var text = ParseEditorMarkdown(markdown);
            if (approvedInterpretation.InterpretationMarkdown == text
                && (assessmentContextFingerprint == null
                    || approvedInterpretation.AssessmentContextFingerprint == assessmentContextFingerprint)) return;
            approvedInterpretation.InterpretationMarkdown = text;
            approvedInterpretation.UserEdited = true;
            approvedInterpretation.ApprovedAtUtc = DateTime.UtcNow;
            if (assessmentContextFingerprint != null)
                approvedInterpretation.AssessmentContextFingerprint = assessmentContextFingerprint;
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
            AnalysisInterpretationRecord approved,
            AnalysisReportOptions presentation = null,
            DateTime? lastEditedUtc = null)
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
            presentationSettings = (presentation ?? new AnalysisReportOptions()).Copy(includeGenerationMetadata: false);
            hasPresentationSettings = presentation != null;
            LastEditedUtc = lastEditedUtc;
        }

        internal void RestoreLastEditedUtc(DateTime? value) => LastEditedUtc = value;

        static bool ValuesEqual<T>(T left, T right) =>
            JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

        internal void SetInterpretationFreshness(AnalysisInterpretationFreshnessResult freshness)
        {
            InterpretationFreshness = freshness?.Status ?? AnalysisInterpretationFreshness.Unverifiable;
            InterpretationFreshnessReason = freshness?.Reason ?? "The interpretation could not be evaluated.";
        }
    }
}
