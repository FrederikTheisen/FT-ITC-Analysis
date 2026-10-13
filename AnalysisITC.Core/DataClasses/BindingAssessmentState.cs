using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;

namespace AnalysisITC.Core.Data
{
    public enum BindingAssessmentScope
    {
        Single,
        Independent,
        Pooled,
    }

    /// <summary>Determines whether a saved result represents one fit or an independent collection.</summary>
    public static class BindingAssessmentScopes
    {
        public static BindingAssessmentScope For(GlobalSolution solution)
            => solution?.Solutions?.Count >= 2 && solution.Model?.ShouldFitIndividually == true
                ? BindingAssessmentScope.Independent
                : solution?.Solutions?.Count == 1 ? BindingAssessmentScope.Single : BindingAssessmentScope.Pooled;
    }

    public sealed class BindingAssessmentMember
    {
        public string SolutionId { get; }
        public string SolutionName { get; }
        public SolutionInterface Member { get; }
        public BindingAssessmentState Assessment { get; }
        public NullModelComparison Comparison { get; }

        internal BindingAssessmentMember(SolutionInterface member, BindingAssessmentState assessment,
            NullModelComparison comparison)
        {
            Member = member;
            SolutionId = member?.Guid ?? string.Empty;
            SolutionName = member?.Data?.Name ?? string.Empty;
            Assessment = assessment;
            Comparison = comparison;
        }

        /// <summary>
        /// Automatic member assessments of a just-finished fit, before it is saved as a result and can carry
        /// manual overrides. Mirrors the initial assessment state of <see cref="AnalysisResult"/>.
        /// </summary>
        public static IReadOnlyList<BindingAssessmentMember> ForFit(SolutionInterface solution)
            => solution == null
                ? Array.Empty<BindingAssessmentMember>()
                : new[] { new BindingAssessmentMember(solution, BindingAssessmentState.FromComparison(solution.NullComparison), solution.NullComparison) };

        /// <inheritdoc cref="ForFit(SolutionInterface)"/>
        public static IReadOnlyList<BindingAssessmentMember> ForFit(GlobalSolution solution)
        {
            var members = solution?.Solutions ?? new List<SolutionInterface>();
            if (BindingAssessmentScopes.For(solution) == BindingAssessmentScope.Independent)
                return members.Select(member => new BindingAssessmentMember(member,
                    BindingAssessmentState.FromComparison(member?.NullComparison), member?.NullComparison)).ToList();
            var assessment = BindingAssessmentState.FromComparison(solution?.NullComparison);
            return members.Select(member => new BindingAssessmentMember(member, assessment, solution.NullComparison)).ToList();
        }
    }

    public enum BindingAssessmentOutcome
    {
        NotAssessed,
        NoBindingDetected,
        Inconclusive,
        BindingDetected,
    }

    /// <summary>Immutable automatic recommendation and optional result-level override.</summary>
    public sealed class BindingAssessmentState
    {
        public const string CurrentRuleId = "aicc-0-15-v1";

        public BindingAssessmentOutcome AutomaticOutcome { get; }
        public string AutomaticRuleId { get; }
        public BindingAssessmentOutcome? ManualOverride { get; }
        public BindingAssessmentOutcome EffectiveOutcome => ManualOverride ?? AutomaticOutcome;
        public bool IsManual => ManualOverride.HasValue;

        BindingAssessmentState(BindingAssessmentOutcome automaticOutcome, string ruleId,
            BindingAssessmentOutcome? manualOverride)
        {
            AutomaticOutcome = automaticOutcome;
            AutomaticRuleId = ruleId;
            ManualOverride = manualOverride;
        }

        public static BindingAssessmentState FromComparison(NullModelComparison comparison)
        {
            var delta = comparison?.DeltaAicc;
            var bindingCriteria = comparison?.BindingInformationCriteria;
            var nullCriteria = comparison?.NullInformationCriteria;
            var comparable = comparison != null && string.IsNullOrWhiteSpace(comparison.ComparisonUnavailableReason)
                && comparison.NullFitSucceeded
                && bindingCriteria?.IsAiccAvailable == true && nullCriteria?.IsAiccAvailable == true
                && bindingCriteria.Aicc.HasValue && nullCriteria.Aicc.HasValue
                && IsFinite(bindingCriteria.Aicc.Value) && IsFinite(nullCriteria.Aicc.Value)
                && bindingCriteria.ObservationCount == nullCriteria.ObservationCount
                && bindingCriteria.LikelihoodMode == nullCriteria.LikelihoodMode
                && delta.HasValue && IsFinite(delta.Value)
                && Math.Abs(delta.Value - (nullCriteria.Aicc.Value - bindingCriteria.Aicc.Value))
                    <= 1e-10 * Math.Max(1, Math.Abs(delta.Value));
            var outcome = comparable
                ? delta.Value <= 0 ? BindingAssessmentOutcome.NoBindingDetected
                    : delta.Value < 15 ? BindingAssessmentOutcome.Inconclusive
                    : BindingAssessmentOutcome.BindingDetected
                : BindingAssessmentOutcome.NotAssessed;
            return new BindingAssessmentState(outcome, CurrentRuleId, null);
        }

        internal static BindingAssessmentState Restore(BindingAssessmentOutcome automaticOutcome,
            string ruleId, BindingAssessmentOutcome? manualOverride)
        {
            if (!Enum.IsDefined(typeof(BindingAssessmentOutcome), automaticOutcome)
                || string.IsNullOrWhiteSpace(ruleId)
                || (manualOverride.HasValue && manualOverride != BindingAssessmentOutcome.BindingDetected
                    && manualOverride != BindingAssessmentOutcome.NoBindingDetected))
                throw new System.ArgumentException("Binding assessment metadata is invalid.");
            return new BindingAssessmentState(automaticOutcome, ruleId, manualOverride);
        }

        internal BindingAssessmentState WithOverride(BindingAssessmentOutcome? value)
            => new BindingAssessmentState(AutomaticOutcome, AutomaticRuleId, value);

        internal BindingAssessmentState WithAutomaticOutcome(BindingAssessmentOutcome value)
            => new BindingAssessmentState(value, AutomaticRuleId, ManualOverride);

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
