using System;
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
        public const string CurrentRuleId = "aicc-6-10-v1";

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
                && comparison.BindingFitSucceeded && comparison.NullFitSucceeded
                && bindingCriteria?.IsAiccAvailable == true && nullCriteria?.IsAiccAvailable == true
                && bindingCriteria.Aicc.HasValue && nullCriteria.Aicc.HasValue
                && IsFinite(bindingCriteria.Aicc.Value) && IsFinite(nullCriteria.Aicc.Value)
                && bindingCriteria.ObservationCount == nullCriteria.ObservationCount
                && bindingCriteria.LikelihoodMode == nullCriteria.LikelihoodMode
                && delta.HasValue && IsFinite(delta.Value)
                && Math.Abs(delta.Value - (nullCriteria.Aicc.Value - bindingCriteria.Aicc.Value))
                    <= 1e-10 * Math.Max(1, Math.Abs(delta.Value));
            var outcome = comparable
                ? delta.Value <= 6 ? BindingAssessmentOutcome.NoBindingDetected
                    : delta.Value < 10 ? BindingAssessmentOutcome.Inconclusive
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
