using AnalysisITC.Core.Application;

namespace AnalysisITC.Core.Presentation
{
    internal static class AggregateUncertaintyPresentation
    {
        internal static string StatisticLabel(AggregateParameterSummary summary) => summary?.Kind switch
        {
            AggregateUncertaintyKind.MeanStandardError => UncertaintyDisplayPresentation.SummaryStatisticLabel(SummaryUncertaintyMode.Mean, false),
            AggregateUncertaintyKind.TemperatureTrendStandardError => UncertaintyDisplayPresentation.SummaryStatisticLabel(SummaryUncertaintyMode.Mean, true),
            AggregateUncertaintyKind.ReplicateStandardDeviation => UncertaintyDisplayPresentation.SummaryStatisticLabel(SummaryUncertaintyMode.ReplicateSpread, false),
            AggregateUncertaintyKind.TemperatureTrendStandardDeviation => UncertaintyDisplayPresentation.SummaryStatisticLabel(SummaryUncertaintyMode.ReplicateSpread, true),
            AggregateUncertaintyKind.RegressionSlopeStandardError => "Slope standard error",
            _ => "SD",
        };

        internal static string IntervalLabel(AggregateParameterSummary summary) => summary?.Kind switch
        {
            AggregateUncertaintyKind.MeanStandardError => "Approximate 95% interval for mean",
            AggregateUncertaintyKind.TemperatureTrendStandardError => "Approximate 95% interval for fitted trend",
            AggregateUncertaintyKind.ReplicateStandardDeviation => "Approximate 95% replicate range",
            AggregateUncertaintyKind.TemperatureTrendStandardDeviation => "Approximate 95% replicate range near trend",
            AggregateUncertaintyKind.RegressionSlopeStandardError => "Approximate 95% interval for slope",
            _ => "95% confidence interval",
        };

        internal static string DisplayLabel(AggregateParameterSummary summary, UncertaintyDisplayStyle style)
        {
            if (summary == null || summary.Kind == AggregateUncertaintyKind.ModelEstimated) return "";
            if (style == UncertaintyDisplayStyle.Automatic)
                style = summary.Value.IsAsymmetric
                    ? UncertaintyDisplayStyle.ConfidenceInterval : UncertaintyDisplayStyle.StandardDeviation;

            var showsSd = (style == UncertaintyDisplayStyle.StandardDeviation
                || style == UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval) && summary.Value.HasError;
            var showsInterval = (style == UncertaintyDisplayStyle.ConfidenceInterval
                || style == UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval) && summary.Value.HasConfidenceInterval;
            if (showsSd && showsInterval) return StatisticLabel(summary) + " and " + IntervalLabel(summary).ToLowerInvariant();
            if (showsSd) return StatisticLabel(summary);
            return showsInterval ? IntervalLabel(summary) : "";
        }
    }
}
