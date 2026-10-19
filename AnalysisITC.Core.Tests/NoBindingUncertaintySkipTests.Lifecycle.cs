using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class NoBindingUncertaintySkipTests
{
    public static IEnumerable<object[]> LifecycleCases() =>
        from method in EstimationMethods
        from scope in Enum.GetValues<BindingAssessmentScope>()
        select new object[] { method, scope };

    [Theory]
    [MemberData(nameof(LifecycleCases))]
    public async Task ManualBindingThenUpdateRestoresUncertaintyAndRetainsOverride(
        ErrorEstimationMethod method, BindingAssessmentScope scope)
    {
        using var session = new ResultSession();
        var models = scope == BindingAssessmentScope.Single ? new[] { PolicyModel(true) }
            : new[] { PolicyModel(true, "first"), PolicyModel(true, "second") };
        var solver = PolicySolver(scope, method, models);
        await Run(solver);
        var result = new AnalysisResult(GlobalSolutionOf(solver));
        session.Load(result);
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);

        OverrideAll(result, BindingAssessmentOutcome.BindingDetected);
        Assert.Equal(AnalysisResultValidity.PartialInvalid, result.ValidityReport.Status);
        Assert.Contains(result.ValidityReport.Reasons, reason => reason.Contains(AnalysisResult.SkippedUncertaintyReason));

        // Removing the override makes the original no-binding decision authoritative again.
        result.UseAutomaticBindingAssessment();
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);
        OverrideAll(result, BindingAssessmentOutcome.BindingDetected);
        var original = result.Solution;
        var convergence = await AnalysisResultUpdater.UpdateAsync(result);

        Assert.NotSame(original, result.Solution);
        Assert.Equal(ErrorEstimationOutcome.Completed, convergence.ErrorEstimationOutcome);
        Assert.All(result.Solution.Solutions, member => AssertEstimated(member, method, scope == BindingAssessmentScope.Pooled));
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);
        if (result.IsIndependentAssessmentCollection)
            Assert.All(result.MemberAssessments, member => Assert.Equal(BindingAssessmentOutcome.BindingDetected, member.Assessment.ManualOverride));
        else
            Assert.Equal(BindingAssessmentOutcome.BindingDetected, result.BindingAssessment.ManualOverride);
    }

    [Theory]
    [MemberData(nameof(LifecycleCases))]
    public async Task AutomaticBindingCannotClearStalenessOfManuallySkippedFits(
        ErrorEstimationMethod method, BindingAssessmentScope scope)
    {
        using var session = new ResultSession();
        var models = scope == BindingAssessmentScope.Single ? new[] { PolicyModel(false) }
            : new[] { PolicyModel(false, "first"), PolicyModel(false, "second") };
        var solver = PolicySolver(scope, method, models);
        if (scope == BindingAssessmentScope.Independent)
            ((GlobalSolver)solver).ManualMemberAssessmentOverrides = models.ToDictionary(
                model => model.Data.UniqueID, _ => BindingAssessmentOutcome.NoBindingDetected);
        else solver.ManualAssessmentOverride = BindingAssessmentOutcome.NoBindingDetected;
        await Run(solver);
        var result = new AnalysisResult(GlobalSolutionOf(solver));
        OverrideAll(result, BindingAssessmentOutcome.NoBindingDetected);
        session.Load(result);
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);
        Assert.All(result.Solution.Solutions, member => AssertSkipped(member, method));

        OverrideAll(result, BindingAssessmentOutcome.BindingDetected);
        Assert.Equal(AnalysisResultValidity.PartialInvalid, result.ValidityReport.Status);
        result.UseAutomaticBindingAssessment();
        Assert.Equal(AnalysisResultValidity.PartialInvalid, result.ValidityReport.Status);
        Assert.Contains(result.ValidityReport.Reasons, reason => reason.Contains(AnalysisResult.SkippedUncertaintyReason));

        await AnalysisResultUpdater.UpdateAsync(result);
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);
        Assert.All(result.Solution.Solutions, member => AssertEstimated(member, method, scope == BindingAssessmentScope.Pooled));
        if (result.IsIndependentAssessmentCollection)
            Assert.All(result.MemberAssessments, member => Assert.Null(member.Assessment.ManualOverride));
        else Assert.Null(result.BindingAssessment.ManualOverride);
    }

    [Theory]
    [MemberData(nameof(LifecycleCases))]
    public async Task GenuineSkippedResultsReopenStrictlyAndCanBeUpdated(
        ErrorEstimationMethod method, BindingAssessmentScope scope)
    {
        using var session = new ResultSession();
        var models = scope switch
        {
            BindingAssessmentScope.Single => new[] { PolicyModel(true) },
            BindingAssessmentScope.Pooled => new[] { PolicyModel(true, "first"), PolicyModel(true, "second") },
            _ => new[] { PolicyModel(false, "eligible"), PolicyModel(true, "skipped") },
        };
        var solver = PolicySolver(scope, method, models);
        await Run(solver);
        var source = new AnalysisResult(GlobalSolutionOf(solver));
        var skippedId = models.Last().Data.UniqueID;
        if (source.IsIndependentAssessmentCollection)
            source.SetMemberBindingAssessmentOverride(models.Last().Solution.Guid, BindingAssessmentOutcome.NoBindingDetected);
        else source.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, models.Select(model => model.Data), new[] { source });
        package.Position = 0;
        var read = await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict);
        Assert.Empty(read.Issues);
        var restored = Assert.Single(read.Containers.OfType<AnalysisResult>());
        Assert.Equal(scope, BindingAssessmentScopes.For(restored.Solution));
        Assert.Equal(method, restored.Solution.ErrorEstimationMethod);
        int? request = method == ErrorEstimationMethod.BootstrapResiduals ? Iterations : null;
        Assert.Equal(request, restored.Solution.Convergence.RequestedBootstrapIterations);
        Assert.All(restored.Solution.Solutions, member => Assert.Equal(request, member.Convergence.RequestedBootstrapIterations));
        Assert.Equal(source.Solution.Convergence.ErrorEstimationOutcome, restored.Solution.Convergence.ErrorEstimationOutcome);
        Assert.Equal(source.NullComparison.DeltaAicc, restored.NullComparison.DeltaAicc);
        foreach (var before in source.Solution.Solutions)
        {
            var after = restored.Solution.Solutions.Single(member => member.Data.UniqueID == before.Data.UniqueID);
            Assert.Equal(before.Parameters.Keys.OrderBy(key => key), after.Parameters.Keys.OrderBy(key => key));
            foreach (var key in before.Parameters.Keys)
                Assert.Equal(before.Parameters[key].Value, after.Parameters[key].Value);
            Assert.Equal(before.ErrorMethod, after.ErrorMethod);
            Assert.Equal(before.Convergence.ErrorEstimationOutcome, after.Convergence.ErrorEstimationOutcome);
            Assert.Equal(before.NullComparison.DeltaAicc, after.NullComparison.DeltaAicc);
            if (before.Convergence.ErrorEstimationOutcome == ErrorEstimationOutcome.SkippedNoBinding
                || scope == BindingAssessmentScope.Pooled && source.Solution.Convergence.ErrorEstimationOutcome == ErrorEstimationOutcome.SkippedNoBinding)
                AssertSkipped(after, method);
            else AssertEstimated(after, method);
        }
        var skipped = restored.Solution.Solutions.Single(member => member.Data.UniqueID == skippedId);
        session.Load(restored);
        Assert.Equal(AnalysisResultValidity.Valid, restored.ValidityReport.Status);
        if (method == ErrorEstimationMethod.BootstrapResiduals)
        {
            FittingOptionsController.BootstrapIterations = 6;
            Assert.Equal(0, restored.Solution.BootstrapIterations);
            Assert.Equal(Iterations, AnalysisResultUpdater.GetEffectiveBootstrapIterations(restored));
            Assert.Equal(Iterations, AnalysisResultUpdater.PrepareSolver(restored).BootstrapIterations);
        }
        if (restored.IsIndependentAssessmentCollection)
        {
            Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, restored.GetMemberBindingAssessment(skipped).ManualOverride);
            restored.SetMemberBindingAssessmentOverride(skipped.Guid, BindingAssessmentOutcome.BindingDetected);
        }
        else
        {
            Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, restored.BindingAssessment.ManualOverride);
            restored.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
        }
        Assert.Equal(AnalysisResultValidity.PartialInvalid, restored.ValidityReport.Status);
        var previous = restored.Solution;
        await AnalysisResultUpdater.UpdateAsync(restored);
        Assert.NotSame(previous, restored.Solution);
        Assert.Equal(AnalysisResultValidity.Valid, restored.ValidityReport.Status);
        Assert.All(restored.Solution.Solutions, member => AssertEstimated(member, method, scope == BindingAssessmentScope.Pooled));
    }

    [Theory]
    [InlineData(BindingAssessmentScope.Independent, false, false, ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(BindingAssessmentScope.Independent, false, true, ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(BindingAssessmentScope.Independent, true, false, ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(BindingAssessmentScope.Pooled, false, false, ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(BindingAssessmentScope.Pooled, false, true, ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(BindingAssessmentScope.Independent, false, false, ErrorEstimationMethod.LeaveOneOut)]
    [InlineData(BindingAssessmentScope.Independent, false, true, ErrorEstimationMethod.LeaveOneOut)]
    [InlineData(BindingAssessmentScope.Independent, true, false, ErrorEstimationMethod.LeaveOneOut)]
    [InlineData(BindingAssessmentScope.Pooled, false, false, ErrorEstimationMethod.LeaveOneOut)]
    [InlineData(BindingAssessmentScope.Pooled, false, true, ErrorEstimationMethod.LeaveOneOut)]
    public async Task CancellationAfterRetainedRefitsPreservesTheStoredResult(
        BindingAssessmentScope scope, bool eligibleFirst, bool dirty, ErrorEstimationMethod method)
    {
        using var session = new ResultSession();
        var models = scope == BindingAssessmentScope.Pooled
            ? new[] { PolicyModel(false, "first"), PolicyModel(false, "second") }
            : eligibleFirst
                ? new[] { PolicyModel(false, "eligible"), PolicyModel(true, "skipped") }
                : new[] { PolicyModel(true, "skipped"), PolicyModel(false, "eligible") };
        var initial = PolicySolver(scope, method, models);
        await Run(initial);
        var result = new AnalysisResult(GlobalSolutionOf(initial));
        session.Load(result);
        if (dirty) DocumentDirtyTracker.MarkDirty();
        else DocumentDirtyTracker.MarkClean();
        var stamp = DocumentDirtyTracker.CaptureSaveStamp();
        var oldSolution = result.Solution;
        var oldComparison = result.NullComparison;
        var oldAssessment = result.BindingAssessment;
        var oldMembers = result.MemberAssessments.ToDictionary(member => member.SolutionId, member => member.Assessment);
        var oldValidity = result.ValiditySnapshot;
        var oldDate = result.Date;
        var oldModels = models.Select(model => model.Data.Model).ToArray();
        var oldAttachments = models.Select(model => model.Data.Solution).ToArray();
        SolverInterface running = null;
        SolverConvergence finishedConvergence = null;
        var cancellationRaised = false;
        var finishedRefits = 0;
        SolverInterface updaterSolver = null;
        Guid? operationId = null;
        EventHandler<TerminationFlag> started = (sender, _) =>
        {
            if (updaterSolver == null && sender is SolverInterface { IsResultUpdate: true, Silent: false } solver)
            {
                updaterSolver = solver;
                operationId = solver.OperationId;
            }
        };
        EventHandler<FittingProgress> updates = (sender, update) =>
        {
            if (!ReferenceEquals(sender, updaterSolver) || update.OperationId != operationId
                || update.Method != method || update.Phase != FittingPhase.EstimatingUncertainty
                || !update.Completed.HasValue || !update.Total.HasValue || update.Completed.Value <= 0
                || update.Completed.Value >= update.Total.Value) return;
            if (!cancellationRaised)
            {
                finishedRefits = update.Completed.Value;
                cancellationRaised = true;
                SolverInterface.TerminateAnalysisFlag.Raise();
            }
        };
        EventHandler<SolverConvergence> finished = (sender, value) =>
        {
            if (!ReferenceEquals(sender, updaterSolver)) return;
            running = sender as SolverInterface;
            finishedConvergence = value;
        };
        SolverInterface.AnalysisStarted += started;
        SolverInterface.ProgressChanged += updates;
        SolverInterface.AnalysisFinished += finished;
        try
        {
            var error = await Record.ExceptionAsync(() => AnalysisResultUpdater.UpdateAsync(result));
            Assert.True(cancellationRaised);
            Assert.True(finishedRefits > 0);
            Assert.IsType<InvalidOperationException>(error);
            Assert.NotNull(finishedConvergence);
            Assert.True(finishedConvergence.Stopped || IsMishap(finishedConvergence.ErrorEstimationOutcome));
            var candidate = GlobalSolutionOf(running);
            if (candidate != null && !finishedConvergence.Stopped)
                Assert.Contains(candidate.Solutions, member => member.BootstrapSolutions.Count > 0);
            Assert.Same(oldSolution, result.Solution);
            Assert.Same(oldComparison, result.NullComparison);
            Assert.Same(oldAssessment, result.BindingAssessment);
            Assert.Same(oldValidity, result.ValiditySnapshot);
            Assert.Equal(oldDate, result.Date);
            foreach (var member in result.MemberAssessments)
                Assert.Same(oldMembers[member.SolutionId], member.Assessment);
            Assert.Equal(oldModels, models.Select(model => model.Data.Model));
            Assert.Equal(oldAttachments, models.Select(model => model.Data.Solution));
            Assert.Equal(dirty, DocumentDirtyTracker.IsDirty);
            Assert.Equal(stamp, DocumentDirtyTracker.CaptureSaveStamp());
        }
        finally
        {
            SolverInterface.AnalysisStarted -= started;
            SolverInterface.ProgressChanged -= updates;
            SolverInterface.AnalysisFinished -= finished;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, ErrorEstimationOutcome.Cancelled)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, ErrorEstimationOutcome.Cancelled)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, ErrorEstimationOutcome.NotRun)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, ErrorEstimationOutcome.NotRun)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, ErrorEstimationOutcome.None)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, ErrorEstimationOutcome.CompleteFailure)]
    public async Task MisleadingAggregateCannotAdmitMissingOrCancelledMemberEvidence(
        ErrorEstimationMethod method, ErrorEstimationOutcome memberOutcome)
    {
        var solver = PolicySolver(BindingAssessmentScope.Independent, method,
            PolicyModel(true, "skipped"), PolicyModel(false, "eligible"));
        await Run(solver);
        var solution = GlobalSolutionOf(solver);
        var eligible = solution.Solutions[1];
        eligible.RestoreBootstrapSolutions(new());
        eligible.Convergence.SetErrorEstimationOutcome(memberOutcome);
        var misleading = Convergence(ErrorEstimationOutcome.SkippedNoBinding);
        Assert.Throws<InvalidOperationException>(() =>
            AnalysisResultUpdater.EnsureUpdateCanReplaceResult(solver, misleading, solution));
    }

    [Fact]
    public async Task MethodNoneNeedsNoReplicatesToReplaceAResult()
    {
        var solver = PolicySolver(BindingAssessmentScope.Independent, ErrorEstimationMethod.None,
            PolicyModel(true, "first"), PolicyModel(true, "second"));
        var convergence = await Run(solver);
        Assert.Equal(ErrorEstimationOutcome.None, convergence.ErrorEstimationOutcome);
        AnalysisResultUpdater.EnsureUpdateCanReplaceResult(solver, convergence, GlobalSolutionOf(solver));
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    public async Task UsablePartialEstimateKeepsMissingEligibleEvidenceVisible(ErrorEstimationMethod method)
    {
        var solver = PolicySolver(BindingAssessmentScope.Independent, method,
            PolicyModel(true, "skipped"), PolicyModel(false, "complete"), PolicyModel(false, "missing"));
        var convergence = await Run(solver);
        var solution = GlobalSolutionOf(solver);
        solution.Solutions[2].RestoreBootstrapSolutions(new());
        solution.Solutions[2].Convergence.SetErrorEstimationOutcome(ErrorEstimationOutcome.NotRun);
        // Partial success remains usable, but the originally completed aggregate is stale.
        AnalysisResultUpdater.EnsureUpdateCanReplaceResult(solver, convergence, solution);
        Assert.True(IsMishap(convergence.ErrorEstimationOutcome));
        Assert.NotEmpty(solution.Solutions[1].BootstrapSolutions);
        Assert.Empty(solution.Solutions[2].BootstrapSolutions);
    }

    [Theory]
    [InlineData(BindingAssessmentScope.Independent)]
    [InlineData(BindingAssessmentScope.Pooled)]
    public async Task CancelledProfilePreservesLiveAttachmentsAndSavedEvidence(BindingAssessmentScope scope)
    {
        using var session = new ResultSession();
        var models = scope == BindingAssessmentScope.Independent
            ? new[] { PolicyModel(true, "skipped"), PolicyModel(false, "eligible") }
            : new[] { PolicyModel(false, "first"), PolicyModel(false, "second") };
        var solver = PolicySolver(scope, ErrorEstimationMethod.ProfileLikelihood, models);
        await Run(solver);
        var result = new AnalysisResult(GlobalSolutionOf(solver));
        session.Load(result);
        DocumentDirtyTracker.MarkClean();
        var stamp = DocumentDirtyTracker.CaptureSaveStamp();
        var previous = result.Solution;
        var comparison = result.NullComparison;
        var validity = result.ValiditySnapshot;
        var date = result.Date;
        var attachments = models.Select(model => model.Data.Model).ToArray();
        var cancelled = false;
        SolverInterface updaterSolver = null;
        Guid? operationId = null;
        EventHandler<TerminationFlag> started = (sender, _) =>
        {
            if (updaterSolver == null && sender is SolverInterface { IsResultUpdate: true, Silent: false } active)
            {
                updaterSolver = active;
                operationId = active.OperationId;
            }
        };
        EventHandler<FittingProgress> handler = (sender, update) =>
        {
            if (cancelled || !ReferenceEquals(sender, updaterSolver) || update.OperationId != operationId
                || update.Method != ErrorEstimationMethod.ProfileLikelihood
                || update.Phase != FittingPhase.EstimatingUncertainty || !update.Completed.HasValue
                || !update.Total.HasValue || update.Completed.Value <= 0 || update.Completed.Value >= update.Total.Value) return;
            cancelled = true;
            SolverInterface.TerminateAnalysisFlag.Raise();
        };
        SolverInterface.AnalysisStarted += started;
        SolverInterface.ProgressChanged += handler;
        try
        {
            var convergence = await AnalysisResultUpdater.UpdateAsync(result).WaitAsync(TimeSpan.FromSeconds(60));
            Assert.True(cancelled);
            Assert.True(convergence.Stopped || IsMishap(convergence.ErrorEstimationOutcome));
            Assert.Same(previous, result.Solution);
            Assert.Same(comparison, result.NullComparison);
            Assert.Same(validity, result.ValiditySnapshot);
            Assert.Equal(date, result.Date);
            Assert.Equal(attachments, models.Select(model => model.Data.Model));
            Assert.False(DocumentDirtyTracker.IsDirty);
            Assert.Equal(stamp, DocumentDirtyTracker.CaptureSaveStamp());
        }
        finally
        {
            SolverInterface.AnalysisStarted -= started;
            SolverInterface.ProgressChanged -= handler;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
    }

    static void OverrideAll(AnalysisResult result, BindingAssessmentOutcome outcome)
    {
        if (result.IsIndependentAssessmentCollection)
            foreach (var member in result.Solution.Solutions)
                result.SetMemberBindingAssessmentOverride(member.Guid, outcome);
        else result.SetBindingAssessmentOverride(outcome);
    }

    sealed class ResultSession : IDisposable
    {
        readonly bool previousDirty = DocumentDirtyTracker.IsDirty;
        public ResultSession()
        {
            DataManager.Clear(DataClearMode.ResetSession);
            GlobalModelFactory.ClearPreviousParameters();
        }
        public void Load(AnalysisResult result)
        {
            DataManager.Clear(DataClearMode.ResetSession);
            foreach (var member in result.Solution.Solutions) DataManager.AddData(member.Data);
            DataManager.AddData(result);
            DataManager.LoadResultSolutionsToExperiments(result);
        }
        public void Dispose()
        {
            DataManager.Clear(DataClearMode.ResetSession);
            GlobalModelFactory.ClearPreviousParameters();
            if (previousDirty) DocumentDirtyTracker.MarkDirty();
            else DocumentDirtyTracker.MarkClean();
        }
    }
}
