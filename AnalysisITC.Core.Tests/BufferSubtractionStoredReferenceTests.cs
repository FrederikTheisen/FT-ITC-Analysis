using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

/// <summary>
/// ITC-062: a buffer reference that is not saved with its target is stored as values on the
/// buffer-subtraction attribute, so the correction stays reproducible without the reference experiment.
/// Expected heats are computed from the constructed blank heats, not from the calculator.
/// </summary>
[Collection(ProjectWriterSaveSelectedCollectionDefinition.Name)]
public sealed class BufferSubtractionStoredReferenceTests : IDisposable
{
    const double TargetHeat = 10e-6;
    const double HeatSd = 1e-8;
    // Blank heats for injection numbers 1–4; not linear, so matched and fitted methods differ.
    static readonly double[] BlankHeats = { 2e-6, 3e-6, 5e-6, 4e-6 };

    readonly string directory = Path.Combine(Path.GetTempPath(), "BufferStoredReference-" + Guid.NewGuid().ToString("N"));
    readonly IFileSavePromptService originalPrompt = PlatformServices.FileSavePromptService;
    readonly ISettingsStore originalStore = PlatformServices.SettingsStore;

    public BufferSubtractionStoredReferenceTests()
    {
        Directory.CreateDirectory(directory);
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        DataManager.Clear(DataClearMode.ResetSession);
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        PlatformServices.RegisterFileSavePromptService(originalPrompt);
        PlatformServices.RegisterSettingsStore(originalStore);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [Theory]
    [InlineData(BufferSubtractionMethod.MatchedInjection)]
    [InlineData(BufferSubtractionMethod.Linear)]
    [InlineData(BufferSubtractionMethod.ExponentialDecay)]
    public void StoredValuesReproduceTheLiveReferenceModelExactly(BufferSubtractionMethod method)
    {
        var blank = CreateExperiment("blank", BlankHeats.Concat(new[] { 3.5e-6, 3.2e-6 }).ToArray());
        blank.Injections[2].Include = false;
        var settings = new BufferSubtractionSettings(blank.UniqueID, method);

        var live = BufferSubtractionCalculator.BuildModel(blank, settings);
        var stored = BufferSubtractionCalculator.BuildModel(BufferSubtractionReferenceSnapshot.Capture(blank), settings);
        Assert.True(live.CanEvaluate);

        for (var injectionNumber = 1; injectionNumber <= 8; injectionNumber++)
        {
            Assert.Equal(live.TryEvaluate(injectionNumber, out var liveHeat), stored.TryEvaluate(injectionNumber, out var storedHeat));
            Assert.Equal(liveHeat.Value, storedHeat.Value);
            Assert.Equal(liveHeat.SD, storedHeat.SD);
        }
    }

    [Fact]
    public void StoredMatchedValuesAverageNeighboursOfAnExcludedInjection()
    {
        var blank = CreateExperiment("blank", BlankHeats);
        blank.Injections[1].Include = false;
        var model = BufferSubtractionCalculator.BuildModel(
            BufferSubtractionReferenceSnapshot.Capture(blank),
            new BufferSubtractionSettings(blank.UniqueID, BufferSubtractionMethod.MatchedInjection));

        Assert.True(model.TryEvaluate(2, out var gap));
        Assert.Equal((2e-6 + 5e-6) / 2, gap.Value, 15);
        Assert.True(model.TryEvaluate(7, out var beyondLast));
        Assert.Equal(4e-6, beyondLast.Value, 15);
    }

    [Fact]
    public async Task SavingSelectedTargetStoresReferenceValuesAndReopensStrictly()
    {
        var (target, _) = AddTargetAndBlank(BufferSubtractionMethod.MatchedInjection);
        var path = await SaveSelected(target);

        var restored = await ReadStrict(path);
        var experiment = Assert.Single(restored.OfType<ExperimentData>());
        Assert.Equal(target.UniqueID, experiment.UniqueID);
        Assert.NotNull(experiment.BufferSubtractionSettings.Snapshot);
        Assert.Equal("blank", experiment.BufferSubtractionSettings.Snapshot.ReferenceName);
        AssertCorrected(experiment, TargetHeat);

        // Reintegration of the target keeps the stored correction.
        DataManager.AddData(experiment);
        foreach (var injection in experiment.Injections) injection.SetPeakArea(new FloatWithError(12e-6, HeatSd));
        AssertCorrected(experiment, 12e-6);
    }

    [Fact]
    public async Task StoredLinearCorrectionMatchesTheLineThroughTheBlankHeats()
    {
        var blank = CreateExperiment("blank", new[] { 1.5e-6, 2e-6, 2.5e-6, 3e-6 });
        var target = CreateExperiment("target", Enumerable.Repeat(TargetHeat, 4).ToArray());
        DataManager.AddData(blank);
        DataManager.AddData(target);
        target.SetBufferSubtraction(blank, BufferSubtractionMethod.Linear, notify: false);

        var experiment = Assert.Single((await ReadStrict(await SaveSelected(target))).OfType<ExperimentData>());

        for (var index = 0; index < 4; index++)
            Assert.Equal(TargetHeat - (1e-6 + 0.5e-6 * (index + 1)), experiment.Injections[index].PeakArea.Value, 15);
    }

    [Fact]
    public async Task StoredValuesAreKeptWhenTheProjectIsSavedAgain()
    {
        var (target, _) = AddTargetAndBlank(BufferSubtractionMethod.MatchedInjection);
        var experiment = await ReopenAlone(await SaveSelected(target));

        var path = Path.Combine(directory, "carry-over.ftxtc");
        await FTXTCWriter.WriteFileAsync(path, DataManager.Data);

        var again = Assert.Single((await ReadStrict(path)).OfType<ExperimentData>());
        Assert.NotNull(again.BufferSubtractionSettings.Snapshot);
        AssertCorrected(again, TargetHeat);
    }

    [Fact]
    public async Task LoadedReferenceTakesPrecedenceAndRefreshesStoredValues()
    {
        var (target, _) = AddTargetAndBlank(BufferSubtractionMethod.MatchedInjection);
        var experiment = await ReopenAlone(await SaveSelected(target));

        var updatedHeats = new[] { 1e-6, 1e-6, 1e-6, 1e-6 };
        DataManager.AddData(CreateExperiment("blank", updatedHeats));
        DataManager.ApplyOptions();

        Assert.All(experiment.Injections, injection => Assert.Equal(TargetHeat - 1e-6, injection.PeakArea.Value, 15));
        Assert.All(experiment.BufferSubtractionSettings.Snapshot.Points, point => Assert.Equal(1e-6, point.Heat.Value, 15));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadingTargetAndReferenceFilesTogetherLinksTheLoadedReference(bool referenceFirst)
    {
        // The target file holds stored values for the original blank heats; the blank file holds changed heats.
        var (target, blank) = AddTargetAndBlank(BufferSubtractionMethod.MatchedInjection);
        var targetPath = await SaveSelected(target);
        var updatedHeats = new[] { 1e-6, 1.5e-6, 2e-6, 2.5e-6 };
        for (var index = 0; index < updatedHeats.Length; index++)
            blank.Injections[index].SetPeakArea(new FloatWithError(updatedHeats[index], HeatSd));
        var blankPath = Path.Combine(directory, "blank.ftxtc");
        await FTXTCWriter.WriteFileAsync(blankPath, new[] { blank });
        DataManager.Clear(DataClearMode.ResetSession);

        var paths = referenceFirst ? new[] { blankPath, targetPath } : new[] { targetPath, blankPath };
        var read = await DataReader.ReadPathsAsync(paths);

        Assert.Equal(2, read.LoadedPathCount);
        var loadedTarget = DataManager.Data.Single(experiment => experiment.UniqueID == target.UniqueID);
        var loadedBlank = DataManager.Data.Single(experiment => experiment.UniqueID == blank.UniqueID);
        Assert.Same(loadedBlank, loadedTarget.ReferenceExperiment);
        for (var index = 0; index < updatedHeats.Length; index++)
        {
            Assert.Equal(TargetHeat - updatedHeats[index], loadedTarget.Injections[index].PeakArea.Value, 15);
            Assert.Equal(updatedHeats[index], loadedTarget.BufferSubtractionSettings.Snapshot.Points[index].Heat.Value, 15);
        }

        // The target follows later changes to the loaded blank: excluding blank injection 1
        // makes target injection 1 use blank injection 2.
        loadedBlank.Injections[0].ToggleDataPointActive();
        Assert.Equal(TargetHeat - updatedHeats[1], loadedTarget.Injections[0].PeakArea.Value, 15);
    }

    [Fact]
    public async Task EditingAttributesKeepsTheStoredCorrection()
    {
        var (target, _) = AddTargetAndBlank(BufferSubtractionMethod.MatchedInjection);
        var experiment = await ReopenAlone(await SaveSelected(target));

        var edited = experiment.Attributes.Select(attribute => attribute.Copy()).ToList();
        var ionicStrength = ExperimentAttribute.FromKey(AttributeKey.IonicStrength);
        ionicStrength.ParameterValue = new FloatWithError(0.15);
        edited.Add(ionicStrength);
        Assert.True(experiment.UpdateDetailAttributes(edited));

        Assert.NotNull(experiment.BufferSubtractionSettings?.Snapshot);
        AssertCorrected(experiment, TargetHeat);

        var linear = experiment.Attributes.Single(attribute => attribute.Key == AttributeKey.BufferSubtraction).Copy();
        linear.IntValue = (int)BufferSubtractionMethod.Linear;
        experiment.AddOrUpdateAttribute(linear, notify: false);

        Assert.Equal(BufferSubtractionMethod.Linear, experiment.BufferSubtractionSettings.Method);
        Assert.NotNull(experiment.BufferSubtractionSettings.Snapshot);
    }

    [Fact]
    public async Task DuplicatingKeepsTheStoredCorrection()
    {
        var (target, _) = AddTargetAndBlank(BufferSubtractionMethod.MatchedInjection);
        var experiment = await ReopenAlone(await SaveSelected(target));

        DataManager.DuplicateSelectedData(experiment);
        var copy = DataManager.Data.Last();

        Assert.NotSame(experiment, copy);
        Assert.NotNull(copy.BufferSubtractionSettings?.Snapshot);
        AssertCorrected(copy, TargetHeat);
    }

    [Fact]
    public async Task SavingSelectedResultIncludesTheBufferReferenceOfAMember()
    {
        using (var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "FileTypeTests", "JORS Example Project.ftxtc")))
            DataManager.AddData(await FTXTCReader.ReadStream(stream));
        var result = DataManager.Results.First();
        var member = result.Solution.Solutions.First().Data;
        var blank = CreateExperiment("result-blank", Enumerable.Repeat(1e-7, member.InjectionCount).ToArray());
        DataManager.AddData(blank);
        member.SetBufferSubtraction(blank, BufferSubtractionMethod.MatchedInjection, notify: false);

        var restored = await ReadStrict(await SaveSelected(result));

        var restoredResult = Assert.Single(restored.OfType<AnalysisResult>());
        Assert.Contains(restored.OfType<ExperimentData>(), experiment => experiment.UniqueID == blank.UniqueID);
        Assert.DoesNotContain(restoredResult.Solution.Solutions, solution => solution.Data.UniqueID == blank.UniqueID);
        Assert.Null(restored.OfType<ExperimentData>().Single(experiment => experiment.UniqueID == member.UniqueID)
            .BufferSubtractionSettings.Snapshot);
    }

