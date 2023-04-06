using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC.AppClasses.Analysis2.Models
{
	public class CompetitiveBinding : OneSetOfSites
	{
        public override AnalysisModel ModelType => AnalysisModel.CompetitiveBinding;

        private const string WeakLigandConc = "WeakLigandConc";
        private const string WeakLigandAffinity = "WeakLigandAffinity";
        private const string WeakLigandEnthalpy = "WeakLigandEnthalpy";

		public CompetitiveBinding(ExperimentData data) : base(data)
        {
        }

        public override void InitializeParameters(ExperimentData data)
        {
            base.InitializeParameters(data);

            ModelOptions.Add(WeakLigandConc, ModelOption.Double("Prebound ligand conc.", 10e-6));
            ModelOptions.Add(WeakLigandEnthalpy, ModelOption.Parameter("Prebound ligand ∆H", ParameterType.Enthalpy1, new FloatWithError(-40000, 0)));
            ModelOptions.Add(WeakLigandAffinity, ModelOption.Parameter("Prebound ligand affinity.", ParameterType.Affinity1, new(1e-6, 0)));
        }

        //public override double Evaluate(int injectionindex, bool withoffset = true)
        //{
        //    if (withoffset) return GetDeltaHeat(injectionindex, Parameters.Table[ParameterTypes.Nvalue1].Value, Parameters.Table[ParameterTypes.Enthalpy1].Value, Parameters.Table[ParameterTypes.Affinity1].Value) + Parameters.Table[ParameterTypes.Offset].Value * Data.Injections[injectionindex].InjectionMass;
        //    else return GetDeltaHeat(injectionindex, Parameters.Table[ParameterTypes.Nvalue1].Value, Parameters.Table[ParameterTypes.Enthalpy1].Value, Parameters.Table[ParameterTypes.Affinity1].Value);
        //}


        //double GetDeltaHeat(int i, double n, double H, double K)
        //{
        //    var inj = Data.Injections[i];
        //    var Qi = GetHeatContent(inj, n, H, K);
        //    var Q_i = i == 0 ? 0.0 : GetHeatContent(Data.Injections[i - 1], n, H, K);

        //    var dQi = Qi + (inj.Volume / Data.CellVolume) * ((Qi + Q_i) / 2.0) - Q_i;

        //    return dQi;
        //}

        //double GetHeatContent(InjectionData inj, double n, double H, double K)
        //{
        //    var ncell = n * inj.ActualCellConcentration;
        //    var first = (ncell * H * Data.CellVolume) / 2.0;
        //    var XnM = inj.ActualTitrantConcentration / ncell;
        //    var nKM = 1.0 / (K * ncell);
        //    var square = (1.0 + XnM + nKM);
        //    var root = (square * square) - 4.0 * XnM;

        //    return first * (1 + XnM + nKM - Math.Sqrt(root));
        //}

        public override Model GenerateSyntheticModel()
        {
            Model mdl = new CompetitiveBinding(Data.GetSynthClone(ModelCloneOptions));

            SetSynthModelParameters(mdl);

            return mdl;
        }

        new public class ModelSolution : SolutionInterface
        {
            Dictionary<string, ModelOption> opt => Model.ModelOptions;

            public Energy dHapp => Parameters[ParameterType.Enthalpy1].Energy;
            public FloatWithError Kapp => Parameters[ParameterType.Affinity1];
            public FloatWithError N => Parameters[ParameterType.Nvalue1];
            public Energy Offset => Parameters[ParameterType.Offset].Energy;

            public FloatWithError Kdapp => new FloatWithError(1) / Kapp;

            public FloatWithError K => Kapp * opt[WeakLigandAffinity].ParameterValue * opt[WeakLigandConc].DoubleValue;
            public FloatWithError Kd => new FloatWithError(1) / K;
            public Energy dH
            {
                get
                {
                    var dh = dHapp.FloatWithError;

                    var top = opt[WeakLigandEnthalpy].ParameterValue * opt[WeakLigandAffinity].ParameterValue * opt[WeakLigandConc].DoubleValue;
                    var btm = (1 + opt[WeakLigandAffinity].ParameterValue * opt[WeakLigandConc].DoubleValue);

                    dh += top / btm;

                    return dh.Energy;
                }
            }
            public Energy GibbsFreeEnergy => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K));
            public Energy TdS => GibbsFreeEnergy - dH;
            public Energy Entropy => TdS / TempKelvin;

            public ModelSolution(Model model, double[] parameters)
            {
                Model = model;
                BootstrapSolutions = new List<SolutionInterface>();
            }

            public override void ComputeErrorsFromBootstrapSolutions()
            {
                var enthalpies = BootstrapSolutions.Select(s => (s as ModelSolution).dHapp.FloatWithError.Value);
                var k = BootstrapSolutions.Select(s => (s as ModelSolution).Kapp.Value);
                var n = BootstrapSolutions.Select(s => (s as ModelSolution).N.Value);
                var offsets = BootstrapSolutions.Select(s => (s as ModelSolution).Offset.Value);

                Parameters[ParameterType.Enthalpy1] = new FloatWithError(enthalpies, dHapp);
                Parameters[ParameterType.Affinity1] = new FloatWithError(k, Kapp);
                Parameters[ParameterType.Nvalue1] = new FloatWithError(n, N);
                Parameters[ParameterType.Offset] = new FloatWithError(offsets, Offset);

                base.ComputeErrorsFromBootstrapSolutions();
            }

            public override List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
            {
                var output = base.UISolutionParameters(info);

                if (info.HasFlag(FinalFigureDisplayParameters.Nvalue)) output.Add(new("N", N.ToString("F2")));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new("Kdapp", Kdapp.AsDissociationConstant()));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new("Kd", Kd.AsDissociationConstant()));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new("∆H", dH.ToString(EnergyUnit.KiloJoule, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.TdS)) output.Add(new("-T∆S", TdS.ToString(EnergyUnit.KiloJoule, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new("∆G", GibbsFreeEnergy.ToString(EnergyUnit.KiloJoule, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Offset)) output.Add(new("Offset", Offset.ToString(EnergyUnit.KiloJoule, permole: true)));

                return output;
            }

            public override List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>> DependenciesToReport => new List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>>
                {
                    new (ParameterType.Enthalpy1, new(sol => (sol as ModelSolution).dH.FloatWithError)),
                    new (ParameterType.EntropyContribution1, new(sol => (sol as ModelSolution).TdS.FloatWithError)),
                    new (ParameterType.Gibbs1, new(sol => (sol as ModelSolution).GibbsFreeEnergy.FloatWithError)),
                };

            public override Dictionary<ParameterType, FloatWithError> ReportParameters => new Dictionary<ParameterType, FloatWithError>
                {
                    { ParameterType.Nvalue1, N },
                    { ParameterType.Affinity1, Kd },
                    { ParameterType.Enthalpy1, dH.FloatWithError },
                    { ParameterType.EntropyContribution1, TdS.FloatWithError} ,
                    { ParameterType.Gibbs1, GibbsFreeEnergy.FloatWithError },
                };
        }
    }
}

