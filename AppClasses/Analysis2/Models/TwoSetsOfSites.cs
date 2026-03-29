using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.Utilities;
using MathNet.Numerics;

namespace AnalysisITC.AppClasses.Analysis2.Models
{
    public class TwoSetsOfSites : Model
	{
        const double sqrt3 = 1.73205080757;
        const double cube2 = 1.25992104989;

        public override AnalysisModel ModelType => AnalysisModel.TwoSetsOfSites;

        public double GuessN1() => 1;
        public double GuessN2() => 1;

        public double GuessLogAffinity1() => Math.Log10(1 / (Data.CellConcentration / 100));
        public double GuessLogAffinity2() => Math.Log10(1 / Data.CellConcentration);

        bool UseSyringeCorrectionMode => ModelOptions[AttributeKey.UseSyringeActiveFraction]?.BoolValue ?? false;

        double GetSyringeFactor()
        {
            return UseSyringeCorrectionMode ? Parameters.Table[ParameterType.Nvalue1].Value : 1.0;
        }

        double GetSiteStoichiometry1()
        {
            return UseSyringeCorrectionMode
                ? ModelOptions[AttributeKey.NumberOfSites1].DoubleValue
                : Parameters.Table[ParameterType.Nvalue1].Value;
        }

        double GetSiteStoichiometry2()
        {
            return UseSyringeCorrectionMode
                ? ModelOptions[AttributeKey.NumberOfSites2].DoubleValue
                : Parameters.Table[ParameterType.Nvalue2].Value;
        }

        public TwoSetsOfSites(ExperimentData data) : base(data)
		{
			
		}

		public override void InitializeParameters(ExperimentData data)
		{
            base.InitializeParameters(data);

            Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, PreviousOrDefault(ParameterType.Nvalue1, this.GuessN1()));
            Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, PreviousOrDefault(ParameterType.Enthalpy1, this.GuessEnthalpy()));
            Parameters.AddOrUpdateParameter(ParameterType.Affinity1, PreviousOrDefault(ParameterType.Affinity1, this.GuessLogAffinity1()));
            Parameters.AddOrUpdateParameter(ParameterType.Nvalue2, PreviousOrDefault(ParameterType.Nvalue2, this.GuessN2()));
            Parameters.AddOrUpdateParameter(ParameterType.Enthalpy2, PreviousOrDefault(ParameterType.Enthalpy2, this.EnthalpyMax()));
            Parameters.AddOrUpdateParameter(ParameterType.Affinity2, PreviousOrDefault(ParameterType.Affinity2, this.GuessLogAffinity2()));
            Parameters.AddOrUpdateParameter(ParameterType.Offset, PreviousOrDefault(ParameterType.Offset, this.GuessOffset()));

