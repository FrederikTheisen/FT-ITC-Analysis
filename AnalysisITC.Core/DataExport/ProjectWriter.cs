using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AnalysisITC.Core.Analysis;
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
            if (ProcessingActivity.IsBusy) return CancelBusySave();
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
            if (ProcessingActivity.IsBusy) return CancelBusySave();
            var request = DocumentDirtyTracker.CaptureSaveStamp();
            return (await SaveCore(request, null, saveAs: false, afterSnapshotCaptured: afterSnapshotCaptured)).Wrote;
        }

        public static async Task<bool> SaveForCloseAsync()
        {
            return await SaveForCloseAsync(null);
        }

        internal static async Task<bool> SaveForCloseAsync(Func<Task> afterSnapshotCaptured)
        {
            if (ProcessingActivity.IsBusy) return CancelBusySave();
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
            if (ProcessingActivity.IsBusy || DocumentDirtyTracker.IsSuspended || DocumentDirtyTracker.IsRestoringDocument)
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

                // Let both desktop status bars render before synchronous snapshot preparation.
                await Task.Delay(50);

                var stamp = DocumentDirtyTracker.CaptureSaveStamp();
                if (!DocumentDirtyTracker.IsCurrentDocument(request))
                {
                    StatusBarManager.ClearAppStatus();
                    return CancelChangedDocumentOutcome();
                }
                if (!IsFtxtcPath(path)) throw new InvalidOperationException("Projects can only be saved in native .ftxtc format.");
                if (!ProcessingActivity.TryCaptureSnapshot(CaptureDocument, out var snapshot))
                {
                    CancelBusySave();
                    return new(false, false, default);
                }
                await FTXTCWriter.WriteSnapshotFileAsync(path, snapshot, afterSnapshotCaptured);

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
            if (ProcessingActivity.IsBusy) return CancelBusySave();
            var title = "Save FT-ITC " + (data is ExperimentData ? "Experiment Data" : "Analysis Result");
            var allowedFileTypes = new[] { "ftxtc" };
            var path = await PlatformServices.FileSavePromptService.ChooseSaveFilePathAsync(title, allowedFileTypes);
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                var options = new FtxtcWriteOptions { StoreUnavailableBufferReferences = true };
                await SaveGate.WaitAsync();
                try
                {
                    StatusBarManager.SetSavingFileMessage(path);
                    await Task.Delay(50);
                    if (!ProcessingActivity.TryCaptureSnapshot(() => CaptureSelected(data, options), out var snapshot))
                        return CancelBusySave();
                    if (snapshot == null) return false;
                    await FTXTCWriter.WriteSnapshotFileAsync(path, snapshot);
                }
                finally
                {
                    SaveGate.Release();
                }

                if (options.StoredBufferReferenceNames.Count == 0) StatusBarManager.SetFileSaveSuccessfulMessage(path);
                else StatusBarManager.SetFileSaveWithStoredBufferReferencesMessage(path, options.StoredBufferReferenceNames);
                return true;
            }
            catch (Exception ex)
            {
                ReportSaveFailure(path, ex);
                return false;
            }
        }

        // Result saves also keep loaded competitor source results, recursively, with their members and buffer references.
        static (List<ExperimentData> Experiments, List<AnalysisResult> Results) WithDependencies(AnalysisResult selected)
        {
            var experiments = new List<ExperimentData>();
            var results = new List<AnalysisResult>();
            var pending = new Queue<AnalysisResult>();
            pending.Enqueue(selected);
            while (pending.Count > 0)
            {
                var result = pending.Dequeue();
                if (results.Contains(result)) continue; // Reference cycles end at a result already written.
                results.Add(result);
                var members = result.Solution.Solutions.Select(solution => solution.Data).Distinct().ToList();
                foreach (var experiment in WithBufferReferences(members).Where(experiment => !experiments.Contains(experiment)))
                {
                    experiments.Add(experiment);
                    var sourceId = experiment.Attributes.FirstOrDefault(attribute => attribute.Key == AttributeKey.CompetitorResult)?.StringValue;
                    var source = DataManager.Results.FirstOrDefault(item => item.UniqueID == sourceId);
                    if (source != null) pending.Enqueue(source);
                }
            }
            return (experiments, results);
        }

        static List<ExperimentData> WithBufferReferences(IReadOnlyList<ExperimentData> experiments)
        {
            // Result saves keep loaded buffer references as experiments so the result stays reproducible.
            var written = experiments.ToList();
            foreach (var reference in experiments.Select(experiment => experiment.ReferenceExperiment))
                if (reference != null && !written.Contains(reference)) written.Add(reference);
            return written;
        }

        public static async Task<bool> WriteAutoSaveAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An autosave path is required.", nameof(path));
            if (!await SaveGate.WaitAsync(0)) return false;
            try
            {
                if (!ProcessingActivity.TryCaptureSnapshot(CaptureDocument, out var snapshot)) return false;
                await FTXTCWriter.WriteSnapshotFileAsync(path, snapshot);
                return true;
            }
            finally
            {
                SaveGate.Release();
            }
        }

        static FTXTCWriter.Snapshot CaptureDocument() => FTXTCWriter.CaptureSnapshot(
            DataManager.Data, DataManager.Results, DataManager.SourceItems, DataManager.Reports);

        static FTXTCWriter.Snapshot CaptureSelected(ITCDataContainer data, FtxtcWriteOptions options)
        {
            switch (data)
            {
                case ExperimentData experiment:
                    return FTXTCWriter.CaptureSnapshot(new[] { experiment }, options: options);
                case AnalysisResult result:
                    var (experiments, results) = WithDependencies(result);
                    return FTXTCWriter.CaptureSnapshot(experiments, results, options: options);
                default:
                    return null;
            }
        }

        static bool CancelBusySave()
        {
            StatusBarManager.SetStatus("Save after fitting or processing finishes.", 5000, priority: 2);
            return false;
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
