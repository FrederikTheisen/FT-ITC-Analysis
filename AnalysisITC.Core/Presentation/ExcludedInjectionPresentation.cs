using AnalysisITC.Core.Application;

namespace AnalysisITC.Core.Presentation
{
    public static class ExcludedInjectionPresentation
    {
        public static bool CanHideExcludedInjections => !AppSettings.TraceabilityModeEnabled;
        public static bool ShowExcludedInjections(bool requested) => !CanHideExcludedInjections || requested;
        public static string VisibilityTooltip => CanHideExcludedInjections
            ? "Show injections excluded from the fit."
            : "Traceability Mode keeps excluded injections visible.";
    }
}
