using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SkiaSharp;

using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using static AnalysisITC.Avalonia.Workspace.WorkspaceControlBuilder;

namespace AnalysisITC.Avalonia.Tools
{
    public sealed class AnalysisReportWindow : Window
    {
        static readonly EnergyUnit?[] EnergyOverrides =
        {
            null, EnergyUnit.Joule, EnergyUnit.KiloJoule, EnergyUnit.Cal, EnergyUnit.KCal
        };
        static readonly Dictionary<string, HashSet<string>> SessionAdvancedSelections = new();
        static int sessionEnergyIndex;
        static int sessionTemperatureIndex;
        static int sessionUncertaintyIndex;

        readonly SkiaAnalysisReportRenderer renderer = new SkiaAnalysisReportRenderer();
        readonly ComboBox resultCombo = Combo(Array.Empty<string>());
        readonly TextBox labelBox = TextBox();
        readonly TextBox titleBox = TextBox();
        readonly ComboBox energyCombo = Combo(new[] { "Automatic", "J", "kJ", "cal", "kcal" });
        readonly ComboBox temperatureCombo = Combo(new[] { "Celsius", "Kelvin" });
        readonly ComboBox uncertaintyCombo = Combo(new[] { "Automatic", "SD", "CI", "SD + CI", "None" });
        readonly StackPanel advancedPanel = new StackPanel { Spacing = 2 };
        readonly ListBox previewPages = new ListBox { SelectionMode = SelectionMode.Single };
        readonly TextBlock previewPlaceholder = Text();
        readonly TextBlock statusText = Text();
        readonly ProgressBar progress = new ProgressBar { IsIndeterminate = true, IsVisible = false, Height = 3 };
        readonly Button previewButton = Button("Preview", 82);
        readonly Button exportButton = Button("Export PDF...", 112);
        readonly List<CheckBox> advancedChecks = new();
        readonly List<AnalysisReportPreviewPage> pageViews = new();

        AnalysisReportDocument? currentDocument;
        AnalysisReportLayoutPlan? currentPlan;
        bool previewStale = true;
        bool changingResult;
        bool busy;

        public AnalysisReportWindow(AnalysisResult? selectedResult = null)
        {
            Title = "Analysis Report";
            Width = 1220;
            Height = 780;
            MinWidth = 960;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            AppTheme.Bind(this, BackgroundProperty, AppTheme.WorkspaceBackground);

            energyCombo.SelectedIndex = sessionEnergyIndex;
            temperatureCombo.SelectedIndex = sessionTemperatureIndex;
            uncertaintyCombo.SelectedIndex = sessionUncertaintyIndex;
            BuildLayout();
            WireEvents();
            PopulateResults(selectedResult);
        }

        protected override void OnClosed(EventArgs e)
        {
            ClearPreview();
            base.OnClosed(e);
        }

        void BuildLayout()
        {
            resultCombo.ItemTemplate = new FuncDataTemplate<AnalysisResult>((result, _) => ResultCell(result));
            resultCombo.MinWidth = 230;
            previewPlaceholder.Text = "Click Preview to build the printable A4 report.";
            previewPlaceholder.HorizontalAlignment = HorizontalAlignment.Center;
            previewPlaceholder.VerticalAlignment = VerticalAlignment.Center;
            AppTheme.Bind(previewPlaceholder, TextBlock.ForegroundProperty, AppTheme.MutedText);

            var previewHost = new Grid();
            var border = ContentBorder(previewHost);
            AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PreviewBackground);
            previewPages.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
            previewPages.Background = Brushes.Transparent;
            previewPages.BorderThickness = new Thickness(0);
            previewPages.IsVisible = false;
            previewHost.Children.Add(previewPages);
            previewHost.Children.Add(previewPlaceholder);

            var inspector = InspectorPanel();
            inspector.Children.Add(Section("Analysis result", resultCombo));
            inspector.Children.Add(Section("Document",
                Labeled("Label", labelBox),
                Labeled("Title", titleBox)));
            inspector.Children.Add(Section("Presentation",
                Labeled("Energy", energyCombo),
                Labeled("Temperature", temperatureCombo),
                Labeled("Uncertainty", uncertaintyCombo)));

            var selectAll = Button("Select all", 76);
            selectAll.Click += (_, _) => SetAllAdvanced(true);
            var clear = Button("Clear", 58);
            clear.Click += (_, _) => SetAllAdvanced(false);
            var actions = Row(selectAll, clear);
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            inspector.Children.Add(Section("Additional analyses", actions, advancedPanel));

