using System;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class NoBindingUncertaintySkipTests
{
    [Theory]
    [InlineData(BindingAssessmentScope.Single)]
    [InlineData(BindingAssessmentScope.Pooled)]
    [InlineData(BindingAssessmentScope.Independent)]
    public async Task EntirelySkippedProfileRetainsItsReasonInTheSharedSummary(BindingAssessmentScope scope)
    {
        var models = scope == BindingAssessmentScope.Single ? new[] { PolicyModel(true) }
            : new[] { PolicyModel(true, "first"), PolicyModel(true, "second") };
        var solver = PolicySolver(scope, ErrorEstimationMethod.ProfileLikelihood, models);
        await Run(solver);
        var solution = GlobalSolutionOf(solver);
        Assert.Equal(ErrorEstimationOutcome.SkippedNoBinding, solution.Convergence.ErrorEstimationOutcome);
        var summary = ProfileLikelihoodEstimator.Summarize(solution);
        Assert.Equal(ErrorEstimationOutcome.SkippedNoBinding, summary.Outcome);
        Assert.Equal(0, summary.TotalSides);
        Assert.Equal(0, summary.EndpointsFound);
        Assert.DoesNotContain("missing", summary.Diagnostics);
        Assert.Equal(SolverConvergence.SkippedForNoBindingDescription, ProfileLikelihoodDisplayFormatter.Status(summary));
    }

    [Fact]
    public async Task MissingEligibleProfileIsDistinctFromASkippedMember()
    {
        var solver = PolicySolver(BindingAssessmentScope.Independent, ErrorEstimationMethod.ProfileLikelihood,
            PolicyModel(false, "first"), PolicyModel(false, "second"));
        await Run(solver);
        var solution = GlobalSolutionOf(solver);
        solution.Solutions[1].ProfileLikelihoodRun = null;
        solution.Solutions[1].Convergence.SetErrorEstimationOutcome(ErrorEstimationOutcome.NotRun);
        var missing = ProfileLikelihoodEstimator.Summarize(solution);
        Assert.Equal(ErrorEstimationOutcome.PartialFailure, missing.Outcome);
        Assert.Equal(4, missing.TotalSides);
        Assert.Equal(2, missing.EndpointsFound);
        Assert.Contains("missing", missing.Diagnostics);
        solution.Solutions[1].Convergence.MarkErrorEstimationSkippedForNoBinding();
        var skipped = ProfileLikelihoodEstimator.Summarize(solution);
        Assert.Equal(ErrorEstimationOutcome.Completed, skipped.Outcome);
        Assert.Equal(2, skipped.TotalSides);
        Assert.DoesNotContain("missing", skipped.Diagnostics);
    }

    [Theory]
    [InlineData(ErrorEstimationOutcome.Cancelled, BindingAssessmentScope.Independent)]
    [InlineData(ErrorEstimationOutcome.CompleteFailure, BindingAssessmentScope.Independent)]
    [InlineData(ErrorEstimationOutcome.Cancelled, BindingAssessmentScope.Pooled)]
    [InlineData(ErrorEstimationOutcome.CompleteFailure, BindingAssessmentScope.Pooled)]
    public void ProfileMishapWithoutARunMustNotBecomeOrdinaryNotRun(
        ErrorEstimationOutcome mishap, BindingAssessmentScope scope)
    {
        var first = PolicyModel(true, "skipped");
        var second = PolicyModel(false, "failed");
        var solver = (GlobalSolver)PolicySolver(scope,
            ErrorEstimationMethod.ProfileLikelihood, first, second);
        first.Solution = SolutionInterface.FromModel(first, Convergence(scope == BindingAssessmentScope.Independent
            ? ErrorEstimationOutcome.SkippedNoBinding : ErrorEstimationOutcome.None));
        second.Solution = SolutionInterface.FromModel(second, Convergence(scope == BindingAssessmentScope.Independent
            ? mishap : ErrorEstimationOutcome.None));
        first.Solution.ErrorMethod = second.Solution.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        var solution = new GlobalSolution(solver, new() { first.Solution, second.Solution },
            scope == BindingAssessmentScope.Independent ? Aggregate(ErrorEstimationOutcome.SkippedNoBinding, mishap)
                : Convergence(mishap));
        var summary = ProfileLikelihoodEstimator.Summarize(solution);
        Assert.True(IsMishap(summary.Outcome), "A failed or cancelled member cannot become ordinary Not run.");
    }

    [Fact]
    public void ZeroCoordinateProfileIsLegitimateNotRunRatherThanMissingOrSkipped()
    {
        var member = PolicyModel(true);
        foreach (var parameter in member.Parameters.Table.Values) parameter.SetValue(parameter.Value, true);
        member.Solution = SolutionInterface.FromModel(member, Convergence(ErrorEstimationOutcome.NotRun));
        member.Solution.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        var solver = new Solver { Model = member, ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood };
        var summary = ProfileLikelihoodEstimator.Summarize(GlobalSolution.FromSingleExperimentSolver(solver));
        Assert.Equal(ErrorEstimationOutcome.NotRun, summary.Outcome);
        Assert.Equal(0, summary.TotalSides);
        Assert.Equal(0, summary.EndpointsFound);
        Assert.DoesNotContain("missing", summary.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(LifecycleCases))]
    public async Task SkippedMethodsAreExplainedInResultAndReportPresentation(
        ErrorEstimationMethod method, BindingAssessmentScope scope)
    {
        var models = scope == BindingAssessmentScope.Single ? new[] { PolicyModel(true) }
            : new[] { PolicyModel(true, "first"), PolicyModel(true, "second") };
        var solver = PolicySolver(scope, method, models);
        await Run(solver);
        var solution = GlobalSolutionOf(solver);
        Assert.Equal(method.Description(), UncertaintyEstimationPresentation.Method(solution));
        var section = UncertaintyEstimationPresentation.Section(solution);
        Assert.Equal(SolverConvergence.SkippedForNoBindingDescription, section.Rows.First().Value);
        Assert.DoesNotContain(section.Rows, row => row.Label is "Bootstraps" or "95% CI endpoints" or "Calculation time");
        var result = new AnalysisResult(solution);
        foreach (var purpose in new[] { ResultOutputPurpose.Standard, ResultOutputPurpose.Diagnostic })
        {
            var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions { OutputPurpose = purpose });
            var uncertaintyRows = document.Sections.Where(section => scope == BindingAssessmentScope.Pooled
                    || section.Kind == AnalysisReportSectionKind.Experiment)
                .SelectMany(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>())
                .SelectMany(block => block.Items).Where(item => item.Label == "Uncertainty").ToList();
            Assert.Equal(scope == BindingAssessmentScope.Pooled ? 1 : models.Length, uncertaintyRows.Count);
            Assert.All(uncertaintyRows, row => Assert.Contains("not estimated (no binding detected)", row.Value));
        }
    }
}
