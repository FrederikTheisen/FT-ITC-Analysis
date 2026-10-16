using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Dialogs;

internal sealed class ProjectInformationWindow : Window
{
    readonly Guid documentId = DocumentDirtyTracker.CurrentDocumentId;
    readonly TabControl tabs = new();
    readonly TextBlock contentSummary = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly ListBox experimentsList = CreateList(ProjectInformationPresentation.ExperimentsTooltip);
    readonly ListBox resultsList = CreateList(ProjectInformationPresentation.AnalysisResultsTooltip);
    readonly ListBox reportsList = CreateList(ProjectInformationPresentation.SavedReportsTooltip);
    readonly StackPanel overviewPanel = new() { Spacing = 3 };
    readonly ScrollViewer overviewScroll = new()
    {
        MaxHeight = 185,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    readonly Border overviewBorder;
    readonly Button openButton = ActionButton(ProjectInformationPresentation.OpenLabel, ProjectInformationPresentation.OpenTooltip);
    readonly Button renameButton = ActionButton(ProjectInformationPresentation.RenameLabel, ProjectInformationPresentation.RenameTooltip);
    readonly Button deleteButton = ActionButton(ProjectInformationPresentation.DeleteLabel, ProjectInformationPresentation.DeleteTooltip);
    readonly Button detailsButton = ActionButton(ProjectInformationPresentation.DetailsLabel, ProjectInformationPresentation.DetailsTooltip);
    readonly Button copyAllButton = ActionButton(ProjectInformationPresentation.CopyAllLabel, ProjectInformationPresentation.CopyAllTooltip);
    readonly List<ITCDataContainer> observed = new();
    bool closed;
    bool detailsOpen;

    internal ListBox ExperimentsList => experimentsList;
    internal ListBox ResultsList => resultsList;
    internal ListBox ReportsList => reportsList;
    internal StackPanel ReportOverviewPanel => overviewPanel;
    internal ScrollViewer ReportOverviewScroll => overviewScroll;
    internal Button OpenButton => openButton;
    internal Button RenameButton => renameButton;
    internal Button DeleteButton => deleteButton;
    internal Button DetailsButton => detailsButton;
    internal Button CopyAllButton => copyAllButton;
    internal TabControl Tabs => tabs;
    AnalysisReport? SelectedReport => (reportsList.SelectedItem as ProjectInformationEntry)?.Report;
    bool CurrentProject => !closed && documentId == DocumentDirtyTracker.CurrentDocumentId;

    public ProjectInformationWindow()
    {
        Title = ProjectInformationPresentation.WindowTitle;
        Width = 860;
        Height = 700;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AppTheme.Bind(this, BackgroundProperty, AppTheme.WorkspaceBackground);

        var reportActions = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto"), ColumnSpacing = 8 };
        reportActions.Children.Add(openButton);
        Grid.SetColumn(renameButton, 1); reportActions.Children.Add(renameButton);
        Grid.SetColumn(deleteButton, 2); reportActions.Children.Add(deleteButton);
        Grid.SetColumn(detailsButton, 3); reportActions.Children.Add(detailsButton);
        Grid.SetColumn(copyAllButton, 5); reportActions.Children.Add(copyAllButton);
        overviewScroll.Content = overviewPanel;
        ToolTip.SetTip(overviewScroll, ProjectInformationPresentation.SavedReportsTooltip);
        overviewBorder = Card(overviewScroll);
        var reportsPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            RowSpacing = 10,
            Margin = new Thickness(0, 10, 0, 0),
        };
        reportsPanel.Children.Add(Card(reportsList));
        Grid.SetRow(overviewBorder, 1);
        reportsPanel.Children.Add(overviewBorder);
        Grid.SetRow(reportActions, 2);
        reportsPanel.Children.Add(reportActions);

        tabs.Margin = new Thickness(12);
        ToolTip.SetTip(tabs, ProjectInformationPresentation.TabsTooltip);
        tabs.Items.Add(new TabItem { Content = TabSection(experimentsList) });
        tabs.Items.Add(new TabItem { Content = TabSection(resultsList) });
        tabs.Items.Add(new TabItem { Content = reportsPanel });
        var note = new TextBlock { Text = ProjectInformationPresentation.StorageNote, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        AppTheme.Bind(note, TextBlock.ForegroundProperty, AppTheme.MutedText);
        var closeButton = ActionButton(ProjectInformationPresentation.CloseLabel, ProjectInformationPresentation.CloseTooltip);
        closeButton.IsCancel = true;
        closeButton.VerticalAlignment = VerticalAlignment.Center;
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;
        closeButton.Click += (_, _) => Close();
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Margin = new Thickness(12, 10) };
        footer.Children.Add(note);
        Grid.SetColumn(closeButton, 1);
        footer.Children.Add(closeButton);
        var footerBorder = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        AppTheme.Bind(footerBorder, Border.BackgroundProperty, AppTheme.PanelBackground);
        AppTheme.Bind(footerBorder, Border.BorderBrushProperty, AppTheme.PanelBorder);
        var root = new DockPanel { LastChildFill = true };
        AppTheme.Bind(root, Panel.BackgroundProperty, AppTheme.WorkspaceBackground);
        DockPanel.SetDock(footerBorder, Dock.Bottom);
        root.Children.Add(footerBorder);
        var header = Header();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(tabs);
        Content = root;

        reportsList.SelectionChanged += (_, _) => UpdateReportDetails();
        openButton.Click += (_, _) => OpenSelectedReport();
        renameButton.Click += async (_, _) => await RenameSelectedAsync();
        deleteButton.Click += async (_, _) => await DeleteSelectedAsync();
        detailsButton.Click += async (_, _) => await ShowDetailsAsync();
        copyAllButton.Click += async (_, _) => await CopyAllAsync();
        DataManager.DataDidChange += OnProjectChanged;
        DataManager.ReportsDidChange += OnReportsChanged;
        RefreshContent();
    }

