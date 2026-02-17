using System;
using System.Linq;
using Accord.Math.Optimization;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using AppKit;
using Accord.Math;
using static alglib;
using Accord.IO;
using static AnalysisITC.Extensions;
using AnalysisITC.AppClasses.Analysis2.Models;

namespace AnalysisITC.AppClasses.Analysis2
{
    public static class FittingOptionsController
    {
        public static ErrorEstimationMethod ErrorEstimationMethod { get; set; } = ErrorEstimationMethod.BootstrapResiduals;
        public static int BootstrapIterations { get; set; } = 100;
        public static bool UnlockBootstrapParameters { get; set; } = false;
        public static bool IncludeConcentrationVariance { get; set; } = false;
        public static bool EnableAutoConcentrationVariance { get; set; } = false;
        public static double AutoConcentrationVariance { get; set; } = 0.05;
        public static SolverAlgorithm Algorithm { get; set; } = SolverAlgorithm.NelderMead;
        public static bool UseErrorWeightedFitting { get; set; } = false;
    }

    public class SolverInterface
    {
        public static TerminationFlag TerminateAnalysisFlag { get; protected set; } = new TerminationFlag();

        public bool Silent { get; set; } = false;

        public static event EventHandler<TerminationFlag> AnalysisStarted;
        public static event EventHandler<Tuple<int, int, float>> BootstrapIterationFinished;
        public static event EventHandler<SolverConvergence> AnalysisFinished;
        public static event EventHandler AnalysisStepFinished;
        public static event EventHandler<SolverUpdate> SolverUpdated;

        public SolverAlgorithm SolverAlgorithm { get; set; } = SolverAlgorithm.NelderMead;
        public int MaxOptimizerIterations { get; set; } = AppSettings.MaximumOptimizerIterations;
        public bool UseErrorWeightedFitting { get; set; } = false;

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
        // public double SolverFunctionTolerance { get; set; } = AppSettings.OptimizerTolerance;
        public double RelativeParameterTolerance => Tolerance(3, 10);

        protected double NMFunctionTolerance(double guessloss)
        {
            return Math.Max(1E-30, guessloss * Tolerance(3, 10)); // 1E-4 - 1E-8
        }

        // LM parameters
        public double LevenbergMarquardtDifferentiationStepSize => Tolerance(1, 8);   // 1E-2 - 1E-6
        public double LevenbergMarquardtEpsilon => Tolerance(4, 11);              // 1E-5 - 1E-9

        internal alglib.minlmstate LMOptimizerState { get; set; }
        public static CancellationTokenSource NelderMeadToken { get; set; }

        protected DateTime starttime;
        protected DateTime endtime;
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
            var solver = new Solver();
            solver.Model = model;

            return solver;
        }

        public static SolverInterface Initialize(GlobalModel model)
        {
            var solver = new GlobalSolver();
            solver.Model = model;

            return solver;
        }

