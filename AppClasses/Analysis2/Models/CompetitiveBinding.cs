using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC.AppClasses.Analysis2.Models
{
    public class CompetitiveBinding : Model
    {
        public override AnalysisModel ModelType => AnalysisModel.CompetitiveBinding;

        double Kapp => Parameters.Table[ParameterType.Affinity1].Value / (ModelOptions[ModelOptionKey.PreboundLigandAffinity].ParameterValue * ModelOptions[ModelOptionKey.PreboundLigandConc].ParameterValue + 1);
        double dHapp
        {
            get
            {
                var top = ModelOptions[ModelOptionKey.PreboundLigandEnthalpy].ParameterValue * ModelOptions[ModelOptionKey.PreboundLigandAffinity].ParameterValue * ModelOptions[ModelOptionKey.PreboundLigandConc].ParameterValue;
                var btm = (1 + ModelOptions[ModelOptionKey.PreboundLigandAffinity].ParameterValue * ModelOptions[ModelOptionKey.PreboundLigandConc].ParameterValue);

                var dh = Parameters.Table[ParameterType.Enthalpy1].Value - top / btm;

                return dh;
            }
        }

        public CompetitiveBinding(ExperimentData data) : base(data)
        {
        }

        public override void InitializeParameters(ExperimentData data)
        {
            base.InitializeParameters(data);

            Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, GuessParameter(ParameterType.Nvalue1, this.GuessN()));
            Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, GuessParameter(ParameterType.Enthalpy1, this.GuessEnthalpy()));
            Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 1E8); //We expect very high affinity if this model is used
            Parameters.AddOrUpdateParameter(ParameterType.Offset, GuessParameter(ParameterType.Offset, this.GuessOffset()));

            ModelOptions.Add(AnalysisClasses.ModelOptions.Concentration(ModelOptionKey.PreboundLigandConc, "[Prebound ligand] (µM)", new FloatWithError(10e-6, 0)).DictionaryEntry);
            ModelOptions.Add(AnalysisClasses.ModelOptions.Parameter(ModelOptionKey.PreboundLigandEnthalpy, "Prebound ligand ∆H", new FloatWithError(-40000, 0)).DictionaryEntry);
            ModelOptions.Add(AnalysisClasses.ModelOptions.Affinity(ModelOptionKey.PreboundLigandAffinity, "Prebound ligand Kd", new(1000000, 0)).DictionaryEntry);
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            if (withoffset) return GetDeltaHeat(injectionindex, Parameters.Table[ParameterType.Nvalue1].Value, dHapp, Kapp) + Parameters.Table[ParameterType.Offset].Value * Data.Injections[injectionindex].InjectionMass;
            else return GetDeltaHeat(injectionindex, Parameters.Table[ParameterType.Nvalue1].Value, dHapp, Kapp);
        }

        double GetDeltaHeat(int i, double n, double H, double K)
        {
            return DeltaHeatFromHeatContent(i, (cm, cl) => GetHeatContent(cm, cl, n, H, K));
        }

        //double GetDeltaHeat(int i, double n, double H, double K)
        //{
        //    var inj = Data.Injections[i];
        //    var Qi = GetHeatContent(inj, n, H, K);
        //    var Q_i = i == 0 ? 0.0 : GetHeatContent(Data.Injections[i - 1], n, H, K);
        //
        //    var dQi = Qi + (inj.Volume / Data.CellVolume) * ((Qi + Q_i) / 2.0) - Q_i;
        //
        //    return dQi;
        //}

        double GetHeatContent(double cellConc, double titrantConc, double n, double H, double K)
        {
            var ncell = n * cellConc;
            var first = (ncell * H * Data.CellVolume) / 2.0;
            var XnM = titrantConc / ncell;
            var nKM = 1.0 / (K * ncell);
            var square = (1.0 + XnM + nKM);
            var root = (square * square) - 4.0 * XnM;

            return first * (1 + XnM + nKM - Math.Sqrt(root));
        }

        public override Model GenerateSyntheticModel()
        {
            Model mdl = new CompetitiveBinding(Data.GetSynthClone(ModelCloneOptions));

            SetSynthModelParameters(mdl);

            return mdl;
        }

        public class ModelSolution : SolutionInterface
        {
            IDictionary<ModelOptionKey, ModelOptions> opt => Model.ModelOptions;

            public Energy Enthalpy => Parameters[ParameterType.Enthalpy1].Energy;
            public FloatWithError K => Parameters[ParameterType.Affinity1];
            public FloatWithError N => Parameters[ParameterType.Nvalue1];
            public Energy Offset => Parameters[ParameterType.Offset].Energy;

            public FloatWithError Kd => new FloatWithError(1) / K;
            public Energy GibbsFreeEnergy => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K));
            public Energy TdS => GibbsFreeEnergy - Enthalpy;
            public Energy Entropy => TdS / TempKelvin;

            public FloatWithError Kapp => K / (opt[ModelOptionKey.PreboundLigandAffinity].ParameterValue * opt[ModelOptionKey.PreboundLigandConc].ParameterValue + 1);
            public FloatWithError Kdapp => new FloatWithError(1) / Kapp;
            public Energy dHapp
            {
                get
                {
                    var top = opt[ModelOptionKey.PreboundLigandEnthalpy].ParameterValue * opt[ModelOptionKey.PreboundLigandAffinity].ParameterValue * opt[ModelOptionKey.PreboundLigandConc].ParameterValue;
                    var btm = (1 + opt[ModelOptionKey.PreboundLigandAffinity].ParameterValue * opt[ModelOptionKey.PreboundLigandConc].ParameterValue);

                    var dh = Enthalpy.FloatWithError - top / btm;

                    return dh.Energy;
                }
            }
           

            public ModelSolution(Model model)
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

                Parameters[ParameterType.Enthalpy1] = new FloatWithError(enthalpies, Enthalpy);
                Parameters[ParameterType.Affinity1] = new FloatWithError(k, K);
                Parameters[ParameterType.Nvalue1] = new FloatWithError(n, N);
                Parameters[ParameterType.Offset] = new FloatWithError(offsets, Offset);

                base.ComputeErrorsFromBootstrapSolutions();
            }

            public override List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
            {
                var output = base.UISolutionParameters(info);

                if (info.HasFlag(FinalFigureDisplayParameters.Nvalue)) output.Add(new("N", N.AsNumber()));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utils.MarkdownStrings.ApparantDissociationConstant, Kdapp.AsFormattedConcentration(true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utils.MarkdownStrings.DissociationConstant, Kd.AsFormattedConcentration(true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(Utils.MarkdownStrings.Enthalpy, Enthalpy.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.TdS)) output.Add(new(Utils.MarkdownStrings.EntropyContribution, TdS.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(Utils.MarkdownStrings.GibbsFreeEnergy, GibbsFreeEnergy.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Offset)) output.Add(new("Offset", Offset.ToFormattedString(ReportEnergyUnit, permole: true)));

                return output;
            }

            public override List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>> DependenciesToReport => new List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>>
                {
                    new (ParameterType.Enthalpy1, new(sol => (sol as ModelSolution).Enthalpy.FloatWithError)),
                    new (ParameterType.EntropyContribution1, new(sol => (sol as ModelSolution).TdS.FloatWithError)),
                    new (ParameterType.Gibbs1, new(sol => (sol as ModelSolution).GibbsFreeEnergy.FloatWithError)),
                };

            public override Dictionary<ParameterType, FloatWithError> ReportParameters => new Dictionary<ParameterType, FloatWithError>
                {
                    { ParameterType.Nvalue1, N },
                    { ParameterType.Affinity1, Kd },
                    { ParameterType.Enthalpy1, Enthalpy.FloatWithError },
                    { ParameterType.EntropyContribution1, TdS.FloatWithError} ,
                    { ParameterType.Gibbs1, GibbsFreeEnergy.FloatWithError },
                };
        }
    }

	public class CompetitiveBindingOld : OneSetOfSites
	{
        public override AnalysisModel ModelType => AnalysisModel.CompetitiveBinding;

		public CompetitiveBindingOld(ExperimentData data) : base(data)
        {
        }

        public override void InitializeParameters(ExperimentData data)
        {
            base.InitializeParameters(data);

            ModelOptions.Add(AnalysisClasses.ModelOptions.Concentration(ModelOptionKey.PreboundLigandConc, "[Prebound ligand] (µM)", new FloatWithError(10e-6, 0)).DictionaryEntry);
            ModelOptions.Add(AnalysisClasses.ModelOptions.Parameter(ModelOptionKey.PreboundLigandEnthalpy, "Prebound ligand ∆H", new FloatWithError(-40000, 0)).DictionaryEntry);
            ModelOptions.Add(AnalysisClasses.ModelOptions.Affinity(ModelOptionKey.PreboundLigandAffinity, "Prebound ligand Kd", new(1000000, 0)).DictionaryEntry);
        }

        public override Model GenerateSyntheticModel()
        {
            Model mdl = new CompetitiveBindingOld(Data.GetSynthClone(ModelCloneOptions));

            SetSynthModelParameters(mdl);

            return mdl;
        }

        new public class ModelSolution : SolutionInterface
        {
            IDictionary<ModelOptionKey,ModelOptions> opt => Model.ModelOptions;

            public Energy dHapp => Parameters[ParameterType.Enthalpy1].Energy;
            public FloatWithError Kapp => Parameters[ParameterType.Affinity1];
            public FloatWithError N => Parameters[ParameterType.Nvalue1];
            public Energy Offset => Parameters[ParameterType.Offset].Energy;

            public FloatWithError Kdapp => new FloatWithError(1) / Kapp;

            public FloatWithError K => Kapp + Kapp * opt[ModelOptionKey.PreboundLigandAffinity].ParameterValue * opt[ModelOptionKey.PreboundLigandConc].ParameterValue;
            public FloatWithError Kd => new FloatWithError(1) / K;
            public Energy dH
            {
                get
                {
                    var dh = dHapp.FloatWithError;

                    var top = opt[ModelOptionKey.PreboundLigandEnthalpy].ParameterValue * opt[ModelOptionKey.PreboundLigandAffinity].ParameterValue * opt[ModelOptionKey.PreboundLigandConc].ParameterValue;
                    var btm = (1 + opt[ModelOptionKey.PreboundLigandAffinity].ParameterValue * opt[ModelOptionKey.PreboundLigandConc].ParameterValue);

                    dh += top / btm;

                    return dh.Energy;
                }
            }
            public Energy GibbsFreeEnergy => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K));
            public Energy TdS => GibbsFreeEnergy - dH;
            public Energy Entropy => TdS / TempKelvin;

            public ModelSolution(Model model)
            {
                Model = model;
                BootstrapSolutions = new List<SolutionInterface>();

                if (opt[ModelOptionKey.PreboundLigandConc].BoolValue) opt[ModelOptionKey.PreboundLigandConc].ParameterValue = Data.Attributes.Find(opt => opt.Key == ModelOptionKey.PreboundLigandConc).ParameterValue;
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

                if (info.HasFlag(FinalFigureDisplayParameters.Nvalue)) output.Add(new("N", N.AsNumber()));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utils.MarkdownStrings.ApparantDissociationConstant, Kdapp.AsFormattedConcentration(true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utils.MarkdownStrings.DissociationConstant, Kd.AsFormattedConcentration(true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(Utils.MarkdownStrings.Enthalpy, dH.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.TdS)) output.Add(new(Utils.MarkdownStrings.EntropyContribution, TdS.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(Utils.MarkdownStrings.GibbsFreeEnergy, GibbsFreeEnergy.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Offset)) output.Add(new("Offset", Offset.ToFormattedString(ReportEnergyUnit, permole: true)));

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

