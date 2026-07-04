using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using SkiaSharp;

using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Avalonia;

public partial class MainWindow : Window
{
    List<DataListEntry> entries = new List<DataListEntry>();
    ITCDataContainer? selectedItem;
    readonly SkiaFigureRenderer finalFigureRenderer = new SkiaFigureRenderer();
    Bitmap? finalFigureBitmap;
    ExperimentData? finalFigureExperiment;
    string? finalFigureCacheKey;

    public MainWindow()
    {
        InitializeComponent();

        OpenButton.Click += async (_, _) => await OpenFilesAsync();
        ClearButton.Click += (_, _) => ClearData();
        FitViewButton.Click += (_, _) => FitActiveWorkspace();
        FitFigureButton.Click += (_, _) => RefreshFinalFigurePreview(force: true);
        ExportFigureButton.Click += async (_, _) => await ExportFinalFigurePdfAsync();
        ItemsList.SelectionChanged += (_, _) => SelectListItem();
        FinalFigurePreviewHost.SizeChanged += (_, _) => RefreshFinalFigurePreview();
        ProcessingWorkspace.StatusChanged += OnProcessingStatusChanged;
        ProcessingWorkspace.ProcessingChanged += OnProcessingChanged;
        AnalysisWorkspace.StatusChanged += OnAnalysisStatusChanged;
        AnalysisWorkspace.GraphChanged += OnAnalysisGraphChanged;
        AnalysisWorkspace.FittingChanged += OnAnalysisFittingChanged;

        WireFinalFigureOption(FinalFigureResidualsCheck);
        WireFinalFigureOption(FinalFigureDetailsCheck);
        WireFinalFigureOption(FinalFigureConfidenceCheck);
        WireFinalFigureOption(FinalFigureErrorBarsCheck);

        DataManager.DataDidChange += OnDataDidChange;
        DataManager.UpdateTable += OnDataManagerUpdate;
        DataManager.UpdateViewCells += OnDataManagerUpdate;
        StatusBarManager.StatusUpdated += OnStatusUpdated;
        StatusBarManager.SecondaryStatusUpdated += OnSecondaryStatusUpdated;
        AppEventHandler.ShowAppMessage += OnAppMessage;

        RefreshDataList();
        UpdateSelection(null);
        SetStatus("Ready");
    }

    void WireFinalFigureOption(CheckBox checkBox)
    {
        checkBox.IsCheckedChanged += (_, _) => RefreshFinalFigurePreview(force: true);
    }

    protected override void OnClosed(EventArgs e)
    {
        DataManager.DataDidChange -= OnDataDidChange;
        DataManager.UpdateTable -= OnDataManagerUpdate;
        DataManager.UpdateViewCells -= OnDataManagerUpdate;
        StatusBarManager.StatusUpdated -= OnStatusUpdated;
        StatusBarManager.SecondaryStatusUpdated -= OnSecondaryStatusUpdated;
        AppEventHandler.ShowAppMessage -= OnAppMessage;
        ProcessingWorkspace.StatusChanged -= OnProcessingStatusChanged;
        ProcessingWorkspace.ProcessingChanged -= OnProcessingChanged;
        AnalysisWorkspace.StatusChanged -= OnAnalysisStatusChanged;
        AnalysisWorkspace.GraphChanged -= OnAnalysisGraphChanged;
        AnalysisWorkspace.FittingChanged -= OnAnalysisFittingChanged;

        finalFigureBitmap?.Dispose();

        base.OnClosed(e);
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

            SetStatus("Opening data...");
            await DataReader.ReadPathsAsync(paths);
            RefreshDataList();
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
        RefreshDataList();
        StatusBarManager.SetStatus("Data cleared", 3000);
    }

