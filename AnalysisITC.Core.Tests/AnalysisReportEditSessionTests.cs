using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class AnalysisReportEditSessionTests : IDisposable
{
    public AnalysisReportEditSessionTests()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.Initialize();
    }
    public void Dispose() => DataManager.Clear(DataClearMode.ResetSession);

    [Fact]
    public void DraftIsDetachedUntilSaveAndNoOpSaveKeepsTimestampAndProjectClean()
    {
        var result = Result();
        var draft = Report(result);
        var session = new AnalysisReportEditSession(draft);
        DocumentDirtyTracker.MarkClean();
        session.Draft.SetManualInterpretation("Original text");
        session.Draft.UpdateStudyContext(new AnalysisStudyContext { ScientificQuestion = "Question" });
        Assert.Empty(DataManager.Reports);
        Assert.False(DocumentDirtyTracker.IsDirty);
        Assert.True(session.HasChanges);
        var saved = Save(session);
        Assert.True(DocumentDirtyTracker.IsDirty);
        Assert.NotSame(saved, session.Draft);
        Assert.Equal(draft.UniqueID, saved.UniqueID);
        Assert.Equal(draft.Date, saved.Date);
        Assert.False(session.HasChanges);
        var lastSaved = saved.LastEditedUtc;
        DocumentDirtyTracker.MarkClean();
        Save(session);
        Assert.Equal(lastSaved, saved.LastEditedUtc);
        Assert.False(DocumentDirtyTracker.IsDirty);
        session.Draft.Comments = "Uncommitted";
        Assert.Equal("", saved.Comments);
        session.Draft.Comments = "";
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void EditingAndRevertingTextStaysDirtyUntilExplicitSave()
    {
        var report = Report(Result());
        report.SetManualInterpretation("## Overall interpretation\nOriginal");
        DataManager.AddReport(report);
        var original = report.ApprovedInterpretation;
        var session = new AnalysisReportEditSession(report, report);
        session.UpdateInterpretationText("## Overall interpretation\nChanged", "changed-context");
        Assert.True(session.HasChanges);
        session.UpdateInterpretationText(original.InterpretationMarkdown, "changed-context");
        Assert.True(session.HasChanges);
        Assert.Equal("changed-context", session.Draft.ApprovedInterpretation.AssessmentContextFingerprint);
        Assert.Equal(original.InterpretationMarkdown, session.Draft.ApprovedInterpretation.InterpretationMarkdown);
        Save(session);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void RevertingPersistedChoicesIgnoresTransientOutputPurposeAndResolvedAutomaticTitle()
    {
        var session = new AnalysisReportEditSession(Report(Result()));
        var choices = session.Draft.PresentationSettings;
        choices.DocumentLabel = "Temporary change";
        choices.OutputPurpose = ResultOutputPurpose.Diagnostic;
        session.Draft.UpdatePresentationSettings(choices);
        Assert.True(session.HasChanges);
        choices.DocumentLabel = "";
        choices.Title = "A newly resolved automatic title";
        session.Draft.UpdatePresentationSettings(choices);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void HistoricalTextWithoutRecordedTimesDoesNotClaimAnUpdateAndScopeCanBeReverted()
    {
        var first = Result(); var second = Result();
        var report = Report(first);
        report.Restore(report.ResultIds.ToArray(), Array.Empty<string>(), new AnalysisStudyContext(), AnalysisInterpretationOptions.Default(),
            new AnalysisInterpretationRecord { Origin = AnalysisInterpretationOrigin.Manual, InterpretationMarkdown = "Historical text" }, report.PresentationSettings);
        var session = new AnalysisReportEditSession(report);
        Assert.False(AnalysisReportEditingPresentation.NeedsInterpretationReview(session.Draft, Resolve));
        session.Draft.SetResultIds(new[] { second.UniqueID });
        Assert.True(AnalysisReportEditingPresentation.NeedsInterpretationReview(session.Draft, Resolve));
        session.Draft.SetResultIds(new[] { first.UniqueID });
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void ChangedScopeAndSupportsSaveTogetherWithoutChoosingAnotherStoredReport()
    {
        var first = Result(); var second = Result();
        var support = InjectionProcessingMethodTests.FittedModel().Data;
        DataManager.AddData(new[] { support });
        var saved = Report(first); saved.SetManualInterpretation("Keep this text"); DataManager.AddReport(saved);
        var other = Report(second); DataManager.AddReport(other);
        var session = new AnalysisReportEditSession(saved, saved);
        session.Draft.SetResultIds(new[] { second.UniqueID });
        session.Draft.SetSupportingExperimentIds(new[] { support.UniqueID });
        Assert.True(AnalysisReportEditingPresentation.NeedsInterpretationReview(session.Draft, Resolve));
        Assert.Same(saved, Save(session));
        Assert.Equal(new[] { second.UniqueID }, saved.ResultIds);
        Assert.Equal(new[] { support.UniqueID }, saved.SupportingExperimentIds);
        Assert.Contains("Keep this text", saved.ApprovedInterpretation.InterpretationMarkdown);
        Assert.Null(other.ApprovedInterpretation);
    }

    [Theory]
    [InlineData(true, "A title", "")]
    [InlineData(false, "  A title  ", "A title")]
    [InlineData(false, "  ", "")]
    public void SavedNameComesFromTitle(bool automatic, string title, string expected)
    {
        var draft = Report(Result());
        draft.UpdatePresentationSettings(new AnalysisReportOptions { AutomaticTitle = automatic, Title = title });
        var session = new AnalysisReportEditSession(draft);
        Assert.Equal(expected, Save(session).Name);
    }

    [Fact]
    public void FailedSavePublishesNothingAndKeepsDraft()
    {
        var session = new AnalysisReportEditSession(Report(Result()));
        session.Draft.SetResultIds(new[] { "missing-result" });
        var draft = session.Draft;
        DocumentDirtyTracker.MarkClean();
        Assert.Throws<InvalidOperationException>(() => Save(session));
        Assert.Same(draft, session.Draft);
        Assert.Empty(DataManager.Reports);
        Assert.False(DocumentDirtyTracker.IsDirty);
    }

    [Theory]
    [InlineData("sha256:utf8:canonical-package-json-v1", false)]
    [InlineData("sha256:utf8:canonical-package-json-v1", true)]
    [InlineData("sha256:utf8:canonical-package-json-v2", false)]
    [InlineData("sha256:utf8:canonical-package-json-v2", true)]
    public async Task SaveAsPreservesFreshnessAcrossCopiesAndStrictReopening(string scheme, bool automaticName)
    {
        var result = Result();
        var report = Report(result);
        report.SetDate(new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Unspecified));
        report.Name = automaticName ? "" : "Original display name";
        report.UpdatePresentationSettings(new AnalysisReportOptions { AutomaticTitle = false, Title = "New saved title", ReportId = "LAB-123" });
        var package = AnalysisInterpretationPackageBuilder.Build(report, Resolve, ResolveExperiment);
        if (scheme == AnalysisInterpretationPromptBuilder.LegacyEvidenceFingerprintScheme)
        {
            // Historical v1 packages used the stored name, including a blank name.
            package.Report.Name = report.Name;
            package.EvidenceCatalog.Single(entry => entry.Id == "report-1").Label = report.Name;
        }
        var fingerprint = AnalysisInterpretationPromptBuilder.Fingerprint(package, scheme);
        report.ApproveInterpretation(new AnalysisInterpretationRecord { InterpretationMarkdown = "## Overall interpretation\nSaved AI text",
            InputFingerprint = fingerprint, EvidenceFingerprintScheme = scheme });
        DataManager.AddReport(report);
        var originalName = report.Name;
        var session = new AnalysisReportEditSession(report, report);
        var first = Save(session, true);
        Assert.NotEqual(report.UniqueID, first.UniqueID);
        Assert.Equal("LAB-123", first.PresentationSettings.ReportId);
        Assert.Equal("New saved title", first.Name);
        Assert.Equal(originalName, report.Name);
        Assert.Equal(fingerprint, first.ApprovedInterpretation.InputFingerprint);
        Assert.Equal(report.ApprovedInterpretation.ApprovedAtUtc, first.ApprovedInterpretation.ApprovedAtUtc);
        Assert.Equal("2026-02-03T04:05:06.0000000Z", first.ApprovedInterpretation.ReportDateUtcForFingerprint);
        Assert.Equal(AnalysisInterpretationFreshness.Current, Freshness(first));
        var second = Save(session, true);
        Assert.Equal(report.UniqueID, second.ApprovedInterpretation.ReportIdForFingerprint);
        Assert.Equal(AnalysisInterpretationFreshness.Current, Freshness(second));
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, DataManager.Data, DataManager.Results, reports: DataManager.Reports);
        stream.Position = 0;
        FtxtcJsonSchemaValidationTests.AssertPackageJsonValidates(stream);
        stream.Position = 0;
        var reopened = await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict);
        var restored = reopened.Reports.Single(item => item.UniqueID == second.UniqueID);
        Assert.Equal(second.ApprovedInterpretation.ReportDateUtcForFingerprint, restored.ApprovedInterpretation.ReportDateUtcForFingerprint);
        Assert.Equal(AnalysisInterpretationFreshness.Current, Freshness(restored));
        restored.UpdateStudyContext(new AnalysisStudyContext { ScientificQuestion = "Different evidence" });
        Assert.Equal(AnalysisInterpretationFreshness.Stale, Freshness(restored));
        var approval = restored.ApprovedInterpretation.ApprovedAtUtc;
        restored.ConfirmInterpretation();
        Assert.Equal(approval, restored.ApprovedInterpretation.ApprovedAtUtc);
        Assert.Equal(fingerprint, restored.ApprovedInterpretation.InputFingerprint);
        Assert.Equal(AnalysisInterpretationFreshness.Stale, Freshness(restored));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    public async Task ReviewUsesResultUpdateInstantsAndConfirmationSurvivesReopening(DateTimeKind kind)
    {
        var result = Result();
        var report = Report(result);
        report.SetManualInterpretation("Retained interpretation");
        var approved = report.ApprovedInterpretation.ApprovedAtUtc;
        var instant = approved.AddSeconds(1);
        result.Date = kind == DateTimeKind.Utc ? instant : DateTime.SpecifyKind(instant.ToLocalTime(), kind);
        result.RestoreLastContentChangedUtc(null); // Exercise the historical local-date fallback.
        Assert.True(AnalysisReportEditingPresentation.NeedsInterpretationReview(report, Resolve));
        result.Date = kind == DateTimeKind.Utc ? approved.AddSeconds(-1)
            : DateTime.SpecifyKind(approved.AddSeconds(-1).ToLocalTime(), kind);
        result.RestoreLastContentChangedUtc(null);
        Assert.False(AnalysisReportEditingPresentation.NeedsInterpretationReview(report, Resolve));
        var second = Result();
        report.SetResultIds(new[] { second.UniqueID });
        Assert.True(AnalysisReportEditingPresentation.NeedsInterpretationReview(report, Resolve));
        report.ConfirmInterpretation();
        Assert.False(AnalysisReportEditingPresentation.NeedsInterpretationReview(report, Resolve));
        Assert.Equal(approved, report.ApprovedInterpretation.ApprovedAtUtc);
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, DataManager.Data, DataManager.Results, reports: new[] { report });
        stream.Position = 0;
        FtxtcJsonSchemaValidationTests.AssertPackageJsonValidates(stream);
        stream.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict)).Reports);
        Assert.False(AnalysisReportEditingPresentation.NeedsInterpretationReview(restored, Resolve));
        Assert.Equal(report.ApprovedInterpretation.ConfirmedAtUtc, restored.ApprovedInterpretation.ConfirmedAtUtc);
    }

    [Fact]
    public void UpdatingResultAdvancesDateAndEditingConfirmsUserText()
    {
        var result = Result(); var report = Report(result);
        report.SetManualInterpretation("Before update");
        result.Date = DateTime.Now.AddDays(-1);
        result.UpdateSolution(result.Solution);
        Assert.True(AnalysisReportEditingPresentation.NeedsInterpretationReview(report, Resolve));
        report.UpdateApprovedInterpretationText("Edited after update");
        Assert.False(AnalysisReportEditingPresentation.NeedsInterpretationReview(report, Resolve));
    }

    [Fact]
    public async Task UserEditReplacesAiProvenanceAndSaveAsPersistsManualCurrentText()
    {
        var result = Result(); var report = Report(result);
        report.ApproveInterpretation(new AnalysisInterpretationRecord
        {
            InterpretationMarkdown = "## Overall interpretation\nOriginal AI text",
            InputFingerprint = "old evidence", EffectiveInputFingerprint = "effective evidence",
            EvidenceFingerprintScheme = AnalysisInterpretationPromptBuilder.EvidenceFingerprintScheme,
            Provider = "provider", Model = "model", ServiceRequestId = "request",
            ReportIdForFingerprint = "source-report", ReportDateUtcForFingerprint = "source-date",
            GeneratedAtUtc = DateTime.UtcNow.AddHours(-1),
        });
        DataManager.AddReport(report);
        Assert.Equal(AnalysisInterpretationFreshness.Stale, Freshness(report));
        var session = new AnalysisReportEditSession(report, report);
        session.UpdateInterpretationText("## Overall interpretation\nUser text", "current assessments");
        var manual = session.Draft.ApprovedInterpretation;
        Assert.Equal(AnalysisInterpretationOrigin.Manual, manual.Origin);
        Assert.Equal(AnalysisInterpretationFreshness.Current, Freshness(session.Draft));
        Assert.Empty(manual.InputFingerprint); Assert.Empty(manual.EffectiveInputFingerprint);
        Assert.Empty(manual.EvidenceFingerprintScheme); Assert.Empty(manual.Provider);
        Assert.Empty(manual.Model); Assert.Empty(manual.ServiceRequestId);
        Assert.Equal(default, manual.GeneratedAtUtc);
        Assert.Null(manual.ReportIdForFingerprint); Assert.Null(manual.ReportDateUtcForFingerprint);
        Assert.Null(manual.ConfirmedAtUtc);
        Assert.Equal(report.ResultIds, manual.ReviewedResultIds);
        Assert.Equal(AnalysisInterpretationOrigin.AiGenerated, report.ApprovedInterpretation.Origin);
        var copy = Save(session, true);
        Assert.Null(copy.ApprovedInterpretation.ReportIdForFingerprint);
        Assert.Null(copy.ApprovedInterpretation.ReportDateUtcForFingerprint);
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, DataManager.Data, DataManager.Results, reports: DataManager.Reports);
        stream.Position = 0; FtxtcJsonSchemaValidationTests.AssertPackageJsonValidates(stream); stream.Position = 0;
        var restored = (await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict)).Reports.Single(item => item.UniqueID == copy.UniqueID);
        Assert.Equal(AnalysisInterpretationOrigin.Manual, restored.ApprovedInterpretation.Origin);
        Assert.Equal(manual.InterpretationMarkdown, restored.ApprovedInterpretation.InterpretationMarkdown);
        Assert.Equal(AnalysisInterpretationFreshness.Current, Freshness(restored));
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void ClearingNewTextOrRevertingNewAiApprovalNeverRestoresEarlierProvenance()
    {
        var session = new AnalysisReportEditSession(Report(Result()));
        session.UpdateInterpretationText("Added word", "");
        session.UpdateInterpretationText("", "");
        Assert.Null(session.Draft.ApprovedInterpretation);
        Assert.True(session.HasChanges);
        Save(session);
        Assert.False(session.HasChanges);
        session.Draft.ApproveInterpretation(new AnalysisInterpretationRecord { InterpretationMarkdown = "New generated text", InputFingerprint = "new" });
        session.UpdateInterpretationText("", "");
        Assert.Null(session.Draft.ApprovedInterpretation);
        Assert.True(session.HasChanges);
    }

    [Theory]
    [InlineData("Plain generated text")]
    [InlineData("## Overall interpretation\nGenerated text")]
    public void LoadingGeneratedTextDoesNotImplyAUserEdit(string text)
    {
        var report = Report(Result());
        report.ApproveInterpretation(new AnalysisInterpretationRecord { InterpretationMarkdown = text, InputFingerprint = "generation" });
        var session = new AnalysisReportEditSession(report, report);
        Assert.False(session.HasChanges);
        Assert.False(session.HasInterpretationTextChanges(text));
        Assert.Equal(AnalysisInterpretationOrigin.AiGenerated, session.Draft.ApprovedInterpretation.Origin);
    }

    [Theory]
    [InlineData(false, "name")]
    [InlineData(true, "name")]
    [InlineData(false, "comments")]
    [InlineData(true, "comments")]
    [InlineData(false, "assessment")]
    [InlineData(true, "assessment")]
    [InlineData(false, "solution")]
    [InlineData(true, "solution")]
    public void EveryResultEditRaisesReviewAndConfirmDoesNotChangeAuthorship(bool ai, string change)
    {
        var result = Result(); var report = Report(result);
        if (ai) report.ApproveInterpretation(new AnalysisInterpretationRecord { InterpretationMarkdown = "## Overall interpretation\nRetained text", InputFingerprint = "retained fingerprint" });
        else report.SetManualInterpretation("Retained text");
        Assert.False(AnalysisReportEditingPresentation.NeedsInterpretationReview(report, Resolve));
        var oldDate = result.Date;
        var oldChange = result.LastContentChangedUtc;
        switch (change)
        {
            case "name": result.Name = "Renamed result"; break;
            case "comments": result.Comments = "Changed annotation"; break;
            case "assessment": result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected); break;
            case "solution": result.UpdateSolution(result.Solution); break;
        }
        Assert.True(result.LastContentChangedUtc > oldChange);
        if (change != "solution") Assert.Equal(oldDate, result.Date);
        Assert.True(AnalysisReportEditingPresentation.NeedsInterpretationReview(report, Resolve));
        var original = report.ApprovedInterpretation;
        report.ConfirmInterpretation();
        Assert.False(AnalysisReportEditingPresentation.NeedsInterpretationReview(report, Resolve));
        Assert.Equal(original.Origin, report.ApprovedInterpretation.Origin);
        Assert.Equal(original.ApprovedAtUtc, report.ApprovedInterpretation.ApprovedAtUtc);
        Assert.Equal(original.InputFingerprint, report.ApprovedInterpretation.InputFingerprint);
        result.Comments += " A further change";
        Assert.True(AnalysisReportEditingPresentation.NeedsInterpretationReview(report, Resolve));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ResultChangeTimestampComparesUtcInstants(DateTimeKind kind)
    {
        var result = Result(); var report = Report(result);
        var instant = new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc);
        report.RestoreInterpretation(new AnalysisInterpretationRecord
        {
            Origin = AnalysisInterpretationOrigin.Manual, InterpretationMarkdown = "Written text",
            ApprovedAtUtc = instant.AddSeconds(-1), ReviewedResultIds = report.ResultIds.ToList(),
        });
        result.RestoreLastContentChangedUtc(kind == DateTimeKind.Local ? instant.ToLocalTime() : DateTime.SpecifyKind(instant, kind));
        Assert.Equal(instant, result.LastContentChangedUtc);
        Assert.Equal(DateTimeKind.Utc, result.LastContentChangedUtc.Value.Kind);
        Assert.True(AnalysisReportEditingPresentation.NeedsInterpretationReview(report, Resolve));
        result.RestoreLastContentChangedUtc(instant.AddSeconds(-2));
        Assert.False(AnalysisReportEditingPresentation.NeedsInterpretationReview(report, Resolve));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResultChangeTimestampSurvivesSavingAndReopeningWithoutSyntheticEdits(bool recorded)
    {
        var result = Result(); var report = Report(result);
        result.Name = "A referenced name";
        if (!recorded) result.RestoreLastContentChangedUtc(null);
        report.SetManualInterpretation("A referenced name has this result.");
        report.ConfirmInterpretation();
        var timestamp = result.LastContentChangedUtc;
        result.Name = result.Name; result.Comments = result.Comments;
        _ = result.PresentationData;
        _ = AnalysisReportBuilder.Build(report, Resolve, ResolveExperiment, report.PresentationSettings);
        Assert.Equal(timestamp, result.LastContentChangedUtc);
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, DataManager.Data, DataManager.Results, reports: new[] { report });
        Assert.Equal(timestamp, result.LastContentChangedUtc);
        stream.Position = 0; FtxtcJsonSchemaValidationTests.AssertPackageJsonValidates(stream); stream.Position = 0;
        var reopened = await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict);
        var restored = Assert.Single(reopened.Containers.OfType<AnalysisResult>());
        Assert.Equal(timestamp, restored.LastContentChangedUtc);
        Assert.False(AnalysisReportEditingPresentation.NeedsInterpretationReview(Assert.Single(reopened.Reports), _ => restored));
    }

    static AnalysisInterpretationFreshness Freshness(AnalysisReport report) =>
        AnalysisInterpretationService.EvaluateFreshness(report, Resolve, ResolveExperiment).Status;
    static AnalysisReport Save(AnalysisReportEditSession session, bool copy = false) => session.Save(copy, Resolve, ResolveExperiment);
    static AnalysisResult Resolve(string id) => DataManager.Results.FirstOrDefault(result => result.UniqueID == id)!;
    static ExperimentData ResolveExperiment(string id) => DataManager.Data.FirstOrDefault(data => data.UniqueID == id)!;
    static AnalysisReport Report(AnalysisResult result)
    {
        var report = new AnalysisReport(); report.SetResultIds(new[] { result.UniqueID });
        report.UpdatePresentationSettings(new AnalysisReportOptions());
        return report;
    }
    static AnalysisResult Result()
    {
        var fitted = InjectionProcessingMethodTests.FittedModel();
        var model = new GlobalModel(new() { fitted }) { Parameters = new GlobalModelParameters(), ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None } };
        model.Parameters.AddIndivdualParameter(fitted.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = model }, new() { fitted.Solution }, fitted.Solution.Convergence, reconstructBootstrap: false);
        model.Solution = global;
        var result = new AnalysisResult(global) { Date = DateTime.Now.AddMinutes(-1) };
        DataManager.AddData(new ITCDataContainer[] { fitted.Data, result });
        return result;
    }
}
