using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Utilities;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC.AppClasses.Analysis2.Models
{
    public class OneSetOfSites : Model
	{
        public override AnalysisModel ModelType => AnalysisModel.OneSetOfSites;

        bool UseSyringeCorrectionMode => ModelOptions[AttributeKey.UseSyringeActiveFraction]?.BoolValue ?? false;

        public override double GuessAffinity()
        {
            return 1 / (Data.CellConcentration / 100); // Assuming user setup c = 100
        }

        public OneSetOfSites(ExperimentData data) : base(data)
		{
		}

        public override void InitializeParameters(ExperimentData data)
        {
			base.InitializeParameters(data);

			Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, PreviousOrDefault(ParameterType.Nvalue1, this.GuessN()));
            Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, PreviousOrDefault(ParameterType.Enthalpy1, this.GuessEnthalpy()));
            Parameters.AddOrUpdateParameter(ParameterType.Affinity1, PreviousOrDefault(ParameterType.Affinity1, this.GuessLogAffinity()));
            Parameters.AddOrUpdateParameter(ParameterType.Offset, PreviousOrDefault(ParameterType.Offset, this.GuessOffset()));

            ModelOptions.Add(ExperimentAttribute.Bool(AttributeKey.UseSyringeActiveFraction, AttributeKey.UseSyringeActiveFraction.GetProperties().Name, false).DictionaryEntry);
            ModelOptions.Add(ExperimentAttribute.Double(AttributeKey.NumberOfSites1, AttributeKey.NumberOfSites1.GetProperties().Name, 1).DictionaryEntry);
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
		{
            double logK = Parameters.Table[ParameterType.Affinity1].Value;
            double K = Math.Pow(10.0, logK);

            var dQ = GetDeltaHeat(
                    injectionindex,
                    Parameters.Table[ParameterType.Nvalue1].Value,
                    Parameters.Table[ParameterType.Enthalpy1].Value,
                    K);

            if (withoffset) return dQ + Parameters.Table[ParameterType.Offset].Value * Data.Injections[injectionindex].InjectionMass;
            else return dQ;
        }

        double GetDeltaHeat(int i, double n, double H, double K)
        {
            return DeltaHeatFromHeatContent(i, (cm, cl) => GetHeatContent(cm, cl, n, H, K));
        }

        double GetHeatContent(double cellConc, double titrantConc, double n, double H, double K)
        {
            double nc = UseSyringeCorrectionMode ? ModelOptions[AttributeKey.NumberOfSites1].DoubleValue : n;
            double ns = UseSyringeCorrectionMode ? n : 1;

            var ncell = nc * cellConc;
            var ntit = ns * titrantConc;
            var first = (ncell * H * Data.CellVolume) / 2.0;
            var XnM = ntit / ncell;
            var nKM = 1.0 / (K * ncell);
            var square = (1.0 + XnM + nKM);
            var root = (square * square) - 4.0 * XnM;

            return first * (1 + XnM + nKM - Math.Sqrt(root));
        }

        public override Model GenerateSyntheticModel()
        {
			Model mdl = new OneSetOfSites(Data.GetSynthClone(ModelCloneOptions));

            SetSynthModelParameters(mdl);

            return mdl;
        }

        public class ModelSolution : SolutionInterface
        {
            public Energy Enthalpy => Parameters[ParameterType.Enthalpy1].Energy;
            private FloatWithError LogK => Parameters[ParameterType.Affinity1];
            public FloatWithError K => FWEMath.Pow(10.0, LogK);
            public FloatWithError N => Parameters[ParameterType.Nvalue1];

            public FloatWithError Kd => 1.0 / K;
            public Energy GibbsFreeEnergy => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K));
            public Energy TdS => GibbsFreeEnergy - Enthalpy;
            public Energy Entropy => TdS / TempKelvin;

            public ModelSolution(Model model)
            {
                Model = model;
                BootstrapSolutions = new List<SolutionInterface>();
            }

            public override void ComputeErrorsFromBootstrapSolutions()
            {
                var enthalpies = BootstrapSolutions.Select(s => (s as ModelSolution).Enthalpy.FloatWithError.Value);
                var k = BootstrapSolutions.Select(s => (s as ModelSolution).LogK.Value);
                var n = BootstrapSolutions.Select(s => (s as ModelSolution).N.Value);
                var offsets = BootstrapSolutions.Select(s => (s as ModelSolution).Offset.Value);

                Parameters[ParameterType.Enthalpy1] = new FloatWithError(enthalpies, Enthalpy);
                Parameters[ParameterType.Affinity1] = new FloatWithError(k, LogK);
                Parameters[ParameterType.Nvalue1] = new FloatWithError(n, N);
                Parameters[ParameterType.Offset] = new FloatWithError(offsets, Offset);

                base.ComputeErrorsFromBootstrapSolutions();
            }

            public override List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
            {
                var output = base.UISolutionParameters(info);

                if (info.HasFlag(FinalFigureDisplayParameters.Nvalue))
                    if (UseSyringeCorrectionMode)
                    {
                        output.Add(new(MarkdownStrings.Alpha + "{syringe}", N.AsNumber()));
                        output.Add(new("N{fixed}", ModelOptions[AttributeKey.NumberOfSites1].DoubleValue.ToString("G2")));
                    }
                    else output.Add(new("N", N.AsNumber()));

                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(MarkdownStrings.DissociationConstant, Kd.AsFormattedConcentration(withunit: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(MarkdownStrings.Enthalpy, Enthalpy.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Entropy)) output.Add(new(MarkdownStrings.EntropyContribution, TdS.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(MarkdownStrings.GibbsFreeEnergy, GibbsFreeEnergy.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Offset)) output.Add(new("Offset", Offset.ToFormattedString(ReportEnergyUnit, permole: true)));

                return output;
            }

            public override List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>> DependenciesToReport => new()
            {
                    new (ParameterType.Enthalpy1, new(sol => (sol as ModelSolution).Enthalpy.FloatWithError)), 
                    new (ParameterType.EntropyContribution1, new(sol => (sol as ModelSolution).TdS.FloatWithError)),
                    new (ParameterType.Gibbs1, new(sol => (sol as ModelSolution).GibbsFreeEnergy.FloatWithError)),
                };

            public override Dictionary<ParameterType, FloatWithError> ReportParameters => new()
            {
                    { ParameterType.Nvalue1, N },
                    { ParameterType.Affinity1, Kd },
                    { ParameterType.Enthalpy1, Enthalpy.FloatWithError },
                    { ParameterType.EntropyContribution1, TdS.FloatWithError} ,
                    { ParameterType.Gibbs1, GibbsFreeEnergy.FloatWithError },
                };
        }
    }
}