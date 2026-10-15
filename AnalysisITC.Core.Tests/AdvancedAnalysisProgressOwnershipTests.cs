using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(SolverEventCollectionDefinition.Name)]
public sealed class AdvancedAnalysisProgressOwnershipTests
{
    [Fact]
    public void DelayedAdvancedEventsKeepTheirOriginalOperationAndCapturedProgress()
    {
        var previous = PlatformServices.MainThreadDispatcher;
        var previousIterations = ResultAnalysisController.CalculationIterations;
        var dispatcher = new HeldDispatcher();
        var analysis = new AdvancedAnalysis(null);
        AdvancedAnalysisEventSource started = null;
        AdvancedAnalysisEventSource finished = null;
        var progress = new List<(AdvancedAnalysisEventSource Source, Tuple<int, int, float, string> Value)>();
        Tuple<int, TimeSpan> completion = null;
        EventHandler<TerminationFlag> onStarted = (sender, _) => started = Assert.IsType<AdvancedAnalysisEventSource>(sender);
        EventHandler<Tuple<int, int, float, string>> onProgress = (sender, value) =>
            progress.Add((Assert.IsType<AdvancedAnalysisEventSource>(sender), value));
        EventHandler<Tuple<int, TimeSpan>> onFinished = (sender, value) =>
        {
            finished = Assert.IsType<AdvancedAnalysisEventSource>(sender);
            completion = value;
        };
        ResultAnalysisController.AnalysisStarted += onStarted;
        ResultAnalysisController.IterationFinished += onProgress;
        ResultAnalysisController.AnalysisFinished += onFinished;
        PlatformServices.RegisterMainThreadDispatcher(dispatcher);
        try
        {
            Guid original;
            using (var root = ProcessingActivity.TryBegin())
            using (root.Enter())
            {
                original = root.Operation.Id;
                ResultAnalysisController.CalculationIterations = 8;
                ResultAnalysisController.ReportCalculationStarted(analysis);
                ResultAnalysisController.ReportCalculationProgress(3, description: "first sample");
                ResultAnalysisController.CalculationIterations = 24;
                ResultAnalysisController.ReportCalculationProgress(5, 10, "second sample");
                ResultAnalysisController.ReportAnalysisFinished(analysis, 5, TimeSpan.FromSeconds(2));
            }
            using var next = ProcessingActivity.TryBegin();
            using var nextOwnership = next.Enter();
            ResultAnalysisController.CalculationIterations = 1000;
            Assert.Null(started);
            Assert.Null(finished);
            Assert.Empty(progress);
            dispatcher.Drain();

            Assert.NotNull(started);
            Assert.NotNull(finished);
            Assert.Equal(original, started.OperationId);
            Assert.Equal(original, finished.OperationId);
            Assert.Same(analysis, started.Analysis);
            Assert.Same(analysis, finished.Analysis);
            Assert.False(ProcessingActivity.IsCurrentOperation(original));
            Assert.NotEqual(next.Operation.Id, started.OperationId);
            Assert.Collection(progress,
                first =>
                {
                    Assert.Equal(original, first.Source.OperationId);
                    Assert.Equal(Tuple.Create(3, 8, .375f, "first sample"), first.Value);
                },
                second =>
                {
                    Assert.Equal(original, second.Source.OperationId);
                    Assert.Equal(Tuple.Create(5, 10, .5f, "second sample"), second.Value);
                });
            Assert.Equal(Tuple.Create(5, TimeSpan.FromSeconds(2)), completion);
        }
        finally
        {
            PlatformServices.RegisterMainThreadDispatcher(previous);
            ResultAnalysisController.CalculationIterations = previousIterations;
            ResultAnalysisController.AnalysisStarted -= onStarted;
            ResultAnalysisController.IterationFinished -= onProgress;
            ResultAnalysisController.AnalysisFinished -= onFinished;
        }
    }

    sealed class HeldDispatcher : IMainThreadDispatcher
    {
        readonly ConcurrentQueue<Action> queue = new();
        public void Invoke(Action action) => queue.Enqueue(action);
        internal void Drain() { while (queue.TryDequeue(out var action)) action(); }
    }
}
