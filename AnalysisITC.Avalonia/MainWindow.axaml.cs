using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Avalonia.Details;
using AnalysisITC.Avalonia.Dialogs;
using AnalysisITC.Avalonia.Menus;
using AnalysisITC.Avalonia.Preferences;

namespace AnalysisITC.Avalonia;

public partial class MainWindow : Window
{
    List<DataListEntry> entries = new List<DataListEntry>();
    ITCDataContainer? selectedItem;
    AppMenuController? menuController;
    bool isUpdatingOverviewMode;
    bool allowDirtyClose;
    bool isHandlingDirtyClose;

    public MainWindow()
    {
        InitializeComponent();

        OpenButton.Click += async (_, _) => await OpenFilesFromMenuAsync();
        ClearButton.Click += async (_, _) => await ClearDataWithConfirmationAsync();
        IncludeAllButton.Click += (_, _) => SetAllExperimentInclusion(true);
        IncludeNoneButton.Click += (_, _) => SetAllExperimentInclusion(false);
        OverviewRawButton.IsCheckedChanged += (_, _) => SelectOverviewMode(rawData: true);
        OverviewInjectionsButton.IsCheckedChanged += (_, _) => SelectOverviewMode(rawData: false);
        OverviewDetailsButton.Click += async (_, _) => await OpenSelectedDetailsAsync();
        ItemsList.SelectionChanged += (_, _) => SelectListItem();
        WorkspaceTabs.SelectionChanged += (_, _) => RefreshMenuState();
        ProcessingWorkspace.StatusChanged += OnProcessingStatusChanged;
        ProcessingWorkspace.ProcessingChanged += OnProcessingChanged;
        AnalysisWorkspace.StatusChanged += OnAnalysisStatusChanged;
        AnalysisWorkspace.GraphChanged += OnAnalysisGraphChanged;
        AnalysisWorkspace.FittingChanged += OnAnalysisFittingChanged;
        FinalFigureWorkspace.StatusChanged += OnFinalFigureStatusChanged;
        ResultWorkspace.StatusChanged += OnResultStatusChanged;
        ResultWorkspace.DetailsRequested += OnResultDetailsRequested;

        DataManager.DataDidChange += OnDataDidChange;
        DataManager.DataInclusionDidChange += OnDataInclusionDidChange;
        DataManager.UpdateTable += OnDataManagerUpdate;
        DataManager.UpdateViewCells += OnDataManagerUpdate;
        DocumentDirtyTracker.Initialize();
        DocumentDirtyTracker.MarkClean();
        DocumentDirtyTracker.DirtyStateChanged += OnDirtyStateChanged;
        FTITCFormat.CurrentAccessedAppDocumentPathChanged += OnCurrentDocumentPathChanged;
        StatusBarManager.StatusUpdated += OnStatusUpdated;
        StatusBarManager.SecondaryStatusUpdated += OnSecondaryStatusUpdated;
        AppEventHandler.ShowAppMessage += OnAppMessage;

        menuController = new AppMenuController(this);
        menuController.Install();

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
        DataManager.DataDidChange -= OnDataDidChange;
        DataManager.DataInclusionDidChange -= OnDataInclusionDidChange;
        DataManager.UpdateTable -= OnDataManagerUpdate;
        DataManager.UpdateViewCells -= OnDataManagerUpdate;
        DocumentDirtyTracker.DirtyStateChanged -= OnDirtyStateChanged;
        FTITCFormat.CurrentAccessedAppDocumentPathChanged -= OnCurrentDocumentPathChanged;
        StatusBarManager.StatusUpdated -= OnStatusUpdated;
        StatusBarManager.SecondaryStatusUpdated -= OnSecondaryStatusUpdated;
        AppEventHandler.ShowAppMessage -= OnAppMessage;
        ProcessingWorkspace.StatusChanged -= OnProcessingStatusChanged;
        ProcessingWorkspace.ProcessingChanged -= OnProcessingChanged;
        AnalysisWorkspace.StatusChanged -= OnAnalysisStatusChanged;
        AnalysisWorkspace.GraphChanged -= OnAnalysisGraphChanged;
        AnalysisWorkspace.FittingChanged -= OnAnalysisFittingChanged;
        FinalFigureWorkspace.StatusChanged -= OnFinalFigureStatusChanged;
        ResultWorkspace.StatusChanged -= OnResultStatusChanged;
        ResultWorkspace.DetailsRequested -= OnResultDetailsRequested;

        base.OnClosed(e);
    }

