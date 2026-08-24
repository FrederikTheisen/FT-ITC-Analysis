using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class FTITCWriterSaveSelectedCollectionDefinition
    {
        public const string Name = "FTITC writer save selected";
    }

    [Collection(FTITCWriterSaveSelectedCollectionDefinition.Name)]
    public sealed class FTITCWriterSaveSelectedTests
    {
        static readonly string FixtureDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "FileTypeTests");

        [Fact]
        public async Task SavingAnalysisResultAllowsOnlyFtxtcAndDoesNotOpenGenericExporter()
        {
            var result = await LoadAnalysisResult();
            var path = TemporaryPath("ftxtc");
            var savePrompt = new FixedFileSavePrompt(path);
            var exportPrompt = new TrackingExportPrompt();

            PlatformServices.RegisterFileSavePromptService(savePrompt);
            PlatformServices.RegisterExportPromptService(exportPrompt);
            try
            {
                Assert.True(await FTITCWriter.SaveSelectedAsync(result));
                Assert.Equal(new[] { "ftxtc" }, savePrompt.AllowedFileTypes);
                Assert.False(exportPrompt.WasInvoked);
                Assert.True(File.Exists(path));

                using var stream = File.OpenRead(path);
                var restored = await FTXTCReader.ReadStream(stream);
                Assert.Contains(restored.OfType<AnalysisResult>(), restoredResult =>
                    restoredResult.Name == result.Name);
            }
            finally
            {
                PlatformServices.RegisterFileSavePromptService(null);
                PlatformServices.RegisterExportPromptService(null);
                DeleteIfPresent(path);
            }
        }

        [Fact]
        public async Task CancellingSaveSelectedDoesNotWriteAnAnalysisResult()
        {
            var result = await LoadAnalysisResult();
            var path = TemporaryPath("ftxtc");
            var savePrompt = new FixedFileSavePrompt(null);

            PlatformServices.RegisterFileSavePromptService(savePrompt);
            try
            {
                Assert.False(await FTITCWriter.SaveSelectedAsync(result));
                Assert.False(File.Exists(path));
            }
            finally
            {
                PlatformServices.RegisterFileSavePromptService(null);
                DeleteIfPresent(path);
            }
        }

        [Fact]
        public async Task SavingExperimentStillWritesAnFtxtcProject()
        {
            var experiment = await LoadExperiment();
            var path = TemporaryPath("ftxtc");
            var savePrompt = new FixedFileSavePrompt(path);

            PlatformServices.RegisterFileSavePromptService(savePrompt);
            try
            {
                Assert.True(await FTITCWriter.SaveSelectedAsync(experiment));
                Assert.Equal(new[] { "ftxtc" }, savePrompt.AllowedFileTypes);

                using var stream = File.OpenRead(path);
                var restored = await FTXTCReader.ReadStream(stream);
                Assert.Contains(restored.OfType<ExperimentData>(), restoredExperiment =>
                    restoredExperiment.UniqueID == experiment.UniqueID);
            }
            finally
            {
                PlatformServices.RegisterFileSavePromptService(null);
                DeleteIfPresent(path);
            }
        }

        static async Task<AnalysisResult> LoadAnalysisResult()
        {
            using var stream = File.OpenRead(Fixture("JORS Example Project.ftxtc"));
            return (await FTXTCReader.ReadStream(stream)).OfType<AnalysisResult>().First();
        }

        static Task<ExperimentData> LoadExperiment()
        {
            return Task.FromResult(MicroCalITC200Reader.ReadPath(Fixture("230908_PRLRlong_W392A_run1.itc")));
        }

        static string Fixture(string fileName) => Path.Combine(FixtureDirectory, fileName);

        static string TemporaryPath(string extension) => Path.Combine(
            Path.GetTempPath(),
            $"analysis-itc-save-selected-{Guid.NewGuid():N}.{extension}");

        static void DeleteIfPresent(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        sealed class FixedFileSavePrompt : IFileSavePromptService
        {
            readonly string path;

            public FixedFileSavePrompt(string path)
            {
                this.path = path;
            }

            public string[] AllowedFileTypes { get; private set; }

            public Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes)
            {
                AllowedFileTypes = allowedFileTypes?.ToArray() ?? Array.Empty<string>();
                return Task.FromResult(path);
            }
        }

        sealed class TrackingExportPrompt : IExportPromptService
        {
            public bool WasInvoked { get; private set; }

            public Task<string> ChooseExportFolderAsync(ExportAccessoryViewSettings settings)
            {
                WasInvoked = true;
                return Task.FromResult<string>(null);
            }

            public bool ConfirmOverwrite(IEnumerable<string> outputPaths)
            {
                WasInvoked = true;
                return false;
            }
        }
    }
}
