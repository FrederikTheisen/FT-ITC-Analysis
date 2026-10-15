using System;
using System.Reflection;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class NoBindingUncertaintySkipTests
{
    [Fact]
    public async Task MissingUpdateConvergenceReportsADescriptiveFailure()
    {
        // Exercise the real completion subscription without relying on an
        // optimizer failure to emit this otherwise exceptional event payload.
        var run = typeof(AnalysisResultUpdater).GetMethod("RunSolverAsync",
            BindingFlags.Static | BindingFlags.NonPublic);
        var task = (Task<SolverConvergence>)run.Invoke(null, new object[] { new MissingConvergenceSolver() });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task);

        Assert.Contains("without convergence information", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPrimaryProfileUpdateReportsFailureAndPreservesStoredState(bool dirty)
    {
        using var session = new ResultSession();
        var model = PolicyModel(false);
        var initial = PolicySolver(BindingAssessmentScope.Single, ErrorEstimationMethod.ProfileLikelihood, model);
        await Run(initial);
        var result = new AnalysisResult(GlobalSolutionOf(initial));
        session.Load(result);
        DocumentDirtyTracker.MarkClean();
        if (dirty) DocumentDirtyTracker.MarkDirty();
        var stamp = DocumentDirtyTracker.CaptureSaveStamp();
        var previous = result.Solution;
        var previousDate = result.Date;
        var previousAssessment = result.BindingAssessment;
        var previousComparison = result.NullComparison;

        // Force a failed primary outcome at the solver completion boundary.
        // The stored result must survive even if the candidate has intervals.
        EventHandler<SolverConvergence> fail = (_, convergence) =>
            convergence.MarkInvalidValues("Controlled primary failure");
        SolverInterface.AnalysisFinished += fail;
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisResultUpdater.UpdateAsync(result));
            Assert.Contains("Controlled primary failure", error.Message);
            Assert.Same(previous, result.Solution);
            Assert.Same(previousAssessment, result.BindingAssessment);
            Assert.Same(previousComparison, result.NullComparison);
            Assert.Equal(previousDate, result.Date);
            Assert.Same(model, model.Data.Model);
            Assert.Same(model.Solution, model.Data.Solution);
            Assert.Equal(stamp, DocumentDirtyTracker.CaptureSaveStamp());
            Assert.Equal(dirty, DocumentDirtyTracker.IsDirty);
            Assert.Null(previous.Convergence.ResultUpdateRetentionReason);
        }
        finally { SolverInterface.AnalysisFinished -= fail; }
    }

    sealed class MissingConvergenceSolver : SolverInterface
    {
        public override void Analyze()
        {
            base.Analyze();
            ReportAnalysisFinished(null);
        }
    }
}
