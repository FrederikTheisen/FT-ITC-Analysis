using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Accord.Math.Optimization;
using System.Threading.Tasks;
using MathNet.Numerics.Optimization;

namespace AnalysisITC
{
    public class SolverConvergence
    {
        public SolverAlgorithm Algorithm { get; private set; }
        public SolverTermination Termination { get; private set; } = SolverTermination.Unknown;
        public ErrorEstimationOutcome ErrorEstimationOutcome { get; private set; } = ErrorEstimationOutcome.None;

        public int Iterations { get; private set; } = 0;
        public double Loss { get; private set; } = 0;

        public TimeSpan Time { get; private set; } = new(0);
        public TimeSpan ErrorEstimationTime { get; private set; } = new(0);

        public string FailureReason { get; private set; } = string.Empty;
        public string ErrorEstimationSummary { get; private set; } = string.Empty;
        public Exception RootCause { get; private set; } = null;

        public TimeSpan TotalTime => Time + ErrorEstimationTime;

        public string Message => GetDisplayMessage();

        public bool Success =>
            Termination == SolverTermination.Converged ||
            Termination == SolverTermination.SmallStep ||
            Termination == SolverTermination.SmallGradient ||
            Termination == SolverTermination.ReachedTarget;

        public bool Failed =>
            Termination == SolverTermination.Failed ||
            Termination == SolverTermination.InvalidValues;

        public bool Stopped =>
            Termination == SolverTermination.Cancelled;

        public bool MaxIterationsReached =>
            Termination == SolverTermination.IterationLimit ||
            Termination == SolverTermination.EvaluationLimit ||
            Termination == SolverTermination.TimeLimit;

        public bool IsUsableForErrorEstimation => !Failed && !Stopped;

        public bool HasErrorEstimationIssues =>
            ErrorEstimationOutcome == ErrorEstimationOutcome.PartialFailure ||
            ErrorEstimationOutcome == ErrorEstimationOutcome.CompleteFailure ||
            ErrorEstimationOutcome == ErrorEstimationOutcome.Cancelled;

        public void SetLoss(double loss) => Loss = loss;

        private SolverConvergence() { }

        public SolverConvergence(NelderMead solver, double loss)
        {
            Algorithm = SolverAlgorithm.NelderMead;
            Iterations = solver.Convergence.Evaluations;
            Time = DateTime.Now - solver.Convergence.StartTime;
            Loss = loss;

            ApplyTermination(TranslateAccord(solver.Status));
        }

        public SolverConvergence(NonlinearMinimizationResult result, TimeSpan time, double loss)
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt;
            Iterations = result.Iterations;
            Time = time;
            Loss = loss;

            ApplyTermination(TranslateMathNet(result.ReasonForExit));
        }

        private SolverConvergence(List<SolverConvergence> list)
        {
            Algorithm = list.First().Algorithm;
            Iterations = list.Sum(c => c.Iterations);
            Time = TimeSpan.FromTicks(list.Sum(c => c.Time.Ticks));
            ErrorEstimationTime = TimeSpan.FromTicks(list.Sum(c => c.ErrorEstimationTime.Ticks));
            Loss = list.Sum(c => c.Loss);
            ErrorEstimationOutcome = AggregateErrorEstimationOutcome(list);

            var term = AggregateTermination(list);

            ApplyTermination(term);
        }

        public static SolverConvergence FromMultiExperimentAnalysis(List<SolverConvergence> list)
        {
            return new SolverConvergence(list);
        }

        public void ApplyErrorEstimationResult(ErrorEstimationMethod method, int failures, int succeeded, TimeSpan time)
        {
            if (method == ErrorEstimationMethod.None)
            {
                ErrorEstimationOutcome = ErrorEstimationOutcome.None;
                ErrorEstimationSummary = "No error estimation";
                return;
            }

            ErrorEstimationTime = time;

            int total = failures + succeeded;

            if (total <= 0)
            {
                ErrorEstimationOutcome = ErrorEstimationOutcome.NotRun;
                ErrorEstimationSummary = $"{method} did not run";
                return;
            }

            if (failures == 0)
            {
                ErrorEstimationOutcome = ErrorEstimationOutcome.Completed;
                ErrorEstimationSummary = $"{method} completed successfully";
                return;
            }

            ErrorEstimationOutcome = succeeded > 0 ? ErrorEstimationOutcome.PartialFailure : ErrorEstimationOutcome.CompleteFailure;

            ErrorEstimationSummary = $"{method}: {succeeded}/{total} succeeded";
        }

