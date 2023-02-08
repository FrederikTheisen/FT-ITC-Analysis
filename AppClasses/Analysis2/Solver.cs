using System;
using System.Linq;
using Accord.Math.Optimization;
//using static CoreFoundation.DispatchSource;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using AppKit;
using Accord.Math;
using Accord.IO;

namespace AnalysisITC.AppClasses.Analysis2
{
    public class SolverInterface
    {
        public static TerminationFlag TerminateAnalysisFlag { get; protected set; } = new TerminationFlag();
        public static int BootstrapIterations { get; set; } = 100;

        public static event EventHandler<Tuple<int, int, float>> BootstrapIterationFinished;
        public static event EventHandler<SolverConvergence> AnalysisFinished;
        public static event EventHandler AnalysisStepFinished;

        public Analysis.SolverAlgorithm SolverAlgorithm { get; set; } = Analysis.SolverAlgorithm.NelderMead;
        public Analysis.ErrorEstimationMethod ErrorEstimationMethod { get; set; } = Analysis.ErrorEstimationMethod.None;

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

        public void ReportBootstrapProgress(int iteration) => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            BootstrapIterationFinished?.Invoke(null, new Tuple<int, int, float>(iteration, BootstrapIterations, iteration / (float)BootstrapIterations));
        });

        public void ReportAnalysisStepFinished()
        {
            AnalysisStepFinished?.Invoke(null, null);
        }

        public void ReportAnalysisFinished(SolverConvergence convergence)
        {
            AnalysisFinished?.Invoke(null, convergence);
        }

        public async void Analyze() => await Task.Run(() => { Solve(); });

        public virtual void Solve()
        {
            starttime = DateTime.Now;
            TerminateAnalysisFlag = new TerminationFlag();

            SolverConvergence convergence = null;

            switch (SolverAlgorithm)
            {
                case Analysis.SolverAlgorithm.NelderMead: convergence = SolveWithNelderMeadAlgorithm(); break;
                case Analysis.SolverAlgorithm.LevenbergMarquardt: convergence = SolverWithLevenbergMarquardtAlgorithm(); break;
                default: throw new NotImplementedException("Solver algorithm not implemented");
            }

            ReportAnalysisStepFinished();

            switch (ErrorEstimationMethod)
            {
                case Analysis.ErrorEstimationMethod.BootstrapResiduals: BoostrapResiduals(); break;
                case Analysis.ErrorEstimationMethod.None:
                default: break;
            }

            endtime = DateTime.Now;

            Console.WriteLine("Analysis time: " + Duration.TotalSeconds + "s");

            ReportAnalysisFinished(convergence);
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
            throw new NotImplementedException();
        }

        protected void SetStepSizes(NelderMead solver, double[] stepsize)
        {
            for (int i = 0; i < solver.StepSize.Length; i++) solver.StepSize[i] = stepsize[i];
        }

        public virtual void SetBounds(object solver, List<double[]> bounds)
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
    }

    public class Solver : SolverInterface
    {
        public Model Model { get; set; }
        public SolutionInterface Solution { get; private set; }

        protected override SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            var f = new NonlinearObjectiveFunction(Model.NumberOfParameters, (w) => Model.LossFunction(w));
            var solver = new NelderMead(f);

            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(Model.NumberOfParameters)
            {
                MaximumEvaluations = 300000,
                AbsoluteFunctionTolerance = double.Epsilon,
                StartTime = DateTime.Now,
            };

            SetStepSizes(solver, Model.Parameters.GetStepSizes());
            SetBounds(solver, Model.Parameters.GetLimits());

            solver.Minimize(Model.Parameters.ToArray());

            Model.Solution = SolutionInterface.FromModel(Model, solver.Solution);
            Model.Solution.Convergence = new SolverConvergence(solver);

            return Model.Solution.Convergence;
        }

        protected override void BoostrapResiduals()
        {
            ReportBootstrapProgress(0);

            int counter = 0;
            var start = DateTime.Now;
            var bag = new ConcurrentBag<SolutionInterface>();

            var res = Parallel.For(0, BootstrapIterations, (i) =>
            {
                if (TerminateAnalysisFlag.Down)
                {
                    var solver = new Solver();
                    solver.SolverAlgorithm = this.SolverAlgorithm;
                    solver.Model = Model.GenerateSyntheticModel();

                    solver.Solve();

                    bag.Add(solver.Model.Solution);
                }

                var currcounter = Interlocked.Increment(ref counter);

                ReportBootstrapProgress(currcounter);
            });

            var solutions = bag.ToList();

            Model.GenerateSyntheticModel();

            Model.Solution.SetBootstrapSolutions(solutions.Where(sol => !sol.Convergence.Failed).ToList());
            Model.Solution.ComputeErrorsFromBootstrapSolutions();
        }
    }

    public class GlobalSolver : SolverInterface
    {
        public GlobalModel Model { get; set; }
        public GlobalSolution Solution => Model.Solution;

        protected override SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            var f = new NonlinearObjectiveFunction(Model.NumberOfParameters, (w) => Model.LossFunction(w));
            var solver = new NelderMead(f);

            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(Model.NumberOfParameters)
            {
                MaximumEvaluations = 300000,
                AbsoluteFunctionTolerance = double.Epsilon,
                StartTime = DateTime.Now,
            };

            SetStepSizes(solver, Model.Parameters.GetStepSizes());
            SetBounds(solver, Model.Parameters.GetLimits());

            solver.Minimize(Model.Parameters.ToArray());

            Model.Solution = new GlobalSolution(Model);
            Model.Solution.Convergence = new SolverConvergence(solver);

            return Model.Solution.Convergence;
        }

        protected override void BoostrapResiduals()
        {
            var bag = new ConcurrentBag<GlobalSolution>();

            int counter = 0;

            ReportBootstrapProgress(0);

            var start = DateTime.Now;

            var opt = new ParallelOptions();
            opt.MaxDegreeOfParallelism = 10;

            var res = Parallel.For(0, Analysis.BootstrapIterations, opt, (i) =>
            {
                if (TerminateAnalysisFlag.Down)
                {
                    var globalmodel = new GlobalModel();

                    foreach (var m in Model.Models) globalmodel.AddModel(m.GenerateSyntheticModel());
                    var solver = new GlobalSolver();
                    solver.Model = globalmodel;
                    solver.SolverAlgorithm = SolverAlgorithm;

                    solver.Solve();

                    bag.Add(new GlobalSolution(globalmodel));
                }

                var currcounter = Interlocked.Increment(ref counter);

                ReportBootstrapProgress(currcounter);
            });

            var solutions = bag.ToList();

            Solution.SetBootstrapSolutions(solutions);

            Solution.Convergence.SetBootstrapTime(DateTime.Now - start);
        }
    }
}

