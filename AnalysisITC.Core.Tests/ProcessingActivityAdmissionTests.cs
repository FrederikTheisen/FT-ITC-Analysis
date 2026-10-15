using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Core.Application;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(SolverEventCollectionDefinition.Name)]
public sealed class ProcessingActivityAdmissionTests
{
    [Fact]
    public async Task SimultaneousUserStartsAdmitExactlyOneOperation()
    {
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 24).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return ProcessingActivity.TryBegin();
        })).ToArray();
        start.SetResult(true);
        var leases = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var admitted = Assert.Single(leases, lease => lease != null);
            Assert.True(ProcessingActivity.IsBusy);
            Assert.True(ProcessingActivity.IsCurrentOperation(admitted.Operation.Id));
            Assert.Null(ProcessingActivity.Current); // Admission alone never confers ambient ownership.
        }
        finally { foreach (var lease in leases) lease?.Dispose(); }
        Assert.False(ProcessingActivity.IsBusy);
    }

    [Fact]
    public async Task ExplicitlyOwnedNestedAndParallelWorkJoinsTheRoot()
    {
        using var root = ProcessingActivity.TryBegin();
        Assert.NotNull(root);
        using var scope = root.Enter();
        Assert.Same(root.Operation, ProcessingActivity.Current);
        Assert.Null(ProcessingActivity.TryBegin()); // A reentrant user start must still fail.

        var children = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            using var child = await ProcessingActivity.BeginAsync();
            using var childScope = child.Enter();
            Assert.Same(root.Operation, ProcessingActivity.Current);
            return child.Operation.Id;
        }))).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.All(children, id => Assert.Equal(root.Operation.Id, id));
        Assert.True(ProcessingActivity.IsBusy);
        using (var nested = ProcessingActivity.Begin())
            Assert.Same(root.Operation, nested.Operation);
        Assert.Same(root.Operation, ProcessingActivity.Current);
    }

    [Fact]
    public async Task PendingBackgroundWorkHasPriorityAndNeverPublishesIdleAtHandoff()
    {
        var changes = new List<bool>();
        EventHandler changed = (_, _) => changes.Add(ProcessingActivity.IsBusy);
        ProcessingActivity.Changed += changed;
        var root = ProcessingActivity.TryBegin();
        var requested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ProcessingActivity.ProcessingLease> pending;
        // An unrelated calculation must not inherit ownership from its initiator.
        using (ExecutionContext.SuppressFlow())
            pending = Task.Run(async () =>
            {
                var wait = ProcessingActivity.BeginAsync();
                requested.SetResult(true);
                return await wait;
            });
        ProcessingActivity.ProcessingLease background = null;
        try
        {
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(pending.IsCompleted);
            Assert.Null(ProcessingActivity.TryBegin());
            Assert.False(ProcessingActivity.TryEditInput(() => throw new Exception("The edit must not run.")));
            Assert.False(ProcessingActivity.TryCaptureSnapshot(() => throw new Exception("The save must not run."), out object snapshot));
            Assert.Null(snapshot);
            root.Dispose();
            background = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(ProcessingActivity.IsBusy);
            Assert.NotEqual(root.Operation.Id, background.Operation.Id);
            Assert.False(ProcessingActivity.IsCurrentOperation(root.Operation.Id));
            Assert.True(ProcessingActivity.IsCurrentOperation(background.Operation.Id));
            Assert.Equal(new[] { true }, changes);
            Assert.Null(ProcessingActivity.TryBegin());
            using (background.Enter()) Assert.Same(background.Operation, ProcessingActivity.Current);
            background.Dispose();
            Assert.Equal(new[] { true, false }, changes);
            Assert.False(ProcessingActivity.IsBusy);
            Assert.True(ProcessingActivity.TryEditInput(() => { }));
            Assert.True(ProcessingActivity.TryCaptureSnapshot(() => "saved", out var saved));
            Assert.Equal("saved", saved);
        }
        finally
        {
            root?.Dispose();
            background ??= await pending.WaitAsync(TimeSpan.FromSeconds(10));
            background.Dispose();
            ProcessingActivity.Changed -= changed;
        }
    }

    [Fact]
    public void RootCannotEndUntilEveryOwnedLeaseHasBeenReleased()
    {
        var root = ProcessingActivity.TryBegin();
        using var scope = root.Enter();
        var child = ProcessingActivity.Begin();
        try
        {
            root.Dispose();
            root.Dispose();
            Assert.True(ProcessingActivity.IsBusy);
            Assert.Null(ProcessingActivity.TryBegin());
            child.Dispose();
            Assert.False(ProcessingActivity.IsBusy);
            Assert.Throws<InvalidOperationException>(() => root.Enter());
            Assert.Throws<InvalidOperationException>(() => child.Enter());
        }
        finally { child.Dispose(); root.Dispose(); }
    }

    [Fact]
    public async Task QueuedTimeIsExcludedAndElapsedStopsAtTerminalRelease()
    {
        var root = ProcessingActivity.TryBegin();
        Task<ProcessingActivity.ProcessingLease> pending;
        var requested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ExecutionContext.SuppressFlow()) pending = Task.Run(async () =>
        {
            var wait = ProcessingActivity.BeginAsync();
            requested.SetResult(true);
            return await wait;
        });
        ProcessingActivity.ProcessingLease queued = null;
        try
        {
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(100);
            var occupied = root.Operation.Elapsed;
            root.Dispose();
            queued = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(occupied >= TimeSpan.FromMilliseconds(50));
            Assert.True(queued.Operation.Elapsed < occupied, "Waiting for admission is not calculation time.");
            using (queued.Enter())
            {
                await Task.Delay(30); // Preparation/publication work belongs to the whole-operation clock.
                Assert.True(queued.Operation.Elapsed >= TimeSpan.FromMilliseconds(20));
            }
            queued.Dispose();
            var terminalElapsed = queued.Operation.Elapsed;
            await Task.Delay(25);
            Assert.Equal(terminalElapsed, queued.Operation.Elapsed);
        }
        finally
        {
            root.Dispose();
            queued ??= await pending.WaitAsync(TimeSpan.FromSeconds(10));
            queued.Dispose();
        }
    }

    [Fact]
    public void ANewAdmissionInvalidatesThePreviousOperationIdentity()
    {
        var first = ProcessingActivity.TryBegin();
        var oldId = first.Operation.Id;
        first.Dispose();
        Assert.True(ProcessingActivity.IsCurrentOperation(oldId)); // Terminal UI delivery is still admissible.
        using var second = ProcessingActivity.TryBegin();
        Assert.NotEqual(oldId, second.Operation.Id);
        Assert.False(ProcessingActivity.IsCurrentOperation(oldId));
        Assert.False(ProcessingActivity.IsCurrentOperation(Guid.Empty));
        Assert.True(ProcessingActivity.IsCurrentOperation(second.Operation.Id));
    }

    [Fact]
    public async Task MainThreadSynchronousAdmissionRejectsUnownedBusyWorkWithoutQueuingIt()
    {
        var previous = PlatformServices.MainThreadDispatcher;
        PlatformServices.RegisterMainThreadDispatcher(new MainThreadDispatcher());
        var root = ProcessingActivity.TryBegin();
        var attempt = Task.Run(() => Record.Exception(() =>
        {
            using var rejected = ProcessingActivity.Begin();
        }));
        try
        {
            var error = await attempt.WaitAsync(TimeSpan.FromSeconds(2));
            var rejection = Assert.IsType<InvalidOperationException>(error);
            Assert.Contains("cannot wait for admission on the main thread", rejection.Message);
            Assert.True(ProcessingActivity.IsBusy);
            Assert.True(ProcessingActivity.IsCurrentOperation(root.Operation.Id));
            root.Dispose();
            Assert.False(ProcessingActivity.IsBusy); // Rejected work left no pending admission behind.
        }
        finally
        {
            root.Dispose();
            await attempt.WaitAsync(TimeSpan.FromSeconds(5));
            PlatformServices.RegisterMainThreadDispatcher(previous);
        }
    }

    [Fact]
    public void MainThreadSynchronousWorkMayStillJoinAnExplicitlyEnteredOwner()
    {
        var previous = PlatformServices.MainThreadDispatcher;
        PlatformServices.RegisterMainThreadDispatcher(new MainThreadDispatcher());
        try
        {
            using var root = ProcessingActivity.TryBegin();
            using var ownership = root.Enter();
            using var child = ProcessingActivity.Begin();
            Assert.Same(root.Operation, child.Operation);
            Assert.Same(root.Operation, ProcessingActivity.Current);
            Assert.Null(ProcessingActivity.TryBegin());
        }
        finally { PlatformServices.RegisterMainThreadDispatcher(previous); }
        Assert.False(ProcessingActivity.IsBusy);
    }

    sealed class MainThreadDispatcher : IMainThreadDispatcher, IMainThreadAccessChecker
    {
        public bool IsMainThread => true;
        public void Invoke(Action action) => action();
    }
}
