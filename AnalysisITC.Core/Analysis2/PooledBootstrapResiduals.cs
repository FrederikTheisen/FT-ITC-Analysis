using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using MathNet.Numerics.Statistics;

namespace AnalysisITC.Core.Analysis
{
    public enum PooledBootstrapScale
    {
        InjectionHeatSd,
        ExperimentResidualSd,
        ZeroResiduals,
    }

    // Prepared from the primary fit before starting any parallel refits.
    internal sealed class PooledBootstrapResiduals
    {
        readonly Dictionary<InjectionData, (double Prediction, double Scale)> targets;
        readonly double[] pool;

        internal PooledBootstrapScale ScaleSource { get; }

        PooledBootstrapResiduals(PooledBootstrapScale source,
            Dictionary<InjectionData, (double Prediction, double Scale)> targets, double[] pool)
        {
            ScaleSource = source;
            this.targets = targets;
            this.pool = pool;
        }

        internal static PooledBootstrapResiduals Prepare(GlobalModel model)
        {
            var source = model.HasCompleteInjectionHeatSds
                ? PooledBootstrapScale.InjectionHeatSd : PooledBootstrapScale.ExperimentResidualSd;
            var targets = new Dictionary<InjectionData, (double Prediction, double Scale)>();
            var standardized = new List<double>();
            bool allZero = true;

            foreach (var member in model.Models)
            {
                var points = member.Data.Injections.Where(injection => injection.Include)
                    .Select(injection =>
                    {
                        var prediction = member.Evaluate(injection.ID, withoffset: true);
                        return (Injection: injection, Prediction: prediction, Residual: injection.PeakArea.Value - prediction);
                    }).ToArray();
                if (points.Length == 0 || points.Any(point => !FWEMath.IsFinite(point.Residual)))
                    throw new InvalidOperationException($"'{member.Data.FileName}' needs finite residuals at included injections.");

                bool zeroMember = points.All(point => point.Residual == 0);
                allZero &= zeroMember;
                double experimentScale = 0;
                if (source == PooledBootstrapScale.ExperimentResidualSd && !zeroMember)
                {
                    experimentScale = points.Select(point => point.Residual).StandardDeviation();
                    if (!FWEMath.IsFinite(experimentScale) || experimentScale <= 0)
                        throw new InvalidOperationException($"Cannot estimate a positive residual SD for '{member.Data.FileName}'.");
                }

                foreach (var point in points)
                {
                    double scale = source == PooledBootstrapScale.InjectionHeatSd
                        ? point.Injection.PeakAreaError : experimentScale;
                    targets.Add(point.Injection, (point.Prediction, scale));
                    if (scale > 0)
                    {
                        double residual = point.Residual / scale;
                        if (!FWEMath.IsFinite(residual))
                            throw new InvalidOperationException($"Cannot standardize residuals for '{member.Data.FileName}'.");
                        standardized.Add(residual);
                    }
                }
            }

            int n = targets.Count;
            int p = model.NumberOfParameters;
            if (n <= p)
                throw new InvalidOperationException("Residual bootstrap requires more included injections than fitted parameters.");
            if (allZero)
                return new PooledBootstrapResiduals(PooledBootstrapScale.ZeroResiduals, targets, Array.Empty<double>());

            double mean = standardized.Average();
            double correction = Math.Sqrt((double)n / (n - p));
            var pool = standardized.Select(residual => (residual - mean) * correction).ToArray();
            if (pool.Any(residual => !FWEMath.IsFinite(residual)))
                throw new InvalidOperationException("The centered pooled residuals are not finite.");
            return new PooledBootstrapResiduals(source, targets, pool);
        }

        internal double SampleHeat(InjectionData injection, Random random)
        {
            var (prediction, scale) = targets[injection];
            if (scale == 0 || pool.Length == 0) return prediction;
            return prediction + scale * pool[random.Next(pool.Length)];
        }
    }
}
