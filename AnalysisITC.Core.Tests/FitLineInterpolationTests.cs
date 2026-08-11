using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public class FitLineInterpolationTests
    {
        [Fact]
        public void LinearPathPassesThroughEveryPoint()
        {
            var points = Points((0, 2), (4, 7), (9, -1));

            var path = FitLinePathBuilder.Build(points, LineSmoothness.Linear);

            AssertPoint(points[0], path.Start);
            Assert.Equal(2, path.Segments.Count);
            Assert.All(path.Segments, segment => Assert.Equal(FitLinePathSegmentKind.Line, segment.Kind));
            AssertPoint(points[1], path.Segments[0].End);
            AssertPoint(points[2], path.Segments[1].End);
        }

        [Fact]
        public void PchipSplineDoesNotOvershootAdjacentKnots()
        {
            var points = Points((0, 0), (20, 10), (50, 4), (80, 8));

            var path = FitLinePathBuilder.Build(points, LineSmoothness.Spline, splineSampleStep: 1);
            var samples = new[] { path.Start }.Concat(path.Segments.Select(segment => segment.End)).ToList();

            Assert.All(path.Segments, segment => Assert.Equal(FitLinePathSegmentKind.Line, segment.Kind));
            foreach (var sample in samples)
            {
                var interval = Enumerable.Range(0, points.Count - 1)
                    .FirstOrDefault(index => sample.X >= points[index].X && sample.X <= points[index + 1].X);
                var minimum = System.Math.Min(points[interval].Y, points[interval + 1].Y) - 1E-9;
                var maximum = System.Math.Max(points[interval].Y, points[interval + 1].Y) + 1E-9;
                Assert.InRange(sample.Y, minimum, maximum);
            }
        }

        [Fact]
        public void SmoothPathMatchesMacMidpointQuadratics()
        {
            var points = Points((0, 0), (10, 10), (20, 0));

            var path = FitLinePathBuilder.Build(points, LineSmoothness.Smooth);

            Assert.Equal(3, path.Segments.Count);
            Assert.Equal(FitLinePathSegmentKind.Quadratic, path.Segments[0].Kind);
            AssertPoint(points[0], path.Segments[0].Control);
            AssertPoint(new FitLineInterpolationPoint(5, 5), path.Segments[0].End);
            Assert.Equal(FitLinePathSegmentKind.Quadratic, path.Segments[1].Kind);
            AssertPoint(points[1], path.Segments[1].Control);
            AssertPoint(new FitLineInterpolationPoint(15, 5), path.Segments[1].End);
            Assert.Equal(FitLinePathSegmentKind.Line, path.Segments[2].Kind);
            AssertPoint(points[2], path.Segments[2].End);
        }

        [Fact]
        public void PchipSplineSupportsReverseTraversalForBandBoundary()
        {
            var points = Points((30, 3), (20, 8), (10, 4), (0, 6));

            var path = FitLinePathBuilder.Build(points, LineSmoothness.Spline, splineSampleStep: 1);
            var samples = new[] { path.Start }.Concat(path.Segments.Select(segment => segment.End)).ToList();

            AssertPoint(points[0], samples[0]);
            AssertPoint(points[points.Count - 1], samples[samples.Count - 1]);
            Assert.True(samples.Zip(samples.Skip(1), (left, right) => right.X < left.X).All(value => value));
        }

        [Fact]
        public void SplineWithTwoPointsFallsBackToLinear()
        {
            var points = Points((0, 2), (10, 6));

            var path = FitLinePathBuilder.Build(points, LineSmoothness.Spline);

            Assert.Single(path.Segments);
            Assert.Equal(FitLinePathSegmentKind.Line, path.Segments[0].Kind);
            AssertPoint(points[1], path.Segments[0].End);
        }

        [Fact]
        public void SplineWithDuplicateXFallsBackToLinear()
        {
            var points = Points((0, 2), (10, 6), (10, 4), (20, 8));

            var path = FitLinePathBuilder.Build(points, LineSmoothness.Spline);

            Assert.Equal(points.Count - 1, path.Segments.Count);
            Assert.All(path.Segments, segment => Assert.Equal(FitLinePathSegmentKind.Line, segment.Kind));
            for (var index = 1; index < points.Count; index++)
                AssertPoint(points[index], path.Segments[index - 1].End);
        }

        static IReadOnlyList<FitLineInterpolationPoint> Points(params (double X, double Y)[] values)
            => values.Select(value => new FitLineInterpolationPoint(value.X, value.Y)).ToList();

        static void AssertPoint(FitLineInterpolationPoint expected, FitLineInterpolationPoint actual)
        {
            Assert.Equal(expected.X, actual.X, 10);
            Assert.Equal(expected.Y, actual.Y, 10);
        }
    }
}
