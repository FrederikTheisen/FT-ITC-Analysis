using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.AppClasses.Analysis2.Models
{
    public class TwoSetsOfSites : Model
	{
        public override AnalysisModel ModelType => AnalysisModel.TwoSetsOfSites;

        public TwoSetsOfSites(ExperimentData data) : base(data)
		{
			throw new NotImplementedException("TwoSetsOfSites not implemented yet");
		}

		public override void InitializeParameters(ExperimentData data)
		{
            base.InitializeParameters(data);

            Parameters.AddParameter(ParameterType.Nvalue1, this.GuessN());
            Parameters.AddParameter(ParameterType.Enthalpy1, this.GuessEnthalpy() / 2);
            Parameters.AddParameter(ParameterType.Affinity1, this.GuessAffinity());
            Parameters.AddParameter(ParameterType.Nvalue2, this.GuessN());
            Parameters.AddParameter(ParameterType.Enthalpy2, this.GuessEnthalpy() / 2);
            Parameters.AddParameter(ParameterType.Affinity2, this.GuessAffinity());
            Parameters.AddParameter(ParameterType.Offset, this.GuessOffset());
        }

        public override Model GenerateSyntheticModel()
        {
            Model mdl = new TwoSetsOfSites(Data.GetSynthClone(ModelCloneOptions));

            foreach (var par in Parameters.Table)
            {
                mdl.Parameters.AddParameter(par.Key, par.Value.Value, par.Value.IsLocked);
            }

            return mdl;
        }

		public class ModelSolution : SolutionInterface
		{
            public new List<ModelSolution> BootstrapSolutions { get; set; }

            public Energy Enthalpy1 => new(Parameters[ParameterType.Enthalpy1]);
            public Energy Enthalpy2 => new(Parameters[ParameterType.Enthalpy2]);
            public FloatWithError K1 => Parameters[ParameterType.Affinity1];
            public FloatWithError K2 => Parameters[ParameterType.Affinity2];
            public FloatWithError N1 => Parameters[ParameterType.Nvalue1];
            public FloatWithError N2 => Parameters[ParameterType.Nvalue2];
            public Energy Offset => new(Parameters[ParameterType.Offset]);

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
                BootstrapSolutions = new List<ModelSolution>();
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

                Parameters[ParameterType.Enthalpy1] = new FloatWithError(enthalpies1, Enthalpy1);
                Parameters[ParameterType.Affinity1] = new FloatWithError(k1, K1);
                Parameters[ParameterType.Nvalue1] = new FloatWithError(n1, N1);
                Parameters[ParameterType.Enthalpy1] = new FloatWithError(enthalpies2, Enthalpy2);
                Parameters[ParameterType.Affinity1] = new FloatWithError(k2, K2);
                Parameters[ParameterType.Nvalue1] = new FloatWithError(n2, N2);
                Parameters[ParameterType.Offset] = new FloatWithError(offsets, Offset);

                base.ComputeErrorsFromBootstrapSolutions();
            }

            public override List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
            {
                var output = base.UISolutionParameters(info);

                if (info.HasFlag(FinalFigureDisplayParameters.Nvalue)) output.Add(new("N{1}", N1.AsNumber()));
                if (info.HasFlag(FinalFigureDisplayParameters.Nvalue)) output.Add(new("N{2}", N2.AsNumber()));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utils.MarkdownStrings.DissociationConstant + "{,1}", Kd1.AsFormattedConcentration(withunit: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utils.MarkdownStrings.DissociationConstant + "{,2}", Kd2.AsFormattedConcentration(withunit: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(Utils.MarkdownStrings.Enthalpy + "{,1}", Enthalpy1.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(Utils.MarkdownStrings.Enthalpy + "{,2}", Enthalpy2.ToFormattedString(ReportEnergyUnit, permole: true)));
                //if (info.HasFlag(FinalFigureDisplayParameters.TdS)) output.Add(new(Utils.MarkdownStrings.EntropyContribution, TdS.ToFormattedString(ReportEnergyUnit, permole: true))); // Perhaps TMI
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(Utils.MarkdownStrings.GibbsFreeEnergy + "{,1}", GibbsFreeEnergy1.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(Utils.MarkdownStrings.GibbsFreeEnergy + "{,2}", GibbsFreeEnergy2.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Offset)) output.Add(new("Offset", Offset.ToFormattedString(ReportEnergyUnit, permole: true)));

                return output;
            }

            public override List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>> DependenciesToReport => new List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>>
                {
                    new (ParameterType.Enthalpy1, new(sol => (sol as ModelSolution).Enthalpy1.FloatWithError)),
                    new (ParameterType.EntropyContribution1, new(sol => (sol as ModelSolution).TdS1.FloatWithError)),
                    new (ParameterType.Gibbs1, new(sol => (sol as ModelSolution).GibbsFreeEnergy1.FloatWithError)),
                };

            public override Dictionary<ParameterType, FloatWithError> ReportParameters => new Dictionary<ParameterType, FloatWithError>
                {
                    { ParameterType.Nvalue1, N1 },
                    { ParameterType.Affinity1, Kd1 },
                    { ParameterType.Enthalpy1, Enthalpy1.FloatWithError },
                    { ParameterType.EntropyContribution1, TdS1.FloatWithError} ,
                    { ParameterType.Gibbs1, GibbsFreeEnergy1.FloatWithError },
                };
        }
    }
}

