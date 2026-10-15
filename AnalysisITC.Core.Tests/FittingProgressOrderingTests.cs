using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
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
    public void ParallelRefitsCannotLowerTheCounterButKeepTheirOriginalFractions(ErrorEstimationMethod method, bool singleMember)
    {
        using var capture = new ProgressCapture(method, singleMember ? 1 : 3);
        var position = singleMember ? 1 : 2;
        var name = singleMember ? "Sample A" : "Sample B";
        var child = new Solver { Silent = true, BootstrapIterations = 10 };
        child.ProgressObserver = (phase, completed, total, fraction) => capture.Owner.PublishProgress(
            phase, completed, total, fraction, position, name);
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
        Assert.Equal(new float?[] { .2f, .1f, .3f }, capture.Received.Select(progress => progress.Fraction));
        Assert.All(capture.Received, progress =>
        {
            Assert.Equal(position, progress.MemberPosition);
            Assert.Equal(name, progress.MemberName);
            Assert.Equal(10, progress.Total);
        });
    }

    [Fact]
    public void ReorderedDispatchCannotLowerTheCounterButStillForwardsEveryFraction()
    {
        using var capture = new ProgressCapture();
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 1, 10, .1f, 1, "Sample A");
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 2, 10, .2f, 1, "Sample A");
        capture.Drain(reverse: true);

        Assert.Equal(new int?[] { 2, 2 }, capture.Received.Select(progress => progress.Completed));
        Assert.Equal(new float?[] { .2f, .1f }, capture.Received.Select(progress => progress.Fraction));
    }

    [Fact]
    public void APreviousMembersDelayedSnapshotCannotRestoreItsNameOrCounter()
    {
        using var capture = new ProgressCapture();
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 9, 10, .3f, 1, "Sample A");
        capture.Owner.PublishProgress(FittingPhase.EstimatingUncertainty, 1, 10, .7f, 3, "Sample C");
        capture.Drain(reverse: true);

        Assert.Equal(new float?[] { .7f, .3f }, capture.Received.Select(progress => progress.Fraction));
        Assert.All(capture.Received, progress =>
        {
            Assert.Equal(3, progress.MemberPosition);
            Assert.Equal("Sample C", progress.MemberName);
            Assert.Equal(1, progress.Completed);
        });
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
