using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class FtxtcPayloadBudgetTests
{
    public static IEnumerable<object[]> PayloadBoundaries()
    {
        foreach (var policy in new[] { FtxtcReadPolicy.Strict, FtxtcReadPolicy.RecoverUsableContent })
            foreach (var bootstrap in new[] { false, true })
                foreach (var budget in new[] { "cumulativeJsonBytes", "cumulativeBinaryBytes" })
                    foreach (var margin in new[] { -1, 0, 1 })
                        yield return new object[] { policy, bootstrap, budget, margin };
    }

    [Theory]
    [MemberData(nameof(PayloadBoundaries))]
    public async Task CumulativePayloadLimitsUseUniqueExpandedEntryBytes(
        FtxtcReadPolicy policy, bool bootstrap, string budget, int margin)
    {
        using var package = await Package(bootstrap);
        var sizes = Measure(package);
        var limits = new FtxtcReadLimits();
        if (budget == "cumulativeJsonBytes") limits.CumulativeJsonBytes = sizes.Json + margin;
        else limits.CumulativeBinaryBytes = sizes.Binary + margin;

        if (margin < 0)
        {
            var error = await Assert.ThrowsAsync<FtxtcResourceLimitException>(() =>
                FTXTCReader.ReadWithRecovery(package, policy, limits: limits));
            Assert.Equal(budget, error.Budget);
        }
        else
        {
            var restored = await FTXTCReader.ReadWithRecovery(package, policy, limits: limits);
            AssertComplete(restored, bootstrap);
        }
    }

    [Theory]
    [InlineData(FtxtcReadPolicy.Strict)]
    [InlineData(FtxtcReadPolicy.RecoverUsableContent)]
    public async Task IndependentResourceLimitsStillRejectOneUnitOverTheirBoundaries(FtxtcReadPolicy policy)
    {
        using var package = await Package(bootstrap: true);
        var sizes = Measure(package);
        // One experiment and solution, two results sharing that fit, and one report.
        // The primary fit and the second result context each restore two replicates.
        int injectionCount;
        using (var source = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        using (var entry = source.GetEntry("experiments/000000/experiment.json").Open())
        using (var metadata = System.Text.Json.JsonDocument.Parse(entry))
            injectionCount = metadata.RootElement.GetProperty("injections").GetArrayLength();
        var boundaries = new (string name, long count, Action<FtxtcReadLimits, long> set)[]
        {
            ("expandedArchiveBytes", sizes.Expanded, (limits, count) => limits.ExpandedArchiveBytes = count),
            ("individualMaterializedPayloadBytes", sizes.LargestEntry, (limits, count) => limits.IndividualMaterializedPayloadBytes = count),
            ("individualJsonPayloadBytes", sizes.LargestJson, (limits, count) => limits.IndividualJsonPayloadBytes = count),
            ("rootComponents", 5, (limits, count) => limits.RootComponents = count),
            ("totalRestoredSamples", 6, (limits, count) => limits.TotalRestoredSamples = count),
            ("bootstrapReplicates", 4, (limits, count) => limits.BootstrapReplicates = count),
            ("injectionRecords", 5L * injectionCount, (limits, count) => limits.InjectionRecords = count),
        };

        foreach (var boundary in boundaries)
        {
            var limits = new FtxtcReadLimits();
            boundary.set(limits, boundary.count);
            package.Position = 0;
            AssertComplete(await FTXTCReader.ReadWithRecovery(package, policy, limits: limits), bootstrap: true);

            boundary.set(limits, boundary.count - 1);
            package.Position = 0;
            var error = await Assert.ThrowsAsync<FtxtcResourceLimitException>(() =>
                FTXTCReader.ReadWithRecovery(package, policy, limits: limits));
            Assert.Equal(boundary.name, error.Budget);
        }
    }

    [Theory]
    [InlineData(FtxtcReadPolicy.Strict)]
    [InlineData(FtxtcReadPolicy.RecoverUsableContent)]
    public async Task SharedThermogramAndBaselineReferencesStillConsumeRestoredSampleBudget(FtxtcReadPolicy policy)
    {
        var first = new ExperimentData("first");
        var second = new ExperimentData("second");
        AddTrace(first);
        AddTrace(second);
        using var original = new MemoryStream();
        await FTXTCWriter.WriteStream(original, new[] { first, second });
        using var package = ShareTraceReferences(original);
        var sizes = Measure(package);
        var limits = new FtxtcReadLimits
        {
            CumulativeJsonBytes = sizes.Json,
            CumulativeBinaryBytes = sizes.Binary,
            TotalRestoredSamples = 12,
        };

        var restored = await FTXTCReader.ReadWithRecovery(package, policy, limits: limits);
        Assert.Empty(restored.Issues);
        Assert.Equal(2, restored.Containers.Length);
        Assert.All(restored.Containers.Cast<ExperimentData>(), data => Assert.Equal(3, data.DataPoints.Count));

        limits.TotalRestoredSamples = 11;
        package.Position = 0;
        var error = await Assert.ThrowsAsync<FtxtcResourceLimitException>(() =>
            FTXTCReader.ReadWithRecovery(package, policy, limits: limits));
        Assert.Equal("totalRestoredSamples", error.Budget);
    }

    static async Task<MemoryStream> Package(bool bootstrap)
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: bootstrap);
        AddTrace(model.Data);
        model.Data.UpdateSolution(model);
        var global = new GlobalModel(new List<Model> { model })
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = model.ModelCloneOptions,
        };
        global.Parameters.AddIndivdualParameter(model.Parameters);
        global.Solution = new GlobalSolution(new GlobalSolver { Model = global }, new List<SolutionInterface> { model.Solution },
            SolverConvergence.FromFixedFit(1, 1e-12), reconstructBootstrap: false);
        var results = new[] { new AnalysisResult(global.Solution), new AnalysisResult(global.Solution) };
        var report = new AnalysisReport();
        report.SetResultIds(results.Select(result => result.UniqueID));
        var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { model.Data }, results, reports: new[] { report });
        package.Position = 0;
        return package;
    }

    static void AddTrace(ExperimentData data)
    {
        data.DataPoints = new List<DataPoint> { new(0, 1e-6f, 25), new(1, 2e-6f, 25), new(2, 1e-6f, 25) };
        data.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
        data.Processor.Interpolator.Baseline = new List<Energy> { new(1e-7), new(2e-7), new(1e-7) };
        data.Processor.BaselineCompleted = true;
    }

    static void AssertComplete(FtxtcReadResult restored, bool bootstrap)
    {
        Assert.Empty(restored.Issues);
        var experiment = Assert.Single(restored.Containers.OfType<ExperimentData>());
        Assert.Equal(3, experiment.DataPoints.Count);
        Assert.Equal(3, experiment.Processor.Interpolator.Baseline.Count);
        Assert.Equal(bootstrap ? 2 : 0, experiment.Solution.BootstrapSolutions.Count);
        var results = restored.Containers.OfType<AnalysisResult>().ToArray();
        Assert.Equal(2, results.Length);
        Assert.Single(restored.Reports);
        var first = Assert.Single(results[0].Solution.Solutions);
        var second = Assert.Single(results[1].Solution.Solutions);
        Assert.Equal(first.Guid, second.Guid);
        Assert.NotSame(first, second);
        Assert.Same(experiment, first.Data);
        Assert.Same(experiment, second.Data);
        Assert.All(new[] { first, second }, member => Assert.Equal(bootstrap ? 2 : 0, member.BootstrapSolutions.Count));
    }

    static (long Json, long Binary, long Expanded, long LargestEntry, long LargestJson) Measure(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var json = archive.Entries.Where(entry => entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToArray();
        var binary = archive.Entries.Except(json).ToArray();
        var result = (json.Sum(entry => entry.Length), binary.Sum(entry => entry.Length),
            archive.Entries.Sum(entry => entry.Length), archive.Entries.Max(entry => entry.Length), json.Max(entry => entry.Length));
        package.Position = 0;
        return result;
    }

    static MemoryStream ShareTraceReferences(MemoryStream original)
    {
        original.Position = 0;
        using var input = new ZipArchive(original, ZipArchiveMode.Read, leaveOpen: true);
        var payloads = input.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var source = entry.Open();
            using var bytes = new MemoryStream();
            source.CopyTo(bytes);
            return bytes.ToArray();
        });
        var project = FTXTCFormat.ReadJson<FtxtcProject>(payloads[FTXTCFormat.ProjectPath], FTXTCFormat.ProjectPath);
        project.Experiments[1].Thermogram = project.Experiments[0].Thermogram;
        project.Experiments[1].Baseline = project.Experiments[0].Baseline;
        payloads[FTXTCFormat.ProjectPath] = FTXTCFormat.JsonBytes(project);
        var manifest = FTXTCFormat.ReadJson<FtxtcManifest>(payloads[FTXTCFormat.ManifestPath], FTXTCFormat.ManifestPath);
        var declaration = manifest.Entries.Single(entry => entry.Path == FTXTCFormat.ProjectPath);
        declaration.Length = payloads[FTXTCFormat.ProjectPath].LongLength;
        declaration.Sha256 = FTXTCFormat.Sha256(payloads[FTXTCFormat.ProjectPath]);
        payloads[FTXTCFormat.ManifestPath] = FTXTCFormat.JsonBytes(manifest);
        var package = new MemoryStream();
        using (var output = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var payload in payloads)
            {
                using var target = output.CreateEntry(payload.Key).Open();
                target.Write(payload.Value);
            }
        package.Position = 0;
        return package;
    }
}
