using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Processing;

namespace AnalysisITC.Core.Interpretation
{
    internal static class AnalysisInterpretationDiagnostics
    {
        const double Micro = 1e6;

        internal static InterpretationBaselineEvidence Baseline(ExperimentData data, string experimentEvidenceId)
        {
            var processor = data?.Processor;
            var interpolator = processor?.Interpolator;
            var result = new InterpretationBaselineEvidence
            {
                EvidenceId = experimentEvidenceId + "/baseline",
                Method = processor?.BaselineType.ToString(),
                Completed = processor?.BaselineCompleted == true,
                Locked = processor?.IsLocked == true,
                IntegrationRegionsExcluded = processor?.DiscardIntegratedPoints == true,
            };
            AddControls(result, interpolator);

            var raw = data?.DataPoints;
            var baseline = interpolator?.Baseline;
            if (raw == null || baseline == null || raw.Count < 2 || baseline.Count != raw.Count)
            {
                result.UnavailableReason = baseline == null || baseline.Count == 0
                    ? "No stored baseline trace is available."
                    : "The baseline and thermogram sample counts do not match.";
                return result;
            }
            var pairs = Enumerable.Range(0, raw.Count)
                .Select(i => new Pair(raw[i].Time, baseline[i].Value, raw[i].Power))
                .Where(p => Finite(p.Time) && Finite(p.Baseline) && Finite(p.Raw))
                .OrderBy(p => p.Time).ToList();
            if (pairs.Count < 2 || pairs.Last().Time <= pairs.First().Time)
            {
                result.UnavailableReason = "Fewer than two finite, time-separated baseline samples are available.";
                return result;
            }

            result.IsAvailable = true;
            var start = pairs.First();
            var end = pairs.Last();
            result.TraceDurationSeconds = F(end.Time - start.Time);
            result.StartPowerMicrowatts = F(start.Baseline * Micro);
            result.EndPowerMicrowatts = F(end.Baseline * Micro);
            result.NetDriftMicrowatts = F((end.Baseline - start.Baseline) * Micro);
            var regression = Regression(pairs.Select(p => p.Time).ToArray(), pairs.Select(p => p.Baseline).ToArray());
            result.LinearDriftRateMicrowattsPerHour = regression.available ? F(regression.slope * Micro * 3600) : null;
            if (!regression.available) result.LinearTrendUnavailableReason = "Finite baseline times do not span a non-zero regression domain.";
            result.RangeMicrowatts = F((pairs.Max(p => p.Baseline) - pairs.Min(p => p.Baseline)) * Micro);
            if (regression.available)
                result.RmsDeviationFromLinearTrendMicrowatts = F(Math.Sqrt(pairs.Average(p =>
                {
                    var d = p.Baseline - (regression.intercept + regression.slope * p.Time);
                    return d * d;
                })) * Micro);

            var outside = pairs.Where(p => !InsideIntegration(data, p.Time))
                .Select(p => (p.Raw - p.Baseline) * Micro).ToArray();
            result.OutsideIntegrationPointCount = outside.Length;
            if (outside.Length > 0)
            {
                result.OutsideIntegrationRmsRawMinusBaselineMicrowatts = F(Math.Sqrt(outside.Average(v => v * v)));
                var median = Median(outside.ToArray());
                result.OutsideIntegrationMedianAbsoluteDeviationRawMinusBaselineMicrowatts =
                    F(Median(outside.Select(v => Math.Abs(v - median)).ToArray()));
            }
            else result.OutsideIntegrationStatisticsUnavailableReason = "No finite samples are available outside integration regions.";

            for (var i = 0; i < 12; i++)
            {
                var time = start.Time + (end.Time - start.Time) * i / 11.0;
                result.Landmarks.Add(new InterpretationBaselineLandmark
                {
                    TimeSeconds = F(time),
                    PowerMicrowatts = F(Interpolate(pairs, time, p => p.Baseline) * Micro),
                });
            }
            return result;
        }

        internal static void AddInjectionBaseline(
            InterpretationInjectionEvidence target, ExperimentData data, InjectionData injection)
        {
            var raw = data?.DataPoints;
            var baseline = data?.Processor?.Interpolator?.Baseline;
            if (raw == null || baseline == null || raw.Count < 2 || raw.Count != baseline.Count) return;
            var pairs = Enumerable.Range(0, raw.Count)
                .Select(i => new Pair(raw[i].Time, baseline[i].Value, raw[i].Power))
                .Where(p => Finite(p.Time) && Finite(p.Baseline)).OrderBy(p => p.Time).ToList();
            if (pairs.Count < 2) return;
            var start = (double)injection.IntegrationStartTime;
            var end = (double)injection.IntegrationEndTime;
            if (end <= start || start < pairs.First().Time || end > pairs.Last().Time) return;
            var a = Interpolate(pairs, start, p => p.Baseline);
            var b = Interpolate(pairs, end, p => p.Baseline);
            target.BaselineAtIntegrationStartMicrowatts = F(a * Micro);
            target.BaselineAtIntegrationEndMicrowatts = F(b * Micro);
            target.BaselineChangeAcrossIntegrationMicrowatts = F((b - a) * Micro);
            target.IntegratedBaselineCorrectionJoules = F(Integrate(pairs, start, end));
        }

