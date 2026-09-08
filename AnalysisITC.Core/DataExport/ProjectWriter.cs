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

        public static bool IsSaved => !string.IsNullOrEmpty(ProjectDocumentState.Path);
        public static bool IsWriteInProgress => SaveGate.CurrentCount == 0;

        public static void Save()
        {
            _ = SaveAsync();
        }

        public static async Task<bool> SaveAsync()
        {
            var path = await PlatformServices.FileSavePromptService.ChooseSaveFilePathAsync("Save FT-ITC Project", new[] { "ftxtc" });
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                StatusBarManager.SetSavingFileMessage(path);
                await SaveGate.WaitAsync();
                try
                {
                    await WriteFile(path);
                }
                finally
                {
                    SaveGate.Release();
                }

                ProjectDocumentState.Path = path;
                AppSettings.LastDocumentPath = path;
                DocumentDirtyTracker.MarkClean();
                StatusBarManager.SetFileSaveSuccessfulMessage(path);
                return true;
            }
            catch (Exception ex)
            {
                ReportSaveFailure(path, ex);
                return false;
            }
        }

        public static void SaveWithPath()
        {
            _ = SaveWithPathAsync();
        }

        public static async Task<bool> SaveWithPathAsync()
        {
            var path = ProjectDocumentState.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                StatusBarManager.SetSavingFileMessage(path);
                await SaveGate.WaitAsync();
                try
                {
                    await WriteFile(path);
                }
                finally
                {
                    SaveGate.Release();
                }
                DocumentDirtyTracker.MarkClean();
                StatusBarManager.SetFileSaveSuccessfulMessage(path);
                return true;
            }
            catch (Exception ex)
            {
                ReportSaveFailure(path, ex);
                return false;
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

        static async Task WriteFile(string path)
        {
            if (!IsFtxtcPath(path)) throw new InvalidOperationException("Projects can only be saved in native .ftxtc format.");
            await FTXTCWriter.WriteFileAsync(path, DataManager.Data, DataManager.Results, DataManager.SourceItems, DataManager.Reports);
        }

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
