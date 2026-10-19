using System;
using System.Globalization;
using AnalysisITC.Core.Analysis;

namespace AnalysisITC.Core.Presentation
{
    public enum FittingPhase { Fitting, AssessingBinding, EstimatingUncertainty, Stopping }
    public enum FittingScope { Single, Individual, Global }

    public sealed class FittingProgress
    {
        public Guid OperationId { get; }
        public FittingPhase Phase { get; }
        public FittingScope Scope { get; }
        public int MemberPosition { get; }
        public int MemberCount { get; }
        public string MemberName { get; }
        public ErrorEstimationMethod Method { get; }
        public int? Completed { get; }
        public int? Total { get; }
        public float? Fraction { get; }
        public FittingProgress(Guid operationId, FittingPhase phase, FittingScope scope,
            int memberPosition = 0, int memberCount = 1, string memberName = null,
            ErrorEstimationMethod method = ErrorEstimationMethod.None,
            int? completed = null, int? total = null, float? fraction = null)
        {
            OperationId = operationId; Phase = phase; Scope = scope;
            MemberPosition = memberPosition; MemberCount = memberCount; MemberName = memberName;
            Method = method; Completed = completed; Total = total; Fraction = fraction;
        }
    }

    public sealed class FittingStatusParts
    {
        public string Action { get; }
        public string Position { get; }
        public string Name { get; }
        public string Text => Action + Position + (Name == null ? Position.Length == 0 ? "…" : "" : ": " + Name);
        internal FittingStatusParts(string action, string position, string name)
        { Action = action; Position = position; Name = name; }
    }

    public sealed class FittingCompletion
    {
        public Guid OperationId { get; }
        public SolverInterface Solver { get; }
        public SolverConvergence Convergence { get; }
        public TimeSpan Elapsed { get; }
        public bool IsResultUpdate { get; }
        public FittingCompletion(Guid operationId, SolverInterface solver, SolverConvergence convergence,
            TimeSpan elapsed, bool isResultUpdate)
        { OperationId = operationId; Solver = solver; Convergence = convergence; Elapsed = elapsed; IsResultUpdate = isResultUpdate; }
    }

    public sealed class FittingCompletionMessage
    {
        public string Text { get; }
        public int DurationMilliseconds { get; }
        public FittingCompletionMessage(string text, int durationMilliseconds)
        { Text = text; DurationMilliseconds = durationMilliseconds; }
    }

    public static class FittingStatusPresentation
    {
        public static bool ShowExperimentNames { get; set; } = true;
        public const string BusyTooltip = "Unavailable while another calculation is running.";
        public const string UpdatingResult = "Updating analysis result…";
        public const string StoppingAnalysis = "Stopping analysis…";
        public static string Failure(Exception error, bool resultUpdate = false)
            => (resultUpdate ? "Result update failed: " : "Analysis failed: ") + error.Message;
        public static FittingStatusParts Parts(FittingProgress progress)
        {
            var action = progress.Phase == FittingPhase.Fitting ? "Fitting"
                : progress.Phase == FittingPhase.AssessingBinding ? "Assessing binding"
                : progress.Phase == FittingPhase.EstimatingUncertainty ? "Estimating uncertainty" : "Stopping analysis";
            if (progress.Phase == FittingPhase.Stopping) return new FittingStatusParts(action, "", null);
            if (progress.Scope == FittingScope.Global)
                return new FittingStatusParts(action + (progress.Phase == FittingPhase.Fitting
                    ? " " + progress.MemberCount + " experiments globally" : ""), "", null);
            var assessingAll = progress.Scope == FittingScope.Individual && progress.Phase == FittingPhase.AssessingBinding;
            var position = !assessingAll && progress.Scope == FittingScope.Individual
                ? " " + progress.MemberPosition + " of " + progress.MemberCount : "";
            var name = !assessingAll && ShowExperimentNames && !string.IsNullOrWhiteSpace(progress.MemberName)
                ? progress.MemberName : null;
            return new FittingStatusParts(action, position, name);
        }
        public static string Message(FittingProgress progress) => Parts(progress).Text;
        public static double IndicatorProgress(FittingProgress progress, bool stopping = false)
        {
            if (stopping || progress.Phase != FittingPhase.EstimatingUncertainty
                || !progress.Fraction.HasValue || progress.Fraction.Value < 0)
                return -0.5;
            return Math.Max(0, Math.Min(1, progress.Fraction.Value));
        }

        public static string Counter(FittingProgress progress)
        {
            if (progress.Phase != FittingPhase.EstimatingUncertainty || !progress.Completed.HasValue || !progress.Total.HasValue) return "";
            var method = progress.Method == ErrorEstimationMethod.BootstrapResiduals ? "Bootstrap"
                : progress.Method == ErrorEstimationMethod.LeaveOneOut ? "Leave-one-out" : "Profile searches";
            return method + " " + progress.Completed + "/" + progress.Total;
        }
        public static string Duration(TimeSpan duration)
        {
            var seconds = Math.Max(0, duration.TotalSeconds);
            if (Math.Round(seconds, 1, MidpointRounding.AwayFromZero) < 10)
                return seconds.ToString("0.0", CultureInfo.CurrentCulture) + " s";
            var rounded = Math.Round(seconds, 0, MidpointRounding.AwayFromZero);
            if (rounded < 60) return rounded.ToString("0", CultureInfo.CurrentCulture) + " s";
            return Math.Floor(rounded / 60).ToString("0", CultureInfo.CurrentCulture) + " min "
                + (rounded % 60).ToString("0", CultureInfo.CurrentCulture) + " s";
        }
    }
}