        public SolverConvergence Copy()
        {
            return new()
            {
                Algorithm = this.Algorithm,
                Termination = this.Termination,
                ErrorEstimationOutcome = this.ErrorEstimationOutcome,

                Iterations = this.Iterations,
                Loss = this.Loss,

                Time = this.Time,
                ErrorEstimationTime = this.ErrorEstimationTime,

                FailureReason = this.FailureReason,
                ErrorEstimationSummary = this.ErrorEstimationSummary,
                RootCause = this.RootCause,
            };
        }

        public SolverConvergenceSnapshot ToSnapshot()
        {
            return new SolverConvergenceSnapshot()
            {
                SchemaVersion = SolverConvergenceSnapshot.CurrentSchemaVersion,
                Algorithm = Algorithm,
                Termination = Termination,
                ErrorEstimationOutcome = ErrorEstimationOutcome,
                Iterations = Iterations,
                Loss = Loss,
                TimeSeconds = Time.TotalSeconds,
                ErrorEstimationTimeSeconds = ErrorEstimationTime.TotalSeconds,
                FailureReason = FailureReason ?? string.Empty,
                ErrorEstimationSummary = ErrorEstimationSummary ?? string.Empty,
            };
        }

        public static SolverConvergence FromSnapshot(SolverConvergenceSnapshot snapshot)
        {
            if (snapshot == null) return null;

            return new SolverConvergence()
            {
                Algorithm = snapshot.Algorithm,
                Termination = snapshot.Termination,
                ErrorEstimationOutcome = snapshot.ErrorEstimationOutcome,
                Iterations = snapshot.Iterations,
                Loss = snapshot.Loss,
                Time = TimeSpan.FromSeconds(snapshot.TimeSeconds),
                ErrorEstimationTime = TimeSpan.FromSeconds(snapshot.ErrorEstimationTimeSeconds),
                FailureReason = snapshot.FailureReason ?? string.Empty,
                ErrorEstimationSummary = snapshot.ErrorEstimationSummary ?? string.Empty,
            };
        }

        public static SolverConvergence ReportFailed(DateTime starttime)
        {
            var conv = new SolverConvergence()
            {
                Time = DateTime.Now - starttime,
                Termination = SolverTermination.Failed,
            };

            return conv;
        }

        public static SolverConvergence ReportStopped(DateTime starttime)
        {
            var conv = new SolverConvergence()
            {
                Time = DateTime.Now - starttime,
                Termination = SolverTermination.Cancelled,
            };

            return conv;
        }

        public static SolverConvergence FromSaveLegacy(int iter, double loss, TimeSpan time, TimeSpan btime, SolverAlgorithm algorithm, string msg, bool failed)
        {
            return new SolverConvergence()
            {
                Iterations = iter,
                Loss = loss,
                Time = time,
                ErrorEstimationTime = btime,
                Algorithm = algorithm,
                FailureReason = msg,
            };
        }

        public static SolverConvergence FromException(Exception ex, DateTime starttime)
        {
            var conv = new SolverConvergence()
            {
                Time = DateTime.Now - starttime,
                RootCause = ex,
            };

            if (ex is AggregateException agg)
            {
                var flat = agg.Flatten().InnerExceptions;

                var cancel = flat.FirstOrDefault(ix =>
                    ix is OptimizerStopException ||
                    ix is OperationCanceledException ||
                    ix is TaskCanceledException);

                if (cancel != null)
                {
                    conv.ApplyTermination(
                        SolverTermination.Cancelled,
                        cancel.Message,
                        cancel);

                    return conv;
                }

                var cause = flat.FirstOrDefault() ?? ex;

                conv.ApplyTermination(
                    cause is OverflowException || cause is ArithmeticException
                        ? SolverTermination.InvalidValues
                        : SolverTermination.Failed,
                    cause.Message,
                    cause);

                return conv;
            }

            if (ex is OptimizerStopException ||
                ex is OperationCanceledException ||
                ex is TaskCanceledException)
            {
                conv.ApplyTermination(
                    SolverTermination.Cancelled,
                    ex.Message,
                    ex);

                return conv;
            }

            conv.ApplyTermination(
                ex is OverflowException || ex is ArithmeticException
                    ? SolverTermination.InvalidValues
                    : SolverTermination.Failed,
                ex.Message,
                ex);

            return conv;
        }

        private void ApplyTermination(SolverTermination termination, string failureReason = "", Exception rootCause = null)
        {
            Termination = termination;
            RootCause = rootCause;

            FailureReason =
                termination == SolverTermination.Failed ||
                termination == SolverTermination.InvalidValues ||
                termination == SolverTermination.Cancelled
                    ? (failureReason ?? string.Empty)
                    : string.Empty;
        }

