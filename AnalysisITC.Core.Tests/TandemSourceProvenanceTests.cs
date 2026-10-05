using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Solver events")]
public sealed class TandemSourceProvenanceTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();
    readonly ITandemImportPromptService originalPrompt = PlatformServices.TandemImportPromptService;

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        original.ApplyToSettings();
        PlatformServices.RegisterTandemImportPromptService(originalPrompt);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("fixed")]
    [InlineData("per-transition")]
    [InlineData("automatic")]
    public void EveryMergeModeRecordsOrderedReadOnlySourceIdentities(string mode)
    {
        AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
        var sources = new List<ExperimentData> { Source("second"), Source("first"), Source("third") };
        var settings = new TandemConcatenation.BackMixingSettings
            { UseBackMixingMethod = true, DeadVolume = 60e-6, MixingFraction = .2 };
        var tandem = mode switch
        {
            "simple" => TandemConcatenation.ConcatTandem(sources),
            "fixed" => TandemConcatenation.ConcatTandemWithBackMixing(sources, settings),
            _ => TandemConcatenation.ConcatTandemWithBackMixing(sources, settings, new[] { .1, .2 },
                automaticCriterion: mode == "automatic" ? TandemMixingCriterion.ModelFree : null),
        };

        Assert.Equal(new[] { "second", "first", "third" }, tandem.TandemSourceExperimentIds);
        sources.Reverse();
        Assert.Equal(new[] { "second", "first", "third" }, tandem.TandemSourceExperimentIds);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)tandem.TandemSourceExperimentIds)[0] = "changed");
    }

    [Fact]
    public async Task NativeRoundTripRetainsIdsWithoutSavingSourceExperiments()
    {
        var tandem = Source("tandem");
        tandem.TandemMergeDescription = "Recorded merge";
        tandem.SetTandemSourceExperimentIds(new[] { "second", "first" });
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { tandem });
        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        using (var reader = new StreamReader(archive.GetEntry("experiments/000000/experiment.json").Open()))
        {
            var stored = JsonNode.Parse(await reader.ReadToEndAsync());
            Assert.Equal(new[] { "second", "first" }, stored["tandemSourceExperimentIds"].AsArray().Select(id => id.GetValue<string>()));
        }
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<ExperimentData>());
        Assert.Equal(new[] { "second", "first" }, restored.TandemSourceExperimentIds);
        Assert.Equal("Recorded merge", restored.TandemMergeDescription);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbsentOrNullNativeSourceIdsRemainReadable(bool explicitNull)
    {
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { Source("legacy") });
        using var historical = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("experiment.json", StringComparison.Ordinal)) return bytes;
            var json = JsonNode.Parse(bytes).AsObject();
            Assert.False(json.ContainsKey("tandemSourceExperimentIds"));
            if (explicitNull) json["tandemSourceExperimentIds"] = null;
            return Encoding.UTF8.GetBytes(json.ToJsonString());
        }, schemaMinor: FTXTCFormat.SchemaMinor);
        var restored = Assert.Single((await FTXTCReader.ReadStream(historical)).OfType<ExperimentData>());
        Assert.Empty(restored.TandemSourceExperimentIds);
    }

    [Fact]
    public void DuplicationSyntheticCloneAndBootstrapRestoreKeepSourceLinks()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var model = InjectionProcessingMethodTests.FittedModel();
        model.Data.TandemMergeDescription = "Recorded merge";
        model.Data.SetTandemSourceExperimentIds(new[] { "second", "first" });
        DataManager.AddData(model.Data);
        DataManager.DuplicateSelectedData(model.Data);
        var duplicate = DataManager.Data.Single(data => data.UniqueID != model.Data.UniqueID);
        var synthetic = model.Data.GetSynthClone(new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None });
        var bootstrap = BootstrapModelSnapshot.Capture(model.Solution, 0).Restore(model).Data;

        foreach (var copy in new[] { duplicate, synthetic, bootstrap })
        {
            Assert.Equal(new[] { "second", "first" }, copy.TandemSourceExperimentIds);
            Assert.Equal("Recorded merge", copy.TandemMergeDescription);
        }
        model.Data.SetTandemSourceExperimentIds(new[] { "new-id" });
        Assert.All(new[] { duplicate, synthetic, bootstrap }, copy =>
            Assert.Equal(new[] { "second", "first" }, copy.TandemSourceExperimentIds));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SingleFileTandemImportRecordsRunCountAndAppliedModeWithoutChangingComments(bool interactive, bool backMixing)
    {
        AppSettings.DilutionCalculationMethod = DilutionMethod.DiscreteDisplacement;
        var prompt = new TandemPrompt(backMixing);
        PlatformServices.RegisterTandemImportPromptService(prompt);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Lenette112+113+114.itc");
        var comments = File.ReadLines(path).First(line => line.StartsWith("?", StringComparison.Ordinal)).Substring(1).Trim();
        using var raw = File.OpenRead(path);
        var imported = MicroCalITC200Reader.ReadStream(raw, "import.itc", interactive: interactive);

        Assert.Equal(3, imported.Segments.Count);
        Assert.Equal(interactive ? 3 : 0, prompt.RunCount);
        Assert.StartsWith("3 experiments concatenated in one data file", imported.TandemMergeDescription);
        Assert.Contains(backMixing ? "fixed back-mixing" : "no back-mixing", imported.TandemMergeDescription);
        Assert.Contains(interactive ? "Discrete displacement bookkeeping" : "MicroCal bookkeeping", imported.TandemMergeDescription);
        if (backMixing)
        {
            Assert.Contains("DeadVolume=60 µL", imported.TandemMergeDescription);
            Assert.Contains("MixFrac=20.0%", imported.TandemMergeDescription);
            Assert.Contains("RemoveOverflow=False", imported.TandemMergeDescription);
        }
        Assert.Empty(imported.TandemSourceExperimentIds);
        Assert.Equal(comments, imported.Comments);
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { imported });
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<ExperimentData>());
        Assert.Equal(imported.TandemMergeDescription, restored.TandemMergeDescription);
        Assert.Empty(restored.TandemSourceExperimentIds);
        Assert.Equal(comments, restored.Comments);
    }

    [Fact]
    public void ReferenceCandidatesUseIdsApplicationOrderAndOnlyDirectResultReferences()
    {
        var first = Source("first"); var second = Source("second");
        first.Name = "Renamed source"; second.Name = "Same name";
        var unrelated = Source("different-id"); unrelated.Name = second.Name;
        var sourceBuffer = Source("source-buffer");
        first.SetBufferSubtraction(sourceBuffer, BufferSubtractionMethod.MatchedInjection, notify: false);
        var tandem = Source("tandem");
        tandem.SetTandemSourceExperimentIds(new[] { "first", "missing", "second" });
        tandem.SetBufferSubtraction(first, BufferSubtractionMethod.MatchedInjection, notify: false);
        var result = Result(tandem);
        var available = new[] { second, unrelated, first, sourceBuffer, first, tandem };

        Assert.Equal(new[] { "second", "first" }, AnalysisReportExperimentReferences.Candidates(new[] { result, result }, available)
            .Select(data => data.UniqueID));
        Assert.Equal("Buffer reference; Tandem source", AnalysisReportExperimentReferences.PickerRole(first, new[] { result }));
        Assert.Equal("Tandem source", AnalysisReportExperimentReferences.PickerRole(second, new[] { result }));
        Assert.Equal("", AnalysisReportExperimentReferences.PickerRole(unrelated, new[] { result }));
        Assert.Equal("Included through Result 2", AnalysisReportExperimentReferences.PickerRole(first, new[] { result, Result(first) }));
        Assert.Equal("Buffer and tandem source for selected results", AnalysisReportExperimentReferences.PickerTooltip(first, new[] { result }));
        Assert.Equal("Run merged into a selected tandem result", AnalysisReportExperimentReferences.PickerTooltip(second, new[] { result }));
        Assert.Equal("Buffer run subtracted from a selected result", AnalysisReportExperimentReferences.PickerTooltip(sourceBuffer, new[] { Result(first) }));
        Assert.Equal("Adds saved data as a supporting experiment", AnalysisReportExperimentReferences.PickerTooltip(unrelated, new[] { result }));
        Assert.Equal("Already reported with a selected result", AnalysisReportExperimentReferences.PickerTooltip(first, new[] { result, Result(first) }));
        Assert.Empty(AnalysisReportExperimentReferences.Candidates(Array.Empty<AnalysisResult>(), available));
    }

    [Fact]
    public void AppendixRolesListAllTargetsIncludingSourcesAlreadyCoveredByResults()
    {
        var first = Source("first"); var second = Source("second"); var tandem = Source("tandem");
        tandem.SetTandemSourceExperimentIds(new[] { "first", "second" });
        tandem.SetBufferSubtraction(first, BufferSubtractionMethod.MatchedInjection, notify: false);
        var results = new[] { Result(tandem), Result(tandem), Result(first) };
        var report = new AnalysisReport();
        report.SetResultIds(results.Select(result => result.UniqueID));
        report.SetSupportingExperimentIds(new[] { first.UniqueID, second.UniqueID });
        var document = AnalysisReportBuilder.Build(report, id => results.FirstOrDefault(result => result.UniqueID == id),
            id => new[] { first, second, tandem }.FirstOrDefault(data => data.UniqueID == id));

        Assert.True(document.IsValid);
        Assert.Equal("S1", Assert.Single(document.SupportingExperiments).Label);
        var sources = document.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportTableBlock>()
            .Single(table => table.Title == "Experiment sources");
        Assert.Equal(3, sources.Rows.Count);
        var firstRow = sources.Rows.Single(row => row.Cells[1] == first.Name);
        Assert.Equal("3A", firstRow.Cells[0]);
        Assert.Equal("Buffer reference for 1A, 2A; Tandem source for 1A, 2A", firstRow.Cells[7]);
        Assert.Equal("Tandem source for 1A, 2A", sources.Rows.Single(row => row.Cells[1] == second.Name).Cells[7]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingSourcesAreOnlyExperimentDetailsInFullCondensedAndSupportingContent(int missingCount)
    {
        var first = Source("first"); var second = Source("second"); var tandem = Source("tandem");
        tandem.TandemMergeDescription = "Recorded merge";
        tandem.SetTandemSourceExperimentIds(new[] { "first", "second" });
        var supportingTandem = Source("supporting-tandem");
        supportingTandem.TandemMergeDescription = "Another recorded merge";
        supportingTandem.SetTandemSourceExperimentIds(new[] { "first", "second" });
        var results = new[] { Result(tandem), Result(tandem) };
        var report = new AnalysisReport();
        report.SetResultIds(results.Select(result => result.UniqueID));
        report.SetSupportingExperimentIds(new[] { supportingTandem.UniqueID });
        var available = new[] { first, second }.Skip(missingCount).Append(supportingTandem).ToList();
        var document = AnalysisReportBuilder.Build(report, id => results.FirstOrDefault(result => result.UniqueID == id),
            id => available.FirstOrDefault(data => data.UniqueID == id));

        Assert.True(document.IsValid);
        Assert.DoesNotContain(document.Diagnostics, item => item.Message.Contains("Tandem sources", StringComparison.Ordinal));
        var metadata = document.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
            .Where(block => block.Items.Any(item => item.Label == "Tandem merge origin")).ToList();
        Assert.Equal(3, metadata.Count);
        foreach (var block in metadata)
        {
            if (missingCount == 0) Assert.DoesNotContain(block.Items, item => item.Label == "Tandem sources");
            else
            {
                var note = Assert.Single(block.Items, item => item.Label == "Tandem sources");
                Assert.Equal(missingCount == 1 ? "1 of 2 recorded source experiments is not in this project."
                    : "2 of 2 recorded source experiments are not in this project.", note.Value);
                var items = block.Items.ToList();
                Assert.Equal("Tandem merge origin", items[items.IndexOf(note) - 1].Label);
            }
        }
        // Available sources were deliberately left unchecked; rendering must not add them.
        Assert.Equal(new[] { "supporting-tandem" }, document.SupportingExperiments.Select(item => item.Id));
    }

    [Fact]
    public void LegacyProvenanceHasNoNoteAndExplicitResolverOverridesProjectLookup()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var first = Source("first"); var tandem = Source("tandem");
        tandem.TandemMergeDescription = "Legacy merge";
        tandem.AddSegment(new TandemExperimentSegment(0, tandem.CellConcentration, 0));
        var result = Result(tandem);
        Assert.DoesNotContain(Details(AnalysisReportBuilder.Build(result)), item => item.Label == "Tandem sources");
        tandem.SetTandemSourceExperimentIds(new[] { "first", "second" });
        DataManager.AddData(first);
        Assert.Contains(Details(AnalysisReportBuilder.Build(result)), item => item.Label == "Tandem sources"
            && item.Value.StartsWith("1 of 2", StringComparison.Ordinal));
        var report = new AnalysisReport(); report.SetResultIds(new[] { result.UniqueID });
        Assert.Contains(Details(AnalysisReportBuilder.Build(report, _ => result, _ => null)), item => item.Label == "Tandem sources"
            && item.Value.StartsWith("2 of 2", StringComparison.Ordinal));
    }

    [Fact]
    public void InterpretationAddsSourceLinksWithoutChangingEmptyProvenanceFingerprints()
    {
        var source = Source("first"); var tandem = Source("tandem"); var result = Result(tandem);
        var report = new AnalysisReport(); report.SetResultIds(new[] { result.UniqueID });
        report.SetSupportingExperimentIds(new[] { source.UniqueID });
        AnalysisInterpretationPackage Build() => AnalysisInterpretationPackageBuilder.Build(report, _ => result,
            id => new[] { source, tandem }.FirstOrDefault(data => data.UniqueID == id));
        var before = AnalysisInterpretationPromptBuilder.Build(Build());
        Assert.DoesNotContain("tandemSourceExperimentIds", before.CanonicalPackageJson);
        tandem.SetTandemSourceExperimentIds(Array.Empty<string>());
        Assert.Equal(before.InputFingerprint, AnalysisInterpretationPromptBuilder.Build(Build()).InputFingerprint);
        tandem.SetTandemSourceExperimentIds(new[] { "first", "unavailable" });
        var package = Build();
        Assert.Equal(new[] { "first", "unavailable" }, package.Result.Experiments.Single().TandemSourceExperimentIds);
        var after = AnalysisInterpretationPromptBuilder.Build(package);
        Assert.Contains("tandemSourceExperimentIds", after.CanonicalPackageJson);
        Assert.Contains("tandemSourceExperimentIds", after.ModelPackageJson);
        Assert.NotEqual(before.InputFingerprint, after.InputFingerprint);
        Assert.Equal(JsonNode.Parse(before.CanonicalPackageJson)["packageSchemaVersion"].GetValue<string>(), package.PackageSchemaVersion);
        var oldSupporting = JsonNode.Parse(before.CanonicalPackageJson)["supportingExperiments"].ToJsonString();
        Assert.Equal(oldSupporting, JsonNode.Parse(after.CanonicalPackageJson)["supportingExperiments"].ToJsonString());
    }

    static IEnumerable<AnalysisReportKeyValueItem> Details(AnalysisReportDocument document) => document.Sections
        .SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
        .Where(block => block.Title == "Experiment details").SelectMany(block => block.Items);

    static ExperimentData Source(string id)
    {
        var data = new ExperimentData(id + ".itc")
        {
            CellConcentration = new FloatWithError(10e-6), SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 200e-6, TargetTemperature = 25, MeasuredTemperature = 25,
        };
        data.SetID(id);
        data.DataPoints.Add(new DataPoint(0, 0, 25)); data.DataPoints.Add(new DataPoint(120, 0, 25));
        data.BaseLineCorrectedDataPoints = data.DataPoints.ToList();
        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 2e-10, include: true)
                { Time = 30 + index * 30, Ratio = index + 1, ActualCellConcentration = 10e-6, ActualTitrantConcentration = (index + 1) * 1e-6 };
            injection.SetPeakArea(new FloatWithError(-2e-6, 1e-8));
            injection.IsIntegrated = true;
            data.Injections.Add(injection);
        }
        return data;
    }

    static AnalysisResult Result(ExperimentData data)
    {
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25_000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            { Algorithm = SolverAlgorithm.LevenbergMarquardt, Termination = SolverTermination.Converged, Loss = 1 });
        model.Solution = SolutionInterface.FromModel(model, convergence);
        var globalModel = new GlobalModel(new List<Model> { model })
            { Parameters = new GlobalModelParameters(), ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None } };
        globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = globalModel }, new List<SolutionInterface> { model.Solution }, convergence,
            reconstructBootstrap: false);
        globalModel.Solution = global;
        return new AnalysisResult(global);
    }

    sealed class TandemPrompt(bool backMixing) : ITandemImportPromptService
    {
        public int RunCount { get; private set; }
        public TandemConcatenation.BackMixingSettings AskBackMixingSettings(string fileName, int segmentCount,
            TandemConcatenation.BackMixingSettings defaults)
        {
            RunCount = segmentCount;
            return new TandemConcatenation.BackMixingSettings
                { UseBackMixingMethod = backMixing, DeadVolume = 60e-6, MixingFraction = .2, DidRemoveOverflow = false };
        }
    }
}
