using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.SolverFoundation.Services;

namespace AnalysisITC.AppClasses.Analysis2
{
    public class Model
    {
		public ExperimentData Data { get; set; }
		public AnalysisModel ModelType { get; set; } = AnalysisModel.OneSetOfSites;
		public ModelParameters Parameters { get; set; }
        public ModelCloneOptions ModelCloneOptions { get; set; }
        public Dictionary<string,bool> ModelOptions { get; set; }

        public SolutionInterface Solution { get; set; }

        public int NumberOfParameters => Parameters.FittingParameterCount;
        public string ModelName => ModelType.ToString();
        bool DataHasSolution => Data.Solution != null;
        bool SolutionHasParameter(ParameterTypes key) => DataHasSolution ? Data.Solution.Parameters.ContainsKey(key) : false;

        public virtual double GuessEnthalpy() => Data.Injections.First(inj => inj.Include).Enthalpy - GuessOffset();
        public virtual double GuessOffset() => 0.8 * Data.Injections.Where(inj => inj.Include).TakeLast(2).Average(inj => inj.Enthalpy);
        public virtual double GuessN() => Data.Injections.Last().Ratio / 2;
        public virtual double GuessAffinity() => 1000000;
        public virtual double GuessAffinityAsGibbs() => -Energy.R * Data.MeasuredTemperatureKelvin * Math.Log(GuessAffinity());

        public virtual double GuessParameter(ParameterTypes key)
        {
            if (SolutionHasParameter(key))
            {
                return Solution.Parameters[key];
            }
            else return 0;
        }

        public Model(ExperimentData data)
		{
			Data = data;

			Parameters = new ModelParameters(Data);
        }

		public virtual void InitializeParameters(ExperimentData data)
		{
            Parameters = new ModelParameters(data);
        }

        public virtual double Evaluate(int injectionindex, bool withoffset = true)
		{
			throw new NotImplementedException();
		}

		public double EvaluateEnthalpy(int injectionindex, bool withoffset = true)
		{
			return Evaluate(injectionindex, withoffset) / Data.Injections[injectionindex].InjectionMass;
		}

        public FloatWithError EvaluateBootstrap(int inj, bool withoff = true)
        {
            var results = new List<double>();

            //Evaluates with offset to include errors in offset
            foreach (var sol in Solution.BootstrapSolutions)
            {
                results.Add(sol.Model.EvaluateEnthalpy(inj, true));
            }

            var val = new FloatWithError(results, EvaluateEnthalpy(inj, true));

            return withoff ? val : val - Solution.Parameters[ParameterTypes.Offset]; //Returns evaluated value with or without offset
        }

		public double LossFunction(double[] parameters)
		{
            if (SolverInterface.NelderMeadToken != null && SolverInterface.NelderMeadToken.IsCancellationRequested) throw new OptimizerStopException(); //Only way to cancel the NM algorithm seems to be from the loss function

            Parameters.UpdateFromArray(parameters);

			return Loss();
		}

		public double Loss()
		{
			double loss = 0;

			foreach (var inj in Data.Injections.Where(i => i.Include))
			{
				var diff = Evaluate(inj.ID) - inj.PeakArea;
				loss += diff * diff;
			}

            return Math.Sqrt(loss / Data.Injections.Count(i => i.Include));
		}

        public virtual Model GenerateSyntheticModel()
        {
            var mdl = new Model(Data.GetSynthClone(ModelCloneOptions));

            foreach (var par in Parameters.Table)
			{
                mdl.Parameters.AddParameter(par.Key, par.Value.Value, par.Value.IsLocked, par.Value.Limits, par.Value.StepSize);
            }

			return mdl;
        }
    }

    public class SolutionInterface
	{
        public string Guid { get; private set; } = new Guid().ToString();
        public bool IsGlobalAnalysisSolution { get; set; } = false;

