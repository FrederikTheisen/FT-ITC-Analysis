using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPrepareRestoresPriorModelsAndResultStateAcrossRepeatedAttempts(bool dirty)
    {
        var (result, experiments) = await LoadCompetitiveResult();
        Assert.True(experiments.Count >= 2);
        var differentFit = CreateDifferentAttachedFit(experiments[0]);
        experiments[0].Model = differentFit;
        experiments[1].Model = null;

        result.Solution.Model.ModelOptions[AttributeKey.PreboundLigandConc].BoolValue = true;
        foreach (var experiment in experiments)
            experiment.Attributes.RemoveAll(attribute => attribute.Key == AttributeKey.PreboundLigandConc);

        var priorModels = experiments.Select(experiment => experiment.Model).ToArray();
        var priorSolutions = experiments.Select(experiment => experiment.Solution).ToArray();
        var notifications = new int[experiments.Count];
        for (var index = 0; index < experiments.Count; index++)
        {
            var capturedIndex = index;
            experiments[index].SolutionChanged += (_, _) => notifications[capturedIndex]++;
        }

        DocumentDirtyTracker.Initialize();
        DocumentDirtyTracker.MarkClean();
        if (dirty) DocumentDirtyTracker.MarkDirty();
        var state = CaptureResultState(result);
        var saveStamp = DocumentDirtyTracker.CaptureSaveStamp();
        var dirtyBefore = DocumentDirtyTracker.IsDirty;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var exception = Assert.Throws<MissingModelOptionAttributesException>(
                () => AnalysisResultUpdater.PrepareSolver(result));
            Assert.Contains(AttributeKey.PreboundLigandConc.GetProperties().Name, exception.Message);
            AssertPriorPrepareState(result, state, experiments, priorModels, priorSolutions, notifications,
                saveStamp, dirtyBefore);
        }
    }

    [Fact]
    public async Task EarlyOptionRejectionDoesNotChangeAttachedModelsOrStoredMetadata()
    {
        var (result, experiments) = await LoadCompetitiveResult();
        var priorModels = experiments.Select(experiment => experiment.Model).ToArray();
        var priorSolutions = experiments.Select(experiment => experiment.Solution).ToArray();
        var notifications = SubscribeToSolutionChanges(experiments);
        DocumentDirtyTracker.MarkClean();
        var state = CaptureResultState(result);
        var saveStamp = DocumentDirtyTracker.CaptureSaveStamp();

        if (AnalysisResultUpdater.CanOverrideBootstrapIterations(result))
        {
            var storedCount = AnalysisResultUpdater.GetEffectiveBootstrapIterations(result);
            Assert.Throws<ArgumentOutOfRangeException>(() => AnalysisResultUpdater.PrepareSolver(
                result, new AnalysisResultUpdateOptions(storedCount)));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => AnalysisResultUpdater.PrepareSolver(
                result, new AnalysisResultUpdateOptions(200)));
        }

        AssertPriorPrepareState(result, state, experiments, priorModels, priorSolutions, notifications,
            saveStamp, expectedDirty: false);
    }

    [Fact]
    public async Task InitialParameterLimitFailureRestoresAttachedModelsAndResultState()
    {
        var previousLimitSetting = AppSettings.ParameterLimitSetting;
        try
        {
            AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
            var (result, experiments) = await LoadCompetitiveResult();
            result.Solution.Model.Models[0].Parameters.Table[ParameterType.Offset].Update(50001);
            var priorModels = experiments.Select(experiment => experiment.Model).ToArray();
            var priorSolutions = experiments.Select(experiment => experiment.Solution).ToArray();
            var notifications = SubscribeToSolutionChanges(experiments);
            DocumentDirtyTracker.MarkClean();
            var state = CaptureResultState(result);
            var saveStamp = DocumentDirtyTracker.CaptureSaveStamp();

            Assert.Throws<InitialParameterLimitException>(() => AnalysisResultUpdater.PrepareSolver(result));

            AssertPriorPrepareState(result, state, experiments, priorModels, priorSolutions, notifications,
                saveStamp, expectedDirty: false);
        }
        finally
        {
            AppSettings.ParameterLimitSetting = previousLimitSetting;
        }
    }

    [Fact]
    public async Task SuccessfulPrepareKeepsCandidateAttachmentsAndModelOptionConfiguration()
    {
        var (result, experiments) = await LoadCompetitiveResult();
        result.Solution.Model.ModelOptions[AttributeKey.PreboundLigandConc].BoolValue = true;
        result.Solution.Model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap = true;
        result.Solution.Model.ModelCloneOptions.EnableAutoConcentrationVariance = true;
        result.Solution.Model.ModelCloneOptions.AutoConcentrationVariance = 0.075;
        result.Solution.Model.ModelCloneOptions.UnlockBootstrapParameters = true;
        var expectedCloneOptions = result.Solution.Model.ModelCloneOptions;
        var originalModels = experiments.Select(experiment => experiment.Model).ToArray();
        DocumentDirtyTracker.MarkClean();

        var solver = Assert.IsType<GlobalSolver>(AnalysisResultUpdater.PrepareSolver(result));

        Assert.Equal(experiments.Count, solver.Model.Models.Count);
        for (var index = 0; index < experiments.Count; index++)
        {
            var experiment = experiments[index];
            var candidate = solver.Model.Models.Single(model => model.Data == experiment);
            Assert.NotSame(originalModels[index], candidate);
            Assert.Same(candidate, experiment.Model);
            Assert.True(candidate.ModelOptions[AttributeKey.PreboundLigandConc].BoolValue);
            Assert.Equal(
                experiment.Attributes.Single(attribute => attribute.Key == AttributeKey.PreboundLigandConc).ParameterValue.Value,
                candidate.ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue.Value,
                12);
        }

        Assert.True(solver.Model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap);
        Assert.True(solver.Model.ModelCloneOptions.EnableAutoConcentrationVariance);
        Assert.Equal(expectedCloneOptions.AutoConcentrationVariance,
            solver.Model.ModelCloneOptions.AutoConcentrationVariance, 12);
        Assert.True(solver.Model.ModelCloneOptions.UnlockBootstrapParameters);
    }

    static async Task<(AnalysisResult Result, System.Collections.Generic.List<ExperimentData> Experiments)> LoadCompetitiveResult()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.Initialize();
        using var source = File.OpenRead(Fixture("competitive.ftxtc"));
        var containers = await FTXTCReader.ReadStream(source);
        foreach (var experiment in containers.OfType<ExperimentData>())
            DataManager.AddData(experiment);

        var result = containers.OfType<AnalysisResult>().First(candidate =>
            candidate.Solution?.Model?.ModelType == AnalysisModel.CompetitiveBinding
            && candidate.Solution.Model.Models.Count >= 2);
        var experiments = result.Solution.Model.Models.Select(model => model.Data).ToList();
        return (result, experiments);
    }

    static CompetitiveBinding CreateDifferentAttachedFit(ExperimentData experiment)
    {
        var sourceModel = experiment.Model;
        var model = new CompetitiveBinding(experiment);
        model.InitializeParameters(experiment);
        model.SetModelOptions(sourceModel.ModelOptions);
        model.ModelCloneOptions = sourceModel.ModelCloneOptions;
        foreach (var (key, parameter) in sourceModel.Parameters.Table)
            if (model.Parameters.Table.ContainsKey(key))
                model.Parameters.AddOrUpdateParameter(key, parameter.Value);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        return model;
    }

    static int[] SubscribeToSolutionChanges(System.Collections.Generic.IReadOnlyList<ExperimentData> experiments)
    {
        var notifications = new int[experiments.Count];
        for (var index = 0; index < experiments.Count; index++)
        {
            var capturedIndex = index;
            experiments[index].SolutionChanged += (_, _) => notifications[capturedIndex]++;
        }
        return notifications;
    }

    static StoredResultState CaptureResultState(AnalysisResult result) => new(
        result.Solution,
        result.NullComparison,
        result.BindingAssessment,
        result.InformationCriteria,
        result.ValiditySnapshot,
        result.OperatorName,
        result.Date,
        result.Name);

    static void AssertPriorPrepareState(
        AnalysisResult result,
        StoredResultState state,
        System.Collections.Generic.IReadOnlyList<ExperimentData> experiments,
        Model[] priorModels,
        SolutionInterface[] priorSolutions,
        int[] notifications,
        DocumentSaveStamp saveStamp,
        bool expectedDirty)
    {
        Assert.Same(state.Solution, result.Solution);
        Assert.Same(state.NullComparison, result.NullComparison);
        Assert.Same(state.BindingAssessment, result.BindingAssessment);
        Assert.Same(state.InformationCriteria, result.InformationCriteria);
        Assert.Same(state.ValiditySnapshot, result.ValiditySnapshot);
        Assert.Equal(state.OperatorName, result.OperatorName);
        Assert.Equal(state.Date, result.Date);
        Assert.Equal(state.Name, result.Name);
        for (var index = 0; index < experiments.Count; index++)
        {
            Assert.Same(priorModels[index], experiments[index].Model);
            Assert.Same(priorSolutions[index], experiments[index].Solution);
            Assert.Equal(0, notifications[index]);
        }
        Assert.Equal(saveStamp, DocumentDirtyTracker.CaptureSaveStamp());
        Assert.Equal(expectedDirty, DocumentDirtyTracker.IsDirty);
    }

    sealed record StoredResultState(
        GlobalSolution Solution,
        NullModelComparison NullComparison,
        BindingAssessmentState BindingAssessment,
        FitInformationCriteria InformationCriteria,
        AnalysisResultValiditySnapshot ValiditySnapshot,
        string OperatorName,
        DateTime Date,
        string Name);

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
        Assert.False(solver.Model.ModelCloneOptions.EnableAutoConcentrationVariance);
        Assert.Equal(0, solver.Model.ModelCloneOptions.AutoConcentrationVariance);
        Assert.False(solver.Model.ModelCloneOptions.UnlockBootstrapParameters);
        Assert.False(solver.Model.ModelCloneOptions.EffectiveIncludeConcentrationErrors);
        Assert.False(solver.Model.ModelCloneOptions.EffectiveUnlockBootstrapParameters);
        Assert.False(solver.Model.ModelCloneOptions.HasLegacyCombinedLeaveOneOut);
        Assert.All(solver.Model.Models, member =>
        {
            Assert.Equal(ErrorEstimationMethod.LeaveOneOut,
                member.ModelCloneOptions.ErrorEstimationMethod);
            Assert.False(member.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap);
            Assert.False(member.ModelCloneOptions.EnableAutoConcentrationVariance);
            Assert.Equal(0, member.ModelCloneOptions.AutoConcentrationVariance);
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
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
        var originalSolution = result.Solution;
        var originalComparison = result.NullComparison;
        var originalAssessment = result.BindingAssessment;
        var originalDate = result.Date;
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
        Assert.Same(originalSolution, result.Solution);
        Assert.Same(originalComparison, result.NullComparison);
        Assert.Same(originalAssessment, result.BindingAssessment);
        Assert.Equal(originalDate, result.Date);
    }

    [Fact]
    public void RejectedRefitKeepsSavedOperatorWhenCurrentOperatorChanges()
    {
        var previousOperator = AppSettings.UserName;
        try
        {
            AppSettings.UserName = "Operator A";
            var result = CreateResult(ErrorEstimationMethod.BootstrapResiduals, retainedBootstrapCount: 0);
            var solver = AnalysisResultUpdater.PrepareSolver(result);
            var originalSolution = result.Solution;
            AppSettings.UserName = "Operator B";
            var failed = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
            failed.ApplyErrorEstimationResult(ErrorEstimationMethod.BootstrapResiduals,
                failures: 10, succeeded: 0, TimeSpan.FromSeconds(1));

            Assert.Throws<InvalidOperationException>(() =>
                AnalysisResultUpdater.EnsureUpdateCanReplaceResult(solver, failed, result.Solution));
            Assert.Same(originalSolution, result.Solution);
            Assert.Equal("Operator A", result.OperatorName);
        }
        finally
        {
            AppSettings.UserName = previousOperator;
        }
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
        foreach (var member in result.Solution.Solutions)
            result.SetMemberBindingAssessmentOverride(member.Guid, BindingAssessmentOutcome.BindingDetected);
        var original = result.Solution;

        var convergence = await AnalysisResultUpdater.UpdateAsync(result);

        Assert.NotSame(original, result.Solution);
        Assert.All(result.MemberAssessments, member => Assert.Null(member.Assessment.ManualOverride));
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
        foreach (var member in result.Solution.Solutions)
            result.SetMemberBindingAssessmentOverride(member.Guid, BindingAssessmentOutcome.NoBindingDetected);
        var original = result.Solution;
        var originalComparison = result.NullComparison;
        var originalAssessments = result.MemberAssessments.ToDictionary(
            member => member.SolutionId, member => member.Assessment);
        var originalDate = result.Date;
        var originalValidity = result.ValiditySnapshot;

        await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisResultUpdater.UpdateAsync(result));

        Assert.Same(original, result.Solution);
        Assert.Same(originalComparison, result.NullComparison);
        foreach (var member in result.MemberAssessments)
            Assert.Same(originalAssessments[member.SolutionId], member.Assessment);
        Assert.Same(originalValidity, result.ValiditySnapshot);
        Assert.Equal(originalDate, result.Date);
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
