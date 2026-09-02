using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>
    /// Applies the platform-independent width rules for the analysis-result
    /// overview. Measurements are supplied in platform logical pixels/points.
    /// </summary>
    public static class AnalysisResultOverviewColumnWidthPolicy
    {
        public const double MinimumWidth = 90;
        public const double MaximumAutomaticWidth = 300;
        public const string FlexibleColumnId = "Experiment";

        public static IReadOnlyDictionary<string, double> Calculate(
            IReadOnlyList<AnalysisResultOverviewColumn> columns,
            IReadOnlyDictionary<string, double> measuredWidths,
            double availableWidth,
            IReadOnlyDictionary<string, double> manualWidths = null)
        {
            if (columns == null) throw new ArgumentNullException(nameof(columns));

            var widths = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var column in columns)
            {
                if (column == null || string.IsNullOrEmpty(column.Id)) continue;

                if (manualWidths != null
                    && manualWidths.TryGetValue(column.Id, out var manualWidth)
                    && IsFinitePositive(manualWidth))
                {
                    widths[column.Id] = Math.Max(MinimumWidth, manualWidth);
                    continue;
                }

                var measuredWidth = column.PreferredWidth;
                if (measuredWidths != null
                    && measuredWidths.TryGetValue(column.Id, out var suppliedWidth)
                    && IsFinitePositive(suppliedWidth))
                    measuredWidth = suppliedWidth;

                widths[column.Id] = Math.Max(
                    MinimumWidth,
                    Math.Min(MaximumAutomaticWidth, measuredWidth));
            }

            if (IsFinitePositive(availableWidth)
                && widths.TryGetValue(FlexibleColumnId, out var flexibleWidth))
            {
                var surplus = availableWidth - widths.Values.Sum();
                if (surplus > 0)
                    widths[FlexibleColumnId] = flexibleWidth + surplus;
            }

            return widths;
        }

        static bool IsFinitePositive(double value) =>
            value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
