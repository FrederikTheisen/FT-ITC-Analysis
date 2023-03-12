using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Math;
using Microsoft.SolverFoundation.Services;

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
			Parameters.UpdateFromArray(parameters);

			return Loss();
		}

		public double Loss()
		{
			double totalloss = 0;

			foreach (var model in Models)
			{
				var loss = model.LossFunction(Parameters.GetParametersForModel(this, model).ToArray());
				totalloss += loss * loss;
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
				model.Parameters.AddorUpdateGlobalParameter(par.Value.Key, par.Value.Value, par.Value.IsLocked, par.Value.Limits);
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
		public ErrorEstimationMethod ErrorEstimationMethod { get; set; } = ErrorEstimationMethod.None;
        public List<GlobalSolution> BootstrapSolutions { get; private set; } = new List<GlobalSolution>();
		public Dictionary<ParameterTypes, LinearFitWithError> TemperatureDependence = new Dictionary<ParameterTypes, LinearFitWithError>();
        public bool IsValid { get; private set; } = true;

        public double Loss => Convergence.Loss;
		public TimeSpan Time => Convergence.Time;
		public TimeSpan BootstrapTime => Convergence.BootstrapTime;
		public TimeSpan TotalTime => Time + BootstrapTime;

		public string SolutionName => Solutions[0].SolutionName;

        public int BootstrapIterations => BootstrapSolutions.Count;
        public double MeanTemperature => Model.MeanTemperature;
		public List<SolutionInterface> Solutions => Model.Models.Select(mdl => mdl.Solution).ToList();
		public List<ParameterTypes> IndividualModelReportParameters => Model.Models[0].Solution.ReportParameters.Select(p => p.Key).ToList();

        public void Invalidate() => IsValid = false;

		public GlobalSolution(GlobalSolver solver, SolverConvergence convergence)
		{
			Model = solver.Model;
			Convergence = convergence;
			ErrorEstimationMethod = solver.ErrorEstimationMethod;

            foreach (var mdl in Model.Models)
            {
                mdl.Solution = SolutionInterface.FromModel(mdl, Model.Parameters.GetParametersForModel(Model, mdl).ToArray());
                mdl.Solution.Convergence = convergence;
                mdl.Solution.IsGlobalAnalysisSolution = true;
            }

            var dependencies = Solutions[0].DependenciesToReport;

            foreach (var dep in dependencies) SetParameterTemperatureDependence(dep.Item1, dep.Item2);
        }

		public GlobalSolution(GlobalSolver solver, List<SolutionInterface> solutions, SolverConvergence convergence)
		{
			Model = solver.Model;
			Convergence = convergence;
			ErrorEstimationMethod = solver.ErrorEstimationMethod;

            var dependencies = Solutions[0].DependenciesToReport;

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

				var tmp = new Dictionary<ParameterTypes, LinearFitWithError>();

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

        void SetParameterTemperatureDependence(ParameterTypes key, Func<SolutionInterface, FloatWithError> func)
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

		public FloatWithError GetStandardParameterValue(ParameterTypes key)
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

			var tmp = new Dictionary<ParameterTypes, LinearFitWithError>();

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

