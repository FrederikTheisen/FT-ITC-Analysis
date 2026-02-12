using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC.AppClasses.Analysis2.Models
{
    public class OneSiteIsomerization : Model
    {
        public override AnalysisModel ModelType => AnalysisModel.PeptideProlineIsomerization;

        const string PeptideInCellOption = "Petide in cell";

        public OneSiteIsomerization(ExperimentData data) : base(data)
        {
        }

        public override void InitializeParameters(ExperimentData data)
        {
            base.InitializeParameters(data);

            Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, this.GuessN());
            Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, this.GuessEnthalpy());
            Parameters.AddOrUpdateParameter(ParameterType.Affinity1, this.GuessAffinity());
            Parameters.AddOrUpdateParameter(ParameterType.Offset, this.GuessOffset());
            //Parameters.AddOrUpdateParameter(ParameterType.IsomerizationEquilibriumConstant, 0.42, islocked: true);

            ModelOptions.Add(AnalysisClasses.ModelOptions.Parameter(ModelOptionKey.Percentage, "*Cis* population (0-100%)", new FloatWithError(0.37,0.02)).DictionaryEntry);
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            if (withoffset) return GetDeltaHeat(injectionindex, Parameters.Table[ParameterType.Nvalue1].Value, Parameters.Table[ParameterType.Enthalpy1].Value, Parameters.Table[ParameterType.Affinity1].Value) + Parameters.Table[ParameterType.Offset].Value * Data.Injections[injectionindex].InjectionMass;
            else return GetDeltaHeat(injectionindex, Parameters.Table[ParameterType.Nvalue1].Value, Parameters.Table[ParameterType.Enthalpy1].Value, Parameters.Table[ParameterType.Affinity1].Value);
        }

        double[] GetBoundIsomerConcentrations(InjectionData inj)
        {
            //Figure out which component isomerises
            //Whatever is in the cell is multiplied by the n-value
            var totpep = ModelOptions[ModelOptionKey.PeptideInCell].BoolValue ? Parameters.Table[ParameterType.Nvalue1].Value * inj.ActualCellConcentration : inj.ActualTitrantConcentration;
            var totprot = ModelOptions[ModelOptionKey.PeptideInCell].BoolValue ? inj.ActualTitrantConcentration : Parameters.Table[ParameterType.Nvalue1].Value * inj.ActualCellConcentration;

            var KxTot = Parameters.Table[ParameterType.IsomerizationEquilibriumConstant].Value * totpep;
            var cis = KxTot / (1 + Parameters.Table[ParameterType.IsomerizationEquilibriumConstant].Value);
            var trans = totpep - cis;

            var f_cisbound = GetFractionCisBound(
                Parameters.Table[ParameterType.Affinity2].Value,
                Parameters.Table[ParameterType.Affinity1].Value,
                totprot, cis, trans);
            var f_transbound = GetFractionTransBound(
                Parameters.Table[ParameterType.Affinity2].Value,
                Parameters.Table[ParameterType.Affinity1].Value,
                totprot, cis, trans);

            return new double[2] { trans * f_transbound, cis * f_cisbound };
        }

        /// <summary>
        /// Get preinjection isomer concentrations
        /// </summary>
        /// <param name="inj"></param>
        /// <returns>double[2] {trans, cis}</returns>
        double[] GetInitialIsomerBoundConcentration(InjectionData inj)
        {
            if (inj.ID == 0)
            {
                /* trans <=> cis
                 * K = cis / trans
                 * K = cis / (Tot-cis)
                 * K * (Tot - cis) = cis
                 * K*tot - K*cis = cis
                 * K*tot = cis + K*cis = cis * (1 + K)
                 * K*tot / (1 + K) = cis
                 * trans = tot - cis
                 */
                //var tot = ModelOptions[ModelOptionKey.PeptideInCell].BoolValue ? inj.ActualCellConcentration : inj.ActualTitrantConcentration;
                //var KxTot = Parameters.Table[ParameterType.IsomerizationEquilibriumConstant].Value * tot;
                //var cis = KxTot / (1 + Parameters.Table[ParameterType.IsomerizationEquilibriumConstant].Value);
                //var trans = tot - cis;

                return new double[] { 0, 0 };
            }
            else
            {
                var _inj = Data.Injections[inj.ID - 1];

                return GetBoundIsomerConcentrations(_inj);
            }
        }

        double[] GetFinalIsomerBoundConcentration(InjectionData inj)
        {
            return GetBoundIsomerConcentrations(inj);
        }

        double GetD(double Kd_cis, double Kd_trans, double conc_binding, double conc_cis, double conc_trans) => Kd_cis + Kd_trans + conc_cis + conc_trans - conc_binding;
        double GetE(double Kd_cis, double Kd_trans, double conc_binding, double conc_cis, double conc_trans) => (conc_trans - conc_binding) * Kd_cis + (conc_cis - conc_binding) * Kd_trans + Kd_cis * Kd_trans;
        double GetF(double Kd_cis, double Kd_trans, double conc_binding) => -Kd_cis * Kd_trans * conc_binding;
        double GetTheta(double d, double e, double f)
        {
            double top = -2 * Math.Pow(d, 3) + 9 * d * e - 27 * f;
            double bottom = 2 * Math.Sqrt(Math.Pow(d, 2) - 3 * e) * Math.Pow(d, 2);

            return Math.Acos(top / bottom);
        }
        double GetFractionLigandBound(double Kd_cis, double Kd_trans, double conc_binding, double conc_cis, double conc_trans)
        {
            double d = GetD(Kd_cis, Kd_trans, conc_binding, conc_cis, conc_trans);
            double e = GetE(Kd_cis, Kd_trans, conc_binding, conc_cis, conc_trans);
            double f = GetF(Kd_cis, Kd_trans, conc_binding);
            double theta = GetTheta(d, e, f);

            double top = 2 * Math.Sqrt(Math.Pow(d, 2) - 3 * e) * Math.Cos(theta / 3) - d;
            double bottom = 3 * Kd_cis + 2 * Math.Sqrt(Math.Pow(d, 2) - 3 * e) * Math.Cos(theta / 3) - d;

            return top / bottom;
        }
        double GetFractionCisBound(double Kd_cis, double Kd_trans, double conc_binding, double conc_cis, double conc_trans) => GetFractionLigandBound(Kd_cis, Kd_trans, conc_binding, conc_cis, conc_trans);
        double GetFractionTransBound(double Kd_cis, double Kd_trans, double conc_binding, double conc_cis, double conc_trans) => GetFractionLigandBound(Kd_trans, Kd_cis, conc_binding, conc_trans, conc_cis);

        double GetDeltaHeat(int i, double n, double H, double Kapp)
        {
            var inj = Data.Injections[i];
            var Qi = GetHeatContent(inj, n, H, Kapp);
            var Q_i = i == 0 ? 0.0 : GetHeatContent(Data.Injections[i - 1], n, H, Kapp);

            var dQi = Qi + (inj.Volume / Data.CellVolume) * ((Qi + Q_i) / 2.0) - Q_i;

            return dQi;
        }

        double GetHeatContent(InjectionData inj, double n, double H, double Kapp)
        {
            //var bound = GetFinalIsomerBoundConcentration(inj);
            //var prev = GetInitialIsomerBoundConcentration(inj);

            //return Parameters.Table[ParameterType.Enthalpy1].Value * (bound[0] + bound[1]);

            //var Kapp = K * ModelOptions[ModelOptionKey.EquilibriumConstant].ParameterValue.Value; 

            var ncell = n * inj.ActualCellConcentration;
            var first = (ncell * H * Data.CellVolume) / 2.0;
            var XnM = inj.ActualTitrantConcentration / ncell;
            var nKM = 1.0 / (Kapp * ncell);
            var square = (1.0 + XnM + nKM);
            var root = (square * square) - 4.0 * XnM;

            return first * (1 + XnM + nKM - Math.Sqrt(root));
        }

        public override Model GenerateSyntheticModel()
        {
            Model mdl = new OneSiteIsomerization(Data.GetSynthClone(ModelCloneOptions));

            SetSynthModelParameters(mdl);

            //Bootstrap value for error estimation //TODO does this work with LeaveOneOut?
            var limits = ParameterType.CisIsomerPopulationPercentage.GetProperties().DefaultLimits;
            mdl.ModelOptions[ModelOptionKey.Percentage].ParameterValue =
                new FloatWithError(Math.Clamp(ModelOptions[ModelOptionKey.Percentage].ParameterValue.Sample(), limits[0], limits[1]), 0);

            return mdl;
        }

        public class ModelSolution : SolutionInterface
        {
            public Energy Enthalpy => Parameters[ParameterType.Enthalpy1].Energy;
            public FloatWithError K_app => Parameters[ParameterType.Affinity1];
            public FloatWithError K => 1/(Kd_app / (1 + 1/IsomerizationEquilibriumConstant));
            public FloatWithError N => Parameters[ParameterType.Nvalue1];
            public Energy Offset => Parameters[ParameterType.Offset].Energy;
            public FloatWithError PercentageCis => Model.ModelOptions[ModelOptionKey.Percentage].ParameterValue;
            public FloatWithError IsomerizationEquilibriumConstant => PercentageCis / (1 - PercentageCis);

            public FloatWithError Kd => new FloatWithError(1) / K;
            public FloatWithError Kd_app => new FloatWithError(1) / K_app;
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
                var k = BootstrapSolutions.Select(s => (s as ModelSolution).K_app.Value);
                var n = BootstrapSolutions.Select(s => (s as ModelSolution).N.Value);
                var offsets = BootstrapSolutions.Select(s => (s as ModelSolution).Offset.Value);
                //var keq = BootstrapSolutions.Select(s => (s as ModelSolution).IsomerizationEquilibriumConstant.Value);

                Parameters[ParameterType.Enthalpy1] = new FloatWithError(enthalpies, Enthalpy);
                Parameters[ParameterType.Affinity1] = new FloatWithError(k, K_app);
                Parameters[ParameterType.Nvalue1] = new FloatWithError(n, N);
                Parameters[ParameterType.Offset] = new FloatWithError(offsets, Offset);
                //Parameters[ParameterType.IsomerizationEquilibriumConstant] = new FloatWithError(keq, IsomerizationEquilibriumConstant);

                base.ComputeErrorsFromBootstrapSolutions();
            }

            public override List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
            {
                var output = base.UISolutionParameters(info);

                if (info.HasFlag(FinalFigureDisplayParameters.Nvalue)) output.Add(new("N", N.AsNumber()));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity))
                {
                    output.Add(new(Utilities.MarkdownStrings.DissociationConstant, Kd.AsFormattedConcentration(true)));
                    if (info.HasFlag(FinalFigureDisplayParameters.Misc))
                    {
                        output.Add(new(Utilities.MarkdownStrings.ApparentDissociationConstant, Kd_app.AsFormattedConcentration(true)));
                        output.Add(new(Utilities.MarkdownStrings.IsomerizationEquilibriumConstant, IsomerizationEquilibriumConstant.AsNumber()));
                    }
                }
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(Utilities.MarkdownStrings.Enthalpy, Enthalpy.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.TdS)) output.Add(new(Utilities.MarkdownStrings.EntropyContribution, TdS.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(Utilities.MarkdownStrings.GibbsFreeEnergy, GibbsFreeEnergy.ToFormattedString(ReportEnergyUnit, permole: true)));

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
                    { ParameterType.ApparentAffinity, Kd_app },
                    { ParameterType.Affinity1, Kd },
                    { ParameterType.IsomerizationEquilibriumConstant, IsomerizationEquilibriumConstant },
                    { ParameterType.Enthalpy1, Enthalpy.FloatWithError },
                    { ParameterType.EntropyContribution1, TdS.FloatWithError} ,
                    { ParameterType.Gibbs1, GibbsFreeEnergy.FloatWithError },
                };
        }
    }
}

