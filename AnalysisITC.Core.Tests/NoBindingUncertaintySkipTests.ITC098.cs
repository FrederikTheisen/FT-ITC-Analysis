using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class NoBindingUncertaintySkipTests
{
    public static IEnumerable<object[]> ProgressSequenceCases()
    {
        foreach (var scope in Enum.GetValues<BindingAssessmentScope>())
            foreach (var method in new[] { ErrorEstimationMethod.None, ErrorEstimationMethod.BootstrapResiduals,
                ErrorEstimationMethod.LeaveOneOut, ErrorEstimationMethod.ProfileLikelihood })
                foreach (var skip in new[] { false, true })
                    yield return new object[] { scope, method, skip };
    }

    [Theory]
    [MemberData(nameof(ProgressSequenceCases))]
    public async Task ProgressDescribesTheActualCalculationOrderWithoutChangingIt(
        BindingAssessmentScope assessmentScope, ErrorEstimationMethod method, bool skip)
    {
        var models = assessmentScope == BindingAssessmentScope.Single
            ? new[] { PolicyModel(false, "Sample A") }
            : new[] { PolicyModel(false, "Sample A"), PolicyModel(false, "Sample B") };
        var solver = PolicySolver(assessmentScope, method, models);
        solver.SkipNoBindingUncertainty = skip;
        var (events, completion) = await CaptureProgress(solver);
        var expectedScope = assessmentScope == BindingAssessmentScope.Independent ? FittingScope.Individual
            : assessmentScope == BindingAssessmentScope.Pooled ? FittingScope.Global : FittingScope.Single;
        Assert.NotEmpty(events);
        Assert.All(events, progress =>
        {
            Assert.Equal(completion.OperationId, progress.OperationId);
            Assert.Equal(expectedScope, progress.Scope);
            Assert.Equal(models.Length, progress.MemberCount);
            Assert.Equal(method, progress.Method);
        });
        Assert.NotEqual(Guid.Empty, completion.OperationId);
        Assert.Same(solver, completion.Solver);
        Assert.True(completion.Convergence.Success, completion.Convergence.Message);
        Assert.False(completion.IsResultUpdate);
        Assert.Equal(solver.OperationId, completion.OperationId);

        var sequence = CollapseProgress(events);
        var expected = new List<string>();
        if (assessmentScope == BindingAssessmentScope.Independent)
        {
            for (var position = 1; position <= models.Length; position++)
            {
                expected.Add("Fitting:" + position);
                if (!skip && method != ErrorEstimationMethod.None)
                    expected.Add("EstimatingUncertainty:" + position);
            }
            expected.Add("AssessingBinding");
            if (skip && method != ErrorEstimationMethod.None)
                for (var position = 1; position <= models.Length; position++)
                    expected.Add("EstimatingUncertainty:" + position);
        }
        else
        {
            expected.Add("Fitting");
            if (!skip && method != ErrorEstimationMethod.None) expected.Add("EstimatingUncertainty");
            expected.Add("AssessingBinding");
            if (skip && method != ErrorEstimationMethod.None) expected.Add("EstimatingUncertainty");
        }
        Assert.Equal(expected, sequence);
        if (method == ErrorEstimationMethod.None)
            Assert.DoesNotContain(events, progress => progress.Completed.HasValue);
        else
        {
            var counted = events.Where(progress => progress.Phase == FittingPhase.EstimatingUncertainty
                && progress.Completed.HasValue).ToList();
            Assert.NotEmpty(counted);
            Assert.All(counted, progress =>
            {
                Assert.NotNull(progress.Total);
                Assert.InRange(progress.Completed.Value, 0, progress.Total.Value);
                Assert.NotEmpty(FittingStatusPresentation.Counter(progress));
            });
        }
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood)]
    public async Task DeferredUncertaintyKeepsOriginalPositionsAndOmitsTheSkippedMember(ErrorEstimationMethod method)
    {
        var solver = PolicySolver(BindingAssessmentScope.Independent, method,
            PolicyModel(false, "Sample A"), PolicyModel(true, "Sample B"), PolicyModel(false, "Sample C"));
        var (events, _) = await CaptureProgress(solver);
        var sequence = CollapseProgress(events);
        Assert.Equal(new[] { "Fitting:1", "Fitting:2", "Fitting:3", "AssessingBinding",
            "EstimatingUncertainty:1", "EstimatingUncertainty:3" }, sequence);
        Assert.DoesNotContain(events, progress => progress.Phase == FittingPhase.EstimatingUncertainty
            && progress.MemberPosition == 2);
        var third = events.First(progress => progress.Phase == FittingPhase.EstimatingUncertainty
            && progress.MemberPosition == 3);
        Assert.Equal("Sample C", third.MemberName);
        Assert.Equal("Estimating uncertainty 3 of 3: Sample C", FittingStatusPresentation.Message(third));
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood)]
    public async Task EntirelySkippedRunNeverClaimsToEstimateUncertainty(ErrorEstimationMethod method)
    {
        var solver = PolicySolver(BindingAssessmentScope.Independent, method,
            PolicyModel(true, "Sample A"), PolicyModel(true, "Sample B"));
        var (events, completion) = await CaptureProgress(solver);
        Assert.Equal(new[] { "Fitting:1", "Fitting:2", "AssessingBinding" }, CollapseProgress(events));
        Assert.Equal(ErrorEstimationOutcome.SkippedNoBinding, completion.Convergence.ErrorEstimationOutcome);
    }

    [Theory]
    [InlineData(BindingAssessmentScope.Single)]
    [InlineData(BindingAssessmentScope.Independent)]
    [InlineData(BindingAssessmentScope.Pooled)]
    public async Task StopKeepsTheStoppingPhaseAndSuppressesLaterRefitCounters(BindingAssessmentScope scope)
    {
        var models = scope == BindingAssessmentScope.Single ? new[] { PolicyModel(false) }
            : new[] { PolicyModel(false, "first"), PolicyModel(false, "second") };
        var solver = PolicySolver(scope, ErrorEstimationMethod.BootstrapResiduals, models);
        solver.BootstrapIterations = 20;
        var raised = false;
        Action<FittingProgress> stop = progress =>
        {
            if (raised || progress.Phase != FittingPhase.EstimatingUncertainty
                || progress.Completed.GetValueOrDefault() == 0) return;
            raised = true;
            SolverInterface.TerminateAnalysisFlag.Raise();
        };
        var (events, completion) = await CaptureProgress(solver, stop);
        Assert.True(raised);
        var stopping = events.FindIndex(progress => progress.Phase == FittingPhase.Stopping);
        Assert.True(stopping >= 0);
        Assert.All(events.Skip(stopping), progress =>
        {
            Assert.Equal(FittingPhase.Stopping, progress.Phase);
            Assert.Null(progress.Completed);
            Assert.Null(progress.Total);
            Assert.Empty(FittingStatusPresentation.Counter(progress));
        });
        Assert.True(completion.Convergence.Stopped
            || completion.Convergence.ErrorEstimationOutcome == ErrorEstimationOutcome.Cancelled);
        Assert.StartsWith("Analysis cancelled after ", UncertaintyEstimationPresentation.CompletionMessages(
            solver, completion.Convergence, completion.Elapsed).First().Text);
    }

    [Fact]
    public async Task CompletionElapsedIncludesPreparationBeforeTheSolverAndKeepsSavedTimingsUnchanged()
    {
        using var activity = ProcessingActivity.TryBegin();
        using var ownership = activity.Enter();
        var solver = PolicySolver(BindingAssessmentScope.Single, ErrorEstimationMethod.None, PolicyModel(false));
        await Task.Delay(80); // Model/preparation work after admission, outside persisted solver timing.
        var (_, completion) = await CaptureProgress(solver);
        Assert.Same(activity.Operation, solver.Operation);
        Assert.True(completion.Elapsed >= TimeSpan.FromMilliseconds(60));
        Assert.True(completion.Elapsed - completion.Convergence.TotalTime >= TimeSpan.FromMilliseconds(50),
            "The operation elapsed clock includes preparation that the saved solver clock excludes.");
        var savedTime = completion.Convergence.TotalTime;
        await Task.Delay(20);
        Assert.Equal(savedTime, completion.Convergence.TotalTime);
        Assert.True(activity.Operation.Elapsed > completion.Elapsed);
    }

    [Fact]
    public async Task DelayedUiDispatchStillReceivesCompletionAfterTheOperationHasFinished()
    {
        var dispatcher = new ProgressQueuedDispatcher();
        var previous = PlatformServices.MainThreadDispatcher;
        var solver = PolicySolver(BindingAssessmentScope.Single, ErrorEstimationMethod.None, PolicyModel(false));
        var progress = new List<FittingProgress>();
        var completions = new List<FittingCompletion>();
        EventHandler<FittingProgress> onProgress = (sender, value) =>
        { if (ReferenceEquals(sender, solver)) progress.Add(value); };
        EventHandler<FittingCompletion> onComplete = (sender, value) =>
        { if (ReferenceEquals(sender, solver)) completions.Add(value); };
        SolverInterface.ProgressChanged += onProgress;
        SolverInterface.FittingCompleted += onComplete;
        PlatformServices.RegisterMainThreadDispatcher(dispatcher);
        try
        {
            solver.Analyze();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while ((solver.Operation == null || ProcessingActivity.IsBusy) && DateTime.UtcNow < deadline)
                await Task.Delay(5);
            Assert.False(ProcessingActivity.IsBusy);
            Assert.NotNull(solver.Operation);
            Assert.Empty(progress);
            Assert.Empty(completions);
            var terminalElapsed = solver.Operation.Elapsed;
            await Task.Delay(20);
            dispatcher.Drain();
            Assert.NotEmpty(progress);
            var completion = Assert.Single(completions);
            Assert.Equal(solver.Operation.Id, completion.OperationId);
            Assert.Same(solver, completion.Solver);
            Assert.True(completion.Convergence.Success, completion.Convergence.Message);
            Assert.True(completion.Elapsed <= terminalElapsed);
            Assert.True(completion.Elapsed >= completion.Convergence.TotalTime);
            Assert.All(progress, value => Assert.Equal(completion.OperationId, value.OperationId));
            Assert.Equal(terminalElapsed, solver.Operation.Elapsed);
        }
        finally
        {
            PlatformServices.RegisterMainThreadDispatcher(previous);
            SolverInterface.ProgressChanged -= onProgress;
            SolverInterface.FittingCompleted -= onComplete;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
    }

    [Fact]
    public async Task UpdateCompletionBelongsToTheAdmittedOperationAndPrecedesReplacementDisposition()
    {
        using var session = new ResultSession();
        var solver = PolicySolver(BindingAssessmentScope.Single, ErrorEstimationMethod.BootstrapResiduals, PolicyModel(false));
        await Run(solver);
        await WaitForUnownedOperationRelease();
        var result = new AnalysisITC.Core.Data.AnalysisResult(GlobalSolutionOf(solver));
        session.Load(result);
        var old = result.Solution;
        using var activity = ProcessingActivity.TryBegin();
        using var ownership = activity.Enter();
        FittingCompletion completion = null;
        EventHandler<FittingCompletion> capture = (sender, value) =>
        {
            Assert.True(value.IsResultUpdate);
            Assert.Equal(activity.Operation.Id, value.OperationId);
            Assert.Same(activity.Operation, value.Solver.Operation);
            Assert.Same(old, result.Solution);
            Assert.True(ProcessingActivity.IsBusy);
            Assert.Null(ProcessingActivity.TryBegin());
            completion = value;
        };
        SolverInterface.FittingCompleted += capture;
        try
        {
            var convergence = await AnalysisResultUpdater.UpdateAsync(result);
            Assert.NotNull(completion);
            Assert.Same(convergence, completion.Convergence);
            Assert.NotSame(old, result.Solution);
            Assert.True(ProcessingActivity.IsBusy); // The UI's root owns final replacement feedback.
            Assert.Equal(activity.Operation.Id, ProcessingActivity.Current.Id);
        }
        finally { SolverInterface.FittingCompleted -= capture; }
    }

    static List<string> CollapseProgress(IEnumerable<FittingProgress> events)
    {
        var result = new List<string>();
        foreach (var progress in events)
        {
            var value = progress.Phase.ToString();
            if (progress.Scope == FittingScope.Individual && progress.Phase is FittingPhase.Fitting or FittingPhase.EstimatingUncertainty)
                value += ":" + progress.MemberPosition;
            if (result.LastOrDefault() != value) result.Add(value);
        }
        return result;
    }

    static async Task<(List<FittingProgress> Progress, FittingCompletion Completion)> CaptureProgress(SolverInterface solver,
        Action<FittingProgress> observe = null)
    {
        var progress = new List<FittingProgress>();
        var finished = new TaskCompletionSource<FittingCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<FittingProgress> onProgress = (sender, value) =>
        {
            if (ReferenceEquals(sender, solver))
            {
                lock (progress) progress.Add(value);
                observe?.Invoke(value);
            }
        };
        EventHandler<FittingCompletion> onComplete = (sender, value) =>
        {
            if (ReferenceEquals(sender, solver)) finished.TrySetResult(value);
        };
        SolverInterface.ProgressChanged += onProgress;
        SolverInterface.FittingCompleted += onComplete;
        try
        {
            SolverInterface.TerminateAnalysisFlag.Lower();
            solver.Analyze();
            var completion = await finished.Task.WaitAsync(TimeSpan.FromSeconds(60));
            await WaitForUnownedOperationRelease();
            lock (progress) return (progress.ToList(), completion);
        }
        finally
        {
            SolverInterface.ProgressChanged -= onProgress;
            SolverInterface.FittingCompleted -= onComplete;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
    }

    static async Task WaitForUnownedOperationRelease()
    {
        if (ProcessingActivity.Current != null) return;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (ProcessingActivity.IsBusy && DateTime.UtcNow < deadline) await Task.Delay(1);
        Assert.False(ProcessingActivity.IsBusy);
    }

    sealed class ProgressQueuedDispatcher : IMainThreadDispatcher
    {
        readonly ConcurrentQueue<Action> queue = new();
        public void Invoke(Action action) => queue.Enqueue(action);
        public void Drain() { while (queue.TryDequeue(out var action)) action(); }
    }
}
