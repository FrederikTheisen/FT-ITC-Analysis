using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Threading;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
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
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Estimating uncertainty...", window.StatusText.Text);

        if (method == ErrorEstimationMethod.BootstrapResiduals)
            solver.ReportBootstrapProgress(40);
        else
            solver.ReportLeaveOneOutProgress(2, 5);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Estimating uncertainty...", window.StatusText.Text);
        Assert.Equal(method == ErrorEstimationMethod.BootstrapResiduals ? "40/100" : "2/5", secondary.Text);
        Assert.Equal(0.4, StatusBarManager.Progress, precision: 6);
    }

    [AvaloniaTheory]
    [InlineData(0.25f)]
    [InlineData(-1f)]
    public void SolverMessagesAndCountersReachTheStatusBarEvenWithoutAProgressValue(float progress)
    {
        var solver = new SolverInterface();
        solver.Analyze();
        solver.ReportSolverUpdate(new SolverUpdate(2, 8)
        {
            Message = "Profile likelihood: endpoints found 2/8",
            Progress = progress,
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Profile likelihood: endpoints found 2/8", window.StatusText.Text);
        Assert.Equal("2/8", secondary.Text);
        Assert.Equal(progress >= 0 ? 0.25 : -0.5, StatusBarManager.Progress);
    }

    [AvaloniaTheory]
    [InlineData(SolverTermination.Converged)]
    [InlineData(SolverTermination.Cancelled)]
    [InlineData(SolverTermination.Failed)]
    public void CompletionRestoresTheCurrentProjectNameAndHidesProgress(SolverTermination termination)
    {
        var solver = new SolverInterface();
        solver.Analyze();
        solver.ReportSolverUpdate(new SolverUpdate(1, 3) { Message = "Estimating uncertainty...", Progress = 1f / 3 });
        StatusBarManager.SetDefaultSecondaryStatus("Example project [M]");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("1/3", secondary.Text);

        // An unrelated solver's completion must not clear the active operation.
        new SolverInterface().ReportAnalysisFinished(Convergence(termination));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("1/3", secondary.Text);

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
                solver.ReportSolverUpdate(new SolverUpdate(1, 3) { Progress = 1f / 3 });
                solver.ReportSolverUpdate(new SolverUpdate(2, 3) { Progress = 2f / 3 });
            }).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new[] { "1/3", "2/3" }, counters);
            Assert.Equal("2/3", secondary.Text);
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
        Task.Run(() => solver.ReportSolverUpdate(new SolverUpdate(1, 3)
        {
            Message = "Estimating uncertainty...", Progress = 1f / 3,
        })).GetAwaiter().GetResult();

        solver.ReportAnalysisFinished(Convergence(SolverTermination.Converged));
        StatusBarManager.SetStatus("Analysis result updated", 0);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Example project", secondary.Text);
        Assert.Equal("Analysis result updated", window.StatusText.Text);
        Assert.Equal(-1, StatusBarManager.Progress);
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
