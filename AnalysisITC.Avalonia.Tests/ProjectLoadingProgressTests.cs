using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ProjectLoadingProgressTests
{
    [AvaloniaFact]
    public async Task ProjectItemsReachTheVisibleStatusBarDuringLoading()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ftxtc-loading-{Guid.NewGuid():N}.ftxtc");
        await FTXTCWriter.WriteFileAsync(path, new[]
        {
            new ExperimentData("first.itc") { Name = "First experiment λ" },
            new ExperimentData("second.itc") { Name = "Second experiment 李明" },
        });
        var window = new MainWindow();
        window.Show();
        var label = window.FindControl<TextBlock>("SecondaryStatusText")!;
        var rendered = new List<string>();
        var previousContext = SynchronizationContext.Current;
        Task<FtxtcReadResult>? read = null;
        EventHandler<string> handler = (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (read?.IsCompleted == false) rendered.Add(label.Text ?? "");
        });
        StatusBarManager.SecondaryStatusUpdated += handler;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
            using var stream = File.OpenRead(path);
            read = FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict, interactive: true);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!read.IsCompleted && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
            Assert.True(read.IsCompleted, "Project loading did not finish while the UI handled updates.");
            var restored = await read;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("Experiment: First experiment λ", rendered);
            Assert.Contains("Experiment: Second experiment 李明", rendered);
            Assert.Equal(2, restored.Containers.Length);
            Assert.Empty(restored.Issues);
            Assert.DoesNotContain("Experiment:", label.Text ?? "");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            StatusBarManager.SecondaryStatusUpdated -= handler;
            StatusBarManager.ClearAppStatus();
            window.Close();
            File.Delete(path);
        }
    }
}
