using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

using SkiaSharp;

using AnalysisITC.Avalonia.Controls;
using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;
using static AnalysisITC.Avalonia.Workspace.WorkspaceControlBuilder;

namespace AnalysisITC.Avalonia.Tools
{
    public sealed class SupportingFigureCanvasWindow : Window
    {
        readonly PublicationFigureOptions figureOptions;
        readonly PublicationFigureCanvasOptions canvasDefaults = new PublicationFigureCanvasOptions();
        readonly List<ITCDataContainer> composition = new List<ITCDataContainer>();
        readonly HashSet<AnalysisResult> observedResults = new HashSet<AnalysisResult>();
        readonly SkiaFigureCanvasRenderer renderer = new SkiaFigureCanvasRenderer();

        readonly ListBox compositionList = new ListBox { SelectionMode = SelectionMode.Single };
        readonly ListBox sourcePickerList = new ListBox { SelectionMode = SelectionMode.Multiple, MaxHeight = 320, MinWidth = 330 };
        readonly TextBox sourceFilterBox = TextBox();
        readonly Flyout sourcePickerFlyout = new Flyout();
        readonly StackPanel previewPages = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        readonly TextBox plotWidthBox = TextBox();
        readonly TextBox plotHeightBox = TextBox();
        readonly NumericUpDown fontSizeStepper = Stepper(8, 5, 24, 0.5m, formatString: "0.#");
        readonly NumericUpDown symbolSizeStepper = Stepper(4, 3, 14, 0.5m, formatString: "0.#");
        readonly NumericUpDown columnsStepper = Stepper(3, 1, 6);
        readonly NumericUpDown rowsStepper = Stepper(3, 1, 10);
        readonly SegmentedSelector strokeWidthSelector = Segmented(new[] { "Light", "Standard" });
        readonly CheckBox panelLettersCheck = Check("Panel letters");
        readonly CheckBox panelTitlesCheck = Check("Panel titles");
        readonly CheckBox groupResultsCheck = Check("Group result figures");
        readonly CheckBox informationBoxesCheck = Check("Parameter / info boxes");
        readonly ComboBox previewZoomCombo = Combo(new[] { "25%", "50%", "75%", "100%" }, 3, 88);
        readonly TextBlock plotSizeText = Text();
        readonly TextBlock figureSizeText = Text();
        readonly TextBlock previewFigureSizeText = Text("Figure dimensions: —");
        readonly TextBlock statusText = Text();
        readonly Button exportButton = Button("Export PDF...", 104);

        readonly List<Bitmap> previewBitmaps = new List<Bitmap>();
        readonly List<Image> previewImages = new List<Image>();
        SkiaFigureCanvasRenderPlan? currentPlan;

        internal NumericUpDown ColumnsStepperForTesting => columnsStepper;
        internal NumericUpDown RowsStepperForTesting => rowsStepper;
        internal NumericUpDown FontSizeStepperForTesting => fontSizeStepper;
        internal NumericUpDown SymbolSizeStepperForTesting => symbolSizeStepper;
        internal SegmentedSelector StrokeWidthSelectorForTesting => strokeWidthSelector;
        internal TextBlock FigureSizeTextForTesting => figureSizeText;
        internal TextBlock PreviewFigureSizeTextForTesting => previewFigureSizeText;
        internal TextBlock StatusTextForTesting => statusText;
        internal StackPanel PreviewPagesForTesting => previewPages;
        internal PublicationFigureCanvasOptions CanvasOptionsForTesting => CurrentCanvasOptions();
        internal SkiaFigureCanvasRenderPlan? CurrentPlanForTesting => currentPlan;

        internal void SetCompositionForTesting(IEnumerable<ITCDataContainer> items)
        {
            composition.Clear();
            composition.AddRange(items ?? Enumerable.Empty<ITCDataContainer>());
            RefreshCompositionList(composition.FirstOrDefault());
            RefreshPreview();
        }

