using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics;

namespace AnalysisITC.AppClasses.Analysis2.Models
{
    public class TwoSetsOfSites : Model
	{
        const double sqrt3 = 1.73205080757;
        const double cube2 = 1.25992104989;

        public override AnalysisModel ModelType => AnalysisModel.TwoSetsOfSites;

        public double GuessN1() => Data.Injections.Last().Ratio / 3;
        public double GuessN2() => Data.Injections.Last().Ratio / 4;

        public double GuessAffinity1() => 10000000;
        public double GuessAffinity2() => 1000000;

        public TwoSetsOfSites(ExperimentData data) : base(data)
		{
			
		}

		public override void InitializeParameters(ExperimentData data)
		{
            base.InitializeParameters(data);

            Parameters.AddParameter(ParameterType.Nvalue1, GuessParameter(ParameterType.Nvalue1, this.GuessN1()));
            Parameters.AddParameter(ParameterType.Enthalpy1, GuessParameter(ParameterType.Enthalpy1, this.GuessEnthalpy()));
            Parameters.AddParameter(ParameterType.Affinity1, GuessParameter(ParameterType.Affinity1, this.GuessAffinity1()));
            Parameters.AddParameter(ParameterType.Nvalue2, GuessParameter(ParameterType.Nvalue2, this.GuessN2()));
            Parameters.AddParameter(ParameterType.Enthalpy2, GuessParameter(ParameterType.Enthalpy2, this.GuessEnthalpy() / 2));
            Parameters.AddParameter(ParameterType.Affinity2, GuessParameter(ParameterType.Affinity2, this.GuessAffinity2()));
            Parameters.AddParameter(ParameterType.Offset, GuessParameter(ParameterType.Offset, this.GuessOffset()));
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            if (withoffset) return GetDeltaHeat(injectionindex) + Parameters.Table[ParameterType.Offset].Value * Data.Injections[injectionindex].InjectionMass;
            else return GetDeltaHeat(injectionindex);
        }

        double Kd1;
        double Kd2;
        double N1;
        double N2;

        double GetDeltaHeat(int i)
        {
            Kd1 = 1 / Parameters.Table[ParameterType.Affinity1].Value;
            Kd2 = 1 / Parameters.Table[ParameterType.Affinity2].Value;
            N1 = Parameters.Table[ParameterType.Nvalue1].Value;
            N2 = Parameters.Table[ParameterType.Nvalue2].Value;

            var inj = Data.Injections[i];
            var Qi = GetHeatContent(inj);
            var Q_i = i == 0 ? 0.0 : GetHeatContent(Data.Injections[i - 1]);

            var dQi = Qi + (inj.Volume / Data.CellVolume) * ((Qi + Q_i) / 2.0) - Q_i;

            return dQi;
        }

        double GetHeatContent(InjectionData inj)
        {
            var p = Kd1 + Kd2 + (N1 + N2) * inj.ActualCellConcentration - inj.ActualTitrantConcentration;
            var q = (Kd2 * N1 + Kd1 * N2) * inj.ActualCellConcentration - (Kd1 + Kd2) * inj.ActualTitrantConcentration + Kd1 * Kd2;
            var r = -inj.ActualTitrantConcentration * Kd1 * Kd2;

            //var cuberoot = Math.Cbrt(-2 * p * p * p + 3 * sqrt3 * Math.Sqrt(4 * p * p * p * r - p * p * q * q - 18 * p * q * r + 4 * q * q * q + 27 * r * r) + 9 * p * q - 27 * r);

            ////term 1
            //var term1 = cuberoot / (3 * cube2);

            ////term2
            //var term2top = cube2 * (3 * q - p * p);
            //var term2btm = 3 * cuberoot;

            //var term2 = -term2top / term2btm;

            ////term3
            //var term3 = -p / 3;

            var x = FindFreeTitrant(p, q, r, inj.ActualTitrantConcentration * 0.05);

            var theta1 = (Parameters.Table[ParameterType.Affinity1].Value * x) / (Parameters.Table[ParameterType.Affinity1].Value * x + 1);
            var theta2 = (Parameters.Table[ParameterType.Affinity2].Value * x) / (Parameters.Table[ParameterType.Affinity2].Value * x + 1);

            var heat = inj.ActualCellConcentration * Data.CellVolume * (N1 * theta1 * Parameters.Table[ParameterType.Enthalpy1].Value + N2 * theta2 * Parameters.Table[ParameterType.Enthalpy2].Value);

            return heat;
        }

        double FindFreeTitrant(double p, double q, double r, double guess)
        {
            bool b = MathNet.Numerics.RootFinding.RobustNewtonRaphson.TryFindRoot(
                (x) => x * x * x + x * x * p + x * q + r,
                (x) => 3 * x * x + 2 * x * p + q,
                lowerBound: 0, upperBound: 1e-3,
                accuracy: 1e-32, maxIterations: 500, subdivision: 20,
                out double root);

            return root;
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
                var n1 = BootstrapSolutions.Select(s => (s as ModelSolution).N1.Value);
                var n2 = BootstrapSolutions.Select(s => (s as ModelSolution).N2.Value);
                var offsets = BootstrapSolutions.Select(s => (double)(s as ModelSolution).Offset);

                Parameters[ParameterType.Enthalpy1] = new FloatWithError(enthalpies1, Enthalpy1);
                Parameters[ParameterType.Affinity1] = new FloatWithError(k1, K1);
                Parameters[ParameterType.Nvalue1] = new FloatWithError(n1, N1);
                Parameters[ParameterType.Enthalpy2] = new FloatWithError(enthalpies2, Enthalpy2);
                Parameters[ParameterType.Affinity2] = new FloatWithError(k2, K2);
                Parameters[ParameterType.Nvalue2] = new FloatWithError(n2, N2);
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
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(Utils.MarkdownStrings.Enthalpy + "{1}", Enthalpy1.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(Utils.MarkdownStrings.Enthalpy + "{2}", Enthalpy2.ToFormattedString(ReportEnergyUnit, permole: true)));
                //if (info.HasFlag(FinalFigureDisplayParameters.TdS)) output.Add(new(Utils.MarkdownStrings.EntropyContribution, TdS.ToFormattedString(ReportEnergyUnit, permole: true))); // Perhaps TMI
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(Utils.MarkdownStrings.GibbsFreeEnergy + "{1}", GibbsFreeEnergy1.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(Utils.MarkdownStrings.GibbsFreeEnergy + "{2}", GibbsFreeEnergy2.ToFormattedString(ReportEnergyUnit, permole: true)));
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
                    { ParameterType.Nvalue2, N2 },
                    { ParameterType.Affinity2, Kd2 },
                    { ParameterType.Enthalpy2, Enthalpy2.FloatWithError },
                    { ParameterType.EntropyContribution2, TdS2.FloatWithError} ,
                    { ParameterType.Gibbs2, GibbsFreeEnergy2.FloatWithError },
                };
        }
    }
}

