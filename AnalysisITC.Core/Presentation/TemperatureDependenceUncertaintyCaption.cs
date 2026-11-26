using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Presentation
{
    public enum TemperatureDependenceIntervalKind
    {
        Model,
        FittedTrend,
        ReplicateRange,
    }

    public static class TemperatureDependenceUncertaintyCaption
    {
        public static bool HasBand(IReadOnlyList<FitEnvelopePoint> points) => UsableBandRuns(points).Any();

        public static IEnumerable<(int Start, int Length)> UsableBandRuns(IReadOnlyList<FitEnvelopePoint> points)
        {
            if (points == null) yield break;

            var index = 0;
            while (index < points.Count)
            {
                if (!IsUsableBandPoint(points[index]))
                {
                    index++;
                    continue;
                }

                var start = index++;
                var hasWidthInTemperature = false;
                while (index < points.Count && IsUsableBandPoint(points[index]))
                {
                    hasWidthInTemperature |= points[index].X != points[start].X;
                    index++;
                }
                if (hasWidthInTemperature)
                    yield return (start, index - start);
            }
        }

        public static bool IsUsableBandPoint(FitEnvelopePoint point) => SummaryUncertainty.IsFinite(point.X)
            && SummaryUncertainty.IsFinite(point.Center) && IsUsableInterval(point.Lower, point.Upper);

        public static bool TryGetIndividualInterval(
            AnalysisResult result, SolutionInterface solution, ParameterType parameter,
            FloatWithError value, out double lower, out double upper)
        {
            lower = value.Lower;
            upper = value.Upper;
            return SummaryUncertainty.HasValue(value) && IsUsableInterval(lower, upper)
                && ParameterFitPresentation.GetStatus(result, solution, parameter) != "Fixed";
        }

        static bool IsUsableInterval(double lower, double upper) =>
            SummaryUncertainty.IsFinite(lower) && SummaryUncertainty.IsFinite(upper) && upper > lower;

        /// <summary>Describes individual intervals and curve bands present in the graph.</summary>
        public static string Build(bool hasIndividualIntervals,
            IEnumerable<(string Label, TemperatureDependenceIntervalKind Kind)> bands)
        {
            var parts = new List<string>();
            if (hasIndividualIntervals) parts.Add("Bars show the 95% limits for each fit");
            var groups = bands.GroupBy(band => band.Kind).ToList();
            foreach (var group in groups)
            {
                var subject = groups.Count == 1 ? "Bands" : string.Join(", ", group.Select(band => band.Label).Distinct()) + " bands";
                var description = group.Key switch
                {
                    TemperatureDependenceIntervalKind.FittedTrend => "approximate 95% intervals for the fitted trend",
                    TemperatureDependenceIntervalKind.ReplicateRange => "approximate 95% replicate ranges",
                    _ => "95% model-fit intervals",
                };
                parts.Add(subject + " show " + description);
            }
            return parts.Count == 0 ? "" : string.Join(". ", parts) + ".";
        }
    }
}