        public SupportingFigureCanvasWindow(PublicationFigureOptions figureOptions, ITCDataContainer? selectedItem)
        {
            this.figureOptions = figureOptions ?? new PublicationFigureOptions();
            ApplyCanvasDefaults();

            Title = "Supporting Figure";
            Width = 1200;
            Height = 720;
            MinWidth = 1040;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            AppTheme.Bind(this, BackgroundProperty, AppTheme.WorkspaceBackground);

            if (selectedItem != null) composition.Add(selectedItem);

            BuildLayout();
            WireEvents();
            RefreshCompositionList(selectedItem);
            RefreshPreview();
        }

        protected override void OnClosed(EventArgs e)
        {
            foreach (var result in observedResults) result.BindingAssessmentChanged -= OnBindingAssessmentChanged;
            observedResults.Clear();
            ClearPreview();
            base.OnClosed(e);
        }

        void BuildLayout()
        {
            ToolTip.SetTip(plotWidthBox, "Width of each plot, in centimeters.");
            ToolTip.SetTip(plotHeightBox, "Height of each plot, in centimeters.");
            ToolTip.SetTip(columnsStepper, "Number of plot columns in the figure.");
            ToolTip.SetTip(rowsStepper, "Number of plot rows in the figure.");
            ToolTip.SetTip(fontSizeStepper, "Base text size; other text sizes are scaled from it.");
            ToolTip.SetTip(symbolSizeStepper, "Size of data points, in points.");
            ToolTip.SetTip(strokeWidthSelector, "Also sets tick mark length.");
            ToolTip.SetTip(panelLettersCheck, "Label panels A, B, C, … in figure order.");
            ToolTip.SetTip(panelTitlesCheck, "Add the experiment name after each panel letter.");
            ToolTip.SetTip(groupResultsCheck, "Treat all figures from one result as a group; only its first panel gets a letter unless panel titles are on.");
            ToolTip.SetTip(informationBoxesCheck, "Show parameter and information boxes on result figures.");
            ToolTip.SetTip(previewZoomCombo, "Change the preview scale; exported size is unchanged.");

            sourcePickerList.ItemTemplate = new FuncDataTemplate<ITCDataContainer>((item, _) => SourceCell(item, showOrder: false));
            compositionList.ItemTemplate = new FuncDataTemplate<ITCDataContainer>((item, _) => SourceCell(item, showOrder: true));

            var addButton = Button("Add…", 60);
            addButton.Click += (_, _) => OpenSourcePicker(addButton);
            var removeButton = Button("Remove", 68);
            removeButton.Click += (_, _) => RemoveSelectedComposition();
            var upButton = Button("↑", 34);
            ToolTip.SetTip(upButton, "Move up");
            upButton.Click += (_, _) => MoveSelected(-1);
            var downButton = Button("↓", 34);
            ToolTip.SetTip(downButton, "Move down");
            downButton.Click += (_, _) => MoveSelected(1);

            var compositionPanel = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 6 };
            compositionPanel.Children.Add(Header("Figure order"));
            Grid.SetRow(compositionList, 1);
            compositionPanel.Children.Add(ContentBorder(compositionList));
            var orderRow = Row(addButton, removeButton, upButton, downButton);
            orderRow.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(orderRow, 2);
            compositionPanel.Children.Add(orderRow);

            BuildSourcePicker();

