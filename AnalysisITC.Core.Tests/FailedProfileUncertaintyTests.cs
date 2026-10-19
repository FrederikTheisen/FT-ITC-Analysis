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
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Viewer;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class FailedProfileUncertaintyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterImprovementRetainsEarlierIntervalOnlyAsDiagnostics(bool weighted)
    {
        var previousParallelism = AppSettings.MaxDegreeOfParallelism;
        SolverInterface.TerminateAnalysisFlag.Lower();
        try
        {
            // Serialize coordinates to reproduce the exact completed-then-failed sequence.
            AppSettings.MaxDegreeOfParallelism = 1;
            var model = CreateProbe();
            var solution = SolutionInterface.FromModel(model, Converged());
            model.Solution = solution;
            var run = ProfileLikelihoodEstimator.Run(model, SolverAlgorithm.LevenbergMarquardt, weighted, 1000);
            Assert.Equal(ErrorEstimationOutcome.CompleteFailure, run.Outcome);
            Assert.Equal(12, run.BaselineObjective, 10);
            Assert.True(run.Coordinates[0].HasCompleteInterval);
            Assert.Equal(ParameterType.Affinity1, run.Coordinates[0].Id.Parameter);
            Assert.Contains(run.Coordinates, coordinate =>
                coordinate.Lower.Outcome == ProfileSideOutcome.PrimaryMinimumImproved
                || coordinate.Upper.Outcome == ProfileSideOutcome.PrimaryMinimumImproved);
            solution.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
            solution.ProfileLikelihoodRun = run;

            var kd = solution.ReportParameters[ParameterType.Affinity1];
            Assert.Equal(1e-7, kd.Value, 14);
            Assert.False(kd.HasConfidenceInterval);
            Assert.Equal(0, kd.SD);
            Assert.Equal(7, model.Parameters.Table[ParameterType.Affinity1].Value);
            Assert.Equal(6, model.Parameters.Table[ParameterType.Affinity2].Value);
            Assert.True(run.Coordinates[0].HasCompleteInterval);
        }
        finally
        {
            AppSettings.MaxDegreeOfParallelism = previousParallelism;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
    }

    [Theory]
    [InlineData(ErrorEstimationOutcome.Completed, true)]
    [InlineData(ErrorEstimationOutcome.PartialFailure, true)]
    [InlineData(ErrorEstimationOutcome.Cancelled, true)]
    [InlineData(ErrorEstimationOutcome.CompleteFailure, false)]
    public void SharedGibbsMappingRespectsOwningRun(ErrorEstimationOutcome outcome, bool available)
    {
        var global = LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.SameForAll,
            affinity: VariableConstraint.TemperatureDependent);
        var member = global.Solutions[0];
        var center = member.ReportParameters[ParameterType.Affinity1].Value;
        var gibbs = global.Model.Parameters.GlobalTable[ParameterType.Gibbs1].Value;
        global.ProfileLikelihoodRun = Run(outcome, Complete(ParameterType.Gibbs1, gibbs));
        var kd = member.ReportParameters[ParameterType.Affinity1];
        Assert.Equal(center, kd.Value, 14);
        Assert.Equal(available, kd.HasConfidenceInterval);
        if (available)
        {
            Assert.Equal(Math.Exp((gibbs - 1) / (Energy.R * member.TempKelvin)), kd.Lower, 14);
            Assert.Equal(Math.Exp((gibbs + 1) / (Energy.R * member.TempKelvin)), kd.Upper, 14);
        }
        else Assert.Equal(0, kd.SD);
    }

    [Theory]
    [InlineData(ErrorEstimationOutcome.Completed, true)]
    [InlineData(ErrorEstimationOutcome.PartialFailure, true)]
    [InlineData(ErrorEstimationOutcome.Cancelled, true)]
    [InlineData(ErrorEstimationOutcome.CompleteFailure, false)]
    public void LocalAndSharedAffinityMappingRespectOwningRun(ErrorEstimationOutcome outcome, bool available)
    {
        var global = LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.SameForAll,
            affinity: VariableConstraint.None);
        var member = global.Solutions[0];
        member.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        var center = member.Parameters[ParameterType.Affinity1].Value;
        var local = Complete(ParameterType.Affinity1, center, member.Data.UniqueID);
        member.ProfileLikelihoodRun = Run(outcome, local);
        Check();
        member.ProfileLikelihoodRun = null;
        global.ProfileLikelihoodRun = Run(outcome,
            Complete(ParameterType.Affinity1, center));
        Check();

        void Check()
        {
            var kd = member.ReportParameters[ParameterType.Affinity1];
            Assert.Equal(Math.Pow(10, -center), kd.Value, 14);
            Assert.Equal(available, kd.HasConfidenceInterval);
            if (available)
            {
                Assert.Equal(Math.Pow(10, -center - 1), kd.Lower, 14);
                Assert.Equal(Math.Pow(10, -center + 1), kd.Upper, 14);
            }
            else Assert.Equal(0, kd.SD);
        }
    }

    [Theory]
    [InlineData(VariableConstraint.None)]
    [InlineData(VariableConstraint.SameForAll)]
    [InlineData(VariableConstraint.TemperatureDependent)]
    public void FailedLinkedProfileDoesNotPropagateCompletedCoordinates(VariableConstraint enthalpy)
    {
        var global = LinkedThermodynamicUncertaintyTests.Create(enthalpy);
        var centers = global.Solutions.Select(member => member.ReportParameters[ParameterType.Affinity1].Value).ToArray();
        var coordinates = new List<ProfileCoordinateResult> { Complete(ParameterType.Gibbs1, -25000) };
        if (enthalpy == VariableConstraint.None)
            coordinates.AddRange(global.Solutions.Select(member =>
                Complete(ParameterType.Enthalpy1, member.Parameters[ParameterType.Enthalpy1].Value, member.Data.UniqueID)));
        else coordinates.Add(Complete(ParameterType.Enthalpy1, -40000));
        if (enthalpy == VariableConstraint.TemperatureDependent)
            coordinates.Add(Complete(ParameterType.HeatCapacity1, 300));
        global.ProfileLikelihoodRun = Run(ErrorEstimationOutcome.CompleteFailure, coordinates.ToArray());

        var summary = new AnalysisResultAggregateSummaryCalculator(new AnalysisResult(global));
        Assert.True(double.IsNaN(summary.BuildDependence(ParameterType.Gibbs1).Evaluate(26.85).SD));
        for (var i = 0; i < global.Solutions.Count; i++)
        {
            var kd = global.Solutions[i].ReportParameters[ParameterType.Affinity1];
            Assert.Equal(centers[i], kd.Value, 14);
            Assert.False(kd.HasConfidenceInterval);
            Assert.True(double.IsNaN(kd.SD));
        }
        Assert.All(global.ProfileLikelihoodRun.Coordinates, coordinate => Assert.True(coordinate.HasCompleteInterval));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void InterpretationDoesNotProjectFailedAffinityInterval(bool changedInputs, bool shared)
    {
        var global = LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.SameForAll,
            affinity: shared ? VariableConstraint.SameForAll : VariableConstraint.None);
        var member = global.Solutions[0];
        member.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        var run = Run(ErrorEstimationOutcome.CompleteFailure,
            Complete(ParameterType.Affinity1, member.Parameters[ParameterType.Affinity1].Value, shared ? null : member.Data.UniqueID));
        if (shared) global.ProfileLikelihoodRun = run;
        else member.ProfileLikelihoodRun = run;
        var result = new AnalysisResult(global);
        if (changedInputs) member.Data.CellConcentration = new FloatWithError(20e-6);
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        var package = AnalysisInterpretationPackageBuilder.Build(report, result);
        var experiment = package.Result.Experiments[0];
        Assert.Equal(changedInputs, !string.IsNullOrWhiteSpace(experiment.MatchedFitDiagnosticsUnavailableReason));
        var kd = Assert.Single(experiment.Parameters, parameter => parameter.QuantityId == "kd-1");
        Assert.Equal(1e-5, kd.BestFitValue.Value, 14);
        Assert.Null(kd.StandardDeviation);
        Assert.Null(kd.Confidence95Lower);
        Assert.Null(kd.Confidence95Upper);
        var diagnostics = shared ? package.Result.Solver.ProfileLikelihood : experiment.Solver.ProfileLikelihood;
        Assert.Equal("CompleteFailure", diagnostics.Outcome);
        Assert.Equal(1, diagnostics.CompleteIntervalCount);
    }

    [Fact]
    public async Task FailedLinkedProfileRetainsDiagnosticsAfterSaveAndViewerRestore()
    {
        var global = LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.SameForAll);
        global.Model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood };
        global.ProfileLikelihoodRun = Run(ErrorEstimationOutcome.CompleteFailure,
            Complete(ParameterType.Gibbs1, -25000), Complete(ParameterType.Enthalpy1, -40000));
        foreach (var member in global.Solutions) member.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        var center = global.Solutions[1].ReportParameters[ParameterType.Affinity1].Value;
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, global.Model.Models.Select(model => model.Data), new[] { new AnalysisResult(global) });
        stream.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(stream)).OfType<AnalysisResult>()).Solution;
        Assert.Equal(ErrorEstimationOutcome.CompleteFailure, restored.ProfileLikelihoodRun.Outcome);
        Assert.Equal(2, restored.ProfileLikelihoodRun.Coordinates.Count);
        Assert.All(restored.ProfileLikelihoodRun.Coordinates, coordinate => Assert.True(coordinate.HasCompleteInterval));
        var kd = restored.Solutions[1].ReportParameters[ParameterType.Affinity1];
        Assert.Equal(center, kd.Value, 14);
        Assert.False(kd.HasConfidenceInterval);
        Assert.True(double.IsNaN(kd.SD));
        stream.Position = 0;
        var viewer = await new ViewerDocumentReader().ReadAsync(stream, "failed-profile.ftxtc", ViewerFileFormat.Ftxtc);
        var dependence = Assert.Single(Assert.Single(viewer.AnalysisResults).TemperatureParameterEvaluation.Dependences,
            item => item.Key == ParameterType.Gibbs1.ToString());
        Assert.NotEmpty(dependence.Contributions);
        Assert.All(dependence.Contributions, contribution =>
        {
            Assert.Null(contribution.Sd);
            Assert.Null(contribution.LowerWidth);
            Assert.Null(contribution.UpperWidth);
        });
    }

    static ProfileCoordinateResult Complete(ParameterType key, double center, string member = null) => new(
        new ProfileCoordinateId(key, member == null ? ParameterBoundaryScope.Shared : ParameterBoundaryScope.Local, member),
        center, center - 10, center + 10,
        new ProfileSideResult(ProfileSideOutcome.EndpointFound, center - 1),
        new ProfileSideResult(ProfileSideOutcome.EndpointFound, center + 1));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedDiagnosticIntervalsCannotMakeAnUpdateUsable(bool globalRun)
    {
        var global = LinkedThermodynamicUncertaintyTests.Create(VariableConstraint.SameForAll,
            affinity: VariableConstraint.None);
        if (globalRun)
            global.ProfileLikelihoodRun = Run(ErrorEstimationOutcome.CompleteFailure,
                Complete(ParameterType.Affinity1, 5));
        else
            foreach (var member in global.Solutions)
                member.ProfileLikelihoodRun = Run(ErrorEstimationOutcome.CompleteFailure,
                    Complete(ParameterType.Affinity1, 5, member.Data.UniqueID));
        var summary = ProfileLikelihoodEstimator.Summarize(global);
        Assert.Equal(ErrorEstimationOutcome.CompleteFailure, summary.Outcome);
        Assert.Equal(globalRun ? 2 : 6, summary.EndpointsFound);
        Assert.False(AnalysisResultUpdater.ShouldReplaceProfileOutcome(true, null, 0,
            summary.Outcome, globalRun ? 1 : 3));
    }

    static ProfileLikelihoodRunResult Run(ErrorEstimationOutcome outcome, params ProfileCoordinateResult[] coordinates) => new(
        .95, ProfileLikelihoodCalibration.UnweightedFCalibratedRss, 30, coordinates.Length, 1, 26, 1, 1,
        SolverAlgorithm.NelderMead, false, 1, 100, 24, 40, TimeSpan.Zero, outcome, coordinates);

    static SolverConvergence Converged() => SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
    { Termination = SolverTermination.Converged });

    static MixedMinimumProbe CreateProbe()
    {
        var data = new ExperimentData("mixed-profile-minimum.itc");
        for (var i = 0; i < 12; i++)
        {
            var injection = new InjectionData(data, i, 1e-6, 1e-9, include: true);
            injection.SetPeakArea(new FloatWithError(i < 6 ? (i % 2 == 0 ? -1 : 1) : 0, 1));
            data.Injections.Add(injection);
        }
        var model = new MixedMinimumProbe(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity2, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1, true);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -10, true);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0, true);
        return model;
    }

    // Independent analytic residuals: S = 6 + 6(x-7)^2 + 6(1+z^2-z^4/2)^2,
    // z=y-6. At (7,6), S=12 and z=0 is a local minimum. Profiling y reaches
    // z=-1.6, where S=6.48121344, after the x interval has completed.
    sealed class MixedMinimumProbe : OneSetOfSites
    {
        public MixedMinimumProbe(ExperimentData data) : base(data) { }
        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            if (injectionindex < 6) return Parameters.Table[ParameterType.Affinity1].Value - 7;
            var z = Parameters.Table[ParameterType.Affinity2].Value - 6;
            return 1 + z * z - .5 * z * z * z * z;
        }
        internal override Model GenerateSyntheticModel(Random random, ModelCloneOptions options)
        {
            var clone = new MixedMinimumProbe(Data.GetSynthClone(options, random));
            SetSynthModelParameters(clone, random, options);
            return clone;
        }
    }
}
