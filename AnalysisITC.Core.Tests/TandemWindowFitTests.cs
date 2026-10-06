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

        [Fact]
        public void MonotonicCubicIsReproducedExactly()
        {
            // y = x^3 + x has slope 3x^2 + 1 > 0, so the constraint is inactive.
            var points = Sample(x => x * x * x + x, 0.5, 2.5, 11);

            var fit = TandemContinuityScanner.FitWindow(points);

            Assert.True(fit.Rss < 1e-12);
            foreach (var x in new[] { 0.5, 1.3, 2.5 })
                Assert.Equal(x * x * x + x, fit.Evaluate(x), 9);
        }

        [Fact]
        public void HumpIsFittedWithoutChangingDirection()
        {
            // A rise-then-fall hump is not monotonic; a free cubic would follow it exactly.
            var points = Sample(x => -(x - 1.2) * (x - 1.2), 0.5, 2.0, 11);

            var fit = TandemContinuityScanner.FitWindow(points);
            var slopes = Slopes(fit, 0.5, 2.0).ToList();

            // The slope sign is enforced at discrete points, so allow a sliver between them.
            var tolerance = 1e-3 * slopes.Max(Math.Abs);
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
