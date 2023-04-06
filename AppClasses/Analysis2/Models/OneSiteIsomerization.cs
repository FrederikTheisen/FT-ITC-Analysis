using System;
using System.Collections.Generic;
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

            Parameters.AddParameter(ParameterType.Nvalue1, this.GuessN());
            Parameters.AddParameter(ParameterType.Enthalpy1, this.GuessEnthalpy());
            Parameters.AddParameter(ParameterType.Affinity1, this.GuessAffinity());
            Parameters.AddParameter(ParameterType.Affinity2, this.GuessAffinity());
            Parameters.AddParameter(ParameterType.Offset, this.GuessOffset());
            Parameters.AddParameter(ParameterType.IsomerizationEquilibriumConstant, 0.42, islocked: true);
            Parameters.AddParameter(ParameterType.IsomerizationRate, 0.001, islocked: true);

            ModelOptions.Add(PeptideInCellOption, ModelOption.Bool(PeptideInCellOption, true));
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            if (withoffset) return GetDeltaHeat(injectionindex, Parameters.Table[ParameterType.Nvalue1].Value, Parameters.Table[ParameterType.Enthalpy1].Value, Parameters.Table[ParameterType.Affinity1].Value) + Parameters.Table[ParameterType.Offset].Value * Data.Injections[injectionindex].InjectionMass;
            else return GetDeltaHeat(injectionindex, Parameters.Table[ParameterType.Nvalue1].Value, Parameters.Table[ParameterType.Enthalpy1].Value, Parameters.Table[ParameterType.Affinity1].Value);
        }

        double[] GetIsomerConcentration(InjectionData inj)
        {
            if (ModelOptions[PeptideInCellOption].BoolValue)
            {


                // K = (B) / (Tot-B)
                // K * (Tot - B) = B
                // Ktot - KB = B
                // Ktot = B + KB = B * (1 + K)
                // Ktot / (1 + K) = B
                if (inj.ID == 0)
                {
                    var KxTot = Parameters.Table[ParameterType.IsomerizationEquilibriumConstant].Value * inj.ActualCellConcentration;
                    var B = KxTot / (1 + Parameters.Table[ParameterType.IsomerizationEquilibriumConstant].Value);
                    var A = inj.ActualCellConcentration - B;

                    return new double[] { A, B };
                }
                else
                {

                    return new double[2];
                }
            }
            else
            {

            }

            return null;
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
        double GetFractionTransTrans(double Kd_cis, double Kd_trans, double conc_binding, double conc_cis, double conc_trans) => GetFractionLigandBound(Kd_trans, Kd_cis, conc_binding, conc_trans, conc_cis);

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

            SetSynthModelParameters(mdl);

            return mdl;
        }

        public class ModelSolution : SolutionInterface
        {
            public ModelSolution(Model model, double[] parameters)
            {
                Model = model;
                BootstrapSolutions = new List<SolutionInterface>();
            }
        }
    }
}

