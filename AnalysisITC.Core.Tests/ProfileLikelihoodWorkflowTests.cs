using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Viewer;
using Xunit;

namespace AnalysisITC.Core.Tests;

/// <summary>
/// Small end-to-end acceptance checks for the profile workflow. Lower-level
/// search and persistence cases remain in the dedicated profile test classes;
/// the publication test covers the small leave-one-out envelope workflow.
/// </summary>
[Collection(SolverEventCollectionDefinition.Name)]
public sealed class ProfileLikelihoodWorkflowTests
{
    [Fact]
    public void LocalUnweightedProfileRetainsPrimaryAndRefitsNuisance()
    {
        var model = CreateLinearModel("workflow-local", weighted: false);
        var solver = new Solver
        {
            Model = model,
            SolverAlgorithm = SolverAlgorithm.NelderMead,
            ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood,
            MaxOptimizerIterations = 120, // profile candidate cap is MaxOptimizerIterations / 3
            CanCreateAnalysisResult = false,
        };

        var convergence = solver.Solve();
        var run = model.Solution.ProfileLikelihoodRun;
        Assert.NotNull(run);
        Assert.True(convergence.Success);
        Assert.Equal(4, run.N);
        Assert.Equal(2, run.P);
        Assert.Contains(run.Coordinates, coordinate => coordinate.Id.Parameter == ParameterType.Enthalpy1);
        Assert.Equal(model.Solution.Parameters[ParameterType.Offset].Value, run.Coordinates
            .Single(coordinate => coordinate.Id.Parameter == ParameterType.Offset).BestValue, 12);
        Assert.InRange(model.Solution.Parameters[ParameterType.Offset].Value, -.5, .5);
        var offset = Assert.Single(run.Coordinates, coordinate => coordinate.Id.Parameter == ParameterType.Offset);
        Assert.True(offset.HasCompleteInterval);

        // For y = a + b*x, profiling a while re-fitting b gives
        // RSS(a) = RSS_min + (a-a_hat)^2 / (X'X)^-1_aa.  These quantities
        // are computed independently from the solver output to distinguish
        // the conditional profile from a curve with the primary slope held
        // fixed.
        var x = new[] { 0d, 1d, 2d, 3d };
        var y = new[] { .22, 1.08, 2.18, 2.91 };
        var sx = x.Sum();
        var sy = y.Sum();
        var sxx = x.Sum(value => value * value);
        var sxy = x.Zip(y, (left, right) => left * right).Sum();
        var determinant = x.Length * sxx - sx * sx;
        var intercept = (sxx * sy - sx * sxy) / determinant;
        var slope = (x.Length * sxy - sx * sy) / determinant;
        var rss = x.Zip(y, (left, right) =>
            Math.Pow(right - intercept - slope * left, 2)).Sum();
        const double f95Dof2 = 18.99999999999999;
        var deltaRss = rss * f95Dof2 / 2d;
        var conditionalWidth = Math.Sqrt(deltaRss / (1d / .7));
        var fixedSlopeWidth = Math.Sqrt(deltaRss / x.Length);
        Assert.InRange(offset.Lower.Endpoint, intercept - conditionalWidth - .08,
            intercept - conditionalWidth + .08);
        Assert.InRange(offset.Upper.Endpoint, intercept + conditionalWidth - .08,
            intercept + conditionalWidth + .08);
        Assert.True(conditionalWidth > fixedSlopeWidth * 1.2);
    }

    [Fact]
    public void WeightedProfileUsesIndependentChiSquareCalibration()
    {
        var model = CreateWeightedConstantModel("workflow-weighted");
        var solver = new Solver
        {
            Model = model,
            SolverAlgorithm = SolverAlgorithm.NelderMead,
            ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood,
            UseErrorWeightedFitting = true,
            MaxOptimizerIterations = 90, // profile candidate cap is MaxOptimizerIterations / 3
        };

        var convergence = solver.Solve();
        var run = model.Solution.ProfileLikelihoodRun;
        Assert.NotNull(run);
        Assert.True(convergence.Success);
        Assert.Equal(ProfileLikelihoodCalibration.WeightedChiSquared, run.Calibration);
        // Independent numerical reference for chi-square(1; .95).
        Assert.Equal(3.841458820694124, run.TargetIncrement, 12);
        var coordinate = Assert.Single(run.Coordinates);
        Assert.True(coordinate.Lower.IsEndpointFound);
        Assert.True(coordinate.Upper.IsEndpointFound);
        var expected = .25 * Math.Sqrt(3.841458820694124 / 4d);
        Assert.InRange(coordinate.Lower.Endpoint, -expected - .02, -expected + .02);
        Assert.InRange(coordinate.Upper.Endpoint, expected - .02, expected + .02);
    }