        private string GetPrimaryTerminationMessage()
        {
            return Termination switch
            {
                SolverTermination.Converged => "Analysis Completed Successfully",
                SolverTermination.SmallStep => "Analysis Completed Successfully",
                SolverTermination.SmallGradient => "Analysis Completed Successfully",
                SolverTermination.ReachedTarget => "Analysis Completed Successfully",

                SolverTermination.IterationLimit => "Analysis Stopped: iteration limit reached",
                SolverTermination.EvaluationLimit => "Analysis Stopped: evaluation limit reached",
                SolverTermination.TimeLimit => "Analysis Stopped: time limit reached",
                SolverTermination.Cancelled => "Analysis Stopped by user",

                SolverTermination.InvalidValues => "Analysis Failed: invalid model values",
                SolverTermination.Failed => string.IsNullOrWhiteSpace(FailureReason)
                    ? "Analysis Failed"
                    : "Analysis Failed: " + FailureReason.Trim(),

                _ => "Analysis Stopped: unknown reason"
            };
        }

        private string GetDisplayMessage()
        {
            var primary = GetPrimaryTerminationMessage();

            if (!Success)
                return primary;

            return ErrorEstimationOutcome switch
            {
                ErrorEstimationOutcome.PartialFailure =>
                    string.IsNullOrWhiteSpace(ErrorEstimationSummary)
                        ? primary + "; error estimation partially failed"
                        : primary + "; " + ErrorEstimationSummary,

                ErrorEstimationOutcome.CompleteFailure =>
                    string.IsNullOrWhiteSpace(ErrorEstimationSummary)
                        ? primary + "; error estimation failed"
                        : primary + "; " + ErrorEstimationSummary,

                ErrorEstimationOutcome.Cancelled =>
                    string.IsNullOrWhiteSpace(ErrorEstimationSummary)
                        ? primary + "; error estimation cancelled"
                        : primary + "; " + ErrorEstimationSummary,

                _ => primary
            };
        }

        private static SolverTermination TranslateMathNet(ExitCondition code)
        {
            return code switch
            {
                ExitCondition.Converged => SolverTermination.Converged,
                ExitCondition.RelativePoints => SolverTermination.SmallStep,
                ExitCondition.RelativeGradient => SolverTermination.SmallGradient,
                ExitCondition.ExceedIterations => SolverTermination.IterationLimit,
                ExitCondition.ManuallyStopped => SolverTermination.Cancelled,
                ExitCondition.InvalidValues => SolverTermination.InvalidValues,
                _ => SolverTermination.Failed,
            };
        }

        private static SolverTermination TranslateAccord(NelderMeadStatus code)
        {
            return code switch
            {
                NelderMeadStatus.Success => SolverTermination.Converged,
                NelderMeadStatus.FunctionToleranceReached => SolverTermination.Converged,
                NelderMeadStatus.SolutionToleranceReached => SolverTermination.SmallStep,
                NelderMeadStatus.MinimumAllowedValueReached => SolverTermination.ReachedTarget,
                NelderMeadStatus.MaximumEvaluationsReached => SolverTermination.EvaluationLimit,
                NelderMeadStatus.MaximumTimeReached => SolverTermination.TimeLimit,
                NelderMeadStatus.ForcedStop => SolverTermination.Cancelled,
                NelderMeadStatus.Failure => SolverTermination.Failed,
                _ => SolverTermination.Failed,
            };
        }

        private static SolverTermination AggregateTermination(IEnumerable<SolverConvergence> list)
        {
            var terms = list.Select(c => c.Termination).ToList();

            if (terms.Any(t => t == SolverTermination.Cancelled))
                return SolverTermination.Cancelled;

            if (terms.Any(t =>
                t == SolverTermination.Failed ||
                t == SolverTermination.InvalidValues ||
                t == SolverTermination.Unknown))
                return SolverTermination.Failed;

            if (terms.Any(t => t == SolverTermination.TimeLimit))
                return SolverTermination.TimeLimit;

            if (terms.Any(t => t == SolverTermination.EvaluationLimit))
                return SolverTermination.EvaluationLimit;

            if (terms.Any(t => t == SolverTermination.IterationLimit))
                return SolverTermination.IterationLimit;

            if (terms.Any(t => t == SolverTermination.SmallGradient))
                return SolverTermination.SmallGradient;

            if (terms.Any(t => t == SolverTermination.SmallStep))
                return SolverTermination.SmallStep;

            if (terms.Any(t => t == SolverTermination.ReachedTarget))
                return SolverTermination.ReachedTarget;

            return SolverTermination.Converged;
        }

