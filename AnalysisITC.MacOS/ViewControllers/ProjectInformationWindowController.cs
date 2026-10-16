using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC
{
    sealed class ProjectInformationWindowController : NSWindowController
    {
        readonly Guid documentId = DocumentDirtyTracker.CurrentDocumentId;
        readonly WorkspaceTabControl tabs = new WorkspaceTabControl(new CGRect(0, 0, 620, 32))
        {
            SegmentCount = 3, ControlSize = NSControlSize.Regular, SelectedSegment = 0,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        readonly NSTableView table = new NSTableView
        {
            HeaderView = null, RowHeight = 66, AllowsMultipleSelection = false,
            AllowsEmptySelection = true, ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly,
        };
        readonly NSTextField emptyLabel = Label("");
        readonly NSTextField contentSummary = Label("");
        readonly NSButton open = Button(ProjectInformationPresentation.OpenLabel, ProjectInformationPresentation.OpenTooltip);
        readonly NSButton rename = Button(ProjectInformationPresentation.RenameLabel, ProjectInformationPresentation.RenameTooltip);
        readonly NSButton delete = Button(ProjectInformationPresentation.DeleteLabel, ProjectInformationPresentation.DeleteTooltip);
        readonly NSButton details = Button(ProjectInformationPresentation.DetailsLabel, ProjectInformationPresentation.DetailsTooltip);
        readonly List<ITCDataContainer> rows = new List<ITCDataContainer>();
        readonly List<ITCDataContainer> observed = new List<ITCDataContainer>();
        readonly RowsDataSource dataSource;
        readonly RowsDelegate tableDelegate;
        readonly NSStackView reportActions;
        readonly NSView reportOverview;
        readonly NSStackView overviewRows;
        readonly NSLayoutConstraint listBottom, reportListBottom;
        bool closed;
        NSWindow parent;
        NSWindow childSheet;
        AnalysisReport reportToOpen;

        public ProjectInformationWindowController() : base(new NSPanel(new CGRect(0, 0, 860, 700),
            NSWindowStyle.Titled | NSWindowStyle.Resizable, NSBackingStore.Buffered, false))
        {
            Window.Title = ProjectInformationPresentation.WindowTitle;
            Window.ReleasedWhenClosed = false;
            Window.ContentMinSize = new CGSize(760, 560);
            dataSource = new RowsDataSource(this);
            tableDelegate = new RowsDelegate(this);
            table.DataSource = dataSource; table.Delegate = tableDelegate;
            table.AddColumn(new NSTableColumn("content") { Width = 800, MinWidth = 200,
                ResizingMask = NSTableColumnResizing.Autoresizing });
            table.ToolTip = ProjectInformationPresentation.ListTooltip;
            SetAccessibilityLabel(table, ProjectInformationPresentation.WindowTitle);
            tabs.ToolTip = ProjectInformationPresentation.TabsTooltip;
            tabs.AccessibilityLabel = ProjectInformationPresentation.TabsTooltip;
            tabs.Activated += (sender, e) => Refresh();
            for (nint segment = 0; segment < 3; segment++) tabs.SetWidth(200, segment);

            var filename = Path.GetFileName(FTITCFormat.CurrentAccessedAppDocumentPath);
            var projectName = Label(string.IsNullOrWhiteSpace(filename) ? ProjectInformationPresentation.UnsavedProject : filename);
            projectName.TextColor = NSColor.SecondaryLabel;
            projectName.LineBreakMode = NSLineBreakMode.TruncatingMiddle;
            projectName.ToolTip = projectName.StringValue;
            contentSummary.ToolTip = ProjectInformationPresentation.ContentSummaryTooltip;
            var header = Header(ProjectInformationPresentation.WindowTitle, projectName, contentSummary);
            var headerRule = Divider();
            var tabBar = new TabBarView { TranslatesAutoresizingMaskIntoConstraints = false };
            tabBar.AddSubview(tabs);
            var note = NSTextField.CreateWrappingLabel(ProjectInformationPresentation.StorageNote);
            note.TextColor = NSColor.SecondaryLabel;
            note.Font = NSFont.SystemFontOfSize(12);
            note.TranslatesAutoresizingMaskIntoConstraints = false;
            var scroll = new NSScrollView
            {
                DocumentView = table, HasVerticalScroller = true, AutohidesScrollers = true,
                HasHorizontalScroller = false, HorizontalScrollElasticity = NSScrollElasticity.None,
                BorderType = NSBorderType.NoBorder, BackgroundColor = NSColor.ControlBackground,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            table.BackgroundColor = NSColor.ControlBackground;
            var listPanel = new PanelView { TranslatesAutoresizingMaskIntoConstraints = false };
            listPanel.AddSubview(scroll);
            reportActions = Row(open, rename, delete, details);
            overviewRows = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                Alignment = NSLayoutAttribute.Leading, Spacing = 6, TranslatesAutoresizingMaskIntoConstraints = false };
            var overviewDocument = new FlippedView { TranslatesAutoresizingMaskIntoConstraints = false };
            overviewDocument.AddSubview(overviewRows);
            var overviewScroll = new NSScrollView { DocumentView = overviewDocument, HasVerticalScroller = true,
                HasHorizontalScroller = false, HorizontalScrollElasticity = NSScrollElasticity.None,
                AutohidesScrollers = true, DrawsBackground = false, BorderType = NSBorderType.NoBorder,
                TranslatesAutoresizingMaskIntoConstraints = false };
            var overviewRule = Divider();
            reportOverview = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            foreach (var view in new NSView[] { overviewRule, overviewScroll, reportActions }) reportOverview.AddSubview(view);
            listPanel.AddSubview(reportOverview);
            listBottom = scroll.BottomAnchor.ConstraintEqualToAnchor(listPanel.BottomAnchor, -12);
            reportListBottom = scroll.BottomAnchor.ConstraintEqualToAnchor(reportOverview.TopAnchor, -10);
            var close = Button(ProjectInformationPresentation.CloseLabel, ProjectInformationPresentation.CloseTooltip);
            close.KeyEquivalent = "\u001b";
            close.Activated += (sender, e) => CloseSheet();
            var footer = Footer(new NSView { TranslatesAutoresizingMaskIntoConstraints = false }, close);
            var bodyDocument = new FlippedView { TranslatesAutoresizingMaskIntoConstraints = false };
            var body = new DetailsSheetContentView(bodyDocument, verticalScrolling: false);
            bodyDocument.AddSubview(listPanel); bodyDocument.AddSubview(note);
            var root = new NSView(new CGRect(0, 0, 860, 700));
            Window.ContentView = root;
            foreach (var view in new NSView[] { header, headerRule, tabBar, body, footer }) root.AddSubview(view);
            listPanel.AddSubview(emptyLabel);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                header.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor, 16),
                header.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor, -16),
                header.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor, 12),
                headerRule.TopAnchor.ConstraintEqualToAnchor(header.BottomAnchor, 12),
                headerRule.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                headerRule.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                tabBar.TopAnchor.ConstraintEqualToAnchor(headerRule.BottomAnchor),
                tabBar.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                tabBar.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                tabBar.HeightAnchor.ConstraintEqualToConstant(44),
                tabs.LeadingAnchor.ConstraintEqualToAnchor(tabBar.LeadingAnchor, 16),
                tabs.CenterYAnchor.ConstraintEqualToAnchor(tabBar.CenterYAnchor),
                tabs.WidthAnchor.ConstraintEqualToConstant(620),
                body.TopAnchor.ConstraintEqualToAnchor(tabBar.BottomAnchor),
                body.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                body.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                body.BottomAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                listPanel.LeadingAnchor.ConstraintEqualToAnchor(bodyDocument.LeadingAnchor, 16),
                listPanel.TrailingAnchor.ConstraintEqualToAnchor(bodyDocument.TrailingAnchor, -16),
                listPanel.TopAnchor.ConstraintEqualToAnchor(bodyDocument.TopAnchor, 16),
                listPanel.BottomAnchor.ConstraintEqualToAnchor(note.TopAnchor, -12),
                scroll.TopAnchor.ConstraintEqualToAnchor(listPanel.TopAnchor, 12),
                scroll.LeadingAnchor.ConstraintEqualToAnchor(listPanel.LeadingAnchor, 12),
                scroll.TrailingAnchor.ConstraintEqualToAnchor(listPanel.TrailingAnchor, -12),
                reportOverview.LeadingAnchor.ConstraintEqualToAnchor(scroll.LeadingAnchor),
                reportOverview.TrailingAnchor.ConstraintEqualToAnchor(scroll.TrailingAnchor),
                reportOverview.BottomAnchor.ConstraintEqualToAnchor(listPanel.BottomAnchor, -12),
                overviewRule.TopAnchor.ConstraintEqualToAnchor(reportOverview.TopAnchor),
                overviewRule.LeadingAnchor.ConstraintEqualToAnchor(reportOverview.LeadingAnchor),
                overviewRule.TrailingAnchor.ConstraintEqualToAnchor(reportOverview.TrailingAnchor),
                overviewScroll.TopAnchor.ConstraintEqualToAnchor(overviewRule.BottomAnchor, 10),
                overviewScroll.LeadingAnchor.ConstraintEqualToAnchor(reportOverview.LeadingAnchor),
                overviewScroll.TrailingAnchor.ConstraintEqualToAnchor(reportOverview.TrailingAnchor),
                overviewScroll.HeightAnchor.ConstraintEqualToConstant(110),
                overviewDocument.WidthAnchor.ConstraintEqualToAnchor(overviewScroll.ContentView.WidthAnchor),
                overviewRows.TopAnchor.ConstraintEqualToAnchor(overviewDocument.TopAnchor),
                overviewRows.LeadingAnchor.ConstraintEqualToAnchor(overviewDocument.LeadingAnchor),
                overviewRows.TrailingAnchor.ConstraintEqualToAnchor(overviewDocument.TrailingAnchor),
                overviewRows.BottomAnchor.ConstraintEqualToAnchor(overviewDocument.BottomAnchor),
                reportActions.TopAnchor.ConstraintEqualToAnchor(overviewScroll.BottomAnchor, 10),
                reportActions.LeadingAnchor.ConstraintEqualToAnchor(reportOverview.LeadingAnchor),
                reportActions.BottomAnchor.ConstraintEqualToAnchor(reportOverview.BottomAnchor),
                emptyLabel.CenterXAnchor.ConstraintEqualToAnchor(scroll.CenterXAnchor),
                emptyLabel.CenterYAnchor.ConstraintEqualToAnchor(scroll.CenterYAnchor),
                note.LeadingAnchor.ConstraintEqualToAnchor(bodyDocument.LeadingAnchor, 16),
                note.TrailingAnchor.ConstraintEqualToAnchor(bodyDocument.TrailingAnchor, -16),
                note.BottomAnchor.ConstraintEqualToAnchor(bodyDocument.BottomAnchor, -12),
                footer.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                footer.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                footer.BottomAnchor.ConstraintEqualToAnchor(root.BottomAnchor),
            });
            open.Activated += (sender, e) => OpenSelected();
            rename.Activated += (sender, e) => RenameSelected();
            delete.Activated += (sender, e) => DeleteSelected();
            details.Activated += (sender, e) => ShowDetails();
            DataManager.ReportsDidChange += OnReportsChanged;
            DataManager.DataDidChange += OnDataChanged;
            Refresh();
        }

        internal bool IsClosed => closed;
        bool IsCurrent => !closed && documentId == DocumentDirtyTracker.CurrentDocumentId;
        AnalysisReport SelectedReport => table.SelectedRow >= 0 && table.SelectedRow < rows.Count
            ? rows[(int)table.SelectedRow] as AnalysisReport : null;
        static AnalysisResult ResolveResult(string id) => DataManager.Results.FirstOrDefault(result => result.UniqueID == id);
        bool CanEdit(AnalysisReport report) => IsCurrent && report != null && DataManager.Reports.Contains(report)
            && AnalysisReportPresentation.MissingResultCount(report, ResolveResult) == 0;

        internal void ShowSheet(NSWindow owner, Action<AnalysisReport> completed)
        {
            parent = owner;
            owner.BeginSheet(Window, response =>
            {
                closed = true;
                Unsubscribe();
                Window.OrderOut(this);
                completed(reportToOpen);
            });
        }

        void CloseSheet()
        {
            if (closed) return;
            closed = true;
            Unsubscribe();
            if (childSheet != null) Window.EndSheet(childSheet);
            if (parent != null) parent.EndSheet(Window);
            else Window.OrderOut(this);
        }

        void OnReportsChanged(object sender, EventArgs e) => DispatchRefresh();
        void OnDataChanged(object sender, ExperimentData e) => DispatchRefresh();
        void OnContentChanged(object sender, EventArgs e) => DispatchRefresh();
        void DispatchRefresh()
        {
            if (NSThread.IsMain) Refresh();
            else NSApplication.SharedApplication.BeginInvokeOnMainThread(Refresh);
        }

        void Refresh()
        {
            if (!IsCurrent) { CloseSheet(); return; }
            foreach (var item in observed) item.ContentChanged -= OnContentChanged;
            observed.Clear();
            observed.AddRange(DataManager.SourceItems);
            observed.AddRange(DataManager.Reports);
            foreach (var item in observed) item.ContentChanged += OnContentChanged;
            var selected = SelectedReport;
            var experiments = DataManager.Data;
            var results = DataManager.Results;
            contentSummary.StringValue = ProjectInformationPresentation.ContentSummary(experiments.Count, results.Count, DataManager.Reports.Count);
            tabs.SetLabel(ProjectInformationPresentation.ExperimentsLabel, 0);
            tabs.SetLabel(ProjectInformationPresentation.AnalysisResultsLabel, 1);
            tabs.SetLabel(ProjectInformationPresentation.SavedReportsLabel, 2);
            rows.Clear();
            if (tabs.SelectedSegment == 0) rows.AddRange(experiments);
            else if (tabs.SelectedSegment == 1) rows.AddRange(results);
            else rows.AddRange(DataManager.Reports);
            table.ReloadData();
            var index = selected == null ? -1 : rows.IndexOf(selected);
            if (index >= 0) table.SelectRow((nint)index, false);
            else table.DeselectAll(this);
            emptyLabel.StringValue = tabs.SelectedSegment == 0 ? "No experiments" : tabs.SelectedSegment == 1
                ? "No Analysis Results" : "No saved reports";
            emptyLabel.Hidden = rows.Count > 0;
            tabs.NeedsDisplay = true;
            UpdateActions();
        }

        void UpdateActions()
        {
            var reportsSelected = tabs.SelectedSegment == 2;
            reportOverview.Hidden = !reportsSelected;
            listBottom.Active = reportListBottom.Active = false;
            (reportsSelected ? reportListBottom : listBottom).Active = true;
            var selected = SelectedReport;
            open.Enabled = rename.Enabled = CanEdit(selected);
            var missing = selected != null && AnalysisReportPresentation.MissingResultCount(selected, ResolveResult) > 0;
            open.ToolTip = missing ? ProjectInformationPresentation.MissingResultsTooltip : ProjectInformationPresentation.OpenTooltip;
            rename.ToolTip = missing ? ProjectInformationPresentation.MissingResultsTooltip : ProjectInformationPresentation.RenameTooltip;
            details.Enabled = delete.Enabled = IsCurrent && selected != null && DataManager.Reports.Contains(selected);
            UpdateOverview(selected);
        }

        void UpdateOverview(AnalysisReport report)
        {
            foreach (var child in overviewRows.Views.ToArray())
            {
                overviewRows.RemoveArrangedSubview(child); child.RemoveFromSuperview(); child.Dispose();
            }
            foreach (var item in ProjectInformationPresentation.OverviewRows(report, ResolveResult))
            {
                var key = Label(item.Label);
                key.TextColor = NSColor.SecondaryLabel; key.Font = NSFont.SystemFontOfSize(12); key.ToolTip = item.Tooltip;
                key.WidthAnchor.ConstraintEqualToConstant(140).Active = true;
                var value = Label(item.Value);
                value.Font = NSFont.SystemFontOfSize(12); value.ToolTip = item.Tooltip;
                value.Cell.Wraps = true; value.Cell.UsesSingleLineMode = false;
                value.LineBreakMode = NSLineBreakMode.ByWordWrapping;
                value.HorizontalContentSizeConstraintActive = false;
                value.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
                var row = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                    Alignment = NSLayoutAttribute.Top, Spacing = 12, TranslatesAutoresizingMaskIntoConstraints = false };
                row.AddArrangedSubview(key); row.AddArrangedSubview(value);
                overviewRows.AddArrangedSubview(row);
                row.WidthAnchor.ConstraintEqualToAnchor(overviewRows.WidthAnchor).Active = true;
            }
        }

        void OpenSelected()
        {
            var selected = SelectedReport;
            if (!CanEdit(selected)) return;
            reportToOpen = selected;
            CloseSheet();
        }

        void RenameSelected()
        {
            var selected = SelectedReport;
            if (!CanEdit(selected) || childSheet != null) return;
            var field = new NSTextField(new CGRect(0, 0, 440, 24))
            {
                StringValue = selected.Name ?? "", PlaceholderString = AnalysisReportPresentation.DisplayName(selected, ResolveResult),
                ToolTip = ProjectInformationPresentation.ReportNameTooltip,
                AccessibilityLabel = ProjectInformationPresentation.ReportNameLabel,
            };
            var alert = new NSAlert
            {
                MessageText = ProjectInformationPresentation.RenameWindowTitle,
                InformativeText = ProjectInformationPresentation.ReportNameLabel,
                AccessoryView = field,
            };
            alert.AddButton(ProjectInformationPresentation.RenameLabel); alert.AddButton(ProjectInformationPresentation.CancelLabel);
            alert.Buttons[0].ToolTip = ProjectInformationPresentation.RenameTooltip;
            alert.Buttons[1].ToolTip = ProjectInformationPresentation.CancelTooltip;
            childSheet = alert.Window;
            alert.BeginSheet(Window, response =>
            {
                childSheet = null;
                if ((int)response == (int)NSAlertButtonReturn.First && CanEdit(selected))
                    ProjectInformationPresentation.RenameReport(selected, field.StringValue);
                Refresh(); alert.Dispose(); field.Dispose();
            });
            alert.Window.MakeFirstResponder(field);
        }

        void DeleteSelected()
        {
            var selected = SelectedReport;
            if (!IsCurrent || selected == null || !DataManager.Reports.Contains(selected) || childSheet != null) return;
            var alert = new NSAlert
            {
                MessageText = ProjectInformationPresentation.DeleteWindowTitle,
                InformativeText = ProjectInformationPresentation.DeleteConfirmation(selected, ResolveResult),
                AlertStyle = NSAlertStyle.Warning,
            };
            alert.AddButton(ProjectInformationPresentation.DeleteLabel); alert.AddButton(ProjectInformationPresentation.CancelLabel);
            alert.Buttons[0].ToolTip = ProjectInformationPresentation.DeleteTooltip;
            alert.Buttons[1].ToolTip = ProjectInformationPresentation.CancelTooltip;
            alert.Buttons[0].HasDestructiveAction = true;
            childSheet = alert.Window;
            alert.BeginSheet(Window, response =>
            {
                childSheet = null;
                if ((int)response == (int)NSAlertButtonReturn.First && IsCurrent && DataManager.Reports.Contains(selected))
                    DataManager.RemoveReport(selected);
                Refresh(); alert.Dispose();
            });
        }

        void ShowDetails()
        {
            var selected = SelectedReport;
            if (!IsCurrent || selected == null || !DataManager.Reports.Contains(selected) || childSheet != null) return;
            PresentDetails(selected);
        }

        void PresentDetails(AnalysisReport selected)
        {
            var sheet = new NSPanel(new CGRect(0, 0, 740, 620), NSWindowStyle.Titled | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false) { Title = ProjectInformationPresentation.DetailsWindowTitle,
                    ReleasedWhenClosed = false, ContentMinSize = new CGSize(620, 460) };
            var reportName = Label(AnalysisReportPresentation.DisplayName(selected, ResolveResult));
            reportName.ToolTip = reportName.StringValue;
            var reportSummary = Label(ProjectInformationPresentation.ResultCount(selected.ResultIds.Count)
                + " · " + ProjectInformationPresentation.LastEditedLabel + ": " + ProjectInformationPresentation.LastEditedText(selected));
            reportSummary.TextColor = NSColor.SecondaryLabel;
            reportSummary.ToolTip = reportSummary.StringValue;
            var header = Header(ProjectInformationPresentation.DetailsWindowTitle, reportName, reportSummary);
            var headerRule = Divider();
            var text = new AnalysisReportTextView(new CGRect(0, 0, 700, 460))
            {
                Editable = false,
                Selectable = true, RichText = false, Font = NSFont.SystemFontOfSize(13),
                TextContainerInset = new CGSize(10, 10), HorizontallyResizable = false, VerticallyResizable = true,
                AutoresizingMask = NSViewResizingMask.WidthSizable, ToolTip = ProjectInformationPresentation.DetailsTextTooltip,
                AccessibilityLabel = ProjectInformationPresentation.DetailsLabel,
            };
            using (var content = new NSAttributedString(ProjectInformationPresentation.DetailsText(selected, ResolveResult),
                new NSStringAttributes { Font = text.Font, ForegroundColor = AnalysisReportTextView.ForegroundFor(text) }))
                text.TextStorage.SetString(content);
            text.TextContainer.WidthTracksTextView = true;
            text.TextContainer.ContainerSize = new CGSize(700, nfloat.MaxValue);
            var scroll = new NSScrollView { DocumentView = text, HasVerticalScroller = true,
                AutohidesScrollers = true, BorderType = NSBorderType.NoBorder, BackgroundColor = NSColor.TextBackground,
                TranslatesAutoresizingMaskIntoConstraints = false };
            text.BackgroundColor = NSColor.TextBackground;
            var panel = ContentPanel(scroll);
            var copy = Button(ProjectInformationPresentation.CopyAllLabel, ProjectInformationPresentation.CopyAllTooltip);
            var close = Button(ProjectInformationPresentation.CloseLabel, ProjectInformationPresentation.DetailsCloseTooltip);
            close.KeyEquivalent = "\u001b";
            copy.Activated += (sender, e) =>
            {
                NSPasteboard.GeneralPasteboard.ClearContents();
                NSPasteboard.GeneralPasteboard.SetStringForType(ProjectInformationPresentation.CopyText(selected), "NSStringPboardType");
            };
            close.Activated += (sender, e) => Window.EndSheet(sheet);
            var footer = Footer(copy, new NSView { TranslatesAutoresizingMaskIntoConstraints = false }, close);
            var bodyDocument = new FlippedView { TranslatesAutoresizingMaskIntoConstraints = false };
            bodyDocument.AddSubview(panel);
            var body = new DetailsSheetContentView(bodyDocument, verticalScrolling: false);
            var root = new NSView(new CGRect(0, 0, 740, 620));
            sheet.ContentView = root;
            foreach (var view in new NSView[] { header, headerRule, body, footer }) root.AddSubview(view);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                header.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor, 16),
                header.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor, -16),
                header.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor, 12),
                headerRule.TopAnchor.ConstraintEqualToAnchor(header.BottomAnchor, 12),
                headerRule.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                headerRule.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                body.TopAnchor.ConstraintEqualToAnchor(headerRule.BottomAnchor),
                body.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                body.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                body.BottomAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                panel.LeadingAnchor.ConstraintEqualToAnchor(bodyDocument.LeadingAnchor, 16),
                panel.TrailingAnchor.ConstraintEqualToAnchor(bodyDocument.TrailingAnchor, -16),
                panel.TopAnchor.ConstraintEqualToAnchor(bodyDocument.TopAnchor, 16),
                panel.BottomAnchor.ConstraintEqualToAnchor(bodyDocument.BottomAnchor, -16),
                footer.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor),
                footer.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor),
                footer.BottomAnchor.ConstraintEqualToAnchor(root.BottomAnchor),
            });
            childSheet = sheet;
            Window.BeginCriticalSheet(sheet, response => { childSheet = null; sheet.OrderOut(this); sheet.Dispose(); });
        }

        void Unsubscribe()
        {
            DataManager.ReportsDidChange -= OnReportsChanged;
            DataManager.DataDidChange -= OnDataChanged;
            foreach (var item in observed) item.ContentChanged -= OnContentChanged;
            observed.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Unsubscribe(); dataSource.Dispose(); tableDelegate.Dispose(); }
            base.Dispose(disposing);
        }

        string RowText(ITCDataContainer item)
        {
            if (item is AnalysisReport report)
            {
                var missing = AnalysisReportPresentation.MissingResultStatus(report, ResolveResult);
                return AnalysisReportPresentation.DisplayName(report, ResolveResult) + "\n" + report.ResultIds.Count
                    + (report.ResultIds.Count == 1 ? " result" : " results") + " · " + ProjectInformationPresentation.LastEditedLabel
                    + ": " + ProjectInformationPresentation.LastEditedText(report)
                    + (string.IsNullOrWhiteSpace(missing) ? "" : "\n" + missing);
            }
            if (item is AnalysisResult result)
                return result.Name + "\n" + ProjectInformationPresentation.ResultDetail(result);
            if (item is ExperimentData experiment)
                return experiment.Name + "\n" + ProjectInformationPresentation.ExperimentDetail(experiment);
            return item.Name + "\n" + LocalDateTimeFormatter.ShortDateTime(item.Date);
        }

        static NSTextField Label(string title) => new NSTextField
        {
            StringValue = title, Bordered = false, Editable = false, Selectable = true, DrawsBackground = false,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        static NSButton Button(string title, string tooltip)
        {
            var button = new NSButton { Title = title, ToolTip = tooltip,
                BezelStyle = NSBezelStyle.Rounded, TranslatesAutoresizingMaskIntoConstraints = false };
            button.WidthAnchor.ConstraintGreaterThanOrEqualToConstant(80).Active = true;
            SetAccessibilityLabel(button, title);
            return button;
        }
        static void SetAccessibilityLabel(NSObject control, string label) => control.SetValueForKey(
            new NSString(label ?? ""), new NSString("accessibilityLabel"));
        static NSStackView Row(params NSView[] views)
        {
            var row = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Alignment = NSLayoutAttribute.CenterY, Spacing = 8, TranslatesAutoresizingMaskIntoConstraints = false };
            foreach (var view in views) row.AddArrangedSubview(view);
            return row;
        }

        static NSStackView Header(string title, params NSTextField[] lines)
        {
            var heading = Label(title);
            heading.Font = NSFont.BoldSystemFontOfSize(NSFont.SystemFontSize);
            var header = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                Alignment = NSLayoutAttribute.Leading, Spacing = 2, TranslatesAutoresizingMaskIntoConstraints = false };
            foreach (var label in new[] { heading }.Concat(lines))
            {
                label.Font = label == heading ? heading.Font : NSFont.SystemFontOfSize(13);
                label.LineBreakMode = NSLineBreakMode.TruncatingTail;
                label.Cell.Wraps = false; label.Cell.UsesSingleLineMode = true;
                label.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
                header.AddArrangedSubview(label);
                label.WidthAnchor.ConstraintEqualToAnchor(header.WidthAnchor).Active = true;
            }
            return header;
        }

        static NSView ContentPanel(NSView body)
        {
            var panel = new PanelView { TranslatesAutoresizingMaskIntoConstraints = false };
            panel.AddSubview(body);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                body.TopAnchor.ConstraintEqualToAnchor(panel.TopAnchor, 12),
                body.LeadingAnchor.ConstraintEqualToAnchor(panel.LeadingAnchor, 12),
                body.TrailingAnchor.ConstraintEqualToAnchor(panel.TrailingAnchor, -12),
                body.BottomAnchor.ConstraintEqualToAnchor(panel.BottomAnchor, -12),
            });
            return panel;
        }

        static NSView Footer(params NSView[] controls)
        {
            var footer = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
            var divider = Divider();
            var buttons = Row(controls);
            foreach (var button in controls.OfType<NSButton>()) button.WidthAnchor.ConstraintGreaterThanOrEqualToConstant(80).Active = true;
            footer.AddSubview(divider); footer.AddSubview(buttons);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                footer.HeightAnchor.ConstraintEqualToConstant(62),
                divider.TopAnchor.ConstraintEqualToAnchor(footer.TopAnchor),
                divider.LeadingAnchor.ConstraintEqualToAnchor(footer.LeadingAnchor),
                divider.TrailingAnchor.ConstraintEqualToAnchor(footer.TrailingAnchor),
                buttons.LeadingAnchor.ConstraintEqualToAnchor(footer.LeadingAnchor, 16),
                buttons.TrailingAnchor.ConstraintEqualToAnchor(footer.TrailingAnchor, -16),
                buttons.CenterYAnchor.ConstraintEqualToAnchor(footer.CenterYAnchor, -1),
            });
            return footer;
        }

        static NSView Divider() => new NSBox { BoxType = NSBoxType.NSBoxSeparator,
            TranslatesAutoresizingMaskIntoConstraints = false };

        class SemanticView : NSView
        {
            public SemanticView() { }
            public SemanticView(CGRect frame) : base(frame) { }
            public override void ViewDidChangeEffectiveAppearance()
            {
                base.ViewDidChangeEffectiveAppearance(); NeedsDisplay = true;
            }
        }

        sealed class FlippedView : NSView
        {
            public override bool IsFlipped => true;
        }

        sealed class TabBarView : SemanticView
        {
            public override void DrawRect(CGRect dirtyRect)
            {
                NSColor.WindowBackground.SetFill(); NSBezierPath.FillRect(Bounds);
                NSColor.Separator.SetFill(); NSBezierPath.FillRect(new CGRect(0, 0, Bounds.Width, 0.5));
            }
        }

        sealed class PanelView : SemanticView
        {
            public override void DrawRect(CGRect dirtyRect)
            {
                base.DrawRect(dirtyRect);
                var rect = new CGRect(0.5, 0.5, Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 1));
                using (var path = NSBezierPath.FromRoundedRect(rect, 4, 4))
                {
                    NSColor.ControlBackground.SetFill(); path.Fill();
                    NSColor.Separator.SetStroke(); path.LineWidth = 0.5f; path.Stroke();
                }
            }
        }
        sealed class RowsDataSource : NSTableViewDataSource
        {
            readonly ProjectInformationWindowController owner;
            public RowsDataSource(ProjectInformationWindowController owner) { this.owner = owner; }
            public override nint GetRowCount(NSTableView tableView) => owner.rows.Count;
        }
        sealed class RowsDelegate : NSTableViewDelegate
        {
            readonly ProjectInformationWindowController owner;
            public RowsDelegate(ProjectInformationWindowController owner) { this.owner = owner; }
            public override void SelectionDidChange(NSNotification notification) => owner.UpdateActions();
            public override nfloat GetRowHeight(NSTableView tableView, nint row) => owner.rows[(int)row] is AnalysisReport ? 66 : 86;
            public override NSView GetViewForItem(NSTableView tableView, NSTableColumn column, nint row)
            {
                var content = owner.RowText(owner.rows[(int)row]);
                var separator = content.IndexOf('\n');
                var width = Math.Max(200, (double)column.Width);
                var height = (double)GetRowHeight(tableView, row);
                var cell = new NSTableCellView(new CGRect(0, 0, width, height)) { ToolTip = content };
                var name = new NSTextField(new CGRect(8, height - 22, width - 16, 18))
                {
                    StringValue = separator < 0 ? content : content.Substring(0, separator), Bordered = false, Editable = false,
                    Selectable = false, DrawsBackground = false, Font = NSFont.SystemFontOfSize(12),
                    MaximumNumberOfLines = 1, UsesSingleLineMode = true, LineBreakMode = NSLineBreakMode.TruncatingTail,
                    AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin,
                    ToolTip = separator < 0 ? content : content.Substring(0, separator),
                };
                name.Cell.Wraps = false; name.Cell.Scrollable = false;
                cell.AddSubview(name);
                var lines = separator < 0 ? Array.Empty<string>() : content.Substring(separator + 1).Split('\n');
                for (var index = 0; index < lines.Length; index++)
                {
                    var metadata = new NSTextField(new CGRect(8, height - 42 - index * 17, width - 16, 16))
                    {
                        StringValue = lines[index], Bordered = false, Editable = false,
                        Selectable = false, DrawsBackground = false, Font = NSFont.SystemFontOfSize(11), TextColor = NSColor.SecondaryLabel,
                        MaximumNumberOfLines = 1, UsesSingleLineMode = true, LineBreakMode = NSLineBreakMode.TruncatingTail,
                        AutoresizingMask = NSViewResizingMask.WidthSizable, ToolTip = content,
                    };
                    metadata.Cell.Wraps = false; metadata.Cell.Scrollable = false;
                    cell.AddSubview(metadata);
                }
                return cell;
            }
        }
    }
}
