using System;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Presentation
{
    // Builds a curve and signed contributions from original fitted coordinates.
    // Never reads ReportParameters: those properties also use this evaluator.
    internal static class LinkedThermodynamicEvaluation
    {
        internal static SummaryDependence Build(GlobalSolution solution, ThermodynamicParameterSlot slot,
            ThermodynamicParameterFamily family, SolutionInterface member = null, bool uncertainty = true)
        {
            var parameters = solution.Model.Parameters;
            var tr = solution.ReferenceTemperatureKelvin;
            var members = solution.Solutions;
            var constraint = parameters.GetConstraintForParameter(slot.Enthalpy);
            var mean = members.Average(m => m.Model.Parameters.ExperimentTemperature);
            var spread = members.Sum(m => Math.Pow(m.Model.Parameters.ExperimentTemperature - mean, 2));
            var curve = new SummaryDependence { ReferenceTemperature = tr - 273.15,
                Kind = AggregateUncertaintyKind.ModelEstimated, SlopeUncertainty = new FloatWithError(0) };
            var isH = family == ThermodynamicParameterFamily.Enthalpy;
            var isS = family == ThermodynamicParameterFamily.EntropyContribution;

            void Add(ParameterType key, SolutionInterface local, double weight, double slope, double basis)
            {
                var parameter = local == null ? parameters.GlobalTable[key] : local.Model.Parameters.Table[key];
                var value = local == null ? parameter.Value : local.Parameters[key].Value;
                curve.Intercept += value * weight;
                curve.Slope += value * slope;
                curve.HeatCapacityTerm += value * basis;
                if (!uncertainty || solution.ProfileLikelihoodRun == null) return;
                var coordinate = solution.ProfileLikelihoodRun.Coordinates.FirstOrDefault(c =>
                    c.Id.Parameter == key && c.Id.Scope == (local == null ? ParameterBoundaryScope.Shared : ParameterBoundaryScope.Local)
                    && (local == null || c.Id.ExperimentIdentity == local.Data.UniqueID));
                var error = parameter.IsLocked ? new FloatWithError(value)
                    : coordinate?.HasCompleteInterval == true ? coordinate.ToFloatWithError()
                    : SummaryUncertainty.Unavailable(value);
                curve.Contributions.Add(new SummaryErrorContribution(weight, slope, error.SD,
                    error.LowerWidth, error.UpperWidth, basis));
            }

            if (!isH) Add(slot.Gibbs, null, 1, 1 / tr, 0);
            void AddEnthalpy(ParameterType key, SolutionInterface local, double a, double b, double memberWeight)
            {
                // H trend = a + b*(T-Tr). Member entropy subtracts its actual H.
                var subtractA = member == null ? a : memberWeight;
                var subtractB = member == null ? b : 0;
                Add(key, local, isH ? subtractA : isS ? -subtractA : 0,
                    isH ? subtractB : -a / tr - (isS ? subtractB : 0), isH ? 0 : b);
            }
            if (constraint == VariableConstraint.None)
            {
                foreach (var local in members)
                {
                    var b = spread == 0 ? 0 : (local.Model.Parameters.ExperimentTemperature - mean) / spread;
                    var a = 1.0 / members.Count + (tr - mean) * b;
                    AddEnthalpy(slot.Enthalpy, local, a, b, local == member ? 1 : 0);
                }
            }
            else
            {
                AddEnthalpy(slot.Enthalpy, null, 1, 0, 1);
                if (constraint == VariableConstraint.TemperatureDependent)
                    AddEnthalpy(slot.HeatCapacity, null, 0, 1,
                        member == null ? 0 : member.Model.Parameters.ExperimentTemperature - tr);
            }
            if (uncertainty && solution.ProfileLikelihoodRun == null && solution.BootstrapSolutions?.Count > 0)
            {
                foreach (var replicate in solution.BootstrapSolutions)
                {
                    // Older files may have member snapshots without shared coordinates.
                    if (!replicate.Model.Parameters.GlobalTable.ContainsKey(slot.Gibbs)) continue;
                    var replicateMember = member == null ? null : replicate.Solutions.FirstOrDefault(m => m.Data.UniqueID == member.Data.UniqueID);
                    if (member != null && replicateMember == null) continue;
                    curve.Replicates.Add(Build(replicate, slot, family, replicateMember, false));
                }
            }
            // Historical files can contain local uncertainty snapshots without global coordinates.
            // Keep their existing uncertainty representation; it cannot be corrected without refitting.
            if (uncertainty && solution.ProfileLikelihoodRun == null && curve.Replicates.Count == 0
                && solution.Solutions.Any(m => m.BootstrapSolutions.Count > 0)
                && solution.TemperatureDependence.TryGetValue(slot.Get(family), out var historical))
            {
                var old = SummaryUncertainty.Model(historical);
                foreach (var term in old.Contributions)
                    curve.Contributions.Add(new SummaryErrorContribution(
                        term.Weight + (curve.ReferenceTemperature - old.ReferenceTemperature) * term.WeightSlope,
                        term.WeightSlope, term.Sd, term.LowerWidth, term.UpperWidth));
            }
            if (!uncertainty) return curve;

            var slope = new SummaryDependence { Intercept = curve.Slope };
            foreach (var contribution in curve.Contributions)
                slope.Contributions.Add(new SummaryErrorContribution(contribution.WeightSlope, 0,
                    contribution.Sd, contribution.LowerWidth, contribution.UpperWidth));
            curve.SlopeUncertainty = curve.Replicates.Count > 0
                ? FloatWithError.FromDistributionInPlace(curve.Replicates.Select(replicate => replicate.Slope).ToList(), curve.Slope)
                : slope.Evaluate(0);
            return curve;
        }
    }
}
