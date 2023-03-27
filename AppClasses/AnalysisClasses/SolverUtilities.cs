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
        public void SetLoss(double loss) => Loss = loss;

        private SolverConvergence() { }

        public SolverConvergence(SolverConvergence conv)
        {
            Algorithm = conv.Algorithm;
            Iterations = conv.Iterations;
            Message = conv.Message;
            Time = conv.Time;
            Loss = 0;

            Failed = conv.Failed;
        }

        public SolverConvergence(NelderMead solver, double loss)
        {
            Algorithm = SolverAlgorithm.NelderMead;
            Iterations = solver.Convergence.Evaluations;
            Message = solver.Status.ToString();
            Time = DateTime.Now - solver.Convergence.StartTime;
            Loss = solver.Value;

            Failed = solver.Status == NelderMeadStatus.Failure;
        }

        public SolverConvergence(minlmstate state, minlmreport rep, TimeSpan time, double loss)
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt;
            Iterations = rep.iterationscount;
            Message = ((AlgLibTerminationCode)rep.terminationtype).GetEnumDescription();
            Time = time;
            Loss = loss;

            Failed = rep.terminationtype > 2;
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
                Message = "Fitting Failed",
            };

            return conv;
        }

        public static SolverConvergence ReportStopped(DateTime starttime)
        {
            var conv = new SolverConvergence()
            {
                Time = DateTime.Now - starttime,
                Failed = true,
                Message = "Fitting Stopped by User",
            };

            return conv;
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
        public int Step { get; set; } = 0;
        public int TotalSteps { get; set; } = 1;
        public int Time { get; set; } = 0;

        public string ProgressString => Step.ToString() + "/" + TotalSteps.ToString();

        public void SendToStatusBar()
        {
            StatusBarManager.Progress = Progress;
            if (Message != "") StatusBarManager.SetStatus(Message, Time, priority: 1);
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
