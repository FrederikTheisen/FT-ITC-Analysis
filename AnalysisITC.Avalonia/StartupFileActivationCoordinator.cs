using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AnalysisITC.Avalonia;

internal sealed class StartupFileActivationCoordinator
{
    readonly List<string> pendingPaths = new();
    readonly List<TaskCompletionSource<bool>> pendingCompletions = new();
    bool initializationStarted;
    bool prerequisitesReady;
    bool draining;
    bool abandoned;
    Func<IReadOnlyList<string>, Task>? pathReader;
    Action? releaseReadyAction;

    public bool IsReady { get; private set; }

    public Task<bool> Request(IEnumerable<string> paths)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (abandoned)
        {
            completion.SetResult(false);
            return completion.Task;
        }

        var pathArray = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (pathArray.Length == 0)
        {
            completion.SetResult(true);
            return completion.Task;
        }

        AddPaths(pathArray, completion);
        if (pendingPaths.Count == 0)
        {
            completion.SetResult(true);
            return completion.Task;
        }

        if (prerequisitesReady && !draining) _ = DrainAsync();
        return completion.Task;
    }

    public void QueueActivation(IEnumerable<string> paths)
    {
        if (abandoned) return;
        AddPaths(paths, completion: null);
        if (prerequisitesReady && !draining) _ = DrainAsync();
    }

    public async Task<bool> InitializeAsync(
        Func<Task<bool>> confirm,
        Func<Task> recover,
        Func<IReadOnlyList<string>, Task> readPaths,
        Action releaseReady)
    {
        if (initializationStarted) return IsReady;
        initializationStarted = true;
        if (!await confirm())
        {
            abandoned = true;
            pendingPaths.Clear();
            foreach (var completion in pendingCompletions) completion.TrySetResult(false);
            pendingCompletions.Clear();
            return false;
        }

        await recover();
        pathReader = readPaths;
        releaseReadyAction = releaseReady;
        prerequisitesReady = true;
        await DrainAsync();
        return IsReady;
    }

    void AddPaths(IEnumerable<string> paths, TaskCompletionSource<bool>? completion)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var fullPath = Path.GetFullPath(path);
            if (!pendingPaths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                pendingPaths.Add(fullPath);
        }

        if (completion != null) pendingCompletions.Add(completion);
    }

    async Task DrainAsync()
    {
        if (!prerequisitesReady || draining) return;
        draining = true;
        try
        {
            while (pendingPaths.Count > 0)
            {
                var paths = pendingPaths.ToArray();
                pendingPaths.Clear();
                var completions = pendingCompletions.ToArray();
                pendingCompletions.Clear();
                try
                {
                    if (pathReader == null)
                        throw new InvalidOperationException("A path reader is required after startup.");
                    await pathReader(paths);
                    foreach (var completion in completions) completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    foreach (var completion in completions) completion.TrySetException(ex);
                    throw;
                }
            }

            if (!IsReady)
            {
                IsReady = true;
                releaseReadyAction?.Invoke();
            }
        }
        finally
        {
            draining = false;
        }
    }
}
