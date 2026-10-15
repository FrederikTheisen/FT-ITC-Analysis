using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Core.Application;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(SolverEventCollectionDefinition.Name)]
public sealed class ProcessingOperationTimingTests
{
    [Fact]
    public void ErrorDialogKeepsAdmissionWithoutAdvancingCalculationTime()
    {
        using var activity = ProcessingActivity.TryBegin();
        using var ownership = activity.Enter();
        var shown = false;
        EventHandler<HandledException> dialog = (sender, _) =>
        {
            shown = true;
            Assert.Same(activity.Operation, sender);
            Assert.True(ProcessingActivity.IsBusy);
            Assert.Null(ProcessingActivity.TryBegin());
            var atOpen = activity.Operation.Elapsed;
            Thread.Sleep(100); // Simulate time waiting for Continue in a modal error dialog.
            Assert.Equal(atOpen, activity.Operation.Elapsed);
        };
        AppEventHandler.ShowAppMessage += dialog;
        var wall = Stopwatch.StartNew();
        var before = activity.Operation.Elapsed;
        try { AppEventHandler.DisplayHandledException(new InvalidOperationException("Timing regression fixture")); }
        finally { AppEventHandler.ShowAppMessage -= dialog; }
        Assert.True(shown);
        Assert.True(wall.Elapsed - (activity.Operation.Elapsed - before) >= TimeSpan.FromMilliseconds(80));
        Assert.True(ProcessingActivity.IsBusy);
    }

    [Fact]
    public async Task OverlappingAsyncDialogsResumeOnceAndNeverRestartAFinishedOperation()
    {
        var activity = ProcessingActivity.TryBegin();
        var operation = activity.Operation;
        var first = operation.PauseElapsed();
        var second = operation.PauseElapsed();
        try
        {
            var paused = operation.Elapsed;
            first.Dispose();
            await Task.Delay(30);
            Assert.Equal(paused, operation.Elapsed);
            second.Dispose();
            await Task.Delay(30);
            Assert.True(operation.Elapsed > paused);
            using var lastDialog = operation.PauseElapsed();
            activity.Dispose();
            var terminal = operation.Elapsed;
            lastDialog.Dispose();
            await Task.Delay(30);
            Assert.Equal(terminal, operation.Elapsed);
        }
        finally { first.Dispose(); second.Dispose(); activity.Dispose(); }
    }
}
