using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Platform;

using Avalonia.Threading;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ImportPathTests
{
    [AvaloniaFact]
    public async Task CaseDistinctRawFilesBothReachTheQueuedReader()
    {
        if (StartupFileActivationCoordinator.DefaultPathComparer.Equals("run.itc", "Run.itc"))
            Assert.Skip("The platform import policy is case-insensitive.");

        var directory = Directory.CreateTempSubdirectory("ftitc-import-paths-").FullName;
        try
        {
            var fixture = RawFixturePath();
            var first = Path.Combine(directory, "run.itc");
            var second = Path.Combine(directory, "Run.itc");
            File.Copy(fixture, first);
            if (File.Exists(second))
                Assert.Skip("The test directory is case-insensitive: Run.itc already exists after creating run.itc.");
            File.Copy(fixture, second);

            var originalStore = PlatformServices.SettingsStore;
            var originalPreferences = PreferencesState.FromSettings();
            var originalDocumentPath = FTITCFormat.CurrentAccessedAppDocumentPath;
            var originalLastPath = AppSettings.LastDocumentPath;
            var originalLastPaths = AppSettings.LastDocumentPaths;
            MainWindow? window = null;
            try
            {
                PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
                PreferencesState.Defaults().ApplyToSettings();
                AppSettings.PerformOnlineChecksOnLaunch = false;
                AppSettings.TraceabilityModeEnabled = false;
                DataManager.Clear(DataClearMode.ResetSession);
                window = new MainWindow();
                window.Show();
                var coordinator = new StartupFileActivationCoordinator();
                var request = coordinator.Request(new[] { first, second, first });

                Assert.True(await coordinator.InitializeAsync(
                    () => Task.FromResult(true),
                    () => Task.CompletedTask,
                    window.OpenQueuedPathsAsync,
                    window.SetStartupReady));
                Assert.True(await request);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(new[] { "run.itc", "Run.itc" }, DataManager.Data.Select(data => data.FileName));
                Assert.All(DataManager.Data, data =>
                {
                    Assert.NotEmpty(data.DataPoints);
                    Assert.NotEmpty(data.Injections);
                });
                Assert.Equal(2, window.DataListEntries.Count);
                Assert.Equal(new[] { first, second }, AppSettings.LastDocumentPaths);
            }
            finally
            {
                DocumentDirtyTracker.MarkClean();
                window?.Close();
                DataManager.Clear(DataClearMode.ResetSession);
                originalPreferences.ApplyToSettings();
                FTITCFormat.CurrentAccessedAppDocumentPath = originalDocumentPath;
                AppSettings.LastDocumentPath = originalLastPath;
                AppSettings.LastDocumentPaths = originalLastPaths;
                PlatformServices.RegisterSettingsStore(originalStore);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static string RawFixturePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "AnalysisITC.Tests", "ScientificValidation",
                "RawPipeline", "analytic-one-site.itc");
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException("The raw ITC import fixture was not found.");
    }
}
