using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Avalonia.Details;
using AnalysisITC.Avalonia.Dialogs;
using AnalysisITC.Avalonia.Help;
using AnalysisITC.Avalonia.ListItems;
using AnalysisITC.Avalonia.Menus;
using AnalysisITC.Avalonia.Preferences;
using AnalysisITC.Avalonia.Printing;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Avalonia.Support;
using AnalysisITC.Avalonia.Tools;

namespace AnalysisITC.Avalonia;

public partial class MainWindow : Window
{
    List<DataListEntry> entries = new List<DataListEntry>();
    ITCDataContainer? selectedItem;
    AppMenuController? menuController;
    bool overviewShowsRawData = true;
    bool allowDirtyClose;
    bool isHandlingDirtyClose;
    bool isReloadingLastFile;
    bool autoSaveInitialized;
    bool isRestoringDataListSelection;
    int activeExperimentWorkspaceIndex;

    public MainWindow()
    {
        InitializeComponent();
        using (var iconStream = AppAssetLoader.Open("Resources/appicon.ico"))
            Icon = new WindowIcon(iconStream);

        IncludeAllButton.Click += (_, _) => SetAllExperimentInclusion(true);
        IncludeNoneButton.Click += (_, _) => SetAllExperimentInclusion(false);
        WelcomeOpenButton.Click += async (_, _) => await OpenFilesAsync();
        WelcomeReloadButton.Click += async (_, _) => await ReloadLastFilesAsync();
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDragLeaveHandler(this, OnDragLeave);
        DragDrop.AddDropHandler(this, OnDrop);
        ItemsList.SelectionChanged += (_, _) =>
        {
            if (!isRestoringDataListSelection) SelectListItem();
        };
        ItemsList.PointerReleased += OnItemsListPointerReleased;
        ItemsList.DoubleTapped += OnItemsListDoubleTapped;
        ItemsList.KeyDown += OnItemsListKeyDown;
        WorkspaceTabs.SelectionChanged += (_, _) => OnWorkspaceTabChanged();
        OverviewRawButton.Click += (_, _) => SelectOverviewMode(rawData: true);
        OverviewInjectionsButton.Click += (_, _) => SelectOverviewMode(rawData: false);
        ProcessingWorkspace.StatusChanged += OnProcessingStatusChanged;
        ProcessingWorkspace.ProcessingChanged += OnProcessingChanged;
        AnalysisWorkspace.StatusChanged += OnAnalysisStatusChanged;
        AnalysisWorkspace.GraphChanged += OnAnalysisGraphChanged;
        AnalysisWorkspace.FittingChanged += OnAnalysisFittingChanged;
        FinalFigureWorkspace.StatusChanged += OnFinalFigureStatusChanged;
        ResultWorkspace.StatusChanged += OnResultStatusChanged;
        ResultWorkspace.ResultUpdated += OnResultUpdated;
        ResultWorkspace.ActiveGraphChanged += OnActiveGraphChanged;

        DataManager.DataDidChange += OnDataDidChange;
        DataManager.DataInclusionDidChange += OnDataInclusionDidChange;
        DataManager.UpdateTable += OnDataManagerUpdate;
        DocumentDirtyTracker.Initialize();
        DocumentDirtyTracker.MarkClean();
        DocumentDirtyTracker.DirtyStateChanged += OnDirtyStateChanged;
        FTITCFormat.CurrentAccessedAppDocumentPathChanged += OnCurrentDocumentPathChanged;
        StatusBarManager.StatusUpdated += OnStatusUpdated;
        StatusBarManager.SecondaryStatusUpdated += OnSecondaryStatusUpdated;
        StatusBarManager.ProgressUpdate += OnProgressUpdated;
        AppEventHandler.ShowAppMessage += OnAppMessage;

        menuController = new AppMenuController(this);
        menuController.Install();

        activeExperimentWorkspaceIndex = Math.Max(WorkspaceTabs.SelectedIndex, 0);
        RefreshDataList();
        UpdateDocumentStatus();
        UpdateSelection(null);
        SetStatus("Ready");
        AppVersion.CheckForUpdatesInBackground();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (allowDirtyClose || !DocumentDirtyTracker.IsDirty)
        {
            allowDirtyClose = false;
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (!isHandlingDirtyClose)
            _ = CloseWithDirtyPromptAsync(SavePromptReason.CloseWindow);
    }

    protected override void OnClosed(EventArgs e)
    {
        AutoSaveManager.Shared.StopCleanly();
        DragDrop.RemoveDragOverHandler(this, OnDragOver);
        DragDrop.RemoveDragLeaveHandler(this, OnDragLeave);
        DragDrop.RemoveDropHandler(this, OnDrop);
        ItemsList.DoubleTapped -= OnItemsListDoubleTapped;
        ItemsList.KeyDown -= OnItemsListKeyDown;
        DataManager.DataDidChange -= OnDataDidChange;
        DataManager.DataInclusionDidChange -= OnDataInclusionDidChange;
        DataManager.UpdateTable -= OnDataManagerUpdate;
        DocumentDirtyTracker.DirtyStateChanged -= OnDirtyStateChanged;
        FTITCFormat.CurrentAccessedAppDocumentPathChanged -= OnCurrentDocumentPathChanged;
        StatusBarManager.StatusUpdated -= OnStatusUpdated;
        StatusBarManager.SecondaryStatusUpdated -= OnSecondaryStatusUpdated;
        StatusBarManager.ProgressUpdate -= OnProgressUpdated;
        AppEventHandler.ShowAppMessage -= OnAppMessage;
        ProcessingWorkspace.StatusChanged -= OnProcessingStatusChanged;
        ProcessingWorkspace.ProcessingChanged -= OnProcessingChanged;
        AnalysisWorkspace.StatusChanged -= OnAnalysisStatusChanged;
        AnalysisWorkspace.GraphChanged -= OnAnalysisGraphChanged;
        AnalysisWorkspace.FittingChanged -= OnAnalysisFittingChanged;
        FinalFigureWorkspace.StatusChanged -= OnFinalFigureStatusChanged;
        ResultWorkspace.StatusChanged -= OnResultStatusChanged;
        ResultWorkspace.ResultUpdated -= OnResultUpdated;
        ResultWorkspace.ActiveGraphChanged -= OnActiveGraphChanged;

        base.OnClosed(e);
    }

    internal async Task InitializeAutoSaveAndRecoveryAsync()
    {
        if (autoSaveInitialized) return;
        autoSaveInitialized = true;

        AutoSaveManager.Shared.Start();
        if (!AppSettings.PromptForAutoSaveRecovery) return;

        var candidate = AutoSaveManager.Shared.GetNewestRecoveryCandidate();
        if (candidate == null) return;

        string? errorMessage = null;
        while (true)
        {
            var dialog = new AutoSaveRecoveryWindow(candidate, errorMessage);
            var action = await dialog.ShowDialog<AutoSaveRecoveryAction?>(this);

            if (action == AutoSaveRecoveryAction.Discard)
            {
                AutoSaveManager.Shared.ResolveRecovery(candidate, deleteFile: true);
                return;
            }

            if (action != AutoSaveRecoveryAction.Recover) return;

            if (await DataReader.ReadRecoveryFileAsync(candidate.FilePath))
            {
                AutoSaveManager.Shared.ResolveRecovery(candidate, deleteFile: false);
                RefreshDataList();
                UpdateDocumentStatus();
                RefreshMenuState();
                StatusBarManager.SetStatus("Recovered autosaved project", 5000);
                return;
            }

            errorMessage = "The autosave could not be recovered. You can open its folder, discard it, or try again.";
        }
    }

    internal Menu MenuHost => InWindowMenu;
    internal AppMenuController MenuController => menuController!;
    internal IReadOnlyList<DataListEntry> DataListEntries => entries;

    internal bool HasDocumentContent() => DataManager.SourceItems.Count > 0;
    internal bool HasDataLoaded() => DataManager.DataIsLoaded;
    internal bool HasSelectedItem() => selectedItem != null;
    internal bool HasSelectedExperiment() => selectedItem is ExperimentData;
    internal bool HasSelectedResult() => selectedItem is AnalysisResult;
    internal bool HasAnyResults() => DataManager.Results.Count > 0;
    internal bool HasAnyProcessedData() => DataManager.AnyDataIsBaselineProcessed;
    internal bool CanUndoDelete() => StateManager.StateCanUndo();
    internal bool HasExperimentsWithAttributes() => DataManager.Data.Any(data => data.Attributes.Count > 0);
    internal bool CanOpenBufferSubtractionTool() => DataManager.Data.Count >= 2;
    internal bool CanOpenTandemMergerTool() => DataManager.TandemMergerToolEnabled;
    internal bool CanEnableAnyExperiment() => DataManager.Data.Any(data => !data.Include);
    internal bool CanDisableAnyExperiment() => DataManager.Data.Any(data => data.Include);
    internal bool SelectedExperimentHasAttributes() => selectedItem is ExperimentData data && data.Attributes.Count > 0;
    internal bool SelectedExperimentHasSolution() => selectedItem is ExperimentData data && data.Solution != null;
    internal bool SelectedExperimentCanToggleInclusion() => selectedItem is ExperimentData data && data.Processor?.IntegrationCompleted == true;
    internal bool SelectedExperimentIsIncluded() => selectedItem is ExperimentData data && data.Include;
    internal bool SelectedResultHasSolution() => selectedItem is AnalysisResult result && result.Solution != null;
    internal bool SelectedResultHasMemberSolutions() => selectedItem is AnalysisResult result && result.Solution?.Solutions?.Count > 0;
    internal bool SelectedResultCanUpdate() => selectedItem is AnalysisResult result && result.Solution?.Model != null;

    internal Task OpenFilesFromMenuAsync() => OpenFilesAsync();

    internal async Task SaveDocumentAsync()
    {
        await SaveCurrentDocumentAsync(forcePrompt: false);
    }

    internal async Task SaveDocumentAsAsync()
    {
        await SaveCurrentDocumentAsync(forcePrompt: true);
    }

    internal async Task SaveSelectedAsync()
    {
        if (selectedItem == null) return;

        await ProjectWriter.SaveSelectedAsync(selectedItem);

        UpdateDocumentStatus();
        RefreshMenuState();
    }

    internal async Task ClearDataWithConfirmationAsync()
    {
        await TryClearDataWithConfirmationAsync();
    }

    async Task<bool> TryClearDataWithConfirmationAsync()
    {
        if (!HasDocumentContent()) return true;

        if (DocumentDirtyTracker.IsDirty)
        {
            if (!await PromptSaveChangesIfNeededAsync(SavePromptReason.ClearAllData))
                return false;
        }
        else
        {
            if (!await ConfirmAsync(
                "Remove All Data/Results",
                "Are you sure you want to remove all loaded data and analysis results?",
                "Keep",
                "Remove"))
                return false;
        }

        ClearData();
        return true;
    }

    internal async Task ExportDataAsync(bool selectedOnly)
    {
        await Exporter.ExportAsync(null, selectedOnly ? ExportDataSelection.SelectedData : null);
        RefreshMenuState();
    }

    internal async Task ExportPeaksAsync()
    {
        await Exporter.ExportAsync(ExportType.Peaks);
        RefreshMenuState();
    }

    internal async Task ExportFinalFigureAsync()
    {
        await FinalFigureWorkspace.ExportPdfAsync();
        RefreshMenuState();
    }

    internal Task UndoDeleteAsync()
    {
        StateManager.Undo();
        RefreshDataList();
        RefreshMenuState();
        return Task.CompletedTask;
    }

    internal Task DuplicateSelectedDataAsync()
    {
        if (selectedItem is not ExperimentData experiment) return Task.CompletedTask;

        DataManager.DuplicateSelectedData(experiment);
        RefreshDataList();
        RefreshMenuState();
        return Task.CompletedTask;
    }

    internal Task CopyAttributesToAllAsync()
    {
        DataManager.CopySelectedAttributesToAll();
        StatusBarManager.SetStatus("Attributes copied to all experiments", 3000);
        RefreshMenuState();
        return Task.CompletedTask;
    }

    internal async Task OpenAttributeOperationsAsync()
    {
        if (selectedItem is not ExperimentData experiment) return;

        await AttributeOperationsWindow.ShowAsync(this, experiment);
        RefreshOverview(experiment);
        RefreshDataList();
        InvalidateFinalFigurePreview();
        RefreshMenuState();
    }

    internal async Task ClearSelectedAttributesAsync()
    {
        if (selectedItem is not ExperimentData experiment || experiment.Attributes.Count == 0) return;

        if (!await ConfirmAsync(
            "Clear Attributes",
            $"Are you sure you want to remove all attributes from {experiment.Name}?",
            "Keep",
            "Clear"))
            return;

        experiment.ClearAttributes();
        DataManager.InvokeUpdateDataViewCells();
        DataManager.InvokeUpdateTable();
        RefreshOverview(experiment);
        RefreshDataList();
        RefreshMenuState();
    }

    internal async Task ClearProcessingResultsAsync()
    {
        if (!HasAnyResults()) return;

        if (!await ConfirmAsync(
            "Clear Processing/Results",
            $"Are you sure you want to remove all {DataManager.Results.Count} analysis results?",
            "Keep",
            "Remove"))
            return;

        DataManager.ClearProcessing();
        RefreshDataList();
        InvalidateFinalFigurePreview();
        RefreshMenuState();
    }

    internal Task SetAllExperimentInclusionAsync(bool include)
    {
        SetAllExperimentInclusion(include);
        return Task.CompletedTask;
    }

    internal Task InvertExperimentInclusionAsync()
    {
        foreach (var data in DataManager.Data)
            data.Include = !data.Include;

        DataManager.InvokeDataInclusionDidChange();
        return Task.CompletedTask;
    }

    internal Task SortDataAsync(DataManager.SortMode mode)
    {
        DataManager.SortContent(mode);
        RefreshDataList();
        RefreshMenuState();
        return Task.CompletedTask;
    }

    internal Task OpenSelectedDetailsFromMenuAsync() => OpenSelectedDetailsAsync();

    internal Task ToggleSelectedExperimentInclusionAsync()
    {
        if (selectedItem is ExperimentData experiment)
            experiment.ToggleInclude();

        return Task.CompletedTask;
    }

    internal async Task ClearSelectedExperimentSolutionAsync()
    {
        if (selectedItem is not ExperimentData experiment || experiment.Solution == null) return;

        if (!await ConfirmAsync(
            "Clear Solution",
            $"Are you sure you want to clear the fitted solution for {experiment.Name}?",
            "Keep",
            "Clear"))
            return;

        experiment.RemoveModel();
        RefreshOverview();
        InvalidateFinalFigurePreview();
        RefreshMenuState();
    }

    internal async Task RemoveSelectedItemAsync()
    {
        if (selectedItem != null)
            await RemoveItemAsync(selectedItem);
    }

    async Task RemoveItemAsync(ITCDataContainer item)
    {
        var itemIndex = DataManager.SourceItems.ToList().IndexOf(item);
        if (itemIndex < 0) return;

        var itemType = item is AnalysisResult ? "Result" : "Data";
        var itemName = string.IsNullOrWhiteSpace(item.Name) ? item.FileName : item.Name;

        if (!await ConfirmAsync(
            $"Remove {itemType}",
            $"Are you sure you want to remove {itemName}?",
            "Keep",
            "Remove"))
            return;

        DataManager.RemoveSourceItemAt(itemIndex);
        RefreshDataList();
        RefreshMenuState();
    }

    internal Task CopyResultTableAsync()
    {
        if (selectedItem is AnalysisResult result)
        {
            Exporter.CopyToClipboard(result, AppSettings.EnergyUnitFamily, energyUnitOverride: null, usekelvin: false);
            StatusBarManager.SetStatus("Result table copied", 3000);
        }

        return Task.CompletedTask;
    }

    internal Task LoadSelectedResultSolutionsAsync()
    {
        if (selectedItem is AnalysisResult result)
        {
            DataManager.LoadResultSolutionsToExperiments(result);
            DataManager.InvokeDataDidChange();
            DataManager.InvokeUpdateTable();
            RefreshDataList();
            InvalidateFinalFigurePreview();
            StatusBarManager.SetStatus("Result solutions loaded into experiments", 3000);
        }

        return Task.CompletedTask;
    }

    internal Task SelectResultExperimentsAsync()
    {
        if (selectedItem is not AnalysisResult result) return Task.CompletedTask;

        var ids = result.Solution.Solutions
            .Select(solution => solution.Data?.UniqueID)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet();

        foreach (var data in DataManager.Data)
            data.Include = ids.Contains(data.UniqueID);

        DataManager.InvokeDataInclusionDidChange();
        StatusBarManager.SetStatus("Experiments used by result selected", 3000);

        return Task.CompletedTask;
    }

    internal bool CanPrintActiveGraph() => TryGetActivePrintTarget(out _);

    internal async Task PrintActiveGraphAsync()
    {
        if (!TryGetActivePrintTarget(out var target) || target == null) return;
        await GraphPrintCoordinator.PrintAsync(this, target);
    }

    bool TryGetActivePrintTarget(out GraphPrintTarget? target)
    {
        target = null;

        if (selectedItem is AnalysisResult)
            return ResultWorkspace.TryGetPrintTarget(out target);

        if (selectedItem is not ExperimentData experiment || !WorkspaceTabs.IsVisible)
            return false;

        return WorkspaceTabs.SelectedIndex switch
        {
            0 when overviewShowsRawData && experiment.HasThermogram =>
                SetPrintTarget(GraphPrintTarget.FromVisual($"{experiment.Name} – Overview", OverviewThermogram), out target),
            1 => ProcessingWorkspace.TryGetPrintTarget(out target),
            2 => AnalysisWorkspace.TryGetPrintTarget(out target),
            3 => FinalFigureWorkspace.TryGetPrintTarget(out target),
            _ => false
        };
    }

    static bool SetPrintTarget(GraphPrintTarget value, out GraphPrintTarget? target)
    {
        target = value;
        return true;
    }

    internal async Task ShowAboutAsync()
    {
        await AboutDialogWindow.ShowAsync(this);
    }

    internal async Task OpenPreferencesAsync()
    {
        var dialog = new PreferencesWindow();
        var applied = await dialog.ShowDialog<bool?>(this);
        if (applied == true || dialog.Applied)
            RefreshAfterPreferencesApplied();
    }

    internal async Task OpenHelpGuideAsync()
    {
        await HelpWindow.ShowAsync(this, "Help and Guide", "HelpTextResource.txt");
    }

    internal async Task OpenTechnicalHelpAsync()
    {
        await HelpWindow.ShowAsync(this, "Technical Details", "ScienceHelpResource.txt");
    }

    internal async Task OpenCitationAsync()
    {
        await CitationWindow.ShowAsync(this);
    }

    internal async Task OpenSupportAsync()
    {
        await SupportWindow.ShowAsync(this);
    }

    internal Task CopySupportReportAsync()
    {
        SupportWindow.CopyReportToClipboard();
        StatusBarManager.SetStatus("Support report copied", 3000);
        return Task.CompletedTask;
    }

    internal Task OpenSourceRepositoryAsync()
    {
        OpenExternalLink(CitationInfo.SoftwareRepositoryUrl, "source repository");

        return Task.CompletedTask;
    }

    internal Task OpenWebsiteAsync()
    {
        OpenExternalLink(CitationInfo.SoftwareWebsiteUrl, "FT-ITC Analysis website");

        return Task.CompletedTask;
    }

    internal Task OpenViewerAsync()
    {
        OpenExternalLink(CitationInfo.SoftwareViewerUrl, "FT-ITC Project Viewer");

        return Task.CompletedTask;
    }

    static void OpenExternalLink(string url, string destination)
    {
        if (!ExternalLinkLauncher.TryOpen(url))
            StatusBarManager.SetStatus($"Could not open {destination}", 3000);
    }

    internal async Task OpenExperimentDesignerAsync()
    {
        var dialog = new ExperimentDesignerWindow();
        await dialog.ShowDialog(this);
        RefreshMenuState();
    }

    internal async Task OpenBufferSubtractionToolAsync()
    {
        if (!CanOpenBufferSubtractionTool()) return;

        var dialog = new BufferSubtractionWindow();
        var applied = await dialog.ShowDialog<bool?>(this);
        if (applied == true || dialog.Applied)
            RefreshAfterAuxiliaryToolChange("Buffer subtraction applied");
        else
            RefreshMenuState();
    }

    internal async Task OpenTandemMergerAsync()
    {
        if (!CanOpenTandemMergerTool()) return;

        var dialog = new TandemMergerWindow();
        var created = await dialog.ShowDialog<bool?>(this);
        if (created == true || dialog.Created)
            RefreshAfterAuxiliaryToolChange("Tandem experiment created");
        else
            RefreshMenuState();
    }

    internal async Task OpenAnalysisResultExporterAsync()
    {
        if (!HasAnyResults()) return;

        var dialog = new AnalysisResultExporterWindow();
        await dialog.ShowDialog(this);
        RefreshMenuState();
    }

    internal async Task OpenSupportingFigureCanvasAsync()
    {
        if (!HasDocumentContent()) return;

        var dialog = new SupportingFigureCanvasWindow(FinalFigureWorkspace.GetOptionsSnapshot(), selectedItem);
        await dialog.ShowDialog(this);
        RefreshMenuState();
    }

    internal Task QuitAsync()
    {
        if (DocumentDirtyTracker.IsDirty)
        {
            if (!isHandlingDirtyClose)
                _ = CloseWithDirtyPromptAsync(SavePromptReason.QuitApplication);
        }
        else if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Close();
        }

        return Task.CompletedTask;
    }