        public void ReportBootstrapProgress(int iteration) => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            if (!Silent) BootstrapIterationFinished?.Invoke(null, new Tuple<int, int, float>(iteration, BootstrapIterations, iteration / (float)BootstrapIterations));
            else SolverUpdated?.Invoke(null, SolverUpdate.BackgroundBootstrapUpdate(iteration, BootstrapIterations));
        });

        public void ReportLeaveOneOutProgress(int iteration, int models) => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            if (!Silent) BootstrapIterationFinished?.Invoke(null, new Tuple<int, int, float>(iteration, models, iteration / (float)models));
            else SolverUpdated?.Invoke(null, SolverUpdate.BackgroundBootstrapUpdate(iteration, models));
        });

        public void ReportAnalysisStepFinished() => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            //if (!Silent)
            AnalysisStepFinished?.Invoke(null, null);
        });

        public void ReportAnalysisFinished(SolverConvergence convergence) => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            if (!Silent) AnalysisFinished?.Invoke(null, convergence);
        });

        public void ReportSolverUpdate(SolverUpdate update) => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            if (!Silent) SolverUpdated?.Invoke(null, update);
        });

        public virtual async void Analyze()
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

            // Copy references to avoid race conditions with solver finalisation in Solve(). If the
            // optimizer state or cancellation token has been disposed or reset concurrently, the
            // calls below will safely no-op. Each call is wrapped in its own try/catch to avoid
            // propagating disposal exceptions.
            var state = LMOptimizerState;
            var token = NelderMeadToken;

            try
            {
                if (state != null)
                {
                    // Suppress any errors thrown due to a disposed or invalid optimizer state.
                    try { alglib.minlmrequesttermination(state); }
                    catch { }
                }
                if (token != null)
                {
                    // Suppress any errors thrown when cancelling a disposed token.
                    try { token.Cancel(); }
                    catch { }
                }
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
            UseErrorWeightedFitting = FittingOptionsController.UseErrorWeightedFitting;

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
                        convergence = SolverWithLevenbergMarquardtAlgorithm();
                        break;
                    default:
                        throw new NotImplementedException("Solver algorithm not implemented");
                }

                ReportAnalysisStepFinished();

                switch (ErrorEstimationMethod)
                {
                    case ErrorEstimationMethod.BootstrapResiduals:
                        BoostrapResiduals();
                        break;
                    case ErrorEstimationMethod.LeaveOneOut:
                        LeaveOneOut();
                        break;
                    case ErrorEstimationMethod.None:
                    default:
                        break;
                }

                endtime = DateTime.Now;
                // Normalise the convergence status and message before returning. This call
                // infers termination conditions (e.g. user cancellation, iteration limits) from
                // the underlying solver messages and ensures the user-facing message and flags
                // are consistent across different algorithms.
                if (convergence != null)
                {
                    convergence.Normalize();
                }

                return convergence;
            }
            finally
            {
                TerminateAnalysisFlag.WasRaised -= TerminateAnalysisFlag_WasRaised;

                // Reset LM state so that a new LM run can be created fresh. If the solver is currently running
                // (e.g. cancelled mid-run), this will allow minlmcreatev to be called again on the next Solve.
                LMOptimizerState = null;

                // Dispose of any cancellation token source used by Nelder–Mead. Without disposing the token,
                // cancelling an NM solve leaves behind a cancelled token that will cause future solves to stop
                // immediately.
                if (NelderMeadToken != null)
                {
                    NelderMeadToken.Dispose();
                    NelderMeadToken = null;
                }
            }
        }

        protected virtual SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            throw new NotImplementedException();
        }

        protected virtual SolverConvergence SolverWithLevenbergMarquardtAlgorithm()
        {
            throw new NotImplementedException();
        }

        protected virtual void BoostrapResiduals()
        {
            ReportBootstrapProgress(0);
        }

        protected virtual void LeaveOneOut()
        {
            if (this is GlobalSolver)
            {
                ReportLeaveOneOutProgress(0, (this as GlobalSolver).Model.Models.Count);
            }
            else if (this is Solver)
            {
                ReportLeaveOneOutProgress(0, (this as Solver).Model.Data.Injections.Where(inj => inj.Include).Count());
            }
        }

        internal void SetStepSizes(NelderMead solver, double[] stepsize)
        {
            for (int i = 0; i < solver.StepSize.Length; i++) solver.StepSize[i] = stepsize[i];
        }

        internal void SetBounds(object solver, List<double[]> bounds)
        {
            var lower = bounds.Select(l => l[0]).ToArray();
            var upper = bounds.Select(l => l[1]).ToArray();

            switch (solver)
            {
                case NelderMead simplex:
                    for (int i = 0; i < simplex.NumberOfVariables; i++)
                    {
                        simplex.LowerBounds[i] = lower[i];
                        simplex.UpperBounds[i] = upper[i];
                    }
                    break;
                case alglib.minlmstate state:
                    alglib.minlmsetbc(state, lower, upper);
                    break;
            }
        }

        internal void SetCancellationToken(object solver)
        {
            switch (solver)
            {
                case NelderMead simplex:
                    NelderMeadToken = new CancellationTokenSource();
                    simplex.Token = NelderMeadToken.Token;
                    break;
                case alglib.minlmstate minlm: LMOptimizerState = minlm; break;
            }
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

            try
            {
                // Run the solve operation on a background thread. Any exceptions thrown during solving will
                // propagate to the catch blocks below.
                await Task.Run(() =>
                {
                    var convergence = Solve();
                    ReportAnalysisFinished(convergence);
                });
            }
            catch (Exception ex)
            {
                var conv = SolverConvergence.FromException(ex, starttime);
                conv.SetLoss(Model.Loss());
                conv.Normalize();

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
            var f = new NonlinearObjectiveFunction(Model.NumberOfParameters, (w) => Model.LossFunction(w, UseErrorWeightedFitting));
            var solver = new NelderMead(f);

            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(Model.NumberOfParameters)
            {
                MaximumEvaluations = MaxOptimizerIterations,
                AbsoluteFunctionTolerance = NMFunctionTolerance(Model.LossFunction(Model.Parameters.GetFittedParameterArray(), UseErrorWeightedFitting)),
                RelativeParameterTolerance = RelativeParameterTolerance,
                StartTime = DateTime.Now,
            };

            SetStepSizes(solver, Model.Parameters.GetStepSizes());
            SetBounds(solver, Model.Parameters.GetLimits());
            // Allow the solver to be cancelled via the TerminateAnalysisFlag by associating it with a CancellationToken.
            SetCancellationToken(solver);

            solver.Minimize(Model.Parameters.GetFittedParameterArray());

            var mdl_pars = Model.Parameters.GetFittedParameters();

            Model.Solution = SolutionInterface.FromModel(Model, new SolverConvergence(solver, Model.Loss()));
            Model.Solution.ErrorMethod = ErrorEstimationMethod;

            return Model.Solution.Convergence;
        }

        protected override SolverConvergence SolverWithLevenbergMarquardtAlgorithm()
        {
            DateTime start = DateTime.Now;
            var guess = Model.Parameters.GetFittedParameterArray();
            var limits = Model.Parameters.GetLimits();
            int n_par = Model.NumberOfParameters;
            int m = Model.Data.Injections.Count(inj => inj.Include);

            alglib.minlmcreatev(n_par, m, guess, LevenbergMarquardtDifferentiationStepSize, out minlmstate state);

            LMOptimizerState = state;

            alglib.minlmsetcond(LMOptimizerState, LevenbergMarquardtEpsilon, MaxOptimizerIterations);
            alglib.minlmsetscale(LMOptimizerState, Model.Parameters.Table.Values.Where(p => p.IsFitted).Select(p => p.StepSize).ToArray());
            alglib.minlmsetbc(LMOptimizerState, limits.Select(p => p[0]).ToArray(), limits.Select(p => p[1]).ToArray());
            alglib.minlmoptimize(LMOptimizerState, (double[] x, double[] fi, object obj) =>
            {
                var res = Model.LossFunctionResiduals(x, UseErrorWeightedFitting);
                for (int i = 0; i < res.Length; i++) fi[i] = res[i];
            },
            null, null);
            alglib.minlmresults(LMOptimizerState, out double[] result, out minlmreport rep);

            Model.Solution = SolutionInterface.FromModel(Model, new SolverConvergence(LMOptimizerState, rep, DateTime.Now - start, Model.Loss()));
            Model.Solution.ErrorMethod = ErrorEstimationMethod;

            return Model.Solution.Convergence;
        }

        protected override void BoostrapResiduals()
        {
            base.BoostrapResiduals();

            int counter = 0;
            int success = 0;
            int failure = 0;
            var start = DateTime.Now;
            var bag = new ConcurrentBag<SolutionInterface>();

            Parallel.For(0, BootstrapIterations, (i) =>
            {
                if (TerminateAnalysisFlag.Down)
                {
                    try
                    {
                        var solver = new Solver
                        {
                            SolverAlgorithm = this.SolverAlgorithm,
                            Model = Model.GenerateSyntheticModel(),
                            SolverToleranceModifier = 10,
                            MaxOptimizerIterations = MaxBootstrapOptimizerIterations
                        };

                        var rconv = solver.Solve();
                        // Treat replicate as successful only if it did not fail and was not
                        // stopped. Fits that reached iteration limits or produced warnings
                        // are still considered successful for bootstrapping purposes.
                        if (rconv != null && !rconv.Failed && !rconv.Stopped)
                        {
                            bag.Add(solver.Model.Solution);
                            Interlocked.Increment(ref success);
                        }
                        else
                        {
                            Interlocked.Increment(ref failure);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Classify and count any replicate exception as a failure. The
                        // exception is not propagated beyond the replicate level; logging is
                        // deferred to the outer scope.
                        Interlocked.Increment(ref failure);
                    }
                }

                var currcounter = Interlocked.Increment(ref counter);
                ReportBootstrapProgress(currcounter);
            });

            var solutions = bag.ToList();

            // Store only the successful solutions. The solver normalises convergence
            // internally, so each Solution.Convergence already reflects the solver outcome.
            Solution.SetBootstrapSolutions(solutions);
            Solution.Convergence.SetBootstrapTime(DateTime.Now - start);

            // Determine whether to flag a warning. A warning is raised when at least one
            // replicate failed but some succeeded. If no replicates succeeded the bootstrap is
            // marked as failed; however the primary fit may still be valid and the warning
            // flag will surface this without marking the entire fit as failed.
            if (failure > 0)
            {
                // Flag a warning when there are failures and successes.
                if (success > 0)
                {
                    Solution.Convergence.Warning = true;
                    // Append the replicate summary to the detailed message for logging. This
                    // summary is not shown directly to users but can help diagnose bootstrap
                    // robustness issues.
                    string summary = $"Bootstrap: {success}/{BootstrapIterations} succeeded ({failure} failed)";
                    if (string.IsNullOrWhiteSpace(Solution.Convergence.DetailedMessage))
                        Solution.Convergence.DetailedMessage = summary;
                    else
                        Solution.Convergence.DetailedMessage += " | " + summary;
                }
                else if (success == 0 && BootstrapIterations > 0)
                {
                    // All replicates failed; mark the bootstrap run as warning. We do not
                    // override the Failed flag here; normalization will use the highest
                    // severity status between the main fit and bootstrap outcome.
                    Solution.Convergence.Warning = true;
                    string summary = $"Bootstrap failed: 0/{BootstrapIterations} replicates succeeded";
                    if (string.IsNullOrWhiteSpace(Solution.Convergence.DetailedMessage))
                        Solution.Convergence.DetailedMessage = summary;
                    else
                        Solution.Convergence.DetailedMessage += " | " + summary;
                }
            }
        }

        protected override void LeaveOneOut()
        {
            base.LeaveOneOut();

            int counter = 0;
            int success = 0;
            int failure = 0;
            var start = DateTime.Now;
            var bag = new ConcurrentBag<SolutionInterface>();
            var injs = Model.Data.Injections.Where(inj => inj.Include).Select(inj => inj.ID);
            int var_conc_loops = Model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap ? BootstrapIterations / Math.Max(1, injs.Count()) : 1;

            var models = new List<Model>();
            foreach (int i in injs) //setup models, not thread safe due to MCO implementation
            {
                for (int j = 0; j < var_conc_loops; j++) //add additional models for concentration variance
                {
                    Model.ModelCloneOptions.DiscardedDataPoint = i;
                    models.Add(Model.GenerateSyntheticModel());
                }
            }

            Parallel.For(0, models.Count, (i) =>
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
                            SolverToleranceModifier = 10,
                            MaxOptimizerIterations = MaxBootstrapOptimizerIterations
                        };

                        var rconv = solver.Solve();
                        if (rconv != null && !rconv.Failed && !rconv.Stopped)
                        {
                            bag.Add(solver.Model.Solution);
                            Interlocked.Increment(ref success);
                        }
                        else
                        {
                            Interlocked.Increment(ref failure);
                        }
                    }
                    catch (Exception)
                    {
                        // Any exception during a replicate counts as a failure. Exceptions are
                        // not propagated beyond the replicate to avoid halting the entire
                        // leave-one-out procedure.
                        Interlocked.Increment(ref failure);
                    }
                }

                var currcounter = Interlocked.Increment(ref counter);
                ReportLeaveOneOutProgress(currcounter, models.Count);
            });

            var solutions = bag.ToList();

            Solution.SetBootstrapSolutions(solutions);
            Solution.Convergence.SetBootstrapTime(DateTime.Now - start);

            // Apply warnings based on replicate success/failure counts.
            if (failure > 0)
            {
                if (success > 0)
                {
                    Solution.Convergence.Warning = true;
                    string summary = $"Leave-one-out: {success}/{models.Count} succeeded ({failure} failed)";
                    if (string.IsNullOrWhiteSpace(Solution.Convergence.DetailedMessage))
                        Solution.Convergence.DetailedMessage = summary;
                    else
                        Solution.Convergence.DetailedMessage += " | " + summary;
                }
                else if (success == 0 && models.Count > 0)
                {
                    Solution.Convergence.Warning = true;
                    string summary = $"Leave-one-out failed: 0/{models.Count} replicates succeeded";
                    if (string.IsNullOrWhiteSpace(Solution.Convergence.DetailedMessage))
                        Solution.Convergence.DetailedMessage = summary;
                    else
                        Solution.Convergence.DetailedMessage += " | " + summary;
                }
            }
        }
    }

    public class GlobalSolver : SolverInterface
    {
        public GlobalModel Model { get; set; }
        public GlobalSolution Solution => Model.Solution;

        public override async void Analyze()
        {
            base.Analyze();

            try
            {
                await Task.Run(() =>
                {
                    SolverConvergence convergence;

                    if (Model.ShouldFitIndividually)
                    {
                        ReportSolverUpdate(new SolverUpdate(0, Model.Models.Count) { Message = "Fitting individually...", Progress = 0 });

                        var convergences = new List<SolverConvergence>();
                        var counter = 0;
                        foreach (var mdl in Model.Models)
                        {
                            // Detect user cancellation
                            if (TerminateAnalysisFlag.Up) throw new OptimizerStopException();

                            var solver = SolverInterface.Initialize(mdl);
                            solver.ErrorEstimationMethod = ErrorEstimationMethod;
                            solver.BootstrapIterations = BootstrapIterations;
                            solver.SolverAlgorithm = SolverAlgorithm;
                            solver.Silent = true;

                            var con = solver.Solve();
                            convergences.Add(con);

                            counter++;

                            ReportSolverUpdate(new SolverUpdate(counter, Model.Models.Count) { Progress = (float)counter / Model.Models.Count });
                        }

                        convergence = new SolverConvergence(convergences);
                        // Normalise the aggregated convergence prior to creating the global solution.
                        convergence.Normalize();

                        Model.Solution = new GlobalSolution(this, Model.Models.Select(mdl => mdl.Solution).ToList(), convergence);
                    }
                    else // Fit globally
                    {
                        convergence = Solve();
                        convergence.SetLoss(Model.Loss());
                    }

                    // Normalise the convergence before reporting. Individual solves already
                    // normalise their own results in Solve(), but aggregated solves require it.
                    if (convergence != null)
                    {
                        convergence.Normalize();
                    }
                    ReportAnalysisFinished(convergence);
                });

                DataManager.AddData(new AnalysisResult(Model.Solution));
            }
            catch (Exception ex)
            {
                // Build a convergence from the exception. Aggregate exceptions and cancellation
                // are unwrapped inside FromException().
                var conv = SolverConvergence.FromException(ex, starttime);
                conv.Normalize();
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
            var f = new NonlinearObjectiveFunction(Model.NumberOfParameters, (w) => Model.LossFunction(w, UseErrorWeightedFitting));
            var solver = new NelderMead(f);

            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(Model.NumberOfParameters)
            {
                MaximumEvaluations = MaxOptimizerIterations,
                AbsoluteFunctionTolerance = NMFunctionTolerance(Model.LossFunction(Model.Parameters.GetFittedParameterArray(), UseErrorWeightedFitting)),
                RelativeParameterTolerance = RelativeParameterTolerance,
                StartTime = DateTime.Now,
            };

            SetStepSizes(solver, Model.Parameters.GetStepSizes());
            SetBounds(solver, Model.Parameters.GetLimits());
            SetCancellationToken(solver);

            solver.Minimize(Model.Parameters.GetFittedParameterArray());

            var mdl_pars = Model.Parameters.GetFittedParameters();

            Model.Solution = new GlobalSolution(this, new SolverConvergence(solver, Model.Loss()));

            return Model.Solution.Convergence;
        }

        protected override SolverConvergence SolverWithLevenbergMarquardtAlgorithm()
        {
            DateTime start = DateTime.Now;
            var guess = Model.Parameters.GetFittedParameterArray();
            var limits = Model.Parameters.GetLimits();
            var parameters = Model.Parameters.GetFittedParameters();
            int n_par = Model.NumberOfParameters;
            int m = Model.GetNumberOfPoints();

            alglib.minlmcreatev(n_par, m, guess, LevenbergMarquardtDifferentiationStepSize, out minlmstate state);

            LMOptimizerState = state;

            alglib.minlmsetcond(state, LevenbergMarquardtEpsilon, MaxOptimizerIterations);
            alglib.minlmsetscale(state, parameters.Select(p => p.StepSize).ToArray());
            alglib.minlmsetbc(state, limits.Select(p => p[0]).ToArray(), limits.Select(p => p[1]).ToArray());
            alglib.minlmoptimize(state, (double[] x, double[] fi, object obj) =>
            {
                var res = Model.LossFunctionResiduals(x, UseErrorWeightedFitting);
                for (int i = 0; i < res.Length; i++) fi[i] = res[i];
            }, null, null);
            alglib.minlmresults(state, out double[] result, out minlmreport rep);

            var loss = Model.LossFunction(result, UseErrorWeightedFitting);

            Model.Solution = new GlobalSolution(this, new SolverConvergence(LMOptimizerState, rep, DateTime.Now - start, loss));

            return Solution.Convergence;
        }

        protected override void BoostrapResiduals()
        {
            base.BoostrapResiduals();

            var bag = new ConcurrentBag<GlobalSolution>();
            int counter = 0;
            int success = 0;
            int failure = 0;
            var start = DateTime.Now;
            var opt = new ParallelOptions();
            opt.MaxDegreeOfParallelism = AppSettings.MaxDegreeOfParallelism;

            Parallel.For(0, BootstrapIterations, opt, (i) =>
            {
                if (TerminateAnalysisFlag.Down)
                {
                    try
                    {
                        var globalmodel = Model.GenerateSyntheticModel();
                        var solver = new GlobalSolver
                        {
                            Model = globalmodel,
                            SolverAlgorithm = SolverAlgorithm,
                            SolverToleranceModifier = 10,
                            MaxOptimizerIterations = MaxBootstrapOptimizerIterations
                        };

                        var convergence = solver.Solve();
                        // Count replicate success/failure based on convergence flags. Success when
                        // not failed and not stopped.
                        if (convergence != null && !convergence.Failed && !convergence.Stopped)
                        {
                            var solution = new GlobalSolution(solver, convergence);
                            bag.Add(solution);
                            Interlocked.Increment(ref success);
                        }
                        else
                        {
                            Interlocked.Increment(ref failure);
                        }
                    }
                    catch (Exception)
                    {
                        // Any exception during a replicate counts as a failure and is not
                        // propagated.
                        Interlocked.Increment(ref failure);
                    }
                }

                var currcounter = Interlocked.Increment(ref counter);
                ReportBootstrapProgress(currcounter);
            });

            var solutions = bag.ToList();

            Solution.SetBootstrapSolutions(solutions);
            Solution.Convergence.SetBootstrapTime(DateTime.Now - start);

            // Apply warnings based on replicate success/failure counts.
            if (failure > 0)
            {
                if (success > 0)
                {
                    Solution.Convergence.Warning = true;
                    string summary = $"Global bootstrap: {success}/{BootstrapIterations} succeeded ({failure} failed)";
                    if (string.IsNullOrWhiteSpace(Solution.Convergence.DetailedMessage))
                        Solution.Convergence.DetailedMessage = summary;
                    else
                        Solution.Convergence.DetailedMessage += " | " + summary;
                }
                else if (success == 0 && BootstrapIterations > 0)
                {
                    Solution.Convergence.Warning = true;
                    string summary = $"Global bootstrap failed: 0/{BootstrapIterations} replicates succeeded";
                    if (string.IsNullOrWhiteSpace(Solution.Convergence.DetailedMessage))
                        Solution.Convergence.DetailedMessage = summary;
                    else
                        Solution.Convergence.DetailedMessage += " | " + summary;
                }
            }
        }

        protected override void LeaveOneOut()
        {
            base.LeaveOneOut();

            var bag = new ConcurrentBag<GlobalSolution>();
            int counter = 0;
            int success = 0;
            int failure = 0;
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
                            SolverToleranceModifier = 10,
                            MaxOptimizerIterations = MaxBootstrapOptimizerIterations
                        };

                        var convergence = solver.Solve();
                        // Consider a replicate successful if the convergence did not fail and was not stopped.
                        if (convergence != null && !convergence.Failed && !convergence.Stopped)
                        {
                            var solution = new GlobalSolution(solver, convergence);
                            bag.Add(solution);
                            Interlocked.Increment(ref success);
                        }
                        else
                        {
                            Interlocked.Increment(ref failure);
                        }
                    }
                    catch (Exception)
                    {
                        // Any exception during a replicate counts as a failure.
                        Interlocked.Increment(ref failure);
                    }
                }

                var currcounter = Interlocked.Increment(ref counter);
                ReportLeaveOneOutProgress(currcounter, Model.Models.Count);
            });

            var solutions = bag.ToList();

            Solution.SetBootstrapSolutions(solutions);
            Solution.Convergence.SetBootstrapTime(DateTime.Now - start);

            // Flag warnings based on replicate success/failure counts.
            if (failure > 0)
            {
                if (success > 0)
                {
                    Solution.Convergence.Warning = true;
                    string summary = $"Global leave-one-out: {success}/{Model.Models.Count} succeeded ({failure} failed)";
                    if (string.IsNullOrWhiteSpace(Solution.Convergence.DetailedMessage))
                        Solution.Convergence.DetailedMessage = summary;
                    else
                        Solution.Convergence.DetailedMessage += " | " + summary;
                }
                else if (success == 0 && Model.Models.Count > 0)
                {
                    Solution.Convergence.Warning = true;
                    string summary = $"Global leave-one-out failed: 0/{Model.Models.Count} replicates succeeded";
                    if (string.IsNullOrWhiteSpace(Solution.Convergence.DetailedMessage))
                        Solution.Convergence.DetailedMessage = summary;
                    else
                        Solution.Convergence.DetailedMessage += " | " + summary;
                }
            }
        }
    }
}