            var cancel = Button("Cancel", 78);
            cancel.Click += (_, _) => Close(false);
            previewButton.Click += async (_, _) => await PreviewAsync();
            exportButton.Click += async (_, _) => await ExportAsync();
            var buttons = Row(cancel, previewButton, exportButton);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            var footer = InspectorFooter(Section("PDF export", progress, buttons, statusText));

            Content = WorkspaceControlBuilder.Workspace(border, Scroll(inspector), footer, useOuterMargin: true);
        }

        void WireEvents()
        {
            resultCombo.SelectionChanged += (_, _) => ResultChanged();
            labelBox.TextChanged += (_, _) => MarkStale();
            titleBox.TextChanged += (_, _) => MarkStale();
            energyCombo.SelectionChanged += (_, _) => { sessionEnergyIndex = energyCombo.SelectedIndex; MarkStale(); };
            temperatureCombo.SelectionChanged += (_, _) => { sessionTemperatureIndex = temperatureCombo.SelectedIndex; MarkStale(); };
            uncertaintyCombo.SelectionChanged += (_, _) => { sessionUncertaintyIndex = uncertaintyCombo.SelectedIndex; MarkStale(); };
        }

        void PopulateResults(AnalysisResult? selected)
        {
            var results = DataManager.Results.ToList();
            resultCombo.ItemsSource = results;
            resultCombo.SelectedItem = selected != null && results.Contains(selected)
                ? selected
                : DataManager.SelectedResult != null && results.Contains(DataManager.SelectedResult)
                    ? DataManager.SelectedResult
                    : results.FirstOrDefault();
        }

