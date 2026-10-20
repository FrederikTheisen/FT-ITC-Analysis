using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class NullComparisonBookkeepingPersistenceTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "NullComparisonBookkeeping-" + Guid.NewGuid().ToString("N"));
    readonly bool priorAutoResult = AppSettings.CreateSingleAnalysisResult;
    readonly EventHandler<HandledException> priorMessageHandler = AppEventHandler.ShowAppMessage;
    readonly List<HandledException> errors = new();

    public NullComparisonBookkeepingPersistenceTests()
    {
        Directory.CreateDirectory(directory);
        DocumentDirtyTracker.Initialize();
        DataManager.Clear(DataClearMode.ResetSession);
        AppSettings.CreateSingleAnalysisResult = false;
        AppEventHandler.ShowAppMessage = (_, error) => errors.Add(error);
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.MarkClean();
        StatusBarManager.ClearAppStatus();
        AppSettings.CreateSingleAnalysisResult = priorAutoResult;
        AppEventHandler.ShowAppMessage = priorMessageHandler;
        Directory.Delete(directory, recursive: true);
    }

    [Theory]
    [InlineData("attached", DilutionMethod.MicroCal, DilutionMethod.Exponential)]
    [InlineData("attached", DilutionMethod.Exponential, DilutionMethod.DiscreteDisplacement)]
    [InlineData("attached", DilutionMethod.DiscreteDisplacement, DilutionMethod.MicroCal)]
    [InlineData("single", DilutionMethod.MicroCal, DilutionMethod.Exponential)]
    [InlineData("single", DilutionMethod.Exponential, DilutionMethod.DiscreteDisplacement)]
    [InlineData("single", DilutionMethod.DiscreteDisplacement, DilutionMethod.MicroCal)]
    [InlineData("pooled", DilutionMethod.MicroCal, DilutionMethod.Exponential)]
    [InlineData("pooled", DilutionMethod.Exponential, DilutionMethod.DiscreteDisplacement)]
    [InlineData("pooled", DilutionMethod.DiscreteDisplacement, DilutionMethod.MicroCal)]
    [InlineData("independent", DilutionMethod.MicroCal, DilutionMethod.Exponential)]
    [InlineData("independent", DilutionMethod.Exponential, DilutionMethod.DiscreteDisplacement)]
    [InlineData("independent", DilutionMethod.DiscreteDisplacement, DilutionMethod.MicroCal)]
    [InlineData("reopened", DilutionMethod.Exponential, DilutionMethod.DiscreteDisplacement)]
    [InlineData("attached", DilutionMethod.Exponential, DilutionMethod.Exponential)]
    public async Task FitThenChangeBookkeepingThenSaveRetainsComparison(
        string scope, DilutionMethod initialMethod, DilutionMethod nextMethod)
    {
        var (experiments, result) = await Fit(scope, initialMethod);
        if (scope == "reopened")
        {
            using var initial = new MemoryStream();
            await FTXTCWriter.WriteStream(initial, experiments);
            initial.Position = 0;
            var containers = (await FTXTCReader.ReadWithRecovery(initial, FtxtcReadPolicy.Strict)).Containers;
            DataManager.Clear(DataClearMode.ResetSession);
            DataManager.AddData(containers);
            experiments = containers.OfType<ExperimentData>().ToList();
        }
        var changed = experiments[0];
        var expected = experiments.ToDictionary(data => data.UniqueID, data => data.Solution.NullComparison);
        var expectedResult = result?.NullComparison;
        var comparisons = expected.Values.Append(expectedResult).ToArray();
        var evidence = comparisons.Select(CapturedEvidence).ToArray();
        var expectedAssessment = result?.BindingAssessment;
        var expectedMemberAssessments = result?.MemberAssessments.Select(member => member.Assessment).ToArray();
        var oldNull = Assert.Single(changed.Solution.NullComparison.NullSolutions,
            solution => solution.Data.UniqueID == changed.UniqueID);
        Assert.Equal(scope != "reopened", ReferenceEquals(changed, oldNull.Data));
        Assert.True(expected[changed.UniqueID].NullFitSucceeded, expected[changed.UniqueID].NullFitReason);
        Assert.NotNull(expected[changed.UniqueID].DeltaAicc);
        var oldHeatMethod = changed.Solution.Model.HeatMethod;
        var observedHeats = changed.Injections.Select(injection => injection.RawPeakArea).ToArray();

        // The main cases change bookkeeping before any save/reopen detaches the Null model.
        RawDataReader.ReprocessInjections(changed, nextMethod);
        Assert.False(changed.Solution.IsValid);
        Assert.Equal(oldHeatMethod, changed.Solution.Model.HeatMethod);
        Assert.Equal(observedHeats, changed.Injections.Select(injection => injection.RawPeakArea));
        Assert.Equal(evidence, comparisons.Select(CapturedEvidence));
        var path = Path.Combine(directory, "changed.ftxtc");
        ProjectDocumentState.Path = path;
        Assert.True(await ProjectWriter.SaveWithPathAsync());

        for (var generation = 0; generation < 2; generation++)
        {
            using var input = File.OpenRead(path);
            var read = await FTXTCReader.ReadWithRecovery(input, FtxtcReadPolicy.Strict);
            Assert.Empty(read.Issues);
            foreach (var experiment in read.Containers.OfType<ExperimentData>())
            {
                NullComparisonPersistenceTests.AssertComparisonEvidence(expected[experiment.UniqueID],
                    experiment.Solution.NullComparison);
                var nullFit = Assert.Single(experiment.Solution.NullComparison.NullSolutions,
                    solution => solution.Data.UniqueID == experiment.UniqueID);
                Assert.Equal(experiment.Solution.IsValid, nullFit.IsValid);
                Assert.NotSame(experiment, nullFit.Data);
                Assert.Equal(experiment.Solution.Model.HeatMethod, nullFit.Model.HeatMethod);
                Assert.Equal(experiment.UniqueID != changed.UniqueID, experiment.Solution.IsValid);
            }
            if (result != null)
            {
                var savedResult = Assert.Single(read.Containers.OfType<AnalysisResult>());
                NullComparisonPersistenceTests.AssertComparisonEvidence(expectedResult, savedResult.NullComparison);
                Assert.Equivalent(expectedAssessment, savedResult.BindingAssessment, strict: true);
                Assert.Equivalent(expectedMemberAssessments,
                    savedResult.MemberAssessments.Select(member => member.Assessment).ToArray(), strict: true);
                Assert.False(savedResult.IsModified);
                foreach (var member in savedResult.Solution.Solutions)
                    Assert.Equal(member.IsValid, Assert.Single(savedResult.NullComparison.NullSolutions,
                        solution => solution.Data.UniqueID == member.Data.UniqueID).IsValid);
            }
            var nextPath = Path.Combine(directory, "round-trip-" + generation + ".ftxtc");
            await FTXTCWriter.WriteFileAsync(nextPath, read.Containers.OfType<ExperimentData>(),
                read.Containers.OfType<AnalysisResult>());
            path = nextPath;
        }
        Assert.False(oldNull.IsValid);
        foreach (var unchanged in experiments.Skip(1))
        {
            Assert.True(unchanged.Solution.IsValid);
            Assert.True(Assert.Single(unchanged.Solution.NullComparison.NullSolutions,
                solution => solution.Data.UniqueID == unchanged.UniqueID).IsValid);
        }
        if (result != null)
        {
            var update = Assert.IsType<GlobalSolver>(AnalysisResultUpdater.PrepareSolver(result));
            Assert.Equal(InjectionBookkeeping.HeatMethodFor(nextMethod), update.Model.Models[0].HeatMethod);
        }
        Assert.Equal(evidence, comparisons.Select(CapturedEvidence));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ExplicitRefitUsesTheNewConventionForBothFits()
    {
        var (experiments, result) = await Fit("single", DilutionMethod.Exponential);
        var comparison = result.NullComparison;
        var evidence = CapturedEvidence(comparison);
        RawDataReader.ReprocessInjections(experiments[0], DilutionMethod.DiscreteDisplacement);
        var solver = Assert.IsType<GlobalSolver>(AnalysisResultUpdater.PrepareSolver(result));
        await Analyze(solver);
        var binding = Assert.Single(solver.Model.Solution.Solutions);
        var nullFit = Assert.Single(binding.NullComparison.NullSolutions);
        Assert.Equal(InjectionHeatMethod.DiscreteDisplacement, binding.Model.HeatMethod);
        Assert.Equal(binding.Model.HeatMethod, nullFit.Model.HeatMethod);
        Assert.True(binding.IsValid);
        Assert.True(nullFit.IsValid);
        Assert.Same(comparison, result.NullComparison);
        Assert.Equal(evidence, CapturedEvidence(comparison));
    }

    [Theory]
    [InlineData("save")]
    [InlineData("close")]
    [InlineData("autosave")]
    public async Task CaptureFailurePreservesExistingDestinationAndDirtyState(string entryPoint)
    {
        var (experiments, _) = await Fit("attached", DilutionMethod.Exponential);
        var path = Path.Combine(directory, "existing.ftxtc");
        ProjectDocumentState.Path = path;
        Assert.True(await ProjectWriter.SaveWithPathAsync());
        var originalBytes = File.ReadAllBytes(path);
        experiments[0].Solution.NullComparison.NullModelId = "";
        DocumentDirtyTracker.MarkDirty();
        var stamp = DocumentDirtyTracker.CaptureSaveStamp();

        if (entryPoint == "autosave")
            await Assert.ThrowsAsync<InvalidDataException>(() => ProjectWriter.WriteAutoSaveAsync(path));
        else
            Assert.False(await (entryPoint == "close" ? ProjectWriter.SaveForCloseAsync() : ProjectWriter.SaveWithPathAsync()));

        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Equal(path, ProjectDocumentState.Path);
        Assert.True(DocumentDirtyTracker.IsDirty);
        Assert.True(DocumentDirtyTracker.MatchesSaveStamp(stamp));
        Assert.False(ProjectWriter.IsWriteInProgress);
        if (entryPoint != "autosave")
        {
            var error = Assert.Single(errors);
            Assert.Equal(nameof(InvalidDataException), error.Title);
            Assert.Equal(HandledException.Severity.Error, error.Level);
        }
    }

    // Freeze captured evidence independently of the mutable solution validity flags.
    static string CapturedEvidence(NullModelComparison comparison) => JsonSerializer.Serialize(comparison == null ? null : new
    {
        comparison.NullModelId,
        comparison.IsIndependentMemberComparison,
        comparison.BindingFitSucceeded,
        comparison.BindingFitReason,
        comparison.NullFitSucceeded,
        comparison.NullFitReason,
        comparison.BindingInformationCriteria,
        comparison.NullInformationCriteria,
        comparison.DeltaAicc,
        comparison.ComparisonUnavailableReason,
        comparison.Members,
    });

    static async Task<(List<ExperimentData> Experiments, AnalysisResult Result)> Fit(string scope, DilutionMethod method)
    {
        var multi = scope is "pooled" or "independent";
        var models = Enumerable.Range(0, multi ? 2 : 1)
            .Select(_ => InjectionProcessingMethodTests.FittedModel(bootstrap: false, method: method)).ToList();
        var experiments = models.Select(model => model.Data).ToList();
        DataManager.AddData(experiments);
        SolverInterface solver;
        if (multi)
        {
            var global = new GlobalModel(models) { Parameters = new GlobalModelParameters() };
            foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
            if (scope == "pooled")
            {
                global.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
                global.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 0);
                global.Parameters.SetIndividualFromGlobal();
            }
            solver = new GlobalSolver { Model = global };
        }
        else solver = new Solver { Model = models[0] };
        await Analyze(solver);
        AnalysisResult result = null;
        if (scope is not ("attached" or "reopened"))
        {
            result = new AnalysisResult(solver is GlobalSolver global
                ? global.Model.Solution : GlobalSolution.FromSingleExperimentSolver((Solver)solver));
            if (result.IsIndependentAssessmentCollection)
                result.SetMemberBindingAssessmentOverride(result.Solution.Solutions[0].Guid, BindingAssessmentOutcome.NoBindingDetected);
            else result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
            DataManager.AddData(result);
        }
        return (experiments, result);
    }

    static async Task Analyze(SolverInterface solver)
    {
        solver.SolverAlgorithm = SolverAlgorithm.NelderMead;
        solver.ErrorEstimationMethod = ErrorEstimationMethod.None;
        solver.UseErrorWeightedFitting = false;
        solver.MaxOptimizerIterations = 300;
        solver.CanCreateAnalysisResult = false;
        var finished = new TaskCompletionSource<SolverConvergence>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object sender, SolverConvergence convergence)
        {
            if (ReferenceEquals(sender, solver)) finished.TrySetResult(convergence);
        }
        SolverInterface.AnalysisFinished += Handler;
        try
        {
            SolverInterface.TerminateAnalysisFlag.Lower();
            solver.Analyze();
            var convergence = await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(convergence.Failed, convergence.FailureReason);
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
            SolverInterface.AnalysisFinished -= Handler;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
    }
}