            ModelOptions.Add(ExperimentAttribute.Bool(AttributeKey.LockDuplicateParameter, AttributeKey.LockDuplicateParameter.GetProperties().Name, false).DictionaryEntry);
            ModelOptions.Add(ExperimentAttribute.Bool(AttributeKey.UseSyringeActiveFraction, AttributeKey.UseSyringeActiveFraction.GetProperties().Name, false).DictionaryEntry);
            ModelOptions.Add(ExperimentAttribute.Double(AttributeKey.NumberOfSites1, "1^{st} " + AttributeKey.NumberOfSites1.GetProperties().Name, 1).DictionaryEntry);
            ModelOptions.Add(ExperimentAttribute.Double(AttributeKey.NumberOfSites2, "2^{nd} " + AttributeKey.NumberOfSites2.GetProperties().Name, 1).DictionaryEntry);
        }

        public override void ApplyModelOptions()
        {
            base.ApplyModelOptions();

            if (ModelOptions[AttributeKey.LockDuplicateParameter].BoolValue || UseSyringeCorrectionMode)
            {
                // Sets the parameter to be the same as N-value 1, also sets the parameter to not be fitted
                // If Use Syringe Factor, we just don't need this parameter and IsGlobalFitted removes it from the parameter list
                // Possibly Locked would be more intuitive
                Parameters.Table[ParameterType.Nvalue2].SetGlobal(Parameters.Table[ParameterType.Nvalue1].Value);
            }
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            if (withoffset) return GetDeltaHeat(injectionindex) + Parameters.Table[ParameterType.Offset].Value * Data.Injections[injectionindex].InjectionMass;
            else return GetDeltaHeat(injectionindex);
        }

        double K1;
        double K2;
        double Kd1;
        double Kd2;
        double N1;
        double N2;

        double GetDeltaHeat(int i)
        {
            K1 = Math.Pow(10, Parameters.Table[ParameterType.Affinity1].Value);
            K2 = Math.Pow(10, Parameters.Table[ParameterType.Affinity2].Value);
            Kd1 = 1 / K1;
            Kd2 = 1 / K2;
            N1 = GetSiteStoichiometry1();
            N2 = GetSiteStoichiometry2();

            return DeltaHeatFromHeatContent(i, (cm, cl) => GetHeatContent(cm, cl));
        }

        double GetHeatContent(double cellConc, double titrantConc)
        {
            var titrant = titrantConc * GetSyringeFactor();

            var p = Kd1 + Kd2 + (N1 + N2) * cellConc - titrant;
            var q = (Kd2 * N1 + Kd1 * N2) * cellConc - (Kd1 + Kd2) * titrant + Kd1 * Kd2;
            var r = -titrant * Kd1 * Kd2;

            var x = FindFreeTitrant(p, q, r, titrant * 0.05);

            var theta1 = (K1 * x) / (K1 * x + 1);
            var theta2 = (K2 * x) / (K2 * x + 1);

            return cellConc * Data.CellVolume *
                   (N1 * theta1 * Parameters.Table[ParameterType.Enthalpy1].Value +
                    N2 * theta2 * Parameters.Table[ParameterType.Enthalpy2].Value);
        }

        double FindFreeTitrant(double p, double q, double r, double guess)
        {
            if (double.IsFinite(p) && double.IsFinite(q) && double.IsFinite(r))
            {
                bool b = MathNet.Numerics.RootFinding.RobustNewtonRaphson.TryFindRoot(
                    (x) => x * x * x + x * x * p + x * q + r,
                    (x) => 3 * x * x + 2 * x * p + q,
                    lowerBound: 0, upperBound: 1e-3,
                    accuracy: 1e-32, maxIterations: 500, subdivision: 20,
                    out double root);

                return root;
            }

            // Unholy...
            return guess;
        }

        public override Model GenerateSyntheticModel()
        {
            Model mdl = new TwoSetsOfSites(Data.GetSynthClone(ModelCloneOptions));

            SetSynthModelParameters(mdl);

            return mdl;
        }

		public class ModelSolution : SolutionInterface
		{
            public Energy Enthalpy1 => new(Parameters[ParameterType.Enthalpy1]);
            public Energy Enthalpy2 => new(Parameters[ParameterType.Enthalpy2]);
            private FloatWithError LogK1 => Parameters[ParameterType.Affinity1];
            public FloatWithError K1 => FWEMath.Pow(10, LogK1);
            private FloatWithError LogK2 => Parameters[ParameterType.Affinity2];
            public FloatWithError K2 => FWEMath.Pow(10, LogK2);
            public FloatWithError N1 => Parameters[ParameterType.Nvalue1];
            public FloatWithError N2 => Parameters[ParameterType.Nvalue2];

            public FloatWithError Kd1 => 1.0 / K1;
            public Energy GibbsFreeEnergy1 => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K1));
            public Energy TdS1 => GibbsFreeEnergy1 - Enthalpy1;
            public Energy Entropy1 => TdS1 / TempKelvin;

            public FloatWithError Kd2 => 1.0 / K2;
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
                var enthalpies1 = BootstrapSolutions.Select(s => (s as ModelSolution).Enthalpy1.Value);
                var enthalpies2 = BootstrapSolutions.Select(s => (s as ModelSolution).Enthalpy2.Value);
                var k1 = BootstrapSolutions.Select(s => (s as ModelSolution).LogK1.Value);
                var k2 = BootstrapSolutions.Select(s => (s as ModelSolution).LogK2.Value);
                var n1 = BootstrapSolutions.Select(s => (s as ModelSolution).N1.Value);
                var n2 = BootstrapSolutions.Select(s => (s as ModelSolution).N2.Value);
                var offsets = BootstrapSolutions.Select(s => (double)(s as ModelSolution).Offset);

                Parameters[ParameterType.Enthalpy1] = new FloatWithError(enthalpies1, Enthalpy1);
                Parameters[ParameterType.Affinity1] = new FloatWithError(k1, LogK1);
                Parameters[ParameterType.Nvalue1] = new FloatWithError(n1, N1);
                Parameters[ParameterType.Enthalpy2] = new FloatWithError(enthalpies2, Enthalpy2);
                Parameters[ParameterType.Affinity2] = new FloatWithError(k2, LogK2);
                Parameters[ParameterType.Nvalue2] = new FloatWithError(n2, N2);
                Parameters[ParameterType.Offset] = new FloatWithError(offsets, Offset);

                base.ComputeErrorsFromBootstrapSolutions();
            }

            public override List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
            {
                var output = base.UISolutionParameters(info);

                // Site 1
                if (info.HasFlag(FinalFigureDisplayParameters.Nvalue))
                    if (UseSyringeCorrectionMode)
                    {
                        output.Add(new(MarkdownStrings.Alpha + "{syringe}", N1.AsNumber()));
                        output.Add(new("N{1,fixed}", StoichiometryOptions.FormatStoichiometry(ModelOptions[AttributeKey.NumberOfSites1].DoubleValue)));
                    }
                    else output.Add(new("N{1}", N1.AsNumber()));

                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utilities.MarkdownStrings.DissociationConstant + "{,1}", Kd1.AsFormattedConcentration(withunit: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(Utilities.MarkdownStrings.Enthalpy + "{1}", Enthalpy1.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(Utilities.MarkdownStrings.GibbsFreeEnergy + "{1}", GibbsFreeEnergy1.ToFormattedString(ReportEnergyUnit, permole: true)));

                // Site 2
                if (info.HasFlag(FinalFigureDisplayParameters.Nvalue))
                    if (UseSyringeCorrectionMode)
                    {
                        output.Add(new("N{2,fixed}", StoichiometryOptions.FormatStoichiometry(ModelOptions[AttributeKey.NumberOfSites2].DoubleValue)));
                    }
                    else output.Add(new("N{2}", N2.AsNumber()));

                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utilities.MarkdownStrings.DissociationConstant + "{,2}", Kd2.AsFormattedConcentration(withunit: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(Utilities.MarkdownStrings.Enthalpy + "{2}", Enthalpy2.ToFormattedString(ReportEnergyUnit, permole: true)));                
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(Utilities.MarkdownStrings.GibbsFreeEnergy + "{2}", GibbsFreeEnergy2.ToFormattedString(ReportEnergyUnit, permole: true)));

                // Offset
                if (info.HasFlag(FinalFigureDisplayParameters.Offset)) output.Add(new("Offset", Offset.ToFormattedString(ReportEnergyUnit, permole: true)));

                return output;
            }

            public override List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>> DependenciesToReport => new()
            {
                    // Interaction 1
                    new (ParameterType.Enthalpy1, new(sol => (sol as ModelSolution).Enthalpy1.FloatWithError)),
                    new (ParameterType.EntropyContribution1, new(sol => (sol as ModelSolution).TdS1.FloatWithError)),
                    new (ParameterType.Gibbs1, new(sol => (sol as ModelSolution).GibbsFreeEnergy1.FloatWithError)),

                    // Interaction 2
                    new (ParameterType.Enthalpy2, new(sol => (sol as ModelSolution).Enthalpy2.FloatWithError)),
                    new (ParameterType.EntropyContribution2, new(sol => (sol as ModelSolution).TdS2.FloatWithError)),
                    new (ParameterType.Gibbs2, new(sol => (sol as ModelSolution).GibbsFreeEnergy2.FloatWithError)),
                };

            public override Dictionary<ParameterType, FloatWithError> ReportParameters
            {
                get
                {
                    var dict = new Dictionary<ParameterType, FloatWithError>()
                    { 
                        // Site 1
                        { ParameterType.Affinity1, Kd1 },
                        { ParameterType.Enthalpy1, Enthalpy1.FloatWithError },
                        { ParameterType.EntropyContribution1, TdS1.FloatWithError} ,
                        { ParameterType.Gibbs1, GibbsFreeEnergy1.FloatWithError },

                        // Site 2
                        { ParameterType.Affinity2, Kd2 },
                        { ParameterType.Enthalpy2, Enthalpy2.FloatWithError },
                        { ParameterType.EntropyContribution2, TdS2.FloatWithError} ,
                        { ParameterType.Gibbs2, GibbsFreeEnergy2.FloatWithError },
                    };

                    if (UseSyringeCorrectionMode)
                    {
                        dict.Add(ParameterType.Nvalue1, N1);
                    }
                    else
                    {
                        dict.Add(ParameterType.Nvalue1, N1);
                        dict.Add(ParameterType.Nvalue2, N2);
                    }

                    return dict;
                }
                
            }
        }
    }
}

