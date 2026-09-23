using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Presentation
{
    public readonly struct FitEnvelopePoint
    {
        public FitEnvelopePoint(double x, double center, double lower, double upper)
        {
            X = x;
            Center = center;
            Lower = lower;
            Upper = upper;
        }

        public double X { get; }
        public double Center { get; }
        public double Lower { get; }
        public double Upper { get; }
        public bool HasBand => IsFinite(Lower)
            && IsFinite(Upper)
            && Upper > Lower
            && Math.Abs(Upper - Lower) > 1E-12;

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public static class FitEnvelopeBuilder
    {
        public const int DefaultSampleIntervals = 300;

        public static IReadOnlyList<double> SampleDomain(
            double minimum,
            double maximum,
            int intervals = DefaultSampleIntervals)
        {
            if (intervals < 1) throw new ArgumentOutOfRangeException(nameof(intervals));
            if (!IsFinite(minimum) || !IsFinite(maximum)) return Array.Empty<double>();

            var values = new List<double>(intervals + 1);
            for (var index = 0; index <= intervals; index++)
                values.Add(minimum + (maximum - minimum) * index / intervals);
            return values;
        }

        public static IReadOnlyList<FitEnvelopePoint> Build(
            IEnumerable<double> xs,
            Func<double, (double Center, double Lower, double Upper)> evaluate)
        {
            if (xs == null || evaluate == null) return Array.Empty<FitEnvelopePoint>();

            var points = new List<FitEnvelopePoint>();
            foreach (var x in xs.Where(IsFinite))
            {
                var value = evaluate(x);
                if (!IsFinite(value.Center)) continue;

                var lower = value.Lower;
                var upper = value.Upper;
                if (!IsFinite(lower) || !IsFinite(upper) || upper <= lower
                    || Math.Abs(upper - lower) <= 1E-12)
                {
                    lower = double.NaN;
                    upper = double.NaN;
                }

                points.Add(new FitEnvelopePoint(x, value.Center, lower, upper));
            }

            return points;
        }

        public static IReadOnlyList<FitEnvelopePoint> Build(
            LinearFitWithError fit,
            IEnumerable<LinearFitWithError> bootstrapFits,
            IEnumerable<double> xs)
        {
            if (fit == null || xs == null || !IsFiniteFit(fit))
                return Array.Empty<FitEnvelopePoint>();

            var usableBootstrapFits = (bootstrapFits ?? Enumerable.Empty<LinearFitWithError>())
                .Where(IsFiniteFit)
                .ToList();
            var useBootstrapEnvelope = usableBootstrapFits.Count > 1;
            var bounds = new List<double>(usableBootstrapFits.Count);
            return Build(xs, x =>
            {
                var center = Evaluate(fit, x);

                bounds.Clear();
                if (useBootstrapEnvelope)
                {
                    foreach (var candidate in usableBootstrapFits)
                    {
                        var value = Evaluate(candidate, x);
                        if (IsFinite(value)) bounds.Add(value);
                    }
                }
                var useBootstrapAtPoint = bounds.Count > 1;
                if (!useBootstrapAtPoint)
                {
                    bounds.Clear();
                    foreach (var value in CornerValues(fit, x))
                        if (IsFinite(value)) bounds.Add(value);
                }

                var lower = double.NaN;
                var upper = double.NaN;
                if (bounds.Count > 1)
                {
                    bounds.Sort();
                    lower = useBootstrapAtPoint
                        ? PercentileSorted(bounds, 0.025)
                        : bounds[0];
                    upper = useBootstrapAtPoint
                        ? PercentileSorted(bounds, 0.975)
                        : bounds[bounds.Count - 1];
                }
                return (center, lower, upper);
            });
        }

        static double Evaluate(LinearFitWithError fit, double x) =>
            fit.FixedZeroX.HasValue
                ? (x - fit.FixedZeroX.Value) * fit.Slope.Value
                : (x - fit.ReferenceT) * fit.Slope.Value + fit.Intercept.Value;

        static IEnumerable<double> CornerValues(LinearFitWithError fit, double x)
        {
            if (fit.FixedZeroX.HasValue)
            {
                var evaluated = fit.Evaluate(x);
                yield return evaluated.Lower;
                yield return evaluated.Upper;
                yield return evaluated.Value;
                yield break;
            }
            var slopes = new[] { fit.Slope.Lower, fit.Slope.Upper, fit.Slope.Value };
            var intercepts = new[] { fit.Intercept.Lower, fit.Intercept.Upper, fit.Intercept.Value };

            foreach (var slope in slopes)
            foreach (var intercept in intercepts)
                yield return (x - fit.ReferenceT) * slope + intercept;
        }

        static double PercentileSorted(IReadOnlyList<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0) return double.NaN;
            if (sortedValues.Count == 1) return sortedValues[0];

            var limitedPercentile = Math.Max(0, Math.Min(1, percentile));
            var position = limitedPercentile * (sortedValues.Count - 1);
            var lowerIndex = (int)Math.Floor(position);
            var upperIndex = (int)Math.Ceiling(position);
            if (lowerIndex == upperIndex) return sortedValues[lowerIndex];

            var weight = position - lowerIndex;
            return sortedValues[lowerIndex] * (1 - weight) + sortedValues[upperIndex] * weight;
        }

        static bool IsFiniteFit(LinearFitWithError fit) =>
            fit != null &&
            IsFinite(fit.ReferenceT) &&
            IsFinite(fit.Slope.Value) &&
            IsFinite(fit.Intercept.Value);

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
