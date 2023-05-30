using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using AnalysisITC.AppClasses.AnalysisClasses;
using Microsoft.SolverFoundation.Services;

namespace AnalysisITC.AppClasses.Analysis2.Models
{
    public class Model
    {
        public static EnergyUnit ReportEnergyUnit => AppSettings.EnergyUnit.IsSI() ? EnergyUnit.KiloJoule : EnergyUnit.KCal;

		public ExperimentData Data { get; set; }
		public virtual AnalysisModel ModelType => AnalysisModel.OneSetOfSites;
		public ModelParameters Parameters { get; set; }
        public ModelCloneOptions ModelCloneOptions { get; set; }
        public IDictionary<ModelOptionKey, ModelOptions> ModelOptions { get; set; } = new Dictionary<ModelOptionKey, ModelOptions>();
        
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

        //TODO consider implementing this feature, but not sure about how it should work yet
        public virtual double GuessParameter(ParameterType key, double def = 0)
        {
            if (SolutionHasParameter(key))
            {
                return Data.Solution.Parameters[key];
            }
            else return def;
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

        public void SetModelOptions(IDictionary<ModelOptionKey, ModelOptions> options = null)
        {
            if (options != null)
                ModelOptions = options;

            // Setup model options

            // Check if prebound ligand should be taken from attributes
            if (ModelOptions.ContainsKey(ModelOptionKey.PreboundLigandConc) && ModelOptions[ModelOptionKey.PreboundLigandConc].BoolValue == true)
            {
                if (!Data.Attributes.Exists(att => att.Key == ModelOptionKey.PreboundLigandConc))
                    throw new KeyNotFoundException("Model option configuration error encountered.\nMissing option key: " + ModelOptionKey.PreboundLigandConc.ToString() + "\n\nTo solve error, either add the attribute to the experiment or uncheck the 'From Exp' options in solver options");
                else ModelOptions[ModelOptionKey.PreboundLigandConc].ParameterValue = Data.Attributes.Find(opt => opt.Key == ModelOptionKey.PreboundLigandConc).ParameterValue;
            }
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

            //Evaluates with offset to include errors in offset (offset error covaries with enthalpy and must therefore be included)
            foreach (var sol in Solution.BootstrapSolutions)
            {
                results.Add(sol.Model.EvaluateEnthalpy(inj, withoffset: true));
            }

            if (results.Count == 0) results.Add(EvaluateEnthalpy(inj, withoffset: true));

            if (!withoff)
            {

                return new FloatWithError(
                    results.Select(r => r - Solution.Parameters[ParameterType.Offset].Value),
                    EvaluateEnthalpy(inj, withoffset: true) - Solution.Parameters[ParameterType.Offset].Value);
            }
            else
            {
                return new FloatWithError(results, EvaluateEnthalpy(inj, withoffset: true));
            }
            var val = new FloatWithError(results, EvaluateEnthalpy(inj, withoffset: true));

            return withoff ? val : val - Solution.Parameters[ParameterType.Offset].Value; //Returns evaluated value with or without offset
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
                mdl.Parameters.AddOrUpdateParameter(par.Key, par.Value.Value, par.Value.IsLocked);
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
        GlobalSolution GlobalParentSolution { get; set; }
        public Model Model { get; protected set; }
        public SolverConvergence Convergence { get; private set; }
        public ErrorEstimationMethod ErrorMethod { get; set; } = ErrorEstimationMethod.None;
        public virtual List<SolutionInterface> BootstrapSolutions { get; protected set; }
		public Dictionary<ParameterType, FloatWithError> Parameters { get; } = new Dictionary<ParameterType, FloatWithError>();

        public AnalysisModel ModelType => Model.ModelType;
        public bool IsGlobalAnalysisSolution => GlobalParentSolution != null;
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
            //FIXME unreproducible error related to modification of the collection while it is being used. Probably cross thread issue. Encountered 2
            return Parameters.Where(par => par.Key.GetProperties().ParentType == key).Select(par => par.Value).ToList();
        }

        public bool IsValid { get; private set; } = true;

        public void SetIsGlobal(GlobalSolution parent)
        {
            if (parent.Model.Parameters.Constraints.Count > 0)
                GlobalParentSolution = parent;
        }

		public void Invalidate()
        {
            IsValid = false;

            if (IsGlobalAnalysisSolution && GlobalParentSolution.IsValid)
            {
                GlobalParentSolution.Invalidate();
            }

            Data.UpdateSolution();
        }
		
		public static SolutionInterface FromModel(Model model, SolverConvergence convergence)
		{
            SolutionInterface solution = null;

            switch (model.ModelType)
			{
				case AnalysisModel.OneSetOfSites: solution = new OneSetOfSites.ModelSolution(model); break;
				case AnalysisModel.TwoSetsOfSites: solution = new TwoSetsOfSites.ModelSolution(model); break;
                case AnalysisModel.CompetitiveBinding: solution = new CompetitiveBinding.ModelSolution(model); break;
                case AnalysisModel.PeptideProlineIsomerization: solution = new OneSiteIsomerization.ModelSolution(model); break;
                case AnalysisModel.TwoCompetingSites: solution = new TwoCompetingSites.ModelSolution(model); break;
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

        public List<Tuple<string,string>> UIExperimentModelAttributes(DisplayAttributeOptions info)
        {
            var output = new List<Tuple<string, string>>();

            if (info.HasFlag(DisplayAttributeOptions.Competitor) && Model.Data.Attributes.Exists(att => att.Key == ModelOptionKey.PreboundLigandConc))
            {
                output.Add(new("[Comp]", Model.Data.Attributes.Find(att => att.Key == ModelOptionKey.PreboundLigandConc).ParameterValue.AsFormattedConcentration(true)));
            }

            if (info.HasFlag(DisplayAttributeOptions.Buffer) && Model.Data.Attributes.Exists(att => att.Key == ModelOptionKey.Buffer))
            {
                foreach (var opt in Model.Data.Attributes.FindAll(att => att.Key == ModelOptionKey.Buffer)) output.Add(new(opt.ParameterValue.AsFormattedConcentration(ConcentrationUnit.mM, true) + " " + ((Buffer)opt.IntValue).GetProperties().Name + " pH " + opt.DoubleValue.ToString("F1"), ""));
            }

            if (info.HasFlag(DisplayAttributeOptions.Salt) && Model.Data.Attributes.Exists(att => att.Key == ModelOptionKey.Salt))
            {
                foreach (var opt in Model.Data.Attributes.FindAll(att => att.Key == ModelOptionKey.Salt)) output.Add(new(opt.ParameterValue.AsFormattedConcentration(ConcentrationUnit.mM, true) + " " + ((Salt)opt.IntValue).GetProperties().Name, ""));
            }

            if (info.HasFlag(DisplayAttributeOptions.IonicStrength) && Model.Data.Attributes.Exists(att => att.Key == ModelOptionKey.Salt))
            {
                output.Add(new("[I]", new FloatWithError(BufferAttribute.GetIonicStrength(Model.Data)).AsF1FormattedConcentration(true)));
            }

            if (info.HasFlag(DisplayAttributeOptions.ProtonationEnthalpy) && Model.Data.Attributes.Exists(att => att.Key == ModelOptionKey.Buffer))
            {
                output.Add(new(Utils.MarkdownStrings.ProtonationEnthalpy, BufferAttribute.GetProtonationEnthalpy(Model.Data).ToString(AppSettings.EnergyUnit, "F1", true, true)));
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

        //public virtual string GetClipboardString(double magnitude, EnergyUnit eunit)
        //{
        //    return null;
        //}

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

            Attributes = 512,

            Fitted = Nvalue | Affinity | Enthalpy,
            Derived = TdS | Gibbs,

            Default = Model | Fitted | Derived | Temperature | Concentrations,
            All = Model | Fitted | Offset | Derived | Temperature | Concentrations | Attributes,

            ListView = Model | Affinity | Enthalpy,
            AnalysisView = Model | Fitted | Derived | Offset
        }

        [Flags]
        public enum DisplayAttributeOptions
        {
            None = 0,
            UsedInAnalysis = 1,
            Buffer = 2,
            Salt = 4,
            IonicStrength = 8,
            ProtonationEnthalpy = 16,
            Competitor = 32,

            All = Buffer | Salt | IonicStrength | ProtonationEnthalpy | Competitor,
            Solvent = Buffer | Salt,

        }
    }
}

