using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class TandemWindowFitTests
    {
        static List<(double x, double y)> Sample(Func<double, double> curve, double from, double to, int count) =>
            Enumerable.Range(0, count).Select(i => from + (to - from) * i / (count - 1)).Select(x => (x, curve(x))).ToList();

        static IEnumerable<double> Slopes(TandemContinuityScanner.WindowFit fit, double from, double to)
        {
            const double h = 1e-6;
            return Enumerable.Range(0, 201).Select(i => from + (to - from) * i / 200.0)
                .Select(x => (fit.Evaluate(x + h) - fit.Evaluate(x - h)) / (2 * h));
        }

        static IEnumerable<double> Curvatures(TandemContinuityScanner.WindowFit fit, double from, double to)
        {
            const double h = 1e-3;
            return Enumerable.Range(0, 201).Select(i => from + (to - from) * i / 200.0)
                .Select(x => (fit.Evaluate(x + h) - 2 * fit.Evaluate(x) + fit.Evaluate(x - h)) / (h * h));
        }

        [Theory]
        [InlineData(1, 0.5, 2.5)]
        [InlineData(-1, 0.5, 2.5)]
        [InlineData(1, -2.5, -0.5)]
        [InlineData(-1, -2.5, -0.5)]
        [InlineData(1, 2, 8)]
        public void SingleCurvatureCubicIsReproducedExactly(double sign, double from, double to)
        {
            // y = +/- (x^3 + x): slope is +/- (3x^2 + 1), curvature is +/- 6x.
            // These intervals cover all four slope/curvature combinations without an inflection.
            double Curve(double x) => sign * (x * x * x + x);
            var points = Sample(Curve, from, to, 11);

            var fit = TandemContinuityScanner.FitWindow(points);

            Assert.NotNull(fit);
            Assert.True(fit.Rss < 1e-12);
            foreach (var x in new[] { from, (from + to) / 2, to })
                Assert.Equal(Curve(x), fit.Evaluate(x), 9);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(-1)]
        public void MonotonicSCurveUsesTheBestFitWithoutAnInflection(double sign)
        {
            var points = Sample(x => sign * (x * x * x + x), -1, 1, 11);

            var fit = TandemContinuityScanner.FitWindow(points);

            Assert.NotNull(fit);
            Assert.All(Slopes(fit, -1, 1), slope => Assert.True(sign * slope >= -1e-7));
            var curvatures = Curvatures(fit, -1, 1).ToList();
            Assert.True(curvatures.All(c => c >= -1e-7) || curvatures.All(c => c <= 1e-7));

            // Independent analytic optimum for the symmetric sample of x + x^3.
            // Remove the constant/linear projections to obtain orthogonal even/odd residuals.
            // For cubic coefficient c, fixed-sign curvature requires |quadratic coefficient| >= 3|c|.
            // The residual is U(1-c)^2 + 9Vc^2, minimised at c=U/(U+9V).
            var meanSquare = points.Average(p => p.x * p.x);
            var projection = points.Sum(p => Math.Pow(p.x, 4)) / points.Sum(p => p.x * p.x);
            var u = points.Sum(p => Math.Pow(p.x * p.x * p.x - projection * p.x, 2));
            var v = points.Sum(p => Math.Pow(p.x * p.x - meanSquare, 2));
            var expectedRss = 9 * u * v / (u + 9 * v);
            Assert.Equal(expectedRss, fit.Rss, 10);
            Assert.True(fit.Rss > 0.1);
        }

        [Theory]
        [InlineData(0, 4)]
        [InlineData(3, -2)]
        [InlineData(-3, 4)]
        public void StraightAndConstantCurvesRemainAllowed(double slope, double intercept)
        {
            var points = Sample(x => slope * x + intercept, 0.5, 3, 11);

            var fit = TandemContinuityScanner.FitWindow(points);

            Assert.NotNull(fit);
            Assert.True(fit.Rss < 1e-12);
            foreach (var point in points) Assert.Equal(point.y, fit.Evaluate(point.x), 9);
        }

        [Fact]
        public void HumpIsFittedWithoutChangingDirection()
        {
            // A rise-then-fall hump is not monotonic; a free cubic would follow it exactly.
            var points = Sample(x => -(x - 1.2) * (x - 1.2), 0.5, 2.0, 11);

            var fit = TandemContinuityScanner.FitWindow(points);
            var slopes = Slopes(fit, 0.5, 2.0).ToList();

            var tolerance = 1e-7 * Math.Max(1, slopes.Max(Math.Abs));
            Assert.True(slopes.All(s => s >= -tolerance) || slopes.All(s => s <= tolerance));
            Assert.True(fit.Rss > 1e-3);

            // A constant is monotonic, so the constrained optimum can be no worse than the mean.
            var mean = points.Average(p => p.y);
            Assert.True(fit.Rss <= points.Sum(p => (p.y - mean) * (p.y - mean)) + 1e-12);
        }

        [Fact]
        public void FallingDataGetsAFallingFit()
        {
            var points = Sample(x => 10 * Math.Exp(-x), 0.5, 3.0, 11);
            points[5] = (points[5].x, points[5].y + 0.4);

            var fit = TandemContinuityScanner.FitWindow(points);
            var slopes = Slopes(fit, 0.5, 3.0).ToList();

            Assert.All(slopes, slope => Assert.True(slope <= 1e-3 * slopes.Max(Math.Abs)));
        }

        [Fact]
        public void TooFewPointsOrNoSpreadGiveNoFit()
        {
            Assert.Null(TandemContinuityScanner.FitWindow(Sample(x => x, 0, 1, 3)));
            Assert.Null(TandemContinuityScanner.FitWindow(Enumerable.Repeat((1.0, 2.0), 6).ToList()));
        }
    }
}
