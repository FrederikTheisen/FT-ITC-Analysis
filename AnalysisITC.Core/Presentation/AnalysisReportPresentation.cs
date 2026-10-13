using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Presentation
{
    public static class AnalysisReportPresentation
    {
        public static string AutomaticTitle(IEnumerable<AnalysisResult> results)
        {
            var selected = (results ?? Enumerable.Empty<AnalysisResult>()).Where(item => item != null).ToList();
            return selected.Count == 1 ? selected[0].Name : "Analysis report";
        }

        public static string DisplayName(AnalysisReport report, Func<string, AnalysisResult> resultResolver)
        {
            if (report == null) return "Analysis report";
            if (!string.IsNullOrEmpty(report.Name)) return report.Name;
            var result = report.ResultIds.Count == 1 ? resultResolver?.Invoke(report.ResultIds[0]) : null;
            return result != null ? result.Name + " report" : "Analysis report";
        }

        public static bool MatchesResultIds(AnalysisReport report, IEnumerable<string> resultIds) =>
            report != null && new HashSet<string>(report.ResultIds, StringComparer.Ordinal)
                .SetEquals(resultIds ?? Enumerable.Empty<string>());

        public static AnalysisReport FindMatchingReport(IEnumerable<AnalysisReport> reports, IEnumerable<string> resultIds)
        {
            var ids = (resultIds ?? Enumerable.Empty<string>()).ToArray();
            // Earlier reports used local creation times, including dates without
            // a Kind. Compare instants while retaining their saved date metadata.
            return (reports ?? Enumerable.Empty<AnalysisReport>())
                .Where(report => MatchesResultIds(report, ids)).OrderBy(report => report.Date.ToUniversalTime()).LastOrDefault();
        }

        public static int MissingResultCount(AnalysisReport report, Func<string, AnalysisResult> resultResolver) =>
            report?.ResultIds.Count(id => resultResolver?.Invoke(id) == null) ?? 0;

        public static string MissingResultStatus(AnalysisReport report, Func<string, AnalysisResult> resultResolver)
        {
            var missing = MissingResultCount(report, resultResolver);
            if (missing == 0) return "";
            var count = report.ResultIds.Count;
            return missing + " of " + count + " result" + (count == 1 ? "" : "s") + " missing";
        }

        public static IReadOnlyList<AnalysisResult> ResultsInProjectOrder(AnalysisReport report, IEnumerable<AnalysisResult> projectResults)
        {
            var ids = new HashSet<string>(report?.ResultIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            return (projectResults ?? Enumerable.Empty<AnalysisResult>())
                .Where(result => result != null && ids.Contains(result.UniqueID)).ToArray();
        }

        internal static List<AnalysisResult> OrderResults(IEnumerable<AnalysisResult> results, IEnumerable<string> resultOrderIds)
        {
            var selected = results.ToList();
            if (resultOrderIds == null) return selected;
            var order = resultOrderIds.Distinct(StringComparer.Ordinal).ToList();
            return selected.OrderBy(result =>
            {
                var index = order.IndexOf(result.UniqueID);
                return index < 0 ? int.MaxValue : index;
            }).ToList();
        }
    }
}
