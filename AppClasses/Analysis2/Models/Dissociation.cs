using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.AppClasses.Analysis2.Models
{
    /// <summary>
    /// Self-association by dilution (dimerization):
    /// 2 M <-> D with association constant Ka = [D] / [M]^2  (units 1/M)
    ///
    /// Intended experiment: syringe contains macromolecule, cell is (initially) buffer.
    /// Heat per injection comes from the change in dimer amount upon dilution/mixing and re-equilibration.
    ///
    /// Parameters:
    /// - ParameterType.Enthalpy1 : ΔH_assoc (J/mol dimer formed)
    /// - ParameterType.Affinity1 : Ka (1/M)   (UI reports Kd = 1/Ka, as for other models)
    /// - ParameterType.Offset    : baseline heat per mol injectant (J/mol), applied as Offset * InjectionMass
    /// </summary>
    public class Dissociation : Model
    {
        public override AnalysisModel ModelType => AnalysisModel.Dissociation;

        public Dissociation(ExperimentData data) : base(data)
        {
        }

        public override void InitializeParameters(ExperimentData data)
        {
            base.InitializeParameters(data);

            Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, GuessParameter(ParameterType.Enthalpy1, GuessEnthalpy()));
            Parameters.AddOrUpdateParameter(ParameterType.Affinity1, GuessParameter(ParameterType.Affinity1, GuessAssociationConstant(data)));
            Parameters.AddOrUpdateParameter(ParameterType.Offset, GuessParameter(ParameterType.Offset, GuessOffset()));
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            var dH = Parameters.Table[ParameterType.Enthalpy1].Value;   // J/mol dimer
            var Ka = Parameters.Table[ParameterType.Affinity1].Value;   // 1/M
            var off = Parameters.Table[ParameterType.Offset].Value;     // J/mol injectant

            var q = GetDeltaHeat(injectionindex, dH, Ka);

            if (withoffset) q += off * Data.Injections[injectionindex].InjectionMass;

            return q;
        }

        double GetDeltaHeat(int i, double dH, double Ka)
        {
            if (Ka <= 0) return 0.0;

            var V = Data.CellVolume;
            if (V <= 0) return 0.0;

            // State variables are moles in the cell
            double nTot = 0.0; // total "monomer equivalents": nTot = nM + 2*nD
            double nD = 0.0;   // moles dimer

            double q = 0.0;

            for (int idx = 0; idx <= i && idx < Data.Injections.Count; idx++)
            {
                var inj = Data.Injections[idx];

                var v = inj.Volume;
                if (v <= 0)
                {
                    q = 0.0;
                    continue;
                }

                var f = 1.0 - (v / V); // retained fraction due to overflow
                if (f < 0) f = 0.0;

                // Total concentration in syringe (monomer equivalents, M)
                // (InjectionMass is in mol; Volume is in L)
                var C_syr = inj.InjectionMass > 0 ? (inj.InjectionMass / v) : 0.0;

                // Dimer concentration in syringe at equilibrium (M)
                var D_syr = DimerFromTotal(C_syr, Ka);

                // Immediately after physical transfer (before re-equilibration in the cell)
                var nTot_pre = nTot * f + inj.InjectionMass;
                var nD_pre = nD * f + (D_syr * v);

                // Re-equilibrate in the cell at the new total concentration
                var C_cell = nTot_pre / V;
                var D_post = DimerFromTotal(C_cell, Ka);
                var nD_post = D_post * V;

                // Heat for this injection comes from shift in dimer amount during re-equilibration
                q = dH * (nD_post - nD_pre);

                // Update for next injection
                nTot = nTot_pre;
                nD = nD_post;
            }

            return q;
        }

        /// <summary>
        /// Given total concentration C = [M] + 2[D] and Ka = [D]/[M]^2, return [D].
        /// Solve: C = x + 2 Ka x^2  with x = [M]
        /// </summary>
        static double DimerFromTotal(double C, double Ka)
        {
            if (C <= 0 || Ka <= 0) return 0.0;

            // 2 Ka x^2 + x - C = 0
            var disc = 1.0 + 8.0 * Ka * C;
            if (disc <= 1.0) return 0.0;

            var x = (-1.0 + Math.Sqrt(disc)) / (4.0 * Ka); // positive root
            if (x <= 0) return 0.0;

            return Ka * x * x;
        }

        double GuessAssociationConstant(ExperimentData data)
        {
            // Conservative default based on syringe concentration (if available).
            // Guess Kd ~ 0.1 * C_syr  => Ka ~ 10 / C_syr
            // Falls back to the generic affinity guess if syringe concentration is missing/invalid.
            if (data.SyringeConcentration > 0)
            {
                var kdGuess = 0.1 * data.SyringeConcentration;
                if (kdGuess > 0) return 1.0 / kdGuess;
            }

            return GuessAffinity();
        }

        public override Model GenerateSyntheticModel()
        {
            Model mdl = new Dissociation(Data.GetSynthClone(ModelCloneOptions));
            SetSynthModelParameters(mdl);
            return mdl;
        }

        public class ModelSolution : SolutionInterface
        {
            public Energy Enthalpy => Parameters[ParameterType.Enthalpy1].Energy;
            public FloatWithError K => Parameters[ParameterType.Affinity1];
            public Energy Offset => Parameters[ParameterType.Offset].Energy;

            public FloatWithError Kd => new FloatWithError(1) / K;

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
                var k = BootstrapSolutions.Select(s => (s as ModelSolution).K.Value);
                var offsets = BootstrapSolutions.Select(s => (s as ModelSolution).Offset.Value);

                Parameters[ParameterType.Enthalpy1] = new FloatWithError(enthalpies, Enthalpy);
                Parameters[ParameterType.Affinity1] = new FloatWithError(k, K);
                Parameters[ParameterType.Offset] = new FloatWithError(offsets, Offset);

                base.ComputeErrorsFromBootstrapSolutions();
            }

            public override List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
            {
                var output = base.UISolutionParameters(info);

                if (info.HasFlag(FinalFigureDisplayParameters.Affinity))
                    output.Add(new(Utils.MarkdownStrings.DissociationConstant, Kd.AsFormattedConcentration(withunit: true)));

                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy))
                    output.Add(new(Utils.MarkdownStrings.Enthalpy, Enthalpy.ToFormattedString(ReportEnergyUnit, permole: true)));

                if (info.HasFlag(FinalFigureDisplayParameters.TdS))
                    output.Add(new(Utils.MarkdownStrings.EntropyContribution, TdS.ToFormattedString(ReportEnergyUnit, permole: true)));

                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs))
                    output.Add(new(Utils.MarkdownStrings.GibbsFreeEnergy, GibbsFreeEnergy.ToFormattedString(ReportEnergyUnit, permole: true)));

                if (info.HasFlag(FinalFigureDisplayParameters.Offset))
                    output.Add(new("Offset", Offset.ToFormattedString(ReportEnergyUnit, permole: true)));

                return output;
            }

            public override List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>> DependenciesToReport =>
                new List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>>
                {
                    new(ParameterType.Enthalpy1, new(sol => (sol as ModelSolution).Enthalpy.FloatWithError)),
                    new(ParameterType.EntropyContribution1, new(sol => (sol as ModelSolution).TdS.FloatWithError)),
                    new(ParameterType.Gibbs1, new(sol => (sol as ModelSolution).GibbsFreeEnergy.FloatWithError)),
                };

            public override Dictionary<ParameterType, FloatWithError> ReportParameters =>
                new Dictionary<ParameterType, FloatWithError>
                {
                    { ParameterType.Affinity1, Kd },
                    { ParameterType.Enthalpy1, Enthalpy.FloatWithError },
                    { ParameterType.EntropyContribution1, TdS.FloatWithError },
                    { ParameterType.Gibbs1, GibbsFreeEnergy.FloatWithError },
                };
        }
    }
}

