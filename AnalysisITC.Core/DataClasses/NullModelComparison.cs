using System.Collections.Generic;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;

namespace AnalysisITC.Core.Data
{
    /// <summary>Saved evidence from comparing a binding fit with its Null model fit.</summary>
    public sealed class NullModelComparison
    {
        public string NullModelId { get; set; } = string.Empty;
        public bool IsIndependentMemberComparison { get; set; }
        public bool BindingFitSucceeded { get; set; }
        public string BindingFitReason { get; set; } = string.Empty;
        public bool NullFitSucceeded { get; set; }
        public string NullFitReason { get; set; } = string.Empty;
        public FitInformationCriteria BindingInformationCriteria { get; set; }
        public FitInformationCriteria NullInformationCriteria { get; set; }
        public double? DeltaAicc { get; set; }
        public string ComparisonUnavailableReason { get; set; } = string.Empty;
        public List<NullModelComparisonMember> Members { get; set; } = new();
        /// <summary>Ordinary Null model solution objects used for evaluation and presentation.</summary>
        public List<SolutionInterface> NullSolutions { get; set; } = new();
    }

    public sealed class NullModelComparisonMember
    {
        public string ExperimentId { get; set; } = string.Empty;
        public double Offset { get; set; }
        public string Scope { get; set; } = "local";
        public SolverConvergenceSnapshot Convergence { get; set; }
        public List<NullModelComparisonPoint> Points { get; set; } = new();
    }

    public sealed class NullModelComparisonPoint
    {
        public int InjectionId { get; set; }
        public double InjectionMass { get; set; }
        public double Ratio { get; set; }
        public double ObservedHeatJoules { get; set; }
        public double PredictedHeatJoules { get; set; }
        public bool Included { get; set; }
    }
}
