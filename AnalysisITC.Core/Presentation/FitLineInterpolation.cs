using System;
using System.Collections.Generic;
using System.Linq;

using MathNet.Numerics.Interpolation;

namespace AnalysisITC.Core.Presentation
{
    internal enum FitLinePathSegmentKind
    {
        Line,
        Quadratic
    }

    internal readonly struct FitLineInterpolationPoint
    {
        public FitLineInterpolationPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

    internal readonly struct FitLinePathSegment
    {
        FitLinePathSegment(FitLinePathSegmentKind kind, FitLineInterpolationPoint control, FitLineInterpolationPoint end)
        {
            Kind = kind;
            Control = control;
            End = end;
        }

        public FitLinePathSegmentKind Kind { get; }
        public FitLineInterpolationPoint Control { get; }
        public FitLineInterpolationPoint End { get; }

        public static FitLinePathSegment Line(FitLineInterpolationPoint end)
            => new FitLinePathSegment(FitLinePathSegmentKind.Line, default, end);

        public static FitLinePathSegment Quadratic(FitLineInterpolationPoint control, FitLineInterpolationPoint end)
            => new FitLinePathSegment(FitLinePathSegmentKind.Quadratic, control, end);
    }

    internal sealed class FitLinePath
    {
        public FitLinePath(FitLineInterpolationPoint start, IReadOnlyList<FitLinePathSegment> segments, bool isEmpty = false)
        {
            Start = start;
            Segments = segments ?? Array.Empty<FitLinePathSegment>();
            IsEmpty = isEmpty;
        }

        public FitLineInterpolationPoint Start { get; }
        public IReadOnlyList<FitLinePathSegment> Segments { get; }
        public bool IsEmpty { get; }
    }

    internal static class FitLinePathBuilder
    {
        public static FitLinePath Build(
            IReadOnlyList<FitLineInterpolationPoint> points,
            LineSmoothness smoothness,
            double splineSampleStep = 1.0)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (points.Count == 0)
                return new FitLinePath(default, Array.Empty<FitLinePathSegment>(), isEmpty: true);

            return smoothness switch
            {
                LineSmoothness.Spline => BuildSpline(points, splineSampleStep),
                LineSmoothness.Smooth => BuildSmooth(points),
                _ => BuildLinear(points)
            };
        }

        static FitLinePath BuildLinear(IReadOnlyList<FitLineInterpolationPoint> points)
        {
            var segments = new List<FitLinePathSegment>(Math.Max(0, points.Count - 1));
            for (var index = 1; index < points.Count; index++)
                segments.Add(FitLinePathSegment.Line(points[index]));

            return new FitLinePath(points[0], segments);
        }

        static FitLinePath BuildSmooth(IReadOnlyList<FitLineInterpolationPoint> points)
        {
            if (points.Count < 3) return BuildLinear(points);

            var segments = new List<FitLinePathSegment>(points.Count);
            var current = points[0];
            for (var index = 0; index < points.Count - 1; index++)
            {
                var next = points[index + 1];
                var midpoint = new FitLineInterpolationPoint(
                    (current.X + next.X) / 2.0,
                    (current.Y + next.Y) / 2.0);
                segments.Add(FitLinePathSegment.Quadratic(current, midpoint));
                current = next;
            }

            segments.Add(FitLinePathSegment.Line(points[points.Count - 1]));
            return new FitLinePath(points[0], segments);
        }

        static FitLinePath BuildSpline(IReadOnlyList<FitLineInterpolationPoint> points, double sampleStep)
        {
            if (points.Count < 3 || !IsFinite(sampleStep) || sampleStep <= 0)
                return BuildLinear(points);

            var direction = Math.Sign(points[points.Count - 1].X - points[0].X);
            if (direction == 0 || !HasStrictlyMonotonicFiniteCoordinates(points, direction))
                return BuildLinear(points);

            try
            {
                var ordered = direction > 0 ? points : points.Reverse().ToArray();
                var spline = CubicSpline.InterpolatePchip(
                    ordered.Select(point => point.X),
                    ordered.Select(point => point.Y));
                var segments = new List<FitLinePathSegment>();
                var startX = points[0].X;
                var endX = points[points.Count - 1].X;

                for (var x = startX + direction * sampleStep;
                    direction > 0 ? x < endX : x > endX;
                    x += direction * sampleStep)
                {
                    var y = spline.Interpolate(x);
                    if (!IsFinite(y)) return BuildLinear(points);
                    segments.Add(FitLinePathSegment.Line(new FitLineInterpolationPoint(x, y)));
                }

                segments.Add(FitLinePathSegment.Line(points[points.Count - 1]));
                return new FitLinePath(points[0], segments);
            }
            catch (Exception)
            {
                return BuildLinear(points);
            }
        }

        static bool HasStrictlyMonotonicFiniteCoordinates(IReadOnlyList<FitLineInterpolationPoint> points, int direction)
        {
            for (var index = 0; index < points.Count; index++)
            {
                if (!IsFinite(points[index].X) || !IsFinite(points[index].Y)) return false;
                if (index > 0 && direction * (points[index].X - points[index - 1].X) <= 0) return false;
            }

            return true;
        }

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
