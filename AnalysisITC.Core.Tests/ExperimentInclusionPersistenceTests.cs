using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Platform;
using Xunit;
using Xunit.Abstractions;

namespace AnalysisITC.Core.Tests;

[Collection(ProjectWriterSaveSelectedCollectionDefinition.Name)]
public sealed class ExperimentInclusionPersistenceTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ExperimentInclusionTests-" + Guid.NewGuid().ToString("N"));
    readonly ISettingsStore originalStore = PlatformServices.SettingsStore;
    readonly IFileSavePromptService originalPrompt = PlatformServices.FileSavePromptService;
    readonly string originalLastPath = AppSettings.LastDocumentPath;
    readonly ITestOutputHelper output;

    public ExperimentInclusionPersistenceTests(ITestOutputHelper output)
    {
        this.output = output;
        Directory.CreateDirectory(directory);
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        DocumentDirtyTracker.Initialize();
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.MarkClean();
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.MarkClean();
        AppSettings.LastDocumentPath = originalLastPath;
        PlatformServices.RegisterSettingsStore(originalStore);
        PlatformServices.RegisterFileSavePromptService(originalPrompt);
        Directory.Delete(directory, recursive: true);
    }

    public static IEnumerable<object[]> SaveCases()
    {
        foreach (var entryPoint in new[] { "save", "selected", "autosave" })
        foreach (var choice in new[] { "default", "included", "excluded" })
        foreach (var integrated in new[] { false, true })
            yield return new object[] { entryPoint, choice, integrated };
    }

    [Theory]
    [MemberData(nameof(SaveCases))]
    public async Task SaveReopenAndIntegratePreservesOriginalInclusionChoice(string entryPoint, string choice, bool integrated)
    {
        var expectedInclude = choice != "excluded";
        var original = RawExperiment("saved", choice);
        var control = RawExperiment("unsaved-control", choice);
        if (integrated)
        {
            await original.Processor.ProcessData(showProgress: false);
            AssertProcessed(original);
        }
        Assert.Equal(integrated, original.CanBeAnalyzed);
        Assert.Equal(integrated && expectedInclude, original.Include);
        DataManager.AddData(new[] { original });

        var path = Path.Combine(directory, "inclusion.ftxtc");
        ProjectDocumentState.Path = path;
        PlatformServices.RegisterFileSavePromptService(new FixedPrompt(path));
        Assert.True(await (entryPoint switch
        {
            "selected" => ProjectWriter.SaveSelectedAsync(original),
            "autosave" => ProjectWriter.WriteAutoSaveAsync(path),
            _ => ProjectWriter.SaveWithPathAsync(),
        }));
        var storedInclude = ReadStoredInclusion(path);

        DataManager.Clear(DataClearMode.ResetSession);
        Assert.True((await DataReader.ReadPathsAsync(new[] { path })).OpenedCleanProject);
        var restored = Assert.Single(DataManager.Data);
        Assert.NotSame(original, restored);
        Assert.Equal(original.UniqueID, restored.UniqueID);
        Assert.Equal(integrated, restored.CanBeAnalyzed);
        Assert.Equal(integrated && expectedInclude, restored.Include);
        Assert.Equal(integrated, Assert.Single(restored.Injections).IsIntegrated);
        if (integrated) AssertProcessed(restored);

        // Complete the real processing sequence before checking the saved choice.
        await restored.Processor.ProcessData(showProgress: false);
        await control.Processor.ProcessData(showProgress: false);
        AssertProcessed(restored);
        AssertProcessed(control);
        output.WriteLine($"Stored inclusion: {storedInclude}; after integration: reopened={restored.Include}, unsaved={control.Include}");
        Assert.Equal(expectedInclude, control.Include);
        Assert.Equal(expectedInclude, restored.Include);
        Assert.Equal(expectedInclude, storedInclude);
    }

    static ExperimentData RawExperiment(string name, string choice)
    {
        var data = new ExperimentData(name + ".itc")
        {
            Name = name,
            CellVolume = 200e-6,
            CellConcentration = new FloatWithError(20e-6),
            SyringeConcentration = new FloatWithError(200e-6),
            TargetTemperature = 25,
        };
        for (var time = 0; time <= 40; time++)
            data.DataPoints.Add(new DataPoint(time, time > 10 && time <= 16 ? 3 : 1));
        var injection = InjectionData.FromPEAQFile(data, 0, true, 10, 2e-6, 20, 2, 25);
        injection.SetIntegrationLengthByTime(6);
        data.Injections.Add(injection);
        if (choice != "default")
        {
            data.Include = false;
            if (choice == "included") data.Include = true;
        }
        data.Processor.DiscardIntegratedPoints = true;
        data.Processor.Interpolator = new PolynomialLeastSquaresInterpolator(data.Processor) { Degree = 0 };
        return data;
    }

    static void AssertProcessed(ExperimentData data)
    {
        Assert.True(data.Processor.BaselineCompleted);
        Assert.True(data.Processor.IntegrationCompleted);
        Assert.True(data.CanBeAnalyzed);
        Assert.Equal(data.DataPoints.Count, data.BaseLineCorrectedDataPoints.Count);
        var injection = Assert.Single(data.Injections);
        Assert.True(injection.IsIntegrated);
        // Six one-second intervals at 2 W above baseline give 12 J.
        Assert.Equal(12, injection.RawPeakArea.Value, 10);
    }

    static bool ReadStoredInclusion(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var stream = archive.GetEntry("experiments/000000/experiment.json").Open();
        using var metadata = JsonDocument.Parse(stream);
        return metadata.RootElement.GetProperty("included").GetBoolean();
    }

    sealed class FixedPrompt(string path) : IFileSavePromptService
    {
        public Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes) => Task.FromResult(path);
    }
}