    internal Menu MenuHost => InWindowMenu;
    internal int ActiveWorkspaceIndex => WorkspaceTabs.IsVisible ? WorkspaceTabs.SelectedIndex : -1;

    internal bool HasDocumentContent() => DataManager.SourceItems.Count > 0;
    internal bool HasDataLoaded() => DataManager.DataIsLoaded;
    internal bool HasSelectedItem() => selectedItem != null;
    internal bool HasSelectedExperiment() => selectedItem is ExperimentData;
    internal bool HasSelectedResult() => selectedItem is AnalysisResult;
    internal bool HasAnyResults() => DataManager.Results.Count > 0;
    internal bool HasAnyProcessedData() => DataManager.AnyDataIsBaselineProcessed;
    internal bool CanUndoDelete() => StateManager.StateCanUndo();
    internal bool HasExperimentsWithAttributes() => DataManager.Data.Any(data => data.Attributes.Count > 0);
    internal bool CanEnableAnyExperiment() => DataManager.Data.Any(data => !data.Include);
    internal bool CanDisableAnyExperiment() => DataManager.Data.Any(data => data.Include);
    internal bool SelectedExperimentHasAttributes() => selectedItem is ExperimentData data && data.Attributes.Count > 0;
    internal bool SelectedExperimentHasSolution() => selectedItem is ExperimentData data && data.Solution != null;
    internal bool HasSelectedProcessor() => selectedItem is ExperimentData data && data.HasThermogram && data.Processor?.Interpolator != null;
    internal bool CanExportFinalFigure() => selectedItem is ExperimentData or AnalysisResult;
    internal bool IsAutoOpenResultEnabled() => AppSettings.AutoOpenNewAnalysisResult;

    internal Task OpenFilesFromMenuAsync() => OpenFilesAsync();

    internal async Task SaveDocumentAsync()
    {
        if (!HasDocumentContent()) return;

        var saved = FTITCWriter.IsSaved
            ? await FTITCWriter.SaveWithPathAsync()
            : await FTITCWriter.SaveState2Async();

        if (saved) StatusBarManager.SetFileSaveSuccessfulMessage(FTITCFormat.CurrentAccessedAppDocumentPath);
        RefreshMenuState();
    }

    internal async Task SaveDocumentAsAsync()
    {
        if (!HasDocumentContent()) return;

        var saved = await FTITCWriter.SaveState2Async();
        if (saved) StatusBarManager.SetFileSaveSuccessfulMessage(FTITCFormat.CurrentAccessedAppDocumentPath);
        RefreshMenuState();
    }

    internal async Task SaveSelectedAsync()
    {
        if (selectedItem == null) return;

        var saved = await FTITCWriter.SaveSelectedAsync(selectedItem);
        if (saved) StatusBarManager.SetStatus("Selected item saved", 3000);
    }

    internal async Task ClearDataWithConfirmationAsync()
    {
        await TryClearDataWithConfirmationAsync();
    }

    async Task<bool> TryClearDataWithConfirmationAsync()
    {
        if (!HasDocumentContent()) return true;

        if (!await PromptSaveChangesIfNeededAsync(SavePromptReason.ClearAllData))
            return false;

        if (!await ConfirmAsync(
            "Remove All Data/Results",
            "Are you sure you want to remove all loaded data and analysis results?",
            "Keep",
            "Remove"))
            return false;

        ClearData();
        return true;
    }

    internal async Task ExportDataAsync(bool selectedOnly)
    {
        await Exporter.ExportAsync(ExportType.Data, selectedOnly ? ExportDataSelection.SelectedData : null);
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
        RefreshMenuState();
        return Task.CompletedTask;
    }

    internal Task InvertExperimentInclusionAsync()
    {
        var included = DataManager.IncludedData.ToList();
        DataManager.SetAllIncludeState(true);

        foreach (var data in included)
            data.Include = false;

        DataManager.InvokeDataInclusionDidChange();
        RefreshDataList();
        RefreshMenuState();
        return Task.CompletedTask;
    }

    internal Task SortDataAsync(DataManager.SortMode mode)
    {
        DataManager.SortContent(mode);
        RefreshDataList();
        RefreshMenuState();
        return Task.CompletedTask;
    }

    internal Task SelectWorkspaceAsync(int index)
    {
        if (selectedItem is ExperimentData && index >= 0 && index < WorkspaceTabs.Items.Count)
        {
            WorkspaceTabs.IsVisible = true;
            ResultWorkspace.IsVisible = false;
            WorkspaceTabs.SelectedIndex = index;
            RefreshMenuState();
        }

        return Task.CompletedTask;
    }

