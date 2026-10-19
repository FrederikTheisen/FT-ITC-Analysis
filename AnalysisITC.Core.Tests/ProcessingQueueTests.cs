using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using Xunit;
using Xunit.Abstractions;

namespace AnalysisITC.Core.Tests;

[Collection(DataManagerUndoOrderCollectionDefinition.Name)]
public sealed class ProcessingQueueTests : IDisposable
{
    static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    readonly ITestOutputHelper output;

    public ProcessingQueueTests(ITestOutputHelper output)
    {
        this.output = output;
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.Initialize();
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        StatusBarManager.ClearAppStatus();
    }

    [Fact]
    public async Task ProcessingWaitsForAnActiveSolverThenRunsInRequestOrderWithoutIdleGaps()
    {
        var changes = new ConcurrentQueue<bool>();
        var starts = new ConcurrentQueue<(string Name, Guid Operation)>();
        var first = Experiment("first");
        var second = Experiment("second");
        var firstBaseline = new ObservedBaseline(first.Processor, () => starts.Enqueue((first.Name, ProcessingActivity.Current.Id)), blockFirst: true);
        var secondBaseline = new ObservedBaseline(second.Processor, () => starts.Enqueue((second.Name, ProcessingActivity.Current.Id)));
        first.Processor.Interpolator = firstBaseline;
        second.Processor.Interpolator = secondBaseline;
        DataManager.AddData(new[] { first, second });
        var solver = new BlockingSolver();
        EventHandler changed = (_, _) => changes.Enqueue(ProcessingActivity.IsBusy);
        ProcessingActivity.Changed += changed;
        var fit = Task.Run(() => solver.Solve());
        Task[] processing = Array.Empty<Task>();
        try
        {
            await solver.Entered.Task.WaitAsync(Deadline);
            processing = new[]
            {
                first.Processor.ProcessData(showProgress: false),
                second.Processor.ProcessData(showProgress: false),
                first.Processor.ProcessData(showProgress: false),
            };
            Assert.All(processing, task => Assert.False(task.IsCompleted));
            Assert.Empty(starts);
            Assert.Null(ProcessingActivity.TryBegin());

            solver.Release.Set();
            await firstBaseline.Entered.Task.WaitAsync(Deadline);
            Assert.Equal(new[] { true }, changes.ToArray());
            Assert.True(ProcessingActivity.IsBusy);
            Assert.Null(ProcessingActivity.TryBegin());
            Assert.False(first.Processor.BaselineCompleted);
            Assert.False(second.Processor.BaselineCompleted);

            firstBaseline.Release.TrySetResult(true);
            await Task.WhenAll(processing.Append(fit)).WaitAsync(Deadline);

            var admitted = starts.ToArray();
            Assert.Equal(new[] { "first", "second", "first" }, admitted.Select(item => item.Name));
            Assert.Equal(3, admitted.Select(item => item.Operation).Distinct().Count());
            Assert.All(admitted, item => Assert.NotEqual(solver.OperationId, item.Operation));
            Assert.Equal(new[] { true, false }, changes.ToArray());
            AssertProcessed(first);
            AssertProcessed(second);
            Assert.False(ProcessingActivity.IsBusy);
        }
        finally
        {
            solver.Release.Set();
            firstBaseline.Release.TrySetResult(true);
            try { await Task.WhenAll(processing.Append(fit)).WaitAsync(Deadline); }
            finally { ProcessingActivity.Changed -= changed; }
        }
    }

