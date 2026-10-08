using System;
using System.Collections.Generic;
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
using AnalysisITC.Core.Numerics;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class FtxtcImportResolverTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftxtc-duplicates-" + Guid.NewGuid().ToString("N"));
    readonly IFtxtcDuplicatePromptService originalPrompt = PlatformServices.FtxtcDuplicatePromptService;
    readonly RecordingPrompt prompt = new(FtxtcDuplicateAction.SkipDuplicates);

    public FtxtcImportResolverTests()
    {
        Directory.CreateDirectory(directory);
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.Initialize();
        PlatformServices.RegisterFtxtcDuplicatePromptService(prompt);
    }

    public void Dispose()
    {
        PlatformServices.RegisterFtxtcDuplicatePromptService(originalPrompt);
        DataManager.Clear(DataClearMode.ResetSession);
        Directory.Delete(directory, true);
    }

    [Theory]
    [InlineData(false, FtxtcDuplicateAction.SkipDuplicates)]
    [InlineData(true, FtxtcDuplicateAction.SkipDuplicates)]
    [InlineData(false, FtxtcDuplicateAction.ImportCopies)]
    [InlineData(true, FtxtcDuplicateAction.ImportCopies)]
    public async Task OverlappingFilesAreResolvedAgainstPreviouslyAcceptedContent(bool together, FtxtcDuplicateAction action)
    {
        var result = Result(Prepared());
        var first = await Save("first", new[] { result });
        var second = await Save("second", new[] { result });
        prompt.Action = action;
        if (together) Assert.True((await DataReader.ReadPathsAsync(new[] { first, second })).LoadedAllRequested);
        else
        {
            Assert.True((await DataReader.ReadPathsAsync(new[] { first })).OpenedCleanProject);
            Assert.True((await DataReader.ReadPathsAsync(new[] { second })).LoadedAllRequested);
        }

        var summary = Assert.Single(prompt.Summaries);
        Assert.Equal("second.ftxtc", summary.FileName);
        Assert.Equal(1, summary.ExperimentCount);
        Assert.Equal(1, summary.ResultCount);
        Assert.True(summary.SolutionCount > 0);
        Assert.Equal(action == FtxtcDuplicateAction.ImportCopies ? 2 : 1, DataManager.Results.Count);
        Assert.Equal(action == FtxtcDuplicateAction.ImportCopies ? 2 : 1, DataManager.Data.Count);
        Assert.Equal(DataManager.SourceItems.Count, DataManager.SourceItems.Select(item => item.UniqueID).Distinct().Count());
        if (action == FtxtcDuplicateAction.ImportCopies)
            Assert.NotEqual(DataManager.Results[0].Solution.Solutions[0].Guid, DataManager.Results[1].Solution.Solutions[0].Guid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EntirelySkippedFilePreservesSaveDestinationAndDirtyState(bool dirty)
    {
        var result = Result(Prepared());
        var first = await Save("original", new[] { result });
        var duplicate = await Save("duplicate", new[] { result });
        await DataReader.ReadPathsAsync(new[] { first });
        if (dirty) DocumentDirtyTracker.MarkDirty();
        var stamp = DocumentDirtyTracker.CaptureSaveStamp();
        var containers = DataManager.SourceItems.ToArray();
        var read = await DataReader.ReadPathsAsync(new[] { duplicate });

        Assert.True(read.LoadedAllRequested);
        Assert.Equal(0, read.AddedItemCount);
        Assert.Equal(first, ProjectDocumentState.Path);
        Assert.Equal(dirty, DocumentDirtyTracker.IsDirty);
        Assert.True(DocumentDirtyTracker.MatchesSaveStamp(stamp));
        Assert.Equal(containers, DataManager.SourceItems);
    }

    [Fact]
    public async Task SkipKeepsNewResultAndBindsItsSavedModelsToExistingExperiments()
    {
        var result = Result(Prepared());
        var experiment = result.Solution.Solutions[0].Data;
        var experimentPath = await Save("experiment", Array.Empty<AnalysisResult>(), new[] { experiment });
        var resultPath = await Save("result", new[] { result });
        await DataReader.ReadPathsAsync(new[] { experimentPath });
        var existing = Assert.Single(DataManager.Data);
        var attached = existing.Solution;
        var parent = attached.ParentSolution;
        var estimates = attached.Parameters.ToDictionary(entry => entry.Key, entry => entry.Value);
        var expectedValidity = result.ValiditySnapshot.Compare(result.Solution).Status;
        await DataReader.ReadPathsAsync(new[] { resultPath });

        var imported = Assert.Single(DataManager.Results);
        var member = Assert.Single(imported.Solution.Solutions);
        Assert.Same(existing, member.Data);
        Assert.Same(attached, existing.Solution);
        Assert.Same(parent, attached.ParentSolution);
        Assert.Same(imported.Solution, member.ParentSolution);
        Assert.Equal(attached.Guid, member.Guid);
        Assert.NotSame(attached, member);
        Assert.Equal(estimates, attached.Parameters);
        Assert.Equal(expectedValidity, imported.ValiditySnapshot.Compare(imported.Solution).Status);

        var combined = await SaveCurrent("combined");
        var restored = await Strict(combined);
        Assert.Equal(member.Guid, Assert.Single(restored.OfType<AnalysisResult>()).Solution.Solutions[0].Guid);
        Assert.Equal(existing.UniqueID, Assert.Single(restored.OfType<AnalysisResult>()).Solution.Solutions[0].Data.UniqueID);
    }

    [Fact]
    public async Task SkipRetainsNewResultWithExistingGlobalFitWithoutReparentingExistingSolutions()
    {
        var first = Result(Prepared());
        var second = new AnalysisResult(first.Solution) { Name = "Additional result for saved fit" };
        var firstPath = await Save("first-result", new[] { first });
        var secondPath = await Save("additional-result", new[] { second });
        await DataReader.ReadPathsAsync(new[] { firstPath });
        var existingResult = DataManager.Results[0];
        var existingExperiment = DataManager.Data[0];
        var existingMember = existingResult.Solution.Solutions[0];
        var attached = existingExperiment.Solution;
        var attachedParent = attached.ParentSolution;
        Assert.NotNull(existingMember.ParentSolution);
        await DataReader.ReadPathsAsync(new[] { secondPath });

        var imported = DataManager.Results[1];
        var member = imported.Solution.Solutions[0];
        Assert.Same(existingResult.Solution, existingMember.ParentSolution);
        Assert.Same(attachedParent, attached.ParentSolution);
        Assert.Same(attached, existingExperiment.Solution);
        Assert.Same(existingExperiment, member.Data);
        Assert.Same(imported.Solution, member.ParentSolution);
        Assert.NotSame(existingResult.Solution, imported.Solution);
        Assert.Equal(existingResult.Solution.UniqueID, imported.Solution.UniqueID);
        Assert.Equal(existingMember.Guid, member.Guid);
        Assert.Equal(second.Name, imported.Name);

        var restored = (await Strict(await SaveCurrent("combined-shared-fit"))).OfType<AnalysisResult>().ToArray();
        Assert.Equal(2, restored.Length);
        Assert.All(restored, result => Assert.Same(result.Solution, result.Solution.Solutions[0].ParentSolution));
        Assert.Equal(restored[0].Solution.UniqueID, restored[1].Solution.UniqueID);
    }

    [Fact]
    public async Task SkipBindsSavedNullModelsToRetainedExperiments()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "report_test.ftxtc");
        var saved = await Strict(path);
        var experimentPath = await Save("existing-experiments", Array.Empty<AnalysisResult>(),
            saved.OfType<ExperimentData>().ToArray());
        await DataReader.ReadPathsAsync(new[] { experimentPath });
        var existing = DataManager.Data.ToDictionary(experiment => experiment.UniqueID);
        var attachments = existing.ToDictionary(entry => entry.Key, entry => entry.Value.Solution);
        await DataReader.ReadPathsAsync(new[] { path });

        var nullSolutions = DataManager.Results.SelectMany(result => result.MemberAssessments)
            .Where(member => member.Comparison != null).SelectMany(member => member.Comparison.NullSolutions).ToArray();
        Assert.NotEmpty(nullSolutions);
        Assert.All(nullSolutions, solution => Assert.Same(existing[solution.Data.UniqueID], solution.Data));
        Assert.All(DataManager.Results.SelectMany(result => result.Solution.Solutions),
            solution => Assert.Same(existing[solution.Data.UniqueID], solution.Data));
        Assert.All(attachments, entry => Assert.Same(entry.Value, existing[entry.Key].Solution));
        var restored = await Strict(await SaveCurrent("combined-retained-null-models"));
        Assert.Equal(DataManager.Results.Count, restored.OfType<AnalysisResult>().Count());
    }

    [Fact]
    public async Task ReusedExperimentChangesRemainVisibleInImportedValiditySnapshot()
    {
        var result = Result(Prepared());
        var data = result.Solution.Solutions[0].Data;
        var path = await Save("saved-result", new[] { result });
        var experimentPath = await Save("experiment", Array.Empty<AnalysisResult>(), new[] { data });
        await DataReader.ReadPathsAsync(new[] { experimentPath });
        var existing = Assert.Single(DataManager.Data);
        existing.CellConcentration = new FloatWithError(existing.CellConcentration.Value * 2);
        await DataReader.ReadPathsAsync(new[] { path });

        var imported = Assert.Single(DataManager.Results);
        Assert.Same(existing, imported.Solution.Solutions[0].Data);
        Assert.Equal(AnalysisResultValidity.Invalid, imported.ValiditySnapshot.Compare(imported.Solution).Status);
        Assert.Equal(data.CellConcentration.Value, imported.ValiditySnapshot.Experiments[0].CellConcentration);
    }

    [Fact]
    public async Task SkipImportsUnrelatedNewContentAndReportOnlyAdditions()
    {
        var first = Result(Prepared());
        var second = Result(Prepared());
        var originalPath = await Save("original", new[] { first });
        var report = Report(first);
        var overlapping = await Save("overlap", new[] { first, second }, reports: new[] { report });
        await DataReader.ReadPathsAsync(new[] { originalPath });
        var original = Assert.Single(DataManager.Results);
        await DataReader.ReadPathsAsync(new[] { overlapping });
        Assert.Same(original, DataManager.Results[0]);
        Assert.Equal(2, DataManager.Results.Count);
        Assert.Single(DataManager.Reports);

        var anotherReport = Report(first);
        var reportOnly = await Save("report-only", new[] { first }, reports: new[] { anotherReport });
        DocumentDirtyTracker.MarkClean();
        var read = await DataReader.ReadPathsAsync(new[] { reportOnly });
        Assert.True(read.LoadedAllRequested);
        Assert.Equal(0, read.AddedItemCount);
        Assert.Equal(2, DataManager.Reports.Count);
        Assert.True(DocumentDirtyTracker.IsDirty);
        Assert.Equal(first.UniqueID, DataManager.Reports[1].ResultIds[0]);
    }

    [Fact]
    public async Task CopyRemapsCompetitorBufferAndTandemReferencesIncludingHistoricalSnapshots()
    {
        var source = Result(Prepared());
        var buffer = Prepared().Data;
        var targetModel = Prepared("competitive");
        targetModel.Data.SetBufferSubtraction(buffer, BufferSubtractionMethod.MatchedInjection, notify: false);
        var reference = ExperimentAttribute.CompetitorResultReference(source.UniqueID);
        reference.SourceSolutionId = source.Solution.UniqueID;
        reference.CapturedAffinity = new FloatWithError(2e-6, .2e-6, 1.6e-6, 2.4e-6);
        reference.CapturedEnthalpy = new FloatWithError(-32000, 1200, -34500, -29500);
        targetModel.Data.Attributes.Add(reference);
        targetModel.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue = true;
        targetModel.Data.SetTandemSourceExperimentIds(new[] { source.Solution.Solutions[0].Data.UniqueID, "external-source" });
        var target = Result(targetModel);
        var report = Report(target);
        var context = new AnalysisStudyContext();
        context.Experiments.Add(new AnalysisExperimentContext { ExperimentId = targetModel.Data.UniqueID, Annotation = "Target" });
        report.UpdateStudyContext(context);
        report.SetSupportingExperimentIds(new[] { buffer.UniqueID, "external-support" });
        report.SetManualInterpretation("Preserve this saved text.", "saved-context-fingerprint");
        // External report selections are retained; package reports allow unresolved references.
        var path = await Save("competitor", new[] { source, target }, new[] { buffer }, new[] { report });
        await DataReader.ReadPathsAsync(new[] { path });
        var originalSource = DataManager.Results[0];
        var originalTarget = DataManager.Results[1];
        prompt.Action = FtxtcDuplicateAction.ImportCopies;
        await DataReader.ReadPathsAsync(new[] { path });

        var copiedSource = DataManager.Results[2];
        var copiedTarget = DataManager.Results[3];
        var data = copiedTarget.Solution.Solutions[0].Data;
        var copiedReference = data.Attributes.Single(attribute => attribute.Key == AttributeKey.CompetitorResult);
        var snapshot = copiedTarget.ValiditySnapshot.Experiments[0];
        var captured = snapshot.Attributes.Single(attribute => attribute.Key == AttributeKey.CompetitorResult);
        Assert.Equal(copiedSource.UniqueID, copiedReference.StringValue);
        Assert.Equal(copiedSource.Solution.UniqueID, copiedReference.SourceSolutionId);
        Assert.Equal(copiedReference.StringValue, captured.StringValue);
        Assert.Equal(copiedReference.SourceSolutionId, captured.SourceSolutionId);
        Assert.Equal(2e-6, copiedReference.CapturedAffinity.Value);
        Assert.Equal(1.6e-6, copiedReference.CapturedAffinity.Lower);
        Assert.Equal(-34500, captured.CapturedEnthalpyLower);
        Assert.Equal(data.ReferenceExperiment.UniqueID,
            snapshot.Attributes.Single(attribute => attribute.Key == AttributeKey.BufferSubtraction).StringValue);
        Assert.NotEqual(originalTarget.Solution.Solutions[0].Data.ReferenceExperiment.UniqueID, data.ReferenceExperiment.UniqueID);
        Assert.Equal(new[] { copiedSource.Solution.Solutions[0].Data.UniqueID, "external-source" }, data.TandemSourceExperimentIds);
        Assert.Equal(originalSource.UniqueID, originalTarget.Solution.Solutions[0].Data.Attributes
            .Single(attribute => attribute.Key == AttributeKey.CompetitorResult).StringValue);
        var copiedReport = DataManager.Reports[1];
        Assert.Equal(copiedTarget.UniqueID, copiedReport.ResultIds[0]);
        Assert.Equal(data.UniqueID, copiedReport.StudyContext.Experiments[0].ExperimentId);
        Assert.Equal("external-support", copiedReport.SupportingExperimentIds[1]);
        Assert.Equal("saved-context-fingerprint", copiedReport.ApprovedInterpretation.AssessmentContextFingerprint);

        var restored = await Strict(await SaveCurrent("combined-competitor"));
        var reopenedTarget = restored.OfType<AnalysisResult>().Single(item => item.UniqueID == copiedTarget.UniqueID);
        var reopenedReference = reopenedTarget.Solution.Solutions[0].Data.Attributes.Single(attribute => attribute.Key == AttributeKey.CompetitorResult);
        Assert.Equal(copiedSource.UniqueID, reopenedReference.StringValue);
        Assert.Equal(copiedSource.Solution.UniqueID, reopenedReference.SourceSolutionId);
        Assert.Equal(-29500, reopenedReference.CapturedEnthalpy.Upper);
    }

    [Fact]
    public async Task CopyPreservesInterpretationProvenanceAndReevaluatesFreshness()
    {
        var result = Result(Prepared());
        var report = Report(result);
        var fingerprint = AnalysisInterpretationPromptBuilder.Build(
            AnalysisInterpretationPackageBuilder.Build(report, id => id == result.UniqueID ? result : null,
                id => result.Solution.Solutions.Select(member => member.Data).FirstOrDefault(data => data.UniqueID == id)))
            .InputFingerprint;
        report.ApproveInterpretation(new AnalysisInterpretationRecord
        {
            InterpretationMarkdown = "## Overall interpretation\nPreserve this saved interpretation.",
            InputFingerprint = fingerprint,
            EffectiveInputFingerprint = "original-provider-input",
            AssessmentContextFingerprint = "original-assessment-context",
            EvidenceFingerprintScheme = AnalysisInterpretationPromptBuilder.EvidenceFingerprintScheme,
            Provider = "saved-provider",
            Model = "saved-model",
        });
        var path = await Save("interpreted", new[] { result }, reports: new[] { report });
        await DataReader.ReadPathsAsync(new[] { path });
        var original = DataManager.Reports[0];
        prompt.Action = FtxtcDuplicateAction.ImportCopies;
        await DataReader.ReadPathsAsync(new[] { path });

        var copy = DataManager.Reports[1];
        Assert.Equal(fingerprint, copy.ApprovedInterpretation.InputFingerprint);
        Assert.Equal("original-provider-input", copy.ApprovedInterpretation.EffectiveInputFingerprint);
        Assert.Equal("original-assessment-context", copy.ApprovedInterpretation.AssessmentContextFingerprint);
        Assert.Equal(original.ApprovedInterpretation.ApprovedAtUtc, copy.ApprovedInterpretation.ApprovedAtUtc);
        Assert.Equal("saved-provider", copy.ApprovedInterpretation.Provider);
        Assert.Equal("saved-model", copy.ApprovedInterpretation.Model);
        Assert.Equal(AnalysisInterpretationFreshness.Stale, copy.InterpretationFreshness);

        using var stream = File.OpenRead(await SaveCurrent("combined-interpretations"));
        var restored = await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict);
        var reopened = restored.Reports.Single(item => item.UniqueID == copy.UniqueID);
        Assert.Equal(fingerprint, reopened.ApprovedInterpretation.InputFingerprint);
        Assert.Equal(AnalysisInterpretationFreshness.Stale, reopened.InterpretationFreshness);
    }

    [Fact]
    public async Task CopyPreservesIndependentAssessmentProfileAndBootstrapIdentitiesAfterRoundTrip()
    {
        var first = Prepared(bootstrap: true);
        var second = Prepared(bootstrap: true);
        var result = Result(first, second);
        result.SetMemberBindingAssessmentOverride(first.Solution.Guid, BindingAssessmentOutcome.NoBindingDetected);
        first.Solution.ProfileLikelihoodRun = LinkedThermodynamicUncertaintyTests.Run(
            LinkedThermodynamicUncertaintyTests.Coordinate(ParameterType.Enthalpy1, -30000, 1000, 1200, first.Data.UniqueID));
        result.Solution.ProfileLikelihoodRun = LinkedThermodynamicUncertaintyTests.Run(
            LinkedThermodynamicUncertaintyTests.Coordinate(ParameterType.Enthalpy1, -30000, 1000, 1200, first.Data.UniqueID));
        var path = await Save("independent", new[] { result });
        await DataReader.ReadPathsAsync(new[] { path });
        prompt.Action = FtxtcDuplicateAction.ImportCopies;
        await DataReader.ReadPathsAsync(new[] { path });

        var copy = DataManager.Results[1];
        var member = copy.Solution.Solutions[0];
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, copy.GetMemberBindingAssessment(member).EffectiveOutcome);
        Assert.Equal(member.Data.UniqueID, member.ProfileLikelihoodRun.Coordinates[0].Id.ExperimentIdentity);
        Assert.Equal(member.Data.UniqueID, copy.Solution.ProfileLikelihoodRun.Coordinates[0].Id.ExperimentIdentity);
        Assert.All(member.BootstrapSolutions, bootstrap => Assert.Equal(member.Data.UniqueID, bootstrap.Data.UniqueID));
        Assert.Equal(first.Solution.Parameters[ParameterType.Enthalpy1].Value, member.Parameters[ParameterType.Enthalpy1].Value);

        var reopened = (await Strict(await SaveCurrent("combined-independent"))).OfType<AnalysisResult>()
            .Single(item => item.UniqueID == copy.UniqueID);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, reopened.GetMemberBindingAssessment(reopened.Solution.Solutions[0]).EffectiveOutcome);
        Assert.All(reopened.Solution.Solutions[0].BootstrapSolutions,
            bootstrap => Assert.Equal(member.Data.UniqueID, bootstrap.Data.UniqueID));
    }

    [Fact]
    public async Task CopyPreservesSavedNullEvidenceFromRealProject()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "report_test.ftxtc");
        await DataReader.ReadPathsAsync(new[] { path });
        var originals = DataManager.Results.ToArray();
        prompt.Action = FtxtcDuplicateAction.ImportCopies;
        await DataReader.ReadPathsAsync(new[] { path });
        var copies = DataManager.Results.Skip(originals.Length).ToArray();
        var evidenceCount = 0;
        for (var i = 0; i < originals.Length; i++)
        {
            var original = originals[i];
            var copy = copies[i];
            for (var j = 0; j < copy.Solution.Solutions.Count; j++)
            {
                var member = copy.Solution.Solutions[j];
                var comparison = copy.GetMemberNullComparison(member);
                var before = original.GetMemberNullComparison(original.Solution.Solutions[j]);
                if (comparison == null) continue;
                evidenceCount++;
                Assert.Equal(before.DeltaAicc, comparison.DeltaAicc);
                Assert.Equal(before.Members.SelectMany(item => item.Points).Select(point => point.PredictedHeatJoules),
                    comparison.Members.SelectMany(item => item.Points).Select(point => point.PredictedHeatJoules));
                Assert.Contains(comparison.Members, item => item.ExperimentId == member.Data.UniqueID);
                Assert.Contains(comparison.NullSolutions, solution => solution.Data.UniqueID == member.Data.UniqueID);
                Assert.Equal(original.GetMemberBindingAssessment(original.Solution.Solutions[j]).EffectiveOutcome,
                    copy.GetMemberBindingAssessment(member).EffectiveOutcome);
            }
        }
        Assert.True(evidenceCount > 0);
        var restored = await Strict(await SaveCurrent("combined-null-evidence"));
        Assert.Equal(originals.Length * 2, restored.OfType<AnalysisResult>().Count());
        foreach (var copy in copies)
        {
            var reopened = restored.OfType<AnalysisResult>().Single(result => result.UniqueID == copy.UniqueID);
            Assert.Equal(copy.MemberAssessments.Select(member => member.Assessment.EffectiveOutcome),
                reopened.MemberAssessments.Select(member => member.Assessment.EffectiveOutcome));
        }
    }

    [Fact]
    public async Task CopyKeepsReferencesToSourcesOutsideIncomingPackage()
    {
        var model = Prepared("competitive");
        var attribute = ExperimentAttribute.CompetitorResultReference("external-result");
        attribute.SourceSolutionId = "external-fit";
        attribute.CapturedAffinity = new FloatWithError(2e-6);
        attribute.CapturedEnthalpy = new FloatWithError(-32000);
        model.Data.Attributes.Add(attribute);
        model.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue = true;
        var path = await Save("external-reference", new[] { Result(model) });
        await DataReader.ReadPathsAsync(new[] { path });
        prompt.Action = FtxtcDuplicateAction.ImportCopies;
        await DataReader.ReadPathsAsync(new[] { path });
        var copy = DataManager.Results[1];
        var live = copy.Solution.Solutions[0].Data.Attributes.Single(item => item.Key == AttributeKey.CompetitorResult);
        var saved = copy.ValiditySnapshot.Experiments[0].Attributes.Single(item => item.Key == AttributeKey.CompetitorResult);
        Assert.Equal("external-result", live.StringValue);
        Assert.Equal("external-fit", live.SourceSolutionId);
        Assert.Equal("external-result", saved.StringValue);
        Assert.Equal("external-fit", saved.SourceSolutionId);
    }

    [Fact]
    public void ReportDuplicateDetectionWorksWithoutExperimentRows()
    {
        var report = new AnalysisReport();
        DataManager.AddReport(report);
        var copy = report.CreateDetachedCopy();
        prompt.Action = FtxtcDuplicateAction.ImportCopies;
        var resolved = FtxtcImportResolver.Resolve("reports.ftxtc", Array.Empty<ITCDataContainer>(), new[] { copy });
        Assert.NotEqual(report.UniqueID, Assert.Single(resolved.Reports).UniqueID);
        Assert.Equal(1, Assert.Single(prompt.Summaries).ReportCount);
    }

    [Fact]
    public async Task FallbackSkipsAndLegacyImportsDoNotAsk()
    {
        var result = Result(Prepared());
        var path = await Save("fallback", new[] { result });
        await DataReader.ReadPathsAsync(new[] { path });
        PlatformServices.RegisterFtxtcDuplicatePromptService(null);
        await DataReader.ReadPathsAsync(new[] { path });
        Assert.Single(DataManager.Results);
        PlatformServices.RegisterFtxtcDuplicatePromptService(prompt);
        DataManager.Clear(DataClearMode.ResetSession);
        var legacy = Path.Combine(AppContext.BaseDirectory, "Fixtures", "one-set.ftitc");
        await DataReader.ReadPathsAsync(new[] { legacy, legacy });
        Assert.Empty(prompt.Summaries);
        Assert.True(DataManager.SourceItems.Count > DataManager.SourceItems.Select(item => item.UniqueID).Distinct().Count());
    }

    static Model Prepared(string id = "one-c100-v0.01", bool bootstrap = false)
    {
        var model = InjectionProcessingMethodTests.FittedModel(id, bootstrap);
        model.Data.UpdateSolution(model);
        return model;
    }

    static AnalysisResult Result(params Model[] models)
    {
        var global = new GlobalModel(models.ToList())
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = global }, models.Select(model => model.Solution).ToList(),
            SolverConvergence.FromFixedFit(1, 1e-12), reconstructBootstrap: false);
        global.Solution = solution;
        return new AnalysisResult(solution);
    }

    static AnalysisReport Report(AnalysisResult result)
    {
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        return report;
    }

    async Task<string> Save(string name, AnalysisResult[] results, ExperimentData[] additional = null, AnalysisReport[] reports = null)
    {
        var path = Path.Combine(directory, name + ".ftxtc");
        var experiments = results.SelectMany(result => result.Solution.Solutions.Select(member => member.Data))
            .Concat(additional ?? Array.Empty<ExperimentData>()).Distinct().ToArray();
        await FTXTCWriter.WriteFileAsync(path, experiments, results, reports: reports);
        return path;
    }

    async Task<string> SaveCurrent(string name)
    {
        var path = Path.Combine(directory, name + ".ftxtc");
        await FTXTCWriter.WriteFileAsync(path, DataManager.Data, DataManager.Results, DataManager.SourceItems, DataManager.Reports);
        return path;
    }

    static async Task<ITCDataContainer[]> Strict(string path)
    {
        using var stream = File.OpenRead(path);
        return await FTXTCReader.ReadStream(stream);
    }

    internal sealed class RecordingPrompt(FtxtcDuplicateAction action) : IFtxtcDuplicatePromptService
    {
        internal FtxtcDuplicateAction Action { get; set; } = action;
        internal List<FtxtcDuplicateSummary> Summaries { get; } = new();
        public FtxtcDuplicateAction ChooseAction(FtxtcDuplicateSummary summary)
        {
            Summaries.Add(summary);
            return Action;
        }
    }
}
