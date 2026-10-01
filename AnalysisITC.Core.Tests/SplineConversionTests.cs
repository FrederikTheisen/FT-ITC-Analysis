using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

using Xunit;

using Algorithm = AnalysisITC.Core.Processing.SplineInterpolator.SplineInterpolatorAlgorithm;
using Segment = AnalysisITC.Core.Processing.SegmentedBaselineInterpolator.BaselineSegment;
using SegmentKind = AnalysisITC.Core.Processing.SegmentedBaselineInterpolator.BaselineSegmentKind;

namespace AnalysisITC.Core.Tests
{
    /// <summary>
    /// Conversion of Polynomial and Segmented baselines to splines. Expected values are
    /// computed here from hand-written polynomials and the documented segmented model:
    /// fitted segments outside integration regions, and B(t) = (1 − w)L(t) + wR(t) with
    /// w = (t − s)/(e − s) across an integration region [s, e].
    /// </summary>
    [Collection("Spline conversion")]
    public class SplineConversionTests : IDisposable
    {
        // Injections at 40, 120, 200 and 280 s with 35 s integration regions.
        static readonly float[] InjectionTimes = { 40f, 120f, 200f, 280f };
        const float IntegrationLength = 35f;
        const double LastTime = 370;

        // Segment spans implied by the injection layout.
        static readonly (double Start, double End)[] SegmentSpans =
        {
            (0, 40), (75, 120), (155, 200), (235, 280), (315, 370),
        };

        static readonly (double Start, double End)[] BlendSpans =
        {
            (40, 75), (120, 155), (200, 235), (280, 315),
        };

        readonly Algorithm originalTarget = SplineInterpolator.PolynomialToSplineConversionTargetAlgorithm;

        public void Dispose() => SplineInterpolator.PolynomialToSplineConversionTargetAlgorithm = originalTarget;

        [Fact]
        public void SegmentDerivativeMatchesHandDifferentiatedPolynomial()
        {
            var constant = new Segment(SegmentKind.InitialDelay, -1, 0, 10, 5, new[] { 3.0 });
            var line = new Segment(SegmentKind.InitialDelay, -1, 0, 10, 5, new[] { 3.0, -2.0 });
            var quadratic = new Segment(SegmentKind.InitialDelay, -1, 0, 10, 5, new[] { 3.0, -2.0, 0.5 });

            Assert.Equal(0.0, constant.Derivative(7));
            Assert.Equal(-2.0, line.Derivative(7));
            // d/dt[3 − 2(t − 5) + 0.5(t − 5)²] = −2 + (t − 5) = 0 at t = 7.
            Assert.Equal(0.0, quadratic.Derivative(7), 12);
            Assert.Equal(-7.0, quadratic.Derivative(0), 12);
        }

        [Fact]
        public async Task LinearConversionOfStraightSegmentsReproducesSegmentsAndAddsBlendMidpoints()
        {
            // The last two segments share a slope, so their blend is a straight line.
            var coefficients = new[]
            {
                new[] { 1.0e-5, 2e-8 },
                new[] { 1.2e-5, -1e-8 },
                new[] { 1.1e-5, 3e-8 },
                new[] { 1.4e-5, 1e-8 },
                new[] { 1.3e-5, 1e-8 },
            };
            var experiment = CreateExperimentWithSegments(coefficients);

            var spline = await ConvertAsync(experiment, Algorithm.Linear);

            Assert.True(experiment.Processor.IsLocked);
            Assert.Equal(Algorithm.Linear, spline.Algorithm);
            Assert.Equal(
                new[] { 0, 40, 57.5, 75, 120, 137.5, 155, 200, 217.5, 235, 280, 315, 370 },
                spline.SplinePoints.Select(point => point.Time).ToArray());

            foreach (var point in spline.SplinePoints)
                AssertClose(Source(coefficients, point.Time), point.Power);

            Assert.All(spline.SplinePoints, point => Assert.False(point.SlopeLocked));

            // A linear spline through the ends of a straight piece reproduces it exactly.
            foreach (var (time, baseline) in BaselineSamples(experiment))
            {
                if (InSegment(time) || (time >= 280 && time <= 315))
                    AssertClose(Source(coefficients, time), baseline);
            }
        }

