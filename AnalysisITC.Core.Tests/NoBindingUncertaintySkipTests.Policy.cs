using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Application;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class NoBindingUncertaintySkipTests
{
    [Theory]
    [InlineData(-2, true)]
    [InlineData(0, true)]
    [InlineData(0.000001, false)]
    [InlineData(8, false)]
    [InlineData(14.999999, false)]
    [InlineData(15, false)]
    public void EstimationDecisionUsesEffectiveAssessmentAtChosenBoundaries(double delta, bool skip)
    {
        var evidence = Comparison(delta);
        Assert.Equal(skip, DecisionProbe.NoBinding(evidence, null));
        Assert.True(DecisionProbe.NoBinding(evidence, BindingAssessmentOutcome.NoBindingDetected));
        Assert.False(DecisionProbe.NoBinding(evidence, BindingAssessmentOutcome.BindingDetected));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("failed-null")]
    [InlineData("missing-criteria")]
    [InlineData("unavailable")]
    [InlineData("nan")]
    [InlineData("infinite")]
    [InlineData("inconsistent-delta")]
    [InlineData("different-count")]
    [InlineData("different-likelihood")]
    public void UnavailableEvidenceNeverAutomaticallySuppressesUncertainty(string defect)
    {
        var comparison = Comparison(-2);
        switch (defect)
        {
            case "missing": comparison = null; break;
            case "failed-null": comparison.NullFitSucceeded = false; break;
            case "missing-criteria": comparison.NullInformationCriteria = null; break;
            case "unavailable": comparison.ComparisonUnavailableReason = "Unavailable test evidence"; break;
            case "nan": comparison.DeltaAicc = double.NaN; break;
            case "infinite": comparison.DeltaAicc = double.PositiveInfinity; break;
            case "inconsistent-delta": comparison.DeltaAicc = -3; break;
            case "different-count": comparison.NullInformationCriteria = Criteria(98, 19); break;
            case "different-likelihood": comparison.NullInformationCriteria = Criteria(98, 20, GaussianLikelihoodMode.KnownObservationSigmas); break;
        }
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, BindingAssessmentState.FromComparison(comparison).AutomaticOutcome);
        Assert.False(DecisionProbe.NoBinding(comparison, null));
        Assert.False(DecisionProbe.NoBinding(comparison, BindingAssessmentOutcome.BindingDetected));
        Assert.True(DecisionProbe.NoBinding(comparison, BindingAssessmentOutcome.NoBindingDetected));
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.None, false)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, true)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, true)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood, true)]
    public void NoneAndDisabledSettingDoNotInstallANoBindingGate(ErrorEstimationMethod method, bool enabled)
    {
        var solver = new DecisionProbe { ErrorEstimationMethod = method, SkipNoBindingUncertainty = true };
        Assert.Equal(enabled, solver.GateEnabled);
        solver.SkipNoBindingUncertainty = false;
        Assert.False(solver.GateEnabled);
    }

    [Fact]
    public void HiddenSettingRoundTripsAndSolversCaptureTheirOwnLaunchSetting()
    {
        var originalStore = PlatformServices.SettingsStore;
        var originalPreferences = PreferencesState.FromSettings();
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        try
        {
            foreach (var value in new[] { false, true })
            {
                AppSettings.SkipUncertaintyEstimationForNoBinding = value;
                AppSettings.Save();
                var launched = new DecisionProbe { ErrorEstimationMethod = ErrorEstimationMethod.LeaveOneOut };
                AppSettings.SkipUncertaintyEstimationForNoBinding = !value;
                Assert.Equal(value, launched.SkipNoBindingUncertainty);
                AppSettings.Load();
                Assert.Equal(value, AppSettings.SkipUncertaintyEstimationForNoBinding);
                Assert.Equal(value, new DecisionProbe { ErrorEstimationMethod = ErrorEstimationMethod.LeaveOneOut }.GateEnabled);
            }
        }
        finally
        {
            originalPreferences.ApplyToSettings();
            AppSettings.SkipUncertaintyEstimationForNoBinding = previousSkip;
            PlatformServices.RegisterSettingsStore(originalStore);
        }
    }

    public static IEnumerable<object[]> OutcomePairs()
    {
        foreach (var first in Enum.GetValues<ErrorEstimationOutcome>())
            foreach (var second in Enum.GetValues<ErrorEstimationOutcome>())
                yield return new object[] { first, second };
    }

    [Theory]
    [MemberData(nameof(OutcomePairs))]
    public void AggregationCannotConcealAnyMemberMishap(ErrorEstimationOutcome first, ErrorEstimationOutcome second)
    {
        var forward = Aggregate(first, second);
        var reversed = Aggregate(second, first);
        Assert.Equal(forward.ErrorEstimationOutcome, reversed.ErrorEstimationOutcome);
        if (new[] { first, second }.Any(IsMishap))
            Assert.True(IsMishap(forward.ErrorEstimationOutcome),
                $"{first} + {second} must remain visibly incomplete or failed.");
        if (first == ErrorEstimationOutcome.SkippedNoBinding && second == ErrorEstimationOutcome.SkippedNoBinding)
            Assert.Equal(ErrorEstimationOutcome.SkippedNoBinding, forward.ErrorEstimationOutcome);
        if (first == ErrorEstimationOutcome.SkippedNoBinding && second == ErrorEstimationOutcome.Completed)
            Assert.Equal(ErrorEstimationOutcome.Completed, forward.ErrorEstimationOutcome);
    }

    [Theory]
    [InlineData(ErrorEstimationOutcome.Cancelled)]
    [InlineData(ErrorEstimationOutcome.PartialFailure)]
    [InlineData(ErrorEstimationOutcome.CompleteFailure)]
    public void ThreeMemberMixturesRetainMishapsInEveryPosition(ErrorEstimationOutcome mishap)
    {
        foreach (var outcomes in new[]
        {
            new[] { mishap, ErrorEstimationOutcome.Completed, ErrorEstimationOutcome.SkippedNoBinding },
            new[] { ErrorEstimationOutcome.Completed, mishap, ErrorEstimationOutcome.SkippedNoBinding },
            new[] { ErrorEstimationOutcome.SkippedNoBinding, ErrorEstimationOutcome.Completed, mishap },
        })
            Assert.True(IsMishap(Aggregate(outcomes).ErrorEstimationOutcome));
    }

    static bool IsMishap(ErrorEstimationOutcome outcome) => outcome is
        ErrorEstimationOutcome.Cancelled or ErrorEstimationOutcome.PartialFailure or ErrorEstimationOutcome.CompleteFailure;

    static SolverConvergence Aggregate(params ErrorEstimationOutcome[] outcomes) =>
        SolverConvergence.FromMultiExperimentAnalysis(outcomes.Select(outcome => Convergence(outcome)).ToList());

    static SolverConvergence Convergence(ErrorEstimationOutcome outcome = ErrorEstimationOutcome.None) =>
        SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Termination = SolverTermination.Converged,
            Algorithm = SolverAlgorithm.NelderMead,
            ErrorEstimationOutcome = outcome,
        });

    static NullModelComparison Comparison(double delta) => new()
    {
        BindingFitSucceeded = true,
        NullFitSucceeded = true,
        BindingInformationCriteria = Criteria(100),
        NullInformationCriteria = Criteria(100 + delta),
        DeltaAicc = delta,
    };

    static FitInformationCriteria Criteria(double aicc, int count = 20,
        GaussianLikelihoodMode mode = GaussianLikelihoodMode.EstimatedCommonVariance) =>
        FitInformationCriteria.Restore(count, 1, 2, mode, 90, aicc - 4, aicc,
            true, true, "", "", 0, 0, 0, 0);

    sealed class DecisionProbe : Solver
    {
        public bool GateEnabled => SkipsNoBindingUncertainty;
        public static bool NoBinding(NullModelComparison comparison, BindingAssessmentOutcome? manual) =>
            IsAssessedNoBinding(comparison, manual);
    }
}
