using System.Linq;
using System;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class IndependentAssessmentTests
{
    [Theory]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentSummaryOutcome.NoBindingDetected, false)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentOutcome.Inconclusive, BindingAssessmentSummaryOutcome.Mixed, false)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentOutcome.BindingDetected, BindingAssessmentSummaryOutcome.Mixed, false)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentOutcome.NotAssessed, BindingAssessmentSummaryOutcome.Mixed, false)]
    [InlineData(BindingAssessmentOutcome.Inconclusive, BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentSummaryOutcome.Mixed, false)]
    [InlineData(BindingAssessmentOutcome.Inconclusive, BindingAssessmentOutcome.Inconclusive, BindingAssessmentSummaryOutcome.Inconclusive, true)]
    [InlineData(BindingAssessmentOutcome.Inconclusive, BindingAssessmentOutcome.NotAssessed, BindingAssessmentSummaryOutcome.Mixed, true)]
    [InlineData(BindingAssessmentOutcome.Inconclusive, BindingAssessmentOutcome.BindingDetected, BindingAssessmentSummaryOutcome.Mixed, true)]
    [InlineData(BindingAssessmentOutcome.BindingDetected, BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentSummaryOutcome.Mixed, false)]
    [InlineData(BindingAssessmentOutcome.BindingDetected, BindingAssessmentOutcome.Inconclusive, BindingAssessmentSummaryOutcome.Mixed, true)]
    [InlineData(BindingAssessmentOutcome.BindingDetected, BindingAssessmentOutcome.BindingDetected, BindingAssessmentSummaryOutcome.BindingDetected, true)]
    [InlineData(BindingAssessmentOutcome.BindingDetected, BindingAssessmentOutcome.NotAssessed, BindingAssessmentSummaryOutcome.Mixed, true)]
    [InlineData(BindingAssessmentOutcome.NotAssessed, BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentSummaryOutcome.Mixed, false)]
    [InlineData(BindingAssessmentOutcome.NotAssessed, BindingAssessmentOutcome.Inconclusive, BindingAssessmentSummaryOutcome.Mixed, true)]
    [InlineData(BindingAssessmentOutcome.NotAssessed, BindingAssessmentOutcome.BindingDetected, BindingAssessmentSummaryOutcome.Mixed, true)]
    [InlineData(BindingAssessmentOutcome.NotAssessed, BindingAssessmentOutcome.NotAssessed, BindingAssessmentSummaryOutcome.NotAssessed, true)]
    public void CollectionOutcomeAndCombinedGateUseMemberAssessments(
        BindingAssessmentOutcome first, BindingAssessmentOutcome second,
        BindingAssessmentSummaryOutcome expected, bool combinedAllowed)
    {
        var result = CreateIndependentResult(out var members);
        SetAutomatic(result, members[0], first);
        SetAutomatic(result, members[1], second);

        Assert.Equal(BindingAssessmentScope.Independent, result.AssessmentScope);
        Assert.Equal(expected, result.CollectionAssessmentOutcome);
        Assert.Equal(combinedAllowed,
            ResultOutputPolicy.IsCombinedBindingOutputAllowed(result, ResultOutputPurpose.Standard));
        Assert.True(ResultOutputPolicy.IsCombinedBindingOutputAllowed(result, ResultOutputPurpose.Diagnostic));
    }

    [Fact]
    public void MemberOverridesAreOwnedByResultAndNotAssessedRemainsUnrestricted()
    {
        var result = CreateIndependentResult(out var members);
        var siblingResult = new AnalysisResult(result.Solution);

        Assert.Null(result.BindingAssessment);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, result.GetMemberBindingAssessment(members[0]).EffectiveOutcome);
        Assert.True(ResultOutputPolicy.IsMemberBindingOutputAllowed(result, members[0]));

        result.SetMemberBindingAssessmentOverride(members[0].Guid, BindingAssessmentOutcome.NoBindingDetected);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, result.GetMemberBindingAssessment(members[0]).EffectiveOutcome);
        Assert.Equal(BindingAssessmentSummaryOutcome.Mixed, result.CollectionAssessmentOutcome);
        Assert.False(ResultOutputPolicy.IsMemberBindingOutputAllowed(result, members[0]));
        Assert.True(ResultOutputPolicy.IsMemberBindingOutputAllowed(result, members[1]));
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, siblingResult.GetMemberBindingAssessment(members[0]).EffectiveOutcome);

        result.UseAutomaticBindingAssessments();
        Assert.Equal(BindingAssessmentSummaryOutcome.NotAssessed, result.CollectionAssessmentOutcome);
        Assert.False(result.GetMemberBindingAssessment(members[0]).IsManual);
        Assert.True(ResultOutputPolicy.IsCombinedBindingOutputAllowed(result));
    }

    [Fact]
    public void SameForAllLockedOffsetStillHasIndependentAssessmentScope()
    {
        var result = CreateIndependentResult(out _);
        result.Model.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
        result.Model.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 0, true);
        Assert.True(result.Model.ShouldFitIndividually);
        Assert.Equal(BindingAssessmentScope.Independent, result.AssessmentScope);
    }

    [Fact]
    public void MemberCriteriaUseParentWeightingAndMatchHandCalculatedAicc()
    {
        var first = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var second = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var models = new[] { first, second };
        var globalModel = new GlobalModel(models.ToList()) { Parameters = new GlobalModelParameters() };
        foreach (var model in models)
        {
            globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        }
        var solver = new GlobalSolver { Model = globalModel, UseErrorWeightedFitting = true };
        var global = new GlobalSolution(solver, Converged());
        var members = global.Solutions;
        foreach (var member in members) member.UseWeightedFitting = false;

        NullModelComparisonCalculator.Calculate(global, weighted: true,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 1_000, toleranceModifier: 1);

        Assert.True(global.Model.ShouldFitIndividually);
        foreach (var member in members)
        {
            var comparison = Assert.IsType<NullModelComparison>(member.NullComparison);
            Assert.True(comparison.BindingFitSucceeded, comparison.BindingFitReason);
            Assert.True(comparison.NullFitSucceeded, comparison.NullFitReason);
            Assert.Equal(GaussianLikelihoodMode.EstimatedWeightedVariance,
                comparison.BindingInformationCriteria.LikelihoodMode);
            Assert.Equal(GaussianLikelihoodMode.EstimatedWeightedVariance,
                comparison.NullInformationCriteria.LikelihoodMode);
            AssertHandCalculatedAicc(comparison.BindingInformationCriteria, member.Model,
                member.Model.NumberOfParameters);
            var nullModel = comparison.NullSolutions.Single().Model;
            AssertHandCalculatedAicc(comparison.NullInformationCriteria, nullModel, fittedParameterCount: 1);
            Assert.Equal(comparison.NullInformationCriteria.Aicc.Value
                - comparison.BindingInformationCriteria.Aicc.Value, comparison.DeltaAicc.Value, 9);
        }
    }

    [Fact]
    public void SmallLocalSamplesKeepTheirFitsAndCanFormAnAvailablePooledAicc()
    {
        var first = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var second = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        foreach (var model in new[] { first, second })
            foreach (var injection in model.Data.Injections.Skip(3)) injection.Include = false;
        var global = CreateIndependentGlobal(first, second);

        NullModelComparisonCalculator.Calculate(global, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 1_000, toleranceModifier: 1);

        Assert.True(global.NullComparison.NullFitSucceeded, global.NullComparison.NullFitReason);
        Assert.True(global.NullComparison.NullInformationCriteria.IsAiccAvailable,
            global.NullComparison.NullInformationCriteria.AiccUnavailableReason);
        foreach (var member in global.Solutions)
        {
            var comparison = Assert.IsType<NullModelComparison>(member.NullComparison);
            Assert.True(comparison.NullFitSucceeded, comparison.NullFitReason);
            Assert.False(comparison.NullInformationCriteria.IsAiccAvailable);
            Assert.Contains("n ≤ K + 1", comparison.NullInformationCriteria.AiccUnavailableReason);
        }
        Assert.Equal(6, global.NullComparison.NullInformationCriteria.ObservationCount);
        Assert.Equal(2, global.NullComparison.NullInformationCriteria.FittedParameterCount);
    }

    [Fact]
    public void FailedLocalNullFitDoesNotDiscardHealthySiblingOrCreatePooledCriteria()
    {
        var healthy = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var unavailable = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        foreach (var injection in unavailable.Data.Injections) injection.Include = false;
        var global = CreateIndependentGlobal(healthy, unavailable);

        NullModelComparisonCalculator.Calculate(global, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 1_000, toleranceModifier: 1);

        var comparisons = global.Solutions.Select(member => Assert.IsType<NullModelComparison>(member.NullComparison)).ToArray();
        Assert.True(comparisons[0].NullFitSucceeded, comparisons[0].NullFitReason);
        Assert.True(comparisons[0].NullInformationCriteria.IsAiccAvailable);
        Assert.False(comparisons[1].NullFitSucceeded);
        Assert.Contains("No included observations", comparisons[1].NullFitReason);
        Assert.False(global.NullComparison.NullFitSucceeded);
        Assert.Null(global.NullComparison.NullInformationCriteria);
    }

    [Fact]
    public void InconclusiveMemberDrawsBindingFigure()
    {
        var result = CreateIndependentResult(out var members);
        SetAutomatic(result, members[0], BindingAssessmentOutcome.Inconclusive);
        var figure = PublicationFigureBuilder.Build(new PublicationFigureSource(
            members[0].Data, members[0], result, ResultOutputPurpose.Standard),
            new PublicationFigureOptions { ShowFitPanel = true });
        var labels = figure.FitPanel.AnnotationBoxes.SelectMany(box => box.Lines).ToList();

        Assert.DoesNotContain("Offset fit unavailable", labels);
        Assert.NotEmpty(figure.FitPanel.Series);
        var ordinary = PublicationFigureBuilder.Build(new PublicationFigureSource(members[0].Data, members[0]),
            new PublicationFigureOptions { ShowFitPanel = true });
        Assert.Equal(ordinary.MetadataKeywords, figure.MetadataKeywords);
        Assert.Equal(ordinary.FitPanel.Series.SelectMany(series => series.Points).Select(point => point.Y),
            figure.FitPanel.Series.SelectMany(series => series.Points).Select(point => point.Y));
    }

    [Fact]
    public void AnyInconclusiveMemberWarnsEvenWhenAnotherMemberHasNoBinding()
    {
        var result = CreateIndependentResult(out var members);
        members[0].Data.Name = "Uncertain member";
        SetAutomatic(result, members[0], BindingAssessmentOutcome.Inconclusive);
        SetAutomatic(result, members[1], BindingAssessmentOutcome.NoBindingDetected);
        Assert.Equal(AnalysisResultHealth.Warning, result.Health);
        Assert.Contains("Uncertain member: Binding assessment is inconclusive.", result.HealthReasons);
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);
        result.SetMemberBindingAssessmentOverride(members[0].Guid, BindingAssessmentOutcome.BindingDetected);
        Assert.Equal(AnalysisResultHealth.Valid, result.Health);
        Assert.Empty(result.HealthReasons);
    }

    [Theory]
    [InlineData("best-fit")]
    [InlineData("bootstrap")]
    [InlineData("limit")]
    public void FitWarningsFromNoBindingMemberDoNotDegradeHealth(string warning)
    {
        var result = CreateIndependentResult(out var members);
        SetAutomatic(result, members[0], BindingAssessmentOutcome.BindingDetected);
        SetAutomatic(result, members[1], BindingAssessmentOutcome.NoBindingDetected);
        AddFitWarning(members[1], warning);
        var memberReason = Assert.Single(ParameterBoundaryWarningFormatter.MessagesFor(
            members[1], result.Solution.ErrorEstimationMethod));

        Assert.Equal(AnalysisResultHealth.Valid, result.Health);
        Assert.Empty(result.HealthReasons);
        Assert.Equal(new[] { members[0] }, result.FitWarningMembers);

        result.SetMemberBindingAssessmentOverride(members[1].Guid, BindingAssessmentOutcome.BindingDetected);
        Assert.Equal(AnalysisResultHealth.Warning, result.Health);
        Assert.Contains(memberReason, result.HealthReasons);
    }

    [Fact]
    public void BindingMemberFitWarningStillDegradesHealthBesideNoBindingMember()
    {
        var result = CreateIndependentResult(out var members);
        SetAutomatic(result, members[0], BindingAssessmentOutcome.BindingDetected);
        SetAutomatic(result, members[1], BindingAssessmentOutcome.NoBindingDetected);
        members[0].RestoreParameterBoundaryHit(true);
        members[1].RestoreParameterBoundaryHit(true);

        Assert.Equal(AnalysisResultHealth.Warning, result.Health);
        Assert.Equal(new[] { ParameterBoundaryWarningFormatter.BestFitMessage }, result.HealthReasons);
    }

    [Fact]
    public void NoBindingMemberWithChangedInputsStillMakesResultPartiallyInvalid()
    {
        var result = CreateIndependentResult(out var members);
        SetAutomatic(result, members[0], BindingAssessmentOutcome.BindingDetected);
        SetAutomatic(result, members[1], BindingAssessmentOutcome.NoBindingDetected);
        members[1].RestoreParameterBoundaryHit(true);
        members[1].Data.CellConcentration = new FloatWithError(members[1].Data.CellConcentration.Value * 1.1);

        Assert.Equal(AnalysisResultValidity.PartialInvalid, result.ValidityReport.Status);
        Assert.Equal(AnalysisResultHealth.PartialInvalid, result.Health);
        Assert.DoesNotContain(ParameterBoundaryWarningFormatter.BestFitMessage, result.HealthReasons);
    }

    static void AddFitWarning(SolutionInterface member, string warning)
    {
        switch (warning)
        {
            case "best-fit":
                member.RestoreParameterBoundaryHit(true);
                break;
            case "bootstrap":
                var replicate = InjectionProcessingMethodTests.FittedModel(bootstrap: false).Solution;
                replicate.RestoreParameterBoundaryHit(true);
                member.SetBootstrapSolutions(new System.Collections.Generic.List<SolutionInterface> { replicate });
                break;
            case "limit":
                member.Convergence.ApplyErrorEstimationResult(ErrorEstimationMethod.BootstrapResiduals,
                    failures: 1, succeeded: 3, TimeSpan.FromSeconds(1), limitTerminated: 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(warning));
        }
    }

    static void SetAutomatic(AnalysisResult result, SolutionInterface member, BindingAssessmentOutcome outcome)
    {
        var comparison = Comparison(outcome switch
        {
            BindingAssessmentOutcome.NoBindingDetected => 0,
            BindingAssessmentOutcome.Inconclusive => 7,
            BindingAssessmentOutcome.BindingDetected => 12,
            _ => (double?)null,
        });
        result.RestoreMemberComparisonAndAssessment(member.Guid, comparison,
            BindingAssessmentState.FromComparison(comparison));
    }

    static NullModelComparison Comparison(double? delta)
    {
        FitInformationCriteria Criteria(double aicc) => FitInformationCriteria.Restore(
            20, 1, 2, GaussianLikelihoodMode.EstimatedCommonVariance,
            aicc - 4, aicc - 2, aicc, true, true, string.Empty, string.Empty,
            1, 1, 1, 0);
        return new NullModelComparison
        {
            BindingFitSucceeded = delta.HasValue,
            NullFitSucceeded = delta.HasValue,
            BindingInformationCriteria = delta.HasValue ? Criteria(100) : null,
            NullInformationCriteria = delta.HasValue ? Criteria(100 + delta.Value) : null,
            DeltaAicc = delta,
            ComparisonUnavailableReason = delta.HasValue ? string.Empty : "No saved comparison.",
        };
    }

    static void AssertHandCalculatedAicc(FitInformationCriteria criteria, Model model, int fittedParameterCount)
    {
        var observations = model.Data.Injections.Where(injection => injection.Include).ToList();
        var sigmas = observations.Select(injection => injection.PeakAreaError).ToArray();
        var fallbackSigma = sigmas.Where(sigma => double.IsFinite(sigma) && sigma > 0).Average();
        var residuals = observations.Select(injection => model.Residual(injection)).ToArray();
        var rawRss = residuals.Sum(residual => residual * residual);
        var q = 0.0;
        var logSigmaSquaredSum = 0.0;
        for (var index = 0; index < residuals.Length; index++)
        {
            var sigma = double.IsFinite(sigmas[index]) && sigmas[index] > 0 ? sigmas[index] : fallbackSigma;
            q += Math.Pow(residuals[index] / sigma, 2);
            logSigmaSquaredSum += 2 * Math.Log(sigma);
        }
        var n = observations.Count;
        var k = fittedParameterCount + 1;
        var minusTwoLogLikelihood = n * (Math.Log(2 * Math.PI) + Math.Log(q / n) + 1)
            + logSigmaSquaredSum;
        var aic = minusTwoLogLikelihood + 2 * k;
        var aicc = aic + 2.0 * k * (k + 1) / (n - k - 1);
        Assert.Equal(n, criteria.ObservationCount);
        Assert.Equal(fittedParameterCount, criteria.FittedParameterCount);
        Assert.Equal(rawRss, criteria.RawResidualSumOfSquares, 8);
        Assert.Equal(q, criteria.StandardizedResidualSumOfSquares, 8);
        Assert.Equal(logSigmaSquaredSum, criteria.LogSigmaSquaredSum, 8);
        Assert.Equal(k, criteria.LikelihoodParameterCount);
        Assert.Equal(minusTwoLogLikelihood, criteria.MinusTwoLogLikelihood.Value, 8);
        Assert.Equal(aic, criteria.Aic.Value, 8);
        Assert.Equal(aicc, criteria.Aicc.Value, 8);
    }

    static SolverConvergence Converged() => SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
    {
        Algorithm = SolverAlgorithm.LevenbergMarquardt,
        Termination = SolverTermination.Converged,
    });

    static GlobalSolution CreateIndependentGlobal(params Model[] models)
    {
        var globalModel = new GlobalModel(models.ToList()) { Parameters = new GlobalModelParameters() };
        foreach (var model in models) globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        Assert.True(globalModel.ShouldFitIndividually);
        var solver = new GlobalSolver { Model = globalModel, UseErrorWeightedFitting = false };
        return new GlobalSolution(solver, models.Select(model => model.Solution).ToList(), Converged(),
            reconstructBootstrap: false);
    }

    internal static AnalysisResult CreateIndependentResult(out SolutionInterface[] solutions)
    {
        var first = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var second = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var models = new[] { first, second };
        var globalModel = new GlobalModel(models.ToList()) { Parameters = new GlobalModelParameters() };
        foreach (var model in models) globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        solutions = models.Select(model => model.Solution).ToArray();
        var convergence = SolverConvergence.FromMultiExperimentAnalysis(solutions.Select(solution => solution.Convergence).ToList());
        var global = new GlobalSolution(new GlobalSolver { Model = globalModel }, solutions.ToList(), convergence,
            reconstructBootstrap: false);
        globalModel.Solution = global;
        return new AnalysisResult(global);
    }
}
