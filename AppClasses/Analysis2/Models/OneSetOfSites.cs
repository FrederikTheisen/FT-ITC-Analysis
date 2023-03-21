using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.AppClasses.Analysis2
{
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

		double GetDeltaHeat(int i, double n, double H, double K)
		{
			var inj = Data.Injections[i];
			var Qi = GetHeatContent(inj, n, H, K);
			var Q_i = i == 0 ? 0.0 : GetHeatContent(Data.Injections[i - 1], n, H, K);

			var dQi = Qi + (inj.Volume / Data.CellVolume) * ((Qi + Q_i) / 2.0) - Q_i;

			return dQi;
		}

		double GetHeatContent(InjectionData inj, double n, double H, double K)
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
			Model mdl = new OneSetOfSites(Data.GetSynthClone(ModelCloneOptions));

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

            public override List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
            {
                var output = base.UISolutionParameters(info);

                if (info.HasFlag(FinalFigureDisplayParameters.Nvalue)) output.Add(new("N", N.ToString("F2")));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new("Kd", Kd.AsDissociationConstant()));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new("∆H", Enthalpy.ToString(EnergyUnit.KiloJoule, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.TdS)) output.Add(new("-T∆S", TdS.ToString(EnergyUnit.KiloJoule, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new("∆G", GibbsFreeEnergy.ToString(EnergyUnit.KiloJoule, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Offset)) output.Add(new("Offset", Offset.ToString(EnergyUnit.KiloJoule, permole: true)));

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
}

