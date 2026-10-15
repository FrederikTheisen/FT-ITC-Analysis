using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class NoBindingUncertaintySkipTests
{
    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, "Bootstrap")]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, "Leave-one-out")]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood, "Profile likelihood")]
    public async Task CompletionReportsCompleteAndSkippedUncertaintyWithTheSelectedMethod(
        ErrorEstimationMethod method, string name)
    {
        foreach (var noBinding in new[] { false, true })
        {
            var solver = PolicySolver(BindingAssessmentScope.Single, method, PolicyModel(noBinding));
            var convergence = await Run(solver);
            var messages = UncertaintyEstimationPresentation.CompletionMessages(solver, convergence,
                TimeSpan.FromSeconds(12));
            Assert.Equal("Analysis complete in 12 s", messages[0].Text);
            Assert.Equal(name + (noBinding ? " skipped: no binding detected" : " complete"), messages.Last().Text);
            Assert.Equal(3000, messages[0].DurationMilliseconds);
            Assert.Equal(3000, messages.Last().DurationMilliseconds);
        }
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, "Bootstrap")]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, "Leave-one-out")]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood, "Profile likelihood")]
    public async Task IndependentCompletionSummarizesMemberEvidenceDespiteAbsentCombinedRefits(
        ErrorEstimationMethod method, string name)
    {
        var solver = PolicySolver(BindingAssessmentScope.Independent, method,
            PolicyModel(false, "complete A"), PolicyModel(true, "skipped"), PolicyModel(false, "complete C"));
        var convergence = await Run(solver);
        Assert.Empty(GlobalSolutionOf(solver).BootstrapSolutions);
        Assert.Equal(name + ": 2 complete, 1 skipped (no binding)",
            UncertaintyEstimationPresentation.CompletionMessages(solver, convergence, TimeSpan.FromSeconds(12)).Last().Text);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, "Bootstrap incomplete: 5 of 8 refits")]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, "Leave-one-out incomplete: 5 of 8 refits")]
    public void AllIncompleteIndependentMembersCountTheirSavedRefitsRatherThanAggregateDiagnostics(
        ErrorEstimationMethod method, string expected)
    {
        var (solver, solution) = CompletionEvidence(method, new[] { 2, 3 },
            new[] { ErrorEstimationOutcome.PartialFailure, ErrorEstimationOutcome.PartialFailure }, requested: 4);
        Assert.Empty(solution.BootstrapSolutions);
        Assert.Equal(expected, UncertaintyEstimationPresentation.CompletionMessages(solver,
            solution.Convergence, TimeSpan.FromSeconds(12), solution: solution).Last().Text);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, "Bootstrap: 1 complete, 1 incomplete")]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, "Leave-one-out: 1 complete, 1 incomplete")]
    public void MixedCompletionDetectsShortMemberCoverageFromSavedEvidence(
        ErrorEstimationMethod method, string expected)
    {
        var (solver, solution) = CompletionEvidence(method, new[] { 4, 2 },
            new[] { ErrorEstimationOutcome.Completed, ErrorEstimationOutcome.Completed }, requested: 4);
        Assert.Equal(expected, UncertaintyEstimationPresentation.CompletionMessages(solver,
            solution.Convergence, TimeSpan.FromSeconds(12), solution: solution).Last().Text);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, "Bootstrap unavailable")]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, "Leave-one-out unavailable")]
    public void NoSavedRefitsAreUnavailableEvenIfTheAggregateClaimsSuccess(
        ErrorEstimationMethod method, string expected)
    {
        var (solver, solution) = CompletionEvidence(method, new[] { 0, 0 },
            new[] { ErrorEstimationOutcome.Completed, ErrorEstimationOutcome.Completed }, requested: 4);
        Assert.Equal(expected, UncertaintyEstimationPresentation.CompletionMessages(solver,
            solution.Convergence, TimeSpan.FromSeconds(12), solution: solution).Last().Text);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, "Bootstrap")]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, "Leave-one-out")]
    public void WholeAnalysisCancellationIncludesAnIndependentUncertaintyCancellation(
        ErrorEstimationMethod method, string name)
    {
        var (solver, solution) = CompletionEvidence(method, new[] { 4, 2 },
            new[] { ErrorEstimationOutcome.Completed, ErrorEstimationOutcome.Cancelled }, requested: 4);
        var messages = UncertaintyEstimationPresentation.CompletionMessages(solver,
            solution.Convergence, TimeSpan.FromSeconds(12), solution: solution);
        Assert.Equal("Analysis cancelled after 12 s", messages[0].Text);
        Assert.Equal(name + ": 1 complete, 1 cancelled", messages.Last().Text);
        var update = Assert.Single(UncertaintyEstimationPresentation.CompletionMessages(solver,
            solution.Convergence, TimeSpan.FromSeconds(12), resultUpdate: true, solution: solution));
        Assert.Equal("Update cancelled; the existing result was kept", update.Text);
        Assert.Equal(5000, update.DurationMilliseconds);
    }

    [Fact]
    public void PartialProfilesReportFoundEndpointsRatherThanFinishedSearchDirections()
    {
        var model = PolicyModel(false);
        model.Parameters.Table[ParameterType.Enthalpy1].SetValue(model.Parameters.Table[ParameterType.Enthalpy1].Value, false);
        var convergence = Convergence(ErrorEstimationOutcome.PartialFailure);
        model.Solution = SolutionInterface.FromModel(model, convergence);
        model.Solution.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        model.ModelCloneOptions.ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood;
        model.Solution.ProfileLikelihoodRun = new ProfileLikelihoodRunResult(.95,
            ProfileLikelihoodCalibration.UnweightedFCalibratedRss, 24, 2, 1, 22,
            10, 1, SolverAlgorithm.NelderMead, false, 2, 100, 24, 40,
            TimeSpan.FromSeconds(1), ErrorEstimationOutcome.PartialFailure,
            new[] { new ProfileCoordinateResult(new ProfileCoordinateId(ParameterType.Offset,
                ParameterBoundaryScope.Local, model.Data.UniqueID, 0), 0, -10, 10,
                new ProfileSideResult(ProfileSideOutcome.EndpointFound, -1),
                new ProfileSideResult(ProfileSideOutcome.SearchExhausted)),
                new ProfileCoordinateResult(new ProfileCoordinateId(ParameterType.Enthalpy1,
                ParameterBoundaryScope.Local, model.Data.UniqueID, 1), 0, -10, 10,
                new ProfileSideResult(ProfileSideOutcome.EndpointFound, -1),
                new ProfileSideResult(ProfileSideOutcome.EndpointFound, 1)) });
        var solver = new Solver { Model = model, ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood };
        var progress = new FittingProgress(Guid.NewGuid(), FittingPhase.EstimatingUncertainty,
            FittingScope.Single, method: ErrorEstimationMethod.ProfileLikelihood, completed: 4, total: 4, fraction: 1);
        Assert.Equal("Profile searches 4/4", FittingStatusPresentation.Counter(progress));
        Assert.Equal("Profile likelihood incomplete: 3 of 4 interval endpoints found",
            UncertaintyEstimationPresentation.CompletionMessages(solver, convergence,
                TimeSpan.FromSeconds(12)).Last().Text);
    }

    [Fact]
    public void BoundaryWarningsStayBetweenTheRoutineMessagesAndHonorNoBindingFiltering()
    {
        var (solver, solution) = CompletionEvidence(ErrorEstimationMethod.BootstrapResiduals,
            new[] { 4, 0 }, new[] { ErrorEstimationOutcome.Completed, ErrorEstimationOutcome.SkippedNoBinding }, 4);
        solution.Solutions[0].NullComparison = Comparison(20);
        solution.Solutions[1].NullComparison = Comparison(-2);
        solution.Convergence.SetParameterBoundaryContacts(new[]
        {
            new ParameterBoundaryContact(ParameterType.Offset, ParameterBoundaryScope.Local,
                solution.Solutions[0].Data.UniqueID, "member 1", ParameterBoundarySide.Upper, 100, 100),
            new ParameterBoundaryContact(ParameterType.Enthalpy1, ParameterBoundaryScope.Local,
                solution.Solutions[1].Data.UniqueID, "member 2", ParameterBoundarySide.Lower, -100, -100),
        });
        var messages = UncertaintyEstimationPresentation.CompletionMessages(solver,
            solution.Convergence, TimeSpan.FromSeconds(12), solution: solution);
        Assert.Equal(3, messages.Count);
        Assert.Equal("Analysis complete in 12 s", messages[0].Text);
        Assert.StartsWith("Warning: parameter bound reached:", messages[1].Text);
        Assert.Contains("member 1", messages[1].Text);
        Assert.DoesNotContain("member 2", messages[1].Text);
        Assert.Equal("Bootstrap: 1 complete, 1 skipped (no binding)", messages[2].Text);
        Assert.Equal(new[] { 3000, 5000, 3000 }, messages.Select(message => message.DurationMilliseconds));
    }

    [Fact]
    public void UpdatedResultWarningsUseEffectiveManualMemberAssessments()
    {
        var (solver, solution) = CompletionEvidence(ErrorEstimationMethod.BootstrapResiduals,
            new[] { 4, 4 }, new[] { ErrorEstimationOutcome.Completed, ErrorEstimationOutcome.Completed }, 4);
        var first = solution.Solutions[0];
        var second = solution.Solutions[1];
        first.NullComparison = Comparison(20);
        second.NullComparison = Comparison(-2);
        var result = new AnalysisResult(solution);
        result.SetMemberBindingAssessmentOverride(first.Guid, BindingAssessmentOutcome.NoBindingDetected);
        result.SetMemberBindingAssessmentOverride(second.Guid, BindingAssessmentOutcome.BindingDetected);
        solution.Convergence.SetParameterBoundaryContacts(new[]
        {
            new ParameterBoundaryContact(ParameterType.Offset, ParameterBoundaryScope.Local,
                first.Data.UniqueID, "automatically binding", ParameterBoundarySide.Upper, 100, 100),
            new ParameterBoundaryContact(ParameterType.Enthalpy1, ParameterBoundaryScope.Local,
                second.Data.UniqueID, "manually binding", ParameterBoundarySide.Lower, -100, -100),
        });

        var automatic = UncertaintyEstimationPresentation.CompletionMessages(solver,
            solution.Convergence, TimeSpan.FromSeconds(12), resultUpdate: true, solution: solution);
        Assert.Contains("automatically binding", automatic[1].Text);
        Assert.DoesNotContain("manually binding", automatic[1].Text);

        var effective = UncertaintyEstimationPresentation.CompletionMessages(solver,
            solution.Convergence, TimeSpan.FromSeconds(12), resultUpdate: true, solution: result.Solution,
            memberAssessments: result.MemberAssessments);
        Assert.Equal("Result updated in 12 s", effective[0].Text);
        Assert.Contains("manually binding", effective[1].Text);
        Assert.DoesNotContain("automatically binding", effective[1].Text);
        Assert.Equal(5000, effective[1].DurationMilliseconds);
        Assert.Equal("Bootstrap complete", effective.Last().Text);
    }

    static (GlobalSolver Solver, GlobalSolution Solution) CompletionEvidence(ErrorEstimationMethod method,
        int[] refitCounts, ErrorEstimationOutcome[] outcomes, int requested)
    {
        var models = refitCounts.Select((_, index) => PolicyModel(false, "member " + (index + 1))).ToArray();
        var solver = (GlobalSolver)PolicySolver(BindingAssessmentScope.Independent, method, models);
        solver.Model.ModelCloneOptions.ErrorEstimationMethod = method;
        for (var index = 0; index < models.Length; index++)
        {
            var model = models[index];
            // Four included observations give an independent leave-one-out expectation.
            foreach (var injection in model.Data.Injections.Skip(4)) injection.Include = false;
            foreach (var injection in model.Data.Injections.Take(4)) injection.Include = true;
            var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Termination = SolverTermination.Converged, ErrorEstimationOutcome = outcomes[index],
                RequestedBootstrapIterations = method == ErrorEstimationMethod.BootstrapResiduals ? requested : null,
                ErrorEstimationSucceededRefits = 999, ErrorEstimationAttemptedRefits = 1000,
            });
            model.Solution = SolutionInterface.FromModel(model, convergence);
            model.Solution.ErrorMethod = method;
            model.Solution.RestoreBootstrapSolutions(Enumerable.Range(0, refitCounts[index])
                .Select(_ => SolutionInterface.FromModel(model, Convergence())).ToList());
        }
        var aggregate = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Termination = SolverTermination.Converged, ErrorEstimationOutcome = ErrorEstimationOutcome.Completed,
            RequestedBootstrapIterations = method == ErrorEstimationMethod.BootstrapResiduals ? requested : null,
            ErrorEstimationSucceededRefits = 999, ErrorEstimationAttemptedRefits = 1000,
        });
        var solution = new GlobalSolution(solver, models.Select(model => model.Solution).ToList(), aggregate,
            reconstructBootstrap: false);
        solver.Model.Solution = solution;
        return (solver, solution);
    }
}
