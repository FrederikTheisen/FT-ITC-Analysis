using System;
using System.Collections.Generic;

namespace AnalysisITC.Core.Presentation
{
    public readonly struct AnalysisReportSummaryLegendPosition
    {
        public AnalysisReportSummaryLegendPosition(double x, int row)
        {
            X = x;
            Row = row;
        }

        public double X { get; }
        public int Row { get; }
    }

    public static class AnalysisReportThermodynamicSummaryLayout
    {
        public const double LegendRowHeight = 12;

        public static IReadOnlyList<AnalysisReportSummaryLegendPosition> LegendPositions(
            IReadOnlyList<AnalysisReportThermodynamicSeries> series,
            double availableWidth,
            Func<string, double> measureLabel)
        {
            var positions = new List<AnalysisReportSummaryLegendPosition>();
            var x = 0.0;
            var row = 0;
            foreach (var item in series)
            {
                // The swatch, label offset, and trailing gap occupy 24 points.
                var width = 24 + Math.Max(0, measureLabel(item.Label));
                if (x > 0 && x + width > availableWidth)
                {
                    x = 0;
                    row++;
                }
                positions.Add(new AnalysisReportSummaryLegendPosition(x, row));
                x += width;
            }
            return positions;
        }

        public static double ChartWidth(double availableWidth, int categoryCount, int seriesCount) =>
            seriesCount > 10
                ? availableWidth
                : Math.Min(availableWidth, Math.Max(280,
                    92 + categoryCount * Math.Max(42, 13 * seriesCount)));
    }
}