    [Fact]
    public async Task FullProjectSavesWithTheReferenceDoNotStoreValues()
    {
        var (target, blank) = AddTargetAndBlank(BufferSubtractionMethod.MatchedInjection);
        var path = Path.Combine(directory, "full.ftxtc");
        await FTXTCWriter.WriteFileAsync(path, new[] { blank, target });

        var restored = (await ReadStrict(path)).OfType<ExperimentData>().Single(experiment => experiment.UniqueID == target.UniqueID);
        Assert.Null(restored.BufferSubtractionSettings.Snapshot);
    }

    (ExperimentData Target, ExperimentData Blank) AddTargetAndBlank(BufferSubtractionMethod method)
    {
        var blank = CreateExperiment("blank", BlankHeats);
        var target = CreateExperiment("target", Enumerable.Repeat(TargetHeat, BlankHeats.Length).ToArray());
        DataManager.AddData(blank);
        DataManager.AddData(target);
        target.SetBufferSubtraction(blank, method, notify: false);
        return (target, blank);
    }

    async Task<string> SaveSelected(ITCDataContainer data)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".ftxtc");
        PlatformServices.RegisterFileSavePromptService(new FixedFileSavePrompt(path));
        Assert.True(await ProjectWriter.SaveSelectedAsync(data));
        return path;
    }

    static async Task<ITCDataContainer[]> ReadStrict(string path)
    {
        using var stream = File.OpenRead(path);
        return await FTXTCReader.ReadStream(stream);
    }

    // Reopen the saved target in a session without its buffer reference.
    async Task<ExperimentData> ReopenAlone(string path)
    {
        var experiment = Assert.Single((await ReadStrict(path)).OfType<ExperimentData>());
        DataManager.Clear(DataClearMode.ResetSession);
        DataManager.AddData(experiment);
        DataManager.ApplyOptions();
        AssertCorrected(experiment, TargetHeat);
        return experiment;
    }

    static void AssertCorrected(ExperimentData experiment, double rawHeat)
    {
        for (var index = 0; index < BlankHeats.Length; index++)
            Assert.Equal(rawHeat - BlankHeats[index], experiment.Injections[index].PeakArea.Value, 15);
    }

    static ExperimentData CreateExperiment(string id, double[] heats)
    {
        var data = new ExperimentData(id + ".itc")
        {
            Name = id,
            CellConcentration = new FloatWithError(10e-6), SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 200e-6, TargetTemperature = 25, MeasuredTemperature = 25,
        };
        data.SetID(id);
        for (var index = 0; index < heats.Length; index++)
            data.Injections.Add(new InjectionData(data, index, 2e-6, 2e-10, include: true) { Time = 30 + index * 30 });
        for (var index = 0; index < heats.Length; index++)
            data.Injections[index].SetPeakArea(new FloatWithError(heats[index], HeatSd));
        return data;
    }

    sealed class FixedFileSavePrompt : IFileSavePromptService
    {
        readonly string path;

        public FixedFileSavePrompt(string path) => this.path = path;

        public Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes) => Task.FromResult(path);
    }
}
