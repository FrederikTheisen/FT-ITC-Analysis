using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Interpretation;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>A window-owned draft. Only Save publishes it to the project.</summary>
    public sealed class AnalysisReportEditSession
    {
        readonly Guid documentId = DocumentDirtyTracker.CurrentDocumentId;
        string baseline;
        bool interpretationEdited;
        public AnalysisReport Draft { get; private set; }
        public AnalysisReport SavedReport { get; private set; }
        public bool HasChanges => interpretationEdited || Draft.ContentKey() != baseline;
        public bool NeedsSave => SavedReport == null || HasChanges || SavedReport.Name != SavedName;
        string SavedName => Draft.PresentationSettings.AutomaticTitle ? "" : (Draft.PresentationSettings.Title ?? "").Trim();
        public bool IsCurrent => documentId == DocumentDirtyTracker.CurrentDocumentId
            && (SavedReport == null || DataManager.Reports.Contains(SavedReport));

        public AnalysisReportEditSession(AnalysisReport draft, AnalysisReport savedReport = null)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            SavedReport = savedReport;
            Draft = draft.CreateDetachedCopy();
            var record = Draft.ApprovedInterpretation;
            if (record != null && record.ReviewedResultIds == null)
            {
                record.ReviewedResultIds = Draft.ResultIds.ToList();
                Draft.RestoreInterpretation(record);
            }
            AcceptBaseline();
        }

        public void AcceptBaseline()
        {
            baseline = Draft.ContentKey();
            interpretationEdited = false;
        }

        public void UpdateInterpretationText(string text, string assessmentFingerprint)
        {
            Draft.UpdateApprovedInterpretationText(text, assessmentFingerprint);
            interpretationEdited = true;
        }

        public bool HasInterpretationTextChanges(string text) => TextDiffers(Draft.ApprovedInterpretation, text);

        static bool TextDiffers(AnalysisInterpretationRecord record, string text)
        {
            var edited = (text ?? "").Trim();
            try
            {
                if (edited.Length > 0)
                {
                    if (record?.Origin == AnalysisInterpretationOrigin.AiGenerated)
                    {
                        edited = AnalysisInterpretationResponseParser.Parse(edited);
                    }
                    else edited = AnalysisInterpretationResponseParser.ParseManual(edited);
                }
                return !string.Equals((record?.InterpretationMarkdown ?? "").Trim(), edited, StringComparison.Ordinal);
            }
            catch (AnalysisInterpretationValidationException) { return true; }
        }

        public void ConfirmInterpretation()
        {
            Draft.ConfirmInterpretation();
        }

        public AnalysisReport Save(bool saveAs, Func<string, AnalysisResult> resultResolver,
            Func<string, ExperimentData> experimentResolver)
        {
            if (!IsCurrent) throw new InvalidOperationException("The report no longer belongs to this project.");
            if (!saveAs && !NeedsSave) return SavedReport;
            var validation = AnalysisReportBuilder.Validate(Draft, resultResolver, experimentResolver);
            if (!validation.IsValid) throw new InvalidOperationException(validation.Errors.FirstOrDefault() ?? "The report cannot be saved.");
            var prepared = Draft.CreateDetachedCopy();
            var displayName = AnalysisReportPresentation.DisplayName(prepared, resultResolver);
            prepared.CaptureLegacyFingerprintName(displayName);
            var target = saveAs || SavedReport == null ? new AnalysisReport() : SavedReport;
            if (!saveAs && SavedReport == null)
            {
                target.SetID(prepared.UniqueID);
                target.SetDate(prepared.Date);
            }
            if (saveAs)
            {
                prepared.PreserveFingerprintSource(displayName);
                prepared.SetID(target.UniqueID);
                prepared.SetDate(target.Date);
            }
            target.SaveContentFrom(prepared, SavedReport == null || saveAs);
            // Publish only after all preparation and validation have succeeded.
            SavedReport = target;
            if (!DataManager.Reports.Contains(target)) DataManager.AddReport(target);
            Draft = target.CreateDetachedCopy();
            AcceptBaseline();
            return target;
        }
    }

    public static class AnalysisReportEditingPresentation
    {
        public const string SavedReportsLabel = "Saved Reports…";
        public const string SavedReportsTooltip = "Open a report stored in this project";
        public const string NoSavedReportsText = "No saved reports in this project.";
        public const string SaveLabel = "Save Report";
        public const string SaveAsLabel = "Save Report As…";
        public const string SaveTooltip = "Store this report in the current project";
        public const string SaveAsTooltip = "Store a separate copy in this project";
        public const string ReviewText = "The included results have changed or been updated. Please confirm the interpretation is still accurate.";
        public const string ReviewTooltip = "Check the text against the updated results";
        public const string ConfirmLabel = "Confirm";
        public const string ConfirmTooltip = "Confirm the interpretation is still accurate";
        public const string CloseTooltip = "Close the report builder";
        public const string DiscardTooltip = "Leave without saving report changes";
        public const string CancelTooltip = "Keep editing this report";
        public const string GeneratedTextTooltip = "Add to the report to edit";
        public const string LeaveTitle = "Save Report Changes?";
        public const string LeaveText = "Save your report changes in the current project before continuing?";

        public static IEnumerable<AnalysisReport> SavedReports(IEnumerable<AnalysisReport> reports) =>
            reports.OrderByDescending(report => report.LastEditedUtc.HasValue)
                .ThenByDescending(report => report.LastEditedUtc?.ToUniversalTime());

        public static bool NeedsInterpretationReview(AnalysisReport report, Func<string, AnalysisResult> resolver)
        {
            var record = report?.ApprovedInterpretation;
            if (record == null) return false;
            if (record.ReviewedResultIds != null
                && !new HashSet<string>(record.ReviewedResultIds, StringComparer.Ordinal).SetEquals(report.ResultIds)) return true;
            var reviewed = record.ConfirmedAtUtc ?? (record.ApprovedAtUtc != default ? record.ApprovedAtUtc : record.GeneratedAtUtc);
            if (reviewed == default) return false; // No recorded instant establishes an update after the text.
            // Provenance dates are UTC, including historical values with no Kind.
            var reviewedUtc = reviewed.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(reviewed, DateTimeKind.Utc) : reviewed.ToUniversalTime();
            return report.ResultIds.Select(resolver).Any(result => result != null &&
                (result.LastContentChangedUtc.HasValue
                    ? Utc(result.LastContentChangedUtc.Value)
                    // Historical result dates are local, including unspecified values.
                    : result.Date.ToUniversalTime()) > reviewedUtc);
        }

        static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
    }
}
