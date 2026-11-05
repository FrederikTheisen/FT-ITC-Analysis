using System;
using System.Reflection;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

// ITC-185: a residual bootstrap that keeps fewer than 50 refits is not complete.
public sealed class RetainedBootstrapRefitMinimumTests
{
    [Fact]
    public void SharedMinimumIsFiftyRetainedRefits()
    {
        Assert.Equal(50, FittingOptionsController.MinimumRetainedBootstrapRefits);
        Assert.Equal(50, BootstrapCorrelationAnalyzer.DefaultMinimumCompleteReplicates);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, 0, 1, ErrorEstimationOutcome.PartialFailure, true)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, 0, 9, ErrorEstimationOutcome.PartialFailure, true)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, 0, 49, ErrorEstimationOutcome.PartialFailure, true)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, 0, 50, ErrorEstimationOutcome.Completed, false)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, 0, 500, ErrorEstimationOutcome.Completed, false)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, 3, 60, ErrorEstimationOutcome.PartialFailure, false)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, 3, 9, ErrorEstimationOutcome.PartialFailure, false)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, 9, 0, ErrorEstimationOutcome.CompleteFailure, false)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, 0, 5, ErrorEstimationOutcome.Completed, false)]
    public void RunTimeOutcomeAppliesTheMinimumOnlyToLossFreeResidualBootstraps(
        ErrorEstimationMethod method, int failed, int kept, ErrorEstimationOutcome expected, bool belowMinimum)
    {
        var convergence = CreateConvergence();

        convergence.ApplyErrorEstimationResult(method, failed, kept, TimeSpan.Zero, requested: failed + kept);

        Assert.Equal(expected, convergence.ErrorEstimationOutcome);
        Assert.Equal(belowMinimum,
            convergence.ErrorEstimationSummary.Contains("below the minimum of 50 retained refits"));
        Assert.Equal(belowMinimum, SolverConvergence.IsBelowRetainedRefitMinimum(method, failed, kept));
    }

    [Fact]
    public void UnknownFailureCountIsNotTreatedAsBelowTheMinimum()
    {
        Assert.False(SolverConvergence.IsBelowRetainedRefitMinimum(ErrorEstimationMethod.BootstrapResiduals, null, 9));
        Assert.False(SolverConvergence.IsBelowRetainedRefitMinimum(ErrorEstimationMethod.BootstrapResiduals, 0, null));
    }

    [Fact]
    public void ReportStatesKeptAndNeededRefitsForALossFreeShortRun()
    {
        var convergence = CreateConvergence();
        convergence.ApplyErrorEstimationResult(ErrorEstimationMethod.BootstrapResiduals, 0, 9, TimeSpan.Zero, requested: 9);

        Assert.Equal("Bootstrap residuals; incomplete; 9 of 9 refits succeeded; at least 50 needed",
            UncertaintyText(ErrorEstimationMethod.BootstrapResiduals, convergence));
        Assert.Equal("Bootstrap residuals; incomplete; 18 of 18 refits succeeded; at least 50 needed per experiment",
            UncertaintyText(ErrorEstimationMethod.BootstrapResiduals, Aggregate(9, 9), true, true));
    }

    [Fact]
    public void ReportKeepsExistingTextForFailedAndCompleteRunsAndOlderSavedOutcomes()
    {
        var failed = CreateConvergence();
        failed.ApplyErrorEstimationResult(ErrorEstimationMethod.BootstrapResiduals, 3, 60, TimeSpan.Zero, requested: 63);
        Assert.Equal("Bootstrap residuals; partial failure; 60 of 63 refits succeeded (3 failed)",
            UncertaintyText(ErrorEstimationMethod.BootstrapResiduals, failed));

        var complete = CreateConvergence();
        complete.ApplyErrorEstimationResult(ErrorEstimationMethod.BootstrapResiduals, 0, 50, TimeSpan.Zero, requested: 50);
        Assert.Equal("Bootstrap residuals; completed; 50 of 50 refits succeeded",
            UncertaintyText(ErrorEstimationMethod.BootstrapResiduals, complete));

        // A result saved before the rule keeps the outcome it was recorded with.
        var older = CreateConvergence();
        older.ApplyErrorEstimationResult(ErrorEstimationMethod.BootstrapResiduals, 0, 9, TimeSpan.Zero, requested: 9);
        older.SetErrorEstimationOutcome(ErrorEstimationOutcome.Completed);
        Assert.Equal("Bootstrap residuals; completed; 9 of 9 refits succeeded",
            UncertaintyText(ErrorEstimationMethod.BootstrapResiduals, older));
    }

    static SolverConvergence Aggregate(params int[] kept)
    {
        var members = new System.Collections.Generic.List<SolverConvergence>();
        foreach (var count in kept)
        {
            var member = CreateConvergence();
            member.ApplyErrorEstimationResult(ErrorEstimationMethod.BootstrapResiduals, 0, count, TimeSpan.Zero, requested: count);
            members.Add(member);
        }
        return SolverConvergence.FromMultiExperimentAnalysis(members);
    }

    static string UncertaintyText(ErrorEstimationMethod method, SolverConvergence convergence,
        bool? belowMinimum = null, bool perExperiment = false)
        => (string)typeof(AnalysisReportBuilder)
            .GetMethod("UncertaintyText", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { method, convergence, belowMinimum, perExperiment });

    static SolverConvergence CreateConvergence()
        => (SolverConvergence)typeof(SolverConvergence)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
            .Invoke(null);
}