    [Fact]
    public async Task IndependentGlobalSolverProfilesEachMemberAndAggregatesSides()
    {
        var first = CreateConstantModel("workflow-global-first");
        var second = CreateConstantModel("workflow-global-second");
        var global = new GlobalModel(new List<Model> { first, second });
        global.Parameters.AddIndivdualParameter(first.Parameters);
        global.Parameters.AddIndivdualParameter(second.Parameters);
        global.ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions;

        var solver = new GlobalSolver
        {
            Model = global,
            SolverAlgorithm = SolverAlgorithm.NelderMead,
            ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood,
            MaxOptimizerIterations = 90, // profile candidate cap is MaxOptimizerIterations / 3
            CanCreateAnalysisResult = false,
        };

        var finished = new TaskCompletionSource<SolverConvergence>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<SolverConvergence> handler = (_, value) => finished.TrySetResult(value);
        SolverInterface.AnalysisFinished += handler;
        SolverConvergence convergence;
        try
        {
            solver.Analyze();
            convergence = await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch
        {
            SolverInterface.TerminateAnalysisFlag.Raise();
            try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { }
            throw;
        }
        finally
        {
            SolverInterface.AnalysisFinished -= handler;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
        Assert.True(convergence.Success);
        Assert.NotNull(global.Solution);
        Assert.All(global.Solution.Solutions, member => Assert.NotNull(member.ProfileLikelihoodRun));
        var summary = ProfileLikelihoodEstimator.Summarize(global.Solution);
        Assert.Equal(4, summary.TotalSides);
        Assert.Equal(convergence.ErrorEstimationOutcome, summary.Outcome);
        Assert.Contains("endpoints found", convergence.ErrorEstimationSummary);
    }

    [Fact]
    public void SharedGlobalSolverProfilesCompleteObjectiveAndMapsSharedCoordinate()
    {
        var first = CreateConstantModel("workflow-shared-first");
        var second = CreateConstantModel("workflow-shared-second");
        second.Data.MeasuredTemperature = 35;
        var global = new GlobalModel(new List<Model> { first, second });
        global.Parameters.AddIndivdualParameter(first.Parameters);
        global.Parameters.AddIndivdualParameter(second.Parameters);
        global.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
        global.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 0,
            limits: new[] { -10000d, 10000d });
        global.Parameters.SetIndividualFromGlobal();
        global.ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions;

        var solver = new GlobalSolver
        {
            Model = global,
            SolverAlgorithm = SolverAlgorithm.NelderMead,
            ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood,
            MaxOptimizerIterations = 90, // profile candidate cap is MaxOptimizerIterations / 3
        };
        var convergence = solver.Solve();

        var run = global.Solution?.ProfileLikelihoodRun;
        Assert.NotNull(run);
        Assert.True(convergence.Success);
        Assert.Equal(8, run.N);
        Assert.Equal(1, run.P);
        var shared = Assert.Single(run.Coordinates, coordinate =>
            coordinate.Id.Scope == ParameterBoundaryScope.Shared
            && coordinate.Id.Parameter == ParameterType.Offset);
        Assert.Equal(convergence.ErrorEstimationOutcome, run.Outcome);
        Assert.True(shared.HasCompleteInterval);
        var mapped = global.Solution.Solutions[1].Parameters[ParameterType.Offset];
        Assert.Equal(shared.ToFloatWithError(), mapped);
    }

    [Fact]
    public void ProfileValueRemainsSampleableAndExportLabelsEquivalentScale()
    {
        var model = CreateLinearModel("workflow-export", weighted: false);
        var solver = new Solver
        {
            Model = model,
            SolverAlgorithm = SolverAlgorithm.NelderMead,
            ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood,
            MaxOptimizerIterations = 250,
        };
        solver.Solve();
        var global = GlobalSolution.FromSingleExperimentSolver(solver);
        var result = new AnalysisResult(global);
        var value = model.Solution.Parameters[ParameterType.Offset];
        var profile = model.Solution.ProfileLikelihoodRun;
        Assert.NotNull(profile);
        var profiledOffset = Assert.Single(profile.Coordinates,
            coordinate => coordinate.Id.Parameter == ParameterType.Offset);
        Assert.True(profiledOffset.HasCompleteInterval);
        Assert.Equal(profiledOffset.Lower.Endpoint, value.Lower);
        Assert.Equal(profiledOffset.Upper.Endpoint, value.Upper);

        Assert.True(double.IsFinite(value.Sample(new Random(31))));
        Assert.True(double.IsFinite(Distribution.Normal(value, new Random(32))));
        Assert.True(double.IsFinite(FWEMath.Multiply(value, new FloatWithError(2, .1)).Value));
        var sampledDistribution = new FloatWithError(new List<FloatWithError> { value, value });
        Assert.True(double.IsFinite(sampledDistribution.Sample(new Random(33))));
        var export = AnalysisResultTableExporter.Build(new[] { result }, new AnalysisResultExportOptions());
        Assert.Contains("equivalent", export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bootstrap", export, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FtxtcProfileRoundTripProjectsViewerDiagnosticsWithoutBootstrapArtifacts()
    {
        using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "one-set.ftitc"));
        var containers = await FTITCReader.ReadStream(source);
        var result = Assert.Single(containers.OfType<AnalysisResult>());
        var member = result.Solution.Solutions[0];
        var parameter = member.Parameters.Keys.First();
        var coordinate = new ProfileCoordinateResult(
            new ProfileCoordinateId(parameter, ParameterBoundaryScope.Local, member.Data.UniqueID, 0),
            member.Parameters[parameter].Value, -100, 100,
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, -10),
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, 10));
        var profile = new ProfileLikelihoodRunResult(.95,
            ProfileLikelihoodCalibration.UnweightedFCalibratedRss, 20, 1, 1, 19,
            12, 1.25, SolverAlgorithm.NelderMead, false, 2, 30, 24, 40,
            TimeSpan.FromSeconds(1.5), ErrorEstimationOutcome.Completed,
            new[] { coordinate }, 4);
        member.ProfileLikelihoodRun = profile;
        member.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        member.Parameters[parameter] = coordinate.ToFloatWithError();
        var experiment = containers.OfType<ExperimentData>().Single(item => item.UniqueID == member.Data.UniqueID);
        experiment.Solution.ProfileLikelihoodRun = profile;
        experiment.Solution.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        experiment.Solution.Parameters[parameter] = coordinate.ToFloatWithError();
        result.Model.ModelCloneOptions.ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood;

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, containers.OfType<ExperimentData>(), new[] { result });
        var packageBytes = package.ToArray();
        using var restoredPackage = new MemoryStream(packageBytes);
        var restored = Assert.Single((await FTXTCReader.ReadStream(restoredPackage)).OfType<AnalysisResult>());
        var restoredMember = restored.Solution.Solutions[0];
        Assert.Equal(-10, restoredMember.Parameters[parameter].Lower);
        Assert.Equal(10, restoredMember.Parameters[parameter].Upper);
        Assert.Equal(1.25, restoredMember.ProfileLikelihoodRun.TargetIncrement);
        Assert.Equal(SolverAlgorithm.NelderMead, restoredMember.ProfileLikelihoodRun.Algorithm);
        Assert.False(restoredMember.ProfileLikelihoodRun.UseWeightedFitting);
        Assert.Equal(2, restoredMember.ProfileLikelihoodRun.Tolerance);
        Assert.Equal(30, restoredMember.ProfileLikelihoodRun.CandidateIterationLimit);
        Assert.Equal(24, restoredMember.ProfileLikelihoodRun.ExpansionLimit);
        Assert.Equal(40, restoredMember.ProfileLikelihoodRun.RefinementLimit);
        var export = AnalysisResultTableExporter.Build(new[] { restored }, new AnalysisResultExportOptions());
        Assert.Contains("equivalent", export, StringComparison.OrdinalIgnoreCase);

        using var viewerPackage = new MemoryStream(packageBytes);
        var document = await new ViewerDocumentReader().ReadAsync(viewerPackage, "profile.ftxtc", ViewerFileFormat.Ftxtc);
        var viewerResult = Assert.Single(document.AnalysisResults);
        Assert.Equal("Profile likelihood", viewerResult.Solver.ErrorEstimationMethod);
        // Only one member was given a local profile record in this projected
        // multi-member result, so the aggregate is correctly partial.
        Assert.Equal("PartialFailure", viewerResult.Solver.ProfileOutcome);
        Assert.Equal(2, viewerResult.Solver.ProfileEndpointsFound);
        var expectedSideCount = result.Solution.Solutions.Sum(item =>
            item.Model.NumberOfParameters * 2);
        Assert.Equal(expectedSideCount, viewerResult.Solver.ProfileSideCount);
        Assert.Equal(1.5, viewerResult.Solver.ProfileElapsedSeconds);
        Assert.Equal(0, viewerResult.Solver.BootstrapIterations);
        Assert.NotEmpty(viewerResult.CorrelationViews);
        Assert.All(viewerResult.CorrelationViews, view =>
        {
            Assert.False(view.IsAvailable);
            Assert.Null(view.CorrelationMatrix);
            Assert.Contains("ResidualBootstrap", view.AvailabilityStatus,
                StringComparison.OrdinalIgnoreCase);
        });
        // The first saved member maps to the first fixture experiment in the
        // viewer projection (viewer keys are ordinal, not persisted UUIDs).
        Assert.NotEmpty(document.Experiments[0].Fits);
        Assert.All(document.Experiments[0].Fits, fit =>
        {
            Assert.All(fit.ConfidenceLowerKilojoulesPerMole, value => Assert.Null(value));
            Assert.All(fit.ConfidenceUpperKilojoulesPerMole, value => Assert.Null(value));
        });
        Assert.Contains("endpoints found", viewerResult.Solver.ErrorEstimationSummary,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AnalysisModel.OneSetOfSites)]
    [InlineData(AnalysisModel.TwoSetsOfSites)]
    [InlineData(AnalysisModel.SequentialBindingSites)]
    [InlineData(AnalysisModel.CompetitiveBinding)]
    [InlineData(AnalysisModel.Dissociation)]
    public void SupportedBindingModelsCloneAndReportWithProfileOptions(AnalysisModel modelType)
    {
        var data = CreateBindingSmokeData(modelType.ToString());
        Model model = modelType switch
        {
            AnalysisModel.OneSetOfSites => new OneSetOfSites(data),
            AnalysisModel.TwoSetsOfSites => new TwoSetsOfSites(data),
            AnalysisModel.SequentialBindingSites => new SequentialBindingSites(data),
            AnalysisModel.CompetitiveBinding => new CompetitiveBinding(data),
            AnalysisModel.Dissociation => new Dissociation(data),
            _ => throw new ArgumentOutOfRangeException(nameof(modelType)),
        };
        model.InitializeParameters(data);
        var sourceInjection = data.Injections[1];
        sourceInjection.SetPeakArea(new FloatWithError(5, 2, 3, 8));
        sourceInjection.Include = false;
        var options = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood };
        options.IncludeConcentrationErrorsInBootstrap = true;
        options.UnlockBootstrapParameters = true;

        var clone = model.GenerateSyntheticModel(new Random(17), options);
        Assert.Equal(modelType, clone.ModelType);
        var clonedInjection = clone.Data.Injections[1];
        Assert.Equal(sourceInjection.Include, clonedInjection.Include);
        Assert.Equal(sourceInjection.PeakArea.Value, clonedInjection.PeakArea.Value);
        Assert.Equal(sourceInjection.PeakArea.SD, clonedInjection.PeakArea.SD);
        Assert.Equal(sourceInjection.PeakArea.Lower, clonedInjection.PeakArea.Lower);
        Assert.Equal(sourceInjection.PeakArea.Upper, clonedInjection.PeakArea.Upper);
        Assert.Equal(model.Data.UniqueID, clone.Data.UniqueID);
        Assert.Equal(model.Data.Injections.Select(item => item.ID),
            clone.Data.Injections.Select(item => item.ID));
        Assert.Equal(model.ModelOptions.Keys, clone.ModelOptions.Keys);
        foreach (var key in model.ModelOptions.Keys)
        {
            var sourceOption = model.ModelOptions[key];
            var clonedOption = clone.ModelOptions[key];
            Assert.Equal(sourceOption.BoolValue, clonedOption.BoolValue);
            Assert.Equal(sourceOption.IntValue, clonedOption.IntValue);
            Assert.Equal(sourceOption.DoubleValue, clonedOption.DoubleValue);
            Assert.Equal(sourceOption.StringValue, clonedOption.StringValue);
            Assert.Equal(sourceOption.ParameterValue.Value, clonedOption.ParameterValue.Value);
        }
        foreach (var (key, sourceParameter) in model.Parameters.Table)
        {
            var clonedParameter = clone.Parameters.Table[key];
            Assert.Equal(sourceParameter.IsLocked, clonedParameter.IsLocked);
            Assert.Equal(sourceParameter.Limits, clonedParameter.Limits);
        }
        Assert.False(options.EffectiveIncludeConcentrationErrors);
        Assert.False(options.EffectiveUnlockBootstrapParameters);
        Assert.False(options.EffectiveSampleModelOptionParameters);
        var solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        solution.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        Assert.NotEmpty(solution.ReportParameters);
    }

    [Fact]
    public void PartialUpdaterMatrixRetainsOnlyOlderOrInvalidPolicies()
    {
        Assert.True(AnalysisResultUpdater.ShouldReplacePartialProfile(true,
            ErrorEstimationOutcome.CompleteFailure, 0, 1));
        Assert.True(AnalysisResultUpdater.ShouldReplacePartialProfile(false,
            ErrorEstimationOutcome.PartialFailure, 2, 2));
        Assert.False(AnalysisResultUpdater.ShouldReplacePartialProfile(false,
            ErrorEstimationOutcome.PartialFailure, 2, 1));
        Assert.False(AnalysisResultUpdater.ShouldReplacePartialProfile(false,
            ErrorEstimationOutcome.Completed, 2, 3));
    }

    [Fact]
    public void UpdaterOutcomeMatrixInstallsOnlyValidProfileOutcomes()
    {
        Assert.True(AnalysisResultUpdater.ShouldReplaceProfileOutcome(
            existingInvalid: false, ErrorEstimationOutcome.Completed, 3,
            ErrorEstimationOutcome.Completed, 3));
        Assert.False(AnalysisResultUpdater.ShouldReplaceProfileOutcome(
            existingInvalid: false, ErrorEstimationOutcome.Completed, 3,
            ErrorEstimationOutcome.Cancelled, 3));
        Assert.False(AnalysisResultUpdater.ShouldReplaceProfileOutcome(
            existingInvalid: false, ErrorEstimationOutcome.Completed, 3,
            ErrorEstimationOutcome.CompleteFailure, 0));
        Assert.False(AnalysisResultUpdater.ShouldReplaceProfileOutcome(
            existingInvalid: false, ErrorEstimationOutcome.Completed, 3,
            ErrorEstimationOutcome.NotRun, 0));
        Assert.False(AnalysisResultUpdater.ShouldReplaceProfileOutcome(
            existingInvalid: false, previousOutcome: null, previousCompleteCount: 0,
            ErrorEstimationOutcome.PartialFailure, candidateCompleteCount: 1));
        Assert.True(AnalysisResultUpdater.ShouldReplaceProfileOutcome(
            existingInvalid: true, ErrorEstimationOutcome.Completed, 3,
            ErrorEstimationOutcome.PartialFailure, 1));
        Assert.True(AnalysisResultUpdater.ShouldReplaceProfileOutcome(
            existingInvalid: false, ErrorEstimationOutcome.PartialFailure, 1,
            ErrorEstimationOutcome.PartialFailure, 1));
        Assert.False(AnalysisResultUpdater.ShouldReplaceProfileOutcome(
            existingInvalid: false, ErrorEstimationOutcome.PartialFailure, 2,
            ErrorEstimationOutcome.PartialFailure, 1));
    }

    static WorkflowLinearModel CreateLinearModel(string id, bool weighted)
    {
        var data = new ExperimentData(id)
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(1),
            CellVolume = 1,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        var observations = new[] { .22, 1.08, 2.18, 2.91 };
        for (var i = 0; i < observations.Length; i++)
        {
            var injection = new InjectionData(data, i, 1, 1, true)
            {
                ActualCellConcentration = 10e-6,
                ActualTitrantConcentration = i,
            };
            injection.SetPeakArea(new FloatWithError(observations[i], weighted ? .25 : 1));
            data.Injections.Add(injection);
        }

        var model = new WorkflowLinearModel(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, 0);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7);
        model.Parameters.Table[ParameterType.Nvalue1].SetValue(1, true);
        model.Parameters.Table[ParameterType.Affinity1].SetValue(7, true);
        // Keep the real two-coordinate fit bounded tightly enough that the
        // deterministic profile search reaches its crossings promptly.
        model.Parameters.Table[ParameterType.Offset].SetLimits(new[] { -5d, 5d });
        model.Parameters.Table[ParameterType.Enthalpy1].SetLimits(new[] { -5d, 5d });
        model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood };
        return model;
    }

    static WorkflowConstantModel CreateWeightedConstantModel(string id)
    {
        var model = CreateConstantModel(id);
        foreach (var (index, injection) in model.Data.Injections.Select((value, i) => (i, value)))
            injection.SetPeakArea(new FloatWithError(index % 2 == 0 ? -1 : 1, .25));
        return model;
    }

    static ExperimentData CreateBindingSmokeData(string id)
    {
        var data = new ExperimentData(id)
        {
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        for (var index = 0; index < 6; index++)
        {
            var volume = index == 0 ? .5e-6 : 2e-6;
            var titrant = 2.5e-6 * (index + 1);
            var cell = 35e-6 * Math.Pow(.9986, index + 1);
            var injection = new InjectionData(data, index, volume,
                data.SyringeConcentration * volume, include: index != 0)
            {
                ActualCellConcentration = cell,
                ActualTitrantConcentration = titrant,
                Ratio = titrant / cell,
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 4e-8, 1e-8));
            data.Injections.Add(injection);
        }
        return data;
    }

    static WorkflowConstantModel CreateConstantModel(string id)
    {
        var data = new ExperimentData(id)
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(1),
            CellVolume = 1,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        foreach (var (index, observation) in new[] { (0, -1000d), (1, 1000d), (2, -1000d), (3, 1000d) })
        {
            var injection = new InjectionData(data, index, 1, 1, true)
            {
                ActualCellConcentration = 10e-6,
                ActualTitrantConcentration = index,
            };
            injection.SetPeakArea(new FloatWithError(observation, 1));
            data.Injections.Add(injection);
        }
        var model = new WorkflowConstantModel(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -10);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7);
        model.Parameters.Table[ParameterType.Nvalue1].SetValue(1, true);
        model.Parameters.Table[ParameterType.Enthalpy1].SetValue(-10, true);
        model.Parameters.Table[ParameterType.Affinity1].SetValue(7, true);
        model.Parameters.Table[ParameterType.Offset].SetLimits(new[] { -10000d, 10000d });
        return model;
    }

    sealed class WorkflowConstantModel : Model
    {
        public WorkflowConstantModel(ExperimentData data) : base(data) { }

        public override double Evaluate(int injectionindex, bool withoffset = true)
            => Parameters.Table[ParameterType.Offset].Value;

        internal override Model GenerateSyntheticModel(Random random, ModelCloneOptions options)
        {
            var clone = new WorkflowConstantModel(Data.GetSynthClone(options, random));
            SetSynthModelParameters(clone, random, options);
            return clone;
        }
    }

    sealed class WorkflowLinearModel : Model
    {
        public WorkflowLinearModel(ExperimentData data) : base(data) { }

        public override double Evaluate(int injectionindex, bool withoffset = true)
            => Parameters.Table[ParameterType.Offset].Value
                + Parameters.Table[ParameterType.Enthalpy1].Value * injectionindex;

        internal override Model GenerateSyntheticModel(Random random, ModelCloneOptions options)
        {
            var clone = new WorkflowLinearModel(Data.GetSynthClone(options, random));
            SetSynthModelParameters(clone, random, options);
            return clone;
        }
    }
}
