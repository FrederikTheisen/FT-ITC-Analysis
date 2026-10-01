using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Analysis.Models
{
    /// <summary>
    /// Shared thermodynamics for solutions whose binding steps are described by
    /// <see cref="ThermodynamicParameterSlots"/>. Each step is fitted as log10 K and
    /// ΔH; Kd, ΔG, −TΔS and ΔS are derived from those at the solution temperature.
    /// </summary>
    public abstract class ThermodynamicSolution : SolutionInterface
    {
        protected ThermodynamicSolution(Model model)
        {
            Model = model;
            BootstrapSolutions = new List<SolutionInterface>();
        }

        private protected virtual IEnumerable<ThermodynamicParameterSlot> ActiveSlots =>
            ThermodynamicParameterSlots.Active(Model);

        private protected FloatWithError AssociationConstantFor(ThermodynamicParameterSlot slot) =>
            FWEMath.Pow(10.0, Parameters[slot.Affinity]);

        private protected FloatWithError DissociationConstantFor(ThermodynamicParameterSlot slot) =>
            ProfileMappedParameter(slot.Affinity, value => 1.0 / Math.Pow(10.0, value), 1.0 / AssociationConstantFor(slot));

        private protected Energy EnthalpyFor(ThermodynamicParameterSlot slot) =>
            new Energy(LinkedThermodynamicParameter(slot.Enthalpy, Parameters[slot.Enthalpy]));

        private protected Energy GibbsFreeEnergyFor(ThermodynamicParameterSlot slot) =>
            new Energy(LinkedThermodynamicParameter(slot.Gibbs,
                -Energy.R.FloatWithError.Value * TempKelvin * FWEMath.Log(AssociationConstantFor(slot))));

        private protected Energy EntropyContributionFor(ThermodynamicParameterSlot slot) =>
            new Energy(LinkedThermodynamicParameter(slot.EntropyContribution,
                (GibbsFreeEnergyFor(slot) - EnthalpyFor(slot)).FloatWithError));

        private protected Energy EntropyFor(ThermodynamicParameterSlot slot) =>
            -1.0 * EntropyContributionFor(slot) / TempKelvin;

        /// <summary>
        /// Every fitted parameter keeps its best-fit value; the bootstrap
        /// replicates supply only its uncertainty.
        /// </summary>
        public override void ComputeErrorsFromBootstrapSolutions()
        {
            foreach (var key in Parameters.Keys.ToList())
                Parameters[key] = BootstrapEstimate(key);
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
                : SummarizeBootstrapDistribution(values, Parameters[key].Value);
        }

        public override List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>> DependenciesToReport
        {
            get
            {
                var dependencies = new List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>>();
                foreach (var slot in ActiveSlots)
                {
                    dependencies.Add(new Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>(
                        slot.Enthalpy, solution => ((ThermodynamicSolution)solution).EnthalpyFor(slot).FloatWithError));
                    dependencies.Add(new Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>(
                        slot.EntropyContribution, solution => ((ThermodynamicSolution)solution).EntropyContributionFor(slot).FloatWithError));
                    dependencies.Add(new Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>(
                        slot.Gibbs, solution => ((ThermodynamicSolution)solution).GibbsFreeEnergyFor(slot).FloatWithError));
                }
                return dependencies;
            }
        }
    }
}
