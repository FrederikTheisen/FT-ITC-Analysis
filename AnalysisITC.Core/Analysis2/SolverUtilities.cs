using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Accord.Math.Optimization;
using System.Threading.Tasks;
using MathNet.Numerics.Optimization;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Analysis
{
    public enum ParameterBoundaryScope
    {
        Local,
        Shared,
        Global = Shared,
    }

    public enum ParameterBoundarySide
    {
        Lower,
        Upper,
    }

    /// <summary>
    /// A transient description of a free parameter whose accepted final value is
    /// effectively on one of its active bounds. Boundary contacts intentionally
    /// are not part of the persisted convergence snapshot.
    /// </summary>
    public sealed class ParameterBoundaryContact
    {
        public ParameterType Parameter { get; }
        public ParameterType ParameterType => Parameter;
        public ParameterType ParameterKey => Parameter;
        public string ParameterIdentity { get; }
        public string DisplayName => Parameter.GetProperties().Name;
        public ParameterBoundaryScope Scope { get; }
        public bool IsShared => Scope == ParameterBoundaryScope.Shared;
        public bool IsGlobal => IsShared;
        public string ExperimentIdentity { get; }
        public string ExperimentId => ExperimentIdentity;
        public string ExperimentID => ExperimentIdentity;
        public string ExperimentUniqueId => ExperimentIdentity;
        public string ExperimentName { get; }
        public ParameterBoundarySide Side { get; }
        public bool IsLower => Side == ParameterBoundarySide.Lower;
        public bool IsUpper => Side == ParameterBoundarySide.Upper;
        public double FinalValue { get; }
        public double FinalParameterValue => FinalValue;
        public double Value => FinalValue;
        public double BoundValue { get; }
        public double Bound => BoundValue;
        public double LowerBound => Side == ParameterBoundarySide.Lower ? BoundValue : double.NaN;
        public double UpperBound => Side == ParameterBoundarySide.Upper ? BoundValue : double.NaN;

        public ParameterBoundaryContact(
            ParameterType parameter,
            ParameterBoundaryScope scope,
            string experimentIdentity,
            string experimentName,
            ParameterBoundarySide side,
            double finalValue,
            double boundValue)
        {
            Parameter = parameter;
            ParameterIdentity = parameter.ToString();
            Scope = scope;
            ExperimentIdentity = experimentIdentity;
            ExperimentName = experimentName;
            Side = side;
            FinalValue = finalValue;
            BoundValue = boundValue;
        }

        public ParameterBoundaryContact Copy() => new ParameterBoundaryContact(
            Parameter,
            Scope,
            ExperimentIdentity,
            ExperimentName,
            Side,
            FinalValue,
            BoundValue);
    }

    /// <summary>
    /// Shared presentation for analysis warnings. The original parameter-boundary
    /// messages remain here alongside uncertainty-refit admission warnings so all
    /// result presenters use the same warning vocabulary.
    /// </summary>
    public static class ParameterBoundaryWarningFormatter
    {
        public const string BestFitMessage = "Best fit reached a parameter boundary.";
        public const string BootstrapFitMessage = "One or more bootstrap fits reached a parameter boundary.";
        public const string LeaveOneOutFitMessage = "One or more leave-one-out fits reached a parameter boundary.";
        public const string BootstrapLimitMessage = "One or more bootstrap refits reached an optimizer limit and were excluded.";
        public const string LeaveOneOutLimitMessage = "One or more leave-one-out refits reached an optimizer limit and were excluded.";

        public static string ErrorEstimationMessage(ErrorEstimationMethod method) =>
            method == ErrorEstimationMethod.LeaveOneOut
                ? LeaveOneOutFitMessage
                : BootstrapFitMessage;

        public static string ErrorEstimationLimitMessage(ErrorEstimationMethod method) =>
            method == ErrorEstimationMethod.LeaveOneOut
                ? LeaveOneOutLimitMessage
                : BootstrapLimitMessage;

        public static IReadOnlyList<string> MessagesFor(
            SolutionInterface solution,
            ErrorEstimationMethod method)
        {
            var messages = new List<string>();
            if (solution?.ParameterBoundaryHit == true)
                messages.Add(BestFitMessage);
            if (solution?.BootstrapParameterBoundaryHit == true)
                messages.Add(ErrorEstimationMessage(method));
            if (solution?.Convergence?.ErrorEstimationLimitTerminations > 0)
                messages.Add(ErrorEstimationLimitMessage(method));
            return messages;
        }

        public static string Format(IEnumerable<ParameterBoundaryContact> contacts)
        {
            var names = (contacts ?? Enumerable.Empty<ParameterBoundaryContact>())
                .Where(contact => contact != null)
                .Select(contact =>
                {
                    var side = contact.IsLower ? "lower" : "upper";
                    return string.IsNullOrWhiteSpace(contact.ExperimentName)
                        ? $"{contact.DisplayName} ({side})"
                        : $"{contact.DisplayName} ({side}, {contact.ExperimentName})";
                })
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (names.Count == 0) return string.Empty;

            const string prefix = "Warning: parameter bound reached";
            if (names.Count <= 3)
                return $"{prefix}: {string.Join(", ", names)}";

            return $"{prefix}: {string.Join(", ", names.Take(3))}, and {names.Count - 3} more";
        }
    }

    /// <summary>
    /// Independent bound-contact detection used after a solver has applied its
    /// accepted parameter vector. It only considers free parameters and the
    /// currently active finite limits.
    /// </summary>
    public static class ParameterBoundaryDetector
    {
        static bool IsAtBound(double value, double bound, double tolerance)
        {
            return FWEMath.IsFinite(value) && FWEMath.IsFinite(bound) && Math.Abs(value - bound) <= tolerance;
        }

        static void AddContact(
            List<ParameterBoundaryContact> contacts,
            Parameter parameter,
            ParameterBoundaryScope scope,
            ExperimentData data)
        {
            if (parameter == null || !parameter.IsFitted || parameter.Limits == null || parameter.Limits.Length < 2)
                return;

            var lower = parameter.Limits[0];
            var upper = parameter.Limits[1];
            if (!FWEMath.IsFinite(lower) || !FWEMath.IsFinite(upper) || upper < lower)
                return;

            var tolerance = Math.Max(1e-10, 1e-6 * Math.Abs(upper - lower));
            var value = parameter.Value;
            var experimentId = scope == ParameterBoundaryScope.Local ? data?.UniqueID : null;
            var experimentName = scope == ParameterBoundaryScope.Local ? data?.Name ?? data?.FileName : null;

            if (IsAtBound(value, lower, tolerance))
                contacts.Add(new ParameterBoundaryContact(
                    parameter.Key, scope, experimentId, experimentName,
                    ParameterBoundarySide.Lower, value, lower));

            if (IsAtBound(value, upper, tolerance))
                contacts.Add(new ParameterBoundaryContact(
                    parameter.Key, scope, experimentId, experimentName,
                    ParameterBoundarySide.Upper, value, upper));
        }

        public static IReadOnlyList<ParameterBoundaryContact> Detect(Model model)
        {
            return Detect(model, ParameterBoundaryScope.Local);
        }

        public static IReadOnlyList<ParameterBoundaryContact> Detect(Model model, ParameterBoundaryScope scope)
        {
            var contacts = new List<ParameterBoundaryContact>();
            foreach (var parameter in model?.Parameters?.Table?.Values ?? Enumerable.Empty<Parameter>())
                AddContact(contacts, parameter, scope, model.Data);
            return contacts;
        }

        public static IReadOnlyList<ParameterBoundaryContact> Detect(GlobalModel model)
        {
            var contacts = new List<ParameterBoundaryContact>();
            foreach (var parameter in model?.Parameters?.GlobalTable?.Values ?? Enumerable.Empty<Parameter>())
                AddContact(contacts, parameter, ParameterBoundaryScope.Shared, null);

            var models = model?.Models ?? new List<Model>();
            var parameterSets = model?.Parameters?.IndividualModelParameterList;
            if (parameterSets == null) return contacts;

            for (var i = 0; i < parameterSets.Count; i++)
            {
                var data = i < models.Count ? models[i]?.Data : null;
                foreach (var parameter in parameterSets[i]?.Table?.Values ?? Enumerable.Empty<Parameter>())
                    AddContact(contacts, parameter, ParameterBoundaryScope.Local, data);
            }

            return contacts;
        }
    }

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
        public int ErrorEstimationLimitTerminations { get; private set; }

        /// <summary>
        /// Boundary contacts from this fit. This is deliberately excluded from
        /// SolverConvergenceSnapshot and is therefore not persisted as contact detail.
        /// </summary>
        public IReadOnlyList<ParameterBoundaryContact> ParameterBoundaryContacts { get; private set; } = Array.Empty<ParameterBoundaryContact>();
        public IReadOnlyList<ParameterBoundaryContact> BoundaryContacts => ParameterBoundaryContacts;

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

        /// <summary>
        /// An uncertainty refit is usable only when the optimizer reports an
        /// acceptable convergence state. Limit-terminated best-so-far points are
        /// intentionally excluded from uncertainty distributions.
        /// </summary>
        public bool IsUsableForErrorEstimation => Success;

        /// <summary>
        /// Indicates that the primary fit may proceed to error estimation. A
        /// limit-terminated primary fit is still allowed to report its result,
        /// but its individual uncertainty refits are subject to the stricter
        /// IsUsableForErrorEstimation gate above.
        /// </summary>
        public bool CanRunErrorEstimation => !Failed && !Stopped;

        public bool HasErrorEstimationLimitWarnings => ErrorEstimationLimitTerminations > 0;

        public bool HasErrorEstimationIssues =>
            ErrorEstimationOutcome == ErrorEstimationOutcome.PartialFailure ||
            ErrorEstimationOutcome == ErrorEstimationOutcome.CompleteFailure ||
            ErrorEstimationOutcome == ErrorEstimationOutcome.Cancelled;

        public void SetLoss(double loss) => Loss = loss;

        internal void MarkInvalidValues(string reason)
        {
            ApplyTermination(SolverTermination.InvalidValues, reason);
        }

        public void SetParameterBoundaryContacts(IEnumerable<ParameterBoundaryContact> contacts)
        {
            ParameterBoundaryContacts = (contacts ?? Enumerable.Empty<ParameterBoundaryContact>())
                .Where(contact => contact != null)
                .Select(contact => contact.Copy())
                .ToArray();
        }

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
            ErrorEstimationLimitTerminations = list.Sum(c => c.ErrorEstimationLimitTerminations);

            SetParameterBoundaryContacts(list.SelectMany(c => c.ParameterBoundaryContacts));

            var term = AggregateTermination(list);

            ApplyTermination(term);
        }

        public static SolverConvergence FromMultiExperimentAnalysis(List<SolverConvergence> list)
        {
            return new SolverConvergence(list);
        }

        public void ApplyErrorEstimationResult(
            ErrorEstimationMethod method,
            int failures,
            int succeeded,
            TimeSpan time,
            bool cancelled = false,
            int requested = 0,
            int limitTerminated = 0)
        {
            ErrorEstimationLimitTerminations = 0;

            if (method == ErrorEstimationMethod.None)
            {
                ErrorEstimationOutcome = ErrorEstimationOutcome.None;
                ErrorEstimationSummary = "No error estimation";
                return;
            }

            ErrorEstimationTime = time;

            int total = failures + succeeded;
            ErrorEstimationLimitTerminations = Math.Min(
                Math.Max(0, failures),
                Math.Max(0, limitTerminated));
            var limitSummary = ErrorEstimationLimitTerminations > 0
                ? $", limit-terminated={ErrorEstimationLimitTerminations}"
                : string.Empty;

            if (cancelled)
            {
                ErrorEstimationOutcome = ErrorEstimationOutcome.Cancelled;
                var requestedSummary = requested > 0 ? $", requested={requested}" : string.Empty;
                ErrorEstimationSummary = $"{method}: cancelled; succeeded={succeeded}, failed={failures}, attempted={total}{requestedSummary}{limitSummary}";
                return;
            }

            if (total <= 0)
            {
                ErrorEstimationOutcome = ErrorEstimationOutcome.NotRun;
                ErrorEstimationSummary = $"{method} did not run";
                return;
            }

            ErrorEstimationOutcome = failures == 0
                ? ErrorEstimationOutcome.Completed
                : succeeded > 0
                    ? ErrorEstimationOutcome.PartialFailure
                    : ErrorEstimationOutcome.CompleteFailure;

            ErrorEstimationSummary = $"{method}: succeeded={succeeded}, failed={failures}, total={total}{limitSummary}";
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
                ErrorEstimationLimitTerminations = this.ErrorEstimationLimitTerminations,
                ParameterBoundaryContacts = this.ParameterBoundaryContacts
                    .Select(contact => contact.Copy())
                    .ToArray(),
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
                ErrorEstimationLimitTerminations = ErrorEstimationLimitTerminations,
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
                ErrorEstimationLimitTerminations = Math.Max(0, snapshot.ErrorEstimationLimitTerminations),
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
        public int ErrorEstimationLimitTerminations { get; set; }
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
