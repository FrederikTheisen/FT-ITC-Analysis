// Run with run-analysis-null-graph-tests.sh; uses the production open queue and real AppKit alerts.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AppKit;
using Foundation;
using AnalysisITC;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.UI.MacOS;

static class ProjectOpenReportOnlyTests
{
    static string directory;
    static string incomingPath;
    static AnalysisReport incomingReport;
    static TestDocumentController documents;
    static string repository;

    static int Main(string[] args)
    {
        NSApplication.Init();
        // Keep recent-file registration local to the harness, without invoking the OS recent-items service.
        documents = new TestDocumentController();
        repository = args[0];
        SynchronizationContext.SetSynchronizationContext(new NativeUiContext());
        DocumentDirtyTracker.Initialize();
        directory = Path.Combine(Path.GetTempPath(), "ft-itc-project-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var traceability = AppSettings.TraceabilityModeEnabled;
        try
        {
            Check(NSDocumentController.SharedDocumentController.Handle == documents.Handle, "Test recent-document controller was not installed");
            AppSettings.TraceabilityModeEnabled = false;
            Wait(MacDataReader.BeginStartupAsync(() => Task.FromResult(true), () => Task.CompletedTask));
            incomingPath = Path.Combine(directory, "incoming.ftxtc");
            incomingReport = Report("Incoming report", "Incoming saved text");
            Wait(FTXTCWriter.WriteFileAsync(incomingPath, new[] { new ExperimentData("incoming.itc") },
                reports: new[] { incomingReport }));

            CheckCancel(false);
            CheckCancel(true);
            CheckReplace(false);
            CheckReplace(true);
            CheckAppend();
            CheckEmptyOpen();
            CheckRawImport();
            Console.WriteLine("PASS: report-only native project open: Cancel, Replace/Cancel, Replace/Clear All, Replace/Save, Append; destinations, dirty state, strict saved/reopened reports, empty open and raw import");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally
        {
            DataManager.Clear(DataClearMode.ResetSession);
            DocumentDirtyTracker.MarkClean();
            AppSettings.TraceabilityModeEnabled = traceability;
            Directory.Delete(directory, true);
        }
    }

    static AnalysisReport Report(string name, string text) => new AnalysisReport { Name = name, AuthorComments = text };

    static AnalysisReport PrepareReportOnly(string name)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var report = Report("Retained report", "Retained saved text λ");
        DataManager.AddData(new[] { new ExperimentData("removed.itc") });
        DataManager.AddReport(report);
        FTITCFormat.CurrentAccessedAppDocumentPath = Path.Combine(directory, name + ".ftxtc");
        Check(Wait(ProjectWriter.SaveWithPathAsync()), "Initial project save failed");
        DataManager.RemoveSourceItemAt(0);
        Check(DataManager.SourceItems.Count == 0 && DataManager.Reports.Single() == report,
            "Removing the final source item did not retain the report");
        Check(DocumentDirtyTracker.IsDirty, "Source removal must leave unsaved changes");
        return report;
    }

    static void CheckCancel(bool duringReplace)
    {
        var report = PrepareReportOnly(duringReplace ? "cancel-replace" : "cancel-open");
        var path = FTITCFormat.CurrentAccessedAppDocumentPath;
        var document = DocumentDirtyTracker.CurrentDocumentId;
        var savedBytes = File.ReadAllBytes(path);
        Open(incomingPath, duringReplace ? new[] { "Replace Data", "Cancel" } : new[] { "Cancel" });
        Check(DataManager.SourceItems.Count == 0 && DataManager.Reports.Single() == report,
            "Cancellation changed the report-only document");
        Check(FTITCFormat.CurrentAccessedAppDocumentPath == path && DocumentDirtyTracker.IsDirty
            && DocumentDirtyTracker.CurrentDocumentId == document, "Cancellation changed destination, dirty state or document identity");
        Check(File.ReadAllBytes(path).SequenceEqual(savedBytes), "Cancellation wrote the current project");
        SaveAndCheckReports(path, report);
    }

    static void CheckReplace(bool saveFirst)
    {
        var report = PrepareReportOnly(saveFirst ? "replace-save" : "replace-discard");
        var oldPath = FTITCFormat.CurrentAccessedAppDocumentPath;
        var oldBytes = File.ReadAllBytes(oldPath);
        var document = DocumentDirtyTracker.CurrentDocumentId;
        Open(incomingPath, "Replace Data", saveFirst ? "Save" : "Clear All");
        Check(DataManager.SourceItems.Count == 1 && DataManager.Data.Single().FileName == "incoming.itc",
            "Replace did not load only the incoming experiment");
        CheckReports(DataManager.Reports, incomingReport);
        Check(FTITCFormat.CurrentAccessedAppDocumentPath == incomingPath && !DocumentDirtyTracker.IsDirty
            && DocumentDirtyTracker.CurrentDocumentId != document, "Replace did not open the incoming project cleanly at its path");
        if (saveFirst)
        {
            using (var stream = File.OpenRead(oldPath))
            {
                var saved = Wait(FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict));
                Check(saved.Issues.Count == 0 && saved.Containers.Length == 0, "Replace/Save did not save source deletion");
                CheckReports(saved.Reports, report);
            }
        }
        else Check(File.ReadAllBytes(oldPath).SequenceEqual(oldBytes), "Replace/Discard wrote the old project");
        SaveAndCheckReports(incomingPath, incomingReport);
    }

