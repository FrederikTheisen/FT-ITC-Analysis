using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(SolverEventCollectionDefinition.Name)]
public sealed class FittingProgressOrderingTests
{
    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, false)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, false)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, true)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, true)]
    public void ParallelRefitsCannotLowerTheCounterOrFraction(ErrorEstimationMethod method, bool singleMember)
    {
        using var capture = new ProgressCapture(method, singleMember ? 1 : 3);
        var position = singleMember ? 1 : 2;
        var name = singleMember ? "Sample A" : "Sample B";
        var child = new Solver { Silent = true, BootstrapIterations = 10 };
        child.SetProgressOwner(capture.Owner, position, name);
        using var higherReported = new ManualResetEventSlim();
        var completedRefits = 0;
        Parallel.For(0, 2, new ParallelOptions { MaxDegreeOfParallelism = 2 }, _ =>
        {
            var completed = Interlocked.Increment(ref completedRefits);
            if (completed == 1) Assert.True(higherReported.Wait(TimeSpan.FromSeconds(10)));
            if (method == ErrorEstimationMethod.BootstrapResiduals) child.ReportBootstrapProgress(completed);
            else child.ReportLeaveOneOutProgress(completed, 10);
            if (completed == 2) higherReported.Set();
        });
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 3, 10, .3f, position, name);
        capture.Drain();

        Assert.Equal(new int?[] { 2, 2, 3 }, capture.Received.Select(progress => progress.Completed));
        Assert.Equal(new float?[] { .2f, .2f, .3f }, capture.Received.Select(progress => progress.Fraction));
        Assert.All(capture.Received, progress =>
        {
            Assert.Equal(position, progress.MemberPosition);
            Assert.Equal(name, progress.MemberName);
            Assert.Equal(10, progress.Total);
        });
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    public void StandaloneRefitsKeepMonotonicStructuredCountersAndFractions(ErrorEstimationMethod method)
    {
        using var capture = new ProgressCapture(method, 1);
        var solver = new Solver { ErrorEstimationMethod = method, BootstrapIterations = 10 };
        var structured = new List<FittingProgress>();
        EventHandler<FittingProgress> onStructured = (sender, update) =>
        {
            if (ReferenceEquals(sender, solver)) structured.Add(update);
        };
        SolverInterface.ProgressChanged += onStructured;
        try
        {
            if (method == ErrorEstimationMethod.BootstrapResiduals)
            { solver.ReportBootstrapProgress(2); solver.ReportBootstrapProgress(1); }
            else
            { solver.ReportLeaveOneOutProgress(2, 10); solver.ReportLeaveOneOutProgress(1, 10); }
            capture.Drain();

            Assert.Equal(new int?[] { 2, 2 }, structured.Select(update => update.Completed));
            Assert.Equal(new float?[] { .2f, .2f }, structured.Select(update => update.Fraction));
        }
        finally
        {
            SolverInterface.ProgressChanged -= onStructured;
        }
    }

    [Fact]
    public void ReorderedDispatchCannotReorderTheOwningSolversNotifications()
    {
        using var capture = new ProgressCapture();
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 1, 10, .1f, 1, "Sample A");
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 2, 10, .2f, 1, "Sample A");
        capture.Drain(reverse: true);

        Assert.Equal(new int?[] { 1, 2 }, capture.Received.Select(progress => progress.Completed));
        Assert.Equal(new float?[] { .1f, .2f }, capture.Received.Select(progress => progress.Fraction));
    }

    [Fact]
    public void QueuedMemberSnapshotsRetainTheirCaptureOrderAcrossMemberChanges()
    {
        using var capture = new ProgressCapture();
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 9, 10, .3f, 1, "Sample A");
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 1, 10, .7f, 3, "Sample C");
        capture.Drain(reverse: true);

        Assert.Equal(new float?[] { .3f, .7f }, capture.Received.Select(progress => progress.Fraction));
        Assert.Equal(new[] { 1, 3 }, capture.Received.Select(progress => progress.MemberPosition));
        Assert.Equal(new[] { "Sample A", "Sample C" }, capture.Received.Select(progress => progress.MemberName));
        Assert.Equal(new int?[] { 9, 1 }, capture.Received.Select(progress => progress.Completed));
    }

    [Fact]
    public void CounterRestartsForTheNextMemberAndClearsAtTheNextPhase()
    {
        using var capture = new ProgressCapture();
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 9, 10, .3f, 1, "Sample A");
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 0, 10, .4f, 3, "Sample C");
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 1, 10, .5f, 3, "Sample C");
        capture.Owner.PublishProgress(FittingPhase.AssessingBinding);
        capture.Drain();

        Assert.Equal(new int?[] { 9, 0, 1, null }, capture.Received.Select(progress => progress.Completed));
        Assert.Equal(new[] { 1, 3, 3, 3 }, capture.Received.Select(progress => progress.MemberPosition));
        Assert.Equal(FittingPhase.AssessingBinding, capture.Received.Last().Phase);
        Assert.Null(capture.Received.Last().Total);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    public void MembersShareStructuredProgressAcrossSkippedMembers(ErrorEstimationMethod method)
    {
        using var capture = new ProgressCapture(method);
        var first = new Solver { Silent = true, BootstrapIterations = 10 };
        var last = new Solver { Silent = true, BootstrapIterations = 10 };
        first.SetProgressOwner(capture.Owner, 1, "Sample A");
        last.SetProgressOwner(capture.Owner, 3, "Sample C");
        void Report(Solver solver, int completed)
        {
            if (method == ErrorEstimationMethod.BootstrapResiduals) solver.ReportBootstrapProgress(completed);
            else solver.ReportLeaveOneOutProgress(completed, 10);
        }
        SolverUpdate.TotalSteps = 3;
        Report(first, 6);
        capture.Owner.ReportSolverUpdate(new SolverUpdate(1, 3) { Progress = 1f / 3 });
        SolverUpdate.Step = 2; // The second member needs no refits.
        Report(last, 2);
        Report(last, 1);
        capture.Owner.ReportSolverUpdate(new SolverUpdate(3, 3) { Progress = 1 });
        SolverUpdate.Step = 8;
        SolverUpdate.TotalSteps = 9;
        capture.Drain(reverse: true);

        var expectedFractions = new[] { .2f, 1f / 3, 11f / 15, 11f / 15, 1f };
        Assert.Equal(expectedFractions.Length, capture.Received.Count);
        foreach (var (actual, expected) in capture.Received.Select(progress => progress.Fraction.Value).Zip(expectedFractions))
            Assert.Equal(expected, actual, 6);
        Assert.Equal(8, SolverUpdate.Step);
        Assert.Equal(9, SolverUpdate.TotalSteps);
        Assert.Equal(3, capture.Received.Last().MemberPosition);
        Assert.Equal(2, capture.Received.Last().Completed);
        Assert.Equal(1, capture.Received.Last().Fraction);
        for (var index = 1; index < capture.Received.Count; index++)
            Assert.True(capture.Received[index].Fraction >= capture.Received[index - 1].Fraction);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewRunResetsTheFractionAndDiscardsQueuedOldRunReports(bool queuedOldReport)
    {
        using var capture = new ProgressCapture();
        var owner = new SolverInterface { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };
        var received = new List<FittingProgress>();
        EventHandler<FittingProgress> onProgress = (sender, update) =>
        { if (ReferenceEquals(sender, owner)) received.Add(update); };
        SolverInterface.ProgressChanged += onProgress;
        try
        {
            owner.Analyze();
            capture.Drain();
            received.Clear();
            owner.PublishProgress(FittingPhase.EstimatingUncertainty, 8, 10, .8f);
            if (!queuedOldReport) capture.Drain();
            owner.Analyze();
            owner.PublishProgress(FittingPhase.EstimatingUncertainty, 0, 10, 0);
            owner.PublishProgress(FittingPhase.EstimatingUncertainty, 1, 10, .1f);
            capture.Drain();

            var uncertaintyProgress = received
                .Where(progress => progress.Phase == FittingPhase.EstimatingUncertainty).ToArray();
            Assert.Equal(queuedOldReport ? new float?[] { 0, .1f } : new float?[] { .8f, 0, .1f },
                uncertaintyProgress.Select(progress => progress.Fraction));
            Assert.Equal(1, uncertaintyProgress.Last().Completed);
        }
        finally
        {
            owner.ReportAnalysisFinished(SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            { Termination = SolverTermination.Converged }));
            capture.Drain();
            SolverInterface.ProgressChanged -= onProgress;
            StatusBarManager.ClearAppStatus();
        }
    }

    [Fact]
    public void ReusingAnOwnerRejectsOldRunAndOldMemberCallbacks()
    {
        using var capture = new ProgressCapture();
        var owner = new SolverInterface { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };
        var received = new List<FittingProgress>();
        EventHandler<FittingProgress> onProgress = (sender, update) =>
        { if (ReferenceEquals(sender, owner)) received.Add(update); };
        SolverInterface.ProgressChanged += onProgress;
        try
        {
            owner.Analyze();
            owner.PublishProgress(FittingPhase.EstimatingUncertainty, 9, 10, .9f);
            var oldChild = new Solver { Silent = true, BootstrapIterations = 10 };
            oldChild.SetProgressOwner(owner, 1, "old");
            var previousId = owner.OperationId;
            owner.Analyze();
            oldChild.ReportBootstrapProgress(10);
            owner.ReportBootstrapProgress(10); // Default request is 100 refits: 10%.
            capture.Drain(reverse: true);

            Assert.NotEqual(previousId, owner.OperationId);
            Assert.All(received, update => Assert.Equal(owner.OperationId, update.OperationId));
            Assert.Equal(new float?[] { null, .1f }, received.Select(update => update.Fraction));
        }
        finally
        {
            owner.ReportAnalysisFinished(SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            { Termination = SolverTermination.Converged }));
            capture.Drain();
            SolverInterface.ProgressChanged -= onProgress;
            StatusBarManager.ClearAppStatus();
        }
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    public void NullNegativeAndOtherPhaseSignalsDoNotResetOrAcquireTheRetainedFraction(ErrorEstimationMethod method)
    {
        using var capture = new ProgressCapture(method);
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, fraction: .8f);
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty);
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, fraction: -.5f);
        capture.Owner.PublishProgress(FittingPhase.AssessingBinding);
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, fraction: .2f);
        capture.Owner.PublishProgress(FittingPhase.Stopping);
        capture.Drain();

        Assert.Equal(new float?[] { .8f, null, -.5f, null, .8f, null },
            capture.Received.Select(progress => progress.Fraction));
        Assert.Null(capture.Received.Last().Completed);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood)]
    [InlineData(ErrorEstimationMethod.None)]
    public void OtherMethodsKeepTheirOriginalFractions(ErrorEstimationMethod method)
    {
        using var capture = new ProgressCapture(method);
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, fraction: .8f);
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, fraction: .2f);
        capture.Drain();
        Assert.Equal(new float?[] { .8f, .2f }, capture.Received.Select(progress => progress.Fraction));
    }

    [Fact]
    public async Task HeadlessObserversAreSerializedWithoutBlockingParallelReporters()
    {
        using var capture = new ProgressCapture();
        PlatformServices.RegisterMainThreadDispatcher(null);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var active = 0;
        var maximum = 0;
        EventHandler<FittingProgress> handler = (sender, update) =>
        {
            if (!ReferenceEquals(sender, capture.Owner)) return;
            var count = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, count);
            try
            {
                if (update.Completed == 1)
                {
                    entered.Set();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                }
            }
            finally { Interlocked.Decrement(ref active); }
        };
        SolverInterface.ProgressChanged += handler;
        Task first = Task.CompletedTask;
        try
        {
            first = Task.Run(() => capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 1, 10, .1f));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            await Task.Run(() => capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 2, 10, .2f))
                .WaitAsync(TimeSpan.FromSeconds(10));
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, maximum);
            Assert.Equal(new float?[] { .1f, .2f }, capture.Received.Select(update => update.Fraction));
        }
        finally
        {
            release.Set();
            try { await first.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { SolverInterface.ProgressChanged -= handler; }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObserverFailureDeliversPendingReportsBeforeRethrowingAndReleasesDelivery(bool immediate)
    {
        using var capture = new ProgressCapture();
        if (immediate) PlatformServices.RegisterMainThreadDispatcher(null);
        var failed = false;
        EventHandler<FittingProgress> handler = (sender, _) =>
        {
            if (!ReferenceEquals(sender, capture.Owner) || failed) return;
            failed = true;
            capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 2, 10, .2f);
            throw new InvalidOperationException("Expected observer failure");
        };
        SolverInterface.ProgressChanged += handler;
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 1, 10, .1f);
                capture.Drain();
            });
        }
        finally { SolverInterface.ProgressChanged -= handler; }
        // The report queued during the failing observer is delivered without a later report.
        Assert.Equal(new float?[] { .1f, .2f }, capture.Received.Select(update => update.Fraction));
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 3, 10, .3f);
        capture.Drain();
        Assert.Equal(new float?[] { .1f, .2f, .3f }, capture.Received.Select(update => update.Fraction));
    }

    [Fact]
    public void ObserverFailuresCannotStrandQueuedCompletionAndTheFirstFailureIsRethrown()
    {
        using var capture = new ProgressCapture();
        var owner = new SolverInterface { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };
        var order = new List<string>();
        EventHandler<FittingProgress> progress = (sender, _) =>
        {
            if (!ReferenceEquals(sender, owner)) return;
            order.Add("progress");
            throw new InvalidOperationException("First observer failure");
        };
        EventHandler<FittingCompletion> completed = (sender, _) =>
        {
            if (!ReferenceEquals(sender, owner)) return;
            order.Add("completed");
            throw new InvalidOperationException("Second observer failure");
        };
        EventHandler<SolverConvergence> finished = (sender, _) =>
        { if (ReferenceEquals(sender, owner)) order.Add("finished"); };
        SolverInterface.ProgressChanged += progress;
        SolverInterface.FittingCompleted += completed;
        SolverInterface.AnalysisFinished += finished;
        try
        {
            owner.ReportBootstrapProgress(50);
            owner.ReportAnalysisFinished(SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            { Termination = SolverTermination.Converged }));
            var failure = Assert.Throws<InvalidOperationException>(() => capture.Drain());

            Assert.Equal("First observer failure", failure.Message);
            Assert.Equal(new[] { "progress", "completed", "finished" }, order);
            capture.Drain();
            Assert.Equal(new[] { "progress", "completed", "finished" }, order);
        }
        finally
        {
            SolverInterface.ProgressChanged -= progress;
            SolverInterface.FittingCompleted -= completed;
            SolverInterface.AnalysisFinished -= finished;
        }
    }

    [Fact]
    public void StructuredProgressCapturesValuesWithoutChangingInputsOrStaticSteps()
    {
        using var capture = new ProgressCapture();
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty);
        var input = new SolverUpdate(1, 3) { Progress = .6f };
        var received = new List<FittingProgress>();
        EventHandler<FittingProgress> handler = (sender, update) =>
        { if (ReferenceEquals(sender, capture.Owner)) received.Add(update); };
        SolverInterface.ProgressChanged += handler;
        try
        {
            capture.Owner.ReportSolverUpdate(input);
            input.Progress = .2f;
            capture.Owner.ReportSolverUpdate(input);
            input.Progress = .01f;
            SolverUpdate.Step = 4;
            SolverUpdate.TotalSteps = 7;
            capture.Drain();

            var capturedValues = received.Where(update => update.Fraction.HasValue).ToArray();
            Assert.Equal(new float?[] { .6f, .6f }, capturedValues.Select(update => update.Fraction));
            Assert.NotSame(capturedValues[0], capturedValues[1]);
            Assert.Equal(.01f, input.Progress);
            Assert.Equal(4, SolverUpdate.Step);
            Assert.Equal(7, SolverUpdate.TotalSteps);
        }
        finally { SolverInterface.ProgressChanged -= handler; }
    }

    [Fact]
    public void CompletionCannotOvertakePreviouslyQueuedProgress()
    {
        using var capture = new ProgressCapture();
        var owner = new SolverInterface { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };
        var order = new List<string>();
        EventHandler<FittingProgress> progress = (sender, _) =>
        { if (ReferenceEquals(sender, owner)) order.Add("progress"); };
        EventHandler<FittingCompletion> completed = (sender, _) =>
        { if (ReferenceEquals(sender, owner)) order.Add("completed"); };
        EventHandler<SolverConvergence> finished = (sender, _) =>
        { if (ReferenceEquals(sender, owner)) order.Add("finished"); };
        SolverInterface.ProgressChanged += progress;
        SolverInterface.FittingCompleted += completed;
        SolverInterface.AnalysisFinished += finished;
        try
        {
            owner.Analyze();
            owner.ReportBootstrapProgress(100);
            owner.ReportAnalysisFinished(SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            { Termination = SolverTermination.Converged }));
            capture.Drain(reverse: true);

            Assert.Equal(new[] { "progress", "progress", "completed", "finished" }, order);
        }
        finally
        {
            SolverInterface.ProgressChanged -= progress;
            SolverInterface.FittingCompleted -= completed;
            SolverInterface.AnalysisFinished -= finished;
            StatusBarManager.ClearAppStatus();
        }
    }

    sealed class ProgressCapture : IMainThreadDispatcher, IDisposable
    {
        readonly IMainThreadDispatcher previousDispatcher = PlatformServices.MainThreadDispatcher;
        readonly int previousStep = SolverUpdate.Step;
        readonly int previousTotal = SolverUpdate.TotalSteps;
        readonly ConcurrentQueue<Action> queue = new();
        internal GlobalSolver Owner { get; }
        internal List<FittingProgress> Received { get; } = new();

        internal ProgressCapture(ErrorEstimationMethod method = ErrorEstimationMethod.BootstrapResiduals, int memberCount = 3)
        {
            var model = new GlobalModel();
            foreach (var name in new[] { "Sample A", "Sample B", "Sample C" }.Take(memberCount))
                model.AddModel(new Offset(new ExperimentData(name)));
            Owner = new GlobalSolver { Model = model, ErrorEstimationMethod = method };
            SolverInterface.TerminateAnalysisFlag.Lower();
            SolverUpdate.Step = 0;
            SolverUpdate.TotalSteps = 1;
            SolverInterface.ProgressChanged += Capture;
            PlatformServices.RegisterMainThreadDispatcher(this);
        }

        void Capture(object sender, FittingProgress progress)
        { if (ReferenceEquals(sender, Owner)) Received.Add(progress); }

        public void Invoke(Action action) => queue.Enqueue(action);
        internal void Drain(bool reverse = false)
        {
            var callbacks = new List<Action>();
            while (queue.TryDequeue(out var callback)) callbacks.Add(callback);
            if (reverse) callbacks.Reverse();
            foreach (var callback in callbacks) callback();
        }

        public void Dispose()
        {
            SolverInterface.ProgressChanged -= Capture;
            PlatformServices.RegisterMainThreadDispatcher(previousDispatcher);
            SolverUpdate.Step = previousStep;
            SolverUpdate.TotalSteps = previousTotal;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
    }
}
