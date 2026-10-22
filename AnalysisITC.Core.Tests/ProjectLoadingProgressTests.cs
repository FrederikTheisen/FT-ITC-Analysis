using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(StatusBarManagerCollection.Name)]
public sealed class ProjectLoadingProgressTests : IDisposable
{
    public ProjectLoadingProgressTests()
    {
        StatusBarManager.SetDefaultSecondaryStatus("Saved project");
        StatusBarManager.ClearAppStatus();
    }

    public void Dispose()
    {
        StatusBarManager.ClearAppStatus();
        StatusBarManager.SetDefaultSecondaryStatus("");
    }

    [Fact]
    public async Task InteractiveReadLetsUiRenderEachItemBeforeLoadingCompletes()
    {
        using var package = await Package();
        using var progress = new ProgressCapture();
        var rendered = new List<string>();
        var context = new UiContext();
        var previousContext = SynchronizationContext.Current;
        Task<FtxtcReadResult> read = null;
        EventHandler<string> handler = (_, message) => context.Post(_ =>
        {
            if (read?.IsCompleted == false) rendered.Add(message);
        }, null);
        StatusBarManager.SecondaryStatusUpdated += handler;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            read = FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict, interactive: true);
            context.RunUntil(read);
            var restored = await read;

            Assert.Contains("Experiment: Named experiment", rendered);
            Assert.Contains("Experiment: Second experiment", rendered);
            Assert.Contains("Fit: Named experiment", rendered);
            Assert.Contains("Result: Named result", rendered);
            Assert.Contains("Report: Named report", rendered);
            Assert.Contains("Finishing…", rendered);
            Assert.Equal(new[] { -0.5, 0, 1 / 43.0, 2 / 43.0, 12 / 43.0, 42 / 43.0, 1, -0.5, -1 },
                progress.Updates.Select(update => update.Progress));
            Assert.True(progress.Updates.Single(update => update.Progress == 1).KeepVisibleAtOne);
            Assert.Equal(2, restored.Containers.OfType<ExperimentData>().Count());
            Assert.Single(restored.Containers.OfType<AnalysisResult>());
            Assert.Single(restored.Reports);
            Assert.Empty(restored.Issues);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            StatusBarManager.SecondaryStatusUpdated -= handler;
        }
    }

    [Fact]
    public async Task BackgroundReadDoesNotReplaceStatusText()
    {
        using var package = await Package();
        StatusBarManager.SetProgress(0.37);
        using var progress = new ProgressCapture();
        var messages = new List<string>();
        EventHandler<string> handler = (_, message) => messages.Add(message);
        StatusBarManager.SecondaryStatusUpdated += handler;
        try
        {
            await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict);
            Assert.Empty(messages);
            Assert.Empty(progress.Updates);
            Assert.Equal(0.37, StatusBarManager.Progress);
        }
        finally { StatusBarManager.SecondaryStatusUpdated -= handler; }
    }

    [Fact]
    public async Task CancellationDuringItemUpdateClearsLoadingText()
    {
        using var package = await Package();
        using var progress = new ProgressCapture();
        using var cancellation = new CancellationTokenSource();
        var messages = new List<string>();
        EventHandler<string> handler = (_, message) =>
        {
            messages.Add(message);
            if (message == "Experiment: Named experiment") cancellation.Cancel();
        };
        StatusBarManager.SecondaryStatusUpdated += handler;
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FTXTCReader.ReadWithRecovery(
                package, FtxtcReadPolicy.Strict, interactive: true, cancellationToken: cancellation.Token));
            Assert.DoesNotContain("Fit: Named experiment", messages);
            Assert.Equal("Saved project", messages.Last());
            Assert.False(DocumentDirtyTracker.IsRestoringDocument);
            Assert.True(progress.Updates.Last().HideProgressWheel);
        }
        finally { StatusBarManager.SecondaryStatusUpdated -= handler; }
    }

    [Fact]
    public async Task EmptyProjectUsesValidationAndFinishingWithoutDividingByZero()
    {
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, Array.Empty<ExperimentData>(), Array.Empty<AnalysisResult>());
        package.Position = 0;
        using var progress = new ProgressCapture();

        await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict, interactive: true);

        Assert.Equal(new[] { -0.5, -0.5, -1 }, progress.Updates.Select(update => update.Progress));
    }

    [Fact]
    public async Task ValidationFailureHidesProgress()
    {
        using var package = new MemoryStream(new byte[] { 1, 2, 3 });
        using var progress = new ProgressCapture();

        await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadWithRecovery(
            package, FtxtcReadPolicy.Strict, interactive: true));

        Assert.Equal(new[] { -0.5, -1 }, progress.Updates.Select(update => update.Progress));
    }

    [Fact]
    public async Task SkippedRecoveryItemsStillAdvanceProgress()
    {
        using var source = await Package();
        using var package = FTXTCFormatTests.RewriteAuthenticatedPackage(source, (path, bytes) =>
            path.StartsWith("experiments/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal)
                ? System.Text.Encoding.UTF8.GetBytes("{}") : bytes, schemaMinor: FTXTCFormat.SchemaMinor);
        using var progress = new ProgressCapture();

        var restored = await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.RecoverUsableContent, interactive: true);

        Assert.NotEmpty(restored.Issues);
        Assert.Equal(new[] { 0, 1 / 43.0, 2 / 43.0, 12 / 43.0, 42 / 43.0, 1 },
            progress.Updates.Where(update => update.IsDeterminate).Select(update => update.Progress));
        Assert.True(progress.Updates.Last().HideProgressWheel);
    }

    [Fact]
    public async Task EachProjectRestartsProgress()
    {
        using var package = await Package();
        using var progress = new ProgressCapture();

        await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict, interactive: true);
        var first = progress.Updates.Select(update => update.Progress).ToArray();
        progress.Updates.Clear();
        package.Position = 0;
        await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict, interactive: true);

        Assert.Equal(first, progress.Updates.Select(update => update.Progress));
        Assert.Contains(1 / 43.0, first);
    }

    [Fact]
    public async Task NestedSolutionRestorationDoesNotCountTwice()
    {
        using var package = await Package(sharedMember: true);
        using var progress = new ProgressCapture();

        var restored = await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict, interactive: true);

        Assert.Equal(2, restored.Containers.OfType<AnalysisResult>().Count());
        Assert.Equal(new[] { 0, 1 / 73.0, 2 / 73.0, 12 / 73.0, 42 / 73.0, 72 / 73.0, 1 },
            progress.Updates.Where(update => update.IsDeterminate).Select(update => update.Progress));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentInsertionShowsFinishingAndThenHidesProgress(bool recovery)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hybrid-loading-{Guid.NewGuid():N}.ftxtc");
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.Initialize();
        using var package = await Package();
        await File.WriteAllBytesAsync(path, package.ToArray());
        using var progress = new ProgressCapture();
        var secondary = "";
        var finishingDuringInsertion = false;
        EventHandler<string> statusHandler = (_, message) => secondary = message;
        EventHandler<ExperimentData> dataHandler = (_, _) =>
        {
            if (DataManager.SourceItems.Count > 0)
                finishingDuringInsertion = secondary == "Finishing…" && StatusBarManager.Progress == -0.5;
        };
        StatusBarManager.SecondaryStatusUpdated += statusHandler;
        DataManager.DataDidChange += dataHandler;
        try
        {
            if (recovery) Assert.True(await DataReader.ReadRecoveryFileAsync(path));
            else Assert.True((await DataReader.ReadPathsAsync(new[] { path })).LoadedAllRequested);

            Assert.True(finishingDuringInsertion);
            Assert.True(progress.Updates.Last().HideProgressWheel);
            Assert.Equal("Saved project", secondary);
        }
        finally
        {
            DataManager.DataDidChange -= dataHandler;
            StatusBarManager.SecondaryStatusUpdated -= statusHandler;
            DataManager.Clear(DataClearMode.ResetSession);
            DocumentDirtyTracker.MarkClean();
            File.Delete(path);
        }
    }

    sealed class ProgressCapture : IDisposable
    {
        public List<ProgressIndicatorEventData> Updates { get; } = new();
        public ProgressCapture() => StatusBarManager.ProgressUpdate += Capture;
        void Capture(object sender, ProgressIndicatorEventData update) => Updates.Add(update);
        public void Dispose() => StatusBarManager.ProgressUpdate -= Capture;
    }

    static async Task<MemoryStream> Package(bool sharedMember = false)
    {
        var model = InjectionProcessingMethodTests.FittedModel();
        model.Data.Name = "Named experiment";
        var second = new ExperimentData("second.itc") { Name = "Second experiment" };
        var globalModel = new GlobalModel(new List<Model> { model })
        { Parameters = new GlobalModelParameters(), ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None } };
        globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = globalModel },
            new List<SolutionInterface> { model.Solution }, model.Solution.Convergence, reconstructBootstrap: false);
        globalModel.Solution = solution;
        var result = new AnalysisResult(solution) { Name = "Named result" };
        var results = new List<AnalysisResult> { result };
        if (sharedMember) results.Add(new AnalysisResult(solution) { Name = "Second result" });
        var report = new AnalysisReport { Name = "Named report" };
        report.SetResultIds(new[] { result.UniqueID });
        var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { model.Data, second }, results, reports: new[] { report });
        package.Position = 0;
        return package;
    }

    sealed class UiContext : SynchronizationContext
    {
        readonly ConcurrentQueue<Action> callbacks = new();
        public override void Post(SendOrPostCallback callback, object state) => callbacks.Enqueue(() => callback(state));

        public void RunUntil(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                if (callbacks.TryDequeue(out var callback)) callback();
                else Thread.Sleep(1);
            }
            Assert.True(task.IsCompleted, "Project loading did not finish while the UI handled callbacks.");
        }
    }
}
