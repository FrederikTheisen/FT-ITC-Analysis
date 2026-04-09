using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.Utilities;

namespace AnalysisITC.AppClasses.AnalysisClasses.Models
{
    public class CompetitiveBinding : Model
    {
        public override AnalysisModel ModelType => AnalysisModel.CompetitiveBinding;

        bool UseSyringeCorrectionMode => ModelOptions[AttributeKey.UseSyringeActiveFraction]?.BoolValue ?? false;

        public override double GuessAffinity()
        {
            return ConcentrationUnit.nM.GetProperties().Mod; // Assume around 1 nM Kd if using this model
        }

        public CompetitiveBinding(ExperimentData data) : base(data)
        {
        }

        public override void InitializeParameters(ExperimentData data)
        {
            base.InitializeParameters(data);

            Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, PreviousOrDefault(ParameterType.Nvalue1, this.GuessN()));
            Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, PreviousOrDefault(ParameterType.Enthalpy1, this.GuessEnthalpy()));
            Parameters.AddOrUpdateParameter(ParameterType.Affinity1, GuessLogAffinity());
            Parameters.AddOrUpdateParameter(ParameterType.Offset, PreviousOrDefault(ParameterType.Offset, this.GuessOffset()));

            ModelOptions.Add(ExperimentAttribute.Concentration(AttributeKey.PreboundLigandConc, AttributeKey.PreboundLigandConc.GetProperties().Name, new FloatWithError(10e-6, 0)).DictionaryEntry);
            ModelOptions.Add(ExperimentAttribute.Parameter(AttributeKey.PreboundLigandEnthalpy, AttributeKey.PreboundLigandEnthalpy.GetProperties().Name, new FloatWithError(-40000, 0)).DictionaryEntry);
            ModelOptions.Add(ExperimentAttribute.Affinity(AttributeKey.PreboundLigandAffinity, AttributeKey.PreboundLigandAffinity.GetProperties().Name, new(6.0, 0)).DictionaryEntry);

            ModelOptions.Add(ExperimentAttribute.Bool(AttributeKey.UseSyringeActiveFraction, AttributeKey.UseSyringeActiveFraction.GetProperties().Name, false).DictionaryEntry);
            ModelOptions.Add(ExperimentAttribute.Double(AttributeKey.NumberOfSites1, AttributeKey.NumberOfSites1.GetProperties().Name, 1).DictionaryEntry);
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            // Extract the fitted parameters for the titrated ligand (A)
            double n = Parameters.Table[ParameterType.Nvalue1].Value;                   // number of binding sites per macromolecule
            double dH_A = Parameters.Table[ParameterType.Enthalpy1].Value;              // binding enthalpy of ligand A (J/mol)
            double K_A = Math.Pow(10, Parameters.Table[ParameterType.Affinity1].Value); // association constant of ligand A (1/M)

            // Compute the reaction heat for this injection using the exact competitive binding model
            double dQ = GetDeltaHeatCompetitive(injectionindex, n, dH_A, K_A);

            // Add the baseline/dilution offset term if requested
            if (withoffset)
            {
                dQ += Parameters.Table[ParameterType.Offset].Value * Data.Injections[injectionindex].InjectionMass;
            }

