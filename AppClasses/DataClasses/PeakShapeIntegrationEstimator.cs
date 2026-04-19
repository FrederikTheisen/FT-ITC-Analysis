using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    internal static class PeakShapeIntegrationEstimator
    {
        public const float DefaultFitFactor = 1.0f;

        const double NominalReturnTauMultiples = 3.0;
        const double DefaultTauFractionOfDelay = 0.07;
        const double MinTauScaleVsGlobal = 0.2;
        const double MaxTauScaleVsGlobal = 3.0;
        const double MaxReturnClampTauMultiples = 5.0;

        const double MinPreWindowSeconds = 6.0;
        const double MaxPreWindowSeconds = 18.0;
        const double PreWindowFractionOfDelay = 0.12;

        const double MinPostWindowSeconds = 8.0;
        const double MaxPostWindowSeconds = 24.0;
        const double PostWindowFractionOfDelay = 0.18;

        const double SmoothingWindowSeconds = 2.0;
        const double HoldBelowThresholdSeconds = 4.0;
        const double NoiseFloorSigma = 2.5;
        const double ReturnThresholdSigma = 3.0;
        const double ReturnThresholdPeakFraction = 0.04;
        const double MinimumPeakSignalToNoise = 4.0;
        const double MinimumApexFractionOfDelay = 0.05;

        public static float[] EstimateEndOffsets(ExperimentData experiment, float factor)
        {
            if (experiment?.Injections == null || experiment.Injections.Count == 0)
                return Array.Empty<float>();

            factor = Math.Max(0.1f, factor);

            var descriptors = experiment.Injections
                .Select(inj => AnalyzeInjection(experiment, inj))
                .ToArray();

            var globalTau = EstimateGlobalTau(experiment, descriptors);
            if (!(globalTau > experiment.TimeStep))
            {
                globalTau = Math.Max(
                    2.0 * experiment.TimeStep,
                    GetMedian(experiment.Injections.Select(inj => (double)inj.Delay), 0) * DefaultTauFractionOfDelay);
            }

            var endOffsets = new float[experiment.Injections.Count];
            for (int i = 0; i < experiment.Injections.Count; i++)
            {
                endOffsets[i] = EstimateEndOffset(experiment.Injections[i], descriptors[i], globalTau, experiment.TimeStep, factor);
            }

            return endOffsets;
        }

        public static float EstimateEndOffset(ExperimentData experiment, InjectionData injection, float factor)
        {
            if (experiment?.Injections == null || injection == null)
                return 0;

            var offsets = EstimateEndOffsets(experiment, factor);
            if (offsets.Length == 0)
                return 0;

            int idx = experiment.Injections.IndexOf(injection);
            if (idx < 0)
                idx = Math.Clamp(injection.ID, 0, offsets.Length - 1);

            return offsets[idx];
        }

        static float EstimateEndOffset(InjectionData injection, PeakShapeDescriptor descriptor, double globalTau, double dt, float factor)
        {
            double minimumIntegrationTime = Math.Max(2.0 * dt, 1.0);
            double tau = descriptor.LocalTau;

            if (!(tau > dt))
                tau = globalTau;

            if (!(tau > dt))
                tau = Math.Max(minimumIntegrationTime, injection.Delay * DefaultTauFractionOfDelay);

            if (globalTau > dt)
            {
                tau = Math.Clamp(tau, MinTauScaleVsGlobal * globalTau, MaxTauScaleVsGlobal * globalTau);

                double weight = GetLocalTauWeight(descriptor.SignalToNoise);
                tau = (weight * tau) + ((1.0 - weight) * globalTau);
            }

            double apexOffset = descriptor.ApexOffset;
            if (!(apexOffset > 0))
                apexOffset = Math.Max(injection.Duration, MinimumApexFractionOfDelay * injection.Delay);

            double endOffset = apexOffset + (NominalReturnTauMultiples * factor * tau);

            if (descriptor.ReturnOffset.HasValue)
            {
                double cappedReturn = Math.Min(descriptor.ReturnOffset.Value, apexOffset + MaxReturnClampTauMultiples * tau);
                endOffset = Math.Max(endOffset, cappedReturn);
            }

            return Math.Clamp(
                (float)endOffset,
                injection.IntegrationStartDelay + (float)minimumIntegrationTime,
                injection.Delay);
        }

        static PeakShapeDescriptor AnalyzeInjection(ExperimentData experiment, InjectionData injection)
        {
            try
            {
                double startTime = injection.Time;
                double endTime = injection.Time + injection.Delay;

                var points = experiment.DataPoints
                    .Where(dp => dp.Time >= startTime && dp.Time <= endTime)
                    .ToList();

                if (points.Count < 8)
                    return PeakShapeDescriptor.Empty(injection.Delay);

                double preWindow = Math.Clamp(injection.Delay * PreWindowFractionOfDelay, MinPreWindowSeconds, MaxPreWindowSeconds);
                double postWindow = Math.Clamp(injection.Delay * PostWindowFractionOfDelay, MinPostWindowSeconds, MaxPostWindowSeconds);

                var prePoints = experiment.DataPoints
                    .Where(dp => dp.Time < startTime && dp.Time >= startTime - preWindow)
                    .ToList();
                var postPoints = experiment.DataPoints
                    .Where(dp => dp.Time > endTime - postWindow && dp.Time <= endTime)
                    .ToList();

                double preLevel = GetMedian(prePoints.Select(dp => (double)dp.Power), points.First().Power);
                double postLevel = GetMedian(postPoints.Select(dp => (double)dp.Power), points.Last().Power);

                double[] time = new double[points.Count];
                double[] detrended = new double[points.Count];

                for (int i = 0; i < points.Count; i++)
                {
                    time[i] = points[i].Time - injection.Time;
                    double fraction = injection.Delay > 0 ? time[i] / injection.Delay : 0.0;
                    double baseline = preLevel + ((postLevel - preLevel) * fraction);
                    detrended[i] = points[i].Power - baseline;
                }

                int smoothHalfWindow = Math.Max(1, (int)Math.Round((SmoothingWindowSeconds / experiment.TimeStep) / 2.0));
                double[] smooth = MovingAverage(detrended, smoothHalfWindow);

                int apex = 0;
                double maxAbs = 0;
                for (int i = 0; i < smooth.Length; i++)
                {
                    double abs = Math.Abs(smooth[i]);
                    if (abs > maxAbs)
                    {
                        maxAbs = abs;
                        apex = i;
                    }
                }

                if (maxAbs <= 0)
                    return PeakShapeDescriptor.Empty(injection.Delay);

                int sign = smooth[apex] >= 0 ? 1 : -1;
                double[] magnitude = new double[smooth.Length];
                for (int i = 0; i < smooth.Length; i++)
                    magnitude[i] = Math.Max(0.0, sign * smooth[i]);

                double peakAmplitude = magnitude[apex];
                if (peakAmplitude <= 0)
                    return PeakShapeDescriptor.Empty(injection.Delay);

                var noiseSamples = new List<double>();
                noiseSamples.AddRange(prePoints.Select(dp => (double)dp.Power - preLevel));
                noiseSamples.AddRange(postPoints.Select(dp => (double)dp.Power - postLevel));

                double noiseSigma = GetMadSigma(noiseSamples);
                if (!(noiseSigma > 0))
                    noiseSigma = Math.Max(peakAmplitude / 1000.0, 1e-12);

                double noiseFloor = NoiseFloorSigma * noiseSigma;
                double effectiveAmplitude = Math.Max(peakAmplitude - noiseFloor, peakAmplitude * 0.35);

                double tailArea = 0;
                double lastTime = time[apex];
                for (int i = apex; i < magnitude.Length; i++)
                {
                    double effectiveSignal = Math.Max(0.0, magnitude[i] - noiseFloor);
                    double dt = time[i] - lastTime;
                    tailArea += effectiveSignal * dt;
                    lastTime = time[i];
                }

                double localTau = effectiveAmplitude > 0 ? tailArea / effectiveAmplitude : 0;
                double threshold = Math.Max(ReturnThresholdSigma * noiseSigma, ReturnThresholdPeakFraction * peakAmplitude);
                double? returnOffset = FindReturnOffset(time, magnitude, apex, threshold, experiment.TimeStep);

                return new PeakShapeDescriptor(
                    apexOffset: time[apex],
                    peakAmplitude: peakAmplitude,
                    noiseSigma: noiseSigma,
                    localTau: localTau,
                    returnOffset: returnOffset,
                    delay: injection.Delay);
            }
            catch
            {
                return PeakShapeDescriptor.Empty(injection.Delay);
            }
        }

        static double EstimateGlobalTau(ExperimentData experiment, PeakShapeDescriptor[] descriptors)
        {
            IEnumerable<double> valid = experiment.Injections
                .Zip(descriptors, (inj, desc) => new { inj, desc })
                .Where(x => x.inj.Include && x.desc.IsUsable(experiment.TimeStep) && x.desc.SignalToNoise >= MinimumPeakSignalToNoise)
                .Select(x => x.desc.LocalTau);

            if (!valid.Any())
            {
                valid = descriptors
                    .Where(desc => desc.IsUsable(experiment.TimeStep))
                    .Select(desc => desc.LocalTau);
            }

            return GetMedian(valid, 0);
        }

        static double? FindReturnOffset(double[] time, double[] magnitude, int apex, double threshold, double dt)
        {
            dt = Math.Max(dt, 1e-6);
            int holdSamples = Math.Max(3, (int)Math.Round(HoldBelowThresholdSeconds / dt));
            if (magnitude.Length <= holdSamples)
                return null;

            for (int i = apex; i <= magnitude.Length - holdSamples; i++)
            {
                bool belowThreshold = true;
                for (int j = i; j < i + holdSamples; j++)
                {
                    if (magnitude[j] > threshold)
                    {
                        belowThreshold = false;
                        break;
                    }
                }

                if (belowThreshold)
                    return time[i];
            }

            return null;
        }

        static double[] MovingAverage(double[] values, int halfWindow)
        {
            if (values.Length == 0)
                return Array.Empty<double>();

            var smooth = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                int start = Math.Max(0, i - halfWindow);
                int end = Math.Min(values.Length - 1, i + halfWindow);

                double sum = 0;
                int count = 0;
                for (int j = start; j <= end; j++)
                {
                    sum += values[j];
                    count++;
                }

                smooth[i] = count > 0 ? sum / count : values[i];
            }

            return smooth;
        }

        static double GetLocalTauWeight(double signalToNoise)
        {
            if (!(signalToNoise > 0))
                return 0.2;

            return Math.Clamp((signalToNoise - 3.0) / 7.0, 0.2, 0.85);
        }

        static double GetMedian(IEnumerable<double> values, double fallback)
        {
            var ordered = values
                .Where(v => !double.IsNaN(v) && !double.IsInfinity(v))
                .OrderBy(v => v)
                .ToArray();

            if (ordered.Length == 0)
                return fallback;

            int middle = ordered.Length / 2;
            if (ordered.Length % 2 == 0)
                return (ordered[middle - 1] + ordered[middle]) / 2.0;

            return ordered[middle];
        }

        static double GetMadSigma(IEnumerable<double> values)
        {
            var sample = values
                .Where(v => !double.IsNaN(v) && !double.IsInfinity(v))
                .ToArray();

            if (sample.Length == 0)
                return 0;

            double median = GetMedian(sample, 0);
            double mad = GetMedian(sample.Select(v => Math.Abs(v - median)), 0);
            return 1.4826 * mad;
        }

        readonly struct PeakShapeDescriptor
        {
            public readonly double ApexOffset;
            public readonly double PeakAmplitude;
            public readonly double NoiseSigma;
            public readonly double LocalTau;
            public readonly double? ReturnOffset;
            public readonly double Delay;

            public PeakShapeDescriptor(double apexOffset, double peakAmplitude, double noiseSigma, double localTau, double? returnOffset, double delay)
            {
                ApexOffset = apexOffset;
                PeakAmplitude = peakAmplitude;
                NoiseSigma = noiseSigma;
                LocalTau = localTau;
                ReturnOffset = returnOffset;
                Delay = delay;
            }

            public double SignalToNoise => NoiseSigma > 0 ? PeakAmplitude / NoiseSigma : 0;

            public bool IsUsable(double dt) => PeakAmplitude > 0 && LocalTau > dt && ApexOffset >= 0 && ApexOffset < Delay;

            public static PeakShapeDescriptor Empty(double delay) => new(0, 0, 0, 0, null, delay);
        }
    }
}
