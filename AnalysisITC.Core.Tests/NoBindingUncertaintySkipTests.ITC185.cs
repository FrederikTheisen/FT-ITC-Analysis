using System;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class NoBindingUncertaintySkipTests
{
    // ITC-185: real bootstrap runs below the 50-refit minimum that lose no refits.
    [Fact]
    public async Task ShortSingleBootstrapIsIncompleteWithKeptAndNeededCounts()
    {
        var solver = PolicySolver(BindingAssessmentScope.Single, ErrorEstimationMethod.BootstrapResiduals, PolicyModel(false));
        solver.BootstrapIterations = 9;

        var convergence = await Run(solver);

        Assert.Equal(0, convergence.ErrorEstimationFailedRefits);
        Assert.Equal(9, convergence.ErrorEstimationSucceededRefits);
        Assert.Equal(ErrorEstimationOutcome.PartialFailure, convergence.ErrorEstimationOutcome);
        Assert.Equal("Bootstrap incomplete: 9 refits kept; at least 50 needed",
            UncertaintyEstimationPresentation.CompletionMessages(solver, convergence, TimeSpan.FromSeconds(12)).Last().Text);
        var status = UncertaintyEstimationPresentation.Section(GlobalSolutionOf(solver)).Rows.First(row => row.Label == "Status");
        Assert.Equal("Incomplete", status.Value);
        Assert.Equal("Too few kept refits for a stable 95% interval", status.Tooltip);
    }

    [Fact]
    public async Task ShortIndependentBootstrapAppliesTheMinimumToEachMember()
    {
        var solver = PolicySolver(BindingAssessmentScope.Independent, ErrorEstimationMethod.BootstrapResiduals,
            PolicyModel(false, "short A"), PolicyModel(false, "short B"));
        solver.BootstrapIterations = 30;

        var convergence = await Run(solver);
        var solution = GlobalSolutionOf(solver);

        Assert.All(solution.Solutions, member =>
        {
            Assert.Equal(0, member.Convergence.ErrorEstimationFailedRefits);
            Assert.Equal(30, member.Convergence.ErrorEstimationSucceededRefits);
            Assert.Equal(ErrorEstimationOutcome.PartialFailure, member.Convergence.ErrorEstimationOutcome);
        });
        Assert.Equal("Bootstrap incomplete: 2 experiments kept fewer than 50 refits",
            UncertaintyEstimationPresentation.CompletionMessages(solver, convergence, TimeSpan.FromSeconds(12)).Last().Text);
        Assert.Equal("Too few kept refits for a stable 95% interval",
            UncertaintyEstimationPresentation.Section(solution).Rows.First(row => row.Label == "Status").Tooltip);
    }
}
