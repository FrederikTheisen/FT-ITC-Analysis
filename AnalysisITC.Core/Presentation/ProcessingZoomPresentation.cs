using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Presentation
{
    public static class ProcessingZoomPresentation
    {
        public static IReadOnlyList<string> BaselineZoomLabels { get; } = Array.AsReadOnly(new[] { "0.5×", "1×", "2×", "4×" });
        public static IReadOnlyList<double> BaselineZoomLevels { get; } = Array.AsReadOnly(new[] { 0.5, 1, 2, 4 });
        public const int DefaultBaselineZoomIndex = 1;
        public const string BaselineZoomTooltip = "Higher values reduce the margin around the baseline";

        // Work in internal power units; each renderer converts the resulting axis limits.
        public static bool TryGetBaselineRange(ExperimentData data, double xMin, double xMax,
            bool corrected, double zoomLevel, out double yMin, out double yMax)
        {
            yMin = yMax = 0;
            var interpolator = data?.Processor?.Interpolator;
            if (interpolator?.Baseline == null || data.DataPoints.Count == 0
                || interpolator.Baseline.Count != data.DataPoints.Count
                || double.IsNaN(zoomLevel) || double.IsInfinity(zoomLevel) || zoomLevel <= 0)
                return false;

            var visibleIndices = Enumerable.Range(0, data.DataPoints.Count)
                .Where(i => data.DataPoints[i].Time >= xMin && data.DataPoints[i].Time <= xMax).ToList();
            var points = interpolator.GetInterpolatedDataPoints(xMin, xMax);
            if (visibleIndices.Count == 0 || points.Count == 0) return false;

            var baselineMin = corrected ? 0 : visibleIndices.Min(i => interpolator.Baseline[i].Value);
            var baselineMax = corrected ? 0 : visibleIndices.Max(i => interpolator.Baseline[i].Value);
            var mean = points.Average(point => point.Power);
            var delta = Math.Min(mean - points.Min(point => point.Power), points.Max(point => point.Power) - mean);
            var margin = delta * 3 / zoomLevel;
            yMin = baselineMin - margin;
            yMax = baselineMax + margin;
            // Keep the previous usable view when there is no finite vertical range.
            return !double.IsNaN(yMin) && !double.IsInfinity(yMin)
                && !double.IsNaN(yMax) && !double.IsInfinity(yMax) && yMax > yMin;
        }
    }
}
