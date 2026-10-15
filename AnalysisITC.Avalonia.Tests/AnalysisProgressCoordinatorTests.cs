using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Threading;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Platform;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisProgressCoordinatorTests : IDisposable
{
    readonly MainWindow window;
    readonly AnalysisProgressCoordinator coordinator;
    readonly TextBlock secondary;
    readonly IMainThreadDispatcher previousDispatcher;
    readonly int previousStep = SolverUpdate.Step;
    readonly int previousTotal = SolverUpdate.TotalSteps;

    public AnalysisProgressCoordinatorTests()
    {
        previousDispatcher = PlatformServices.MainThreadDispatcher;
        // Exercise the coordinator's worker-to-UI dispatch, rather than having
        // Core dispatch the events to the UI before the coordinator sees them.
        PlatformServices.RegisterMainThreadDispatcher(null!);
        StatusBarManager.ClearAppStatus();
        window = new MainWindow();
        window.Show();
        secondary = window.FindControl<TextBlock>("SecondaryStatusText")!;
        coordinator = new AnalysisProgressCoordinator();
        StatusBarManager.SetDefaultSecondaryStatus("Example project");
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    public void UncertaintyEventsReachTheStatusBarOutsideTheAnalysisWorkspace(ErrorEstimationMethod method)
    {
        // Base Analyze publishes the operation's start without running a fit.
        // This exercises the same events used by Update Result while the
        // Analysis workspace's isFitting flag remains false.
        var solver = new SolverInterface { ErrorEstimationMethod = method, BootstrapIterations = 100 };
        solver.Analyze();
        solver.ReportAnalysisStepFinished();
        solver.PublishProgress(FittingPhase.EstimatingUncertainty);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Estimating uncertainty…", window.StatusText.Text);

        if (method == ErrorEstimationMethod.BootstrapResiduals)
            solver.ReportBootstrapProgress(40);
        else
            solver.ReportLeaveOneOutProgress(2, 5);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Estimating uncertainty…", window.StatusText.Text);
        Assert.Equal(method == ErrorEstimationMethod.BootstrapResiduals ? "Bootstrap 40/100" : "Leave-one-out 2/5", secondary.Text);
        Assert.Equal(0.4, StatusBarManager.Progress, precision: 6);
    }

    [AvaloniaTheory]
    [InlineData(0.25f)]
    [InlineData(-1f)]
    public void SolverMessagesAndCountersReachTheStatusBarEvenWithoutAProgressValue(float progress)
    {
        var solver = new SolverInterface();
        solver.Analyze();
        solver.ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood;
        solver.PublishProgress(FittingPhase.EstimatingUncertainty, 2, 8, progress >= 0 ? progress : null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Estimating uncertainty…", window.StatusText.Text);
        Assert.Equal("Profile searches 2/8", secondary.Text);
        Assert.Equal(progress >= 0 ? 0.25 : -0.5, StatusBarManager.Progress);
    }

    [AvaloniaTheory]
    [InlineData(SolverTermination.Converged)]
    [InlineData(SolverTermination.Cancelled)]
    [InlineData(SolverTermination.Failed)]
    public void CompletionRestoresTheCurrentProjectNameAndHidesProgress(SolverTermination termination)
    {
        var solver = new SolverInterface { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };
        solver.Analyze();
        solver.PublishProgress(FittingPhase.EstimatingUncertainty, 1, 3, 1f / 3);
        StatusBarManager.SetDefaultSecondaryStatus("Example project [M]");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Bootstrap 1/3", secondary.Text);

        // An unrelated solver's completion must not clear the active operation.
        new SolverInterface().ReportAnalysisFinished(Convergence(termination));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Bootstrap 1/3", secondary.Text);

        solver.ReportAnalysisFinished(Convergence(termination));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Example project [M]", secondary.Text);
        Assert.Equal(-1, StatusBarManager.Progress);
    }

    [AvaloniaFact]
    public void WorkerUpdatesCaptureTheirCountersBeforeTheUiHandlesThem()
    {
        var solver = new SolverInterface();
        solver.Analyze();
        var counters = new List<string>();
        EventHandler<string> handler = (_, text) => counters.Add(text);
        StatusBarManager.SecondaryStatusUpdated += handler;
        try
        {
            Task.Run(() =>
            {
                solver.ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
                solver.PublishProgress(FittingPhase.EstimatingUncertainty, 1, 3, 1f / 3);
                solver.PublishProgress(FittingPhase.EstimatingUncertainty, 2, 3, 2f / 3);
            }).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new[] { "Bootstrap 1/3", "Bootstrap 2/3" }, counters);
            Assert.Equal("Bootstrap 2/3", secondary.Text);
        }
        finally
        {
            StatusBarManager.SecondaryStatusUpdated -= handler;
        }
    }

    [AvaloniaFact]
    public void QueuedWorkerUpdatesCannotReplaceTheProjectNameAfterCompletion()
    {
        var solver = new SolverInterface();
        solver.Analyze();
        Task.Run(() => solver.PublishProgress(FittingPhase.EstimatingUncertainty, 1, 3, 1f / 3)).GetAwaiter().GetResult();

        solver.ReportAnalysisFinished(Convergence(SolverTermination.Converged));
        StatusBarManager.SetStatus("Analysis result updated", 0);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Example project", secondary.Text);
        Assert.Equal("Analysis result updated", window.StatusText.Text);
        Assert.Equal(-1, StatusBarManager.Progress);
    }

    [AvaloniaFact]
    public void NewOperationRejectsDelayedProgressAndCompletionFromPreviousOperation()
    {
        var first = new SolverInterface();
        using (var lease = ProcessingActivity.TryBegin()!)
        {
            using var ownership = lease.Enter();
            first.Analyze();
            Task.Run(() => first.PublishProgress(FittingPhase.EstimatingUncertainty, 9, 10, 0.9f)).GetAwaiter().GetResult();
        }
        using var next = ProcessingActivity.TryBegin()!;
        using var nextOwnership = next.Enter();
        var second = new SolverInterface();
        second.Analyze();
        second.PublishProgress(FittingPhase.AssessingBinding);
        first.ReportAnalysisFinished(Convergence(SolverTermination.Converged));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Assessing binding…", window.StatusText.Text);
        Assert.DoesNotContain("9/10", secondary.Text ?? "");
    }

    [AvaloniaFact]
    public void NamedProgressRespectsTheSharedNameGateAndTailTrimming()
    {
        var previous = FittingStatusPresentation.ShowExperimentNames;
        var solver = new SolverInterface();
        solver.Analyze();
        try
        {
            FittingStatusPresentation.ShowExperimentNames = true;
            var name = new string('x', 300);
            solver.PublishProgress(FittingPhase.AssessingBinding, memberName: name);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Assessing binding: " + name, window.StatusText.Text);
            Assert.Equal(global::Avalonia.Media.TextTrimming.CharacterEllipsis, window.StatusText.TextTrimming);
            FittingStatusPresentation.ShowExperimentNames = false;
            solver.PublishProgress(FittingPhase.AssessingBinding, memberName: name);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Assessing binding…", window.StatusText.Text);
        }
        finally { FittingStatusPresentation.ShowExperimentNames = previous; }
    }

    [AvaloniaFact]
    public void QueuedUncertaintyProgressCannotRestoreTextOrCounterAfterStop()
    {
        var solver = new SolverInterface { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };
        solver.Analyze();
        solver.PublishProgress(FittingPhase.EstimatingUncertainty, 2, 10, 0.2f);
        Task.Run(() => solver.PublishProgress(FittingPhase.EstimatingUncertainty, 5, 10, 0.5f))
            .GetAwaiter().GetResult();
        try
        {
            SolverInterface.TerminateAnalysisFlag.Raise();
            solver.PublishProgress(FittingPhase.Stopping);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(FittingStatusPresentation.StoppingAnalysis, window.StatusText.Text);
            Assert.Equal("Example project", secondary.Text);
            Assert.Equal(0.5, StatusBarManager.Progress, precision: 6);

            // Once observed, stopping stays latched for this operation.
            SolverInterface.TerminateAnalysisFlag.Lower();
            solver.PublishProgress(FittingPhase.EstimatingUncertainty, 6, 10, 0.6f);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(FittingStatusPresentation.StoppingAnalysis, window.StatusText.Text);
            Assert.Equal("Example project", secondary.Text);
            Assert.Equal(0.6, StatusBarManager.Progress, precision: 6);
        }
        finally { SolverInterface.TerminateAnalysisFlag.Lower(); }
    }

    [AvaloniaFact]
    public void DelayedAdvancedEventsCannotReplaceANewRunOrReachAnUnrelatedWorkspace()
    {
        var delayed = new QueuedDispatcher();
        var workspace = new AnalysisResultWorkspaceControl();
        var resultWindow = new Window { Content = workspace };
        var statuses = new List<string>();
        workspace.StatusChanged += (_, value) => statuses.Add(value);
        resultWindow.Show();
        try
        {
            using (var first = ProcessingActivity.TryBegin()!)
            {
                using var ownership = first.Enter();
                PlatformServices.RegisterMainThreadDispatcher(delayed);
                ResultAnalysisController.ReportCalculationStarted();
                ResultAnalysisController.ReportCalculationProgress(9, 10, "Old analysis");
                ResultAnalysisController.ReportAnalysisFinished(new AdvancedAnalysis(null!), 9, TimeSpan.FromSeconds(1));
            }
            using var next = ProcessingActivity.TryBegin()!;
            using var nextOwnership = next.Enter();
            PlatformServices.RegisterMainThreadDispatcher(null!);
            ResultAnalysisController.ReportCalculationStarted();
            ResultAnalysisController.ReportCalculationProgress(3, 4);
            delayed.Drain();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.75, StatusBarManager.Progress, precision: 6);
            Assert.Empty(statuses);
            Assert.DoesNotContain("Old analysis", window.StatusText.Text ?? "");
        }
        finally
        {
            PlatformServices.RegisterMainThreadDispatcher(null!);
            resultWindow.Close();
        }
    }

    sealed class QueuedDispatcher : IMainThreadDispatcher
    {
        readonly List<Action> actions = new();
        public void Invoke(Action action) => actions.Add(action);
        public void Drain() { foreach (var action in actions) action(); actions.Clear(); }
    }

    public void Dispose()
    {
        coordinator.Dispose();
        StatusBarManager.ClearAppStatus();
        window.Close();
        StatusBarManager.SetDefaultSecondaryStatus("");
        SolverUpdate.Step = previousStep;
        SolverUpdate.TotalSteps = previousTotal;
        PlatformServices.RegisterMainThreadDispatcher(previousDispatcher);
    }

    static SolverConvergence Convergence(SolverTermination termination) =>
        SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot { Termination = termination });
}