            return dQ;
        }

        /// <summary>
        /// Calculate the differential heat for one injection using the heat content function
        /// for competitive binding. The returned value corresponds to ΔQ_i in Eq. 27 of
        /// Sigurskjold (2000), ignoring the baseline offset which is added separately.
        /// </summary>
        double GetDeltaHeatCompetitive(int i, double n, double H_A, double K_A)
        {
            return DeltaHeatFromHeatContent(i, (cm, cl) => GetHeatContentCompetitive(cm, cl, n, H_A, K_A));
        }

        /// <summary>
        /// Compute the heat content (Q) of the cell for a given post‑injection state in the
        /// competitive binding model. The heat content is the total reaction enthalpy contained
        /// in the cell due to bound ligands and is given by
        /// Q = V_0 · n ·[P]_0 · (ΔH_A·x_{PA} + ΔH_B·x_{PB}), where x_{PA}
        /// and x_{PB} are the mole fractions of protein bound to A or B, respectively.  These
        /// fractions are obtained by solving the cubic binding equation derived from mass
        /// conservation and competitive binding equilibria.
        /// Method handles both normal and syringe correction mode.
        /// </summary>
        double GetHeatContentCompetitive(double cellConc, double titrantConc, double nOrFactor, double H_A, double K_A)
        {
            // If the macromolecule concentration is zero, there can be no heat content
            if (cellConc <= 0 || nOrFactor <= 0) return 0.0;

            var stoich = UseSyringeCorrectionMode ? ModelOptions[AttributeKey.NumberOfSites1].DoubleValue : nOrFactor;
            var titrant = UseSyringeCorrectionMode ? nOrFactor * titrantConc : titrantConc;

            // Retrieve parameters for the pre‑bound ligand (B)
            double K_B = Math.Pow(10, ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue);    // association constant of ligand B
            double dH_B = ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue;                 // binding enthalpy of ligand B (J/mol)
            double B_0 = ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue;                      // initial concentration of ligand B (M)

            // Compute the stoichiometric ratios rA and rB.  For a multivalent protein with stoich
            // identical and independent binding sites, [P]_0 in the theory of Sigurskjold is
            // replaced by stoich·[P]_0. Consequently, rA = [A]_0 / (n·[P]_0) and rB = [B]_0 /(stoich·[P]_0).
            // The ratio rB remains constant during the titration because both [B]_0 and [P]_0
            // are diluted by the same factor upon each injection.
            double rA;
            double rB;

            // stoich·[P]_0 is the concentration of binding sites in the cell at this injection
            double nP = stoich * cellConc;
            if (nP <= 0) return 0.0;

            rA = titrant / nP;

            // rB is based on initial concentrations in the cell: [B]_0,initial and [P]_0,initial (Data.CellConcentration)
            // Both are diluted equally during titration so the ratio stays constant
            double nP_initial = stoich * Data.CellConcentration;
            if (nP_initial > 0) rB = B_0 / nP_initial;
            else rB = 0;


            // Compute the dimensionless parameters cA = K_A·stoich·[P]_0 and cB = K_B·stoich·[P]_0
            double cA = K_A * nP;
            double cB = K_B * nP;

            // Guard against non‑physical values
            if (cA <= 0 || cB <= 0) return 0.0;

            // Coefficients of the cubic equation xP^3 + a xP^2 + b xP + c = 0
            double a = (1.0 / cA) + (1.0 / cB) + rA + rB - 1.0;
            double b = (rA - 1.0) / cB + (rB - 1.0) / cA + (1.0 / (cA * cB));
            double c = -1.0 / (cA * cB);

            // Compute the discriminant parameters for the trigonometric solution of the cubic
            // If discriminant is negative, numerical errors may dominate; clamp to zero
            double discriminant = Math.Max(0.0, a * a - 3.0 * b);
            
            // Compute the argument of the arccosine in the solution. Clamp the argument to
            // [−1,1] to avoid NaNs due to floating point rounding.
            double numerator = -2.0 * a * a * a + 9.0 * a * b - 27.0 * c;
            double denominator = 2.0 * Math.Sqrt(discriminant * discriminant * discriminant);
            double argu;
            if (denominator == 0.0) argu = 1.0;
            else
            {
                argu = numerator / denominator;
                argu = Math.Max(-1.0, Math.Min(1.0, argu));
            }

            double u = Math.Acos(argu);

            // Only one root in (0,1) is physically meaningful. The trigonometric solution
            // provides the real root corresponding to the free protein fraction xP.
            double xP = (2 * Math.Sqrt(discriminant) * Math.Cos(u / 3.0) - a) / 3.0;   //(2.0 * Q * Math.Cos(u / 3.0) - a) / 3.0;

            // Enforce physical bounds on xP
            if (xP < 0.0) xP = 0.0;
            if (xP > 1.0) xP = 1.0;

            // Compute mole fractions of A‑bound and B‑bound protein
            double denomA = (1.0 / cA) + xP;
            double denomB = (1.0 / cB) + xP;
            double xPA = (denomA > 0.0) ? (rA * xP) / denomA : 0.0;
            double xPB = (denomB > 0.0) ? (rB * xP) / denomB : 0.0;

            // Compute the heat content. For stoich identical sites, multiply by stoich·[P]_0 and cell volume
            double heat = Data.CellVolume * stoich * cellConc * (H_A * xPA + dH_B * xPB);

            return heat;
        }

        public override Model GenerateSyntheticModel()
        {
            Model mdl = new CompetitiveBinding(Data.GetSynthClone(ModelCloneOptions));

            SetSynthModelParameters(mdl);

            return mdl;
        }

        public class ModelSolution : SolutionInterface
        {
            IDictionary<AttributeKey, ExperimentAttribute> opt => Model.ModelOptions;

            public Energy Enthalpy => Parameters[ParameterType.Enthalpy1].Energy;
            private FloatWithError LogK => Parameters[ParameterType.Affinity1];
            public FloatWithError K => FWEMath.Pow(10, LogK);
            public FloatWithError N => Parameters[ParameterType.Nvalue1];
            public FloatWithError LigandK => FWEMath.Pow(10, opt[AttributeKey.PreboundLigandAffinity].ParameterValue);
            //public Energy Offset => Parameters[ParameterType.Offset].Energy;

            public FloatWithError Kd => 1.0 / K;
            public Energy GibbsFreeEnergy => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K));
            public Energy TdS => GibbsFreeEnergy - Enthalpy;
            public Energy Entropy => TdS / TempKelvin;

            public FloatWithError Kapp => K / (LigandK * opt[AttributeKey.PreboundLigandConc].ParameterValue + 1);
            public FloatWithError Kdapp => 1.0 / Kapp;
            public Energy dHapp
            {
                get
                {
                    var Kligand = LigandK;

                    var top = opt[AttributeKey.PreboundLigandEnthalpy].ParameterValue * Kligand * opt[AttributeKey.PreboundLigandConc].ParameterValue;
                    var btm = (1 + Kligand * opt[AttributeKey.PreboundLigandConc].ParameterValue);

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

                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utilities.MarkdownStrings.ApparentDissociationConstant, Kdapp.AsFormattedConcentration(true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(Utilities.MarkdownStrings.DissociationConstant, Kd.AsFormattedConcentration(true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(Utilities.MarkdownStrings.Enthalpy, Enthalpy.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Entropy)) output.Add(new(Utilities.MarkdownStrings.EntropyContribution, TdS.ToFormattedString(ReportEnergyUnit, permole: true)));
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
                    { ParameterType.ApparentAffinity, Kdapp },
                    { ParameterType.Affinity1, Kd },
                    { ParameterType.Enthalpy1, Enthalpy.FloatWithError },
                    { ParameterType.EntropyContribution1, TdS.FloatWithError} ,
                    { ParameterType.Gibbs1, GibbsFreeEnergy.FloatWithError },
                };
        }
    }
}

