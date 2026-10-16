using System;

namespace AnalysisITC.Core.Export
{
    /// <summary>Describes how much of a planned export batch reached its destinations.</summary>
    public sealed class ExportOutcome
    {
        public int PlannedCount { get; }
        public int CompletedCount { get; }
        public int ReplacedCount { get; }
        public string FailedPath { get; }
        public Exception Error { get; }
        public bool IsSuccess => Error == null;

        internal ExportOutcome(int plannedCount, int completedCount, int replacedCount, string failedPath, Exception error)
        {
            PlannedCount = plannedCount;
            CompletedCount = completedCount;
            ReplacedCount = replacedCount;
            FailedPath = failedPath;
            Error = error;
        }

        public static ExportOutcome Failure(int plannedCount, Exception error, string failedPath = null, int completedCount = 0, int replacedCount = 0) =>
            new ExportOutcome(plannedCount, completedCount, replacedCount, failedPath, error ?? throw new ArgumentNullException(nameof(error)));
    }
}
