using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Math;
using AnalysisITC.Core.Analysis.Models;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Analysis
{
	public class GlobalModel
	{
        public List<Model> Models { get; private set; }
		public GlobalModelParameters Parameters { get; set; }
		public GlobalSolution Solution { get; set; }

		public double MeanTemperature => Models.Average(mdl => mdl.Data.MeasuredTemperature);
		public bool TemperatureDependenceExposed { get; private set; }
		public ModelCloneOptions ModelCloneOptions { get; set; }

        public int NumberOfParameters => Parameters.TotalFittingParameters;
		public bool ShouldFitIndividually => !Parameters.RequiresGlobalFitting;
		public AnalysisModel ModelType => Models.First().ModelType;
		public IDictionary<AttributeKey, ExperimentAttribute> ModelOptions => Models.First()?.ModelOptions ?? null;

        public bool UseSyringeCorrectionMode => ModelOptions != null
                && ModelOptions.TryGetValue(AttributeKey.UseSyringeActiveFraction, out var option)
                && option.BoolValue;

        public int GetNumberOfPoints()
		{
			var m = 0;

			foreach (var model in Models)
			{
				m += model.NumberOfPoints;
			}

			return m;
		}

        public GlobalModel()
		{
			Parameters = new GlobalModelParameters();
			Models = new List<Model>();
		}

		public GlobalModel(List<Model> models)
		{
            Parameters = new GlobalModelParameters();
            Models = new List<Model>();

            foreach (var mdl in models) AddModel(mdl);
        }

        public void AddModel(Model model)
		{
			Models.Add(model);

			TemperatureDependenceExposed = Models.Max(mdl => mdl.Data.MeasuredTemperature) - Models.Min(mdl => mdl.Data.MeasuredTemperature) > AppSettings.MinimumTemperatureSpanForFitting;
        }

		public double LossFunction(double[] parameters, bool errorweighted)
		{
			// Abort early if a termination has been requested by the user.
            if (SolverInterface.TerminateAnalysisFlag?.Up == true)
                throw new OptimizerStopException();

            Parameters.UpdateFromArray(parameters);

            double totalloss = 0;

            foreach (var model in Models)
            {
                var loss = model.LossFunction(Parameters.GetParametersForModel(this, model).GetFittedParameterArray(), errorweighted);
                totalloss += loss;
            }

            return totalloss;
        }

        internal bool TryLossFunction(double[] parameters, bool errorweighted, out double totalLoss)
        {
            ThrowIfTerminationRequested();
            Parameters.UpdateFromArray(parameters);

            totalLoss = 0;
            foreach (var model in Models)
            {
                var memberParameters = Parameters.GetParametersForModel(this, model)
                    .GetFittedParameterArray();
                if (!model.TryLossFunction(memberParameters, errorweighted, out var memberLoss)
                    || !FWEMath.IsFinite(totalLoss + memberLoss))
                {
                    totalLoss = double.NaN;
                    return false;
                }
                totalLoss += memberLoss;
            }

            return FWEMath.IsFinite(totalLoss);
        }

		public double[] LossFunctionResiduals(double[] parameters, bool errorweighted)
		{
            // Honour termination requests (e.g. user cancellation) through the shared stop flag.
            // This also ensures that LM fits can be aborted quickly.
            if (SolverInterface.TerminateAnalysisFlag?.Up == true)
                throw new OptimizerStopException();

            // Update the global parameter table from the incoming parameter array. Without this update, the
            // residuals would be calculated at the previous parameter values, causing the LM solver to stop with
            // zero step size on the first iteration.
            Parameters.UpdateFromArray(parameters);

            // Preallocate the result list with the total number of residuals across all models for efficiency.
            var res = new List<double>(GetNumberOfPoints());

            // Compute residuals model-by-model using the updated global parameter set.
            foreach (var model in Models)
			{
				var par = Parameters.GetParametersForModel(this, model).GetFittedParameterArray();

				res.AddRange(model.LossFunctionResiduals(par, errorweighted));
			}

			return res.ToArray();
		}

        internal bool TryLossFunctionResiduals(
            double[] parameters,
            bool errorweighted,
            out double[] residuals)
        {
            ThrowIfTerminationRequested();
            Parameters.UpdateFromArray(parameters);

            var result = new List<double>(GetNumberOfPoints());
            foreach (var model in Models)
            {
                var memberParameters = Parameters.GetParametersForModel(this, model)
                    .GetFittedParameterArray();
                if (!model.TryLossFunctionResiduals(
                        memberParameters, errorweighted, out var memberResiduals))
                {
                    residuals = null;
                    return false;
                }
                result.AddRange(memberResiduals);
            }

            residuals = result.ToArray();
            return true;
        }

        static void ThrowIfTerminationRequested()
        {
            if (SolverInterface.TerminateAnalysisFlag?.Up == true)
                throw new OptimizerStopException();
        }

        public double Loss()
		{
			return GaussianLikelihoodEvaluator
                .Evaluate(this, GaussianLikelihoodMode.EstimatedCommonVariance)
                .RmsdMicrojoules;
		}

		public GlobalModel GenerateSyntheticModel()
		{
            return GenerateSyntheticModel(BootstrapRandomStreams.CreateOne());
        }

        internal GlobalModel GenerateSyntheticModel(Random random)
        {
            GlobalModel model = new GlobalModel();

			// Preserve member order so bootstrap replicates can be joined by stable
			// experiment identity. Each child keeps the replicate-local random stream.
			foreach (var mdl in Models)
			{
				model.AddModel(mdl.GenerateSyntheticModel(random));
			}

            CopyUncertaintyFitTopologyTo(model);

            return model;
        }

		public GlobalModel LeaveOneOut(int idx)
		{
            GlobalModel model = new GlobalModel();

            var mdls = new List<Model>(Models.Where((v, i) => i != idx));

            foreach (var mdl in mdls)
            {
                model.AddModel(mdl.GenerateSyntheticModel());
            }

            CopyUncertaintyFitTopologyTo(model);

            return model;
        }

        void CopyUncertaintyFitTopologyTo(GlobalModel model)
        {
            foreach (var constraint in Parameters.Constraints)
            {
                model.Parameters.SetConstraintForParameter(constraint.Key, constraint.Value);
            }

            var unlockGlobalParameters = ModelCloneOptions?.EffectiveUnlockBootstrapParameters == true;
            foreach (var parameter in Parameters.GlobalTable.Values)
            {
                model.Parameters.AddorUpdateGlobalParameter(
                    parameter.Key,
                    parameter.Value,
                    islocked: parameter.IsLocked && !unlockGlobalParameters,
                    limits: parameter.Limits);
            }

            foreach (var member in model.Models)
            {
                model.Parameters.AddIndivdualParameter(member.Parameters);
            }
        }
	}

	public class GlobalSolution
	{
        public string UniqueID { get; private set; } = Guid.NewGuid().ToString();

        internal void SetID(string id) => UniqueID = id;

        public GlobalModel Model { get; set; }
        public SolverConvergence Convergence { get; set; }
        public List<GlobalSolution> BootstrapSolutions { get; private set; } = new List<GlobalSolution>();
		public Dictionary<ParameterType, LinearFitWithError> TemperatureDependence = new Dictionary<ParameterType, LinearFitWithError>();
        public bool IsValid { get; private set; } = true;
		
        public double Loss => Convergence.Loss;
		public TimeSpan Time => Convergence.Time;
		public TimeSpan BootstrapTime => Convergence.ErrorEstimationTime;
		public TimeSpan TotalTime => Time + BootstrapTime;

		public string SolutionName => Solutions[0].SolutionName;

        public int BootstrapIterations => BootstrapSolutions.Count;
        public double MeanTemperature => Model.MeanTemperature;
		public List<SolutionInterface> Solutions => Model.Models.Select(mdl => mdl.Solution).ToList();
		public List<ParameterType> IndividualModelReportParameters => Model.Models[0].Solution.ReportParameters.Select(p => p.Key).ToList();
		public ModelCloneOptions ModelCloneOptions => Model.ModelCloneOptions;
        public ErrorEstimationMethod ErrorEstimationMethod => ModelCloneOptions.ErrorEstimationMethod;

		public bool UseWeightedFitting { get; set; } = false;

        public static GlobalSolution FromSingleExperimentSolver(Solver solver)
        {
            if (solver?.Model?.Solution == null)
                throw new InvalidOperationException("Cannot create an analysis result before the single experiment analysis has a solution.");

            var globalModel = new GlobalModel(new List<Model> { solver.Model })
            {
                ModelCloneOptions = solver.Model.ModelCloneOptions ?? ModelCloneOptions.DefaultOptions,
                Parameters = new GlobalModelParameters(),
            };
            globalModel.Parameters.AddIndivdualParameter(solver.Model.Parameters);

            var globalSolver = new GlobalSolver
            {
                Model = globalModel,
                ErrorEstimationMethod = solver.ErrorEstimationMethod,
                UseErrorWeightedFitting = solver.UseErrorWeightedFitting,
            };

            var solution = new GlobalSolution(
                globalSolver,
                new List<SolutionInterface> { solver.Model.Solution },
                solver.Model.Solution.Convergence);

            globalModel.Solution = solution;

            return solution;
        }

        public void Invalidate()
		{
			IsValid = false;

			foreach (var sol in Solutions) sol.Invalidate();
		}

        /// <summary>
        /// Marks a saved global solution invalid without invalidating every member
        /// solution or changing their attached experiment models.
        /// </summary>
        internal void InvalidateForExperimentChange()
        {
            IsValid = false;
        }

        internal void RestoreValidity(bool isValid)
        {
            IsValid = isValid;
        }

		public GlobalSolution(GlobalSolver solver, SolverConvergence convergence)
		{
			Model = solver.Model;
			Convergence = convergence;
			UseWeightedFitting = solver.UseErrorWeightedFitting;
			Convergence?.SetLoss(Model.Loss());

            foreach (var mdl in Model.Models)
            {
                mdl.Solution = SolutionInterface.FromModel(mdl, convergence.Copy());
                mdl.Solution.Convergence.SetLoss(mdl.Loss());
                mdl.Solution.SetParentSolution(this);
            }

            var dependencies = Solutions[0].DependenciesToReport;

            foreach (var dep in dependencies) SetParameterTemperatureDependence(dep.Item1, dep.Item2);
        }

		public GlobalSolution(GlobalSolver solver, List<SolutionInterface> solutions, SolverConvergence convergence)
		{
			Model = solver.Model;
			Convergence = convergence;
			UseWeightedFitting = solver.UseErrorWeightedFitting;
			Convergence?.SetLoss(Model.Loss());

            var dependencies = solutions[0].DependenciesToReport;

			// Get the parameters 
            foreach (var dep in dependencies) SetParameterTemperatureDependence(dep.Item1, dep.Item2);

            var indexedBootstrapSolutions = solutions
                .Select(solution => solution.BootstrapSolutions
                    .Select((bootstrap, ordinal) => new
                    {
                        Index = bootstrap.BootstrapReplicateIndex ?? ordinal,
                        Solution = bootstrap,
                    })
                    .ToDictionary(item => item.Index, item => item.Solution))
                .ToList();

            var commonReplicateIndices = indexedBootstrapSolutions.Count == 0
                ? new List<int>()
                : indexedBootstrapSolutions
                    .Skip(1)
                    .Aggregate(
                        new HashSet<int>(indexedBootstrapSolutions[0].Keys),
                        (common, member) =>
                        {
                            common.IntersectWith(member.Keys);
                            return common;
                        })
                    .OrderBy(index => index)
                    .ToList();

            if (commonReplicateIndices.Count != 0)
			{
                // Snapshot-backed solutions carry explicit indices. Legacy bootstrap
                // lists use their ordinal as the implicit index. Joining the common
                // keys prevents a missing/filtered replicate from shifting all later
                // global pairings.
                var sets = commonReplicateIndices
                    .Select(index => indexedBootstrapSolutions
                        .Select(member => member[index])
                        .ToList())
                    .ToArray();

                // Construct global solutions for each refit
                // This determines a 'dependency' for each parameter (may be zero slope and just a value)
                var bootstrapSolutions = new GlobalSolution[sets.Length];

                System.Threading.Tasks.Parallel.For(0, sets.Length, i =>
                {
                    bootstrapSolutions[i] = new GlobalSolution(sets[i]);
                });

                BootstrapSolutions = bootstrapSolutions.ToList();

                SetTemperatureDependenceErrorsFromBootstrapSolutions(BootstrapSolutions);
            }

			foreach (var sol in solutions) sol.SetParentSolution(this);
        }

        private GlobalSolution(List<SolutionInterface> solutions)
        {
            foreach (var solution in solutions)
                solution.Model.Solution = solution;

            Model = new GlobalModel(solutions.Select(sol => sol.Model).ToList());

            var dependencies = solutions[0].DependenciesToReport;

            foreach (var dep in dependencies) SetParameterTemperatureDependence(dep.Item1, dep.Item2, solutions);
        }

		private GlobalSolution(GlobalModel model)
		{
			Model = model;

            var dependencies = Solutions[0].DependenciesToReport;

            foreach (var dep in dependencies) SetParameterTemperatureDependence(dep.Item1, dep.Item2);
        }

        void SetParameterTemperatureDependence(ParameterType key, Func<SolutionInterface, FloatWithError> func)
        {
            SetParameterTemperatureDependence(key, func, Solutions);
        }

        void SetParameterTemperatureDependence(ParameterType key, Func<SolutionInterface, FloatWithError> func, IReadOnlyList<SolutionInterface> solutions)
		{
			if (Model.TemperatureDependenceExposed)
			{
				var xy = solutions.Select(sol => new double[] { sol.Data.MeasuredTemperature - Model.MeanTemperature, func(sol) }).ToArray();
				var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

				TemperatureDependence[key] = new LinearFitWithError(reg.B, reg.A, MeanTemperature);
			}
			else
			{
				// No temperature dependence possible, slope is zero, intercept + error from distribution of model values
				TemperatureDependence[key] = new LinearFitWithError(new(0), new(solutions.Select(func).ToList()), MeanTemperature);
            }
		}

		public FloatWithError GetStandardParameterValue(ParameterType key)
		{
			if (!TemperatureDependence.ContainsKey(key)) throw new Exception("GlobMdl: GetStdParam: KeyNotFound: " + key.ToString());

			return TemperatureDependence[key].Evaluate(AppSettings.ReferenceTemperature);
		}

        public void SetBootstrapSolutions(List<GlobalSolution> solutions)
		{
			BootstrapSolutions = solutions;

			//Set individual data models bootstrapped parameters
            foreach (var model in Model.Models)
            {
                var sols = BootstrapSolutions.SelectMany(gs => gs.Solutions.Where(s => s.Model.Data.UniqueID == model.Data.UniqueID)).ToList();

				model.Solution.SetBootstrapSolutions(sols.Where(sol => sol.Convergence.IsUsableForErrorEstimation).ToList());
            }

            SetTemperatureDependenceErrorsFromBootstrapSolutions(BootstrapSolutions);
        }

        void SetTemperatureDependenceErrorsFromBootstrapSolutions(List<GlobalSolution> solutions)
        {
            var tmp = new Dictionary<ParameterType, LinearFitWithError>();

            foreach (var par in TemperatureDependence)
            {
                var slope = solutions.Select(gsol => gsol.TemperatureDependence[par.Key].Slope.Value).ToList();
                var intercept = solutions.Select(gsol => gsol.TemperatureDependence[par.Key].Intercept.Value).ToList();

                tmp[par.Key] = new LinearFitWithError(
                    new FloatWithError(slope, TemperatureDependence[par.Key].Slope),
                    new FloatWithError(intercept, TemperatureDependence[par.Key].Intercept),
                    MeanTemperature);
            }

            TemperatureDependence = tmp;
        }
    }
}
