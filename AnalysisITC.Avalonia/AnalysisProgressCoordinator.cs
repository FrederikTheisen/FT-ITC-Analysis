using System;
using System.Threading;

using Avalonia.Threading;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;

namespace AnalysisITC.Avalonia;

/// <summary>
/// Owns application-level analysis progress independently of workspace lifetime.
/// </summary>
internal sealed class AnalysisProgressCoordinator : IDisposable
{
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
    Guid operationId;
    bool requiresCurrentOperation;
    bool stopping;

    public AnalysisProgressCoordinator()
    {
        SolverInterface.AnalysisStarted += OnSolverStarted;
        SolverInterface.AnalysisStepFinished += OnAnalysisStepFinished;
        SolverInterface.ProgressChanged += OnFittingProgress;
        SolverInterface.FittingCompleted += OnSolverFinished;

        ResultAnalysisController.AnalysisStarted += OnAdvancedAnalysisStarted;
        ResultAnalysisController.IterationFinished += OnAdvancedAnalysisIterationFinished;
        ResultAnalysisController.AnalysisFinished += OnAdvancedAnalysisFinished;
    }

    void OnSolverStarted(object? sender, TerminationFlag e)
    {
        if (sender is SolverInterface { Silent: true }) return;
        if (sender is SolverInterface { Operation: not null } solver
            && !ProcessingActivity.IsCurrentOperation(solver.OperationId)) return;

        BeginOperation(OperationKind.Solver, sender);
    }

    void OnAnalysisStepFinished(object? sender, EventArgs e)
    {
        // Preserve the existing indicator reset without inferring a phase.
        lock (stateLock)
            if (!ReferenceEquals(activeSolver, sender)) return;
        UpdateProgress(OperationKind.Solver, 0);
    }

    void OnFittingProgress(object? sender, FittingProgress progress)
    {
        lock (stateLock)
        {
            if (!ReferenceEquals(activeSolver, sender) || operationId != progress.OperationId) return;
            if (progress.Phase == FittingPhase.Stopping) stopping = true;
        }
        UpdateProgress(OperationKind.Solver, progress.Fraction ?? -1,
            FittingStatusPresentation.Message(progress), FittingStatusPresentation.Counter(progress));
    }

    void OnSolverFinished(object? sender, FittingCompletion completion)
    {
        lock (stateLock)
        {
            if (!ReferenceEquals(activeSolver, sender) || operationId != completion.OperationId) return;
        }
        CompleteOperation(OperationKind.Solver, sender, () =>
        {
            if (completion.IsResultUpdate) StatusBarManager.ClearAppStatus();
            else UncertaintyEstimationPresentation.PublishCompletion(completion.Solver, completion.Convergence,
                    completion.Elapsed, operationId: completion.Solver.Operation == null ? Guid.Empty : completion.OperationId);
        });
    }

    void OnAdvancedAnalysisStarted(object? sender, TerminationFlag e)
    {
        if (sender is not AdvancedAnalysisEventSource source) return;
        if (source.OperationId != Guid.Empty && !ProcessingActivity.IsCurrentOperation(source.OperationId)) return;
        BeginOperation(OperationKind.AdvancedAnalysis, source.Analysis, source.OperationId);
    }

    void OnAdvancedAnalysisIterationFinished(object? sender, Tuple<int, int, float, string> e)
    {
        if (!MatchesAdvancedSource(sender)) return;
        UpdateProgress(OperationKind.AdvancedAnalysis, e.Item3);
    }

    void OnAdvancedAnalysisFinished(object? sender, Tuple<int, TimeSpan> e)
    {
        if (!MatchesAdvancedSource(sender)) return;
        CompleteOperation(OperationKind.AdvancedAnalysis, ((AdvancedAnalysisEventSource)sender!).Analysis);
    }

    bool MatchesAdvancedSource(object? sender)
    {
        if (sender is not AdvancedAnalysisEventSource source) return false;
        lock (stateLock)
            return !disposed && activeOperation == OperationKind.AdvancedAnalysis
                && operationId == source.OperationId && IsCurrent()
                && (source.Analysis == null || activeSolver == null || ReferenceEquals(source.Analysis, activeSolver));
    }

    void BeginOperation(OperationKind kind, object? solver, Guid capturedOperationId = default)
    {
        long token;

        lock (stateLock)
        {
            if (disposed) return;

            token = ++generation;
            activeOperation = kind;
            activeSolver = solver;
            stopping = false;
            operationId = (solver as SolverInterface)?.OperationId ?? capturedOperationId;
            requiresCurrentOperation = solver is SolverInterface fit ? fit.Operation != null : operationId != Guid.Empty;
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
            bool showStopping;
            lock (stateLock)
            {
                // Evaluate at delivery: older snapshots can be queued before Stop.
                if (kind == OperationKind.Solver && SolverInterface.TerminateAnalysisFlag.Up) stopping = true;
                showStopping = kind == OperationKind.Solver && stopping;
            }
            if (progress >= 0) StatusBarManager.SetProgress(Math.Clamp(progress, 0, 1));
            if (showStopping)
            {
                StatusBarManager.SetStatus(FittingStatusPresentation.StoppingAnalysis, 0, priority: 1);
                StatusBarManager.SetSecondaryStatus("", 0);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(message)) StatusBarManager.SetStatus(message, 0, priority: 1);
                if (counter != null) StatusBarManager.SetSecondaryStatus(counter, 0);
            }
        });
    }

    void CompleteOperation(OperationKind kind, object? solver, Action? completion = null)
    {
        long token;

        lock (stateLock)
        {
            if (disposed || activeOperation != kind) return;
            if (activeSolver != null && !ReferenceEquals(activeSolver, solver)) return;

            activeOperation = OperationKind.None;
            activeSolver = null;
            token = ++generation;
        }

        DispatchCompletion(token, completion ?? (() => StatusBarManager.ClearAppStatus()));
    }

    void Dispatch(long token, OperationKind kind, Action action)
    {
        RunOnUiThread(() =>
        {
            lock (stateLock)
            {
                if (disposed || generation != token || activeOperation != kind || !IsCurrent()) return;
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
                if (disposed || generation != token || activeOperation != OperationKind.None || !IsCurrent()) return;
            }

            action();
        });
    }

    bool IsCurrent() => !requiresCurrentOperation || ProcessingActivity.IsCurrentOperation(operationId);

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
        SolverInterface.ProgressChanged -= OnFittingProgress;
        SolverInterface.FittingCompleted -= OnSolverFinished;

        ResultAnalysisController.AnalysisStarted -= OnAdvancedAnalysisStarted;
        ResultAnalysisController.IterationFinished -= OnAdvancedAnalysisIterationFinished;
        ResultAnalysisController.AnalysisFinished -= OnAdvancedAnalysisFinished;
    }
}