    void RefreshDataList()
    {
        var previous = selectedItem;

        entries = DataManager.SourceItems
            .Select(DataListEntry.From)
            .ToList();

        ItemsList.ItemsSource = entries;
        ItemCountText.Text = $"{entries.Count} item{(entries.Count == 1 ? "" : "s")}";

        var nextIndex = previous == null
            ? DataManager.SelectedContentIndex
            : entries.FindIndex(entry => ReferenceEquals(entry.Item, previous));

        if (nextIndex < 0 && entries.Count > 0) nextIndex = Math.Min(Math.Max(DataManager.SelectedContentIndex, 0), entries.Count - 1);
        ItemsList.SelectedIndex = nextIndex >= 0 && nextIndex < entries.Count ? nextIndex : -1;

        var next = ItemsList.SelectedItem is DataListEntry entry ? entry.Item : null;
        UpdateSelection(next);
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

    void UpdateSelection(ITCDataContainer? item)
    {
        selectedItem = item;

        OverviewText.Text = item == null ? "No loaded data." : BuildOverview(item);
        ResultText.Text = item is AnalysisResult result ? BuildResultSummary(result) : "No result selected.";
        UpdateFinalFigureContext(item);
        ProcessingWorkspace.Experiment = item as ExperimentData;
        AnalysisWorkspace.Experiment = item as ExperimentData;

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
    }

    void UpdateFinalFigureContext(ITCDataContainer? item)
    {
        finalFigureCacheKey = null;

        if (item is ExperimentData experiment)
        {
            finalFigureExperiment = experiment;
            RefreshFinalFigurePreview(force: true);
            return;
        }

        if (item is AnalysisResult result)
        {
            DataManager.LoadResultSolutionsToExperiments(result);
            finalFigureExperiment = GetResultExperiments(result).FirstOrDefault();
            RefreshFinalFigurePreview(force: true);
            return;
        }

        finalFigureExperiment = null;
        finalFigureBitmap?.Dispose();
        finalFigureBitmap = null;
        FinalFigureImage.Source = null;
        FinalFigureText.Text = "No figure selected";
    }

    void RefreshFinalFigurePreview(bool force = false)
    {
        var experiment = finalFigureExperiment;

        if (experiment == null)
        {
            FinalFigureText.Text = "No figure selected";
            return;
        }

        var options = BuildFinalFigureOptions();
        var hostWidth = FinalFigurePreviewHost.Bounds.Width;
        var pixelWidth = Math.Max(850, Math.Min(2200, (int)Math.Round((hostWidth > 1 ? hostWidth : 1000) * 2)));
        var solutionKey = experiment.Solution == null ? "no-solution" : experiment.Solution.GetHashCode().ToString();
        var cacheKey = $"{experiment.UniqueID}|{solutionKey}|{pixelWidth}|{options.CacheKey}";

        if (!force && cacheKey == finalFigureCacheKey) return;

        try
        {
            var document = PublicationFigureBuilder.Build(experiment, options);
            using var bitmap = finalFigureRenderer.RenderBitmap(document, pixelWidth);
            var nextBitmap = ToAvaloniaBitmap(bitmap);

            finalFigureBitmap?.Dispose();
            finalFigureBitmap = nextBitmap;
            FinalFigureImage.Source = nextBitmap;
            finalFigureCacheKey = cacheKey;
            FinalFigureText.Text = experiment.Solution == null
                ? $"{experiment.Name}: preview without fitted solution"
                : $"{experiment.Name}: publication figure";
        }
        catch (Exception ex)
        {
            FinalFigureText.Text = $"Could not render figure: {ex.Message}";
            finalFigureBitmap?.Dispose();
            finalFigureBitmap = null;
            FinalFigureImage.Source = null;
        }
    }

    PublicationFigureOptions BuildFinalFigureOptions()
    {
        return new PublicationFigureOptions
        {
            ShowResiduals = FinalFigureResidualsCheck.IsChecked == true,
            ShowExperimentDetails = FinalFigureDetailsCheck.IsChecked == true,
            ShowFitParameters = FinalFigureDetailsCheck.IsChecked == true,
            ShowConfidenceBand = FinalFigureConfidenceCheck.IsChecked == true,
            ShowErrorBars = FinalFigureErrorBarsCheck.IsChecked == true,
            EnergyUnit = AppSettings.EnergyUnit,
            DisplayParameters = AppSettings.FinalFigureParameterDisplay,
            AttributeOptions = AppSettings.DisplayAttributeOptions,
            TextUncertaintyStyle = AppSettings.UncertaintyDisplayStyle
        };
    }

    async Task ExportFinalFigurePdfAsync()
    {
        if (selectedItem is AnalysisResult result)
        {
            await ExportResultFiguresAsync(result);
            return;
        }

        if (finalFigureExperiment == null)
        {
            SetStatus("No figure selected");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Final Figure",
            SuggestedFileName = SanitizeFileName(finalFigureExperiment.Name) + ".pdf",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PDF figure") { Patterns = new[] { "*.pdf" } },
                FilePickerFileTypes.All
            }
        });

        var path = file == null ? null : GetLocalPath(file);
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";

