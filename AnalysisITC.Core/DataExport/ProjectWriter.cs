using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Platform;

namespace AnalysisITC.Core.Export
{
    /// <summary>
    /// Coordinates native project saves and autosaves through a single write gate.
    /// </summary>
    public static class ProjectWriter
    {
        static readonly SemaphoreSlim SaveGate = new SemaphoreSlim(1, 1);

        readonly struct SaveOutcome
        {
            public bool Wrote { get; }
            public bool MarkedClean { get; }
            public DocumentSaveStamp Stamp { get; }

            public SaveOutcome(bool wrote, bool markedClean, DocumentSaveStamp stamp)
            {
                Wrote = wrote;
                MarkedClean = markedClean;
                Stamp = stamp;
            }
        }

        public static bool IsSaved => !string.IsNullOrEmpty(ProjectDocumentState.Path);
        public static bool IsWriteInProgress => SaveGate.CurrentCount == 0;

        public static void Save()
        {
            _ = SaveAsync();
        }

        public static async Task<bool> SaveAsync()
        {
            return await SaveAsync(null);
        }

        internal static async Task<bool> SaveAsync(Func<Task> afterSnapshotCaptured)
        {
            var request = DocumentDirtyTracker.CaptureSaveStamp();
            var path = await PlatformServices.FileSavePromptService.ChooseSaveFilePathAsync("Save FT-ITC Project", new[] { "ftxtc" });
            if (!DocumentDirtyTracker.IsCurrentDocument(request)) return CancelChangedDocument();
            if (string.IsNullOrWhiteSpace(path)) return false;
            return (await SaveCore(request, path, saveAs: true, afterSnapshotCaptured: afterSnapshotCaptured)).Wrote;
        }

        public static void SaveWithPath()
        {
            _ = SaveWithPathAsync();
        }

        public static async Task<bool> SaveWithPathAsync()
        {
            return await SaveWithPathAsync(null);
        }

        internal static async Task<bool> SaveWithPathAsync(Func<Task> afterSnapshotCaptured)
        {
            var request = DocumentDirtyTracker.CaptureSaveStamp();
            return (await SaveCore(request, null, saveAs: false, afterSnapshotCaptured: afterSnapshotCaptured)).Wrote;
        }

        public static async Task<bool> SaveForCloseAsync()
        {
            return await SaveForCloseAsync(null);
        }

        internal static async Task<bool> SaveForCloseAsync(Func<Task> afterSnapshotCaptured)
        {
            var request = DocumentDirtyTracker.CaptureSaveStamp();
            var saveAs = string.IsNullOrWhiteSpace(ProjectDocumentState.Path);
            var path = saveAs
                ? await PlatformServices.FileSavePromptService.ChooseSaveFilePathAsync("Save FT-ITC Project", new[] { "ftxtc" })
                : null;
            if (!DocumentDirtyTracker.IsCurrentDocument(request)) return CancelChangedDocument();
            if (saveAs && string.IsNullOrWhiteSpace(path)) return false;
            var outcome = await SaveCore(request, path, saveAs, afterSnapshotCaptured);

            if (!outcome.Wrote) return false;
            if (!DocumentDirtyTracker.IsCurrentDocument(request))
            {
                SetStatus("The active document changed while saving. The document was kept open.");
                return false;
            }
            if (DocumentDirtyTracker.IsSuspended || DocumentDirtyTracker.IsRestoringDocument)
            {
                SetStatus("Saved, but document updates are still in progress. The document was kept open.");
                return false;
            }
            if (!outcome.MarkedClean || !DocumentDirtyTracker.MatchesSaveStamp(outcome.Stamp) || DocumentDirtyTracker.IsDirty)
            {
                SetStatus("Saved, but newer changes remain unsaved. The document was kept open.");
                return false;
            }
            return true;
        }

