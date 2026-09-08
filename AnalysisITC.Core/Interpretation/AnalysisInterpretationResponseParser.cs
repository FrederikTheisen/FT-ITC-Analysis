using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnalysisITC.Core.Interpretation
{
    public sealed class AnalysisInterpretationValidationException : Exception
    {
        public IReadOnlyList<string> Errors { get; }
        public AnalysisInterpretationValidationException(IEnumerable<string> errors)
            : base("The interpretation response is invalid: " + string.Join("; ", errors ?? Enumerable.Empty<string>()))
        { Errors = (errors ?? Enumerable.Empty<string>()).ToList(); }
    }

    public static class AnalysisInterpretationResponseParser
    {
        public static int WordCount(string markdown) => Regex.Matches(markdown ?? "", @"\S+").Count;

        // Formatting is a writing preference, never a condition for accepting a draft.
        // The report renderer draws unsupported syntax as text; it does not execute HTML.
        public static string ParseForApproval(string markdown) => Parse(markdown);

        public static string Parse(string markdown, AnalysisInterpretationPackage package = null) =>
            (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        public static string ParseManual(string markdown)
        {
            var normalized = Parse(markdown);
            if (normalized.Length > 0 && !normalized.StartsWith("## ", StringComparison.Ordinal))
                normalized = "## Overall interpretation\n" + normalized;
            return normalized;
        }
    }
}
