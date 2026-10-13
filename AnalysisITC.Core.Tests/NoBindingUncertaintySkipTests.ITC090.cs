using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class NoBindingUncertaintySkipTests
{
    [Theory]
    [MemberData(nameof(LifecycleCases))]
    public async Task AcceptedUpdateAttachesOnlyThePrimaryModels(
        ErrorEstimationMethod method, BindingAssessmentScope scope)
    {
        using var session = new ResultSession();
        var models = scope == BindingAssessmentScope.Single ? new[] { PolicyModel(false) }
            : new[] { PolicyModel(false, "first"), PolicyModel(false, "second") };
        var initial = PolicySolver(scope, method, models);
        await Run(initial);
        var result = new AnalysisResult(GlobalSolutionOf(initial));
        session.Load(result);
        DocumentDirtyTracker.Initialize();
        DocumentDirtyTracker.MarkClean();
        var stamp = DocumentDirtyTracker.CaptureSaveStamp();
        var previous = result.Solution;
        var heats = models.Select(model => model.Data.Injections.Select(injection => injection.PeakArea).ToArray()).ToArray();

        await AnalysisResultUpdater.UpdateAsync(result);

        Assert.NotSame(previous, result.Solution);
        for (var index = 0; index < models.Length; index++)
        {
            var live = models[index].Data;
            var accepted = result.Solution.Solutions.Single(member => member.Data.UniqueID == live.UniqueID);
            Assert.Same(live, accepted.Data);
            Assert.Same(accepted.Model, live.Model);
            Assert.Same(accepted, live.Solution);
            Assert.Equal(heats[index], live.Injections.Select(injection => injection.PeakArea));
            Assert.All(accepted.BootstrapSolutions, replicate => Assert.NotSame(live, replicate.Data));
        }
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);
        Assert.True(DocumentDirtyTracker.IsDirty);
        Assert.False(DocumentDirtyTracker.MatchesSaveStamp(stamp));
    }

    [Fact]
    public async Task CandidateCopiesObservedHeatsAndAttributesWithoutChangingLiveData()
    {
        using var session = new ResultSession();
        var model = PolicyModel(false);
        var initial = PolicySolver(BindingAssessmentScope.Single, ErrorEstimationMethod.None, model);
        await Run(initial);
        var result = new AnalysisResult(GlobalSolutionOf(initial));
        session.Load(result);
        var experiment = model.Data;
        experiment.Injections[0].SetPeakArea(new FloatWithError(-1e-6, 2e-8, -1.1e-6, -0.95e-6));
        experiment.Injections[0].Include = false;
        var blank = PolicyModel(true, "blank").Data;
        foreach (var injection in blank.Injections) injection.SetPeakArea(new FloatWithError(2e-8));
        DataManager.AddData(blank);
        experiment.SetBufferSubtraction(blank, BufferSubtractionMethod.MatchedInjection, notify: false);
        var observed = experiment.Injections[0].PeakArea;
        Assert.NotEqual(experiment.Injections[0].RawPeakArea.Value, observed.Value);
        experiment.Attributes.Add(ExperimentAttribute.Parameter(AttributeKey.PreboundLigandConc, "", new FloatWithError(3e-6)));
        DocumentDirtyTracker.MarkClean();
        var stamp = DocumentDirtyTracker.CaptureSaveStamp();

        var solver = Assert.IsType<GlobalSolver>(AnalysisResultUpdater.PrepareSolver(result));
        var candidate = Assert.Single(solver.Model.Models).Data;

        Assert.NotSame(experiment, candidate);
        Assert.Equal(experiment.UniqueID, candidate.UniqueID);
        Assert.Equal(experiment.CellConcentration, candidate.CellConcentration);
        Assert.Equal(experiment.SyringeConcentration, candidate.SyringeConcentration);
        Assert.Equal(experiment.HeatMethod, candidate.HeatMethod);
        Assert.Equal(experiment.AppliedDilutionMethod, candidate.AppliedDilutionMethod);
        for (var index = 0; index < experiment.Injections.Count; index++)
        {
            var original = experiment.Injections[index];
            var copy = candidate.Injections[index];
            Assert.NotSame(original, copy);
            Assert.Same(candidate, copy.Experiment);
            Assert.Equal(original.Include, copy.Include);
            Assert.Equal(original.PeakArea, copy.PeakArea);
            Assert.Equal(original.ActualCellConcentration, copy.ActualCellConcentration);
            Assert.Equal(original.ActualTitrantConcentration, copy.ActualTitrantConcentration);
        }
        Assert.NotSame(experiment.Attributes.Last(), candidate.Attributes.Last());
        candidate.Attributes.Last().ParameterValue = new FloatWithError(8e-6);
        candidate.Injections[0].SetPeakArea(new FloatWithError(5e-6));
        Assert.Equal(3e-6, experiment.Attributes.Last().ParameterValue.Value);
        Assert.Equal(observed, experiment.Injections[0].PeakArea);
        Assert.Equal(-1e-6, experiment.Injections[0].RawPeakArea.Value);
        Assert.Same(model, experiment.Model);
        Assert.False(DocumentDirtyTracker.IsDirty);
        Assert.Equal(stamp, DocumentDirtyTracker.CaptureSaveStamp());
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, BindingAssessmentScope.Independent)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, BindingAssessmentScope.Independent)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, BindingAssessmentScope.Pooled)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, BindingAssessmentScope.Pooled)]
    public async Task StoredRootCancellationCannotBeHiddenByReturnedSuccess(
        ErrorEstimationMethod method, BindingAssessmentScope scope)
    {
        var solver = PolicySolver(scope, method, PolicyModel(false, "first"), PolicyModel(false, "second"));
        await Run(solver);
        var solution = GlobalSolutionOf(solver);
        Assert.NotEmpty(solution.BootstrapSolutions);
        solution.Convergence.SetErrorEstimationOutcome(ErrorEstimationOutcome.Cancelled);

        Assert.Throws<InvalidOperationException>(() => AnalysisResultUpdater.EnsureUpdateCanReplaceResult(
            solver, Convergence(ErrorEstimationOutcome.Completed), solution));
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    public async Task StaleCombinedRefitsCannotReplaceMissingIndependentMemberEvidence(ErrorEstimationMethod method)
    {
        var solver = PolicySolver(BindingAssessmentScope.Independent, method,
            PolicyModel(false, "first"), PolicyModel(false, "second"));
        var convergence = await Run(solver);
        var solution = GlobalSolutionOf(solver);
        Assert.NotEmpty(solution.BootstrapSolutions);
        foreach (var member in solution.Solutions)
        {
            member.RestoreBootstrapSolutions(new List<SolutionInterface>());
            member.Convergence.SetErrorEstimationOutcome(ErrorEstimationOutcome.NotRun);
        }

        Assert.Throws<InvalidOperationException>(() => AnalysisResultUpdater.EnsureUpdateCanReplaceResult(solver, convergence, solution));
    }

    [Fact]
    public async Task CancelledProfileMemberCannotHideBehindSkippedAggregate()
    {
        using var session = new ResultSession();
        var models = new[] { PolicyModel(true, "skipped"), PolicyModel(false, "eligible") };
        var initial = PolicySolver(BindingAssessmentScope.Independent, ErrorEstimationMethod.ProfileLikelihood, models);
        await Run(initial);
        var result = new AnalysisResult(GlobalSolutionOf(initial));
        session.Load(result);
        DocumentDirtyTracker.MarkClean();
        var stamp = DocumentDirtyTracker.CaptureSaveStamp();
        var previous = result.Solution;
        EventHandler<SolverConvergence> handler = (sender, convergence) =>
        {
            var candidate = GlobalSolutionOf((SolverInterface)sender);
            candidate.Solutions[1].Convergence.SetErrorEstimationOutcome(ErrorEstimationOutcome.Cancelled);
            convergence.SetErrorEstimationOutcome(ErrorEstimationOutcome.SkippedNoBinding);
        };
        SolverInterface.AnalysisFinished += handler;
        try
        {
            var convergence = await AnalysisResultUpdater.UpdateAsync(result);
            Assert.Contains("cancelled", convergence.ResultUpdateRetentionReason);
            Assert.Same(previous, result.Solution);
            Assert.All(models, model => Assert.Same(model, model.Data.Model));
            Assert.False(DocumentDirtyTracker.IsDirty);
            Assert.Equal(stamp, DocumentDirtyTracker.CaptureSaveStamp());
        }
        finally { SolverInterface.AnalysisFinished -= handler; }
    }

    [Fact]
    public async Task DocumentChangesDuringUpdatePreventAttachingTheCandidate()
    {
        using var session = new ResultSession();
        var model = PolicyModel(false);
        var initial = PolicySolver(BindingAssessmentScope.Single, ErrorEstimationMethod.None, model);
        await Run(initial);
        var result = new AnalysisResult(GlobalSolutionOf(initial));
        session.Load(result);
        DocumentDirtyTracker.Initialize();
        DocumentDirtyTracker.MarkClean();
        var previous = result.Solution;
        EventHandler<SolverConvergence> handler = (_, _) => model.Data.Comments = "Edited during update";
        SolverInterface.AnalysisFinished += handler;
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisResultUpdater.UpdateAsync(result));
            Assert.Contains("document changed", error.Message);
            Assert.Same(previous, result.Solution);
            Assert.Same(model, model.Data.Model);
            Assert.Equal("Edited during update", model.Data.Comments);
            Assert.True(DocumentDirtyTracker.IsDirty);
        }
        finally { SolverInterface.AnalysisFinished -= handler; }
    }
}
