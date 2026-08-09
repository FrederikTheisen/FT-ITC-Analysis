using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Viewer;
using AnalysisITC.Core.Analysis;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection("AutoSaveManager")]
    public sealed class FTXTCFormatTests
    {
        [Fact]
        public async Task RoundTripPreservesExactNumericStateAndFits()
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            var experiments = containers.OfType<ExperimentData>().ToList();
            var experiment = experiments[0];
            var result = Assert.Single(containers.OfType<AnalysisResult>());

            var original = experiment.DataPoints[0];
            experiment.DataPoints[0] = new DataPoint(original.Time, original.Power, original.Temperature,
                1.25f, 2.5f, 3.75f, 5.0f);
            experiment.BaseLineCorrectedDataPoints[0] = new DataPoint(
                original.Time, -7.25f, original.Temperature, 6, 7, 8, 9);
            experiment.Processor.DiscardIntegratedPoints = false;
            experiment.Processor.IntegrationLengthMode = InjectionData.IntegrationLengthMode.Factor;
            experiment.Processor.IntegrationLengthFactor = 4.25f;
            var expectedBaseline = experiment.Processor.Interpolator.Baseline
                .Select(value => new[] { value.Value, value.SD, value.FloatWithError.Lower, value.FloatWithError.Upper })
                .ToArray();

            var expectedCurves = experiment.Solution.BootstrapSolutions
                .Select(solution => experiment.Injections.Select(injection => solution.Model.EvaluateEnthalpy(injection.ID, true)).ToArray())
                .ToArray();
            var expectedBands = experiment.Injections
                .Select(injection => experiment.Model.EvaluateBootstrap(injection.ID, true).DistributionConfidence95.ToArray())
                .ToArray();

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, experiments, new[] { result });
            package.Position = 0;
            var restored = await FTXTCReader.ReadStream(package);
            var restoredExperiment = restored.OfType<ExperimentData>().Single(item => item.UniqueID == experiment.UniqueID);

            Assert.Equal(1.25f, restoredExperiment.DataPoints[0].DT);
            Assert.Equal(2.5f, restoredExperiment.DataPoints[0].ShieldT);
            Assert.Equal(3.75f, restoredExperiment.DataPoints[0].ATP);
            Assert.Equal(5.0f, restoredExperiment.DataPoints[0].JFBI);
            Assert.Equal(-7.25f, restoredExperiment.BaseLineCorrectedDataPoints[0].Power);
            Assert.Equal(8, restoredExperiment.BaseLineCorrectedDataPoints[0].ATP);
            Assert.False(restoredExperiment.Processor.DiscardIntegratedPoints);
            Assert.Equal(InjectionData.IntegrationLengthMode.Factor, restoredExperiment.Processor.IntegrationLengthMode);
            Assert.Equal(4.25f, restoredExperiment.Processor.IntegrationLengthFactor);
            Assert.Equal(expectedBaseline.Length, restoredExperiment.Processor.Interpolator.Baseline.Count);
            for (var index = 0; index < expectedBaseline.Length; index++)
            {
                var actual = restoredExperiment.Processor.Interpolator.Baseline[index].FloatWithError;
                Assert.Equal(expectedBaseline[index][0], actual.Value);
                Assert.Equal(expectedBaseline[index][1], actual.SD);
                Assert.Equal(expectedBaseline[index][2], actual.Lower);
                Assert.Equal(expectedBaseline[index][3], actual.Upper);
            }
            Assert.Equal(experiment.Injections.Select(item => item.RawPeakArea.Value),
                restoredExperiment.Injections.Select(item => item.RawPeakArea.Value));
            Assert.Equal(experiment.Injections.Select(item => item.PeakArea.Value),
                restoredExperiment.Injections.Select(item => item.PeakArea.Value));
            Assert.Equal(experiment.Solution.BootstrapSolutions.Count, restoredExperiment.Solution.BootstrapSolutions.Count);

            for (var replicate = 0; replicate < expectedCurves.Length; replicate++)
            for (var injection = 0; injection < expectedCurves[replicate].Length; injection++)
                Assert.Equal(expectedCurves[replicate][injection],
                    restoredExperiment.Solution.BootstrapSolutions[replicate].Model.EvaluateEnthalpy(
                        restoredExperiment.Injections[injection].ID, true), 10);
            for (var injection = 0; injection < expectedBands.Length; injection++)
            {
                var actual = restoredExperiment.Model.EvaluateBootstrap(
                    restoredExperiment.Injections[injection].ID, true).DistributionConfidence95;
                Assert.Equal(expectedBands[injection][0], actual[0], 10);
                Assert.Equal(expectedBands[injection][1], actual[1], 10);
            }
        }

        [Fact]
        public async Task PackageHasVersionedManifestAndTypedPayloads()
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, containers.OfType<ExperimentData>(), containers.OfType<AnalysisResult>());
            package.Position = 0;
            using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

            Assert.NotNull(archive.GetEntry("manifest.json"));
            Assert.NotNull(archive.GetEntry("project.json"));
            Assert.NotNull(archive.GetEntry("experiments/000000/experiment.json"));
            Assert.NotNull(archive.GetEntry("experiments/000000/thermogram.ftxb"));
            Assert.NotNull(archive.GetEntry("experiments/000000/baseline.ftxb"));
            Assert.NotNull(archive.GetEntry("experiments/000000/corrected-trace.ftxb"));
            Assert.NotNull(archive.GetEntry("solutions/000000/solution.json"));
            Assert.NotNull(archive.GetEntry("solutions/000000/bootstrap.json"));
            Assert.NotNull(archive.GetEntry("solutions/000000/bootstrap-parameters.ftxb"));
            Assert.NotNull(archive.GetEntry("solutions/000000/bootstrap-parameter-locks.ftxb"));
            Assert.NotNull(archive.GetEntry("solutions/000000/bootstrap-injections.ftxb"));
            Assert.NotNull(archive.GetEntry("solutions/000000/bootstrap-injection-includes.ftxb"));
            Assert.NotNull(archive.GetEntry("results/000000/result.json"));

            using var projectReader = new StreamReader(archive.GetEntry("project.json").Open());
            var projectText = await projectReader.ReadToEndAsync();
            Assert.DoesNotContain("semanticGraph", projectText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("payloadBase64", projectText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FTITCVersion", projectText, StringComparison.Ordinal);

            using var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json").Open());
            Assert.Equal("ftxtc", manifest.RootElement.GetProperty("format").GetString());
            Assert.Equal(1, manifest.RootElement.GetProperty("schemaMajor").GetInt32());
            Assert.All(manifest.RootElement.GetProperty("entries").EnumerateArray(), entry =>
            {
                Assert.Equal(64, entry.GetProperty("sha256").GetString().Length);
                Assert.True(entry.GetProperty("length").GetInt64() >= 0);
            });
        }

        [Fact]
        public async Task ReadOnlyViewerOpensFtxtc()
        {
            using var package = await CreatePackage();
            var document = await new ViewerDocumentReader().ReadAsync(package, "project.ftxtc", ViewerFileFormat.Ftxtc);

            Assert.Equal("ftxtc", document.Format);
            Assert.Equal("1.0", document.FormatVersion);
            Assert.NotEmpty(document.Experiments);
            Assert.NotEmpty(document.AnalysisResults);
        }

        [Fact]
        public async Task GlobalReplicatesRemainPairedByExplicitIndex()
        {
            using var source = File.OpenRead(Fixture("jors.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            var sourceResult = containers.OfType<AnalysisResult>()
                .First(result => result.Solution.Solutions.Count > 1
                    && result.Solution.Solutions.All(member => member.BootstrapSolutions.Count >= 3));

            for (var memberIndex = 0; memberIndex < sourceResult.Solution.Solutions.Count; memberIndex++)
            {
                var replicates = sourceResult.Solution.Solutions[memberIndex].BootstrapSolutions.Take(3).ToList();
                for (var index = 0; index < replicates.Count; index++) replicates[index].BootstrapReplicateIndex = index;
                if (memberIndex == 1) replicates = new List<SolutionInterface> { replicates[2], replicates[0] };
                sourceResult.Solution.Solutions[memberIndex].SetBootstrapSolutions(replicates);
            }

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, containers.OfType<ExperimentData>(), new[] { sourceResult });
            package.Position = 0;
            var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>()).Solution;

            Assert.Equal(2, restored.BootstrapSolutions.Count);
            Assert.All(restored.BootstrapSolutions[0].Solutions, solution => Assert.Equal(0, solution.BootstrapReplicateIndex));
            Assert.All(restored.BootstrapSolutions[1].Solutions, solution => Assert.Equal(2, solution.BootstrapReplicateIndex));
        }

        [Fact]
        public async Task AppendingSamePackageRemapsCollidingIdsAndInternalReferences()
        {
            var path = Path.Combine(Path.GetTempPath(), "ftxtc-append-" + Guid.NewGuid().ToString("N") + ".ftxtc");
            try
            {
                DataManager.Init();
                using (var source = File.OpenRead(Fixture("one-set.ftitc")))
                {
                    var containers = await FTITCReader.ReadStream(source);
                    await FTXTCWriter.WriteFileAsync(path, containers.OfType<ExperimentData>(), containers.OfType<AnalysisResult>());
                }

                Assert.True((await DataReader.ReadPathsAsync(new[] { path })).OpenedCleanProject);
                var experimentCount = DataManager.Data.Count;
                var resultCount = DataManager.Results.Count;
                await DataReader.ReadPathsAsync(new[] { path });

                Assert.Equal(experimentCount * 2, DataManager.Data.Count);
                Assert.Equal(resultCount * 2, DataManager.Results.Count);
                Assert.Equal(DataManager.SourceItems.Count,
                    DataManager.SourceItems.Select(item => item.UniqueID).Distinct(StringComparer.Ordinal).Count());
                var appendedExperimentIds = new HashSet<string>(
                    DataManager.Data.Skip(experimentCount).Select(item => item.UniqueID), StringComparer.Ordinal);
                Assert.All(DataManager.Results.Skip(resultCount).SelectMany(result => result.Solution.Solutions),
                    solution => Assert.Contains(solution.Data.UniqueID, appendedExperimentIds));
            }
            finally
            {
                DataManager.Init();
                FTITCFormat.CurrentAccessedAppDocumentPath = "";
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public async Task ChecksumFailureDoesNotFallBackToEmbeddedState()
        {
            using var package = await CreatePackage();
            using var corrupt = RewritePackage(package, (path, bytes) =>
                path == "experiments/000000/experiment.json" ? bytes.Concat(new byte[] { 0 }).ToArray() : bytes);

            var error = await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(corrupt));
            Assert.Contains("length", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task UnsupportedFutureSchemaIsRejected()
        {
            using var package = await CreatePackage();
            using var future = RewritePackage(package, (path, bytes) =>
            {
                if (path != "manifest.json") return bytes;
                using var json = JsonDocument.Parse(bytes);
                var text = Encoding.UTF8.GetString(bytes).Replace("\"schemaMajor\": 1", "\"schemaMajor\": 2");
                return Encoding.UTF8.GetBytes(text);
            });

            await Assert.ThrowsAsync<NotSupportedException>(() => FTXTCReader.ReadStream(future));
        }

        [Fact]
        public async Task RecoveryRetainsIntegratedExperimentWhenThermogramIsCorrupt()
        {
            using var package = await CreatePackage();
            using var corrupt = RewritePackage(package, (path, bytes) =>
                path == "experiments/000000/thermogram.ftxb" ? bytes.Concat(new byte[] { 0 }).ToArray() : bytes);

            var recovered = await FTXTCReader.ReadWithRecovery(corrupt, FtxtcReadPolicy.RecoverUsableContent);
            var experiment = Assert.Single(recovered.Containers.OfType<ExperimentData>(), item => item.DataPoints.Count == 0);
            Assert.True(recovered.IsPartial);
            Assert.Empty(experiment.DataPoints);
            Assert.NotEmpty(experiment.Injections);
            Assert.Contains(recovered.Issues, issue => issue.Code == "checksum-failure");

            corrupt.Position = 0;
            await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(corrupt));
        }

        [Fact]
        public async Task SaveLoadSaveKeepsNormalizedPayloadHashes()
        {
            using var first = await CreatePackage();
            var restored = await FTXTCReader.ReadStream(first);
            using var second = new MemoryStream();
            await FTXTCWriter.WriteStream(second, restored.OfType<ExperimentData>(), restored.OfType<AnalysisResult>());

            Assert.Equal(PayloadHashes(first), PayloadHashes(second));
        }

        [Fact]
        public void PersistenceRegistryCoversEverySolutionModel()
        {
            var expected = Enum.GetValues<AnalysisModel>().OrderBy(value => value).ToArray();
            Assert.Equal(expected, FtxtcModelRegistry.SupportedModels.OrderBy(value => value));
            Assert.All(expected, model => Assert.False(string.IsNullOrWhiteSpace(FtxtcWireIds.Model(model))));
        }

        [Fact]
        public async Task FtitcOpenIsDetachedForNativeSaveAs()
        {
            FTITCFormat.CurrentAccessedAppDocumentPath = "previous.ftxtc";
            var restored = await FTITCReader.ReadPath(Fixture("one-set.ftitc"));

            Assert.NotEmpty(restored);
            Assert.Equal(string.Empty, FTITCFormat.CurrentAccessedAppDocumentPath);
        }

        static async Task<MemoryStream> CreatePackage()
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, containers.OfType<ExperimentData>(), containers.OfType<AnalysisResult>());
            package.Position = 0;
            return package;
        }

        static MemoryStream RewritePackage(Stream source, Func<string, byte[], byte[]> transform)
        {
            var items = new List<(string path, byte[] bytes)>();
            source.Position = 0;
            using (var input = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            {
                foreach (var entry in input.Entries)
                {
                    using var entryStream = entry.Open();
                    using var copy = new MemoryStream();
                    entryStream.CopyTo(copy);
                    items.Add((entry.FullName, transform(entry.FullName, copy.ToArray())));
                }
            }
            var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var item in items)
                {
                    var entry = archive.CreateEntry(item.path);
                    using var destination = entry.Open();
                    destination.Write(item.bytes, 0, item.bytes.Length);
                }
            }
            output.Position = 0;
            return output;
        }

        static string[] PayloadHashes(Stream source)
        {
            source.Position = 0;
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            return archive.Entries.Where(entry => entry.FullName != "manifest.json")
                .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
                .Select(entry =>
                {
                    using var stream = entry.Open();
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    return entry.FullName + ":" + Convert.ToHexString(sha.ComputeHash(stream));
                }).ToArray();
        }

        static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    }
}
