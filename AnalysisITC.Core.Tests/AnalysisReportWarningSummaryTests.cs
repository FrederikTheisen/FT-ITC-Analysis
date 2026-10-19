using System;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class AnalysisReportBuilderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SingleAndPooledOverviewKeepInconclusiveAfterFitWarnings(int memberCount)
    {
        var result = CreateResult(memberCount);
        if (memberCount > 1)
        {
            result.Model.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.SameForAll);
            result.Model.Parameters.AddorUpdateGlobalParameter(ParameterType.Affinity1, 6);
        }
        result.RestoreBindingAssessment(BindingAssessmentState.Restore(
            BindingAssessmentOutcome.Inconclusive, BindingAssessmentState.CurrentRuleId, null));
        var member = result.Solution.Solutions[0];
        member.RestoreParameterBoundaryHit(true);
        var bootstrap = SolutionInterface.FromModel(member.Model, Convergence());
        bootstrap.RestoreParameterBoundaryHit(true);
        member.SetBootstrapSolutions(new[] { bootstrap }.ToList());
        result.Solution.Model.ModelCloneOptions.ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
        member.Convergence.ApplyErrorEstimationResult(ErrorEstimationMethod.BootstrapResiduals,
            1, 3, TimeSpan.FromSeconds(1), limitTerminated: 1);
        Assert.False(result.IsIndependentAssessmentCollection);
        var appReasons = result.HealthReasons.ToArray();
        Assert.Equal(AnalysisResultHealthReasonFormatter.InconclusiveMessage, appReasons.Last());
        var overview = OverviewWarnings(AnalysisReportBuilder.Build(result));
        var bestFit = overview.IndexOf(ParameterBoundaryWarningFormatter.BestFitMessage, StringComparison.Ordinal);
        var uncertainty = overview.IndexOf(ParameterBoundaryWarningFormatter.BootstrapFitMessage, StringComparison.Ordinal);
        var limit = overview.IndexOf(ParameterBoundaryWarningFormatter.BootstrapLimitMessage, StringComparison.Ordinal);
        var inconclusive = overview.IndexOf(AnalysisResultHealthReasonFormatter.InconclusiveMessage, StringComparison.Ordinal);
        Assert.True(bestFit >= 0 && uncertainty > bestFit && limit > uncertainty && inconclusive > limit, overview);
        Assert.DoesNotContain("Binding assessment is inconclusive. 1 of", overview);
        Assert.Equal(appReasons, result.HealthReasons);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    public void AppendixSummarizesCapturedMemberWarningsInCategoryOrder(ErrorEstimationMethod method)
    {
        var result = CreateResult(24);
        result.Solution.Model.ModelCloneOptions.ErrorEstimationMethod = method;
        foreach (var member in result.Solution.Solutions)
        {
            member.RestoreParameterBoundaryHit(true);
            var bootstrap = SolutionInterface.FromModel(member.Model, Convergence());
            bootstrap.RestoreParameterBoundaryHit(true);
            member.SetBootstrapSolutions(new[] { bootstrap }.ToList());
            member.Convergence.ApplyErrorEstimationResult(method, 1, 3, TimeSpan.FromSeconds(1), limitTerminated: 1);
        }
        var document = AnalysisReportBuilder.Build(result);
        var lines = AppendixWarnings(document).Split(Environment.NewLine);
        Assert.Equal(4, lines.Length);
        Assert.Contains("status:", lines[0]);
        Assert.Equal("Report result: " + ParameterBoundaryWarningFormatter.BestFitMessage + " 24 of 24 experiments (1A–1X).", lines[1]);
        Assert.Equal("Report result: " + ParameterBoundaryWarningFormatter.ErrorEstimationMessage(method) + " 24 of 24 experiments (1A–1X).", lines[2]);
        Assert.Equal("Report result: " + ParameterBoundaryWarningFormatter.ErrorEstimationLimitMessage(method) + " 24 of 24 experiments (1A–1X).", lines[3]);
        var captured = document.Diagnostics.Where(item => item.WarningContext != null).ToList();
        Assert.Equal(72, captured.Count);
        Assert.All(captured, item =>
        {
            Assert.StartsWith("result-1-", item.Code);
            Assert.Equal(24, item.WarningContext.MemberCount);
            Assert.Equal(method, item.WarningContext.UncertaintyMethod);
            Assert.StartsWith("Report result: ", item.Message);
        });
        Assert.Equal(document.Diagnostics.First().Message, document.Warnings.First());
        Assert.Equal("1X", captured.Last().WarningContext.MemberLabel);
    }

    [Fact]
    public void SummaryRangesUseMemberIndicesAcrossZAndRetainSameNamedMembers()
    {
        var result = CreateResult(29);
        foreach (var index in new[] { 0, 3, 24, 25, 26, 27 })
        {
            result.Solution.Solutions[index].Data.Name = "Same name";
            result.Solution.Solutions[index].RestoreParameterBoundaryHit(true);
        }
        result.Solution.Solutions[2].Convergence.ApplyErrorEstimationResult(ErrorEstimationMethod.BootstrapResiduals, 1, 3, TimeSpan.FromSeconds(1), limitTerminated: 1);
        var document = AnalysisReportBuilder.Build(result);
        Assert.Contains("6 of 29 experiments (1A, 1D, 1Y–1AB).", AppendixWarnings(document));
        Assert.Contains("1 of 29 experiments (1C).", AppendixWarnings(document));
        Assert.Contains("6 of 29 experiments (1A, 1D, 1Y–1AB).", OverviewWarnings(document));
    }

    [Fact]
    public void SingleMemberWarningsUsePlainMethodSpecificMessagesAndResultsStaySeparate()
    {
        var first = CreateResult(1); first.Name = "First";
        var second = CreateResult(1); second.Name = "Second";
        foreach (var result in new[] { first, second }) result.Solution.Solutions[0].RestoreParameterBoundaryHit(true);
        var document = AnalysisReportBuilder.Build(new[] { first, second });
        Assert.Contains("First: " + ParameterBoundaryWarningFormatter.BestFitMessage, AppendixWarnings(document));
        Assert.Contains("Second: " + ParameterBoundaryWarningFormatter.BestFitMessage, AppendixWarnings(document));
        Assert.DoesNotContain("1 of 1", AppendixWarnings(document));
        Assert.Equal(new[] { 0, 1 }, document.Diagnostics.Where(item => item.WarningContext != null)
            .Select(item => item.WarningContext.ResultIndex));
    }

    [Theory]
    [InlineData(ResultOutputPurpose.Standard, 1)]
    [InlineData(ResultOutputPurpose.Diagnostic, 2)]
    public void SummaryEligibilityIsLocationSpecificWithAllMemberDenominators(ResultOutputPurpose purpose, int appendixCount)
    {
        var result = CreateResult(2);
        foreach (var member in result.Solution.Solutions) member.RestoreParameterBoundaryHit(true);
        result.SetMemberBindingAssessmentOverride(result.Solution.Solutions[1].Guid, BindingAssessmentOutcome.NoBindingDetected);
        var appReasons = result.HealthReasons.ToArray();
        var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions { OutputPurpose = purpose });
        Assert.Contains(appendixCount + " of 2 experiments", AppendixWarnings(document));
        Assert.Contains("1 of 2 experiments (1A).", OverviewWarnings(document));
        Assert.DoesNotContain("(1B)", OverviewWarnings(document));
        Assert.Equal(appReasons, result.HealthReasons);
        Assert.Contains(ParameterBoundaryWarningFormatter.BestFitMessage, appReasons);
    }

    [Fact]
    public void ReportInconclusiveReferencesSurviveAppSentenceDeduplication()
    {
        var result = CreateResult(2);
        foreach (var member in result.Solution.Solutions)
        {
            member.Data.Name = "Duplicate";
            RestoreAutomaticAssessment(result, member, 5);
        }
        var appReasons = result.HealthReasons.ToArray();
        Assert.Single(appReasons, message => message.Contains("Binding assessment is inconclusive"));
        Assert.Equal(2, AnalysisResultHealthReasonFormatter.FormatStructured(result)
            .Count(reason => reason.Category == AnalysisWarningCategory.Inconclusive));
        var document = AnalysisReportBuilder.Build(result);
        Assert.Contains("Binding assessment is inconclusive. 2 of 2 experiments (1A, 1B).", OverviewWarnings(document));
        Assert.Equal(appReasons, result.HealthReasons);
    }

    [Fact]
    public void UngroupableDiagnosticsPassThroughWithoutMessageRecognition()
    {
        var document = new AnalysisReportDocument();
        document.AddDiagnostic(AnalysisReportDiagnosticSeverity.Warning, "custom", ParameterBoundaryWarningFormatter.BestFitMessage);
        document.AddDiagnostic(AnalysisReportDiagnosticSeverity.Warning, "mixed-bookkeeping", "Original bookkeeping text");
        Assert.Equal(document.Warnings, AnalysisReportWarningSummary.Appendix(document.Diagnostics));
    }

    [Fact]
    public void SeventyTraceabilityIdentifiersAreIndependentPageableRows()
    {
        var results = Enumerable.Range(1, 70).Select(_ => CreateResult(1)).ToArray();
        var document = AnalysisReportBuilder.Build(results, new AnalysisReportOptions { ExtraTraceability = true });
        var block = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Appendix)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(item => item.Title == "Result identifiers");
        Assert.Equal(results.Select(result => result.UniqueID), block.Items.Select(item => item.Value));
        Assert.Equal(Enumerable.Range(1, 70).Select(index => "Result " + index), block.Items.Select(item => item.Label));
        var plan = AnalysisReportLayoutEngine.Paginate(document, new FakeTextMeasurer());
        var fragments = plan.Pages.SelectMany(page => page.Fragments).Where(fragment => fragment.Block == block).ToList();
        Assert.True(fragments.Count > 1);
        Assert.Equal(70, fragments.Sum(fragment => fragment.ItemCount));
        Assert.All(fragments.Skip(1), fragment => Assert.Equal("Result identifiers – continued", fragment.Title));
        Assert.DoesNotContain(AnalysisReportBuilder.Build(results).Sections.SelectMany(section => section.Blocks),
            item => item.Title == "Result identifiers");
    }

    static string AppendixWarnings(AnalysisReportDocument document) => document.Sections
        .Single(section => section.Kind == AnalysisReportSectionKind.Appendix).Blocks.OfType<AnalysisReportNoticeBlock>()
        .Single(block => block.Title == "Report warnings").Message;

    static string OverviewWarnings(AnalysisReportDocument document) => string.Join("\n", document.Sections
        .Where(section => section.Kind == AnalysisReportSectionKind.ResultOverview).SelectMany(section => section.Blocks)
        .OfType<AnalysisReportNoticeBlock>().Select(block => block.Message));
}