    Border Header()
    {
        var path = FTITCFormat.CurrentAccessedAppDocumentPath;
        var title = new TextBlock { Text = ProjectInformationPresentation.WindowTitle, FontSize = 16, FontWeight = FontWeight.SemiBold };
        var filename = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(path) ? ProjectInformationPresentation.UnsavedProject : Path.GetFileName(path),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTip.SetTip(filename, string.IsNullOrWhiteSpace(path) ? ProjectInformationPresentation.UnsavedProject : path);
        ToolTip.SetTip(contentSummary, ProjectInformationPresentation.ContentSummaryTooltip);
        AppTheme.Bind(title, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
        AppTheme.Bind(filename, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
        AppTheme.Bind(contentSummary, TextBlock.ForegroundProperty, AppTheme.MutedText);
        var header = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 12),
            Child = new StackPanel { Spacing = 2, Children = { title, filename, contentSummary } },
        };
        AppTheme.Bind(header, Border.BackgroundProperty, AppTheme.PanelBackground);
        AppTheme.Bind(header, Border.BorderBrushProperty, AppTheme.PanelBorder);
        return header;
    }

    static Border TabSection(Control content) =>
        new() { Padding = new Thickness(0, 10, 0, 0), Child = Card(content) };

    static Border Card(Control content)
    {
        var border = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(12, 10), Child = content };
        AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
        AppTheme.Bind(border, Border.BorderBrushProperty, AppTheme.PanelBorder);
        return border;
    }

    static Control OverviewRow(ProjectInformationOverviewRow row)
    {
        var label = new TextBlock { Text = row.Label, FontSize = 12, VerticalAlignment = VerticalAlignment.Top };
        var value = new SelectableTextBlock { Text = row.Value, FontSize = 13, TextWrapping = TextWrapping.Wrap };
        AppTheme.Bind(label, TextBlock.ForegroundProperty, AppTheme.MutedText);
        AppTheme.Bind(value, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
        ToolTip.SetTip(label, row.Tooltip);
        ToolTip.SetTip(value, row.Tooltip);
        var pair = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*"), ColumnSpacing = 10, MinHeight = 23 };
        pair.Children.Add(label);
        Grid.SetColumn(value, 1);
        pair.Children.Add(value);
        return pair;
    }

    static ListBox CreateList(string tooltip)
    {
        var list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<ProjectInformationEntry>((entry, _) =>
            {
                var name = new TextBlock
                {
                    Text = entry?.Name,
                    FontWeight = FontWeight.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                ToolTip.SetTip(name, entry?.Name);
                var detail = new TextBlock { Text = entry?.Detail, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
                AppTheme.Bind(detail, TextBlock.ForegroundProperty, AppTheme.MutedText);
                return new StackPanel { Spacing = 3, Margin = new Thickness(4), Children = { name, detail } };
            }),
            [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled,
            [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
        };
        AppTheme.Bind(list, BackgroundProperty, AppTheme.PanelBackground);
        AppTheme.Bind(list, ForegroundProperty, AppTheme.PrimaryText);
        ToolTip.SetTip(list, tooltip);
        list.Styles.Add(new global::Avalonia.Styling.Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) },
        });
        return list;
    }

    static Button ActionButton(string label, string tooltip)
    {
        var button = new Button { Content = label, MinWidth = 82 };
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    void OnProjectChanged(object? sender, ExperimentData e) => QueueRefresh();
    void OnReportsChanged(object? sender, EventArgs e) => QueueRefresh();
    void OnContentChanged(object? sender, EventArgs e) => QueueRefresh();

    void QueueRefresh()
    {
        if (Dispatcher.UIThread.CheckAccess()) RefreshContent();
        else Dispatcher.UIThread.Post(RefreshContent);
    }

    internal void RefreshContent()
    {
        if (closed) return;
        if (!CurrentProject) { Close(); return; }
        var selectedReport = SelectedReport;
        foreach (var item in observed) item.ContentChanged -= OnContentChanged;
        observed.Clear();
        observed.AddRange(DataManager.SourceItems);
        observed.AddRange(DataManager.Reports);
        foreach (var item in observed) item.ContentChanged += OnContentChanged;

        experimentsList.ItemsSource = DataManager.Data.Select(experiment => new ProjectInformationEntry(
            experiment.Name, ProjectInformationPresentation.ExperimentDetail(experiment))).ToList();
        resultsList.ItemsSource = DataManager.Results.Select(result => new ProjectInformationEntry(result.Name,
            ProjectInformationPresentation.ResultDetail(result))).ToList();
        var reports = DataManager.Reports.Select(report => new ProjectInformationEntry(
            AnalysisReportPresentation.DisplayName(report, ResolveResult),
            ProjectInformationPresentation.ResultCount(report.ResultIds.Count) + " · "
            + ProjectInformationPresentation.LastEditedLabel + ": " + ProjectInformationPresentation.LastEditedText(report)
            + MissingSuffix(report), report)).ToList();
        reportsList.ItemsSource = reports;
        reportsList.SelectedItem = reports.FirstOrDefault(entry => ReferenceEquals(entry.Report, selectedReport));
        SetTabHeader(0, ProjectInformationPresentation.ExperimentsLabel, ProjectInformationPresentation.ExperimentsTooltip);
        SetTabHeader(1, ProjectInformationPresentation.AnalysisResultsLabel, ProjectInformationPresentation.AnalysisResultsTooltip);
        SetTabHeader(2, ProjectInformationPresentation.SavedReportsLabel, ProjectInformationPresentation.SavedReportsTooltip);
        contentSummary.Text = ProjectInformationPresentation.ContentSummary(DataManager.Data.Count, DataManager.Results.Count, DataManager.Reports.Count);
        UpdateReportDetails();
    }

    static string MissingSuffix(AnalysisReport report)
    {
        var status = AnalysisReportPresentation.MissingResultStatus(report, ResolveResult);
        return string.IsNullOrWhiteSpace(status) ? "" : "\n" + status;
    }

    void SetTabHeader(int index, string label, string tooltip)
    {
        var tab = (TabItem)tabs.Items[index]!;
        tab.Header = new TextBlock { Text = label, FontSize = 12, TextWrapping = TextWrapping.NoWrap };
        ToolTip.SetTip(tab, tooltip);
    }

    static AnalysisResult? ResolveResult(string id) => DataManager.Results.FirstOrDefault(result => result.UniqueID == id);
    bool ReportBelongsToProject(AnalysisReport? report) => CurrentProject && report != null && DataManager.Reports.Contains(report);
    bool CanOpenReport(AnalysisReport? report) => ReportBelongsToProject(report)
        && AnalysisReportPresentation.MissingResultCount(report!, ResolveResult) == 0;

    void UpdateReportDetails()
    {
        var report = SelectedReport;
        var exists = ReportBelongsToProject(report);
        var canOpen = CanOpenReport(report);
        openButton.IsEnabled = renameButton.IsEnabled = canOpen;
        deleteButton.IsEnabled = detailsButton.IsEnabled = copyAllButton.IsEnabled = exists;
        overviewPanel.Children.Clear();
        if (exists)
            foreach (var row in ProjectInformationPresentation.OverviewRows(report!, ResolveResult))
                overviewPanel.Children.Add(OverviewRow(row));
        overviewBorder.IsVisible = exists;
        ToolTip.SetTip(openButton, exists && !canOpen ? ProjectInformationPresentation.MissingResultsTooltip : ProjectInformationPresentation.OpenTooltip);
        ToolTip.SetTip(renameButton, exists && !canOpen ? ProjectInformationPresentation.MissingResultsTooltip : ProjectInformationPresentation.RenameTooltip);
    }

    internal void OpenSelectedReport()
    {
        var report = SelectedReport;
        if (CanOpenReport(report)) Close(report);
    }

    internal async Task RenameSelectedAsync(Func<AnalysisReport, Task<string?>>? prompt = null)
    {
        var report = SelectedReport;
        if (!CanOpenReport(report)) return;
        var name = prompt != null ? await prompt(report!) : await new ReportRenameWindow(report!.Name).ShowDialog<string?>(this);
        if (name == null || !CanOpenReport(report)) return;
        ProjectInformationPresentation.RenameReport(report!, name);
        RefreshContent();
    }

    internal async Task DeleteSelectedAsync(Func<AnalysisReport, Task<bool>>? confirm = null)
    {
        var report = SelectedReport;
        if (!ReportBelongsToProject(report)) return;
        var accepted = confirm != null ? await confirm(report!)
            : await ConfirmationDialogWindow.ConfirmDeleteReportAsync(this, report!);
        if (!accepted || !ReportBelongsToProject(report)) return;
        DataManager.RemoveReport(report);
        RefreshContent();
    }

    internal async Task CopyAllAsync()
    {
        var report = SelectedReport;
        if (ReportBelongsToProject(report) && Clipboard != null)
            await Clipboard.SetTextAsync(ProjectInformationPresentation.CopyText(report!));
    }

    internal async Task ShowDetailsAsync()
    {
        var report = SelectedReport;
        if (detailsOpen || !ReportBelongsToProject(report)) return;
        detailsOpen = true;
        try { await new ReportDetailsWindow(report!).ShowDialog(this); }
        finally { detailsOpen = false; }
    }

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        DataManager.DataDidChange -= OnProjectChanged;
        DataManager.ReportsDidChange -= OnReportsChanged;
        foreach (var item in observed) item.ContentChanged -= OnContentChanged;
        observed.Clear();
        base.OnClosed(e);
    }
}

