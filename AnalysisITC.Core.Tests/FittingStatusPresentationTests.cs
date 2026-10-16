using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(SolverEventCollectionDefinition.Name)]
public sealed class FittingStatusPresentationTests
{
    public static IEnumerable<object[]> RunningMessages()
    {
        yield return new object[] { FittingPhase.Fitting, FittingScope.Single, "Fitting: Sample B" };
        yield return new object[] { FittingPhase.AssessingBinding, FittingScope.Single, "Assessing binding: Sample B" };
        yield return new object[] { FittingPhase.EstimatingUncertainty, FittingScope.Single, "Estimating uncertainty: Sample B" };
        yield return new object[] { FittingPhase.Fitting, FittingScope.Individual, "Fitting 2 of 3: Sample B" };
        yield return new object[] { FittingPhase.AssessingBinding, FittingScope.Individual, "Assessing binding…" };
        yield return new object[] { FittingPhase.EstimatingUncertainty, FittingScope.Individual, "Estimating uncertainty 2 of 3: Sample B" };
        yield return new object[] { FittingPhase.Fitting, FittingScope.Global, "Fitting 3 experiments globally…" };
        yield return new object[] { FittingPhase.AssessingBinding, FittingScope.Global, "Assessing binding…" };
        yield return new object[] { FittingPhase.EstimatingUncertainty, FittingScope.Global, "Estimating uncertainty…" };
        foreach (var scope in Enum.GetValues<FittingScope>())
            yield return new object[] { FittingPhase.Stopping, scope, "Stopping analysis…" };
    }

    [Theory]
    [MemberData(nameof(RunningMessages))]
    public void RunningWordingMatchesThePhaseAndActualFittingScope(FittingPhase phase, FittingScope scope, string expected)
    {
        var previous = FittingStatusPresentation.ShowExperimentNames;
        try
        {
            FittingStatusPresentation.ShowExperimentNames = true;
            var progress = new FittingProgress(Guid.NewGuid(), phase, scope, 2, 3, "Sample B");
            Assert.Equal(expected, FittingStatusPresentation.Message(progress));
            var parts = FittingStatusPresentation.Parts(progress);
            Assert.Equal(expected, parts.Text);
            if (phase != FittingPhase.Stopping && scope != FittingScope.Global
                && !(scope == FittingScope.Individual && phase == FittingPhase.AssessingBinding))
                Assert.Equal("Sample B", parts.Name);
            else Assert.Null(parts.Name);
        }
        finally { FittingStatusPresentation.ShowExperimentNames = previous; }
    }

    [Fact]
    public void NamesCanBeHiddenWithoutLosingTheActionOrOriginalPosition()
    {
        var previous = FittingStatusPresentation.ShowExperimentNames;
        try
        {
            FittingStatusPresentation.ShowExperimentNames = false;
            Assert.Equal("Fitting 2 of 3", FittingStatusPresentation.Message(
                new FittingProgress(Guid.NewGuid(), FittingPhase.Fitting, FittingScope.Individual, 2, 3, "Sample B")));
            Assert.Equal("Fitting…", FittingStatusPresentation.Message(
                new FittingProgress(Guid.NewGuid(), FittingPhase.Fitting, FittingScope.Single, memberName: "Sample B")));
            Assert.Equal("Estimating uncertainty 3 of 3", FittingStatusPresentation.Message(
                new FittingProgress(Guid.NewGuid(), FittingPhase.EstimatingUncertainty, FittingScope.Individual, 3, 3, "Sample C")));
        }
        finally { FittingStatusPresentation.ShowExperimentNames = previous; }
    }

