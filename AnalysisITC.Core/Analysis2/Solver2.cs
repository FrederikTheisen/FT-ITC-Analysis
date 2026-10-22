using System;
using System.Linq;
using Accord.Math.Optimization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.ExceptionServices;
using Accord.Math;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Platform;

using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Core.Analysis
{
    internal sealed class InvalidInitialModelException : ArithmeticException
    {
        internal InvalidInitialModelException(string message) : base(message) { }
    }

    public static class FittingOptionsController
    {
        public static IReadOnlyList<int> BootstrapIterationPresets { get; } = Array.AsReadOnly(new[]
        {
            10,
            50,
            100,
            200,
            500,
            1_000,
            2_000,
            5_000,
            10_000,
        });

        public static ErrorEstimationMethod ErrorEstimationMethod { get; set; } = ErrorEstimationMethod.BootstrapResiduals;
        public static int BootstrapIterations { get; set; } = 100;
        public static bool UnlockBootstrapParameters { get; set; } = false;
        public static bool IncludeConcentrationVariance { get; set; } = false;
        public static SolverAlgorithm Algorithm { get; set; } = SolverAlgorithm.NelderMead;
        public static bool UseErrorWeightedFitting { get; set; } = false;
        public static bool EnableSolverDiagnostics { get; set; } = false;

        /// <summary>
        /// Restores the live inspector fitting options from the current application
        /// preferences without changing or persisting those preferences.
        /// </summary>
        public static void ResetToPreferenceDefaults()
        {
            ErrorEstimationMethod = AppSettings.DefaultErrorEstimationMethod;
            BootstrapIterations = AppSettings.DefaultBootstrapIterations;
            IncludeConcentrationVariance = AppSettings.IncludeConcentrationErrorsInBootstrap;
            Algorithm = AppSettings.DefaultSolverAlgorithm;
            UseErrorWeightedFitting = AppSettings.UseInjectionErrorWeightedFitting;
            UnlockBootstrapParameters = false;
        }
    }

    public class SolverInterface
    {
        public const double ErrorEstimationToleranceModifier = 2;

        public static TerminationFlag TerminateAnalysisFlag { get; protected set; } = new TerminationFlag();

        public bool Silent { get; set; } = false;

        // Used by an owning global solver to aggregate deterministic profile
        // progress from silent member solvers without exposing child events.
        internal Action<ProfileLikelihoodProgress> ProfileProgressObserver { get; set; }

        public static event EventHandler<TerminationFlag> AnalysisStarted;
        public static event EventHandler<SolverConvergence> AnalysisFinished;
        public static event EventHandler AnalysisStepFinished;
        public static event EventHandler<FittingProgress> ProgressChanged;
        public static event EventHandler<FittingCompletion> FittingCompleted;
        public ProcessingOperation Operation { get; private set; }
        Guid standaloneOperationId = Guid.NewGuid();
        public Guid OperationId => Operation?.Id ?? standaloneOperationId;
        public bool IsResultUpdate { get; internal set; }
        protected bool deferCompletion;
        bool completionPublished;
        SolverConvergence pendingCompletion;
        int currentMemberPosition;
        string currentMemberName;
        FittingPhase currentPhase;
        int? currentCompleted, currentTotal;
        readonly object progressGate = new();
        long progressSequence, deliveredProgressSequence;
        FittingProgress deliveredProgress;
        SolverInterface progressOwner;
        ProgressDeliveryState progressDelivery = new();
        internal Action<FittingPhase, int?, int?, float?> ProgressObserver { get; set; }

        bool HasMonotonicRefitProgress => ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals
            || ErrorEstimationMethod == ErrorEstimationMethod.LeaveOneOut;

        internal void SetProgressOwner(SolverInterface owner, int position, string name)
        {
            progressOwner = owner;
            progressDelivery = owner.progressDelivery;
            var delivery = progressDelivery;
            ProgressObserver = (phase, completed, total, fraction) =>
            {
                if (ReferenceEquals(owner.progressDelivery, delivery))
                    owner.PublishProgress(phase, completed, total, fraction, position, name);
            };
        }

        void DispatchProgress(float? fraction, bool uncertainty, Action<float?> notify)
        {
            var owner = progressOwner ?? this;
            if (!owner.HasMonotonicRefitProgress)
            {
                PlatformServices.MainThreadDispatcher.Invoke(() => notify(fraction));
                return;
            }

            var delivery = progressDelivery;
            lock (delivery.Gate)
            {
                if (!ReferenceEquals(owner.progressDelivery, delivery)) return;
                delivery.Pending.Enqueue(new ProgressNotification(owner.OperationId, uncertainty, fraction, notify));
                if (delivery.Draining) return;
                delivery.Draining = true;
            }
            var started = 0;
            try
            {
                // Neither dispatch nor observers run while a state lock is held.
                PlatformServices.MainThreadDispatcher.Invoke(() =>
                {
                    Interlocked.Exchange(ref started, 1);
                    owner.DrainProgress(delivery);
                });
            }
            catch
            {
                // Once started, the drainer owns cleanup; it releases delivery before
                // rethrowing an observer failure, so another producer may already drain.
                if (Volatile.Read(ref started) == 0)
                    lock (delivery.Gate) delivery.Draining = false;
                throw;
            }
        }

        void DrainProgress(ProgressDeliveryState delivery)
        {
            var finished = false;
            ExceptionDispatchInfo failure = null;
            try
            {
                while (true)
                {
                    ProgressNotification notification;
                    float? fraction;
                    lock (delivery.Gate)
                    {
                        if (!ReferenceEquals(progressDelivery, delivery)) delivery.Pending.Clear();
                        if (delivery.Pending.Count == 0)
                        {
                            delivery.Draining = false;
                            finished = true;
                            break;
                        }
                        notification = delivery.Pending.Dequeue();
                        if (notification.OperationId != OperationId) continue;
                        fraction = notification.Fraction;
                        if (notification.Uncertainty && fraction.HasValue && fraction.Value >= 0)
                        {
                            fraction = Math.Max(delivery.HighestFraction ?? fraction.Value, fraction.Value);
                            delivery.HighestFraction = fraction;
                        }
                    }
                    // An observer failure must not strand later notifications, such as completion,
                    // that have no further report to schedule their delivery.
                    try { notification.Notify(fraction); }
                    catch (Exception ex) { failure ??= ExceptionDispatchInfo.Capture(ex); }
                }
            }
            finally
            {
                // On normal exit the flag was cleared atomically with the empty check.
                if (!finished) lock (delivery.Gate) delivery.Draining = false;
            }
            failure?.Throw();
        }

        internal sealed class ProgressDeliveryState
        {
            internal readonly object Gate = new();
            internal readonly Queue<ProgressNotification> Pending = new();
            internal bool Draining;
            internal float? HighestFraction;
        }

        internal sealed class ProgressNotification
        {
            internal readonly Guid OperationId;
            internal readonly bool Uncertainty;
            internal readonly float? Fraction;
            internal readonly Action<float?> Notify;
            internal ProgressNotification(Guid operationId, bool uncertainty, float? fraction, Action<float?> notify)
            { OperationId = operationId; Uncertainty = uncertainty; Fraction = fraction; Notify = notify; }
        }

        internal void PublishProgress(FittingPhase phase, int? completed = null, int? total = null, float? fraction = null,
            int memberPosition = 0, string memberName = null)
        {
            if (ProgressObserver != null) { ProgressObserver(phase, completed, total, fraction); return; }
            if (Silent) return;
            FittingProgress snapshot;
            long sequence;
            lock (progressGate)
            {
                if (TerminateAnalysisFlag.Up) { phase = FittingPhase.Stopping; completed = total = null; }
                if (phase != currentPhase || memberPosition > 0 && memberPosition != currentMemberPosition
                    || total.HasValue && currentTotal.HasValue && total != currentTotal)
                    currentCompleted = currentTotal = null;
                if (completed.HasValue) currentCompleted = Math.Max(currentCompleted ?? completed.Value, completed.Value);
                if (total.HasValue) currentTotal = total;
                currentPhase = phase;
                if (phase == FittingPhase.Stopping) currentCompleted = currentTotal = null;
                completed = currentCompleted;
                total = currentTotal;
                if (memberPosition > 0) { currentMemberPosition = memberPosition; currentMemberName = memberName; }
                var global = this as GlobalSolver;
                var count = global?.Model?.Models.Count ?? 1;
                var scope = count > 1 && global?.Model?.ShouldFitIndividually == false ? FittingScope.Global
                    : count > 1 ? FittingScope.Individual : FittingScope.Single;
                if (scope == FittingScope.Individual)
                {
                    memberPosition = currentMemberPosition > 0 ? currentMemberPosition : 1;
                    memberName ??= currentMemberName ?? global.Model.Models[memberPosition - 1].Data.Name;
                }
                if (scope == FittingScope.Single)
                    memberName ??= (this as Solver)?.Model?.Data?.Name ?? global?.Model?.Models.FirstOrDefault()?.Data?.Name;
                snapshot = new FittingProgress(OperationId, phase, scope, memberPosition, count, memberName,
                    ErrorEstimationMethod, completed, total, fraction);
                sequence = ++progressSequence;
            }
            DispatchProgress(snapshot.Fraction, snapshot.Phase == FittingPhase.EstimatingUncertainty,
                fraction => DeliverProgress(snapshot, sequence, fraction));
        }

        void DeliverProgress(FittingProgress snapshot, long sequence, float? fraction)
        {
            snapshot = new FittingProgress(snapshot.OperationId, snapshot.Phase, snapshot.Scope,
                snapshot.MemberPosition, snapshot.MemberCount, snapshot.MemberName, snapshot.Method,
                snapshot.Completed, snapshot.Total, fraction);
            lock (progressGate)
            {
                if (snapshot.OperationId == OperationId)
                {
                    // Keep metadata current even if capture and submission were reordered.
                    if (deliveredProgress != null && sequence < deliveredProgressSequence)
                        snapshot = new FittingProgress(snapshot.OperationId, deliveredProgress.Phase,
                            deliveredProgress.Scope, deliveredProgress.MemberPosition, deliveredProgress.MemberCount,
                            deliveredProgress.MemberName, deliveredProgress.Method, deliveredProgress.Completed,
                            deliveredProgress.Total, snapshot.Fraction);
                    else
                    {
                        deliveredProgress = snapshot;
                        deliveredProgressSequence = sequence;
                    }
                }
            }
            ProgressChanged?.Invoke(this, snapshot);
        }

        protected void PublishCompletion()
        {
            TerminateAnalysisFlag.WasRaised -= ReportStopping;
            if (Silent || completionPublished || pendingCompletion == null) return;
            completionPublished = true;
            var snapshot = new FittingCompletion(OperationId, this, pendingCompletion,
                Operation?.Elapsed ?? pendingCompletion.TotalTime, IsResultUpdate);
            DispatchProgress(null, uncertainty: false, _ => FittingCompleted?.Invoke(this, snapshot));
        }

        public SolverAlgorithm SolverAlgorithm { get; set; } = SolverAlgorithm.NelderMead;
        public int MaxOptimizerIterations { get; set; } = AppSettings.MaximumOptimizerIterations;
        public bool UseErrorWeightedFitting { get; set; } = false;
        public bool CanCreateAnalysisResult { get; set; } = true;
        public bool CanReportAnalysisStepFinished { get; set; } = true;
        public bool EnableSolverDiagnostics { get; set; } = true;

        public ErrorEstimationMethod ErrorEstimationMethod { get; set; } = ErrorEstimationMethod.None;
        public int BootstrapIterations { get; set; } = 100;

        internal enum UncertaintyDecision { Estimate, Skip, Defer }

        /// <summary>
        /// Called between the fit and uncertainty estimation when estimation could run. Top-level
        /// analyses use it to calculate the null comparison first and skip estimation for fits
        /// assessed as no binding. Defer leaves estimation to the caller (<see cref="EstimateUncertainty"/>).
        /// </summary>
        internal Func<SolverConvergence, UncertaintyDecision> UncertaintyGate { get; set; }

        /// <summary>Manual assessment applied in place of the automatic one when deciding to skip (Update Result).</summary>
        internal BindingAssessmentOutcome? ManualAssessmentOverride { get; set; }

        /// <summary>Skip uncertainty estimation for fits assessed as no binding (hidden setting, ITC-083).</summary>
        internal bool SkipNoBindingUncertainty { get; set; } = AppSettings.SkipUncertaintyEstimationForNoBinding;

        protected bool SkipsNoBindingUncertainty =>
            SkipNoBindingUncertainty && ErrorEstimationMethod != ErrorEstimationMethod.None;

        protected static bool IsAssessedNoBinding(NullModelComparison comparison, BindingAssessmentOutcome? manualOverride) =>
            (manualOverride ?? BindingAssessmentState.FromComparison(comparison).AutomaticOutcome)
                == BindingAssessmentOutcome.NoBindingDetected;
        public int MaxBootstrapOptimizerIterations => Math.Max(10, MaxOptimizerIterations / 3);
        public double SolverToleranceModifier { get; set; } = 1;

        /// <summary>
        /// Transforms tolerance setting into negative exponent. Tolerance = 1 will yield 10^-max.
        /// </summary>
        /// <param name="min">Smallest absolute exponent</param>
        /// <param name="max">Largest absolute exponent</param>
        /// <returns>10^-exp where exp is between min and max</returns>
        public double Tolerance(double min, double max)
        {
            var exp = min + AppSettings.OptimizerTolerance * (max - min);

            return Math.Pow(10, -exp) * SolverToleranceModifier;
        }

        // NM Parameters
        public double RelativeParameterTolerance => Tolerance(3, 10);

        protected double NMFunctionTolerance(double guessloss)
        {
            return Math.Max(1E-30, guessloss * Tolerance(9, 14));
        }

        // LM parameters
        public double LevenbergMarquardtDifferentiationStepSize => Tolerance(17, 22);  
        public double LevenbergMarquardtEpsilon => Tolerance(15, 22);
        public double LevenbergMarquardtGradientTolerance => Tolerance(19, 24);
        public double LevenbergMarquardtStepTolerance => Tolerance(18, 23);

        protected double[] GetLevenbergMarquardtInitialGuess(IReadOnlyList<Parameter> parameters, double[] exact, out bool usedInward)
        {
            const double zeroValueStepFraction = 1e-3;
            var inward = ParameterStartNormalizer.Inward(parameters, exact, out usedInward);
            // Give Math.NET a scale-aware nonzero point from which to estimate the numerical derivative.
            for (var i = 0; i < parameters.Count && i < inward.Length; i++)
                if (inward[i] == exact[i] && exact[i] == 0)
                {
                    var kicked = parameters[i].StepSize * zeroValueStepFraction;
                    var limits = parameters[i].Limits;
                    if (limits == null || limits.Length < 2)
                        inward[i] = kicked;
                    else
                    {
                        var lower = limits[0]; var upper = limits[1];
                        var span = upper - lower;
                        if (FWEMath.IsFinite(lower) && FWEMath.IsFinite(upper) && FWEMath.IsFinite(span) && upper > lower)
                        {
                            if (!(kicked >= lower && kicked <= upper)) kicked = Math.Min(upper, Math.Max(lower, kicked));
                            if (kicked == lower || kicked == upper)
                            {
                                var scale = Math.Max(Math.Max(Math.Abs(lower), Math.Abs(upper)), Math.Abs(parameters[i].StepSize));
                                var delta = Math.Min(span / 4, Math.Max(0.002 * Math.Abs(parameters[i].StepSize), 32 * 2.2204460492503131e-16 * scale));
                                var interior = kicked == lower ? lower + delta : upper - delta;
                                if (interior > lower && interior < upper)
                                {
                                    kicked = interior;
                                    usedInward = true;
                                }
                            }
                            inward[i] = kicked;
                        }
                    }
                }
            return inward;
        }

        protected double[] GetLevenbergMarquardtInitialGuess(IReadOnlyList<Parameter> parameters)
        {
            return GetLevenbergMarquardtInitialGuess(parameters,
                parameters.Select(parameter => parameter.Value).ToArray(), out _);
        }

        CancellationTokenSource nelderMeadToken;
        internal NonlinearMinimizationResult LmResult { get; set; }

        protected DateTime starttime;
        protected DateTime endtime;
        protected IReadOnlyList<ParameterBoundaryContact> LastParameterBoundaryContacts { get; private set; } = Array.Empty<ParameterBoundaryContact>();
        double invalidCandidateObjective;
        double[] invalidCandidateResiduals = Array.Empty<double>();
        int rejectedTrialEvaluationCount;

        protected readonly struct FitMetrics
        {
            public double UnweightedRmsd { get; }
            public double? Objective { get; }

            public FitMetrics(double unweightedRmsd, double? objective)
            {
                UnweightedRmsd = unweightedRmsd;
                Objective = objective;
            }
        }

        internal int RejectedTrialEvaluationCount => rejectedTrialEvaluationCount;
        public TimeSpan Duration
        {
            get
            {
                if (endtime != null) return endtime - starttime;
                else if (starttime != null) return DateTime.Now - starttime;
                else return TimeSpan.Zero;
            }
        }

        public static SolverInterface Initialize(ModelFactory factory)
        {
            switch (factory)
            {
                default:
                case SingleModelFactory: return Initialize((factory as SingleModelFactory).Model);
                case GlobalModelFactory: return Initialize((factory as GlobalModelFactory).Model);
            }
        }

        public static SolverInterface Initialize(Model model)
        {
            ValidateInitialParameterLimits(model);
            var solver = new Solver();
            solver.Model = model;

            return solver;
        }

        public static SolverInterface Initialize(GlobalModel model)
        {
            ValidateInitialParameterLimits(model);
            var solver = new GlobalSolver();
            solver.Model = model;

            return solver;
        }

        internal static void ValidateInitialParameterLimits(Model model)
        {
            if (model?.Parameters == null) return;
            ParameterStartNormalizer.Clamp(model);
            ValidateFiniteStartingCoordinates(model.Parameters.Table.Values);
            InitialParameterLimitViolationDetector.ThrowIfAny(
                InitialParameterLimitViolationDetector.Detect(model));
        }

        internal static void ValidateInitialParameterLimits(GlobalModel model)
        {
            if (model?.Parameters == null) return;

            // A programmatic GlobalModel may not have gone through
            // AnalysisContext.FinalizeForSolver yet. Build the member mapping
            // here so constrained coordinates are marked globally determined
            // before they are checked.
            if (model.Parameters.IndividualModelParameterList.Count == 0
                && model.Models?.Count > 0)
            {
                foreach (var member in model.Models)
                    model.Parameters.AddIndivdualParameter(member.Parameters);
            }

            model.Parameters.SetIndividualFromGlobal();

            ParameterStartNormalizer.Clamp(model);
            model.Parameters.SetIndividualFromGlobal();
            ValidateFiniteStartingCoordinates(model.Parameters.GlobalTable.Values);
            ValidateFiniteStartingCoordinates(model.Parameters.IndividualModelParameterList
                .SelectMany(parameters => parameters?.Table?.Values ?? Enumerable.Empty<Parameter>()));
            InitialParameterLimitViolationDetector.ThrowIfAny(
                InitialParameterLimitViolationDetector.Detect(model));
        }

        static void ValidateFiniteStartingCoordinates(IEnumerable<Parameter> parameters)
        {
            foreach (var parameter in parameters ?? Enumerable.Empty<Parameter>())
            {
                if (parameter == null || !parameter.IsFitted) continue;
                if (!FWEMath.IsFinite(parameter.Value))
                    throw new InvalidInitialModelException($"The starting value for {parameter.Key} is not finite.");
                if (parameter.Limits == null || parameter.Limits.Length < 2) continue;
                if (!FWEMath.IsFinite(parameter.Limits[0]) || !FWEMath.IsFinite(parameter.Limits[1])
                    || parameter.Limits[1] < parameter.Limits[0])
                    throw new InvalidInitialModelException($"The active limits for {parameter.Key} are invalid.");
            }
        }

        public void ReportBootstrapProgress(int iteration) => ReportErrorEstimationProgress(iteration, BootstrapIterations);

        public void ReportLeaveOneOutProgress(int iteration, int models) => ReportErrorEstimationProgress(iteration, models);

        void ReportErrorEstimationProgress(int iteration, int total)
        {
            var background = Silent ? SolverUpdate.BackgroundBootstrapUpdate(iteration, total) : null;
            PublishProgress(FittingPhase.EstimatingUncertainty, iteration, total,
                background?.Progress ?? iteration / (float)total);
        }

        public void ReportAnalysisStepFinished()
        {
            if (Silent || !CanReportAnalysisStepFinished) return;

            DispatchProgress(null, uncertainty: false, _ =>
            {
                AnalysisStepFinished?.Invoke(this, EventArgs.Empty);
            });
        }

        public void ReportAnalysisFinished(SolverConvergence convergence)
        {
            if (Silent) return;
            // Every completion listener, including desktop progress handlers,
            // needs a concrete outcome even when no solver result was produced.
            convergence ??= SolverConvergence.FromException(new InvalidOperationException(
                "Analysis finished without convergence information."), starttime);
            pendingCompletion = convergence;
            if (!deferCompletion || IsResultUpdate) PublishCompletion();
            DispatchProgress(null, uncertainty: false, _ => AnalysisFinished?.Invoke(this, convergence));
        }

        public void ReportSolverUpdate(SolverUpdate update)
        {
            if (Silent) return;
            var progress = update.Progress;
            FittingPhase phase;
            lock (progressGate) phase = currentPhase;
            if (progress >= 0) PublishProgress(phase, fraction: progress);
        }

        public virtual void Analyze()
        {
            starttime = DateTime.Now;
            var operation = ProcessingActivity.Current;
            lock (progressGate)
            {
                Operation = operation;
                standaloneOperationId = Guid.NewGuid();
                currentMemberPosition = 0;
                currentMemberName = null;
                currentCompleted = currentTotal = null;
                deliveredProgress = null;
                progressDelivery = new ProgressDeliveryState();
            }
            completionPublished = false;
            pendingCompletion = null;
            TerminateAnalysisFlag.Lower();
            TerminateAnalysisFlag.WasRaised -= ReportStopping;
            TerminateAnalysisFlag.WasRaised += ReportStopping;
            AnalysisStarted?.Invoke(this, TerminateAnalysisFlag);

            StatusBarManager.StartInderminateProgress();

            PublishProgress(FittingPhase.Fitting);
        }

        void ReportStopping(object sender, EventArgs e) => PublishProgress(FittingPhase.Stopping);

        private void TerminateAnalysisFlag_WasRaised(object sender, EventArgs e)
        {
            PublishProgress(FittingPhase.Stopping);

            // Each solver owns its optimizer token. The shared termination flag broadcasts the
            // stop request, while this handler only cancels this solver's token.
            var token = Volatile.Read(ref nelderMeadToken);

            try
            {
                token?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Solver finalisation won the race with the stop request.
            }
            catch (Exception ex)
            {
                // Log and surface unexpected exceptions via the central handler. The use of
                // DisplayHandledException will ensure only a concise message is shown to the user.
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        public virtual SolverConvergence Solve()
        {
            using var activity = ProcessingActivity.Begin();
            Operation = ProcessingActivity.Current;
            ApplyErrorEstimationPolicy();

            // Subscribe to the termination flag so we can stop the underlying solver when requested.
            TerminateAnalysisFlag.WasRaised += TerminateAnalysisFlag_WasRaised;
            try
            {
                SolverConvergence convergence;

                switch (this)
                {
                    case Solver local:
                        ValidateInitialParameterLimits(local.Model);
                        break;
                    case GlobalSolver global:
                        ValidateInitialParameterLimits(global.Model);
                        break;
                }

                switch (SolverAlgorithm)
                {
                    case SolverAlgorithm.NelderMead:
                        convergence = SolveWithNelderMeadAlgorithm();
                        break;
                    case SolverAlgorithm.LevenbergMarquardt:
                        convergence = SolverWithLevenbergMarquardtAlgorithm2();
                        break;
                    default:
                        throw new NotImplementedException("Solver algorithm not implemented");
                }

                // Capture the primary request before estimation, including intentional skips.
                convergence?.CaptureBootstrapRequest(ErrorEstimationMethod, BootstrapIterations);
                if (this is GlobalSolver pooled)
                    foreach (var member in pooled.Model.Solution?.Solutions ?? new List<SolutionInterface>())
                        member.Convergence?.CaptureBootstrapRequest(ErrorEstimationMethod, BootstrapIterations);
                ReportAnalysisStepFinished();

                if (convergence?.CanRunErrorEstimation == true)
                {
                    switch (UncertaintyGate?.Invoke(convergence) ?? UncertaintyDecision.Estimate)
                    {
                        case UncertaintyDecision.Estimate:
                            if (ErrorEstimationMethod != ErrorEstimationMethod.None) PublishProgress(FittingPhase.EstimatingUncertainty);
                            EstimateUncertainty(convergence);
                            break;
                        case UncertaintyDecision.Skip:
                            convergence.MarkErrorEstimationSkippedForNoBinding();
                            break;
                    }
                }

                endtime = DateTime.Now;

                return convergence;
            }
            catch (InitialParameterLimitException)
            {
                throw;
            }
            catch (InvalidInitialModelException ex)
            {
                var convergence = SolverConvergence.FromException(ex, starttime);
                switch (this)
                {
                    case Solver local when local.Model != null:
                        local.Model.Solution = SolutionInterface.FromModel(local.Model, convergence);
                        local.Model.Solution.ErrorMethod = ErrorEstimationMethod;
                        local.Model.Solution.UseWeightedFitting = UseErrorWeightedFitting;
                        break;
                    case GlobalSolver global when global.Model != null:
                        global.Model.Solution = new GlobalSolution(global, convergence);
                        break;
                }
                endtime = DateTime.Now;
                return convergence;
            }
            finally
            {
                TerminateAnalysisFlag.WasRaised -= TerminateAnalysisFlag_WasRaised;

                // Clear the instance field before disposal so a concurrent stop request cannot
                // discover a token owned by another solver or by a later solve.
                Interlocked.Exchange(ref nelderMeadToken, null)?.Dispose();
            }
        }

        internal void EstimateUncertainty(SolverConvergence convergence)
        {
            switch (ErrorEstimationMethod)
            {
                case ErrorEstimationMethod.BootstrapResiduals:
                    BoostrapResiduals();
                    break;
                case ErrorEstimationMethod.LeaveOneOut:
                    LeaveOneOut();
                    break;
                case ErrorEstimationMethod.ProfileLikelihood:
                    if (convergence.Success) ProfileLikelihood();
                    break;
                case ErrorEstimationMethod.None:
                default:
                    break;
            }
        }

        /// <summary>
        /// Keeps clone construction aligned with the method selected on the solver.
        /// Leave-one-out is deliberately deletion-only: stochastic input variation
        /// and parameter unlocking remain residual-bootstrap settings.
        /// </summary>
        internal void ApplyErrorEstimationPolicy()
        {
            switch (this)
            {
                case Solver local when local.Model != null:
                    local.Model.ModelCloneOptions = ApplyErrorEstimationPolicy(
                        local.Model.ModelCloneOptions,
                        ErrorEstimationMethod,
                        isGlobalClone: false);
                    break;

                case GlobalSolver global when global.Model != null:
                    var isGlobalClone = global.Model.ModelCloneOptions?.IsGlobalClone
                        ?? !global.Model.ShouldFitIndividually;
                    global.Model.ModelCloneOptions = ApplyErrorEstimationPolicy(
                        global.Model.ModelCloneOptions,
                        ErrorEstimationMethod,
                        isGlobalClone);
                    foreach (var member in global.Model.Models)
                    {
                        member.ModelCloneOptions = ApplyErrorEstimationPolicy(
                            member.ModelCloneOptions,
                            ErrorEstimationMethod,
                            isGlobalClone);
                    }
                    break;
            }
        }

        static ModelCloneOptions ApplyErrorEstimationPolicy(
            ModelCloneOptions options,
            ErrorEstimationMethod method,
            bool isGlobalClone)
        {
            // Profile runs are deterministic diagnostics layered on the primary
            // fit. Keep the source graph's user-selected bootstrap flags intact;
            // only the run graph receives the profile configuration.
            if (method == ErrorEstimationMethod.ProfileLikelihood)
            {
                var runOptions = new ModelCloneOptions
                {
                    IsGlobalClone = isGlobalClone,
                    ErrorEstimationMethod = options?.ErrorEstimationMethod ?? method,
                    IncludeConcentrationErrorsInBootstrap = options?.IncludeConcentrationErrorsInBootstrap ?? false,
                    EnableAutoConcentrationVariance = false,
                    AutoConcentrationVariance = 0,
                    DiscardedDataPoint = options?.DiscardedDataPoint ?? 0,
                    UnlockBootstrapParameters = options?.UnlockBootstrapParameters ?? false,
                };
                runOptions.ConfigureForRun(method);
                return runOptions;
            }

            options ??= isGlobalClone
                ? ModelCloneOptions.DefaultGlobalOptions
                : ModelCloneOptions.DefaultOptions;
            options.ConfigureForRun(method);

            return options;
        }

        protected virtual SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            throw new NotImplementedException();
        }

        protected virtual SolverConvergence SolverWithLevenbergMarquardtAlgorithm()
        {
            throw new NotImplementedException();
        }

        protected virtual SolverConvergence SolverWithLevenbergMarquardtAlgorithm2()
        {
            throw new NotImplementedException();
        }

        protected virtual void BoostrapResiduals()
        {
            AppEventHandler.Print($"Running Bootstrap Error with {BootstrapIterations} iterations...");

            ReportBootstrapProgress(0);
        }

        protected virtual void LeaveOneOut()
        {
            AppEventHandler.Print($"Running LeaveOneOut Error...");

            if (this is GlobalSolver)
            {
                ReportLeaveOneOutProgress(0, (this as GlobalSolver).Model.Models.Count);
            }
            else if (this is Solver)
            {
                ReportLeaveOneOutProgress(0, (this as Solver).Model.Data.Injections.Where(inj => inj.Include).Count());
            }
        }

        protected virtual void ProfileLikelihood() { }

        internal void SetStepSizes(NelderMead solver, double[] stepsize)
        {
            for (int i = 0; i < solver.StepSize.Length; i++) solver.StepSize[i] = stepsize[i];
        }

        internal void SetBounds(NelderMead solver, List<double[]> bounds)
        {
            var lower = bounds.Select(l => l[0]).ToArray();
            var upper = bounds.Select(l => l[1]).ToArray();

            for (int i = 0; i < solver.NumberOfVariables; i++)
            {
                solver.LowerBounds[i] = lower[i];
                solver.UpperBounds[i] = upper[i];
            }
        }

        internal void SetCancellationToken(object solver)
        {
            switch (solver)
            {
                case NelderMead simplex:
                    var token = new CancellationTokenSource();
                    Interlocked.Exchange(ref nelderMeadToken, token)?.Dispose();
                    simplex.Token = token.Token;
                    break;
                //case alglib.minlmstate minlm: LMOptimizerState = minlm; break;
            }
        }

        protected double PrepareCandidateEvaluations(
            Model model,
            double[] initial,
            bool errorWeighted,
            int pointCount)
        {
            if (!model.TryLossFunction(initial, errorWeighted, out var baselineObjective)
                || !model.HasFiniteIncludedPredictions())
                throw new InvalidInitialModelException(
                    "The initial model parameters do not produce finite predictions.");

            ConfigureInvalidCandidatePenalty(baselineObjective, pointCount);
            return baselineObjective;
        }

        protected double PrepareCandidateEvaluations(
            GlobalModel model,
            double[] initial,
            bool errorWeighted,
            int pointCount)
        {
            if (!model.TryLossFunction(initial, errorWeighted, out var baselineObjective)
                || model.Models.Any(member => !member.HasFiniteIncludedPredictions()))
                throw new InvalidInitialModelException(
                    "The initial global model parameters do not produce finite predictions.");

            ConfigureInvalidCandidatePenalty(baselineObjective, pointCount);
            return baselineObjective;
        }

        void ConfigureInvalidCandidatePenalty(double baselineObjective, int pointCount)
        {
            if (!FWEMath.IsFinite(baselineObjective) || baselineObjective < 0 || pointCount <= 0)
                throw new InvalidInitialModelException("The initial model objective is not finite.");

            invalidCandidateObjective = Math.Max(1.0, baselineObjective) * 1e12;
            if (!FWEMath.IsFinite(invalidCandidateObjective))
                throw new InvalidInitialModelException(
                    "The invalid-candidate penalty is not representable.");

            var residualPenalty = Math.Sqrt(invalidCandidateObjective / pointCount);
            if (!FWEMath.IsFinite(residualPenalty))
                throw new InvalidInitialModelException(
                    "The invalid-candidate residual penalty is not representable.");

            invalidCandidateResiduals = Enumerable.Repeat(residualPenalty, pointCount).ToArray();
            rejectedTrialEvaluationCount = 0;
        }

        protected double EvaluateCandidate(Model model, double[] parameters, bool errorWeighted)
        {
            if (model.TryLossFunction(parameters, errorWeighted, out var objective))
                return objective;

            rejectedTrialEvaluationCount++;
            return invalidCandidateObjective;
        }

        protected double EvaluateCandidate(GlobalModel model, double[] parameters, bool errorWeighted)
        {
            if (model.TryLossFunction(parameters, errorWeighted, out var objective))
                return objective;

            rejectedTrialEvaluationCount++;
            return invalidCandidateObjective;
        }

        protected double[] EvaluateCandidateResiduals(Model model, double[] parameters, bool errorWeighted)
        {
            if (model.TryLossFunctionResiduals(parameters, errorWeighted, out var residuals))
                return residuals;

            rejectedTrialEvaluationCount++;
            return invalidCandidateResiduals;
        }

        protected double[] EvaluateCandidateResiduals(GlobalModel model, double[] parameters, bool errorWeighted)
        {
            if (model.TryLossFunctionResiduals(parameters, errorWeighted, out var residuals))
                return residuals;

            rejectedTrialEvaluationCount++;
            return invalidCandidateResiduals;
        }

        protected void LogRejectedTrialEvaluations(string scope)
        {
            if (!SolverDiagnosticsEnabled || rejectedTrialEvaluationCount == 0) return;
            AppEventHandler.PrintAndLog(
                $"[FitDiag] {scope}: rejectedTrialEvaluations={rejectedTrialEvaluationCount}");
        }

        protected bool SolverDiagnosticsEnabled => EnableSolverDiagnostics && (FittingOptionsController.EnableSolverDiagnostics || AppSettings.Verbose);

        protected void LogSolverInput(string scope, IReadOnlyList<Parameter> parameters, double[] initial, double[] stepSizes, List<double[]> bounds)
        {
            if (!SolverDiagnosticsEnabled) return;

            AppEventHandler.PrintAndLog(
                $"[FitDiag] {scope} input: algorithm={SolverAlgorithm}, weighted={UseErrorWeightedFitting}, tolerance={AppSettings.OptimizerTolerance:G17}, maxIterations={MaxOptimizerIterations}, fittedParameters={initial.Length}");

            for (int i = 0; i < initial.Length; i++)
            {
                var parameter = i < parameters.Count ? parameters[i] : null;
                var key = parameter?.Key.ToString() ?? $"p{i}";
                var lower = i < bounds.Count ? bounds[i][0] : double.NaN;
                var upper = i < bounds.Count ? bounds[i][1] : double.NaN;
                var step = i < stepSizes.Length ? stepSizes[i] : double.NaN;

                AppEventHandler.PrintAndLog(
                    $"[FitDiag] {scope} input[{i}] {key}: value={initial[i]:G17}, step={step:G17}, bounds=[{lower:G17}, {upper:G17}]",
                    1);
            }
        }

        protected void LogSolverOutput(
            string scope,
            IReadOnlyList<Parameter> parameters,
            double[] initial,
            double[] fitted,
            double initialObjective,
            double fittedObjective,
            double initialLoss,
            double fittedLoss,
            bool acceptedFitted)
        {
            if (!SolverDiagnosticsEnabled) return;

            AppEventHandler.PrintAndLog(
                $"[FitDiag] {scope} output: accepted={(acceptedFitted ? "optimizer" : "initial")}, initialObjective={initialObjective:G17}, fittedObjective={fittedObjective:G17}, initialRMSD={initialLoss:G17}, fittedRMSD={fittedLoss:G17}");

            for (int i = 0; i < initial.Length; i++)
            {
                var parameter = i < parameters.Count ? parameters[i] : null;
                var key = parameter?.Key.ToString() ?? $"p{i}";
                var fittedValue = i < fitted.Length ? fitted[i] : double.NaN;
                var delta = fittedValue - initial[i];

                AppEventHandler.PrintAndLog(
                    $"[FitDiag] {scope} output[{i}] {key}: initial={initial[i]:G17}, optimizer={fittedValue:G17}, delta={delta:G17}",
                    1);
            }
        }

        protected FitMetrics ApplyBestFittedParameters(Model model, double[] initial, double[] fitted, bool errorWeighted, string scope, IReadOnlyList<Parameter> parameters)
            => ApplyBestFittedParameters(model, initial, fitted, errorWeighted, scope, parameters, usedInward: false);

        protected FitMetrics ApplyBestFittedParameters(Model model, double[] initial, double[] fitted, bool errorWeighted, string scope, IReadOnlyList<Parameter> parameters, bool usedInward)
        {
            if (!model.TryLossFunction(initial, errorWeighted, out var initialObjective)
                || !model.HasFiniteIncludedPredictions())
                throw new ArithmeticException("The initial model parameters no longer produce finite predictions.");
            var initialLoss = model.Loss();

            var fittedIsValid = model.TryLossFunction(fitted, errorWeighted, out var fittedObjective)
                                && model.HasFiniteIncludedPredictions();
            var fittedLoss = fittedIsValid ? model.Loss() : double.NaN;
            if (!fittedIsValid) fittedObjective = invalidCandidateObjective;
            var acceptedFitted = fittedIsValid && (usedInward
                ? fittedObjective < initialObjective
                : fittedObjective <= initialObjective);

            LogSolverOutput(scope, parameters, initial, fitted, initialObjective, fittedObjective, initialLoss, fittedLoss, acceptedFitted);

            if (acceptedFitted)
            {
                LastParameterBoundaryContacts = ParameterBoundaryDetector.Detect(model, ParameterBoundaryScope.Local);
                return new FitMetrics(fittedLoss, fittedObjective);
            }

            if (!model.TryLossFunction(initial, errorWeighted, out _)
                || !model.HasFiniteIncludedPredictions())
                throw new ArithmeticException("The initial model parameters could not be restored.");
            LastParameterBoundaryContacts = ParameterBoundaryDetector.Detect(model, ParameterBoundaryScope.Local);
            return new FitMetrics(initialLoss, initialObjective);
        }

        protected FitMetrics ApplyBestFittedParameters(GlobalModel model, double[] initial, double[] fitted, bool errorWeighted, string scope, IReadOnlyList<Parameter> parameters)
            => ApplyBestFittedParameters(model, initial, fitted, errorWeighted, scope, parameters, usedInward: false);

        protected FitMetrics ApplyBestFittedParameters(GlobalModel model, double[] initial, double[] fitted, bool errorWeighted, string scope, IReadOnlyList<Parameter> parameters, bool usedInward)
        {
            if (!model.TryLossFunction(initial, errorWeighted, out var initialObjective)
                || model.Models.Any(member => !member.HasFiniteIncludedPredictions()))
                throw new ArithmeticException("The initial global model parameters no longer produce finite predictions.");
            var initialLoss = model.Loss();

            var fittedIsValid = model.TryLossFunction(fitted, errorWeighted, out var fittedObjective)
                                && model.Models.All(member => member.HasFiniteIncludedPredictions());
            var fittedLoss = fittedIsValid ? model.Loss() : double.NaN;
            if (!fittedIsValid) fittedObjective = invalidCandidateObjective;
            var acceptedFitted = fittedIsValid && (usedInward
                ? fittedObjective < initialObjective
                : fittedObjective <= initialObjective);

            LogSolverOutput(scope, parameters, initial, fitted, initialObjective, fittedObjective, initialLoss, fittedLoss, acceptedFitted);

            if (acceptedFitted)
            {
                LastParameterBoundaryContacts = ParameterBoundaryDetector.Detect(model);
                return new FitMetrics(fittedLoss, fittedObjective);
            }

            if (!model.TryLossFunction(initial, errorWeighted, out _)
                || model.Models.Any(member => !member.HasFiniteIncludedPredictions()))
                throw new ArithmeticException("The initial global model parameters could not be restored.");
            LastParameterBoundaryContacts = ParameterBoundaryDetector.Detect(model);
            return new FitMetrics(initialLoss, initialObjective);
        }

        protected void ApplyBoundaryContacts(SolverConvergence convergence)
        {
            convergence?.SetParameterBoundaryContacts(LastParameterBoundaryContacts);
        }

        protected bool ShouldCreateAnalysisResult(SolverConvergence convergence)
        {
            return CanCreateAnalysisResult && convergence != null && !convergence.Failed && !convergence.Stopped;
        }

        protected static void ValidateFinalPredictions(
            SolverConvergence convergence,
            IEnumerable<Model> models)
        {
            if (convergence == null || convergence.Stopped) return;

            var allFinite = (models ?? Enumerable.Empty<Model>())
                .Where(model => model != null)
                .All(model => model.HasFiniteIncludedPredictions());
            if (!allFinite)
                convergence.MarkInvalidValues("The accepted model parameters produced non-finite predictions.");
        }
    }

    public class Solver : SolverInterface
    {
        public Model Model { get; set; }
        public SolutionInterface Solution => Model.Solution;

        public override async void Analyze()
        {
            using var activity = await ProcessingActivity.BeginAsync();
            using var ownership = activity.Enter();
            deferCompletion = true;
            // Prepare analysis state and notify listeners.
            base.Analyze();

            SolverConvergence convergence = null;

            try
            {
                // Run the solve operation on a background thread. Any exceptions thrown during solving will
                // propagate to the catch blocks below.
                await Task.Run(() =>
                {
                    var comparisonCalculated = false;
                    if (SkipsNoBindingUncertainty)
                        UncertaintyGate = _ =>
                        {
                            if (Model?.Solution == null) return UncertaintyDecision.Estimate;
                            CalculateNullComparison();
                            comparisonCalculated = true;
                            return IsAssessedNoBinding(Model.Solution.NullComparison, ManualAssessmentOverride)
                                ? UncertaintyDecision.Skip : UncertaintyDecision.Estimate;
                        };
                    try { convergence = Solve(); }
                    finally { UncertaintyGate = null; }
                    if (!comparisonCalculated && convergence?.Stopped != true && Model?.Solution != null)
                        CalculateNullComparison();
                    ReportAnalysisFinished(convergence);
                });

                if (AppSettings.CreateSingleAnalysisResult
                    && ShouldCreateAnalysisResult(convergence)
                    && Model?.Solution != null)
                {
                    DataManager.AddData(new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(this)));
                }
            }
            catch (Exception ex)
            {
                var conv = SolverConvergence.FromException(ex, starttime);
                try { conv.SetUnweightedRmsd(Model.Loss()); }
                catch { }

                // Log and notify the user only for genuine failures; user cancellations are
                // considered non-error conditions.
                if (!conv.Stopped)
                {
                    try
                    {
                        Model.Solution = SolutionInterface.FromModel(Model, conv);
                        Model.Solution.UseWeightedFitting = UseErrorWeightedFitting;
                        NullModelComparisonCalculator.Calculate(Model.Solution, UseErrorWeightedFitting,
                            SolverAlgorithm, MaxOptimizerIterations, SolverToleranceModifier);
                    }
                    catch (Exception comparisonException)
                    {
                        if (Model?.Solution != null)
                            Model.Solution.NullComparison = NullModelComparisonCalculator.Failure(Model.Solution, comparisonException);
                        AppEventHandler.AddLog(comparisonException);
                    }
                    AppEventHandler.DisplayHandledException(conv.RootCause ?? ex);
                }

                ReportAnalysisFinished(conv);
            }
            finally { PublishCompletion(); }
        }

        void CalculateNullComparison()
        {
            PublishProgress(FittingPhase.AssessingBinding);
            try { NullModelComparisonCalculator.Calculate(Model.Solution, UseErrorWeightedFitting,
                SolverAlgorithm, MaxOptimizerIterations, SolverToleranceModifier); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Model.Solution.NullComparison = NullModelComparisonCalculator.Failure(Model.Solution, ex);
                AppEventHandler.AddLog(ex);
            }
        }

        protected override SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            var initialGuess = Model.Parameters.GetFittedParameterArray();
            var initialObjective = PrepareCandidateEvaluations(
                Model, initialGuess, UseErrorWeightedFitting, Model.NumberOfPoints);
            var f = new NonlinearObjectiveFunction(
                Model.NumberOfParameters,
                w => EvaluateCandidate(Model, w, UseErrorWeightedFitting));
            var solver = new NelderMead(f);

            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(Model.NumberOfParameters)
            {
                MaximumEvaluations = MaxOptimizerIterations,
                AbsoluteFunctionTolerance = NMFunctionTolerance(initialObjective),
                RelativeParameterTolerance = RelativeParameterTolerance,
                StartTime = DateTime.Now,
            };

            var fittedParameters = Model.Parameters.GetFittedParameters();
            var optimizerInitialGuess = ParameterStartNormalizer.Inward(fittedParameters, initialGuess, out var usedInward);
            var stepSizes = Model.Parameters.GetStepSizes();
            var bounds = Model.Parameters.GetLimits();
            SetStepSizes(solver, stepSizes);
            SetBounds(solver, bounds);
            // Allow the solver to be cancelled via the TerminateAnalysisFlag by associating it with a CancellationToken.
            SetCancellationToken(solver);

            LogSolverInput("Single/NelderMead", fittedParameters, optimizerInitialGuess, stepSizes, bounds);
            solver.Minimize(optimizerInitialGuess);
            LogRejectedTrialEvaluations("Single/NelderMead");

            var metrics = ApplyBestFittedParameters(Model, initialGuess, solver.Solution, UseErrorWeightedFitting, "Single/NelderMead", fittedParameters, usedInward);

            var convergence = new SolverConvergence(solver, metrics.UnweightedRmsd);
            convergence.SetObjective(metrics.Objective);
            ValidateFinalPredictions(convergence, new[] { Model });
            if (convergence.Failed || convergence.Stopped)
                convergence.SetObjective(null);
            if (!convergence.Failed && !convergence.Stopped)
                convergence.SetResidualStatistics(Model.ResidualStatistics());
            ApplyBoundaryContacts(convergence);
            Model.Solution = SolutionInterface.FromModel(Model, convergence);
            Model.Solution.ErrorMethod = ErrorEstimationMethod;
            Model.Solution.UseWeightedFitting = UseErrorWeightedFitting;

            return Model.Solution.Convergence;
        }

        protected override SolverConvergence SolverWithLevenbergMarquardtAlgorithm2()
        {
            DateTime start = DateTime.Now;

            var fittedParameters = Model.Parameters.GetFittedParameters();
            var limits = Model.Parameters.GetLimits();
            int m = Model.NumberOfPoints;
            double[] initialGuess = Model.Parameters.GetFittedParameterArray();
            double[] optimizerInitialGuess = GetLevenbergMarquardtInitialGuess(fittedParameters, initialGuess, out var usedInward);
            PrepareCandidateEvaluations(
                Model, initialGuess, UseErrorWeightedFitting, m);

            var observedX = Vector<double>.Build.Dense(m, i => (double)i); // dummy x
            var observedY = Vector<double>.Build.Dense(m, 0.0);            // fit residuals to zero

            IObjectiveModel objective = ObjectiveFunction.NonlinearModel(
                (Vector<double> p, Vector<double> x) =>
                {
                    var r = EvaluateCandidateResiduals(
                        Model, p.ToArray(), UseErrorWeightedFitting);
                    return Vector<double>.Build.DenseOfArray(r);
                },
                observedX,
                observedY
            );

            var minimizer = new LevenbergMarquardtMinimizer(
                gradientTolerance: this.LevenbergMarquardtGradientTolerance,
                functionTolerance: this.LevenbergMarquardtEpsilon,
                stepTolerance: this.LevenbergMarquardtStepTolerance,
                maximumIterations: MaxOptimizerIterations);

            double[] lower = limits.Select(b => b[0]).ToArray();
            double[] upper = limits.Select(b => b[1]).ToArray();
            double[] scales = optimizerInitialGuess.Select(g => Math.Max(1, Math.Abs(g))).ToArray();
            LogSolverInput("Single/LevenbergMarquardt", fittedParameters, optimizerInitialGuess, scales, limits);

            var result = minimizer.FindMinimum(
                objective,
                optimizerInitialGuess,
                lower,
                upper,
                scales);
            LogRejectedTrialEvaluations("Single/LevenbergMarquardt");

            //LmResult = result;

            var fitted = result.MinimizingPoint.ToArray();
            var metrics = ApplyBestFittedParameters(Model, initialGuess, fitted, UseErrorWeightedFitting, "Single/LevenbergMarquardt", fittedParameters, usedInward);

            var convergence = new SolverConvergence(result, DateTime.Now - start, metrics.UnweightedRmsd);
            convergence.SetObjective(metrics.Objective);
            ValidateFinalPredictions(convergence, new[] { Model });
            if (convergence.Failed || convergence.Stopped)
                convergence.SetObjective(null);
            if (!convergence.Failed && !convergence.Stopped)
                convergence.SetResidualStatistics(Model.ResidualStatistics());
            ApplyBoundaryContacts(convergence);
            Model.Solution = SolutionInterface.FromModel(Model, convergence);
            Model.Solution.ErrorMethod = ErrorEstimationMethod;
            Model.Solution.UseWeightedFitting = UseErrorWeightedFitting;

            return Model.Solution.Convergence;
        }

        protected override void BoostrapResiduals()
        {
            base.BoostrapResiduals();

            int counter = 0;
            int success = 0;
            int failure = 0;
            int limitTerminated = 0;
            var start = DateTime.Now;
            var solutionsByReplicate = new SolutionInterface[BootstrapIterations];
            var randomStreams = BootstrapRandomStreams.Create(BootstrapIterations);
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, AppSettings.MaxDegreeOfParallelism),
            };

            Parallel.For(0, BootstrapIterations, options, (i) =>
            {
                if (TerminateAnalysisFlag.Down)
                {
                    try
                    {
                        var solver = new Solver
                        {
                            SolverAlgorithm = this.SolverAlgorithm,
                            Model = Model.GenerateSyntheticModel(randomStreams[i]),
                            SolverToleranceModifier = ErrorEstimationToleranceModifier,
                            MaxOptimizerIterations = MaxBootstrapOptimizerIterations,
                            UseErrorWeightedFitting = this.UseErrorWeightedFitting,
                            EnableSolverDiagnostics = false,
                            Silent = true,
                        };

                        var rconv = solver.Solve();
                        // Only converged refits contribute to the bootstrap distribution.
                        // Limit-terminated best-so-far points are counted separately and
                        // reported as excluded warnings.
                        if (rconv?.IsUsableForErrorEstimation == true)
                        {
                            solver.Model.Solution.BootstrapReplicateIndex = i;
                            solutionsByReplicate[i] = solver.Model.Solution;
                            Interlocked.Increment(ref success);
                        }
                        else
                        {
                            if (rconv?.MaxIterationsReached == true)
                                Interlocked.Increment(ref limitTerminated);
                            Interlocked.Increment(ref failure);
                        }
                    } 
                    catch (Exception ex)
                    {
                        // Classify and count any replicate exception as a failure. The
                        // exception is not propagated beyond the replicate level; logging is
                        // deferred to the outer scope.
                        Interlocked.Increment(ref failure);
                        AppEventHandler.Print($"Bootstrap Error: {ex.Message}");
                    }
                }

                var currcounter = Interlocked.Increment(ref counter);
                ReportBootstrapProgress(currcounter);
            });

            var solutions = solutionsByReplicate.Where(solution => solution != null).ToList();

            Solution.SetBootstrapSolutions(solutions);
            failure += Math.Max(0, success - Solution.BootstrapSolutions.Count);
            success = Solution.BootstrapSolutions.Count;
            Solution.Convergence.ApplyErrorEstimationResult(
                ErrorEstimationMethod,
                failure,
                success,
                DateTime.Now - start,
                TerminateAnalysisFlag.Up,
                BootstrapIterations,
                limitTerminated);
        }

        protected override void LeaveOneOut()
        {
            AppEventHandler.Print($"Running LeaveOneOut Error...");

            int counter = 0;
            int success = 0;
            int failure = 0;
            int limitTerminated = 0;
            var start = DateTime.Now;
            var includedInjectionIds = Model.Data.Injections
                .Where(inj => inj.Include)
                .Select(inj => inj.ID)
                .ToList();
            var models = new Model[includedInjectionIds.Count];
            for (var index = 0; index < includedInjectionIds.Count; index++) //setup models, not thread safe due to MCO implementation
            {
                Model.ModelCloneOptions.DiscardedDataPoint = includedInjectionIds[index];
                models[index] = Model.GenerateSyntheticModel();
            }

            var solutionsByOmission = new SolutionInterface[models.Length];

            ReportLeaveOneOutProgress(0, models.Length);

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, AppSettings.MaxDegreeOfParallelism),
            };

            Parallel.For(0, models.Length, options, (i) =>
            {
                if (TerminateAnalysisFlag.Down)
                {
                    try
                    {
                        var mdl = models[i];
                        var solver = new Solver
                        {
                            SolverAlgorithm = this.SolverAlgorithm,
                            Model = mdl,
                            SolverToleranceModifier = ErrorEstimationToleranceModifier,
                            MaxOptimizerIterations = MaxBootstrapOptimizerIterations,
                            UseErrorWeightedFitting = this.UseErrorWeightedFitting,
                            EnableSolverDiagnostics = false,
                            Silent = true,
                        };

                        var rconv = solver.Solve();
                        if (rconv?.IsUsableForErrorEstimation == true)
                        {
                            solver.Model.Solution.BootstrapReplicateIndex = i;
                            solutionsByOmission[i] = solver.Model.Solution;
                            Interlocked.Increment(ref success);
                        }
                        else
                        {
                            if (rconv?.MaxIterationsReached == true)
                                Interlocked.Increment(ref limitTerminated);
                            Interlocked.Increment(ref failure);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Any exception during a replicate counts as a failure. Exceptions are
                        // not propagated beyond the replicate to avoid halting the entire
                        // leave-one-out procedure.
                        Interlocked.Increment(ref failure);
                        AppEventHandler.Print($"Bootstrap Error: {ex.Message}");
                    }
                }

                var currcounter = Interlocked.Increment(ref counter);
                ReportLeaveOneOutProgress(currcounter, models.Length);
            });

            var solutions = solutionsByOmission.Where(solution => solution != null).ToList();

            Solution.SetBootstrapSolutions(solutions);
            failure += Math.Max(0, success - Solution.BootstrapSolutions.Count);
            success = Solution.BootstrapSolutions.Count;
            Solution.Convergence.ApplyErrorEstimationResult(
                ErrorEstimationMethod,
                failure,
                success,
                DateTime.Now - start,
                TerminateAnalysisFlag.Up,
                models.Length,
                limitTerminated);
        }

        protected override void ProfileLikelihood()
        {
            var run = ProfileLikelihoodEstimator.Run(Model, SolverAlgorithm, UseErrorWeightedFitting,
                MaxBootstrapOptimizerIterations, ErrorEstimationToleranceModifier,
                progress: progress =>
                {
                    ProfileProgressObserver?.Invoke(progress);
                    if (ProfileProgressObserver == null)
                        PublishProgress(FittingPhase.EstimatingUncertainty, progress.CompletedSides, progress.TotalSides,
                            progress.TotalSides == 0 ? 1 : (float)progress.CompletedSides / progress.TotalSides);
                    ReportSolverUpdate(new SolverUpdate(progress.CompletedSides, Math.Max(1, progress.TotalSides))
                    {
                        Progress = progress.TotalSides == 0 ? 1 : (float)progress.CompletedSides / progress.TotalSides,
                        Message = $"Profile likelihood: endpoints found {progress.EndpointsFound}/{progress.TotalSides}; attempted solver calls={progress.AttemptedSolverCalls}"
                    });
                });
            if (Model.Solution == null) return;
            Model.Solution.ProfileLikelihoodRun = run;
            Model.Solution.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
            Model.Solution.UseWeightedFitting = UseErrorWeightedFitting;
            Model.Solution.SetBootstrapSolutions(new List<SolutionInterface>());
            if (run.CanUseIntervals)
                foreach (var result in run.Coordinates.Where(r => r.HasCompleteInterval))
                    Model.Solution.Parameters[result.Id.Parameter] = result.ToFloatWithError();
            Model.Solution.Convergence.ApplyErrorEstimationResult(
                ErrorEstimationMethod.ProfileLikelihood,
                run.Coordinates.Count(r => !r.HasCompleteInterval),
                run.Coordinates.Count(r => r.HasCompleteInterval),
                run.Elapsed,
                run.Outcome == ErrorEstimationOutcome.Cancelled,
                run.P * 2);
            Model.Solution.Convergence.SetErrorEstimationOutcome(run.Outcome);
            var profileSummary = ProfileLikelihoodEstimator.Describe(run);
            Model.Solution.Convergence.AppendErrorEstimationSummary(profileSummary);
            ReportSolverUpdate(new SolverUpdate(run.Coordinates.Count * 2,
                Math.Max(1, run.P * 2))
            { Progress = 1, Message = profileSummary });
        }
    }

    public class GlobalSolver : SolverInterface
    {
        public GlobalModel Model { get; set; }
        public GlobalSolution Solution => Model.Solution;

        public override async void Analyze()
        {
            using var activity = await ProcessingActivity.BeginAsync();
            using var ownership = activity.Enter();
            deferCompletion = true;
            base.Analyze();
            ApplyErrorEstimationPolicy();

            SolverConvergence convergence = null;

            try
            {
                // Validate the complete global graph before entering the individual-fit
                // loop; otherwise a later member could fail after earlier members ran.
                ValidateInitialParameterLimits(Model);
                await Task.Run(() =>
                {
                    var comparisonCalculated = false;
                    if (Model.ShouldFitIndividually)
                    {
                        ReportSolverUpdate(new SolverUpdate(0, Model.Models.Count) { Message = "Fitting individually...", Progress = 0 });

                        var convergences = new List<SolverConvergence>();
                        var memberSolvers = new List<(SolverInterface Solver, Model Model, SolverConvergence Convergence)>();
                        var counter = 0;
                        var profileTotalSides = ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood
                            ? Model.Models.Sum(member => member.Parameters.GetFittedParameters().Length * 2)
                            : 0;
                        var profileCompletedOffset = 0;
                        var profileEndpointOffset = 0;
                        var profileAttemptedOffset = 0;

                        void ConfigureProfileProgress(SolverInterface solver, Model member)
                        {
                            if (ErrorEstimationMethod != ErrorEstimationMethod.ProfileLikelihood) return;
                            var completedOffset = profileCompletedOffset;
                            var endpointOffset = profileEndpointOffset;
                            var attemptedOffset = profileAttemptedOffset;
                            solver.ProfileProgressObserver = profile =>
                            {
                                var completed = completedOffset + profile.CompletedSides;
                                var endpoints = endpointOffset + profile.EndpointsFound;
                                var attempted = attemptedOffset + profile.AttemptedSolverCalls;
                                PublishProgress(FittingPhase.EstimatingUncertainty, profile.CompletedSides, profile.TotalSides,
                                    profileTotalSides == 0 ? 1 : (float)completed / profileTotalSides,
                                    Model.Models.IndexOf(member) + 1, member.Data.Name);
                                ReportSolverUpdate(new SolverUpdate(completed, Math.Max(1, profileTotalSides))
                                {
                                    Progress = profileTotalSides == 0 ? 1 : (float)completed / profileTotalSides,
                                    Message = $"Profile likelihood: endpoints found {endpoints}/{profileTotalSides}; attempted solver calls={attempted}"
                                });
                            };
                        }

                        void TrackProfileProgress(Model mdl)
                        {
                            if (ErrorEstimationMethod != ErrorEstimationMethod.ProfileLikelihood) return;
                            var run = mdl.Solution?.ProfileLikelihoodRun;
                            profileCompletedOffset += run?.Coordinates.Count * 2 ?? 0;
                            profileEndpointOffset += run?.Coordinates.Sum(coordinate =>
                                (coordinate.Lower.IsEndpointFound ? 1 : 0)
                                + (coordinate.Upper.IsEndpointFound ? 1 : 0)) ?? 0;
                            profileAttemptedOffset += run?.AttemptedSolverCalls ?? 0;
                        }

                        // Members are assessed before any uncertainty estimation, so members
                        // assessed as no binding can skip it.
                        var deferEstimation = SkipsNoBindingUncertainty;
                        foreach (var mdl in Model.Models)
                        {
                            // Detect user cancellation
                            if (TerminateAnalysisFlag.Up) throw new OptimizerStopException();

                            var solver = SolverInterface.Initialize(mdl);
                            var position = Model.Models.IndexOf(mdl) + 1;
                            solver.SetProgressOwner(this, position, mdl.Data.Name);
                            PublishProgress(FittingPhase.Fitting, memberPosition: position, memberName: mdl.Data.Name);
                            solver.ErrorEstimationMethod = ErrorEstimationMethod;
                            solver.BootstrapIterations = BootstrapIterations;
                            solver.SolverAlgorithm = SolverAlgorithm;
                            solver.MaxOptimizerIterations = MaxOptimizerIterations;
                            solver.SolverToleranceModifier = SolverToleranceModifier;
                            solver.UseErrorWeightedFitting = this.UseErrorWeightedFitting;
                            solver.Silent = true;
                            if (deferEstimation) solver.UncertaintyGate = _ => UncertaintyDecision.Defer;
                            else ConfigureProfileProgress(solver, mdl);

                            var con = solver.Solve();
                            solver.UncertaintyGate = null;
                            convergences.Add(con);
                            memberSolvers.Add((solver, mdl, con));
                            if (!deferEstimation) TrackProfileProgress(mdl);

                            counter++;

                            // Deferred fits are quick next to the uncertainty pass, which alone fills the bar.
                            if (!deferEstimation && ErrorEstimationMethod != ErrorEstimationMethod.ProfileLikelihood)
                                ReportSolverUpdate(new SolverUpdate(counter, Model.Models.Count) { Progress = (float)counter / Model.Models.Count });
                        }

                        if (deferEstimation)
                        {
                            Model.Solution = new GlobalSolution(this, Model.Models.Select(mdl => mdl.Solution).ToList(),
                                SolverConvergence.FromMultiExperimentAnalysis(convergences), reconstructBootstrap: false);
                            CalculateNullComparison();
                            comparisonCalculated = true;
                            var comparison = Model.Solution.NullComparison;
                            var independent = Model.Models.Count >= 2;

                            // Member refit progress is placed by the current step, so start at the first member.
                            counter = 0;
                            if (ErrorEstimationMethod != ErrorEstimationMethod.ProfileLikelihood)
                                ReportSolverUpdate(new SolverUpdate(counter, Model.Models.Count)
                                {
                                    Progress = 0,
                                    Message = "Estimating uncertainty...",
                                });
                            foreach (var (solver, mdl, con) in memberSolvers)
                            {
                                if (TerminateAnalysisFlag.Up) throw new OptimizerStopException();
                                if (con?.CanRunErrorEstimation == true)
                                {
                                    var manual = independent ? MemberAssessmentOverride(mdl) : ManualAssessmentOverride;
                                    if (IsAssessedNoBinding(mdl.Solution?.NullComparison, manual))
                                        con.MarkErrorEstimationSkippedForNoBinding();
                                    else
                                    {
                                        PublishProgress(FittingPhase.EstimatingUncertainty,
                                            memberPosition: Model.Models.IndexOf(mdl) + 1, memberName: mdl.Data.Name);
                                        ConfigureProfileProgress(solver, mdl);
                                        solver.EstimateUncertainty(con);
                                        TrackProfileProgress(mdl);
                                    }
                                }

                                counter++;
                                if (ErrorEstimationMethod != ErrorEstimationMethod.ProfileLikelihood)
                                    ReportSolverUpdate(new SolverUpdate(counter, Model.Models.Count)
                                    {
                                        Progress = (float)counter / Model.Models.Count,
                                        Message = "Estimating uncertainty...",
                                    });
                            }

                            convergence = SolverConvergence.FromMultiExperimentAnalysis(convergences);
                            if (!convergence.Failed && !convergence.Stopped)
                                convergence.SetResidualStatistics(Model.ResidualStatistics());
                            Model.Solution = new GlobalSolution(this, Model.Models.Select(mdl => mdl.Solution).ToList(), convergence);
                            Model.Solution.NullComparison = comparison;
                        }
                        else
                        {
                            convergence = SolverConvergence.FromMultiExperimentAnalysis(convergences);
                            if (!convergence.Failed && !convergence.Stopped)
                                convergence.SetResidualStatistics(Model.ResidualStatistics());

                            Model.Solution = new GlobalSolution(this, Model.Models.Select(mdl => mdl.Solution).ToList(), convergence);
                        }

                        if (ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood && Model.Solution != null
                            && convergence.ErrorEstimationOutcome != ErrorEstimationOutcome.SkippedNoBinding)
                        {
                            var profileSummary = ProfileLikelihoodEstimator.Summarize(Model.Solution);
                            convergence.SetErrorEstimationOutcome(profileSummary.Outcome);
                            convergence.AppendErrorEstimationSummary(profileSummary.Diagnostics);
                            PublishProgress(FittingPhase.EstimatingUncertainty, fraction: 1);
                            ReportSolverUpdate(new SolverUpdate(profileSummary.TotalSides, Math.Max(1, profileSummary.TotalSides))
                            {
                                Progress = 1,
                                Message = profileSummary.Diagnostics,
                            });
                        }
                    }
                    else // Fit globally
                    {
                        if (SkipsNoBindingUncertainty)
                            UncertaintyGate = _ =>
                            {
                                if (Model?.Solution == null) return UncertaintyDecision.Estimate;
                                CalculateNullComparison();
                                comparisonCalculated = true;
                                return IsAssessedNoBinding(Model.Solution.NullComparison, ManualAssessmentOverride)
                                    ? UncertaintyDecision.Skip : UncertaintyDecision.Estimate;
                            };
                        try { convergence = Solve(); }
                        finally { UncertaintyGate = null; }
                    }

                    if (!comparisonCalculated && convergence?.Stopped != true && Model?.Solution != null)
                        CalculateNullComparison();

                    ReportAnalysisFinished(convergence);
                });

                if (AppSettings.CreateGlobalAnalysisResult
                    && ShouldCreateAnalysisResult(convergence)
                    && Model?.Solution != null)
                {
                    var result = new AnalysisResult(Model.Solution);
                    DataManager.AddData(result);
                }
            }
            catch (Exception ex)
            {
                // Build a convergence from the exception. Aggregate exceptions and cancellation
                // are unwrapped inside FromException().
                var conv = SolverConvergence.FromException(ex, starttime);
                // Only log and alert on genuine failures. User cancellations are considered
                // non-error conditions.
                if (!conv.Stopped)
                {
                    try
                    {
                        Model.Solution = new GlobalSolution(this, conv);
                        NullModelComparisonCalculator.Calculate(Model.Solution, UseErrorWeightedFitting,
                            SolverAlgorithm, MaxOptimizerIterations, SolverToleranceModifier);
                    }
                    catch (Exception comparisonException)
                    {
                        if (Model?.Solution != null)
                            Model.Solution.NullComparison = NullModelComparisonCalculator.Failure(Model.Solution, comparisonException);
                        AppEventHandler.AddLog(comparisonException);
                    }
                    AppEventHandler.DisplayHandledException(conv.RootCause ?? ex);
                }

                ReportAnalysisFinished(conv);
            }
            finally { PublishCompletion(); }
        }

        /// <summary>Manual member assessments by experiment ID, applied when deciding to skip (Update Result).</summary>
        internal IReadOnlyDictionary<string, BindingAssessmentOutcome> ManualMemberAssessmentOverrides { get; set; }

        BindingAssessmentOutcome? MemberAssessmentOverride(Model member) =>
            member?.Data?.UniqueID != null && ManualMemberAssessmentOverrides != null
                && ManualMemberAssessmentOverrides.TryGetValue(member.Data.UniqueID, out var outcome)
                ? outcome : (BindingAssessmentOutcome?)null;

        void CalculateNullComparison()
        {
            PublishProgress(FittingPhase.AssessingBinding);
            try { NullModelComparisonCalculator.Calculate(Model.Solution, UseErrorWeightedFitting,
                SolverAlgorithm, MaxOptimizerIterations, SolverToleranceModifier); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Model.Solution.NullComparison = NullModelComparisonCalculator.Failure(Model.Solution, ex);
                AppEventHandler.AddLog(ex);
            }
        }

        protected override SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            var initialGuess = Model.Parameters.GetFittedParameterArray();
            var initialObjective = PrepareCandidateEvaluations(
                Model, initialGuess, UseErrorWeightedFitting, Model.GetNumberOfPoints());
            var f = new NonlinearObjectiveFunction(
                Model.NumberOfParameters,
                w => EvaluateCandidate(Model, w, UseErrorWeightedFitting));
            var solver = new NelderMead(f);

            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(Model.NumberOfParameters)
            {
                MaximumEvaluations = MaxOptimizerIterations,
                AbsoluteFunctionTolerance = NMFunctionTolerance(initialObjective),
                RelativeParameterTolerance = RelativeParameterTolerance,
                StartTime = DateTime.Now,
            };

            var fittedParameters = Model.Parameters.GetFittedParameters();
            var optimizerInitialGuess = ParameterStartNormalizer.Inward(fittedParameters, initialGuess, out var usedInward);
            var stepSizes = Model.Parameters.GetStepSizes();
            var bounds = Model.Parameters.GetLimits();
            SetStepSizes(solver, stepSizes);
            SetBounds(solver, bounds);
            SetCancellationToken(solver);

            LogSolverInput("Global/NelderMead", fittedParameters, optimizerInitialGuess, stepSizes, bounds);
            solver.Minimize(optimizerInitialGuess);
            LogRejectedTrialEvaluations("Global/NelderMead");

            var metrics = ApplyBestFittedParameters(Model, initialGuess, solver.Solution, UseErrorWeightedFitting, "Global/NelderMead", fittedParameters, usedInward);

            var convergence = new SolverConvergence(solver, metrics.UnweightedRmsd);
            convergence.SetObjective(metrics.Objective);
            ValidateFinalPredictions(convergence, Model.Models);
            if (convergence.Failed || convergence.Stopped)
                convergence.SetObjective(null);
            ApplyBoundaryContacts(convergence);
            Model.Solution = new GlobalSolution(this, convergence);

            return Model.Solution.Convergence;
        }

        protected override SolverConvergence SolverWithLevenbergMarquardtAlgorithm2()
        {
            DateTime start = DateTime.Now;

            var fittedParameters = Model.Parameters.GetFittedParameters();
            var limits = Model.Parameters.GetLimits();
            int m = Model.GetNumberOfPoints();
            double[] initialGuess = Model.Parameters.GetFittedParameterArray();
            double[] optimizerInitialGuess = GetLevenbergMarquardtInitialGuess(fittedParameters, initialGuess, out var usedInward);
            PrepareCandidateEvaluations(
                Model, initialGuess, UseErrorWeightedFitting, m);

            var observedX = Vector<double>.Build.Dense(m, i => (double)i); // dummy x
            var observedY = Vector<double>.Build.Dense(m, 0.0);            // fit residuals to zero

            IObjectiveModel objective = ObjectiveFunction.NonlinearModel(
                (Vector<double> p, Vector<double> x) =>
                {
                    var r = EvaluateCandidateResiduals(
                        Model, p.ToArray(), UseErrorWeightedFitting);
                    return Vector<double>.Build.DenseOfArray(r);
                },
                observedX,
                observedY
            );

            var minimizer = new LevenbergMarquardtMinimizer(
                gradientTolerance: this.LevenbergMarquardtGradientTolerance,
                functionTolerance: this.LevenbergMarquardtEpsilon,
                stepTolerance: this.LevenbergMarquardtStepTolerance,
                maximumIterations: MaxOptimizerIterations);

            double[] lower = limits.Select(b => b[0]).ToArray();
            double[] upper = limits.Select(b => b[1]).ToArray();
            double[] scales = optimizerInitialGuess.Select(g => Math.Max(1, Math.Abs(g))).ToArray();
            LogSolverInput("Global/LevenbergMarquardt", fittedParameters, optimizerInitialGuess, scales, limits);

            var result = minimizer.FindMinimum(
                objective,
                optimizerInitialGuess,
                lower,
                upper,
                scales);
            LogRejectedTrialEvaluations("Global/LevenbergMarquardt");

            //LmResult = result;

            var fitted = result.MinimizingPoint.ToArray();

            var metrics = ApplyBestFittedParameters(Model, initialGuess, fitted, UseErrorWeightedFitting, "Global/LevenbergMarquardt", fittedParameters, usedInward);

            var convergence = new SolverConvergence(result, DateTime.Now - start, metrics.UnweightedRmsd);
            convergence.SetObjective(metrics.Objective);
            ValidateFinalPredictions(convergence, Model.Models);
            if (convergence.Failed || convergence.Stopped)
                convergence.SetObjective(null);
            ApplyBoundaryContacts(convergence);
            Model.Solution = new GlobalSolution(this, convergence);

            return Model.Solution.Convergence;
        }

        protected override void BoostrapResiduals()
        {
            base.BoostrapResiduals();

            var solutionsByReplicate = new GlobalSolution[BootstrapIterations];
            var randomStreams = BootstrapRandomStreams.Create(BootstrapIterations);
            int counter = 0;
            int success = 0;
            int failure = 0;
            int limitTerminated = 0;
            var start = DateTime.Now;
            var opt = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, AppSettings.MaxDegreeOfParallelism),
            };

            Parallel.For(0, BootstrapIterations, opt, (i) =>
            {
                if (TerminateAnalysisFlag.Down)
                {
                    try
                    {
                        var globalmodel = Model.GenerateSyntheticModel(randomStreams[i]);
                        var solver = new GlobalSolver
                        {
                            Model = globalmodel,
                            SolverAlgorithm = SolverAlgorithm,
                            SolverToleranceModifier = ErrorEstimationToleranceModifier,
                            MaxOptimizerIterations = MaxBootstrapOptimizerIterations,
                            UseErrorWeightedFitting = this.UseErrorWeightedFitting,
                            EnableSolverDiagnostics = false,
                            Silent = true,
                        };

                        var rconv = solver.Solve();
                        // Only converged refits contribute to the bootstrap distribution.
                        // Limit-terminated best-so-far points are counted separately and
                        // reported as excluded warnings.
                        if (rconv?.IsUsableForErrorEstimation == true)
                        {
                            var solution = new GlobalSolution(solver, rconv);
                            foreach (var member in solution.Solutions)
                                member.BootstrapReplicateIndex = i;
                            solutionsByReplicate[i] = solution;
                            Interlocked.Increment(ref success);
                        }
                        else
                        {
                            if (rconv?.MaxIterationsReached == true)
                                Interlocked.Increment(ref limitTerminated);
                            Interlocked.Increment(ref failure);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Any exception during a replicate counts as a failure and is not
                        // propagated.
                        Interlocked.Increment(ref failure);
                        AppEventHandler.Print($"Bootstrap Error: {ex.Message}");
                    }
                }

                var currcounter = Interlocked.Increment(ref counter);
                ReportBootstrapProgress(currcounter);
            });

            var solutions = solutionsByReplicate.Where(solution => solution != null).ToList();

            Solution.SetBootstrapSolutions(solutions);
            failure += Math.Max(0, success - Solution.BootstrapSolutions.Count);
            success = Solution.BootstrapSolutions.Count;
            Solution.Convergence.ApplyErrorEstimationResult(
                ErrorEstimationMethod,
                failure,
                success,
                DateTime.Now - start,
                TerminateAnalysisFlag.Up,
                BootstrapIterations,
                limitTerminated);
        }

        protected override void LeaveOneOut()
        {
            base.LeaveOneOut();

            var solutionsByOmission = new GlobalSolution[Model.Models.Count];
            int counter = 0;
            int success = 0;
            int failure = 0;
            int limitTerminated = 0;
            var start = DateTime.Now;
            var opt = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, AppSettings.MaxDegreeOfParallelism),
            };

            Parallel.For(0, Model.Models.Count, opt, (i) =>
            {
                if (TerminateAnalysisFlag.Down)
                {
                    try
                    {
                        var globalmodel = Model.LeaveOneOut(i);
                        var solver = new GlobalSolver
                        {
                            Model = globalmodel,
                            SolverAlgorithm = SolverAlgorithm,
                            SolverToleranceModifier = ErrorEstimationToleranceModifier,
                            MaxOptimizerIterations = MaxBootstrapOptimizerIterations,
                            UseErrorWeightedFitting = this.UseErrorWeightedFitting,
                            EnableSolverDiagnostics = false,
                            Silent = true,
                        };

                        var rconv = solver.Solve();
                        if (rconv?.IsUsableForErrorEstimation == true)
                        {
                            var solution = new GlobalSolution(solver, rconv);
                            foreach (var member in solution.Solutions)
                                member.BootstrapReplicateIndex = i;
                            solutionsByOmission[i] = solution;
                            Interlocked.Increment(ref success);
                        }
                        else
                        {
                            if (rconv?.MaxIterationsReached == true)
                                Interlocked.Increment(ref limitTerminated);
                            Interlocked.Increment(ref failure);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Any exception during a replicate counts as a failure.
                        Interlocked.Increment(ref failure);
                        AppEventHandler.Print($"Bootstrap Error: {ex.Message}");
                    }
                }

                var currcounter = Interlocked.Increment(ref counter);
                ReportLeaveOneOutProgress(currcounter, Model.Models.Count);
            });

            var solutions = solutionsByOmission.Where(solution => solution != null).ToList();

            Solution.SetBootstrapSolutions(solutions);
            failure += Math.Max(0, success - Solution.BootstrapSolutions.Count);
            success = Solution.BootstrapSolutions.Count;
            Solution.Convergence.ApplyErrorEstimationResult(
                ErrorEstimationMethod,
                failure,
                success,
                DateTime.Now - start,
                TerminateAnalysisFlag.Up,
                Model.Models.Count,
                limitTerminated);
        }

        protected override void ProfileLikelihood()
        {
            // Independent global analyses already profile each member in the
            // member Solver instances. Shared coordinates require the complete
            // global objective and are profiled together here.
            if (Model.ShouldFitIndividually) return;
            var run = ProfileLikelihoodEstimator.Run(Model, SolverAlgorithm, UseErrorWeightedFitting,
                MaxBootstrapOptimizerIterations, ErrorEstimationToleranceModifier,
                progress: progress =>
                {
                    PublishProgress(FittingPhase.EstimatingUncertainty, progress.CompletedSides, progress.TotalSides,
                        progress.TotalSides == 0 ? 1 : (float)progress.CompletedSides / progress.TotalSides);
                    ReportSolverUpdate(new SolverUpdate(progress.CompletedSides, Math.Max(1, progress.TotalSides))
                    {
                        Progress = progress.TotalSides == 0 ? 1 : (float)progress.CompletedSides / progress.TotalSides,
                        Message = $"Profile likelihood: endpoints found {progress.EndpointsFound}/{progress.TotalSides}; attempted solver calls={progress.AttemptedSolverCalls}"
                    });
                });
            if (Model.Solution == null) return;
            Model.Solution.ProfileLikelihoodRun = run;
            foreach (var member in Model.Models)
            {
                if (member.Solution != null)
                {
                    member.Solution.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
                    member.Solution.UseWeightedFitting = UseErrorWeightedFitting;
                }
                member.Solution?.SetBootstrapSolutions(new List<SolutionInterface>());
            }
            if (run.CanUseIntervals)
            foreach (var result in run.Coordinates.Where(r => r.HasCompleteInterval))
            {
                var fwe = result.ToFloatWithError();
                if (result.Id.Scope == ParameterBoundaryScope.Shared)
                {
                    if (ThermodynamicParameterSlots.TryResolve(result.Id.Parameter, out var slot, out var family)
                        && family == ThermodynamicParameterFamily.Gibbs)
                    {
                        if (Model.Parameters.GetConstraintForParameter(slot.Affinity)
                            == VariableConstraint.ThermodynamicallyLinked)
                            continue;

                        foreach (var member in Model.Models)
                        {
                            if (!member.Parameters.Table.ContainsKey(slot.Affinity) || member.Solution == null) continue;
                            var temperature = member.Data.MeasuredTemperatureKelvin;
                            member.Solution.Parameters[slot.Affinity] = result.Transform(g => GlobalConstraintSemantics.Log10AffinityFromGibbs(g, temperature));
                        }
                    }
                    else if (!(ThermodynamicParameterSlots.TryResolve(result.Id.Parameter, out slot, out family)
                        && family == ThermodynamicParameterFamily.HeatCapacity
                        && Model.Parameters.GetConstraintForParameter(slot.Enthalpy) == VariableConstraint.TemperatureDependent)
                        && !(ThermodynamicParameterSlots.TryResolve(result.Id.Parameter, out slot, out family)
                        && family == ThermodynamicParameterFamily.Enthalpy
                        && Model.Parameters.GetConstraintForParameter(slot.Enthalpy) == VariableConstraint.TemperatureDependent))
                    {
                        foreach (var member in Model.Models)
                            if (member.Parameters.Table.ContainsKey(result.Id.Parameter) && member.Solution != null)
                                member.Solution.Parameters[result.Id.Parameter] = fwe;
                    }
                }
                else
                {
                    var member = Model.Models.FirstOrDefault(m => m.Data.UniqueID == result.Id.ExperimentIdentity);
                    if (member?.Solution != null) member.Solution.Parameters[result.Id.Parameter] = fwe;
                }
            }
            Model.Solution.ApplyProfileTemperatureCoordinates(run);
            Model.Solution.Convergence.ApplyErrorEstimationResult(
                ErrorEstimationMethod.ProfileLikelihood,
                run.Coordinates.Count(r => !r.HasCompleteInterval),
                run.Coordinates.Count(r => r.HasCompleteInterval),
                run.Elapsed,
                run.Outcome == ErrorEstimationOutcome.Cancelled,
                run.P * 2);
            Model.Solution.Convergence.SetErrorEstimationOutcome(run.Outcome);
            var profileSummary = ProfileLikelihoodEstimator.Describe(run);
            Model.Solution.Convergence.AppendErrorEstimationSummary(profileSummary);
            ReportSolverUpdate(new SolverUpdate(run.Coordinates.Count * 2,
                Math.Max(1, run.P * 2))
            { Progress = 1, Message = profileSummary });
        }
    }
}
