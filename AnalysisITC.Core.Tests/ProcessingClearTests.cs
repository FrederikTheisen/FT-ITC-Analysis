using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(DataManagerUndoOrderCollectionDefinition.Name)]
public sealed class ProcessingClearTests : IDisposable
{
    public ProcessingClearTests()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.Initialize();
    }
    public void Dispose() => DataManager.Clear(DataClearMode.ResetSession);

    [Theory]
    [InlineData(ProcessingClearScope.Selected, 1)]
    [InlineData(ProcessingClearScope.Active, 2)]
    [InlineData(ProcessingClearScope.All, 3)]
    public void ScopeIsCapturedBeforeClearingChangesActiveState(ProcessingClearScope scope, int count)
    {
        var experiments = new[] { Experiment(), Experiment(), Experiment() };
        experiments[2].Include = false;
        DataManager.AddData(experiments);
        DataManager.SelectIndex(0);
        var plan = DataManager.PrepareProcessingClear(scope);
        Assert.Equal(count, plan.Count);
        Assert.True(plan.TryApply());
        Assert.Equal(count, experiments.Count(data => data.Solution == null));
        Assert.Equal(count, experiments.Count(data => !data.CanBeAnalyzed));
        Assert.Empty(DataManager.DeletedDataList);
        Assert.False(plan.TryApply());
    }

    [Fact]
    public void ResetRestoresDefaultsAndPreservesInputsAndAttributes()
    {
        var data = Experiment();
        var raw = data.DataPoints;
        var concentrations = data.Injections.Select(injection => injection.ActualCellConcentration).ToArray();
        var ratios = data.Injections.Select(injection => injection.Ratio).ToArray();
        data.Injections[0].Include = false;
        data.Comments = "Keep me";
        var attribute = ExperimentAttribute.FromKey(AttributeKey.Salt);
        attribute.DoubleValue = 0.15;
        data.Attributes.Add(attribute);
        data.Processor.IntegrationLengthMode = InjectionData.IntegrationLengthMode.Factor;
        data.Processor.IntegrationLengthFactor = 7;
        data.Processor.Lock();
        DataManager.AddData(data);
        DocumentDirtyTracker.MarkClean();
        var oldProcessor = data.Processor;
        var revision = data.ProcessingRevision;
        Assert.True(DataManager.PrepareProcessingClear(ProcessingClearScope.All).TryApply());
        Assert.NotSame(oldProcessor, data.Processor);
        Assert.False(data.Processor.IsLocked);
        Assert.False(data.Processor.BaselineCompleted);
        Assert.Equal(InjectionData.IntegrationLengthMode.Time, data.Processor.IntegrationLengthMode);
        Assert.Equal(2, data.Processor.IntegrationLengthFactor);
        Assert.Equal(AppSettings.DiscardIntegrationRegionForBaseline, data.Processor.DiscardIntegratedPoints);
        var spline = Assert.IsType<SplineInterpolator>(data.Processor.Interpolator);
        Assert.Empty(spline.SplinePoints);
        Assert.Empty(spline.Baseline);
        Assert.Equal(SplineInterpolator.DefaultPointDensity, spline.PointDensity);
        Assert.Equal(SplineInterpolator.DefaultHandleMode, spline.HandleMode);
        Assert.Null(data.BaseLineCorrectedDataPoints);
        Assert.Null(data.Model);
        Assert.Same(raw, data.DataPoints);
        Assert.Same(attribute, Assert.Single(data.Attributes));
        Assert.Equal("Keep me", data.Comments);
        Assert.False(data.Injections[0].Include);
        Assert.Equal(concentrations, data.Injections.Select(injection => injection.ActualCellConcentration));
        Assert.Equal(ratios, data.Injections.Select(injection => injection.Ratio));
        Assert.All(data.Injections, injection =>
        {
            Assert.False(injection.IsIntegrated);
            Assert.True(double.IsNaN(injection.RawPeakArea.Value));
            Assert.True(double.IsNaN(injection.PeakArea.Value));
            Assert.Equal(0, injection.IntegrationStartDelay);
            Assert.Equal(10, injection.IntegrationEndOffset); // First injection delay is 20 s.
        });
        Assert.Equal(revision + 1, data.ProcessingRevision);
        Assert.True(DocumentDirtyTracker.IsDirty);
        DocumentDirtyTracker.MarkClean();
        Assert.False(DataManager.CanClearProcessing(ProcessingClearScope.All));
        Assert.False(DataManager.PrepareProcessingClear(ProcessingClearScope.All).TryApply());
        Assert.False(DocumentDirtyTracker.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedResultsAreRetainedAndValidityUsesChangedInputs(bool thermogram)
    {
        var data = Experiment(thermogram);
        var result = Result(data);
        var solution = result.Solution;
        var snapshot = result.ValiditySnapshot;
        var heat = data.Injections[0].RawPeakArea;
        DataManager.AddData(new ITCDataContainer[] { data, result });
        Assert.True(result.IsValidForCurrentData);
        Assert.True(DataManager.PrepareProcessingClear(ProcessingClearScope.All).TryApply());
        Assert.Same(result, Assert.Single(DataManager.Results));
        Assert.Same(solution, result.Solution);
        Assert.Same(snapshot, result.ValiditySnapshot);
        Assert.Null(data.Solution);
        Assert.Equal(!thermogram, result.IsValidForCurrentData);
        if (!thermogram)
        {
            Assert.Equal(heat, data.Injections[0].RawPeakArea);
            Assert.True(data.CanBeAnalyzed);
            Assert.False(DataManager.CanClearProcessing(ProcessingClearScope.All));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearedStateStrictlyReopensWithHistoricalResult(bool thermogram)
    {
        var data = Experiment(thermogram);
        var result = Result(data);
        DataManager.AddData(new ITCDataContainer[] { data, result });
        Assert.True(DataManager.PrepareProcessingClear(ProcessingClearScope.All).TryApply());
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, DataManager.Data, DataManager.Results, DataManager.SourceItems);
        stream.Position = 0;
        var read = await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict);
        Assert.Empty(read.Issues);
        var restored = Assert.Single(read.Containers.OfType<ExperimentData>());
        var saved = Assert.Single(read.Containers.OfType<AnalysisResult>());
        Assert.Null(restored.Solution);
        Assert.Equal(!thermogram, restored.CanBeAnalyzed);
        Assert.Equal(!thermogram, saved.IsValidForCurrentData);
        if (thermogram)
        {
            Assert.IsType<SplineInterpolator>(restored.Processor.Interpolator);
            Assert.False(restored.Processor.BaselineCompleted);
            Assert.Null(restored.BaseLineCorrectedDataPoints);
            Assert.All(restored.Injections, injection => Assert.True(double.IsNaN(injection.RawPeakArea.Value)));
        }
    }

    [Fact]
    public void DefaultIntegrationEndpointIsClampedForShorterInjections()
    {
        var data = Experiment();
        var shortInjection = InjectionData.FromPEAQFile(data, 1, true, 20, 2e-6, 6, 2, 25);
        shortInjection.SetPeakArea(new FloatWithError(-1e-6));
        data.Injections[1] = shortInjection;
        DataManager.AddData(data);
        Assert.True(DataManager.PrepareProcessingClear(ProcessingClearScope.All).TryApply());
        Assert.Equal(10, data.Injections[0].IntegrationEndOffset);
        Assert.Equal(6, data.Injections[1].IntegrationEndOffset);
    }

    [Fact]
    public void ClearingZeroHeatsStillMakesSavedInputsStale()
    {
        var data = Experiment();
        data.Processor.InitializeBaseline(BaselineInterpolatorTypes.Spline);
        foreach (var injection in data.Injections)
        {
            injection.SetPeakArea(new FloatWithError(0));
            injection.SetIntegrationLengthByTime(10);
        }
        var result = Result(data);
        DataManager.AddData(new ITCDataContainer[] { data, result });
        Assert.True(result.IsValidForCurrentData);
        Assert.True(DataManager.PrepareProcessingClear(ProcessingClearScope.All).TryApply());
        Assert.False(result.IsValidForCurrentData);
    }

    [Fact]
    public async Task FailedProcessingReleasesBusyAdmission()
    {
        var data = Experiment();
        data.Processor.Interpolator = new FailingBaseline(data.Processor);
        DataManager.AddData(data);
        await Assert.ThrowsAsync<ArithmeticException>(() => data.Processor.InterpolateBaseline(throwOnError: true));
        Assert.False(ProcessingActivity.IsBusy);
        Assert.True(DataManager.CanClearProcessing(ProcessingClearScope.All));
    }

    [Fact]
    public async Task ResultUpdateRemainsBusyThroughExperimentAttachment()
    {
        var data = Experiment();
        var result = Result(data);
        DataManager.AddData(new ITCDataContainer[] { data, result });
        var observed = false;
        EventHandler changed = (_, _) =>
        {
            if (ReferenceEquals(data.Solution, result.Solution.Solutions[0]))
                observed = ProcessingActivity.IsBusy;
        };
        data.SolutionChanged += changed;
        try
        {
            await AnalysisResultUpdater.UpdateAsync(result);
            Assert.True(observed);
            Assert.False(ProcessingActivity.IsBusy);
        }
        finally { data.SolutionChanged -= changed; }
    }

    [Fact]
    public void PlansRejectChangedDocumentStateAndReplacementDocuments()
    {
        var data = Experiment();
        DataManager.AddData(data);
        var plan = DataManager.PrepareProcessingClear(ProcessingClearScope.All);
        data.Comments = "Changed after confirmation began";
        Assert.False(plan.TryApply());
        Assert.NotNull(data.Solution);
        plan = DataManager.PrepareProcessingClear(ProcessingClearScope.All);
        data.Processor.IntegrationLengthFactor = 8; // No document event: target signature still catches this.
        Assert.False(plan.TryApply());
        plan = DataManager.PrepareProcessingClear(ProcessingClearScope.All);
        DataManager.Clear(DataClearMode.ResetSession);
        DataManager.AddData(data);
        Assert.False(plan.TryApply());
        Assert.NotNull(data.Solution);
    }

    [Fact]
    public void SelectedResultCannotBeClearedAndDeleteResultsKeepsItsExistingUndo()
    {
        var data = Experiment();
        var result = Result(data);
        DataManager.AddData(new ITCDataContainer[] { data, result });
        DataManager.SelectIndex(1);
        Assert.False(DataManager.CanClearProcessing(ProcessingClearScope.Selected));
        var processor = data.Processor;
        var solution = data.Solution;
        DataManager.DeleteAllResults();
        Assert.Empty(DataManager.Results);
        Assert.Same(processor, data.Processor);
        Assert.Same(solution, data.Solution);
        DataManager.UndoDeleteData();
        Assert.Same(result, Assert.Single(DataManager.Results));
    }

    [Fact]
    public void BufferReferenceUpdatesDoNotRestoreClearedHeatsOrLoseAttributes()
    {
        var reference = Experiment();
        var target = Experiment();
        target.SetBufferSubtraction(reference, BufferSubtractionMethod.MatchedInjection, notify: false);
        var attribute = Assert.Single(target.Attributes);
        DataManager.AddData(new[] { reference, target });
        DataManager.SelectIndex(1);
        Assert.True(DataManager.PrepareProcessingClear(ProcessingClearScope.Selected).TryApply());
        Assert.Same(attribute, Assert.Single(target.Attributes));
        Assert.Same(reference, target.ReferenceExperiment);
        reference.Injections[0].SetPeakArea(new FloatWithError(-8e-6));
        reference.UpdateProcessing();
        Assert.All(target.Injections, injection => Assert.True(double.IsNaN(injection.PeakArea.Value)));
        Assert.All(target.Injections, injection => Assert.False(injection.IsIntegrated));
    }

    [Fact]
    public async Task PendingProcessingBlocksEveryScopeAndRejectedPlanMutatesNothing()
    {
        var data = Experiment();
        var blocker = new BlockingBaseline(data.Processor);
        data.Processor.Interpolator = blocker;
        DataManager.AddData(data);
        var plan = DataManager.PrepareProcessingClear(ProcessingClearScope.All);
        var task = data.Processor.ProcessData(showProgress: false);
        await blocker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var queued = data.Processor.ProcessData(showProgress: false);
        try
        {
            Assert.True(ProcessingActivity.IsBusy);
            Assert.All(Enum.GetValues<ProcessingClearScope>(), scope => Assert.False(DataManager.CanClearProcessing(scope)));
            Assert.False(plan.TryApply());
            Assert.Same(blocker, data.Processor.Interpolator);
        }
        finally
        {
            blocker.Release.TrySetResult(true);
            await Task.WhenAll(task, queued);
        }
        Assert.False(ProcessingActivity.IsBusy);
        Assert.True(DataManager.PrepareProcessingClear(ProcessingClearScope.All).TryApply());
        Assert.False(data.CanBeAnalyzed);
        Assert.False(data.Processor.BaselineCompleted);
    }

    [Fact]
    public async Task FittingRemainsBusyThroughFinishedEventAndResultPublication()
    {
        var data = Experiment();
        DataManager.AddData(data);
        var solver = new Solver
        {
            Model = data.Model, ErrorEstimationMethod = ErrorEstimationMethod.None,
            SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
        };
        var savedPreference = AppSettings.CreateSingleAnalysisResult;
        AppSettings.CreateSingleAnalysisResult = true;
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publication = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<SolverConvergence> onFinished = (sender, _) =>
        {
            if (!ReferenceEquals(sender, solver)) return;
            finished.TrySetResult(ProcessingActivity.IsBusy && !DataManager.PrepareProcessingClear(ProcessingClearScope.All).TryApply());
        };
        EventHandler<ExperimentData> onData = (_, _) =>
        {
            if (DataManager.Results.Count > 0) publication.TrySetResult(ProcessingActivity.IsBusy);
        };
        EventHandler onActivity = (_, _) => { if (!ProcessingActivity.IsBusy) idle.TrySetResult(true); };
        SolverInterface.AnalysisFinished += onFinished;
        DataManager.DataDidChange += onData;
        ProcessingActivity.Changed += onActivity;
        try
        {
            solver.Analyze();
            Assert.True(await finished.Task.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.True(await publication.Task.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.True(await idle.Task.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.False(ProcessingActivity.IsBusy);
        }
        finally
        {
            AppSettings.CreateSingleAnalysisResult = savedPreference;
            SolverInterface.AnalysisFinished -= onFinished;
            DataManager.DataDidChange -= onData;
            ProcessingActivity.Changed -= onActivity;
        }
    }

    static ExperimentData Experiment(bool thermogram = true)
    {
        var data = new ExperimentData("clear.itc")
        {
            CellConcentration = new FloatWithError(35e-6), SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3, MeasuredTemperature = 25, TargetTemperature = 25,
        };
        if (thermogram)
        {
            for (var time = 0; time <= 100; time++) data.DataPoints.Add(new DataPoint(time, 1));
            data.BaseLineCorrectedDataPoints = data.DataPoints.Select(point => point.Copy()).ToList();
            data.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
            data.Processor.Interpolator.Baseline = data.DataPoints.Select(_ => new Energy(1)).ToList();
            data.Processor.BaselineCompleted = true;
        }
        else data.DataSourceFormat = ITCDataFormat.IntegratedHeats;
        for (var index = 0; index < 5; index++)
        {
            var injection = InjectionData.FromPEAQFile(data, index, true, index * 20, 2e-6, 20, 2, 25);
            injection.ActualCellConcentration = 35e-6;
            injection.ActualTitrantConcentration = (index + 1) * 5e-6;
            injection.Ratio = (index + 1) / 7d;
            injection.InitializeIntegrationTimes();
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7, 1e-8));
            data.Injections.Add(injection);
        }
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None };
        model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        { Algorithm = SolverAlgorithm.LevenbergMarquardt }));
        data.UpdateSolution(model);
        return data;
    }
    static AnalysisResult Result(ExperimentData data) => new(GlobalSolution.FromSingleExperimentSolver(new Solver
    { Model = data.Model, ErrorEstimationMethod = ErrorEstimationMethod.None }));

    sealed class FailingBaseline : PolynomialLeastSquaresInterpolator
    {
        public FailingBaseline(DataProcessor processor) : base(processor) { }
        public override Task Interpolate(CancellationToken token, bool replace = true)
            => throw new ArithmeticException("Controlled processing failure");
    }

    sealed class BlockingBaseline : PolynomialLeastSquaresInterpolator
    {
        public readonly TaskCompletionSource<bool> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BlockingBaseline(DataProcessor processor) : base(processor) { }
        public override async Task Interpolate(CancellationToken token, bool replace = true)
        {
            Entered.TrySetResult(true);
            await Release.Task;
            Baseline = Data.DataPoints.Select(_ => new Energy(0)).ToList();
        }
    }
}