    static void CheckAppend()
    {
        var report = PrepareReportOnly("append");
        var oldPath = FTITCFormat.CurrentAccessedAppDocumentPath;
        var oldBytes = File.ReadAllBytes(oldPath);
        var incomingBytes = File.ReadAllBytes(incomingPath);
        var document = DocumentDirtyTracker.CurrentDocumentId;
        Open(incomingPath, "Append");
        Check(DataManager.SourceItems.Count == 1, "Append did not load the incoming experiment");
        CheckReports(DataManager.Reports, report, incomingReport);
        Check(FTITCFormat.CurrentAccessedAppDocumentPath == oldPath && DocumentDirtyTracker.IsDirty
            && DocumentDirtyTracker.CurrentDocumentId == document, "Append did not preserve the destination, dirty state and document identity");
        Check(File.ReadAllBytes(oldPath).SequenceEqual(oldBytes), "Append wrote the original project");
        SaveAndCheckReports(oldPath, report, incomingReport);
        Check(File.ReadAllBytes(incomingPath).SequenceEqual(incomingBytes), "Save overwrote the appended source project");
    }

    static void CheckEmptyOpen()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.MarkClean();
        Open(incomingPath);
        CheckReports(DataManager.Reports, incomingReport);
        Check(DataManager.SourceItems.Count == 1 && !DocumentDirtyTracker.IsDirty
            && FTITCFormat.CurrentAccessedAppDocumentPath == incomingPath, "Empty-document opening changed");
    }

    static void CheckRawImport()
    {
        var report = PrepareReportOnly("raw-import");
        var path = FTITCFormat.CurrentAccessedAppDocumentPath;
        Open(Path.Combine(repository, "AnalysisITC.Tests", "ScientificValidation", "RawPipeline", "analytic-one-site.itc"));
        Check(DataManager.SourceItems.Count == 1 && DataManager.Data.Single().HasThermogram && DataManager.Reports.Single() == report
            && FTITCFormat.CurrentAccessedAppDocumentPath == path && DocumentDirtyTracker.IsDirty,
            "Non-project opening changed the retained document");
    }

    static void SaveAndCheckReports(string path, params AnalysisReport[] expected)
    {
        Check(Wait(ProjectWriter.SaveWithPathAsync()) && !DocumentDirtyTracker.IsDirty, "Project save failed or stayed dirty");
        using (var stream = File.OpenRead(path))
        {
            var reopened = Wait(FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict));
            Check(reopened.Issues.Count == 0, "Strict reopening reported recovery issues");
            CheckReports(reopened.Reports, expected);
        }
    }

    static void CheckReports(IEnumerable<AnalysisReport> actual, params AnalysisReport[] expected)
    {
        Check(actual.Select(report => report.UniqueID).OrderBy(id => id)
            .SequenceEqual(expected.Select(report => report.UniqueID).OrderBy(id => id)), "Unexpected report set");
        foreach (var report in expected)
            Check(actual.Single(item => item.UniqueID == report.UniqueID).AuthorComments == report.AuthorComments,
                "Saved report text changed");
    }

    static void Open(string path, params string[] choices)
    {
        Exception failure = null;
        var step = 0;
        using (var input = NSTimer.CreateRepeatingTimer(0.05, timer =>
        {
            var window = NSApplication.SharedApplication.ModalWindow;
            if (window == null) return;
            try
            {
                var buttons = Buttons(window.ContentView).ToArray();
                Check(step < choices.Length, "Unexpected native prompt: " + string.Join(", ", buttons.Select(button => button.Title)));
                var expected = step == 0 ? new[] { "Replace Data", "Append", "Cancel" } : new[] { "Save", "Cancel", "Clear All" };
                Check(buttons.Select(button => button.Title).OrderBy(title => title).SequenceEqual(expected.OrderBy(title => title)),
                    "Unexpected project-open/save prompt buttons");
                var choice = choices[step++];
                buttons.Single(button => button.Title == choice).PerformClick(window);
            }
            catch (Exception error) { failure = error; NSApplication.SharedApplication.AbortModal(); }
        }))
        using (var timeout = NSTimer.CreateTimer(10, timer =>
        {
            failure = new Exception("Native open timed out");
            NSApplication.SharedApplication.AbortModal();
        }))
        {
            NSRunLoop.Main.AddTimer(input, NSRunLoopMode.ModalPanel);
            NSRunLoop.Main.AddTimer(input, NSRunLoopMode.Default);
            NSRunLoop.Main.AddTimer(timeout, NSRunLoopMode.ModalPanel);
            NSRunLoop.Main.AddTimer(timeout, NSRunLoopMode.Default);
            try
            {
                using (var url = NSUrl.FromFilename(path)) Wait(MacDataReader.ReadAsync(new[] { url }));
                if (failure != null) throw failure;
                Check(step == choices.Length, "Expected native project-open/save prompt was skipped");
            }
            finally { input.Invalidate(); timeout.Invalidate(); }
        }
    }

    static IEnumerable<NSButton> Buttons(NSView view)
    {
        var button = view as NSButton;
        if (button != null) yield return button;
        foreach (var child in view.Subviews)
            foreach (var nested in Buttons(child)) yield return nested;
    }

    static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.005));
        Check(task.IsCompleted, "Native asynchronous operation did not complete");
        task.GetAwaiter().GetResult();
    }

    static T Wait<T>(Task<T> task) { Wait((Task)task); return task.GetAwaiter().GetResult(); }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    sealed class NativeUiContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object state) =>
            NSApplication.SharedApplication.BeginInvokeOnMainThread(() => callback(state));
    }

    sealed class TestDocumentController : NSDocumentController
    {
        public override void NoteNewRecentDocumentURL(NSUrl url) { }
    }
}
