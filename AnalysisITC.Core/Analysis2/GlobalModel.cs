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

        public bool UseSyringeCorrectionMode => ModelOptions[AttributeKey.UseSyringeActiveFraction]?.BoolValue ?? false;

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
            // Abort early if a termination has been requested by the user or via the Nelder–Mead cancellation token.
            if (SolverInterface.TerminateAnalysisFlag?.Up == true)
                throw new OptimizerStopException();
            // Also honour the cancellation token used by the Nelder–Mead solver. This ensures we stop when
            // nested NM solvers are cancelled (e.g. during bootstrapping).
            if (SolverInterface.NelderMeadToken != null && SolverInterface.NelderMeadToken.IsCancellationRequested)
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

		public double[] LossFunctionResiduals(double[] parameters, bool errorweighted)
		{
            // Honour termination requests (e.g. user cancellation) by checking the global termination flag and
            // the Nelder–Mead cancellation token. If either has been signalled, abort immediately. We add
            // the TerminateAnalysisFlag check to ensure that LM fits can also be aborted quickly.
            if (SolverInterface.TerminateAnalysisFlag?.Up == true)
                throw new OptimizerStopException();
            if (SolverInterface.NelderMeadToken != null && SolverInterface.NelderMeadToken.IsCancellationRequested)
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

        public double Loss()
		{
			double totalloss = 0;

			foreach (var model in Models)
			{
				totalloss += model.Loss();
			}

            return totalloss;
		}

        public GlobalModel GenerateSyntheticModel()
        {
            GlobalModel model = new GlobalModel();

			var mdls = new List<Model>(Models);
            global::AnalysisITC.Core.Utilities.Extensions.Shuffle(mdls);

			foreach (var mdl in mdls)
			{
				model.AddModel(mdl.GenerateSyntheticModel());
			}

			foreach (var con in Parameters.Constraints)
			{
				model.Parameters.SetConstraintForParameter(con.Key, con.Value);
			}

            foreach (var par in Parameters.GlobalTable)
			{
				model.Parameters.AddorUpdateGlobalParameter(par.Value.Key, par.Value.Value, par.Value.IsLocked, par.Value.Limits); //TODO implement global determined?
			}

			foreach (var parset in model.Models)
			{
				model.Parameters.AddIndivdualParameter(parset.Parameters);
			}

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

            foreach (var con in Parameters.Constraints)
            {
                model.Parameters.SetConstraintForParameter(con.Key, con.Value);
            }

            foreach (var par in Parameters.GlobalTable)
            {
                model.Parameters.AddorUpdateGlobalParameter(par.Value.Key, par.Value.Value, par.Value.IsLocked, par.Value.Limits); //TODO implement global determiend?
            }

            foreach (var parset in model.Models)
            {
                model.Parameters.AddIndivdualParameter(parset.Parameters);
            }

            return model;
        }
	}

	public class GlobalSolution
	{
        public string UniqueID { get; private set; } = Guid.NewGuid().ToString();

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

		public GlobalSolution(GlobalSolver solver, SolverConvergence convergence)
		{
			Model = solver.Model;
			Convergence = convergence;
			UseWeightedFitting = solver.UseErrorWeightedFitting;

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

            var dependencies = solutions[0].DependenciesToReport;

			// Get the parameters 
            foreach (var dep in dependencies) SetParameterTemperatureDependence(dep.Item1, dep.Item2);

            int min_error_sol_count = solutions.Min(sol => sol.BootstrapSolutions.Count);
            if (min_error_sol_count != 0)
			{
                // Create global error solutions from each experiment's saved refit at the same bootstrap index.
                // Use the SolutionInterface instances directly so reconstructed project files keep the
                // bootstrap parameter values copied into SolutionInterface.Parameters.
                var sets = new List<SolutionInterface>[min_error_sol_count];

                for (int i = 0; i < min_error_sol_count; i++)
                {
                    var set = new List<SolutionInterface>(solutions.Count);

                    foreach (var sol in solutions)
                        set.Add(sol.BootstrapSolutions[i]);

                    sets[i] = set;
                }

                // Construct global solutions for each refit
                // This determines a 'dependency' for each parameter (may be zero slope and just a value)
                var bootstrapSolutions = new GlobalSolution[min_error_sol_count];

                System.Threading.Tasks.Parallel.For(0, min_error_sol_count, i =>
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
                var slope = solutions.Select(gsol => gsol.TemperatureDependence[par.Key].Slope).ToList();
                var intercept = solutions.Select(gsol => gsol.TemperatureDependence[par.Key].Intercept).ToList();

                tmp[par.Key] = new LinearFitWithError(
                    new(slope, mean: TemperatureDependence[par.Key].Slope),
                    new(intercept, mean: TemperatureDependence[par.Key].Intercept),
                    MeanTemperature);
            }

            TemperatureDependence = tmp;
        }
    }
}
