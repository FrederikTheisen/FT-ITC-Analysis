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
        const double RelativeMassBalanceTolerance = 8.8817841970012523e-16; // Four double-precision epsilons.
        const double AbsoluteMassBalanceTolerance = 1e-24;
        const int MaximumBisectionIterations = 512;

        int initializedSiteCount;
        readonly Func<double, double, double> heatContent;
        ObjectiveEvaluation evaluation;

        // Cumulative count of state solves requested by heat evaluation on this model instance.
        internal long HeatContentSolveCount { get; private set; }

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
            heatContent = HeatContent;
        }

        protected override void BeginObjectiveEvaluation() => evaluation = new ObjectiveEvaluation();

        protected override void EndObjectiveEvaluation() => evaluation = null;

        public override void InitializeParameters(ExperimentData data)
        {
            base.InitializeParameters(data);
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
            var heat = DeltaHeatFromHeatContent(injectionindex, heatContent);
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

            var context = evaluation;
            if (context != null && context.OwnerThreadId != Environment.CurrentManagedThreadId) context = null;
            if (context != null && context.Memo.TryGet(totalMacromolecule, totalLigand, out var cached)) return cached;

            if (context != null)
            {
                ValidateStateInputs(totalMacromolecule, totalLigand, RelativeMassBalanceTolerance);
                if (context.Workspace == null)
                    context.Workspace = new EquilibriumWorkspace(CumulativeLogAssociationConstants(SiteCount));
                var workspace = context.Workspace;
                HeatContentSolveCount++;
                SolveState(totalMacromolecule, totalLigand, RelativeMassBalanceTolerance, workspace);
                if (context.CumulativeEnthalpies == null)
                {
                    var enthalpies = new double[workspace.LogBeta.Length];
                    foreach (var slot in ThermodynamicParameterSlots.Active(SiteCount))
                        enthalpies[slot.Index] = enthalpies[slot.Index - 1] + Parameters.Table[slot.Enthalpy].Value;
                    context.CumulativeEnthalpies = enthalpies;
                }
                var heatPerMolecule = 0.0;
                for (var step = 1; step < workspace.Fractions.Length; step++)
                    heatPerMolecule += workspace.Fractions[step] * context.CumulativeEnthalpies[step];
                var heat = Data.CellVolume * totalMacromolecule * heatPerMolecule;
                context.Memo.Store(totalMacromolecule, totalLigand, heat);
                return heat;
            }

            HeatContentSolveCount++;
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
            ValidateStateInputs(totalMacromolecule, totalLigand, relativeTolerance);
            var workspace = new EquilibriumWorkspace(CumulativeLogAssociationConstants(SiteCount));
            var state = SolveState(totalMacromolecule, totalLigand, relativeTolerance, workspace);
            // This workspace belongs to this diagnostic result, never to an objective memo.
            return new SequentialBindingState(state.FreeLigand, workspace.Fractions,
                state.MeanOccupancy, state.MassBalanceResidual);
        }

        static void ValidateStateInputs(double totalMacromolecule, double totalLigand, double relativeTolerance)
        {
            if (!IsFinite(totalMacromolecule) || totalMacromolecule < 0)
                throw new ArgumentOutOfRangeException(nameof(totalMacromolecule));
            if (!IsFinite(totalLigand) || totalLigand < 0)
                throw new ArgumentOutOfRangeException(nameof(totalLigand));
            if (!IsFinite(relativeTolerance) || relativeTolerance <= 0)
                throw new ArgumentOutOfRangeException(nameof(relativeTolerance));
        }

        static EquilibriumState SolveState(double totalMacromolecule, double totalLigand,
            double relativeTolerance, EquilibriumWorkspace workspace)
        {
            var count = workspace.LogBeta.Length - 1;
            if (totalLigand == 0)
                return StateAtFreeLigand(0, totalMacromolecule, totalLigand, workspace);

            if (totalMacromolecule == 0)
                return StateAtFreeLigand(totalLigand, totalMacromolecule, totalLigand, workspace);

            var lower = 0.0;
            var upper = totalLigand;
            var tolerance = Math.Max(AbsoluteMassBalanceTolerance,
                relativeTolerance * Math.Max(totalLigand, count * totalMacromolecule));
            var upperState = StateAtFreeLigand(upper, totalMacromolecule, totalLigand, workspace);
            if (Math.Abs(upperState.MassBalanceResidual) <= tolerance) return upperState;

            for (var iteration = 0; iteration < MaximumBisectionIterations; iteration++)
            {
                var midpoint = lower + (upper - lower) * 0.5;
                if (midpoint == lower || midpoint == upper) break;

                var midpointState = StateAtFreeLigand(
                    midpoint, totalMacromolecule, totalLigand, workspace);
                if (Math.Abs(midpointState.MassBalanceResidual) <= tolerance)
                    return midpointState;

                if (midpointState.MassBalanceResidual > 0)
                    upper = midpoint;
                else
                    lower = midpoint;
            }

            var resolved = lower + (upper - lower) * 0.5;
            if (resolved == lower || resolved == upper)
                resolved = Math.Abs(StateAtFreeLigand(lower, totalMacromolecule, totalLigand, workspace).MassBalanceResidual)
                    <= Math.Abs(StateAtFreeLigand(upper, totalMacromolecule, totalLigand, workspace).MassBalanceResidual)
                    ? lower
                    : upper;
            return StateAtFreeLigand(resolved, totalMacromolecule, totalLigand, workspace);
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

        static EquilibriumState StateAtFreeLigand(
            double freeLigand,
            double totalMacromolecule,
            double totalLigand,
            EquilibriumWorkspace workspace)
        {
            var logBeta = workspace.LogBeta;
            var count = logBeta.Length - 1;
            var fractions = workspace.Fractions;
            if (freeLigand == 0)
            {
                Array.Clear(fractions, 0, fractions.Length);
                fractions[0] = 1;
                return new EquilibriumState(0, 0, -totalLigand);
            }

            var logFree = Math.Log(freeLigand);
            var logWeights = workspace.LogWeights;
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
            return new EquilibriumState(freeLigand, meanOccupancy, residual);
        }

        sealed class ObjectiveEvaluation
        {
            internal readonly int OwnerThreadId = Environment.CurrentManagedThreadId;
            internal HeatContentMemo Memo;
            internal EquilibriumWorkspace Workspace;
            internal double[] CumulativeEnthalpies;
        }

        sealed class EquilibriumWorkspace
        {
            internal EquilibriumWorkspace(double[] logBeta)
            {
                LogBeta = logBeta;
                Fractions = new double[logBeta.Length];
                LogWeights = new double[logBeta.Length];
            }

            internal double[] LogBeta { get; }
            internal double[] Fractions { get; }
            internal double[] LogWeights { get; }
        }

        readonly struct EquilibriumState
        {
            internal EquilibriumState(double freeLigand, double meanOccupancy, double massBalanceResidual)
            {
                FreeLigand = freeLigand;
                MeanOccupancy = meanOccupancy;
                MassBalanceResidual = massBalanceResidual;
            }

            internal double FreeLigand { get; }
            internal double MeanOccupancy { get; }
            internal double MassBalanceResidual { get; }
        }

        internal override Model GenerateSyntheticModel(Random random)
        {
            return GenerateSyntheticModel(random, ModelCloneOptions);
        }

        internal override Model GenerateSyntheticModel(Random random, ModelCloneOptions options)
        {
            var model = new SequentialBindingSites(Data.GetSynthClone(options, random));
            SetSynthModelParameters(model, random, options);
            model.initializedSiteCount = SiteCount;
            return model;
        }

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public sealed class ModelSolution : ThermodynamicSolution
        {
            public int SiteCount => ((SequentialBindingSites)Model).SiteCount;

            public ModelSolution(Model model) : base(model)
            {
            }

            private protected override IEnumerable<ThermodynamicParameterSlot> ActiveSlots =>
                ThermodynamicParameterSlots.Active(SiteCount);

            public FloatWithError AssociationConstant(int step) =>
                AssociationConstantFor(ThermodynamicParameterSlots.ForStep(step));

            public FloatWithError DissociationConstant(int step) =>
                DissociationConstantFor(ThermodynamicParameterSlots.ForStep(step));

            public Energy Enthalpy(int step) => EnthalpyFor(ThermodynamicParameterSlots.ForStep(step));

            public Energy GibbsFreeEnergy(int step) => GibbsFreeEnergyFor(ThermodynamicParameterSlots.ForStep(step));

            public Energy EntropyContribution(int step) => EntropyContributionFor(ThermodynamicParameterSlots.ForStep(step));

            public Energy Entropy(int step) => EntropyFor(ThermodynamicParameterSlots.ForStep(step));

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
