using AnalysisITC.Core.Application;

namespace AnalysisITC.Core.Presentation
{
    public static class ProcessingClearPresentation
    {
        public const string DeleteResultsTooltip = "Remove saved analysis results; retain experiment processing";
        public const string KeepTooltip = "Keep existing processing and fits";
        public const string ConfirmTooltip = "Reset processing without Undo";
        public const string HeadingTooltip = "Restore processing defaults and remove attached fits";
        public static string Tooltip(ProcessingClearScope scope) => scope switch
        {
            ProcessingClearScope.Selected => "Reset the selected experiment's processing and attached fit",
            ProcessingClearScope.Active => "Reset active experiments' processing and attached fits",
            _ => "Reset all experiments' processing and attached fits"
        };
        public static string Confirmation(int count) =>
            $"Clear processing and attached fits for {count} experiments? Processing settings will return to defaults. Experiment inputs, attributes, and saved Analysis Results will be retained. This cannot be undone.";
        public const string InvalidatedMessage = "Processing could not be cleared because the document changed or an operation started. Try again when processing and fitting have finished.";
    }
}
