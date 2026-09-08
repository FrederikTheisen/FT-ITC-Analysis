using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(ProjectWriterSaveSelectedCollectionDefinition.Name)]
public sealed class ProjectWriterTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ProjectWriterTests-" + Guid.NewGuid().ToString("N"));
    readonly ISettingsStore originalStore = PlatformServices.SettingsStore;
    readonly IFileSavePromptService originalPrompt = PlatformServices.FileSavePromptService;
    readonly string originalLastPath = AppSettings.LastDocumentPath;

    public ProjectWriterTests()
    {
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

    [Fact]
    public async Task SaveAndSaveAsUpdatePathAndCleanStateWhileSaveWithPathOverwrites()
    {
        await LoadDocument();
        var first = Path.Combine(directory, "first.ftxtc");
        var second = Path.Combine(directory, "second.ftxtc");
        var expectedIds = DataManager.SourceItems.Select(item => item.UniqueID).ToArray();
        PlatformServices.RegisterFileSavePromptService(new FixedPrompt(first));

        Assert.True(await ProjectWriter.SaveAsync());
        Assert.True(ProjectWriter.IsSaved);
        Assert.False(ProjectWriter.IsWriteInProgress);
        Assert.False(DocumentDirtyTracker.IsDirty);
        Assert.Equal(first, ProjectDocumentState.Path);
        Assert.Equal(first, AppSettings.LastDocumentPath);
        Assert.Equal(expectedIds, (await Read(first)).Select(item => item.UniqueID));

        var experiment = DataManager.Data.First();
        experiment.Name = "Updated experiment";
        DocumentDirtyTracker.MarkDirty();
        Assert.True(await ProjectWriter.SaveWithPathAsync());
        Assert.False(DocumentDirtyTracker.IsDirty);
        Assert.Contains(await Read(first), item => item.Name == "Updated experiment");

        DocumentDirtyTracker.MarkDirty();
        PlatformServices.RegisterFileSavePromptService(new FixedPrompt(second));
        Assert.True(await ProjectWriter.SaveAsync());
        Assert.False(DocumentDirtyTracker.IsDirty);
        Assert.Equal(second, ProjectDocumentState.Path);
        Assert.Equal(second, AppSettings.LastDocumentPath);
        Assert.True(File.Exists(first));
        Assert.Equal(expectedIds, (await Read(second)).Select(item => item.UniqueID));
    }

    [Fact]
    public async Task CancelAndFailedSavesPreserveDocumentStateAndAllowRetry()
    {
        await LoadDocument();
        var current = Path.Combine(directory, "current.ftxtc");
        ProjectDocumentState.Path = current;
        var lastPath = AppSettings.LastDocumentPath;
        PlatformServices.RegisterFileSavePromptService(new FixedPrompt(null));
        Assert.False(await ProjectWriter.SaveAsync());
        Assert.True(DocumentDirtyTracker.IsDirty);
        Assert.Equal(current, ProjectDocumentState.Path);
        Assert.Equal(lastPath, AppSettings.LastDocumentPath);
        Assert.Empty(Directory.GetFiles(directory));

        var legacyPath = Path.Combine(directory, "legacy.ftitc");
        PlatformServices.RegisterFileSavePromptService(new FixedPrompt(legacyPath));
        Assert.False(await ProjectWriter.SaveAsync());
        Assert.False(File.Exists(legacyPath));
        Assert.False(ProjectWriter.IsWriteInProgress);
        Assert.True(DocumentDirtyTracker.IsDirty);
        Assert.Equal(current, ProjectDocumentState.Path);

        var blocker = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(blocker, "blocks directory creation");
        ProjectDocumentState.Path = Path.Combine(blocker, "failed.ftxtc");
        Assert.False(await ProjectWriter.SaveWithPathAsync());
        Assert.False(ProjectWriter.IsWriteInProgress);
        Assert.True(DocumentDirtyTracker.IsDirty);

        ProjectDocumentState.Path = current;
        Assert.True(await ProjectWriter.SaveWithPathAsync());
        Assert.False(DocumentDirtyTracker.IsDirty);
    }

    [Fact]
    public async Task SelectedExperimentAndResultSavesLeaveActiveDocumentDirtyAndAtItsOriginalPath()
    {
        await LoadDocument();
        ProjectDocumentState.Path = Path.Combine(directory, "active.ftxtc");
        var lastPath = AppSettings.LastDocumentPath;
        foreach (var selected in new ITCDataContainer[] { DataManager.Data.First(), DataManager.Results.First() })
        {
            var path = Path.Combine(directory, selected.UniqueID + ".ftxtc");
            PlatformServices.RegisterFileSavePromptService(new FixedPrompt(path));
            Assert.True(await ProjectWriter.SaveSelectedAsync(selected));
            Assert.True(DocumentDirtyTracker.IsDirty);
            Assert.Equal(Path.Combine(directory, "active.ftxtc"), ProjectDocumentState.Path);
            Assert.Equal(lastPath, AppSettings.LastDocumentPath);
            var restored = await Read(path);
            if (selected is ExperimentData experiment)
            {
                Assert.Equal(experiment.UniqueID, Assert.Single(restored.OfType<ExperimentData>()).UniqueID);
                Assert.Empty(restored.OfType<AnalysisResult>());
            }
            else
            {
                var result = (AnalysisResult)selected;
                Assert.Equal(result.UniqueID, Assert.Single(restored.OfType<AnalysisResult>()).UniqueID);
                Assert.Equal(result.Solution.Solutions.Select(solution => solution.Data.UniqueID).Distinct().OrderBy(id => id),
                    restored.OfType<ExperimentData>().Select(item => item.UniqueID).OrderBy(id => id));
            }
        }
    }

    [Fact]
    public async Task AutoSaveRecoversAfterDirectoryCreationSerializationAndMoveFailures()
    {
        await LoadDocument();
        var originalPath = ProjectDocumentState.Path;
        var lastPath = AppSettings.LastDocumentPath;
        var blocker = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(blocker, "blocks directory creation");
        await Assert.ThrowsAnyAsync<IOException>(() => ProjectWriter.WriteAutoSaveAsync(Path.Combine(blocker, "auto.ftxtc")));
        Assert.False(ProjectWriter.IsWriteInProgress);

        var experiment = DataManager.Data.First();
        var originalInstrument = experiment.Instrument;
        try
        {
            experiment.Instrument = (ITCInstrument)int.MaxValue;
            await Assert.ThrowsAsync<NotSupportedException>(() => ProjectWriter.WriteAutoSaveAsync(Path.Combine(directory, "invalid.ftxtc")));
            Assert.False(ProjectWriter.IsWriteInProgress);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
        }
        finally
        {
            experiment.Instrument = originalInstrument;
        }

        var invalidDestination = Path.Combine(directory, "directory.ftxtc");
        Directory.CreateDirectory(invalidDestination);
        await Assert.ThrowsAnyAsync<IOException>(() => ProjectWriter.WriteAutoSaveAsync(invalidDestination));
        Assert.False(ProjectWriter.IsWriteInProgress);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));

        var valid = Path.Combine(directory, "autosave", "valid.ftxtc");
        Assert.True(await ProjectWriter.WriteAutoSaveAsync(valid));
        Assert.True(await ProjectWriter.WriteAutoSaveAsync(valid));
        Assert.False(ProjectWriter.IsWriteInProgress);
        Assert.True(DocumentDirtyTracker.IsDirty);
        Assert.Equal(originalPath, ProjectDocumentState.Path);
        Assert.Equal(lastPath, AppSettings.LastDocumentPath);
        Assert.Equal(DataManager.SourceItems.Count, (await Read(valid)).Count);
    }

    static async Task LoadDocument()
    {
        await DataReader.ReadPathsAsync(new[] { Path.Combine(AppContext.BaseDirectory, "Fixtures", "one-set.ftitc") });
        DocumentDirtyTracker.MarkDirty();
    }

    static async Task<List<ITCDataContainer>> Read(string path)
    {
        using var stream = File.OpenRead(path);
        return (await FTXTCReader.ReadStream(stream)).ToList();
    }

    sealed class FixedPrompt(string path) : IFileSavePromptService
    {
        public Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes) =>
            Task.FromResult(path);
    }
}