    [Fact]
    public void LongNamesRemainTheLastPartSoThePlatformCanTruncateTheTail()
    {
        var previous = FittingStatusPresentation.ShowExperimentNames;
        try
        {
            FittingStatusPresentation.ShowExperimentNames = true;
            var name = new string('A', 500) + " β-cell 測定";
            var parts = FittingStatusPresentation.Parts(new FittingProgress(Guid.NewGuid(),
                FittingPhase.EstimatingUncertainty, FittingScope.Individual, 3, 4, name));
            Assert.Equal("Estimating uncertainty", parts.Action);
            Assert.Equal(" 3 of 4", parts.Position);
            Assert.Equal(name, parts.Name);
            Assert.Equal("Estimating uncertainty 3 of 4: " + name, parts.Text);
        }
        finally { FittingStatusPresentation.ShowExperimentNames = previous; }
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, "Bootstrap 3/8")]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, "Leave-one-out 3/8")]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood, "Profile searches 3/8")]
    public void SecondaryCounterNamesTheWorkBeingCounted(ErrorEstimationMethod method, string expected)
    {
        var operation = Guid.NewGuid();
        var progress = new FittingProgress(operation, FittingPhase.EstimatingUncertainty,
            FittingScope.Single, method: method, completed: 3, total: 8, fraction: .375f);
        Assert.Equal(expected, FittingStatusPresentation.Counter(progress));
        Assert.Equal(.375f, progress.Fraction);
        Assert.Empty(FittingStatusPresentation.Counter(new FittingProgress(operation,
            FittingPhase.Stopping, FittingScope.Single, method: method, completed: 3, total: 8)));
        Assert.Empty(FittingStatusPresentation.Counter(new FittingProgress(operation,
            FittingPhase.Fitting, FittingScope.Single, method: method, completed: 3, total: 8)));
    }

    [Theory]
    [InlineData(-1, "0.0 s")]
    [InlineData(0, "0.0 s")]
    [InlineData(2.34, "2.3 s")]
    [InlineData(9.94, "9.9 s")]
    [InlineData(9.95, "10 s")]
    [InlineData(10, "10 s")]
    [InlineData(48.4, "48 s")]
    [InlineData(59.49, "59 s")]
    [InlineData(59.5, "1 min 0 s")]
    [InlineData(60, "1 min 0 s")]
    [InlineData(250, "4 min 10 s")]
    [InlineData(3599.5, "60 min 0 s")]
    [InlineData(3661, "61 min 1 s")]
    public void WholeOperationDurationUsesNormalizedSecondsAndMinutes(double seconds, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(expected, FittingStatusPresentation.Duration(TimeSpan.FromSeconds(seconds)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(SolverTermination.Converged, "Analysis complete in 12 s")]
    [InlineData(SolverTermination.IterationLimit, "Analysis complete in 12 s; fit stopped at the iteration limit")]
    [InlineData(SolverTermination.EvaluationLimit, "Analysis complete in 12 s; fit stopped at the evaluation limit")]
    [InlineData(SolverTermination.TimeLimit, "Analysis complete in 12 s; fit stopped at the time limit")]
    [InlineData(SolverTermination.Cancelled, "Analysis cancelled after 12 s")]
    public void FirstCompletionMessageDescribesTheWholeAnalysis(SolverTermination termination, string expected)
    {
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Termination = termination, Iterations = 9876, Loss = 12345,
            Algorithm = SolverAlgorithm.NelderMead, TimeSeconds = 1,
        });
        var message = Assert.Single(UncertaintyEstimationPresentation.CompletionMessages(null,
            convergence, TimeSpan.FromSeconds(12)));
        Assert.Equal(expected, message.Text);
        Assert.Equal(3000, message.DurationMilliseconds);
        Assert.DoesNotContain("9876", message.Text);
        Assert.DoesNotContain("12345", message.Text);
        Assert.DoesNotContain("RMSD", message.Text);
        Assert.DoesNotContain("Nelder", message.Text);
        Assert.Equal(TimeSpan.FromSeconds(1), convergence.TotalTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrMissingCompletionHasOnlyADescriptiveFiveSecondMessage(bool resultUpdate)
    {
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        { Termination = SolverTermination.InvalidValues, FailureReason = "Invalid enthalpy" });
        var prefix = resultUpdate ? "Result update failed: " : "Analysis failed: ";
        var failure = Assert.Single(UncertaintyEstimationPresentation.CompletionMessages(null,
            convergence, TimeSpan.FromSeconds(12), resultUpdate));
        Assert.Equal(prefix + "Invalid enthalpy", failure.Text);
        Assert.Equal(5000, failure.DurationMilliseconds);
        var missing = Assert.Single(UncertaintyEstimationPresentation.CompletionMessages(null,
            null, TimeSpan.FromSeconds(12), resultUpdate));
        Assert.Equal(prefix + "Completion information unavailable.", missing.Text);
        Assert.Equal(5000, missing.DurationMilliseconds);
    }

    [Fact]
    public void RetainedUpdateHasPrecedenceOverSuccessAndOldUncertainty()
    {
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        { Termination = SolverTermination.Converged, ErrorEstimationOutcome = ErrorEstimationOutcome.Completed });
        convergence.ResultUpdateRetentionReason = "Existing profile retained: fewer complete intervals.";
        var messages = UncertaintyEstimationPresentation.CompletionMessages(
            new Solver { ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood }, convergence,
            TimeSpan.FromSeconds(12), true);
        var retained = Assert.Single(messages);
        Assert.Equal(convergence.ResultUpdateRetentionReason, retained.Text);
        Assert.Equal(5000, retained.DurationMilliseconds);
    }

    [Fact]
    public void PublicationFromAnOldOperationDoesNotEraseTheCurrentStatusOrCounter()
    {
        var old = ProcessingActivity.TryBegin();
        old.Dispose();
        using var current = ProcessingActivity.TryBegin();
        var statuses = new List<string>();
        var counters = new List<string>();
        EventHandler<string> status = (_, text) => statuses.Add(text);
        EventHandler<string> secondary = (_, text) => counters.Add(text);
        StatusBarManager.StatusUpdated += status;
        StatusBarManager.SecondaryStatusUpdated += secondary;
        try
        {
            UncertaintyEstimationPresentation.PublishCompletion(null,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot { Termination = SolverTermination.Converged }),
                TimeSpan.FromSeconds(1), operationId: old.Operation.Id);
            Assert.Empty(statuses);
            Assert.Empty(counters);
        }
        finally
        {
            StatusBarManager.StatusUpdated -= status;
            StatusBarManager.SecondaryStatusUpdated -= secondary;
        }
    }

    [Fact]
    public void ParallelRefitCountersAndMonotonicFractionsKeepCapturedMemberOffsets()
    {
        var dispatcher = new QueuedDispatcher();
        var previousDispatcher = PlatformServices.MainThreadDispatcher;
        var previousStep = SolverUpdate.Step;
        var previousTotal = SolverUpdate.TotalSteps;
        var owner = new Solver { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals, BootstrapIterations = 100 };
        var child = new Solver { Silent = true, BootstrapIterations = 100 };
        child.SetProgressOwner(owner, 0, null);
        var received = new List<FittingProgress>();
        EventHandler<FittingProgress> capture = (sender, progress) =>
        { if (ReferenceEquals(sender, owner)) received.Add(progress); };
        SolverInterface.ProgressChanged += capture;
        PlatformServices.RegisterMainThreadDispatcher(dispatcher);
        try
        {
            SolverUpdate.Step = 1;
            SolverUpdate.TotalSteps = 3;
            Parallel.For(1, 101, completed => child.ReportBootstrapProgress(completed));
            Assert.Empty(received);
            // The UI may receive this batch after the solver moves to a different member.
            SolverUpdate.Step = 2;
            SolverUpdate.TotalSteps = 9;
            dispatcher.Drain();
            Assert.Equal(100, received.Count);
            Assert.Equal(100, received.Last().Completed);
            for (var index = 1; index < received.Count; index++)
                Assert.True(received[index].Completed >= received[index - 1].Completed);
            Assert.All(received, progress => Assert.InRange(progress.Fraction.Value, 1f / 3 - 1e-6f, 2f / 3 + 1e-6f));
            for (var index = 1; index < received.Count; index++)
                Assert.True(received[index].Fraction >= received[index - 1].Fraction);
            Assert.Equal(2d / 3, received.Last().Fraction.Value, 6);
            Assert.All(received, progress =>
            {
                Assert.Equal(owner.OperationId, progress.OperationId);
                Assert.Equal(100, progress.Total);
                Assert.InRange(progress.Completed.Value, 1, 100);
            });
            Assert.All(typeof(FittingProgress).GetProperties(), property => Assert.False(property.CanWrite));
        }
        finally
        {
            SolverInterface.ProgressChanged -= capture;
            PlatformServices.RegisterMainThreadDispatcher(previousDispatcher);
            SolverUpdate.Step = previousStep;
            SolverUpdate.TotalSteps = previousTotal;
        }
    }

    sealed class QueuedDispatcher : IMainThreadDispatcher
    {
        readonly ConcurrentQueue<Action> queue = new();
        public void Invoke(Action action) => queue.Enqueue(action);
        public void Drain() { while (queue.TryDequeue(out var action)) action(); }
    }
}
