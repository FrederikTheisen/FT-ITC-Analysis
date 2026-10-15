using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Platform;
using Xunit;
using Xunit.Abstractions;

namespace AnalysisITC.Core.Tests;

[Collection(SolverEventCollectionDefinition.Name)]
public sealed class ProcessingQueueLoadingTests : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    readonly ITestOutputHelper output;
    readonly PreferencesState preferences = PreferencesState.FromSettings();
    readonly string lastDocumentPath = AppSettings.LastDocumentPath;
    readonly string[] lastDocumentPaths = AppSettings.LastDocumentPaths?.ToArray();
    readonly IMainThreadDispatcher dispatcher = PlatformServices.MainThreadDispatcher;
    readonly IDataValidationPromptService validation = PlatformServices.DataValidationPromptService;

    public ProcessingQueueLoadingTests(ITestOutputHelper output)
    {
        this.output = output;
        Assert.False(ProcessingActivity.IsBusy);
        PreferencesState.Defaults().ApplyToSettings();
        PlatformServices.RegisterMainThreadDispatcher(null);
        PlatformServices.RegisterDataValidationPromptService(null);
        DataManager.Clear(DataClearMode.ResetSession);
        StatusBarManager.ClearAppStatus();
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        StatusBarManager.ClearAppStatus();
        PlatformServices.RegisterMainThreadDispatcher(dispatcher);
        PlatformServices.RegisterDataValidationPromptService(validation);
        preferences.ApplyToSettings();
        AppSettings.LastDocumentPath = lastDocumentPath;
        AppSettings.LastDocumentPaths = lastDocumentPaths;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedFileBatchPreservesIdleLoadOutputAndPublishesUnderOneOwner(bool nativeProject)
    {
        var paths = nativeProject
            ? new[] { Fixture("two-sites.ftxtc") }
            : new[] { Fixture("one-set.ftitc"), Fixture("data_1.itc") };
        var idleTimer = Stopwatch.StartNew();
        var idle = await DataReader.ReadPathsAsync(paths).WaitAsync(Timeout);
        idleTimer.Stop();
        Assert.True(idle.LoadedAllRequested);
        var expected = Capture(DataManager.SourceItems);
        var expectedReports = DataManager.Reports.Select(report => report.Name).ToArray();
        var expectedProcessed = DataManager.Data.Count(experiment => experiment.Processor.BaselineCompleted);
        Assert.True(expectedProcessed > 0);
        Assert.False(ProcessingActivity.IsBusy);
        DataManager.Clear(DataClearMode.ResetSession);

        var publications = new List<(string Path, Guid Owner, bool Busy)>();
        var messages = new ConcurrentQueue<string>();
        EventHandler<string> onStatus = (_, text) => messages.Enqueue(text);
        StatusBarManager.StatusUpdated += onStatus;
        var root = ProcessingActivity.TryBegin(); // Held calculation; never enter its ownership in the loader.
        Assert.NotNull(root);
        var waiting = Stopwatch.StartNew();
        var pending = DataReader.ReadPathsAsync(paths, path => publications.Add(
            (path, ProcessingActivity.Current?.Id ?? Guid.Empty, ProcessingActivity.IsBusy)));
        var service = new Stopwatch();
        try
        {
            Assert.False(pending.IsCompleted);
            Assert.Empty(DataManager.SourceItems);
            Assert.Empty(publications);
            Assert.DoesNotContain(messages, text => text.StartsWith("Reading data", StringComparison.Ordinal));
            Assert.Null(ProcessingActivity.TryBegin());
            await Task.Delay(25); // Measure deliberate queue wait separately from actual loading.
        }
        finally
        {
            waiting.Stop();
            service.Start();
            root.Dispose();
            try { await pending.WaitAsync(Timeout); }
            finally { StatusBarManager.StatusUpdated -= onStatus; service.Stop(); }
        }

        var queued = await pending;
        Assert.True(queued.LoadedAllRequested);
        Assert.Equal(paths, queued.LoadedPaths);
        Assert.Equal(idle.AddedItemCount, queued.AddedItemCount);
        Assert.Equal(idle.OpenedCleanProject, queued.OpenedCleanProject);
        Assert.Equal(paths, publications.Select(item => item.Path));
        var loadOwner = Assert.Single(publications.Select(item => item.Owner).Distinct());
        Assert.NotEqual(Guid.Empty, loadOwner);
        Assert.NotEqual(root.Operation.Id, loadOwner);
        Assert.All(publications, item => Assert.True(item.Busy));
        Assert.Equal(expectedProcessed, DataManager.Data.Count(experiment => experiment.Processor.BaselineCompleted));
        AssertEquivalent(expected, Capture(DataManager.SourceItems));
        Assert.Equal(expectedReports, DataManager.Reports.Select(report => report.Name));
        Assert.False(DocumentDirtyTracker.IsRestoringDocument);
        Assert.False(ProcessingActivity.IsBusy);
        output.WriteLine("{0}: idle load {1:F2} ms; deliberate admission wait {2:F2} ms; queued load after release {3:F2} ms.",
            string.Join(" + ", paths.Select(Path.GetFileName)), idleTimer.Elapsed.TotalMilliseconds,
            waiting.Elapsed.TotalMilliseconds, service.Elapsed.TotalMilliseconds);
    }

    [Fact]
    public async Task DirectLegacyReaderKeepsNestedProcessingInItsOwnQueuedOperation()
    {
        using var idleStream = File.OpenRead(Fixture("one-set.ftitc"));
        var expected = Capture(await FTITCReader.ReadStream(idleStream).WaitAsync(Timeout));
        using var queuedStream = File.OpenRead(Fixture("one-set.ftitc"));
        var owners = new ConcurrentQueue<Guid>();
        var completed = new ConcurrentQueue<ExperimentData>();
        EventHandler onProcessed = (sender, _) =>
        {
            if (sender is not ExperimentData experiment) return;
            completed.Enqueue(experiment);
            owners.Enqueue(ProcessingActivity.Current?.Id ?? Guid.Empty);
            Assert.True(ProcessingActivity.IsBusy);
        };
        DataProcessor.ProcessingCompleted += onProcessed;
        var root = ProcessingActivity.TryBegin();
        Assert.NotNull(root);
        var pending = FTITCReader.ReadStream(queuedStream);
        try
        {
            Assert.False(pending.IsCompleted);
            Assert.Equal(0, queuedStream.Position);
            Assert.Empty(completed);
        }
        finally
        {
            root.Dispose();
            try { await pending.WaitAsync(Timeout); }
            finally { DataProcessor.ProcessingCompleted -= onProcessed; }
        }

        var actual = await pending;
        AssertEquivalent(expected, Capture(actual));
        Assert.NotEmpty(completed);
        Assert.All(completed, experiment =>
        {
            Assert.True(experiment.Processor.BaselineCompleted);
            Assert.True(experiment.Processor.IntegrationCompleted);
            Assert.NotEmpty(experiment.BaseLineCorrectedDataPoints);
        });
        var owner = Assert.Single(owners.Distinct());
        Assert.NotEqual(Guid.Empty, owner);
        Assert.NotEqual(root.Operation.Id, owner);
        Assert.False(ProcessingActivity.IsBusy);
    }

    [Fact]
    public async Task DirectNativeReaderWaitsBeforeReadingAndRestoresIdenticalSavedOutput()
    {
        using var idleStream = File.OpenRead(Fixture("two-sites.ftxtc"));
        var idle = await FTXTCReader.ReadWithRecovery(idleStream, FtxtcReadPolicy.Strict).WaitAsync(Timeout);
        Assert.Empty(idle.Issues);
        var expected = Capture(idle.Containers);
        using var queuedStream = File.OpenRead(Fixture("two-sites.ftxtc"));
        var root = ProcessingActivity.TryBegin();
        Assert.NotNull(root);
        var pending = FTXTCReader.ReadWithRecovery(queuedStream, FtxtcReadPolicy.Strict);
        try
        {
            Assert.False(pending.IsCompleted);
            Assert.Equal(0, queuedStream.Position);
        }
        finally
        {
            root.Dispose();
            await pending.WaitAsync(Timeout);
        }
        var actual = await pending;
        Assert.Empty(actual.Issues);
        AssertEquivalent(expected, Capture(actual.Containers));
        Assert.Equal(idle.Reports.Select(report => report.Name), actual.Reports.Select(report => report.Name));
        Assert.False(ProcessingActivity.IsBusy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledOrFailedQueuedNativeReadReleasesAdmissionToFollowingLoad(bool cancel)
    {
        using Stream first = cancel
            ? File.OpenRead(Fixture("two-sites.ftxtc"))
            : new MemoryStream(new byte[] { 0, 1, 2, 3 });
        using var second = File.OpenRead(Fixture("two-sites.ftxtc"));
        using var cancellation = new CancellationTokenSource();
        var busyChanges = new ConcurrentQueue<bool>();
        EventHandler onActivity = (_, _) => busyChanges.Enqueue(ProcessingActivity.IsBusy);
        ProcessingActivity.Changed += onActivity;
        var root = ProcessingActivity.TryBegin();
        Assert.NotNull(root);
        var rejected = FTXTCReader.ReadWithRecovery(first, FtxtcReadPolicy.Strict,
            cancellationToken: cancellation.Token);
        var following = FTXTCReader.ReadWithRecovery(second, FtxtcReadPolicy.Strict);
        try
        {
            Assert.False(rejected.IsCompleted);
            Assert.False(following.IsCompleted);
            if (cancel)
            {
                cancellation.Cancel();
                // Admission currently has no cancellation token: the queued read
                // remains pending, then checks cancellation before any stream read.
                Assert.False(rejected.IsCompleted);
            }
        }
        finally
        {
            root.Dispose();
            try
            {
                if (cancel)
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rejected.WaitAsync(Timeout));
                else
                    await Assert.ThrowsAsync<InvalidDataException>(() => rejected.WaitAsync(Timeout));
                await following.WaitAsync(Timeout);
            }
            finally { ProcessingActivity.Changed -= onActivity; }
        }

        if (cancel) Assert.Equal(0, first.Position);
        Assert.Empty((await following).Issues);
        Assert.NotEmpty((await following).Containers);
        Assert.Equal(new[] { true, false }, busyChanges);
        Assert.False(DocumentDirtyTracker.IsRestoringDocument);
        Assert.False(ProcessingActivity.IsBusy);
        using var next = ProcessingActivity.TryBegin();
        Assert.NotNull(next);
    }

    [Fact]
    public async Task MissingFileInQueuedBatchDoesNotDiscardFollowingValidProject()
    {
        var missing = Path.Combine(Path.GetTempPath(), "missing-queued-project-" + Guid.NewGuid().ToString("N") + ".ftxtc");
        var good = Fixture("two-sites.ftxtc");
        var root = ProcessingActivity.TryBegin();
        Assert.NotNull(root);
        var pending = DataReader.ReadPathsAsync(new[] { missing, good });
        try { Assert.False(pending.IsCompleted); }
        finally { root.Dispose(); await pending.WaitAsync(Timeout); }
        var result = await pending;
        Assert.Equal(new[] { good }, result.LoadedPaths);
        Assert.Equal(1, result.FailedOrSkippedPathCount);
        Assert.NotEmpty(DataManager.Data);
        Assert.False(ProcessingActivity.IsBusy);
        Assert.False(DocumentDirtyTracker.IsRestoringDocument);
    }

    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    sealed class OutputSnapshot
    {
        public readonly List<string> Structure = new();
        public readonly List<long> Numbers = new();
        public void Values(params double[] values) => Numbers.AddRange(values.Select(BitConverter.DoubleToInt64Bits));
        public void Quantity(FloatWithError value) => Values(value.Value, value.SD, value.Lower, value.Upper);
    }

    static OutputSnapshot Capture(IEnumerable<ITCDataContainer> containers)
    {
        var snapshot = new OutputSnapshot();
        foreach (var container in containers)
        {
            snapshot.Structure.Add(container.GetType().Name + ":" + container.Name);
            if (container is ExperimentData experiment) CaptureExperiment(snapshot, experiment);
            if (container is AnalysisResult result)
            {
                snapshot.Structure.Add("Result:" + result.Solution.Solutions.Count + ":" + result.Solution.BootstrapSolutions.Count);
                foreach (var solution in result.Solution.Solutions) CaptureSolution(snapshot, solution);
            }
        }
        return snapshot;
    }

    static void CaptureExperiment(OutputSnapshot snapshot, ExperimentData experiment)
    {
        snapshot.Structure.Add(experiment.FileName + ":" + experiment.Processor.BaselineType + ":"
            + experiment.Processor.BaselineCompleted + ":" + experiment.Processor.IntegrationCompleted + ":"
            + experiment.Processor.IsLocked + ":" + experiment.AppliedDilutionMethod + ":" + experiment.HeatMethod);
        snapshot.Quantity(experiment.CellConcentration);
        snapshot.Quantity(experiment.SyringeConcentration);
        snapshot.Values(experiment.CellVolume, experiment.MeasuredTemperature);
        CapturePoints(snapshot, "Raw", experiment.DataPoints);
        CapturePoints(snapshot, "Corrected", experiment.BaseLineCorrectedDataPoints);
        var baseline = experiment.Processor.Interpolator?.Baseline;
        snapshot.Structure.Add("Baseline:" + (baseline?.Count ?? -1));
        if (baseline != null)
            foreach (var value in baseline) snapshot.Quantity(value.FloatWithError);
        snapshot.Structure.Add("Injections:" + experiment.InjectionCount);
        foreach (var injection in experiment.Injections)
        {
            snapshot.Structure.Add(injection.ID + ":" + injection.Include + ":" + injection.IsIntegrated);
            snapshot.Values(injection.Time, injection.Volume, injection.ActualCellConcentration,
                injection.ActualTitrantConcentration, injection.Ratio, injection.InjectionMass,
                injection.IntegrationStartDelay, injection.IntegrationEndOffset);
            snapshot.Quantity(injection.RawPeakArea);
            snapshot.Quantity(injection.PeakArea);
        }
        if (experiment.Solution != null) CaptureSolution(snapshot, experiment.Solution);
    }

    static void CapturePoints(OutputSnapshot snapshot, string kind, IReadOnlyCollection<DataPoint> points)
    {
        snapshot.Structure.Add(kind + ":" + (points?.Count ?? -1));
        if (points != null)
            foreach (var point in points) snapshot.Values(point.Time, point.Power, point.Temperature);
    }

    static void CaptureSolution(OutputSnapshot snapshot, SolutionInterface solution)
    {
        snapshot.Structure.Add("Solution:" + solution.Data.FileName + ":" + solution.ModelType + ":" + solution.ErrorMethod
            + ":" + solution.BootstrapSolutions?.Count + ":" + solution.Convergence?.Termination);
        foreach (var parameter in solution.Parameters.OrderBy(item => item.Key))
        {
            snapshot.Structure.Add(parameter.Key.ToString());
            snapshot.Quantity(parameter.Value);
        }
    }

    static void AssertEquivalent(OutputSnapshot expected, OutputSnapshot actual)
    {
        Assert.Equal(expected.Structure, actual.Structure);
        // Compare exact stored floating-point bits, without fitting or recomputing reference mathematics.
        Assert.Equal(expected.Numbers, actual.Numbers);
    }
}
