using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AppMessageTests
{
    [AvaloniaFact]
    public async Task FailedProjectOpenKeepsTheErrorVisibleAfterStatusUpdates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ftxtc-open-error-{Guid.NewGuid():N}.ftxtc");
        var previousDocumentPath = FTITCFormat.CurrentAccessedAppDocumentPath;
        var previousLastDocumentPath = AppSettings.LastDocumentPath;
        var previousLastDocumentPaths = AppSettings.LastDocumentPaths;
        DataManager.Clear(DataClearMode.ResetSession);
        var window = new MainWindow();
        window.SetStartupReady();
        window.Show();
        try
        {
            using (var file = File.Create(path))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            using (var entry = archive.CreateEntry("project.json").Open())
                entry.Write(new byte[] { (byte)'{', (byte)'}' });
            var documentId = DocumentDirtyTracker.CurrentDocumentId;

            await window.OpenQueuedPathsAsync(new[] { path });
            Dispatcher.UIThread.RunJobs();

            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("InvalidDataException", dialog.Title);
            Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(),
                block => block.Text == "FTXTC package is missing manifest.json.");
            Assert.True(dialog.IsVisible);
            Assert.Empty(DataManager.SourceItems);
            Assert.False(DocumentDirtyTracker.IsDirty);
            Assert.Equal(documentId, DocumentDirtyTracker.CurrentDocumentId);

            StatusBarManager.SetStatus("Another status update", 0);
            Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.IsVisible);
            Dismiss(dialog);
            Assert.Empty(window.OwnedWindows);
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            DocumentDirtyTracker.MarkClean();
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
            FTITCFormat.CurrentAccessedAppDocumentPath = previousDocumentPath;
            AppSettings.LastDocumentPath = previousLastDocumentPath;
            AppSettings.LastDocumentPaths = previousLastDocumentPaths;
            File.Delete(path);
        }
    }

    [AvaloniaTheory]
    [InlineData(HandledException.Severity.Error)]
    [InlineData(HandledException.Severity.Warning)]
    [InlineData(HandledException.Severity.Message)]
    public void HandledMessagesShowTheirCompleteTextAndCanBeDismissed(HandledException.Severity severity)
    {
        var message = "FTXTC entry 'solutions/000004/bootstrap-injections.ftxb' exceeds the compression-ratio limit.\n"
            + string.Join("\n", Enumerable.Repeat("Additional details — λβ and 李明", 100));
        var window = new MainWindow();
        window.Show();
        try
        {
            AppEventHandler.DisplayHandledException(new HandledException(severity, "File Reading Error", message));
            Dispatcher.UIThread.RunJobs();

            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("File Reading Error", dialog.Title);
            Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(), block => block.Text == message);
            Assert.Contains(dialog.GetLogicalDescendants().OfType<ScrollViewer>(),
                viewer => viewer.Content is TextBlock block && block.Text == message);
            Dismiss(dialog);
            Assert.Empty(window.OwnedWindows);
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            DocumentDirtyTracker.MarkClean();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task WorkerThreadErrorsAreShownOnTheWindowThread()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await Task.Run(() => AppEventHandler.DisplayHandledException(new IOException("Project read failed.")));
            Dispatcher.UIThread.RunJobs();

            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("IOException", dialog.Title);
            Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(), block => block.Text == "Project read failed.");
            Dismiss(dialog);
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            DocumentDirtyTracker.MarkClean();
            window.Close();
        }
    }

    static void Dismiss(Window dialog)
    {
        dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "OK"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
}
