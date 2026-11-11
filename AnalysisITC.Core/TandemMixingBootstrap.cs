using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Math.Convergence;
using Accord.Math.Optimization;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Application;

namespace AnalysisITC.Core.Processing
{
    internal static class TandemMixingBootstrap
    {
        internal static bool IsEligible(ExperimentData experiment) => TryGetSegments(experiment, out _);

        static bool TryGetSegments(ExperimentData experiment, out TandemConcatenation.TandemInjectionSegment[] ranges)
        {
            ranges = null;
            var info = experiment?.TandemMergeInfo;
            var segments = experiment?.Segments;
            if (info == null || segments == null || segments.Count != info.Fractions.Count + 1
                || segments[0].FirstInjectionID != 0
                || (info.Criterion == TandemMixingCriterion.ModelFree && info.PointsPerSide != TandemContinuityScanner.PointsPerSide))
                return false;

            var result = new TandemConcatenation.TandemInjectionSegment[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                int start = segments[i].FirstInjectionID;
                int end = i + 1 < segments.Count ? segments[i + 1].FirstInjectionID : experiment.InjectionCount;
                if (end <= start || end > experiment.InjectionCount) return false;
                result[i] = new TandemConcatenation.TandemInjectionSegment(start, end - start);
            }
            ranges = result;
            return true;
        }

        static TandemConcatenation.BackMixingSettings Settings(TandemMergeInfo info) =>
            new TandemConcatenation.BackMixingSettings
            {
                UseBackMixingMethod = true,
                DeadVolume = info.DeadVolume,
                DidRemoveOverflow = info.DidRemoveOverflow,
                RemoveOverflowVolume = info.RemoveOverflowVolume,
            };

        internal static void Prepare(ExperimentData observed, ModelCloneOptions options)
        {
            if (options?.EffectiveReestimateTandemMixing != true) return;
            options.OneSiteMixingSeed = observed.TandemMergeInfo?.Criterion == TandemMixingCriterion.OneSiteFit
                ? PrepareOneSite(observed) : null;
        }

        internal static OneSiteSeed PrepareOneSite(ExperimentData observed)
        {
            if (!TryGetSegments(observed, out var ranges))
                throw new InvalidOperationException("The saved tandem layout cannot be used to estimate mixing.");
            var info = observed.TandemMergeInfo;
            var scratch = observed.GetSynthClone(new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None }, new Random(0));
            TandemConcatenation.ProcessInjectionsWithBackMixingModel(scratch, ranges, Settings(info), info.Fractions, info.Bookkeeping);
            scratch.HeatMethod = observed.HeatMethod;
            var model = TandemMixingScanner.CreateOneSiteMergeModel(scratch);
            var convergence = TandemMixingScanner.SolveOneSiteMergeModel(model);
            if (SolverInterface.TerminateAnalysisFlag.Up) return null;
            if (convergence.Failed || convergence.Stopped || !TandemMixingUncertainty.Finite(convergence.UnweightedRmsd))
                throw new InvalidOperationException($"The one-site mixing seed could not be fitted for '{observed.Name}'.");
            return new OneSiteSeed(model.Parameters.GetFittedParameters(), info.Fractions);
        }

        internal static void Reestimate(ExperimentData clone, OneSiteSeed seed)
        {
            if (!TryGetSegments(clone, out var ranges))
                throw new InvalidOperationException("The saved tandem layout cannot be used to estimate mixing.");
            var info = clone.TandemMergeInfo;
            var settings = Settings(info);
            var heatMethod = clone.HeatMethod;
            var fractions = info.Criterion == TandemMixingCriterion.ModelFree
                ? TandemContinuityScanner.FindBest(clone, ranges, settings, null, info.Bookkeeping, logResults: false)?.TransitionMixingFractions
                : seed?.FindBest(clone, ranges, settings)?.TransitionMixingFractions;
            if (SolverInterface.TerminateAnalysisFlag.Up) throw new OptimizerStopException();
            if (fractions == null)
                throw new InvalidOperationException($"Tandem mixing could not be re-estimated for '{clone.Name}'.");
            TandemConcatenation.ProcessInjectionsWithBackMixingModel(clone, ranges, settings, fractions, info.Bookkeeping);
            clone.HeatMethod = heatMethod;
        }

        internal sealed class OneSiteSeed
        {
            readonly double[] start;
            readonly double[] scales;
            readonly double[] lower;
            readonly double[] upper;

