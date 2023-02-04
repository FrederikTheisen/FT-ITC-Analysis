using System;
using System.Collections.Generic;
using System.Linq;

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

		public void SetSolutions()
		{
			GlobalSolution globalsolution = new GlobalSolution();

			foreach (var mdl in Models)
			{
				var solution = SolutionInterface.FromModel(mdl, Parameters.GetParametersForModel(this, mdl).ToArray());

				mdl.Solution = solution;
				globalsolution.Solutions.Add(solution);
			}
		}
	}

	public class GlobalSolution
	{
        public GlobalModel Model { get; set; }
        public SolverConvergence Convergence { get; set; }
        public List<SolutionInterface> Solutions { get; private set; } = new List<SolutionInterface>();

        public double Loss { get; private set; } = 0;
        public TimeSpan BootstrapTime { get; internal set; }

        public int BootstrapIterations => Solutions[0].BootstrapSolutions.Count;
        public double ReferenceTemperature => Model.MeanTemperature;

		public static GlobalSolution FromModel(GlobalModel model)
		{
            GlobalSolution globalsolution = new GlobalSolution();

            foreach (var mdl in model.Models)
            {
                var solution = SolutionInterface.FromModel(mdl, model.Parameters.GetParametersForModel(model, mdl).ToArray());

                mdl.Solution = solution;
                globalsolution.Solutions.Add(solution);
            }

			return globalsolution;
        }

		public void SetParametersFromBootstrap(List<GlobalSolution> solutions)
		{

		}
    }
}

