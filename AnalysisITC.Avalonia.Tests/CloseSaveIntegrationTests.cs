using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class CloseSaveIntegrationTests
{
    public CloseSaveIntegrationTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Theory]
    [InlineData(".ftxtc", true)]
    [InlineData(".invalid", false)]
    public void CloseSaveRefreshesWindowAndPropagatesSaveOutcome(string extension, bool expected)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ftitc-close-save-{Guid.NewGuid():N}{extension}");
        var previousDocumentPath = FTITCFormat.CurrentAccessedAppDocumentPath;
        var previousLastDocumentPath = AppSettings.LastDocumentPath;
        var previousLastDocumentPaths = AppSettings.LastDocumentPaths;
        MainWindow? window = null;

        try
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                DataManager.Clear(DataClearMode.ResetSession);
                DataManager.AddData(CreateExperiment());
                FTITCFormat.CurrentAccessedAppDocumentPath = path;

                window = new MainWindow();
                window.Show();
                DocumentDirtyTracker.MarkDirty();

                var save = typeof(MainWindow).GetMethod(
                    "SaveForCloseAndRefreshAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var saveTask = (Task<bool>)save.Invoke(window, null)!;
                PumpUntilCompleted(saveTask);

                Assert.Equal(expected, saveTask.GetAwaiter().GetResult());
                Assert.Equal(expected, File.Exists(path));
                Assert.Equal(!expected, DocumentDirtyTracker.IsDirty);
                var title = window!.Title!;
                Assert.Equal(expected, !title.Contains("[M]", StringComparison.Ordinal));
                Assert.Contains(Path.GetFileNameWithoutExtension(path), title, StringComparison.Ordinal);
            });
        }
        finally
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                DocumentDirtyTracker.MarkClean();
                window?.Close();
                DataManager.Clear(DataClearMode.ResetSession);
                FTITCFormat.CurrentAccessedAppDocumentPath = previousDocumentPath;
                AppSettings.LastDocumentPath = previousLastDocumentPath;
                AppSettings.LastDocumentPaths = previousLastDocumentPaths;
            });
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static void PumpUntilCompleted(Task task)
    {
        var timeout = Stopwatch.StartNew();
        while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Yield();
        }

        Assert.True(task.IsCompleted, "The close-save operation did not complete.");
        Dispatcher.UIThread.RunJobs();
    }

    static ExperimentData CreateExperiment()
    {
        var data = new ExperimentData("close-save-test")
        {
            CellVolume = 200e-6,
            CellConcentration = new(20.123456789e-6),
            SyringeConcentration = new(400e-6),
            MeasuredTemperature = 25,
        };

        for (var i = 0; i < 4; i++)
        {
            var injection = new InjectionData(data, i, 2e-6, 8e-10, true);
            injection.SetPeakArea(new FloatWithError(-1e-5 * (i + 1), 1e-9));
            data.Injections.Add(injection);
        }

        RawDataReader.ProcessInjections(data, DilutionMethod.MicroCal);
        return data;
    }
}
