using System;
using System.Linq;
using System.Collections.Generic;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Analysis
{
    /// <summary>Runs the automatic Null model fit after a primary analysis.</summary>
    internal static class NullModelComparisonCalculator
    {
        internal static NullModelComparison Failure(string bindingReason, string nullReason)
            => new NullModelComparison
            {
                NullModelId = "offset",
                BindingFitSucceeded = false,
                BindingFitReason = bindingReason ?? "Binding optimization did not converge.",
                NullFitSucceeded = false,
                NullFitReason = nullReason ?? "Null model comparison could not be calculated.",
                ComparisonUnavailableReason = nullReason ?? "Null model comparison could not be calculated.",
            };

        internal static NullModelComparison Failure(SolutionInterface binding, Exception exception)
            => Failure(binding?.Convergence?.Success == true,
                binding?.Convergence?.FailureReason, exception?.Message, binding);

        internal static NullModelComparison Failure(GlobalSolution binding, Exception exception)
            => Failure(binding?.Convergence?.Success == true,
                binding?.Convergence?.FailureReason, exception?.Message, binding);

        static NullModelComparison Failure(bool bindingSucceeded, string bindingReason, string nullReason,
            object solution)
        {
            var comparison = new NullModelComparison
            {
                NullModelId = "offset",
                BindingFitSucceeded = bindingSucceeded,
                BindingFitReason = bindingSucceeded ? string.Empty : string.IsNullOrWhiteSpace(bindingReason)
                    ? "Binding optimization did not converge." : bindingReason,
                NullFitSucceeded = false,
                NullFitReason = nullReason ?? "Null model comparison could not be calculated.",
                ComparisonUnavailableReason = nullReason ?? "Null model comparison could not be calculated.",
            };
            if (bindingSucceeded)
            {
                try
                {
                    comparison.BindingInformationCriteria = solution is GlobalSolution global
                        ? FitInformationCriteriaCalculator.Calculate(global)
                        : FitInformationCriteriaCalculator.Calculate((SolutionInterface)solution);
                }
                catch { }
            }
            return comparison;
        }

        public static void Calculate(SolutionInterface binding, bool weighted, SolverAlgorithm algorithm, int maxIterations, double toleranceModifier)
        {
            if (binding?.Model == null || binding.Model.IsNullModel) return;
            if (SolverInterface.TerminateAnalysisFlag.Up) return;

            var model = binding.Model;
            var comparison = new NullModelComparison
            {
                NullModelId = "offset",
                BindingFitSucceeded = binding.Convergence?.Success == true,
                BindingFitReason = binding.Convergence?.FailureReason ?? string.Empty,
            };
            if (comparison.BindingFitSucceeded)
                comparison.BindingInformationCriteria = FitInformationCriteriaCalculator.Calculate(binding);
            else
                comparison.BindingFitReason = string.IsNullOrWhiteSpace(comparison.BindingFitReason)
                    ? "Binding optimization did not converge." : comparison.BindingFitReason;

            try
            {
                if (model.NumberOfPoints < 1)
                    throw new InvalidOperationException("No included observations are available for the Null model fit.");
                var nullModel = CreateOffset(model);
                ValidateOffsetInput(nullModel);
                ReleaseOffset(nullModel.Parameters.Table[ParameterType.Offset], nullModel.Data.Injections,
                    "The Offset cannot be estimated because every included injection amount is zero.");
                var fit = FitSingle(nullModel, weighted, algorithm, maxIterations, toleranceModifier);
                if (fit.Solution != null) comparison.NullSolutions.Add(fit.Solution);
                FillMember(comparison, fit.Model, "local");
                comparison.NullFitSucceeded = fit.Convergence?.Success == true;
                comparison.NullFitReason = comparison.NullFitSucceeded ? string.Empty
                    : fit.Convergence?.FailureReason ?? "Null model optimization did not converge.";
                if (comparison.NullFitSucceeded)
                    comparison.NullInformationCriteria = FitInformationCriteriaCalculator.Calculate(fit.Solution);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                comparison.NullFitSucceeded = false;
                comparison.NullFitReason = ex.Message;
            }

            FinishComparison(comparison);
            binding.NullComparison = comparison;
        }

        public static void Calculate(GlobalSolution binding, bool weighted, SolverAlgorithm algorithm, int maxIterations, double toleranceModifier)
        {
            if (binding?.Model?.Models == null || binding.Model.Models.Count == 0
                || binding.Model.Models.Any(model => model.IsNullModel)) return;
            if (SolverInterface.TerminateAnalysisFlag.Up) return;

            var comparison = new NullModelComparison
            {
                NullModelId = "offset",
                BindingFitSucceeded = binding.Convergence?.Success == true,
                BindingFitReason = binding.Convergence?.FailureReason ?? string.Empty,
            };
            if (comparison.BindingFitSucceeded)
                comparison.BindingInformationCriteria = FitInformationCriteriaCalculator.Calculate(binding);
            else
                comparison.BindingFitReason = string.IsNullOrWhiteSpace(comparison.BindingFitReason)
                    ? "Binding optimization did not converge." : comparison.BindingFitReason;

            try
            {
                var sourceModels = binding.Model.Models;
                var independent = sourceModels.Count >= 2 && binding.Model.ShouldFitIndividually;
                var nullModels = new List<Model>();
                var memberComparisons = new List<NullModelComparison>();
                var allNullFitsAvailable = true;
                foreach (var source in sourceModels)
                {
                    if (SolverInterface.TerminateAnalysisFlag.Up) return;
                    var memberComparison = new NullModelComparison
                    {
                        NullModelId = "offset",
                        IsIndependentMemberComparison = independent,
                        BindingFitSucceeded = source.Solution?.Convergence?.Success == true,
                        BindingFitReason = source.Solution?.Convergence?.FailureReason ?? string.Empty,
                    };
                    if (!memberComparison.BindingFitSucceeded)
                        memberComparison.BindingFitReason = string.IsNullOrWhiteSpace(memberComparison.BindingFitReason)
                            ? "Binding optimization did not converge." : memberComparison.BindingFitReason;

                    try
                    {
                        if (memberComparison.BindingFitSucceeded)
                            memberComparison.BindingInformationCriteria = Calculate(source.Solution, weighted);
                        if (source.NumberOfPoints < 1)
                            throw new InvalidOperationException("No included observations are available for the Null model fit.");
                        var nullModel = CreateOffset(source);
                        ValidateOffsetInput(nullModel);
                        ReleaseOffset(nullModel.Parameters.Table[ParameterType.Offset], nullModel.Data.Injections,
                            "The Offset cannot be estimated because every included injection amount is zero.");
                        var fit = FitSingle(nullModel, weighted, algorithm, maxIterations, toleranceModifier);
                        if (fit.Solution != null)
                        {
                            nullModels.Add(nullModel);
                            memberComparison.NullSolutions.Add(fit.Solution);
                        }
                        FillMember(memberComparison, nullModel, "local");
                        memberComparison.NullFitSucceeded = fit.Convergence?.Success == true && fit.Solution != null;
                        memberComparison.NullFitReason = memberComparison.NullFitSucceeded ? string.Empty
                            : fit.Convergence?.FailureReason ?? "Null model optimization did not converge.";
                        if (memberComparison.NullFitSucceeded)
                            memberComparison.NullInformationCriteria = FitInformationCriteriaCalculator.Calculate(fit.Solution);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        memberComparison.NullFitSucceeded = false;
                        memberComparison.NullFitReason = ex.Message;
                    }

                    FinishComparison(memberComparison);
                    memberComparisons.Add(memberComparison);
                    allNullFitsAvailable &= memberComparison.NullFitSucceeded;
                }

                if (independent)
                {
                    for (var index = 0; index < sourceModels.Count; index++)
                    {
                        sourceModels[index].Solution.NullComparison = memberComparisons[index];
                    }
                }

                comparison.NullSolutions = nullModels.Select(model => model.Solution).Where(solution => solution != null).ToList();
                comparison.Members = memberComparisons.SelectMany(value => value.Members).ToList();
                comparison.NullFitSucceeded = allNullFitsAvailable && nullModels.Count == sourceModels.Count;
                comparison.NullFitReason = comparison.NullFitSucceeded ? string.Empty
                    : memberComparisons.FirstOrDefault(value => !value.NullFitSucceeded)?.NullFitReason
                        ?? memberComparisons.FirstOrDefault(value => value.NullInformationCriteria?.IsAiccAvailable != true)?.NullInformationCriteria?.AiccUnavailableReason
                        ?? "Null model comparison is unavailable for one or more members.";

                if (comparison.NullFitSucceeded)
                {
                    // This is diagnostic pooled evidence only. Every Offset was fitted locally above;
                    // the pooled likelihood uses all observations and the total local parameter count.
                    var nullGlobal = new GlobalModel(nullModels)
                    {
                        ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
                        Parameters = new GlobalModelParameters(),
                    };
                    foreach (var model in nullModels) nullGlobal.Parameters.AddIndivdualParameter(model.Parameters);
                    var pooled = GaussianLikelihoodEvaluator.Evaluate(nullGlobal,
                        weighted ? GaussianLikelihoodMode.EstimatedWeightedVariance : GaussianLikelihoodMode.EstimatedCommonVariance);
                    comparison.NullInformationCriteria = FitInformationCriteriaCalculator.Calculate(pooled,
                        nullModels.Sum(model => model.NumberOfParameters));
                }

                if (!independent)
                    for (var index = 0; index < sourceModels.Count; index++)
                        sourceModels[index].Solution.NullComparison = comparison;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                comparison.NullFitSucceeded = false;
                comparison.NullFitReason = ex.Message;
            }

            FinishComparison(comparison);
            binding.NullComparison = comparison;
            if (binding.Model.Models.Count < 2 || !binding.Model.ShouldFitIndividually)
                foreach (var member in binding.Solutions) member.NullComparison = comparison;
        }

        static Model CreateOffset(Model source)
        {
            var offset = new Offset(source.Data);
            offset.InitializeParameters(source.Data);
            offset.HeatMethod = source.HeatMethod;
            offset.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            // Only the starting value is reused. The null Offset is always fitted and never
            // inherits a lock or limits from the binding model.
            if (source.Parameters.Table.TryGetValue(ParameterType.Offset, out var original))
                offset.Parameters.Table[ParameterType.Offset].SetValue(original.Value, false);
            return offset;
        }

        static void ReleaseOffset(Parameter parameter, IEnumerable<InjectionData> injections, string noAmountReason)
        {
            var limits = UnreachableOffsetLimits(injections) ?? throw new InvalidOperationException(noAmountReason);
            parameter.SetLimits(limits);
            parameter.SetValue(Clamp(parameter.Value, limits), false);
        }

        /// <summary>
        /// The least-squares Offset is a (weighted) mean of the included molar heats q/m, so it always
        /// lies within their range. Solvers need finite bounds; these lie outside that range and can
        /// never be reached, which leaves the Offset effectively unbounded.
        /// </summary>
        internal static double[] UnreachableOffsetLimits(IEnumerable<InjectionData> injections)
        {
            var molarHeats = injections
                .Where(injection => injection.Include && injection.InjectionMass > 0)
                .Select(injection => injection.PeakArea.Value / injection.InjectionMass)
                .Where(FWEMath.IsFinite)
                .ToList();
            if (molarHeats.Count == 0) return null;
            var min = molarHeats.Min();
            var max = molarHeats.Max();
            var margin = Math.Max(1.0, Math.Max(max - min, Math.Max(Math.Abs(min), Math.Abs(max))));
            return new[] { min - margin, max + margin };
        }

        static double Clamp(double value, double[] limits)
            => FWEMath.IsFinite(value) ? Math.Min(Math.Max(value, limits[0]), limits[1]) : (limits[0] + limits[1]) / 2;

        static void ValidateOffsetInput(Model model)
        {
            foreach (var injection in model.Data.Injections.Where(injection => injection.Include))
            {
                if (!FWEMath.IsFinite(injection.InjectionMass) || injection.InjectionMass < 0
                    || !FWEMath.IsFinite(injection.PeakArea))
                    throw new InvalidOperationException("Included observations contain a non-finite heat or injection amount.");
            }
        }

        static (Model Model, SolutionInterface Solution, SolverConvergence Convergence) FitSingle(Model model, bool weighted,
            SolverAlgorithm algorithm, int maxIterations, double toleranceModifier)
        {
            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = algorithm,
                MaxOptimizerIterations = maxIterations,
                SolverToleranceModifier = toleranceModifier,
                UseErrorWeightedFitting = weighted,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                CanCreateAnalysisResult = false,
                CanReportAnalysisStepFinished = false,
                Silent = true,
            };
            var convergence = solver.Solve();
            return (model, model.Solution, convergence);
        }

        static FitInformationCriteria Calculate(SolutionInterface solution, bool weighted)
        {
            var mode = weighted ? GaussianLikelihoodMode.EstimatedWeightedVariance
                : GaussianLikelihoodMode.EstimatedCommonVariance;
            var likelihood = GaussianLikelihoodEvaluator.Evaluate(solution.Model, mode);
            return FitInformationCriteriaCalculator.Calculate(likelihood, solution.Model.NumberOfParameters);
        }

        static void FillMember(NullModelComparison comparison, Model model, string scope, Parameter sharedParameter = null)
        {
            var parameter = sharedParameter ?? model.Parameters.Table[ParameterType.Offset];
            var member = new NullModelComparisonMember
            {
                ExperimentId = model.Data.UniqueID,
                Offset = parameter.Value,
                Scope = scope,
                Convergence = model.Solution?.Convergence?.ToSnapshot(),
            };
            foreach (var injection in model.Data.Injections)
            {
                if (!FWEMath.IsFinite(injection.InjectionMass) || !FWEMath.IsFinite(injection.PeakArea)
                    || !FWEMath.IsFinite(injection.Ratio)) continue;
                var predicted = model.Evaluate(injection.ID, withoffset: true);
                if (!FWEMath.IsFinite(predicted)) continue;
                member.Points.Add(new NullModelComparisonPoint
                {
                    InjectionId = injection.ID,
                    InjectionMass = injection.InjectionMass,
                    Ratio = injection.Ratio,
                    ObservedHeatJoules = injection.PeakArea,
                    PredictedHeatJoules = predicted,
                    Included = injection.Include,
                });
            }
            comparison.Members.Add(member);
        }

        static void FinishComparison(NullModelComparison comparison)
        {
            var binding = comparison.BindingInformationCriteria;
            var nullModel = comparison.NullInformationCriteria;
            if (!comparison.BindingFitSucceeded)
                comparison.ComparisonUnavailableReason = "The binding fit did not converge.";
            else if (!comparison.NullFitSucceeded)
                comparison.ComparisonUnavailableReason = "The Null model fit did not converge.";
            else if (binding?.IsAiccAvailable != true)
                comparison.ComparisonUnavailableReason = binding?.AiccUnavailableReason ?? "Binding AICc is unavailable.";
            else if (nullModel?.IsAiccAvailable != true)
                comparison.ComparisonUnavailableReason = nullModel?.AiccUnavailableReason ?? "Null model AICc is unavailable.";
            else if (binding.ObservationCount != nullModel.ObservationCount
                || binding.LikelihoodMode != nullModel.LikelihoodMode)
                comparison.ComparisonUnavailableReason = "The fits do not use the same observations and likelihood convention.";
            else
                comparison.DeltaAicc = nullModel.Aicc.Value - binding.Aicc.Value;
        }
    }
}
