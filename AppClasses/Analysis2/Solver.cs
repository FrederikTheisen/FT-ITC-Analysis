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
        public int MaxOptimizerIterations { get; private set; } = AppSettings.MaximumOptimizerIterations;
        public ErrorEstimationMethod ErrorEstimationMethod { get; set; } = ErrorEstimationMethod.None;
        public int BootstrapIterations { get; set; } = 100;
        public double SolverFunctionTolerance { get; set; } = AppSettings.OptimizerTolerance;
        public double RelativeParameterTolerance { get; set; } = 2E-5;
        public double SolverBootstrapTolerance { get; set; } = 1.0E-13;
        public double LevenbergMarquardtDifferentiationStepSize { get; set; } = 0.001;
        public double LevenbergMarquardtEpsilon { get; set; } = 1E-22;

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
            AnalysisStarted.Invoke(this, TerminateAnalysisFlag);

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
            StatusBarManager.SetStatus("Terminating analysis...",0,3);

            try
            {
                if (LMOptimizerState != null) alglib.minlmrequesttermination(LMOptimizerState);
                else if (NelderMeadToken != null) NelderMeadToken.Cancel();
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        public virtual SolverConvergence Solve()
        {
            TerminateAnalysisFlag.WasRaised += TerminateAnalysisFlag_WasRaised;
            SolverConvergence convergence;

            switch (SolverAlgorithm)
            {
                case SolverAlgorithm.NelderMead: convergence = SolveWithNelderMeadAlgorithm(); break;
                case SolverAlgorithm.LevenbergMarquardt: convergence = SolverWithLevenbergMarquardtAlgorithm(); break;
                default: throw new NotImplementedException("Solver algorithm not implemented");
            }

            ReportAnalysisStepFinished();

            switch (ErrorEstimationMethod)
            {
                case ErrorEstimationMethod.BootstrapResiduals: BoostrapResiduals(); break;
                case ErrorEstimationMethod.LeaveOneOut: LeaveOneOut(); break;
                case ErrorEstimationMethod.None:
                default: break;
            }

            endtime = DateTime.Now;

            return convergence;
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
            base.Analyze();

            try
            {
                await Task.Run(() =>
                {
                    var convergence = Solve();
                    ReportAnalysisFinished(convergence);
                });
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);

                ReportAnalysisFinished(SolverConvergence.ReportFailed(starttime));
            }
        }

        protected override SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            var f = new NonlinearObjectiveFunction(Model.NumberOfParameters, (w) => Model.LossFunction(w));
            var solver = new NelderMead(f);

            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(Model.NumberOfParameters)
            {
                MaximumEvaluations = MaxOptimizerIterations,
                //AbsoluteFunctionTolerance = SolverFunctionTolerance,
                //RelativeParameterTolerance = RelativeParameterTolerance,
                StartTime = DateTime.Now,
            };

            SetStepSizes(solver, Model.Parameters.GetStepSizes());
            SetBounds(solver, Model.Parameters.GetLimits());

            solver.Minimize(Model.Parameters.GetFittedParameterArray());

            var mdl_pars = Model.Parameters.GetFittedParameters();

            Model.Solution = SolutionInterface.FromModel(Model, new SolverConvergence(solver, Model.LossFunction(Model.Parameters.GetFittedParameterArray())));
            Model.Solution.ErrorMethod = ErrorEstimationMethod;

            return Model.Solution.Convergence;
        }

        protected override SolverConvergence SolverWithLevenbergMarquardtAlgorithm()
        {
            DateTime start = DateTime.Now;
            var guess = Model.Parameters.GetFittedParameterArray();
            var limits = Model.Parameters.GetLimits();

            alglib.minlmcreatev(Model.NumberOfParameters, guess, LevenbergMarquardtDifferentiationStepSize, out minlmstate minlmstate);

            LMOptimizerState = minlmstate;

            alglib.minlmsetcond(LMOptimizerState, LevenbergMarquardtEpsilon, MaxOptimizerIterations);
            alglib.minlmsetscale(LMOptimizerState, Model.Parameters.Table.Values.Where(p => p.IsFitted).Select(p => p.StepSize).ToArray());
            alglib.minlmsetbc(LMOptimizerState, limits.Select(p => p[0]).ToArray(), limits.Select(p => p[1]).ToArray());
            alglib.minlmoptimize(LMOptimizerState, (double[] x, double[] fi, object obj) => { fi[0] = Model.LossFunction(x); }, null, null);
            alglib.minlmresults(LMOptimizerState, out double[] result, out minlmreport rep);

            Model.Solution = SolutionInterface.FromModel(Model, new SolverConvergence(LMOptimizerState, rep, DateTime.Now - start, Model.LossFunction(result)));
            Model.Solution.ErrorMethod = ErrorEstimationMethod;

            return Model.Solution.Convergence;
        }

        protected override void BoostrapResiduals()
        {
            base.BoostrapResiduals();

            int counter = 0;
            var start = DateTime.Now;
            var bag = new ConcurrentBag<SolutionInterface>();
            var opt = new ParallelOptions();
            opt.MaxDegreeOfParallelism = AppSettings.MaxDegreeOfParallelism;

            var res = Parallel.For(0, BootstrapIterations, (i) =>
            {
                if (TerminateAnalysisFlag.Down)
                {
                    var solver = new Solver();
                    solver.SolverAlgorithm = this.SolverAlgorithm;
                    solver.Model = Model.GenerateSyntheticModel();
                    solver.SolverFunctionTolerance = SolverBootstrapTolerance;

                    solver.Solve();

                    bag.Add(solver.Model.Solution);
                }

                var currcounter = Interlocked.Increment(ref counter);

                ReportBootstrapProgress(currcounter);
            });

            var solutions = bag.ToList();

            Solution.SetBootstrapSolutions(solutions.Where(sol => !sol.Convergence.Failed).ToList());
            Solution.Convergence.SetBootstrapTime(DateTime.Now - start);
        }

        protected override void LeaveOneOut()
        {
            base.LeaveOneOut();

            int counter = 0;
            var start = DateTime.Now;
            var bag = new ConcurrentBag<SolutionInterface>();
            var injs = Model.Data.Injections.Where(inj => inj.Include).Select(inj => inj.ID);
            int var_conc_loops = Model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap ? BootstrapIterations / injs.Count() : 1;

            var models = new List<Model>();
            foreach (int i in injs) //setup models, not thread safe due to MCO implementation
            {
                for (int j = 0; j < var_conc_loops; j++) //add additional models for concentration variance
                {
                    Model.ModelCloneOptions.DiscardedDataPoint = i;
                    models.Add(Model.GenerateSyntheticModel());
                }
            }

            var res = Parallel.For(0 , models.Count(), (i) =>
            {
                if (TerminateAnalysisFlag.Down)
                {
                    var mdl = models[i];
                    var solver = new Solver();
                    solver.SolverAlgorithm = this.SolverAlgorithm;
                    solver.Model = mdl;
                    solver.SolverFunctionTolerance = SolverBootstrapTolerance;

                    solver.Solve();

                    bag.Add(solver.Model.Solution);
                }

                var currcounter = Interlocked.Increment(ref counter);

                ReportLeaveOneOutProgress(currcounter, models.Count());
            });

            var solutions = bag.ToList();

            Solution.SetBootstrapSolutions(solutions.Where(sol => !sol.Convergence.Failed).ToList());
            Solution.Convergence.SetBootstrapTime(DateTime.Now - start);
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

                        Model.Solution = new GlobalSolution(this, Model.Models.Select(mdl => mdl.Solution).ToList(), convergence);
                    }
                    else //Fit globally
                    {
                        convergence = Solve();
                    }

                    ReportAnalysisFinished(convergence);
                });

                DataManager.AddData(new AnalysisResult(Model.Solution));
            }
            catch (OptimizerStopException ex)
            {
                ReportAnalysisFinished(SolverConvergence.ReportStopped(starttime));
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);

                ReportAnalysisFinished(SolverConvergence.ReportFailed(starttime));
            }
        }

        protected override SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            var f = new NonlinearObjectiveFunction(Model.NumberOfParameters, (w) => Model.LossFunction(w));
            var solver = new NelderMead(f);

            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(Model.NumberOfParameters)
            {
                MaximumEvaluations = MaxOptimizerIterations,
                AbsoluteFunctionTolerance = SolverFunctionTolerance,
                //RelativeParameterTolerance = RelativeParameterTolerance,
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
            var varcount = Model.NumberOfParameters;
            var guess = Model.Parameters.GetFittedParameterArray();
            var limits = Model.Parameters.GetLimits();
            var parameters = Model.Parameters.GetFittedParameters();

            alglib.minlmcreatev(varcount, guess, LevenbergMarquardtDifferentiationStepSize, out minlmstate LMOptimizerState);
            alglib.minlmsetcond(LMOptimizerState, LevenbergMarquardtEpsilon, MaxOptimizerIterations);
            alglib.minlmsetscale(LMOptimizerState, parameters.Select(p => p.StepSize).ToArray());
            alglib.minlmsetbc(LMOptimizerState, limits.Select(p => p[0]).ToArray(), limits.Select(p => p[1]).ToArray());
            alglib.minlmoptimize(LMOptimizerState, (double[] parameters, double[] fi, object obj) => { fi[0] = Model.LossFunction(parameters); }, null, null);
            alglib.minlmresults(LMOptimizerState, out double[] result, out minlmreport rep);

            var loss = Model.LossFunction(result);

            Model.Solution = new GlobalSolution(this, new SolverConvergence(LMOptimizerState, rep, DateTime.Now - start, loss));

            return Solution.Convergence;
        }

        protected override void BoostrapResiduals()
        {
            base.BoostrapResiduals();

            var bag = new ConcurrentBag<GlobalSolution>();
            int counter = 0;
            var start = DateTime.Now;
            var opt = new ParallelOptions();
            opt.MaxDegreeOfParallelism = AppSettings.MaxDegreeOfParallelism;

            var res = Parallel.For(0, BootstrapIterations, opt, (i) =>
            {
                if (TerminateAnalysisFlag.Down)
                {
                    var globalmodel = Model.GenerateSyntheticModel();
                    var solver = new GlobalSolver();
                    solver.Model = globalmodel;
                    solver.SolverAlgorithm = SolverAlgorithm;
                    solver.SolverFunctionTolerance = SolverBootstrapTolerance;

                    var convergence = solver.Solve();
                    var solution = new GlobalSolution(solver, convergence);

                    bag.Add(solution);
                }

                var currcounter = Interlocked.Increment(ref counter);

                ReportBootstrapProgress(currcounter);
            });

            var solutions = bag.ToList();

            Solution.SetBootstrapSolutions(solutions);
            Solution.Convergence.SetBootstrapTime(DateTime.Now - start);
        }

        protected override void LeaveOneOut()
        {
            base.LeaveOneOut();

            var bag = new ConcurrentBag<GlobalSolution>();
            int counter = 0;
            var start = DateTime.Now;
            var opt = new ParallelOptions();
            opt.MaxDegreeOfParallelism = 10;

            var res = Parallel.For(0, Model.Models.Count, opt, (i) =>
            {
                if (TerminateAnalysisFlag.Down)
                {
                    var globalmodel = Model.LeaveOneOut(i);
                    var solver = new GlobalSolver();
                    solver.Model = globalmodel;
                    solver.SolverAlgorithm = SolverAlgorithm;
                    solver.SolverFunctionTolerance = SolverBootstrapTolerance;

                    var convergence = solver.Solve();
                    var solution = new GlobalSolution(solver, convergence);

                    bag.Add(solution);
                }

                var currcounter = Interlocked.Increment(ref counter);

                ReportLeaveOneOutProgress(currcounter, Model.Models.Count);
            });

            var solutions = bag.ToList();

            Solution.SetBootstrapSolutions(solutions);
            Solution.Convergence.SetBootstrapTime(DateTime.Now - start);
        }
    }
}

