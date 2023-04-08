﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using AnalysisITC.AppClasses.AnalysisClasses;
using Microsoft.SolverFoundation.Services;

namespace AnalysisITC.AppClasses.Analysis2.Models
{
    public class Model
    {
		public ExperimentData Data { get; set; }
		public virtual AnalysisModel ModelType => AnalysisModel.OneSetOfSites;
		public ModelParameters Parameters { get; set; }
        public ModelCloneOptions ModelCloneOptions { get; set; }
        public Dictionary<string, ModelOption> ModelOptions { get; set; } = new Dictionary<string, ModelOption>();

        public SolutionInterface Solution { get; set; }

        public int NumberOfParameters => Parameters.FittingParameterCount;
        public string ModelName => ModelType.ToString();
        bool DataHasSolution => Data.Solution != null;
        bool SolutionHasParameter(ParameterType key) => DataHasSolution ? Data.Solution.Parameters.ContainsKey(key) : false;

        public virtual double GuessEnthalpy() => Data.Injections.First(inj => inj.Include).Enthalpy - GuessOffset();
        public virtual double GuessOffset() => 0.8 * Data.Injections.Where(inj => inj.Include).TakeLast(2).Average(inj => inj.Enthalpy);
        public virtual double GuessN() => Data.Injections.Last().Ratio / 2;
        public virtual double GuessAffinity() => 1000000;
        public virtual double GuessAffinityAsGibbs() => -Energy.R * Data.MeasuredTemperatureKelvin * Math.Log(GuessAffinity());

        public virtual double GuessParameter(ParameterType key)
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

            return withoff ? val : val - Solution.Parameters[ParameterType.Offset]; //Returns evaluated value with or without offset
        }

		public double LossFunction(double[] parameters)
		{
            //Only way to cancel the Simplex algorithm seems to be from the loss function
            if (SolverInterface.NelderMeadToken != null && SolverInterface.NelderMeadToken.IsCancellationRequested) throw new OptimizerStopException(); 

            Parameters.UpdateFromArray(parameters);

			return Loss();
		}

		public virtual double Loss()
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

            SetSynthModelParameters(mdl);

            return mdl;
        }

        internal void SetSynthModelParameters(Model mdl)
        {
            foreach (var par in Parameters.Table)
            {
                mdl.Parameters.AddParameter(par.Key, par.Value.Value, par.Value.IsLocked);
            }

            foreach (var opt in ModelOptions)
            {
                mdl.ModelOptions.Add(opt.Key, opt.Value);
            }
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
		public Dictionary<ParameterType, FloatWithError> Parameters { get; } = new Dictionary<ParameterType, FloatWithError>();

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
                foreach (var par in ParametersConformingToKey(ParameterType.Enthalpy1)) dH += par;
                return dH;
            }
        }
        public List<FloatWithError> ParametersConformingToKey(ParameterType key)
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
                case AnalysisModel.CompetitiveBinding: solution = new CompetitiveBinding.ModelSolution(model, parameters); break;
                case AnalysisModel.PeptideProlineIsomerization: solution = new OneSiteIsomerization.ModelSolution(model, parameters); break;
                case AnalysisModel.SequentialBindingSites:
				case AnalysisModel.Dissociation:
				default: throw new Exception("Model Solution not implemented");
			}

            solution.Convergence = convergence;

            foreach (var par in model.Parameters.Table) solution.Parameters.Add(par.Key, new (par.Value.Value));

            return solution;
		}

        public virtual List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>> DependenciesToReport => new List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>>();
        public virtual Dictionary<ParameterType, FloatWithError> ReportParameters => new Dictionary<ParameterType, FloatWithError>();

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
