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
        var messages = new List<string>();
        EventHandler<string> handler = (_, message) => messages.Add(message);
        StatusBarManager.SecondaryStatusUpdated += handler;
        try
        {
            await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict);
            Assert.Empty(messages);
        }
        finally { StatusBarManager.SecondaryStatusUpdated -= handler; }
    }

    [Fact]
    public async Task CancellationDuringItemUpdateClearsLoadingText()
    {
        using var package = await Package();
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
        }
        finally { StatusBarManager.SecondaryStatusUpdated -= handler; }
    }

    static async Task<MemoryStream> Package()
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
        var report = new AnalysisReport { Name = "Named report" };
        report.SetResultIds(new[] { result.UniqueID });
        var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { model.Data, second }, new[] { result }, reports: new[] { report });
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
