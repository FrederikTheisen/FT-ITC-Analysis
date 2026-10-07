using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(ProjectWriterSaveSelectedCollectionDefinition.Name)]
public sealed class DocumentDirtyTrackerTests : IDisposable
{
    public DocumentDirtyTrackerTests()
    {
        DocumentDirtyTracker.Initialize();
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.MarkClean();
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.MarkClean();
    }

    [Fact]
    public void RepeatedContentEditsAndExplicitDirtyMarksAdvanceRevision()
    {
        var report = new AnalysisReport();
        DataManager.AddReport(report);
        DocumentDirtyTracker.MarkClean();
        var initial = DocumentDirtyTracker.CaptureSaveStamp();

        report.Name = "First edit";
        var firstEdit = DocumentDirtyTracker.CaptureSaveStamp();
        report.Name = "Second edit";
        var secondEdit = DocumentDirtyTracker.CaptureSaveStamp();
        DocumentDirtyTracker.MarkDirty();
        var explicitMark = DocumentDirtyTracker.CaptureSaveStamp();

        Assert.True(firstEdit.Revision > initial.Revision);
        Assert.True(secondEdit.Revision > firstEdit.Revision);
        Assert.True(explicitMark.Revision > secondEdit.Revision);
        Assert.True(DocumentDirtyTracker.IsDirty);
    }

    [Fact]
    public void SuspendedChangesAdvanceRevisionWithoutSettingDirtyState()
    {
        var report = new AnalysisReport();
        DataManager.AddReport(report);
        DocumentDirtyTracker.MarkClean();
        var initial = DocumentDirtyTracker.CaptureSaveStamp();

        using (DocumentDirtyTracker.Suspend())
        {
            report.Name = "Changed under suspension";
            DocumentDirtyTracker.MarkDirty();
        }

        var changed = DocumentDirtyTracker.CaptureSaveStamp();
        Assert.True(changed.Revision > initial.Revision);
        Assert.False(DocumentDirtyTracker.IsDirty);
    }

    [Fact]
    public void StaleSaveStampMarksSameDocumentDirtyAfterSuspensionEnds()
    {
        var report = new AnalysisReport();
        DataManager.AddReport(report);
        DocumentDirtyTracker.MarkClean();
        var stamp = DocumentDirtyTracker.CaptureSaveStamp();

        using (DocumentDirtyTracker.Suspend())
            report.Name = "Edited while saving";

        Assert.False(DocumentDirtyTracker.IsDirty);
        Assert.False(DocumentDirtyTracker.TryMarkClean(stamp));
        Assert.True(DocumentDirtyTracker.IsDirty);
    }

    [Fact]
    public void StaleSaveDuringSuspensionDefersDirtyNotificationUntilScopeEnds()
    {
        var report = new AnalysisReport();
        DataManager.AddReport(report);
        DocumentDirtyTracker.MarkClean();
        var stamp = DocumentDirtyTracker.CaptureSaveStamp();

        using (DocumentDirtyTracker.Suspend())
        {
            report.Name = "Edited while saving";
            Assert.False(DocumentDirtyTracker.TryMarkClean(stamp));
            Assert.False(DocumentDirtyTracker.IsDirty);
        }

        Assert.True(DocumentDirtyTracker.IsDirty);
    }

    [Fact]
    public void PendingStaleSaveDirtyStateIsDiscardedWhenDocumentChanges()
    {
        var report = new AnalysisReport();
        DataManager.AddReport(report);
        DocumentDirtyTracker.MarkClean();
        var stamp = DocumentDirtyTracker.CaptureSaveStamp();

        using (DocumentDirtyTracker.Suspend())
        {
            report.Name = "Edited while saving";
            Assert.False(DocumentDirtyTracker.TryMarkClean(stamp));
            DataManager.Clear(DataClearMode.ResetSession);
        }

        Assert.False(DocumentDirtyTracker.IsCurrentDocument(stamp));
        Assert.False(DocumentDirtyTracker.IsDirty);
    }

    [Fact]
    public void CollectionInclusionAndReportNotificationsAdvanceRevision()
    {
        var initial = DocumentDirtyTracker.CaptureSaveStamp();
        DataManager.AddData(new ITCDataContainer());
        var afterAdd = DocumentDirtyTracker.CaptureSaveStamp();
        Assert.True(afterAdd.Revision > initial.Revision);

        DataManager.InvokeDataInclusionDidChange();
        var afterInclusion = DocumentDirtyTracker.CaptureSaveStamp();
        Assert.True(afterInclusion.Revision > afterAdd.Revision);

        DataManager.AddReport(new AnalysisReport());
        var afterReport = DocumentDirtyTracker.CaptureSaveStamp();
        Assert.True(afterReport.Revision > afterInclusion.Revision);
        Assert.True(DocumentDirtyTracker.IsDirty);
    }

