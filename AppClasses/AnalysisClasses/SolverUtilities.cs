using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Accord.Math.Optimization;
using AnalysisITC.AppClasses.Analysis2;
using static alglib;
using System.Threading.Tasks;

namespace AnalysisITC
{
    public class SolverConvergence
    {
        public int Iterations { get; private set; } = 0;
        /// <summary>
        /// A short, user friendly description of the solver outcome. This string is normalized
        /// by <see cref="Normalize"/> to map solver-specific messages onto a unified set of
        /// completion statuses (e.g. "Completed", "Stopped by user", "Reached iteration limit",
        /// "Completed with warnings", "Failed").
        /// </summary>
        public string Message { get; set; } = string.Empty;
        public TimeSpan Time { get; set; } = new(0);
        public TimeSpan BootstrapTime { get; private set; } = new(0);
        public double Loss { get; private set; } = 0;
        /// <summary>
        /// Indicates that the solver hit an unrecoverable error. Note that a fit stopped by the
        /// user or terminated due to reaching the maximum number of iterations is not considered
        /// a failure. See <see cref="Stopped"/> and <see cref="MaxIterationsReached"/> for those
        /// cases.
        /// </summary>
        public bool Failed { get; set; } = false;
        /// <summary>
        /// Indicates that the solver was cancelled by the user or externally. When true the fit
        /// was aborted before completion; this flag always supersedes <see cref="Failed"/> and
        /// <see cref="MaxIterationsReached"/>.
        /// </summary>
        public bool Stopped { get; set; } = false;
        /// <summary>
        /// Indicates that the solver reached its iteration or evaluation limit before
        /// converging. This is not considered a fatal failure but should be surfaced to the
        /// user as "Reached iteration limit".
        /// </summary>
        public bool MaxIterationsReached { get; set; } = false;
        /// <summary>
        /// Indicates that the fit completed successfully but some warnings occurred (e.g. during
        /// bootstrapping some replicates failed). When true the user facing message will be
        /// "Completed with warnings".
        /// </summary>
        public bool Warning { get; set; } = false;
        /// <summary>
        /// Stores the detailed solver or exception message prior to normalization. This is used
        /// when generating the user friendly <see cref="Message"/> string. It may include the
        /// description of the underlying termination code or an exception message. Do not show
        /// this value directly to users.
        /// </summary>
        public string DetailedMessage { get; set; } = string.Empty;
        /// <summary>
        /// If the fit ended due to an exception then this property holds the root cause for
        /// diagnostic logging. It is never shown directly to the user but may be passed to
        /// <see cref="AppEventHandler.DisplayHandledException"/> for logging and alerting.
        /// </summary>
        public Exception RootCause { get; set; } = null;
        public SolverAlgorithm Algorithm { get; private set; }

        public bool Success => (!Failed && !Stopped);

        public void SetBootstrapTime(TimeSpan time) => BootstrapTime = time;
        public void SetLoss(double loss) => Loss = loss;

        private SolverConvergence() { }

        public SolverConvergence(SolverConvergence conv)
        {
            Algorithm = conv.Algorithm;
            Iterations = conv.Iterations;
            Message = conv.Message;
            Time = conv.Time;
            Loss = conv.Loss;

            Failed = conv.Failed;
            Stopped = conv.Stopped;
            MaxIterationsReached = conv.MaxIterationsReached;
            Warning = conv.Warning;
            DetailedMessage = conv.DetailedMessage;
            RootCause = conv.RootCause;
        }

        public SolverConvergence(NelderMead solver, double loss)
        {
            Algorithm = SolverAlgorithm.NelderMead;
            Iterations = solver.Convergence.Evaluations;
            // Capture the raw status string as the detailed message. This will be
            // normalized later by Normalize().
            DetailedMessage = solver.Status != null ? solver.Status.ToString() : string.Empty;
            Message = DetailedMessage;
            Time = DateTime.Now - solver.Convergence.StartTime;
            Loss = solver.Value;
            // Accord returns a Status that may indicate failure. Flag this for now; it will
            // be normalised later. Typical statuses include Success, Failure, MaximumIterations,
            // Stopped, etc.
            Failed = solver.Status != null && solver.Status.Equals(NelderMeadStatus.Failure);
        }

        public SolverConvergence(minlmstate state, minlmreport rep, TimeSpan time, double loss)
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt;
            Iterations = rep.iterationscount;
            // Store the description of the termination type as the detailed message. See
            // AlgLibTerminationCode for descriptions. This will be normalised later.
            DetailedMessage = ((AlgLibTerminationCode)rep.terminationtype).GetEnumDescription();
            Message = DetailedMessage;
            Time = time;
            Loss = loss;

