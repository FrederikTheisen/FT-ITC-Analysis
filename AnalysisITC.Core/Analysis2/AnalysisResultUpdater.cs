using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis.Models;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Analysis
{
    public static class AnalysisResultUpdater
    {
        public static async Task<SolverConvergence> UpdateAsync(AnalysisResult result)
            => await UpdateAsync(result, AnalysisResultUpdateOptions.StoredSettings);

        public static async Task<SolverConvergence> UpdateAsync(
            AnalysisResult result,
            AnalysisResultUpdateOptions options)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var solver = PrepareSolver(result, options);
            var convergence = await RunSolverAsync(solver);
            var solution = GetGlobalSolution(solver);

            if (solver.ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood)
            {
                // Profile estimation is an optional diagnostic layered on the
                // successful primary fit. Stage it and apply the replacement
                // policy only after the complete run has finished.
                if (solution != null && ShouldReplaceProfileResult(result, convergence, solution))
                {
                    result.UpdateSolution(solution);
                    DataManager.LoadResultSolutionsToExperiments(result);
                }
                else if (convergence != null)
                {
                    convergence.AppendErrorEstimationSummary(ProfileRetentionReason(result, convergence, solution));
                }
                return convergence;
            }

            EnsureUpdateCanReplaceResult(solver, convergence, solution);

            result.UpdateSolution(solution);
            DataManager.LoadResultSolutionsToExperiments(result);

            return convergence;
        }

        static string ProfileRetentionReason(AnalysisResult existing, SolverConvergence convergence, GlobalSolution candidate)
        {
            if (convergence == null || convergence.Failed || convergence.Stopped)
                return "Profile result retained: the primary update failed or was cancelled.";
            if (candidate == null || !candidate.IsValid)
                return "Profile result retained: no valid replacement solution was produced.";
            var outcome = AggregateProfileOutcome(candidate) ?? convergence.ErrorEstimationOutcome;
            if (outcome == ErrorEstimationOutcome.Cancelled)
                return "Profile result retained: profiling was cancelled.";
            if (outcome == ErrorEstimationOutcome.CompleteFailure)
                return "Profile result retained: profiling produced no complete interval.";
            if (outcome == ErrorEstimationOutcome.NotRun)
                return "Profile result retained: profiling was not run.";
            var oldOutcome = AggregateProfileOutcome(existing?.Solution);
            var oldCount = CompleteProfileCoordinates(existing?.Solution);
            var candidateCount = CompleteProfileCoordinates(candidate);
            return $"Profile result retained: partial profiling found {candidateCount} complete coordinate interval(s), while the existing result has {oldCount} ({oldOutcome?.ToString() ?? "no profile"}).";
        }

        static bool ShouldReplaceProfileResult(AnalysisResult existing, SolverConvergence convergence, GlobalSolution candidate)
        {
            if (convergence == null || convergence.Failed || convergence.Stopped || candidate == null || !candidate.IsValid) return false;
            var candidateOutcome = AggregateProfileOutcome(candidate) ?? convergence.ErrorEstimationOutcome;
            var candidateCount = CompleteProfileCoordinates(candidate);
            var oldCount = CompleteProfileCoordinates(existing.Solution);
            var oldOutcome = AggregateProfileOutcome(existing.Solution);
            return ShouldReplaceProfileOutcome(!existing.IsValidForCurrentData, oldOutcome, oldCount,
                candidateOutcome, candidateCount);
        }

        /// <summary>Pure replacement policy used by the staged profile updater and its tests.</summary>
        internal static bool ShouldReplaceProfileOutcome(bool existingInvalid,
            ErrorEstimationOutcome? previousOutcome, int previousCompleteCount,
            ErrorEstimationOutcome candidateOutcome, int candidateCompleteCount)
            => candidateOutcome == ErrorEstimationOutcome.Completed
                || candidateOutcome == ErrorEstimationOutcome.PartialFailure
                    && ShouldReplacePartialProfile(existingInvalid, previousOutcome,
                        previousCompleteCount, candidateCompleteCount);

        internal static bool ShouldReplacePartialProfile(bool existingInvalid,
            ErrorEstimationOutcome? previousOutcome, int previousCompleteCount, int candidateCompleteCount)
            => existingInvalid
                ? candidateCompleteCount > 0
                : previousOutcome == ErrorEstimationOutcome.PartialFailure
                    && candidateCompleteCount >= previousCompleteCount;

        static int CompleteProfileCoordinates(GlobalSolution solution)
            => ProfileRuns(solution).Sum(run => run.Coordinates.Count(c => c.HasCompleteInterval));

        static IReadOnlyList<ProfileLikelihoodRunResult> ProfileRuns(GlobalSolution solution)
        {
            if (solution?.ProfileLikelihoodRun != null)
                return new[] { solution.ProfileLikelihoodRun };
            return (solution?.Solutions ?? new List<SolutionInterface>())
                .Select(member => member?.ProfileLikelihoodRun)
                .Where(run => run != null)
                .ToList();
        }

        static ErrorEstimationOutcome? AggregateProfileOutcome(GlobalSolution solution)
        {
            var isProfile = solution?.ProfileLikelihoodRun != null
                || solution?.ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood
                || solution?.Solutions?.Any(member => member?.ProfileLikelihoodRun != null
                    || member?.ErrorMethod == ErrorEstimationMethod.ProfileLikelihood) == true;
            return isProfile ? ProfileLikelihoodEstimator.Summarize(solution).Outcome : (ErrorEstimationOutcome?)null;
        }

        internal static void EnsureUpdateCanReplaceResult(
            SolverInterface solver,
            SolverConvergence convergence,
            GlobalSolution solution)
        {
            if (convergence == null)
                throw new InvalidOperationException("Analysis update finished without convergence information.");
            if (convergence.Failed || convergence.Stopped)
                throw new InvalidOperationException($"Analysis update did not produce a usable result: {convergence.Message}");
            if (convergence.ErrorEstimationOutcome == ErrorEstimationOutcome.Cancelled)
                throw new InvalidOperationException("Analysis update was cancelled during error estimation.");
            if (solution == null)
                throw new InvalidOperationException("Analysis update did not produce a solution.");

            if (solver.ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals
                && (convergence.ErrorEstimationOutcome == ErrorEstimationOutcome.NotRun
                    || convergence.ErrorEstimationOutcome == ErrorEstimationOutcome.CompleteFailure
                    || solution.BootstrapIterations == 0))
            {
                throw new InvalidOperationException("Analysis update did not produce any usable bootstrap refits.");
            }
        }

        public static SolverInterface PrepareSolver(AnalysisResult result)
            => PrepareSolver(result, AnalysisResultUpdateOptions.StoredSettings);

        public static SolverInterface PrepareSolver(
            AnalysisResult result,
            AnalysisResultUpdateOptions options)
        {
            if (result?.Solution?.Model == null)
                throw new InvalidOperationException("The selected analysis result has no stored solution.");

            options = options ?? AnalysisResultUpdateOptions.StoredSettings;
            ValidateOptions(result, options);

            var sourceSolution = result.Solution;
            var sourceModel = sourceSolution.Model;
            var data = ResolveResultExperiments(sourceModel);
            if (sourceSolution.UseWeightedFitting)
                AnalysisBuilder.ValidateErrorWeightedFitting(data);

            var factory = new GlobalModelFactory(sourceModel.ModelType);
            factory.InitializeModel(data);

            ApplyModelOptions(factory, sourceModel);
            ApplyConstraints(factory, sourceModel.Parameters);
            factory.InitializeGlobalParameters();
            ApplyGlobalParameters(factory, sourceModel.Parameters);
            ApplyIndividualParameters(factory.Model, sourceModel);

            factory.BuildModel();
            ApplyCloneOptions(factory.Model, sourceModel.ModelCloneOptions);

            var solver = SolverInterface.Initialize(factory.Model);
            solver.CanCreateAnalysisResult = false;
            solver.SolverAlgorithm = sourceSolution.Convergence?.Algorithm ?? FittingOptionsController.Algorithm;
            solver.ErrorEstimationMethod = GetErrorEstimationMethod(sourceSolution);
            solver.BootstrapIterations = options.BootstrapIterationsOverride
                ?? GetBootstrapIterations(sourceSolution);
            solver.UseErrorWeightedFitting = sourceSolution.UseWeightedFitting;
            SetCloneErrorEstimationMethod(factory.Model, solver.ErrorEstimationMethod);

            return solver;
        }

        public static bool CanOverrideBootstrapIterations(AnalysisResult result)
        {
            return result?.Solution != null
                && GetErrorEstimationMethod(result.Solution) == ErrorEstimationMethod.BootstrapResiduals;
        }

        public static int GetEffectiveBootstrapIterations(AnalysisResult result)
        {
            if (result?.Solution == null)
                throw new InvalidOperationException("The selected analysis result has no stored solution.");

            return GetBootstrapIterations(result.Solution);
        }

        public static IReadOnlyList<int> GetLargerBootstrapIterationPresets(AnalysisResult result)
        {
            var current = GetEffectiveBootstrapIterations(result);
            return FittingOptionsController.BootstrapIterationPresets
                .Where(value => value > current)
                .ToList()
                .AsReadOnly();
        }

        static void ValidateOptions(AnalysisResult result, AnalysisResultUpdateOptions options)
        {
            if (!options.BootstrapIterationsOverride.HasValue) return;

            if (!CanOverrideBootstrapIterations(result))
                throw new InvalidOperationException("Bootstrap iterations can be overridden only for a residual-bootstrap result.");

            var requested = options.BootstrapIterationsOverride.Value;
            var current = GetEffectiveBootstrapIterations(result);
            if (requested <= current)
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    $"Bootstrap iterations must be greater than the stored count of {current}.");

            if (!FittingOptionsController.BootstrapIterationPresets.Contains(requested))
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "Bootstrap iterations must use one of the supported presets.");
        }

        static List<ExperimentData> ResolveResultExperiments(GlobalModel sourceModel)
        {
            var data = new List<ExperimentData>();

            foreach (var sourceData in sourceModel.Models.Select(model => model.Data))
            {
                var current = DataManager.Data.FirstOrDefault(d => d.UniqueID == sourceData.UniqueID);
                if (current == null)
                {
                    var name = string.IsNullOrWhiteSpace(sourceData.Name) ? sourceData.FileName : sourceData.Name;
                    throw new InvalidOperationException($"Cannot update result because the experiment is no longer loaded: {name}");
                }

                data.Add(current);
            }

            return data;
        }

        static void ApplyModelOptions(GlobalModelFactory factory, GlobalModel sourceModel)
        {
            if (sourceModel.ModelOptions == null) return;

            foreach (var option in sourceModel.ModelOptions.Values)
                factory.SetModelOption(option.Copy());
        }

        static void ApplyConstraints(GlobalModelFactory factory, GlobalModelParameters sourceParameters)
        {
            if (sourceParameters?.Constraints == null) return;

            foreach (var constraint in sourceParameters.Constraints)
                factory.GlobalModelParameters.SetConstraintForParameter(constraint.Key, constraint.Value);
        }

        static void ApplyGlobalParameters(GlobalModelFactory factory, GlobalModelParameters sourceParameters)
        {
            if (sourceParameters?.GlobalTable == null) return;

            foreach (var parameter in sourceParameters.GlobalTable.Values)
            {
                factory.GlobalModelParameters.AddorUpdateGlobalParameter(
                    parameter.Key,
                    parameter.Value,
                    parameter.IsLocked,
                    parameter.Limits);
            }
        }

        static void ApplyIndividualParameters(GlobalModel targetModel, GlobalModel sourceModel)
        {
            foreach (var source in sourceModel.Models)
            {
                var target = targetModel.Models.FirstOrDefault(model => model.Data.UniqueID == source.Data.UniqueID);
                if (target == null) continue;

                var sourceParameters = source.Solution?.Model?.Parameters ?? source.Parameters;
                foreach (var parameter in sourceParameters.Table.Values)
                {
                    if (target.Parameters.Table.ContainsKey(parameter.Key))
                        target.Parameters.AddOrUpdateParameter(parameter.Copy());
                }
            }
        }

        static void ApplyCloneOptions(GlobalModel targetModel, ModelCloneOptions sourceOptions)
        {
            var options = CopyCloneOptions(sourceOptions);

            targetModel.ModelCloneOptions = options;
            foreach (var model in targetModel.Models)
                model.ModelCloneOptions = CopyCloneOptions(options);
        }

        static ModelCloneOptions CopyCloneOptions(ModelCloneOptions source)
        {
            if (source == null) return ModelCloneOptions.DefaultOptions;

            return new ModelCloneOptions
            {
                IsGlobalClone = source.IsGlobalClone,
                ErrorEstimationMethod = source.ErrorEstimationMethod,
                IncludeConcentrationErrorsInBootstrap = source.IncludeConcentrationErrorsInBootstrap,
                EnableAutoConcentrationVariance = source.EnableAutoConcentrationVariance,
                AutoConcentrationVariance = source.AutoConcentrationVariance,
                DiscardedDataPoint = source.DiscardedDataPoint,
                UnlockBootstrapParameters = source.UnlockBootstrapParameters,
            };
        }

        static int GetBootstrapIterations(GlobalSolution sourceSolution)
        {
            var iterations = sourceSolution.BootstrapIterations;

            return iterations > 0 ? iterations : FittingOptionsController.BootstrapIterations;
        }

        static ErrorEstimationMethod GetErrorEstimationMethod(GlobalSolution sourceSolution)
        {
            var method = sourceSolution.ModelCloneOptions?.ErrorEstimationMethod ?? ErrorEstimationMethod.None;
            if (method != ErrorEstimationMethod.None) return method;

            return sourceSolution.Solutions.FirstOrDefault()?.ErrorMethod ?? ErrorEstimationMethod.None;
        }

        static void SetCloneErrorEstimationMethod(GlobalModel model, ErrorEstimationMethod method)
        {
            if (model.ModelCloneOptions != null)
                model.ModelCloneOptions.ErrorEstimationMethod = method;

            foreach (var child in model.Models)
            {
                if (child.ModelCloneOptions != null)
                    child.ModelCloneOptions.ErrorEstimationMethod = method;
            }
        }

        static Task<SolverConvergence> RunSolverAsync(SolverInterface solver)
        {
            var completion = new TaskCompletionSource<SolverConvergence>();

            void OnAnalysisFinished(object sender, SolverConvergence convergence)
            {
                if (!ReferenceEquals(sender, solver)) return;

                SolverInterface.AnalysisFinished -= OnAnalysisFinished;
                completion.TrySetResult(convergence);
            }

            SolverInterface.AnalysisFinished += OnAnalysisFinished;

            try
            {
                solver.Analyze();
            }
            catch (Exception ex)
            {
                SolverInterface.AnalysisFinished -= OnAnalysisFinished;
                completion.TrySetException(ex);
            }

            return completion.Task;
        }

        static GlobalSolution GetGlobalSolution(SolverInterface solver)
        {
            return solver switch
            {
                GlobalSolver globalSolver => globalSolver.Model?.Solution,
                Solver singleSolver => singleSolver.Model?.Solution == null
                    ? null
                    : GlobalSolution.FromSingleExperimentSolver(singleSolver),
                _ => null
            };
        }
    }
}
