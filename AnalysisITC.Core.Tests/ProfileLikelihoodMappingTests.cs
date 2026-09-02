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
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Viewer;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class ProfileLikelihoodMappingTests
{
    [Fact]
    public void IncompleteSideDoesNotProduceAnInterval()
    {
        var result = new ProfileCoordinateResult(
            new ProfileCoordinateId(ParameterType.Enthalpy1, ParameterBoundaryScope.Shared),
            -10, -100, 100,
            new ProfileSideResult(ProfileSideOutcome.BoundReachedBeforeCrossing, -100),
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, 2));

        Assert.False(result.HasCompleteInterval);
        Assert.Null(result.Interval);
    }

    [Fact]
    public void CoordinateIdentityRetainsScopeAndExperiment()
    {
        var id = new ProfileCoordinateId(ParameterType.Affinity2, ParameterBoundaryScope.Local, "experiment-2", 7);
        Assert.Equal(ParameterType.Affinity2, id.ParameterKey);
        Assert.False(id.IsShared);
        Assert.Equal("experiment-2", id.ExperimentId);
        Assert.Equal(7, id.PrimaryOptimizerIndex);
    }

    [Fact]
    public void DecreasingTransformReordersEndpointsAndRecomputesEquivalentScale()
    {
        var result = new ProfileCoordinateResult(
            new ProfileCoordinateId(ParameterType.Gibbs1, ParameterBoundaryScope.Shared),
            10, 0, 20,
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, 8, crossingG: -0.01),
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, 14, crossingG: 0.01));

        var transformed = result.Transform(x => -x);
        Assert.Equal(-10, transformed.Value);
        Assert.Equal(-14, transformed.Lower);
        Assert.Equal(-8, transformed.Upper);
        Assert.Equal(ProfileLikelihoodEstimator.EquivalentStandardDeviation(-10, -14, -8), transformed.SD, 12);
    }

    [Fact]
    public void EffectiveBoundsAndWarningsAreReadOnlySnapshots()
    {
        var result = new ProfileCoordinateResult(
            new ProfileCoordinateId(ParameterType.Offset, ParameterBoundaryScope.Local, "e1"),
            0, -1, 1,
            new ProfileSideResult(ProfileSideOutcome.SearchExhausted, warnings: new[] { "NonMonotonicObserved" }),
            new ProfileSideResult(ProfileSideOutcome.Cancelled));

        Assert.IsAssignableFrom<System.Collections.ObjectModel.ReadOnlyCollection<double>>(result.EffectiveBounds);
        Assert.Contains("NonMonotonicObserved", result.Lower.Warnings);
        Assert.Throws<NotSupportedException>(() => ((IList<double>)result.EffectiveBounds)[0] = 7);
    }

    [Fact]
    public void LocalLogAffinityProfileIsReportedAsExactKdInterval()
    {
        var data = new ExperimentData("profile-mapping.itc");
        var model = new OneSetOfSites(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -10);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        var solution = SolutionInterface.FromModel(model, null);
        solution.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        solution.ProfileLikelihoodRun = new ProfileLikelihoodRunResult(.95,
            ProfileLikelihoodCalibration.UnweightedFCalibratedRss, 4, 1, 1, 3, 1, 1,
            SolverAlgorithm.NelderMead, false, 1, 10, 24, 40, TimeSpan.Zero,
            ErrorEstimationOutcome.Completed, new[] { new ProfileCoordinateResult(
                new ProfileCoordinateId(ParameterType.Affinity1, ParameterBoundaryScope.Local, data.UniqueID), 7, 0, 10,
                new ProfileSideResult(ProfileSideOutcome.EndpointFound, 6),
                new ProfileSideResult(ProfileSideOutcome.EndpointFound, 8)) });

        var kd = solution.ReportParameters[ParameterType.Affinity1];
        Assert.Equal(1e-7, kd.Value, 14);
        Assert.Equal(1e-8, kd.Lower, 14);
        Assert.Equal(1e-6, kd.Upper, 14);
        Assert.Equal(ProfileLikelihoodEstimator.EquivalentStandardDeviation(1e-7, 1e-8, 1e-6), kd.SD, 14);
        Assert.True(double.IsFinite(kd.Sample(new Random(7))));
        var uiKd = Assert.IsType<OneSetOfSites.ModelSolution>(solution).Kd;
        Assert.Equal(kd.Value, uiKd.Value, 14);
        Assert.Equal(kd.Lower, uiKd.Lower, 14);
        Assert.Equal(kd.Upper, uiKd.Upper, 14);
        Assert.Equal(kd.SD, uiKd.SD, 14);
    }

    [Fact]
    public void SharedGibbsProfileComposesToExactMemberKdInterval()
    {
        var data = new ExperimentData("profile-gibbs-mapping.itc");
        data.MeasuredTemperature = 26.85;
        var model = new OneSetOfSites(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -10);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        var globalModel = new GlobalModel(new List<Model> { model });
        globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var globalSolver = new GlobalSolver { Model = globalModel };
        var globalSolution = new GlobalSolution(globalSolver, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        globalModel.Solution = globalSolution;
        var temperature = data.MeasuredTemperatureKelvin;
        var coordinate = new ProfileCoordinateResult(
            new ProfileCoordinateId(ParameterType.Gibbs1, ParameterBoundaryScope.Shared),
            GlobalConstraintSemantics.GibbsFromLog10Affinity(7, temperature), -1e6, 1e6,
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, GlobalConstraintSemantics.GibbsFromLog10Affinity(8, temperature)),
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, GlobalConstraintSemantics.GibbsFromLog10Affinity(6, temperature)));
        globalSolution.ProfileLikelihoodRun = new ProfileLikelihoodRunResult(.95,
            ProfileLikelihoodCalibration.UnweightedFCalibratedRss, 4, 1, 1, 3, 1, 1,
            SolverAlgorithm.NelderMead, false, 1, 10, 24, 40, TimeSpan.Zero,
            ErrorEstimationOutcome.Completed, new[] { coordinate });

        var kd = globalSolution.Solutions[0].ReportParameters[ParameterType.Affinity1];
        Assert.Equal(1e-7, kd.Value, 14);
        Assert.Equal(1e-8, kd.Lower, 14);
        Assert.Equal(1e-6, kd.Upper, 14);
        Assert.Equal(ProfileLikelihoodEstimator.EquivalentStandardDeviation(1e-7, 1e-8, 1e-6), kd.SD, 14);
        var uiKd = Assert.IsType<OneSetOfSites.ModelSolution>(globalSolution.Solutions[0]).Kd;
        Assert.Equal(kd.Value, uiKd.Value, 14);
        Assert.Equal(kd.Lower, uiKd.Lower, 14);
        Assert.Equal(kd.Upper, uiKd.Upper, 14);
        Assert.Equal(kd.SD, uiKd.SD, 14);
    }

    [Fact]
    public void ProfileSummaryPreservesIdenticalDirectEndpointValue()
    {
        var data = new ExperimentData("profile-export-summary.itc");
        var model = new OneSetOfSites(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -10);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        var globalModel = new GlobalModel(new List<Model> { model });
        globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        globalModel.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood };
        var globalSolution = new GlobalSolution(new GlobalSolver { Model = globalModel },
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        globalModel.Solution = globalSolution;
        var result = new AnalysisResult(globalSolution);
        var direct = new FloatWithError(7, .25, 6.5, 7.5);

        var summary = AnalysisResultTableExporter.SummaryValue(result, new List<FloatWithError> { direct, direct });

        Assert.Equal(direct, summary);
        Assert.Equal(6.5, summary.Lower);
        Assert.Equal(7.5, summary.Upper);
    }

    [Fact]
    public void TemperatureProfileCoordinatesReplaceExactComponentsAndEvaluateNormally()
    {
        var solution = CreateTemperatureDependentSolution(twoSlots: false);
        var originalReference = solution.TemperatureDependence[ParameterType.Enthalpy1].ReferenceT;
        var href = CompleteCoordinate(ParameterType.Enthalpy1, -10, -14, -9);
        var cp = CompleteCoordinate(ParameterType.HeatCapacity1, .4, .1, .8);

        solution.ApplyProfileTemperatureCoordinates(ProfileRun(ErrorEstimationOutcome.Completed, href, cp));

        var dependence = solution.TemperatureDependence[ParameterType.Enthalpy1];
        Assert.Equal(originalReference, dependence.ReferenceT);
        Assert.Equal((-14, -9), (dependence.Intercept.Lower, dependence.Intercept.Upper));
        Assert.Equal((.1, .8), (dependence.Slope.Lower, dependence.Slope.Upper));
        Assert.Equal(dependence.Intercept, dependence.Evaluate(dependence.ReferenceT));
        var evaluated = dependence.Evaluate(dependence.ReferenceT + 5);
        Assert.Equal(-10 + 5 * .4, evaluated.Value, 12);
        Assert.Equal(-14, solution.Solutions[0].Parameters[ParameterType.Enthalpy1].Value, 12);
        Assert.Equal(-6, solution.Solutions[1].Parameters[ParameterType.Enthalpy1].Value, 12);
        Assert.True(solution.Solutions[0].Parameters[ParameterType.Enthalpy1].HasError);
        Assert.True(solution.Solutions[1].Parameters[ParameterType.Enthalpy1].HasError);
        Assert.Equal(dependence.Evaluate(solution.Solutions[0].Temp),
            solution.Solutions[0].ReportParameters[ParameterType.Enthalpy1]);
        Assert.Equal(dependence.Evaluate(solution.Solutions[1].Temp),
            solution.Solutions[1].ReportParameters[ParameterType.Enthalpy1]);
        Assert.True(solution.Solutions[0].ReportParameters[ParameterType.EntropyContribution1].HasError);
        Assert.True(solution.Solutions[1].ReportParameters[ParameterType.EntropyContribution1].HasError);
    }

    [Theory]
    [InlineData(ErrorEstimationOutcome.PartialFailure)]
    [InlineData(ErrorEstimationOutcome.Cancelled)]
    public void IncompleteTemperatureProfileUpdatesOnlyCompletedCoordinate(ErrorEstimationOutcome outcome)
    {
        var solution = CreateTemperatureDependentSolution(twoSlots: false);
        var original = solution.TemperatureDependence[ParameterType.Enthalpy1];
        var incompleteHref = new ProfileCoordinateResult(
            new ProfileCoordinateId(ParameterType.Enthalpy1, ParameterBoundaryScope.Shared),
            -12, -20, 0,
            new ProfileSideResult(ProfileSideOutcome.SearchExhausted),
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, -9));
        var cp = CompleteCoordinate(ParameterType.HeatCapacity1, .4, .1, .8);

        solution.ApplyProfileTemperatureCoordinates(ProfileRun(outcome, incompleteHref, cp));

        var dependence = solution.TemperatureDependence[ParameterType.Enthalpy1];
        Assert.Equal(original.Intercept, dependence.Intercept);
        Assert.Equal((.1, .8), (dependence.Slope.Lower, dependence.Slope.Upper));
        Assert.Equal(original.ReferenceT, dependence.ReferenceT);
    }

    [Fact]
    public void CompleteFailureDoesNotChangeTemperatureDependence()
    {
        var solution = CreateTemperatureDependentSolution(twoSlots: false);
        var original = solution.TemperatureDependence[ParameterType.Enthalpy1];

        solution.ApplyProfileTemperatureCoordinates(ProfileRun(
            ErrorEstimationOutcome.CompleteFailure,
            CompleteCoordinate(ParameterType.Enthalpy1, -12, -14, -9),
            CompleteCoordinate(ParameterType.HeatCapacity1, .4, .1, .8)));

        var dependence = solution.TemperatureDependence[ParameterType.Enthalpy1];
        Assert.Equal(original.Intercept, dependence.Intercept);
        Assert.Equal(original.Slope, dependence.Slope);
        Assert.Equal(original.ReferenceT, dependence.ReferenceT);
    }

    [Fact]
    public void MultiSlotTemperatureProfileCoordinatesMatchTheirThermodynamicSlot()
    {
        var solution = CreateTemperatureDependentSolution(twoSlots: true);
        var slot1 = solution.TemperatureDependence[ParameterType.Enthalpy1];

        solution.ApplyProfileTemperatureCoordinates(ProfileRun(
            ErrorEstimationOutcome.Completed,
            CompleteCoordinate(ParameterType.Enthalpy2, -25, -28, -22),
            CompleteCoordinate(ParameterType.HeatCapacity2, -.7, -1.1, -.2)));

        Assert.Equal(slot1.Intercept, solution.TemperatureDependence[ParameterType.Enthalpy1].Intercept);
        Assert.Equal(slot1.Slope, solution.TemperatureDependence[ParameterType.Enthalpy1].Slope);
        var slot2 = solution.TemperatureDependence[ParameterType.Enthalpy2];
        Assert.Equal((-28, -22), (slot2.Intercept.Lower, slot2.Intercept.Upper));
        Assert.Equal((-1.1, -.2), (slot2.Slope.Lower, slot2.Slope.Upper));
        Assert.Equal(slot2.Evaluate(solution.Solutions[0].Temp),
            solution.Solutions[0].ReportParameters[ParameterType.Enthalpy2]);
        Assert.Equal(slot2.Evaluate(solution.Solutions[1].Temp),
            solution.Solutions[1].ReportParameters[ParameterType.Enthalpy2]);
        Assert.True(solution.Solutions[0].ReportParameters[ParameterType.Enthalpy2].HasError);
        Assert.True(solution.Solutions[1].ReportParameters[ParameterType.Enthalpy2].HasError);
    }

    [Fact]
    public void TemperatureProfileTooltipsDistinguishDirectAndEvaluatedIntervals()
    {
        var solution = CreateTemperatureDependentSolution(twoSlots: false);
        var run = ProfileRun(
            ErrorEstimationOutcome.Completed,
            CompleteCoordinate(ParameterType.Enthalpy1, -10, -14, -9),
            CompleteCoordinate(ParameterType.HeatCapacity1, .4, .1, .8));
        solution.ProfileLikelihoodRun = run;
        solution.ApplyProfileTemperatureCoordinates(run);
        var result = new AnalysisResult(solution);

        var atReference = AnalysisResultParameterEvaluator.Evaluate(
            result, solution.MeanTemperature, EnergyUnit.Joule, UncertaintyDisplayStyle.ConfidenceInterval);
        Assert.Contains("(direct profile)", atReference.Rows.Single(row => row.Label.Contains("∆Cp")).Tooltip);
        Assert.Contains("(direct profile)", atReference.Rows.Single(row => row.Label.Contains("∆H")).Tooltip);

        var away = AnalysisResultParameterEvaluator.Evaluate(
            result, solution.MeanTemperature + 5, EnergyUnit.Joule, UncertaintyDisplayStyle.ConfidenceInterval);
        Assert.Contains("(propagated)", away.Rows.Single(row => row.Label.Contains("∆H")).Tooltip);
    }

    [Fact]
    public async Task TemperatureProfileCoordinatesSurviveFtxtcRoundTrip()
    {
        var solution = CreateTemperatureDependentSolution(twoSlots: true);
        var run = ProfileRun(
            ErrorEstimationOutcome.Completed,
            CompleteCoordinate(ParameterType.Enthalpy2, -25, -28, -22),
            CompleteCoordinate(ParameterType.HeatCapacity2, -.7, -1.1, -.2));
        solution.ProfileLikelihoodRun = run;
        solution.ApplyProfileTemperatureCoordinates(run);
        var result = new AnalysisResult(solution);

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, solution.Model.Models.Select(model => model.Data), new[] { result });
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());

        var dependence = restored.Solution.TemperatureDependence[ParameterType.Enthalpy2];
        Assert.Equal((-28, -22), (dependence.Intercept.Lower, dependence.Intercept.Upper));
        Assert.Equal((-1.1, -.2), (dependence.Slope.Lower, dependence.Slope.Upper));
        Assert.Equal(dependence.Evaluate(restored.Solution.Solutions[0].Temp),
            restored.Solution.Solutions[0].ReportParameters[ParameterType.Enthalpy2]);
        Assert.Equal(dependence.Evaluate(restored.Solution.Solutions[1].Temp),
            restored.Solution.Solutions[1].ReportParameters[ParameterType.Enthalpy2]);
    }

    [Fact]
    public void TemperatureDependentAffinityMapsDirectGibbsAndPropagatesSummaryErrors()
    {
        const double gibbsValue = -40000;
        var solution = CreateAffinityConstrainedSolution(VariableConstraint.TemperatureDependent, gibbsValue);
        var coordinate = CompleteCoordinate(ParameterType.Gibbs1, gibbsValue, -43123.456, -38000);
        var run = ProfileRun(ErrorEstimationOutcome.Completed, coordinate);
        solution.ProfileLikelihoodRun = run;

        solution.ApplyProfileTemperatureCoordinates(run);

        var dependence = solution.TemperatureDependence[ParameterType.Gibbs1];
        Assert.Equal(new FloatWithError(0), dependence.Slope);
        Assert.Equal(coordinate.ToFloatWithError(), dependence.Intercept);
        Assert.Equal(dependence.Intercept, dependence.Evaluate(20));
        Assert.Equal(dependence.Intercept, dependence.Evaluate(40));

        var evaluation = AnalysisResultParameterEvaluator.Evaluate(
            new AnalysisResult(solution), 25, EnergyUnit.Joule, UncertaintyDisplayStyle.ConfidenceInterval);
        Assert.Contains("(direct profile)",
            evaluation.Rows.Single(row => row.Label.Contains("∆G")).Tooltip);
        Assert.Contains("(propagated)",
            evaluation.Rows.Single(row => row.Label.Contains("Kd")).Tooltip);
        Assert.Contains("-43123", evaluation.Rows.Single(row => row.Label.Contains("∆G")).Tooltip);
        Assert.DoesNotContain("-43123.5", evaluation.Rows.Single(row => row.Label.Contains("∆G")).Tooltip);
        Assert.True(FWEMath.Exp(dependence.Evaluate(25) / ((25 + 273.15) * Energy.R)).HasError);
        Assert.False(solution.Solutions[0].Parameters.ContainsKey(ParameterType.Gibbs1));
        Assert.False(solution.Solutions[0].Parameters.ContainsKey(ParameterType.EntropyContribution1));
    }

    [Fact]
    public void SameForAllAffinityUsesSingleUncertainAbsoluteZeroDependence()
    {
        const double logKa = 7;
        var solution = CreateAffinityConstrainedSolution(VariableConstraint.SameForAll, logKa);
        var coordinate = CompleteCoordinate(ParameterType.Affinity1, logKa, 6, 8);

        solution.ApplyProfileTemperatureCoordinates(ProfileRun(ErrorEstimationOutcome.Completed, coordinate));

        var dependence = solution.TemperatureDependence[ParameterType.Gibbs1];
        var expectedSlope = -Energy.R * Math.Log(10.0) * coordinate.ToFloatWithError();
        Assert.Equal(-273.15, dependence.ReferenceT);
        Assert.Equal(new FloatWithError(0), dependence.Intercept);
        Assert.Equal(expectedSlope, dependence.Slope);
        foreach (var temperature in new[] { 20.0, 40.0 })
        {
            var expected = (temperature + 273.15) * expectedSlope;
            var actual = dependence.Evaluate(temperature);
            Assert.Equal(expected, actual);
            var kd = FWEMath.Exp(actual / ((temperature + 273.15) * Energy.R));
            Assert.Equal(1e-8, kd.Lower, 13);
            Assert.Equal(1e-7, kd.Value, 13);
            Assert.Equal(1e-6, kd.Upper, 13);
        }
    }

    [Fact]
    public void AffinityProfileUpdatesOnlyItsMatchingThermodynamicSlot()
    {
        var source = CreateTemperatureDependentSolution(twoSlots: true);
        var model = source.Model;
        model.Parameters.SetConstraintForParameter(ParameterType.Affinity2, VariableConstraint.SameForAll);
        model.Parameters.AddorUpdateGlobalParameter(ParameterType.Affinity2, 6);
        var solution = new GlobalSolution(
            new GlobalSolver { Model = model, ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood },
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        model.Solution = solution;
        var originalGibbs1 = solution.TemperatureDependence[ParameterType.Gibbs1];
        var originalEntropy1 = solution.TemperatureDependence[ParameterType.EntropyContribution1];

        solution.ApplyProfileTemperatureCoordinates(ProfileRun(
            ErrorEstimationOutcome.Completed,
            CompleteCoordinate(ParameterType.Affinity2, 6, 5.5, 6.8)));

        AssertLinearFitEqual(originalGibbs1, solution.TemperatureDependence[ParameterType.Gibbs1]);
        AssertLinearFitEqual(originalEntropy1, solution.TemperatureDependence[ParameterType.EntropyContribution1]);
        Assert.True(solution.TemperatureDependence[ParameterType.Gibbs2].Slope.HasError);
        Assert.True(solution.TemperatureDependence[ParameterType.EntropyContribution2].Slope.HasError);
        Assert.False(solution.TemperatureDependence[ParameterType.Gibbs1].Slope.HasError);
    }

    [Fact]
    public async Task GlobalFitWithLocalEnthalpiesPropagatesSummaryEnthalpyAndHeatCapacityErrors()
    {
        var solution = CreateAffinityConstrainedSolution(VariableConstraint.TemperatureDependent, -40000);
        solution = SetEnthalpyConstraint(solution, VariableConstraint.None);
        var original = solution.TemperatureDependence[ParameterType.Enthalpy1];
        var coordinates = solution.Solutions.Select((member, index) =>
            CompleteLocalCoordinate(ParameterType.Enthalpy1, member.Data.UniqueID,
                index == 0 ? -14 : -6,
                index == 0 ? -17 : -9,
                index == 0 ? -12 : -4)).ToArray();
        for (var index = 0; index < solution.Solutions.Count; index++)
            solution.Solutions[index].Parameters[ParameterType.Enthalpy1] = coordinates[index].ToFloatWithError();
        var first = coordinates[0].ToFloatWithError();
        var second = coordinates[1].ToFloatWithError();
        var expectedSlope = (-10 * first + 10 * second) / 200;
        var expectedIntercept = (first + second) / 2;
        var run = ProfileRun(
            ErrorEstimationOutcome.Completed,
            CompleteCoordinate(ParameterType.Gibbs1, -40000, -43000, -38000),
            coordinates[0], coordinates[1]);
        solution.ProfileLikelihoodRun = run;

        solution.ApplyProfileTemperatureCoordinates(run);

        var enthalpy = solution.TemperatureDependence[ParameterType.Enthalpy1];
        Assert.Equal(original.Slope.Value, enthalpy.Slope.Value, 12);
        Assert.Equal(original.Intercept.Value, enthalpy.Intercept.Value, 12);
        Assert.Equal(expectedSlope.SD, enthalpy.Slope.SD, 12);
        Assert.Equal(expectedSlope.Lower, enthalpy.Slope.Lower, 12);
        Assert.Equal(expectedSlope.Upper, enthalpy.Slope.Upper, 12);
        Assert.Equal(expectedIntercept.SD, enthalpy.Intercept.SD, 12);
        Assert.True(enthalpy.Slope.HasError);
        Assert.True(enthalpy.Intercept.HasError);
        Assert.Equal(first, solution.Solutions[0].Parameters[ParameterType.Enthalpy1]);
        Assert.Equal(second, solution.Solutions[1].Parameters[ParameterType.Enthalpy1]);
        Assert.True(solution.TemperatureDependence[ParameterType.EntropyContribution1].Evaluate(25).HasError);

        var evaluation = AnalysisResultParameterEvaluator.Evaluate(
            new AnalysisResult(solution), solution.MeanTemperature,
            EnergyUnit.Joule, UncertaintyDisplayStyle.ConfidenceInterval);
        Assert.Contains("(propagated)", evaluation.Rows.Single(row => row.Label.Contains("∆Cp")).Tooltip);
        Assert.Contains("(propagated)", evaluation.Rows.Single(row => row.Label.Contains("∆H")).Tooltip);
        Assert.Contains("(direct profile)", evaluation.Rows.Single(row => row.Label.Contains("∆G")).Tooltip);

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, solution.Model.Models.Select(model => model.Data),
            new[] { new AnalysisResult(solution) });
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());
        AssertLinearFitEqual(enthalpy, restored.Solution.TemperatureDependence[ParameterType.Enthalpy1]);
        AssertLinearFitEqual(solution.TemperatureDependence[ParameterType.Gibbs1],
            restored.Solution.TemperatureDependence[ParameterType.Gibbs1]);
        AssertLinearFitEqual(solution.TemperatureDependence[ParameterType.EntropyContribution1],
            restored.Solution.TemperatureDependence[ParameterType.EntropyContribution1]);
    }

    [Fact]
    public void IncompleteLocalEnthalpyRemainsCentralOnlyInGlobalRegression()
    {
        var solution = SetEnthalpyConstraint(
            CreateAffinityConstrainedSolution(VariableConstraint.TemperatureDependent, -40000),
            VariableConstraint.None);
        var firstMember = solution.Solutions[0];
        var secondMember = solution.Solutions[1];
        var incomplete = new ProfileCoordinateResult(
            new ProfileCoordinateId(ParameterType.Enthalpy1, ParameterBoundaryScope.Local, firstMember.Data.UniqueID),
            -14, -30, 0,
            new ProfileSideResult(ProfileSideOutcome.SearchExhausted),
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, -12));
        var complete = CompleteLocalCoordinate(
            ParameterType.Enthalpy1, secondMember.Data.UniqueID, -6, -9, -4);
        firstMember.Parameters[ParameterType.Enthalpy1] = new FloatWithError(-14);
        secondMember.Parameters[ParameterType.Enthalpy1] = complete.ToFloatWithError();
        var expectedSlope = (-10 * new FloatWithError(-14) + 10 * complete.ToFloatWithError()) / 200;

        solution.ApplyProfileTemperatureCoordinates(ProfileRun(
            ErrorEstimationOutcome.PartialFailure, incomplete, complete));

        var enthalpy = solution.TemperatureDependence[ParameterType.Enthalpy1];
        Assert.Equal(expectedSlope.SD, enthalpy.Slope.SD, 12);
        Assert.Equal(expectedSlope.Lower, enthalpy.Slope.Lower, 12);
        Assert.Equal(expectedSlope.Upper, enthalpy.Slope.Upper, 12);
        Assert.False(firstMember.Parameters[ParameterType.Enthalpy1].HasError);
        Assert.Equal(complete.ToFloatWithError(), secondMember.Parameters[ParameterType.Enthalpy1]);
    }

    [Fact]
    public void SameForAllEnthalpyProducesDirectConstantDependence()
    {
        var solution = SetEnthalpyConstraint(
            CreateAffinityConstrainedSolution(VariableConstraint.TemperatureDependent, -40000),
            VariableConstraint.SameForAll);
        var coordinate = CompleteCoordinate(ParameterType.Enthalpy1, -10, -14, -8);
        var run = ProfileRun(ErrorEstimationOutcome.Completed, coordinate);
        solution.ProfileLikelihoodRun = run;

        solution.ApplyProfileTemperatureCoordinates(run);

        var enthalpy = solution.TemperatureDependence[ParameterType.Enthalpy1];
        Assert.Equal(new FloatWithError(0), enthalpy.Slope);
        Assert.Equal(coordinate.ToFloatWithError(), enthalpy.Intercept);
        Assert.Equal(enthalpy.Intercept, enthalpy.Evaluate(20));
        Assert.Equal(enthalpy.Intercept, enthalpy.Evaluate(40));

        var evaluation = AnalysisResultParameterEvaluator.Evaluate(
            new AnalysisResult(solution), 40, EnergyUnit.Joule, UncertaintyDisplayStyle.ConfidenceInterval);
        Assert.DoesNotContain(evaluation.Rows, row => row.Label.Contains("∆Cp"));
        Assert.Contains("(direct profile)", evaluation.Rows.Single(row => row.Label.Contains("∆H")).Tooltip);
    }

    [Fact]
    public void LocalAffinityProfilesPropagateIntoGlobalGibbsRegressionAndEntropy()
    {
        var solution = CreateTemperatureDependentSolution(twoSlots: false);
        var coordinates = solution.Solutions.Select((member, index) =>
            CompleteLocalCoordinate(ParameterType.Affinity1, member.Data.UniqueID, 7,
                6.7 - index * .1, 7.4 + index * .1)).ToArray();
        for (var index = 0; index < solution.Solutions.Count; index++)
            solution.Solutions[index].Parameters[ParameterType.Affinity1] = coordinates[index].ToFloatWithError();
        var memberGibbs = solution.Solutions.Select(member => member.ReportParameters[ParameterType.Gibbs1]).ToArray();
        var expectedSlope = (-10 * memberGibbs[0] + 10 * memberGibbs[1]) / 200;
        var expectedIntercept = (memberGibbs[0] + memberGibbs[1]) / 2;
        var enthalpy = solution.TemperatureDependence[ParameterType.Enthalpy1];

        solution.ApplyProfileTemperatureCoordinates(ProfileRun(ErrorEstimationOutcome.PartialFailure, coordinates));

        var gibbs = solution.TemperatureDependence[ParameterType.Gibbs1];
        Assert.Equal(expectedSlope.SD, gibbs.Slope.SD, 12);
        Assert.Equal(expectedSlope.Lower, gibbs.Slope.Lower, 12);
        Assert.Equal(expectedSlope.Upper, gibbs.Slope.Upper, 12);
        Assert.Equal(expectedIntercept.SD, gibbs.Intercept.SD, 12);
        var entropy = solution.TemperatureDependence[ParameterType.EntropyContribution1];
        Assert.Equal(gibbs.Slope - enthalpy.Slope, entropy.Slope);
        Assert.Equal(gibbs.Evaluate(entropy.ReferenceT) - enthalpy.Evaluate(entropy.ReferenceT), entropy.Intercept);
        Assert.True(entropy.Evaluate(25).HasError);
    }

    [Fact]
    public void CompleteFailureCoordinatesAreNotPresentedAsDirectIntervals()
    {
        var solution = CreateTemperatureDependentSolution(twoSlots: false);
        solution.ProfileLikelihoodRun = ProfileRun(
            ErrorEstimationOutcome.CompleteFailure,
            CompleteCoordinate(ParameterType.Enthalpy1, -10, -14, -9),
            CompleteCoordinate(ParameterType.HeatCapacity1, .4, .1, .8));

        var evaluation = AnalysisResultParameterEvaluator.Evaluate(
            new AnalysisResult(solution), solution.MeanTemperature,
            EnergyUnit.Joule, UncertaintyDisplayStyle.ConfidenceInterval);

        Assert.DoesNotContain("(direct profile)",
            evaluation.Rows.Single(row => row.Label.Contains("∆Cp")).Tooltip);
        Assert.DoesNotContain("(direct profile)",
            evaluation.Rows.Single(row => row.Label.Contains("∆H")).Tooltip);
    }

    [Fact]
    public async Task GibbsAndEntropyProfileDependencesSurviveFtxtcRoundTrip()
    {
        var solution = CreateAffinityConstrainedSolution(VariableConstraint.SameForAll, 7);
        var run = ProfileRun(
            ErrorEstimationOutcome.Completed,
            CompleteCoordinate(ParameterType.Affinity1, 7, 6, 8),
            CompleteCoordinate(ParameterType.Enthalpy1, -10, -14, -9),
            CompleteCoordinate(ParameterType.HeatCapacity1, .4, .1, .8));
        solution.ProfileLikelihoodRun = run;
        solution.ApplyProfileTemperatureCoordinates(run);
        var expectedGibbs = solution.TemperatureDependence[ParameterType.Gibbs1];
        var expectedEntropy = solution.TemperatureDependence[ParameterType.EntropyContribution1];

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, solution.Model.Models.Select(model => model.Data),
            new[] { new AnalysisResult(solution) });
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());

        AssertLinearFitEqual(expectedGibbs, restored.Solution.TemperatureDependence[ParameterType.Gibbs1]);
        AssertLinearFitEqual(expectedEntropy, restored.Solution.TemperatureDependence[ParameterType.EntropyContribution1]);

        package.Position = 0;
        var viewer = await new ViewerDocumentReader().ReadAsync(
            package, "profile-dependences.ftxtc", ViewerFileFormat.Ftxtc);
        var viewerDependences = Assert.Single(viewer.AnalysisResults).TemperatureParameterEvaluation.Dependences;
        var viewerGibbs = Assert.Single(viewerDependences, item => item.Key == ParameterType.Gibbs1.ToString());
        var viewerEntropy = Assert.Single(viewerDependences,
            item => item.Key == ParameterType.EntropyContribution1.ToString());
        Assert.True(viewerGibbs.Slope.Sd > 0);
        Assert.True(viewerEntropy.Slope.Sd > 0);
        Assert.NotNull(viewerGibbs.Slope.ConfidenceLower);
        Assert.NotNull(viewerEntropy.Intercept.ConfidenceUpper);
    }

    [Fact]
    public void IndividualProfilesPropagateIntoApproximateTemperatureRegression()
    {
        var first = CompleteLocalCoordinate(ParameterType.Enthalpy1, "first", -14, -17, -12);
        var second = CompleteLocalCoordinate(ParameterType.Enthalpy1, "second", -6, -9, -4);
        var solution = CreateIndividuallyProfiledSolution(first, second);
        var firstValue = first.ToFloatWithError();
        var secondValue = second.ToFloatWithError();
        var expectedIntercept = (firstValue + secondValue) / 2;
        var expectedSlope = (-10 * firstValue + 10 * secondValue) / 200;

        var dependence = solution.TemperatureDependence[ParameterType.Enthalpy1];
        Assert.Equal(.4, dependence.Slope.Value, 12);
        Assert.Equal(-10, dependence.Intercept.Value, 12);
        Assert.Equal(expectedSlope.SD, dependence.Slope.SD, 12);
        Assert.Equal(expectedSlope.Lower, dependence.Slope.Lower, 12);
        Assert.Equal(expectedSlope.Upper, dependence.Slope.Upper, 12);
        Assert.Equal(expectedIntercept.SD, dependence.Intercept.SD, 12);
        Assert.Equal(expectedIntercept.Lower, dependence.Intercept.Lower, 12);
        Assert.Equal(expectedIntercept.Upper, dependence.Intercept.Upper, 12);
        Assert.Equal(firstValue, solution.Solutions[0].Parameters[ParameterType.Enthalpy1]);
        Assert.Equal(secondValue, solution.Solutions[1].Parameters[ParameterType.Enthalpy1]);

        var result = new AnalysisResult(solution);
        var evaluation = AnalysisResultParameterEvaluator.Evaluate(
            result, solution.MeanTemperature, EnergyUnit.Joule, UncertaintyDisplayStyle.ConfidenceInterval);
        Assert.Contains("(propagated)",
            evaluation.Rows.Single(row => row.Label.Contains("∆Cp")).Tooltip);
    }

    [Fact]
    public void IncompleteIndividualProfileContributesNoFabricatedRegressionError()
    {
        var incomplete = new ProfileCoordinateResult(
            new ProfileCoordinateId(ParameterType.Enthalpy1, ParameterBoundaryScope.Local, "first"),
            -14, -30, 0,
            new ProfileSideResult(ProfileSideOutcome.SearchExhausted),
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, -12));
        var complete = CompleteLocalCoordinate(ParameterType.Enthalpy1, "second", -6, -9, -4);
        var solution = CreateIndividuallyProfiledSolution(incomplete, complete);
        var fixedFirst = new FloatWithError(-14);
        var secondValue = complete.ToFloatWithError();
        var expectedSlope = (-10 * fixedFirst + 10 * secondValue) / 200;

        var dependence = solution.TemperatureDependence[ParameterType.Enthalpy1];
        Assert.Equal(expectedSlope.SD, dependence.Slope.SD, 12);
        Assert.Equal(expectedSlope.Lower, dependence.Slope.Lower, 12);
        Assert.Equal(expectedSlope.Upper, dependence.Slope.Upper, 12);
        Assert.False(solution.Solutions[0].Parameters[ParameterType.Enthalpy1].HasError);
        Assert.Equal(complete.ToFloatWithError(), solution.Solutions[1].Parameters[ParameterType.Enthalpy1]);
    }

    [Fact]
    public async Task IndividualProfileRegressionSurvivesFtxtcRoundTrip()
    {
        var solution = CreateIndividuallyProfiledSolution(
            CompleteLocalCoordinate(ParameterType.Enthalpy1, "first", -14, -17, -12),
            CompleteLocalCoordinate(ParameterType.Enthalpy1, "second", -6, -9, -4));
        var expected = solution.TemperatureDependence[ParameterType.Enthalpy1];
        var result = new AnalysisResult(solution);

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, solution.Model.Models.Select(model => model.Data), new[] { result });
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());

        var actual = restored.Solution.TemperatureDependence[ParameterType.Enthalpy1];
        Assert.Equal(expected.Slope, actual.Slope);
        Assert.Equal(expected.Intercept, actual.Intercept);
        Assert.Equal(solution.Solutions[0].Parameters[ParameterType.Enthalpy1],
            restored.Solution.Solutions[0].Parameters[ParameterType.Enthalpy1]);
        Assert.Equal(solution.Solutions[1].Parameters[ParameterType.Enthalpy1],
            restored.Solution.Solutions[1].Parameters[ParameterType.Enthalpy1]);
    }

    static GlobalSolution CreateTemperatureDependentSolution(bool twoSlots)
    {
        Model CreateModel(string name, double temperature, double enthalpy1, double enthalpy2)
        {
            var data = new ExperimentData(name)
            {
                MeasuredTemperature = temperature,
                TargetTemperature = temperature,
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 1.4e-3,
            };
            for (var index = 0; index < 2; index++)
            {
                var injection = new InjectionData(data, index, 2e-6, 2e-10, include: true)
                {
                    ActualCellConcentration = 10e-6,
                    ActualTitrantConcentration = (index + 1) * 2e-6,
                    Ratio = index + 1,
                };
                injection.SetPeakArea(new FloatWithError(-2e-6 * (index + 1), 1e-8));
                data.Injections.Add(injection);
            }
            Model model = twoSlots ? new TwoSetsOfSites(data) : new OneSetOfSites(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, enthalpy1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7);
            if (twoSlots)
            {
                model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue2, 1);
                model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy2, enthalpy2);
                model.Parameters.AddOrUpdateParameter(ParameterType.Affinity2, 6);
            }
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            return model;
        }

        var models = new List<Model>
        {
            CreateModel("profile-temperature-low.itc", 20, -14, -18),
            CreateModel("profile-temperature-high.itc", 40, -6, -32),
        };
        var globalModel = new GlobalModel(models)
        {
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood },
        };
        foreach (var model in models) globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        globalModel.Parameters.SetConstraintForParameter(ParameterType.Enthalpy1, VariableConstraint.TemperatureDependent);
        globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.Enthalpy1, -10);
        globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.HeatCapacity1, .4);
        if (twoSlots)
        {
            globalModel.Parameters.SetConstraintForParameter(ParameterType.Enthalpy2, VariableConstraint.TemperatureDependent);
            globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.Enthalpy2, -25);
            globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.HeatCapacity2, -.7);
        }

        var solution = new GlobalSolution(
            new GlobalSolver { Model = globalModel, ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood },
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        globalModel.Solution = solution;
        return solution;
    }

    static ProfileCoordinateResult CompleteCoordinate(ParameterType parameter, double value, double lower, double upper) =>
        new(new ProfileCoordinateId(parameter, ParameterBoundaryScope.Shared), value, lower - 10, upper + 10,
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, lower),
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, upper));

    static GlobalSolution CreateAffinityConstrainedSolution(VariableConstraint constraint, double coordinateValue)
    {
        var source = CreateTemperatureDependentSolution(twoSlots: false);
        var model = source.Model;
        model.Parameters.SetConstraintForParameter(ParameterType.Affinity1, constraint);
        if (constraint == VariableConstraint.TemperatureDependent)
        {
            model.Parameters.AddorUpdateGlobalParameter(ParameterType.Gibbs1, coordinateValue);
            foreach (var member in model.Models)
                member.Parameters.AddOrUpdateParameter(ParameterType.Affinity1,
                    GlobalConstraintSemantics.Log10AffinityFromGibbs(
                        coordinateValue, member.Data.MeasuredTemperatureKelvin));
        }
        else
        {
            model.Parameters.AddorUpdateGlobalParameter(ParameterType.Affinity1, coordinateValue);
            foreach (var member in model.Models)
                member.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, coordinateValue);
        }

        var solution = new GlobalSolution(
            new GlobalSolver { Model = model, ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood },
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        model.Solution = solution;
        return solution;
    }

    static GlobalSolution SetEnthalpyConstraint(GlobalSolution source, VariableConstraint constraint)
    {
        var model = source.Model;
        model.Parameters.SetConstraintForParameter(ParameterType.Enthalpy1, constraint);
        model.Parameters.GlobalTable.Remove(ParameterType.HeatCapacity1);
        if (constraint == VariableConstraint.None)
            model.Parameters.GlobalTable.Remove(ParameterType.Enthalpy1);
        else
            model.Parameters.AddorUpdateGlobalParameter(ParameterType.Enthalpy1, -10);

        var solution = new GlobalSolution(
            new GlobalSolver { Model = model, ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood },
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        model.Solution = solution;
        return solution;
    }

    static void AssertLinearFitEqual(LinearFitWithError expected, LinearFitWithError actual)
    {
        Assert.Equal(expected.ReferenceT, actual.ReferenceT);
        Assert.Equal(expected.Slope, actual.Slope);
        Assert.Equal(expected.Intercept, actual.Intercept);
    }

    static ProfileCoordinateResult CompleteLocalCoordinate(
        ParameterType parameter,
        string experimentIdentity,
        double value,
        double lower,
        double upper) =>
        new(new ProfileCoordinateId(parameter, ParameterBoundaryScope.Local, experimentIdentity),
            value, lower - 10, upper + 10,
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, lower),
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, upper));

    static GlobalSolution CreateIndividuallyProfiledSolution(
        ProfileCoordinateResult firstCoordinate,
        ProfileCoordinateResult secondCoordinate)
    {
        var source = CreateTemperatureDependentSolution(twoSlots: false);
        var model = source.Model;
        model.Parameters.SetConstraintForParameter(ParameterType.Enthalpy1, VariableConstraint.None);
        model.Parameters.ClearGlobalTable();
        var coordinates = new[] { firstCoordinate, secondCoordinate };

        for (var index = 0; index < model.Models.Count; index++)
        {
            var member = model.Models[index].Solution;
            var sourceCoordinate = coordinates[index];
            var coordinate = new ProfileCoordinateResult(
                new ProfileCoordinateId(sourceCoordinate.Id.Parameter, ParameterBoundaryScope.Local,
                    member.Data.UniqueID, sourceCoordinate.Id.PrimaryOptimizerIndex),
                sourceCoordinate.BestValue, sourceCoordinate.LowerBound, sourceCoordinate.UpperBound,
                sourceCoordinate.Lower, sourceCoordinate.Upper, sourceCoordinate.ShapeWarnings);
            member.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
            member.ProfileLikelihoodRun = ProfileRun(
                coordinate.HasCompleteInterval ? ErrorEstimationOutcome.Completed : ErrorEstimationOutcome.CompleteFailure,
                coordinate);
            member.Parameters[ParameterType.Enthalpy1] = coordinate.ToFloatWithError();
        }

        var solution = new GlobalSolution(
            new GlobalSolver { Model = model, ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood },
            model.Models.Select(member => member.Solution).ToList(),
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        model.Solution = solution;
        return solution;
    }

    static ProfileLikelihoodRunResult ProfileRun(
        ErrorEstimationOutcome outcome,
        params ProfileCoordinateResult[] coordinates) =>
        new(.95, ProfileLikelihoodCalibration.UnweightedFCalibratedRss, 20, coordinates.Length, coordinates.Length, 18,
            1, 1, SolverAlgorithm.NelderMead, false, 1, 10, 24, 40, TimeSpan.Zero,
            outcome, coordinates);
}
