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

		public SolutionInterface Solution { get; set; }

        public int NumberOfParameters => Parameters.FittingParameterCount;
        public string ModelName => ModelType.ToString();

        public virtual double GuessEnthalpy() => Data.Injections.First(inj => inj.Include).Enthalpy - GuessOffset();
        public virtual double GuessOffset() => 0.8*Data.Injections.Where(inj => inj.Include).TakeLast(2).Average(inj => inj.Enthalpy);
        public virtual double GuessN() => Data.Injections.Last().Ratio / 2;
        public virtual double GuessAffinity() => 1000000;
        public virtual double GuessAffinityAsGibbs() => -Energy.R * Data.MeasuredTemperatureKelvin * Math.Log(GuessAffinity());

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

        public FloatWithError EvaluateBootstrap(int inj)
        {
            var results = new List<double>();

            foreach (var sol in Solution.BootstrapSolutions)
            {
                results.Add(sol.Model.EvaluateEnthalpy(inj, true));
            }

            return new FloatWithError(results, EvaluateEnthalpy(inj, true)) - Solution.Parameters[ParameterTypes.Offset];
        }

		public double LossFunction(double[] parameters)
		{
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
            var mdl = new Model(Data.GetSynthClone());

            foreach (var par in Parameters.Table)
			{
                mdl.Parameters.AddParameter(par.Key, par.Value.Value, par.Value.IsLocked, par.Value.Limits, par.Value.StepSize);
            }

			return mdl;
        }
    }

	public class OneSetOfSites : Model
	{
        public OneSetOfSites(ExperimentData data) : base(data)
		{
		}

        public override void InitializeParameters(ExperimentData data)
        {
			base.InitializeParameters(data);

			Parameters.AddParameter(ParameterTypes.Nvalue1, this.GuessN());
            Parameters.AddParameter(ParameterTypes.Enthalpy1, this.GuessEnthalpy());
            Parameters.AddParameter(ParameterTypes.Affinity1, this.GuessAffinity());
            Parameters.AddParameter(ParameterTypes.Offset, this.GuessOffset());
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
		{
			if (withoffset) return GetDeltaHeat(injectionindex, Parameters.Table[ParameterTypes.Nvalue1].Value, Parameters.Table[ParameterTypes.Enthalpy1].Value, Parameters.Table[ParameterTypes.Affinity1].Value) + Parameters.Table[ParameterTypes.Offset].Value * Data.Injections[injectionindex].InjectionMass;
			else return GetDeltaHeat(injectionindex, Parameters.Table[ParameterTypes.Nvalue1].Value, Parameters.Table[ParameterTypes.Enthalpy1].Value, Parameters.Table[ParameterTypes.Affinity1].Value);
        }

		public double GetDeltaHeat(int i, double n, double H, double K)
		{
			var inj = Data.Injections[i];
			var Qi = GetHeatContent(inj, n, H, K);
			var Q_i = i == 0 ? 0.0 : GetHeatContent(Data.Injections[i - 1], n, H, K);

			var dQi = Qi + (inj.Volume / Data.CellVolume) * ((Qi + Q_i) / 2.0) - Q_i;

			return dQi;
		}

		public double GetHeatContent(InjectionData inj, double n, double H, double K)
		{
			var ncell = n * inj.ActualCellConcentration;
			var first = (ncell * H * Data.CellVolume) / 2.0;
			var XnM = inj.ActualTitrantConcentration / ncell;
			var nKM = 1.0 / (K * ncell);
			var square = (1.0 + XnM + nKM);
			var root = (square * square) - 4.0 * XnM;

			return first * (1 + XnM + nKM - Math.Sqrt(root));
		}

        public override Model GenerateSyntheticModel()
        {
			Model mdl = new OneSetOfSites(Data.GetSynthClone());

			foreach (var par in Parameters.Table)
			{
				mdl.Parameters.AddParameter(par.Key, par.Value.Value, par.Value.IsLocked, par.Value.Limits, par.Value.StepSize);
			}

			return mdl;
        }

        public class ModelSolution : SolutionInterface
        {
            public Energy Enthalpy => Parameters[ParameterTypes.Enthalpy1].Energy;
            public FloatWithError K => Parameters[ParameterTypes.Affinity1];
            public FloatWithError N => Parameters[ParameterTypes.Nvalue1];
            public Energy Offset => Parameters[ParameterTypes.Offset].Energy;

            public FloatWithError Kd => new FloatWithError(1) / K;
            public Energy GibbsFreeEnergy => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K));
            public Energy TdS => GibbsFreeEnergy - Enthalpy;
            public Energy Entropy => TdS / TempKelvin;

            public ModelSolution(Model model, double[] parameters)
            {
                Model = model;
                BootstrapSolutions = new List<SolutionInterface>();
            }

            public override void ComputeErrorsFromBootstrapSolutions()
            {
                var enthalpies = BootstrapSolutions.Select(s => (s as ModelSolution).Enthalpy.FloatWithError.Value);
                var k = BootstrapSolutions.Select(s => (s as ModelSolution).K.Value);
                var n = BootstrapSolutions.Select(s => (s as ModelSolution).N.Value);
                var offsets = BootstrapSolutions.Select(s => (s as ModelSolution).Offset.Value);

                Parameters[ParameterTypes.Enthalpy1] = new FloatWithError(enthalpies, Enthalpy);
                Parameters[ParameterTypes.Affinity1] = new FloatWithError(k, K);
                Parameters[ParameterTypes.Nvalue1] = new FloatWithError(n, N);
                Parameters[ParameterTypes.Offset] = new FloatWithError(offsets, Offset);

                base.ComputeErrorsFromBootstrapSolutions();
            }

            public override List<Tuple<string, string>> UISolutionParameters(SolutionInfo info)
            {
                var output = base.UISolutionParameters(info);

                if ((int)info > 0) output.Add(new("N", N.ToString("F2")));

                output.Add(new("Kd", Kd.AsDissociationConstant()));
                output.Add(new("∆H", Enthalpy.ToString(EnergyUnit.KiloJoule, permole: true)));

                if ((int)info > 0)
                {
                    output.Add(new("-T∆S", TdS.ToString(EnergyUnit.KiloJoule, permole: true)));
                    output.Add(new("∆G", GibbsFreeEnergy.ToString(EnergyUnit.KiloJoule, permole: true)));
                }

                if ((int)info > 1)
                {
                    output.Add(new("Offset", Offset.ToString(EnergyUnit.KiloJoule, permole: true)));
                }

                return output;
            }

            public override List<Tuple<ParameterTypes, Func<SolutionInterface, FloatWithError>>> DependenciesToReport => new List<Tuple<ParameterTypes, Func<SolutionInterface, FloatWithError>>>
                {
                    new (ParameterTypes.Enthalpy1, new(sol => (sol as ModelSolution).Enthalpy.FloatWithError)), 
                    new (ParameterTypes.EntropyContribution1, new(sol => (sol as ModelSolution).TdS.FloatWithError)),
                    new (ParameterTypes.Gibbs1, new(sol => (sol as ModelSolution).GibbsFreeEnergy.FloatWithError)),
                };

            public override Dictionary<ParameterTypes, FloatWithError> ReportParameters => new Dictionary<ParameterTypes, FloatWithError>
                {
                    { ParameterTypes.Nvalue1, N },
                    { ParameterTypes.Affinity1, Kd },
                    { ParameterTypes.Enthalpy1, Enthalpy.FloatWithError },
                    { ParameterTypes.EntropyContribution1, TdS.FloatWithError} ,
                    { ParameterTypes.Gibbs1, GibbsFreeEnergy.FloatWithError },
                };
        }
    }

	public class TwoSetsOfSites : Model
	{
		public TwoSetsOfSites(ExperimentData data) : base(data)
		{
			throw new NotImplementedException("TwoSetsOfSites not implemented yet");
		}

		public override void InitializeParameters(ExperimentData data)
		{
            base.InitializeParameters(data);

            Parameters.AddParameter(ParameterTypes.Nvalue1, this.GuessN(), limits: new double[] { 0.1, 10 });
            Parameters.AddParameter(ParameterTypes.Enthalpy1, this.GuessEnthalpy() / 2, limits: new double[] { -500000, 500000 });
            Parameters.AddParameter(ParameterTypes.Affinity1, this.GuessAffinity(), limits: new double[] { 10E-12, 0.1 });
            Parameters.AddParameter(ParameterTypes.Nvalue2, this.GuessN(), limits: new double[] { 0.1, 10 });
            Parameters.AddParameter(ParameterTypes.Enthalpy2, this.GuessEnthalpy() / 2, limits: new double[] { -500000, 500000 });
            Parameters.AddParameter(ParameterTypes.Affinity2, this.GuessAffinity(), limits: new double[] { 10E-12, 0.1 });
            Parameters.AddParameter(ParameterTypes.Offset, this.GuessOffset(), limits: new double[] { -500000, 500000 });
        }

        public override Model GenerateSyntheticModel()
        {
            return new TwoSetsOfSites(Data.GetSynthClone());
        }

		public class ModelSolution : SolutionInterface
		{
            public new List<ModelSolution> BootstrapSolutions { get; set; }

            public Energy Enthalpy1 => new(Parameters[ParameterTypes.Enthalpy1]);
            public Energy Enthalpy2 => new(Parameters[ParameterTypes.Enthalpy2]);
            public FloatWithError K1 => Parameters[ParameterTypes.Affinity1];
            public FloatWithError K2 => Parameters[ParameterTypes.Affinity2];
            public FloatWithError N1 => Parameters[ParameterTypes.Nvalue1];
            public FloatWithError N2 => Parameters[ParameterTypes.Nvalue2];
            public Energy Offset => new(Parameters[ParameterTypes.Offset]);

            public FloatWithError Kd1 => new FloatWithError(1) / K1;
            public Energy GibbsFreeEnergy1 => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K1));
            public Energy TdS1 => GibbsFreeEnergy1 - Enthalpy1;
            public Energy Entropy1 => TdS1 / TempKelvin;

            public FloatWithError Kd2 => new FloatWithError(1) / K2;
            public Energy GibbsFreeEnergy2 => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K2));
            public Energy TdS2 => GibbsFreeEnergy2 - Enthalpy2;
            public Energy Entropy2 => TdS2 / TempKelvin;

            public ModelSolution(Model model, double[] parameters)
            {
                Model = model;
            }

            public override void ComputeErrorsFromBootstrapSolutions()
            {
                var enthalpies1 = BootstrapSolutions.Select(s => s.Enthalpy1.FloatWithError.Value);
                var enthalpies2 = BootstrapSolutions.Select(s => s.Enthalpy2.FloatWithError.Value);
                var k1 = BootstrapSolutions.Select(s => s.K1.Value);
                var k2 = BootstrapSolutions.Select(s => s.K2.Value);
                var n1 = BootstrapSolutions.Select(s => s.N1.Value);
                var n2 = BootstrapSolutions.Select(s => s.N2.Value);
                var offsets = BootstrapSolutions.Select(s => (double)s.Offset);

                Parameters[ParameterTypes.Enthalpy1] = new FloatWithError(enthalpies1, Enthalpy1);
                Parameters[ParameterTypes.Affinity1] = new FloatWithError(k1, K1);
                Parameters[ParameterTypes.Nvalue1] = new FloatWithError(n1, N1);
                Parameters[ParameterTypes.Enthalpy1] = new FloatWithError(enthalpies2, Enthalpy2);
                Parameters[ParameterTypes.Affinity1] = new FloatWithError(k2, K2);
                Parameters[ParameterTypes.Nvalue1] = new FloatWithError(n2, N2);
                Parameters[ParameterTypes.Offset] = new FloatWithError(offsets, Offset);

                base.ComputeErrorsFromBootstrapSolutions();
            }

            public override List<Tuple<string, string>> UISolutionParameters(SolutionInfo info)
            {
                var output = base.UISolutionParameters(info);

                output.Add(new("Kd1", Kd1.ToString()));
                output.Add(new("∆H1", Enthalpy1.ToString()));

                return output;
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
		
		public static SolutionInterface FromModel(Model model, double[] parameters)
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

            foreach (var par in model.Parameters.Table) solution.Parameters.Add(par.Key, new (par.Value.Value));

            Console.WriteLine(solution.TotalEnthalpy.Value.ToString());

            return solution;
		}

        public virtual List<Tuple<ParameterTypes, Func<SolutionInterface, FloatWithError>>> DependenciesToReport => null;
        public virtual Dictionary<ParameterTypes, FloatWithError> ReportParameters => null;

        public virtual List<Tuple<string,string>> UISolutionParameters(SolutionInfo info)
		{
            var output = new List<Tuple<string, string>>();

            string mdl = SolutionName;

			output.Add(new(mdl, Loss.ToString("G3")));

			return output;
        }

		public void SetBootstrapSolutions(List<SolutionInterface> list)
		{
			BootstrapSolutions = list;
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
    }
}