internal sealed record ProjectInformationEntry(string Name, string Detail, AnalysisReport? Report = null);

internal sealed class ReportDetailsWindow : Window
{
    readonly Guid documentId = DocumentDirtyTracker.CurrentDocumentId;
    readonly AnalysisReport report;
    readonly TextBox detailsBox = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalContentAlignment = VerticalAlignment.Top,
        [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled,
        [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
    };
    readonly Button copyAllButton = new() { Content = ProjectInformationPresentation.CopyAllLabel, MinWidth = 82 };
    bool closed;

    internal TextBox DetailsTextBox => detailsBox;
    internal Button CopyAllButton => copyAllButton;
    bool CurrentReport => !closed && documentId == DocumentDirtyTracker.CurrentDocumentId && DataManager.Reports.Contains(report);

    public ReportDetailsWindow(AnalysisReport report)
    {
        this.report = report;
        Title = ProjectInformationPresentation.DetailsWindowTitle;
        Width = 740;
        Height = 620;
        MinWidth = 620;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AppTheme.Bind(this, BackgroundProperty, AppTheme.WorkspaceBackground);
        AppTheme.Bind(detailsBox, ForegroundProperty, AppTheme.PrimaryText);
        ToolTip.SetTip(detailsBox, ProjectInformationPresentation.DetailsTextTooltip);
        ToolTip.SetTip(copyAllButton, ProjectInformationPresentation.CopyAllTooltip);
        copyAllButton.Click += async (_, _) => await CopyAllAsync();
        var close = new Button { Content = ProjectInformationPresentation.CloseLabel, MinWidth = 82, IsCancel = true };
        ToolTip.SetTip(close, ProjectInformationPresentation.DetailsCloseTooltip);
        close.Click += (_, _) => Close();
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, Margin = new Thickness(12, 10) };
        footer.Children.Add(copyAllButton);
        Grid.SetColumn(close, 2);
        footer.Children.Add(close);
        var footerBorder = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        AppTheme.Bind(footerBorder, Border.BackgroundProperty, AppTheme.PanelBackground);
        AppTheme.Bind(footerBorder, Border.BorderBrushProperty, AppTheme.PanelBorder);
        var root = new DockPanel { LastChildFill = true };
        AppTheme.Bind(root, Panel.BackgroundProperty, AppTheme.WorkspaceBackground);
        DockPanel.SetDock(footerBorder, Dock.Bottom);
        root.Children.Add(footerBorder);
        var textBorder = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(12, 10), Margin = new Thickness(12), Child = detailsBox };
        AppTheme.Bind(textBorder, Border.BackgroundProperty, AppTheme.PanelBackground);
        AppTheme.Bind(textBorder, Border.BorderBrushProperty, AppTheme.PanelBorder);
        root.Children.Add(textBorder);
        Content = root;
        DataManager.DataDidChange += OnProjectChanged;
        DataManager.ReportsDidChange += OnReportsChanged;
        report.ContentChanged += OnReportsChanged;
        Opened += (_, _) => RefreshDetails();
        RefreshDetails();
    }

    void OnProjectChanged(object? sender, ExperimentData e) => QueueRefresh();
    void OnReportsChanged(object? sender, EventArgs e) => QueueRefresh();

    void QueueRefresh()
    {
        if (Dispatcher.UIThread.CheckAccess()) RefreshDetails();
        else Dispatcher.UIThread.Post(RefreshDetails);
    }

    void RefreshDetails()
    {
        if (closed) return;
        if (!CurrentReport) { Close(); return; }
        detailsBox.Text = ProjectInformationPresentation.DetailsText(report,
            id => DataManager.Results.FirstOrDefault(result => result.UniqueID == id));
    }

    internal async Task CopyAllAsync()
    {
        if (CurrentReport && Clipboard != null)
            await Clipboard.SetTextAsync(ProjectInformationPresentation.CopyText(report));
    }

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        DataManager.DataDidChange -= OnProjectChanged;
        DataManager.ReportsDidChange -= OnReportsChanged;
        report.ContentChanged -= OnReportsChanged;
        base.OnClosed(e);
    }
}

