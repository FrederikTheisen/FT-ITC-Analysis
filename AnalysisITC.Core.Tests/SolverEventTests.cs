using System;
using System.Threading;
using System.Threading.Tasks;

using Accord.Math.Optimization;
using AnalysisITC.Core.Analysis;

using Xunit;

namespace AnalysisITC.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SolverEventCollectionDefinition
{
    public const string Name = "Solver events";
}

[Collection(SolverEventCollectionDefinition.Name)]
public sealed class SolverEventTests
{
    [Fact]
    public async Task SharedStopCancelsEveryActiveNelderMeadSolver()
    {
        SolverInterface.TerminateAnalysisFlag.Lower();
        var first = new CancellationProbeSolver();
        var second = new CancellationProbeSolver();

        try
        {
            var firstTask = Task.Run(() => first.Solve());
            var secondTask = Task.Run(() => second.Solve());

            Assert.True(first.WaitUntilReady());
            Assert.True(second.WaitUntilReady());

            SolverInterface.TerminateAnalysisFlag.Raise();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await Task.WhenAll(firstTask, secondTask));
            Assert.True(first.Token.IsCancellationRequested);
            Assert.True(second.Token.IsCancellationRequested);
        }
        finally
        {
            first.Release();
            second.Release();
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
    }

    [Fact]
    public async Task CompletedSolverCannotDisposeAnotherSolversToken()
    {
        SolverInterface.TerminateAnalysisFlag.Lower();
        var completed = new CancellationProbeSolver(returnWithoutCancellation: true);
        var active = new CancellationProbeSolver();

        try
        {
            var completedTask = Task.Run(() => completed.Solve());
            var activeTask = Task.Run(() => active.Solve());

            Assert.True(completed.WaitUntilReady());
            Assert.True(active.WaitUntilReady());
            completed.Release();
            await completedTask;

            SolverInterface.TerminateAnalysisFlag.Raise();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await activeTask);
            Assert.True(active.Token.IsCancellationRequested);
        }
        finally
        {
            completed.Release();
            active.Release();
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
    }

    [Fact]
    public async Task LaterSolverReceivesFreshUncancelledToken()
    {
        SolverInterface.TerminateAnalysisFlag.Lower();
        var cancelled = new CancellationProbeSolver();

        try
        {
            var cancelledTask = Task.Run(() => cancelled.Solve());
            Assert.True(cancelled.WaitUntilReady());
            SolverInterface.TerminateAnalysisFlag.Raise();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelledTask);

            SolverInterface.TerminateAnalysisFlag.Lower();
            var later = new CancellationProbeSolver(returnWithoutCancellation: true);
            var laterTask = Task.Run(() => later.Solve());
            Assert.True(later.WaitUntilReady());
            Assert.False(later.Token.IsCancellationRequested);
            later.Release();
            await laterTask;
        }
        finally
        {
            cancelled.Release();
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void SilentSolversDoNotReportAnalysisStepFinished(bool silent, int expectedEvents)
    {
        var solver = new Solver { Silent = silent };
        var eventCount = 0;
        EventHandler handler = (_, _) => eventCount++;
        SolverInterface.AnalysisStepFinished += handler;

        try
        {
            solver.ReportAnalysisStepFinished();

            Assert.Equal(expectedEvents, eventCount);
        }
        finally
        {
            SolverInterface.AnalysisStepFinished -= handler;
        }
    }

    sealed class CancellationProbeSolver : SolverInterface
    {
        readonly ManualResetEventSlim ready = new(false);
        readonly ManualResetEventSlim release = new(false);
        readonly bool returnWithoutCancellation;

        internal CancellationToken Token { get; private set; }

        internal CancellationProbeSolver(bool returnWithoutCancellation = false)
        {
            this.returnWithoutCancellation = returnWithoutCancellation;
        }

        internal bool WaitUntilReady() => ready.Wait(TimeSpan.FromSeconds(5));
        internal void Release() => release.Set();

        protected override SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            var objective = new NonlinearObjectiveFunction(1, _ => 0);
            var simplex = new NelderMead(objective);
            SetCancellationToken(simplex);
            Token = simplex.Token;
            ready.Set();

            if (returnWithoutCancellation)
            {
                if (!release.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The cancellation probe was not released.");
                return null;
            }

            if (!Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The cancellation probe was not cancelled.");
            Token.ThrowIfCancellationRequested();
            return null;
        }
    }
}