        [Fact]
        public async Task LinearConversionOfConstantSegmentsReproducesTheWholeBaseline()
        {
            var coefficients = new[]
            {
                new[] { 1.0e-5 }, new[] { 1.2e-5 }, new[] { 0.9e-5 }, new[] { 1.4e-5 }, new[] { 1.3e-5 },
            };
            var experiment = CreateExperimentWithSegments(coefficients);

            var spline = await ConvertAsync(experiment, Algorithm.Linear);

            // A blend of two constants is a straight line, so no midpoints are needed.
            Assert.Equal(
                new double[] { 0, 40, 75, 120, 155, 200, 235, 280, 315, 370 },
                spline.SplinePoints.Select(point => point.Time).ToArray());

            foreach (var (time, baseline) in BaselineSamples(experiment))
                AssertClose(Source(coefficients, time), baseline);
        }

        [Fact]
        public async Task SmoothConversionOfQuadraticSegmentsReproducesSegmentsWithLockedSlopes()
        {
            var coefficients = new[]
            {
                new[] { 1.0e-5, 2e-8, 3e-10 },
                new[] { 1.2e-5, -1e-8, -2e-10 },
                new[] { 1.1e-5, 3e-8, 1e-10 },
                new[] { 1.4e-5, 0, 4e-10 },
                new[] { 1.3e-5, 1e-8, -1e-10 },
            };
            var experiment = CreateExperimentWithSegments(coefficients);

            var spline = await ConvertAsync(experiment, Algorithm.Smooth);

            // Hermite slopes reproduce quadratic segments, so only blends get midpoints.
            Assert.Equal(
                new[] { 0, 40, 57.5, 75, 120, 137.5, 155, 200, 217.5, 235, 280, 297.5, 315, 370 },
                spline.SplinePoints.Select(point => point.Time).ToArray());
            Assert.All(spline.SplinePoints, point => Assert.True(point.SlopeLocked));

            foreach (var point in spline.SplinePoints)
            {
                AssertClose(Source(coefficients, point.Time), point.Power);
                AssertClose(ExpectedConversionSlope(coefficients, point.Time), point.Slope, relativeTolerance: 1e-9);
            }

            foreach (var (time, baseline) in BaselineSamples(experiment))
            {
                if (InSegment(time))
                    AssertClose(Source(coefficients, time), baseline, relativeTolerance: 1e-9);
            }
        }

        [Fact]
        public async Task LinearConversionOfQuadraticSegmentsAddsSegmentMidpoints()
        {
            var coefficients = new[]
            {
                new[] { 1.0e-5, 2e-8, 3e-10 },
                new[] { 1.2e-5, -1e-8 },
                new[] { 1.1e-5, 3e-8, 1e-10 },
                new[] { 1.4e-5, 0 },
                new[] { 1.3e-5, 1e-8, -1e-10 },
            };
            var experiment = CreateExperimentWithSegments(coefficients);

            var spline = await ConvertAsync(experiment, Algorithm.Linear);

            Assert.Equal(
                new[] { 0, 20, 40, 57.5, 75, 120, 137.5, 155, 177.5, 200, 217.5, 235, 280, 297.5, 315, 342.5, 370 },
                spline.SplinePoints.Select(point => point.Time).ToArray());

            foreach (var point in spline.SplinePoints)
                AssertClose(Source(coefficients, point.Time), point.Power);
        }

        [Fact]
        public async Task CloseSegmentAndIntegrationBoundsAreMergedIntoSeparatedPoints()
        {
            var coefficients = new[]
            {
                new[] { 1.0e-5, 2e-8 }, new[] { 1.2e-5, -1e-8 }, new[] { 1.1e-5, 3e-8 }, new[] { 1.4e-5, 1e-8 }, new[] { 1.3e-5, 0 },
            };
            var spans = SegmentSpans.ToArray();
            spans[1] = (75.3, 119.8);
            var experiment = CreateExperimentWithSegments(coefficients, spans);

            var spline = await ConvertAsync(experiment, Algorithm.Linear);

            var times = spline.SplinePoints.Select(point => point.Time).ToArray();
            Assert.All(times.Zip(times.Skip(1), (left, right) => right - left), spacing => Assert.True(spacing > 0.5));
            Assert.Single(times, time => Math.Abs(time - 75) < 0.5);
            Assert.Single(times, time => Math.Abs(time - 120) < 0.5);
            Assert.All(BaselineSamples(experiment), sample => Assert.True(double.IsFinite(sample.Baseline)));
        }

