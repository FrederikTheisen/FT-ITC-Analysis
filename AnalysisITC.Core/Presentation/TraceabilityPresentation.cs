namespace AnalysisITC.Core.Presentation
{
    /// <summary>Shared user-facing wording and startup policy for traceability.</summary>
    public static class TraceabilityPresentation
    {
        public const string SectionHeader = "Attribution and traceability";
        public const string ModeLabel = "Enable Traceability Mode";
        public const string ModeExplanation = "Requires a user name and adds attribution to reports. Optionally asks for experiment and sample IDs after loading data.";
        public const string UserNameDescription = "Used for new Analysis Results and report attribution. Existing results keep their saved user name.";
        public const string StartupConfirmationDescription = "New and updated Analysis Results and newly generated reports use this name. Change it later in General Preferences.";
        public const string UserNameLabel = "User name";
        public const string ConfirmUserLabel = "Confirm User";
        public const string ConfirmUserAtStartupLabel = "Confirm user at startup";
        public const string AnalyzedByLabel = "Analyzed by";
        public const string ImportIdentifiersLabel = "Review experiment and sample IDs after import";
        public const string UserNameRequired = "A user name is required when Traceability Mode is enabled.";
        public const string ModeToolTip = "Adds report attribution and identifier prompts.";
        public const string UserNameToolTip = "Name attributed to new results and reports.";
        public const string ConfirmUserAtStartupToolTip = "Check who is working on this computer.";
        public const string ImportIdentifiersToolTip = "Optional laboratory identifiers; shared values are allowed.";

        public static bool ShouldConfirmAtStartup(bool traceabilityModeEnabled, bool confirmUserAtStartup, string userName) =>
            traceabilityModeEnabled && (confirmUserAtStartup || string.IsNullOrWhiteSpace(userName));
    }
}
