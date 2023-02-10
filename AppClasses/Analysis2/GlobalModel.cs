using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Math;
using Microsoft.SolverFoundation.Services;

namespace AnalysisITC.AppClasses.Analysis2
{
	public class GlobalModel
	{
		public List<Model> Models { get; set; }
		public GlobalModelParameters Parameters { get; set; }
		public GlobalSolution Solution { get; set; }

		public int NumberOfParameters => Parameters.TotalFittingParameters;

		public double MeanTemperature { get; private set; }

		public GlobalModel()
		{
			Parameters = new GlobalModelParameters();
			Models = new List<Model>();
		}

		public void AddModel(Model model)
		{
			Models.Add(model);

			MeanTemperature = Models.Average(mdl => mdl.Data.MeasuredTemperature);
		}

		public double LossFunction(double[] parameters)
		{
			Parameters.UpdateFromArray(parameters);

			return Loss();
		}

		public double Loss()
		{
			double loss = 0;

			foreach (var model in Models)
				loss += model.LossFunction(Parameters.GetParametersForModel(this, model).ToArray());

			return loss;
		}

        public GlobalModel GenerateSyntheticModel()
        {
            GlobalModel model = new GlobalModel();

			foreach (var mdl in Models)
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
        public List<GlobalSolution> BootstrapSolutions { get; private set; } = new List<GlobalSolution>();
		private SolutionInterface Solution { get; set; }
		public Dictionary<ParameterTypes, LinearFitWithError> TemperatureDependence = new Dictionary<ParameterTypes, LinearFitWithError>();
        public bool IsValid { get; private set; } = true;

        public double Loss => Convergence.Loss;
		public TimeSpan Time => Convergence.Time;
		public TimeSpan BootstrapTime => Convergence.BootstrapTime;
		public TimeSpan TotalTime => Time + BootstrapTime;

        public int BootstrapIterations => BootstrapSolutions.Count;
        public double ReferenceTemperature => Model.MeanTemperature;
		List<SolutionInterface> Solutions => Model.Models.Select(mdl => mdl.Solution).ToList();

        public void Invalidate() => IsValid = false;

		public GlobalSolution(GlobalModel model, SolverConvergence convergence)
		{
			Model = model;
			Convergence = convergence;

			foreach (var mdl in model.Models)
			{
				mdl.Solution = SolutionInterface.FromModel(mdl, model.Parameters.GetParametersForModel(model, mdl).ToArray());
				mdl.Solution.Convergence = convergence;
            }

			Solution = SolutionInterface.FromModel(model.Models[0], model.Parameters.GetParametersForModel(model, model.Models[0]).ToArray());

			foreach (var par in Solution.Parameters) SetParameterTemperatureDependence(par.Key);
		}

		void SetParameterTemperatureDependence(ParameterTypes key)
		{
            var xy = Model.Models.Select((m, i) => new double[] { m.Data.MeasuredTemperature - Model.MeanTemperature, m.Parameters.Table[key].Value }).ToArray();
            var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

			TemperatureDependence[key] = new LinearFitWithError(reg.B, reg.A, ReferenceTemperature);
        }

        public void SetBootstrapSolutions(List<GlobalSolution> solutions)
		{
			BootstrapSolutions = solutions;

			//Set individual data models bootstrapped parameters
            foreach (var model in Model.Models)
            {
                var sols = BootstrapSolutions.SelectMany(gs => gs.Solutions.Where(s => s.Model.Data.UniqueID == model.Data.UniqueID)).ToList();

				model.Solution.SetBootstrapSolutions(sols.Where(sol => !sol.Convergence.Failed).ToList());
                model.Solution.ComputeErrorsFromBootstrapSolutions();
            }

			foreach (var par in Solution.Parameters)
			{
                var slope = solutions.Select(gsol => gsol.TemperatureDependence[par.Key].Slope.Value).ToList();
                var intercept = solutions.Select(gsol => gsol.TemperatureDependence[par.Key].Intercept.Value).ToList();

                TemperatureDependence[par.Key] = new LinearFitWithError(new(slope), new(intercept), ReferenceTemperature);
            }
        }
    }
}