            //Failed = rep.terminationtype > 2;
        }

        //public SolverConvergence(MathNet.Numerics.Optimization.NonlinearMinimizationResult result, TimeSpan time, SolverAlgorithm algorithm)
        //{
        //    Algorithm = algorithm;
        //    Iterations = result.Iterations;
        //    Message = result.ReasonForExit.ToString();
        //    Time = time;
        //    Loss = result.ModelInfoAtMinimum.Value;

        //    Failed = result.ReasonForExit != MathNet.Numerics.Optimization.ExitCondition.Converged;
        //}

        public SolverConvergence(List<SolverConvergence> list)
        {
            Algorithm = list.First().Algorithm;
            Iterations = list.First().Iterations;
            Message = list.First().Message;
            Time = TimeSpan.FromTicks(list.Sum(sc => sc.Time.Ticks));
            Loss = list.Sum(sc => sc.Loss);

            Failed = list.Any(con => con.Failed);
        }

        public static SolverConvergence ReportFailed(DateTime starttime)
        {
            var conv = new SolverConvergence()
            {
                Time = DateTime.Now - starttime,
                Failed = true,
                DetailedMessage = "Fitting failed",
                Message = "Failed",
            };

            return conv;
        }

        public static SolverConvergence ReportStopped(DateTime starttime)
        {
            var conv = new SolverConvergence()
            {
                Time = DateTime.Now - starttime,
                Stopped = true,
                Failed = false,
                DetailedMessage = "The optimization was stopped by the user",
                Message = "Stopped by user",
            };

            return conv;
        }

        public static SolverConvergence FromSave(int iter, double loss, TimeSpan time, TimeSpan btime, SolverAlgorithm algorithm, string msg, bool failed)
        {
            return new SolverConvergence()
            {
                Iterations = iter,
                Loss = loss,
                Time = time,
                BootstrapTime = btime,
                Algorithm = algorithm,
                Message = msg,
                DetailedMessage = msg,
                Failed = failed,
            };
        }

        /// <summary>
        /// Builds a <see cref="SolverConvergence"/> from an exception and records the root cause.
        /// The returned convergence represents either a failure or a user cancellation, and
        /// contains a preliminary message that will be normalised by <see cref="Normalize"/>.
        /// </summary>
        /// <param name="ex">The exception thrown during analysis.</param>
        /// <param name="starttime">The time when the analysis started.</param>
        public static SolverConvergence FromException(Exception ex, DateTime starttime)
        {
            var conv = new SolverConvergence()
            {
                Time = DateTime.Now - starttime,
                RootCause = ex,
                Failed = false,
                Stopped = false,
                MaxIterationsReached = false,
                Warning = false,
            };

            if (ex is AggregateException agg)
            {
                var flat = agg.Flatten().InnerExceptions;
                // Look for any inner exception that represents a user cancellation. If found,
                // classify this as a stopped fit; otherwise treat as a failure and use the
                // first inner exception as the root cause.
                var cancel = flat.FirstOrDefault(ix => ix is OptimizerStopException || ix is OperationCanceledException || ix is TaskCanceledException);
                if (cancel != null)
                {
                    conv.Stopped = true;
                    conv.Failed = false;
                    conv.DetailedMessage = cancel.Message;
                    conv.Message = cancel.Message;
                    conv.RootCause = cancel;
                }
                else
                {
                    conv.Failed = true;
                    // Choose the first inner exception as the root cause for logging
                    var cause = flat.FirstOrDefault();
                    if (cause != null)
                    {
                        conv.DetailedMessage = cause.Message;
                        conv.Message = cause.Message;
                        conv.RootCause = cause;
                    }
                    else
                    {
                        conv.DetailedMessage = ex.Message;
                        conv.Message = ex.Message;
                    }
                }
            }
            else if (ex is OptimizerStopException || ex is OperationCanceledException || ex is TaskCanceledException)
            {
                conv.Stopped = true;
                conv.Failed = false;
                conv.DetailedMessage = ex.Message;
                conv.Message = ex.Message;
            }
            else
            {
                conv.Failed = true;
                conv.DetailedMessage = ex.Message;
                conv.Message = ex.Message;
            }

            return conv;
        }

