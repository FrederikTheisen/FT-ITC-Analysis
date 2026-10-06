using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Presentation
{
    public static class AnalysisResultHealthReasonFormatter
    {
        public const string InconclusiveMessage = "Binding assessment is inconclusive.";

        public static IReadOnlyList<string> Format(AnalysisResult result)
        {
            var reasons = AnalysisResultValidityReasonFormatter.Format(result).ToList();
            if (result?.Solution?.Solutions != null)
            {
                reasons.AddRange(result.FitWarningMembers.SelectMany(solution =>
                    ParameterBoundaryWarningFormatter.MessagesFor(solution, result.Solution.ErrorEstimationMethod)));
                if (result.IsIndependentAssessmentCollection)
                    reasons.AddRange(result.MemberAssessments
                        .Where(member => member.Assessment?.EffectiveOutcome == BindingAssessmentOutcome.Inconclusive)
                        .Select(member => $"{member.SolutionName}: {InconclusiveMessage}"));
                else if (result.BindingAssessment?.EffectiveOutcome == BindingAssessmentOutcome.Inconclusive)
                    reasons.Add(InconclusiveMessage);
            }
            return reasons.Distinct().ToList();
        }
    }
}
