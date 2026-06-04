using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.AppClasses.AnalysisClasses.Models;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
    public static class AnalysisResultUpdater
    {
        public static async Task<SolverConvergence> UpdateAsync(AnalysisResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var solver = PrepareSolver(result);
            var convergence = await RunSolverAsync(solver);

            if (convergence == null)
                throw new InvalidOperationException("Analysis update finished without convergence information.");
            if (convergence.Failed || convergence.Stopped)
                throw new InvalidOperationException($"Analysis update did not produce a usable result: {convergence.Message}");

            var solution = GetGlobalSolution(solver);
            if (solution == null)
                throw new InvalidOperationException("Analysis update did not produce a solution.");

            result.UpdateSolution(solution);
            DataManager.LoadResultSolutionsToExperiments(result);

            return convergence;
        }

        public static SolverInterface PrepareSolver(AnalysisResult result)
        {
            if (result?.Solution?.Model == null)
                throw new InvalidOperationException("The selected analysis result has no stored solution.");

            var sourceSolution = result.Solution;
            var sourceModel = sourceSolution.Model;
            var data = ResolveResultExperiments(sourceModel);

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
            solver.BootstrapIterations = GetBootstrapIterations(sourceSolution);
            solver.UseErrorWeightedFitting = sourceSolution.UseWeightedFitting;
            SetCloneErrorEstimationMethod(factory.Model, solver.ErrorEstimationMethod);

            return solver;
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