        [Fact]
        public async Task SegmentedBaselineWithoutSegmentsUsesUniformSampling()
        {
            var experiment = CreateExperiment();
            experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Segmented);
            var segmented = Assert.IsType<SegmentedBaselineInterpolator>(experiment.Processor.Interpolator);
            segmented.Baseline = experiment.DataPoints.Select(point => new Energy(1e-5 + 1e-8 * point.Time)).ToList();

            var spline = await ConvertAsync(experiment, Algorithm.Linear, pointDensity: 2);

            Assert.NotEmpty(spline.SplinePoints);
            foreach (var point in spline.SplinePoints)
            {
                Assert.Contains(experiment.DataPoints, dataPoint => dataPoint.Time == point.Time);
                AssertClose(1e-5 + 1e-8 * point.Time, point.Power);
            }
        }

        [Fact]
        public async Task ConversionOfLockedProcessingDoesNothing()
        {
            var experiment = CreateExperimentWithSegments(SegmentSpans.Select(_ => new[] { 1e-5 }).ToArray());
            experiment.Processor.Lock();

            await experiment.Processor.Interpolator.ConvertToSplineAsync(4, showProgress: false);

            Assert.IsType<SegmentedBaselineInterpolator>(experiment.Processor.Interpolator);
            Assert.True(experiment.Processor.IsLocked);
        }

        [Fact]
        public async Task PolynomialConversionKeepsPointsSampledFromThePolynomial()
        {
            var experiment = CreateExperiment(withSignal: true);
            experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
            ((PolynomialLeastSquaresInterpolator)experiment.Processor.Interpolator).Degree = 3;
            await experiment.Processor.ProcessData(showProgress: false);
            var sourceBaseline = experiment.Processor.Interpolator.Baseline.Select(value => value.Value).ToArray();

            var spline = await ConvertAsync(experiment, Algorithm.Linear, pointDensity: 4);

            // Regenerated points would take means of the noisy raw data instead.
            Assert.True(experiment.Processor.IsLocked);
            Assert.True(spline.SplinePoints.Count > InjectionTimes.Length * 4);
            foreach (var point in spline.SplinePoints)
            {
                var index = experiment.DataPoints.FindIndex(dataPoint => dataPoint.Time == point.Time);
                Assert.True(index >= 0, $"Point at {point.Time} is not on a sample time.");
                Assert.Equal(sourceBaseline[index], point.Power);
            }
        }

        [Fact]
        public async Task FittedSegmentedConversionKeepsTheFittedSegments()
        {
            var experiment = CreateExperiment(withSignal: true);
            experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Segmented);
            await experiment.Processor.ProcessData(showProgress: false);
            var sourceBaseline = experiment.Processor.Interpolator.Baseline.Select(value => value.Value).ToArray();

            var spline = await ConvertAsync(experiment, Algorithm.Smooth);

            Assert.True(experiment.Processor.IsLocked);
            foreach (var point in spline.SplinePoints.Where(point => point.Time % 1 == 0))
                AssertClose(sourceBaseline[(int)point.Time], point.Power);

