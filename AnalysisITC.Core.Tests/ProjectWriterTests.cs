using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(ProjectWriterSaveSelectedCollectionDefinition.Name)]
public sealed class ProjectWriterTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ProjectWriterTests-" + Guid.NewGuid().ToString("N"));
    readonly ISettingsStore originalStore = PlatformServices.SettingsStore;
    readonly IFileSavePromptService originalPrompt = PlatformServices.FileSavePromptService;
    readonly IFtxtcDuplicatePromptService originalDuplicatePrompt = PlatformServices.FtxtcDuplicatePromptService;
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
        PlatformServices.RegisterFtxtcDuplicatePromptService(originalDuplicatePrompt);
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
    public async Task SaveForCloseKeepsDocumentOpenWhenAnEditFollowsSnapshotCapture()
    {
        await LoadDocument();
        var path = Path.Combine(directory, "close-race.ftxtc");
        var originalName = DataManager.Data.First().Name;
        PlatformServices.RegisterFileSavePromptService(new FixedPrompt(path));
        var statuses = new List<string>();
        void OnStatus(object sender, string value) => statuses.Add(value);
        StatusBarManager.StatusUpdated += OnStatus;
        try
        {
            var closed = await ProjectWriter.SaveForCloseAsync(async () =>
            {
                DataManager.Data.First().Name = "First edit during write";
                DataManager.Data.First().Name = "Edited during write";
                await Task.CompletedTask;
            });

            Assert.False(closed);
            Assert.True(File.Exists(path));
            Assert.True(DocumentDirtyTracker.IsDirty);
            Assert.Equal("Edited during write", DataManager.Data.First().Name);
            Assert.Contains(await Read(path), item => item.Name == originalName);
            Assert.DoesNotContain(await Read(path), item => item.Name == "Edited during write");
            Assert.Contains(statuses, message => message == "Saved, but newer changes remain unsaved. The document was kept open.");
        }
        finally
        {
            StatusBarManager.StatusUpdated -= OnStatus;
        }
    }

    [Fact]
    public async Task SaveWhoseDocumentIsReplacedAfterSnapshotDoesNotUpdateReplacementState()
    {
        await LoadDocument();
        var path = Path.Combine(directory, "replacement.ftxtc");
        ProjectDocumentState.Path = path;
        var lastPath = AppSettings.LastDocumentPath;
        var statuses = CaptureStatuses();

        try
        {
            Assert.True(await ProjectWriter.SaveWithPathAsync(() =>
            {
                DataManager.Clear(DataClearMode.ResetSession);
                ProjectDocumentState.Path = string.Empty;
                return Task.CompletedTask;
            }));

            Assert.True(File.Exists(path));
            Assert.Empty(DataManager.SourceItems);
            Assert.Equal(string.Empty, ProjectDocumentState.Path);
            Assert.Equal(lastPath, AppSettings.LastDocumentPath);
            Assert.Contains("The active document changed while saving.", statuses.Messages);
            Assert.NotEqual(-0.5, StatusBarManager.Progress);
            Assert.False(ProjectWriter.IsWriteInProgress);
        }
        finally
        {
            StatusBarManager.StatusUpdated -= statuses.Handler;
        }
    }

    [Fact]
    public async Task SaveForCloseDoesNotApproveAnEditMadeDuringSuspensionAfterScopeEnds()
    {
        await LoadDocument();
        DocumentDirtyTracker.MarkClean();
        var path = Path.Combine(directory, "suspended-edit.ftxtc");
        ProjectDocumentState.Path = path;

        var closed = await ProjectWriter.SaveForCloseAsync(() =>
        {
            using (DocumentDirtyTracker.Suspend())
            {
                DataManager.Data.First().Name = "Changed while suspended";
            }
            return Task.CompletedTask;
        });

        Assert.False(closed);
        Assert.True(DocumentDirtyTracker.IsDirty);
        Assert.Equal("Changed while suspended", DataManager.Data.First().Name);
    }

    [Fact]
    public async Task SaveAndSaveForCloseCancelWhenDocumentChangesDuringSavePathPrompt()
    {
        await LoadDocument();
        var path = Path.Combine(directory, "stale-prompt.ftxtc");
        foreach (var close in new[] { false, true })
        {
            DataManager.Clear(DataClearMode.ResetSession);
            ProjectDocumentState.Path = string.Empty;
            await LoadDocument();
            var prompt = new DeferredPrompt();
            PlatformServices.RegisterFileSavePromptService(prompt);
            var statuses = CaptureStatuses();
            try
            {
                var request = close ? ProjectWriter.SaveForCloseAsync() : ProjectWriter.SaveAsync();
                await prompt.Called.Task;
                DataManager.Clear(DataClearMode.ResetSession);
                prompt.Complete(path);

                Assert.False(await request);
                Assert.False(File.Exists(path));
                Assert.Contains("Save cancelled because the open document changed.", statuses.Messages);
            }
            finally
            {
                StatusBarManager.StatusUpdated -= statuses.Handler;
            }
        }
    }

    [Fact]
    public async Task QueuedSaveIsCancelledWhenTheDocumentChangesBeforeItGetsTheGate()
    {
        await LoadDocument();
        var activePath = Path.Combine(directory, "active-write.ftxtc");
        var queuedPath = Path.Combine(directory, "queued-write.ftxtc");
        ProjectDocumentState.Path = activePath;
        var snapshotCaptured = NewSignal();
        var continueWrite = NewSignal();
        var activeSave = ProjectWriter.SaveWithPathAsync(async () =>
        {
            snapshotCaptured.TrySetResult(true);
            await continueWrite.Task;
        });
        await snapshotCaptured.Task;

        PlatformServices.RegisterFileSavePromptService(new FixedPrompt(queuedPath));
        var queuedSave = ProjectWriter.SaveAsync();
        Assert.False(queuedSave.IsCompleted);
        DataManager.Clear(DataClearMode.ResetSession);
        continueWrite.TrySetResult(true);

        Assert.True(await activeSave);
        Assert.False(await queuedSave);
        Assert.True(File.Exists(activePath));
        Assert.False(File.Exists(queuedPath));
        Assert.False(ProjectWriter.IsWriteInProgress);
    }

    [Fact]
    public async Task QueuedSaveWithPathUsesTheDestinationAndContentsFromThePrecedingSaveAs()
    {
        await LoadDocument();
        var firstPath = Path.Combine(directory, "save-as-first.ftxtc");
        var oldPath = Path.Combine(directory, "old-active.ftxtc");
        ProjectDocumentState.Path = oldPath;
        PlatformServices.RegisterFileSavePromptService(new FixedPrompt(firstPath));
        var snapshotCaptured = NewSignal();
        var continueWrite = NewSignal();
        var saveAs = ProjectWriter.SaveAsync(async () =>
        {
            snapshotCaptured.TrySetResult(true);
            await continueWrite.Task;
        });
        await snapshotCaptured.Task;

        var save = ProjectWriter.SaveWithPathAsync();
        Assert.False(save.IsCompleted);
        DataManager.Data.First().Name = "Latest queued content";
        continueWrite.TrySetResult(true);

        Assert.True(await saveAs);
        Assert.True(await save);
        Assert.True(File.Exists(firstPath));
        Assert.False(File.Exists(oldPath));
        Assert.Equal(firstPath, ProjectDocumentState.Path);
        Assert.Contains(await Read(firstPath), item => item.Name == "Latest queued content");
        Assert.False(DocumentDirtyTracker.IsDirty);
    }

    [Fact]
    public async Task DataReaderAppendDuringSaveRetainsDocumentIdentityAndPreventsClose()
    {
        PlatformServices.RegisterFtxtcDuplicatePromptService(
            new FtxtcImportResolverTests.RecordingPrompt(FtxtcDuplicateAction.ImportCopies));
        await LoadDocument();
        var appendPath = Path.Combine(directory, "append-source.ftxtc");
        await FTXTCWriter.WriteFileAsync(appendPath, DataManager.Data, DataManager.Results, DataManager.SourceItems, DataManager.Reports);
        var savePath = Path.Combine(directory, "append-save.ftxtc");
        ProjectDocumentState.Path = savePath;
        var captured = DataManager.SourceItems.Count;
        var originalIds = DataManager.SourceItems.Select(item => item.UniqueID).ToHashSet();
        var snapshotCaptured = NewSignal();
        var continueWrite = NewSignal();

        var save = ProjectWriter.SaveForCloseAsync(async () =>
        {
            snapshotCaptured.TrySetResult(true);
            await continueWrite.Task;
        });
        await snapshotCaptured.Task;
        await DataReader.ReadPathsAsync(new[] { appendPath });
        Assert.True(DataManager.SourceItems.Count > captured);
        Assert.True(originalIds.IsSubsetOf(DataManager.SourceItems.Select(item => item.UniqueID)));
        continueWrite.TrySetResult(true);

        Assert.False(await save);
        Assert.True(DocumentDirtyTracker.IsDirty);
        Assert.True(File.Exists(savePath));
        Assert.Equal(appendPath, ProjectDocumentState.Path);
        Assert.Equal(appendPath, AppSettings.LastDocumentPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveForCloseKeepsDocumentOpenWhileUpdateScopeIsActive(bool restoring)
    {
        await LoadDocument();
        var path = Path.Combine(directory, "restoring.ftxtc");
        ProjectDocumentState.Path = path;
        var statuses = CaptureStatuses();
        IDisposable restoration = null;
        try
        {
            var closed = await ProjectWriter.SaveForCloseAsync(() =>
            {
                restoration = restoring ? DocumentDirtyTracker.RestoreDocument() : DocumentDirtyTracker.Suspend();
                return Task.CompletedTask;
            });

            Assert.False(closed);
            Assert.Contains("Saved, but document updates are still in progress. The document was kept open.", statuses.Messages);
            Assert.True(DocumentDirtyTracker.IsSuspended);
            Assert.Equal(restoring, DocumentDirtyTracker.IsRestoringDocument);
        }
        finally
        {
            restoration?.Dispose();
            StatusBarManager.StatusUpdated -= statuses.Handler;
        }
    }

    [Fact]
    public async Task ReopeningTheSameFilenameDuringWriteDoesNotCleanTheReplacementDocument()
    {
        await LoadDocument();
        var path = Path.Combine(directory, "same-filename.ftxtc");
        var originalName = DataManager.Data.First().Name;
        await FTXTCWriter.WriteFileAsync(path, DataManager.Data, DataManager.Results, DataManager.SourceItems, DataManager.Reports);
        ProjectDocumentState.Path = path;
        var save = ProjectWriter.SaveWithPathAsync(async () =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            ProjectDocumentState.Path = string.Empty;
            var read = await DataReader.ReadPathsAsync(new[] { path });
            Assert.True(read.OpenedCleanProject);
            DataManager.Data.First().Name = "Replacement edit during save";
            ProjectDocumentState.Path = path;
        });

        Assert.True(await save);
        Assert.Equal(path, ProjectDocumentState.Path);
        Assert.True(DocumentDirtyTracker.IsDirty);
        Assert.NotEmpty(DataManager.SourceItems);
        Assert.Contains(await Read(path), item => item.Name == originalName);
    }

    [Fact]
    public async Task ReportEditAfterSnapshotRemainsUnsaved()
    {
        await LoadDocument();
        var report = new AnalysisReport();
        DataManager.AddReport(report);
        DocumentDirtyTracker.MarkClean();
        ProjectDocumentState.Path = Path.Combine(directory, "report-edit.ftxtc");

        var closed = await ProjectWriter.SaveForCloseAsync(() =>
        {
            report.AuthorComments = "Changed after snapshot";
            return Task.CompletedTask;
        });

        Assert.False(closed);
        Assert.True(DocumentDirtyTracker.IsDirty);
        Assert.Equal("Changed after snapshot", report.AuthorComments);
        using var stream = File.OpenRead(ProjectDocumentState.Path);
        var savedProject = await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict);
        Assert.Equal(string.Empty, Assert.Single(savedProject.Reports).AuthorComments);
    }

    [Fact]
    public async Task AutosaveRemainsEligibleAfterAnEditArrivesDuringTheSave()
    {
        await LoadDocument();
        var enabled = AppSettings.AutoSaveEnabled;
        AutoSaveManager manager = null;
        try
        {
            var path = Path.Combine(directory, "late-edit.ftxtc");
            ProjectDocumentState.Path = path;
            Assert.True(await ProjectWriter.SaveWithPathAsync(async () =>
            {
                DataManager.Data.First().Name = "Late edit";
                await Task.CompletedTask;
            }));
            Assert.True(DocumentDirtyTracker.IsDirty);
            Assert.Equal(-1, StatusBarManager.Progress);

            AppSettings.AutoSaveEnabled = true;
            manager = new AutoSaveManager(Path.Combine(directory, "autosave"));
            manager.Start();
            Assert.True(await manager.TickNowAsync());
            Assert.Single(Directory.GetFiles(manager.AutoSaveDirectory, "*.ftxtc"));
        }
        finally
        {
            manager?.StopCleanly();
            AppSettings.AutoSaveEnabled = enabled;
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

    static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    static (List<string> Messages, EventHandler<string> Handler) CaptureStatuses()
    {
        var messages = new List<string>();
        void Handler(object sender, string message) => messages.Add(message);
        StatusBarManager.StatusUpdated += Handler;
        return (messages, Handler);
    }

    sealed class DeferredPrompt : IFileSavePromptService
    {
        readonly TaskCompletionSource<string> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Called { get; } = NewSignal();

        public Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes)
        {
            Called.TrySetResult(true);
            return response.Task;
        }

        public void Complete(string path) => response.TrySetResult(path);
    }

    sealed class FixedPrompt(string path) : IFileSavePromptService
    {
        public Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes) =>
            Task.FromResult(path);
    }
}
