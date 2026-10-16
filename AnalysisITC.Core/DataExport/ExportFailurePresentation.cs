using System;
using AnalysisITC.Core.Application;

namespace AnalysisITC.Core.Export
{
    public static class ExportFailurePresentation
    {
        public static string FormatMessage(string outputDescription, ExportOutcome outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            var label = string.IsNullOrWhiteSpace(outputDescription) ? "Export" : outputDescription.Trim();
            var reason = outcome.Error?.Message ?? "Unknown error";
            var remaining = Math.Max(0, outcome.PlannedCount - outcome.CompletedCount);
            var message = outcome.PlannedCount == 0
                ? $"{label} could not start."
                : $"{label} stopped. {outcome.CompletedCount} of {outcome.PlannedCount} files were written or replaced with the new export.";
            message += string.IsNullOrWhiteSpace(outcome.FailedPath)
                ? $"\n\nThe export could not be prepared.\n{reason}"
                : $"\n\nCould not write:\n{outcome.FailedPath}\n\n{reason}";
            if (remaining > 0)
                message += remaining == 1
                    ? "\n\n1 planned file was not written."
                    : $"\n\n{remaining} planned files were not written.";
            if (outcome.ReplacedCount > 0)
            {
                message += "\n\nThe folder now holds a mix of old and new exports. Existing files among the remaining planned outputs still contain the previous export; destinations without files remain absent.";
            }
            return message;
        }

        public static HandledException CreateException(string outputDescription, ExportOutcome outcome) =>
            new HandledException(HandledException.Severity.Error, "Export Failed", FormatMessage(outputDescription, outcome));

        public static void Report(string outputDescription, ExportOutcome outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            if (outcome.IsSuccess) return;
            if (outcome.Error != null) AppEventHandler.AddLog(outcome.Error);
            AppEventHandler.DisplayHandledException(CreateException(outputDescription, outcome));
        }
    }
}
