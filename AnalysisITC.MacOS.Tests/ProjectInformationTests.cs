// Run with run-analysis-null-graph-tests.sh; exercises the real AppKit sheet and report controls.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;

static class ProjectInformationTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Type InfoType = typeof(MainWindowController).Assembly.GetType("AnalysisITC.ProjectInformationWindowController");
    static readonly Type ReportType = typeof(MainWindowController).Assembly.GetType("AnalysisITC.AnalysisReportViewController");
    static readonly Type ContentType = typeof(MainWindowController).Assembly.GetType("AnalysisITC.DetailsSheetContentView");
    static readonly Dictionary<string, double[]> referenceBackgrounds = new Dictionary<string, double[]>();
    static int Main(string[] args)
    {
        NSApplication.Init();
        DocumentDirtyTracker.Initialize();
        var automatic = AppSettings.AutoSelectReportReferenceExperiments;
        try
        {
            CheckResponsiveLists();
            CheckEmptyAndMissing();
            CheckRenameDeleteAndDetails();
            CheckReportSelectionAndGuards();
            CheckSaveEligibility();
            Console.WriteLine("PASS: native Project Information counts, missing reports, rename/delete confirmation, selectable details/copy, exact report variants, supporting persistence, deletion/document guards and report-only saving");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally { DataManager.Clear(DataClearMode.ResetSession); AppSettings.AutoSelectReportReferenceExperiments = automatic; }
    }

    static void CheckResponsiveLists()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var experiment = Experiment("Long experiment name λ β Æ 中文 " + new string('W', 160));
        var result = Result(experiment);
        result.Name = "Long result name λ β Æ 中文 " + new string('W', 160);
        DataManager.AddData(new ITCDataContainer[] { experiment, result });
        DataManager.AddReport(Report(result, "Long report name λ β Æ 中文 " + new string('W', 160)));
        using (var info = (NSWindowController)Activator.CreateInstance(InfoType))
        using (var parent = Owner())
        {
            Show(info, parent, null);
            CheckListWidths(info);
            info.Window.SetContentSize(info.Window.ContentMinSize); Pump();
            CheckListWidths(info);
            info.Window.SetContentSize(new CGSize(1080, 800)); Pump();
            CheckListWidths(info);
            Invoke(info, "CloseSheet"); Pump();
        }
    }

    static void CheckEmptyAndMissing()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        using (var info = (NSWindowController)Activator.CreateInstance(InfoType))
        {
            var tabs = (NSSegmentedControl)Field(info, "tabs");
            Check(((NSTextField)Field(info, "contentSummary")).StringValue == ProjectInformationPresentation.ContentSummary(0, 0, 0),
                "Empty project counts differ from its saved content");
            Check(tabs.GetLabel(0) == ProjectInformationPresentation.ExperimentsLabel
                && tabs.GetLabel(1) == ProjectInformationPresentation.AnalysisResultsLabel
                && tabs.GetLabel(2) == ProjectInformationPresentation.SavedReportsLabel,
                "Navigator tabs repeat project counts");
            Check(!((NSTextField)Field(info, "emptyLabel")).Hidden, "Empty project gives no list state");
        }
        var report = new AnalysisReport(); report.SetResultIds(new[] { "missing-a", "missing-b" });
        report.AuthorComments = "Saved remarks λ β\nA second line";
        DataManager.AddReport(report);
        using (var info = (NSWindowController)Activator.CreateInstance(InfoType))
        using (var parent = Owner())
        {
            Show(info, parent, null);
            SelectReports(info, 0);
            Check(!((NSButton)Field(info, "open")).Enabled && !((NSButton)Field(info, "rename")).Enabled,
                "Missing results permit opening or renaming");
            Check(((NSButton)Field(info, "details")).Enabled && ((NSButton)Field(info, "delete")).Enabled,
                "Missing-result report cannot be inspected or removed");
            var row = (string)InfoType.GetMethod("RowText", Instance).Invoke(info, new object[] { report });
            Check(row.Contains("2 of 2 results missing"), "Missing result status lost its count");
            Check(OverviewText(info).Contains("2 of 2 results missing") && OverviewText(info).Contains(ProjectInformationPresentation.NoInterpretationSaved),
                "Compact overview omitted missing-result status or interpretation availability");
            ((NSButton)Field(info, "details")).PerformClick(info); Pump();
            var detail = (NSWindow)Field(info, "childSheet");
            Check(detail != null && detail.Title == ProjectInformationPresentation.DetailsWindowTitle, "Details sheet not shown");
            var text = Descendants(detail.ContentView).OfType<NSTextView>().Single();
            Check(text.Selectable && !text.Editable && text.String.Contains("Missing result") && text.String.Contains("Saved remarks λ β"),
                "Missing report details lost selectable saved text");
            Descendants(detail.ContentView).OfType<NSButton>().Single(button => button.Title == "Copy All").PerformClick(info);
            Check(NSPasteboard.GeneralPasteboard.GetStringForType("NSStringPboardType").Contains("Saved remarks λ β"), "Copy All omitted saved remarks");
            info.Window.EndSheet(detail); Pump();
            Invoke(info, "CloseSheet"); Pump();
        }
    }

    static void CheckRenameDeleteAndDetails()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var first = Experiment("first"); first.Include = false;
        var second = Experiment("second");
        var result = Result(first); result.Name = "OneSetOfSites (3)";
        DataManager.AddData(new ITCDataContainer[] { first, second, result });
        var report = Report(result, "Original report");
        report.AuthorComments = "Three independent titrations agree with the fitted model.\nReview the buffer-reference series before exporting.";
        report.UpdatePresentationSettings(new AnalysisReportOptions { Title = "Binding characterization", AutomaticTitle = false,
            DocumentLabel = "Ligand titration series", ReportId = "Study-2026-10" });
        report.UpdateStudyContext(new AnalysisStudyContext { ScientificQuestion = "What does the binding mean?", AdditionalNotes = "Notes for the saved study" });
        report.UpdateApprovedInterpretationText("The three titrations show a consistent binding response.");
        DataManager.AddReport(report);
        var reportNames = new[] { "Control series report", "Binding comparison", "Affinity by temperature", "Replicate assessment",
            "Buffer-reference review", "Protein–ligand analysis" };
        for (var i = 0; i < 24; i++) DataManager.AddReport(Report(result, i < reportNames.Length ? reportNames[i] : "Study report " + (i + 1)));
        using (var info = (NSWindowController)Activator.CreateInstance(InfoType))
        using (var parent = Owner())
        {
            Show(info, parent, null);
            var tabs = (NSSegmentedControl)Field(info, "tabs");
            var summary = ((NSTextField)Field(info, "contentSummary")).StringValue;
            Check(summary.Contains("2 experiments") && summary.Contains("1 result") && summary.Contains("25 reports"),
                "Header counts omitted inactive data or included result-owned experiment copies");
            for (var index = 0; index < 2; index++)
            {
                tabs.SelectedSegment = index; Invoke(info, "Refresh");
                Check(((NSView)Field(info, "reportOverview")).Hidden,
                    "Report actions or overview appear outside the Saved reports tab");
            }
            SelectReports(info, 0);
            Check(!((NSView)Field(info, "reportOverview")).Hidden && OverviewText(info).Contains("OneSetOfSites (3)")
                && OverviewText(info).Contains("1 fitted · 0 supporting selected") && OverviewText(info).Contains("Ligand titration series")
                && OverviewText(info).Contains(ProjectInformationPresentation.InterpretationPresent),
                "Compact overview omitted the selected report's saved content");
            var table = (NSTableView)Field(info, "table");
            info.Window.ContentView.LayoutSubtreeIfNeeded();
            Check(table.EnclosingScrollView.HasVerticalScroller && table.RowCount == 25, "Saved report list cannot scroll");
            foreach (var name in new[] { "open", "rename", "delete", "details" })
            {
                var button = (NSButton)Field(info, name);
                Check(button.Enabled && button.Frame.Width > 10 && !string.IsNullOrWhiteSpace(button.ToolTip), "Report action is unreachable or lacks help: " + name);
            }
            SaveView(info.Window.ContentView, "macos-project-information");
            CheckAppearances(info.Window, "macos-project-information");
            var defaultSize = info.Window.ContentView.Bounds.Size;
            DataManager.Reports[1].Name = "Long study name · λ β Æ 中文 " + new string('W', 160);
            Invoke(info, "Refresh");
            info.Window.SetContentSize(info.Window.ContentMinSize); Pump();
            CheckFooterReachable(info.Window.ContentView);
            SaveView(info.Window.ContentView, "macos-project-information-minimum");
            CheckAppearances(info.Window, "macos-project-information-minimum");
            var resultName = result.Name;
            result.Name = string.Join(" ", Enumerable.Repeat("Long result name λ β Æ 中文", 16));
            Invoke(info, "Refresh"); Pump();
            info.Window.ContentView.LayoutSubtreeIfNeeded();
            var overviewScroll = Descendants((NSView)Field(info, "reportOverview")).OfType<NSScrollView>().Single();
            Check(((NSView)overviewScroll.DocumentView).Frame.Height > overviewScroll.ContentView.Bounds.Height,
                "Long overview result names have no vertical scroll range");
            CheckFooterReachable(info.Window.ContentView);
            SaveView(info.Window.ContentView, "macos-project-information-overview-minimum");
            result.Name = resultName; Invoke(info, "Refresh");
            info.Window.SetContentSize(defaultSize); Pump();
            var before = report.LastEditedUtc;
            ((NSButton)Field(info, "rename")).PerformClick(info); Pump();
            var renameSheet = (NSWindow)Field(info, "childSheet");
            EditableFields(renameSheet).Single().StringValue = "Cancelled edit";
            info.Window.EndSheet(renameSheet, (NSModalResponse)1001); Pump();
            Check(report.Name == "Original report" && report.LastEditedUtc == before, "Cancelled rename edited report");
            ((NSButton)Field(info, "rename")).PerformClick(info); Pump();
            renameSheet = (NSWindow)Field(info, "childSheet");
            EditableFields(renameSheet).Single().StringValue = "";
            info.Window.EndSheet(renameSheet, (NSModalResponse)1000); Pump();
            Check(report.Name == "" && AnalysisReportPresentation.DisplayName(report, _ => result) == "OneSetOfSites (3) report",
                "Clearing a name did not restore content naming");
            Check(report.LastEditedUtc > before, "Rename did not update its last edit date");
            ((NSButton)Field(info, "details")).PerformClick(info); Pump();
            var detail = (NSWindow)Field(info, "childSheet");
            var text = Descendants(detail.ContentView).OfType<NSTextView>().Single();
            detail.ContentView.LayoutSubtreeIfNeeded();
            Check(text.EnclosingScrollView.HasVerticalScroller && text.String.Contains("What does the binding mean?"),
                "Details omitted study context or scrolling");
            SaveView(detail.ContentView, "macos-report-details");
            CheckAppearances(detail, "macos-report-details");
            info.Window.EndSheet(detail); Pump();
            report.AuthorComments = string.Join("\n", Enumerable.Repeat("Scientific remarks with mixed scripts λ β Æ 中文", 80));
            ((NSButton)Field(info, "details")).PerformClick(info); Pump();
            detail = (NSWindow)Field(info, "childSheet");
            detail.SetContentSize(detail.ContentMinSize); Pump();
            CheckFooterReachable(detail.ContentView);
            text = Descendants(detail.ContentView).OfType<NSTextView>().Single();
            text.ScrollRangeToVisible(new NSRange(text.String.IndexOf("Scientific remarks", StringComparison.Ordinal), 1)); Pump();
            SaveView(detail.ContentView, "macos-report-details-minimum");
            CheckAppearances(detail, "macos-report-details-minimum");
            info.Window.EndSheet(detail); Pump();
            ((NSButton)Field(info, "delete")).PerformClick(info); Pump();
            var deleteSheet = (NSWindow)Field(info, "childSheet");
            info.Window.EndSheet(deleteSheet, (NSModalResponse)1001); Pump();
            Check(DataManager.Reports.Contains(report), "Cancelled deletion removed report");
            ((NSButton)Field(info, "delete")).PerformClick(info); Pump();
            deleteSheet = (NSWindow)Field(info, "childSheet");
            info.Window.EndSheet(deleteSheet, (NSModalResponse)1000); Pump();
            Check(!DataManager.Reports.Contains(report) && DataManager.Data.Count == 2 && DataManager.Results.Count == 1,
                "Confirmed deletion did not preserve data/results");
            Check(DocumentDirtyTracker.IsDirty, "Confirmed deletion was not marked for saving");
            Invoke(info, "CloseSheet"); Pump();
        }
    }

    static void CheckReportSelectionAndGuards()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        AppSettings.AutoSelectReportReferenceExperiments = true;
        var data = Experiment("selection-a"); var support = Experiment("support");
        var first = Result(data); var second = Result(Experiment("selection-b"));
        DataManager.AddData(new ITCDataContainer[] { data, support, first, second });
        var older = Report(first, "Older variant"); older.SetDate(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var latest = Report(first, "Latest variant"); latest.SetDate(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        latest.SetSupportingExperimentIds(new[] { support.UniqueID });
        var other = Report(second, "Other result");
        other.UpdateApprovedInterpretationText("Second result interpretation");
        DataManager.AddReports(new[] { older, latest, other });
        using (var ordinary = (NSViewController)Activator.CreateInstance(ReportType, new object[] { first }))
        {
            var view = ordinary.View;
            Check(ReferenceEquals(Field(ordinary, "report"), latest), "Ordinary opening ignored the newest matching saved report");
            Check(((List<ExperimentData>)Field(ordinary, "selectedSupportingExperiments")).Contains(support), "Opening lost saved supporting selections");
            Apply(ordinary, new[] { first }, new ExperimentData[0]);
            Check(ReferenceEquals(Field(ordinary, "report"), latest) && latest.SupportingExperimentIds.Count == 0,
                "Removing supporting experiments created another report");
            Apply(ordinary, new[] { second }, new[] { support });
            Check(ReferenceEquals(Field(ordinary, "report"), other) && other.SupportingExperimentIds.Count == 0,
                "Changed result selection used same-Apply supporting edits");
            var editor = (NSTextView)Field(ordinary, "interpretationText");
            Check(editor.String.Contains("Second result interpretation"), "Result selection lost its own approved text");
            Apply(ordinary, new[] { first }, new[] { support });
            Check(((NSTextView)Field(ordinary, "interpretationText")).String == "", "Interpretation carried across result sets");
        }
        using (var reopened = (NSViewController)Activator.CreateInstance(ReportType, new object[] { first }))
        {
            var view = reopened.View;
            Check(((List<ExperimentData>)Field(reopened, "selectedSupportingExperiments")).Count == 0,
                "Reopening added removed automatic supporting selections");
        }
        var before = older.LastEditedUtc;
        using (var exact = (NSViewController)Activator.CreateInstance(ReportType, new object[] { older }))
        {
            var view = exact.View;
            Check(ReferenceEquals(Field(exact, "report"), older) && older.LastEditedUtc == before,
                "Exact opening redirected to newest variant or marked it edited");
            DataManager.RemoveReport(older);
            SetText((NSTextView)Field(exact, "interpretationText"), "Must not resurrect deleted report");
            Invoke(exact, "CommitInterpretationEditor");
            Invoke(exact, "MarkStale");
            Check(!DataManager.Reports.Contains(older) && older.ApprovedInterpretation == null,
                "A removed report was re-registered by editor completion");
        }
        using (var stale = (NSViewController)Activator.CreateInstance(ReportType, new object[] { latest }))
        {
            var view = stale.View;
            DataManager.Clear(DataClearMode.ResetSession);
            SetText((NSTextView)Field(stale, "interpretationText"), "Must not affect replacement document");
            Invoke(stale, "CommitInterpretationEditor");
            Invoke(stale, "MarkStale");
            Check(DataManager.Reports.Count == 0 && latest.ApprovedInterpretation == null, "Stale report wrote to replacement project");
        }
    }

    static void CheckSaveEligibility()
    {
        DataManager.Clear(DataClearMode.ResetSession); DocumentDirtyTracker.MarkClean();
        var path = Path.Combine(Path.GetTempPath(), "ft-itc-native-report-only-" + Guid.NewGuid().ToString("N") + ".ftxtc");
        FTITCFormat.CurrentAccessedAppDocumentPath = path;
        var report = new AnalysisReport { Name = "Report-only project" }; DataManager.AddReport(report);
        using (var app = new AppDelegate())
        {
            Check(app.ValidateMenuItem(new NSMenuItem { Identifier = "save" }), "Report-only project cannot be saved");
            Check(Wait(ProjectWriter.SaveWithPathAsync()), "Report-only project save failed");
            DataManager.RemoveReport(report);
            Check(app.ValidateMenuItem(new NSMenuItem { Identifier = "save" }), "Deleting the last saved report disables saving its removal");
            Check(Wait(ProjectWriter.SaveWithPathAsync()), "Saving last-report deletion failed");
            using (var stream = File.OpenRead(path))
            {
                var reopened = Wait(FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict));
                Check(reopened.Reports.Count == 0, "Deleted last report reappeared after saving/reopening");
            }
        }
        File.Delete(path);
    }

    static NSWindow Owner()
    {
        var window = new NSWindow(new CGRect(100, 100, 1000, 740), NSWindowStyle.Titled, NSBackingStore.Buffered, false);
        window.MakeKeyAndOrderFront(null); return window;
    }
    static void Show(NSWindowController info, NSWindow owner, Action<AnalysisReport> callback)
    {
        InfoType.GetMethod("ShowSheet", Instance).Invoke(info, new object[] { owner, callback ?? (_ => { }) }); Pump();
    }
    static void SelectReports(NSWindowController info, int row)
    {
        ((NSSegmentedControl)Field(info, "tabs")).SelectedSegment = 2; Invoke(info, "Refresh");
        ((NSTableView)Field(info, "table")).SelectRow((nint)row, false); Pump(); Invoke(info, "UpdateActions");
    }
    static void Apply(NSViewController controller, IEnumerable<AnalysisResult> results, IEnumerable<ExperimentData> supporting) =>
        ReportType.GetMethod("ApplyResultSelection", Instance).Invoke(controller, new object[] { results, true, supporting, null });
    static IEnumerable<NSTextField> EditableFields(NSWindow window) => Descendants(window.ContentView).OfType<NSTextField>().Where(field => field.Editable);
    static string OverviewText(NSWindowController info) => string.Join("\n", Descendants((NSView)Field(info, "overviewRows"))
        .OfType<NSTextField>().Select(field => field.StringValue));
    static object Field(object instance, string name) => instance.GetType().GetField(name, Instance).GetValue(instance);
    static object Invoke(object instance, string name) => instance.GetType().GetMethod(name, Instance).Invoke(instance, null);
    static void Pump() { NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.4)); }
    static T Wait<T>(Task<T> task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.005));
        Check(task.IsCompleted, "Native asynchronous operation did not complete");
        return task.GetAwaiter().GetResult();
    }
    static void SetText(NSTextView text, string value) { using (var content = new NSAttributedString(value)) text.TextStorage.SetString(content); }
    static IEnumerable<NSView> Descendants(NSView root)
    {
        yield return root;
        foreach (var child in root.Subviews) foreach (var descendant in Descendants(child)) yield return descendant;
    }
    static void SaveView(NSView view, string filename)
    {
        var directory = Environment.GetEnvironmentVariable("FTITC_PROJECT_INFORMATION_QA_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        view.LayoutSubtreeIfNeeded();
        using (var image = CaptureView(view))
        {
            using (var png = image.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary()))
                File.WriteAllBytes(Path.Combine(directory, filename + ".png"), png.ToArray());
        }
    }
    static void CheckAppearances(NSWindow window, string filename)
    {
        var original = window.Appearance;
        window.Appearance = NSAppearance.GetAppearance(NSAppearance.NameAqua); Pump();
        SaveView(window.ContentView, filename + "-light");
        var light = BackgroundBrightness(window.ContentView);
        CheckContentBase(window, "light");
        window.Appearance = NSAppearance.GetAppearance(NSAppearance.NameDarkAqua); Pump();
        SaveView(window.ContentView, filename + "-dark");
        var dark = BackgroundBrightness(window.ContentView);
        CheckContentBase(window, "dark");
        Check(light > dark + 0.3, "Sheet background does not follow its native light/dark appearance");
        window.Appearance = original; Pump();
    }
    static double BackgroundBrightness(NSView view)
    {
        using (var bitmap = CaptureView(view))
        {
            using (var color = bitmap.ColorAt(bitmap.PixelsWide / 2, 8).UsingColorSpace(NSColorSpace.DeviceRGB))
                return (double)(color.RedComponent + color.GreenComponent + color.BlueComponent) / 3;
        }
    }
    static void CheckContentBase(NSWindow window, string appearance)
    {
        var root = window.ContentView;
        var body = Descendants(root).OfType<NSScrollView>().Single(view => view.GetType() == ContentType);
        Check(body.DrawsBackground && body.BackgroundColor.Equals(NSColor.WindowBackground)
            && body.BorderType == NSBorderType.NoBorder && !body.HasHorizontalScroller && !body.HasVerticalScroller,
            "Project Information does not use the shared native details content base");
        var document = (NSView)body.DocumentView;
        Check(Math.Abs((double)(document.Frame.Height - body.ContentView.Bounds.Height)) < 1
            && Math.Abs((double)(document.Frame.Width - body.ContentView.Bounds.Width)) < 1,
            "The outer content base can scroll instead of the nested content");
        var panel = Descendants(document).Single(view => view.GetType().Name == "PanelView");
        using (var bitmap = CaptureView(root))
        {
            var background = Sample(bitmap, root, body, new CGPoint(4, body.Bounds.Height / 2));
            var panelColor = Sample(bitmap, root, panel, new CGPoint(4, panel.Bounds.Height / 2));
            var reference = ReferenceBackground(window.Appearance, appearance);
            Check(background.Zip(reference, (actual, expected) => Math.Abs(actual - expected)).Max() < 0.015,
                "Project content background differs from the native reference details page");
            if (appearance == "light")
                Check(panelColor.Average() > background.Average() + 0.03,
                    "Light-mode content area does not show grey around its white panel");
            else
                Check(background.Average() > panelColor.Average() + 0.02,
                    "Dark-mode content and panel surfaces have lost their native contrast");
        }
    }
    static double[] ReferenceBackground(NSAppearance appearance, string name)
    {
        if (referenceBackgrounds.TryGetValue(name, out var saved)) return saved;
        double[] experimentColor = null;
        foreach (var controller in new[] { typeof(ExperimentDetailsPopoverController), typeof(BindingAnalysisViewController) })
        {
            var content = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            content.HeightAnchor.ConstraintEqualToConstant(40).Active = true;
            var method = controller.GetMethod("MakePage", BindingFlags.Static | BindingFlags.NonPublic);
            var arguments = controller == typeof(ExperimentDetailsPopoverController)
                ? new object[] { content, null } : new object[] { content };
            using (var page = (NSScrollView)method.Invoke(null, arguments))
            using (var window = new NSWindow(new CGRect(120, 120, 320, 240), NSWindowStyle.Titled, NSBackingStore.Buffered, false))
            {
                Check(page.GetType() == ContentType && page.DrawsBackground && page.HasVerticalScroller
                    && !page.HasHorizontalScroller && page.BackgroundColor.Equals(NSColor.WindowBackground),
                    "Native reference details page does not use the shared content base");
                var root = new NSView(new CGRect(0, 0, 320, 240));
                root.AddSubview(page);
                NSLayoutConstraint.ActivateConstraints(new[]
                {
                    page.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor),
                    page.BottomAnchor.ConstraintEqualToAnchor(root.BottomAnchor),
                    page.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                    page.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                });
                window.ContentView = root; window.Appearance = appearance;
                window.MakeKeyAndOrderFront(null); Pump(); page.LayoutSubtreeIfNeeded();
                using (var bitmap = CaptureView(root))
                {
                    var color = Sample(bitmap, root, page, new CGPoint(4, page.Bounds.Height / 2));
                    if (experimentColor != null)
                        Check(color.Zip(experimentColor, (actual, expected) => Math.Abs(actual - expected)).Max() < 0.015,
                            "Experiment and result details content surfaces differ");
                    experimentColor = color;
                }
                window.OrderOut(null);
            }
        }
        referenceBackgrounds.Add(name, experimentColor);
        return experimentColor;
    }
    static double[] Sample(NSBitmapImageRep bitmap, NSView root, NSView view, CGPoint point)
    {
        var location = view.ConvertPointToView(point, root);
        var scale = bitmap.PixelsWide / (double)root.Bounds.Width;
        var y = root.IsFlipped ? location.Y : root.Bounds.Height - location.Y;
        using (var color = bitmap.ColorAt((nint)(location.X * scale), (nint)(y * scale)).UsingColorSpace(NSColorSpace.DeviceRGB))
            return new[] { (double)color.RedComponent, (double)color.GreenComponent, (double)color.BlueComponent };
    }
    static NSBitmapImageRep CaptureView(NSView view)
    {
        view.Window.DisplayIfNeeded();
        using (var windowImage = CGImage.ScreenImage((int)view.Window.WindowNumber, CGRect.Null,
            CGWindowListOption.IncludingWindow, CGWindowImageOption.BoundsIgnoreFraming | CGWindowImageOption.BestResolution))
        {
            Check(windowImage != null, "Native sheet backing screenshot is unavailable");
            var content = view.ConvertRectToView(view.Bounds, null);
            var scale = windowImage.Width / (double)view.Window.Frame.Width;
            var crop = new CGRect(content.X * scale,
                (view.Window.Frame.Height - content.Y - content.Height) * scale,
                content.Width * scale, content.Height * scale);
            using (var contentImage = windowImage.WithImageInRect(crop))
                return new NSBitmapImageRep(contentImage);
        }
    }
    static void CheckFooterReachable(NSView root)
    {
        root.LayoutSubtreeIfNeeded();
        foreach (var button in Descendants(root).OfType<NSButton>().Where(button => !button.Hidden && button.Title != ""))
        {
            var frame = button.ConvertRectToView(button.Bounds, root);
            Check(frame.Left >= 0 && frame.Right <= root.Bounds.Width && frame.Bottom <= root.Bounds.Height && frame.Top >= 0,
                "Minimum sheet size clips its action: " + button.Title);
        }
    }
    static void CheckListWidths(NSWindowController info)
    {
        var tabs = (NSSegmentedControl)Field(info, "tabs");
        var selected = tabs.SelectedSegment;
        var table = (NSTableView)Field(info, "table");
        for (var index = 0; index < 3; index++)
        {
            tabs.SelectedSegment = index; Invoke(info, "Refresh"); Pump();
            info.Window.ContentView.LayoutSubtreeIfNeeded();
            var scroll = table.EnclosingScrollView;
            Check(Math.Abs((double)(table.Frame.Width - scroll.ContentView.Bounds.Width)) < 1,
                "Project list is wider than its viewport on tab " + index);
            var proposed = scroll.ContentView.Bounds;
            proposed.X = 100;
            var constrained = scroll.ContentView.ConstrainBoundsRect(proposed);
            Check(Math.Abs((double)constrained.X) < 1,
                "Project list can scroll horizontally on tab " + index);
            Check(!scroll.HasHorizontalScroller && scroll.HorizontalScrollElasticity == NSScrollElasticity.None,
                "Project list permits horizontal scrolling on tab " + index);
            var cell = table.GetView(0, 0, true);
            var text = string.Join("\n", Descendants(cell).OfType<NSTextField>().Select(field => field.StringValue));
            Check(index != 0 || (text.Contains("Cell:") && text.Contains("Syringe:") && text.Contains("integrated · Fit:")),
                "Experiment row omits conditions or processing/fit status");
            Check(index != 1 || (text.Contains("Experiments:") && text.Contains("°C") && text.Contains("Fit: Converged") && text.Contains("Not assessed")),
                "Result row omits saved members, temperature or assessment");
            foreach (var label in Descendants(cell).OfType<NSTextField>())
                Check(label.Frame.Y >= 0 && label.Frame.Right <= cell.Bounds.Width && label.Frame.Bottom <= cell.Bounds.Height,
                    "Project row clips a metadata line on tab " + index);
            SaveView(info.Window.ContentView, "macos-project-list-" + index + "-" + (int)info.Window.ContentView.Bounds.Width);
        }
        tabs.SelectedSegment = selected; Invoke(info, "Refresh"); Pump();
        if (selected == 2) table.SelectRow(0, false);
    }
    static AnalysisReport Report(AnalysisResult result, string name)
    {
        var report = new AnalysisReport { Name = name }; report.SetResultIds(new[] { result.UniqueID }); return report;
    }
    static ExperimentData Experiment(string name)
    {
        var data = new ExperimentData(name + ".itc") { CellConcentration = new FloatWithError(10e-6), SyringeConcentration = new FloatWithError(100e-6), CellVolume = 200e-6, TargetTemperature = 25 };
        for (var i = 0; i < 3; i++) { var injection = new InjectionData(data, i, 2e-6, 2e-10, true); injection.SetPeakArea(new FloatWithError(-2e-6, 1e-8)); data.Injections.Add(injection); }
        return data;
    }
    static AnalysisResult Result(ExperimentData data)
    {
        var model = new OneSetOfSites(data); model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1); model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000); model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot { Algorithm = SolverAlgorithm.LevenbergMarquardt, Termination = SolverTermination.Converged, Loss = 1 });
        model.Solution = SolutionInterface.FromModel(model, convergence);
        var globalModel = new GlobalModel(new List<Model> { model }) { Parameters = new GlobalModelParameters(), ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None } };
        globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = globalModel }, new List<SolutionInterface> { model.Solution }, convergence, reconstructBootstrap: false);
        globalModel.Solution = global; return new AnalysisResult(global);
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