internal sealed class ReportRenameWindow : Window
{
    public ReportRenameWindow(string name)
    {
        Title = ProjectInformationPresentation.RenameDialogTitle;
        Width = 480;
        Height = 175;
        MinWidth = 360;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AppTheme.Bind(this, BackgroundProperty, AppTheme.PanelBackground);
        var nameBox = new TextBox { Text = name, HorizontalAlignment = HorizontalAlignment.Stretch };
        ToolTip.SetTip(nameBox, ProjectInformationPresentation.NameTooltip);
        var cancel = new Button { Content = ProjectInformationPresentation.CancelLabel, MinWidth = 82, IsCancel = true };
        ToolTip.SetTip(cancel, ProjectInformationPresentation.CancelTooltip);
        cancel.Click += (_, _) => Close();
        var rename = new Button { Content = ProjectInformationPresentation.RenameLabel, MinWidth = 82, IsDefault = true };
        ToolTip.SetTip(rename, ProjectInformationPresentation.RenameTooltip);
        rename.Click += (_, _) => Close(nameBox.Text ?? "");
        var label = new TextBlock { Text = ProjectInformationPresentation.NameLabel };
        ToolTip.SetTip(label, ProjectInformationPresentation.NameTooltip);
        Content = new StackPanel
        {
            Margin = new Thickness(18), Spacing = 10,
            Children =
            {
                label, nameBox,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { cancel, rename } },
            },
        };
        Opened += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };
    }
}
