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
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class ProjectInformationTests
{
    [Fact]
    public void ExperimentRowsShowConditionsAndActualIntegrationProgress()
    {
        var data = new ExperimentData("conditions.itc")
        {
            MeasuredTemperature = 25,
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
        };
        for (var i = 0; i < 3; i++) data.Injections.Add(new InjectionData(data, i, 2e-6, 2e-10, true));
        data.Injections[0].SetPeakArea(new FloatWithError(-2e-6));
        var text = ProjectInformationPresentation.ExperimentDetail(data);
        Assert.Contains("3 injections", text);
        Assert.Contains("25 °C · Cell: 10 µM · Syringe: 100 µM", text);
        Assert.Contains("1/3 integrated · Fit: Not fitted", text);
        data.Injections[1].SetPeakArea(new FloatWithError(-3e-6));
        Assert.Contains("2/3 integrated", ProjectInformationPresentation.ExperimentDetail(data));
    }

    [Fact]
    public void ResultRowsUseSavedMembersAndPreserveManualAssessment()
    {
        var result = Result("Saved result", SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        { Termination = SolverTermination.Converged }));
        var member = result.Solution.Solutions.Single();
        member.Data.Name = "Saved experiment";
        var text = ProjectInformationPresentation.ResultDetail(result);
        Assert.Contains("1 experiment", text);
        Assert.Contains(member.Temp.ToString("G4") + " °C", text);
        Assert.Contains("Experiments: Saved experiment", text);
        Assert.Contains("Not assessed", text);
        Assert.Contains("Fit: Converged", text);
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        Assert.Contains("No binding detected (manual)", ProjectInformationPresentation.ResultDetail(result));
        Assert.Equal(3, text.Split('\n').Length);
    }

    [Fact]
    public void ResultRowsKeepStoppedFitsSeparateFromBindingAssessment()
    {
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        { Termination = SolverTermination.IterationLimit });
        var result = Result("Stopped result", convergence);
        var text = ProjectInformationPresentation.ResultDetail(result);
        Assert.Contains("Fit: Stopped: iteration limit", text);
        Assert.Contains("Not assessed", text);
    }

    [Fact]
    public void ResultRowsShowSavedTemperatureRangeAndMixedMemberAssessments()
    {
        var result = Result("Temperature series");
        var first = result.Solution.Solutions[0];
        first.Data.MeasuredTemperature = 25;
        first.Data.Name = "First experiment";
        var second = InjectionProcessingMethodTests.FittedModel();
        second.Data.SetID("second-experiment");
        second.Data.MeasuredTemperature = 30;
        second.Data.Name = "Second experiment";
        result.Model.AddModel(second);
        result.Model.Parameters.AddIndivdualParameter(second.Parameters);
        result.RestoreMemberAssessment(second.Solution.Guid,
            BindingAssessmentState.FromComparison(null).WithOverride(BindingAssessmentOutcome.NoBindingDetected));
        var text = ProjectInformationPresentation.ResultDetail(result);
        Assert.Contains("2 experiments", text);
        Assert.Contains("25–30 °C", text);
        Assert.Contains("Experiments: First experiment, Second experiment", text);
        Assert.Contains("Mixed assessments (includes manual)", text);
        second.Data.MeasuredTemperature = double.PositiveInfinity;
        Assert.Contains("Temperature unavailable", ProjectInformationPresentation.ResultDetail(result));
    }

    [Fact]
    public void AutomaticNamesFollowContentWhileExistingNamesRemainUnchanged()
    {
        var first = Result("OneSetOfSites (3)");
        var second = Result("Other result");
        var report = Report(first);
        Assert.Equal("", report.Name);
        Assert.Equal(report.Date, report.LastEditedUtc);
        Assert.Equal("OneSetOfSites (3) report", AnalysisReportPresentation.DisplayName(report, id => first));
        Assert.Equal("OneSetOfSites (3)", AnalysisReportPresentation.AutomaticTitle(new[] { first }));
        var edited = report.LastEditedUtc;
        first.Name = "Renamed result";
        Assert.Equal("Renamed result report", AnalysisReportPresentation.DisplayName(report, id => first));
        Assert.Equal(edited, report.LastEditedUtc);
        report.SetResultIds(new[] { first.UniqueID, second.UniqueID });
        Assert.Equal("Analysis report", AnalysisReportPresentation.DisplayName(report, id => first));
        report.Name = "Analysis report";
        report.SetResultIds(new[] { first.UniqueID });
        Assert.Equal("Analysis report", AnalysisReportPresentation.DisplayName(report, id => first));
        report.Name = "Custom report";
        Assert.Equal("Custom report", AnalysisReportPresentation.DisplayName(report, id => first));
        report.Name = "";
        Assert.Equal("Renamed result report", AnalysisReportPresentation.DisplayName(report, id => first));
    }

    [Fact]
    public void ResultSetMatchingUsesNewestCreationAndLastTieWithoutRemovingVariants()
    {
        var older = new AnalysisReport(); older.SetResultIds(new[] { "a", "b" }); older.SetDate(new DateTime(2026, 1, 1));
        var newest = new AnalysisReport(); newest.SetResultIds(new[] { "b", "a" }); newest.SetDate(new DateTime(2026, 2, 1));
        var tie = newest.CreateDetachedCopy(); tie.SetID("tie");
        var extra = new AnalysisReport(); extra.SetResultIds(new[] { "a", "b", "missing" });
        var reports = new[] { newest, older, tie, extra };
        var edited = tie.LastEditedUtc;
        Assert.Same(tie, AnalysisReportPresentation.FindMatchingReport(reports, new[] { "a", "b" }));
        Assert.True(AnalysisReportPresentation.MatchesResultIds(older, new[] { "b", "a" }));
        Assert.False(AnalysisReportPresentation.MatchesResultIds(extra, new[] { "a", "b" }));
        tie.SetSupportingExperimentIds(new[] { "support" });
        Assert.Same(tie, AnalysisReportPresentation.FindMatchingReport(reports, new[] { "b", "a" }));
        Assert.Equal(4, reports.Length);
        Assert.Equal(new[] { "b", "a" }, tie.ResultIds);
        Assert.True(tie.LastEditedUtc > edited);
    }

    [Fact]
    public void CreationInstantsAreComparedAcrossLocalAndUtcWithoutChangingSavedDates()
    {
        var instant = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var legacy = new AnalysisReport(); legacy.SetResultIds(new[] { "a" });
        var modern = new AnalysisReport(); modern.SetResultIds(new[] { "a" });
        var legacyDate = instant.ToLocalTime();
        legacy.SetDate(legacyDate);
        var laterUtc = instant.AddMinutes(30);
        modern.SetDate(laterUtc);
        Assert.Same(modern, AnalysisReportPresentation.FindMatchingReport(new[] { modern, legacy }, new[] { "a" }));
        Assert.Equal(legacyDate, legacy.Date);
        Assert.Equal(DateTimeKind.Local, legacy.Date.Kind);
        Assert.Equal(laterUtc, modern.Date);
        Assert.Equal(DateTimeKind.Utc, modern.Date.Kind);

        // Earlier versions also supplied dates without a Kind. These retain the
        // historical local-clock meaning rather than becoming UTC clock values.
        legacy.SetDate(DateTime.SpecifyKind(legacyDate, DateTimeKind.Unspecified));
        Assert.Same(modern, AnalysisReportPresentation.FindMatchingReport(new[] { modern, legacy }, new[] { "a" }));
        Assert.Equal(DateTimeKind.Unspecified, legacy.Date.Kind);

        // Identical instants still use the last matching collection entry.
        modern.SetDate(instant);
        Assert.Same(legacy, AnalysisReportPresentation.FindMatchingReport(new[] { modern, legacy }, new[] { "a" }));
        Assert.Same(modern, AnalysisReportPresentation.FindMatchingReport(new[] { legacy, modern }, new[] { "a" }));
    }

    [Fact]
    public void DetailsAndCopyExposeSavedTextEvenWhenResultsAreMissing()
    {
        var result = Result("Present result");
        var report = Report(result);
        report.SetResultIds(new[] { result.UniqueID, "missing" });
        report.UpdatePresentationSettings(new AnalysisReportOptions
        { Title = "Study title", AutomaticTitle = false, DocumentLabel = "Study label", ReportId = "QA-32" });
        report.AuthorComments = "Comments to retain";
        report.UpdateStudyContext(new AnalysisStudyContext
        {
            ScientificQuestion = "Question to retain",
            AdditionalNotes = "Notes to retain",
            Experiments = new List<AnalysisExperimentContext> { new() { Purpose = "Control purpose", Annotation = "Annotation" } },
            References = new List<AnalysisUserReference> { new() { Label = "Source label", CitationOrUrl = "Citation", Notes = "Reference notes" } },
        });
        report.SetManualInterpretation("Approved text to retain");
        AnalysisResult Resolve(string id) => id == result.UniqueID ? result : null;
        Assert.Equal(1, AnalysisReportPresentation.MissingResultCount(report, Resolve));
        Assert.Equal("1 of 2 results missing", AnalysisReportPresentation.MissingResultStatus(report, Resolve));
        var details = ProjectInformationPresentation.DetailsText(report, Resolve);
        var copy = ProjectInformationPresentation.CopyText(report);
        foreach (var text in new[] { "Study title", "Study label", "QA-32", "Comments to retain", "Question to retain",
            "Notes to retain", "Control purpose", "Annotation", "Source label", "Citation", "Reference notes", "Approved text to retain" })
        {
            Assert.Contains(text, details);
            Assert.Contains(text, copy);
        }
        Assert.Contains("Present result", details);
        Assert.Contains("Missing result", details);
        Assert.DoesNotContain("missing", copy);
        Assert.DoesNotContain(report.UniqueID, copy);
        Assert.DoesNotContain("Last edited", copy);
        var automatic = report.PresentationSettings;
        automatic.AutomaticTitle = true;
        automatic.Title = "Derived title from a result name";
        report.UpdatePresentationSettings(automatic);
        Assert.Contains("Derived title from a result name", ProjectInformationPresentation.DetailsText(report, Resolve));
        Assert.DoesNotContain("Derived title from a result name", ProjectInformationPresentation.CopyText(report));
    }

    [Fact]
    public void ReportOverviewSummarizesSavedContentWithoutDuplicatingReportDetails()
    {
        var first = Result("First fit");
        var second = Result("Second fit");
        var firstExperimentId = first.Solution.Solutions[0].Data.UniqueID;
        second.Solution.Solutions[0].Data.SetID(firstExperimentId);
        var report = Report(second, first);
        report.SetSupportingExperimentIds(new[] { firstExperimentId, "support" });
        report.Name = "Full details report name";
        report.AuthorComments = "Full details author comments";
        report.UpdateStudyContext(new AnalysisStudyContext { ScientificQuestion = "Full details context" });
        report.UpdatePresentationSettings(new AnalysisReportOptions
        { Title = "Full details report title", AutomaticTitle = false, DocumentLabel = "Saved report subtitle" });
        report.SetManualInterpretation("Full details approved interpretation");
        AnalysisResult Resolve(string id) => id == first.UniqueID ? first : second;

        var rows = ProjectInformationPresentation.OverviewRows(report, Resolve);
        Assert.Equal(new[] { "Analysis Results", "Experiments", "Status", "Saved interpretation", "Subtitle" },
            rows.Select(row => row.Label));
        Assert.Equal("Second fit" + Environment.NewLine + "First fit", rows[0].Value);
        Assert.Equal("1 fitted · 2 supporting selected", rows[1].Value);
        Assert.Equal("Results available", rows[2].Value);
        Assert.Equal("Present", rows[3].Value);
        Assert.Equal("Saved report subtitle", rows[4].Value);
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Tooltip)));
        foreach (var fullDetails in new[] { "Full details report name", "Full details author comments",
            "Full details context", "Full details report title", "Full details approved interpretation" })
            Assert.DoesNotContain(rows, row => row.Value.Contains(fullDetails, StringComparison.Ordinal));
    }

    [Fact]
    public void ReportOverviewKeepsMissingCoverageAndInterpretationPresenceSeparateAndReadOnly()
    {
        var result = Result("Available fit");
        var report = Report(result);
        report.SetResultIds(new[] { "missing", result.UniqueID });
        report.SetSupportingExperimentIds(new[] { "unresolved-support" });
        report.ApproveInterpretation(new AnalysisInterpretationRecord
        { InterpretationMarkdown = "## Overall interpretation\nSaved interpretation.", InputFingerprint = "unknown-evidence" });
        report.SetInterpretationFreshness(new AnalysisInterpretationFreshnessResult
        { Status = AnalysisInterpretationFreshness.Stale, Reason = "Existing freshness state" });
        report.MarkClean();
        var timestamp = report.LastEditedUtc;
        AnalysisResult Resolve(string id) => id == result.UniqueID ? result : null;

        var rows = ProjectInformationPresentation.OverviewRows(report, Resolve);
        Assert.Equal("Missing result" + Environment.NewLine + "Available fit", rows[0].Value);
        Assert.Equal("At least 1 fitted · 1 supporting selected", rows[1].Value);
        Assert.Equal("1 of 2 results missing", rows[2].Value);
        Assert.Equal("Present", rows[3].Value);
        Assert.DoesNotContain(rows, row => row.Label == "Subtitle");
        Assert.Equal(timestamp, report.LastEditedUtc);
        Assert.False(report.IsModified);
        Assert.Equal(AnalysisInterpretationFreshness.Stale, report.InterpretationFreshness);
        Assert.Equal("Existing freshness state", report.InterpretationFreshnessReason);
        Assert.Equal(new[] { "missing", result.UniqueID }, report.ResultIds);
        Assert.Equal(new[] { "unresolved-support" }, report.SupportingExperimentIds);
    }

    [Fact]
    public void ReportOverviewDoesNotInventFittedCountsForMissingResultsOrMissingMemberEvidence()
    {
        var report = new AnalysisReport();
        report.SetResultIds(new[] { "missing" });
        var missingRows = ProjectInformationPresentation.OverviewRows(report, id => null);
        Assert.Equal("Missing result", missingRows[0].Value);
        Assert.Equal("Fitted count unavailable · 0 supporting selected", missingRows[1].Value);
        Assert.Equal("1 of 1 result missing", missingRows[2].Value);
        Assert.Equal("None saved", missingRows[3].Value);

        var result = Result("Fit without available member data");
        report.SetResultIds(new[] { result.UniqueID });
        result.Model.Models[0].BindImportedExperiment(null);
        var missingDataRows = ProjectInformationPresentation.OverviewRows(report, id => result);
        Assert.Equal("Fitted count unavailable · 0 supporting selected", missingDataRows[1].Value);
        Assert.Equal("Results available", missingDataRows[2].Value);
    }

    [Fact]
    public void EmptyReportOverviewOmitsBlankSubtitleAndBlankInterpretation()
    {
        var report = new AnalysisReport();
        report.Restore(Array.Empty<string>(), Array.Empty<string>(), new AnalysisStudyContext(),
            AnalysisInterpretationOptions.Default(), new AnalysisInterpretationRecord { InterpretationMarkdown = " \r\n " },
            new AnalysisReportOptions { DocumentLabel = " \r\n " });
        var rows = ProjectInformationPresentation.OverviewRows(report, id => null);
        Assert.Equal("None selected", rows[0].Value);
        Assert.Equal("0 fitted · 0 supporting selected", rows[1].Value);
        Assert.Equal("None selected", rows[2].Value);
        Assert.Equal("None saved", rows[3].Value);
        Assert.DoesNotContain(rows, row => row.Label == "Subtitle");
        Assert.Null(report.LastEditedUtc);
    }

    [Fact]
    public void ActualStoredChangesAdvanceTimestampButEqualValuesDoNot()
    {
        var report = new AnalysisReport();
        report.RestoreLastEditedUtc(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        report.MarkClean();
        var original = report.LastEditedUtc;
        report.Name = "";
        report.AuthorComments = null;
        report.UpdateStudyContext(new AnalysisStudyContext());
        report.UpdateInterpretationSettings(AnalysisInterpretationOptions.Default());
        report.ClearApprovedInterpretation();
        Assert.Equal(original, report.LastEditedUtc);
        Assert.False(report.IsModified);

        var changes = new Action[]
        {
            () => report.Name = "Named",
            () => report.SetSupportingExperimentIds(new[] { "support" }),
            () => report.AuthorComments = "Comment",
            () => report.UpdateStudyContext(new AnalysisStudyContext { ScientificQuestion = "Question", References = new() { new() { Notes = "Reference" } } }),
            () => report.UpdateInterpretationSettings(new AnalysisInterpretationOptions { IncludeProcessingInformation = true }),
            () => report.UpdatePresentationSettings(new AnalysisReportOptions { DocumentLabel = "Label" }),
            () => report.SetManualInterpretation("Saved interpretation"),
            () => report.ClearApprovedInterpretation(),
        };
        foreach (var change in changes)
        {
            var before = report.LastEditedUtc;
            change();
            Assert.True(report.LastEditedUtc > before);
            var after = report.LastEditedUtc;
            change();
            Assert.Equal(after, report.LastEditedUtc);
        }
        var automatic = report.PresentationSettings;
        automatic.Title = "Renamed automatic result title";
        report.UpdatePresentationSettings(automatic);
        var latest = report.LastEditedUtc;
        automatic.Title = "Another result name";
        report.UpdatePresentationSettings(automatic);
        Assert.Equal(latest, report.LastEditedUtc);
    }

    [Fact]
    public void RestoreDetachedCopiesAndFreshnessEvaluationDoNotAdvanceTimestamp()
    {
        var result = Result("Original");
        var report = Report(result);
        var timestamp = new DateTime(2025, 1, 1, 2, 3, 4, DateTimeKind.Utc);
        report.RestoreLastEditedUtc(timestamp);
        using (DocumentDirtyTracker.RestoreDocument())
        {
            report.Name = "Restored name";
            report.AuthorComments = "Restored comments";
            report.UpdateStudyContext(new AnalysisStudyContext { ScientificQuestion = "Restored context" });
        }
        Assert.Equal(timestamp, report.LastEditedUtc);
        var copy = report.CreateDetachedCopy();
        Assert.Equal(timestamp, copy.LastEditedUtc);
        Assert.False(copy.IsModified);
        AnalysisReportBuilder.Build(report, id => result);
        AnalysisInterpretationService.EvaluateFreshness(report, id => result, id => null);
        Assert.Equal(timestamp, report.LastEditedUtc);
        copy.Name = "Changed copy";
        Assert.Equal("Restored name", report.Name);
        Assert.Equal(timestamp, report.LastEditedUtc);
    }

    [Fact]
    public void PdfTitlesAreSeparateFromNamesAndProjectOrderDoesNotRewriteSavedIds()
    {
        var first = Result("Alpha");
        var second = Result("Beta");
        var report = Report(first);
        report.Name = "Private project name";
        AnalysisResult Resolve(string id) => id == first.UniqueID ? first : second;
        Assert.Equal("Alpha", AnalysisReportBuilder.Build(report, Resolve).Title);
        Assert.Equal("Explicit title", AnalysisReportBuilder.Build(report, Resolve, new AnalysisReportOptions
        { Title = "Explicit title", AutomaticTitle = false }).Title);
        Assert.Equal("Alpha", AnalysisReportBuilder.Build(report, Resolve, new AnalysisReportOptions
        { Title = "", AutomaticTitle = false }).Title);
        report.SetResultIds(new[] { first.UniqueID, second.UniqueID });
        var timestamp = report.LastEditedUtc;
        var document = AnalysisReportBuilder.Build(report, Resolve, new AnalysisReportOptions
        { ResultOrderIds = new[] { second.UniqueID, first.UniqueID } });
        Assert.Equal("Analysis report", document.Title);
        Assert.Contains("Beta", document.Sections.First(section => section.Kind == AnalysisReportSectionKind.ResultOverview).Title);
        Assert.Equal(new[] { first.UniqueID, second.UniqueID }, report.ResultIds);
        Assert.Equal(timestamp, report.LastEditedUtc);
        Assert.Equal(new[] { second, first }, AnalysisReportPresentation.ResultsInProjectOrder(report, new[] { second, first }));
    }

    [Fact]
    public void NewFingerprintExcludesExactlyTheTwoReportLabels()
    {
        var package = new AnalysisInterpretationPackage
        {
            Report = new InterpretationReportEvidence { EvidenceId = "report-1", ReportId = "report", Name = "Display name" },
            EvidenceCatalog = new()
            {
                new() { Id = "report-1", Kind = "report", Label = "Display name" },
                new() { Id = "result-1", Kind = "result", Label = "Scientific result name" },
            },
            StudyContext = new AnalysisStudyContext { ScientificQuestion = "Scientific question" },
        };
        var prompt = AnalysisInterpretationPromptBuilder.Build(package);
        var expected = JsonNode.Parse(prompt.CanonicalPackageJson);
        expected["report"]["name"] = "";
        expected["evidenceCatalog"][0]["label"] = "";
        Assert.Equal(Hash(expected.ToJsonString(new JsonSerializerOptions { WriteIndented = false })), prompt.InputFingerprint);
        Assert.Contains("Display name", prompt.CanonicalPackageJson);
        package.Report.Name = "Renamed";
        package.EvidenceCatalog[0].Label = "Renamed";
        Assert.Equal(prompt.InputFingerprint, AnalysisInterpretationPromptBuilder.Build(package).InputFingerprint);
        package.EvidenceCatalog[1].Label = "Changed result evidence";
        Assert.NotEqual(prompt.InputFingerprint, AnalysisInterpretationPromptBuilder.Build(package).InputFingerprint);
    }

    [Fact]
    public void DisplayNamesReachAiWhileReportRenamingPreservesFreshnessAndChangedEvidenceDoesNot()
    {
        var result = Result("Content summary");
        var report = Report(result);
        AnalysisResult Resolve(string id) => id == result.UniqueID ? result : null;
        var package = AnalysisInterpretationPackageBuilder.Build(report, Resolve, id => null);
        Assert.Equal("Content summary report", package.Report.Name);
        Assert.Equal(package.Report.Name, package.EvidenceCatalog.Single(entry => entry.Id == "report-1").Label);
        var prompt = AnalysisInterpretationPromptBuilder.Build(package);
        report.ApproveInterpretation(new AnalysisInterpretationRecord
        { InterpretationMarkdown = "## Overall interpretation\nSaved text.", InputFingerprint = prompt.InputFingerprint,
            EvidenceFingerprintScheme = AnalysisInterpretationPromptBuilder.EvidenceFingerprintScheme });
        report.Name = "Renamed";
        var timestamp = report.LastEditedUtc;
        Assert.Equal(AnalysisInterpretationFreshness.Current, AnalysisInterpretationService.EvaluateFreshness(report, Resolve, id => null).Status);
        Assert.Equal(timestamp, report.LastEditedUtc);
        report.UpdateStudyContext(new AnalysisStudyContext { ScientificQuestion = "A changed question" });
        Assert.Equal(AnalysisInterpretationFreshness.Stale, AnalysisInterpretationService.EvaluateFreshness(report, Resolve, id => null).Status);
        report.UpdateStudyContext(new AnalysisStudyContext());
        report.SetSupportingExperimentIds(new[] { "support" });
        var support = AnalysisInterpretationCollectionTests.Supporting(); support.SetID("support");
        Assert.Equal(AnalysisInterpretationFreshness.Stale, AnalysisInterpretationService.EvaluateFreshness(report, Resolve, id => support).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Legacy custom name")]
    public async Task LegacyFingerprintRemainsVerifiableAcrossRenameAndSaveWithoutBeingRewritten(string name)
    {
        var result = Result("Result");
        var report = Report(result); report.Name = name;
        AnalysisResult Resolve(string id) => id == result.UniqueID ? result : null;
        var package = AnalysisInterpretationPackageBuilder.Build(report, Resolve, id => null);
        package.Report.Name = name;
        package.EvidenceCatalog.Single(entry => entry.Id == "report-1").Label = name;
        var fingerprint = Hash(AnalysisInterpretationPromptBuilder.Build(package).CanonicalPackageJson);
        // Restore models a pre-existing record without optional rename provenance.
        report.Restore(report.ResultIds.ToArray(), Array.Empty<string>(), new AnalysisStudyContext(),
            AnalysisInterpretationOptions.Default(), new AnalysisInterpretationRecord
            { InterpretationMarkdown = "## Overall interpretation\nLegacy text.", InputFingerprint = fingerprint,
                EvidenceFingerprintScheme = AnalysisInterpretationPromptBuilder.LegacyEvidenceFingerprintScheme });
        Assert.Equal(AnalysisInterpretationFreshness.Current, AnalysisInterpretationService.EvaluateFreshness(report, Resolve, id => null).Status);
        report.Name = "Renamed legacy report";
        Assert.Equal(name, report.ApprovedInterpretation.ReportNameForFingerprint);
        Assert.Equal(fingerprint, report.ApprovedInterpretation.InputFingerprint);
        Assert.Equal(AnalysisInterpretationFreshness.Current, AnalysisInterpretationService.EvaluateFreshness(report, Resolve, id => null).Status);
        using var saved = await Save(report);
        var restored = Assert.Single((await FTXTCReader.ReadWithRecovery(saved, FtxtcReadPolicy.Strict)).Reports);
        Assert.Equal(name, restored.ApprovedInterpretation.ReportNameForFingerprint);
        Assert.Equal(fingerprint, restored.ApprovedInterpretation.InputFingerprint);
        Assert.Equal(AnalysisInterpretationFreshness.Current, AnalysisInterpretationService.EvaluateFreshness(restored, Resolve, id => null).Status);
        restored.AuthorComments = "Changed evidence";
        restored.Name = "Renamed again";
        Assert.Equal(AnalysisInterpretationFreshness.Stale, AnalysisInterpretationService.EvaluateFreshness(restored, Resolve, id => null).Status);
    }

    [Fact]
    public void ProjectOrderKeepsInterpretationAndPdfReferencesAlignedAndMakesChangedOrderStale()
    {
        var first = Result("Alpha"); var second = Result("Beta");
        var report = Report(first, second);
        AnalysisResult Resolve(string id) => id == first.UniqueID ? first : second;
        var order = new[] { second.UniqueID, first.UniqueID };
        var package = AnalysisInterpretationPackageBuilder.Build(report, Resolve, id => null, resultOrderIds: order);
        Assert.Equal("Beta", package.Results[0].Name);
        Assert.Equal("1", package.Results[0].ReportReference);
        Assert.Equal(order, package.Report.ResultIds);
        report.ApproveInterpretation(new AnalysisInterpretationRecord
        { InterpretationMarkdown = "## Overall interpretation\nSaved text.",
            InputFingerprint = AnalysisInterpretationPromptBuilder.Build(package).InputFingerprint,
            EvidenceFingerprintScheme = AnalysisInterpretationPromptBuilder.EvidenceFingerprintScheme });
        var timestamp = report.LastEditedUtc;
        Assert.Equal(AnalysisInterpretationFreshness.Current, AnalysisInterpretationService.EvaluateFreshness(report, Resolve, id => null, order).Status);
        Assert.Equal(AnalysisInterpretationFreshness.Stale, AnalysisInterpretationService.EvaluateFreshness(report, Resolve, id => null,
            new[] { first.UniqueID, second.UniqueID }).Status);
        Assert.Equal(new[] { first.UniqueID, second.UniqueID }, report.ResultIds);
        Assert.Equal(timestamp, report.LastEditedUtc);
    }

    [Fact]
    public void IdenticalAiApprovalOrEditorTextDoesNotCountAsAnEdit()
    {
        var report = new AnalysisReport();
        var record = new AnalysisInterpretationRecord
        { InterpretationMarkdown = "## Overall interpretation\nSaved text.",
            EvidenceFingerprintScheme = AnalysisInterpretationPromptBuilder.EvidenceFingerprintScheme };
        report.ApproveInterpretation(record);
        var timestamp = report.LastEditedUtc;
        var approved = report.ApprovedInterpretation.ApprovedAtUtc;
        report.ApproveInterpretation(record);
        report.UpdateApprovedInterpretationText(record.InterpretationMarkdown);
        Assert.Equal(timestamp, report.LastEditedUtc);
        Assert.Equal(approved, report.ApprovedInterpretation.ApprovedAtUtc);
        Assert.False(report.ApprovedInterpretation.UserEdited);
        report.UpdateApprovedInterpretationText("## Overall interpretation\nChanged text.");
        Assert.True(report.LastEditedUtc > timestamp);
        Assert.True(report.ApprovedInterpretation.UserEdited);
    }

    [Fact]
    public void ReapprovingSameLegacyGenerationAfterRenameKeepsItsOriginalNameAndTimestamp()
    {
        var report = new AnalysisReport { Name = "Original report name" };
        var record = new AnalysisInterpretationRecord
        { InterpretationMarkdown = "## Overall interpretation\nSaved legacy text.", InputFingerprint = "same-original-fingerprint",
            EvidenceFingerprintScheme = AnalysisInterpretationPromptBuilder.LegacyEvidenceFingerprintScheme };
        report.ApproveInterpretation(record);
        report.Name = "Renamed report";
        var timestamp = report.LastEditedUtc;
        report.ApproveInterpretation(record);
        Assert.Equal(timestamp, report.LastEditedUtc);
        Assert.Equal("Original report name", report.ApprovedInterpretation.ReportNameForFingerprint);
        Assert.Equal("same-original-fingerprint", report.ApprovedInterpretation.InputFingerprint);
    }

    [Fact]
    public async Task TimestampRoundTripsAndOlderReportsRemainUnknownUntilRealEdits()
    {
        var report = new AnalysisReport { Name = "Stored report" };
        var recorded = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        report.RestoreLastEditedUtc(recorded);
        using var saved = await Save(report);
        var restored = Assert.Single((await FTXTCReader.ReadWithRecovery(saved, FtxtcReadPolicy.Strict)).Reports);
        Assert.Equal(recorded, restored.LastEditedUtc);
        Assert.False(restored.IsModified);
        using var older = RewriteReport(saved, state => state.Remove("lastEditedUtc"));
        var legacy = Assert.Single((await FTXTCReader.ReadWithRecovery(older, FtxtcReadPolicy.Strict)).Reports);
        Assert.Null(legacy.LastEditedUtc);
        Assert.Equal("Not recorded", ProjectInformationPresentation.LastEditedText(legacy));
        legacy.UpdateStudyContext(legacy.StudyContext);
        legacy.UpdateInterpretationSettings(legacy.InterpretationSettings);
        Assert.Null(legacy.LastEditedUtc);
        Assert.Null(legacy.CreateDetachedCopy().LastEditedUtc);
        using var unchanged = await Save(legacy);
        using (var zip = new ZipArchive(unchanged, ZipArchiveMode.Read, leaveOpen: true))
        using (var json = JsonDocument.Parse(zip.GetEntry("reports/000000/report.json").Open()))
            Assert.False(json.RootElement.TryGetProperty("lastEditedUtc", out _));
        legacy.Name = "Edited legacy report";
        Assert.NotNull(legacy.LastEditedUtc);
    }

    [Fact]
    public async Task RemovingLastReportCanBeSavedAndStrictlyReopenedAsEmpty()
    {
        DocumentDirtyTracker.Initialize();
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.MarkClean();
        try
        {
            var report = new AnalysisReport { Name = "Retained text" };
            DataManager.AddReport(report);
            DocumentDirtyTracker.MarkClean();
            Assert.True(DataManager.RemoveReport(report));
            Assert.True(DocumentDirtyTracker.IsDirty);
            using var empty = new MemoryStream();
            await FTXTCWriter.WriteStream(empty, DataManager.Data, DataManager.Results, DataManager.SourceItems, DataManager.Reports);
            empty.Position = 0;
            var restored = await FTXTCReader.ReadWithRecovery(empty, FtxtcReadPolicy.Strict);
            Assert.Empty(restored.Reports);
            Assert.Empty(restored.Containers);
        }
        finally { DataManager.Clear(DataClearMode.ResetSession); DocumentDirtyTracker.MarkClean(); }
    }

    static AnalysisReport Report(params AnalysisResult[] results)
    {
        var report = new AnalysisReport();
        report.SetResultIds(results.Select(result => result.UniqueID));
        report.RestoreLastEditedUtc(report.Date);
        return report;
    }

    static AnalysisResult Result(string name, SolverConvergence convergence = null)
    {
        var model = InjectionProcessingMethodTests.FittedModel();
        var globalModel = new GlobalModel(new List<Model> { model })
        { Parameters = new GlobalModelParameters(), ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None } };
        globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = globalModel },
            new List<SolutionInterface> { model.Solution }, convergence ?? model.Solution.Convergence, reconstructBootstrap: false);
        globalModel.Solution = solution;
        var result = new AnalysisResult(solution) { Name = name };
        result.SetID(Guid.NewGuid().ToString("N"));
        model.Data.SetID(Guid.NewGuid().ToString("N"));
        return result;
    }

    static async Task<MemoryStream> Save(AnalysisReport report)
    {
        var saved = new MemoryStream();
        await FTXTCWriter.WriteStream(saved, Array.Empty<ExperimentData>(), reports: new[] { report });
        saved.Position = 0;
        return saved;
    }

    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    static MemoryStream RewriteReport(Stream source, Action<JsonObject> transform)
    {
        source.Position = 0;
        using var input = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var entries = input.Entries.ToDictionary(entry => entry.FullName, entry =>
        { using var stream = entry.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray(); });
        var state = JsonNode.Parse(entries["reports/000000/report.json"]).AsObject();
        transform(state);
        entries["reports/000000/report.json"] = Encoding.UTF8.GetBytes(state.ToJsonString());
        var manifest = JsonNode.Parse(entries["manifest.json"]).AsObject();
        foreach (var entry in manifest["entries"].AsArray())
        {
            var bytes = entries[entry["path"].GetValue<string>()];
            entry["length"] = bytes.LongLength;
            entry["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        entries["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in entries)
            { using var stream = archive.CreateEntry(entry.Key).Open(); stream.Write(entry.Value); }
        output.Position = 0;
        return output;
    }
}