            // Default degree-1 segments are reproduced by the locked Hermite slopes.
            foreach (var (time, baseline) in BaselineSamples(experiment))
            {
                if (InSegment(time))
                    AssertClose(sourceBaseline[(int)time], baseline, relativeTolerance: 1e-9);
            }
        }

        static async Task<SplineInterpolator> ConvertAsync(ExperimentData experiment, Algorithm target, int pointDensity = 2)
        {
            SplineInterpolator.PolynomialToSplineConversionTargetAlgorithm = target;
            await experiment.Processor.Interpolator.ConvertToSplineAsync(pointDensity, showProgress: false);

            return Assert.IsType<SplineInterpolator>(experiment.Processor.Interpolator);
        }

        static ExperimentData CreateExperimentWithSegments(double[][] coefficients, (double Start, double End)[] spans = null)
        {
            spans ??= SegmentSpans;
            var experiment = CreateExperiment();
            experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Segmented);
            var segmented = Assert.IsType<SegmentedBaselineInterpolator>(experiment.Processor.Interpolator);

            segmented.RestoreSegments(coefficients.Select((c, i) => new Segment(
                i == 0 ? SegmentKind.InitialDelay : SegmentKind.InjectionScope,
                i - 1,
                spans[i].Start,
                spans[i].End,
                Center(i),
                c)));

            return experiment;
        }

        static ExperimentData CreateExperiment(bool withSignal = false)
        {
            var experiment = new ExperimentData("spline-conversion.itc")
            {
                InitialDelay = 40,
                CellVolume = 0.0002,
                CellConcentration = new FloatWithError(0.0001),
                SyringeConcentration = new FloatWithError(0.001),
            };

            for (int i = 0; i <= LastTime; i++)
            {
                var time = (float)i;
                var power = 1.0e-5;
                if (withSignal)
                {
                    power += 1.0e-8 * time + 2.0e-6 * Math.Sin(time / 65.0) + 2.0e-7 * Math.Sin(time * 1.73);
                    for (int injection = 0; injection < InjectionTimes.Length; injection++)
                    {
                        var elapsed = time - InjectionTimes[injection];
                        if (elapsed >= 0 && elapsed <= 80)
                            power -= 8.0e-5 * Math.Exp(-elapsed / 8.0);
                    }
                }
                experiment.DataPoints.Add(new DataPoint(time, (float)power, 25));
            }

            for (int i = 0; i < InjectionTimes.Length; i++)
            {
                var injection = InjectionData.FromPEAQFile(
                    experiment,
                    i,
                    include: true,
                    time: InjectionTimes[i],
                    volume: 2.0e-6,
                    delay: 80,
                    duration: 2,
                    temperature: 25);
                experiment.Injections.Add(injection);
                injection.SetIntegrationLengthByTime(IntegrationLength);
            }

            return experiment;
        }

        static double Center(int segment) => 0.5 * (SegmentSpans[segment].Start + SegmentSpans[segment].End);

        static double Polynomial(double[] c, int segment, double time)
        {
            var x = time - Center(segment);
            return c[0] + (c.Length > 1 ? c[1] * x : 0) + (c.Length > 2 ? c[2] * x * x : 0);
        }

        static double PolynomialSlope(double[] c, int segment, double time)
        {
            var x = time - Center(segment);
            return (c.Length > 1 ? c[1] : 0) + (c.Length > 2 ? 2 * c[2] * x : 0);
        }

        static double Source(double[][] coefficients, double time)
        {
            for (int i = 0; i < BlendSpans.Length; i++)
            {
                var (start, end) = BlendSpans[i];
                if (time < start || time > end) continue;

                var w = (time - start) / (end - start);
                return (1 - w) * Polynomial(coefficients[i], i, time) + w * Polynomial(coefficients[i + 1], i + 1, time);
            }

            var segment = Array.FindIndex(SegmentSpans, span => time >= span.Start && time <= span.End);
            return Polynomial(coefficients[segment], segment, time);
        }

        /// <summary>
        /// Points at segment ends take the segment slope; blend midpoints take the blend
        /// slope (1 − w)L′ + wR′ + (R − L)/(e − s).
        /// </summary>
        static double ExpectedConversionSlope(double[][] coefficients, double time)
        {
            var segment = Array.FindIndex(SegmentSpans, span => time >= span.Start && time <= span.End);
            if (segment >= 0) return PolynomialSlope(coefficients[segment], segment, time);

            var blend = Array.FindIndex(BlendSpans, span => time > span.Start && time < span.End);
            var (start, end) = BlendSpans[blend];
            var w = (time - start) / (end - start);
            var left = coefficients[blend];
            var right = coefficients[blend + 1];

            return (1 - w) * PolynomialSlope(left, blend, time)
                + w * PolynomialSlope(right, blend + 1, time)
                + (Polynomial(right, blend + 1, time) - Polynomial(left, blend, time)) / (end - start);
        }

        static bool InSegment(double time) => SegmentSpans.Any(span => time >= span.Start && time <= span.End);

        static IEnumerable<(double Time, double Baseline)> BaselineSamples(ExperimentData experiment)
        {
            var baseline = experiment.Processor.Interpolator.Baseline;
            return experiment.DataPoints.Select((point, i) => ((double)point.Time, baseline[i].Value));
        }

        static void AssertClose(double expected, double actual, double relativeTolerance = 1e-12)
        {
            var tolerance = relativeTolerance * Math.Max(Math.Abs(expected), 1e-12);
            Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, got {actual:R}.");
        }
    }
}