        private static ErrorEstimationOutcome AggregateErrorEstimationOutcome(IEnumerable<SolverConvergence> list)
        {
            var outcomes = list.Select(c => c.ErrorEstimationOutcome).ToList();

            if (outcomes.All(o => o == ErrorEstimationOutcome.None))
                return ErrorEstimationOutcome.None;

            if (outcomes.All(o => o == ErrorEstimationOutcome.None || o == ErrorEstimationOutcome.NotRun))
                return ErrorEstimationOutcome.NotRun;

            if (outcomes.All(o =>
                o == ErrorEstimationOutcome.None ||
                o == ErrorEstimationOutcome.NotRun ||
                o == ErrorEstimationOutcome.Completed))
                return ErrorEstimationOutcome.Completed;

            if (outcomes.All(o => o == ErrorEstimationOutcome.Cancelled))
                return ErrorEstimationOutcome.Cancelled;

            if (outcomes.All(o => o == ErrorEstimationOutcome.CompleteFailure))
                return ErrorEstimationOutcome.CompleteFailure;

            return ErrorEstimationOutcome.PartialFailure;
        }
    }

    public sealed class SolverConvergenceSnapshot
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public SolverAlgorithm Algorithm { get; set; }
        public SolverTermination Termination { get; set; } = SolverTermination.Unknown;
        public ErrorEstimationOutcome ErrorEstimationOutcome { get; set; } = ErrorEstimationOutcome.None;
        public int Iterations { get; set; }
        public double Loss { get; set; }
        public double TimeSeconds { get; set; }
        public double ErrorEstimationTimeSeconds { get; set; }
        public string FailureReason { get; set; } = string.Empty;
        public string ErrorEstimationSummary { get; set; } = string.Empty;
    }

    public class TerminationFlag
    {
        public event EventHandler WasRaised;

        bool FlagIsRaised { get; set; } = false;

        public bool Up => FlagIsRaised;
        public bool Down => !FlagIsRaised;

        public TerminationFlag()
        {
            FlagIsRaised = false;
        }

        public void Raise()
        {
            FlagIsRaised = true;

            WasRaised?.Invoke(this, null);
        }

        public void Lower()
        {
            FlagIsRaised = false;
        }
    }

    public class SolverUpdate
    {
        public string Message { get; set; } = "";
        public float Progress { get; set; } = -1;
        public static int Step { get; set; } = 0;
        public static int TotalSteps { get; set; } = 1;
        public int Time { get; set; } = 0;

        public string ProgressString => Step.ToString() + "/" + TotalSteps.ToString();

        private SolverUpdate()
        {

        }

        public SolverUpdate(int step, int totalsteps)
        {
            Step = step;
            TotalSteps = totalsteps;
        }

        public void SendToStatusBar()
        {
            StatusBarManager.SetProgress(Progress);
            if (Message != "") StatusBarManager.SetStatus(Message, Time, priority: 1);
            if (TotalSteps > 0) StatusBarManager.SetSecondaryStatus(ProgressString, Time);
        }

        public static SolverUpdate BackgroundBootstrapUpdate(int counter, int bootiterations)
        {
            float stepsize = 1.0f / TotalSteps;

            return new SolverUpdate()
            {
                Progress = stepsize * (float)counter / bootiterations + (float)Step / TotalSteps,
            };
        }
    }

    [Description]
    public enum VariableConstraint
    {
        [Description("None")]
        None,
        [Description("Temperature dependent")]
        TemperatureDependent,
        [Description("Same for all")]
        SameForAll
    }

    public enum ErrorEstimationMethod
    {
        None,
        BootstrapResiduals,
        LeaveOneOut
    }

    public class SolverAlgorithmAttribute : Attribute
    {
        public string Name { get; private set; }
        public string ShortName { get; private set; }

        public SolverAlgorithmAttribute(string name, string shortname)
        {
            Name = name;
            ShortName = shortname;
        }
    }

    public enum SolverAlgorithm
    {
        [SolverAlgorithmAttribute("Nelder-Mead [SIMPLEX]", "SIMPLEX")]
        NelderMead,
        [SolverAlgorithmAttribute("Levenberg-Marquardt", "LM")]
        LevenbergMarquardt
    }

    public enum SolverTermination
    {
        Unknown = 0,

        // Successful / acceptable termination
        Converged,
        SmallStep,
        SmallGradient,
        ReachedTarget,

        // Incomplete termination
        IterationLimit,
        EvaluationLimit,
        TimeLimit,
        Cancelled,

        // Bad termination
        InvalidValues,
        Failed
    }

    public enum ErrorEstimationOutcome
    {
        None = 0,
        NotRun,
        Completed,
        PartialFailure,
        CompleteFailure,
        Cancelled
    }
}