        Control ResultCell(AnalysisResult? result)
        {
            if (result == null) return Text();
            var count = result.Solution?.Solutions?.Count ?? 0;
            var model = result.Model?.ModelType.GetProperties().Name ?? "Unknown model";
            var panel = new StackPanel { Spacing = 1, Margin = new Thickness(5, 3) };
            panel.Children.Add(new TextBlock { Text = result.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var details = new TextBlock
            {
                Text = $"{result.Date:g} | {model} | {count} experiment{(count == 1 ? "" : "s")} | {result.Health}",
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            AppTheme.Bind(details, TextBlock.ForegroundProperty, AppTheme.MutedText);
            panel.Children.Add(details);
            return panel;
        }

        void ResultChanged()
        {
            if (changingResult) return;
            changingResult = true;
            try
            {
                var result = SelectedResult;
                titleBox.Text = result?.Name ?? "";
                RebuildAdvancedChoices(result);
                ShowValidation(result);
            }
            finally { changingResult = false; }
            MarkStale();
        }

        void RebuildAdvancedChoices(AnalysisResult? result)
        {
            advancedPanel.Children.Clear();
            advancedChecks.Clear();
            if (result == null) return;
            var selected = SessionAdvancedSelections.TryGetValue(ResultKey(result), out var saved)
                ? saved : new HashSet<string>();
            foreach (var descriptor in AnalysisReportBuilder.GetAvailableAdvancedSections(result))
            {
                var check = Check(descriptor.Title);
                check.Tag = descriptor;
                check.IsChecked = selected.Contains(descriptor.Request.Key);
                ToolTip.SetTip(check, descriptor.Description);
                check.IsCheckedChanged += (_, _) =>
                {
                    SaveAdvancedDraft();
                    MarkStale();
                };
                advancedChecks.Add(check);
                advancedPanel.Children.Add(check);
            }
            if (advancedChecks.Count == 0)
            {
                var none = Text("No saved advanced analyses are available for this result.");
                AppTheme.Bind(none, TextBlock.ForegroundProperty, AppTheme.MutedText);
                advancedPanel.Children.Add(none);
            }
        }

        void SetAllAdvanced(bool selected)
        {
            foreach (var check in advancedChecks) check.IsChecked = selected;
            SaveAdvancedDraft();
            MarkStale();
        }

        void SaveAdvancedDraft()
        {
            var result = SelectedResult;
            if (result == null) return;
            SessionAdvancedSelections[ResultKey(result)] = advancedChecks
                .Where(check => check.IsChecked == true)
                .Select(check => ((AnalysisReportAdvancedSectionDescriptor)check.Tag!).Request.Key)
                .ToHashSet();
        }

        AnalysisReportOptions CurrentOptions()
        {
            var options = new AnalysisReportOptions
            {
                DocumentLabel = labelBox.Text ?? "",
                Title = titleBox.Text ?? "",
                EnergyUnitFamily = AppSettings.EnergyUnitFamily,
                EnergyUnitOverride = energyCombo.SelectedIndex >= 0 && energyCombo.SelectedIndex < EnergyOverrides.Length
                    ? EnergyOverrides[energyCombo.SelectedIndex] : null,
                UseKelvin = temperatureCombo.SelectedIndex == 1,
                UncertaintyDisplayStyle = uncertaintyCombo.SelectedIndex switch
                {
                    1 => UncertaintyDisplayStyle.StandardDeviation,
                    2 => UncertaintyDisplayStyle.ConfidenceInterval,
                    3 => UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
                    4 => UncertaintyDisplayStyle.None,
                    _ => UncertaintyDisplayStyle.Automatic
                }
            };
            foreach (var descriptor in advancedChecks
                .Where(check => check.IsChecked == true)
                .Select(check => (AnalysisReportAdvancedSectionDescriptor)check.Tag!))
                options.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(
                    descriptor.Request.Kind, descriptor.Request.CorrelationMemberIndex));
            return options;
        }

        async Task PreviewAsync()
        {
            if (busy || SelectedResult == null) return;
            await BuildAsync(showPreview: true);
        }

        async Task<bool> BuildAsync(bool showPreview)
        {
            var result = SelectedResult;
            if (result == null) return false;
            var validation = AnalysisReportBuilder.Validate(result);
            if (!validation.IsValid)
            {
                SetStatus(validation.Errors.FirstOrDefault() ?? "This result cannot be reported.", true);
                return false;
            }

            SetBusy(true, showPreview ? "Building preview..." : "Preparing report...");
            try
            {
                var options = CurrentOptions();
                var built = await Task.Run(() =>
                {
                    var document = AnalysisReportBuilder.Build(result, options);
                    var plan = renderer.CreatePlan(document);
                    return (document, plan);
                });
                currentDocument = built.document;
                currentPlan = built.plan;
                previewStale = false;
                if (showPreview || previewPages.IsVisible) ShowPreview(built.document, built.plan);
                var warning = built.document.Warnings.FirstOrDefault();
                SetStatus(warning ?? $"{built.plan.Pages.Count} A4 page{(built.plan.Pages.Count == 1 ? "" : "s")} ready.", false, warning != null);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Could not build report: " + ex.Message, true);
                return false;
            }
            finally { SetBusy(false, null); }
        }

        void ShowPreview(AnalysisReportDocument document, AnalysisReportLayoutPlan plan)
        {
            ClearPreview();
            for (var index = 0; index < plan.Pages.Count; index++)
                pageViews.Add(new AnalysisReportPreviewPage(renderer, document, plan, index));
            previewPages.ItemsSource = pageViews.ToList();
            previewPages.IsVisible = true;
            previewPlaceholder.IsVisible = false;
        }

        async Task ExportAsync()
        {
            if (busy || SelectedResult == null) return;
            if (previewStale || currentDocument == null || currentPlan == null)
                if (!await BuildAsync(showPreview: false)) return;

            var document = currentDocument!;
            var plan = currentPlan!;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Analysis Report",
                SuggestedFileName = SanitizeFileName(SelectedResult.Name) + "-analysis-report.pdf",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PDF document") { Patterns = new[] { "*.pdf" } },
                    FilePickerFileTypes.All
                }
            });
            var path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";

