using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC.AppClasses.Analysis2.Models
{
	public class CompetitiveBinding : OneSetOfSites
	{
        public override AnalysisModel ModelType => AnalysisModel.CompetitiveBinding;

		public CompetitiveBinding(ExperimentData data) : base(data)
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
            Model mdl = new CompetitiveBinding(Data.GetSynthClone(ModelCloneOptions));

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

            public ModelSolution(Model model, double[] parameters)
            {
                Model = model;
                BootstrapSolutions = new List<SolutionInterface>();

                if (opt[ModelOptionKey.PreboundLigandConc].BoolValue) opt[ModelOptionKey.PreboundLigandConc].ParameterValue = Data.ExperimentOptions[ModelOptionKey.PreboundLigandConc].ParameterValue;
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
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utils.MarkdownStrings.ApparantDissociationConstant, Kdapp.AsDissociationConstant()));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utils.MarkdownStrings.DissociationConstant, Kd.AsDissociationConstant()));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(Utils.MarkdownStrings.Enthalpy, dH.ToString(EnergyUnit.KiloJoule, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.TdS)) output.Add(new(Utils.MarkdownStrings.EntropyContribution, TdS.ToString(EnergyUnit.KiloJoule, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(Utils.MarkdownStrings.GibbsFreeEnergy, GibbsFreeEnergy.ToString(EnergyUnit.KiloJoule, permole: true)));
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

