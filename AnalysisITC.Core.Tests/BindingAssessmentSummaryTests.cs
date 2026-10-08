using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Tests;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class BindingAssessmentSummaryTests
{
    [Fact]
    public void AggregateReturnsNotAssessedForEmptyInputAndIsOrderIndependent()
    {
        Assert.Equal(BindingAssessmentSummaryOutcome.NotAssessed,
            BindingAssessmentSummary.Aggregate(null));
        Assert.Equal(BindingAssessmentSummaryOutcome.NotAssessed,
            BindingAssessmentSummary.Aggregate(System.Array.Empty<BindingAssessmentOutcome>()));
        Assert.Equal(BindingAssessmentSummaryOutcome.Mixed, BindingAssessmentSummary.Aggregate(new[]
        {
            BindingAssessmentOutcome.BindingDetected,
            BindingAssessmentOutcome.Inconclusive,
            BindingAssessmentOutcome.NoBindingDetected,
            BindingAssessmentOutcome.NotAssessed,
        }));
        Assert.Equal(BindingAssessmentSummaryOutcome.Mixed, BindingAssessmentSummary.Aggregate(new[]
        {
            BindingAssessmentOutcome.NotAssessed,
            BindingAssessmentOutcome.NoBindingDetected,
            BindingAssessmentOutcome.Inconclusive,
            BindingAssessmentOutcome.BindingDetected,
        }));
    }

    [Theory]
    [InlineData(BindingAssessmentOutcome.NotAssessed, BindingAssessmentSummaryOutcome.NotAssessed)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentSummaryOutcome.NoBindingDetected)]
    [InlineData(BindingAssessmentOutcome.Inconclusive, BindingAssessmentSummaryOutcome.Inconclusive)]
    [InlineData(BindingAssessmentOutcome.BindingDetected, BindingAssessmentSummaryOutcome.BindingDetected)]
    public void AggregatePreservesUniformOutcome(BindingAssessmentOutcome outcome,
        BindingAssessmentSummaryOutcome expected)
    {
        Assert.Equal(expected, BindingAssessmentSummary.Aggregate(new[] { outcome, outcome }));
    }

    [Theory]
    [InlineData(BindingAssessmentOutcome.NotAssessed, BindingAssessmentSummaryOutcome.NotAssessed)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentSummaryOutcome.NoBindingDetected)]
    [InlineData(BindingAssessmentOutcome.Inconclusive, BindingAssessmentSummaryOutcome.Inconclusive)]
    [InlineData(BindingAssessmentOutcome.BindingDetected, BindingAssessmentSummaryOutcome.BindingDetected)]
    public void FromOutcomePreservesSingleAndPooledVerdicts(BindingAssessmentOutcome outcome,
        BindingAssessmentSummaryOutcome expected)
    {
        Assert.Equal(expected, BindingAssessmentSummary.FromOutcome(outcome));
    }

    [Fact]
    public void SingleResultRetainsItsAssessmentOutcome()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        result.RestoreBindingAssessment(BindingAssessmentState.Restore(
            BindingAssessmentOutcome.Inconclusive, BindingAssessmentState.CurrentRuleId, null));

        Assert.Equal(BindingAssessmentSummaryOutcome.Inconclusive, result.CollectionAssessmentOutcome);
    }
}
