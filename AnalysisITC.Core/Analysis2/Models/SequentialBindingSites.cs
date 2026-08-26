using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Analysis.Models
{
    /// <summary>
    /// Macroscopic stepwise sequential binding model for two to four ligand-binding
    /// transitions on a macromolecule in the cell.
    /// </summary>
    public sealed class SequentialBindingSites : Model
    {
        const double Ln10 = 2.3025850929940456840179914546844;
        const double RelativeMassBalanceTolerance = 1e-14;
        const double AbsoluteMassBalanceTolerance = 1e-24;
        const int MaximumBisectionIterations = 512;

        int initializedSiteCount;

        public override AnalysisModel ModelType => AnalysisModel.SequentialBindingSites;

        public int SiteCount
        {
            get
            {
                if (!ModelOptions.TryGetValue(AttributeKey.SequentialSiteCount, out var option))
                    return ThermodynamicParameterSlots.MinimumSequentialCount;
                ThermodynamicParameterSlots.ValidateSequentialCount(option.IntValue);
                return option.IntValue;
            }
        }

        public SequentialBindingSites(ExperimentData data) : base(data)
        {
        }

        public override void InitializeParameters(ExperimentData data)
        {
            base.InitializeParameters(data);
            ModelOptions.Clear();
            ModelOptions.Add(ExperimentAttribute.Int(
                AttributeKey.SequentialSiteCount,
                AttributeKey.SequentialSiteCount.GetProperties().Name,
                ThermodynamicParameterSlots.MinimumSequentialCount).DictionaryEntry);

            initializedSiteCount = ThermodynamicParameterSlots.MinimumSequentialCount;
            InitializeParameterTable(initializedSiteCount, reuseAttachedValues: true);
        }

        public override void ApplyModelOptions()
        {
            base.ApplyModelOptions();
            var effectiveCount = SiteCount;
            if (initializedSiteCount == effectiveCount && HasExactFittedShape(effectiveCount)) return;

            InitializeParameterTable(effectiveCount, reuseAttachedValues: false);
            initializedSiteCount = effectiveCount;
        }

        void InitializeParameterTable(int count, bool reuseAttachedValues)
        {
            ThermodynamicParameterSlots.ValidateSequentialCount(count);
            var previous = Parameters?.Table?.ToDictionary(item => item.Key, item => item.Value.Copy())
                ?? new Dictionary<ParameterType, Parameter>();
            var rebuilt = new ModelParameters(Data) { ModelType = ModelType };

            foreach (var slot in ThermodynamicParameterSlots.Active(count))
            {
                AddParameter(rebuilt, previous, slot.Affinity, GuessLogAffinity(slot.Index, count), reuseAttachedValues);
                AddParameter(rebuilt, previous, slot.Enthalpy, GuessStepEnthalpy(), reuseAttachedValues);
            }

            AddParameter(rebuilt, previous, ParameterType.Offset, GuessSequentialOffset(), reuseAttachedValues);
            Parameters = rebuilt;
        }

        void AddParameter(
            ModelParameters target,
            IReadOnlyDictionary<ParameterType, Parameter> previous,
            ParameterType key,
            double guess,
            bool reuseAttachedValues)
        {
            if (previous.TryGetValue(key, out var existing))
            {
                target.AddOrUpdateParameter(existing);
                return;
            }

            var value = reuseAttachedValues ? PreviousOrDefault(key, guess) : guess;
            target.AddOrUpdateParameter(key, value);
        }

        bool HasExactFittedShape(int count)
        {
            if (Parameters?.Table == null || Parameters.Table.Count != count * 2 + 1) return false;
            if (!Parameters.Table.ContainsKey(ParameterType.Offset)) return false;
            return ThermodynamicParameterSlots.Active(count).All(slot =>
                Parameters.Table.ContainsKey(slot.Affinity) && Parameters.Table.ContainsKey(slot.Enthalpy));
        }

        double GuessStepEnthalpy()
        {
            var included = Data.Injections?.Where(injection => injection.Include).ToList()
                ?? new List<InjectionData>();
            if (included.Count == 0) return 0;
            return included[0].Enthalpy - GuessSequentialOffset();
        }

        double GuessSequentialOffset()
        {
            if (Data.Injections == null || !Data.Injections.Any(injection => injection.Include))
                return 0;
            return base.GuessOffset();
        }

        double GuessLogAffinity(int step, int count)
        {
            var concentrations = Data.Injections
                .Select(injection => injection.ActualTitrantConcentration)
                .Where(value => IsFinite(value) && value > 0)
                .ToList();

            double minimum;
            double maximum;
            if (concentrations.Count > 0)
            {
                minimum = concentrations.Min();
                maximum = concentrations.Max();
            }
            else
            {
                var scale = IsFinite(Data.CellConcentration) && Data.CellConcentration > 0
                    ? Data.CellConcentration.Value
                    : 1e-6;
                minimum = Math.Max(scale * 0.01, 1e-12);
                maximum = Math.Max(scale * 10.0, minimum * 100.0);
            }

            var strongest = Math.Log10(1.0 / minimum);
            var weakest = Math.Log10(1.0 / maximum);
            var fraction = count == 1 ? 0 : (step - 1.0) / (count - 1.0);
            var guess = strongest + fraction * (weakest - strongest);
            var limits = ParameterType.Affinity1.GetProperties().DefaultLimits;
            return Math.Max(limits[0], Math.Min(limits[1], guess));
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            var heat = DeltaHeatFromHeatContent(injectionindex, HeatContent);
            if (withoffset)
                heat += Parameters.Table[ParameterType.Offset].Value
                    * Data.Injections[injectionindex].InjectionMass;
            return heat;
        }

        double HeatContent(double totalMacromolecule, double totalLigand)
        {
            totalMacromolecule = Math.Max(0, totalMacromolecule);
            totalLigand = Math.Max(0, totalLigand);
            if (totalMacromolecule == 0) return 0;

            var state = CalculateState(totalMacromolecule, totalLigand);
            var cumulativeEnthalpy = 0.0;
            var molarHeat = 0.0;
            foreach (var slot in ThermodynamicParameterSlots.Active(SiteCount))
            {
                cumulativeEnthalpy += Parameters.Table[slot.Enthalpy].Value;
                molarHeat += state.Fractions[slot.Index] * cumulativeEnthalpy;
            }

            return Data.CellVolume * totalMacromolecule * molarHeat;
        }

        internal SequentialBindingState CalculateState(
            double totalMacromolecule,
            double totalLigand,
            double relativeTolerance = RelativeMassBalanceTolerance)
        {
            if (!IsFinite(totalMacromolecule) || totalMacromolecule < 0)
                throw new ArgumentOutOfRangeException(nameof(totalMacromolecule));
            if (!IsFinite(totalLigand) || totalLigand < 0)
                throw new ArgumentOutOfRangeException(nameof(totalLigand));
            if (!IsFinite(relativeTolerance) || relativeTolerance <= 0)
                throw new ArgumentOutOfRangeException(nameof(relativeTolerance));

            var count = SiteCount;
            var logBeta = CumulativeLogAssociationConstants(count);
            if (totalLigand == 0)
                return StateAtFreeLigand(0, totalMacromolecule, totalLigand, logBeta);

            if (totalMacromolecule == 0)
                return StateAtFreeLigand(totalLigand, totalMacromolecule, totalLigand, logBeta);

            var lower = 0.0;
            var upper = totalLigand;
            var tolerance = Math.Max(AbsoluteMassBalanceTolerance,
                relativeTolerance * Math.Max(totalLigand, count * totalMacromolecule));
            var upperState = StateAtFreeLigand(upper, totalMacromolecule, totalLigand, logBeta);
            if (Math.Abs(upperState.MassBalanceResidual) <= tolerance) return upperState;

            SequentialBindingState midpointState = null;
            for (var iteration = 0; iteration < MaximumBisectionIterations; iteration++)
            {
                var midpoint = lower + (upper - lower) * 0.5;
                if (midpoint == lower || midpoint == upper) break;

                midpointState = StateAtFreeLigand(
                    midpoint, totalMacromolecule, totalLigand, logBeta);
                if (Math.Abs(midpointState.MassBalanceResidual) <= tolerance)
                    return midpointState;

                if (midpointState.MassBalanceResidual > 0)
                    upper = midpoint;
                else
                    lower = midpoint;
            }

            var resolved = lower + (upper - lower) * 0.5;
            if (resolved == lower || resolved == upper)
                resolved = Math.Abs(StateAtFreeLigand(lower, totalMacromolecule, totalLigand, logBeta).MassBalanceResidual)
                    <= Math.Abs(StateAtFreeLigand(upper, totalMacromolecule, totalLigand, logBeta).MassBalanceResidual)
                    ? lower
                    : upper;
            return StateAtFreeLigand(resolved, totalMacromolecule, totalLigand, logBeta);
        }

        double[] CumulativeLogAssociationConstants(int count)
        {
            var logBeta = new double[count + 1];
            foreach (var slot in ThermodynamicParameterSlots.Active(count))
            {
                var logKa = Parameters.Table[slot.Affinity].Value;
                if (!IsFinite(logKa))
                    throw new InvalidOperationException($"Sequential affinity {slot.Index} is not finite.");
                logBeta[slot.Index] = logBeta[slot.Index - 1] + logKa * Ln10;
            }
            return logBeta;
        }

        static SequentialBindingState StateAtFreeLigand(
            double freeLigand,
            double totalMacromolecule,
            double totalLigand,
            IReadOnlyList<double> logBeta)
        {
            var count = logBeta.Count - 1;
            var fractions = new double[count + 1];
            if (freeLigand == 0)
            {
                fractions[0] = 1;
                return new SequentialBindingState(0, fractions, 0, -totalLigand);
            }

            var logFree = Math.Log(freeLigand);
            var logWeights = new double[count + 1];
            logWeights[0] = 0;
            for (var step = 1; step <= count; step++)
                logWeights[step] = logBeta[step] + step * logFree;

            var maximum = logWeights.Max();
            var scaledSum = 0.0;
            for (var step = 0; step <= count; step++)
            {
                fractions[step] = Math.Exp(logWeights[step] - maximum);
                scaledSum += fractions[step];
            }

            var meanOccupancy = 0.0;
            for (var step = 0; step <= count; step++)
            {
                fractions[step] /= scaledSum;
                meanOccupancy += step * fractions[step];
            }

            var residual = freeLigand + totalMacromolecule * meanOccupancy - totalLigand;
            return new SequentialBindingState(freeLigand, fractions, meanOccupancy, residual);
        }

        public override Model GenerateSyntheticModel()
        {
            var model = new SequentialBindingSites(Data.GetSynthClone(ModelCloneOptions));
            SetSynthModelParameters(model);
            model.initializedSiteCount = SiteCount;
            return model;
        }

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public sealed class ModelSolution : SolutionInterface
        {
            public int SiteCount => ((SequentialBindingSites)Model).SiteCount;

            public ModelSolution(Model model)
            {
                Model = model;
                BootstrapSolutions = new List<SolutionInterface>();
            }

            public FloatWithError AssociationConstant(int step)
            {
                var slot = ThermodynamicParameterSlots.ForStep(step);
                return FWEMath.Pow(10.0, Parameters[slot.Affinity]);
            }

            public FloatWithError DissociationConstant(int step) => 1.0 / AssociationConstant(step);

            public Energy Enthalpy(int step) =>
                new Energy(Parameters[ThermodynamicParameterSlots.ForStep(step).Enthalpy]);

            public Energy GibbsFreeEnergy(int step) => new Energy(
                -Energy.R.FloatWithError * TempKelvin * FWEMath.Log(AssociationConstant(step)));

            public Energy EntropyContribution(int step) => GibbsFreeEnergy(step) - Enthalpy(step);

            public Energy Entropy(int step) => EntropyContribution(step) / TempKelvin;

            public override void ComputeErrorsFromBootstrapSolutions()
            {
                foreach (var slot in ThermodynamicParameterSlots.Active(SiteCount))
                {
                    Parameters[slot.Affinity] = BootstrapEstimate(slot.Affinity);
                    Parameters[slot.Enthalpy] = BootstrapEstimate(slot.Enthalpy);
                }
                Parameters[ParameterType.Offset] = BootstrapEstimate(ParameterType.Offset);
                base.ComputeErrorsFromBootstrapSolutions();
            }

            FloatWithError BootstrapEstimate(ParameterType key)
            {
                var values = BootstrapSolutions
                    .Where(solution => solution?.Parameters?.ContainsKey(key) == true)
                    .Select(solution => solution.Parameters[key].Value)
                    .ToList();
                return values.Count == 0
                    ? Parameters[key]
                    : new FloatWithError(values, Parameters[key].Value);
            }

            public override List<Tuple<string, string>> UISolutionParameters(FinalFigureDisplayParameters info)
            {
                var output = base.UISolutionParameters(info);
                foreach (var slot in ThermodynamicParameterSlots.Active(SiteCount))
                {
                    var suffix = "{" + slot.Index + "}";
                    if (info.HasFlag(FinalFigureDisplayParameters.Affinity))
                        output.Add(new Tuple<string, string>(
                            MarkdownStrings.DissociationConstant + "{," + slot.Index + "}",
                            DissociationConstant(slot.Index).AsFormattedConcentration(withunit: true)));
                    if (info.HasFlag(FinalFigureDisplayParameters.Enthalpy))
                        output.Add(new Tuple<string, string>(MarkdownStrings.Enthalpy + suffix,
                            Enthalpy(slot.Index).ToFormattedString(ReportEnergyUnit, permole: true)));
                    if (info.HasFlag(FinalFigureDisplayParameters.Entropy))
                        output.Add(new Tuple<string, string>(MarkdownStrings.EntropyContribution + suffix,
                            EntropyContribution(slot.Index).ToFormattedString(ReportEnergyUnit, permole: true)));
                    if (info.HasFlag(FinalFigureDisplayParameters.Gibbs))
                        output.Add(new Tuple<string, string>(MarkdownStrings.GibbsFreeEnergy + suffix,
                            GibbsFreeEnergy(slot.Index).ToFormattedString(ReportEnergyUnit, permole: true)));
                }

                if (info.HasFlag(FinalFigureDisplayParameters.Offset))
                    output.Add(new Tuple<string, string>("Offset",
                        Offset.ToFormattedString(ReportEnergyUnit, permole: true)));
                return output;
            }

            public override List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>> DependenciesToReport
            {
                get
                {
                    var dependencies = new List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>>();
                    foreach (var slot in ThermodynamicParameterSlots.Active(SiteCount))
                    {
                        var step = slot.Index;
                        dependencies.Add(new Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>(
                            slot.Enthalpy, solution => ((ModelSolution)solution).Enthalpy(step).FloatWithError));
                        dependencies.Add(new Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>(
                            slot.EntropyContribution, solution => ((ModelSolution)solution).EntropyContribution(step).FloatWithError));
                        dependencies.Add(new Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>(
                            slot.Gibbs, solution => ((ModelSolution)solution).GibbsFreeEnergy(step).FloatWithError));
                    }
                    return dependencies;
                }
            }

            public override Dictionary<ParameterType, FloatWithError> ReportParameters
            {
                get
                {
                    var result = new Dictionary<ParameterType, FloatWithError>();
                    foreach (var slot in ThermodynamicParameterSlots.Active(SiteCount))
                    {
                        result.Add(slot.Affinity, DissociationConstant(slot.Index));
                        result.Add(slot.Enthalpy, Enthalpy(slot.Index).FloatWithError);
                        result.Add(slot.EntropyContribution, EntropyContribution(slot.Index).FloatWithError);
                        result.Add(slot.Gibbs, GibbsFreeEnergy(slot.Index).FloatWithError);
                    }
                    return result;
                }
            }
        }
    }

    internal sealed class SequentialBindingState
    {
        internal SequentialBindingState(
            double freeLigand,
            double[] fractions,
            double meanOccupancy,
            double massBalanceResidual)
        {
            FreeLigand = freeLigand;
            Fractions = fractions;
            MeanOccupancy = meanOccupancy;
            MassBalanceResidual = massBalanceResidual;
        }

        internal double FreeLigand { get; }
        internal IReadOnlyList<double> Fractions { get; }
        internal double MeanOccupancy { get; }
        internal double MassBalanceResidual { get; }
    }
}
