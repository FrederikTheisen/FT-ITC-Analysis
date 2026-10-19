using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Presentation
{
    public enum AnalysisWarningCategory
    {
        None,
        ParameterBoundary,
        UncertaintyBoundary,
        UncertaintyLimit,
        Inconclusive,
    }

    public sealed class AnalysisResultHealthReason
    {
        internal AnalysisResultHealthReason(string message, AnalysisWarningCategory category = AnalysisWarningCategory.None,
            int? memberIndex = null)
        {
            Message = message;
            Category = category;
            MemberIndex = memberIndex;
        }
        public string Message { get; }
        public AnalysisWarningCategory Category { get; }
        public int? MemberIndex { get; }
    }

    public static class AnalysisResultHealthReasonFormatter
    {
        public const string InconclusiveMessage = "Binding assessment is inconclusive.";

        public static IReadOnlyList<string> Format(AnalysisResult result)
            => FormatStructured(result).Select(reason => reason.Message).Distinct().ToList();

        public static IReadOnlyList<AnalysisResultHealthReason> FormatStructured(AnalysisResult result)
        {
            var reasons = AnalysisResultValidityReasonFormatter.Format(result)
                .Select(message => new AnalysisResultHealthReason(message)).ToList();
            if (result?.Solution?.Solutions != null)
            {
                var members = result.Solution.Solutions;
                reasons.AddRange(result.FitWarningMembers.SelectMany(solution =>
                    FitReasons(solution, result.Solution.ErrorEstimationMethod, members.IndexOf(solution))));
                if (result.IsIndependentAssessmentCollection)
                    reasons.AddRange(result.MemberAssessments
                        .Where(member => member.Assessment?.EffectiveOutcome == BindingAssessmentOutcome.Inconclusive)
                        .Select(member => new AnalysisResultHealthReason($"{member.SolutionName}: {InconclusiveMessage}",
                            AnalysisWarningCategory.Inconclusive, members.IndexOf(member.Member))));
                else if (result.BindingAssessment?.EffectiveOutcome == BindingAssessmentOutcome.Inconclusive)
                    reasons.Add(new AnalysisResultHealthReason(InconclusiveMessage, AnalysisWarningCategory.Inconclusive));
            }
            return reasons;
        }

        internal static IEnumerable<AnalysisResultHealthReason> FitReasons(SolutionInterface solution,
            ErrorEstimationMethod method, int memberIndex)
        {
            if (solution.ParameterBoundaryHit)
                yield return new AnalysisResultHealthReason(ParameterBoundaryWarningFormatter.BestFitMessage,
                    AnalysisWarningCategory.ParameterBoundary, memberIndex);
            if (solution.BootstrapParameterBoundaryHit)
                yield return new AnalysisResultHealthReason(ParameterBoundaryWarningFormatter.ErrorEstimationMessage(method),
                    AnalysisWarningCategory.UncertaintyBoundary, memberIndex);
            if (solution.Convergence?.HasErrorEstimationLimitWarnings == true)
                yield return new AnalysisResultHealthReason(ParameterBoundaryWarningFormatter.ErrorEstimationLimitMessage(method),
                    AnalysisWarningCategory.UncertaintyLimit, memberIndex);
        }

        internal static IReadOnlyList<string> FormatReport(AnalysisResult result, IReadOnlyList<string> labels)
        {
            var reasons = FormatStructured(result);
            var output = reasons.Where(reason => reason.Category == AnalysisWarningCategory.None)
                .Select(reason => reason.Message).Distinct().ToList();
            foreach (var group in reasons.Where(reason => reason.Category != AnalysisWarningCategory.None)
                .GroupBy(reason => reason.Category).OrderBy(group => group.Key))
            {
                var message = group.Key == AnalysisWarningCategory.Inconclusive ? InconclusiveMessage : group.First().Message;
                output.Add(group.Any(reason => !reason.MemberIndex.HasValue) ? message
                    : AnalysisReportWarningSummary.WithMembers(message, result.Solution.Solutions.Count,
                        group.Select(reason => reason.MemberIndex.Value), index => labels[index]));
            }
            return output;
        }
    }

    internal static class AnalysisReportWarningSummary
    {
        internal static string WithMembers(string message, int memberCount, IEnumerable<int> memberIndices,
            Func<int, string> label)
        {
            if (memberCount <= 1) return message;
            var indices = memberIndices.Distinct().OrderBy(index => index).ToList();
            var references = new List<string>();
            for (var first = 0; first < indices.Count;)
            {
                var last = first;
                while (last + 1 < indices.Count && indices[last + 1] == indices[last] + 1) last++;
                if (last - first >= 2)
                    references.Add(label(indices[first]) + "–" + label(indices[last]));
                else
                    for (var index = first; index <= last; index++) references.Add(label(indices[index]));
                first = last + 1;
            }
            return message + " " + indices.Count + " of " + memberCount + " experiments ("
                + string.Join(", ", references) + ").";
        }

        internal static IReadOnlyList<string> Appendix(IEnumerable<AnalysisReportDiagnostic> diagnostics)
        {
            var warnings = diagnostics.Where(item => item.Severity == AnalysisReportDiagnosticSeverity.Warning).ToList();
            var emitted = new HashSet<(int, AnalysisWarningCategory)>();
            var output = new List<string>();
            foreach (var warning in warnings)
            {
                var context = warning.WarningContext;
                if (context == null || context.Category == AnalysisWarningCategory.None)
                {
                    output.Add(warning.Message);
                    continue;
                }
                if (!emitted.Add((context.ResultIndex, context.Category))) continue;
                var members = warnings.Where(item => item.WarningContext?.ResultIndex == context.ResultIndex
                    && item.WarningContext.Category == context.Category).Select(item => item.WarningContext).ToList();
                var message = context.Category == AnalysisWarningCategory.ParameterBoundary
                    ? ParameterBoundaryWarningFormatter.BestFitMessage
                    : context.Category == AnalysisWarningCategory.UncertaintyBoundary
                        ? ParameterBoundaryWarningFormatter.ErrorEstimationMessage(context.UncertaintyMethod)
                        : ParameterBoundaryWarningFormatter.ErrorEstimationLimitMessage(context.UncertaintyMethod);
                output.Add(context.ResultName + ": " + WithMembers(message, context.MemberCount,
                    members.Select(item => item.MemberIndex), index => members.First(item => item.MemberIndex == index).MemberLabel));
            }
            return output;
        }
    }
}
