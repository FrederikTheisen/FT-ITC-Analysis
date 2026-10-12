using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class FtxtcCompressionTests
{
    [Theory]
    [InlineData(FtxtcReadPolicy.Strict)]
    [InlineData(FtxtcReadPolicy.RecoverUsableContent)]
    public async Task HighlyCompressedBootstrapInjectionsRestoreEveryReplicate(FtxtcReadPolicy policy)
    {
        const int replicateCount = 10_000;
        var model = InjectionProcessingMethodTests.FittedModel();
        model.Data.Injections.RemoveRange(4, model.Data.Injections.Count - 4);
        for (var index = 0; index < replicateCount; index++)
        {
            var replicate = SolutionInterface.FromModel(model, null);
            replicate.BootstrapReplicateIndex = index;
            model.Solution.BootstrapSolutions.Add(replicate);
        }
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { model.Data });
        using var compressed = Recompress(package);
        AssertHighlyCompressed(compressed, "solutions/000000/bootstrap-injections.ftxb");

        var restored = await FTXTCReader.ReadWithRecovery(compressed, policy);

        Assert.Empty(restored.Issues);
        var experiment = Assert.Single(restored.Containers.OfType<ExperimentData>());
        Assert.Equal(replicateCount, experiment.Solution.BootstrapSolutions.Count);
        Assert.Equal(Enumerable.Range(0, replicateCount),
            experiment.Solution.BootstrapSolutions.Select(value => value.BootstrapReplicateIndex.Value));
        Assert.All(experiment.Solution.BootstrapSolutions, replicate =>
        {
            Assert.Equal(model.Data.Injections.Select(value => value.Volume),
                replicate.Data.Injections.Select(value => value.Volume));
            Assert.Equal(model.Data.Injections.Select(value => value.ActualCellConcentration),
                replicate.Data.Injections.Select(value => value.ActualCellConcentration));
            Assert.Equal(model.Data.Injections.Select(value => value.ActualTitrantConcentration),
                replicate.Data.Injections.Select(value => value.ActualTitrantConcentration));
            Assert.Equal(model.Data.Injections.Select(value => value.Ratio),
                replicate.Data.Injections.Select(value => value.Ratio));
        });
    }

    [Theory]
    [InlineData(FtxtcReadPolicy.Strict)]
    [InlineData(FtxtcReadPolicy.RecoverUsableContent)]
    public async Task HighlyCompressedJsonRestoresWithoutDroppingContent(FtxtcReadPolicy policy)
    {
        var experiment = new ExperimentData("Repetitive metadata") { Comments = new string('x', 2 * 1024 * 1024) };
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { experiment });
        AssertHighlyCompressed(package, "experiments/000000/experiment.json");

        var restored = await FTXTCReader.ReadWithRecovery(package, policy);

        Assert.Empty(restored.Issues);
        Assert.Equal(experiment.Comments, Assert.Single(restored.Containers.OfType<ExperimentData>()).Comments);
    }

    [Theory]
    [InlineData("individualMaterializedPayloadBytes")]
    [InlineData("expandedArchiveBytes")]
    [InlineData("cumulativeBinaryBytes")]
    public async Task HighlyCompressedEntriesStillRespectConfiguredByteBudgets(string budget)
    {
        using var package = CreateCompressedEntry("payload.ftxb", new byte[2 * 1024 * 1024]);
        var limits = new FtxtcReadLimits();
        switch (budget)
        {
            case "individualMaterializedPayloadBytes": limits.IndividualMaterializedPayloadBytes = 1024 * 1024; break;
            case "expandedArchiveBytes": limits.ExpandedArchiveBytes = 1024 * 1024; break;
            case "cumulativeBinaryBytes": limits.CumulativeBinaryBytes = 1024 * 1024; break;
        }

        var failure = await Assert.ThrowsAsync<FtxtcResourceLimitException>(() =>
            FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict, limits: limits));

        Assert.Equal(budget, failure.Budget);
    }

    [Fact]
    public async Task EntryAboveAbsoluteSizeLimitIsRejectedBeforeDecompression()
    {
        using var package = CreateCompressedEntry("payload.ftxb", new byte[16]);
        using var oversized = WithDeclaredZipLength(package, checked((uint)FTXTCFormat.MaxEntryBytes + 1));

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FTXTCReader.ReadWithRecovery(oversized, FtxtcReadPolicy.Strict));

        Assert.Contains("exceeds the size limit", failure.Message);
    }

    [Fact]
    public async Task TruncatedEntryMustMatchZipDeclaration()
    {
        using var package = CreateCompressedEntry("payload.ftxb", new byte[2 * 1024 * 1024]);
        using var forged = WithDeclaredZipLength(package, 3 * 1024 * 1024);

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FTXTCReader.ReadWithRecovery(forged, FtxtcReadPolicy.Strict));

        Assert.Contains("does not match its declared uncompressed length", failure.Message);
    }

    [Fact]
    public async Task HighlyCompressedPayloadStillRequiresManifestAuthentication()
    {
        var experiment = new ExperimentData("Authenticated metadata") { Comments = new string('x', 2 * 1024 * 1024) };
        using var source = new MemoryStream();
        await FTXTCWriter.WriteStream(source, new[] { experiment });
        source.Position = 0;
        using var tampered = new MemoryStream();
        using (var input = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
        using (var output = new ZipArchive(tampered, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                using var entryInput = entry.Open();
                using var bytes = new MemoryStream();
                entryInput.CopyTo(bytes);
                var payload = bytes.ToArray();
                if (entry.FullName == "experiments/000000/experiment.json")
                    payload = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(payload).Replace(new string('x', 100), new string('y', 100)));
                using var entryOutput = output.CreateEntry(entry.FullName, CompressionLevel.Optimal).Open();
                entryOutput.Write(payload);
            }
        }
        tampered.Position = 0;

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FTXTCReader.ReadWithRecovery(tampered, FtxtcReadPolicy.Strict));

        Assert.Contains("failed its length or SHA-256 check", failure.Message);
    }

    static void AssertHighlyCompressed(Stream package, string path)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        {
            var entry = archive.GetEntry(path);
            Assert.NotNull(entry);
            Assert.True(entry.Length > 1024 * 1024);
            Assert.True(entry.Length / entry.CompressedLength > 200,
                $"Expected the previous compression guard to reject '{path}': {entry.Length} / {entry.CompressedLength}.");
        }
        package.Position = 0;
    }

    static MemoryStream CreateCompressedEntry(string path, byte[] payload)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        using (var output = archive.CreateEntry(path, CompressionLevel.Optimal).Open())
            output.Write(payload);
        package.Position = 0;
        return package;
    }

    static MemoryStream Recompress(Stream package)
    {
        // ZIP compression settings can change without changing any saved data.
        package.Position = 0;
        var compressed = new MemoryStream();
        using (var input = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        using (var output = new ZipArchive(compressed, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in input.Entries)
            {
                using var source = entry.Open();
                using var destination = output.CreateEntry(entry.FullName, CompressionLevel.SmallestSize).Open();
                source.CopyTo(destination);
            }
        compressed.Position = 0;
        return compressed;
    }

    static MemoryStream WithDeclaredZipLength(MemoryStream package, uint length)
    {
        var bytes = package.ToArray();
        // EOCD offset 16 points to the central directory. Its entry's offset 24
        // is the uncompressed length; patching it needs no oversized allocation.
        var directoryOffset = BitConverter.ToInt32(bytes, bytes.Length - 22 + 16);
        BitConverter.GetBytes(length).CopyTo(bytes, directoryOffset + 24);
        return new MemoryStream(bytes);
    }
}
