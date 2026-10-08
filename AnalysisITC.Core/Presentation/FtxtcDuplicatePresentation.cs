using System;
using System.Linq;
using AnalysisITC.Core.DataReaders;

namespace AnalysisITC.Core.Presentation
{
    public static class FtxtcDuplicatePresentation
    {
        public const string Title = "Objects Already Loaded";
        public const string SkipLabel = "Skip Duplicates";
        public const string CopyLabel = "Import Copies";
        public const string SkipToolTip = "Keep existing objects and import new content";
        public const string CopyToolTip = "Import separate copies with their own references";

        public static string Message(FtxtcDuplicateSummary summary)
        {
            var counts = new[]
            {
                Count(summary.ExperimentCount, "experiment"),
                Count(summary.ResultCount, "Analysis Result"),
                Count(summary.ReportCount, "report"),
                Count(summary.SolutionCount, "saved fit"),
            }.Where(value => value != null);
            var names = summary.Names.Count == 0 ? "" : "\n\n" + string.Join("\n", summary.Names.Take(8))
                + (summary.Names.Count > 8 ? "\n…" : "");
            return $"{summary.FileName} contains objects already loaded: {string.Join(", ", counts)}.{names}"
                + "\n\nSkip Duplicates keeps the existing objects and imports new content. "
                + "Import Copies creates separate copies and keeps references within this file connected to those copies.";
        }

        static string Count(int count, string label) => count == 0 ? null : $"{count} {label}{(count == 1 ? "" : "s")}";
    }
}
