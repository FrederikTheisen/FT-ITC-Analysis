using System;
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

using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(AnalysisResultUpdaterCollectionDefinition.Name)]
public sealed class AnalysisResultUpdaterTests : IDisposable
{
    readonly int previousBootstrapIterations = FittingOptionsController.BootstrapIterations;

    public AnalysisResultUpdaterTests()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        GlobalModelFactory.ClearPreviousParameters();
        FittingOptionsController.BootstrapIterations = 100;
    }

    public void Dispose()
    {
        FittingOptionsController.BootstrapIterations = previousBootstrapIterations;
        GlobalModelFactory.ClearPreviousParameters();
        DataManager.Clear(DataClearMode.ResetSession);
    }

    [Fact]
    public void BootstrapIterationPresetsAreSharedAndStable()
    {
        Assert.Equal(
            new[] { 10, 50, 100, 200, 500, 1_000, 2_000, 5_000, 10_000 },
            FittingOptionsController.BootstrapIterationPresets);
    }

    [Fact]
    public void StoredSettingsPreserveSolverAndBootstrapConfiguration()
    {
        var result = CreateResult(ErrorEstimationMethod.BootstrapResiduals, retainedBootstrapCount: 50);
        var sourceParameter = result.Solution.Model.Models[0].Parameters.Table[ParameterType.Affinity1];
        sourceParameter.SetLimits(new[] { 2.5, 8.5 });

        var solver = Assert.IsType<GlobalSolver>(AnalysisResultUpdater.PrepareSolver(result));
        var targetParameter = solver.Model.Models[0].Parameters.Table[ParameterType.Affinity1];

        Assert.Equal(50, solver.BootstrapIterations);
        Assert.Equal(SolverAlgorithm.LevenbergMarquardt, solver.SolverAlgorithm);
        Assert.True(solver.UseErrorWeightedFitting);
        Assert.Equal(ErrorEstimationMethod.BootstrapResiduals, solver.ErrorEstimationMethod);
        Assert.Equal(new[] { 2.5, 8.5 }, targetParameter.Limits);
        Assert.True(solver.Model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap);
        Assert.True(solver.Model.ModelCloneOptions.EnableAutoConcentrationVariance);
        Assert.Equal(0.075, solver.Model.ModelCloneOptions.AutoConcentrationVariance, 12);
        Assert.True(solver.Model.ModelCloneOptions.UnlockBootstrapParameters);
    }

    [Fact]
    public async Task LegacyCombinedLeaveOneOutRoundTripsAndRerunPolicyNormalizesTheNewModel()
    {
        var result = CreateResult(ErrorEstimationMethod.LeaveOneOut, retainedBootstrapCount: 0);
        var experiments = result.Solution.Solutions.Select(solution => solution.Data).ToList();

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, experiments, new[] { result });
        package.Position = 0;
        var containers = await FTXTCReader.ReadStream(package);
        var restored = Assert.Single(containers.OfType<AnalysisResult>());

        Assert.True(restored.Solution.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap);
        Assert.True(restored.Solution.ModelCloneOptions.EnableAutoConcentrationVariance);
        Assert.True(restored.Solution.ModelCloneOptions.UnlockBootstrapParameters);
        Assert.False(restored.Solution.ModelCloneOptions.EffectiveIncludeConcentrationErrors);
        Assert.False(restored.Solution.ModelCloneOptions.EffectiveUnlockBootstrapParameters);
        Assert.True(restored.Solution.ModelCloneOptions.HasLegacyCombinedLeaveOneOut);

        DataManager.Clear(DataClearMode.ResetSession);
        foreach (var experiment in containers.OfType<ExperimentData>())
            DataManager.AddData(experiment);

        var solver = Assert.IsType<GlobalSolver>(AnalysisResultUpdater.PrepareSolver(restored));
        Assert.True(solver.Model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap);
        Assert.True(solver.Model.ModelCloneOptions.UnlockBootstrapParameters);
        Assert.False(solver.Model.ModelCloneOptions.EffectiveIncludeConcentrationErrors);
        Assert.False(solver.Model.ModelCloneOptions.EffectiveUnlockBootstrapParameters);
        var liveConcentrationPreference = FittingOptionsController.IncludeConcentrationVariance;
        var liveUnlockPreference = FittingOptionsController.UnlockBootstrapParameters;

        solver.ApplyErrorEstimationPolicy();

        Assert.Equal(ErrorEstimationMethod.LeaveOneOut,
            solver.Model.ModelCloneOptions.ErrorEstimationMethod);
        Assert.False(solver.Model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap);
        Assert.True(solver.Model.ModelCloneOptions.EnableAutoConcentrationVariance);
        Assert.False(solver.Model.ModelCloneOptions.UnlockBootstrapParameters);
        Assert.False(solver.Model.ModelCloneOptions.EffectiveIncludeConcentrationErrors);
        Assert.False(solver.Model.ModelCloneOptions.EffectiveUnlockBootstrapParameters);
        Assert.False(solver.Model.ModelCloneOptions.HasLegacyCombinedLeaveOneOut);
        Assert.All(solver.Model.Models, member =>
        {
            Assert.Equal(ErrorEstimationMethod.LeaveOneOut,
                member.ModelCloneOptions.ErrorEstimationMethod);
            Assert.False(member.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap);
            Assert.True(member.ModelCloneOptions.EnableAutoConcentrationVariance);
            Assert.False(member.ModelCloneOptions.UnlockBootstrapParameters);
            Assert.False(member.ModelCloneOptions.EffectiveIncludeConcentrationErrors);
            Assert.False(member.ModelCloneOptions.EffectiveUnlockBootstrapParameters);
        });

        Assert.True(restored.Solution.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap);
        Assert.True(restored.Solution.ModelCloneOptions.UnlockBootstrapParameters);
        Assert.Equal(liveConcentrationPreference,
            FittingOptionsController.IncludeConcentrationVariance);
        Assert.Equal(liveUnlockPreference,
            FittingOptionsController.UnlockBootstrapParameters);
    }

    [Fact]
    public void LargerPresetOverridesOnlyBootstrapIterations()
    {
        var result = CreateResult(ErrorEstimationMethod.BootstrapResiduals, retainedBootstrapCount: 50);
        var stored = AnalysisResultUpdater.PrepareSolver(result);
        var overridden = AnalysisResultUpdater.PrepareSolver(
            result,
            new AnalysisResultUpdateOptions(500));

        Assert.Equal(50, stored.BootstrapIterations);
        Assert.Equal(500, overridden.BootstrapIterations);
        Assert.Equal(stored.SolverAlgorithm, overridden.SolverAlgorithm);
        Assert.Equal(stored.ErrorEstimationMethod, overridden.ErrorEstimationMethod);
        Assert.Equal(stored.UseErrorWeightedFitting, overridden.UseErrorWeightedFitting);
    }

    [Fact]
    public async Task LargerPresetOverrideAlsoAppliesToGlobalResults()
    {
        using var source = File.OpenRead(Fixture("jors.ftxtc"));
        var containers = await FTXTCReader.ReadStream(source);
        var result = containers.OfType<AnalysisResult>()
            .First(candidate => candidate.Solution.Solutions.Count > 1);
        foreach (var experiment in containers.OfType<ExperimentData>())
            DataManager.AddData(experiment);

        var requested = AnalysisResultUpdater.GetLargerBootstrapIterationPresets(result).Last();
        var stored = Assert.IsType<GlobalSolver>(AnalysisResultUpdater.PrepareSolver(result));
        var overridden = Assert.IsType<GlobalSolver>(AnalysisResultUpdater.PrepareSolver(
            result,
            new AnalysisResultUpdateOptions(requested)));

        Assert.Equal(result.Solution.BootstrapIterations, stored.BootstrapIterations);
        Assert.Equal(requested, overridden.BootstrapIterations);
        Assert.Equal(stored.Model.Models.Count, overridden.Model.Models.Count);
        Assert.Equal(stored.SolverAlgorithm, overridden.SolverAlgorithm);
        Assert.Equal(stored.UseErrorWeightedFitting, overridden.UseErrorWeightedFitting);
    }

    [Fact]
    public void LargerPresetChoicesExcludeStoredAndSmallerCounts()
    {
        var result = CreateResult(ErrorEstimationMethod.BootstrapResiduals, retainedBootstrapCount: 200);

        Assert.Equal(
            new[] { 500, 1_000, 2_000, 5_000, 10_000 },
            AnalysisResultUpdater.GetLargerBootstrapIterationPresets(result));
    }

    [Fact]
    public void OverrideRequiresResidualBootstrapSupportedPresetAndLargerCount()
    {
        var bootstrap = CreateResult(ErrorEstimationMethod.BootstrapResiduals, retainedBootstrapCount: 50);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnalysisResultUpdater.PrepareSolver(bootstrap, new AnalysisResultUpdateOptions(50)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnalysisResultUpdater.PrepareSolver(bootstrap, new AnalysisResultUpdateOptions(75)));

        DataManager.Clear(DataClearMode.ResetSession);
        GlobalModelFactory.ClearPreviousParameters();
        var noErrors = CreateResult(ErrorEstimationMethod.None, retainedBootstrapCount: 0);
        Assert.False(AnalysisResultUpdater.CanOverrideBootstrapIterations(noErrors));
        Assert.Throws<InvalidOperationException>(() =>
            AnalysisResultUpdater.PrepareSolver(noErrors, new AnalysisResultUpdateOptions(200)));
    }

    [Fact]
    public void CancelledAndEmptyBootstrapUpdatesCannotReplaceStoredResult()
    {
        var result = CreateResult(ErrorEstimationMethod.BootstrapResiduals, retainedBootstrapCount: 0);
        var solver = AnalysisResultUpdater.PrepareSolver(result);

        var cancelled = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
        cancelled.ApplyErrorEstimationResult(
            ErrorEstimationMethod.BootstrapResiduals,
            failures: 1,
            succeeded: 2,
            TimeSpan.FromSeconds(1),
            cancelled: true,
            requested: 100);
        Assert.Equal(ErrorEstimationOutcome.Cancelled, cancelled.ErrorEstimationOutcome);
        Assert.Contains("requested=100", cancelled.ErrorEstimationSummary);
        Assert.Throws<InvalidOperationException>(() =>
            AnalysisResultUpdater.EnsureUpdateCanReplaceResult(solver, cancelled, result.Solution));

        var failed = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
        failed.ApplyErrorEstimationResult(
            ErrorEstimationMethod.BootstrapResiduals,
            failures: 100,
            succeeded: 0,
            TimeSpan.FromSeconds(1));
        Assert.Equal(ErrorEstimationOutcome.CompleteFailure, failed.ErrorEstimationOutcome);
        Assert.Throws<InvalidOperationException>(() =>
            AnalysisResultUpdater.EnsureUpdateCanReplaceResult(solver, failed, result.Solution));
    }

    [Fact]
    public void CompletedPartialBootstrapWithUsableRefitCanReplaceStoredResult()
    {
        var result = CreateResult(ErrorEstimationMethod.BootstrapResiduals, retainedBootstrapCount: 0);
        var solver = AnalysisResultUpdater.PrepareSolver(result);
        result.Solution.BootstrapSolutions.Add(result.Solution);

        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
        convergence.ApplyErrorEstimationResult(
            ErrorEstimationMethod.BootstrapResiduals,
            failures: 1,
            succeeded: 1,
            TimeSpan.FromSeconds(1));

        Assert.Equal(ErrorEstimationOutcome.PartialFailure, convergence.ErrorEstimationOutcome);
        AnalysisResultUpdater.EnsureUpdateCanReplaceResult(solver, convergence, result.Solution);
    }

    [Fact]
    public async Task SuccessfulRerunReplacesTheStoredSolution()
    {
        using var source = File.OpenRead(Fixture("one-set.ftitc"));
        var containers = await FTITCReader.ReadStream(source);
        var result = Assert.Single(containers.OfType<AnalysisResult>());
        foreach (var experiment in containers.OfType<ExperimentData>())
            DataManager.AddData(experiment);
        result.Solution.Model.ModelCloneOptions.ErrorEstimationMethod = ErrorEstimationMethod.None;
        foreach (var member in result.Solution.Solutions)
            member.ErrorMethod = ErrorEstimationMethod.None;
        var original = result.Solution;

        var convergence = await AnalysisResultUpdater.UpdateAsync(result);

        Assert.NotSame(original, result.Solution);
        Assert.Same(convergence, result.Solution.Convergence);
        Assert.False(convergence.Failed);
        Assert.False(convergence.Stopped);
    }

    [Fact]
    public async Task PrimaryFitFailurePreservesTheStoredSolution()
    {
        using var source = File.OpenRead(Fixture("one-set.ftitc"));
        var containers = await FTITCReader.ReadStream(source);
        var result = Assert.Single(containers.OfType<AnalysisResult>());
        foreach (var experiment in containers.OfType<ExperimentData>())
        {
            foreach (var injection in experiment.Injections.Where(injection => injection.Include))
                injection.SetPeakArea(new FloatWithError(double.NaN));
            DataManager.AddData(experiment);
        }
        result.Solution.Model.ModelCloneOptions.ErrorEstimationMethod = ErrorEstimationMethod.None;
        foreach (var member in result.Solution.Solutions)
            member.ErrorMethod = ErrorEstimationMethod.None;
        var original = result.Solution;

        await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisResultUpdater.UpdateAsync(result));

        Assert.Same(original, result.Solution);
    }

    [Fact]
    public async Task UpdatedResultRoundTripsBootstrapReplicatesAndDiagnostics()
    {
        using var source = File.OpenRead(Fixture("one-set.ftitc"));
        var containers = await FTITCReader.ReadStream(source);
        var experiments = containers.OfType<ExperimentData>().ToList();
        var result = Assert.Single(containers.OfType<AnalysisResult>());
        var retained = result.Solution.BootstrapIterations;

        result.Solution.Convergence.ApplyErrorEstimationResult(
            ErrorEstimationMethod.BootstrapResiduals,
            failures: 3,
            succeeded: retained,
            TimeSpan.FromSeconds(2),
            limitTerminated: 2);
        result.UpdateSolution(result.Solution);

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, experiments, new[] { result });
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());

        Assert.Equal(retained, restored.Solution.BootstrapIterations);
        Assert.Equal(ErrorEstimationOutcome.PartialFailure, restored.Solution.Convergence.ErrorEstimationOutcome);
        Assert.Equal(2, restored.Solution.Convergence.ErrorEstimationLimitTerminations);
        Assert.Equal(result.Solution.Convergence.ErrorEstimationSummary,
            restored.Solution.Convergence.ErrorEstimationSummary);
    }

    static AnalysisResult CreateResult(ErrorEstimationMethod method, int retainedBootstrapCount)
    {
        var experiment = CreateExperiment();
        DataManager.AddData(experiment);

        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25_000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        model.ModelCloneOptions = new ModelCloneOptions
        {
            ErrorEstimationMethod = method,
            IncludeConcentrationErrorsInBootstrap = true,
            EnableAutoConcentrationVariance = true,
            AutoConcentrationVariance = 0.075,
            UnlockBootstrapParameters = true,
        };
        model.Solution = SolutionInterface.FromModel(
            model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Algorithm = SolverAlgorithm.LevenbergMarquardt,
            }));
        model.Solution.ErrorMethod = method;
        model.Solution.UseWeightedFitting = true;

        var solver = new Solver
        {
            Model = model,
            SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
            ErrorEstimationMethod = method,
            UseErrorWeightedFitting = true,
        };
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(solver));
        for (var index = 0; index < retainedBootstrapCount; index++)
            result.Solution.BootstrapSolutions.Add(result.Solution);
        return result;
    }

    static ExperimentData CreateExperiment()
    {
        var experiment = new ExperimentData("result-update-options.itc")
        {
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };

        for (var index = 0; index < 5; index++)
        {
            var injection = new InjectionData(
                experiment,
                index,
                2e-6,
                experiment.SyringeConcentration * 2e-6,
                include: true)
            {
                ActualCellConcentration = experiment.CellConcentration,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = index + 1,
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7, 1e-8));
            experiment.Injections.Add(injection);
        }

        return experiment;
    }

    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AnalysisResultUpdaterCollectionDefinition
{
    public const string Name = "Analysis result updater";
}
