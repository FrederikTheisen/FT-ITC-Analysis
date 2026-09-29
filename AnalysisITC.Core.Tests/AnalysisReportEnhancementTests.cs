using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class AnalysisReportEnhancementTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();
    readonly ISettingsStore originalStore = PlatformServices.SettingsStore;

    public AnalysisReportEnhancementTests() => PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());

    public void Dispose()
    {
        original.ApplyToSettings();
        AppSettings.ApplySettings();
        PlatformServices.RegisterSettingsStore(originalStore);
        DataManager.Clear(DataClearMode.ResetSession);
    }

    [Fact]
    public void UserNameDefaultsStagesAndPersistsWithoutUsingAnOperatingSystemIdentity()
    {
        AppSettings.UserName = "";
        var defaults = PreferencesState.Defaults();
        Assert.Equal("", defaults.UserName);

        var staged = PreferencesState.FromSettings();
        staged.UserName = "Zoë 李";
        Assert.Equal("", AppSettings.UserName);
        staged.Apply();
        AppSettings.ApplySettings();
        AppSettings.UserName = "";
        AppSettings.Load();

        Assert.Equal("Zoë 李", PreferencesState.FromSettings().UserName);
        Assert.Equal("Zoë 李", AppSettings.UserName);
    }

    [Fact]
    public async Task PresentationSettingsUseStableWireIdsRoundTripAndRemainDetached()
    {
        var report = new AnalysisReport { Name = "Saved report" };
        var options = new AnalysisReportOptions
        {
            DocumentLabel = "Study β",
            Title = "Fixed title",
            AutomaticTitle = false,
            EnergyUnitFamily = EnergyUnitFamily.Calories,
            EnergyUnitOverride = EnergyUnit.Cal,
            UseKelvin = true,
            UncertaintyDisplayStyle = UncertaintyDisplayStyle.ConfidenceInterval,
            IncludeInjectionTables = false,
            CondenseRepeatedExperiments = false,
            ExpandedExplanations = true,
            ExtraTraceability = true,
            Author = "Transient author",
            ReportId = "Transient ID",
            ApplicationVersion = "Transient version",
            GeneratedAtUtc = new DateTime(2026, 9, 1, 2, 3, 4, DateTimeKind.Utc),
        };
        options.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(AnalysisReportAdvancedSectionKind.Correlation, 2));
        options.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(AnalysisReportAdvancedSectionKind.DebyeHuckel));
        report.UpdatePresentationSettings(options);

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, Array.Empty<ExperimentData>(), reports: new[] { report });
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        using (var json = JsonDocument.Parse(archive.GetEntry("reports/000000/report.json")!.Open()))
        {
            var presentation = json.RootElement.GetProperty("presentationSettings");
            Assert.Equal("calories", presentation.GetProperty("energyUnitFamily").GetString());
            Assert.Equal("calorie", presentation.GetProperty("energyUnitOverride").GetString());
            Assert.Equal("confidence-interval", presentation.GetProperty("uncertaintyDisplayStyle").GetString());
            Assert.True(presentation.GetProperty("expandedExplanations").GetBoolean());
            var advanced = presentation.GetProperty("advancedSections").EnumerateArray().ToArray();
            Assert.Equal("correlation", advanced[0].GetProperty("kind").GetString());
            Assert.Equal(2, advanced[0].GetProperty("correlationMemberIndex").GetInt32());
            Assert.Equal("debye-huckel", advanced[1].GetProperty("kind").GetString());
            foreach (var transient in new[] { "author", "reportId", "applicationVersion", "generatedAtUtc" })
                Assert.False(presentation.TryGetProperty(transient, out _));
        }

        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict)).Reports);
        Assert.True(restored.HasPresentationSettings);
        Assert.True(restored.PresentationSettingsEqual(options));
        Assert.Equal("", restored.PresentationSettings.Author);
        Assert.Equal(new[] { "correlation:member-2", "DebyeHuckel" },
            restored.PresentationSettings.AdvancedSections.Select(item => item.Key));

        var detached = restored.PresentationSettings;
        detached.Title = "Edited copy";
        detached.AdvancedSections.Clear();
        Assert.Equal("Fixed title", restored.PresentationSettings.Title);
        Assert.Equal(2, restored.PresentationSettings.AdvancedSections.Count);

        var detachedReport = restored.CreateDetachedCopy();
        var detachedSettings = detachedReport.PresentationSettings;
        detachedSettings.DocumentLabel = "Detached copy";
        detachedSettings.AdvancedSections.Clear();
        detachedReport.UpdatePresentationSettings(detachedSettings);
        Assert.Equal("Study β", restored.PresentationSettings.DocumentLabel);
        Assert.Equal(2, restored.PresentationSettings.AdvancedSections.Count);

        using var older = RewriteReportEntry(package, bytes =>
        {
            var state = JsonNode.Parse(bytes)!.AsObject();
            state["presentationSettings"]!.AsObject().Remove("expandedExplanations");
            return Encoding.UTF8.GetBytes(state.ToJsonString());
        });
        var restoredOlder = Assert.Single((await FTXTCReader.ReadWithRecovery(older, FtxtcReadPolicy.Strict)).Reports);
        Assert.False(restoredOlder.PresentationSettings.ExpandedExplanations);
    }

    [Fact]
    public async Task ReportWithoutPresentationFieldsLoadsAsAnOlderDefinition()
    {
        var report = new AnalysisReport { Name = "Historical report" };
        using var current = new MemoryStream();
        await FTXTCWriter.WriteStream(current, Array.Empty<ExperimentData>(), reports: new[] { report });
        current.Position = 0;
        using (var archive = new ZipArchive(current, ZipArchiveMode.Read, leaveOpen: true))
        using (var json = JsonDocument.Parse(archive.GetEntry("reports/000000/report.json")!.Open()))
            Assert.False(json.RootElement.TryGetProperty("presentationSettings", out _));
        using var older = RewriteReportEntry(current, bytes =>
        {
            var state = JsonNode.Parse(bytes)!.AsObject();
            state.Remove("presentationSettings");
            state.Remove("hasPresentationSettings");
            return Encoding.UTF8.GetBytes(state.ToJsonString());
        });

        var restored = Assert.Single((await FTXTCReader.ReadWithRecovery(older, FtxtcReadPolicy.Strict)).Reports);
        Assert.False(restored.HasPresentationSettings);
        Assert.True(restored.PresentationSettings.AutomaticTitle);
        Assert.Empty(restored.PresentationSettings.AdvancedSections);
        Assert.Equal("", restored.PresentationSettings.Title);
        var detached = restored.CreateDetachedCopy();
        Assert.False(detached.HasPresentationSettings);
        Assert.Equal("", detached.PresentationSettings.Author);
    }

    [Fact]
    public void BuilderUsesExplicitUnicodeAuthorAndFreshGenerationMetadataAndLimitsSignaturesToCover()
    {
        var first = CreateResult(1);
        var second = CreateResult(2);
        var report = new AnalysisReport { Name = "Test report" };
        report.SetResultIds(new[] { first.UniqueID, second.UniqueID });
        var generated = new DateTime(2026, 9, 3, 10, 15, 0, DateTimeKind.Utc);
        var options = new AnalysisReportOptions
        {
            Title = "Stability review",
            Author = "Zoë 李",
            GeneratedAtUtc = generated,
            ApplicationVersion = "9.8-test",
            ExtraTraceability = true,
        };

        var document = AnalysisReportBuilder.Build(report,
            id => id == first.UniqueID ? first : id == second.UniqueID ? second : null,
            _ => null, options);
        var cover = Assert.Single(document.Sections, section => section.Kind == AnalysisReportSectionKind.Cover);
        var signature = Assert.Single(cover.Blocks.OfType<AnalysisReportTextBlock>(), block => block.Title == "Signature");
        Assert.Contains(report.UniqueID, signature.Text);
        Assert.Contains("Signature for Zoë 李:", signature.Text);
        Assert.Contains("Date: ____________________", signature.Text);
        Assert.Contains(report.UniqueID, string.Join("\n", cover.Blocks.OfType<AnalysisReportTextBlock>().Select(block => block.Text)));
        Assert.Contains("Zoë 李", signature.Text);
        Assert.Equal("Zoë 李", document.Author);
        Assert.Equal(generated, document.GeneratedAtUtc);
        Assert.DoesNotContain(document.Sections.Where(section => section.Kind != AnalysisReportSectionKind.Cover)
            .SelectMany(section => section.Blocks).OfType<AnalysisReportTextBlock>(), block => block.Title == "Signature");
        Assert.DoesNotContain(document.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportTextBlock>(),
            block => block.Title == "Report generated by" && block.Text.Contains("Not recorded", StringComparison.Ordinal));

        options.ExtraTraceability = false;
        var withoutTrace = AnalysisReportBuilder.Build(report,
            id => id == first.UniqueID ? first : second, _ => null, options);
        Assert.DoesNotContain(withoutTrace.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportTextBlock>(),
            block => block.Title == "Signature");
        Assert.DoesNotContain(withoutTrace.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
            .SelectMany(block => block.Items), item => item.Label == "Report identifier" || item.Label == "Result identifiers");

        options.Author = "";
        options.ExtraTraceability = true;
        var noAuthor = AnalysisReportBuilder.Build(report,
            id => id == first.UniqueID ? first : second, _ => null, options);
        Assert.Equal("", noAuthor.Author);
        Assert.Contains(noAuthor.Sections.First(section => section.Kind == AnalysisReportSectionKind.Cover)
            .Blocks.OfType<AnalysisReportTextBlock>(), block => block.Title == "Report generated by" && block.Text == "Not recorded");
        Assert.Contains(noAuthor.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportTextBlock>(),
            block => block.Title == "Signature" && block.Text.Contains("Signature for Not recorded:", StringComparison.Ordinal));

        var single = AnalysisReportBuilder.Build(first, new AnalysisReportOptions
        { GeneratedAtUtc = generated, ApplicationVersion = "9.8-test" });
        var singleCover = single.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Cover);
        var analysisAsOf = singleCover.Blocks.OfType<AnalysisReportKeyValueBlock>()
            .SelectMany(block => block.Items).Single(item => item.Label == "Analysis as of");
        Assert.Equal("30 Aug 2026", analysisAsOf.Value);
        var reportDetails = single.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Report details");
        Assert.Contains(reportDetails.Items, item => item.Label == "Generated (UTC)" && item.Value == "2026-09-03 10:15:00Z");
    }

    [Theory]
    [InlineData(ExperimentDateSource.DataFile, "Experiment date (data file)")]
    [InlineData(ExperimentDateSource.UserModified, "Experiment date (user modified)")]
    [InlineData(ExperimentDateSource.FileSystem, "File timestamp")]
    [InlineData(ExperimentDateSource.Unknown, "Experiment date")]
    public void ExperimentDateTextExplainsItsProvenance(ExperimentDateSource source, string expectedLabel)
    {
        var result = CreateResult(1);
        var data = result.Solution.Solutions.Single().Data;
        data.DateSource = source;
        var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions { CondenseRepeatedExperiments = false });
        var details = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");

        Assert.Contains(details.Items, item => item.Label == expectedLabel);
        if (source == ExperimentDateSource.FileSystem)
            Assert.Contains(details.Items, item => item.Label == "Experiment date" && item.Value.Contains("Unavailable", StringComparison.Ordinal));
        if (source == ExperimentDateSource.Unknown)
            Assert.Contains(details.Items, item => item.Value == "Unavailable");
    }

    [Theory]
    [InlineData(ExperimentDateSource.DataFile, "Experiment date (data file)")]
    [InlineData(ExperimentDateSource.UserModified, "Experiment date (user modified)")]
    [InlineData(ExperimentDateSource.FileSystem, "File timestamp")]
    public void CondensedAndSupportingExperimentDetailsRetainDateProvenance(
        ExperimentDateSource source, string expectedLabel)
    {
        var repeated = CreateResult(1);
        var repeatedAgain = CreateResult(1);
        var firstData = repeated.Solution.Solutions.Single().Data;
        var secondData = repeatedAgain.Solution.Solutions.Single().Data;
        secondData.SetID(firstData.UniqueID);
        secondData.DateSource = source;
        var repeatedReport = new AnalysisReport();
        repeatedReport.SetResultIds(new[] { repeated.UniqueID, repeatedAgain.UniqueID });
        var condensed = AnalysisReportBuilder.Build(repeatedReport,
            id => id == repeated.UniqueID ? repeated : id == repeatedAgain.UniqueID ? repeatedAgain : null,
            _ => null, new AnalysisReportOptions { CondenseRepeatedExperiments = true });
        var repeatedSection = condensed.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Single(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Any(block => block.Title == "Experiment details — condensed"));
        var metadata = repeatedSection.Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Experiment details — condensed");
        Assert.Contains(metadata.Items, item => item.Label == expectedLabel);

        var report = new AnalysisReport();
        report.SetResultIds(new[] { repeated.UniqueID });
        var supporting = CreateResult(1).Solution.Solutions.Single().Data;
        supporting.SetID(Guid.NewGuid().ToString("N"));
        supporting.DateSource = source;
        report.SetSupportingExperimentIds(new[] { supporting.UniqueID });
        var withSupporting = AnalysisReportBuilder.Build(report,
            id => id == repeated.UniqueID ? repeated : null,
            id => id == supporting.UniqueID ? supporting : null,
            new AnalysisReportOptions());
        var supportingMetadata = withSupporting.Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.Contains(supportingMetadata.Items, item => item.Label == expectedLabel);
    }

    [Fact]
    public void SupportingSubtractionNotesDistinguishAbsentAppliedAndUnavailableReferences()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var result = CreateResult(1);
        var target = CreateResult(1).Solution.Solutions.Single().Data;
        var reference = CreateResult(1).Solution.Solutions.Single().Data;
        target.SetID(Guid.NewGuid().ToString("N"));
        reference.SetID(Guid.NewGuid().ToString("N"));
        reference.Name = "Buffer blank";
        DataManager.AddData(new[] { target, reference });
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        report.SetSupportingExperimentIds(new[] { target.UniqueID });
        var options = new AnalysisReportOptions();

        AnalysisReportDocument BuildSupporting() => AnalysisReportBuilder.Build(report,
            id => id == result.UniqueID ? result : null,
            id => id == target.UniqueID ? target : null,
            options);
        AnalysisReportKeyValueBlock Processing(AnalysisReportDocument document) => document
            .Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Correction and exceptions");

        var withoutSubtraction = BuildSupporting();
        var withoutSubtractionProcessing = withoutSubtraction.Sections
            .Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>()
            .FirstOrDefault(block => block.Title == "Correction and exceptions");
        Assert.Null(withoutSubtractionProcessing);
        var targetMetadata = withoutSubtraction.Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.Contains(targetMetadata.Items, item => item.Label == "Buffer subtraction" && item.Value == "None");

        target.SetBufferSubtraction(reference, BufferSubtractionMethod.Linear);
        var withSubtraction = BuildSupporting();
        Assert.Contains(Processing(withSubtraction).Items,
            item => item.Label == "Integrated heats" && item.Value == "Stored corrected heats; configured reference Buffer blank (Linear); current processing details are listed separately");
        targetMetadata = withSubtraction.Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.Contains(targetMetadata.Items, item => item.Label == "Buffer subtraction"
            && item.Value == "Current processing: reference Buffer blank; Linear");

        target.Attributes.RemoveAll(item => item.Key == AttributeKey.BufferSubtraction);
        target.Attributes.Add(new BufferSubtractionSettings("missing-reference-id", BufferSubtractionMethod.ExponentialDecay).ToAttribute());
        var missing = BuildSupporting();
        Assert.Contains(Processing(missing).Items, item => item.Label == "Integrated heats"
            && item.Value.Contains("configured reference Missing reference experiment is unavailable (Exp. decay)", StringComparison.Ordinal));
        targetMetadata = missing.Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.Contains(targetMetadata.Items, item => item.Label == "Buffer subtraction"
            && item.Value == "Current configuration: Unavailable reference; Exp. decay. Stored corrected heats are used because the reference is unavailable.");
        options.ExtraTraceability = true;
        var traced = BuildSupporting();
        targetMetadata = traced.Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.Contains(targetMetadata.Items, item => item.Label == "Buffer subtraction"
            && item.Value.Contains("[missing-reference-id]", StringComparison.Ordinal));
    }

    [Fact]
    public void FixedFittedAndTransformedParametersHaveDistinctTypesAndUncertaintyFormatting()
    {
        var result = CreateResult(1);
        var member = result.Solution.Solutions.Single();
        member.Model.Parameters.Table[ParameterType.Nvalue1].Update(1.25, lockpar: true);
        member.Parameters[ParameterType.Nvalue1] = new FloatWithError(1.25, 0.2, 0.9, 1.6);
        member.Parameters[ParameterType.Affinity1] = new FloatWithError(6, 0.5, 5.5, 6.5);
        result.Model.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.ThermodynamicallyLinked);
        result.Model.Parameters.SetConstraintForParameter(ParameterType.Enthalpy1, VariableConstraint.SameForAll);
        result.Model.Parameters.AddorUpdateGlobalParameter(ParameterType.Enthalpy1, -25_000, islocked: false);
        result.Model.Parameters.AddorUpdateGlobalParameter(ParameterType.Gibbs1, -10_000, islocked: false);
        member.Parameters[ParameterType.Gibbs1] = new FloatWithError(-10_000);

        var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
        { UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval });
        var parameters = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportTableBlock>().Single(block => block.Title == "Fitted and derived parameters");

        var nValue = Assert.Single(parameters.Rows, row => row.Cells[0] == "N-value");
        Assert.Equal("Fixed", nValue.Cells[1]);
        Assert.DoesNotContain("±", nValue.Cells[2]);
        Assert.DoesNotContain("[", nValue.Cells[2]);
        var affinity = Assert.Single(parameters.Rows, row => row.Cells[0] == "Affinity");
        Assert.Equal("Derived", affinity.Cells[1]);
        Assert.True(affinity.Cells[2].Contains("±", StringComparison.Ordinal)
            || affinity.Cells[2].Contains("[", StringComparison.Ordinal));
        var gibbs = Assert.Single(parameters.Rows, row => row.Cells[0] == "Gibbs free energy");
        Assert.Equal("Derived", gibbs.Cells[1]);
        var enthalpy = Assert.Single(parameters.Rows, row => row.Cells[0] == "Enthalpy");
        Assert.Equal("Fitted", enthalpy.Cells[1]);
    }

    [Fact]
    public void AffinityStatusDistinguishesLocalGlobalAndThermodynamicallyTransformedCoordinates()
    {
        var result = CreateResult(1);
        var member = result.Solution.Solutions.Single();

        Assert.Equal("Fitted", AffinityStatus(result));
        member.Model.Parameters.Table[ParameterType.Affinity1].Update(6, lockpar: true);
        Assert.Equal("Fixed", AffinityStatus(result));
        member.Model.Parameters.Table[ParameterType.Affinity1].Unlock();

        result.Model.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.SameForAll);
        result.Model.Parameters.AddorUpdateGlobalParameter(ParameterType.Affinity1, 6, islocked: false);
        Assert.Equal("Fitted", AffinityStatus(result));
        result.Model.Parameters.GlobalTable[ParameterType.Affinity1].Update(6, lockpar: true);
        Assert.Equal("Fixed", AffinityStatus(result));

        result.Model.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.ThermodynamicallyLinked);
        result.Model.Parameters.AddorUpdateGlobalParameter(ParameterType.Gibbs1, -10_000, islocked: false);
        member.Parameters[ParameterType.Gibbs1] = new FloatWithError(-10_000);
        Assert.Equal("Derived", AffinityStatus(result));
    }

    static AnalysisResult CreateResult(int memberCount)
    {
        var models = Enumerable.Range(0, memberCount)
            .Select(_ => InjectionProcessingMethodTests.FittedModel())
            .ToList();
        var model = new GlobalModel(models)
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        foreach (var item in models) model.Parameters.AddIndivdualParameter(item.Parameters);
        var convergence = models[0].Solution.Convergence;
        var global = new GlobalSolution(new GlobalSolver { Model = model },
            models.Select(item => item.Solution).ToList(), convergence, reconstructBootstrap: false);
        model.Solution = global;
        var result = new AnalysisResult(global) { Name = "Report fixture", Date = new DateTime(2026, 8, 30) };
        result.SetID(Guid.NewGuid().ToString("N"));
        foreach (var item in models) item.Data.SetID(Guid.NewGuid().ToString("N"));
        return result;
    }

    static string AffinityStatus(AnalysisResult result)
    {
        var row = AnalysisReportBuilder.Build(result, new AnalysisReportOptions())
            .Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportTableBlock>()
            .Single(block => block.Title == "Fitted and derived parameters")
            .Rows.Single(item => item.Cells[0] == "Affinity");
        return row.Cells[1];
    }

    static MemoryStream RewriteReportEntry(Stream source, Func<byte[], byte[]> transform)
    {
        source.Position = 0;
        using var input = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var entries = input.Entries.ToDictionary(entry => entry.FullName,
            entry => { using var stream = entry.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray(); });
        entries["reports/000000/report.json"] = transform(entries["reports/000000/report.json"]);
        var manifest = JsonNode.Parse(entries["manifest.json"])!.AsObject();
        foreach (var item in manifest["entries"]!.AsArray())
        {
            var path = item!["path"]!.GetValue<string>();
            var bytes = entries[path];
            using var sha = SHA256.Create();
            item["length"] = bytes.LongLength;
            item["sha256"] = Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        }
        entries["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in entries)
            {
                using var stream = archive.CreateEntry(entry.Key).Open();
                stream.Write(entry.Value, 0, entry.Value.Length);
            }
        output.Position = 0;
        return output;
    }
}
