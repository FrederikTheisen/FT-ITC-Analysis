﻿using System;
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

		public virtual double GuessEnthalpy()
		{
			return Data.Injections.First(inj => inj.Include).Enthalpy - GuessOffset();
		}
		public virtual double GuessOffset()
		{
			return Data.Injections.Where(inj => inj.Include).TakeLast(2).Average(inj => inj.Enthalpy);
		}
		public virtual double GuessN()
		{
			return Data.Injections.Last().Ratio / 2;
		}
		public virtual double GuessAffinity() => 1000000;

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

			Parameters.AddParameter(ParameterTypes.Nvalue1, this.GuessN(), limits: new double[] { 0.1, 10 }, stepsize: 0.5);
            Parameters.AddParameter(ParameterTypes.Enthalpy1, this.GuessEnthalpy(), limits: new double[] { -500000, 500000 }, stepsize: 1000);
            Parameters.AddParameter(ParameterTypes.Affinity1, this.GuessAffinity(), limits: new double[] { 10, 100000000000 }, stepsize: 10000);
            Parameters.AddParameter(ParameterTypes.Offset, this.GuessOffset(), limits: new double[] { -500000, 500000 }, stepsize: 1000);
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
            public new List<ModelSolution> BootstrapSolutions { get; set; }

            public Energy Enthalpy => new (Parameters[ParameterTypes.Enthalpy1]);
			public FloatWithError K => Parameters[ParameterTypes.Affinity1];
			public FloatWithError N => Parameters[ParameterTypes.Nvalue1];
            public Energy Offset => new (Parameters[ParameterTypes.Offset]);

            public FloatWithError Kd => new FloatWithError(1) / K;
            public Energy GibbsFreeEnergy => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K));
            public Energy TdS => GibbsFreeEnergy - Enthalpy;
            public Energy Entropy => TdS / TempKelvin;

            public ModelSolution(Model model, double[] parameters)
            {
				Model = model;
				BootstrapSolutions = new List<ModelSolution>();
            }

            public override void SetBootstrapSolutions(List<SolutionInterface> list)
            {
                BootstrapSolutions.AddRange(list.Select(sol => sol as ModelSolution));
            }

            public override void ComputeErrorsFromBootstrapSolutions()
            {
                var enthalpies = BootstrapSolutions.Select(s => s.Enthalpy.FloatWithError.Value);
                var k = BootstrapSolutions.Select(s => s.K.Value);
                var n = BootstrapSolutions.Select(s => s.N.Value);
                var offsets = BootstrapSolutions.Select(s => (double)s.Offset);

                Parameters[ParameterTypes.Enthalpy1] = new FloatWithError(enthalpies, Enthalpy);
				Parameters[ParameterTypes.Affinity1] = new FloatWithError(k, K);
                Parameters[ParameterTypes.Nvalue1] = new FloatWithError(n, N);
                Parameters[ParameterTypes.Offset] = new FloatWithError(offsets, Offset);

				base.ComputeErrorsFromBootstrapSolutions();
            }

            public override List<Tuple<string, string>> UISolutionParameters(bool all = false)
			{
				var output = base.UISolutionParameters();

				if (all) output.Add(new("N", N.ToString()));

				output.Add(new("Kd", Kd.ToString()));
				output.Add(new("∆H", Enthalpy.ToString()));

				if (all)
				{
					output.Add(new("-T∆S", TdS.ToString()));
					output.Add(new("∆G", GibbsFreeEnergy.ToString()));
				}

				return output;
			}
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

            public override void SetBootstrapSolutions(List<SolutionInterface> list)
            {
                BootstrapSolutions.AddRange(list.Select(sol => sol as ModelSolution));
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

            public override List<Tuple<string, string>> UISolutionParameters(bool all = false)
            {
                var output = base.UISolutionParameters();

                output.Add(new("Kd1", Kd1.ToString()));
                output.Add(new("∆H1", Enthalpy1.ToString()));

                return output;
            }
        }
    }

	public class SolutionInterface
	{
        public string Guid { get; private set; } = new Guid().ToString();

        public Model Model { get; protected set; }
        public SolverConvergence Convergence { get; set; }
        public virtual List<SolutionInterface> BootstrapSolutions { get; protected set; }
		public Dictionary<ParameterTypes, FloatWithError> Parameters { get; } = new Dictionary<ParameterTypes, FloatWithError>();

        public ExperimentData Data => Model.Data;
        public double T => Data.MeasuredTemperature;
        public double TempKelvin => T + 273.15;
		public double Loss => Convergence.Loss;

        public bool IsValid { get; private set; } = true;

		public void Invalidate() => IsValid = false;
		
		public static SolutionInterface FromModel(Model model, double[] parameters)
		{
			switch (model.ModelType)
			{
				case AnalysisModel.OneSetOfSites: return new OneSetOfSites.ModelSolution(model, parameters);
				case AnalysisModel.TwoSetsOfSites: return new TwoSetsOfSites.ModelSolution(model, parameters);
                case AnalysisModel.SequentialBindingSites:
				case AnalysisModel.Dissociation:
				default: throw new Exception("Model type not found");
			}
		}

		public virtual List<Tuple<string,string>> UISolutionParameters(bool all = false)
		{
            var output = new List<Tuple<string, string>>();

			output.Add(new(Model.ToString() + ":", Loss.ToString("G3")));

			return output;
        }

		public virtual void SetBootstrapSolutions(List<SolutionInterface> list)
		{
			BootstrapSolutions = list;
		}

        public virtual void ComputeErrorsFromBootstrapSolutions()
        {
            Data.UpdateSolution(null);
        }
    }
}

