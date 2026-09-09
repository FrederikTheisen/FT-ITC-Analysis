using System;
using System.Linq;
using AnalysisITC.Core.Application;

namespace AnalysisITC.Core.Interpretation
{
    public static class AnalysisInterpretationLog
    {
        // Only structural diagnostics belong here: never package text, credentials or response bodies.
        public static void Write(string stage, string requestId, string details) =>
            AppEventHandler.PrintAndLog($"[Interpretation] stage={stage} request={Token(requestId)} {details}");

        public static void Summary(string message) =>
            AppEventHandler.PrintAndLog($"[Interpretation] {message}");

        public static string Token(string value) => string.IsNullOrEmpty(value) ? "none" :
            new string(value.Take(128).Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_').ToArray());
    }
}
