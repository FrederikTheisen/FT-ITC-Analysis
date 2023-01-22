using System;
using System.Linq;
using Accord.Math.Optimization;
using static alglib;
using static CoreFoundation.DispatchSource;

namespace AnalysisITC.AppClasses.Analysis2
{
    public class Solver
    {
        Model Model { get; set; }

        public SolverConvergence Fit(Analysis.SolverAlgorithm algorithm)
        {
            var starttime = DateTime.Now;

            SolverConvergence convergence;

            switch (algorithm)
            {
                case Analysis.SolverAlgorithm.NelderMead: convergence = SolveWithNelderMeadAlgorithm(); break;
                case Analysis.SolverAlgorithm.LevenbergMarquardt:
                default: throw new NotImplementedException("Solver algorithm not implemented");
            }

            return convergence;
        }

        SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            var f = new NonlinearObjectiveFunction(4, (w) => Model.LossFunction(w));
            var solver = new NelderMead(f);
            SetStepSizes(solver);

            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(4)
            {
                MaximumEvaluations = 300000,
                AbsoluteFunctionTolerance = double.Epsilon,
                StartTime = DateTime.Now,
            };
            SetBounds(solver);


            solver.Minimize(InitialGuessVector is null ? new double[4] { GuessN, GuessH, GuessK, GuessOffset } : InitialGuessVector);

            Model.Data.Solution = Solution.FromAccordNelderMead(solver.Solution, this, RMSD(solver.Solution[0], solver.Solution[1], solver.Solution[2], solver.Solution[3], false)); // solver.Function(solver.Solution));
            Model.Data.Solution.Convergence = new SolverConvergence(solver);

            return Model.Data.Solution.Convergence;
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
                case minlmstate state:
                    alglib.minlmsetbc(state, lower, upper);
                    break;
            }
        }
    }
}

