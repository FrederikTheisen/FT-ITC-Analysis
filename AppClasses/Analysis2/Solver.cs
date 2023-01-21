using System;

namespace AnalysisITC.AppClasses.Analysis2
{
    public class Solver
    {
        Model Model { get; set; }

        public SolverConvergence Fit(Analysis.SolverAlgorithm algorithm)
        {
            var starttime = DateTime.Now;

            switch (algorithm)
            {
                case Analysis.SolverAlgorithm.NelderMead:
                    break;
                case Analysis.SolverAlgorithm.LevenbergMarquardt:
                    break;
            }
        }

        SolverConvergence SolveUsingNelderMead()
        {

        }
    }
}

