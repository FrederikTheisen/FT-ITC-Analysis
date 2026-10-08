using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.Core.Data
{
    /// <summary>Outcome shown for a result-level summary of binding assessments.</summary>
    public enum BindingAssessmentSummaryOutcome
    {
        NotAssessed,
        NoBindingDetected,
        Inconclusive,
        BindingDetected,
        Mixed,
    }

    /// <summary>Aggregation and conversion for result-level assessment summaries.</summary>
    public static class BindingAssessmentSummary
    {
        public static BindingAssessmentSummaryOutcome Aggregate(IEnumerable<BindingAssessmentOutcome> outcomes)
        {
            var distinct = (outcomes ?? Enumerable.Empty<BindingAssessmentOutcome>()).Distinct().ToList();
            if (distinct.Count == 0) return BindingAssessmentSummaryOutcome.NotAssessed;
            if (distinct.Count > 1) return BindingAssessmentSummaryOutcome.Mixed;
            return FromOutcome(distinct[0]);
        }

        public static BindingAssessmentSummaryOutcome FromOutcome(BindingAssessmentOutcome outcome) => outcome switch
        {
            BindingAssessmentOutcome.NoBindingDetected => BindingAssessmentSummaryOutcome.NoBindingDetected,
            BindingAssessmentOutcome.Inconclusive => BindingAssessmentSummaryOutcome.Inconclusive,
            BindingAssessmentOutcome.BindingDetected => BindingAssessmentSummaryOutcome.BindingDetected,
            _ => BindingAssessmentSummaryOutcome.NotAssessed,
        };
    }
}
