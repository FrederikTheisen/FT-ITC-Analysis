using AnalysisITC.Core.Application;

namespace AnalysisITC.Core.Presentation
{
    public static class UncertaintyDisplayPresentation
    {
        public const string SymmetricLabel = "±";
        public const string LimitsLabel = "95% limits";
        public const string BothLabel = "± and 95% limits";
        public static string StyleLabel(UncertaintyDisplayStyle style) => style switch
        {
            UncertaintyDisplayStyle.StandardDeviation => SymmetricLabel,
            UncertaintyDisplayStyle.ConfidenceInterval => LimitsLabel,
            UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval => BothLabel,
            UncertaintyDisplayStyle.None => "None",
            _ => "Automatic",
        };

        public const string SummaryLabel = "Summary uncertainty";
        public const string MeanLabel = "Uncertainty in the mean (SEM)";
        public const string ReplicateSpreadLabel = "Replicate spread (SD)";
        public const string SummaryTooltip =
            "For averages, choose standard error of the mean (SEM) or replicate spread (SD).";

        public static string SummaryModeLabel(SummaryUncertaintyMode mode) =>
            mode == SummaryUncertaintyMode.ReplicateSpread ? ReplicateSpreadLabel : MeanLabel;

        public static string SummaryStatisticLabel(SummaryUncertaintyMode mode, bool temperatureTrend) =>
            mode == SummaryUncertaintyMode.ReplicateSpread
                ? (temperatureTrend ? "SD around fitted trend" : "Replicate SD")
                : (temperatureTrend ? "SE of fitted value" : "SEM");

        public static string SummaryModeTooltip(SummaryUncertaintyMode mode) =>
            (mode == SummaryUncertaintyMode.ReplicateSpread
                ? "± gives the standard deviation (SD) across experiments or around the fitted trend. The limits give an approximate 95% replicate range."
                : "For averages, ± is the standard error of the mean (SEM). For temperature trends, it is the standard error of the fitted value at the selected temperature. The limits give an approximate 95% interval for that estimate.")
            + " Both include the fitting uncertainty of individual experiments. Shared parameters and slopes use their own fit uncertainty.";

        public const string StyleTooltip =
            "Automatic shows limits when one side is about 50% longer, and ± otherwise.";
    }
}