    [Fact]
    public async Task RepeatedQueuedRequestsLeaveTheFinalStateMatchingTheLatestSplinePoints()
    {
        var data = Experiment("spline");
        var spline = new SplineInterpolator(data.Processor)
        { Algorithm = SplineInterpolator.SplineInterpolatorAlgorithm.Linear };
        spline.SetSplinePoints(new List<SplineInterpolator.SplinePoint>
        {
            new(0, 1, 0), new(20, 1, 1), new(40, 1, 2),
        });
        data.Processor.Interpolator = spline;
        DataManager.AddData(data);
        var root = ProcessingActivity.TryBegin();
        var tasks = new List<Task>();
        try
        {
            tasks.Add(data.Processor.ProcessData(replace: false, showProgress: false));
            spline.MoveSplinePoint(1, 20, 2);
            tasks.Add(data.Processor.ProcessData(replace: false, showProgress: false));
            spline.MoveSplinePoint(1, 20, 3);
            tasks.Add(data.Processor.ProcessData(replace: false, showProgress: false));
            Assert.All(tasks, task => Assert.False(task.IsCompleted));
            Assert.Null(data.BaseLineCorrectedDataPoints);

            root.Dispose();
            await Task.WhenAll(tasks).WaitAsync(Deadline);

            // The final triangle is B(t) = 1 + t/10 up to 20 s, then 5 - t/10.
            foreach (var sample in data.DataPoints.Select((point, index) => (point, index)))
            {
                var expectedBaseline = sample.point.Time <= 20 ? 1 + sample.point.Time / 10d : 5 - sample.point.Time / 10d;
                Assert.Equal(expectedBaseline, spline.Baseline[sample.index].Value, 12);
                Assert.Equal((float)(sample.point.Power - (float)expectedBaseline), data.BaseLineCorrectedDataPoints[sample.index].Power);
            }
            Assert.Equal(3, spline.SplinePoints[1].Power);
            Assert.True(data.Processor.BaselineCompleted);
            Assert.True(data.Processor.IntegrationCompleted);
            Assert.False(ProcessingActivity.IsBusy);
        }
        finally
        {
            root.Dispose();
            await Task.WhenAll(tasks).WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task FireAndForgetSplineProcessingKeepsItsEnteredOwnerBusyUntilItFinishes()
    {
        var data = Experiment("spline edit");
        var spline = new BlockingSpline(data.Processor);
        spline.SetSplinePoints(new List<SplineInterpolator.SplinePoint> { new(0, 1, 0), new(40, 1, 1) });
        spline.RefreshBaselineFromCurrentSplinePoints();
        data.Processor.Interpolator = spline;
        DataManager.AddData(data);
        var idle = Signal();
        EventHandler changed = (_, _) => { if (!ProcessingActivity.IsBusy) idle.TrySetResult(true); };
        ProcessingActivity.Changed += changed;
        var root = ProcessingActivity.TryBegin();
        try
        {
            using (root.Enter()) spline.InsertSplinePoint(20);
            await spline.Entered.Task.WaitAsync(Deadline);
            Assert.Equal(root.Operation.Id, spline.OperationId);
            root.Dispose(); // The menu callback has returned, but the unawaited processing still owns admission.
            Assert.True(ProcessingActivity.IsBusy);
            Assert.Null(ProcessingActivity.TryBegin());

            spline.Release.TrySetResult(true);
            await idle.Task.WaitAsync(Deadline);
            AssertProcessed(data);
            Assert.Equal(3, spline.SplinePoints.Count);
        }
        finally
        {
            spline.Release.TrySetResult(true);
            root.Dispose();
            try { await idle.Task.WaitAsync(Deadline); }
            finally { ProcessingActivity.Changed -= changed; }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledQueuedWorkReleasesAdmissionForTheNextRequest(bool cancel)
    {
        var first = Experiment("unsuccessful");
        first.Processor.Interpolator = new UnsuccessfulBaseline(first.Processor, cancel);
        var next = Experiment("next");
        var root = ProcessingActivity.TryBegin();
        var unsuccessful = first.Processor.InterpolateBaseline(throwOnError: true);
        var following = next.Processor.ProcessData(showProgress: false);
        try
        {
            Assert.False(unsuccessful.IsCompleted);
            Assert.False(following.IsCompleted);
            root.Dispose();
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unsuccessful.WaitAsync(Deadline));
            else await Assert.ThrowsAsync<ArithmeticException>(() => unsuccessful.WaitAsync(Deadline));
            await following.WaitAsync(Deadline);
            AssertProcessed(next);
            Assert.False(ProcessingActivity.IsBusy);
            using var admitted = ProcessingActivity.TryBegin();
            Assert.NotNull(admitted);
        }
        finally
        {
            root.Dispose();
            try { await unsuccessful.WaitAsync(Deadline); }
            catch (Exception ex) when (ex is ArithmeticException or OperationCanceledException) { }
            await following.WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task QueuedSplineConversionCanAwaitItsNestedProcessingAndLockBeforeLaterRequests()
    {
        var data = Experiment("convert");
        data.Processor.Interpolator.Baseline = data.DataPoints.Select(_ => new Energy(1)).ToList();
        var root = ProcessingActivity.TryBegin();
        var conversion = data.Processor.Interpolator.ConvertToSplineAsync(showProgress: false);
        var later = data.Processor.ProcessData(showProgress: false);
        try
        {
            Assert.False(conversion.IsCompleted);
            Assert.False(later.IsCompleted);
            root.Dispose();
            await Task.WhenAll(conversion, later).WaitAsync(Deadline);
            Assert.IsType<SplineInterpolator>(data.Processor.Interpolator);
            Assert.True(data.Processor.IsLocked);
            AssertProcessed(data);
            Assert.False(ProcessingActivity.IsBusy);
        }
        finally
        {
            root.Dispose();
            await Task.WhenAll(conversion, later).WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task QueuedCopyBatchCanAwaitMemberProcessingWithoutWaitingForItsOwnAdmission()
    {
        var source = Experiment("source");
        source.Processor.Interpolator.Baseline = source.DataPoints.Select(_ => new Energy(1)).ToList();
        var targets = new[] { source, Experiment("first target"), Experiment("second target") };
        DataManager.AddData(targets);
        var idle = Signal();
        EventHandler changed = (_, _) => { if (!ProcessingActivity.IsBusy) idle.TrySetResult(true); };
        ProcessingActivity.Changed += changed;
        var root = ProcessingActivity.TryBegin();
        try
        {
            DataManager.CopySelectedProcessToSelection(source, targets.ToList());
            Assert.All(targets.Skip(1), data => Assert.False(data.Processor.BaselineCompleted));
            root.Dispose();
            await idle.Task.WaitAsync(Deadline);
            Assert.All(targets.Skip(1), AssertProcessed);
            Assert.False(ProcessingActivity.IsBusy);
        }
        finally
        {
            root.Dispose();
            try { await idle.Task.WaitAsync(Deadline); }
            finally { ProcessingActivity.Changed -= changed; }
        }
    }

    [Theory]
    [InlineData(true, true, InjectionData.IntegrationLengthMode.Time)]
    [InlineData(true, false, InjectionData.IntegrationLengthMode.Time)]
    [InlineData(false, true, InjectionData.IntegrationLengthMode.Time)]
    [InlineData(false, false, InjectionData.IntegrationLengthMode.Time)]
    [InlineData(true, true, InjectionData.IntegrationLengthMode.Fit)]
    [InlineData(true, false, InjectionData.IntegrationLengthMode.Fit)]
    public async Task CopyProcessingRecalculatesDestinationBeforeApplyingSourceLock(
        bool lockSource, bool processDestination, InjectionData.IntegrationLengthMode mode)
    {
        var source = Experiment("source");
        await source.Processor.ProcessData(showProgress: false);
        source.Processor.IntegrationLengthMode = mode;
        source.Processor.IntegrationLengthFactor = 3;
        if (lockSource) source.Processor.Lock();
        var sourceProcessor = source.Processor;
        var sourceCorrected = source.BaseLineCorrectedDataPoints;
        var sourceBaseline = sourceProcessor.Interpolator.Baseline.ToArray();
        var sourceHeat = source.Injections[0].RawPeakArea;

        var destination = Experiment("destination");
        var oldBaseline = new SplineInterpolator(destination.Processor)
        { Algorithm = SplineInterpolator.SplineInterpolatorAlgorithm.Linear };
        oldBaseline.SetSplinePoints(new List<SplineInterpolator.SplinePoint>
        {
            new(0, 2, 0), new(40, 2, 1),
        });
        destination.Processor.Interpolator = oldBaseline;
        destination.Processor.DiscardIntegratedPoints = false;
        destination.Injections[0].SetIntegrationStartTime(1);
        // Fit Peaks retains this endpoint when there is no tail after its fit-start time.
        destination.Injections[0].SetIntegrationLengthByTime(
            mode == InjectionData.IntegrationLengthMode.Fit ? 10 : 3);
        if (processDestination) await destination.Processor.ProcessData(replace: false, showProgress: false);
        var oldProcessor = destination.Processor;
        var oldCorrected = destination.BaseLineCorrectedDataPoints;
        var oldHeat = destination.Injections[0].RawPeakArea.Value;
        Assert.NotEqual(12, oldHeat);
        if (processDestination)
        {
            Assert.All(oldBaseline.Baseline, point => Assert.Equal(2, point.Value));
            Assert.Equal(-1, oldCorrected[0].Power);
        }
        DataManager.AddData(new[] { source, destination });

        await CopyProcessingAsync(source, source, destination);

        AssertProcessed(destination);
        Assert.NotEqual(oldHeat, destination.Injections[0].RawPeakArea.Value);
        Assert.NotSame(oldProcessor, destination.Processor);
        Assert.NotSame(oldCorrected, destination.BaseLineCorrectedDataPoints);
        Assert.IsType<PolynomialLeastSquaresInterpolator>(destination.Processor.Interpolator);
        Assert.True(destination.Processor.DiscardIntegratedPoints);
        Assert.Equal(mode, destination.Processor.IntegrationLengthMode);
        Assert.Equal(3, destination.Processor.IntegrationLengthFactor);
        Assert.Equal(0, destination.Injections[0].IntegrationStartDelay);
        Assert.Equal(mode == InjectionData.IntegrationLengthMode.Fit ? 10 : 6,
            destination.Injections[0].IntegrationEndOffset);
        Assert.Equal(lockSource, destination.Processor.IsLocked);
        Assert.All(destination.BaseLineCorrectedDataPoints,
            point => Assert.Equal(point.Time > 10 && point.Time <= 16 ? 2 : 0, point.Power));

        Assert.Same(sourceProcessor, source.Processor);
        Assert.Same(sourceCorrected, source.BaseLineCorrectedDataPoints);
        Assert.Equal(sourceBaseline, source.Processor.Interpolator.Baseline);
        Assert.Equal(sourceHeat, source.Injections[0].RawPeakArea);
        Assert.Equal(0, source.Injections[0].IntegrationStartDelay);
        Assert.Equal(6, source.Injections[0].IntegrationEndOffset);
        Assert.Equal(lockSource, source.Processor.IsLocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyProcessingLeavesAlreadyLockedDestinationUntouched(bool hasInterpolator)
    {
        var source = Experiment("source");
        await source.Processor.ProcessData(showProgress: false);
        source.Processor.Lock();
        var destination = Experiment("locked destination");
        destination.Processor.DiscardIntegratedPoints = false;
        destination.Injections[0].SetIntegrationLengthByTime(3);
        if (hasInterpolator) await destination.Processor.ProcessData(showProgress: false);
        else destination.Processor.Interpolator = null;
        destination.Processor.Lock();
        var processor = destination.Processor;
        var corrected = destination.BaseLineCorrectedDataPoints;
        var heat = destination.Injections[0].RawPeakArea;
        var integrated = destination.Injections[0].IsIntegrated;
        DataManager.AddData(new[] { source, destination });

        await CopyProcessingAsync(source, source, destination);

        Assert.Same(processor, destination.Processor);
        Assert.Same(corrected, destination.BaseLineCorrectedDataPoints);
        Assert.Equal(heat, destination.Injections[0].RawPeakArea);
        Assert.Equal(integrated, destination.Injections[0].IsIntegrated);
        Assert.Equal(3, destination.Injections[0].IntegrationEndOffset);
        Assert.False(destination.Processor.DiscardIntegratedPoints);
        Assert.True(destination.Processor.IsLocked);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CopyProcessingDoesNotTransferLockAfterFailedOrCancelledProcessing(
        bool cancelBaseline, bool failPeakFit)
    {
        var source = Experiment("source");
        source.Processor.Interpolator = new FailingCopiedBaseline(source.Processor, cancelBaseline, failPeakFit);
        await source.Processor.ProcessData(showProgress: false);
        if (failPeakFit) source.Processor.IntegrationLengthMode = InjectionData.IntegrationLengthMode.Fit;
        source.Processor.Lock();
        var destination = Experiment("destination");
        DataManager.AddData(new[] { source, destination });

        await CopyProcessingAsync(source, source, destination);

        Assert.False(destination.Processor.IsLocked);
        if (failPeakFit)
        {
            // Rollback can restore coherent outputs even though Fit Peaks failed.
            AssertProcessed(destination);
        }
        else Assert.False(destination.Processor.BaselineCompleted);
        Assert.True(source.Processor.IsLocked);
        Assert.False(ProcessingActivity.IsBusy);
    }

    [Fact]
    public async Task StoppingCopyProcessingDoesNotTransferLocksToCurrentOrPendingDestinations()
    {
        var source = Experiment("source");
        await source.Processor.ProcessData(showProgress: false);
        source.Processor.Lock();
        var first = Experiment("first target");
        var next = Experiment("next target");
        DataManager.AddData(new[] { source, first, next });
        EventHandler completed = (sender, _) =>
        {
            if (ReferenceEquals(sender, first.Processor)) DataManager.StopProcessCopying = true;
        };
        DataProcessor.BaselineInterpolationCompleted += completed;
        try
        {
            await CopyProcessingAsync(source, source, first, next);

            AssertProcessed(first);
            Assert.False(first.Processor.IsLocked);
            Assert.False(next.Processor.IsLocked);
            Assert.False(next.Processor.IntegrationCompleted);
            Assert.Null(next.BaseLineCorrectedDataPoints);
        }
        finally
        {
            DataProcessor.BaselineInterpolationCompleted -= completed;
            DataManager.StopProcessCopying = false;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingOrClosingQueuedWorkDoesNotReinsertItOrPreventLaterProcessing(bool replaceDocument)
    {
        var removed = Experiment("removed");
        DataManager.AddData(removed);
        var root = ProcessingActivity.TryBegin();
        var requested = removed.Processor.ProcessData(showProgress: false);
        var orphanNotifications = 0;
        EventHandler completed = (sender, _) => { if (ReferenceEquals(sender, removed)) orphanNotifications++; };
        DataProcessor.ProcessingCompleted += completed;
        Task following = Task.CompletedTask;
        try
        {
            Assert.False(requested.IsCompleted);
            if (replaceDocument) DataManager.Clear(DataClearMode.ResetSession);
            else DataManager.RemoveSourceItemAt(0);
            var replacement = Experiment("replacement");
            DataManager.AddData(replacement);
            following = replacement.Processor.ProcessData(showProgress: false);
            root.Dispose();
            await Task.WhenAll(requested, following).WaitAsync(Deadline);

            Assert.Same(replacement, Assert.Single(DataManager.Data));
            Assert.DoesNotContain(removed, DataManager.SourceItems);
            AssertProcessed(replacement);
            Assert.False(ProcessingActivity.IsBusy);
            // Diagnostic evidence for the separate lifetime-policy review; no assertion
            // endorses publishing work for an experiment that has been removed.
            output.WriteLine($"Processing callbacks for the removed experiment after admission: {orphanNotifications}");
        }
        finally
        {
            root.Dispose();
            try { await Task.WhenAll(requested, following).WaitAsync(Deadline); }
            finally { DataProcessor.ProcessingCompleted -= completed; }
        }
    }

    static async Task CopyProcessingAsync(ExperimentData source, params ExperimentData[] targets)
    {
        var idle = Signal();
        EventHandler changed = (_, _) => { if (!ProcessingActivity.IsBusy) idle.TrySetResult(true); };
        ProcessingActivity.Changed += changed;
        var root = ProcessingActivity.TryBegin();
        try
        {
            Assert.NotNull(root);
            DataManager.CopySelectedProcessToSelection(source, targets.ToList());
            root.Dispose();
            await idle.Task.WaitAsync(Deadline);
        }
        finally
        {
            root?.Dispose();
            try { await idle.Task.WaitAsync(Deadline); }
            finally { ProcessingActivity.Changed -= changed; }
        }
    }

    static ExperimentData Experiment(string name)
    {
        var data = new ExperimentData(name + ".itc") { Name = name };
        // Six one-second periods at 2 W above a 1 W baseline give exactly 12 J.
        for (var time = 0; time <= 40; time++)
            data.DataPoints.Add(new DataPoint(time, time > 10 && time <= 16 ? 3 : 1));
        var injection = InjectionData.FromPEAQFile(data, 0, true, 10, 2e-6, 20, 2, 25);
        injection.InitializeIntegrationTimes();
        injection.SetIntegrationLengthByTime(6);
        data.Injections.Add(injection);
        data.Processor.DiscardIntegratedPoints = true;
        data.Processor.Interpolator = new PolynomialLeastSquaresInterpolator(data.Processor) { Degree = 0 };
        return data;
    }

    static void AssertProcessed(ExperimentData data)
    {
        Assert.True(data.Processor.BaselineCompleted);
        Assert.True(data.Processor.IntegrationCompleted);
        Assert.Equal(data.DataPoints.Count, data.BaseLineCorrectedDataPoints.Count);
        Assert.Equal(12, Assert.Single(data.Injections).RawPeakArea.Value, 10);
        Assert.All(data.Processor.Interpolator.Baseline, point => Assert.Equal(1, point.Value, 12));
    }

    static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    sealed class FailingCopiedBaseline : PolynomialLeastSquaresInterpolator
    {
        readonly bool cancelBaseline;
        readonly bool failPeakFit;
        int invocations;

        public FailingCopiedBaseline(DataProcessor processor, bool cancelBaseline, bool failPeakFit) : base(processor)
        {
            Degree = 0;
            this.cancelBaseline = cancelBaseline;
            this.failPeakFit = failPeakFit;
        }

        public override BaselineInterpolator Copy(DataProcessor processor)
        {
            var copy = new FailingCopiedBaseline(processor, cancelBaseline, failPeakFit);
            copy.CopyBaselineFrom(this);
            if (failPeakFit)
                processor.PeakEndOffsetEstimator = _ => throw new ArithmeticException("Expected copied peak-fit failure.");
            return copy;
        }

        public override Task Interpolate(CancellationToken token, bool replace = true)
        {
            if (Interlocked.Increment(ref invocations) == 2 && !failPeakFit)
            {
                if (cancelBaseline) throw new OperationCanceledException("Expected copied baseline cancellation.");
                throw new ArithmeticException("Expected copied baseline failure.");
            }
            return base.Interpolate(token, replace);
        }
    }

    sealed class ObservedBaseline : PolynomialLeastSquaresInterpolator
    {
        readonly Action started;
        readonly bool blockFirst;
        int invocations;
        public readonly TaskCompletionSource<bool> Entered = Signal();
        public readonly TaskCompletionSource<bool> Release = Signal();
        public ObservedBaseline(DataProcessor processor, Action started, bool blockFirst = false) : base(processor)
        { this.started = started; this.blockFirst = blockFirst; }
        public override async Task Interpolate(CancellationToken token, bool replace = true)
        {
            started();
            Entered.TrySetResult(true);
            if (Interlocked.Increment(ref invocations) == 1 && blockFirst) await Release.Task.WaitAsync(Deadline);
            Baseline = Data.DataPoints.Select(_ => new Energy(1)).ToList();
        }
    }

    sealed class UnsuccessfulBaseline : PolynomialLeastSquaresInterpolator
    {
        readonly bool cancel;
        public UnsuccessfulBaseline(DataProcessor processor, bool cancel) : base(processor) => this.cancel = cancel;
        public override Task Interpolate(CancellationToken token, bool replace = true) => cancel
            ? Task.FromCanceled(new CancellationToken(canceled: true))
            : Task.FromException(new ArithmeticException("Controlled processing failure"));
    }

    sealed class BlockingSpline : SplineInterpolator
    {
        public readonly TaskCompletionSource<bool> Entered = Signal();
        public readonly TaskCompletionSource<bool> Release = Signal();
        public Guid OperationId;
        public BlockingSpline(DataProcessor processor) : base(processor) => Algorithm = SplineInterpolatorAlgorithm.Linear;
        public override async Task Interpolate(CancellationToken token, bool replace = true)
        {
            OperationId = ProcessingActivity.Current.Id;
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(Deadline);
            await base.Interpolate(token, replace);
        }
    }

    sealed class BlockingSolver : SolverInterface
    {
        public readonly TaskCompletionSource<bool> Entered = Signal();
        public readonly ManualResetEventSlim Release = new(false);
        public BlockingSolver() { Silent = true; ErrorEstimationMethod = ErrorEstimationMethod.None; }
        protected override SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            Entered.TrySetResult(true);
            if (!Release.Wait(Deadline)) throw new TimeoutException("The test did not release the solver.");
            return null;
        }
    }
}
