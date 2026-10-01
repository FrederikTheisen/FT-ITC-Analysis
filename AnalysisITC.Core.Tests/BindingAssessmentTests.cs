using System;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class BindingAssessmentTests
{
    [Theory]
    [InlineData(-2, BindingAssessmentOutcome.NoBindingDetected)]
    [InlineData(6, BindingAssessmentOutcome.NoBindingDetected)]
    [InlineData(6.0001, BindingAssessmentOutcome.Inconclusive)]
    [InlineData(9.9999, BindingAssessmentOutcome.Inconclusive)]
    [InlineData(10, BindingAssessmentOutcome.BindingDetected)]
    public void AppliesChosenSignedDeltaThresholds(double delta, BindingAssessmentOutcome expected)
    {
        var state = BindingAssessmentState.FromComparison(AvailableComparison(delta));
        Assert.Equal(expected, state.EffectiveOutcome);
        Assert.Equal("aicc-6-10-v1", state.AutomaticRuleId);
    }

    [Fact]
    public void OnlyFiniteSuccessfulComparisonIsAutomaticallyAssessed()
    {
        Assert.Equal(BindingAssessmentOutcome.NotAssessed,
            BindingAssessmentState.FromComparison(null).AutomaticOutcome);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed,
            BindingAssessmentState.FromComparison(AvailableComparison(double.NaN)).AutomaticOutcome);
        var failed = AvailableComparison(20);
        failed.NullFitSucceeded = false;
        Assert.Equal(BindingAssessmentOutcome.NotAssessed,
            BindingAssessmentState.FromComparison(failed).AutomaticOutcome);
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteDeltaIsUnavailable(double delta)
    {
        Assert.Equal(BindingAssessmentOutcome.NotAssessed,
            BindingAssessmentState.FromComparison(AvailableComparison(delta)).AutomaticOutcome);
    }

    [Fact]
    public void MismatchedCriteriaOrExplicitUnavailableReasonIsNotAssessed()
    {
        var mismatchedCount = AvailableComparison(10);
        mismatchedCount.NullInformationCriteria = Criteria(110, observationCount: 19);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed,
            BindingAssessmentState.FromComparison(mismatchedCount).AutomaticOutcome);
        var mismatchedMode = AvailableComparison(10);
        mismatchedMode.NullInformationCriteria = Criteria(110, mode: GaussianLikelihoodMode.KnownObservationSigmas);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed,
            BindingAssessmentState.FromComparison(mismatchedMode).AutomaticOutcome);
        var unavailable = AvailableComparison(10);
        unavailable.ComparisonUnavailableReason = "AICc could not be calculated.";
        Assert.Equal(BindingAssessmentOutcome.NotAssessed,
            BindingAssessmentState.FromComparison(unavailable).AutomaticOutcome);
        var missingCriteria = AvailableComparison(10);
        missingCriteria.NullInformationCriteria = null;
        Assert.Equal(BindingAssessmentOutcome.NotAssessed,
            BindingAssessmentState.FromComparison(missingCriteria).AutomaticOutcome);
    }

    [Fact]
    public void DeltaAloneCanRecommendBindingDespiteNoOtherEvidenceGate()
    {
        var comparison = AvailableComparison(12);
        comparison.BindingInformationCriteria = Criteria(104, parameterCount: 0, likelihoodParameterCount: 1,
            observationCount: 4, minusTwoLogLikelihood: 100);
        comparison.NullInformationCriteria = Criteria(116, parameterCount: 1, likelihoodParameterCount: 2,
            observationCount: 4, minusTwoLogLikelihood: 100);
        Assert.Equal(BindingAssessmentOutcome.BindingDetected,
            BindingAssessmentState.FromComparison(comparison).AutomaticOutcome);
    }

    [Fact]
    public void FormatsCompactNullRmsdDeltaAndTooltipInSelectedHeatUnits()
    {
        var comparison = AvailableComparison(10);
        comparison.NullInformationCriteria.ResidualRmsdMicrojoules = 4.184;

        Assert.Equal("1", NullModelComparisonPresentation.NullRmsd(comparison, EnergyUnitFamily.Calories));
        Assert.Equal("1 / +10", NullModelComparisonPresentation.NullRmsdAndDeltaAicc(
            comparison, EnergyUnitFamily.Calories));
        var tooltip = NullModelComparisonPresentation.NullEvidenceTooltip(comparison, EnergyUnitFamily.Calories);
        Assert.Contains("Null AICc: 110", tooltip);
        Assert.Contains("RMSD unit: µcal", tooltip);
    }

    [Fact]
    public void ResultOverrideChangesOnlyAssessmentAndRaisesRefreshEvent()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var global = GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model });
        var result = new AnalysisResult(global);
        result.MarkClean();
        var solution = result.Solution;
        var criteria = result.InformationCriteria;
        var validity = result.ValiditySnapshot;
        var fitDate = result.Date;
        var activeExperiment = DataManager.Current;
        var resultExperiment = result.Solution.Solutions[0].Data;
        var changes = 0;
        result.BindingAssessmentChanged += (_, _) => changes++;

        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
        result.Comments = "already dirty";
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
        Assert.Equal(1, changes);
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        result.UseAutomaticBindingAssessment();
        Assert.Equal(3, changes);
        Assert.Same(solution, result.Solution);
        Assert.Same(criteria, result.InformationCriteria);
        Assert.Same(validity, result.ValiditySnapshot);
        Assert.Equal(fitDate, result.Date);
        Assert.Same(resultExperiment, result.Solution.Solutions[0].Data);
        Assert.Same(activeExperiment, DataManager.Current);
        Assert.True(result.IsModified);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, result.BindingAssessment.EffectiveOutcome);
    }

    [Fact]
    public void ResultRejectsInconclusiveManualOverride()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            result.SetBindingAssessmentOverride(BindingAssessmentOutcome.Inconclusive));
    }

    [Fact]
    public void OverrideCanBeAppliedToUnavailableEvidenceAndCleared()
    {
        var state = BindingAssessmentState.FromComparison(null)
            .WithOverride(BindingAssessmentOutcome.BindingDetected);
        Assert.Equal(BindingAssessmentOutcome.BindingDetected, state.EffectiveOutcome);
        Assert.True(state.IsManual);
        state = state.WithOverride(null);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, state.EffectiveOutcome);
        Assert.False(state.IsManual);
    }

    static NullModelComparison AvailableComparison(double delta) => new()
    {
        BindingFitSucceeded = true,
        NullFitSucceeded = true,
        DeltaAicc = delta,
        BindingInformationCriteria = Criteria(100),
        NullInformationCriteria = Criteria(100 + delta),
    };

    static FitInformationCriteria Criteria(double aicc, int parameterCount = 2, int observationCount = 20,
        GaussianLikelihoodMode mode = GaussianLikelihoodMode.EstimatedCommonVariance, double minusTwoLogLikelihood = 90,
        int? likelihoodParameterCount = null)
        => FitInformationCriteria.Restore(
        observationCount: observationCount, fittedParameterCount: parameterCount,
        likelihoodParameterCount: likelihoodParameterCount
            ?? parameterCount + (mode == GaussianLikelihoodMode.KnownObservationSigmas ? 0 : 1),
        likelihoodMode: mode,
        minusTwoLogLikelihood: minusTwoLogLikelihood, aic: aicc - 4, aicc: aicc,
        isAicAvailable: true, isAiccAvailable: true, aicUnavailableReason: string.Empty,
        aiccUnavailableReason: string.Empty, rawResidualSumOfSquares: 0,
        residualRmsdMicrojoules: 0, standardizedResidualSumOfSquares: 0, logSigmaSquaredSum: 0);
}
