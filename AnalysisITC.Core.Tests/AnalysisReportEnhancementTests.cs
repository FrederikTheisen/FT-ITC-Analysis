using System;
using System.Collections.Generic;
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
            ReportId = "  QA-2026-α  ",
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
            Assert.Equal("QA-2026-α", presentation.GetProperty("reportId").GetString());
            var advanced = presentation.GetProperty("advancedSections").EnumerateArray().ToArray();
            Assert.Equal("correlation", advanced[0].GetProperty("kind").GetString());
            Assert.Equal(2, advanced[0].GetProperty("correlationMemberIndex").GetInt32());
            Assert.Equal("debye-huckel", advanced[1].GetProperty("kind").GetString());
            foreach (var transient in new[] { "author", "applicationVersion", "generatedAtUtc" })
                Assert.False(presentation.TryGetProperty(transient, out _));
        }

        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict)).Reports);
        Assert.True(restored.HasPresentationSettings);
        Assert.True(restored.PresentationSettingsEqual(options));
        Assert.Equal("", restored.PresentationSettings.Author);
        Assert.Equal("QA-2026-α", restored.PresentationSettings.ReportId);
        Assert.Equal("QA-2026-α", restored.CreateDetachedCopy().PresentationSettings.ReportId);
        var changedId = restored.PresentationSettings;
        changedId.ReportId = "QA-2026-β";
        Assert.False(restored.PresentationSettingsEqual(changedId));
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
            state["presentationSettings"]!.AsObject().Remove("reportId");
            return Encoding.UTF8.GetBytes(state.ToJsonString());
        });
        var restoredOlder = Assert.Single((await FTXTCReader.ReadWithRecovery(older, FtxtcReadPolicy.Strict)).Reports);
        Assert.False(restoredOlder.PresentationSettings.ExpandedExplanations);
        Assert.Equal("", restoredOlder.PresentationSettings.ReportId);
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

    [Theory]
    [InlineData("  QA-2026-α  ", "QA-2026-α")]
    [InlineData("", "Not recorded")]
    [InlineData("   ", "Not recorded")]
    public void SavedReportIdReplacesInternalIdentityInTraceabilityDetails(string enteredId, string displayedId)
    {
        var result = CreateResult(1);
        var report = new AnalysisReport { Name = "Traceability report" };
        var internalId = report.UniqueID;
        report.SetResultIds(new[] { result.UniqueID });
        report.UpdatePresentationSettings(new AnalysisReportOptions
        { ReportId = enteredId, ExtraTraceability = true });

        var document = AnalysisReportBuilder.Build(report, _ => result, _ => null);
        var signature = Assert.Single(document.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportSignOffBlock>());
        Assert.Equal(displayedId, signature.ReportId);
        Assert.NotEqual(internalId, signature.ReportId);
        var idItems = document.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items)
            .Where(item => item.Label == "Report identifier").ToList();
        Assert.NotEmpty(idItems);
        Assert.All(idItems, item => Assert.Equal(displayedId, item.Value));
        Assert.Equal(internalId, report.UniqueID);
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
            ReportId = "QA-2026-42",
        };

        var document = AnalysisReportBuilder.Build(report,
            id => id == first.UniqueID ? first : id == second.UniqueID ? second : null,
            _ => null, options);
        var cover = Assert.Single(document.Sections, section => section.Kind == AnalysisReportSectionKind.Cover);
        var signature = Assert.Single(cover.Blocks.OfType<AnalysisReportSignOffBlock>());
        Assert.Equal("QA-2026-42", signature.ReportId);
        Assert.NotEqual(report.UniqueID, signature.ReportId);
        Assert.Equal("Zoë 李", signature.PreparedBy);
        Assert.Equal(document.ExportDateText, signature.GeneratedAt);
        Assert.Equal("Zoë 李", document.Author);
        Assert.Equal(generated, document.GeneratedAtUtc);
        Assert.Empty(document.Sections.Where(section => section.Kind != AnalysisReportSectionKind.Cover)
            .SelectMany(section => section.Blocks).OfType<AnalysisReportSignOffBlock>());
        Assert.DoesNotContain(document.Sections.Where(section => section.Kind != AnalysisReportSectionKind.Cover)
            .SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items),
            item => item.Label == "Report prepared by" || item.Label == "Generated at");

        options.ExtraTraceability = false;
        var withoutTrace = AnalysisReportBuilder.Build(report,
            id => id == first.UniqueID ? first : second, _ => null, options);
        Assert.Empty(withoutTrace.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportSignOffBlock>());
        Assert.DoesNotContain(withoutTrace.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
            .SelectMany(block => block.Items), item => item.Label == "Report identifier" || item.Label == "Result identifiers"
                || item.Label == "Report prepared by" || item.Label == "Generated at" || item.Label == "Analysis operator");

        options.Author = "";
        options.ExtraTraceability = true;
        var noAuthor = AnalysisReportBuilder.Build(report,
            id => id == first.UniqueID ? first : second, _ => null, options);
        Assert.Equal("", noAuthor.Author);
        Assert.Equal("Not recorded", Assert.Single(noAuthor.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportSignOffBlock>()).PreparedBy);

        var single = AnalysisReportBuilder.Build(first, new AnalysisReportOptions
        { GeneratedAtUtc = generated, ApplicationVersion = "9.8-test", ExtraTraceability = true });
        var singleOverview = single.Sections.Single(section => section.Kind == AnalysisReportSectionKind.ResultOverview);
        var analysisAsOf = singleOverview.Blocks.OfType<AnalysisReportKeyValueBlock>()
            .SelectMany(block => block.Items).Single(item => item.Label == "Analysis date");
        Assert.Equal("30 Aug 2026", analysisAsOf.Value);
        var singlePreparation = Assert.Single(single.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Cover)
            .Blocks.OfType<AnalysisReportSignOffBlock>());
        Assert.StartsWith(generated.ToLocalTime().ToString("d MMM yyyy, HH:mm ", System.Globalization.CultureInfo.InvariantCulture),
            singlePreparation.GeneratedAt);
        var reportDetails = single.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Report details");
        Assert.DoesNotContain(reportDetails.Items, item => item.Label == "Generated at" || item.Label == "Report prepared by");

        var singleWithoutTrace = AnalysisReportBuilder.Build(first, new AnalysisReportOptions
        { GeneratedAtUtc = generated, ApplicationVersion = "9.8-test", ExtraTraceability = false });
        Assert.Empty(singleWithoutTrace.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportSignOffBlock>());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void SignOffStaysAtBottomOfFirstPageWithoutOverlappingCoverContent(int resultCount, bool overflowingComments)
    {
        var results = Enumerable.Range(1, resultCount).Select(index => CreateResult(index)).ToList();
        var document = AnalysisReportBuilder.Build(results, new AnalysisReportOptions
        {
            ExtraTraceability = true,
            Author = "Zoë 李 " + string.Join(" ", Enumerable.Repeat("Long preparer name", 8)),
            ReportId = "STUDY-" + new string('A', 160),
            DocumentLabel = "Report subtitle that introduces the study.",
        });
        if (overflowingComments)
            document.Sections[0].Add(new AnalysisReportTextBlock("Report comments",
                string.Join("\n", Enumerable.Repeat("A separate line of author comments.", 100)),
                AnalysisReportLayoutPolicy.AllowContinuation));

        var plan = AnalysisReportLayoutEngine.Paginate(document, new SignOffTextMeasurer());
        var coverPage = plan.Pages[0];
        var signOff = Assert.Single(coverPage.Fragments, fragment => fragment.Kind == AnalysisReportFragmentKind.SignOff);
        Assert.Equal(plan.PageHeight - plan.MarginBottom - 18, signOff.Bounds.Bottom, 6);
        Assert.True(signOff.Bounds.Y > plan.PageHeight * .65);
        Assert.All(coverPage.Fragments.Where(fragment => fragment != signOff),
            fragment => Assert.True(fragment.Bounds.Bottom <= signOff.Bounds.Y - 9 + 1e-6));
        Assert.DoesNotContain(plan.Pages.Skip(1).SelectMany(page => page.Fragments),
            fragment => fragment.Kind == AnalysisReportFragmentKind.SignOff);
        Assert.True(signOff.SignOffLayout.Fields.Single(field => field.Label == "Prepared by").Lines.Count > 1);
        Assert.True(signOff.SignOffLayout.Fields.Single(field => field.Label == "Report ID").Lines.Count > 1);
        Assert.All(signOff.SignOffLayout.Fields, field =>
            Assert.True(field.Bounds.Bottom < signOff.SignOffLayout.Signature.Y));
        if (overflowingComments)
            Assert.Equal(100, plan.Pages.SelectMany(page => page.Fragments)
                .Where(fragment => fragment.Block is AnalysisReportTextBlock text && text.Title == "Report comments")
                .Sum(fragment => fragment.Lines.Count));
    }

    [Fact]
    public void DisablingCoverSignatureOmitsTheEntirePreparationBlock()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(1), new AnalysisReportOptions
        { ExtraTraceability = true, IncludeCoverSignature = false });
        Assert.Empty(document.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportSignOffBlock>());
    }

    sealed class SignOffTextMeasurer : IAnalysisReportTextMeasurer
    {
        public AnalysisReportSize Measure(string text, AnalysisReportTextStyle style) =>
            new AnalysisReportSize((text ?? "").Length * style.FontSize * .5, style.FontSize);
    }

    [Fact]
    public void ReportAlwaysShowsPopulatedExternalIdentifiersAndTraceOnlyAnalysisOperator()
    {
        var result = CreateResult(1);
        var data = result.Solution.Solutions.Single().Data;
        data.ExternalExperimentId = "experiment / α-00017";
        data.CellSampleId = "cell-batch-0003";
        data.SyringeSampleId = "syringe-batch-0008";
        result.RestoreOperatorName("Operator A");

        var full = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
        { ExtraTraceability = false, CondenseRepeatedExperiments = false });
        var details = full.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.Contains(details.Items, item => item.Label == "External experiment ID" && item.Value == "experiment / α-00017");
        Assert.Contains(details.Items, item => item.Label == "Cell sample/batch ID" && item.Value == "cell-batch-0003");
        Assert.Contains(details.Items, item => item.Label == "Syringe sample/batch ID" && item.Value == "syringe-batch-0008");
        Assert.DoesNotContain(details.Items, item => item.Label == "Internal experiment identifier");
        var identifiers = details.Items.Select((item, index) => (item, index))
            .Where(entry => entry.item.Label is "External experiment ID" or "Cell sample/batch ID" or "Syringe sample/batch ID")
            .ToList();
        var settingsIndex = details.Items.ToList().FindIndex(item => item.Label == "Experiment settings");
        var instrumentIndex = details.Items.ToList().FindIndex(item => item.Label == "Instrument");
        Assert.Equal(3, identifiers.Count);
        Assert.All(identifiers, entry => Assert.True(entry.index < settingsIndex));
        Assert.True(settingsIndex < instrumentIndex);
        Assert.All(identifiers, entry => Assert.Equal(0, entry.item.IndentLevel));
        var analysis = full.Sections.Single(section => section.Kind == AnalysisReportSectionKind.ResultOverview)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Analysis");
        Assert.DoesNotContain(analysis.Items, item => item.Label == "Analysis operator");
        var traced = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
        { ExtraTraceability = true, CondenseRepeatedExperiments = false });
        var tracedAnalysis = traced.Sections.Single(section => section.Kind == AnalysisReportSectionKind.ResultOverview)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Analysis");
        Assert.Contains(tracedAnalysis.Items, item => item.Label == "Analysis operator" && item.Value == "Operator A");

        var repeated = CreateResult(1);
        var repeatedData = repeated.Solution.Solutions.Single().Data;
        repeatedData.SetID(data.UniqueID);
        repeatedData.ExternalExperimentId = data.ExternalExperimentId;
        repeatedData.CellSampleId = data.CellSampleId;
        repeatedData.SyringeSampleId = data.SyringeSampleId;
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID, repeated.UniqueID });
        var condensed = AnalysisReportBuilder.Build(report,
            id => id == result.UniqueID ? result : id == repeated.UniqueID ? repeated : null,
            _ => null, new AnalysisReportOptions { CondenseRepeatedExperiments = true, ExtraTraceability = true });
        var condensedDetails = condensed.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Experiment details — condensed");
        Assert.Contains(condensedDetails.Items, item => item.Label == "Cell sample/batch ID");
        Assert.DoesNotContain(condensed.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items),
            item => item.Label == "Internal experiment identifier" || item.Value == data.UniqueID);
    }

    [Fact]
    public void MissingIdentifiersAreNotRecordedOnlyInTraceabilityReports()
    {
        var result = CreateResult(1);
        var data = result.Solution.Solutions.Single().Data;
        data.ExternalExperimentId = "";
        data.CellSampleId = "cell-batch-0003";
        data.SyringeSampleId = "   ";
        var labels = new[] { "External experiment ID", "Cell sample/batch ID", "Syringe sample/batch ID" };

        IReadOnlyList<AnalysisReportKeyValueItem> Details(bool traceability) =>
            AnalysisReportBuilder.Build(result, new AnalysisReportOptions
                { ExtraTraceability = traceability, CondenseRepeatedExperiments = false })
                .Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
                .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details")
                .Items.Where(item => labels.Contains(item.Label)).ToList();

        Assert.Equal(new[] { ("Cell sample/batch ID", "cell-batch-0003") },
            Details(false).Select(item => (item.Label, item.Value)));
        Assert.Equal(new[]
            {
                ("External experiment ID", "Not recorded"),
                ("Cell sample/batch ID", "cell-batch-0003"),
                ("Syringe sample/batch ID", "Not recorded"),
            },
            Details(true).Select(item => (item.Label, item.Value)));
    }

    [Fact]
    public void TraceabilityPolicyAppliesToFullCondensedAndSupportingMetadataWithoutMutatingOptions()
    {
        AppSettings.TraceabilityModeEnabled = false;
        var result = CreateResult(1);
        var data = result.Solution.Solutions.Single().Data;
        var offOptions = new AnalysisReportOptions { ExtraTraceability = false, CondenseRepeatedExperiments = false };
        var off = AnalysisReportBuilder.Build(result, offOptions);
        var fullDetails = off.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.DoesNotContain(fullDetails.Items, item => item.Label == "Not recorded");
        Assert.False(offOptions.ExtraTraceability);

        var explicitlyEnabled = new AnalysisReportOptions { ExtraTraceability = true, CondenseRepeatedExperiments = false };
        var on = AnalysisReportBuilder.Build(result, explicitlyEnabled);
        var fullOn = on.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.DoesNotContain(fullOn.Items, item => item.Label == "Not recorded"
            && item.Value.Contains("experiment ID", StringComparison.OrdinalIgnoreCase));
        Assert.True(explicitlyEnabled.ExtraTraceability);

        var saved = new AnalysisReport { Name = "Saved traceability policy" };
        saved.SetResultIds(new[] { result.UniqueID });
        var supporting = new ExperimentData("supporting.itc");
        supporting.SetID("supporting-experiment");
        supporting.ExternalExperimentId = "support-id-β";
        saved.SetSupportingExperimentIds(new[] { supporting.UniqueID });
        saved.UpdatePresentationSettings(new AnalysisReportOptions { ExtraTraceability = false });
        AppSettings.UserName = "Current operator";
        AppSettings.TraceabilityModeEnabled = true;
        var fromSavedSettings = AnalysisReportBuilder.Build(saved, _ => result,
            id => id == supporting.UniqueID ? supporting : null);
        Assert.False(AnalysisReportBuilder.NeedsPreparerRefresh(fromSavedSettings));
        Assert.Equal("Current operator", fromSavedSettings.Author);
        Assert.DoesNotContain(fromSavedSettings.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items),
            item => item.Label == "Not recorded" && item.Value.Contains("experiment ID", StringComparison.OrdinalIgnoreCase));
        var supportingDetails = fromSavedSettings.Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.DoesNotContain(supportingDetails.Items, item => item.Label == "Not recorded"
            && item.Value.Contains("experiment ID", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(supportingDetails.Items, item => item.Label == "External experiment ID" && item.Value == "support-id-β");
        Assert.False(saved.PresentationSettings.ExtraTraceability);

        saved.UpdatePresentationSettings(new AnalysisReportOptions { ExtraTraceability = true });
        var savedTraceabilityEnabled = AnalysisReportBuilder.Build(saved, _ => result,
            id => id == supporting.UniqueID ? supporting : null);
        Assert.DoesNotContain(savedTraceabilityEnabled.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items),
            item => item.Label == "Not recorded" && item.Value.Contains("experiment ID", StringComparison.OrdinalIgnoreCase));
        Assert.True(saved.PresentationSettings.ExtraTraceability);

        var repeated = CreateResult(1);
        repeated.Solution.Solutions.Single().Data.SetID(data.UniqueID);
        var condensedReport = new AnalysisReport { Name = "Traceability condensed" };
        condensedReport.SetResultIds(new[] { result.UniqueID, repeated.UniqueID });
        condensedReport.UpdatePresentationSettings(new AnalysisReportOptions
        { CondenseRepeatedExperiments = true, ExtraTraceability = false });
        var condensedModeOn = AnalysisReportBuilder.Build(condensedReport,
            id => id == result.UniqueID ? result : repeated);
        var condensedDetails = condensedModeOn.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Experiment details — condensed");
        Assert.Contains(condensedDetails.Items, item => item.Label == "Experiment date" && item.Value == "Not recorded");
        Assert.DoesNotContain(condensedDetails.Items, item => item.Label == "Internal experiment identifier");
        Assert.False(condensedReport.PresentationSettings.ExtraTraceability);

        Assert.True(AnalysisReportBuilder.NeedsPreparerRefresh(
            AnalysisReportBuilder.Build(result, new AnalysisReportOptions { Author = "Previous operator" })));

        var modeOverride = new AnalysisReportOptions { ExtraTraceability = false, CondenseRepeatedExperiments = false };
        var globallyEnabled = AnalysisReportBuilder.Build(result, modeOverride);
        Assert.Contains(globallyEnabled.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items),
            item => item.Label == "Experiment date" && item.Value == "Not recorded");
        Assert.False(modeOverride.ExtraTraceability);
        Assert.True(AnalysisReportBuilder.NeedsPreparerRefresh(null));
        AppSettings.TraceabilityModeEnabled = false;
        var restoredOff = AnalysisReportBuilder.Build(result,
            new AnalysisReportOptions { ExtraTraceability = false, CondenseRepeatedExperiments = false });
        Assert.DoesNotContain(restoredOff.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items),
            item => item.Label == "Internal experiment identifier" || item.Label == "Not recorded");
    }

    [Fact]
    public async Task ReadPathsReturnsAcceptedRawExperimentsByReferenceAndExcludesProjects()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var rawPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "data_1.itc");
        var ftxtcPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "two-sites.ftxtc");
        var ftitcPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "one-set.ftitc");
        var read = await DataReader.ReadPathsAsync(new[] { rawPath, rawPath, ftxtcPath, ftitcPath });

        Assert.Equal(2, read.ImportedExperiments.Count);
        Assert.All(read.ImportedExperiments, experiment =>
            Assert.Contains(DataManager.SourceItems, item => ReferenceEquals(item, experiment)));
        Assert.DoesNotContain(read.ImportedExperiments, experiment =>
            experiment.DataSourceFormat == ITCDataFormat.FTXTC);
        Assert.Equal(4, read.LoadedPathCount);
    }

    [Fact]
    public void DataReadResultSnapshotsImportedReferencesAndDefaultsToEmpty()
    {
        var first = new ExperimentData("first.itc");
        var second = new ExperimentData("second.itc");
        var source = new List<ExperimentData> { first };
        var read = new DataReadResult(1, new[] { "first.itc" }, 0, 1, false, source);
        source.Add(second);

        Assert.Single(read.ImportedExperiments);
        Assert.Same(first, read.ImportedExperiments[0]);
        Assert.True(((IList<ExperimentData>)read.ImportedExperiments).IsReadOnly);
        var legacyCall = new DataReadResult(0, Array.Empty<string>(), 0, 0, false);
        Assert.Empty(legacyCall.ImportedExperiments);
    }

    [Fact]
    public async Task ReadPathsKeepsEarlierAcceptedExperimentsWhenLaterFileFails()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var rawPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "data_1.itc");
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.itc");
        var read = await DataReader.ReadPathsAsync(new[] { rawPath, missingPath });

        var experiment = Assert.Single(read.ImportedExperiments);
        Assert.Contains(DataManager.SourceItems, item => ReferenceEquals(item, experiment));
        Assert.Single(read.LoadedPaths);
    }

    [Theory]
    [InlineData(ExperimentDateSource.DataFile, false, "Experiment date", "8 Sep 2023 (data file)")]
    [InlineData(ExperimentDateSource.UserModified, false, "Experiment date", "8 Sep 2023 (user provided)")]
    [InlineData(ExperimentDateSource.FileSystem, false, null, null)]
    [InlineData(ExperimentDateSource.Unknown, false, null, null)]
    [InlineData(ExperimentDateSource.DataFile, true, "Experiment date", "8 Sep 2023 (data file)")]
    [InlineData(ExperimentDateSource.UserModified, true, "Experiment date", "8 Sep 2023 (user provided)")]
    [InlineData(ExperimentDateSource.FileSystem, true, "Experiment date", "8 Sep 2023 (file system date)")]
    [InlineData(ExperimentDateSource.Unknown, true, "Experiment date", "Not recorded")]
    public void ExperimentDateTextExplainsItsProvenance(ExperimentDateSource source, bool traceability,
        string expectedLabel, string expectedValue)
    {
        AppSettings.TraceabilityModeEnabled = false;
        var result = CreateResult(1);
        var data = result.Solution.Solutions.Single().Data;
        data.Date = new DateTime(2023, 9, 8);
        data.DateSource = source;
        var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
        { CondenseRepeatedExperiments = false, ExtraTraceability = traceability });
        var details = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");

        var dates = details.Items.Where(item => item.Label.StartsWith("Experiment date", StringComparison.Ordinal)).ToList();
        if (expectedLabel == null)
        {
            Assert.Empty(dates);
            Assert.DoesNotContain(details.Items, item => item.Value.Contains("8 Sep 2023", StringComparison.Ordinal));
            return;
        }
        var date = Assert.Single(dates);
        Assert.Same(details.Items[0], date);
        Assert.Equal(expectedLabel, date.Label);
        Assert.Equal(expectedValue, date.Value);
    }

    [Theory]
    [InlineData(ExperimentDateSource.DataFile, " (data file)")]
    [InlineData(ExperimentDateSource.UserModified, " (user provided)")]
    public void CondensedAndSupportingExperimentDetailsRetainDateProvenance(
        ExperimentDateSource source, string expectedSuffix)
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
        Assert.Equal("Experiment date", metadata.Items[0].Label);
        Assert.EndsWith(expectedSuffix, metadata.Items[0].Value);

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
        Assert.Equal("Experiment date", supportingMetadata.Items[0].Label);
        Assert.EndsWith(expectedSuffix, supportingMetadata.Items[0].Value);
    }

    [Theory]
    [InlineData("run.itc", ITCDataFormat.ITC200, "run.itc (MicroCal ITC Data File)")]
    [InlineData("run.ftitc", ITCDataFormat.FTITC, "run.ftitc (FT-ITC)")]
    [InlineData("run.ftxtc", ITCDataFormat.FTXTC, "run.ftxtc (FT-ITC Project)")]
    [InlineData("run.ta", ITCDataFormat.TAITC, "run.ta (TA Instruments Nano Analyze)")]
    [InlineData("run.dh", ITCDataFormat.IntegratedHeats, "run.dh (Integrated Heats File)")]
    [InlineData("run.apj", ITCDataFormat.PEAQITCProject, "run.apj (PEAQ-ITC Project File)")]
    [InlineData("run.opj", ITCDataFormat.OriginProject, "run.opj (Origin ITC Project File)")]
    [InlineData("run.nitc", ITCDataFormat.NanoITC, "run.nitc (TA Instruments NanoITC Data File)")]
    [InlineData("run.itc", ITCDataFormat.Unknown, "run.itc (Unknown format)")]
    [InlineData("run.itc", (ITCDataFormat)12345, "run.itc (Unknown format)")]
    [InlineData("run.itc", (ITCDataFormat)1, "run.itc (Unknown format)")]
    [InlineData("run.ftxtc", ITCDataFormat.ITC200, "run.ftxtc (MicroCal ITC Data File)")]
    [InlineData(null, ITCDataFormat.ITC200, "Unavailable (MicroCal ITC Data File)")]
    [InlineData("", ITCDataFormat.Unknown, "Unavailable (Unknown format)")]
    [InlineData("  ", (ITCDataFormat)12345, "Unavailable (Unknown format)")]
    public void SourceFileIncludesRecordedFormatInFullCondensedAndSupportingDetails(
        string fileName, ITCDataFormat format, string expected)
    {
        var first = CreateResult(1);
        var repeated = CreateResult(1);
        var firstData = first.Solution.Solutions.Single().Data;
        var repeatedData = repeated.Solution.Solutions.Single().Data;
        repeatedData.SetID(firstData.UniqueID);
        var supporting = CreateResult(1).Solution.Solutions.Single().Data;
        foreach (var data in new[] { firstData, repeatedData, supporting })
        {
            data.SetFileName(fileName);
            data.DataSourceFormat = format;
        }
        var report = new AnalysisReport();
        report.SetResultIds(new[] { first.UniqueID, repeated.UniqueID });
        report.SetSupportingExperimentIds(new[] { supporting.UniqueID });
        var document = AnalysisReportBuilder.Build(report,
            id => id == first.UniqueID ? first : id == repeated.UniqueID ? repeated : null,
            id => id == supporting.UniqueID ? supporting : null,
            new AnalysisReportOptions { CondenseRepeatedExperiments = true });
        var details = document.Sections
            .Where(section => section.Kind == AnalysisReportSectionKind.Experiment
                || section.Kind == AnalysisReportSectionKind.SupportingData)
            .SelectMany(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>())
            .Where(block => block.Title.StartsWith("Experiment details", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, details.Count);
        Assert.Single(details, block => block.Title == "Experiment details — condensed");
        Assert.All(details, block =>
        {
            Assert.Equal(expected, Assert.Single(block.Items, item => item.Label == "Source file").Value);
            Assert.DoesNotContain(block.Items, item => item.Label == "Source format");
        });
        var sources = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Appendix)
            .Blocks.OfType<AnalysisReportTableBlock>().Single(block => block.Title == "Experiment sources");
        var fileColumn = sources.Columns.ToList().FindIndex(column => column.Id == "File");
        Assert.Equal(2, sources.Rows.Count);
        Assert.All(sources.Rows, row => Assert.Equal(fileName ?? "", row.Cells[fileColumn]));
        Assert.All(new[] { firstData, repeatedData, supporting }, data =>
        {
            Assert.Equal(fileName, data.FileName);
            Assert.Equal(format, data.DataSourceFormat);
        });
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
            id => id == target.UniqueID ? target : id == reference.UniqueID ? reference : null,
            options);
        AnalysisReportKeyValueBlock Processing(AnalysisReportDocument document) => document
            .Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Notes");

        var withoutSubtraction = BuildSupporting();
        var withoutSubtractionProcessing = withoutSubtraction.Sections
            .Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>()
            .FirstOrDefault(block => block.Title == "Notes");
        Assert.Null(withoutSubtractionProcessing);
        var targetMetadata = withoutSubtraction.Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.DoesNotContain(targetMetadata.Items, item => item.Label == "Buffer subtraction");

        target.SetBufferSubtraction(reference, BufferSubtractionMethod.Linear);
        var withSubtraction = BuildSupporting();
        Assert.Contains(Processing(withSubtraction).Items,
            item => item.Label == "Integrated heats" && item.Value == "Stored corrected heats; configured reference Buffer blank (Linear)");
        targetMetadata = withSubtraction.Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.Contains(targetMetadata.Items, item => item.IndentLevel == 1 && item.Label == "Buffer subtraction"
            && item.Value == "Buffer blank (Linear)");
        report.SetSupportingExperimentIds(new[] { target.UniqueID, reference.UniqueID });
        var withReference = BuildSupporting();
        var referencedMetadata = withReference.Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().First(block => block.Title == "Experiment details");
        Assert.Contains(referencedMetadata.Items, item => item.Label == "Buffer subtraction"
            && item.Value == "Buffer blank (Experiment S2; Linear)");
        report.SetSupportingExperimentIds(new[] { target.UniqueID });

        target.Attributes.RemoveAll(item => item.Key == AttributeKey.BufferSubtraction);
        target.Attributes.Add(new BufferSubtractionSettings("missing-reference-id", BufferSubtractionMethod.ExponentialDecay).ToAttribute());
        var missing = BuildSupporting();
        Assert.Contains(Processing(missing).Items, item => item.Label == "Integrated heats"
            && item.Value.Contains("configured reference Missing reference experiment is unavailable (Exp. decay)", StringComparison.Ordinal));
        targetMetadata = missing.Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.Contains(targetMetadata.Items, item => item.Label == "Buffer subtraction"
            && item.Value == "Missing reference experiment (Exp. decay)");
        options.ExtraTraceability = true;
        var traced = BuildSupporting();
        targetMetadata = traced.Sections.Single(section => section.Kind == AnalysisReportSectionKind.SupportingData)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.Contains(targetMetadata.Items, item => item.Label == "Buffer subtraction");
    }

    [Fact]
    public void CompetitorPropertiesReportFitTimeValuesUnitsAndSourceAttribution()
    {
        var source = CreateResult(1);
        source.Name = "One-set-of-sites";
        DataManager.AddData(source);
        var result = CreateResult(1);
        var data = result.Solution.Solutions.Single().Data;
        var attribute = ExperimentAttribute.CompetitorResultReference(source.UniqueID);
        attribute.CapturedAffinity = new FloatWithError(25e-9);
        attribute.CapturedEnthalpy = new FloatWithError(-35_000);
        data.Attributes.Add(attribute);
        result.SetValiditySnapshot(AnalysisResultValiditySnapshot.Capture(result.Solution));
        result.ValiditySnapshot.Experiments.Single().Attributes.Add(ExperimentAttributeSnapshot.Capture(attribute));

        // A later edit must not replace the values used by the saved fit in its report.
        attribute.CapturedAffinity = new FloatWithError(40e-9);
        attribute.CapturedEnthalpy = new FloatWithError(-20_000);

        var details = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
            { EnergyUnitFamily = EnergyUnitFamily.Joules })
            .Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        var competitor = Assert.Single(details.Items, item => item.Label == "Competitor properties");

        Assert.Contains("Kd = ", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("25", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("nM", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("ΔH = ", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("-35", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("kJ/mol", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("from result \"One-set-of-sites\"", competitor.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("40", competitor.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("20", competitor.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void CompetitorPropertiesKeepCapturedValuesWhenSourceIsUnavailable()
    {
        var result = CreateResult(1);
        var attribute = ExperimentAttribute.CompetitorResultReference("deleted-source-result");
        attribute.CapturedAffinity = new FloatWithError(12e-9);
        attribute.CapturedEnthalpy = new FloatWithError(0);
        result.Solution.Solutions.Single().Data.Attributes.Add(attribute);

        var details = AnalysisReportBuilder.Build(result)
            .Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        var competitor = Assert.Single(details.Items, item => item.Label == "Competitor properties");

        Assert.Contains("12", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("nM", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("ΔH = 0", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("source result unavailable", competitor.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void CondensedExperimentDetailsUseTheSameCompetitorPropertiesFormatting()
    {
        var source = CreateResult(1);
        source.Name = "Competitor source";
        DataManager.AddData(source);
        var first = CreateResult(1);
        var repeated = CreateResult(1);
        var firstData = first.Solution.Solutions.Single().Data;
        var repeatedData = repeated.Solution.Solutions.Single().Data;
        repeatedData.SetID(firstData.UniqueID);
        var attribute = ExperimentAttribute.CompetitorResultReference(source.UniqueID);
        attribute.CapturedAffinity = new FloatWithError(8e-9);
        attribute.CapturedEnthalpy = new FloatWithError(-12_000);
        repeatedData.Attributes.Add(attribute);
        var report = new AnalysisReport();
        report.SetResultIds(new[] { first.UniqueID, repeated.UniqueID });

        var document = AnalysisReportBuilder.Build(report,
            id => id == first.UniqueID ? first : id == repeated.UniqueID ? repeated : null,
            _ => null, new AnalysisReportOptions { CondenseRepeatedExperiments = true });
        var condensed = document.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Experiment details — condensed");
        var competitor = Assert.Single(condensed.Items, item => item.Label == "Competitor properties");

        Assert.Contains("Kd = ", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("8", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("nM", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("ΔH = ", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("-12", competitor.Value, StringComparison.Ordinal);
        Assert.Contains("from result \"Competitor source\"", competitor.Value, StringComparison.Ordinal);
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
