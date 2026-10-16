using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Presentation
{
    public static class ProjectInformationPresentation
    {
        public const string WindowTitle = "Project Information";
        public const string FileMenuTitle = "Project Information…";
        public const string UnsavedProject = "Unsaved project";
        public const string ExperimentsLabel = "Experiments";
        public const string AnalysisResultsLabel = "Analysis Results";
        public const string SavedReportsLabel = "Saved reports";
        public const string LastEditedLabel = "Last saved";
        public const string NameLabel = "Name";
        public const string ReportNameLabel = NameLabel;
        public const string OpenLabel = "Open";
        public const string RenameLabel = "Rename";
        public const string DeleteLabel = "Delete";
        public const string DetailsLabel = "Details";
        public const string CopyAllLabel = "Copy All";
        public const string CloseLabel = "Close";
        public const string CancelLabel = "Cancel";
        public const string RenameDialogTitle = "Rename Report";
        public const string RenameWindowTitle = RenameDialogTitle;
        public const string DeleteDialogTitle = "Delete Report";
        public const string DeleteWindowTitle = DeleteDialogTitle;
        public const string DetailsWindowTitle = "Report Details";
        public const string NotRecorded = "Not recorded";
        public const string MissingResult = "Missing result";
        public const string StorageNote = "Saved reports contain selections, settings, study context, and approved interpretation text. They contain no PDFs or copies of measurements.";
        public const string FileMenuTooltip = "Inspect project content and manage saved reports";
        public const string TabsTooltip = "Choose which project content to inspect";
        public const string ListTooltip = "Inspect items saved in this project";
        public const string ExperimentsTooltip = "Inspect loaded measurements, including inactive experiments";
        public const string AnalysisResultsTooltip = "Inspect saved fits and their experiment counts";
        public const string SavedReportsTooltip = "Inspect saved report definitions and text";
        public const string ContentSummaryTooltip = "Includes inactive experiments; excludes experiment copies inside results";
        public const string NameTooltip = "Leave blank to use a content summary";
        public const string ReportNameTooltip = NameTooltip;
        public const string OpenTooltip = "Restore the selected report's saved contents";
        public const string RenameTooltip = "Change the report title and saved name";
        public const string DeleteTooltip = "Remove saved report settings and text";
        public const string DetailsTooltip = "Inspect saved report metadata and text";
        public const string DetailsTextTooltip = "Select saved text to copy it";
        public const string DetailsCloseTooltip = "Return to project information";
        public const string CopyAllTooltip = "Copy saved user-entered text as plain text";
        public const string CloseTooltip = "Return to the main window";
        public const string CancelTooltip = "Leave the saved report unchanged";
        public const string MissingResultsTooltip = "Restore missing results before opening or renaming";
        public const string OverviewResultsLabel = "Analysis Results";
        public const string OverviewExperimentsLabel = "Experiments";
        public const string OverviewStatusLabel = "Status";
        public const string OverviewInterpretationLabel = "Saved interpretation";
        public const string OverviewSubtitleLabel = "Subtitle";
        public const string OverviewResultsTooltip = "Saved result references, including missing results";
        public const string OverviewExperimentsTooltip = "Counts distinct fitted experiments and saved supporting selections";
        public const string OverviewStatusTooltip = "Whether referenced Analysis Results are loaded";
        public const string OverviewInterpretationTooltip = "Whether approved interpretation text is stored";
        public const string OverviewSubtitleTooltip = "Saved text below the report title";
        public const string ResultsAvailable = "Results available";
        public const string NoResultsSelected = "None selected";
        public const string InterpretationPresent = "Present";
        public const string NoInterpretationSaved = "None saved";

        public static string ExperimentCount(int count) => count + " experiment" + (count == 1 ? "" : "s");

        public static void RenameReport(AnalysisReport report, string title)
        {
            var draft = report.CreateDetachedCopy();
            draft.CaptureLegacyFingerprintName(AnalysisReportPresentation.DisplayName(report,
                id => DataManager.Results.FirstOrDefault(result => result.UniqueID == id)));
            var settings = draft.PresentationSettings;
            settings.Title = (title ?? "").Trim();
            settings.AutomaticTitle = string.IsNullOrWhiteSpace(settings.Title);
            draft.UpdatePresentationSettings(settings);
            report.SaveContentFrom(draft);
        }
        public static string ResultCount(int count) => count + " result" + (count == 1 ? "" : "s");
        public static string ReportCount(int count) => count + " report" + (count == 1 ? "" : "s");
        public static string ContentSummary(int experimentCount, int resultCount, int reportCount) =>
            ExperimentCount(experimentCount) + " · " + ResultCount(resultCount) + " · " + ReportCount(reportCount);

        public static string ExperimentDetail(ExperimentData experiment)
        {
            if (experiment == null) return "";
            var injections = experiment.Injections;
            var count = injections?.Count ?? 0;
            var integrated = injections?.Count(injection => injection.IsIntegrated) ?? 0;
            var fit = experiment.Solution == null ? "Not fitted"
                : experiment.Model.ModelType.GetProperties().Name + " (" + FitStatus(experiment.Solution.Convergence) + ")";
            return LocalDateTimeFormatter.ShortDateTime(experiment.Date) + " · " + count
                + (count == 1 ? " injection" : " injections") + "\n"
                + TemperatureText(experiment.MeasuredTemperature) + " · Cell: "
                + experiment.CellConcentration.AsConcentration(ConcentrationUnit.µM)
                + " · Syringe: " + experiment.SyringeConcentration.AsConcentration(ConcentrationUnit.µM) + "\n"
                + integrated + "/" + count + " integrated · Fit: " + fit;
        }

        public static string ResultDetail(AnalysisResult result)
        {
            if (result == null) return "";
            var members = result.Solution?.Solutions;
            var temperatures = members?.Select(member => member?.Data?.MeasuredTemperature ?? double.NaN).ToArray()
                ?? Array.Empty<double>();
            var temperature = temperatures.Length == 0 || temperatures.Any(value => !IsFinite(value))
                ? "Temperature unavailable"
                : temperatures.Min() == temperatures.Max() ? TemperatureText(temperatures[0])
                : temperatures.Min().ToString("G4") + "–" + temperatures.Max().ToString("G4") + " °C";
            var names = members == null || members.Count == 0 ? "Unavailable"
                : string.Join(", ", members.Select(member => (member?.Data?.Name ?? "Unavailable")
                    .Replace('\r', ' ').Replace('\n', ' ')));
            var manual = result.IsIndependentAssessmentCollection
                ? result.MemberAssessments.Any(member => member.Assessment?.IsManual == true)
                : result.BindingAssessment?.IsManual == true;
            return (result.Solution?.Model?.ModelType.GetProperties().Name ?? "Unknown model") + " · "
                + ExperimentCount(members?.Count ?? 0) + " · " + LocalDateTimeFormatter.ShortDateTime(result.Date) + "\n"
                + temperature + " · Fit: " + FitStatus(result.Solution?.Convergence) + " · " + ResultHealthText(result.Health) + " · "
                + NullModelComparisonPresentation.OutcomeText(result.CollectionAssessmentOutcome)
                + (manual ? result.IsIndependentAssessmentCollection ? " (includes manual)" : " (manual)" : "")
                + "\nExperiments: " + names;
        }

        static string TemperatureText(double value) => IsFinite(value) ? value.ToString("G4") + " °C" : "Temperature unavailable";
        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static string FitStatus(SolverConvergence convergence) => convergence?.Termination switch
        {
            SolverTermination.Converged or SolverTermination.SmallStep or SolverTermination.SmallGradient or SolverTermination.ReachedTarget => "Converged",
            SolverTermination.IterationLimit => "Stopped: iteration limit",
            SolverTermination.EvaluationLimit => "Stopped: evaluation limit",
            SolverTermination.TimeLimit => "Stopped: time limit",
            SolverTermination.Cancelled => "Cancelled",
            SolverTermination.InvalidValues => "Failed: invalid values",
            SolverTermination.Failed => "Failed",
            _ => "Status unknown",
        };
        static string ResultHealthText(AnalysisResultHealth health) => health switch
        {
            AnalysisResultHealth.Valid => "Valid",
            AnalysisResultHealth.Warning => "Valid with warnings",
            AnalysisResultHealth.PartialInvalid => "Partially invalid or stale",
            AnalysisResultHealth.Invalid => "Invalid or stale",
            _ => "Validity unknown",
        };

        public static IReadOnlyList<ProjectInformationOverviewRow> OverviewRows(
            AnalysisReport report, Func<string, AnalysisResult> resultResolver)
        {
            if (report == null) return Array.Empty<ProjectInformationOverviewRow>();
            var results = report.ResultIds.Select(id => resultResolver?.Invoke(id)).ToArray();
            var missing = results.Count(result => result == null);
            var unknownFittedCoverage = missing > 0;
            var fittedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var result in results.Where(result => result != null))
            {
                var models = result.Solution?.Model?.Models;
                if (models == null || models.Count == 0)
                {
                    unknownFittedCoverage = true;
                    continue;
                }
                foreach (var model in models)
                {
                    var dataId = model?.Solution?.Model?.Data?.UniqueID;
                    if (string.IsNullOrWhiteSpace(dataId)) unknownFittedCoverage = true;
                    else fittedIds.Add(dataId);
                }
            }
            var fitted = unknownFittedCoverage
                ? fittedIds.Count > 0 ? "At least " + fittedIds.Count + " fitted" : "Fitted count unavailable"
                : fittedIds.Count + " fitted";
            var supporting = report.SupportingExperimentIds.Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal).Count();
            var experiments = fitted + " · " + supporting + " supporting selected";
            var resultNames = results.Length == 0 ? NoResultsSelected : string.Join(Environment.NewLine,
                results.Select(result => result?.Name ?? MissingResult));
            var status = results.Length == 0 ? NoResultsSelected : missing > 0
                ? missing + " of " + results.Length + " result" + (results.Length == 1 ? "" : "s") + " missing"
                : ResultsAvailable;
            var rows = new List<ProjectInformationOverviewRow>
            {
                new ProjectInformationOverviewRow(OverviewResultsLabel, resultNames, OverviewResultsTooltip),
                new ProjectInformationOverviewRow(OverviewExperimentsLabel, experiments, OverviewExperimentsTooltip),
                new ProjectInformationOverviewRow(OverviewStatusLabel, status, OverviewStatusTooltip),
                new ProjectInformationOverviewRow(OverviewInterpretationLabel,
                    string.IsNullOrWhiteSpace(report.ApprovedInterpretation?.InterpretationMarkdown)
                        ? NoInterpretationSaved : InterpretationPresent, OverviewInterpretationTooltip),
            };
            var subtitle = report.PresentationSettings.DocumentLabel;
            if (!string.IsNullOrWhiteSpace(subtitle))
                rows.Add(new ProjectInformationOverviewRow(OverviewSubtitleLabel, subtitle, OverviewSubtitleTooltip));
            return rows.AsReadOnly();
        }

        public static string LastEditedText(AnalysisReport report) => report?.LastEditedUtc is DateTime edited
            ? LocalDateTimeFormatter.ShortDateTime(edited.ToLocalTime()) : NotRecorded;

        public static string DeleteConfirmation(AnalysisReport report, Func<string, AnalysisResult> resultResolver) =>
            "Delete “" + AnalysisReportPresentation.DisplayName(report, resultResolver)
            + "”? Its saved selections, settings, study context, and interpretation will be removed."
            + " Experiments, Analysis Results, and exported PDFs will be kept.";

        public static string DetailsText(AnalysisReport report, Func<string, AnalysisResult> resultResolver)
        {
            if (report == null) return "";
            var text = new StringBuilder();
            Append(text, "Name", AnalysisReportPresentation.DisplayName(report, resultResolver));
            Append(text, "Created", LocalDateTimeFormatter.ShortDateTime(report.Date.ToLocalTime()));
            Append(text, LastEditedLabel, LastEditedText(report));
            Append(text, "Results", string.Join(Environment.NewLine, report.ResultIds.Select(id =>
                resultResolver?.Invoke(id)?.Name ?? MissingResult)));
            Append(text, "Supporting experiment count", report.SupportingExperimentIds.Count.ToString());
            AppendSavedText(text, report, includeName: false, includeBlankPresentation: true);
            return text.ToString().TrimEnd();
        }

        public static string CopyText(AnalysisReport report)
        {
            if (report == null) return "";
            var text = new StringBuilder();
            AppendSavedText(text, report, includeName: true, includeBlankPresentation: false);
            return text.ToString().TrimEnd();
        }

        static void AppendSavedText(StringBuilder text, AnalysisReport report, bool includeName, bool includeBlankPresentation)
        {
            if (includeName) AppendIfPresent(text, NameLabel, report.Name);
            var options = report.PresentationSettings;
            foreach (var field in new[] { ("Title", options.Title), ("Document label", options.DocumentLabel), ("Report ID", options.ReportId) })
            {
                if (includeBlankPresentation) Append(text, field.Item1, field.Item2);
                else if (field.Item1 != "Title" || !options.AutomaticTitle)
                    AppendIfPresent(text, field.Item1, field.Item2);
            }
            AppendIfPresent(text, "Author comments", report.AuthorComments);
            AppendContext(text, report.StudyContext);
            AppendIfPresent(text, "Approved interpretation", report.ApprovedInterpretation?.InterpretationMarkdown);
        }

        static void AppendContext(StringBuilder text, AnalysisStudyContext context)
        {
            foreach (var field in new[]
            {
                ("Scientific question", context.ScientificQuestion), ("System description", context.SystemDescription),
                ("Component types", context.ComponentTypes), ("Interaction considerations", context.InteractionConsiderations),
                ("Expected outcome", context.ExpectedOutcome), ("Cell contents and role", context.CellContentsAndRole),
                ("Syringe contents and role", context.SyringeContentsAndRole), ("Related systems or constructs", context.RelatedSystemsOrConstructs),
                ("Previous results and controls", context.PreviousResultsAndControls), ("Buffer considerations", context.BufferConsiderations),
                ("Temperature considerations", context.TemperatureConsiderations), ("Additional notes", context.AdditionalNotes),
            }) AppendIfPresent(text, field.Item1, field.Item2);
            for (var index = 0; index < context.Experiments.Count; index++)
            {
                var item = context.Experiments[index];
                AppendIfPresent(text, "Experiment context " + (index + 1) + " purpose", item.Purpose);
                AppendIfPresent(text, "Experiment context " + (index + 1) + " annotation", item.Annotation);
            }
            for (var index = 0; index < context.References.Count; index++)
            {
                var item = context.References[index];
                AppendIfPresent(text, "Reference " + (index + 1) + " label", item.Label);
                AppendIfPresent(text, "Reference " + (index + 1) + " citation or URL", item.CitationOrUrl);
                AppendIfPresent(text, "Reference " + (index + 1) + " notes", item.Notes);
            }
        }

        static void AppendIfPresent(StringBuilder text, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) Append(text, label, value);
        }

        static void Append(StringBuilder text, string label, string value) =>
            text.Append(label).Append(':').AppendLine().AppendLine(value ?? "").AppendLine();
    }

    public sealed class ProjectInformationOverviewRow
    {
        public ProjectInformationOverviewRow(string label, string value, string tooltip)
        {
            Label = label ?? "";
            Value = value ?? "";
            Tooltip = tooltip ?? "";
        }

        public string Label { get; }
        public string Value { get; }
        public string Tooltip { get; }
    }
}
