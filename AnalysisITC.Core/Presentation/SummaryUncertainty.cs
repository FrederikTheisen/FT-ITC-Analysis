using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Presentation
{
    // A numerical contribution to an evaluated linear summary. Its signed weight
    // is Weight + WeightSlope * (temperature - reference temperature).
    internal sealed class SummaryErrorContribution
    {
        internal double Weight { get; }
        internal double WeightSlope { get; }
        internal double Sd { get; }
        internal double LowerWidth { get; }
        internal double UpperWidth { get; }

        internal SummaryErrorContribution(double weight, double weightSlope, double sd, double lower, double upper)
        {
            Weight = weight;
            WeightSlope = weightSlope;
            Sd = sd;
            LowerWidth = lower;
            UpperWidth = upper;
        }
    }

    // Transient evaluation data shared by desktop summaries and the browser.
    // No fitted or persisted temperature dependence is modified.
    internal sealed class SummaryDependence
    {
        internal double ReferenceTemperature { get; set; }
        internal double Intercept { get; set; }
        internal double Slope { get; set; }
        internal double LowerOffset { get; set; }
        internal double UpperOffset { get; set; }
        internal List<SummaryErrorContribution> Contributions { get; } = new List<SummaryErrorContribution>();
        internal AggregateUncertaintyKind Kind { get; set; }
        internal int Count { get; set; }
        internal FloatWithError SlopeUncertainty { get; set; }

        internal FloatWithError Evaluate(double temperature)
        {
            var delta = temperature - ReferenceTemperature;
            var value = Intercept + delta * Slope;
            double variance = 0, lowerVariance = 0, upperVariance = 0;
            foreach (var term in Contributions)
            {
                var weight = term.Weight + delta * term.WeightSlope;
                variance += Square(weight * term.Sd);
                lowerVariance += Square(weight * (weight < 0 ? term.UpperWidth : term.LowerWidth));
                upperVariance += Square(weight * (weight < 0 ? term.LowerWidth : term.UpperWidth));
            }
            return new FloatWithError(value, Math.Sqrt(variance),
                value + LowerOffset - Math.Sqrt(lowerVariance),
                value + UpperOffset + Math.Sqrt(upperVariance));
        }

        internal void MakeUncertaintyUnavailable()
        {
            Contributions.Clear();
            LowerOffset = UpperOffset = 0;
            Contributions.Add(new SummaryErrorContribution(1, 0, double.NaN, double.NaN, double.NaN));
            SlopeUncertainty = SummaryUncertainty.Unavailable(Slope);
        }

        static double Square(double value) => value * value;
    }

    internal static class SummaryUncertainty
    {
        internal const double Normal95 = 1.96;
        internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static bool HasValue(FloatWithError value) => !FloatWithError.IsNaN(value) && IsFinite(value.Value);
        internal static double UsableSd(FloatWithError value) => IsFinite(value.SD) && value.SD >= 0 ? value.SD : 0;
        internal static FloatWithError Unavailable(double value) => new FloatWithError(value, double.NaN, double.NaN, double.NaN);

        internal static bool HasOrderedInterval(FloatWithError value) =>
            value.DistributionConfidence95?.Length == 2 && IsFinite(value.Lower) && IsFinite(value.Upper)
            && value.Lower <= value.Upper;

        internal static SummaryErrorContribution Input(FloatWithError value, double weight, double weightSlope = 0, bool includeSd = false)
        {
            var valid = HasOrderedInterval(value) && value.Lower <= value.Value && value.Value <= value.Upper;
            return new SummaryErrorContribution(weight, weightSlope, includeSd ? UsableSd(value) : 0,
                valid ? value.LowerWidth : double.NaN, valid ? value.UpperWidth : double.NaN);
        }

        internal static SummaryErrorContribution Symmetric(double weight, double weightSlope, double sd, double width) =>
            new SummaryErrorContribution(weight, weightSlope, sd, width, width);

        internal static SummaryDependence Mean(IReadOnlyList<FloatWithError> values, double central)
        {
            var summary = new SummaryDependence
            {
                Intercept = central, Count = values.Count,
                Kind = AggregateUncertaintyKind.ReplicateStandardDeviation,
                SlopeUncertainty = new FloatWithError(0),
            };
            if (values.Count == 0)
            {
                summary.MakeUncertaintyUnavailable();
                return summary;
            }
            if (values.Count == 1)
            {
                var value = values[0];
                // Direct offsets also preserve ordered intervals that do not bracket
                // the original best fit; such intervals cannot be width-propagated.
                summary.LowerOffset = HasOrderedInterval(value) ? value.Lower - value.Value : double.NaN;
                summary.UpperOffset = HasOrderedInterval(value) ? value.Upper - value.Value : double.NaN;
                summary.Contributions.Add(Symmetric(1, 0, UsableSd(value), 0));
                return summary;
            }
            var mean = values.Average(value => value.Value);
            var betweenVariance = values.Sum(value => Square(value.Value - mean)) / (values.Count - 1);
            var variance = betweenVariance + values.Average(value => Square(UsableSd(value)));
            summary.Contributions.Add(Symmetric(1, 0, Math.Sqrt(variance), Normal95 * Math.Sqrt(betweenVariance / values.Count)));
            foreach (var value in values) summary.Contributions.Add(Input(value, 1.0 / values.Count));
            return summary;
        }

        internal static SummaryDependence Model(LinearFitWithError fit)
        {
            var summary = new SummaryDependence
            {
                ReferenceTemperature = fit.ReferenceT, Intercept = fit.Intercept.Value, Slope = fit.Slope.Value,
                Kind = AggregateUncertaintyKind.ModelEstimated, SlopeUncertainty = fit.Slope,
            };
            summary.Contributions.Add(new SummaryErrorContribution(1, 0, fit.Intercept.SD, fit.Intercept.LowerWidth, fit.Intercept.UpperWidth));
            summary.Contributions.Add(new SummaryErrorContribution(0, 1, fit.Slope.SD, fit.Slope.LowerWidth, fit.Slope.UpperWidth));
            return summary;
        }

        internal static SummaryDependence Trend(LinearFitWithError central, IReadOnlyList<FloatWithError> values, IReadOnlyList<double> temperatures)
        {
            if (values.Count == 1)
            {
                var single = Mean(values, central.Intercept.Value);
                single.ReferenceTemperature = central.ReferenceT;
                single.Slope = central.Slope.Value;
                single.SlopeUncertainty = Unavailable(central.Slope.Value);
                return single;
            }
            var summary = Model(central);
            summary.Kind = AggregateUncertaintyKind.TemperatureTrendStandardDeviation;
            summary.Count = values.Count;
            summary.Contributions.Clear();
            if (values.Count < 2)
            {
                summary.MakeUncertaintyUnavailable();
                return summary;
            }
            var reference = temperatures.Average();
            var z = temperatures.Select(t => t - reference).ToArray();
            var sxx = z.Sum(Square);
            if (!IsFinite(sxx) || sxx <= 0)
            {
                summary.MakeUncertaintyUnavailable();
                return summary;
            }
            summary.ReferenceTemperature = reference;
            summary.Intercept = central.Intercept.Value + (reference - central.ReferenceT) * central.Slope.Value;
            var mean = values.Average(value => value.Value);
            var slope = z.Select((x, i) => x * (values[i].Value - mean)).Sum() / sxx;
            var residualVariance = values.Count > 2
                ? z.Select((x, i) => Square(values[i].Value - mean - slope * x)).Sum() / (values.Count - 2)
                : 0;
            var combinedSd = Math.Sqrt(residualVariance + values.Average(value => Square(UsableSd(value))));
            summary.Contributions.Add(Symmetric(1, 0, combinedSd, Normal95 * Math.Sqrt(residualVariance / values.Count)));
            summary.Contributions.Add(Symmetric(0, 1, 0, Normal95 * Math.Sqrt(residualVariance / sxx)));

            var slopeSummary = new SummaryDependence { Intercept = central.Slope.Value };
            slopeSummary.Contributions.Add(Symmetric(1, 0, Math.Sqrt(residualVariance / sxx), Normal95 * Math.Sqrt(residualVariance / sxx)));
            for (var i = 0; i < values.Count; i++)
            {
                var slopeWeight = z[i] / sxx;
                summary.Contributions.Add(Input(values[i], 1.0 / values.Count, slopeWeight));
                slopeSummary.Contributions.Add(Input(values[i], slopeWeight, includeSd: true));
            }
            summary.SlopeUncertainty = slopeSummary.Evaluate(0);
            return summary;
        }

        internal static bool HasDuplicateExperiments(IEnumerable<ExperimentData> experiments)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var unidentified = new List<ExperimentData>();
            foreach (var experiment in experiments)
            {
                if (experiment == null) continue;
                if (!string.IsNullOrEmpty(experiment.UniqueID))
                {
                    if (!ids.Add(experiment.UniqueID)) return true;
                }
                else
                {
                    if (unidentified.Any(previous => ReferenceEquals(previous, experiment))) return true;
                    unidentified.Add(experiment);
                }
            }
            return false;
        }

        static double Square(double value) => value * value;
    }
}
