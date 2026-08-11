using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Presentation
{
    public readonly struct LinearFitEnvelopePoint
    {
        public LinearFitEnvelopePoint(double x, double center, double lower, double upper)
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
        public bool HasBand => IsFinite(Lower) && IsFinite(Upper) && Math.Abs(Upper - Lower) > 1E-12;

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public static class LinearFitEnvelopeBuilder
    {
        public const int DefaultSampleIntervals = 400;

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

        public static IReadOnlyList<LinearFitEnvelopePoint> Build(
            LinearFitWithError fit,
            IEnumerable<LinearFitWithError> bootstrapFits,
            IEnumerable<double> xValues)
        {
            if (fit == null || xValues == null || !IsFiniteFit(fit))
                return Array.Empty<LinearFitEnvelopePoint>();

            var usableBootstrapFits = (bootstrapFits ?? Enumerable.Empty<LinearFitWithError>())
                .Where(IsFiniteFit)
                .ToList();
            var useBootstrapEnvelope = usableBootstrapFits.Count > 1;
            var points = new List<LinearFitEnvelopePoint>();

            foreach (var x in xValues.Where(IsFinite))
            {
                var center = Evaluate(fit, x);
                if (!IsFinite(center)) continue;

                var bounds = useBootstrapEnvelope
                    ? usableBootstrapFits.Select(candidate => Evaluate(candidate, x)).Where(IsFinite).OrderBy(value => value).ToList()
                    : new List<double>();
                var useBootstrapAtPoint = bounds.Count > 1;

                if (!useBootstrapAtPoint)
                    bounds = CornerValues(fit, x).Where(IsFinite).OrderBy(value => value).ToList();

                var lower = double.NaN;
                var upper = double.NaN;
                if (bounds.Count > 1)
                {
                    lower = useBootstrapAtPoint
                        ? PercentileSorted(bounds, 0.025)
                        : bounds.First();
                    upper = useBootstrapAtPoint
                        ? PercentileSorted(bounds, 0.975)
                        : bounds.Last();

                    if (!IsFinite(lower) || !IsFinite(upper) || Math.Abs(upper - lower) <= 1E-12)
                    {
                        lower = double.NaN;
                        upper = double.NaN;
                    }
                }

                points.Add(new LinearFitEnvelopePoint(x, center, lower, upper));
            }

            return points;
        }

        static double Evaluate(LinearFitWithError fit, double x) =>
            (x - fit.ReferenceT) * fit.Slope.Value + fit.Intercept.Value;

        static IEnumerable<double> CornerValues(LinearFitWithError fit, double x)
        {
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