    [Fact]
    public void MoveAndRemovalNotificationsAdvanceRevisionAndRemovedItemsAreUnsubscribed()
    {
        var first = new ITCDataContainer { Name = "First" };
        var second = new ITCDataContainer { Name = "Second" };
        var third = new ITCDataContainer { Name = "Third" };
        DataManager.AddData(new[] { first, second, third });
        DocumentDirtyTracker.MarkClean();

        var beforeMove = DocumentDirtyTracker.CaptureSaveStamp();
        DataManager.MoveSourceItem(2, 0);
        var afterMove = DocumentDirtyTracker.CaptureSaveStamp();
        Assert.True(afterMove.Revision > beforeMove.Revision);

        var removed = DataManager.SourceItems[0];
        var beforeRemoval = DocumentDirtyTracker.CaptureSaveStamp();
        DataManager.RemoveSourceItemAt(0);
        var afterRemoval = DocumentDirtyTracker.CaptureSaveStamp();
        Assert.True(afterRemoval.Revision > beforeRemoval.Revision);

        var beforeDetachedEdit = DocumentDirtyTracker.CaptureSaveStamp();
        removed.Name = "Detached edit";
        Assert.Equal(beforeDetachedEdit.Revision, DocumentDirtyTracker.CaptureSaveStamp().Revision);

        var report = new AnalysisReport();
        DataManager.AddReport(report);
        var beforeReportRemoval = DocumentDirtyTracker.CaptureSaveStamp();
        Assert.True(DataManager.RemoveReport(report));
        var afterReportRemoval = DocumentDirtyTracker.CaptureSaveStamp();
        Assert.True(afterReportRemoval.Revision > beforeReportRemoval.Revision);
        report.Name = "Detached report edit";
        Assert.Equal(afterReportRemoval.Revision, DocumentDirtyTracker.CaptureSaveStamp().Revision);
    }

    [Fact]
    public async Task OpeningProjectChangesIdentityButAppendingItOnlyAdvancesRevision()
    {
        var path = TemporaryProjectPath();
        try
        {
            await CreateProjectFixture(path);
            var beforeOpen = DocumentDirtyTracker.CaptureSaveStamp();
            var opened = await DataReader.ReadPathsAsync(new[] { path });
            Assert.True(opened.OpenedCleanProject);
            var afterOpen = DocumentDirtyTracker.CaptureSaveStamp();
            Assert.False(DocumentDirtyTracker.IsCurrentDocument(beforeOpen));

            await DataReader.ReadPathsAsync(new[] { path });
            var afterAppend = DocumentDirtyTracker.CaptureSaveStamp();
            Assert.True(DocumentDirtyTracker.IsCurrentDocument(afterOpen));
            Assert.True(afterAppend.Revision > afterOpen.Revision);
        }
        finally
        {
            FTITCFormat.CurrentAccessedAppDocumentPath = "";
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task DetachedProjectReadDoesNotChangeActiveDocumentIdentity()
    {
        var path = TemporaryProjectPath();
        try
        {
            await CreateProjectFixture(path);
            var before = DocumentDirtyTracker.CaptureSaveStamp();
            using var stream = File.OpenRead(path);
            var containers = await FTXTCReader.ReadStream(stream);
            Assert.NotEmpty(containers);
            Assert.True(DocumentDirtyTracker.IsCurrentDocument(before));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SuccessfulRecoveryChangesIdentityAndSolutionReattachmentDoesNot()
    {
        var path = TemporaryProjectPath();
        try
        {
            await CreateProjectFixture(path);
            var beforeRecovery = DocumentDirtyTracker.CaptureSaveStamp();
            Assert.True(await DataReader.ReadRecoveryFileAsync(path));
            var afterRecovery = DocumentDirtyTracker.CaptureSaveStamp();
            Assert.False(DocumentDirtyTracker.IsCurrentDocument(beforeRecovery));

            var result = DataManager.Results.First();
            DataManager.LoadResultSolutionsToExperiments(result, markDocumentDirty: false);
            Assert.True(DocumentDirtyTracker.IsCurrentDocument(afterRecovery));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RestoreScopeSuppressesRevisionButClearChangesDocumentIdentity()
    {
        var initial = DocumentDirtyTracker.CaptureSaveStamp();
        using (DocumentDirtyTracker.RestoreDocument())
        {
            DocumentDirtyTracker.MarkDirty();
            DataManager.InvokeDataDidChange();
        }

        var afterRestore = DocumentDirtyTracker.CaptureSaveStamp();
        Assert.Equal(initial.Revision, afterRestore.Revision);
        Assert.True(DocumentDirtyTracker.IsCurrentDocument(afterRestore));

        DataManager.Clear(DataClearMode.ResetSession);
        var afterClear = DocumentDirtyTracker.CaptureSaveStamp();
        Assert.False(DocumentDirtyTracker.IsCurrentDocument(afterRestore));
        Assert.True(afterClear.Revision >= afterRestore.Revision);
    }

    static string TemporaryProjectPath() => Path.Combine(Path.GetTempPath(), "DocumentDirtyTracker-" + Guid.NewGuid().ToString("N") + ".ftxtc");

    static async Task CreateProjectFixture(string path)
    {
        using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "one-set.ftitc"));
        var containers = await FTITCReader.ReadStream(source);
        await FTXTCWriter.WriteFileAsync(path, containers.OfType<ExperimentData>(), containers.OfType<AnalysisResult>());
    }
}