        internal static InterpretationResidualDiagnosticsEvidence Residuals(
            ExperimentData data, SolutionInterface solution, string experimentEvidenceId,
            Func<AnalysisXAxisType, InjectionData, double?> axisValue)
        {
            var result = new InterpretationResidualDiagnosticsEvidence
            { EvidenceId = experimentEvidenceId + "/residual-diagnostics" };
            var rows = new List<ResidualRow>();
            foreach (var injection in data.Injections.Where(i => i.Include).OrderBy(i => i.ID))
            {
                double residual;
                try { residual = solution.Model.Residual(injection) / injection.InjectionMass; }
                catch { continue; }
                if (!Finite(residual)) continue;
                rows.Add(new ResidualRow(residual, axisValue(data.AxisType, injection),
                    Finite(injection.SD) && injection.SD > 0 ? (double?)injection.SD : null));
            }
            result.IncludedFiniteResidualCount = rows.Count;
            if (rows.Count == 0)
            {
                result.UnavailableReason = "No included injection has a finite residual.";
                result.StandardisedResidualUnavailableReason = "No included injection has both a finite residual and positive finite injection error.";
                return result;
            }
            result.IsAvailable = true;
            var values = rows.Select(r => r.Residual).ToArray();
            result.MeanResidualJoulesPerMole = F(values.Average());
            result.RmsResidualJoulesPerMole = F(Math.Sqrt(values.Average(v => v * v)));
            result.MeanAbsoluteResidualJoulesPerMole = F(values.Average(Math.Abs));
            result.MedianAbsoluteResidualJoulesPerMole = F(Median(values.Select(Math.Abs).ToArray()));
            if (values.Length >= 3)
            {
                var baseSize = values.Length / 3;
                var remainder = values.Length % 3;
                var sizes = new[] { baseSize + (remainder > 0 ? 1 : 0), baseSize + (remainder > 1 ? 1 : 0), baseSize };
                var groups = new double[3][];
                var offset = 0;
                for (var g = 0; g < 3; g++) { groups[g] = values.Skip(offset).Take(sizes[g]).ToArray(); offset += sizes[g]; }
                result.EarlyMeanResidualJoulesPerMole = Mean(groups[0]);
                result.MiddleMeanResidualJoulesPerMole = Mean(groups[1]);
                result.LateMeanResidualJoulesPerMole = Mean(groups[2]);
            }
            else result.InjectionOrderGroupMeansUnavailableReason = "At least three included finite residuals are required for early, middle, and late groups.";
            var axisRows = rows.Where(r => r.Axis.HasValue && Finite(r.Axis.Value)).ToArray();
            if (axisRows.Length >= 2)
            {
                var regression = Regression(axisRows.Select(r => r.Axis.Value).ToArray(), axisRows.Select(r => r.Residual).ToArray());
                result.ResidualSlopeAgainstAnalysisAxis = regression.available ? F(regression.slope) : null;
                if (!regression.available) result.ResidualSlopeUnavailableReason = "The supplied finite analysis-axis values have no variation.";
            }
            else result.ResidualSlopeUnavailableReason = "At least two finite residual and analysis-axis pairs are required.";
            if (values.Length >= 2)
            {
                var left = values.Take(values.Length - 1).ToArray();
                var right = values.Skip(1).ToArray();
                result.LagOneAutocorrelation = Correlation(left, right);
                if (!result.LagOneAutocorrelation.HasValue) result.LagOneAutocorrelationUnavailableReason = "Lagged residual values have no finite variance.";
            }
            else result.LagOneAutocorrelationUnavailableReason = "At least two included finite residuals are required.";
            var signs = values.Select(Math.Sign).Where(s => s != 0).ToArray();
            if (signs.Length > 0)
            {
                var runs = 1;
                var longest = 1;
                var current = 1;
                for (var i = 1; i < signs.Length; i++)
                {
                    if (signs[i] == signs[i - 1]) { current++; longest = Math.Max(longest, current); }
                    else { runs++; current = 1; }
                }
                result.SignRunCount = runs;
                result.LongestSameSignRun = longest;
            }
            else result.SignRunsUnavailableReason = "All included finite residuals are exactly zero.";
            var standardised = rows.Where(r => r.Error.HasValue).Select(r => Math.Abs(r.Residual / r.Error.Value)).Where(Finite).ToArray();
            result.StandardisedResidualCount = standardised.Length;
            if (standardised.Length > 0)
            {
                result.MaximumAbsoluteStandardisedResidual = F(standardised.Max());
                result.CountAboveTwoInjectionErrors = standardised.Count(v => v > 2);
                result.CountAboveThreeInjectionErrors = standardised.Count(v => v > 3);
            }
            else result.StandardisedResidualUnavailableReason = "No included injection has both a finite residual and positive finite injection error.";
            return result;
        }