            SetBusy(true, "Exporting PDF...");
            try
            {
                await Task.Run(() => renderer.WritePdf(document, plan, path));
                SetStatus("Analysis report PDF exported.");
                StatusBarManager.SetStatus("Analysis report PDF exported", 3000);
            }
            catch (Exception ex) { SetStatus("Could not export PDF: " + ex.Message, true); }
            finally { SetBusy(false, null); }
        }

        void MarkStale()
        {
            if (changingResult) return;
            previewStale = true;
            currentDocument = null;
            currentPlan = null;
            if (previewPages.IsVisible && AnalysisReportBuilder.Validate(SelectedResult).IsValid)
                SetStatus("Preview is out of date. Click Preview to refresh.", false, true);
        }

        void ShowValidation(AnalysisResult? result)
        {
            var validation = AnalysisReportBuilder.Validate(result);
            var valid = validation.IsValid;
            previewButton.IsEnabled = valid;
            exportButton.IsEnabled = valid;
            if (!valid) SetStatus(validation.Errors.FirstOrDefault() ?? "This result cannot be reported.", true);
            else if (result?.Health != AnalysisResultHealth.Valid)
                SetStatus("This result has warnings or may be stale. The report will include a prominent notice.", false, true);
            else SetStatus("");
        }

        void SetBusy(bool value, string? message)
        {
            busy = value;
            progress.IsVisible = value;
            resultCombo.IsEnabled = !value;
            labelBox.IsEnabled = !value;
            titleBox.IsEnabled = !value;
            energyCombo.IsEnabled = !value;
            temperatureCombo.IsEnabled = !value;
            uncertaintyCombo.IsEnabled = !value;
            advancedPanel.IsEnabled = !value;
            previewButton.IsEnabled = !value && AnalysisReportBuilder.Validate(SelectedResult).IsValid;
            exportButton.IsEnabled = previewButton.IsEnabled;
            if (!string.IsNullOrWhiteSpace(message)) SetStatus(message);
        }

        void SetStatus(string message, bool error = false, bool warning = false)
        {
            statusText.Text = message ?? "";
            AppTheme.Bind(statusText, TextBlock.ForegroundProperty,
                error ? AppTheme.StatusError : warning ? AppTheme.StatusWarning : AppTheme.SecondaryText);
        }

        void ClearPreview()
        {
            previewPages.ItemsSource = null;
            foreach (var view in pageViews) view.Dispose();
            pageViews.Clear();
            previewPages.IsVisible = false;
            previewPlaceholder.IsVisible = true;
        }

        AnalysisResult? SelectedResult => resultCombo.SelectedItem as AnalysisResult;

        static string ResultKey(AnalysisResult result) => string.IsNullOrWhiteSpace(result.UniqueID)
            ? result.Name + "|" + result.Date.Ticks : result.UniqueID;

        internal static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars().ToHashSet();
            var cleaned = new string((value ?? "analysis")
                .Select(character => invalid.Contains(character) || character == '/' || character == '\\' ? '-' : character)
                .ToArray()).Trim(' ', '.', '-');
            return string.IsNullOrWhiteSpace(cleaned) ? "analysis" : cleaned;
        }
    }

    sealed class AnalysisReportPreviewPage : Border, IDisposable
    {
        readonly SkiaAnalysisReportRenderer renderer;
        readonly AnalysisReportDocument document;
        readonly AnalysisReportLayoutPlan plan;
        readonly int pageIndex;
        readonly Image image = new Image { Stretch = Stretch.Uniform };
        readonly TextBlock placeholder = new TextBlock { Text = "Rendering page...", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        Bitmap? bitmap;

        public AnalysisReportPreviewPage(SkiaAnalysisReportRenderer renderer,
            AnalysisReportDocument document, AnalysisReportLayoutPlan plan, int pageIndex)
        {
            this.renderer = renderer;
            this.document = document;
            this.plan = plan;
            this.pageIndex = pageIndex;
            Width = 595;
            Height = 842;
            Margin = new Thickness(0, 7);
            HorizontalAlignment = HorizontalAlignment.Center;
            Background = Brushes.White;
            BorderBrush = new SolidColorBrush(Color.FromRgb(190, 190, 190));
            BorderThickness = new Thickness(1);
            Child = placeholder;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (bitmap == null) _ = RenderAsync(cancellation.Token);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            DisposeBitmap();
            base.OnDetachedFromVisualTree(e);
        }

        async Task RenderAsync(CancellationToken token)
        {
            try
            {
                var bytes = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using var rendered = renderer.RenderPageBitmap(document, plan, pageIndex, 900);
                    using var skImage = SKImage.FromBitmap(rendered);
                    using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 95);
                    return encoded.ToArray();
                }, token);
                if (token.IsCancellationRequested) return;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    using var stream = new MemoryStream(bytes);
                    bitmap = new Bitmap(stream);
                    image.Source = bitmap;
                    Child = image;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => placeholder.Text = "Could not render page: " + ex.Message);
            }
        }

        void DisposeBitmap()
        {
            image.Source = null;
            bitmap?.Dispose();
            bitmap = null;
            Child = placeholder;
        }

        public void Dispose()
        {
            cancellation.Cancel();
            cancellation.Dispose();
            DisposeBitmap();
        }
    }
}
