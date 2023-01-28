using System;
using System.Linq;
using Accord.Math.Optimization;
//using static CoreFoundation.DispatchSource;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using AppKit;

namespace AnalysisITC.AppClasses.Analysis2
{
    public class Solver
    {
        public static TerminationFlag TerminateAnalysisFlag { get; private set; } = new TerminationFlag();

        public static event EventHandler<Tuple<int, int, float>> BootstrapIterationFinished;

        public Model Model { get; set; }
        public Analysis.SolverAlgorithm SolverAlgorithm { get; set; } = Analysis.SolverAlgorithm.NelderMead;
        public Analysis.ErrorEstimationMethod ErrorEstimationMethod { get; set; } = Analysis.ErrorEstimationMethod.BootstrapResiduals;
        public static int BootstrapIterations { get; set; } = 100;

        public static void ReportBootstrapProgress(int iteration) => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            BootstrapIterationFinished?.Invoke(null, new Tuple<int, int, float>(iteration, BootstrapIterations, iteration / (float)BootstrapIterations));
        });

        public SolverConvergence Solve()
        {
            var starttime = DateTime.Now;
            TerminateAnalysisFlag = new TerminationFlag();

            SolverConvergence convergence;

            switch (SolverAlgorithm)
            {
                case Analysis.SolverAlgorithm.NelderMead: convergence = SolveWithNelderMeadAlgorithm(); break;
                case Analysis.SolverAlgorithm.LevenbergMarquardt:
                default: throw new NotImplementedException("Solver algorithm not implemented");
            }

            switch (ErrorEstimationMethod)
            {

            }

            return convergence;
        }

        SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            var f = new NonlinearObjectiveFunction(4, (w) => Model.LossFunction(w));
            var solver = new NelderMead(f);

            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(4)
            {
                MaximumEvaluations = 300000,
                AbsoluteFunctionTolerance = double.Epsilon,
                StartTime = DateTime.Now,
            };

            SetStepSizes(solver);
            SetBounds(solver);

            solver.Minimize(Model.Parameters.ToArray());

            Model.Data.Solution = Solution.FromAccordNelderMead(Model); // solver.Function(solver.Solution));
            Model.Data.Solution.Convergence = new SolverConvergence(solver);

            return Model.Data.Solution.Convergence;
        }

        void BoostrapResiduals()
        {
            ReportBootstrapProgress(0);

            int counter = 0;
            var start = DateTime.Now;
            var solutions = new List<Solution>();

            var bag = new ConcurrentBag<Solution>();

            var res = Parallel.For(0, BootstrapIterations, (i) =>
            {
                if (TerminateAnalysisFlag.Down)
                {
                    var solver = new Solver();
                    solver.SolverAlgorithm = this.SolverAlgorithm;
                    solver.ErrorEstimationMethod = Analysis.ErrorEstimationMethod.None;
                    solver.Model = Model.GenerateSyntheticModel();
                    //solver.Model.SetBootstrapStart(Solution.Raw);

                    solver.Solve();

                    //bag.Add(solver.Solution);
                }

                var currcounter = Interlocked.Increment(ref counter);

                ReportBootstrapProgress(currcounter);
            });

            solutions = bag.ToList();

            //Solution.BootstrapSolutions = solutions.Where(sol => !sol.Convergence.Failed).ToList();
            //Solution.ComputeErrorsFromBootstrapSolutions();
        }

        private void SetStepSizes(NelderMead solver)
        {
            var stepsize = Model.Parameters.GetStepSizes();

            for (int i = 0; i < solver.StepSize.Length; i++) solver.StepSize[i] = stepsize[i];
        }

        public virtual void SetBounds(object solver)
        {
            var bounds = Model.Parameters.GetLimits();

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
}