        static void AddControls(InterpretationBaselineEvidence result, BaselineInterpolator interpolator)
        {
            if (interpolator is SplineInterpolator spline)
                result.Spline = new InterpretationSplineBaselineControls
                {
                    Algorithm = spline.Algorithm.ToString(), Density = spline.PointDensity.ToString(),
                    HandleMode = spline.HandleMode.ToString(), PointsPerInjection = spline.PointsPerInjection,
                    ControlPoints = spline.SplinePoints.Select(p => new InterpretationSplineControlPoint
                    {
                        TimeSeconds = F(p.Time), PowerMicrowatts = F(p.Power * Micro),
                        SlopeMicrowattsPerSecond = F(p.Slope * Micro), Locked = p.Locked,
                        SlopeLocked = p.SlopeLocked, UserDefined = p.UserDefined, Linear = p.Linear,
                    }).ToList(),
                };
            else if (interpolator is SegmentedBaselineInterpolator segmented)
                result.Segmented = new InterpretationSegmentedBaselineControls
                {
                    Degree = segmented.Degree,
                    Segments = segmented.Segments.Select(s => new InterpretationBaselineSegment
                    {
                        Scope = s.Kind.ToString(), InjectionId = s.InjectionID < 0 ? (int?)null : s.InjectionID + 1,
                        StartTimeSeconds = F(s.StartTime), EndTimeSeconds = F(s.EndTime), CenterTimeSeconds = F(s.CenterTime),
                        CoefficientsSi = s.Coefficients.Select(F).ToList(),
                    }).ToList(),
                };
            else if (interpolator is PolynomialLeastSquaresInterpolator polynomial)
                result.Polynomial = new InterpretationPolynomialBaselineControls
                { Degree = polynomial.Degree, RejectionZLimit = F(polynomial.ZLimit) };
            else if (interpolator is AssymetricLeastSquaresInterpolator als)
                result.AsymmetricLeastSquares = new InterpretationAsymmetricLeastSquaresControls
                { Iterations = als.Iterations, Lambda = F(als.Lambda), Asymmetry = F(als.Asymmetry) };
        }

        static bool InsideIntegration(ExperimentData data, double time) =>
            data.Injections.Any(i => time >= i.IntegrationStartTime && time <= i.IntegrationEndTime);

        static double Integrate(List<Pair> points, double start, double end)
        {
            var times = new List<double> { start };
            times.AddRange(points.Where(p => p.Time > start && p.Time < end).Select(p => p.Time));
            times.Add(end);
            var sum = 0.0;
            for (var i = 1; i < times.Count; i++)
                sum += (times[i] - times[i - 1]) *
                    (Interpolate(points, times[i - 1], p => p.Baseline) + Interpolate(points, times[i], p => p.Baseline)) / 2.0;
            return sum;
        }

        static double Interpolate(List<Pair> points, double time, Func<Pair, double> selector)
        {
            if (time <= points[0].Time) return selector(points[0]);
            if (time >= points[points.Count - 1].Time) return selector(points[points.Count - 1]);
            var hi = points.FindIndex(p => p.Time >= time);
            var a = points[hi - 1]; var b = points[hi];
            if (b.Time == a.Time) return selector(b);
            var f = (time - a.Time) / (b.Time - a.Time);
            return selector(a) + f * (selector(b) - selector(a));
        }

        static (bool available, double slope, double intercept) Regression(double[] x, double[] y)
        {
            if (x.Length < 2 || x.Length != y.Length) return (false, 0, 0);
            var xm = x.Average(); var ym = y.Average();
            var denominator = x.Sum(v => (v - xm) * (v - xm));
            if (!(denominator > 0) || !Finite(denominator)) return (false, 0, 0);
            var slope = x.Zip(y, (a, b) => (a - xm) * (b - ym)).Sum() / denominator;
            return Finite(slope) ? (true, slope, ym - slope * xm) : (false, 0, 0);
        }

        static double? Correlation(double[] x, double[] y)
        {
            var xm = x.Average(); var ym = y.Average();
            var xx = x.Sum(v => (v - xm) * (v - xm));
            var yy = y.Sum(v => (v - ym) * (v - ym));
            if (!(xx > 0) || !(yy > 0)) return null;
            return F(x.Zip(y, (a, b) => (a - xm) * (b - ym)).Sum() / Math.Sqrt(xx * yy));
        }

        static double? Mean(double[] values) => values.Length == 0 ? null : F(values.Average());
        static double Median(double[] values)
        {
            Array.Sort(values);
            return values.Length % 2 == 1 ? values[values.Length / 2] : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2;
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static double? F(double value) => Finite(value) ? (double?)value : null;
        readonly struct Pair { internal readonly double Time, Baseline, Raw; internal Pair(double t, double b, double r) { Time = t; Baseline = b; Raw = r; } }
        readonly struct ResidualRow { internal readonly double Residual; internal readonly double? Axis, Error; internal ResidualRow(double r, double? a, double? e) { Residual = r; Axis = a; Error = e; } }
    }
}