        public Model Model { get; protected set; }
        public SolverConvergence Convergence { get; set; }
        public ErrorEstimationMethod ErrorMethod { get; set; } = ErrorEstimationMethod.None;
        public virtual List<SolutionInterface> BootstrapSolutions { get; protected set; }
		public Dictionary<ParameterTypes, FloatWithError> Parameters { get; } = new Dictionary<ParameterTypes, FloatWithError>();

        public string SolutionName => (IsGlobalAnalysisSolution ? "Global." : "") + Model.ModelName;
        public ExperimentData Data => Model.Data;
        public double Temp => Data.MeasuredTemperature;
        public double TempKelvin => Temp + 273.15;
		public double Loss => Convergence.Loss;
        public FloatWithError TotalEnthalpy
        {
            get
            {
                var dH = new FloatWithError(0.0);
                foreach (var par in ParametersConformingToKey(ParameterTypes.Enthalpy1)) dH += par;
                return dH;
            }
        }
        public List<FloatWithError> ParametersConformingToKey(ParameterTypes key)
        {
            return Parameters.Where(par => par.Key.GetProperties().ParentType == key).Select(par => par.Value).ToList();
        }

        public bool IsValid { get; private set; } = true;

		public void Invalidate() => IsValid = false;
		
		public static SolutionInterface FromModel(Model model, double[] parameters, SolverConvergence convergence)
		{
            SolutionInterface solution = null;

            switch (model.ModelType)
			{
				case AnalysisModel.OneSetOfSites: solution = new OneSetOfSites.ModelSolution(model, parameters); break;
				case AnalysisModel.TwoSetsOfSites: solution = new TwoSetsOfSites.ModelSolution(model, parameters); break;
                case AnalysisModel.SequentialBindingSites:
				case AnalysisModel.Dissociation:
				default: throw new Exception("Model type not found");
			}

            solution.Convergence = convergence;

            foreach (var par in model.Parameters.Table) solution.Parameters.Add(par.Key, new (par.Value.Value));

            return solution;
		}

        public virtual List<Tuple<ParameterTypes, Func<SolutionInterface, FloatWithError>>> DependenciesToReport => new List<Tuple<ParameterTypes, Func<SolutionInterface, FloatWithError>>>();
        public virtual Dictionary<ParameterTypes, FloatWithError> ReportParameters => new Dictionary<ParameterTypes, FloatWithError>();

  //      public virtual List<Tuple<string,string>> UISolutionParameters(SolutionInfo info)
		//{
  //          var output = new List<Tuple<string, string>>();

  //          string mdl = SolutionName;

		//	output.Add(new(mdl, Loss.ToString("G3")));

		//	return output;
  //      }

        public virtual List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
        {
            var output = new List<Tuple<string, string>>();

            if (info.HasFlag(FinalFigureDisplayParameters.Model))
            {
                output.Add(new(SolutionName, Loss.ToString("G3")));
            }

            return output;
        }

        public void SetBootstrapSolutions(List<SolutionInterface> list)
		{
			BootstrapSolutions = list;

            if (list.Count > 0) ComputeErrorsFromBootstrapSolutions();
        }

        public virtual void ComputeErrorsFromBootstrapSolutions()
        {
            Data.UpdateSolution(null);
        }

        public virtual string GetClipboardString(double magnitude, EnergyUnit eunit)
        {
            return null;
        }

        public enum SolutionInfo
        {
            TableSummaryLines = 0,
            FinalFigure = 1,
            Analysis = 2
        }

        [Flags]
        public enum FinalFigureDisplayParameters
        {
            None = 0,
            Model = 1,
            Nvalue = 2,
            Affinity = 4,
            Enthalpy = 8,
            TdS = 16,
            Gibbs = 32,
            Offset = 64,

            Temperature = 128,
            Concentrations = 256,

            Fitted = Nvalue | Affinity | Enthalpy,
            Derived = TdS | Gibbs,

            Default = Model | Fitted | Derived | Temperature | Concentrations,
            All = Model | Fitted | Offset | Derived | Temperature | Concentrations,

            ListView = Model | Affinity | Enthalpy,
            AnalysisView = Model | Fitted | Derived | Offset
        }
    }
}

