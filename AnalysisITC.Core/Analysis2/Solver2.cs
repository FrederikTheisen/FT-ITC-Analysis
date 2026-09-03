using System;
using System.Linq;
using Accord.Math.Optimization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
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
        public static bool EnableAutoConcentrationVariance { get; set; } = false;
        public static double AutoConcentrationVariance { get; set; } = 0.05;
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
            AutoConcentrationVariance = AppSettings.ConcentrationAutoVariance;
            EnableAutoConcentrationVariance = AppSettings.IsConcentrationAutoVarianceEnabled;
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
        public static event EventHandler<Tuple<int, int, float>> ErrorEstimationIterationCompleted;
        public static event EventHandler<SolverConvergence> AnalysisFinished;
        public static event EventHandler AnalysisStepFinished;
        public static event EventHandler<SolverUpdate> SolverUpdated;

        public SolverAlgorithm SolverAlgorithm { get; set; } = SolverAlgorithm.NelderMead;
        public int MaxOptimizerIterations { get; set; } = AppSettings.MaximumOptimizerIterations;
        public bool UseErrorWeightedFitting { get; set; } = false;
        public bool CanCreateAnalysisResult { get; set; } = true;
        public bool CanReportAnalysisStepFinished { get; set; } = true;
        public bool EnableSolverDiagnostics { get; set; } = true;

        public ErrorEstimationMethod ErrorEstimationMethod { get; set; } = ErrorEstimationMethod.None;
        public int BootstrapIterations { get; set; } = 100;
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
            return Math.Max(1E-30, guessloss * Tolerance(5, 10)); // 1E-4 - 1E-8
        }

        // LM parameters
        public double LevenbergMarquardtDifferentiationStepSize => Tolerance(17, 22);  
        public double LevenbergMarquardtEpsilon => Tolerance(15, 22);
        public double LevenbergMarquardtGradientTolerance => Tolerance(19, 24);
        public double LevenbergMarquardtStepTolerance => Tolerance(18, 23);

        protected double[] GetLevenbergMarquardtInitialGuess(IReadOnlyList<Parameter> parameters)
        {
            const double zeroValueStepFraction = 1e-3;

            // Give Math.NET a scale-aware nonzero point from which to estimate the numerical derivative.
            return parameters
                .Select(parameter => parameter.Value == 0
                    ? parameter.StepSize * zeroValueStepFraction
                    : parameter.Value)
                .ToArray();
        }

        CancellationTokenSource nelderMeadToken;
        internal NonlinearMinimizationResult LmResult { get; set; }

        protected DateTime starttime;
        protected DateTime endtime;
        protected IReadOnlyList<ParameterBoundaryContact> LastParameterBoundaryContacts { get; private set; } = Array.Empty<ParameterBoundaryContact>();
        double invalidCandidateObjective;
        double[] invalidCandidateResiduals = Array.Empty<double>();
        int rejectedTrialEvaluationCount;

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

            InitialParameterLimitViolationDetector.ThrowIfAny(
                InitialParameterLimitViolationDetector.Detect(model));
        }

        public void ReportBootstrapProgress(int iteration) => PlatformServices.MainThreadDispatcher.Invoke(() =>
        {
            if (!Silent) ErrorEstimationIterationCompleted?.Invoke(null, new Tuple<int, int, float>(iteration, BootstrapIterations, iteration / (float)BootstrapIterations));
            else SolverUpdated?.Invoke(null, SolverUpdate.BackgroundBootstrapUpdate(iteration, BootstrapIterations));
        });

        public void ReportLeaveOneOutProgress(int iteration, int models) => PlatformServices.MainThreadDispatcher.Invoke(() =>
        {
            if (!Silent) ErrorEstimationIterationCompleted?.Invoke(null, new Tuple<int, int, float>(iteration, models, iteration / (float)models));
            else SolverUpdated?.Invoke(null, SolverUpdate.BackgroundBootstrapUpdate(iteration, models));
        });

        public void ReportAnalysisStepFinished()
        {
            if (Silent || !CanReportAnalysisStepFinished) return;

            PlatformServices.MainThreadDispatcher.Invoke(() =>
            {
                AnalysisStepFinished?.Invoke(null, null);
            });
        }

        public void ReportAnalysisFinished(SolverConvergence convergence) => PlatformServices.MainThreadDispatcher.Invoke(() =>
        {
            if (!Silent) AnalysisFinished?.Invoke(this, convergence);
        });

        public void ReportSolverUpdate(SolverUpdate update) => PlatformServices.MainThreadDispatcher.Invoke(() =>
        {
            if (!Silent) SolverUpdated?.Invoke(null, update);
        });

        public virtual void Analyze()
        {
            starttime = DateTime.Now;
            TerminateAnalysisFlag.Lower();
            AnalysisStarted?.Invoke(this, TerminateAnalysisFlag);

            StatusBarManager.StartInderminateProgress();

            string mdl = this switch
            {
                Solver => (this as Solver).Model.ModelType.ToString(),
                GlobalSolver => "Global." + (this as GlobalSolver).Model.ModelType.ToString(),
                _ => "",
            };
            StatusBarManager.SetStatus("Fitting " + mdl + " using " + SolverAlgorithm.GetProperties().ShortName + "...", 0, priority: 1);
        }

        private void TerminateAnalysisFlag_WasRaised(object sender, EventArgs e)
        {
            StatusBarManager.SetStatus("Terminating analysis...", 0, 3);

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
            ApplyErrorEstimationPolicy();

            switch (this)
            {
                case Solver local:
                    ValidateInitialParameterLimits(local.Model);
                    break;
                case GlobalSolver global:
                    ValidateInitialParameterLimits(global.Model);
                    break;
            }

            // Subscribe to the termination flag so we can stop the underlying solver when requested.
            TerminateAnalysisFlag.WasRaised += TerminateAnalysisFlag_WasRaised;
            try
            {
                SolverConvergence convergence;

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

                ReportAnalysisStepFinished();

                if (convergence?.CanRunErrorEstimation == true)
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
                    EnableAutoConcentrationVariance = options?.EnableAutoConcentrationVariance ?? false,
                    AutoConcentrationVariance = options?.AutoConcentrationVariance ?? 0.05,
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

        private static bool IsMeaningfullyWorse(double candidateObjective, double baselineObjective)
        {
            if (!FWEMath.IsFinite(baselineObjective)) return false;
            if (!FWEMath.IsFinite(candidateObjective)) return true;

            var tolerance = Math.Max(1e-12, Math.Abs(baselineObjective) * 1e-10);
            return candidateObjective > baselineObjective + tolerance;
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

        protected double ApplyBestFittedParameters(Model model, double[] initial, double[] fitted, bool errorWeighted, string scope, IReadOnlyList<Parameter> parameters)
        {
            if (!model.TryLossFunction(initial, errorWeighted, out var initialObjective)
                || !model.HasFiniteIncludedPredictions())
                throw new ArithmeticException("The initial model parameters no longer produce finite predictions.");
            var initialLoss = model.Loss();

            var fittedIsValid = model.TryLossFunction(fitted, errorWeighted, out var fittedObjective)
                                && model.HasFiniteIncludedPredictions();
            var fittedLoss = fittedIsValid ? model.Loss() : double.NaN;
            if (!fittedIsValid) fittedObjective = invalidCandidateObjective;
            var acceptedFitted = fittedIsValid
                                 && !IsMeaningfullyWorse(fittedObjective, initialObjective);

            LogSolverOutput(scope, parameters, initial, fitted, initialObjective, fittedObjective, initialLoss, fittedLoss, acceptedFitted);

            if (acceptedFitted)
            {
                LastParameterBoundaryContacts = ParameterBoundaryDetector.Detect(model, ParameterBoundaryScope.Local);
                return fittedLoss;
            }

            if (!model.TryLossFunction(initial, false, out _)
                || !model.HasFiniteIncludedPredictions())
                throw new ArithmeticException("The initial model parameters could not be restored.");
            LastParameterBoundaryContacts = ParameterBoundaryDetector.Detect(model, ParameterBoundaryScope.Local);
            return initialLoss;
        }

        protected double ApplyBestFittedParameters(GlobalModel model, double[] initial, double[] fitted, bool errorWeighted, string scope, IReadOnlyList<Parameter> parameters)
        {
            if (!model.TryLossFunction(initial, errorWeighted, out var initialObjective)
                || model.Models.Any(member => !member.HasFiniteIncludedPredictions()))
                throw new ArithmeticException("The initial global model parameters no longer produce finite predictions.");
            var initialLoss = model.Loss();

            var fittedIsValid = model.TryLossFunction(fitted, errorWeighted, out var fittedObjective)
                                && model.Models.All(member => member.HasFiniteIncludedPredictions());
            var fittedLoss = fittedIsValid ? model.Loss() : double.NaN;
            if (!fittedIsValid) fittedObjective = invalidCandidateObjective;
            var acceptedFitted = fittedIsValid
                                 && !IsMeaningfullyWorse(fittedObjective, initialObjective);

            LogSolverOutput(scope, parameters, initial, fitted, initialObjective, fittedObjective, initialLoss, fittedLoss, acceptedFitted);

            if (acceptedFitted)
            {
                LastParameterBoundaryContacts = ParameterBoundaryDetector.Detect(model);
                return fittedLoss;
            }

            if (!model.TryLossFunction(initial, false, out _)
                || model.Models.Any(member => !member.HasFiniteIncludedPredictions()))
                throw new ArithmeticException("The initial global model parameters could not be restored.");
            LastParameterBoundaryContacts = ParameterBoundaryDetector.Detect(model);
            return initialLoss;
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
            // Prepare analysis state and notify listeners.
            base.Analyze();

            SolverConvergence convergence = null;

            try
            {
                // Run the solve operation on a background thread. Any exceptions thrown during solving will
                // propagate to the catch blocks below.
                await Task.Run(() =>
                {
                    convergence = Solve();
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
                conv.SetLoss(Model.Loss());

                // Log and notify the user only for genuine failures; user cancellations are
                // considered non-error conditions.
                if (!conv.Stopped)
                {
                    AppEventHandler.DisplayHandledException(conv.RootCause ?? ex);
                }

                ReportAnalysisFinished(conv);
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
            var stepSizes = Model.Parameters.GetStepSizes();
            var bounds = Model.Parameters.GetLimits();
            SetStepSizes(solver, stepSizes);
            SetBounds(solver, bounds);
            // Allow the solver to be cancelled via the TerminateAnalysisFlag by associating it with a CancellationToken.
            SetCancellationToken(solver);

            LogSolverInput("Single/NelderMead", fittedParameters, initialGuess, stepSizes, bounds);
            solver.Minimize(initialGuess);
            LogRejectedTrialEvaluations("Single/NelderMead");

            var loss = ApplyBestFittedParameters(Model, initialGuess, solver.Solution, UseErrorWeightedFitting, "Single/NelderMead", fittedParameters);

            var convergence = new SolverConvergence(solver, loss);
            ValidateFinalPredictions(convergence, new[] { Model });
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
            double[] optimizerInitialGuess = GetLevenbergMarquardtInitialGuess(fittedParameters);
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
            var loss = ApplyBestFittedParameters(Model, initialGuess, fitted, UseErrorWeightedFitting, "Single/LevenbergMarquardt", fittedParameters);

            var convergence = new SolverConvergence(result, DateTime.Now - start, loss);
            ValidateFinalPredictions(convergence, new[] { Model });
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

            Parallel.For(0, models.Length, (i) =>
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
            if (run.Outcome != ErrorEstimationOutcome.CompleteFailure)
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
                    if (Model.ShouldFitIndividually)
                    {
                        ReportSolverUpdate(new SolverUpdate(0, Model.Models.Count) { Message = "Fitting individually...", Progress = 0 });

                        var convergences = new List<SolverConvergence>();
                        var counter = 0;
                        var profileTotalSides = ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood
                            ? Model.Models.Sum(member => member.Parameters.GetFittedParameters().Length * 2)
                            : 0;
                        var profileCompletedOffset = 0;
                        var profileEndpointOffset = 0;
                        var profileAttemptedOffset = 0;
                        foreach (var mdl in Model.Models)
                        {
                            // Detect user cancellation
                            if (TerminateAnalysisFlag.Up) throw new OptimizerStopException();

                            var solver = SolverInterface.Initialize(mdl);
                            solver.ErrorEstimationMethod = ErrorEstimationMethod;
                            solver.BootstrapIterations = BootstrapIterations;
                            solver.SolverAlgorithm = SolverAlgorithm;
                            solver.UseErrorWeightedFitting = this.UseErrorWeightedFitting;
                            solver.Silent = true;
                            if (ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood)
                            {
                                var completedOffset = profileCompletedOffset;
                                var endpointOffset = profileEndpointOffset;
                                var attemptedOffset = profileAttemptedOffset;
                                solver.ProfileProgressObserver = profile =>
                                {
                                    var completed = completedOffset + profile.CompletedSides;
                                    var endpoints = endpointOffset + profile.EndpointsFound;
                                    var attempted = attemptedOffset + profile.AttemptedSolverCalls;
                                    ReportSolverUpdate(new SolverUpdate(completed, Math.Max(1, profileTotalSides))
                                    {
                                        Progress = profileTotalSides == 0 ? 1 : (float)completed / profileTotalSides,
                                        Message = $"Profile likelihood: endpoints found {endpoints}/{profileTotalSides}; attempted solver calls={attempted}"
                                    });
                                };
                            }

                            var con = solver.Solve();
                            convergences.Add(con);

                            if (ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood)
                            {
                                var run = mdl.Solution?.ProfileLikelihoodRun;
                                profileCompletedOffset += run?.Coordinates.Count * 2 ?? 0;
                                profileEndpointOffset += run?.Coordinates.Sum(coordinate =>
                                    (coordinate.Lower.IsEndpointFound ? 1 : 0)
                                    + (coordinate.Upper.IsEndpointFound ? 1 : 0)) ?? 0;
                                profileAttemptedOffset += run?.AttemptedSolverCalls ?? 0;
                            }

                            counter++;

                            if (ErrorEstimationMethod != ErrorEstimationMethod.ProfileLikelihood)
                                ReportSolverUpdate(new SolverUpdate(counter, Model.Models.Count) { Progress = (float)counter / Model.Models.Count });
                        }

                        convergence = SolverConvergence.FromMultiExperimentAnalysis(convergences);
                        if (!convergence.Failed && !convergence.Stopped)
                            convergence.SetResidualStatistics(Model.ResidualStatistics());

                        Model.Solution = new GlobalSolution(this, Model.Models.Select(mdl => mdl.Solution).ToList(), convergence);
                        if (ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood && Model.Solution != null)
                        {
                            var profileSummary = ProfileLikelihoodEstimator.Summarize(Model.Solution);
                            convergence.SetErrorEstimationOutcome(profileSummary.Outcome);
                            convergence.AppendErrorEstimationSummary(profileSummary.Diagnostics);
                            ReportSolverUpdate(new SolverUpdate(profileSummary.TotalSides, Math.Max(1, profileSummary.TotalSides))
                            {
                                Progress = 1,
                                Message = profileSummary.Diagnostics,
                            });
                        }
                    }
                    else // Fit globally
                    {
                        convergence = Solve();
                    }

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
                    AppEventHandler.DisplayHandledException(conv.RootCause ?? ex);
                }

                ReportAnalysisFinished(conv);
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
            var stepSizes = Model.Parameters.GetStepSizes();
            var bounds = Model.Parameters.GetLimits();
            SetStepSizes(solver, stepSizes);
            SetBounds(solver, bounds);
            SetCancellationToken(solver);

            LogSolverInput("Global/NelderMead", fittedParameters, initialGuess, stepSizes, bounds);
            solver.Minimize(initialGuess);
            LogRejectedTrialEvaluations("Global/NelderMead");

            var loss = ApplyBestFittedParameters(Model, initialGuess, solver.Solution, UseErrorWeightedFitting, "Global/NelderMead", fittedParameters);

            var convergence = new SolverConvergence(solver, loss);
            ValidateFinalPredictions(convergence, Model.Models);
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
            double[] optimizerInitialGuess = GetLevenbergMarquardtInitialGuess(fittedParameters);
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

            var loss = ApplyBestFittedParameters(Model, initialGuess, fitted, UseErrorWeightedFitting, "Global/LevenbergMarquardt", fittedParameters);

            var convergence = new SolverConvergence(result, DateTime.Now - start, loss);
            ValidateFinalPredictions(convergence, Model.Models);
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
            var opt = new ParallelOptions();
            opt.MaxDegreeOfParallelism = AppSettings.MaxDegreeOfParallelism;

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
            var opt = new ParallelOptions();
            opt.MaxDegreeOfParallelism = 10;

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
                progress: progress => ReportSolverUpdate(new SolverUpdate(progress.CompletedSides, Math.Max(1, progress.TotalSides))
                {
                    Progress = progress.TotalSides == 0 ? 1 : (float)progress.CompletedSides / progress.TotalSides,
                    Message = $"Profile likelihood: endpoints found {progress.EndpointsFound}/{progress.TotalSides}; attempted solver calls={progress.AttemptedSolverCalls}"
                }));
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
            if (run.Outcome != ErrorEstimationOutcome.CompleteFailure)
            foreach (var result in run.Coordinates.Where(r => r.HasCompleteInterval))
            {
                var fwe = result.ToFloatWithError();
                if (result.Id.Scope == ParameterBoundaryScope.Shared)
                {
                    if (ThermodynamicParameterSlots.TryResolve(result.Id.Parameter, out var slot, out var family)
                        && family == ThermodynamicParameterFamily.Gibbs)
                    {
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
