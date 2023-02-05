using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using static CoreFoundation.DispatchSource;

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

			model.Parameters.EnthalpyStyle = Parameters.EnthalpyStyle;
            model.Parameters.AffinityStyle = Parameters.AffinityStyle;
            model.Parameters.NStyle = Parameters.NStyle;

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

  //      public void SetSolution()
		//{
		//	Solution = new GlobalSolution();

		//	foreach (var mdl in Models)
		//	{
		//		var solution = SolutionInterface.FromModel(mdl, Parameters.GetParametersForModel(this, mdl).ToArray());

		//		mdl.Solution = solution;
  //              Solution.Solutions.Add(solution);
		//	}
		//}
	}

	public class GlobalSolution
	{
        public GlobalModel Model { get; set; }
        public SolverConvergence Convergence { get; set; }
        public List<GlobalSolution> Solutions { get; private set; } = new List<GlobalSolution>();
		public SolutionInterface Solution { get; set; }
        public bool IsValid { get; private set; } = true;

        public double Loss => Convergence.Loss;
		public TimeSpan Time => Convergence.Time;
		public TimeSpan BootstrapTime => TimeSpan.FromSeconds(Solutions.Sum(sol => sol.Time.TotalSeconds));

        public int BootstrapIterations => Solutions.Count;
        public double ReferenceTemperature => Model.MeanTemperature;

        public void Invalidate() => IsValid = false;

        public static GlobalSolution FromModel(GlobalModel model)
		{
            GlobalSolution globalsolution = new GlobalSolution();

            foreach (var mdl in model.Models)
            {
                var solution = SolutionInterface.FromModel(mdl, model.Parameters.GetParametersForModel(model, mdl).ToArray());

                mdl.Solution = solution;
			}

			globalsolution.Solution = SolutionInterface.FromModel(model.Models[0], model.Parameters.GetParametersForModel(model, model.Models[0]).ToArray());

			foreach (var par in globalsolution.Solution.Parameters.Table)
			{
				switch (par.Key)
				{
					case ParameterTypes.Nvalue1: break;
				}
			}

			return globalsolution;
        }

		public void SetParametersFromBootstrap(List<GlobalSolution> solutions)
		{

		}
    }
}

