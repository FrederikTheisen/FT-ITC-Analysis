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
    public static IEnumerable<object[]> PresentationOutcomes() =>
        from method in EstimationMethods
        from scope in Enum.GetValues<BindingAssessmentScope>()
        from outcome in new[] { ErrorEstimationOutcome.Completed, ErrorEstimationOutcome.PartialFailure,
            ErrorEstimationOutcome.Cancelled, ErrorEstimationOutcome.CompleteFailure }
        select new object[] { method, scope, outcome };

    [Theory]
    [MemberData(nameof(PresentationOutcomes))]
    public async Task ITC094SectionsRetainRecordedProblemsAcrossMethodsAndScopes(
        ErrorEstimationMethod method, BindingAssessmentScope scope, ErrorEstimationOutcome outcome)
    {
        var models = scope == BindingAssessmentScope.Single ? new[] { PolicyModel(false) }
            : new[] { PolicyModel(false, "first"), PolicyModel(false, "second") };
        var solver = PolicySolver(scope, method, models);
        await Run(solver);
        var solution = GlobalSolutionOf(solver);
        solution.Convergence.SetErrorEstimationOutcome(outcome);
        var expected = outcome switch { ErrorEstimationOutcome.Completed => "Complete",
            ErrorEstimationOutcome.Cancelled => "Cancelled", _ => "Incomplete" };
        var section = UncertaintyEstimationPresentation.Section(solution);
        Assert.Equal(expected, section.Rows.First().Value);
        Assert.Contains(section.Rows, row => row.Label == "Calculation time");
        Assert.Equal(method == ErrorEstimationMethod.BootstrapResiduals, section.Rows.Any(row => row.Label == "Bootstraps"));
        Assert.Equal(method == ErrorEstimationMethod.ProfileLikelihood, section.Rows.Any(row => row.Label == "95% CI endpoints"));
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood)]
    public async Task ITC094MixedCollectionsExplainMemberAndCombinedEvidence(ErrorEstimationMethod method)
    {
        var solver = PolicySolver(BindingAssessmentScope.Independent, method,
            PolicyModel(false, "eligible"), PolicyModel(true, "skipped"));
        await Run(solver);
        var solution = GlobalSolutionOf(solver);
        var status = UncertaintyEstimationPresentation.Section(solution).Rows.First();
        Assert.Equal("Mixed", status.Value);
        Assert.Contains("combined", status.Tooltip);
        Assert.Empty(solution.BootstrapSolutions);
        if (method == ErrorEstimationMethod.BootstrapResiduals)
        {
            var row = UncertaintyEstimationPresentation.Section(solution).Rows.Single(row => row.Label == "Bootstraps");
            Assert.Equal(Iterations.ToString(), row.Value);
            Assert.Contains("combined replicates: 0", row.Tooltip);
            Assert.Contains("own refits", row.Tooltip);
        }
        solution.Solutions[0].Convergence.SetErrorEstimationOutcome(ErrorEstimationOutcome.Cancelled);
        Assert.Equal("Cancelled", UncertaintyEstimationPresentation.Section(solution).Rows.First().Value);
    }

    [Theory]
    [InlineData(ErrorEstimationOutcome.Completed)]
    [InlineData(ErrorEstimationOutcome.Cancelled)]
    [InlineData(ErrorEstimationOutcome.CompleteFailure)]
    public async Task ITC094LegacyLeaveOneOutShowsExactlyTwoSavedOptions(ErrorEstimationOutcome outcome)
    {
        var solver = PolicySolver(BindingAssessmentScope.Single, ErrorEstimationMethod.LeaveOneOut, PolicyModel(false));
        await Run(solver);
        var solution = GlobalSolutionOf(solver);
        solution.Convergence.SetErrorEstimationOutcome(outcome);
        Assert.DoesNotContain(UncertaintyEstimationPresentation.Section(solution).Rows, row => row.Label == "Concentration uncertainty");
        var options = solution.ModelCloneOptions;
        options.IncludeConcentrationErrorsInBootstrap = true;
        options.EnableAutoConcentrationVariance = true;
        options.AutoConcentrationVariance = .025;
        var rows = UncertaintyEstimationPresentation.Section(solution).Rows;
        Assert.Equal(4, rows.Count);
        Assert.Equal("Included · Legacy automatic " + 2.5.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + "%", rows.Single(row => row.Label == "Concentration uncertainty").Value);
        Assert.Equal("Disabled", rows.Single(row => row.Label == "Parameter unlocking").Value);
        Assert.All(rows.Skip(2), row => Assert.Contains("historical combined leave-one-out", row.Tooltip));
        options.IncludeConcentrationErrorsInBootstrap = false;
        options.UnlockBootstrapParameters = true;
        Assert.Equal("Not included", UncertaintyEstimationPresentation.Section(solution).Rows.Single(row => row.Label == "Concentration uncertainty").Value);
    }

    [Fact]
    public async Task ITC094RequestedCountIsDistinctFromRetainedCountAndAcceptsEveryUpdatePreset()
    {
        using var session = new ResultSession();
        var solver = PolicySolver(BindingAssessmentScope.Single, ErrorEstimationMethod.BootstrapResiduals, PolicyModel(false));
        await Run(solver);
        var solution = GlobalSolutionOf(solver);
        solution.Convergence.CaptureBootstrapRequest(ErrorEstimationMethod.BootstrapResiduals, 100);
        solution.BootstrapSolutions.Clear();
        for (var i = 0; i < 95; i++) solution.BootstrapSolutions.Add(solution);
        var result = new AnalysisResult(solution);
        session.Load(result);
        var row = UncertaintyEstimationPresentation.Section(solution).Rows.Single(row => row.Label == "Bootstraps");
        Assert.Equal("100", row.Value);
        Assert.Contains("95", row.Tooltip);
        Assert.Equal(100, AnalysisResultUpdater.GetEffectiveBootstrapIterations(result));
        Assert.Equal(100, AnalysisResultUpdater.PrepareSolver(result).BootstrapIterations);
        Assert.Equal(new[] { 100, 200, 500, 1000, 2000, 5000, 10000 }, AnalysisResultUpdater.BootstrapUpdateIterationPresets);
        foreach (var count in AnalysisResultUpdater.BootstrapUpdateIterationPresets)
            Assert.Equal(count, AnalysisResultUpdater.PrepareSolver(result, new AnalysisResultUpdateOptions(count)).BootstrapIterations);
        solution.Convergence.CaptureBootstrapRequest(ErrorEstimationMethod.BootstrapResiduals, 2000);
        Assert.Equal(100, AnalysisResultUpdater.PrepareSolver(result, new AnalysisResultUpdateOptions(100)).BootstrapIterations);
        Assert.Throws<ArgumentOutOfRangeException>(() => AnalysisResultUpdater.PrepareSolver(result, new AnalysisResultUpdateOptions(50)));
        solution.Convergence.CaptureBootstrapRequest(ErrorEstimationMethod.BootstrapResiduals, 37);
        Assert.Equal(37, AnalysisResultUpdater.PrepareSolver(result).BootstrapIterations);
    }

    [Fact]
    public void ITC094CopyAndSnapshotPreserveOnlyDurableBootstrapMetadata()
    {
        var convergence = Convergence();
        convergence.CaptureBootstrapRequest(ErrorEstimationMethod.BootstrapResiduals, 100);
        convergence.ResultUpdateRetentionReason = "Profile result retained: test reason";
        Assert.Equal(100, convergence.Copy().RequestedBootstrapIterations);
        Assert.Null(convergence.Copy().ResultUpdateRetentionReason);
        var restored = SolverConvergence.FromSnapshot(convergence.ToSnapshot());
        Assert.Equal(100, restored.RequestedBootstrapIterations);
        Assert.Null(restored.ResultUpdateRetentionReason);
        foreach (var method in new[] { ErrorEstimationMethod.None, ErrorEstimationMethod.LeaveOneOut, ErrorEstimationMethod.ProfileLikelihood })
        {
            convergence.CaptureBootstrapRequest(method, 100);
            Assert.Null(convergence.RequestedBootstrapIterations);
        }
        Assert.Null(SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()).RequestedBootstrapIterations);
    }

    [Theory]
    [MemberData(nameof(LifecycleCases))]
    public async Task ITC094PrimaryRequestsAreCapturedBeforeSkipsAndNeverCopiedToRefits(
        ErrorEstimationMethod method, BindingAssessmentScope scope)
    {
        var models = scope == BindingAssessmentScope.Single ? new[] { PolicyModel(false) }
            : new[] { PolicyModel(false, "first"), PolicyModel(true, "second") };
        var solver = PolicySolver(scope, method, models);
        await Run(solver);
        var solution = GlobalSolutionOf(solver);
        int? request = method == ErrorEstimationMethod.BootstrapResiduals ? Iterations : null;
        Assert.Equal(request, solution.Convergence.RequestedBootstrapIterations);
        Assert.All(solution.Solutions, member =>
        {
            Assert.Equal(request, member.Convergence.RequestedBootstrapIterations);
            Assert.All(member.BootstrapSolutions, refit => Assert.Null(refit.Convergence?.RequestedBootstrapIterations));
        });
        Assert.All(solution.BootstrapSolutions, refit => Assert.Null(refit.Convergence?.RequestedBootstrapIterations));
    }
    [Fact]
    public async Task ITC094RetainedUpdateReasonIsTransientAndAbsentAfterStrictReopening()
    {
        using var session = new ResultSession();
        var model = PolicyModel(false);
        var solver = PolicySolver(BindingAssessmentScope.Single, ErrorEstimationMethod.ProfileLikelihood, model);
        await Run(solver);
        var result = new AnalysisResult(GlobalSolutionOf(solver));
        session.Load(result);
        var original = result.Solution;
        var originalConvergence = original.Convergence;
        var originalSummary = originalConvergence.ErrorEstimationSummary;
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
        EventHandler<FittingProgress> handler = (sender, progress) =>
        {
            if (cancelled || !ReferenceEquals(sender, updaterSolver) || progress.OperationId != operationId
                || progress.Method != ErrorEstimationMethod.ProfileLikelihood
                || progress.Phase != FittingPhase.EstimatingUncertainty || !progress.Completed.HasValue
                || !progress.Total.HasValue || progress.Completed.Value <= 0 || progress.Completed.Value >= progress.Total.Value) return;
            cancelled = true;
            SolverInterface.TerminateAnalysisFlag.Raise();
        };
        SolverConvergence returned;
        SolverInterface.AnalysisStarted += started;
        SolverInterface.ProgressChanged += handler;
        try { returned = await AnalysisResultUpdater.UpdateAsync(result); }
        finally
        {
            SolverInterface.AnalysisStarted -= started;
            SolverInterface.ProgressChanged -= handler;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
        Assert.True(cancelled);
        Assert.Same(original, result.Solution);
        Assert.Same(originalConvergence, result.Solution.Convergence);
        Assert.NotSame(originalConvergence, returned);
        Assert.False(string.IsNullOrWhiteSpace(returned.ResultUpdateRetentionReason));
        Assert.Equal(returned.ResultUpdateRetentionReason, UncertaintyEstimationPresentation.UpdateCompletion(result.Solution, returned));
        Assert.Equal(returned.ResultUpdateRetentionReason, UncertaintyEstimationPresentation.UpdateStatus(result.Solution, returned));
        Assert.Null(originalConvergence.ResultUpdateRetentionReason);
        Assert.Equal(originalSummary, originalConvergence.ErrorEstimationSummary);
        Assert.DoesNotContain(returned.ResultUpdateRetentionReason, returned.ToSnapshot().ErrorEstimationSummary);
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { model.Data }, new[] { result });
        package.Position = 0;
        var reopened = await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict);
        Assert.Empty(reopened.Issues);
        var retained = Assert.Single(reopened.Containers.OfType<AnalysisResult>());
        Assert.Null(retained.Solution.Convergence.ResultUpdateRetentionReason);
        Assert.Equal(originalSummary, retained.Solution.Convergence.ErrorEstimationSummary);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.None)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood)]
    public async Task ITC094OtherMethodsSaveNoBootstrapRequest(ErrorEstimationMethod method)
    {
        var model = PolicyModel(true);
        var solver = PolicySolver(BindingAssessmentScope.Single, method, model);
        await Run(solver);
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { model.Data }, new[] { new AnalysisResult(GlobalSolutionOf(solver)) });
        package.Position = 0;
        using (var archive = new System.IO.Compression.ZipArchive(package, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
            foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".json")))
            {
                using var reader = new StreamReader(entry.Open());
                var json = await reader.ReadToEndAsync();
                Assert.DoesNotContain("requestedBootstrapIterations", json);
                Assert.DoesNotContain("resultUpdateRetentionReason", json);
            }
        package.Position = 0;
        var reopened = await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict);
        var result = Assert.Single(reopened.Containers.OfType<AnalysisResult>());
        Assert.Null(result.Solution.Convergence.RequestedBootstrapIterations);
        Assert.All(result.Solution.Solutions, member => Assert.Null(member.Convergence.RequestedBootstrapIterations));
    }

    [Fact]
    public async Task ITC094HistoricalBootstrapRequestStaysUnknownWithExistingUpdateFallback()
    {
        using var session = new ResultSession();
        var model = PolicyModel(true);
        var solver = PolicySolver(BindingAssessmentScope.Single, ErrorEstimationMethod.BootstrapResiduals, model);
        await Run(solver);
        var source = GlobalSolutionOf(solver);
        source.Convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        { Termination = SolverTermination.Converged, ErrorEstimationOutcome = ErrorEstimationOutcome.SkippedNoBinding });
        model.Solution = SolutionInterface.FromModel(model, source.Convergence);
        model.Solution.ErrorMethod = ErrorEstimationMethod.BootstrapResiduals;
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { model.Data }, new[] { new AnalysisResult(source) });
        package.Position = 0;
        var reopened = await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict);
        Assert.Empty(reopened.Issues);
        var result = Assert.Single(reopened.Containers.OfType<AnalysisResult>());
        Assert.Equal("Unknown", UncertaintyEstimationPresentation.BootstrapIterations(result.Solution));
        session.Load(result);
        FittingOptionsController.BootstrapIterations = 6;
        Assert.Equal(6, AnalysisResultUpdater.PrepareSolver(result).BootstrapIterations);
    }

    [Fact]
    public async Task ITC094ReportsReadPreservedCancellationAndKeepNoBindingExplanations()
    {
        var solver = PolicySolver(BindingAssessmentScope.Independent, ErrorEstimationMethod.BootstrapResiduals,
            PolicyModel(true, "skipped"), PolicyModel(false, "eligible"));
        await Run(solver);
        var solution = GlobalSolutionOf(solver);
        solution.Solutions[1].Convergence.SetErrorEstimationOutcome(ErrorEstimationOutcome.Cancelled);
        solution.Convergence = SolverConvergence.FromMultiExperimentAnalysis(solution.Solutions.Select(member => member.Convergence).ToList());
        Assert.Equal(ErrorEstimationOutcome.Cancelled, solution.Convergence.ErrorEstimationOutcome);
        var result = new AnalysisResult(solution);
        foreach (var purpose in new[] { ResultOutputPurpose.Standard, ResultOutputPurpose.Diagnostic })
        {
            var report = AnalysisReportBuilder.Build(result, new AnalysisReportOptions { OutputPurpose = purpose });
            var rows = report.Sections.SelectMany(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>())
                .SelectMany(block => block.Items).Where(row => row.Label == "Uncertainty").ToList();
            Assert.Contains(rows, row => row.Value.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(rows, row => row.Value.Contains("not estimated (no binding detected)", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, result.GetMemberBindingAssessment(solution.Solutions[0]).EffectiveOutcome);
        }
    }

}
