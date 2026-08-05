using System;
using System.Collections.Generic;

using AnalysisITC.Core.Analysis;

namespace AnalysisITC.Core.Presentation
{
    public static class AnalysisContextSummaryPresentation
    {
        public static IReadOnlyList<string> BuildLines(AnalysisContext context)
        {
            if (context == null)
                return Array.Empty<string>();

            var variableCount = context.FittingVariableCount;
            var pointCount = context.FittingPointCount;
            var summary = $"{variableCount:N0} {Pluralize(variableCount, "variable", "variables")}"
                + $" • {pointCount:N0} {Pluralize(pointCount, "data point", "data points")}";

            if (!context.IsMultiExperiment)
                return new[] { summary };

            var experimentCount = context.FittingExperimentCount;
            summary += $" • {experimentCount:N0} {Pluralize(experimentCount, "experiment", "experiments")}";

            return new[]
            {
                summary,
                context.WillFitGlobally
                    ? "Will experiments fit globally"
                    : "Will fit experiments individually"
            };
        }

        public static string BuildText(AnalysisContext context)
        {
            var lines = BuildLines(context);
            return lines.Count == 0
                ? "No analysis ready"
                : string.Join(Environment.NewLine, lines);
        }

        static string Pluralize(int count, string singular, string plural)
        {
            return count == 1 ? singular : plural;
        }
    }
}
