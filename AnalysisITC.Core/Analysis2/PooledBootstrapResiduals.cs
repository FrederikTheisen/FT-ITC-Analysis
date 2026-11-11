using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Analysis
{
    // Prepared from the primary fit before starting any parallel refits.
    // Each experiment's residuals are standardized as in the within-experiment bootstrap
    // (divided by the heat SD only for weighted fits), centred on the experiment's own mean
    // and scaled to unit RMS before pooling. The pool therefore shares only the shape of the
    // noise; each target keeps its own experiment's noise size, its fitted-parameter
    // correction and, for weighted fits, its SD.
    internal sealed class PooledBootstrapResiduals
    {
        readonly Dictionary<InjectionData, (double Prediction, double Scale)> targets;
        readonly double[] pool;

        PooledBootstrapResiduals(Dictionary<InjectionData, (double Prediction, double Scale)> targets, double[] pool)
        {
            this.targets = targets;
            this.pool = pool;
        }

        internal static PooledBootstrapResiduals Prepare(GlobalModel model, bool weighted)
        {
            var targets = new Dictionary<InjectionData, (double Prediction, double Scale)>();
            var unitResiduals = new List<double>();
            int totalPoints = model.GetNumberOfPoints();

            foreach (var member in model.Models)
            {
                var included = member.Data.Injections.Where(injection => injection.Include).ToList();
                double correction = member.ResidualBootstrapScale(included.Count, model.Parameters, totalPoints);

                var points = included.Select(injection =>
                {
                    var prediction = member.Evaluate(injection.ID, withoffset: true);
                    var weight = weighted ? Models.Model.GetSigmaForWeighting(injection, included) : 1.0;
                    return (Injection: injection, Prediction: prediction, Weight: weight,
                        Standardized: (injection.PeakArea.Value - prediction) / weight);
                }).ToArray();
                if (points.Any(point => !FWEMath.IsFinite(point.Prediction) || !FWEMath.IsFinite(point.Standardized)))
                    throw new InvalidOperationException($"'{member.Data.FileName}' needs finite residuals at included injections.");

                // Subtract a reference first so identical residuals stay exactly zero.
                var reference = points[0].Standardized;
                var centred = points.Select(point => point.Standardized - reference).ToArray();
                var centre = centred.Average();
                for (int i = 0; i < centred.Length; i++)
                    centred[i] -= centre;
                var rms = Math.Sqrt(centred.Sum(value => value * value) / centred.Length);
                if (!FWEMath.IsFinite(rms))
                    throw new InvalidOperationException($"Cannot scale residuals for '{member.Data.FileName}'.");

                // An experiment without residual spread receives no synthetic noise, as in the
                // within-experiment bootstrap, and contributes nothing to the shared pool.
                for (int i = 0; i < points.Length; i++)
                {
                    targets.Add(points[i].Injection, (points[i].Prediction, rms > 0 ? points[i].Weight * rms * correction : 0));
                    if (rms > 0) unitResiduals.Add(centred[i] / rms);
                }
            }

            return new PooledBootstrapResiduals(targets, unitResiduals.ToArray());
        }

        internal double SampleHeat(InjectionData injection, Random random)
        {
            var (prediction, scale) = targets[injection];
            if (scale == 0 || pool.Length == 0) return prediction;
            return prediction + scale * pool[random.Next(pool.Length)];
        }
    }
}