            internal OneSiteSeed(Parameter[] parameters, IReadOnlyList<double> fractions)
            {
                scales = parameters.Select(parameter => parameter.Key switch
                {
                    ParameterType.Enthalpy1 => 40000.0,
                    ParameterType.Offset => 1000.0,
                    _ => 1.0,
                }).Concat(Enumerable.Repeat(1.0, fractions.Count)).ToArray();
                start = parameters.Select(parameter => parameter.Value).Concat(fractions)
                    .Select((value, index) => value / scales[index]).ToArray();
                lower = parameters.Select((parameter, index) => parameter.Limits[0] / scales[index])
                    .Concat(Enumerable.Repeat(0.0, fractions.Count)).ToArray();
                upper = parameters.Select((parameter, index) => parameter.Limits[1] / scales[index])
                    .Concat(Enumerable.Repeat(1.0, fractions.Count)).ToArray();
            }

            internal TandemMixingScanPoint FindBest(
                ExperimentData scratch,
                IList<TandemConcatenation.TandemInjectionSegment> ranges,
                TandemConcatenation.BackMixingSettings settings,
                int evaluationLimit = 4000)
            {
                var method = scratch.TandemMergeInfo.Bookkeeping;
                var heatMethod = scratch.HeatMethod;
                var coordinates = new double[4];
                var fractions = start.Skip(coordinates.Length).ToArray();
                TandemConcatenation.ProcessInjectionsWithBackMixingModel(scratch, ranges, settings, fractions, method);
                scratch.HeatMethod = heatMethod;
                var model = TandemMixingScanner.CreateOneSiteMergeModel(scratch);
                var best = (double[])start.Clone();
                var bestLoss = double.PositiveInfinity;
                int evaluations = 0;
                var objective = new NonlinearObjectiveFunction(start.Length, trial =>
                {
                    if (SolverInterface.TerminateAnalysisFlag.Up) throw new OptimizerStopException();
                    evaluations++;
                    for (int i = 0; i < coordinates.Length; i++) coordinates[i] = trial[i] * scales[i];
                    for (int i = 0; i < fractions.Length; i++) fractions[i] = trial[coordinates.Length + i];
                    TandemConcatenation.ProcessInjectionsWithBackMixingModel(scratch, ranges, settings, fractions, method);
                    if (!model.TryLossFunction(coordinates, errorweighted: false, out var loss))
                        return double.MaxValue;
                    // Work in squared microjoules so the stopping tolerance is at the heat scale.
                    loss *= 1e12;
                    if (loss < bestLoss)
                    {
                        bestLoss = loss;
                        Array.Copy(trial, best, best.Length);
                    }
                    return loss;
                });

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    var optimizer = new NelderMead(objective)
                    {
                        Convergence = new GeneralConvergence(start.Length)
                        {
                            MaximumEvaluations = evaluationLimit,
                            AbsoluteFunctionTolerance = 1e-10,
                            RelativeParameterTolerance = 1e-8,
                            StartTime = DateTime.Now,
                        },
                    };
                    for (int i = 0; i < start.Length; i++)
                    {
                        optimizer.LowerBounds[i] = lower[i];
                        optimizer.UpperBounds[i] = upper[i];
                        var step = Math.Min(attempt == 0 ? .03 : .01, (upper[i] - lower[i]) / 2);
                        optimizer.StepSize[i] = best[i] - lower[i] <= upper[i] - best[i] ? step : -step;
                    }
                    optimizer.Minimize((double[])best.Clone());
                    var status = optimizer.Status;
                    if (!TandemMixingUncertainty.Finite(bestLoss)
                        || !(status == NelderMeadStatus.Success || status == NelderMeadStatus.FunctionToleranceReached
                            || status == NelderMeadStatus.SolutionToleranceReached || status == NelderMeadStatus.MinimumAllowedValueReached))
                        continue;

                    var values = model.Parameters.GetFittedParameters()
                        .Select((parameter, index) => (parameter.Key, Value: best[index] * scales[index]))
                        .ToDictionary(parameter => parameter.Key, parameter => parameter.Value);
                    return new TandemMixingScanPoint(best.Skip(coordinates.Length).ToArray(),
                        Math.Sqrt(bestLoss / model.NumberOfPoints), values[ParameterType.Nvalue1],
                        values[ParameterType.Affinity1], values[ParameterType.Enthalpy1], values[ParameterType.Offset],
                        status.ToString(), evaluations);
                }
                return null;
            }
        }
    }
}