            var previewHost = new Border
            {
                BorderThickness = new Thickness(1),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new Border
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = previewPages
                    }
                }
            };
            AppTheme.Bind(previewHost, Border.BackgroundProperty, AppTheme.PreviewBackground);
            AppTheme.Bind(previewHost, Border.BorderBrushProperty, AppTheme.PanelBorder);

            var previewPanel = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 6 };
            AppTheme.Bind(previewFigureSizeText, TextBlock.ForegroundProperty, AppTheme.MutedText);
            var previewToolbar = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 8
            };
            var previewTitle = Row(Header("Preview"), previewFigureSizeText);
            previewToolbar.Children.Add(previewTitle);
            var previewZoomToolbar = Row(Text("Preview zoom"), previewZoomCombo);
            previewZoomToolbar.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(previewZoomToolbar, 2);
            previewToolbar.Children.Add(previewZoomToolbar);
            previewPanel.Children.Add(previewToolbar);
            Grid.SetRow(previewHost, 1);
            previewPanel.Children.Add(previewHost);

            var main = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("300,*"),
                ColumnSpacing = 8
            };
            main.Children.Add(compositionPanel);
            Grid.SetColumn(previewPanel, 1);
            main.Children.Add(previewPanel);

            var inspector = InspectorPanel();
            inspector.Children.Add(Section("Common plot size",
                Labeled("Width cm", plotWidthBox),
                Labeled("Height cm", plotHeightBox),
                plotSizeText));
            inspector.Children.Add(Section("Grid",
                Labeled("Columns", columnsStepper),
                Labeled("Rows", rowsStepper)));
            inspector.Children.Add(Section("Output figure", figureSizeText));
            inspector.Children.Add(Section("Typography",
                Labeled("Base font pt", fontSizeStepper),
                Text("Ticks use the base size; axis titles use base + 1 pt; parameter and info boxes use 6 pt; panel letters use 10 pt.")));
            inspector.Children.Add(Section("Data points",
                Labeled("Size pt", symbolSizeStepper)));
            inspector.Children.Add(Section("Lines and ticks",
                Labeled("Weight", strokeWidthSelector)));
            inspector.Children.Add(Section("Labels",
                panelLettersCheck,
                panelTitlesCheck,
                groupResultsCheck,
                informationBoxesCheck));
            AutomationProperties.SetName(panelTitlesCheck, "Show panel titles");
            AutomationProperties.SetHelpText(panelTitlesCheck, "Add the experiment name after each panel letter.");
            var closeButton = Button("Close", 82);
            closeButton.Click += (_, _) => Close();
            exportButton.Click += async (_, _) => await ExportPdfAsync();

            Content = WorkspaceControlBuilder.Workspace(
                main,
                Scroll(inspector),
                InspectorFooter(Section("Export figure",
                    Row(closeButton, exportButton),
                    statusText)),
                useOuterMargin: true);
        }

        void WireEvents()
        {
            plotWidthBox.TextChanged += (_, _) => RefreshPreview();
            plotHeightBox.TextChanged += (_, _) => RefreshPreview();
            fontSizeStepper.ValueChanged += (_, _) => RefreshPreview();
            symbolSizeStepper.ValueChanged += (_, _) => RefreshPreview();
            strokeWidthSelector.SelectionChanged += (_, _) => RefreshPreview();
            columnsStepper.ValueChanged += (_, _) => RefreshPreview();
            rowsStepper.ValueChanged += (_, _) => RefreshPreview();
            panelLettersCheck.IsCheckedChanged += (_, _) => RefreshPreview();
            panelTitlesCheck.IsCheckedChanged += (_, _) => RefreshPreview();
            groupResultsCheck.IsCheckedChanged += (_, _) => RefreshPreview();
            informationBoxesCheck.IsCheckedChanged += (_, _) => RefreshPreview();
            previewZoomCombo.SelectionChanged += (_, _) => ApplyPreviewZoom();
            sourceFilterBox.TextChanged += (_, _) => RefreshAvailableSources();
        }

        void BuildSourcePicker()
        {
            sourceFilterBox.PlaceholderText = "Filter by name or type";

            var addSelectedButton = Button("Add selected", 96);
            addSelectedButton.Click += (_, _) => AddSelectedSources();
            var cancelButton = Button("Cancel", 72);
            cancelButton.Click += (_, _) => sourcePickerFlyout.IsOpen = false;

            var content = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 6,
                Width = 350
            };
            content.Children.Add(sourceFilterBox);
            var listBorder = ContentBorder(sourcePickerList);
            Grid.SetRow(listBorder, 1);
            content.Children.Add(listBorder);
            var buttons = Row(cancelButton, addSelectedButton);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(buttons, 2);
            content.Children.Add(buttons);
            sourcePickerFlyout.Content = content;
        }

        void OpenSourcePicker(Control target)
        {
            sourceFilterBox.Text = "";
            sourcePickerList.SelectedItems?.Clear();
            RefreshAvailableSources();
            sourcePickerFlyout.ShowAt(target);
            sourceFilterBox.Focus();
        }

        void RefreshAvailableSources()
        {
            var filter = sourceFilterBox.Text?.Trim() ?? "";
            var available = DataManager.SourceItems
                .Where(item => !composition.Contains(item))
                .Where(item => string.IsNullOrWhiteSpace(filter) || SourceMatches(item, filter))
                .ToList();
            sourcePickerList.ItemsSource = available;
        }

        static bool SourceMatches(ITCDataContainer item, string filter)
        {
            var type = item is AnalysisResult ? "result analysis" : "experiment data";
            return (item.Name ?? "").IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                   type.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        Control SourceCell(ITCDataContainer? item, bool showOrder)
        {
            if (item == null) return Text();

            var isResult = item is AnalysisResult;
            var count = isResult ? ((AnalysisResult)item).Solution?.Solutions?.Count ?? 0 : 1;
            var suffix = isResult
                ? $"Result · {count} figure{(count == 1 ? "" : "s")}"
                : "Experiment";
            var panel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(showOrder ? "28,*,Auto" : "*,Auto"),
                ColumnSpacing = 6,
                Margin = new Thickness(7, 3)
            };
            if (showOrder)
            {
                var order = new TextBlock
                {
                    Text = $"{composition.IndexOf(item) + 1}.",
                    VerticalAlignment = VerticalAlignment.Center
                };
                AppTheme.Bind(order, TextBlock.ForegroundProperty, AppTheme.MutedText);
                panel.Children.Add(order);
            }
            var name = new TextBlock
            {
                Text = item.Name,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            AppTheme.Bind(name, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
            Grid.SetColumn(name, showOrder ? 1 : 0);
            panel.Children.Add(name);
            var detail = new TextBlock
            {
                Text = suffix,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            AppTheme.Bind(detail, TextBlock.ForegroundProperty, AppTheme.MutedText);
            Grid.SetColumn(detail, showOrder ? 2 : 1);
            panel.Children.Add(detail);
            return panel;
        }

        void AddSelectedSources()
        {
            var selected = new HashSet<ITCDataContainer>(
                sourcePickerList.SelectedItems?.OfType<ITCDataContainer>() ?? Enumerable.Empty<ITCDataContainer>());
            ITCDataContainer? lastAdded = null;
            foreach (var item in DataManager.SourceItems.Where(selected.Contains))
            {
                if (composition.Contains(item)) continue;
                composition.Add(item);
                lastAdded = item;
            }
            sourcePickerFlyout.IsOpen = false;
            sourceFilterBox.Text = "";
            sourcePickerList.SelectedItems?.Clear();
            RefreshCompositionList(lastAdded);
            RefreshPreview();
        }

        void RemoveSelectedComposition()
        {
            if (!(compositionList.SelectedItem is ITCDataContainer selected)) return;
            var index = composition.IndexOf(selected);
            if (index < 0) return;
            composition.RemoveAt(index);
            var next = composition.Count == 0 ? null : composition[Math.Min(index, composition.Count - 1)];
            RefreshCompositionList(next);
            RefreshPreview();
        }

        void MoveSelected(int offset)
        {
            if (!(compositionList.SelectedItem is ITCDataContainer selected)) return;
            var index = composition.IndexOf(selected);
            var target = index + offset;
            if (index < 0 || target < 0 || target >= composition.Count) return;
            composition.RemoveAt(index);
            composition.Insert(target, selected);
            RefreshCompositionList(selected);
            RefreshPreview();
        }

        void RefreshCompositionList(ITCDataContainer? selected)
        {
            var current = composition.OfType<AnalysisResult>().ToHashSet();
            foreach (var result in observedResults.Where(result => !current.Contains(result)).ToList())
            {
                result.BindingAssessmentChanged -= OnBindingAssessmentChanged;
                observedResults.Remove(result);
            }
            foreach (var result in current.Where(result => observedResults.Add(result)))
                result.BindingAssessmentChanged += OnBindingAssessmentChanged;
            compositionList.ItemsSource = null;
            compositionList.ItemsSource = composition.ToList();
            compositionList.SelectedItem = selected;
        }

        void OnBindingAssessmentChanged(object? sender, EventArgs e)
            => global::Avalonia.Threading.Dispatcher.UIThread.Post(RefreshPreview);

        void RefreshPreview()
        {
            try
            {
                var canvasOptions = CurrentCanvasOptions();
                var document = PublicationFigureCanvasBuilder.Build(composition, figureOptions, canvasOptions);
                var plan = renderer.CreatePlan(document);
                currentPlan = plan;
                exportButton.IsEnabled = plan.IsValid;

                if (!plan.IsValid)
                {
                    ClearPreview(keepPlan: true);
                    plotSizeText.Text = "Plot size: —";
                    figureSizeText.Text = "Figure dimensions: —";
                    previewFigureSizeText.Text = "Figure dimensions: —";
                    statusText.Text = plan.ValidationError;
                    return;
                }

                plotSizeText.Text = $"Plot size: {plan.LayoutResult.PlotWidthCentimeters:F2} × {plan.LayoutResult.PlotHeightCentimeters:F2} cm";
                figureSizeText.Text = $"Figure dimensions: {plan.LayoutResult.FigureWidthCentimeters:F2} × {plan.LayoutResult.FigureHeightCentimeters:F2} cm";
                previewFigureSizeText.Text = $"{plan.LayoutResult.FigureWidthCentimeters:F2} × {plan.LayoutResult.FigureHeightCentimeters:F2} cm";
                var smallPlotWarning = plan.LayoutResult.PlotWidthCentimeters < 2.5 || plan.LayoutResult.PlotHeightCentimeters < 4
                    ? "The common plot size is small; consider increasing it. · "
                    : "";
                statusText.Text = $"{smallPlotWarning}{plan.Document.Cells.Count} panels · {plan.PageCount} page{(plan.PageCount == 1 ? "" : "s")}";

                ClearPreview(keepPlan: true);
                var pages = plan.Pages.Count == 0 ? new[] { plan } : plan.Pages;
                for (var index = 0; index < pages.Count; index++)
                {
                    var page = pages[index];
                    var label = new TextBlock
                    {
                        Text = $"Page {index + 1} · {page.LayoutResult.FigureWidthCentimeters:F2} × {page.LayoutResult.FigureHeightCentimeters:F2} cm",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    AppTheme.Bind(label, TextBlock.ForegroundProperty, AppTheme.MutedText);
                    previewPages.Children.Add(label);
                    using var rendered = renderer.RenderPageBitmap(plan, index, 1400);
                    var bitmap = ToAvaloniaBitmap(rendered);
                    var image = new Image { Stretch = Stretch.Uniform, Source = bitmap };
                    previewBitmaps.Add(bitmap);
                    previewImages.Add(image);
                    previewPages.Children.Add(image);
                }
                ApplyPreviewZoom();
            }
            catch (Exception ex)
            {
                currentPlan = null;
                exportButton.IsEnabled = false;
                ClearPreview();
                plotSizeText.Text = "Plot size: —";
                figureSizeText.Text = "Figure dimensions: —";
                previewFigureSizeText.Text = "Figure dimensions: —";
                statusText.Text = $"Could not render figure: {ex.Message}";
            }
        }

        PublicationFigureCanvasOptions CurrentCanvasOptions()
        {
            return new PublicationFigureCanvasOptions
            {
                PlotWidthCentimeters = ParseDouble(plotWidthBox.Text, canvasDefaults.PlotWidthCentimeters),
                PlotHeightCentimeters = ParseDouble(plotHeightBox.Text, canvasDefaults.PlotHeightCentimeters),
                FontSize = (double)(fontSizeStepper.Value ?? (decimal)canvasDefaults.FontSize),
                SymbolSize = (double)(symbolSizeStepper.Value ?? (decimal)canvasDefaults.SymbolSize),
                StrokeWidth = strokeWidthSelector.SelectedIndex == 0 ? 0.5 : 1,
                Columns = (int)(columnsStepper.Value ?? canvasDefaults.Columns),
                Rows = (int)(rowsStepper.Value ?? canvasDefaults.Rows),
                ShowPanelLetters = panelLettersCheck.IsChecked ?? canvasDefaults.ShowPanelLetters,
                ShowPanelTitles = panelTitlesCheck.IsChecked ?? canvasDefaults.ShowPanelTitles,
                GroupResultFigures = groupResultsCheck.IsChecked ?? canvasDefaults.GroupResultFigures,
                ShowInformationBoxes = informationBoxesCheck.IsChecked ?? canvasDefaults.ShowInformationBoxes
            };
        }

        void ApplyCanvasDefaults()
        {
            plotWidthBox.Text = canvasDefaults.PlotWidthCentimeters.ToString("G6", CultureInfo.CurrentCulture);
            plotHeightBox.Text = canvasDefaults.PlotHeightCentimeters.ToString("G6", CultureInfo.CurrentCulture);
            fontSizeStepper.Value = (decimal)canvasDefaults.FontSize;
            symbolSizeStepper.Value = (decimal)canvasDefaults.SymbolSize;
            strokeWidthSelector.SelectedIndex = canvasDefaults.StrokeWidth <= 0.5 ? 0 : 1;
            columnsStepper.Value = canvasDefaults.Columns;
            rowsStepper.Value = canvasDefaults.Rows;
            panelLettersCheck.IsChecked = canvasDefaults.ShowPanelLetters;
            panelTitlesCheck.IsChecked = canvasDefaults.ShowPanelTitles;
            groupResultsCheck.IsChecked = canvasDefaults.GroupResultFigures;
            informationBoxesCheck.IsChecked = canvasDefaults.ShowInformationBoxes;
        }

        void ApplyPreviewZoom()
        {
            if (currentPlan == null || !currentPlan.IsValid) return;
            var zoom = previewZoomCombo.SelectedIndex switch
            {
                0 => 0.25,
                1 => 0.5,
                2 => 0.75,
                _ => 1.0
            };
            const double dipsPerPdfPoint = 96.0 / 72.0;
            for (var index = 0; index < previewImages.Count; index++)
            {
                var page = currentPlan.Pages.Count == 0 ? currentPlan : currentPlan.Pages[index];
                previewImages[index].Width = page.CanvasWidth * dipsPerPdfPoint * zoom;
                previewImages[index].Height = page.CanvasHeight * dipsPerPdfPoint * zoom;
            }
        }

        async Task ExportPdfAsync()
        {
            var plan = currentPlan;
            if (plan == null || !plan.IsValid) return;

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Supporting Figure",
                SuggestedFileName = "supporting-figure.pdf",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Vector PDF") { Patterns = new[] { "*.pdf" } },
                    FilePickerFileTypes.All
                }
            });
            var path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";

            renderer.WritePdf(plan, path);
            statusText.Text = $"Supporting figure PDF exported · {plan.LayoutResult.FigureWidthCentimeters:F2} × {plan.LayoutResult.FigureHeightCentimeters:F2} cm.";
            StatusBarManager.SetStatus("Supporting figure PDF exported", 3000);
        }

        void ClearPreview(bool keepPlan = false)
        {
            previewPages.Children.Clear();
            foreach (var bitmap in previewBitmaps) bitmap.Dispose();
            previewBitmaps.Clear();
            previewImages.Clear();
            if (!keepPlan) currentPlan = null;
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

        static double ParseDouble(string? text, double fallback)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
                   double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

    }
}
