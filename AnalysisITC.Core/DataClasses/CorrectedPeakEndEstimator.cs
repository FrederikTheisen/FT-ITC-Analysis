using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Data
{
    /// <summary>
    /// Fits a zero-plateau exponential to the tail of each baseline-corrected injection
    /// peak. The corrected trace is treated as immutable input by the caller.
    /// </summary>
    internal static class CorrectedPeakEndEstimator
    {
        internal const double TailFitStartOffsetSeconds = 10.0;
        internal const double EndpointTauMultiples = 6;

        const int MinimumFitPointCount = 4;
        const int TauGridPointCount = 80;
        const int TauRefinementIterations = 32;
        const int RobustReweightingIterations = 4;
        const double HuberTransition = 1.5;
        internal const double MinimumFitImprovement = 0.80;
        const double TauBoundaryTolerance = 0.02;

        public static PeakEndDetectionResult EstimateEndOffsets(
            ExperimentData experiment,
            IReadOnlyList<DataPoint> correctedTrace)
        {
            if (experiment?.Injections == null || experiment.Injections.Count == 0 ||
                correctedTrace == null || correctedTrace.Count == 0)
            {
                return PeakEndDetectionResult.Empty;
            }

            var trace = correctedTrace
                .Where(IsFinite)
                .OrderBy(point => point.Time)
                .ToArray();

            if (trace.Length == 0)
                return PeakEndDetectionResult.Empty;

            var estimates = experiment.Injections
                .Select(injection => AnalyzeInjection(experiment, injection, trace))
                .ToArray();

            ApplyReliablePeerFallbacks(experiment, estimates);
            estimates = ApplyNeighbourDurationMedian(experiment, estimates);

            return new PeakEndDetectionResult(estimates);
        }

        static PeakEndEstimate AnalyzeInjection(
            ExperimentData experiment,
            InjectionData injection,
            IReadOnlyList<DataPoint> trace)
        {
            var requestedFitStartOffset = Math.Max(
                TailFitStartOffsetSeconds,
                injection.IntegrationStartDelay);
            var nextInjectionTime = experiment.Injections
                .Where(candidate => candidate.Time > injection.Time)
                .Select(candidate => (double)candidate.Time)
                .DefaultIfEmpty(double.PositiveInfinity)
                .Min();
            var scopeEnd = Math.Min(injection.Time + injection.Delay, nextInjectionTime);
            var points = trace
                .Where(point =>
                    point.Time >= injection.Time + requestedFitStartOffset &&
                    point.Time <= scopeEnd &&
                    point.Time < nextInjectionTime)
                .ToArray();

            if (points.Length < MinimumFitPointCount)
            {
                return PeakEndEstimate.Unreliable(
                    injection.IntegrationEndOffset,
                    PeakEndDecision.InsufficientSamples,
                    points.Length,
                    requestedFitStartOffset);
            }

            var fitStartOffset = points[0].Time - injection.Time;
            var x = points.Select(point => (double)point.Time - points[0].Time).ToArray();
            var y = points.Select(point => (double)point.Power).ToArray();
            var initialAmplitude = y[0];
            var zeroLoss = y.Sum(value => value * value);

            if (!(zeroLoss > 0))
            {
                return PeakEndEstimate.Unreliable(
                    injection.IntegrationEndOffset,
                    PeakEndDecision.NoSignal,
                    points.Length,
                    fitStartOffset,
                    initialAmplitude: initialAmplitude);
            }

            var sampleInterval = EstimateSampleInterval(x, experiment.TimeStep);
            var minimumTau = Math.Max(0.1, 0.5 * sampleInterval);
            var maximumTau = Math.Max(
                2.0 * minimumTau,
                points[points.Length - 1].Time - points[0].Time);
            var fit = FindBestFit(x, y, minimumTau, maximumTau, zeroLoss);

            if (!fit.IsFinite || Math.Abs(fit.Amplitude) <= double.Epsilon)
            {
                return PeakEndEstimate.Unreliable(
                    injection.IntegrationEndOffset,
                    PeakEndDecision.NumericalFailure,
                    points.Length,
                    fitStartOffset,
                    initialAmplitude: initialAmplitude);
            }

            var polarity = fit.Amplitude >= 0
                ? PeakPolarity.Positive
                : PeakPolarity.Negative;
            var commonArguments = new PeakFitDiagnostics(
                polarity,
                points.Length,
                fitStartOffset,
                initialAmplitude,
                fit.Amplitude,
                fit.Tau,
                fit.RootMeanSquareError,
                fit.Improvement);

            if (fit.Improvement < MinimumFitImprovement)
            {
                return PeakEndEstimate.Unreliable(
                    injection.IntegrationEndOffset,
                    PeakEndDecision.PoorFit,
                    commonArguments);
            }

            var atLowerBoundary = fit.Tau <= minimumTau * (1 + TauBoundaryTolerance);
            var atUpperBoundary = fit.Tau >= maximumTau * (1 - TauBoundaryTolerance);
            if (atLowerBoundary || atUpperBoundary)
            {
                return PeakEndEstimate.Unreliable(
                    injection.IntegrationEndOffset,
                    PeakEndDecision.TauAtBoundary,
                    commonArguments);
            }

            var endOffset = ClampToInjection(
                experiment,
                injection,
                fitStartOffset + EndpointTauMultiples * fit.Tau);
            return PeakEndEstimate.Reliable(
                endOffset,
                PeakEndDecision.ExponentialTail,
                commonArguments);
        }

        static ExponentialFit FindBestFit(
            IReadOnlyList<double> x,
            IReadOnlyList<double> y,
            double minimumTau,
            double maximumTau,
            double zeroLoss)
        {
            var logMinimum = Math.Log(minimumTau);
            var logMaximum = Math.Log(maximumTau);
            var gridStep = (logMaximum - logMinimum) / (TauGridPointCount - 1);
            var bestIndex = 0;
            var best = EvaluateFit(x, y, minimumTau, zeroLoss);

            for (int i = 1; i < TauGridPointCount; i++)
            {
                var candidate = EvaluateFit(
                    x,
                    y,
                    Math.Exp(logMinimum + i * gridStep),
                    zeroLoss);
                if (candidate.RobustLoss < best.RobustLoss)
                {
                    best = candidate;
                    bestIndex = i;
                }
            }

            var lowerLogTau = logMinimum + Math.Max(0, bestIndex - 1) * gridStep;
            var upperLogTau = logMinimum + Math.Min(TauGridPointCount - 1, bestIndex + 1) * gridStep;
            const double goldenRatioConjugate = 0.6180339887498949;
            var left = upperLogTau - goldenRatioConjugate * (upperLogTau - lowerLogTau);
            var right = lowerLogTau + goldenRatioConjugate * (upperLogTau - lowerLogTau);
            var leftFit = EvaluateFit(x, y, Math.Exp(left), zeroLoss);
            var rightFit = EvaluateFit(x, y, Math.Exp(right), zeroLoss);

            for (int iteration = 0; iteration < TauRefinementIterations; iteration++)
            {
                if (leftFit.RobustLoss <= rightFit.RobustLoss)
                {
                    upperLogTau = right;
                    right = left;
                    rightFit = leftFit;
                    left = upperLogTau - goldenRatioConjugate * (upperLogTau - lowerLogTau);
                    leftFit = EvaluateFit(x, y, Math.Exp(left), zeroLoss);
                }
                else
                {
                    lowerLogTau = left;
                    left = right;
                    leftFit = rightFit;
                    right = lowerLogTau + goldenRatioConjugate * (upperLogTau - lowerLogTau);
                    rightFit = EvaluateFit(x, y, Math.Exp(right), zeroLoss);
                }
            }

            var refined = leftFit.RobustLoss <= rightFit.RobustLoss ? leftFit : rightFit;
            return refined.RobustLoss < best.RobustLoss ? refined : best;
        }

        static ExponentialFit EvaluateFit(
            IReadOnlyList<double> x,
            IReadOnlyList<double> y,
            double tau,
            double zeroLoss)
        {
            var decay = new double[x.Count];
            for (int i = 0; i < decay.Length; i++)
                decay[i] = Math.Exp(-x[i] / tau);

            var weights = Enumerable.Repeat(1.0, x.Count).ToArray();
            var amplitude = WeightedAmplitude(decay, y, weights);
            var residuals = new double[x.Count];

            for (int iteration = 0; iteration < RobustReweightingIterations; iteration++)
            {
                for (int i = 0; i < residuals.Length; i++)
                    residuals[i] = y[i] - amplitude * decay[i];

                var scale = EstimateMadSigma(residuals);
                if (!(scale > 0))
                    break;

                var cutoff = HuberTransition * scale;
                for (int i = 0; i < weights.Length; i++)
                {
                    var absoluteResidual = Math.Abs(residuals[i]);
                    weights[i] = absoluteResidual <= cutoff
                        ? 1.0
                        : cutoff / absoluteResidual;
                }

                amplitude = WeightedAmplitude(decay, y, weights);
            }

            double robustLoss = 0;
            double squaredError = 0;
            for (int i = 0; i < residuals.Length; i++)
            {
                residuals[i] = y[i] - amplitude * decay[i];
                robustLoss += weights[i] * residuals[i] * residuals[i];
                squaredError += residuals[i] * residuals[i];
            }

            var rmse = Math.Sqrt(squaredError / residuals.Length);
            var improvement = zeroLoss > 0
                ? Math.Max(0, 1 - squaredError / zeroLoss)
                : 0;
            return new ExponentialFit(
                amplitude,
                tau,
                robustLoss,
                rmse,
                improvement);
        }

        static double WeightedAmplitude(
            IReadOnlyList<double> decay,
            IReadOnlyList<double> y,
            IReadOnlyList<double> weights)
        {
            double numerator = 0;
            double denominator = 0;
            for (int i = 0; i < decay.Count; i++)
            {
                numerator += weights[i] * decay[i] * y[i];
                denominator += weights[i] * decay[i] * decay[i];
            }

            return denominator > double.Epsilon ? numerator / denominator : 0;
        }

        static void ApplyReliablePeerFallbacks(
            ExperimentData experiment,
            PeakEndEstimate[] estimates)
        {
            var peerDuration = Median(
                experiment.Injections
                    .Zip(estimates, (injection, estimate) => new { injection, estimate })
                    .Where(item => item.estimate.IsReliable)
                    .Select(item => (double)item.estimate.EndOffset - item.injection.IntegrationStartDelay),
                double.NaN);

            for (int i = 0; i < estimates.Length; i++)
            {
                if (estimates[i].IsReliable)
                    continue;

                var injection = experiment.Injections[i];
                var fallback = double.IsNaN(peerDuration)
                    ? injection.IntegrationEndOffset
                    : injection.IntegrationStartDelay + peerDuration;
                estimates[i] = estimates[i].WithFallback(
                    ClampToInjection(experiment, injection, fallback),
                    usedPeerFallback: !double.IsNaN(peerDuration));
            }
        }

        static PeakEndEstimate[] ApplyNeighbourDurationMedian(
            ExperimentData experiment,
            PeakEndEstimate[] estimates)
        {
            if (estimates.Length < 3)
                return estimates;

            var durations = experiment.Injections
                .Zip(estimates, (injection, estimate) =>
                    (double)estimate.EndOffset - injection.IntegrationStartDelay)
                .ToArray();
            var filtered = estimates.ToArray();

            for (int i = 1; i < filtered.Length - 1; i++)
            {
                var medianDuration = Median(
                    new[] { durations[i - 1], durations[i], durations[i + 1] },
                    durations[i]);
                var injection = experiment.Injections[i];
                filtered[i] = filtered[i].WithNeighbourFilteredEndOffset(
                    ClampToInjection(
                        experiment,
                        injection,
                        injection.IntegrationStartDelay + medianDuration));
            }

            return filtered;
        }

        static double EstimateSampleInterval(IReadOnlyList<double> x, double fallback)
        {
            var intervals = new List<double>();
            for (int i = 1; i < x.Count; i++)
            {
                var interval = x[i] - x[i - 1];
                if (interval > 0)
                    intervals.Add(interval);
            }

            return Median(intervals, Math.Max(0.1, fallback));
        }

        static double EstimateMadSigma(IEnumerable<double> values)
        {
            var sample = values.Where(IsFinite).ToArray();
            if (sample.Length == 0)
                return 0;

            var median = Median(sample, 0);
            return 1.4826 * Median(sample.Select(value => Math.Abs(value - median)), 0);
        }

        static float ClampToInjection(
            ExperimentData experiment,
            InjectionData injection,
            double endOffset)
        {
            var minimum = injection.IntegrationStartDelay +
                Math.Max(2f * (float)experiment.TimeStep, 1f);
            return FWEMath.Clamp((float)endOffset, minimum, injection.Delay);
        }

        static double Median(IEnumerable<double> values, double fallback)
        {
            var ordered = values
                .Where(IsFinite)
                .OrderBy(value => value)
                .ToArray();

            if (ordered.Length == 0)
                return fallback;

            var middle = ordered.Length / 2;
            if (ordered.Length % 2 == 0)
                return 0.5 * (ordered[middle - 1] + ordered[middle]);

            return ordered[middle];
        }

        static bool IsFinite(DataPoint point) =>
            IsFinite(point.Time) && IsFinite(point.Power);

        static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        readonly struct ExponentialFit
        {
            public double Amplitude { get; }
            public double Tau { get; }
            public double RobustLoss { get; }
            public double RootMeanSquareError { get; }
            public double Improvement { get; }
            public bool IsFinite =>
                CorrectedPeakEndEstimator.IsFinite(Amplitude) &&
                CorrectedPeakEndEstimator.IsFinite(Tau) &&
                CorrectedPeakEndEstimator.IsFinite(RobustLoss);

            public ExponentialFit(
                double amplitude,
                double tau,
                double robustLoss,
                double rootMeanSquareError,
                double improvement)
            {
                Amplitude = amplitude;
                Tau = tau;
                RobustLoss = robustLoss;
                RootMeanSquareError = rootMeanSquareError;
                Improvement = improvement;
            }
        }
    }

    internal enum PeakPolarity
    {
        Unknown,
        Positive,
        Negative,
    }

    internal enum PeakEndDecision
    {
        ExponentialTail,
        PeerDuration,
        KeptCurrentEnd,
        InsufficientSamples,
        NoSignal,
        PoorFit,
        TauAtBoundary,
        NumericalFailure,
    }

    internal readonly struct PeakFitDiagnostics
    {
        public PeakPolarity Polarity { get; }
        public int SampleCount { get; }
        public double FitStartOffset { get; }
        public double InitialAmplitude { get; }
        public double Amplitude { get; }
        public double Tau { get; }
        public double RootMeanSquareError { get; }
        public double FitImprovement { get; }

        public PeakFitDiagnostics(
            PeakPolarity polarity,
            int sampleCount,
            double fitStartOffset,
            double initialAmplitude,
            double amplitude,
            double tau,
            double rootMeanSquareError,
            double fitImprovement)
        {
            Polarity = polarity;
            SampleCount = sampleCount;
            FitStartOffset = fitStartOffset;
            InitialAmplitude = initialAmplitude;
            Amplitude = amplitude;
            Tau = tau;
            RootMeanSquareError = rootMeanSquareError;
            FitImprovement = fitImprovement;
        }
    }

    internal readonly struct PeakEndEstimate
    {
        public float EndOffset { get; }
        public PeakPolarity Polarity { get; }
        public bool IsReliable { get; }
        public bool UsedPeerFallback { get; }
        public float IndividualEndOffset { get; }
        public bool NeighbourFilterApplied { get; }
        public PeakEndDecision DetectionDecision { get; }
        public PeakEndDecision FinalDecision { get; }
        public int SampleCount { get; }
        public double FitStartOffset { get; }
        public double InitialAmplitude { get; }
        public double Amplitude { get; }
        public double Tau { get; }
        public double RootMeanSquareError { get; }
        public double FitImprovement { get; }

        PeakEndEstimate(
            float endOffset,
            bool isReliable,
            bool usedPeerFallback,
            float individualEndOffset,
            bool neighbourFilterApplied,
            PeakEndDecision detectionDecision,
            PeakEndDecision finalDecision,
            PeakFitDiagnostics diagnostics)
        {
            EndOffset = endOffset;
            Polarity = diagnostics.Polarity;
            IsReliable = isReliable;
            UsedPeerFallback = usedPeerFallback;
            IndividualEndOffset = individualEndOffset;
            NeighbourFilterApplied = neighbourFilterApplied;
            DetectionDecision = detectionDecision;
            FinalDecision = finalDecision;
            SampleCount = diagnostics.SampleCount;
            FitStartOffset = diagnostics.FitStartOffset;
            InitialAmplitude = diagnostics.InitialAmplitude;
            Amplitude = diagnostics.Amplitude;
            Tau = diagnostics.Tau;
            RootMeanSquareError = diagnostics.RootMeanSquareError;
            FitImprovement = diagnostics.FitImprovement;
        }

        public static PeakEndEstimate Reliable(
            float endOffset,
            PeakEndDecision decision,
            PeakFitDiagnostics diagnostics) =>
            new PeakEndEstimate(
                endOffset,
                isReliable: true,
                usedPeerFallback: false,
                individualEndOffset: endOffset,
                neighbourFilterApplied: false,
                detectionDecision: decision,
                finalDecision: decision,
                diagnostics);

        public static PeakEndEstimate Unreliable(
            float endOffset,
            PeakEndDecision decision,
            int sampleCount,
            double fitStartOffset,
            double initialAmplitude = 0) =>
            Unreliable(
                endOffset,
                decision,
                new PeakFitDiagnostics(
                    PeakPolarity.Unknown,
                    sampleCount,
                    fitStartOffset,
                    initialAmplitude,
                    0,
                    double.NaN,
                    double.NaN,
                    0));

        public static PeakEndEstimate Unreliable(
            float endOffset,
            PeakEndDecision decision,
            PeakFitDiagnostics diagnostics) =>
            new PeakEndEstimate(
                endOffset,
                isReliable: false,
                usedPeerFallback: false,
                individualEndOffset: endOffset,
                neighbourFilterApplied: false,
                detectionDecision: decision,
                finalDecision: decision,
                diagnostics);

        public PeakEndEstimate WithFallback(
            float endOffset,
            bool usedPeerFallback) =>
            new PeakEndEstimate(
                endOffset,
                isReliable: false,
                usedPeerFallback: usedPeerFallback,
                individualEndOffset: endOffset,
                neighbourFilterApplied: false,
                DetectionDecision,
                finalDecision: usedPeerFallback
                    ? PeakEndDecision.PeerDuration
                    : PeakEndDecision.KeptCurrentEnd,
                new PeakFitDiagnostics(
                    Polarity,
                    SampleCount,
                    FitStartOffset,
                    InitialAmplitude,
                    Amplitude,
                    Tau,
                    RootMeanSquareError,
                    FitImprovement));

        public PeakEndEstimate WithNeighbourFilteredEndOffset(float endOffset) =>
            new PeakEndEstimate(
                endOffset,
                IsReliable,
                UsedPeerFallback,
                individualEndOffset: EndOffset,
                neighbourFilterApplied: true,
                DetectionDecision,
                FinalDecision,
                new PeakFitDiagnostics(
                    Polarity,
                    SampleCount,
                    FitStartOffset,
                    InitialAmplitude,
                    Amplitude,
                    Tau,
                    RootMeanSquareError,
                    FitImprovement));
    }

    internal sealed class PeakEndDetectionResult
    {
        public static PeakEndDetectionResult Empty { get; } =
            new PeakEndDetectionResult(Array.Empty<PeakEndEstimate>());

        public PeakEndEstimate[] Estimates { get; }
        public float[] EndOffsets =>
            Estimates.Select(estimate => estimate.EndOffset).ToArray();

        public PeakEndDetectionResult(PeakEndEstimate[] estimates)
        {
            Estimates = estimates ?? Array.Empty<PeakEndEstimate>();
        }
    }
}
