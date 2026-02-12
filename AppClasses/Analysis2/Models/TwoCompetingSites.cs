using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.AppClasses.Analysis2.Models
{
    public class TwoCompetingSites : Model
    {
        public override AnalysisModel ModelType => AnalysisModel.TwoCompetingSites;

        double K1 => Parameters.Table[ParameterType.Affinity1].Value;
        double K2 => Parameters.Table[ParameterType.Affinity2].Value;

        double GetPL1(InjectionData inj, double pl2)
        {
            var nP = Parameters.Table[ParameterType.Nvalue1].Value * inj.ActualCellConcentration;

            var L = inj.ActualTitrantConcentration;
            var LnP = (L - nP);
            var sqrt = 1 + LnP * LnP * K1 * K1 + (2 * L + 2 * nP - 4 * pl2) * K1;
            var top = -Math.Sqrt(sqrt) + 1 + (L + nP - 2 * pl2) * K1;
            var btm = 2 * K1;

            return top / btm;
        }

        double GetPL2(InjectionData inj)
        {
            var nP = Parameters.Table[ParameterType.Nvalue1].Value * inj.ActualCellConcentration;

            var K1K2 = K1 + K2;
            var L = inj.ActualTitrantConcentration;
            var LnP = (L + nP);
            var sqrt = K1K2 * K1K2 * L * L - 2 * K1K2 * (-1 + K1K2 * nP) * L + (1 + K1K2 * nP) * (1 + K1K2 * nP);
            var top = K2 * (-Math.Sqrt(sqrt) + LnP * K2 + 1 + LnP * K1);
            var btm = 2 * (K1K2* K1K2);

            return top / btm;
        }

        public TwoCompetingSites(ExperimentData data) : base(data)
        {
        }

        public override void InitializeParameters(ExperimentData data)
        {
            base.InitializeParameters(data);

            Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, GuessParameter(ParameterType.Nvalue1, this.GuessN()));
            Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, GuessParameter(ParameterType.Enthalpy1, this.GuessEnthalpy()));
            Parameters.AddOrUpdateParameter(ParameterType.Affinity1, GuessParameter(ParameterType.Affinity1, 100*this.GuessAffinity()));
            Parameters.AddOrUpdateParameter(ParameterType.Enthalpy2, GuessParameter(ParameterType.Enthalpy2, 0.1*this.GuessEnthalpy()));
            Parameters.AddOrUpdateParameter(ParameterType.Affinity2, GuessParameter(ParameterType.Affinity2, this.GuessAffinity()));
            Parameters.AddOrUpdateParameter(ParameterType.Offset, GuessParameter(ParameterType.Offset, this.GuessOffset()));
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            if (withoffset) return GetDeltaHeat(injectionindex) + Parameters.Table[ParameterType.Offset].Value * Data.Injections[injectionindex].InjectionMass;
            else return GetDeltaHeat(injectionindex);
        }

        double GetDeltaHeat(int i)
        {
            var inj = Data.Injections[i];
            var Qi = GetHeatContent(inj);
            var Q_i = i == 0 ? 0.0 : GetHeatContent(Data.Injections[i - 1]);

            var dQi = Qi + (inj.Volume / Data.CellVolume) * ((Qi + Q_i) / 2.0) - Q_i;

            return dQi;
        }

        double GetHeatContent(InjectionData inj)
        {
            var pl2 = GetPL2(inj);
            var pl1 = GetPL1(inj, pl2);

            var dH1 = Data.CellVolume * pl1 * Parameters.Table[ParameterType.Enthalpy1].Value;
            var dH2 = Data.CellVolume * pl2 * Parameters.Table[ParameterType.Enthalpy2].Value;

            return dH1 + dH2;
        }

        public override Model GenerateSyntheticModel()
        {
            Model mdl = new TwoCompetingSites(Data.GetSynthClone(ModelCloneOptions));

            SetSynthModelParameters(mdl);

            return mdl;
        }

        public class ModelSolution : SolutionInterface
        {
            public Energy Enthalpy1 => new(Parameters[ParameterType.Enthalpy1]);
            public Energy Enthalpy2 => new(Parameters[ParameterType.Enthalpy2]);
            public FloatWithError K1 => Parameters[ParameterType.Affinity1];
            public FloatWithError K2 => Parameters[ParameterType.Affinity2];
            public FloatWithError N => Parameters[ParameterType.Nvalue1];
            public Energy Offset => new(Parameters[ParameterType.Offset]);

            public FloatWithError Kd1 => new FloatWithError(1) / K1;
            public Energy GibbsFreeEnergy1 => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K1));
            public Energy TdS1 => GibbsFreeEnergy1 - Enthalpy1;
            public Energy Entropy1 => TdS1 / TempKelvin;

            public FloatWithError Kd2 => new FloatWithError(1) / K2;
            public Energy GibbsFreeEnergy2 => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K2));
            public Energy TdS2 => GibbsFreeEnergy2 - Enthalpy2;
            public Energy Entropy2 => TdS2 / TempKelvin;

            public ModelSolution(Model model)
            {
                Model = model;
                BootstrapSolutions = new List<SolutionInterface>();
            }

            public override void ComputeErrorsFromBootstrapSolutions()
            {
                var enthalpies1 = BootstrapSolutions.Select(s => (s as ModelSolution).Enthalpy1.FloatWithError.Value);
                var enthalpies2 = BootstrapSolutions.Select(s => (s as ModelSolution).Enthalpy2.FloatWithError.Value);
                var k1 = BootstrapSolutions.Select(s => (s as ModelSolution).K1.Value);
                var k2 = BootstrapSolutions.Select(s => (s as ModelSolution).K2.Value);
                var n = BootstrapSolutions.Select(s => (s as ModelSolution).N.Value);
                var offsets = BootstrapSolutions.Select(s => (double)(s as ModelSolution).Offset);

                Parameters[ParameterType.Enthalpy1] = new FloatWithError(enthalpies1, Enthalpy1);
                Parameters[ParameterType.Affinity1] = new FloatWithError(k1, K1);
                Parameters[ParameterType.Nvalue1] = new FloatWithError(n, N);
                Parameters[ParameterType.Enthalpy2] = new FloatWithError(enthalpies2, Enthalpy2);
                Parameters[ParameterType.Affinity2] = new FloatWithError(k2, K2);
                Parameters[ParameterType.Offset] = new FloatWithError(offsets, Offset);

                base.ComputeErrorsFromBootstrapSolutions();
            }

            public override List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
            {
                var output = base.UISolutionParameters(info);

                if (info.HasFlag(FinalFigureDisplayParameters.Nvalue)) output.Add(new("N", N.AsNumber()));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utilities.MarkdownStrings.DissociationConstant + "{,1}", Kd1.AsFormattedConcentration(withunit: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utilities.MarkdownStrings.DissociationConstant + "{,2}", Kd2.AsFormattedConcentration(withunit: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(Utilities.MarkdownStrings.Enthalpy + "{1}", Enthalpy1.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(Utilities.MarkdownStrings.Enthalpy + "{2}", Enthalpy2.ToFormattedString(ReportEnergyUnit, permole: true)));
                //if (info.HasFlag(FinalFigureDisplayParameters.TdS)) output.Add(new(Utilities.MarkdownStrings.EntropyContribution, TdS.ToFormattedString(ReportEnergyUnit, permole: true))); // Perhaps TMI
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(Utilities.MarkdownStrings.GibbsFreeEnergy + "{1}", GibbsFreeEnergy1.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(Utilities.MarkdownStrings.GibbsFreeEnergy + "{2}", GibbsFreeEnergy2.ToFormattedString(ReportEnergyUnit, permole: true)));
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
                    { ParameterType.Nvalue1, N },
                    { ParameterType.Affinity1, Kd1 },
                    { ParameterType.Enthalpy1, Enthalpy1.FloatWithError },
                    { ParameterType.EntropyContribution1, TdS1.FloatWithError} ,
                    { ParameterType.Gibbs1, GibbsFreeEnergy1.FloatWithError },
                    { ParameterType.Affinity2, Kd2 },
                    { ParameterType.Enthalpy2, Enthalpy2.FloatWithError },
                    { ParameterType.EntropyContribution2, TdS2.FloatWithError} ,
                    { ParameterType.Gibbs2, GibbsFreeEnergy2.FloatWithError },
                };
        }
    }
}