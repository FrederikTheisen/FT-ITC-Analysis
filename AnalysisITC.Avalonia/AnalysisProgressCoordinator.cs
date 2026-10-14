using System;
using System.Threading;

using Avalonia.Threading;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Platform;

namespace AnalysisITC.Avalonia;

/// <summary>
/// Owns application-level analysis progress independently of workspace lifetime.
/// </summary>
internal sealed class AnalysisProgressCoordinator : IDisposable
{
    static readonly TimeSpan FitNotificationThreshold = TimeSpan.FromSeconds(15);
    enum OperationKind
    {
        None,
        Solver,
        AdvancedAnalysis
    }

    readonly object stateLock = new();

    long generation;
    OperationKind activeOperation;
    object? activeSolver;
    bool disposed;

    public AnalysisProgressCoordinator()
    {
        SolverInterface.AnalysisStarted += OnSolverStarted;
        SolverInterface.AnalysisStepFinished += OnAnalysisStepFinished;
        SolverInterface.ErrorEstimationIterationCompleted += OnErrorEstimationIterationCompleted;
        SolverInterface.SolverUpdated += OnSolverUpdated;
        SolverInterface.AnalysisFinished += OnSolverFinished;

        ResultAnalysisController.AnalysisStarted += OnAdvancedAnalysisStarted;
        ResultAnalysisController.IterationFinished += OnAdvancedAnalysisIterationFinished;
        ResultAnalysisController.AnalysisFinished += OnAdvancedAnalysisFinished;
    }

    void OnSolverStarted(object? sender, TerminationFlag e)
    {
        if (sender is SolverInterface { Silent: true }) return;

        BeginOperation(OperationKind.Solver, sender);
    }

    void OnAnalysisStepFinished(object? sender, EventArgs e)
    {
        string? message;
        lock (stateLock)
        {
            message = activeSolver is SolverInterface { ErrorEstimationMethod: not ErrorEstimationMethod.None }
                ? "Estimating uncertainty..."
                : null;
        }
        UpdateProgress(OperationKind.Solver, 0, message);
    }

    void OnErrorEstimationIterationCompleted(object? sender, Tuple<int, int, float> e)
    {
        UpdateProgress(OperationKind.Solver, e.Item3,
            "Estimating uncertainty...", $"{e.Item1}/{e.Item2}");
    }

    void OnSolverUpdated(object? sender, SolverUpdate update)
    {
        // SolverUpdate's counters are shared mutable state. Capture the text
        // before posting to the UI thread, while it still describes this event.
        var counter = SolverUpdate.TotalSteps > 0 ? update.ProgressString : null;
        UpdateProgress(OperationKind.Solver, update.Progress, update.Message, counter);
    }

    void OnSolverFinished(object? sender, SolverConvergence convergence)
    {
        if (sender is SolverInterface { Silent: false }
            && !convergence.Stopped
            && convergence.TotalTime >= FitNotificationThreshold)
            PlatformServices.AppNotificationService.ShowSystemNotificationIfBackground(
                "Model fitting finished", convergence.Message);

        CompleteOperation(OperationKind.Solver, sender);
    }

    void OnAdvancedAnalysisStarted(object? sender, TerminationFlag e)
    {
        BeginOperation(OperationKind.AdvancedAnalysis, null);
    }

    void OnAdvancedAnalysisIterationFinished(object? sender, Tuple<int, int, float, string> e)
    {
        UpdateProgress(OperationKind.AdvancedAnalysis, e.Item3);
    }

    void OnAdvancedAnalysisFinished(object? sender, Tuple<int, TimeSpan> e)
    {
        CompleteOperation(OperationKind.AdvancedAnalysis, null);
    }

    void BeginOperation(OperationKind kind, object? solver)
    {
        long token;

        lock (stateLock)
        {
            if (disposed) return;

            token = ++generation;
            activeOperation = kind;
            activeSolver = kind == OperationKind.Solver ? solver : null;
        }

        Dispatch(token, kind, () => StatusBarManager.StartInderminateProgress());
    }

    void UpdateProgress(OperationKind kind, double progress, string? message = null, string? counter = null)
    {
        long token;

        lock (stateLock)
        {
            if (disposed || activeOperation != kind) return;
            token = generation;
        }

        Dispatch(token, kind, () =>
        {
            if (progress >= 0) StatusBarManager.SetProgress(Math.Clamp(progress, 0, 1));
            if (!string.IsNullOrWhiteSpace(message)) StatusBarManager.SetStatus(message, 0, priority: 1);
            if (counter != null) StatusBarManager.SetSecondaryStatus(counter, 0);
        });
    }

    void CompleteOperation(OperationKind kind, object? solver)
    {
        long token;

        lock (stateLock)
        {
            if (disposed || activeOperation != kind) return;
            if (kind == OperationKind.Solver && activeSolver != null && !ReferenceEquals(activeSolver, solver)) return;

            activeOperation = OperationKind.None;
            activeSolver = null;
            token = ++generation;
        }

        DispatchCompletion(token, () => StatusBarManager.ClearAppStatus());
    }

    void Dispatch(long token, OperationKind kind, Action action)
    {
        RunOnUiThread(() =>
        {
            lock (stateLock)
            {
                if (disposed || generation != token || activeOperation != kind) return;
            }

            action();
        });
    }

    void DispatchCompletion(long token, Action action)
    {
        RunOnUiThread(() =>
        {
            lock (stateLock)
            {
                if (disposed || generation != token || activeOperation != OperationKind.None) return;
            }

            action();
        });
    }

    static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    public void Dispose()
    {
        lock (stateLock)
        {
            if (disposed) return;

            disposed = true;
            activeOperation = OperationKind.None;
            activeSolver = null;
            Interlocked.Increment(ref generation);
        }

        SolverInterface.AnalysisStarted -= OnSolverStarted;
        SolverInterface.AnalysisStepFinished -= OnAnalysisStepFinished;
        SolverInterface.ErrorEstimationIterationCompleted -= OnErrorEstimationIterationCompleted;
        SolverInterface.SolverUpdated -= OnSolverUpdated;
        SolverInterface.AnalysisFinished -= OnSolverFinished;

        ResultAnalysisController.AnalysisStarted -= OnAdvancedAnalysisStarted;
        ResultAnalysisController.IterationFinished -= OnAdvancedAnalysisIterationFinished;
        ResultAnalysisController.AnalysisFinished -= OnAdvancedAnalysisFinished;
    }
}