    internal Task ShowResultViewAsync()
    {
        if (selectedItem is AnalysisResult)
        {
            WorkspaceTabs.IsVisible = false;
            ResultWorkspace.IsVisible = true;
            RefreshMenuState();
        }

        return Task.CompletedTask;
    }

    internal Task OpenSelectedDetailsFromMenuAsync() => OpenSelectedDetailsAsync();

    internal Task ToggleSelectedExperimentInclusionAsync()
    {
        if (selectedItem is ExperimentData experiment)
        {
            experiment.ToggleInclude();
            RefreshDataList();
            AnalysisWorkspace.RefreshIncludedDataState();
            InvalidateFinalFigurePreview();
            RefreshMenuState();
        }

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
        if (selectedItem == null) return;

        var itemType = selectedItem is AnalysisResult ? "Result" : "Data";
        var itemName = string.IsNullOrWhiteSpace(selectedItem.Name) ? selectedItem.FileName : selectedItem.Name;

        if (!await ConfirmAsync(
            $"Remove {itemType}",
            $"Are you sure you want to remove {itemName}?",
            "Keep",
            "Remove"))
            return;

        DataManager.RemoveSourceItemAt(DataManager.SelectedContentIndex);
        RefreshDataList();
        RefreshMenuState();
    }

    internal Task ToggleSelectedProcessorLockAsync()
    {
        if (selectedItem is ExperimentData experiment && experiment.Processor != null)
        {
            experiment.Processor.ToggleLock();
            ProcessingWorkspace.Experiment = experiment;
            RefreshMenuState();
        }

        return Task.CompletedTask;
    }

    internal Task CopyProcessingToActiveAsync()
    {
        DataManager.CopySelectedProcessToActive();
        RefreshMenuState();
        return Task.CompletedTask;
    }

    internal Task CopyProcessingToNonProcessedAsync()
    {
        DataManager.CopySelectedProcessToNonProcessed();
        RefreshMenuState();
        return Task.CompletedTask;
    }

    internal Task ToggleAutoOpenResultAsync()
    {
        AppSettings.AutoOpenNewAnalysisResult = !AppSettings.AutoOpenNewAnalysisResult;
        AppSettings.Save();
        RefreshMenuState();
        return Task.CompletedTask;
    }

    internal Task RestoreAnalysisDefaultsAsync()
    {
        AppSettings.CreateSingleAnalysisResult = false;
        AppSettings.CreateGlobalAnalysisResult = true;
        AppSettings.AutoOpenNewAnalysisResult = true;
        AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
        AppSettings.EnableExtendedParameterLimits = false;
        AppSettings.AnalysisParameterDisplay =
            FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Fitted | FinalFigureDisplayParameters.Derived;

        AnalysisSessionState.Reset();
        ModelFactory.ResetStoredAnalysisState();
        AppSettings.Save();

        StatusBarManager.SetStatus("Analysis defaults restored", 3000);
        AnalysisWorkspace.Experiment = selectedItem as ExperimentData;
        RefreshMenuState();
        return Task.CompletedTask;
    }

    internal Task CopyResultTableAsync()
    {
        if (selectedItem is AnalysisResult result)
        {
            Exporter.CopyToClipboard(result, result.AppropriateAffinityUnit, AppSettings.EnergyUnit, usekelvin: false);
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
        DataManager.InvokeUpdateTable();
        RefreshDataList();
        AnalysisWorkspace.RefreshIncludedDataState();
        InvalidateFinalFigurePreview();
        StatusBarManager.SetStatus("Experiments used by result selected", 3000);

        return Task.CompletedTask;
    }

    internal Task NotImplementedAsync()
    {
        StatusBarManager.SetStatus("This menu item is not available in the Avalonia app yet", 3000);
        return Task.CompletedTask;
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
        return await ConfirmationDialogWindow.ConfirmAsync(this, title, message, cancelButton, confirmButton);
    }

    void RefreshMenuState()
    {
        menuController?.Refresh();
    }

    async Task OpenFilesAsync()
    {
        OpenButton.IsEnabled = false;

        try
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
                .ToArray();

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
            await DataReader.ReadPathsAsync(paths);
            RefreshDataList();
            RefreshMenuState();
        }
        finally
        {
            OpenButton.IsEnabled = true;
        }
    }

    static string? GetLocalPath(IStorageFile file)
    {
        var path = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) return path;

