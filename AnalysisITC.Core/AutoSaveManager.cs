using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Platform;

namespace AnalysisITC.Core.Application
{
    public sealed class AutoSaveEntry
    {
        public string FilePath { get; set; }
        public string RunId { get; set; }
        public string SourceProjectPath { get; set; }
        public string SourceProjectName { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime LastWrittenUtc { get; set; }
        public bool RecoveryPending { get; set; }

        internal AutoSaveEntry Copy() => (AutoSaveEntry)MemberwiseClone();
    }

    sealed class AutoSaveIndex
    {
        public List<AutoSaveEntry> Entries { get; set; } = new List<AutoSaveEntry>();
    }

    public sealed class AutoSaveManager : IDisposable
    {
        const string IndexFileName = ".autosave-index.json";
        const int MinimumIntervalMinutes = 1;
        const int MaximumIntervalMinutes = 60;
        const int MinimumFileLimit = 1;
        const int MaximumFileLimit = 100;

        static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        readonly object sync = new object();
        readonly Func<DateTime> localNow;
        readonly Func<DateTime> utcNow;
        readonly string configuredDirectory;

        Timer timer;
        string runId;
        string currentSourcePath = "";
        string currentAutoSavePath = "";
        DateTime applicationStartTime;
        int tickInProgress;
        bool started;
        bool disposed;

        public static AutoSaveManager Shared { get; } = new AutoSaveManager();

        public AutoSaveManager(
            string autoSaveDirectory = null,
            Func<DateTime> localNow = null,
            Func<DateTime> utcNow = null)
        {
            configuredDirectory = autoSaveDirectory;
            this.localNow = localNow ?? (() => DateTime.Now);
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public string AutoSaveDirectory => configuredDirectory ?? PlatformServices.AppEnvironment.AutoSaveDirectory;
        public string CurrentAutoSavePath { get { lock (sync) return currentAutoSavePath; } }
        public bool IsStarted { get { lock (sync) return started; } }

        public void Start()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                if (started) return;

                Directory.CreateDirectory(AutoSaveDirectory);
                started = true;
                runId = Guid.NewGuid().ToString("N");
                applicationStartTime = localNow();
                var currentPath = FTITCFormat.CurrentAccessedAppDocumentPath ?? "";
                if (string.IsNullOrWhiteSpace(currentPath))
                {
                    SetUntitledIdentity();
                }
                else
                {
                    currentSourcePath = currentPath;
                    currentAutoSavePath = Path.Combine(AutoSaveDirectory, BuildProjectFileName(localNow(), currentPath));
                }

                AppSettings.SettingsDidUpdate += OnSettingsDidUpdate;
                DocumentDirtyTracker.DirtyStateChanged += OnDirtyStateChanged;
                FTITCFormat.CurrentAccessedAppDocumentPathChanged += OnCurrentDocumentPathChanged;

                ScheduleTimer();
                PruneToLimit();
            }
        }

        public Task<bool> TickNowAsync()
        {
            return RunTickAsync();
        }

        public AutoSaveEntry GetNewestRecoveryCandidate()
        {
            lock (sync)
            {
                var index = LoadIndex();
                RemoveMissingEntries(index);
                SaveIndex(index);

                return index.Entries
                    .Where(entry => entry.RecoveryPending
                        && !string.Equals(entry.RunId, runId, StringComparison.Ordinal)
                        && File.Exists(entry.FilePath))
                    .OrderByDescending(entry => entry.LastWrittenUtc)
                    .Select(entry => entry.Copy())
                    .FirstOrDefault();
            }
        }

