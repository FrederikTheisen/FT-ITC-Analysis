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
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("JORS Example Project.ftxtc")]
    [InlineData("JORS Table1.ftxtc")]
    public async Task ProjectItemsReachTheVisibleStatusBarDuringLoading(string? projectFile)
    {
        var exampleProject = projectFile != null;
        var path = Path.Combine(Path.GetTempPath(), $"ftxtc-loading-{Guid.NewGuid():N}.ftxtc");
        if (exampleProject)
            File.Copy(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../AnalysisITC.Tests", projectFile!)), path);
        else await FTXTCWriter.WriteFileAsync(path, new[]
        {
            new ExperimentData("first.itc") { Name = "First experiment λ" },
            new ExperimentData("second.itc") { Name = "Second experiment 李明" },
        });
        var window = new MainWindow();
        window.Show();
        var label = window.FindControl<TextBlock>("SecondaryStatusText")!;
        var indicator = window.FindControl<ProgressBar>("StatusProgressBar")!;
        var rendered = new List<string>();
        var states = new List<(string Text, bool Visible, bool Indeterminate, double Value)>();
        var previousContext = SynchronizationContext.Current;
        Task<FtxtcReadResult>? read = null;
        EventHandler<string> handler = (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (read?.IsCompleted == false)
            {
                rendered.Add(label.Text ?? "");
                states.Add((label.Text ?? "", indicator.IsVisible, indicator.IsIndeterminate, indicator.Value));
            }
        });
        StatusBarManager.SecondaryStatusUpdated += handler;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
            using var stream = File.OpenRead(path);
            read = FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict, interactive: true);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!read.IsCompleted && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
            Assert.True(read.IsCompleted, "Project loading did not finish while the UI handled updates.");
            var restored = await read;
            Dispatcher.UIThread.RunJobs();

            if (!exampleProject)
            {
                Assert.Contains("Experiment: First experiment λ", rendered);
                Assert.Contains("Experiment: Second experiment 李明", rendered);
                Assert.Contains(states, state => state.Text == "Experiment: Second experiment 李明"
                    && state.Visible && !state.Indeterminate && state.Value == 50);
                Assert.Equal(2, restored.Containers.Length);
            }
            else
            {
                Assert.Contains(rendered, text => text.StartsWith("Fit: ", StringComparison.Ordinal));
                Assert.Contains(rendered, text => text.StartsWith("Result: ", StringComparison.Ordinal));
                Assert.Contains(rendered, text => text.StartsWith("Report: ", StringComparison.Ordinal));
                Assert.Contains(states, state => state.Visible && !state.Indeterminate && state.Value > 0 && state.Value < 100);
                Assert.Contains(states, state => state.Text.StartsWith("Fit: ", StringComparison.Ordinal)
                    && state.Visible && !state.Indeterminate && state.Value > 0 && state.Value < 10);
                if (projectFile == "JORS Table1.ftxtc")
                    Assert.Contains(states, state => state.Text.StartsWith("Result: ", StringComparison.Ordinal)
                        && state.Visible && !state.Indeterminate && state.Value > 30 && state.Value < 50);
                Assert.Equal(projectFile == "JORS Table1.ftxtc" ? 21 : 5, restored.Containers.Length);
            }
            Assert.Contains(states, state => state.Text == "Validating project..." && state.Visible && state.Indeterminate);
            Assert.Contains(states, state => state.Text == "Finishing…" && state.Visible && state.Indeterminate);
            Assert.False(indicator.IsVisible);
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
