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
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(DataManagerUndoOrderCollectionDefinition.Name)]
public sealed class ProcessingCopyTests : IDisposable
{
    static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    readonly ConcurrentQueue<string> statuses = new();
    readonly ConcurrentQueue<HandledException> messages = new();
    readonly EventHandler<HandledException> previousMessageHandler = AppEventHandler.ShowAppMessage;

    public ProcessingCopyTests()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        StatusBarManager.ClearAppStatus();
        StatusBarManager.StatusUpdated += OnStatus;
        AppEventHandler.ShowAppMessage = (_, error) => messages.Enqueue(error);
    }

    public void Dispose()
    {
        AppEventHandler.ShowAppMessage = previousMessageHandler;
        StatusBarManager.StatusUpdated -= OnStatus;
        StatusBarManager.ClearAppStatus();
        DataManager.StopProcessCopying = false;
        DataManager.Clear(DataClearMode.ResetSession);
    }

    void OnStatus(object sender, string status) => statuses.Enqueue(status);

    [Theory]
    [InlineData("selection")]
    [InlineData("all")]
    [InlineData("active")]
    [InlineData("new")]
    public async Task CopyScopesSkipHeatOnlyImportsWithoutChangingTheirProcessingOrFit(string scope)
    {
        var source = RawExperiment("source");
        await source.Processor.ProcessData(showProgress: false);
        source.Processor.Lock();
        var destination = RawExperiment("destination");
        if (scope == "active")
        {
            destination.Injections[0].SetIntegrationLengthByTime(3);
            await destination.Processor.ProcessData(showProgress: false);
        }
        destination.Processor.Interpolator = null;
        var heatOnly = HeatOnlyExperiment();
        var processor = heatOnly.Processor;
        var solution = heatOnly.Solution;
        var area = heatOnly.Injections[0].RawPeakArea;
        var offset = heatOnly.Injections[0].IntegrationEndOffset;
        var revision = heatOnly.ProcessingRevision;
        DataManager.AddData(new[] { destination, heatOnly, source });

        await RunCopyAsync(() => StartCopy(scope, source, destination, heatOnly));

        Assert.Same(processor, heatOnly.Processor);
        Assert.Null(heatOnly.Processor.Interpolator);
        Assert.True(heatOnly.Injections[0].IsIntegrated);
        Assert.Equal(area, heatOnly.Injections[0].RawPeakArea);
        Assert.Equal(offset, heatOnly.Injections[0].IntegrationEndOffset);
        Assert.Equal(revision, heatOnly.ProcessingRevision);
        Assert.Same(solution, heatOnly.Solution);
        Assert.True(solution.IsValid);
        Assert.Null(heatOnly.BaseLineCorrectedDataPoints);
        AssertProcessed(destination);
        Assert.True(destination.Processor.IsLocked);
        Assert.Empty(messages);
        Assert.Contains(statuses, status => status.Contains("skipped 1 experiment without a thermogram"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task DirectCopyWithOnlyTargetsWithoutThermogramsDoesNotMutateOrClaimSuccess(int samples)
    {
        var source = RawExperiment("source");
        await source.Processor.ProcessData(showProgress: false);
        var target = HeatOnlyExperiment();
        target.DataPoints = samples < 0 ? null : Enumerable.Range(0, samples)
            .Select(time => new DataPoint(time, 1)).ToList();
        var processor = target.Processor;
        var revision = target.ProcessingRevision;
        DataManager.AddData(new[] { target, source });

        await RunCopyAsync(() => DataManager.CopySelectedProcessToSelection(source, new() { source, target }));

        Assert.Same(processor, target.Processor);
        Assert.Equal(revision, target.ProcessingRevision);
        Assert.True(target.Injections[0].IsIntegrated);
        Assert.Equal(-7e-6, target.Injections[0].RawPeakArea.Value);
        Assert.True(target.Solution.IsValid);
        Assert.Empty(messages);
        Assert.Contains(statuses, status => status.Contains("skipped 1 experiment without a thermogram"));
        Assert.DoesNotContain("Data processed", statuses);
        Assert.Equal(1, StatusBarManager.Progress);
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("new")]
    public async Task CopyCanInitializeARawDestinationWithANullProcessor(string scope)
    {
        var source = RawExperiment("source");
        await source.Processor.ProcessData(showProgress: false);
        var destination = RawExperiment("destination");
        DataManager.AddData(new[] { destination, source });
        destination.SetProcessor(null);

        await RunCopyAsync(() => StartCopy(scope, source, destination));

        AssertProcessed(destination);
        Assert.False(destination.Processor.IsLocked);
        Assert.Empty(messages);
    }

    [Fact]
    public async Task CopyRechecksThermogramAvailabilityAfterWaitingForAdmission()
    {
        var source = RawExperiment("source");
        await source.Processor.ProcessData(showProgress: false);
        var destination = RawExperiment("destination");
        destination.Processor.Interpolator = null;
        destination.Injections[0].SetPeakArea(new FloatWithError(9));
        var processor = destination.Processor;
        var revision = destination.ProcessingRevision;
        var heatOnly = HeatOnlyExperiment();
        DataManager.AddData(new[] { destination, heatOnly, source });

        await RunCopyAsync(DataManager.CopySelectedProcessToNonProcessed,
            queued: () => destination.DataPoints.Clear());

        Assert.Same(processor, destination.Processor);
        Assert.Equal(revision, destination.ProcessingRevision);
        Assert.True(destination.Injections[0].IsIntegrated);
        Assert.Equal(9, destination.Injections[0].RawPeakArea.Value);
        Assert.True(heatOnly.Solution.IsValid);
        Assert.Empty(messages);
        Assert.Contains(statuses, status => status.Contains("skipped 2 experiments without a thermogram"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchFailuresAreHandledWhileAdmissionIsRetainedAndFollowingWorkCanRun(bool failPublication)
    {
        var source = RawExperiment("source");
        if (!failPublication) source.Processor.Interpolator = new UncopyableBaseline(source.Processor);
        await source.Processor.ProcessData(showProgress: false);
        var destination = RawExperiment("destination");
        var following = RawExperiment("following");
        var heatOnly = HeatOnlyExperiment();
        DataManager.AddData(new[] { destination, following, heatOnly, source });
        Task next = Task.CompletedTask;
        var completionCount = 0;
        EventHandler completed = (sender, _) =>
        {
            if (failPublication && ReferenceEquals(sender, destination) && ++completionCount == 2)
                throw new InvalidOperationException("Injected copy failure");
        };
        DataProcessor.ProcessingCompleted += completed;
        AppEventHandler.ShowAppMessage = (sender, error) =>
        {
            messages.Enqueue(error);
            Assert.Same(ProcessingActivity.Current, Assert.IsType<ProcessingOperation>(sender));
            Assert.True(ProcessingActivity.IsBusy);
            using var rejected = ProcessingActivity.TryBegin();
            Assert.Null(rejected);
            Assert.False(next.IsCompleted);
            Assert.False(following.Processor.BaselineCompleted);
        };
        try
        {
            await RunCopyAsync(
                () => DataManager.CopySelectedProcessToSelection(source, new() { destination, heatOnly }),
                queued: () => next = following.Processor.ProcessData(showProgress: false));
            await next.WaitAsync(Deadline);

            Assert.Equal("Injected copy failure", Assert.Single(messages).Message);
            AssertProcessed(following);
            Assert.True(heatOnly.Injections[0].IsIntegrated);
            Assert.True(heatOnly.Solution.IsValid);
            Assert.Contains(statuses, status => status.Contains("Copy processing failed")
                && status.Contains("skipped 1 experiment without a thermogram"));
            Assert.DoesNotContain("Data processed", statuses);
            Assert.False(ProcessingActivity.IsBusy);
            Assert.Equal(1, StatusBarManager.Progress);
        }
        finally { DataProcessor.ProcessingCompleted -= completed; }
    }

    static void StartCopy(string scope, ExperimentData source, params ExperimentData[] targets)
    {
        switch (scope)
        {
            case "selection": DataManager.CopySelectedProcessToSelection(source, targets.ToList()); break;
            case "all": DataManager.CopySelectedProcessToAll(); break;
            case "active": DataManager.CopySelectedProcessToActive(); break;
            case "new": DataManager.CopySelectedProcessToNonProcessed(); break;
            default: throw new ArgumentOutOfRangeException(nameof(scope));
        }
    }

    static async Task RunCopyAsync(Action start, Action queued = null)
    {
        var idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler changed = (_, _) => { if (!ProcessingActivity.IsBusy) idle.TrySetResult(true); };
        var context = new CapturedExceptionContext();
        ProcessingActivity.Changed += changed;
        var root = ProcessingActivity.TryBegin();
        try
        {
            Assert.NotNull(root);
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try { start(); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            queued?.Invoke();
            root.Dispose();
            await idle.Task.WaitAsync(Deadline);
            Assert.Empty(context.Errors);
        }
        finally
        {
            root?.Dispose();
            try { await idle.Task.WaitAsync(Deadline); }
            finally { ProcessingActivity.Changed -= changed; }
        }
    }

    static ExperimentData RawExperiment(string name)
    {
        var data = new ExperimentData(name + ".itc") { Name = name };
        for (var time = 0; time <= 40; time++)
            data.DataPoints.Add(new DataPoint(time, time > 10 && time <= 16 ? 3 : 1));
        var injection = InjectionData.FromPEAQFile(data, 0, true, 10, 2e-6, 20, 2, 25);
        injection.SetIntegrationLengthByTime(6);
        data.Injections.Add(injection);
        data.Processor.DiscardIntegratedPoints = true;
        data.Processor.Interpolator = new PolynomialLeastSquaresInterpolator(data.Processor) { Degree = 0 };
        return data;
    }

    static ExperimentData HeatOnlyExperiment()
    {
        var data = new ExperimentData("heat-only.dh");
        var injection = new InjectionData(data, 2e-6f, 20, 0, 2);
        injection.InitializeIntegrationTimes();
        injection.SetPeakArea(new FloatWithError(-7e-6, 1e-8));
        data.Injections.Add(injection);
        var model = new Offset(data);
        model.InitializeParameters(data);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot { Algorithm = SolverAlgorithm.LevenbergMarquardt }));
        data.UpdateSolution(model);
        return data;
    }

    static void AssertProcessed(ExperimentData data)
    {
        Assert.True(data.Processor.BaselineCompleted);
        Assert.True(data.Processor.IntegrationCompleted);
        Assert.Equal(data.DataPoints.Count, data.BaseLineCorrectedDataPoints.Count);
        // Six one-second periods at 2 W above baseline give exactly 12 J.
        Assert.Equal(12, Assert.Single(data.Injections).RawPeakArea.Value, 10);
    }

    sealed class CapturedExceptionContext : SynchronizationContext
    {
        public readonly ConcurrentQueue<Exception> Errors = new();
        public override void Post(SendOrPostCallback callback, object state)
        {
            // Observe async-void exceptions without terminating the test host.
            try { callback(state); }
            catch (Exception error) { Errors.Enqueue(error); }
        }
    }

    sealed class UncopyableBaseline : PolynomialLeastSquaresInterpolator
    {
        public UncopyableBaseline(DataProcessor processor) : base(processor) { Degree = 0; }
        public override BaselineInterpolator Copy(DataProcessor processor) =>
            throw new InvalidOperationException("Injected copy failure");
    }
}