        public void ResolveRecovery(AutoSaveEntry entry, bool deleteFile)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath)) return;

            lock (sync)
            {
                var index = LoadIndex();
                var stored = FindEntry(index, entry.FilePath);
                if (stored == null) return;

                if (deleteFile)
                {
                    TryDelete(stored.FilePath);
                    index.Entries.Remove(stored);
                }
                else
                {
                    stored.RecoveryPending = false;
                }

                SaveIndex(index);
            }
        }

        public void ResolveCurrent()
        {
            lock (sync)
            {
                if (string.IsNullOrWhiteSpace(currentAutoSavePath)) return;

                var index = LoadIndex();
                var entry = FindEntry(index, currentAutoSavePath);
                if (entry == null || !entry.RecoveryPending) return;

                entry.RecoveryPending = false;
                SaveIndex(index);
            }
        }

        public void StopCleanly()
        {
            Stop(resolveCurrent: true);
        }

        public void StopWithoutResolving()
        {
            Stop(resolveCurrent: false);
        }

        public void PruneToLimit()
        {
            lock (sync)
            {
                var index = LoadIndex();
                RemoveMissingEntries(index);

                var limit = NormalizeFileLimit(AppSettings.AutoSaveFileLimit);
                foreach (var entry in index.Entries
                    .OrderByDescending(item => item.LastWrittenUtc)
                    .Skip(limit)
                    .ToArray())
                {
                    TryDelete(entry.FilePath);
                    index.Entries.Remove(entry);
                }

                SaveIndex(index);
            }
        }

        public static string BuildUntitledFileName(DateTime startedAt)
        {
            return $"AutoSave_AppStarted_{startedAt:yyyy-MM-dd_HH-mm-ss-fff}.ftxtc";
        }

        public static string BuildProjectFileName(DateTime openedAt, string projectPath)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            projectName = SanitizeFileName(projectName);
            if (string.IsNullOrWhiteSpace(projectName)) projectName = "Project";
            return $"AutoSave_Opened_{openedAt:yyyy-MM-dd_HH-mm-ss-fff}_{projectName}.ftxtc";
        }

        public static string SanitizeFileName(string value)
        {
            var result = value ?? "";
            foreach (var invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
            result = result.Trim().TrimEnd('.');
            return result.Length <= 100 ? result : result.Substring(0, 100);
        }

        void OnSettingsDidUpdate(object sender, EventArgs e)
        {
            lock (sync)
            {
                if (!started) return;
                ScheduleTimer();
            }

            PruneToLimit();
        }

        void OnDirtyStateChanged(object sender, EventArgs e)
        {
            if (!DocumentDirtyTracker.IsDirty) ResolveCurrent();
        }

        void OnCurrentDocumentPathChanged(object sender, EventArgs e)
        {
            lock (sync)
            {
                if (!started) return;

                var path = FTITCFormat.CurrentAccessedAppDocumentPath ?? "";
                if (string.Equals(path, currentSourcePath, StringComparison.OrdinalIgnoreCase)) return;

                ResolveCurrent();
                if (string.IsNullOrWhiteSpace(path))
                {
                    SetUntitledIdentity();
                }
                else
                {
                    currentSourcePath = path;
                    currentAutoSavePath = Path.Combine(AutoSaveDirectory, BuildProjectFileName(localNow(), path));
                }
            }
        }

        void SetUntitledIdentity()
        {
            currentSourcePath = "";
            currentAutoSavePath = Path.Combine(AutoSaveDirectory, BuildUntitledFileName(applicationStartTime));
        }

        void ScheduleTimer()
        {
            timer?.Dispose();
            timer = null;
            if (!AppSettings.AutoSaveEnabled || !started) return;

            var interval = TimeSpan.FromMinutes(NormalizeInterval(AppSettings.AutoSaveIntervalMinutes));
            timer = new Timer(_ => PlatformServices.MainThreadDispatcher.Invoke(() => _ = RunTickAsync()), null, interval, interval);
        }

        async Task<bool> RunTickAsync()
        {
            if (Interlocked.Exchange(ref tickInProgress, 1) != 0) return false;

            try
            {
                if (!IsStarted
                    || !AppSettings.AutoSaveEnabled
                    || !DocumentDirtyTracker.IsDirty
                    || DocumentDirtyTracker.IsSuspended
                    || DataManager.SourceItems == null
                    || DataManager.SourceItems.Count == 0
                    || FTITCWriter.IsWriteInProgress)
                {
                    return false;
                }

                string path;
                string sourcePath;
                string activeRunId;
                lock (sync)
                {
                    path = currentAutoSavePath;
                    sourcePath = currentSourcePath;
                    activeRunId = runId;
                }

                var didWrite = await FTITCWriter.WriteAutoSaveAsync(path);
                if (!didWrite) return false;

                lock (sync)
                {
                    var now = utcNow();
                    var stillCurrentIdentity = started
                        && string.Equals(currentAutoSavePath, path, StringComparison.OrdinalIgnoreCase);
                    var index = LoadIndex();
                    var entry = FindEntry(index, path);
                    if (entry == null)
                    {
                        entry = new AutoSaveEntry
                        {
                            FilePath = path,
                            CreatedUtc = now
                        };
                        index.Entries.Add(entry);
                    }

                    entry.RunId = activeRunId;
                    entry.SourceProjectPath = sourcePath;
                    entry.SourceProjectName = string.IsNullOrWhiteSpace(sourcePath)
                        ? "Unsaved Project"
                        : Path.GetFileName(sourcePath);
                    entry.LastWrittenUtc = now;
                    entry.RecoveryPending = stillCurrentIdentity && DocumentDirtyTracker.IsDirty;
                    SaveIndex(index);
                }

                PruneToLimit();
                return true;
            }
            catch (Exception ex)
            {
                AppEventHandler.PrintAndLog("Autosave failed: " + ex.Message);
                return false;
            }
            finally
            {
                Interlocked.Exchange(ref tickInProgress, 0);
            }
        }

        void Stop(bool resolveCurrent)
        {
            lock (sync)
            {
                if (!started) return;
                if (resolveCurrent) ResolveCurrent();

                started = false;
                timer?.Dispose();
                timer = null;
                AppSettings.SettingsDidUpdate -= OnSettingsDidUpdate;
                DocumentDirtyTracker.DirtyStateChanged -= OnDirtyStateChanged;
                FTITCFormat.CurrentAccessedAppDocumentPathChanged -= OnCurrentDocumentPathChanged;
            }
        }

        AutoSaveIndex LoadIndex()
        {
            try
            {
                var path = Path.Combine(AutoSaveDirectory, IndexFileName);
                if (!File.Exists(path)) return new AutoSaveIndex();
                return JsonSerializer.Deserialize<AutoSaveIndex>(File.ReadAllText(path), JsonOptions) ?? new AutoSaveIndex();
            }
            catch (Exception ex)
            {
                AppEventHandler.PrintAndLog("Could not read autosave index: " + ex.Message);
                return new AutoSaveIndex();
            }
        }

        void SaveIndex(AutoSaveIndex index)
        {
            Directory.CreateDirectory(AutoSaveDirectory);
            var path = Path.Combine(AutoSaveDirectory, IndexFileName);
            var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");

            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(index, JsonOptions));
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        static AutoSaveEntry FindEntry(AutoSaveIndex index, string path)
        {
            return index.Entries.FirstOrDefault(entry =>
                string.Equals(entry.FilePath, path, StringComparison.OrdinalIgnoreCase));
        }

        static void RemoveMissingEntries(AutoSaveIndex index)
        {
            index.Entries.RemoveAll(entry => string.IsNullOrWhiteSpace(entry.FilePath) || !File.Exists(entry.FilePath));
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                AppEventHandler.PrintAndLog("Could not delete autosave file: " + ex.Message);
            }
        }

        static int NormalizeInterval(int value) => Math.Max(MinimumIntervalMinutes, Math.Min(MaximumIntervalMinutes, value));
        static int NormalizeFileLimit(int value) => Math.Max(MinimumFileLimit, Math.Min(MaximumFileLimit, value));

        void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(AutoSaveManager));
        }

        public void Dispose()
        {
            if (disposed) return;
            StopCleanly();
            disposed = true;
        }
    }
}
