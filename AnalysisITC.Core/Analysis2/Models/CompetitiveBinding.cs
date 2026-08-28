using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Analysis.Models
{
    public class CompetitiveBinding : Model
    {
        internal const double StateValidationTolerance = 1e-12;
        internal const double FallbackResidualTolerance = 1e-13;
        internal const int MaximumFallbackIterations = 64;

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
            if (TryEvaluateCompetitive(injectionindex, withoffset, out var value))
                return value;

            throw new ArithmeticException(
                $"Competitive equilibrium could not be resolved for injection {injectionindex}.");
        }

        internal override bool TryEvaluate(int injectionindex, bool withoffset, out double value) =>
            TryEvaluateCompetitive(injectionindex, withoffset, out value);

        bool TryEvaluateCompetitive(int injectionindex, bool withoffset, out double value)
        {
            value = double.NaN;

            var n = Parameters.Table[ParameterType.Nvalue1].Value;
            var enthalpyA = Parameters.Table[ParameterType.Enthalpy1].Value;
            var logAffinityA = Parameters.Table[ParameterType.Affinity1].Value;
            var offset = Parameters.Table[ParameterType.Offset].Value;
            if (!FWEMath.IsFinite(n) || n <= 0
                || !FWEMath.IsFinite(enthalpyA)
                || !FWEMath.IsFinite(logAffinityA)
                || !FWEMath.IsFinite(offset))
                return false;

            var affinityA = Math.Pow(10, logAffinityA);
            if (!FWEMath.IsFinite(affinityA) || affinityA <= 0)
                return false;

            var useSyringeCorrection = UseSyringeCorrectionMode;
            var stoich = useSyringeCorrection
                ? ModelOptions[AttributeKey.NumberOfSites1].DoubleValue
                : n;
            var syringeFactor = useSyringeCorrection ? n : 1.0;
            var initialSites = stoich * Data.CellConcentration.Value;
            var competitorTotal = ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue.Value;
            if (!FWEMath.IsFinite(stoich) || stoich <= 0
                || !FWEMath.IsFinite(initialSites) || initialSites <= 0)
                return false;
            if (!FWEMath.IsFinite(competitorTotal) || competitorTotal < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(competitorTotal), "Prebound ligand concentration must be finite and nonnegative.");

            var ratioB = competitorTotal / initialSites;
            if (!FWEMath.IsFinite(ratioB) || ratioB < 0)
                return false;

            // An absent competitor must not depend on its affinity or enthalpy.
            var affinityB = double.NaN;
            var enthalpyB = 0.0;
            if (ratioB > 0)
            {
                var logAffinityB = ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue.Value;
                enthalpyB = ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue.Value;
                if (!FWEMath.IsFinite(logAffinityB) || !FWEMath.IsFinite(enthalpyB))
                    throw new ArgumentOutOfRangeException(
                        nameof(logAffinityB), "Prebound ligand properties must be finite.");

                affinityB = Math.Pow(10, logAffinityB);
                if (!FWEMath.IsFinite(affinityB) || affinityB <= 0)
                    return false;
            }

            var deltaHeat = DeltaHeatFromHeatContent(
                injectionindex,
                (cellConcentration, titrantConcentration) =>
                {
                    if (!FWEMath.IsFinite(cellConcentration) || cellConcentration < 0
                        || !FWEMath.IsFinite(titrantConcentration) || titrantConcentration < 0)
                        return double.NaN;
                    if (cellConcentration == 0) return 0;

                    var totalSites = stoich * cellConcentration;
                    var titrant = syringeFactor * titrantConcentration;
                    var ratioA = titrant / totalSites;
                    var cA = affinityA * totalSites;
                    var cB = ratioB > 0 ? affinityB * totalSites : double.NaN;
                    if (!FWEMath.IsFinite(totalSites) || totalSites <= 0
                        || !FWEMath.IsFinite(titrant) || titrant < 0
                        || !FWEMath.IsFinite(ratioA) || ratioA < 0
                        || !FWEMath.IsFinite(cA) || cA <= 0
                        || (ratioB > 0 && (!FWEMath.IsFinite(cB) || cB <= 0)))
                        return double.NaN;

                    var state = CalculateState(ratioA, ratioB, cA, cB);
                    if (!state.Success) return double.NaN;
                    return Data.CellVolume * totalSites
                           * (enthalpyA * state.BoundAFraction
                              + enthalpyB * state.BoundBFraction);
                });
            if (!FWEMath.IsFinite(deltaHeat))
                return false;

            value = withoffset
                ? deltaHeat + offset * Data.Injections[injectionindex].InjectionMass
                : deltaHeat;
            return FWEMath.IsFinite(value);
        }

        internal static CompetitiveBindingState CalculateState(
            double ratioA,
            double ratioB,
            double cA,
            double cB)
        {
            if (!(ratioA >= 0 && ratioA <= double.MaxValue)
                || !(ratioB >= 0 && ratioB <= double.MaxValue)
                || (ratioA > 0 && !(cA > 0 && cA <= double.MaxValue))
                || (ratioB > 0 && !(cB > 0 && cB <= double.MaxValue)))
                return CompetitiveBindingState.Invalid();

            if (ratioA == 0 && ratioB == 0)
                return new CompetitiveBindingState(true, 1, 0, 0, 0, false, 0);
            if (ratioB == 0)
                return OneLigandState(ratioA, cA, ligandA: true);
            if (ratioA == 0)
                return OneLigandState(ratioB, cB, ligandA: false);

            var candidate = CubicCandidate(ratioA, ratioB, cA, cB);
            if (IsPhysicalState(
                    candidate, ratioA, ratioB,
                    StateValidationTolerance))
                return candidate;

            return SolveSafeguarded(ratioA, ratioB, cA, cB, candidate.FreeSiteFraction);
        }

        static CompetitiveBindingState OneLigandState(double ratio, double c, bool ligandA)
        {
            if (!FWEMath.IsFinite(ratio) || ratio < 0 || !FWEMath.IsFinite(c) || c <= 0)
                return CompetitiveBindingState.Invalid();

            if (ratio == 0)
                return new CompetitiveBindingState(true, 1, 0, 0, 0, false, 0);

            var linear = 1.0 + c * (ratio - 1.0);
            var discriminant = linear * linear + 4.0 * c;
            if (!FWEMath.IsFinite(linear) || !FWEMath.IsFinite(discriminant)
                || discriminant < 0)
                return CompetitiveBindingState.Invalid();

            var root = Math.Sqrt(discriminant);
            // Positive root of c*x^2 + [1+c(r-1)]*x - 1 = 0,
            // rationalized on the cancellation-prone branch.
            var free = linear >= 0
                ? 2.0 / (linear + root)
                : (root - linear) / (2.0 * c);
            // Derive the bound fraction from the equilibrium expression rather
            // than 1-free so weak-binding occupancies do not lose significance.
            var bound = SaturatedAmount(free, ratio, c);
            var state = ligandA
                ? CreateState(free, bound, 0, false, 0)
                : CreateState(free, 0, bound, false, 0);

            return IsFullyValidated(
                    state,
                    ligandA ? ratio : 0,
                    ligandA ? 0 : ratio,
                    ligandA ? c : 1,
                    ligandA ? 1 : c,
                    StateValidationTolerance)
                ? state
                : CompetitiveBindingState.Invalid();
        }

        static CompetitiveBindingState CubicCandidate(
            double ratioA,
            double ratioB,
            double cA,
            double cB)
        {
            var inverseA = 1.0 / cA;
            var inverseB = 1.0 / cB;
            var a = inverseA + inverseB + ratioA + ratioB - 1.0;
            var b = (ratioA - 1.0) * inverseB
                    + (ratioB - 1.0) * inverseA
                    + inverseA * inverseB;
            var c = -inverseA * inverseB;
            var discriminant = a * a - 3.0 * b;
            discriminant = Math.Max(0.0, discriminant);

            var numerator = -2.0 * a * a * a + 9.0 * a * b - 27.0 * c;
            var sqrtDiscriminant = Math.Sqrt(discriminant);
            var denominator = 2.0 * discriminant * sqrtDiscriminant;
            var argument = denominator == 0 ? 1.0 : numerator / denominator;
            argument = Math.Max(-1.0, Math.Min(1.0, argument));

            var free = (2.0 * sqrtDiscriminant * Math.Cos(Math.Acos(argument) / 3.0) - a) / 3.0;
            if (!(free >= 0 && free <= 1))
                return CompetitiveBindingState.Invalid();

            // The cubic already has the inverse affinities available. This form is
            // both overflow-safe (the parenthesized fraction is in [0,1]) and one
            // multiplication cheaper than rebuilding c*free for each ligand. It
            // constructs each bound/free-ligand split directly from its analytical
            // total, so ligand mass balance and equilibrium hold by construction;
            // IsPhysicalState then verifies a nonnegative remainder for each total.
            var boundA = ratioA * (free / (inverseA + free));
            var boundB = ratioB * (free / (inverseB + free));
            return CreateState(free, boundA, boundB, false, 0);
        }

        static CompetitiveBindingState SolveSafeguarded(
            double ratioA,
            double ratioB,
            double cA,
            double cB,
            double candidate)
        {
            var lower = 0.0;
            var upper = 1.0;
            var current = FWEMath.IsFinite(candidate) && candidate > lower && candidate < upper
                ? candidate
                : InitialFreeSiteEstimate(ratioA, ratioB, cA, cB);
            if (!FWEMath.IsFinite(current) || current <= lower || current >= upper)
                current = 0.5;

            CompetitiveBindingState best = CompetitiveBindingState.Invalid();
            for (var iteration = 1; iteration <= MaximumFallbackIterations; iteration++)
            {
                if (!TryCreateState(
                        current, ratioA, ratioB, cA, cB,
                        true, iteration, out var state))
                    return CompetitiveBindingState.Invalid(iteration);

                if (!best.Success || Math.Abs(state.SiteBalanceResidual) < Math.Abs(best.SiteBalanceResidual))
                    best = state;
                if (Math.Abs(state.SiteBalanceResidual) <= FallbackResidualTolerance
                    && IsFullyValidated(state, ratioA, ratioB, cA, cB, StateValidationTolerance))
                    return state;

                if (state.SiteBalanceResidual < 0)
                    lower = current;
                else
                    upper = current;

                var derivative = BalanceDerivative(current, ratioA, ratioB, cA, cB);
                var next = current - state.SiteBalanceResidual / derivative;
                if (!FWEMath.IsFinite(derivative) || derivative <= 0
                    || !FWEMath.IsFinite(next) || next <= lower || next >= upper)
                    next = lower + (upper - lower) * 0.5;

                if (next == current || next == lower || next == upper)
                    break;
                current = next;
            }

            return Math.Abs(best.SiteBalanceResidual) <= FallbackResidualTolerance
                   && IsFullyValidated(best, ratioA, ratioB, cA, cB, StateValidationTolerance)
                ? best
                : CompetitiveBindingState.Invalid(MaximumFallbackIterations);
        }

        static double InitialFreeSiteEstimate(double ratioA, double ratioB, double cA, double cB)
        {
            var derivativeAtZero = 1.0 + ratioA * cA + ratioB * cB;
            return FWEMath.IsFinite(derivativeAtZero) && derivativeAtZero > 1
                ? 1.0 / derivativeAtZero
                : 0.5;
        }

        static double BalanceDerivative(
            double free,
            double ratioA,
            double ratioB,
            double cA,
            double cB)
        {
            var derivativeA = SaturationDerivative(free, ratioA, cA);
            var derivativeB = SaturationDerivative(free, ratioB, cB);
            return 1.0 + derivativeA + derivativeB;
        }

        static double SaturationDerivative(double free, double ratio, double c)
        {
            if (ratio == 0) return 0;
            var product = c * free;
            if (double.IsPositiveInfinity(product)) return 0;
            var denominator = 1.0 + product;
            return ratio * c / (denominator * denominator);
        }

        static bool TryCreateState(
            double free,
            double ratioA,
            double ratioB,
            double cA,
            double cB,
            bool usedFallback,
            int iterations,
            out CompetitiveBindingState state)
        {
            state = CompetitiveBindingState.Invalid(iterations);
            if (!(free >= 0 && free <= 1))
                return false;

            var boundA = SaturatedAmount(free, ratioA, cA);
            var boundB = SaturatedAmount(free, ratioB, cB);
            if (!(boundA >= 0 && boundA <= double.MaxValue)
                || !(boundB >= 0 && boundB <= double.MaxValue))
                return false;

            state = CreateState(free, boundA, boundB, usedFallback, iterations);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static CompetitiveBindingState CreateState(
            double free,
            double boundA,
            double boundB,
            bool usedFallback,
            int iterations) =>
            new(
                true,
                free,
                boundA,
                boundB,
                free + boundA + boundB - 1.0,
                usedFallback,
                iterations);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double SaturatedAmount(double free, double ratio, double c)
        {
            if (ratio == 0 || free == 0) return 0;
            var product = c * free;
            if (double.IsPositiveInfinity(product)) return ratio;
            if (!FWEMath.IsFinite(product) || product < 0) return double.NaN;
            return ratio * (product / (1.0 + product));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsPhysicalState(
            CompetitiveBindingState state,
            double ratioA,
            double ratioB,
            double tolerance)
        {
            if (!state.Success
                || !(state.FreeSiteFraction >= -tolerance
                     && state.FreeSiteFraction <= 1.0 + tolerance)
                || !(state.BoundAFraction >= -tolerance
                     && state.BoundAFraction <= 1.0 + tolerance)
                || !(state.BoundBFraction >= -tolerance
                     && state.BoundBFraction <= 1.0 + tolerance))
                return false;

            var ratioToleranceA = tolerance * Math.Max(1.0, ratioA);
            var ratioToleranceB = tolerance * Math.Max(1.0, ratioB);
            if (state.BoundAFraction > ratioA + ratioToleranceA
                || state.BoundBFraction > ratioB + ratioToleranceB
                || !(Math.Abs(state.SiteBalanceResidual) <= tolerance))
                return false;

            return true;
        }

        static bool IsFullyValidated(
            CompetitiveBindingState state,
            double ratioA,
            double ratioB,
            double cA,
            double cB,
            double tolerance)
        {
            if (!IsPhysicalState(state, ratioA, ratioB, tolerance))
                return false;

            return EquilibriumResidualWithinTolerance(
                       state.FreeSiteFraction, state.BoundAFraction, ratioA, cA, tolerance)
                   && EquilibriumResidualWithinTolerance(
                       state.FreeSiteFraction, state.BoundBFraction, ratioB, cB, tolerance);
        }

        static bool EquilibriumResidualWithinTolerance(
            double free,
            double bound,
            double ratio,
            double c,
            double tolerance)
        {
            if (ratio == 0) return Math.Abs(bound) <= tolerance;
            if (ratio - bound < -tolerance * Math.Max(1.0, ratio)) return false;

            var product = c * free;
            if (double.IsPositiveInfinity(product))
                return Math.Abs(bound - ratio) <= tolerance * Math.Max(1.0, ratio);
            if (!FWEMath.IsFinite(product) || product < 0) return false;

            // Rearranged form of bound = c*free*(ratio-bound). This avoids
            // cancellation in ratio-bound for nearly saturated ligand.
            var left = bound * (1.0 + product);
            var right = ratio * product;
            if (!FWEMath.IsFinite(left) || !FWEMath.IsFinite(right)) return false;
            var scale = Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));
            return Math.Abs(left - right) <= tolerance * scale;
        }

        internal readonly struct CompetitiveBindingState
        {
            internal CompetitiveBindingState(
                bool success,
                double freeSiteFraction,
                double boundAFraction,
                double boundBFraction,
                double siteBalanceResidual,
                bool usedFallback,
                int iterations)
            {
                Success = success;
                FreeSiteFraction = freeSiteFraction;
                BoundAFraction = boundAFraction;
                BoundBFraction = boundBFraction;
                SiteBalanceResidual = siteBalanceResidual;
                UsedFallback = usedFallback;
                Iterations = iterations;
            }

            internal bool Success { get; }
            internal double FreeSiteFraction { get; }
            internal double BoundAFraction { get; }
            internal double BoundBFraction { get; }
            internal double SiteBalanceResidual { get; }
            internal bool UsedFallback { get; }
            internal int Iterations { get; }

            internal static CompetitiveBindingState Invalid(int iterations = 0) =>
                new(false, double.NaN, double.NaN, double.NaN, double.NaN, false, iterations);
        }

        internal override Model GenerateSyntheticModel(Random random)
        {
            Model mdl = new CompetitiveBinding(Data.GetSynthClone(ModelCloneOptions, random));

            SetSynthModelParameters(mdl, random);

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

                Parameters[ParameterType.Enthalpy1] = SummarizeBootstrapDistribution(enthalpies, Enthalpy);
                Parameters[ParameterType.Affinity1] = SummarizeBootstrapDistribution(k, LogK);
                Parameters[ParameterType.Nvalue1] = SummarizeBootstrapDistribution(n, N);
                Parameters[ParameterType.Offset] = SummarizeBootstrapDistribution(offsets, Offset);

                base.ComputeErrorsFromBootstrapSolutions();
            }

            public override List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
            {
                var output = base.UISolutionParameters(info);

                if (info.HasFlag(FinalFigureDisplayParameters.Nvalue))
                    if (UseSyringeCorrectionMode)
                    {
                        output.Add(new(MarkdownStrings.Alpha + "{syringe}", N.AsNumber()));
                        output.Add(new("N{fixed}", StoichiometryOptions.FormatAsParameter(ModelOptions[AttributeKey.NumberOfSites1].DoubleValue)));
                    }
                    else output.Add(new("N", N.AsNumber()));

                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(MarkdownStrings.ApparentDissociationConstant, Kdapp.AsFormattedConcentration(true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Affinity)) output.Add(new(MarkdownStrings.DissociationConstant, Kd.AsFormattedConcentration(true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy)) output.Add(new(MarkdownStrings.Enthalpy, Enthalpy.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Entropy)) output.Add(new(MarkdownStrings.EntropyContribution, TdS.ToFormattedString(ReportEnergyUnit, permole: true)));
                if (info.HasFlag(FinalFigureDisplayParameters.Gibbs)) output.Add(new(MarkdownStrings.GibbsFreeEnergy, GibbsFreeEnergy.ToFormattedString(ReportEnergyUnit, permole: true)));
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
