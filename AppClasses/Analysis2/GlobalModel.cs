using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Math;
using AnalysisITC.AppClasses.Analysis2.Models;

namespace AnalysisITC.AppClasses.Analysis2
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

		public double LossFunction(double[] parameters)
		{
            //SolverInterface.NelderMeadToken?.Token.ThrowIfCancellationRequested();
            if (SolverInterface.NelderMeadToken != null && SolverInterface.NelderMeadToken.IsCancellationRequested) throw new OptimizerStopException();

            Parameters.UpdateFromArray(parameters);

			return Loss();
		}

		public double[] LossFunctionResiduals(double[] parameters)
		{
            // If the Nelder–Mead cancellation token has been signalled then abort immediately. This mirrors the
            // behaviour of LossFunction() and prevents unnecessary work when a cancellation is requested.
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

				res.AddRange(model.LossFunctionResiduals(par));
			}

			return res.ToArray();
		}

		public double Loss()
		{
			double totalloss = 0;

			foreach (var model in Models)
			{
				var loss = model.LossFunction(Parameters.GetParametersForModel(this, model).GetFittedParameterArray()); //Loss Function = RMSD
				totalloss += loss * loss; //Unclear if correct loss function
			}

			return Math.Sqrt(totalloss);
		}

        public GlobalModel GenerateSyntheticModel()
        {
            GlobalModel model = new GlobalModel();

			var mdls = new List<Model>(Models);
			mdls.Shuffle();

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
        public GlobalModel Model { get; set; }
        public SolverConvergence Convergence { get; set; }
        public List<GlobalSolution> BootstrapSolutions { get; private set; } = new List<GlobalSolution>();
		public Dictionary<ParameterType, LinearFitWithError> TemperatureDependence = new Dictionary<ParameterType, LinearFitWithError>();
        public bool IsValid { get; private set; } = true;

        public double Loss => Convergence.Loss;
		public TimeSpan Time => Convergence.Time;
		public TimeSpan BootstrapTime => Convergence.BootstrapTime;
		public TimeSpan TotalTime => Time + BootstrapTime;

		public string SolutionName => Solutions[0].SolutionName;

        public int BootstrapIterations => BootstrapSolutions.Count;
        public double MeanTemperature => Model.MeanTemperature;
		public List<SolutionInterface> Solutions => Model.Models.Select(mdl => mdl.Solution).ToList();
		public List<ParameterType> IndividualModelReportParameters => Model.Models[0].Solution.ReportParameters.Select(p => p.Key).ToList();
		public ModelCloneOptions ModelCloneOptions => Model.ModelCloneOptions;
        public ErrorEstimationMethod ErrorEstimationMethod => ModelCloneOptions.ErrorEstimationMethod;

        public void Invalidate()
		{
			IsValid = false;

			foreach (var sol in Solutions) sol.Invalidate();
		}

		public GlobalSolution(GlobalSolver solver, SolverConvergence convergence)
		{
			Model = solver.Model;
			Convergence = convergence;

            foreach (var mdl in Model.Models)
            {
                mdl.Solution = SolutionInterface.FromModel(mdl, new(convergence));
                mdl.Solution.Convergence.SetLoss(mdl.Loss());
                mdl.Solution.SetIsGlobal(this);
            }

			//HACK possibly due to inappropriate loss function
			Convergence.SetLoss(Solutions.Sum(sol => sol.Convergence.Loss) / Solutions.Count);

            var dependencies = Solutions[0].DependenciesToReport;

            foreach (var dep in dependencies) SetParameterTemperatureDependence(dep.Item1, dep.Item2);
        }

		public GlobalSolution(GlobalSolver solver, List<SolutionInterface> solutions, SolverConvergence convergence)
		{
			Model = solver.Model;
			Convergence = convergence;

            var dependencies = solutions[0].DependenciesToReport; //Changed Solu... to solu...

            foreach (var dep in dependencies) SetParameterTemperatureDependence(dep.Item1, dep.Item2);

			if (solutions[0].BootstrapSolutions.Count != 0)
			{
				var sets = new List<List<SolutionInterface>>();

				for (int i = 0; i < solutions.Min(sol => sol.BootstrapSolutions.Count); i++)
				{
					var set = new List<SolutionInterface>();

					foreach (var sol in solutions) set.Add(sol.BootstrapSolutions[i]);

					sets.Add(set);
                }

				BootstrapSolutions = (sets.Select(set => new GlobalSolution(new GlobalModel(set.Select(s => s.Model).ToList())))).ToList();

				var tmp = new Dictionary<ParameterType, LinearFitWithError>();

                foreach (var par in TemperatureDependence)
                {
                    var slope = BootstrapSolutions.Select(gsol => gsol.TemperatureDependence[par.Key].Slope).ToList();
                    var intercept = BootstrapSolutions.Select(gsol => gsol.TemperatureDependence[par.Key].Intercept).ToList();

                    tmp[par.Key] = new LinearFitWithError(new(slope), new(intercept), MeanTemperature);
                }

                TemperatureDependence = tmp;
            }
        }

		private GlobalSolution(GlobalModel model)
		{
			Model = model;

            var dependencies = Solutions[0].DependenciesToReport;

            foreach (var dep in dependencies) SetParameterTemperatureDependence(dep.Item1, dep.Item2);
        }

        void SetParameterTemperatureDependence(ParameterType key, Func<SolutionInterface, FloatWithError> func)
		{
			if (Model.TemperatureDependenceExposed)
			{
				var xy = Model.Models.Select((m, i) => new double[] { m.Data.MeasuredTemperature - Model.MeanTemperature, func(m.Solution) }).ToArray();
				var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

				TemperatureDependence[key] = new LinearFitWithError(reg.B, reg.A, MeanTemperature);
			}
			else
			{
				TemperatureDependence[key] = new LinearFitWithError(new(0), new(Model.Models.Select((m) => func(m.Solution))), MeanTemperature);
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

				model.Solution.SetBootstrapSolutions(sols.Where(sol => !sol.Convergence.Failed).ToList());
            }

			var tmp = new Dictionary<ParameterType, LinearFitWithError>();

            foreach (var par in TemperatureDependence)
			{
                var slope = solutions.Select(gsol => gsol.TemperatureDependence[par.Key].Slope.Value).ToList();
                var intercept = solutions.Select(gsol => gsol.TemperatureDependence[par.Key].Intercept.Value).ToList();

                tmp[par.Key] = new LinearFitWithError(new(slope), new(intercept), MeanTemperature);
            }

			TemperatureDependence = tmp;
        }
    }
}

