using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Accord.Math.Optimization;
using AnalysisITC.AppClasses.Analysis2;
using static alglib;

namespace AnalysisITC
{
    public class SolverConvergence
    {
        public int Iterations { get; private set; } = 0;
        public string Message { get; private set; } = "";
        public TimeSpan Time { get; private set; } = new(0);
        public TimeSpan BootstrapTime { get; private set; } = new(0);
        public double Loss { get; private set; } = 0;
        public bool Failed { get; private set; } = false;
        public SolverAlgorithm Algorithm { get; private set; }

        public void SetBootstrapTime(TimeSpan time) => BootstrapTime = time;

        public SolverConvergence() { }

        public SolverConvergence(NelderMead solver)
        {
            Algorithm = SolverAlgorithm.NelderMead;
            Iterations = solver.Convergence.Evaluations;
            Message = solver.Status.ToString();
            Time = DateTime.Now - solver.Convergence.StartTime;
            Loss = solver.Value;

            Failed = solver.Status == NelderMeadStatus.Failure;
        }

        public SolverConvergence(minlmstate state, minlmreport rep, TimeSpan time)
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt;
            Iterations = rep.iterationscount;
            Message = rep.terminationtype.ToString();
            Time = time;
            Loss = state.f;

            Failed = rep.terminationtype != 2;
        }

        public SolverConvergence(MathNet.Numerics.Optimization.NonlinearMinimizationResult result, TimeSpan time, SolverAlgorithm algorithm)
        {
            Algorithm = algorithm;
            Iterations = result.Iterations;
            Message = result.ReasonForExit.ToString();
            Time = time;
            Loss = result.ModelInfoAtMinimum.Value;

            Failed = result.ReasonForExit != MathNet.Numerics.Optimization.ExitCondition.Converged;
        }

        public SolverConvergence(List<SolverConvergence> list)
        {
            Algorithm = list.First().Algorithm;
            Iterations = list.First().Iterations;
            Message = list.First().Message;
            Time = TimeSpan.FromTicks(list.Sum(sc => sc.Time.Ticks));
            Loss = list.Sum(sc => sc.Loss);

            Failed = list.Any(con => con.Failed);
        }

        public static SolverConvergence ReportFailed()
        {
            var conv = new SolverConvergence()
            {
                Failed = true,
                Message = "Optimization failed due to error",
            };

            return conv;
        }
    }

    public class TerminationFlag
    {
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
        public int Step { get; set; } = 0;
        public int TotalSteps { get; set; } = 1;
        public int Time { get; set; } = 0;

        public string ProgressString => Step.ToString() + "/" + TotalSteps.ToString();

        public void SendToStatusBar()
        {
            StatusBarManager.Progress = Progress;
            if (Message != "") StatusBarManager.SetStatus(Message, Time);
            if (TotalSteps > 0) StatusBarManager.SetSecondaryStatus(ProgressString, Time);
        }
    }

    public class ModelCloneOptions
    {
        public ErrorEstimationMethod ErrorEstimationMethod { get; set; } = ErrorEstimationMethod.None;
        public bool IncludeConcentrationErrorsInBootstrap { get; set; } = false;
        public bool EnableAutoConcentrationVariance { get; set; } = false;
        public double AutoConcentrationVariance { get; set; } = 0.05f;

        public ModelCloneOptions()
        {
            ErrorEstimationMethod = FittingOptionsController.ErrorEstimationMethod;
            IncludeConcentrationErrorsInBootstrap = FittingOptionsController.IncludeConcentrationVariance;
            EnableAutoConcentrationVariance = FittingOptionsController.EnableAutoConcentrationVariance;
            AutoConcentrationVariance = FittingOptionsController.AutoConcentrationVariance;
        }
    }

    public enum AnalysisModel
    {
        OneSetOfSites,
        TwoSetsOfSites,
        SequentialBindingSites,
        Dissociation
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
        BootstrapResiduals
    }

    public enum SolverAlgorithm
    {
        [Description("Nelder-Mead [SIMPLEX]")]
        NelderMead,
        [Description("Levenberg-Marquardt")]
        LevenbergMarquardt
    }
}
