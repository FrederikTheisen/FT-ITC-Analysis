using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using MathNet.Numerics;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Data
{
    public enum BufferSubtractionMethod
    {
        MatchedInjection = 0,
        Linear = 1,
        ExponentialDecay = 2,
    }

    public sealed class BufferSubtractionSettings
    {
        // Persisted buffer subtraction attribute: reference experiment ID plus model type.
        public string ReferenceExperimentId { get; }
        public BufferSubtractionMethod Method { get; }
        public string MethodDisplayName => Method.GetDisplayName();

        public ExperimentData ReferenceExperiment => string.IsNullOrEmpty(ReferenceExperimentId)
            ? null
            : DataManager.Data.FirstOrDefault(d => d.UniqueID == ReferenceExperimentId);

        public BufferSubtractionSettings(string referenceExperimentId, BufferSubtractionMethod method)
        {
            ReferenceExperimentId = referenceExperimentId;
            Method = method;
        }

        public static BufferSubtractionSettings FromAttribute(ExperimentAttribute attribute)
        {
            if (attribute == null || attribute.Key != AttributeKey.BufferSubtraction) return null;

            return new BufferSubtractionSettings(
                attribute.StringValue,
                NormalizeMethod(attribute.IntValue));
        }

        public ExperimentAttribute ToAttribute()
        {
            // Reference ID stays in StringValue; method is stored in IntValue.
            var attribute = ExperimentAttribute.ExperimentReference("Reference", ReferenceExperimentId);
            attribute.IntValue = (int)Method;

            return attribute;
        }

        static BufferSubtractionMethod NormalizeMethod(int method)
        {
            if (Enum.IsDefined(typeof(BufferSubtractionMethod), method))
                return (BufferSubtractionMethod)method;

            return BufferSubtractionMethod.MatchedInjection;
        }
    }

    public static class BufferSubtractionMethodExtensions
    {
        public static string GetDisplayName(this BufferSubtractionMethod method)
        {
            switch (method)
            {
                case BufferSubtractionMethod.Linear:
                    return "Linear";
                case BufferSubtractionMethod.ExponentialDecay:
                    return "Exp. decay";
                default:
                    return "Matched";
            }
        }
    }

    public sealed class BufferSubtractionModelPoint
    {
        public double InjectionNumber { get; }
        public double Heat { get; }

        public BufferSubtractionModelPoint(double injectionNumber, double heat)
        {
            InjectionNumber = injectionNumber;
            Heat = heat;
        }
    }

    public sealed class BufferSubtractionModel
    {
        // Common evaluator used by matched-point subtraction and fitted subtraction models.
        public delegate bool Evaluator(double injectionNumber, out FloatWithError heat);

        readonly Evaluator evaluator;

        public BufferSubtractionMethod Method { get; }
        public bool CanEvaluate => evaluator != null;
        public bool CanDrawLine => CanEvaluate && Method != BufferSubtractionMethod.MatchedInjection;

        public static BufferSubtractionModel Empty(BufferSubtractionMethod method) => new BufferSubtractionModel(method, null);

        public BufferSubtractionModel(BufferSubtractionMethod method, Evaluator evaluator)
        {
            Method = method;
            this.evaluator = evaluator;
        }

        public bool TryEvaluate(double injectionNumber, out FloatWithError heat)
        {
            heat = default;
            return evaluator != null && evaluator(injectionNumber, out heat);
        }

        public List<BufferSubtractionModelPoint> GetLinePoints(double minInjectionNumber, double maxInjectionNumber)
        {
            // Only fitted methods draw a continuous model line in the preview graph.
            var points = new List<BufferSubtractionModelPoint>();
            if (!CanDrawLine) return points;

            var pointCount = Method == BufferSubtractionMethod.Linear ? 2 : 80;
            var span = maxInjectionNumber - minInjectionNumber;
            if (span <= 0) return points;

            for (int i = 0; i < pointCount; i++)
            {
                var fraction = pointCount == 1 ? 0 : (double)i / (pointCount - 1);
                var injectionNumber = minInjectionNumber + fraction * span;

                if (TryEvaluate(injectionNumber, out var heat))
                    points.Add(new BufferSubtractionModelPoint(injectionNumber, heat.Value));
            }

            return points;
        }
    }

    public static class BufferSubtractionCalculator
    {
        public static BufferSubtractionModel BuildModel(BufferSubtractionSettings settings)
        {
            return BuildModel(settings?.ReferenceExperiment, settings);
        }

        public static BufferSubtractionModel BuildModel(ExperimentData referenceExperiment, BufferSubtractionSettings settings)
        {
            // referenceExperiment is the buffer/reference data, not the target data being corrected.
            var method = settings?.Method ?? BufferSubtractionMethod.MatchedInjection;
            if (referenceExperiment == null) return BufferSubtractionModel.Empty(method);

            switch (method)
            {
                case BufferSubtractionMethod.Linear:
                    return BuildLinearModel(referenceExperiment);
                case BufferSubtractionMethod.ExponentialDecay:
                    return BuildExponentialDecayModel(referenceExperiment);
                default:
                    return BuildMatchedInjectionModel(referenceExperiment);
            }
        }

        public static bool TryGetReferenceHeat(InjectionData targetInjection, BufferSubtractionModel model, out FloatWithError heat)
        {
            // Evaluate the buffer heat at the target injection's injection number.
            heat = default;
            if (targetInjection == null || model == null) return false;

            return model.TryEvaluate(GetInjectionNumber(targetInjection), out heat);
        }

        static BufferSubtractionModel BuildMatchedInjectionModel(ExperimentData referenceExperiment)
        {
            // Legacy behavior: same injection first, then nearby included buffer injections.
            return new BufferSubtractionModel(
                BufferSubtractionMethod.MatchedInjection,
                (double injectionNumber, out FloatWithError heat) => TryGetMatchedReferenceHeat(referenceExperiment, injectionNumber, out heat));
        }

        static BufferSubtractionModel BuildLinearModel(ExperimentData referenceExperiment)
        {
            // Fits use included, integrated buffer injections only.
            var points = GetValidReferencePoints(referenceExperiment).ToList();
            if (points.Count < 2) return BufferSubtractionModel.Empty(BufferSubtractionMethod.Linear);

            var x = points.Select(p => p.InjectionNumber).ToArray();
            var y = points.Select(p => p.Heat.Value).ToArray();
            var fit = Fit.Line(x, y);

            var intercept = fit.A;
            var slope = fit.B;
            var residualSd = EstimateResidualSd(points, t => intercept + slope * t, 2);

            return new BufferSubtractionModel(
                BufferSubtractionMethod.Linear,
                (double injectionNumber, out FloatWithError heat) =>
                {
                    heat = new FloatWithError(intercept + slope * injectionNumber, residualSd);
                    return true;
                });
        }

        static BufferSubtractionModel BuildExponentialDecayModel(ExperimentData referenceExperiment)
        {
            // Fits use included, integrated buffer injections only.
            var points = GetValidReferencePoints(referenceExperiment).ToList();
            if (points.Count < 3) return BufferSubtractionModel.Empty(BufferSubtractionMethod.ExponentialDecay);

            var x = points.Select(p => p.InjectionNumber).ToArray();
            var y = points.Select(p => p.Heat.Value).ToArray();

            try
            {
                var offsetGuess = y.Last();
                var amplitudeGuess = y.First() - offsetGuess;
                var logRateGuess = Math.Log(0.1);

                var fit = Fit.Curve(
                    x,
                    y,
                    (offset, amplitude, logRate, injectionNumber) =>
                        EvaluateExponentialDecay(offset, amplitude, logRate, injectionNumber),
                    offsetGuess,
                    amplitudeGuess,
                    logRateGuess);

                var offset = fit.P0;
                var amplitude = fit.P1;
                var logRate = fit.P2;
                var residualSd = EstimateResidualSd(points, t => EvaluateExponentialDecay(offset, amplitude, logRate, t), 3);

                return new BufferSubtractionModel(
                    BufferSubtractionMethod.ExponentialDecay,
                    (double injectionNumber, out FloatWithError heat) =>
                    {
                        heat = new FloatWithError(EvaluateExponentialDecay(offset, amplitude, logRate, injectionNumber), residualSd);
                        return true;
                    });
            }
            catch
            {
                // Fit failure just disables subtraction for this model; the UI remains usable.
                return BufferSubtractionModel.Empty(BufferSubtractionMethod.ExponentialDecay);
            }
        }

        static bool TryGetMatchedReferenceHeat(ExperimentData referenceExperiment, double injectionNumber, out FloatWithError heat)
        {
            heat = default;

            if (referenceExperiment == null || referenceExperiment.InjectionCount == 0) return false;

            var idx = FWEMath.Clamp((int)Math.Round(injectionNumber) - 1, 0, referenceExperiment.InjectionCount - 1);
            var same = referenceExperiment.Injections[idx];
            if (IsValidReferenceInjection(same))
            {
                heat = same.RawPeakArea;
                return true;
            }

            InjectionData previous = null;
            for (int i = idx - 1; i >= 0; i--)
            {
                var injection = referenceExperiment.Injections[i];
                if (IsValidReferenceInjection(injection))
                {
                    previous = injection;
                    break;
                }
            }

            InjectionData next = null;
            for (int i = idx + 1; i < referenceExperiment.InjectionCount; i++)
            {
                var injection = referenceExperiment.Injections[i];
                if (IsValidReferenceInjection(injection))
                {
                    next = injection;
                    break;
                }
            }

            if (previous != null && next != null)
            {
                heat = FWEMath.Average(previous.RawPeakArea, next.RawPeakArea);
                return true;
            }

            if (previous != null)
            {
                heat = previous.RawPeakArea;
                return true;
            }

            if (next != null)
            {
                heat = next.RawPeakArea;
                return true;
            }

            return false;
        }

        static IEnumerable<BufferSubtractionReferencePoint> GetValidReferencePoints(ExperimentData referenceExperiment)
        {
            return referenceExperiment?.Injections
                ?.Where(IsValidReferenceInjection)
                .Select(injection => new BufferSubtractionReferencePoint(GetInjectionNumber(injection), injection.RawPeakArea))
                ?? Enumerable.Empty<BufferSubtractionReferencePoint>();
        }

        static bool IsValidReferenceInjection(InjectionData injection)
        {
            return injection != null && injection.Include && injection.IsIntegrated;
        }

        static double GetInjectionNumber(InjectionData injection)
        {
            return injection.ID + 1;
        }

        static double EstimateResidualSd(List<BufferSubtractionReferencePoint> points, Func<double, double> evaluate, int parameterCount)
        {
            // Crude uncertainty estimate propagated with fitted buffer heats.
            if (points.Count <= parameterCount) return 0;

            var sum = points.Sum(p =>
            {
                var residual = p.Heat.Value - evaluate(p.InjectionNumber);
                return residual * residual;
            });

            return Math.Sqrt(sum / (points.Count - parameterCount));
        }

        static double EvaluateExponentialDecay(double offset, double amplitude, double logRate, double injectionNumber)
        {
            var rate = Math.Exp(logRate);
            return offset + amplitude * Math.Exp(-rate * injectionNumber);
        }

        sealed class BufferSubtractionReferencePoint
        {
            public double InjectionNumber { get; }
            public FloatWithError Heat { get; }

            public BufferSubtractionReferencePoint(double injectionNumber, FloatWithError heat)
            {
                InjectionNumber = injectionNumber;
                Heat = heat;
            }
        }
    }
}