        ExportExperimentFigure(finalFigureExperiment, path);
        SetStatus("Final figure exported");
    }

    async Task ExportResultFiguresAsync(AnalysisResult result)
    {
        DataManager.LoadResultSolutionsToExperiments(result);
        var experiments = GetResultExperiments(result).ToList();

        if (experiments.Count == 0)
        {
            SetStatus("Selected result has no experiment figures");
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose Figure Export Folder",
            AllowMultiple = false
        });

        var folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(folderPath)) return;

        Directory.CreateDirectory(folderPath);

        foreach (var target in CreateFigureExportTargets(experiments, folderPath))
        {
            ExportExperimentFigure(target.Experiment, target.Path);
        }

        SetStatus($"{experiments.Count} final figure{(experiments.Count == 1 ? "" : "s")} exported");
    }

    void ExportExperimentFigure(ExperimentData experiment, string path)
    {
        var document = PublicationFigureBuilder.Build(experiment, BuildFinalFigureOptions());
        finalFigureRenderer.WritePdf(document, path);
    }

    static IEnumerable<ExperimentData> GetResultExperiments(AnalysisResult result)
    {
        return result.Solution?.Solutions?
            .Where(solution => solution?.Data != null)
            .Select(solution => solution.Data)
            .Where(experiment => experiment != null)
            .GroupBy(experiment => experiment.UniqueID)
            .Select(group => group.First())
            ?? Enumerable.Empty<ExperimentData>();
    }

    static List<FigureExportTarget> CreateFigureExportTargets(IEnumerable<ExperimentData> experiments, string folderPath)
    {
        var targets = new List<FigureExportTarget>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var experiment in experiments)
        {
            var baseName = SanitizeFileName(experiment.Name);
            var fileName = baseName + ".pdf";
            var suffix = 2;

            while (usedNames.Contains(fileName))
            {
                fileName = $"{baseName} ({suffix}).pdf";
                suffix++;
            }

            usedNames.Add(fileName);
            targets.Add(new FigureExportTarget(experiment, Path.Combine(folderPath, fileName)));
        }

        return targets;
    }

    static string SanitizeFileName(string name)
    {
        var cleanName = string.IsNullOrWhiteSpace(name) ? "Untitled Figure" : name.Trim();

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            cleanName = cleanName.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(cleanName) ? "Untitled Figure" : cleanName;
    }

    static Bitmap ToAvaloniaBitmap(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;

        return new Bitmap(stream);
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

    void OnDataDidChange(object? sender, ExperimentData? e)
    {
        Dispatcher.UIThread.Post(RefreshDataList);
    }

    void OnDataManagerUpdate(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshDataList);
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
        InvalidateFinalFigurePreview();
    }

    void OnAnalysisStatusChanged(object? sender, string status)
    {
        SetStatus(status);
    }

    void OnAnalysisGraphChanged(object? sender, EventArgs e)
    {
        InvalidateFinalFigurePreview();
    }

    void OnAnalysisFittingChanged(object? sender, EventArgs e)
    {
        InvalidateFinalFigurePreview();
    }

    void InvalidateFinalFigurePreview()
    {
        finalFigureCacheKey = null;
        if (finalFigureExperiment != null)
            RefreshFinalFigurePreview(force: true);
    }

    void FitActiveWorkspace()
    {
        switch (WorkspaceTabs.SelectedIndex)
        {
            case 1:
                ProcessingWorkspace.FitToData();
                break;
            case 2:
                AnalysisWorkspace.FitToData();
                break;
            case 3:
                RefreshFinalFigurePreview(force: true);
                break;
        }
    }

    void SetStatus(string status)
    {
        StatusText.Text = status ?? "";
        ToolbarStatusText.Text = status ?? "";
    }

    sealed class DataListEntry
    {
        public ITCDataContainer Item { get; }
        readonly string text;

        DataListEntry(ITCDataContainer item, string text)
        {
            Item = item;
            this.text = text;
        }

        public static DataListEntry From(ITCDataContainer item)
        {
            var kind = item is AnalysisResult ? "Result" : "Data";
            return new DataListEntry(item, $"{kind}: {item.Name}{Environment.NewLine}{BuildShortSummary(item)}");
        }

        public override string ToString() => text;
    }

    sealed class FigureExportTarget
    {
        public FigureExportTarget(ExperimentData experiment, string path)
        {
            Experiment = experiment;
            Path = path;
        }

        public ExperimentData Experiment { get; }
        public string Path { get; }
    }
}
