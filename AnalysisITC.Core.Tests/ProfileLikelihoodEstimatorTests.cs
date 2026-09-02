using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class ProfileLikelihoodEstimatorTests
{
    [Fact]
    public void UnweightedTargetUsesFCalibration()
    {
        var target = ProfileLikelihoodEstimator.CalculateUnweightedTarget(20, 12);
        // Independent numerical reference: F(1,12;0.95)=4.747225346722511.
        Assert.Equal(6.666518876390058, target, 10);
    }

    [Fact]
    public void WeightedTargetIsChiSquareOneDegree()
    {
        var target = ProfileLikelihoodEstimator.CalculateWeightedTarget();
        Assert.Equal(3.841458820694124, target, 12);
    }

    [Fact]
    public void InvalidDegreesOfFreedomAndBaselineAreRejectedByCalibrationHelpers()
    {
        Assert.True(double.IsNaN(ProfileLikelihoodEstimator.CalculateUnweightedTarget(2, 0)));
        Assert.True(double.IsNaN(ProfileLikelihoodEstimator.CalculateUnweightedTarget(0, 2)));
        Assert.True(double.IsNaN(ProfileLikelihoodEstimator.CalculateWeightedTarget(1)));
    }

    [Fact]
    public void CompleteProfileUsesEquivalentDisplayScaleAndPreservesEndpoints()
    {
        var result = new ProfileCoordinateResult(
            new ProfileCoordinateId(ParameterType.Affinity1, ParameterBoundaryScope.Local, "e1", 0),
            10, 0, 100,
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, 8),
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, 14));

        var value = result.ToFloatWithError();
        var expected = ProfileLikelihoodEstimator.EquivalentStandardDeviation(10, 8, 14);
        Assert.Equal(10, value.Value);
        Assert.Equal(8, value.Lower);
        Assert.Equal(14, value.Upper);
        Assert.Equal(expected, value.SD, 12);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.None)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood)]
    public void StaleStochasticCloneFlagsAreDisabledOutsideResidualBootstrap(ErrorEstimationMethod method)
    {
        var options = new ModelCloneOptions
        {
            ErrorEstimationMethod = method,
            IncludeConcentrationErrorsInBootstrap = true,
            UnlockBootstrapParameters = true,
        };

        Assert.False(options.EffectiveIncludeConcentrationErrors);
        Assert.False(options.EffectiveUnlockBootstrapParameters);
        Assert.False(options.EffectiveSampleModelOptionParameters);
    }

    [Fact]
    public void ResidualBootstrapIsTheOnlyStochasticCloneMode()
    {
        var options = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };
        Assert.True(options.EffectiveSampleModelOptionParameters);
    }

    [Fact]
    public void ProfileClonePreservesCompletePeakAreaAndSourceBootstrapOptions()
    {
        var model = CreateQuadraticProbe(new[] { 1.0, 2.0, 3.0, 4.0 });
        var sourceOptions = new ModelCloneOptions
        {
            ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
            IncludeConcentrationErrorsInBootstrap = true,
            UnlockBootstrapParameters = true,
        };
        model.ModelCloneOptions = sourceOptions;
        var source = model.Data.Injections[0];
        source.SetPeakArea(new FloatWithError(5, 2, 3, 8));
        source.Include = false;

        var profileOptions = new ModelCloneOptions
        {
            ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood,
            IncludeConcentrationErrorsInBootstrap = true,
            UnlockBootstrapParameters = true,
        };
        var clone = model.GenerateSyntheticModel(new Random(9), profileOptions);
        var copied = clone.Data.Injections[0];

        Assert.Equal(source.PeakArea.Value, copied.PeakArea.Value);
        Assert.Equal(source.PeakArea.SD, copied.PeakArea.SD);
        Assert.Equal(source.PeakArea.Lower, copied.PeakArea.Lower);
        Assert.Equal(source.PeakArea.Upper, copied.PeakArea.Upper);
        Assert.Equal(source.Include, copied.Include);
        Assert.Equal(ErrorEstimationMethod.BootstrapResiduals, model.ModelCloneOptions.ErrorEstimationMethod);
        Assert.True(model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap);
        Assert.True(model.ModelCloneOptions.UnlockBootstrapParameters);
        Assert.False(profileOptions.EffectiveIncludeConcentrationErrors);
        Assert.False(profileOptions.EffectiveUnlockBootstrapParameters);
        Assert.False(profileOptions.EffectiveSampleModelOptionParameters);
    }

    [Fact]
    public void InvalidZeroRssBaselineIsCompleteFailure()
    {
        var data = new ExperimentData("profile-invalid.itc");
        var model = new Model(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);

        var run = ProfileLikelihoodEstimator.Run(model, SolverAlgorithm.NelderMead, false, 10);

        Assert.Equal(ErrorEstimationOutcome.CompleteFailure, run.Outcome);
        Assert.Empty(run.Coordinates);
    }

    [Fact]
    public void QuadraticProbeUsesGeometricBracketAndBisection()
    {
        var model = CreateQuadraticProbe(new[] { -1000.0, 1000.0, -1000.0, 1000.0 });
        var run = ProfileLikelihoodEstimator.Run(model, SolverAlgorithm.NelderMead, false, 10);

        var coordinate = Assert.Single(run.Coordinates);
        Assert.Equal(ErrorEstimationOutcome.Completed, run.Outcome);
        Assert.Equal(ProfileSideOutcome.EndpointFound, coordinate.Lower.Outcome);
        Assert.Equal(ProfileSideOutcome.EndpointFound, coordinate.Upper.Outcome);
        Assert.InRange(coordinate.Lower.Endpoint, -2500, -1000);
        Assert.InRange(coordinate.Upper.Endpoint, 1000, 2500);
        Assert.True(coordinate.Lower.EvaluationCount >= 2);
        Assert.True(coordinate.Upper.EvaluationCount >= 2);
        Assert.Equal(0, coordinate.Lower.AttemptedSolverCalls);
        Assert.Equal(4, run.N);
        Assert.Equal(1, run.P);
        Assert.Equal(3, run.Df);
    }

    [Fact]
    public void FittedCoordinateAtFiniteSideBoundIsCensoredWithoutEvaluation()
    {
        var model = CreateQuadraticProbe(new[] { 1.0, 1.0, 1.0, 1.0 });
        model.Parameters.Table[ParameterType.Offset].SetLimits(new[] { 0.0, 100.0 });
        var run = ProfileLikelihoodEstimator.Run(model, SolverAlgorithm.NelderMead, false, 10);

        var coordinate = Assert.Single(run.Coordinates);
        Assert.Equal(ProfileSideOutcome.BoundReachedBeforeCrossing, coordinate.Lower.Outcome);
        Assert.Equal(0, coordinate.Lower.EvaluationCount);
        Assert.Equal(0, coordinate.Lower.AttemptedSolverCalls);
    }

    [Fact]
    public void ProgressReportsMonotoneSideCompletionAndEndpointCounts()
    {
        var progress = new List<ProfileLikelihoodProgress>();
        var run = ProfileLikelihoodEstimator.Run(
            CreateQuadraticProbe(new[] { 1.0, 1.0, 1.0, 1.0 }), SolverAlgorithm.NelderMead, false, 10,
            progress: progress.Add);

        Assert.NotEmpty(progress);
        Assert.Equal(2 * run.P, progress[^1].CompletedSides);
        Assert.True(progress.Zip(progress.Skip(1), (a, b) => b.CompletedSides >= a.CompletedSides
            && b.AttemptedSolverCalls >= a.AttemptedSolverCalls).All(value => value));
        Assert.True(progress[^1].EndpointsFound <= progress[^1].CompletedSides);
    }

    [Fact]
    public void ProgressCompletesBothSearchesWhenOneSideIsCensored()
    {
        var progress = new List<ProfileLikelihoodProgress>();
        var model = CreateQuadraticProbe(new[] { 1.0, 1.0, 1.0, 1.0 });
        model.Parameters.Table[ParameterType.Offset].SetLimits(new[] { 0d, 100d });
        ProfileLikelihoodEstimator.Run(model, SolverAlgorithm.NelderMead, false, 10, progress: progress.Add);

        Assert.Equal(2, progress[^1].CompletedSides);
        Assert.True(progress[^1].EndpointsFound < progress[^1].CompletedSides);
    }

    [Fact]
    public void AggregateProfileSummaryCountsMissingIndependentMemberAsPartial()
    {
        var first = CreateSummaryModel("profile-summary-first");
        var second = CreateSummaryModel("profile-summary-second");
        var model = new GlobalModel(new List<Model> { first, second });
        model.Parameters.AddIndivdualParameter(first.Parameters);
        model.Parameters.AddIndivdualParameter(second.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = model },
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        model.Solution = global;
        global.Solutions[0].ProfileLikelihoodRun = new ProfileLikelihoodRunResult(.95,
            ProfileLikelihoodCalibration.UnweightedFCalibratedRss, 4, 1, 1, 3, 1, 1,
            SolverAlgorithm.NelderMead, false, 1, 10, 24, 40, TimeSpan.Zero,
            ErrorEstimationOutcome.Completed, new[] { new ProfileCoordinateResult(
                new ProfileCoordinateId(ParameterType.Offset, ParameterBoundaryScope.Local, first.Data.UniqueID), 0, -1, 1,
                new ProfileSideResult(ProfileSideOutcome.EndpointFound, -.5),
                new ProfileSideResult(ProfileSideOutcome.EndpointFound, .5)) });

        var summary = ProfileLikelihoodEstimator.Summarize(global);
        Assert.Equal(ErrorEstimationOutcome.PartialFailure, summary.Outcome);
        Assert.Equal(2, summary.EndpointsFound);
        Assert.Equal(16, summary.TotalSides);
    }

    [Fact]
    public void AggregateProfileSummaryWithNoMemberRunsIsNotRun()
    {
        var first = CreateSummaryModel("profile-summary-none-first");
        var second = CreateSummaryModel("profile-summary-none-second");
        var model = new GlobalModel(new List<Model> { first, second });
        model.Parameters.AddIndivdualParameter(first.Parameters);
        model.Parameters.AddIndivdualParameter(second.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = model },
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));

        var summary = ProfileLikelihoodEstimator.Summarize(global);
        Assert.Equal(ErrorEstimationOutcome.NotRun, summary.Outcome);
        Assert.Equal(0, summary.EndpointsFound);
        Assert.Equal(16, summary.TotalSides);
    }

    [Fact]
    public void AggregateProfileSummaryWithFailureAndMissingMemberIsCompleteFailure()
    {
        var first = CreateSummaryModel("profile-summary-failure-first");
        var second = CreateSummaryModel("profile-summary-failure-second");
        var model = new GlobalModel(new List<Model> { first, second });
        model.Parameters.AddIndivdualParameter(first.Parameters);
        model.Parameters.AddIndivdualParameter(second.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = model },
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        global.Solutions[0].ProfileLikelihoodRun = new ProfileLikelihoodRunResult(.95,
            ProfileLikelihoodCalibration.UnweightedFCalibratedRss, 4, 1, 1, 3, 1, 1,
            SolverAlgorithm.NelderMead, false, 1, 10, 24, 40, TimeSpan.Zero,
            ErrorEstimationOutcome.CompleteFailure, Array.Empty<ProfileCoordinateResult>());

        var summary = ProfileLikelihoodEstimator.Summarize(global);
        Assert.Equal(ErrorEstimationOutcome.CompleteFailure, summary.Outcome);
        Assert.Equal(0, summary.EndpointsFound);
        Assert.Equal(16, summary.TotalSides);
    }

    [Fact]
    public void AggregateProfileSummaryIgnoresZeroCoordinateNotRunMember()
    {
        var fitted = CreateOffsetOnlyModel("profile-summary-fitted");
        var empty = CreateEmptyOneSetModel("profile-summary-empty");
        var model = new GlobalModel(new List<Model> { fitted, empty });
        model.Parameters.AddIndivdualParameter(fitted.Parameters);
        model.Parameters.AddIndivdualParameter(empty.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = model },
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        fitted.Solution.ProfileLikelihoodRun = CreateCompleteOffsetRun();
        empty.Solution.ProfileLikelihoodRun = new ProfileLikelihoodRunResult(.95,
            ProfileLikelihoodCalibration.UnweightedFCalibratedRss, 0, 0, 1, 0, 1, 1,
            SolverAlgorithm.NelderMead, false, 1, 10, 24, 40, TimeSpan.Zero,
            ErrorEstimationOutcome.NotRun, Array.Empty<ProfileCoordinateResult>());

        var summary = ProfileLikelihoodEstimator.Summarize(global);
        Assert.Equal(ErrorEstimationOutcome.Completed, summary.Outcome);
        Assert.Equal(2, summary.EndpointsFound);
        Assert.Equal(2, summary.TotalSides);
    }

    [Fact]
    public void AggregateProfileSummaryIgnoresMissingZeroCoordinateMember()
    {
        var fitted = CreateOffsetOnlyModel("profile-summary-fitted-missing");
        var empty = CreateEmptyOneSetModel("profile-summary-empty-missing");
        var model = new GlobalModel(new List<Model> { fitted, empty });
        model.Parameters.AddIndivdualParameter(fitted.Parameters);
        model.Parameters.AddIndivdualParameter(empty.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = model },
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        fitted.Solution.ProfileLikelihoodRun = CreateCompleteOffsetRun();

        var summary = ProfileLikelihoodEstimator.Summarize(global);
        Assert.Equal(ErrorEstimationOutcome.Completed, summary.Outcome);
        Assert.Equal(2, summary.EndpointsFound);
        Assert.Equal(2, summary.TotalSides);
    }

    [Fact]
    public void AggregateProfileSummaryUsesPersistedRunParameterCount()
    {
        var empty = CreateEmptyOneSetModel("profile-summary-persisted");
        var model = new GlobalModel(new List<Model> { empty });
        model.Parameters.AddIndivdualParameter(empty.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = model },
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        empty.Solution.ProfileLikelihoodRun = CreateCompleteOffsetRun();

        var summary = ProfileLikelihoodEstimator.Summarize(global);
        Assert.Equal(ErrorEstimationOutcome.Completed, summary.Outcome);
        Assert.Equal(2, summary.TotalSides);
    }

    [Fact]
    public void FlatFiniteProfileExhaustsItsExpansionBudget()
    {
        var model = CreateProbe(new[] { 1.0, 1.0, 1.0, 1.0 }, _ => 0);
        model.Parameters.Table[ParameterType.Offset].SetLimits(new[] { -1e12, 1e12 });
        var coordinate = Assert.Single(ProfileLikelihoodEstimator.Run(model, SolverAlgorithm.NelderMead, false, 10).Coordinates);
        Assert.Equal(ProfileSideOutcome.SearchExhausted, coordinate.Lower.Outcome);
        Assert.Equal(ProfileSideOutcome.SearchExhausted, coordinate.Upper.Outcome);
    }

    [Fact]
    public void NonfiniteProfileFrontierIsReportedWithoutAnEndpoint()
    {
        var model = CreateProbe(new[] { 1.0, 1.0, 1.0, 1.0 }, value => value == 0 ? 0 : double.NaN);
        var coordinate = Assert.Single(ProfileLikelihoodEstimator.Run(model, SolverAlgorithm.NelderMead, false, 10).Coordinates);
        Assert.Equal(ProfileSideOutcome.NonFiniteCandidate, coordinate.Lower.Outcome);
        Assert.Equal(ProfileSideOutcome.NonFiniteCandidate, coordinate.Upper.Outcome);
    }

    [Fact]
    public void SearchRetriesAnUnusableWarmCandidateExactlyOnceFromColdStart()
    {
        SolverInterface.TerminateAnalysisFlag.Lower();
        var warmFailures = 0;
        var coldRetries = 0;
        var side = ProfileLikelihoodEstimator.SearchForTesting((value, warm) =>
        {
            if (warm != null && Math.Abs(value - 1) < 1e-12)
            {
                warmFailures++;
                return new ProfileLikelihoodEstimator.Candidate { OptimizerFailure = true };
            }

            if (warm == null && Math.Abs(value - 1) < 1e-12)
                coldRetries++;
            return new ProfileLikelihoodEstimator.Candidate
            {
                Usable = true,
                Objective = Math.Exp(value * value),
                ObservationCount = 1,
                NuisanceValues = new Dictionary<string, double> { ["nuisance"] = value },
            };
        }, 0, new[] { 0d, 8d }, 1, .5, 1, 1);

        Assert.Equal(ProfileSideOutcome.EndpointFound, side.Outcome);
        Assert.Equal(1, warmFailures);
        Assert.Equal(1, coldRetries);
    }

    [Fact]
    public void PersistentlyUnusableProfileCandidateReportsOptimizerFailure()
    {
        SolverInterface.TerminateAnalysisFlag.Lower();
        var warmFailures = 0;
        var coldRetries = 0;
        var side = ProfileLikelihoodEstimator.SearchForTesting((value, warm) =>
        {
            if (value > .5 && warm != null) warmFailures++;
            if (value > .5 && warm == null) coldRetries++;
            if (value > .5)
                return new ProfileLikelihoodEstimator.Candidate { OptimizerFailure = true };
            return new ProfileLikelihoodEstimator.Candidate
            {
                Usable = true,
                Objective = Math.Exp(value * value),
                ObservationCount = 1,
                NuisanceValues = new Dictionary<string, double> { ["nuisance"] = value },
            };
        }, 0, new[] { 0d, 8d }, 1, .5, 1, 1);

        Assert.Equal(ProfileSideOutcome.OptimizerFailure, side.Outcome);
        Assert.True(warmFailures > 0);
        Assert.Equal(warmFailures, coldRetries);
    }

    [Fact]
    public void UnusableRefinementCandidateRemainsOptimizerFailure()
    {
        SolverInterface.TerminateAnalysisFlag.Lower();
        var midpointAttempts = 0;
        var side = ProfileLikelihoodEstimator.SearchForTesting((value, warm) =>
        {
            if (Math.Abs(value - .75) < 1e-12)
            {
                midpointAttempts++;
                return new ProfileLikelihoodEstimator.Candidate { OptimizerFailure = true };
            }
            return new ProfileLikelihoodEstimator.Candidate
            {
                Usable = true,
                Objective = Math.Exp(2 * value * value),
                ObservationCount = 1,
                NuisanceValues = new Dictionary<string, double> { ["nuisance"] = value },
            };
        }, 0, new[] { 0d, 8d }, 1, .5, 1, 1);

        Assert.Equal(ProfileSideOutcome.OptimizerFailure, side.Outcome);
        Assert.Equal(2, midpointAttempts);
    }

    [Fact]
    public void NonmonotonicProfileRetainsFirstCrossingAndRecordsReentry()
    {
        var model = CreateProbe(new[] { .1, -.1, .1, -.1 }, value => Math.Sin(Math.PI * value / 200.0));
        model.Parameters.Table[ParameterType.Offset].SetLimits(new[] { 0d, 5000d });
        model.Parameters.Table[ParameterType.Offset].SetReducedStepSize();
        var coordinate = Assert.Single(ProfileLikelihoodEstimator.Run(model, SolverAlgorithm.NelderMead, false, 10).Coordinates);
        Assert.Equal(ProfileSideOutcome.EndpointFound, coordinate.Upper.Outcome);
        Assert.Contains("NonMonotonicObserved", coordinate.Upper.Warnings);
        Assert.Contains("DisconnectedBeyondEndpoint", coordinate.Upper.Warnings);
    }

    [Fact]
    public void FiniteBoundReachedBeforeCrossingLeavesSideCensored()
    {
        var model = CreateQuadraticProbe(new[] { -1000.0, 1000.0, -1000.0, 1000.0 });
        model.Parameters.Table[ParameterType.Offset].SetLimits(new[] { -100.0, 100.0 });
        var run = ProfileLikelihoodEstimator.Run(model, SolverAlgorithm.NelderMead, false, 10);

        var coordinate = Assert.Single(run.Coordinates);
        Assert.Equal(ErrorEstimationOutcome.CompleteFailure, run.Outcome);
        Assert.Equal(ProfileSideOutcome.BoundReachedBeforeCrossing, coordinate.Lower.Outcome);
        Assert.Equal(ProfileSideOutcome.BoundReachedBeforeCrossing, coordinate.Upper.Outcome);
        Assert.True(double.IsNaN(coordinate.Lower.Endpoint));
        Assert.True(double.IsNaN(coordinate.Upper.Endpoint));
        Assert.False(coordinate.HasCompleteInterval);
    }

    [Fact]
    public void CancellationProducesCancelledRunWithoutInventedIntervals()
    {
        SolverInterface.TerminateAnalysisFlag.Lower();
        using var cancellation = new System.Threading.CancellationTokenSource();
        cancellation.Cancel();
        var run = ProfileLikelihoodEstimator.Run(
            CreateQuadraticProbe(new[] { -1000.0, 1000.0, -1000.0, 1000.0 }),
            SolverAlgorithm.NelderMead, false, 10, cancellationToken: cancellation.Token);

        Assert.Equal(ErrorEstimationOutcome.Cancelled, run.Outcome);
        Assert.DoesNotContain(run.Coordinates, c => c.HasCompleteInterval);
        SolverInterface.TerminateAnalysisFlag.Lower();
    }

    [Fact]
    public void ImprovedPrimaryAbortsAndRetainsTriggerDiagnosticsWithoutChangingSource()
    {
        var model = CreateQuadraticProbe(new[] { 1000.0, 1000.0, 1000.0, 1000.0 });
        var original = model.Parameters.Table[ParameterType.Offset].Value;
        var run = ProfileLikelihoodEstimator.Run(model, SolverAlgorithm.NelderMead, false, 10);

        Assert.Equal(ErrorEstimationOutcome.CompleteFailure, run.Outcome);
        Assert.Contains(run.Coordinates, coordinate =>
            coordinate.Lower.Outcome == ProfileSideOutcome.PrimaryMinimumImproved
            || coordinate.Upper.Outcome == ProfileSideOutcome.PrimaryMinimumImproved);
        Assert.Equal(original, model.Parameters.Table[ParameterType.Offset].Value);
    }

    [Fact]
    public void ZeroRssCandidateIsRecognizedAsImprovedPrimary()
    {
        var run = ProfileLikelihoodEstimator.Run(
            CreateQuadraticProbe(new[] { 500.0, 500.0, 500.0, 500.0 }),
            SolverAlgorithm.NelderMead, false, 10);

        Assert.Equal(ErrorEstimationOutcome.CompleteFailure, run.Outcome);
        Assert.Contains(run.Coordinates, coordinate =>
            coordinate.Lower.Outcome == ProfileSideOutcome.PrimaryMinimumImproved
            || coordinate.Upper.Outcome == ProfileSideOutcome.PrimaryMinimumImproved);
    }

    [Fact]
    public void PartialUpdaterPolicyUsesCompleteIntervalCounts()
    {
        Assert.True(AnalysisResultUpdater.ShouldReplacePartialProfile(
            existingInvalid: true, previousOutcome: null, previousCompleteCount: 0, candidateCompleteCount: 1));
        Assert.True(AnalysisResultUpdater.ShouldReplacePartialProfile(
            existingInvalid: false, previousOutcome: ErrorEstimationOutcome.PartialFailure,
            previousCompleteCount: 2, candidateCompleteCount: 2));
        Assert.False(AnalysisResultUpdater.ShouldReplacePartialProfile(
            existingInvalid: false, previousOutcome: ErrorEstimationOutcome.PartialFailure,
            previousCompleteCount: 2, candidateCompleteCount: 1));
        Assert.False(AnalysisResultUpdater.ShouldReplacePartialProfile(
            existingInvalid: false, previousOutcome: ErrorEstimationOutcome.Completed,
            previousCompleteCount: 2, candidateCompleteCount: 3));
    }

    [Fact]
    public void GlobalProfileUsesCompleteObservationAndCoordinateCounts()
    {
        var first = CreateQuadraticProbe(new[] { -1000.0, 1000.0, -1000.0, 1000.0 });
        var second = CreateQuadraticProbe(new[] { -1000.0, 1000.0, -1000.0, 1000.0 });
        second.Data.SetID("profile-quadratic-2");
        var global = new GlobalModel(new List<Model> { first, second });
        global.Parameters.AddIndivdualParameter(first.Parameters);
        global.Parameters.AddIndivdualParameter(second.Parameters);
        global.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
        global.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 0);
        global.Parameters.SetIndividualFromGlobal();
        global.ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions;

        var run = ProfileLikelihoodEstimator.Run(global, SolverAlgorithm.NelderMead, false, 10);

        Assert.Equal(8, run.N);
        Assert.Equal(1, run.P);
        Assert.Equal(7, run.Df);
        Assert.Equal(ParameterType.Offset, Assert.Single(run.Coordinates).Id.Parameter);
        Assert.Equal(ErrorEstimationOutcome.Completed, run.Outcome);
        Assert.True(Assert.Single(run.Coordinates).HasCompleteInterval);
    }

    static ProbeModel CreateQuadraticProbe(IReadOnlyList<double> observations)
        => CreateProbe(observations, value => value);

    static OneSetOfSites CreateSummaryModel(string id)
    {
        var data = new ExperimentData(id);
        var model = new OneSetOfSites(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -10);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        return model;
    }

    static Model CreateOffsetOnlyModel(string id)
    {
        var data = new ExperimentData(id)
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 1.4e-3,
        };
        data.Injections.Add(new InjectionData(data, 0, 2e-6, 2e-10, true)
        {
            ActualCellConcentration = 10e-6,
            ActualTitrantConcentration = 0,
        });
        data.Injections[0].SetPeakArea(new FloatWithError(0, 1));
        var model = new OneSetOfSites(data);
        model.InitializeParameters(model.Data);
        foreach (var parameter in model.Parameters.Table.Values)
            parameter.SetValue(parameter.Value, true);
        model.Parameters.Table[ParameterType.Offset].SetValue(0, false);
        return model;
    }

    static Model CreateEmptyOneSetModel(string id)
    {
        var model = (OneSetOfSites)CreateOffsetOnlyModel(id);
        foreach (var parameter in model.Parameters.Table.Values)
            parameter.SetValue(parameter.Value, true);
        return model;
    }

    static ProfileLikelihoodRunResult CreateCompleteOffsetRun()
        => new ProfileLikelihoodRunResult(.95,
            ProfileLikelihoodCalibration.UnweightedFCalibratedRss, 1, 1, 1, 0, 1, 1,
            SolverAlgorithm.NelderMead, false, 1, 10, 24, 40, TimeSpan.Zero,
            ErrorEstimationOutcome.Completed, new[] { new ProfileCoordinateResult(
                new ProfileCoordinateId(ParameterType.Offset, ParameterBoundaryScope.Local, "profile-summary-fitted"), 0, -1, 1,
                new ProfileSideResult(ProfileSideOutcome.EndpointFound, -.5),
                new ProfileSideResult(ProfileSideOutcome.EndpointFound, .5)) });

    static ProbeModel CreateProbe(IReadOnlyList<double> observations, Func<double, double> prediction)
    {
        var data = new ExperimentData("profile-quadratic.itc");
        for (var i = 0; i < observations.Count; i++)
        {
            var injection = new InjectionData(data, i, 1e-6, 1e-9, include: true);
            injection.SetPeakArea(new FloatWithError(observations[i], 1));
            data.Injections.Add(injection);
        }

        var model = new ProbeModel(data, prediction);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        return model;
    }

    sealed class ProbeModel : Model
    {
        readonly Func<double, double> prediction;

        public ProbeModel(ExperimentData data, Func<double, double> prediction = null) : base(data)
        {
            this.prediction = prediction ?? (_ => 0);
        }

        public override double Evaluate(int injectionindex, bool withoffset = true)
            => prediction(Parameters.Table[ParameterType.Offset].Value);

        internal override Model GenerateSyntheticModel(Random random, ModelCloneOptions options)
        {
            var clone = new ProbeModel(Data.GetSynthClone(options, random), prediction);
            SetSynthModelParameters(clone, random, options);
            return clone;
        }
    }
}
