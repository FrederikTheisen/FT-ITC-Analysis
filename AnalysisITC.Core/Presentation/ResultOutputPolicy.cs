using AnalysisITC.Core.Data;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using System.Linq;

namespace AnalysisITC.Core.Presentation
{
    public enum ResultOutputPurpose
    {
        Standard,
        Diagnostic
    }

    /// <summary>Central policy for binding-derived content in result outputs.</summary>
    public static class ResultOutputPolicy
    {
        public static bool IsBindingOutputAllowed(BindingAssessmentOutcome outcome)
            => BindingAssessmentInterpretation.TreatAsBinding(outcome);

        public static bool IsMemberBindingOutputAllowed(AnalysisResult result, SolutionInterface member,
            ResultOutputPurpose purpose = ResultOutputPurpose.Standard)
        {
            if (purpose == ResultOutputPurpose.Diagnostic) return true;
            var assessment = result?.IsIndependentAssessmentCollection == true
                ? result.GetMemberBindingAssessment(member)?.EffectiveOutcome
                : result?.BindingAssessment?.EffectiveOutcome;
            return IsBindingOutputAllowed(assessment ?? BindingAssessmentOutcome.NotAssessed);
        }

        public static bool IsCombinedBindingOutputAllowed(AnalysisResult result,
            ResultOutputPurpose purpose = ResultOutputPurpose.Standard)
        {
            if (purpose == ResultOutputPurpose.Diagnostic) return true;
            if (result?.IsIndependentAssessmentCollection == true)
                return result.MemberAssessments.All(member => IsBindingOutputAllowed(
                    member.Assessment?.EffectiveOutcome ?? BindingAssessmentOutcome.NotAssessed));
            return IsBindingOutputAllowed(result?.BindingAssessment?.EffectiveOutcome
                ?? BindingAssessmentOutcome.NotAssessed);
        }

        public static bool SuppressBindingOutputs(AnalysisResult result,
            ResultOutputPurpose purpose = ResultOutputPurpose.Standard)
            => !IsCombinedBindingOutputAllowed(result, purpose);

        public static string FormatModelName(AnalysisResult result, string modelName,
            ResultOutputPurpose purpose = ResultOutputPurpose.Standard)
            => SuppressBindingOutputs(result, purpose)
                ? "Attempted: " + (modelName ?? "")
                : modelName ?? "";
    }
}
