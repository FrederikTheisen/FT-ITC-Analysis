using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Analysis.Models
{
    public class TwoSetsOfSites : Model
	{
        const double sqrt3 = 1.73205080757;
        const double cube2 = 1.25992104989;
        internal const double AbsoluteMassBalanceTolerance = 1e-24;
        internal const double RelativeMassBalanceTolerance = 1e-14;
        const int MaximumBisectionIterations = 500;

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
            ModelOptions.Add(ExperimentAttribute.Double(AttributeKey.NumberOfSites1, "1^st^ " + AttributeKey.NumberOfSites1.GetProperties().Name, 1).DictionaryEntry);
            ModelOptions.Add(ExperimentAttribute.Double(AttributeKey.NumberOfSites2, "2^nd^ " + AttributeKey.NumberOfSites2.GetProperties().Name, 1).DictionaryEntry);
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

        double Kd1;
        double Kd2;
        double N1;
        double N2;

        double GetDeltaHeat(int i)
        {
            Kd1 = 1 / Math.Pow(10, Parameters.Table[ParameterType.Affinity1].Value);
            Kd2 = 1 / Math.Pow(10, Parameters.Table[ParameterType.Affinity2].Value);
            N1 = GetSiteStoichiometry1();
            N2 = GetSiteStoichiometry2();

            return DeltaHeatFromHeatContent(i, (cm, cl) => GetHeatContent(cm, cl));
        }

        double GetHeatContent(double cellConc, double titrantConc)
        {
            var titrant = titrantConc * GetSyringeFactor();
            var state = CalculateState(cellConc, titrant, Kd1, Kd2, N1, N2);
            if (!state.Success) return double.NaN;

            return cellConc * Data.CellVolume *
                   (N1 * state.Occupancy1 * Parameters.Table[ParameterType.Enthalpy1].Value +
                    N2 * state.Occupancy2 * Parameters.Table[ParameterType.Enthalpy2].Value);
        }

        internal static TwoSetBindingState CalculateState(
            double totalMacromolecule,
            double totalTitrant,
            double kd1,
            double kd2,
            double n1,
            double n2,
            double relativeTolerance = RelativeMassBalanceTolerance)
        {
            if (!TryGetMassBalanceTolerance(
                    totalMacromolecule, totalTitrant, kd1, kd2, n1, n2,
                    relativeTolerance, out var tolerance))
                return TwoSetBindingState.Invalid();

            if (totalTitrant == 0)
                return new TwoSetBindingState(true, 0, 0, 0, 0, 0);

            if (!TryCreateState(0, totalMacromolecule, totalTitrant, kd1, kd2, n1, n2, 0, out var lowerState)
                || !TryCreateState(totalTitrant, totalMacromolecule, totalTitrant, kd1, kd2, n1, n2, 0, out var upperState))
                return TwoSetBindingState.Invalid();

            if (Math.Abs(lowerState.MassBalanceResidual) <= tolerance) return lowerState;
            if (Math.Abs(upperState.MassBalanceResidual) <= tolerance) return upperState;
            if (lowerState.MassBalanceResidual >= 0 || upperState.MassBalanceResidual <= 0)
                return TwoSetBindingState.Invalid();

            var lower = 0.0;
            var upper = totalTitrant;
            for (var iteration = 1; iteration <= MaximumBisectionIterations; iteration++)
            {
                var midpoint = lower + (upper - lower) * 0.5;
                if (midpoint == lower || midpoint == upper)
                    return BestValidatedState(lowerState, upperState, tolerance);

                if (!TryCreateState(
                        midpoint, totalMacromolecule, totalTitrant, kd1, kd2, n1, n2,
                        iteration, out var midpointState))
                    return TwoSetBindingState.Invalid(iteration);

                if (Math.Abs(midpointState.MassBalanceResidual) <= tolerance)
                    return midpointState;

                if (midpointState.MassBalanceResidual < 0)
                {
                    lower = midpoint;
                    lowerState = midpointState;
                }
                else
                {
                    upper = midpoint;
                    upperState = midpointState;
                }
            }

            return BestValidatedState(lowerState, upperState, tolerance);
        }

        /// <summary>
        /// Retained expanded-cubic reference implementation. Production heat
        /// evaluation uses <see cref="CalculateState"/> and never falls back to
        /// this method.
        /// </summary>
        internal static TwoSetBindingState CalculateStateWithExpandedCubic(
            double totalMacromolecule,
            double totalTitrant,
            double kd1,
            double kd2,
            double n1,
            double n2,
            double relativeTolerance = RelativeMassBalanceTolerance)
        {
            if (!TryGetMassBalanceTolerance(
                    totalMacromolecule, totalTitrant, kd1, kd2, n1, n2,
                    relativeTolerance, out var tolerance))
                return TwoSetBindingState.Invalid();

            if (totalTitrant == 0)
                return new TwoSetBindingState(true, 0, 0, 0, 0, 0);

            var p = kd1 + kd2 + (n1 + n2) * totalMacromolecule - totalTitrant;
            var q = (kd2 * n1 + kd1 * n2) * totalMacromolecule
                    - (kd1 + kd2) * totalTitrant + kd1 * kd2;
            var r = -totalTitrant * kd1 * kd2;
            if (!FWEMath.IsFinite(p) || !FWEMath.IsFinite(q) || !FWEMath.IsFinite(r))
                return TwoSetBindingState.Invalid();

            var lower = 0.0;
            var upper = totalTitrant;
            var fLower = FreeTitrantPolynomial(lower, p, q, r);
            var fUpper = FreeTitrantPolynomial(upper, p, q, r);
            if (!FWEMath.IsFinite(fLower) || !FWEMath.IsFinite(fUpper))
                return TwoSetBindingState.Invalid();

            if (!TryCreateState(lower, totalMacromolecule, totalTitrant, kd1, kd2, n1, n2, 0, out var lowerState)
                || !TryCreateState(upper, totalMacromolecule, totalTitrant, kd1, kd2, n1, n2, 0, out var upperState))
                return TwoSetBindingState.Invalid();

            if (Math.Abs(lowerState.MassBalanceResidual) <= tolerance) return lowerState;
            if (Math.Abs(upperState.MassBalanceResidual) <= tolerance) return upperState;
            if (fLower >= 0 || fUpper <= 0)
                return TwoSetBindingState.Invalid();

            for (var iteration = 1; iteration <= MaximumBisectionIterations; iteration++)
            {
                var mid = lower + (upper - lower) * 0.5;
                if (mid == lower || mid == upper)
                    return BestValidatedState(lowerState, upperState, tolerance);

                var fMid = FreeTitrantPolynomial(mid, p, q, r);
                if (!FWEMath.IsFinite(fMid))
                    return TwoSetBindingState.Invalid(iteration);

                if (!TryCreateState(
                        mid, totalMacromolecule, totalTitrant, kd1, kd2, n1, n2,
                        iteration, out var midpointState))
                    return TwoSetBindingState.Invalid(iteration);

                if (Math.Abs(midpointState.MassBalanceResidual) <= tolerance)
                    return midpointState;

                if (fMid < 0)
                {
                    lower = mid;
                    fLower = fMid;
                    lowerState = midpointState;
                }
                else
                {
                    upper = mid;
                    upperState = midpointState;
                }
            }

            return BestValidatedState(lowerState, upperState, tolerance);
        }

        static bool TryGetMassBalanceTolerance(
            double totalMacromolecule,
            double totalTitrant,
            double kd1,
            double kd2,
            double n1,
            double n2,
            double relativeTolerance,
            out double tolerance)
        {
            tolerance = double.NaN;
            if (!FWEMath.IsFinite(totalMacromolecule) || totalMacromolecule < 0
                || !FWEMath.IsFinite(totalTitrant) || totalTitrant < 0
                || !FWEMath.IsFinite(kd1) || kd1 <= 0
                || !FWEMath.IsFinite(kd2) || kd2 <= 0
                || !FWEMath.IsFinite(n1) || n1 < 0
                || !FWEMath.IsFinite(n2) || n2 < 0
                || !FWEMath.IsFinite(relativeTolerance) || relativeTolerance <= 0)
                return false;

            var scale = Math.Max(totalTitrant, totalMacromolecule * (n1 + n2));
            tolerance = Math.Max(AbsoluteMassBalanceTolerance, relativeTolerance * scale);
            return FWEMath.IsFinite(tolerance);
        }

        static bool TryCreateState(
            double freeTitrant,
            double totalMacromolecule,
            double totalTitrant,
            double kd1,
            double kd2,
            double n1,
            double n2,
            int iterations,
            out TwoSetBindingState state)
        {
            state = TwoSetBindingState.Invalid(iterations);
            if (!FWEMath.IsFinite(freeTitrant) || freeTitrant < 0 || freeTitrant > totalTitrant)
                return false;

            var denominator1 = kd1 + freeTitrant;
            var denominator2 = kd2 + freeTitrant;
            if (!FWEMath.IsFinite(denominator1) || denominator1 <= 0
                || !FWEMath.IsFinite(denominator2) || denominator2 <= 0)
                return false;

            var occupancy1 = freeTitrant / denominator1;
            var occupancy2 = freeTitrant / denominator2;
            var residual = freeTitrant
                           + totalMacromolecule * (n1 * occupancy1 + n2 * occupancy2)
                           - totalTitrant;
            if (!FWEMath.IsFinite(occupancy1) || occupancy1 < 0 || occupancy1 > 1
                || !FWEMath.IsFinite(occupancy2) || occupancy2 < 0 || occupancy2 > 1
                || !FWEMath.IsFinite(residual))
                return false;

            state = new TwoSetBindingState(
                true, freeTitrant, occupancy1, occupancy2, residual, iterations);
            return true;
        }

        static TwoSetBindingState BestValidatedState(
            TwoSetBindingState first,
            TwoSetBindingState second,
            double tolerance)
        {
            var best = Math.Abs(first.MassBalanceResidual) <= Math.Abs(second.MassBalanceResidual)
                ? first
                : second;
            return Math.Abs(best.MassBalanceResidual) <= tolerance
                ? best
                : TwoSetBindingState.Invalid(Math.Max(first.Iterations, second.Iterations));
        }

        static double FreeTitrantPolynomial(double x, double p, double q, double r) =>
            x * x * x + x * x * p + x * q + r;

        internal sealed class TwoSetBindingState
        {
            public bool Success { get; }
            public double FreeTitrant { get; }
            public double Occupancy1 { get; }
            public double Occupancy2 { get; }
            public double MassBalanceResidual { get; }
            public int Iterations { get; }

            internal TwoSetBindingState(
                bool success,
                double freeTitrant,
                double occupancy1,
                double occupancy2,
                double massBalanceResidual,
                int iterations)
            {
                Success = success;
                FreeTitrant = freeTitrant;
                Occupancy1 = occupancy1;
                Occupancy2 = occupancy2;
                MassBalanceResidual = massBalanceResidual;
                Iterations = iterations;
            }

            internal static TwoSetBindingState Invalid(int iterations = 0) => new(
                false, double.NaN, double.NaN, double.NaN, double.NaN, iterations);
        }

        internal override Model GenerateSyntheticModel(Random random)
        {
            return GenerateSyntheticModel(random, ModelCloneOptions);
        }

        internal override Model GenerateSyntheticModel(Random random, ModelCloneOptions options)
        {
            Model mdl = new TwoSetsOfSites(Data.GetSynthClone(options, random));

            SetSynthModelParameters(mdl, random, options);

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

            public FloatWithError Kd1 => ProfileMappedParameter(ParameterType.Affinity1,
                value => 1.0 / Math.Pow(10.0, value), 1.0 / K1);
            public Energy GibbsFreeEnergy1 => new(-1.0 * Energy.R.FloatWithError * TempKelvin * FWEMath.Log(K1));
            public Energy TdS1 => GibbsFreeEnergy1 - Enthalpy1;
            public Energy Entropy1 => TdS1 / TempKelvin;

            public FloatWithError Kd2 => ProfileMappedParameter(ParameterType.Affinity2,
                value => 1.0 / Math.Pow(10.0, value), 1.0 / K2);
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

                Parameters[ParameterType.Enthalpy1] = SummarizeBootstrapDistribution(enthalpies1, Enthalpy1);
                Parameters[ParameterType.Affinity1] = SummarizeBootstrapDistribution(k1, LogK1);
                Parameters[ParameterType.Nvalue1] = SummarizeBootstrapDistribution(n1, N1);
                Parameters[ParameterType.Enthalpy2] = SummarizeBootstrapDistribution(enthalpies2, Enthalpy2);
                Parameters[ParameterType.Affinity2] = SummarizeBootstrapDistribution(k2, LogK2);
                Parameters[ParameterType.Nvalue2] = SummarizeBootstrapDistribution(n2, N2);
                Parameters[ParameterType.Offset] = SummarizeBootstrapDistribution(offsets, Offset);

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
                        output.Add(new("N{1,fixed}", StoichiometryOptions.FormatAsParameter(ModelOptions[AttributeKey.NumberOfSites1].DoubleValue)));
                    }
                    else output.Add(new("N{1}", N1.AsNumber()));

                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(MarkdownStrings.DissociationConstant + "{,1}", Kd1.AsFormattedConcentration(withunit: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(MarkdownStrings.Enthalpy + "{1}", Enthalpy1.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(MarkdownStrings.GibbsFreeEnergy + "{1}", GibbsFreeEnergy1.ToFormattedString(ReportEnergyUnit, permole: true)));

                // Site 2
                if (info.HasFlag(FinalFigureDisplayParameters.Nvalue))
                    if (UseSyringeCorrectionMode)
                    {
                        output.Add(new("N{2,fixed}", StoichiometryOptions.FormatAsParameter(ModelOptions[AttributeKey.NumberOfSites2].DoubleValue)));
                    }
                    else output.Add(new("N{2}", N2.AsNumber()));

                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(MarkdownStrings.DissociationConstant + "{,2}", Kd2.AsFormattedConcentration(withunit: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(MarkdownStrings.Enthalpy + "{2}", Enthalpy2.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(MarkdownStrings.GibbsFreeEnergy + "{2}", GibbsFreeEnergy2.ToFormattedString(ReportEnergyUnit, permole: true)));

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
                        { ParameterType.Nvalue1, N1 }, // We always have N1

                        // Site 1
                        { ParameterType.Affinity1, Kd1 },
                        { ParameterType.Enthalpy1, Enthalpy1.FloatWithError },
                        { ParameterType.EntropyContribution1, TdS1.FloatWithError },
                        { ParameterType.Gibbs1, GibbsFreeEnergy1.FloatWithError },

                    };

                    if (!UseSyringeCorrectionMode) //We only have one N if syringe correction mode is used
                    {
                        dict.Add(ParameterType.Nvalue2, N2);
                    }

                    dict.Add(ParameterType.Affinity2, Kd2);
                    dict.Add(ParameterType.Enthalpy2, Enthalpy2.FloatWithError);
                    dict.Add(ParameterType.EntropyContribution2, TdS2.FloatWithError);
                    dict.Add(ParameterType.Gibbs2, GibbsFreeEnergy2.FloatWithError);

                    return dict;
                }
            }
        }
    }
}