        static async Task<SaveOutcome> SaveCore(
            DocumentSaveStamp request,
            string requestedPath,
            bool saveAs,
            Func<Task> afterSnapshotCaptured)
        {
            string path = requestedPath;
            var gateAcquired = false;
            try
            {
                await SaveGate.WaitAsync();
                gateAcquired = true;
                if (!DocumentDirtyTracker.IsCurrentDocument(request))
                    return CancelChangedDocumentOutcome();

                if (!saveAs) path = ProjectDocumentState.Path;
                if (string.IsNullOrWhiteSpace(path)) return new(false, false, default);
                StatusBarManager.SetSavingFileMessage(path);

                var stamp = DocumentDirtyTracker.CaptureSaveStamp();
                if (!DocumentDirtyTracker.IsCurrentDocument(request))
                    return CancelChangedDocumentOutcome();
                await WriteFile(path, afterSnapshotCaptured);

                if (!DocumentDirtyTracker.IsCurrentDocument(stamp))
                {
                    StatusBarManager.ClearAppStatus();
                    SetStatus("The active document changed while saving.");
                    return new(true, false, stamp);
                }

                if (saveAs)
                {
                    ProjectDocumentState.Path = path;
                    AppSettings.LastDocumentPath = path;
                }
                var clean = DocumentDirtyTracker.TryMarkClean(stamp);
                if (clean) StatusBarManager.SetFileSaveSuccessfulMessage(path);
                else if (DocumentDirtyTracker.IsCurrentDocument(stamp) && !DocumentDirtyTracker.MatchesSaveStamp(stamp))
                {
                    StatusBarManager.ClearAppStatus();
                    SetStatus("Saved, but newer changes remain unsaved.");
                }
                else if (DocumentDirtyTracker.IsCurrentDocument(stamp))
                {
                    StatusBarManager.ClearAppStatus();
                    SetStatus("Saved, but document updates are still in progress.");
                }
                return new(true, clean, stamp);
            }
            catch (Exception ex)
            {
                ReportSaveFailure(path, ex);
                return new(false, false, default);
            }
            finally
            {
                if (gateAcquired) SaveGate.Release();
            }
        }

        public static void SaveSelected(ITCDataContainer data)
        {
            _ = SaveSelectedAsync(data);
        }

        public static async Task<bool> SaveSelectedAsync(ITCDataContainer data)
        {
            var title = "Save FT-ITC " + (data is ExperimentData ? "Experiment Data" : "Analysis Result");
            var allowedFileTypes = new[] { "ftxtc" };
            var path = await PlatformServices.FileSavePromptService.ChooseSaveFilePathAsync(title, allowedFileTypes);
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                StatusBarManager.SetSavingFileMessage(path);
                await SaveGate.WaitAsync();
                try
                {
                    switch (data)
                    {
                        case ExperimentData experiment:
                            await FTXTCWriter.WriteFileAsync(path, new[] { experiment });
                            break;
                        case AnalysisResult result:
                            await FTXTCWriter.WriteFileAsync(
                                path,
                                result.Solution.Solutions.Select(solution => solution.Data).Distinct(),
                                new[] { result });
                            break;
                    }
                }
                finally
                {
                    SaveGate.Release();
                }

                StatusBarManager.SetFileSaveSuccessfulMessage(path);
                return true;
            }
            catch (Exception ex)
            {
                ReportSaveFailure(path, ex);
                return false;
            }
        }

        public static async Task<bool> WriteAutoSaveAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An autosave path is required.", nameof(path));
            if (!await SaveGate.WaitAsync(0)) return false;

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

                var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await FTXTCWriter.WriteStream(stream, DataManager.Data, DataManager.Results, DataManager.SourceItems, DataManager.Reports);

                    if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                    else File.Move(temporaryPath, path);

                    return true;
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
            }
            finally
            {
                SaveGate.Release();
            }
        }

        static async Task WriteFile(string path, Func<Task> afterSnapshotCaptured = null)
        {
            if (!IsFtxtcPath(path)) throw new InvalidOperationException("Projects can only be saved in native .ftxtc format.");
            if (afterSnapshotCaptured == null)
                await FTXTCWriter.WriteFileAsync(path, DataManager.Data, DataManager.Results, DataManager.SourceItems, DataManager.Reports);
            else
                await FTXTCWriter.WriteFileAsync(path, DataManager.Data, DataManager.Results, DataManager.SourceItems, DataManager.Reports, afterSnapshotCaptured);
        }

        static bool CancelChangedDocument()
        {
            SetStatus("Save cancelled because the open document changed.");
            return false;
        }

        static SaveOutcome CancelChangedDocumentOutcome()
        {
            CancelChangedDocument();
            return new(false, false, default);
        }

        static void SetStatus(string message) => StatusBarManager.SetStatus(message, 5000);

        static void ReportSaveFailure(string path, Exception exception)
        {
            AppEventHandler.DisplayHandledException(exception);
            PlatformServices.MainThreadDispatcher.Invoke(() =>
                StatusBarManager.SetFileSaveFailedMessage(path));
        }

        static bool IsFtxtcPath(string path) =>
            string.Equals(Path.GetExtension(path), FTXTCFormat.Extension, StringComparison.OrdinalIgnoreCase);
    }
}
