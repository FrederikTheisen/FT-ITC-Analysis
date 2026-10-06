namespace AnalysisITC.Core.Data
{
    /// <summary>
    /// Single source for deciding whether a binding assessment is treated as binding.
    /// Call this instead of comparing <see cref="BindingAssessmentOutcome"/> values directly,
    /// so output policy, result health, and future consumers stay consistent.
    /// Only an effective <see cref="BindingAssessmentOutcome.NoBindingDetected"/> is treated as non-binding;
    /// inconclusive and not-assessed outcomes keep binding behavior.
    /// </summary>
    public static class BindingAssessmentInterpretation
    {
        public static bool TreatAsBinding(BindingAssessmentOutcome outcome)
            => outcome == BindingAssessmentOutcome.BindingDetected
                || outcome == BindingAssessmentOutcome.Inconclusive
                || outcome == BindingAssessmentOutcome.NotAssessed;

        public static bool TreatAsBinding(BindingAssessmentState assessment)
            => TreatAsBinding(assessment?.EffectiveOutcome ?? BindingAssessmentOutcome.NotAssessed);
    }
}
