using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppKit;
using Foundation;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.DataReaders;

namespace AnalysisITC.UI.MacOS
{
    public static class MacDataReader
    {
        sealed class ReadRequest
        {
            public NSUrl[] Urls { get; }
            public TaskCompletionSource<bool> Completion { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public ReadRequest(NSUrl[] urls) => Urls = urls;
        }

        static readonly object Sync = new object();
        static readonly Queue<ReadRequest> Pending = new Queue<ReadRequest>();
        static bool startupStarted;
        static bool startupComplete;
        static bool startupAbandoned;
        static bool draining;

        public static bool StartupComplete
        {
            get { lock (Sync) return startupComplete; }
        }

        public static event EventHandler StartupStateChanged;

        public static async void Read(NSUrl url) => await ReadAsync(new[] { url });

        public static async void Read(IEnumerable<NSUrl> urls) => await ReadAsync(urls);

        public static Task ReadAsync(IEnumerable<NSUrl> urls)
        {
            var request = new ReadRequest(urls?.Where(url => url != null).ToArray() ?? Array.Empty<NSUrl>());
            lock (Sync)
            {
                if (startupAbandoned)
                {
                    request.Completion.TrySetResult(false);
                    return request.Completion.Task;
                }

                Pending.Enqueue(request);
                if (startupComplete && !draining)
                {
                    draining = true;
                    NSApplication.SharedApplication.BeginInvokeOnMainThread(() => _ = DrainNormalQueueAsync());
                }
            }
            return request.Completion.Task;
        }

        // Startup is one gate: confirm the operator, recover autosave state, then drain
        // every activation received while the window was being initialized.
        public static async Task<bool> BeginStartupAsync(
            Func<Task<bool>> confirmOperator,
            Func<Task> initializeRecovery)
        {
            lock (Sync)
            {
                if (startupStarted) return startupComplete;
                startupStarted = true;
            }

            bool confirmed;
            try
            {
                confirmed = await confirmOperator();
                if (!confirmed)
                {
                    AbandonPendingRequests();
                    return false;
                }

                await initializeRecovery();
            }
            catch
            {
                AbandonPendingRequests();
                throw;
            }

            while (true)
            {
                ReadRequest[] batch;
                lock (Sync)
                {
                    if (Pending.Count == 0)
                    {
                        startupComplete = true;
                        draining = false;
                        RaiseStartupStateChanged();
                        return true;
                    }

                    batch = Pending.ToArray();
                    Pending.Clear();
                    draining = true;
                }

                try
                {
                    await ReadBatchAsync(batch.SelectMany(request => request.Urls));
                    foreach (var request in batch) request.Completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    foreach (var request in batch) request.Completion.TrySetException(ex);
                }
            }
        }

        static void AbandonPendingRequests()
        {
            lock (Sync)
            {
                startupAbandoned = true;
                draining = false;
                while (Pending.Count > 0) Pending.Dequeue().Completion.TrySetResult(false);
                RaiseStartupStateChanged();
            }
        }

        static async Task DrainNormalQueueAsync()
        {
            while (true)
            {
                ReadRequest request;
                lock (Sync)
                {
                    if (Pending.Count == 0)
                    {
                        draining = false;
                        return;
                    }
                    request = Pending.Dequeue();
                }

                try
                {
                    await ReadBatchAsync(request.Urls);
                    request.Completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    request.Completion.TrySetException(ex);
                }
            }
        }

        static async Task ReadBatchAsync(IEnumerable<NSUrl> urls)
        {
            var urlList = urls?.Where(url => url != null).ToArray() ?? Array.Empty<NSUrl>();
            var paths = urlList
                .Select(url => url.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (paths.Length == 0) return;

            var containsProjectFile = paths.Any(DataReader.IsProjectFile);
            if (containsProjectFile && DataManager.SourceItems != null && DataManager.SourceItems.Count > 0)
            {
                switch (AppDelegate.PromptProjectLoadAction())
                {
                    case AppDelegate.ProjectLoadAction.Replace:
                        if (!await AppDelegate.CloseAllDataAsync(DataClearMode.ResetSession)) return;
                        break;
                    case AppDelegate.ProjectLoadAction.Cancel:
                        return;
                    case AppDelegate.ProjectLoadAction.Append:
                        break;
                }
            }

            var urlsByPath = urlList
                .Where(url => !string.IsNullOrWhiteSpace(url.Path))
                .GroupBy(url => url.Path, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var result = await DataReader.ReadPathsAsync(paths, path =>
            {
                if (urlsByPath.TryGetValue(path, out var url))
                    NSDocumentController.SharedDocumentController.NoteNewRecentDocumentURL(url);
            });

            if (AppSettings.TraceabilityModeEnabled && AppSettings.PromptForIdentifiersOnImport
                && result.ImportedExperiments.Count > 0)
                await MacIdentifierEditor.ReviewImportedExperiments(result.ImportedExperiments);
        }

        static void RaiseStartupStateChanged()
        {
            NSApplication.SharedApplication.BeginInvokeOnMainThread(() => StartupStateChanged?.Invoke(null, EventArgs.Empty));
        }
    }
}