        return file.Path.IsFile ? file.Path.LocalPath : null;
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

        ItemsList.ItemsSource = entries;
        UpdateListHeader();

        var nextIndex = previous == null
            ? DataManager.SelectedContentIndex
            : entries.FindIndex(entry => ReferenceEquals(entry.Item, previous));

        if (nextIndex < 0 && entries.Count > 0) nextIndex = Math.Min(Math.Max(DataManager.SelectedContentIndex, 0), entries.Count - 1);
        ItemsList.SelectedIndex = nextIndex >= 0 && nextIndex < entries.Count ? nextIndex : -1;

        var next = ItemsList.SelectedItem is DataListEntry entry ? entry.Item : null;
        UpdateSelection(next);
        RefreshMenuState();
    }

    void SelectListItem()
    {
        if (ItemsList.SelectedItem is not DataListEntry entry)
        {
            UpdateSelection(null);
            return;
        }

        var index = entries.IndexOf(entry);
        if (index >= 0) DataManager.SelectIndex(index);

        UpdateSelection(entry.Item);
    }

    void SetAllExperimentInclusion(bool include)
    {
        DataManager.SetAllIncludeState(include);
        RefreshDataList();
        AnalysisWorkspace.RefreshIncludedDataState();
        InvalidateFinalFigurePreview();
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
        RefreshOverview(item);
        ResultWorkspace.Result = item as AnalysisResult;
        UpdateFinalFigureContext(item);
        ProcessingWorkspace.Experiment = item as ExperimentData;
        AnalysisWorkspace.Experiment = item as ExperimentData;
        OverviewDetailsButton.IsEnabled = item is ExperimentData or AnalysisResult;

        if (item is ExperimentData experiment)
        {
            WorkspaceTabs.IsVisible = true;
            ResultWorkspace.IsVisible = false;
            WorkspaceTabs.SelectedIndex = 1;
        }
        else
        {
            WorkspaceTabs.IsVisible = item is not AnalysisResult;
            ResultWorkspace.IsVisible = item is AnalysisResult;
            WorkspaceTabs.SelectedIndex = 0;
        }

        RefreshMenuState();
    }

    void SelectOverviewMode(bool rawData)
    {
        if (isUpdatingOverviewMode) return;

        var selectedButton = rawData ? OverviewRawButton : OverviewInjectionsButton;
        if (selectedButton.IsChecked != true)
        {
            selectedButton.IsChecked = true;
            return;
        }

        isUpdatingOverviewMode = true;
        OverviewRawButton.IsChecked = rawData;
        OverviewInjectionsButton.IsChecked = !rawData;
        isUpdatingOverviewMode = false;

        UpdateOverviewVisibility();
    }

    void UpdateOverviewVisibility()
    {
        var showRaw = OverviewRawButton.IsChecked == true;
        OverviewRawHost.IsVisible = showRaw;
        OverviewInjectionsHost.IsVisible = !showRaw;
    }

    void RefreshOverview(ITCDataContainer? item = null)
    {
        item ??= selectedItem;
        var experiment = item as ExperimentData;

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
            .Select(line => line.Trim())
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
            grid.Children.Add(new TextBlock
            {
                Text = line.Substring(0, separator).Trim(),
                Foreground = Solid("#607080"),
                FontSize = 12
            });
            var value = new TextBlock
            {
                Text = line.Substring(separator + 1).Trim(),
                Foreground = Solid("#202832"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            return grid;
        }

        return new TextBlock
        {
            Text = line,
            Foreground = Solid("#202832"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
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

        var grid = new Grid
        {
            Background = Solid("#FFFFFF")
        };

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
        return new TextBlock
        {
            Text = message,
            Foreground = Solid("#607080"),
            Margin = new Thickness(16),
            TextWrapping = TextWrapping.Wrap
        };
    }

    void AddOverviewCell(Grid grid, string text, int column, int row, ExperimentOverviewColumnAlignment alignment, bool isHeader, bool isIncluded)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            Margin = new Thickness(8, 5),
            FontSize = isHeader ? 11 : 12,
            FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal,
            Foreground = isHeader ? Solid("#202832") : isIncluded ? Solid("#202832") : Solid("#7B8794"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignmentFor(alignment)
        };

        var border = new Border
        {
            BorderBrush = Solid("#E3E7EC"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = isHeader ? Solid("#F5F7FA") : row % 2 == 0 ? Solid("#FFFFFF") : Solid("#FAFBFC"),
            Child = textBlock,
            MinHeight = isHeader ? 30 : 28
        };

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

    static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));

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
                $"Temperature: {experiment.MeasuredTemperature:F1} C",
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
            .Trim();
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
            RefreshDataList();
            AnalysisWorkspace.RefreshIncludedDataState();
            InvalidateFinalFigurePreview();
            UpdateDocumentStatus();
        });
    }

    void OnDataManagerUpdate(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshDataList);
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
        RefreshOverview();
        InvalidateFinalFigurePreview();
    }

    void OnAnalysisStatusChanged(object? sender, string status)
    {
        SetStatus(status);
    }

    void OnAnalysisGraphChanged(object? sender, EventArgs e)
    {
        RefreshOverview();
        InvalidateFinalFigurePreview();
    }

    void OnAnalysisFittingChanged(object? sender, EventArgs e)
    {
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

    async void OnResultDetailsRequested(object? sender, EventArgs e)
    {
        await OpenSelectedDetailsAsync();
    }

    async Task OpenSelectedDetailsAsync()
    {
        switch (selectedItem)
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

    void RefreshAfterPreferencesApplied()
    {
        RefreshDataList();
        RefreshOverview();
        ProcessingWorkspace.Experiment = selectedItem as ExperimentData;
        AnalysisWorkspace.Experiment = selectedItem as ExperimentData;
        AnalysisWorkspace.RefreshIncludedDataState();
        ResultWorkspace.Refresh();
        FinalFigureWorkspace.ApplySettingsDefaults();
        InvalidateFinalFigurePreview();
        RefreshMenuState();
        StatusBarManager.SetStatus("Preferences updated", 2500);
    }

    void InvalidateFinalFigurePreview()
    {
        FinalFigureWorkspace.InvalidatePreview();
    }

    void SetStatus(string status)
    {
        StatusText.Text = status ?? "";
        ToolbarStatusText.Text = status ?? "";
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
                return FTITCWriter.IsSaved
                    ? await FTITCWriter.SaveWithPathAsync()
                    : await FTITCWriter.SaveState2Async();
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

        if (!FTITCWriter.IsSaved)
            return "Unsaved";

        var path = FTITCFormat.CurrentAccessedAppDocumentPath;
        var fileName = Path.GetFileName(path);
        if (string.Equals(Path.GetExtension(fileName), ".ftitc", StringComparison.OrdinalIgnoreCase))
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
                Foreground = Solid("#202832"),
                Margin = new Thickness(0, 0, 0, 18)
            };

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

            Content = new Border
            {
                Background = Brushes.White,
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
                Foreground = Solid("#202832"),
                Margin = new Thickness(0, 0, 0, 18)
            };

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

            Content = new Border
            {
                Background = Brushes.White,
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

    public sealed class DataListEntry
    {
        public ITCDataContainer Item { get; }
        readonly ExperimentData? experiment;

        DataListEntry(ITCDataContainer item, string kindLabel, string dateLine, string detailLine, string fitLine)
        {
            Item = item;
            experiment = item as ExperimentData;
            KindLabel = kindLabel;
            DateLine = dateLine;
            DetailLine = detailLine;
            FitLine = fitLine;
        }

        public string Title => Item.Name;
        public string KindLabel { get; }
        public string DateLine { get; }
        public string DetailLine { get; }
        public string FitLine { get; }
        public bool CanInclude => experiment != null;
        public bool CanIncludeActive => experiment?.Processor?.IntegrationCompleted == true;
        public bool IsIncluded
        {
            get => experiment?.Include == true;
            set
            {
                if (experiment == null || experiment.Include == value) return;
                experiment.ToggleInclude();
            }
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
            var detail = $"{experiment.MeasuredTemperature:G3} °C | {experiment.SyringeConcentration.AsFormattedConcentration(true)} | {experiment.CellConcentration.AsFormattedConcentration(true)}";
            var fit = BuildExperimentFitLine(experiment);

            if (string.IsNullOrWhiteSpace(fit))
            {
                var processing = experiment.Processor?.IntegrationCompleted == true
                    ? $"{experiment.InjectionCount} integrated injections"
                    : $"{experiment.InjectionCount} injections, not processed";
                fit = $"{processing} | {System.IO.Path.GetFileName(experiment.FileName)}";
            }

            return new DataListEntry(experiment, "DATA", experiment.UIShortDateWithTime, detail, fit);
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
            var fitLine = lines.Count > 2 ? string.Join(Environment.NewLine, lines.Skip(2)) : "";

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

            return PlainListText(string.Join(Environment.NewLine, lines));
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
