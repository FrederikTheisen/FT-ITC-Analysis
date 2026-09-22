using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Analysis
{
    internal sealed class AnalysisCValue
    {
        public string QuantityId { get; }
        public string Label { get; }
        public string Kind { get; }
        public int? Index { get; }
        public string ConcentrationBasis { get; }
        public FloatWithError? Estimate { get; }

        public bool IsAvailable => Estimate.HasValue;

        public AnalysisCValue(
            string quantityId,
            string label,
            string kind,
            int? index,
            string concentrationBasis,
            FloatWithError? estimate)
        {
            QuantityId = quantityId;
            Label = label;
            Kind = kind;
            Index = index;
            ConcentrationBasis = concentrationBasis;
            Estimate = estimate;
        }
    }

    internal static class AnalysisCValueCalculator
    {
        const string WisemanKind = "wiseman";
        const string SequentialKind = "sequential-step";
        const string CompetitiveKind = "competitive-apparent";

        public static IReadOnlyList<AnalysisCValue> Calculate(SolutionInterface solution)
        {
            if (solution?.Data == null) return Array.Empty<AnalysisCValue>();

            var concentration = InitialCellConcentration(solution, out var concentrationBasis);
            switch (solution)
            {
                case OneSetOfSites.ModelSolution one:
                    return new[]
                    {
                        Create("wiseman-c", "Wiseman c-value", WisemanKind, null, concentrationBasis,
                            concentration, Stoichiometry(solution, one.N, AttributeKey.NumberOfSites1), one.Kd)
                    };

                case TwoSetsOfSites.ModelSolution two:
                    return new[]
                    {
                        Create("wiseman-c-site-1", "Wiseman c-value (site 1)", WisemanKind, 1, concentrationBasis,
                            concentration, Stoichiometry(solution, two.N1, AttributeKey.NumberOfSites1), two.Kd1),
                        Create("wiseman-c-site-2", "Wiseman c-value (site 2)", WisemanKind, 2, concentrationBasis,
                            concentration, Stoichiometry(solution, two.N2, AttributeKey.NumberOfSites2), two.Kd2),
                    };

                case SequentialBindingSites.ModelSolution sequential:
                    return Enumerable.Range(1, sequential.SiteCount)
                        .Select(step => Create(
                            "c-step-" + step,
                            "c-value (step " + step + ")",
                            SequentialKind,
                            step,
                            concentrationBasis,
                            concentration,
                            new FloatWithError(1),
                            sequential.DissociationConstant(step)))
                        .ToList();

                case CompetitiveBinding.ModelSolution competitive:
                    return new[]
                    {
                        Create("apparent-c", "Apparent c-value", CompetitiveKind, null, concentrationBasis,
                            concentration, Stoichiometry(solution, competitive.N, AttributeKey.NumberOfSites1),
                            ApparentKd(competitive))
                    };

                default:
                    return Array.Empty<AnalysisCValue>();
            }
        }

        static FloatWithError InitialCellConcentration(
            SolutionInterface solution,
            out string basis)
        {
            var firstSegment = solution.Data.Segments?
                .OrderBy(segment => segment.FirstInjectionID)
                .FirstOrDefault();
            basis = firstSegment == null
                ? "initial-cell-concentration"
                : "initial-tandem-segment";
            return firstSegment == null ? solution.Data.CellConcentration
                : new FloatWithError(firstSegment.SegmentInitialActiveCellConc);
        }

        static FloatWithError? Stoichiometry(
            SolutionInterface solution,
            FloatWithError fitted,
            AttributeKey fixedSiteKey)
        {
            var syringeCorrection = solution.ModelOptions.TryGetValue(
                AttributeKey.UseSyringeActiveFraction, out var option) && option.BoolValue;
            if (!syringeCorrection) return fitted;
            return solution.ModelOptions.TryGetValue(fixedSiteKey, out var fixedSites)
                ? new FloatWithError(fixedSites.DoubleValue)
                : (FloatWithError?)null;
        }

        static FloatWithError? ApparentKd(CompetitiveBinding.ModelSolution solution)
        {
            try { return solution.Kdapp; }
            catch (ArithmeticException) { return null; }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        static AnalysisCValue Create(
            string quantityId,
            string label,
            string kind,
            int? index,
            string concentrationBasis,
            FloatWithError concentration,
            FloatWithError? stoichiometry,
            FloatWithError? dissociationConstant)
        {
            if (!Positive(concentration) || !stoichiometry.HasValue || !Positive(stoichiometry.Value)
                || !dissociationConstant.HasValue || !Positive(dissociationConstant.Value))
                return new AnalysisCValue(quantityId, label, kind, index, concentrationBasis, null);

            var estimate = stoichiometry.Value * concentration / dissociationConstant.Value;
            return Positive(estimate)
                ? new AnalysisCValue(quantityId, label, kind, index, concentrationBasis, estimate)
                : new AnalysisCValue(quantityId, label, kind, index, concentrationBasis, null);
        }

        static bool Positive(FloatWithError value) =>
            !FloatWithError.IsNaN(value)
            && !double.IsNaN(value.Value)
            && !double.IsInfinity(value.Value)
            && value.Value > 0;
    }
}