        /// <summary>
        /// Examines the current convergence result and normalises its status and message to a
        /// unified set of completion outcomes. After calling this method the <see cref="Message"/>
        /// property will contain one of:
        /// "Completed", "Completed with warnings", "Stopped by user",
        /// "Reached iteration limit", or a failure message. It will also adjust the
        /// <see cref="Failed"/>, <see cref="Stopped"/>, <see cref="MaxIterationsReached"/>, and
        /// <see cref="Warning"/> flags as necessary based on the contents of <see
        /// cref="DetailedMessage"/>.
        /// </summary>
        public void Normalize()
        {
            // If the fit was already explicitly marked as stopped or reaching the iteration limit
            // by external logic then honour those flags. Otherwise attempt to infer these
            // conditions from the detailed message.
            var msg = (DetailedMessage ?? Message ?? string.Empty).ToLowerInvariant();

            // Infer stop and iteration-limit conditions from the detailed message when not
            // already set. This handles cases where underlying libraries encode the result in
            // text strings (e.g. "Terminated by user", "Optimizer iteration limit").
            if (!Stopped && !MaxIterationsReached)
            {
                // User-initiated cancellation
                if (msg.Contains("term") && msg.Contains("user") || msg.Contains("stopped") || msg.Contains("cancel"))
                {
                    Stopped = true;
                    Failed = false;
                }
                // Maximum iterations or evaluations reached
                else if (msg.Contains("max") && msg.Contains("it"))
                {
                    MaxIterationsReached = true;
                    Failed = false;
                }
                else if (msg.Contains("iteration") && msg.Contains("limit"))
                {
                    MaxIterationsReached = true;
                    Failed = false;
                }
                else if (msg.Contains("maximum") && msg.Contains("evalu"))
                {
                    MaxIterationsReached = true;
                    Failed = false;
                }
            }

            // Determine the normalised user message based on flags.
            if (Stopped)
            {
                Message = "Stopped by user";
                Failed = false;
                MaxIterationsReached = false;
                return;
            }

            if (MaxIterationsReached)
            {
                Message = "Reached iteration limit";
                Failed = false;
                return;
            }

            if (Failed)
            {
                // Provide a concise failure message.
                if (!string.IsNullOrEmpty(DetailedMessage))
                {
                    Message = "Failed: " + DetailedMessage.Trim();
                }
                else
                {
                    Message = "Failed";
                }
                return;
            }

            // At this point the fit is not failed or stopped or limited. If warnings have
            // occurred (e.g. bootstrap partial failures), surface them.
            if (Warning)
            {
                Message = "Completed with warnings";
                return;
            }

            // Otherwise the fit completed successfully.
            Message = "Completed Successfully";
        }
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

    public class ModelCloneOptions
    {
        public bool IsGlobalClone { get; set; } = false;

        public ErrorEstimationMethod ErrorEstimationMethod { get; set; } = ErrorEstimationMethod.None;
        public bool IncludeConcentrationErrorsInBootstrap { get; set; } = false;
        public bool EnableAutoConcentrationVariance { get; set; } = false;
        public double AutoConcentrationVariance { get; set; } = 0.05f;
        public int DiscardedDataPoint { get; set; } = 0;
        public bool UnlockBootstrapParameters { get; set; } = false;

        public ModelCloneOptions()
        {
            ErrorEstimationMethod = FittingOptionsController.ErrorEstimationMethod;
            IncludeConcentrationErrorsInBootstrap = FittingOptionsController.IncludeConcentrationVariance;
            EnableAutoConcentrationVariance = FittingOptionsController.EnableAutoConcentrationVariance;
            AutoConcentrationVariance = FittingOptionsController.AutoConcentrationVariance;
            UnlockBootstrapParameters = FittingOptionsController.UnlockBootstrapParameters;
        }

        public static ModelCloneOptions DefaultOptions
        {
            get
            {
                return new ModelCloneOptions();
            }
        }

        public static ModelCloneOptions DefaultGlobalOptions
        {
            get
            {
                return new ModelCloneOptions()
                {
                    IsGlobalClone = true,
                };
            }
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

    public enum AlgLibTerminationCode
    {
        [Description("Code0")]
        Code0 = 0,
        [Description("Loss function successfully converged")]
        FunctionConverged = 1,
        [Description("Step size smaller than limit")]
        StepSizeTooSmall = 2,
        [Description("Code3")]
        Code3 = 3,
        [Description("Gradient ")]
        GradientIsFlat = 4,
        [Description("Optimizer iteration limit")]
        MaxIterations = 5,
        Code6 = 6,
        [Description("Not converging")]
        NotReachingStopCriteria = 7,
        [Description("Terminated by user")]
        TerminatedByUser = 8,
        //1=relative function improvement is no more than EpsF. 2=relative step is no more than EpsX. 4=gradient norm is no more than EpsG. 5=MaxIts steps was taken. 7=stopping conditions are too stringent, further improvement is impossible, we return best X found so far. 8= terminated by user
    }
}
