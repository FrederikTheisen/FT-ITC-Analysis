using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Presentation
{
    public sealed class ReducedChiSquareDiagnostic
    {
        public double Value { get; internal set; }
        public int ObservationCount { get; internal set; }
        public int ParameterCount { get; internal set; }
    }

    public static class ReducedChiSquarePresentation
    {
        public const string Label = "Reduced chi-square";
        public const string NullLabel = "Null reduced chi-square";

        public static ReducedChiSquareDiagnostic ForFit(SolutionInterface solution)
        {
            if (solution == null || (solution.ParentSolution?.Solutions?.Count > 1
                && solution.ParentSolution.Model?.ShouldFitIndividually == false)) return null;
            return Pool(new[] { solution.Convergence });
        }

        public static ReducedChiSquareDiagnostic ForResult(AnalysisResult result)
        {
            var solution = result?.Solution;
            if (solution == null) return null;
            if (result.AssessmentScope == BindingAssessmentScope.Independent)
            {
                var members = solution.Solutions;
                var expected = solution.Model?.Models;
                if (members == null || expected == null || members.Count != expected.Count
                    || members.Any(m => m?.Data == null)
                    || members.Select(m => m.Data.UniqueID).Distinct().Count() != expected.Count
                    || expected.Any(m => m?.Data == null || !members.Any(s => s.Data.UniqueID == m.Data.UniqueID))) return null;
                return Pool(members.Select(m => m.Convergence));
            }
            return result.AssessmentScope == BindingAssessmentScope.Single
                ? ForFit(solution.Solutions?.FirstOrDefault()) : Pool(new[] { solution.Convergence });
        }

        public static ReducedChiSquareDiagnostic ForNull(NullModelComparison comparison, IEnumerable<string> expectedExperimentIds = null)
        {
            if (comparison?.NullFitSucceeded != true) return null;
            var expected = (expectedExperimentIds ?? comparison.Members?.Select(m => m.ExperimentId) ?? Enumerable.Empty<string>()).ToList();
            var solutions = comparison.NullSolutions;
            if (expected.Count == 0 || expected.Any(string.IsNullOrEmpty) || expected.Distinct().Count() != expected.Count
                || solutions == null || solutions.Count != expected.Count || solutions.Any(s => s?.Data == null)
                || solutions.Select(s => s.Data.UniqueID).Distinct().Count() != expected.Count
                || expected.Any(id => !solutions.Any(s => s.Data.UniqueID == id))) return null;
            return Pool(solutions.Select(s => s.Convergence));
        }

        static ReducedChiSquareDiagnostic Pool(IEnumerable<SolverConvergence> convergences)
        {
            var list = convergences?.ToList();
            if (list == null || list.Count == 0) return null;
            double q = 0;
            long n = 0, p = 0;
            foreach (var c in list)
            {
                if (c?.ReducedChiSquareQ == null || !FWEMath.IsFinite(c.ReducedChiSquareQ.Value) || c.ReducedChiSquareQ < 0
                    || !c.ReducedChiSquareObservationCount.HasValue || c.ReducedChiSquareObservationCount <= 0
                    || !c.ReducedChiSquareParameterCount.HasValue || c.ReducedChiSquareParameterCount < 0) return null;
                q += c.ReducedChiSquareQ.Value;
                n += c.ReducedChiSquareObservationCount.Value;
                p += c.ReducedChiSquareParameterCount.Value;
            }
            if (!FWEMath.IsFinite(q) || n > int.MaxValue || p > int.MaxValue || n <= p) return null;
            var value = q / (n - p);
            return FWEMath.IsFinite(value) ? new ReducedChiSquareDiagnostic { Value = value, ObservationCount = (int)n, ParameterCount = (int)p } : null;
        }

        public static string Format(ReducedChiSquareDiagnostic diagnostic) => diagnostic == null ? string.Empty : diagnostic.Value.ToString("G4", CultureInfo.CurrentCulture);
    }
}