    async Task<bool> ConfirmAsync(string title, string message, string cancelButton, string confirmButton)
    {
        if (!AppSettings.ConfirmRemoveDelete)
            return true;

        return await ConfirmationDialogWindow.ConfirmAsync(this, title, message, cancelButton, confirmButton);
    }

    void RefreshMenuState()
    {
        menuController?.Refresh();
    }

    async Task OpenFilesAsync()
    {
        var patterns = ITCFormatAttribute.GetAllExtensions()
            .Select(extension => "*" + extension)
            .ToList();

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open ITC Data",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ITC data") { Patterns = patterns },
                FilePickerFileTypes.All
            }
        });

        var paths = files
            .Select(GetLocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToArray();

        await OpenPathsAsync(paths);
    }

    async Task ReloadLastFilesAsync()
    {
        if (isReloadingLastFile) return;

        var paths = LastDocumentPaths().Where(File.Exists).ToArray();
        if (paths.Length == 0)
        {
            UpdateEmptyWorkspaceState();
            StatusBarManager.SetStatus("The last opened file is no longer available", 4000);
            return;
        }

        isReloadingLastFile = true;
        UpdateEmptyWorkspaceState();
        try
        {
            await OpenPathsAsync(paths);
        }
        finally
        {
            isReloadingLastFile = false;
            UpdateEmptyWorkspaceState();
        }
    }

    async Task OpenPathsAsync(string[] paths)
    {
        if (paths.Length == 0) return;

        if (paths.Any(path => DataReader.GetFormat(path) == ITCDataFormat.FTITC) && HasDocumentContent())
        {
            switch (await ProjectLoadDialogWindow.PromptAsync(this))
            {
                case ProjectLoadAction.Replace:
                    if (!await TryClearDataWithConfirmationAsync()) return;
                    break;
                case ProjectLoadAction.Append:
                    break;
                default:
                    return;
            }
        }

        SetStatus("Opening data...");
        var result = await DataReader.ReadPathsAsync(paths);
        RefreshDataList();
        SetOpenResultStatus(result);
        UpdateDocumentStatus();
        RefreshMenuState();
    }

    void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        StatusBarManager.SetStatus("Drop supported files to open", 0);
    }

    void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        StatusBarManager.ClearAppStatus();
    }

    async void OnDrop(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        StatusBarManager.ClearAppStatus();

        var files = e.DataTransfer.TryGetFiles();
        var paths = files?
            .OfType<IStorageFile>()
            .Select(GetLocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        var supportedPaths = paths
            .Where(path => DataReader.GetFormat(path) != ITCDataFormat.Unknown)
            .ToArray();

        if (supportedPaths.Length == 0)
        {
            StatusBarManager.SetStatus("No supported data files were dropped", 4000);
            return;
        }

        await OpenPathsAsync(supportedPaths);
    }

    internal Task OpenExternalPathsAsync(IEnumerable<string> paths)
    {
        var existingPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return OpenPathsAsync(existingPaths);
    }

    static IEnumerable<string> LastDocumentPaths()
    {
        var paths = AppSettings.LastDocumentPaths ?? Array.Empty<string>();
        if (paths.Length == 0 && !string.IsNullOrWhiteSpace(AppSettings.LastDocumentPath))
            paths = new[] { AppSettings.LastDocumentPath };

        return paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct();
    }

    static string? GetLocalPath(IStorageFile file)
    {
        var path = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) return path;

        return file.Path.IsFile ? file.Path.LocalPath : null;
    }

    async Task<bool> SaveCurrentDocumentAsync(bool forcePrompt)
    {
        if (!HasDocumentContent()) return false;

        var saved = forcePrompt || !ProjectWriter.IsSaved
            ? await ProjectWriter.SaveAsync()
            : await ProjectWriter.SaveWithPathAsync();

        UpdateDocumentStatus();
        RefreshMenuState();
        return saved;
    }

    void SetOpenResultStatus(DataReadResult result)
    {
        if (result.RequestedPathCount <= 0) return;

        if (!result.LoadedAny)
        {
            StatusBarManager.SetStatus("No compatible data opened", 4000);
            return;
        }

        var itemText = result.AddedItemCount == 1 ? "item" : "items";
        var fileText = result.LoadedPathCount == 1 ? "file" : "files";

        if (result.LoadedAllRequested)
        {
            StatusBarManager.SetStatus($"Opened {result.AddedItemCount} {itemText} from {result.LoadedPathCount} {fileText}", 3500);
            return;
        }

        StatusBarManager.SetStatus(
            $"Opened {result.AddedItemCount} {itemText}; {result.FailedOrSkippedPathCount} file{(result.FailedOrSkippedPathCount == 1 ? "" : "s")} skipped or failed",
            6000);
    }

    void ClearData()
    {
        selectedItem = null;
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.MarkClean();
        RefreshDataList();
        UpdateDocumentStatus();
        StatusBarManager.SetStatus("Data cleared", 3000);
        RefreshMenuState();
    }

    void RefreshDataList()
    {
        var previous = selectedItem;

        entries = DataManager.SourceItems
            .Select(DataListEntry.From)
            .ToList();

        var nextIndex = previous == null
            ? DataManager.SelectedContentIndex
            : entries.FindIndex(entry => ReferenceEquals(entry.Item, previous));

        if (nextIndex < 0 && entries.Count > 0) nextIndex = Math.Min(Math.Max(DataManager.SelectedContentIndex, 0), entries.Count - 1);

        isRestoringDataListSelection = true;
        try
        {
            ItemsList.ItemsSource = entries;
            ItemsList.SelectedIndex = nextIndex >= 0 && nextIndex < entries.Count ? nextIndex : -1;
        }
        finally
        {
            isRestoringDataListSelection = false;
        }

        UpdateListHeader();
        UpdateEmptyWorkspaceState();
        SelectListItem(forceRefresh: true);
        RefreshMenuState();
    }

    void UpdateEmptyWorkspaceState()
    {
        var isEmpty = entries.Count == 0;
        EmptyWorkspacePanel.IsVisible = isEmpty;
        if (!isEmpty) return;

        var lastPaths = LastDocumentPaths().Where(File.Exists).ToArray();
        WelcomeReloadButton.IsEnabled = !isReloadingLastFile && lastPaths.Length > 0;
        WelcomeLastFileText.Text = lastPaths.Length switch
        {
            0 => "No previous file is available to reload.",
            1 => $"Last file: {Path.GetFileName(lastPaths[0])}",
            _ => $"Reload {lastPaths.Length} files from the previous session."
        };
    }

    void SelectListItem(bool forceRefresh = false)
    {
        if (ItemsList.SelectedItem is not DataListEntry entry)
        {
            if (DataManager.SelectedContentIndex != -1)
                DataManager.SelectIndex(-1);

            if (forceRefresh || selectedItem != null)
                UpdateSelection(null);
            return;
        }

        var index = entries.IndexOf(entry);
        if (index < 0) return;

        var managerSelectionMatches = DataManager.SelectedContentIndex == index
            && index < DataManager.SourceItems.Count
            && ReferenceEquals(DataManager.SourceItems[index], entry.Item);
        if (!managerSelectionMatches)
            DataManager.SelectIndex(index);

        if (forceRefresh || !ReferenceEquals(selectedItem, entry.Item))
            UpdateSelection(entry.Item);
    }

    void OnInlineListActionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Row actions explicitly select their bound row. Suppress the bubbling
        // pointer event so ListBox selection cannot race the action.
        e.Handled = true;
    }

    async void OnListItemDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: DataListEntry entry }) return;

        SelectDataListEntry(entry);
        await OpenDetailsAsync(entry.Item);
        e.Handled = true;
    }

    async void OnListItemRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: DataListEntry entry }) return;

        SelectDataListEntry(entry);
        await RemoveItemAsync(entry.Item);
        e.Handled = true;
    }

    void SelectDataListEntry(DataListEntry entry)
    {
        if (!entries.Contains(entry)) return;

        if (ReferenceEquals(ItemsList.SelectedItem, entry))
        {
            if (!ReferenceEquals(selectedItem, entry.Item))
                SelectListItem(forceRefresh: true);
            return;
        }

        ItemsList.SelectedItem = entry;
    }

    void OnItemsListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right) return;

        var source = e.Source as Visual;
        var itemContainer = source?.FindAncestorOfType<ListBoxItem>();
        if (itemContainer?.DataContext is not DataListEntry entry) return;

        ShowDataListItemMenu(entry, itemContainer);
        e.Handled = true;
    }

    void OnDataListItemMoreRequested(object? sender, EventArgs e)
    {
        if (sender is not DataListItemControl { DataContext: DataListEntry entry } control) return;
        ShowDataListItemMenu(entry, control.MenuAnchor);
    }

    async void OnDataListItemRemoveRequested(object? sender, EventArgs e)
    {
        if (sender is not DataListItemControl { DataContext: DataListEntry entry }) return;

        await RemoveItemAsync(entry.Item);
    }

    async void OnItemsListDoubleTapped(object? sender, TappedEventArgs e)
    {
        var source = e.Source as Visual;
        var itemContainer = source?.FindAncestorOfType<ListBoxItem>();
        if (itemContainer?.DataContext is not DataListEntry entry) return;

        SelectDataListEntry(entry);
        await OpenDetailsAsync(entry.Item);
        e.Handled = true;
    }

    async void OnItemsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ItemsList.SelectedItem is not DataListEntry entry) return;

        await OpenDetailsAsync(entry.Item);
        e.Handled = true;
    }

    void ShowDataListItemMenu(DataListEntry entry, Control anchor)
    {
        CreateDataListItemMenu(entry).ShowAt(anchor);
    }

    internal MenuFlyout CreateDataListItemMenu(DataListEntry entry)
    {
        SelectDataListEntry(entry);
        return menuController?.CreateSelectionContextFlyout() ?? new MenuFlyout();
    }

    void OnWorkspaceTabChanged()
    {
        if (WorkspaceTabs.IsVisible
            && selectedItem is ExperimentData
            && WorkspaceTabs.SelectedIndex >= 0
            && WorkspaceTabs.SelectedIndex < WorkspaceTabs.Items.Count)
        {
            activeExperimentWorkspaceIndex = WorkspaceTabs.SelectedIndex;
        }

        RefreshMenuState();
    }

    void SetAllExperimentInclusion(bool include)
    {
        DataManager.SetAllIncludeState(include);
    }

    void UpdateListHeader()
    {
        var experimentCount = DataManager.Data.Count;
        var includedCount = DataManager.IncludedData.Count();
        ItemCountText.Text = $"{entries.Count} item{(entries.Count == 1 ? "" : "s")}";
        IncludedCountText.Text = experimentCount == 0
            ? "No experiments"
            : $"{includedCount}/{experimentCount} included";
        IncludeAllButton.IsEnabled = experimentCount > 0 && includedCount < experimentCount;
        IncludeNoneButton.IsEnabled = experimentCount > 0 && includedCount > 0;
        RefreshMenuState();
    }

    void UpdateSelection(ITCDataContainer? item)
    {
        selectedItem = item;

        OverviewText.Text = item == null ? "No loaded data." : BuildOverview(item);
        OverviewTitleText.Text = item == null
            ? "No Selection"
            : string.IsNullOrWhiteSpace(item.Name) ? item.FileName : item.Name;
        RefreshOverview(item);
        ResultWorkspace.Result = item as AnalysisResult;
        UpdateFinalFigureContext(item);
        ProcessingWorkspace.Experiment = item as ExperimentData;
        AnalysisWorkspace.Experiment = item as ExperimentData;

        if (item is ExperimentData experiment)
        {
            WorkspaceTabs.IsVisible = true;
            ResultWorkspace.IsVisible = false;
            WorkspaceTabs.SelectedIndex = ValidExperimentWorkspaceIndex();
        }
        else
        {
            WorkspaceTabs.IsVisible = item is not AnalysisResult;
            ResultWorkspace.IsVisible = item is AnalysisResult;
            if (item == null)
                WorkspaceTabs.SelectedIndex = ValidExperimentWorkspaceIndex();
        }

        RefreshDataListEntryStates();
        RefreshMenuState();
    }

    int ValidExperimentWorkspaceIndex()
    {
        if (WorkspaceTabs.Items.Count == 0) return -1;
        return Math.Clamp(activeExperimentWorkspaceIndex, 0, WorkspaceTabs.Items.Count - 1);
    }

    void SelectOverviewMode(bool rawData)
    {
        overviewShowsRawData = rawData;
        OverviewRawButton.IsChecked = rawData;
        OverviewInjectionsButton.IsChecked = !rawData;
        UpdateOverviewVisibility();
        RefreshMenuState();
    }

    void UpdateOverviewVisibility()
    {
        OverviewRawHost.IsVisible = overviewShowsRawData;
        OverviewInjectionsHost.IsVisible = !overviewShowsRawData;
    }

    void RefreshOverview(ITCDataContainer? item = null)
    {
        item ??= selectedItem;
        var experiment = item as ExperimentData;

        OverviewRawButton.IsEnabled = experiment != null;
        OverviewInjectionsButton.IsEnabled = experiment != null;

        OverviewThermogram.Experiment = experiment?.HasThermogram == true ? experiment : null;
        OverviewThermogram.IsVisible = experiment?.HasThermogram == true;
        OverviewText.IsVisible = experiment?.HasThermogram != true;
        OverviewText.Text = item == null ? "No loaded data." : BuildOverview(item);
        BuildOverviewDescription(item);

        BuildOverviewInjectionTable(experiment);
        UpdateOverviewVisibility();
    }

    void BuildOverviewDescription(ITCDataContainer? item)
    {
        OverviewDescriptionPanel.Children.Clear();

        var lines = BuildOverviewDescriptionLines(item)
            .Select(PlainText)
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            OverviewDescriptionPanel.Children.Add(OverviewMessage("No loaded data."));
            return;
        }

        foreach (var line in lines)
            OverviewDescriptionPanel.Children.Add(OverviewDescriptionLine(line));
    }

    static IEnumerable<string> BuildOverviewDescriptionLines(ITCDataContainer? item)
    {
        if (item == null)
        {
            yield return "No loaded data.";
            yield break;
        }

        if (item is ExperimentData experiment)
        {
            foreach (var line in experiment.GetInfoString())
                yield return line;
            yield break;
        }

        if (item is AnalysisResult result)
        {
            foreach (var line in result.GetListDescriptionString().Split(new[] { Environment.NewLine }, StringSplitOptions.None))
                yield return line;

            yield return $"Date: {result.UILongDateWithTime}";
            yield return $"Solver: {result.Solution.Convergence?.Algorithm.GetProperties().Name ?? ""}";
            yield return $"Fitting: {(result.Solution.UseWeightedFitting ? "Weighted injection errors" : "Unweighted")}";
            yield break;
        }

        yield return item.Name;
    }

    static Control OverviewDescriptionLine(string line)
    {
        var separator = line.IndexOf(':');
        if (separator > 0 && separator < 28)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("135,*"),
                ColumnSpacing = 8
            };
            var label = new TextBlock
            {
                Text = line.Substring(0, separator).TrimEnd(),
                FontSize = 12
            };
            AppTheme.Bind(label, TextBlock.ForegroundProperty, AppTheme.MutedText);
            grid.Children.Add(label);
            var value = new TextBlock
            {
                Text = line.Substring(separator + 1).Trim(),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            AppTheme.Bind(value, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            return grid;
        }

        var textBlock = new TextBlock
        {
            Text = line,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
        return textBlock;
    }

    void BuildOverviewInjectionTable(ExperimentData? experiment)
    {
        OverviewInjectionTable.Children.Clear();

        if (experiment == null)
        {
            OverviewInjectionTable.Children.Add(OverviewMessage("No experiment selected."));
            return;
        }

        var table = ExperimentOverviewTable.Build(experiment);
        var columns = table.Columns.Where(column => column.IsVisible).ToList();
        if (columns.Count == 0 || table.Rows.Count == 0)
        {
            OverviewInjectionTable.Children.Add(OverviewMessage("No injections available."));
            return;
        }

        var grid = new Grid();
        AppTheme.Bind(grid, Panel.BackgroundProperty, AppTheme.PanelBackground);

        foreach (var column in columns)
            grid.ColumnDefinitions.Add(new ColumnDefinition(column.PreferredWidth, GridUnitType.Pixel));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int i = 0; i < table.Rows.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            AddOverviewCell(grid, columns[columnIndex].Title, columnIndex, 0, columns[columnIndex].Alignment, isHeader: true, isIncluded: true);

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                var column = columns[columnIndex];
                AddOverviewCell(grid, row[column.Id], columnIndex, rowIndex + 1, column.Alignment, isHeader: false, row.IsIncluded);
            }
        }

        OverviewInjectionTable.Children.Add(grid);
    }

    Control OverviewMessage(string message)
    {
        var textBlock = new TextBlock
        {
            Text = message,
            Margin = new Thickness(16),
            TextWrapping = TextWrapping.Wrap
        };
        AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.MutedText);
        return textBlock;
    }

    void AddOverviewCell(Grid grid, string text, int column, int row, ExperimentOverviewColumnAlignment alignment, bool isHeader, bool isIncluded)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            Margin = new Thickness(8, 5),
            FontSize = isHeader ? 11 : 12,
            FontWeight = isHeader ? FontWeight.SemiBold : AppTheme.BodyFontWeight,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignmentFor(alignment)
        };
        AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, !isHeader && !isIncluded ? AppTheme.DisabledText : AppTheme.PrimaryText);

        var border = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = textBlock,
            MinHeight = isHeader ? 30 : 28
        };
        AppTheme.Bind(border, Border.BorderBrushProperty, AppTheme.SectionBorder);
        AppTheme.Bind(border, Border.BackgroundProperty, isHeader
            ? AppTheme.TableHeaderBackground
            : row % 2 == 0 ? AppTheme.PanelBackground : AppTheme.TableAlternateRow);

        Grid.SetColumn(border, column);
        Grid.SetRow(border, row);
        grid.Children.Add(border);
    }

    static HorizontalAlignment HorizontalAlignmentFor(ExperimentOverviewColumnAlignment alignment)
    {
        return alignment switch
        {
            ExperimentOverviewColumnAlignment.Left => HorizontalAlignment.Left,
            ExperimentOverviewColumnAlignment.Center => HorizontalAlignment.Center,
            _ => HorizontalAlignment.Right,
        };
    }

    void UpdateFinalFigureContext(ITCDataContainer? item)
    {
        FinalFigureWorkspace.SelectedItem = item;
    }

    static string BuildShortSummary(ITCDataContainer item)
    {
        return item switch
        {
            ExperimentData experiment => $"{experiment.DataPoints.Count} points, {experiment.InjectionCount} injections, {Path.GetFileName(experiment.FileName)}",
            AnalysisResult result => BuildResultSummary(result),
            _ => item.GetType().Name,
        };
    }

    static string BuildOverview(ITCDataContainer item)
    {
        return item switch
        {
            ExperimentData experiment => string.Join(Environment.NewLine, new[]
            {
                $"Name: {experiment.Name}",
                $"File: {experiment.FileName}",
                $"Data points: {experiment.DataPoints.Count}",
                $"Injections: {experiment.InjectionCount}",
                $"Temperature: {experiment.MeasuredTemperature:F1} °C",
                $"Instrument: {experiment.Instrument}"
            }),
            AnalysisResult result => BuildResultSummary(result),
            _ => item.Name,
        };
    }

    static string BuildProcessSummary(ExperimentData experiment)
    {
        if (!experiment.HasThermogram) return "No raw thermogram is available for this item.";

        return $"{experiment.DataPoints.Count} thermogram points, {experiment.InjectionCount} injection markers";
    }

    static string BuildResultSummary(AnalysisResult result)
    {
        var fitCount = result.Solution?.Solutions?.Count ?? 0;
        return $"{result.Name}, {fitCount} fitted experiment{(fitCount == 1 ? "" : "s")}";
    }

    static string PlainText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        return string.Concat(MarkdownProcessor.GetSegments(text).Select(segment => segment.Text))
            .Replace("∆", "Δ")
            .TrimEnd();
    }

    void OnDataDidChange(object? sender, ExperimentData? e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshDataList();
            UpdateDocumentStatus();
        });
    }

    void OnDataInclusionDidChange(object? sender, ExperimentData? e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshDataListEntryStates();

            UpdateListHeader();
            InvalidateFinalFigurePreview();
            UpdateDocumentStatus();
        });
    }

    void OnDataManagerUpdate(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshDataList);
    }

    void RefreshDataListEntryStates()
    {
        var selectedResult = selectedItem as AnalysisResult;
        foreach (var entry in entries)
            entry.RefreshState(selectedResult);
    }

    void OnDirtyStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateDocumentStatus);
    }

    void OnCurrentDocumentPathChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateDocumentStatus);
    }

    void OnStatusUpdated(object? sender, string status)
    {
        Dispatcher.UIThread.Post(() => SetStatus(status));
    }

    void OnSecondaryStatusUpdated(object? sender, string status)
    {
        Dispatcher.UIThread.Post(() => SecondaryStatusText.Text = status ?? "");
    }

    void OnProgressUpdated(object? sender, ProgressIndicatorEventData progress)
    {
        var value = progress.Progress;
        if (Dispatcher.UIThread.CheckAccess()) SetProgressState(value);
        else Dispatcher.UIThread.Post(() => SetProgressState(value));
    }

    void OnAppMessage(object? sender, HandledException message)
    {
        Dispatcher.UIThread.Post(() => SetStatus($"{message.Title}: {message.Message}"));
    }

    void OnProcessingStatusChanged(object? sender, string status)
    {
        SetStatus(status);
    }

    void OnProcessingChanged(object? sender, EventArgs e)
    {
        RefreshDataListEntryStates();
        RefreshOverview();
        InvalidateFinalFigurePreview();
        RefreshMenuState();
    }

    void OnAnalysisStatusChanged(object? sender, string status)
    {
        SetStatus(status);
    }

    void OnAnalysisGraphChanged(object? sender, EventArgs e)
    {
        RefreshDataListEntryStates();
        RefreshOverview();
        InvalidateFinalFigurePreview();
        RefreshMenuState();
    }

    void OnAnalysisFittingChanged(object? sender, EventArgs e)
    {
        RefreshDataListEntryStates();
        RefreshOverview();
        InvalidateFinalFigurePreview();
    }

    void OnFinalFigureStatusChanged(object? sender, string status)
    {
        SetStatus(status);
    }

    void OnResultStatusChanged(object? sender, string status)
    {
        SetStatus(status);
    }

    void OnResultUpdated(object? sender, EventArgs e)
    {
        RefreshAfterResultUpdate();
    }

    void OnActiveGraphChanged(object? sender, EventArgs e)
    {
        RefreshMenuState();
    }

    internal async Task UpdateSelectedResultAsync()
    {
        if (selectedItem is not AnalysisResult result) return;

        ResultWorkspace.Result = result;
        await ResultWorkspace.UpdateResultAsync();
        RefreshAfterResultUpdate();
    }

    async Task OpenSelectedDetailsAsync()
    {
        if (selectedItem != null)
            await OpenDetailsAsync(selectedItem);
    }

    async Task OpenDetailsAsync(ITCDataContainer item)
    {
        switch (item)
        {
            case ExperimentData experiment:
            {
                var dialog = new ExperimentDetailsWindow(experiment);
                var applied = await dialog.ShowDialog<bool?>(this);
                if (applied == true || dialog.Applied)
                    RefreshAfterDetailsEdit();
                break;
            }
            case AnalysisResult result:
            {
                var dialog = new AnalysisResultDetailsWindow(result);
                var applied = await dialog.ShowDialog<bool?>(this);
                if (applied == true || dialog.Applied)
                    RefreshAfterDetailsEdit();
                break;
            }
        }
    }

    void RefreshAfterDetailsEdit()
    {
        RefreshDataList();
        RefreshOverview();
        ProcessingWorkspace.Experiment = selectedItem as ExperimentData;
        AnalysisWorkspace.Experiment = selectedItem as ExperimentData;
        ResultWorkspace.Refresh();
        AnalysisWorkspace.RefreshIncludedDataState();
        InvalidateFinalFigurePreview();
    }

    void RefreshAfterResultUpdate()
    {
        RefreshDataList();
        RefreshOverview();
        AnalysisWorkspace.RefreshIncludedDataState();
        ResultWorkspace.Refresh();
        InvalidateFinalFigurePreview();
        RefreshMenuState();
    }

    void RefreshAfterPreferencesApplied()
    {
        RefreshDataList();
        RefreshOverview();
        ProcessingWorkspace.Experiment = selectedItem as ExperimentData;
        // A family preference changes the display scale even when the selected
        // experiment instance itself did not change. Re-fit both thermogram
        // surfaces so their stored display-space view bounds use the new unit.
        ProcessingWorkspace.FitToData();
        OverviewThermogram.FitToData();
        AnalysisWorkspace.Experiment = selectedItem as ExperimentData;
        AnalysisWorkspace.RefreshIncludedDataState();
        ResultWorkspace.Refresh();
        FinalFigureWorkspace.ApplySettingsDefaults();
        InvalidateFinalFigurePreview();
        RefreshMenuState();
        StatusBarManager.SetStatus("Preferences updated", 2500);
    }

    void RefreshAfterAuxiliaryToolChange(string status)
    {
        RefreshDataList();
        RefreshOverview();
        ProcessingWorkspace.Experiment = selectedItem as ExperimentData;
        AnalysisWorkspace.Experiment = selectedItem as ExperimentData;
        AnalysisWorkspace.RefreshIncludedDataState();
        ResultWorkspace.Refresh();
        InvalidateFinalFigurePreview();
        RefreshMenuState();
        StatusBarManager.SetStatus(status, 3000);
    }

    void InvalidateFinalFigurePreview()
    {
        FinalFigureWorkspace.InvalidatePreview();
    }

    void SetStatus(string status)
    {
        StatusText.Text = status ?? "";
    }

    void SetProgressState(double progress)
    {
        if (progress < 0)
        {
            var isActiveIndeterminate = Math.Abs(Math.Abs(progress) - 1) > double.Epsilon;
            StatusProgressBar.IsVisible = isActiveIndeterminate;
            StatusProgressBar.IsIndeterminate = isActiveIndeterminate;
            StatusProgressText.IsVisible = false;
            StatusProgressText.Text = "";
            return;
        }

        if (progress >= 1)
        {
            StatusProgressBar.IsVisible = false;
            StatusProgressBar.IsIndeterminate = false;
            StatusProgressText.IsVisible = false;
            StatusProgressText.Text = "";
            return;
        }

        var percent = Math.Clamp(progress, 0, 1);
        StatusProgressBar.IsVisible = true;
        StatusProgressBar.IsIndeterminate = false;
        StatusProgressBar.Value = percent * 100;
        StatusProgressText.IsVisible = true;
        StatusProgressText.Text = percent.ToString("P0");
    }

    async Task CloseWithDirtyPromptAsync(SavePromptReason reason)
    {
        isHandlingDirtyClose = true;

        try
        {
            if (!await PromptSaveChangesIfNeededAsync(reason))
                return;

            allowDirtyClose = true;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else
                Close();
        }
        finally
        {
            isHandlingDirtyClose = false;
        }
    }

    async Task<bool> PromptSaveChangesIfNeededAsync(SavePromptReason reason)
    {
        if (!DocumentDirtyTracker.IsDirty) return true;

        switch (await SaveChangesDialogWindow.PromptAsync(this, reason))
        {
            case PendingSaveAction.Save:
                return await SaveCurrentDocumentAsync(forcePrompt: false);
            case PendingSaveAction.Discard:
                return true;
            default:
                return false;
        }
    }

    void UpdateDocumentStatus()
    {
        var documentStatus = GetDocumentStatusText();
        StatusBarManager.SetDefaultSecondaryStatus(documentStatus);
        Title = string.IsNullOrWhiteSpace(documentStatus)
            ? "FT-ITC Analysis"
            : $"FT-ITC Analysis - {documentStatus}";
    }

    static string GetDocumentStatusText()
    {
        if (DataManager.SourceItems == null || DataManager.SourceItems.Count == 0)
            return "";

        if (!ProjectWriter.IsSaved)
            return "Unsaved";

        var path = FTITCFormat.CurrentAccessedAppDocumentPath;
        var fileName = Path.GetFileName(path);
        if (string.Equals(Path.GetExtension(fileName), ".ftitc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(fileName), ".ftxtc", StringComparison.OrdinalIgnoreCase))
            fileName = Path.GetFileNameWithoutExtension(fileName);

        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "Unsaved";

        return DocumentDirtyTracker.IsDirty ? $"{fileName} [M]" : fileName;
    }

    enum PendingSaveAction
    {
        Save,
        Cancel,
        Discard
    }

    enum SavePromptReason
    {
        CloseWindow,
        QuitApplication,
        ClearAllData
    }

    enum ProjectLoadAction
    {
        Replace,
        Append,
        Cancel
    }

    sealed class ProjectLoadDialogWindow : Window
    {
        ProjectLoadDialogWindow()
        {
            Title = "Load Project";
            Width = 460;
            Height = 205;
            MinWidth = 380;
            MinHeight = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;

            var messageText = new TextBlock
            {
                Text = "You can replace the current data before loading this project, or append the project contents to what is already open.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            };
            AppTheme.Bind(messageText, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

            var replace = DialogButton("Replace Data");
            replace.Click += (_, _) => Close(ProjectLoadAction.Replace);

            var append = DialogButton("Append");
            append.Click += (_, _) => Close(ProjectLoadAction.Append);

            var cancel = DialogButton("Cancel");
            cancel.Click += (_, _) => Close(ProjectLoadAction.Cancel);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { replace, append, cancel }
            };

            var content = new Border
            {
                Padding = new Thickness(16),
                Child = new DockPanel
                {
                    LastChildFill = true,
                    Children =
                    {
                        buttons,
                        messageText
                    }
                }
            };
            AppTheme.Bind(content, Border.BackgroundProperty, AppTheme.PanelBackground);
            Content = content;

            DockPanel.SetDock(buttons, Dock.Bottom);
        }

        static Button DialogButton(string text) => new()
        {
            Content = text,
            MinWidth = 82,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        public static async Task<ProjectLoadAction> PromptAsync(Window owner)
        {
            var dialog = new ProjectLoadDialogWindow();
            return await dialog.ShowDialog<ProjectLoadAction>(owner);
        }
    }

    sealed class SaveChangesDialogWindow : Window
    {
        SaveChangesDialogWindow(SavePromptReason reason)
        {
            var (title, message, discardButtonText) = reason switch
            {
                SavePromptReason.QuitApplication => (
                    "Save Changes Before Quitting?",
                    "Unsaved changes will be lost if you quit without saving.",
                    "Don't Save"),
                SavePromptReason.ClearAllData => (
                    "Save Changes Before Clearing?",
                    "Clearing all data will remove the current project from the program. Unsaved changes will be lost.",
                    "Clear All"),
                _ => (
                    "Save Changes Before Closing?",
                    "Unsaved changes will be lost if you close without saving.",
                    "Don't Save")
            };

            Title = title;
            Width = 440;
            Height = 200;
            MinWidth = 380;
            MinHeight = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;

            var messageText = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            };
            AppTheme.Bind(messageText, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

            var save = DialogButton("Save");
            save.Click += (_, _) => Close(PendingSaveAction.Save);

            var cancel = DialogButton("Cancel");
            cancel.Click += (_, _) => Close(PendingSaveAction.Cancel);

            var discard = DialogButton(discardButtonText);
            discard.Click += (_, _) => Close(PendingSaveAction.Discard);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { save, cancel, discard }
            };

            var content = new Border
            {
                Padding = new Thickness(16),
                Child = new DockPanel
                {
                    LastChildFill = true,
                    Children =
                    {
                        buttons,
                        messageText
                    }
                }
            };
            AppTheme.Bind(content, Border.BackgroundProperty, AppTheme.PanelBackground);
            Content = content;

            DockPanel.SetDock(buttons, Dock.Bottom);
        }

        static Button DialogButton(string text) => new()
        {
            Content = text,
            MinWidth = 82,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        public static async Task<PendingSaveAction> PromptAsync(Window owner, SavePromptReason reason)
        {
            var dialog = new SaveChangesDialogWindow(reason);
            return await dialog.ShowDialog<PendingSaveAction>(owner);
        }
    }

    public sealed class DataListEntry : INotifyPropertyChanged
    {
        public ITCDataContainer Item { get; }
        readonly ExperimentData? experiment;
        AnalysisResultValidity validityStatus;
        string validityTooltip = "";
        bool isSelectedResultMember;
        bool isSelectedResultCurrentSolution;
        string selectedResultMembershipTooltip = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        DataListEntry(ITCDataContainer item, string kindLabel, string dateLine, string detailLine, string fitLine)
        {
            Item = item;
            experiment = item as ExperimentData;
            KindLabel = kindLabel;
            DateLine = dateLine;
            DetailLine = detailLine;
            FitLine = fitLine;
            UpdateValidityState();
        }

        public string Title => Item.Name;
        public string KindLabel { get; }
        public string DateLine { get; private set; }
        public string DetailLine { get; private set; }
        public string FitLine { get; private set; }
        public string DetailsLabel => Item is AnalysisResult ? "Open result details" : "Open data details";
        public string RemoveLabel => Item is AnalysisResult ? "Remove result" : "Remove data";
        public string MoreActionsLabel => $"More actions for {Title}";
        public bool CanInclude => experiment != null;
        public bool IsResult => Item is AnalysisResult;
        public bool CanIncludeActive => experiment?.Processor?.IntegrationCompleted == true;
        public string ActiveStateLabel => !CanIncludeActive
            ? "Not processed"
            : IsIncluded ? "Active" : "Inactive";
        public string InclusionLabel => !CanIncludeActive
            ? "Experiment is not processed"
            : IsIncluded ? "Deactivate experiment" : "Activate experiment";
        public bool IsIncluded
        {
            get => experiment?.Include == true;
            set
            {
                if (experiment == null || experiment.Include == value) return;
                experiment.ToggleInclude();
            }
        }

        public string ValidityLabel => validityStatus switch
        {
            AnalysisResultValidity.Valid => "Valid",
            AnalysisResultValidity.PartialInvalid => "Partial",
            AnalysisResultValidity.Invalid => "Invalid",
            _ => "Unknown"
        };

        public string ValidityTooltip => validityTooltip;
        public bool IsValidityValid => IsResult && validityStatus == AnalysisResultValidity.Valid;
        public bool IsValidityPartial => IsResult && validityStatus == AnalysisResultValidity.PartialInvalid;
        public bool IsValidityInvalid => IsResult && validityStatus == AnalysisResultValidity.Invalid;
        public bool IsValidityUnknown => IsResult && validityStatus == AnalysisResultValidity.Unknown;
        public bool IsSelectedResultMember => isSelectedResultMember;
        public bool IsSelectedResultCurrentSolution => isSelectedResultCurrentSolution;
        public string SelectedResultMembershipTooltip => selectedResultMembershipTooltip;

        internal void RefreshState(AnalysisResult? selectedResult)
        {
            UpdateDisplayState();
            UpdateValidityState();
            UpdateSelectedResultState(selectedResult);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncluded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanIncludeActive)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveStateLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InclusionLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DateLine)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailLine)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FitLine)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValidityLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValidityTooltip)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsValidityValid)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsValidityPartial)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsValidityInvalid)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsValidityUnknown)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedResultMember)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedResultCurrentSolution)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedResultMembershipTooltip)));
        }

        void UpdateDisplayState()
        {
            if (experiment == null) return;

            DateLine = experiment.UIShortDateWithTime;
            DetailLine = BuildExperimentDetailLine(experiment);
            FitLine = BuildExperimentSummaryLine(experiment);
        }

        void UpdateValidityState()
        {
            if (Item is not AnalysisResult result)
            {
                validityStatus = AnalysisResultValidity.Unknown;
                validityTooltip = "";
                return;
            }

            var report = result.ValidityReport;
            validityStatus = report.Status;
            var title = report.Status switch
            {
                AnalysisResultValidity.Valid => "Analysis result is valid for the current data.",
                AnalysisResultValidity.PartialInvalid => "Analysis result is partially invalid for the current data.",
                AnalysisResultValidity.Invalid => "Analysis result is invalid for the current data.",
                _ => "Analysis result validity is unknown."
            };
            validityTooltip = report.Reasons.Count == 0
                ? title
                : title + Environment.NewLine + string.Join(Environment.NewLine, report.Reasons);
        }

        void UpdateSelectedResultState(AnalysisResult? selectedResult)
        {
            isSelectedResultMember = false;
            isSelectedResultCurrentSolution = false;
            selectedResultMembershipTooltip = "";

            if (experiment == null || selectedResult?.Solution?.Solutions == null) return;

            var resultSolution = selectedResult.Solution.Solutions
                .FirstOrDefault(solution => ReferenceEquals(solution?.Data, experiment));

            if (resultSolution == null && !string.IsNullOrWhiteSpace(experiment.UniqueID))
            {
                resultSolution = selectedResult.Solution.Solutions.FirstOrDefault(solution =>
                    string.Equals(solution?.Data?.UniqueID, experiment.UniqueID, StringComparison.Ordinal));
            }

            if (resultSolution == null) return;

            isSelectedResultMember = true;
            isSelectedResultCurrentSolution = experiment.Solution != null
                && string.Equals(experiment.Solution.Guid, resultSolution.Guid, StringComparison.Ordinal);

            var resultName = string.IsNullOrWhiteSpace(selectedResult.Name)
                ? "the selected analysis result"
                : $"analysis result '{selectedResult.Name}'";
            selectedResultMembershipTooltip = isSelectedResultCurrentSolution
                ? $"Used in {resultName}. Its stored result solution is currently loaded on this experiment."
                : $"Used in {resultName}. Its stored result solution is not currently loaded on this experiment.";
        }

        public static DataListEntry From(ITCDataContainer item)
        {
            return item switch
            {
                ExperimentData experiment => FromExperiment(experiment),
                AnalysisResult result => FromResult(result),
                _ => new DataListEntry(item, item.GetType().Name, item.UIShortDateWithTime, BuildShortSummary(item), "")
            };
        }

        static DataListEntry FromExperiment(ExperimentData experiment)
        {
            return new DataListEntry(
                experiment,
                "DATA",
                experiment.UIShortDateWithTime,
                BuildExperimentDetailLine(experiment),
                BuildExperimentSummaryLine(experiment));
        }

        static string BuildExperimentDetailLine(ExperimentData experiment) =>
            $"{experiment.MeasuredTemperature:G3} °C | {experiment.SyringeConcentration.AsFormattedConcentration(true)} | {experiment.CellConcentration.AsFormattedConcentration(true)}";

        static string BuildExperimentSummaryLine(ExperimentData experiment)
        {
            var fit = BuildExperimentFitLine(experiment);
            if (!string.IsNullOrWhiteSpace(fit)) return fit;

            return experiment.Processor?.IntegrationCompleted == true
                ? $"{experiment.InjectionCount} integrated injections"
                : $"{experiment.InjectionCount} injections, not processed";
        }

        static DataListEntry FromResult(AnalysisResult result)
        {
            var description = PlainListText(result.GetListDescriptionString());
            var lines = description
                .Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            var dateLine = lines.Count > 0 ? lines[0] : BuildResultSummary(result);
            var detailLine = lines.Count > 1 ? lines[1] : "";
            var fitLine = lines.Count > 2 ? string.Join(" | ", lines.Skip(2)) : "";

            return new DataListEntry(result, "RESULT", dateLine, detailLine, fitLine);
        }

        static string BuildExperimentFitLine(ExperimentData experiment)
        {
            if (experiment.Solution == null) return "";

            var lines = new List<string>();
            foreach (var parameter in experiment.Solution.UISolutionParameters(FinalFigureDisplayParameters.ListView))
            {
                if (lines.Count == 0)
                    lines.Add($"{parameter.Item1} | RMSD = {parameter.Item2}");
                else
                    lines.Add($"{parameter.Item1} = {parameter.Item2}");
            }

            return PlainListText(string.Join(" | ", lines));
        }

        static string PlainListText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            return string.Concat(MarkdownProcessor.GetSegments(text).Select(segment => segment.Text))
                .Replace("∆", "Δ")
                .Trim();
        }

        public override string ToString() => Title;
    }
}
