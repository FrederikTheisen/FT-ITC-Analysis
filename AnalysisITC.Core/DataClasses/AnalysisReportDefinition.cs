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
            if (approvedInterpretation != null && approvedInterpretation.ReviewedResultIds == null)
                approvedInterpretation.ReviewedResultIds = resultIds.ToList();
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
            next.ReviewedResultIds = resultIds.ToList();
            if (ValuesEqual(approvedInterpretation, next)) return;
            next.ApprovedAtUtc = DateTime.UtcNow;
            approvedInterpretation = next;
            MarkModified();
        }

        public void SetManualInterpretation(string markdown, string assessmentContextFingerprint = null)
            => SetManualInterpretation(markdown, assessmentContextFingerprint, userEdit: false);

        void SetManualInterpretation(string markdown, string assessmentContextFingerprint, bool userEdit)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                ClearApprovedInterpretation();
                return;
            }
            var text = AnalysisInterpretationResponseParser.ParseManual(markdown);
            var fingerprint = assessmentContextFingerprint ?? "";
            if (!userEdit && approvedInterpretation?.Origin == AnalysisInterpretationOrigin.Manual
                && approvedInterpretation.InterpretationMarkdown == text
                && approvedInterpretation.AssessmentContextFingerprint == fingerprint) return;
            approvedInterpretation = new AnalysisInterpretationRecord
            {
                Origin = AnalysisInterpretationOrigin.Manual,
                InterpretationMarkdown = text,
                ApprovedAtUtc = DateTime.UtcNow,
                AssessmentContextFingerprint = fingerprint,
                ReviewedResultIds = resultIds.ToList(),
            };
            MarkModified();
        }

        public void UpdateApprovedInterpretationText(string markdown, string assessmentContextFingerprint = null)
            => SetManualInterpretation(markdown, assessmentContextFingerprint, userEdit: true);

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

        public void ConfirmInterpretation()
        {
            if (approvedInterpretation == null) return;
            approvedInterpretation.ConfirmedAtUtc = DateTime.UtcNow;
            approvedInterpretation.ReviewedResultIds = resultIds.ToList();
            MarkModified();
        }

        internal void RestoreInterpretation(AnalysisInterpretationRecord record) => approvedInterpretation = record?.Copy();

        internal string ContentKey()
        {
            var presentation = PresentationSettings;
            // Compare persisted choices, excluding the current output purpose and
            // the resolved text of an automatic title.
            presentation.OutputPurpose = ResultOutputPurpose.Standard;
            if (presentation.AutomaticTitle) presentation.Title = "";
            return JsonSerializer.Serialize(new
            {
                Name, Comments, ResultIds, SupportingExperimentIds, StudyContext,
                InterpretationSettings, ApprovedInterpretation, HasPresentationSettings,
                PresentationSettings = presentation,
            });
        }

        internal bool SaveContentFrom(AnalysisReport source, bool newReport = false)
        {
            // Copy and normalize before publishing any changed state or notification.
            var next = source.CreateDetachedCopy();
            var settings = next.PresentationSettings;
            next.Name = settings.AutomaticTitle ? "" : (settings.Title ?? "").Trim();
            if (!newReport && ContentKey() == next.ContentKey()) return false;
            var now = DateTime.UtcNow;
            if (LastEditedUtc.HasValue && now <= LastEditedUtc.Value) now = LastEditedUtc.Value.AddTicks(1);
            Restore(next.ResultIds, next.SupportingExperimentIds, next.StudyContext,
                next.InterpretationSettings, next.ApprovedInterpretation,
                next.HasPresentationSettings ? next.PresentationSettings : null, now);
            RestoreTextMetadata(next.Name, next.Comments);
            MarkModified();
            return true;
        }

        internal void CaptureLegacyFingerprintName(string displayName)
        {
            if (approvedInterpretation != null
                && approvedInterpretation.EvidenceFingerprintScheme == AnalysisInterpretationPromptBuilder.LegacyEvidenceFingerprintScheme)
                approvedInterpretation.ReportNameForFingerprint = approvedInterpretation.ReportNameForFingerprint ?? displayName;
        }

        internal void PreserveFingerprintSource(string displayName)
        {
            if (approvedInterpretation == null || approvedInterpretation.Origin != AnalysisInterpretationOrigin.AiGenerated) return;
            approvedInterpretation.ReportIdForFingerprint = approvedInterpretation.ReportIdForFingerprint ?? UniqueID;
            approvedInterpretation.ReportDateUtcForFingerprint = approvedInterpretation.ReportDateUtcForFingerprint
                ?? AnalysisInterpretationPackageBuilder.ReportDateUtc(Date);
            CaptureLegacyFingerprintName(displayName);
        }

        static bool ValuesEqual<T>(T left, T right) =>
            JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

        internal void SetInterpretationFreshness(AnalysisInterpretationFreshnessResult freshness)
        {
            InterpretationFreshness = freshness?.Status ?? AnalysisInterpretationFreshness.Unverifiable;
            InterpretationFreshnessReason = freshness?.Reason ?? "The interpretation could not be evaluated.";
        }
    }
}
