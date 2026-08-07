using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    [CollectionDefinition("AutoSaveManager", DisableParallelization = true)]
    public sealed class AutoSaveManagerCollection
    {
    }

    [Collection("AutoSaveManager")]
    public sealed class AutoSaveManagerTests
    {
        [Fact]
        public void BuildsStableUserReadableNames()
        {
            var timestamp = new DateTime(2026, 8, 7, 14, 5, 6, 123);

            Assert.Equal(
                "AutoSave_AppStarted_2026-08-07_14-05-06-123.ftitc",
                AutoSaveManager.BuildUntitledFileName(timestamp));
            Assert.Equal(
                "AutoSave_Opened_2026-08-07_14-05-06-123_My Project.ftitc",
                AutoSaveManager.BuildProjectFileName(timestamp, Path.Combine("somewhere", "My Project.ftitc")));
        }

        [Fact]
        public async Task AutoSavePreservesDocumentStateAndCanBeRecoveredAsUnsaved()
        {
            var directory = NewTemporaryDirectory();
            AutoSaveManager manager = null;
            AutoSaveManager recoveryManager = null;
            var originalEnabled = AppSettings.AutoSaveEnabled;
            var originalLimit = AppSettings.AutoSaveFileLimit;

            try
            {
                ResetDocument();
                var opened = await DataReader.ReadPathsAsync(new[] { Fixture("one-set.ftitc") });
                Assert.True(opened.OpenedCleanProject);

                AppSettings.AutoSaveEnabled = true;
                AppSettings.AutoSaveFileLimit = 10;
                var activeProjectPath = FTITCFormat.CurrentAccessedAppDocumentPath;

                manager = NewManager(directory);
                manager.Start();
                Assert.False(await manager.TickNowAsync());
                Assert.Empty(Directory.GetFiles(directory, "*.ftitc"));

                DocumentDirtyTracker.MarkDirty();
                AppSettings.AutoSaveEnabled = false;
                Assert.False(await manager.TickNowAsync());
                AppSettings.AutoSaveEnabled = true;
                Assert.True(await manager.TickNowAsync());
                Assert.True(DocumentDirtyTracker.IsDirty);
                Assert.Equal(activeProjectPath, FTITCFormat.CurrentAccessedAppDocumentPath);
                Assert.Single(Directory.GetFiles(directory, "*.ftitc"));

                manager.StopWithoutResolving();
                manager = null;
                ResetDocument();

                recoveryManager = NewManager(directory);
                recoveryManager.Start();
                var candidate = recoveryManager.GetNewestRecoveryCandidate();
                Assert.NotNull(candidate);
                Assert.True(await DataReader.ReadRecoveryFileAsync(candidate.FilePath));
                Assert.True(DocumentDirtyTracker.IsDirty);
                Assert.Empty(FTITCFormat.CurrentAccessedAppDocumentPath);
                Assert.NotEmpty(DataManager.SourceItems);

                recoveryManager.ResolveRecovery(candidate, deleteFile: false);
                Assert.Null(recoveryManager.GetNewestRecoveryCandidate());
            }
            finally
            {
                manager?.StopWithoutResolving();
                recoveryManager?.StopCleanly();
                AppSettings.AutoSaveEnabled = originalEnabled;
                AppSettings.AutoSaveFileLimit = originalLimit;
                ResetDocument();
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task OverwritesPerIdentityAndPrunesGlobally()
        {
            var directory = NewTemporaryDirectory();
            AutoSaveManager manager = null;
            var originalEnabled = AppSettings.AutoSaveEnabled;
            var originalLimit = AppSettings.AutoSaveFileLimit;

            try
            {
                ResetDocument();
                await DataReader.ReadPathsAsync(new[] { Fixture("one-set.ftitc") });
                DocumentDirtyTracker.MarkDirty();
                AppSettings.AutoSaveEnabled = true;
                AppSettings.AutoSaveFileLimit = 2;

                manager = NewManager(directory);
                manager.Start();
                Assert.True(await manager.TickNowAsync());
                Assert.True(await manager.TickNowAsync());
                Assert.Single(Directory.GetFiles(directory, "*.ftitc"));

                FTITCFormat.CurrentAccessedAppDocumentPath = Path.Combine(directory, "Second Project.ftitc");
                Assert.True(await manager.TickNowAsync());
                FTITCFormat.CurrentAccessedAppDocumentPath = Path.Combine(directory, "Third Project.ftitc");
                Assert.True(await manager.TickNowAsync());

                var files = Directory.GetFiles(directory, "*.ftitc");
                Assert.Equal(2, files.Length);
                Assert.DoesNotContain(files, file => Path.GetFileName(file).Contains("data"));
                Assert.Contains(files, file => Path.GetFileName(file).Contains("Second Project"));
                Assert.Contains(files, file => Path.GetFileName(file).Contains("Third Project"));
            }
            finally
            {
                manager?.StopWithoutResolving();
                AppSettings.AutoSaveEnabled = originalEnabled;
                AppSettings.AutoSaveFileLimit = originalLimit;
                ResetDocument();
                Directory.Delete(directory, recursive: true);
            }
        }

        static AutoSaveManager NewManager(string directory)
        {
            var utc = new DateTime(2026, 8, 7, 12, 5, 6, DateTimeKind.Utc);
            return new AutoSaveManager(
                directory,
                () => new DateTime(2026, 8, 7, 14, 5, 6, 123),
                () => utc = utc.AddSeconds(1));
        }

        static void ResetDocument()
        {
            DocumentDirtyTracker.Initialize();
            DataManager.Clear(DataClearMode.ResetSession);
            DocumentDirtyTracker.MarkClean();
        }

        static string NewTemporaryDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "AnalysisITC-AutoSaveTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    }
}
