using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SkiaSharp;

using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Avalonia.Controls;
using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using static AnalysisITC.Avalonia.Workspace.WorkspaceControlBuilder;

namespace AnalysisITC.Avalonia.Tools
{
    public sealed class AnalysisReportWindow : Window
    {
        static readonly EnergyUnitFamily[] EnergyFamilies =
        {
            EnergyUnitFamily.Joules, EnergyUnitFamily.Calories
        };
        static readonly Dictionary<string, HashSet<string>> SessionAdvancedSelections = new();
        static int sessionEnergyIndex = AppSettings.EnergyUnitFamily == EnergyUnitFamily.Calories ? 1 : 0;
        static int sessionTemperatureIndex;
        static int sessionUncertaintyIndex = 3;
        static bool sessionIncludeInjectionTables = true;
        static bool sessionCondenseRepeatedExperiments = true;
        static readonly double[] PreviewZoomLevels = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
        static readonly HttpClient InterpretationHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        static readonly Uri InterpretationBaseUri = new Uri("https://app.ft-itc.org");

        readonly SkiaAnalysisReportRenderer renderer = new SkiaAnalysisReportRenderer();
        readonly Button selectResultsButton = Button("Select results…", 0);
        readonly Flyout resultPickerFlyout = new Flyout();
        readonly StackPanel resultPickerItems = new StackPanel { Spacing = 4 };
        readonly TextBlock resultSummaryText = Text("No result selected.");
        readonly TextBox labelBox = TextBox();
        readonly TextBox titleBox = TextBox();
        readonly ComboBox energyCombo = Combo(new[] { "Joule", "Calories" });
        readonly ComboBox temperatureCombo = Combo(new[] { "Celsius", "Kelvin" });
        readonly ComboBox uncertaintyCombo = Combo(new[] { "Automatic", "Standard deviation (SD)", "95% CI", "SD + 95% CI", "None" });
        readonly StackPanel advancedPanel = new StackPanel { Spacing = 2 };
        readonly CheckBox injectionTablesCheck = Check("Injection tables");
        readonly CheckBox condenseRepeatedCheck = Check("Condense repeated experiments");
        readonly SegmentedSelector workspaceSelector = new SegmentedSelector(new[] { "Interpretation", "Preview" });
        readonly TextBox interpretationBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        readonly TextBlock interpretationWorkspaceStatus = Text();
        readonly TextBlock interpretationSummaryText = Text("No interpretation");
        readonly Button editInterpretationButton = Button("Edit interpretation", 128);
        readonly Button generateInterpretationButton = Button("Generate with AI...", 142);
        readonly Grid interpretationHost = new Grid();
        readonly Grid previewHost = new Grid();
        readonly ItemsControl previewPages = new ItemsControl { Focusable = false };
        readonly ComboBox previewZoomCombo = Combo(
            new[] { "50%", "75%", "100%", "125%", "150%", "200%" }, 2, 86);
        readonly ScrollViewer previewScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        readonly TextBlock previewPlaceholder = Text();
        readonly TextBlock statusText = Text();
        readonly Border statusHost = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 6), BorderThickness = new Thickness(1), IsVisible = false };
        readonly ProgressBar progress = new ProgressBar { IsIndeterminate = true, IsVisible = false, Height = 3 };
        readonly Button previewButton = Button("Update Preview", 112);
        readonly Button exportButton = Button("Export...", 86);
        readonly List<CheckBox> advancedChecks = new();
        readonly Button selectAllButton = Button("Select all", 76);
        readonly Button clearButton = Button("Clear", 58);
        readonly List<AnalysisReportPreviewPage> pageViews = new();
        readonly List<AnalysisResult> availableResults = new();
        readonly List<AnalysisResult> selectedResults = new();
        readonly List<ExperimentData> availableExperiments = new();
        readonly List<ExperimentData> selectedSupportingExperiments = new();
        readonly List<CheckBox> resultPickerChecks = new();

        AnalysisReportDocument? currentDocument;
        AnalysisReportLayoutPlan? currentPlan;
        bool previewStale = true;
        bool changingResult;
        bool automaticTitle = true;
        bool changingWorkspace;
        bool busy;
        bool loadingInterpretation;
        bool previewPinchActive;
        double previewPinchStartZoom = 1.0;
        DateTime lastPreviewWheelEventUtc;
        int lastPreviewWheelDirection;
        DateTime lastPreviewMagnifyEventUtc;
        double previewMagnifyAccumulator;
        AnalysisReport? report;

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
            injectionTablesCheck.IsChecked = sessionIncludeInjectionTables;
            condenseRepeatedCheck.IsChecked = sessionCondenseRepeatedExperiments;
            BuildLayout();
            WireEvents();
            PopulateResults(selectedResult);
        }

        protected override void OnClosed(EventArgs e)
        {
            CommitInterpretationEditor();
            ClearPreview();
            base.OnClosed(e);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            ShowInterpretationWorkspace(focusEditor: true);
        }

        void BuildLayout()
        {
            selectResultsButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            selectResultsButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            selectResultsButton.Height = 42;
            selectResultsButton.MinHeight = 42;
            BuildResultPicker();
            resultSummaryText.FontSize = 11;
            resultSummaryText.TextWrapping = TextWrapping.Wrap;
            AppTheme.Bind(resultSummaryText, TextBlock.ForegroundProperty, AppTheme.MutedText);
            previewPlaceholder.Text = "No preview yet\nSelect Preview to build the report.";
            previewPlaceholder.TextAlignment = TextAlignment.Center;
            previewPlaceholder.LineHeight = 22;
            previewPlaceholder.HorizontalAlignment = HorizontalAlignment.Center;
            previewPlaceholder.VerticalAlignment = VerticalAlignment.Center;
            AppTheme.Bind(previewPlaceholder, TextBlock.ForegroundProperty, AppTheme.MutedText);

            AppTheme.Bind(previewHost, Panel.BackgroundProperty, AppTheme.PreviewBackground);
            previewHost.RowDefinitions = new RowDefinitions("Auto,*");
            previewPages.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
            previewPages.IsVisible = false;
            previewScroll.Content = previewPages;
            previewScroll.Background = Brushes.Transparent;
            previewScroll.GestureRecognizers.Add(new PinchGestureRecognizer());
            var previewZoomToolbar = new Border
            {
                Padding = new Thickness(10, 6),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = Row(Text("Zoom"), previewZoomCombo),
            };
            previewZoomToolbar.Child.HorizontalAlignment = HorizontalAlignment.Right;
            AppTheme.Bind(previewZoomToolbar, Border.BackgroundProperty, AppTheme.PanelBackground);
            AppTheme.Bind(previewZoomToolbar, Border.BorderBrushProperty, AppTheme.SectionBorder);
            previewHost.Children.Add(previewZoomToolbar);
            Grid.SetRow(previewScroll, 1);
            previewHost.Children.Add(previewScroll);
            Grid.SetRow(previewPlaceholder, 1);
            previewHost.Children.Add(previewPlaceholder);

            interpretationWorkspaceStatus.FontSize = 11;
            interpretationWorkspaceStatus.TextWrapping = TextWrapping.Wrap;
            AppTheme.Bind(interpretationWorkspaceStatus, TextBlock.ForegroundProperty, AppTheme.MutedText);
            var interpretationHeading = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock { Text = "Report interpretation", FontSize = 18, FontWeight = FontWeight.SemiBold },
                    interpretationWorkspaceStatus,
                }
            };
            var editorFrame = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = interpretationBox,
            };
            AppTheme.Bind(editorFrame, Border.BackgroundProperty, AppTheme.PanelBackground);
            AppTheme.Bind(editorFrame, Border.BorderBrushProperty, AppTheme.PanelBorder);
            var editorLayout = new Grid
            {
                Margin = new Thickness(24),
                RowDefinitions = new RowDefinitions("Auto,*"),
                RowSpacing = 14,
            };
            editorLayout.Children.Add(interpretationHeading);
            Grid.SetRow(editorFrame, 1);
            editorLayout.Children.Add(editorFrame);
            interpretationHost.Children.Add(editorLayout);

            var workspaceContent = new Grid();
            workspaceContent.Children.Add(previewHost);
            workspaceContent.Children.Add(interpretationHost);
            previewHost.IsVisible = false;
            interpretationHost.IsVisible = true;
            var workspaceHeader = new Border
            {
                Padding = new Thickness(14, 9),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = workspaceSelector,
            };
            AppTheme.Bind(workspaceHeader, Border.BackgroundProperty, AppTheme.PanelBackground);
            AppTheme.Bind(workspaceHeader, Border.BorderBrushProperty, AppTheme.SectionBorder);
            workspaceSelector.Width = 248;
            workspaceSelector.HorizontalAlignment = HorizontalAlignment.Center;
            var workspace = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            workspace.Children.Add(workspaceHeader);
            Grid.SetRow(workspaceContent, 1);
            workspace.Children.Add(workspaceContent);
            var border = ContentBorder(workspace);

            var inspector = InspectorPanel();
            var heading = new TextBlock { Text = "Build Analysis Report", FontSize = 18, FontWeight = FontWeight.SemiBold };
            var help = new TextBlock { Text = "Choose the result and content to include in the PDF.", TextWrapping = TextWrapping.Wrap };
            AppTheme.Bind(help, TextBlock.ForegroundProperty, AppTheme.MutedText);
            inspector.Children.Add(Section(heading, help));
            inspector.Children.Add(Section("Report contents", selectResultsButton, resultSummaryText));
            inspector.Children.Add(Section("Document",
                Labeled("Title", titleBox),
                Labeled("Subtitle", labelBox)));
            inspector.Children.Add(Section("Presentation",
                Labeled("Energy", energyCombo),
                Labeled("Temperature", temperatureCombo),
                Labeled("Uncertainties", uncertaintyCombo)));

            selectAllButton.Click += (_, _) => SetAllAdvanced(true);
            clearButton.Click += (_, _) => SetAllAdvanced(false);
            var actions = EqualWidthRow(selectAllButton, clearButton);
            inspector.Children.Add(Section("Optional content", injectionTablesCheck,
                condenseRepeatedCheck, advancedPanel, actions));
            interpretationSummaryText.FontSize = 11;
            interpretationSummaryText.TextWrapping = TextWrapping.Wrap;
            AppTheme.Bind(interpretationSummaryText, TextBlock.ForegroundProperty, AppTheme.MutedText);
            var interpretationActions = EqualWidthRow(editInterpretationButton, generateInterpretationButton);
            inspector.Children.Add(Section("Interpretation", interpretationSummaryText, interpretationActions));

            var cancel = Button("Cancel", 78);
            cancel.Click += (_, _) => Close(false);
            previewButton.Click += async (_, _) => await PreviewAsync();
            exportButton.Click += async (_, _) => await ExportAsync();
            var buttons = Row(cancel, previewButton, exportButton);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            statusHost.Child = statusText;
            var footer = InspectorFooter(Section("PDF export", progress, statusHost, buttons));

            AutomationProperties.SetName(selectResultsButton, "Select report contents");
            AutomationProperties.SetName(resultSummaryText, "Selected report contents details");
            AutomationProperties.SetName(labelBox, "Report subtitle");
            AutomationProperties.SetName(titleBox, "Report title");
            AutomationProperties.SetName(energyCombo, "Energy units");
            AutomationProperties.SetName(temperatureCombo, "Temperature units");
            AutomationProperties.SetName(uncertaintyCombo, "Uncertainties");
            AutomationProperties.SetName(workspaceSelector, "Report workspace view");
            AutomationProperties.SetName(interpretationHost, "Interpretation workspace");
            AutomationProperties.SetName(previewHost, "Report preview workspace");
            AutomationProperties.SetName(previewPages, "Report preview pages");
            AutomationProperties.SetName(previewZoomCombo, "Report preview zoom");
            AutomationProperties.SetHelpText(previewZoomCombo,
                "Change the report preview magnification. Control-scroll or Command-scroll also adjusts zoom.");
            AutomationProperties.SetName(interpretationBox, "Report interpretation editor");
            AutomationProperties.SetHelpText(interpretationBox, "Write an interpretation or approve an AI-generated draft for inclusion in the report.");
            AutomationProperties.SetName(interpretationSummaryText, "Interpretation status");
            AutomationProperties.SetName(editInterpretationButton, "Edit report interpretation");
            AutomationProperties.SetName(generateInterpretationButton, "Generate interpretation with AI");
            AutomationProperties.SetName(injectionTablesCheck, "Include injection tables");
            AutomationProperties.SetName(condenseRepeatedCheck, "Condense repeated experiments");
            AutomationProperties.SetHelpText(condenseRepeatedCheck,
                "For later appearances of the same experiment, retain figures, comments, and core conditions while omitting repeated processing details.");
            AutomationProperties.SetName(previewButton, "Update report preview");
            AutomationProperties.SetName(exportButton, "Export analysis report as PDF");
            AutomationProperties.SetName(statusHost, "Report status");
            AutomationProperties.SetName(footer, "Report export footer");
            AutomationProperties.SetLiveSetting(statusHost, AutomationLiveSetting.Polite);

            Content = WorkspaceControlBuilder.Workspace(border, Scroll(inspector), footer, useOuterMargin: true);
        }

        void WireEvents()
        {
            selectResultsButton.Click += (_, _) => OpenResultPicker();
            labelBox.TextChanged += (_, _) => MarkStale();
            titleBox.TextChanged += (_, _) => { if (!changingResult) automaticTitle = false; MarkStale(); };
            energyCombo.SelectionChanged += (_, _) => { sessionEnergyIndex = energyCombo.SelectedIndex; MarkStale(); };
            temperatureCombo.SelectionChanged += (_, _) => { sessionTemperatureIndex = temperatureCombo.SelectedIndex; MarkStale(); };
            uncertaintyCombo.SelectionChanged += (_, _) => { sessionUncertaintyIndex = uncertaintyCombo.SelectedIndex; MarkStale(); };
            injectionTablesCheck.IsCheckedChanged += (_, _) =>
            {
                sessionIncludeInjectionTables = injectionTablesCheck.IsChecked == true;
                MarkStale();
            };
            condenseRepeatedCheck.IsCheckedChanged += (_, _) =>
            {
                sessionCondenseRepeatedExperiments = condenseRepeatedCheck.IsChecked == true;
                MarkStale();
            };
            workspaceSelector.SelectionChanged += async (_, _) => await WorkspaceSelectionChangedAsync();
            previewZoomCombo.SelectionChanged += (_, _) => ApplyPreviewZoom();
            previewScroll.AddHandler(InputElement.PointerWheelChangedEvent,
                PreviewPointerWheelChanged, RoutingStrategies.Tunnel, true);
            previewScroll.AddHandler(InputElement.PointerTouchPadGestureMagnifyEvent,
                PreviewTouchPadMagnify, RoutingStrategies.Tunnel, true);
            previewScroll.Pinch += PreviewPinch;
            previewScroll.PinchEnded += PreviewPinchEnded;
            interpretationBox.TextChanged += (_, _) => { if (!loadingInterpretation) MarkStale(); };
            interpretationBox.LostFocus += (_, _) => CommitInterpretationEditor();
            editInterpretationButton.Click += (_, _) => ShowInterpretationWorkspace(focusEditor: true);
            generateInterpretationButton.Click += async (_, _) => await GenerateInterpretationAsync();
        }

        void PopulateResults(AnalysisResult? selected)
        {
            availableResults.Clear();
            availableResults.AddRange(DataManager.Results);
            availableExperiments.Clear();
            availableExperiments.AddRange(DataManager.Data);
            var initial = selected != null && availableResults.Contains(selected) ? selected
                : DataManager.SelectedResult != null && availableResults.Contains(DataManager.SelectedResult)
                    ? DataManager.SelectedResult : availableResults.FirstOrDefault();
            var initialResults = initial == null ? Array.Empty<AnalysisResult>() : new[] { initial };
            ApplyResultSelection(initialResults, false, AppSettings.AutoSelectReportReferenceExperiments
                ? ReferenceExperimentsFor(initialResults) : Enumerable.Empty<ExperimentData>());
        }

        void BuildResultPicker()
        {
            var selectAll = Button("Select all", 78);
            var clear = Button("Clear", 58);
            var cancel = Button("Cancel", 68);
            var apply = Button("Apply", 68);
            void UpdateExperimentAvailability()
            {
                var coveredIds = resultPickerChecks
                    .Where(check => check.Tag is AnalysisResult && check.IsChecked == true)
                    .SelectMany(check => ((AnalysisResult)check.Tag!).Solution?.Solutions ?? new List<SolutionInterface>())
                    .Select(solution => solution?.Data?.UniqueID)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var check in resultPickerChecks.Where(check => check.Tag is ExperimentData))
                    check.IsEnabled = !coveredIds.Contains(((ExperimentData)check.Tag!).UniqueID);
            }
            selectAll.Click += (_, _) =>
            {
                foreach (var check in resultPickerChecks.Where(check => check.Tag is AnalysisResult)) check.IsChecked = true;
                UpdateExperimentAvailability();
                foreach (var check in resultPickerChecks.Where(check => check.Tag is ExperimentData && check.IsEnabled)) check.IsChecked = true;
                UpdateResultPickerApply(apply);
            };
            clear.Click += (_, _) =>
            {
                foreach (var check in resultPickerChecks) check.IsChecked = false;
                UpdateExperimentAvailability(); UpdateResultPickerApply(apply);
            };
            cancel.Click += (_, _) => resultPickerFlyout.IsOpen = false;
            apply.Click += (_, _) =>
            {
                if (!CommitInterpretationEditor()) return;
                var chosen = availableResults.Where(result => resultPickerChecks.Any(check =>
                    ReferenceEquals(check.Tag, result) && check.IsChecked == true)).ToList();
                var chosenExperiments = availableExperiments.Where(experiment => resultPickerChecks.Any(check =>
                    ReferenceEquals(check.Tag, experiment) && check.IsChecked == true)).ToList();
                if (chosen.Count == 0) return;
                ApplyResultSelection(chosen, true, chosenExperiments);
                resultPickerFlyout.IsOpen = false;
            };
            resultPickerFlyout.Opened += (_, _) =>
            {
                resultPickerItems.Children.Clear(); resultPickerChecks.Clear();
                Action<ITCDataContainer> addPickerRow = item =>
                {
                    var result = item as AnalysisResult;
                    var experiment = item as ExperimentData;
                    var details = new TextBlock
                    {
                        Text = result != null ? ResultPickerDetails(result) : ExperimentPickerDetails(experiment!), FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                    };
                    AppTheme.Bind(details, TextBlock.ForegroundProperty, AppTheme.MutedText);
                    var content = new StackPanel { Spacing = 1 };
                    content.Children.Add(new TextBlock
                    {
                        Text = (result != null ? "Result · " : "Experiment · ") + item.Name,
                        FontWeight = FontWeight.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                    content.Children.Add(details);
                    var check = new CheckBox
                    {
                        Content = content,
                        Tag = item,
                        IsChecked = result != null
                            ? selectedResults.Contains(result)
                            : selectedSupportingExperiments.Contains(experiment!),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MinHeight = 52,
                    };
                    AutomationProperties.SetName(check, "Include "
                        + (result != null ? "result " : "supporting experiment ") + item.Name);
                    check.IsCheckedChanged += (_, _) =>
                    {
                        if (AppSettings.AutoSelectReportReferenceExperiments
                            && check.Tag is AnalysisResult selectedResult
                            && check.IsChecked == true)
                        {
                            var references = ReferenceExperimentsFor(new[] { selectedResult }).ToHashSet();
                            foreach (var referenceCheck in resultPickerChecks.Where(candidate =>
                                candidate.Tag is ExperimentData experiment && references.Contains(experiment)))
                                referenceCheck.IsChecked = true;
                        }
                        UpdateExperimentAvailability();
                        UpdateResultPickerApply(apply);
                    };
                    resultPickerChecks.Add(check); resultPickerItems.Children.Add(check);
                };
                foreach (var result in availableResults) addPickerRow(result);
                if (availableResults.Count > 0 && availableExperiments.Count > 0)
                {
                    resultPickerItems.Children.Add(new Separator
                    {
                        Margin = new Thickness(0, 4),
                    });
                }
                foreach (var experiment in availableExperiments) addPickerRow(experiment);
                UpdateExperimentAvailability();
                UpdateResultPickerApply(apply);
            };
            var scroll = new ScrollViewer { Content = resultPickerItems, MaxHeight = 390, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 6 };
            footer.Children.Add(selectAll); Grid.SetColumn(clear, 1); footer.Children.Add(clear);
            Grid.SetColumn(cancel, 3); footer.Children.Add(cancel); Grid.SetColumn(apply, 4); footer.Children.Add(apply);
            var contentHost = new Grid { Width = 420, RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 10 };
            contentHost.Children.Add(scroll); Grid.SetRow(footer, 1); contentHost.Children.Add(footer);
            resultPickerFlyout.Content = contentHost;
        }

        void OpenResultPicker() => resultPickerFlyout.ShowAt(selectResultsButton);

        void UpdateResultPickerApply(Button apply) =>
            apply.IsEnabled = resultPickerChecks.Any(check => check.IsChecked == true
                && check.Tag is AnalysisResult);

        static string ResultPickerDetails(AnalysisResult result)
        {
            var model = result.Model?.ModelType.GetProperties().Name ?? "Unknown model";
            var count = result.Solution?.Solutions?.Count ?? 0;
            return result.Date.ToString("g") + " • " + model + " • " + count +
                " experiment" + (count == 1 ? "" : "s");
        }

        string ExperimentPickerDetails(ExperimentData experiment)
        {
            var processing = experiment.HasThermogram
                ? experiment.Processor?.BaselineCompleted == true ? "processed thermogram" : "thermogram; baseline incomplete"
                : experiment.Injections?.Any(injection => injection.IsIntegrated) == true ? "integrated heats only" : "processing incomplete";
            var covered = CoveringResult(experiment);
            var reference = ReferenceTargets(experiment).FirstOrDefault();
            var relation = covered != null ? " • Included through " + covered
                : reference != null ? " • Subtraction reference for " + reference : "";
            var date = experiment.DateSource == ExperimentDateSource.DataFile
                || experiment.DateSource == ExperimentDateSource.UserModified
                ? experiment.Date.ToString("d") + " • " : "";
            return date + processing + relation;
        }

        Control ResultCell(AnalysisResult? result)
        {
            if (result == null) return Text();
            var panel = new StackPanel { Spacing = 1, Margin = new Thickness(5, 3) };
            panel.Children.Add(new TextBlock { Text = result.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var details = new TextBlock
            {
                Text = result.Date.ToString("g"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            AppTheme.Bind(details, TextBlock.ForegroundProperty, AppTheme.MutedText);
            panel.Children.Add(details);
            return panel;
        }

        void ApplyResultSelection(IEnumerable<AnalysisResult> selection, bool preserveCustomTitle,
            IEnumerable<ExperimentData>? supportingSelection = null)
        {
            changingResult = true;
            try
            {
                selectedResults.Clear();
                selectedResults.AddRange(availableResults.Where(result => selection.Contains(result)));
                condenseRepeatedCheck.IsEnabled = !busy
                    && AnalysisReportBuilder.HasRepeatedExperiments(selectedResults);
                selectedSupportingExperiments.Clear();
                var requestedSupporting = supportingSelection ?? Enumerable.Empty<ExperimentData>();
                selectedSupportingExperiments.AddRange(availableExperiments.Where(requestedSupporting.Contains));
                var effectiveCount = EffectiveSupportingExperiments().Count;
                selectResultsButton.Content = selectedResults.Count == 0 ? "Select report contents…"
                    : selectedResults.Count + " result" + (selectedResults.Count == 1 ? "" : "s")
                    + (effectiveCount > 0 ? " · " + effectiveCount + " supporting" : "");
                resultSummaryText.Text = selectedResults.Count == 0 ? "No result selected."
                    : ResultSummary(selectedResults) + " · " + effectiveCount
                        + " supporting experiment" + (effectiveCount == 1 ? "" : "s")
                        + (selectedResults.Count > 1 ? "\n" + string.Join(" · ", selectedResults.Select(result => result.Name)) : "");
                if (!preserveCustomTitle || automaticTitle)
                {
                    automaticTitle = true;
                    titleBox.Text = selectedResults.Count == 1 ? selectedResults[0].Name
                        : selectedResults.Count > 1 ? "Analysis report" : "";
                }
                RebuildAdvancedChoices(selectedResults);
                LoadReport(selectedResults, selectedSupportingExperiments);
                if (selectedSupportingExperiments.Count > 0) EnsureReportRegistered();
                if (selectedResults.Count > 0) ShowValidation(selectedResults);
                else
                {
                    previewButton.IsEnabled = exportButton.IsEnabled = false;
                    SetStatus("");
                }
            }
            finally { changingResult = false; }
            MarkStale();
        }

        void LoadReport(IReadOnlyList<AnalysisResult> results, IReadOnlyList<ExperimentData> experiments)
        {
            var ids = results.Select(result => result.UniqueID).ToList();
            report = ids.Count == 0 ? null : DataManager.Reports.FirstOrDefault(item =>
                item.ResultIds.SequenceEqual(ids, StringComparer.Ordinal)
                && item.SupportingExperimentIds.SequenceEqual(
                    experiments.Select(experiment => experiment.UniqueID), StringComparer.Ordinal));
            if (report == null && ids.Count > 0)
            {
                report = new AnalysisReport { Name = ids.Count == 1 ? results[0].Name + " report" : "Analysis report" };
                report.SetResultIds(ids);
                report.SetSupportingExperimentIds(experiments.Select(experiment => experiment.UniqueID));
            }
            loadingInterpretation = true;
            interpretationBox.Text = report?.ApprovedInterpretation?.InterpretationMarkdown ?? "";
            loadingInterpretation = false;
            UpdateInterpretationStatus();
        }

        List<ExperimentData> EffectiveSupportingExperiments()
        {
            var memberIds = selectedResults.SelectMany(result => result.Solution?.Solutions ?? new List<SolutionInterface>())
                .Select(solution => solution?.Data?.UniqueID).Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);
            return selectedSupportingExperiments.Where(experiment => !memberIds.Contains(experiment.UniqueID)).ToList();
        }

        IEnumerable<ExperimentData> ReferenceExperimentsFor(IEnumerable<AnalysisResult> selection)
        {
            var referenceIds = selection
                .SelectMany(result => result.Solution?.Solutions ?? new List<SolutionInterface>())
                .Select(solution => solution?.Data?.BufferSubtractionSettings?.ReferenceExperimentId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);
            return availableExperiments.Where(experiment => referenceIds.Contains(experiment.UniqueID));
        }

        string? CoveringResult(ExperimentData experiment)
        {
            for (var resultIndex = 0; resultIndex < selectedResults.Count; resultIndex++)
                if ((selectedResults[resultIndex].Solution?.Solutions ?? new List<SolutionInterface>())
                    .Any(solution => solution?.Data?.UniqueID == experiment.UniqueID))
                    return "Result " + (resultIndex + 1);
            return null;
        }

        IEnumerable<string> ReferenceTargets(ExperimentData experiment)
        {
            for (var resultIndex = 0; resultIndex < selectedResults.Count; resultIndex++)
            for (var memberIndex = 0; memberIndex < (selectedResults[resultIndex].Solution?.Solutions?.Count ?? 0); memberIndex++)
                if (selectedResults[resultIndex].Solution.Solutions[memberIndex]?.Data?.BufferSubtractionSettings?.ReferenceExperimentId == experiment.UniqueID)
                    yield return AnalysisReportReferenceLabels.Experiment(resultIndex, memberIndex);
        }

        void EnsureReportRegistered()
        {
            if (report != null && !DataManager.Reports.Contains(report)) DataManager.AddReport(report);
        }

        bool CommitInterpretationEditor()
        {
            if (loadingInterpretation || report == null) return true;
            var current = report.ApprovedInterpretation?.InterpretationMarkdown ?? "";
            var edited = interpretationBox.Text ?? "";
            if (string.Equals(current.Trim(), edited.Trim(), StringComparison.Ordinal)) return true;
            try
            {
                report.UpdateApprovedInterpretationText(edited);
                EnsureReportRegistered();
                UpdateInterpretationStatus();
                return true;
            }
            catch (AnalysisInterpretationValidationException ex)
            {
                SetStatus(ex.Errors.FirstOrDefault() ?? ex.Message, true);
                return false;
            }
        }

        async Task GenerateInterpretationAsync()
        {
            if (busy || report == null || selectedResults.Count == 0) return;
            var dialog = new AnalysisInterpretationDialog(report,
                id => availableResults.FirstOrDefault(result => result.UniqueID == id)!,
                id => availableExperiments.FirstOrDefault(experiment => experiment.UniqueID == id)!,
                InterpretationHttpClient, () =>
            {
                EnsureReportRegistered();
                MarkStale();
                UpdateInterpretationStatus();
            });
            var draft = await dialog.ShowDialog<AnalysisInterpretationRecord?>(this);
            if (draft == null) return;
            report.ApproveInterpretation(draft);
            EnsureReportRegistered();
            loadingInterpretation = true;
            interpretationBox.Text = report.ApprovedInterpretation?.InterpretationMarkdown ?? "";
            loadingInterpretation = false;
            MarkStale();
            UpdateInterpretationStatus();
            ShowInterpretationWorkspace(focusEditor: true);
        }

        void RebuildAdvancedChoices(IReadOnlyList<AnalysisResult> results)
        {
            advancedPanel.Children.Clear();
            advancedChecks.Clear();
            if (results == null || results.Count == 0)
            {
                selectAllButton.IsVisible = false;
                clearButton.IsVisible = false;
                return;
            }
            var descriptors = results.SelectMany(AnalysisReportBuilder.GetAvailableAdvancedSections)
                .GroupBy(item => item.Request.Kind)
                .Select(group => group.First()).ToList();
            var key = ResultKey(results);
            var selected = SessionAdvancedSelections.TryGetValue(key, out var saved)
                ? saved : descriptors.Select(item => item.Request.Kind.ToString()).ToHashSet();
            foreach (var descriptor in descriptors)
            {
                var check = Check(descriptor.Title);
                check.Tag = descriptor;
                check.IsChecked = selected.Contains(descriptor.Request.Kind.ToString());
                ToolTip.SetTip(check, descriptor.Description);
                AutomationProperties.SetName(check, "Include " + descriptor.Title);
                AutomationProperties.SetHelpText(check, descriptor.Description);
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
                var none = Text("No saved advanced analyses are available for this selection.");
                AppTheme.Bind(none, TextBlock.ForegroundProperty, AppTheme.MutedText);
                advancedPanel.Children.Add(none);
            }
            var showBulkActions = advancedChecks.Count > 1;
            selectAllButton.IsVisible = showBulkActions;
            clearButton.IsVisible = showBulkActions;
        }

        void SetAllAdvanced(bool selected)
        {
            foreach (var check in advancedChecks) check.IsChecked = selected;
            SaveAdvancedDraft();
            MarkStale();
        }

        void SaveAdvancedDraft()
        {
            if (selectedResults.Count == 0) return;
            SessionAdvancedSelections[ResultKey(selectedResults)] = advancedChecks
                .Where(check => check.IsChecked == true)
                .Select(check => ((AnalysisReportAdvancedSectionDescriptor)check.Tag!).Request.Kind.ToString())
                .ToHashSet();
        }

        AnalysisReportOptions CurrentOptions()
        {
            var options = new AnalysisReportOptions
            {
                DocumentLabel = labelBox.Text ?? "",
                Title = titleBox.Text ?? "",
                EnergyUnitFamily = energyCombo.SelectedIndex >= 0 && energyCombo.SelectedIndex < EnergyFamilies.Length
                    ? EnergyFamilies[energyCombo.SelectedIndex] : AppSettings.EnergyUnitFamily,
                EnergyUnitOverride = null,
                UseKelvin = temperatureCombo.SelectedIndex == 1,
                IncludeInjectionTables = injectionTablesCheck.IsChecked == true,
                CondenseRepeatedExperiments = condenseRepeatedCheck.IsChecked == true,
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
            if (busy || selectedResults.Count == 0) return;
            if (await BuildAsync(showPreview: true)) ShowPreviewWorkspace();
            else ShowInterpretationWorkspace(focusEditor: true);
        }

        async Task WorkspaceSelectionChangedAsync()
        {
            if (changingWorkspace || busy) return;
            if (workspaceSelector.SelectedIndex == 0)
            {
                ShowInterpretationWorkspace(focusEditor: true);
                return;
            }
            if (previewStale || currentDocument == null || currentPlan == null)
            {
                if (!await BuildAsync(showPreview: true))
                    ShowInterpretationWorkspace(focusEditor: true);
                else ShowPreviewWorkspace();
                return;
            }
            ShowPreviewWorkspace();
        }

        void ShowInterpretationWorkspace(bool focusEditor)
        {
            SetWorkspaceSelection(0);
            interpretationHost.IsVisible = true;
            previewHost.IsVisible = false;
            if (focusEditor && interpretationBox.IsEnabled)
                Dispatcher.UIThread.Post(() => interpretationBox.Focus(), DispatcherPriority.Input);
        }

        void ShowPreviewWorkspace()
        {
            SetWorkspaceSelection(1);
            interpretationHost.IsVisible = false;
            previewHost.IsVisible = true;
        }

        void SetWorkspaceSelection(int index)
        {
            changingWorkspace = true;
            workspaceSelector.SelectedIndex = index;
            changingWorkspace = false;
        }

        async Task<bool> BuildAsync(bool showPreview)
        {
            if (selectedResults.Count == 0) return false;
            if (!CommitInterpretationEditor()) return false;
            var validation = CurrentValidation();
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
                    var document = report == null
                        ? AnalysisReportBuilder.Build(selectedResults, options)
                        : AnalysisReportBuilder.Build(report,
                            id => availableResults.FirstOrDefault(result => result.UniqueID == id),
                            id => availableExperiments.FirstOrDefault(experiment => experiment.UniqueID == id),
                            options);
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
            ApplyPreviewZoom();
            previewPages.ItemsSource = pageViews.ToList();
            previewPages.IsVisible = true;
            previewPlaceholder.IsVisible = false;
        }

        void PreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            var modifier = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (!modifier || e.Delta.Y == 0) return;

            var now = DateTime.UtcNow;
            var direction = e.Delta.Y > 0 ? 1 : -1;
            var startsNewGesture = now - lastPreviewWheelEventUtc > TimeSpan.FromMilliseconds(240)
                || direction != lastPreviewWheelDirection;
            lastPreviewWheelEventUtc = now;
            lastPreviewWheelDirection = direction;
            if (startsNewGesture) StepPreviewZoom(direction);
            e.Handled = true;
        }

        void PreviewTouchPadMagnify(object? sender, PointerDeltaEventArgs e)
        {
            var now = DateTime.UtcNow;
            if (now - lastPreviewMagnifyEventUtc > TimeSpan.FromMilliseconds(240))
                previewMagnifyAccumulator = 0;
            lastPreviewMagnifyEventUtc = now;
            var delta = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X)
                ? e.Delta.Y
                : e.Delta.X;
            previewMagnifyAccumulator += delta;

            const double stepThreshold = 0.12;
            if (Math.Abs(previewMagnifyAccumulator) >= stepThreshold)
            {
                var direction = previewMagnifyAccumulator > 0 ? 1 : -1;
                StepPreviewZoom(direction);
                previewMagnifyAccumulator -= direction * stepThreshold;
            }
            e.Handled = true;
        }

        void PreviewPinch(object? sender, PinchEventArgs e)
        {
            if (!previewPinchActive)
            {
                previewPinchActive = true;
                previewPinchStartZoom = CurrentPreviewZoom();
            }
            var requested = previewPinchStartZoom * e.Scale;
            var nearest = Enumerable.Range(0, PreviewZoomLevels.Length)
                .OrderBy(index => Math.Abs(PreviewZoomLevels[index] - requested))
                .First();
            previewZoomCombo.SelectedIndex = nearest;
            e.Handled = true;
        }

        void PreviewPinchEnded(object? sender, PinchEndedEventArgs e)
        {
            previewPinchActive = false;
            e.Handled = true;
        }

        void ApplyPreviewZoom()
        {
            var zoom = CurrentPreviewZoom();
            foreach (var page in pageViews) page.SetZoom(zoom);
        }

        void StepPreviewZoom(int direction)
        {
            var next = previewZoomCombo.SelectedIndex + Math.Sign(direction);
            previewZoomCombo.SelectedIndex = Math.Max(0,
                Math.Min(PreviewZoomLevels.Length - 1, next));
        }

        double CurrentPreviewZoom()
        {
            var index = previewZoomCombo.SelectedIndex;
            return index >= 0 && index < PreviewZoomLevels.Length
                ? PreviewZoomLevels[index]
                : 1.0;
        }

        async Task ExportAsync()
        {
            if (busy || selectedResults.Count == 0) return;
            if (previewStale || currentDocument == null || currentPlan == null)
                if (!await BuildAsync(showPreview: false)) return;

            var document = currentDocument!;
            var plan = currentPlan!;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Analysis Report",
                SuggestedFileName = SanitizeFileName(selectedResults.Count == 1
                    ? selectedResults[0].Name : (titleBox.Text ?? "Analysis-report")) + "-analysis-report.pdf",
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
            if (previewPages.IsVisible && CurrentValidation().IsValid)
                SetStatus("Preview is out of date. Select Update Preview to refresh.", false, true);
            UpdateInterpretationStatus();
        }

        void ShowValidation(IReadOnlyList<AnalysisResult> results)
        {
            var validation = CurrentValidation();
            var valid = validation.IsValid;
            previewButton.IsEnabled = valid;
            exportButton.IsEnabled = valid;
            if (!valid) SetStatus(validation.Errors.FirstOrDefault() ?? "This result cannot be reported.", true);
            else if (results.Any(result => result.Health != AnalysisResultHealth.Valid))
                SetStatus("One or more selected results have warnings or may be stale. The report will identify them.", false, true);
            else SetStatus("");
        }

        void SetBusy(bool value, string? message)
        {
            busy = value;
            progress.IsVisible = value;
            selectResultsButton.IsEnabled = !value;
            labelBox.IsEnabled = !value;
            titleBox.IsEnabled = !value;
            energyCombo.IsEnabled = !value;
            temperatureCombo.IsEnabled = !value;
            uncertaintyCombo.IsEnabled = !value;
            injectionTablesCheck.IsEnabled = !value && selectedResults.Count > 0;
            condenseRepeatedCheck.IsEnabled = !value
                && AnalysisReportBuilder.HasRepeatedExperiments(selectedResults);
            advancedPanel.IsEnabled = !value;
            workspaceSelector.IsEnabled = !value;
            interpretationBox.IsEnabled = !value && selectedResults.Count > 0;
            editInterpretationButton.IsEnabled = !value && selectedResults.Count > 0;
            generateInterpretationButton.IsEnabled = !value && selectedResults.Count > 0;
            selectAllButton.IsEnabled = clearButton.IsEnabled = !value && advancedChecks.Count > 0;
            previewButton.IsEnabled = !value && CurrentValidation().IsValid;
            exportButton.IsEnabled = previewButton.IsEnabled;
            if (!string.IsNullOrWhiteSpace(message)) SetStatus(message);
        }

        AnalysisReportValidationResult CurrentValidation() => report == null
            ? AnalysisReportBuilder.Validate(selectedResults)
            : AnalysisReportBuilder.Validate(report,
                id => availableResults.FirstOrDefault(result => result.UniqueID == id),
                id => availableExperiments.FirstOrDefault(experiment => experiment.UniqueID == id));

        void UpdateInterpretationStatus()
        {
            var record = report?.ApprovedInterpretation;
            string summary;
            if (record == null) summary = "No interpretation";
            else if (record.Origin == AnalysisInterpretationOrigin.Manual) summary = "Manual";
            else
            {
                summary = record.UserEdited ? "AI-generated, edited" : "AI-generated";
                var freshness = AnalysisInterpretationService.EvaluateFreshness(report,
                    id => availableResults.FirstOrDefault(result => result.UniqueID == id)!,
                    id => availableExperiments.FirstOrDefault(experiment => experiment.UniqueID == id)!).Status;
                summary += freshness == AnalysisInterpretationFreshness.Current ? " • Current"
                    : freshness == AnalysisInterpretationFreshness.Stale ? " • Out of date" : " • Cannot verify";
            }
            interpretationSummaryText.Text = summary;
            interpretationWorkspaceStatus.Text = summary == "No interpretation"
                ? "Write remarks or a scientific interpretation to include in the report."
                : summary;
            interpretationBox.IsEnabled = !busy && selectedResults.Count > 0;
            editInterpretationButton.IsEnabled = !busy && selectedResults.Count > 0;
            generateInterpretationButton.IsEnabled = !busy && selectedResults.Count > 0;
        }

        void SetStatus(string message, bool error = false, bool warning = false)
        {
            statusText.Text = message ?? "";
            statusHost.IsVisible = !string.IsNullOrWhiteSpace(message);
            AppTheme.Bind(statusText, TextBlock.ForegroundProperty,
                error ? AppTheme.StatusError : warning ? AppTheme.StatusWarning : AppTheme.SecondaryText);
            AppTheme.Bind(statusHost, Border.BackgroundProperty, AppTheme.PanelBackground);
            AppTheme.Bind(statusHost, Border.BorderBrushProperty,
                error ? AppTheme.StatusError : warning ? AppTheme.StatusWarning : AppTheme.PanelBorder);
        }

        void ClearPreview()
        {
            previewPages.ItemsSource = null;
            foreach (var view in pageViews) view.Dispose();
            pageViews.Clear();
            previewPages.IsVisible = false;
            previewPlaceholder.IsVisible = true;
        }

        AnalysisResult? SelectedResult => selectedResults.Count == 1 ? selectedResults[0] : null;

        static string ResultKey(IEnumerable<AnalysisResult> results) => string.Join("|",
            results.Select(result => string.IsNullOrWhiteSpace(result.UniqueID)
                ? result.Name + ":" + result.Date.Ticks : result.UniqueID));

        internal static string ResultSummary(IEnumerable<AnalysisResult> selection)
        {
            var selected = (selection ?? Enumerable.Empty<AnalysisResult>())
                .Where(result => result != null)
                .ToList();
            if (selected.Count == 0) return "No result selected.";
            if (selected.Count > 1)
            {
                var experiments = AnalysisReportBuilder.CountDistinctExperiments(selected);
                return selected.Count + " results selected • " + experiments + " distinct experiments";
            }
            var result = selected[0];
            var model = result.Model?.ModelType.GetProperties().Name ?? "Unknown model";
            var count = result.Solution?.Solutions?.Count ?? 0;
            return model + " • " + count + " experiment" + (count == 1 ? "" : "s")
                + " • " + result.Health;
        }

        internal static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars().ToHashSet();
            var cleaned = new string((value ?? "analysis")
                .Select(character => invalid.Contains(character) || character == '/' || character == '\\' ? '-' : character)
                .ToArray()).Trim(' ', '.', '-');
            return string.IsNullOrWhiteSpace(cleaned) ? "analysis" : cleaned;
        }
    }

    sealed class AnalysisInterpretationDialog : Window
    {
        readonly AnalysisReport report;
        readonly Func<string, AnalysisResult> resultResolver;
        readonly Func<string, ExperimentData> experimentResolver;
        readonly HttpClient httpClient;
        readonly Action ensureRegistered;
        readonly CheckBox includeThermograms = new CheckBox { Content = "Include compressed thermograms" };
        readonly CheckBox includeInjectionTables = new CheckBox { Content = "Include injection tables" };
        readonly CheckBox includeProcessingInformation = new CheckBox { Content = "Include processing information" };
        readonly StackPanel thermogramOptions = new StackPanel { Spacing = 4 };
        readonly TextBlock dataInclusionLabel = Heading("Data included");
        readonly bool thermogramsAvailable;
        readonly TextBox questionBox = ContextBox();
        readonly TextBox contextBox = ContextBox(120);
        readonly TextBox draftBox = ContextBox(180);
        readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        readonly TextBlock serviceStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        readonly TextBlock packageSize = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        readonly Button retryServiceStatus = WorkspaceControlBuilder.Button("Retry", 72);
        readonly TextBlock interpretationAccountSummary = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        readonly TextBlock interpretationSetting = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        readonly TextBlock interpretationOptionDescription = Hint("");
        readonly TextBlock generationLabel = Heading("Generation");
        readonly TextBlock generationSettingLabel = Heading("Generation setting");
        readonly ComboBox interpretationPresetCombo = Combo(170);
        readonly ComboBox interpretationModelCombo = Combo(170);
        readonly ComboBox interpretationReasoningCombo = Combo(170);
        readonly StackPanel interpretationSelectionControls = new StackPanel { Spacing = 4 };
        readonly StackPanel interpretationPresetSelectionRow;
        readonly StackPanel interpretationModelSelectionRow;
        readonly StackPanel interpretationReasoningSelectionRow;
        readonly ProgressBar progress = new ProgressBar { IsIndeterminate = true, IsVisible = false, Height = 3 };
        readonly Button savePackage = WorkspaceControlBuilder.Button("Save AI package…", 142);
        readonly Button generate = WorkspaceControlBuilder.Button("Generate", 92);
        readonly Button use = WorkspaceControlBuilder.Button("Use in report", 112);
        readonly Button cancel = WorkspaceControlBuilder.Button("Cancel", 78);
        CancellationTokenSource? cancellation;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        AnalysisInterpretationRecord? generatedRecord;
        InterpretationOperatorOptionsResponse? interpretationOptions;
        bool interpretationSelectionEnabled;
        bool serviceAllowsGeneration = true;

        public AnalysisInterpretationDialog(AnalysisReport report, AnalysisResult result, HttpClient httpClient, Action ensureRegistered)
            : this(report, id => result?.UniqueID == id ? result : null!, _ => null!, httpClient, ensureRegistered) { }

        public AnalysisInterpretationDialog(AnalysisReport report, Func<string, AnalysisResult> resultResolver,
            Func<string, ExperimentData> experimentResolver, HttpClient httpClient, Action ensureRegistered)
        {
            this.report = report;
            this.resultResolver = resultResolver;
            this.experimentResolver = experimentResolver;
            this.httpClient = httpClient;
            this.ensureRegistered = ensureRegistered;
            thermogramsAvailable = InterpretationAccessDisplay.CanIncludeThermograms();
            includeInjectionTables.IsVisible = !string.IsNullOrWhiteSpace(AppSettings.InterpretationOperatorCode);
            includeProcessingInformation.IsVisible = thermogramsAvailable;
            includeInjectionTables.IsChecked = report.InterpretationSettings.InjectionRows != AnalysisInterpretationInjectionRows.None;
            includeProcessingInformation.IsChecked = report.InterpretationSettings.IncludeProcessingInformation;
            includeThermograms.IsVisible = thermogramsAvailable;
            includeThermograms.IsChecked = thermogramsAvailable && report.InterpretationSettings.IncludeThermograms;
            thermogramOptions.Children.Add(includeThermograms);
            thermogramOptions.Children.Insert(0, includeInjectionTables);
            thermogramOptions.Children.Insert(1, includeProcessingInformation);
            thermogramOptions.IsVisible = thermogramsAvailable;
            dataInclusionLabel.IsVisible = thermogramsAvailable;
            interpretationPresetSelectionRow = SelectionRow("AI interpretation detail level", interpretationPresetCombo, 240);
            interpretationModelSelectionRow = SelectionRow("Model", interpretationModelCombo);
            interpretationReasoningSelectionRow = SelectionRow("Reasoning", interpretationReasoningCombo);
            interpretationSelectionControls.Children.Add(interpretationPresetSelectionRow);
            interpretationSelectionControls.Children.Add(interpretationModelSelectionRow);
            interpretationSelectionControls.Children.Add(interpretationReasoningSelectionRow);
            PopulateInterpretationChoices();
            retryServiceStatus.IsVisible = false;
            Opened += async (_, _) => { await RefreshInterpretationAccountAsync(); await RefreshServiceStatusAsync(); };
            Title = "Generate Interpretation";
            Width = 660; Height = 660; MinWidth = 580; MinHeight = 580;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var context = report.StudyContext;
            questionBox.Text = context.ScientificQuestion;
            contextBox.Text = string.Join("\n\n", new[] { context.SystemDescription, context.AdditionalNotes }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            questionBox.TextChanged += (_, _) => UpdatePackageSize();
            contextBox.TextChanged += (_, _) => UpdatePackageSize();
            includeInjectionTables.IsCheckedChanged += (_, _) => UpdatePackageSize();
            includeProcessingInformation.IsCheckedChanged += (_, _) => UpdatePackageSize();
            includeThermograms.IsCheckedChanged += (_, _) => UpdatePackageSize();
            draftBox.IsVisible = false;
            use.IsVisible = false;
            use.Click += async (_, _) =>
            {
                if (generatedRecord == null) return;
                try
                {
                    var original = generatedRecord.InterpretationMarkdown;
                    var normalized = AnalysisInterpretationResponseParser.ParseForApproval(draftBox.Text ?? "");
                    var candidate = generatedRecord.Copy();
                    candidate.InterpretationMarkdown = normalized;
                    candidate.UserEdited = !string.Equals(original.Trim(), normalized.Trim(), StringComparison.Ordinal);
                    candidate.ApprovedAtUtc = DateTime.UtcNow;
                    if (report.ApprovedInterpretation != null && !await ConfirmReplacementAsync()) return;
                    Close(candidate);
                }
                catch (AnalysisInterpretationValidationException ex) { SetError(ex.Errors.FirstOrDefault() ?? ex.Message); }
            };
            savePackage.Click += async (_, _) => await SavePackageAsync();
            AutomationProperties.SetName(savePackage, "Save AI package locally without generation");
            generate.Click += async (_, _) => await GenerateAsync(httpClient);
            interpretationPresetCombo.SelectionChanged += (_, _) => UpdateInterpretationSetting();
            interpretationModelCombo.SelectionChanged += (_, _) =>
            {
                PopulateReasoningChoices();
                UpdateInterpretationSetting();
            };
            interpretationReasoningCombo.SelectionChanged += (_, _) => UpdateInterpretationSetting();
            retryServiceStatus.Click += async (_, _) => await RefreshServiceStatusAsync();
            AutomationProperties.SetName(retryServiceStatus, "Retry interpretation service availability check");
            cancel.Click += (_, _) => { if (cancellation != null) cancellation.Cancel(); else Close(null); };
            AutomationProperties.SetName(includeThermograms, "Include compressed thermograms");
            AutomationProperties.SetName(questionBox, "Main question");
            AutomationProperties.SetName(contextBox, "Additional context");
            AutomationProperties.SetName(draftBox, "Generated interpretation draft");
            AutomationProperties.SetName(interpretationSetting, "Interpretation account status");
            AutomationProperties.SetName(interpretationAccountSummary, "Interpretation account");
            ToolTip.SetTip(packageSize, "UTF-8 size of the compact scientific model package. Includes context and selected evidence, but excludes output instructions and request-envelope overhead; measured before transport fallbacks.");
            AutomationProperties.SetName(interpretationOptionDescription, "Selected generation option description");
            AppTheme.Bind(interpretationAccountSummary, TextBlock.ForegroundProperty, AppTheme.MutedText);
            AutomationProperties.SetName(use, "Use generated interpretation in report");
            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { savePackage, cancel, generate, use }
            };
            var footer = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 12),
                Child = actionRow,
            };
            AppTheme.Bind(footer, Border.BackgroundProperty, AppTheme.PanelBackground);
            AppTheme.Bind(footer, Border.BorderBrushProperty, AppTheme.PanelBorder);
            DockPanel.SetDock(footer, Dock.Bottom);
            Content = new DockPanel
            {
                Children =
                {
                    footer,
                    new ScrollViewer
                    {
                        Content = new StackPanel
                        {
                            Margin = new Thickness(20), Spacing = 10,
                            Children =
                            {
                                Heading("Main question"), questionBox,
                                Heading("Additional context"), Hint("Describe the system, cell and syringe contents, expected outcomes, controls, limitations, or caveats."), contextBox,
                                dataInclusionLabel, thermogramOptions,
                                generationLabel,
                                new StackPanel
                                {
                                    Spacing = 2,
                                    Children =
                                    {
                                        generationSettingLabel,
                                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { serviceStatus, retryServiceStatus } },
                                        packageSize,
                                        interpretationAccountSummary,
                                        interpretationSelectionControls,
                                        interpretationSetting,
                                        interpretationOptionDescription,
                                    }
                                },
                                progress, status, draftBox,
                            }
                        }
                    }
                }
            };
        }

        async Task<bool> ConfirmReplacementAsync()
        {
            var confirm = new Window
            {
                Title = "Replace interpretation?", Width = 430, Height = 170,
                CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var replace = WorkspaceControlBuilder.Button("Replace", 86);
            var keep = WorkspaceControlBuilder.Button("Keep editing", 98);
            replace.Click += (_, _) => confirm.Close(true);
            keep.Click += (_, _) => confirm.Close(false);
            confirm.Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "The report already contains an interpretation. Replace it with this draft?", TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { keep, replace } },
                }
            };
            return await confirm.ShowDialog<bool>(this);
        }

        async Task<bool> ConfirmWarningsAsync(IReadOnlyList<string> warnings)
        {
            var window = new Window { Title = "Review analysis warnings", Width = 540, Height = 330,
                WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var proceed = WorkspaceControlBuilder.Button("Generate anyway", 140);
            var back = WorkspaceControlBuilder.Button("Cancel", 78);
            proceed.Click += (_, _) => window.Close(true);
            back.Click += (_, _) => window.Close(false);
            window.Content = new DockPanel { Margin = new Thickness(20), Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right, [DockPanel.DockProperty] = Dock.Bottom,
                    Children = { back, proceed } },
                new ScrollViewer { Content = new TextBlock { TextWrapping = TextWrapping.Wrap,
                    Text = string.Join("\n\n", warnings) + "\n\nThe interpretation will distinguish historical fit evidence from current observations." } },
            } };
            return await window.ShowDialog<bool>(this);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            cancellation?.Cancel();
            lifetime.Cancel();
            base.OnClosing(e);
        }

        void SaveInputs()
        {
            var settings = report.InterpretationSettings;
            settings.IncludeThermograms = thermogramsAvailable && includeThermograms.IsChecked == true;
            settings.InjectionRows = includeInjectionTables.IsVisible && includeInjectionTables.IsChecked == true
                ? AnalysisInterpretationInjectionRows.All : AnalysisInterpretationInjectionRows.None;
            settings.IncludeProcessingInformation = includeProcessingInformation.IsVisible && includeProcessingInformation.IsChecked == true;
            report.UpdateInterpretationSettings(settings);
            var context = report.StudyContext;
            context.ScientificQuestion = questionBox.Text ?? "";
            context.SystemDescription = "";
            context.AdditionalNotes = contextBox.Text ?? "";
            report.UpdateStudyContext(context);
            ensureRegistered();
        }

        void PopulateInterpretationChoices()
        {
            InterpretationOperatorOptionsResponse? options = null;
            if (!string.IsNullOrWhiteSpace(AppSettings.InterpretationOperatorCode)
                && AppSettings.TryGetInterpretationAccessOptions(AppSettings.InterpretationOperatorCode, out var cached))
                options = cached;
            PopulateInterpretationChoices(options);
        }

        void PopulateInterpretationChoices(InterpretationOperatorOptionsResponse? options)
        {
            var selectedPreset = (interpretationPresetCombo.SelectedItem as InterpretationPresetOption)?.Id;
            var selectedModel = interpretationModelCombo.SelectedItem as string;
            var selectedReasoning = interpretationReasoningCombo.SelectedItem as string;
            interpretationOptions = options;
            UpdateInterpretationAccountSummary();
            var canTables = InterpretationAccessDisplay.CanIncludeInjectionTables(options);
            var canProcessing = InterpretationAccessDisplay.CanIncludeProcessingInformation(options);
            includeInjectionTables.IsVisible = canTables;
            includeProcessingInformation.IsVisible = canProcessing;
            includeInjectionTables.IsEnabled = canTables;
            includeProcessingInformation.IsEnabled = canProcessing;
            if (!canTables) includeInjectionTables.IsChecked = false;
            if (!canProcessing) includeProcessingInformation.IsChecked = false;

            if (interpretationOptions?.Mode == "custom")
            {
                interpretationPresetSelectionRow.IsVisible = false;
                interpretationModelSelectionRow.IsVisible = true;
                interpretationReasoningSelectionRow.IsVisible = true;
                interpretationModelCombo.ItemsSource = interpretationOptions.Models.Select(model => model.Id).ToList();
                var model = !string.IsNullOrWhiteSpace(selectedModel)
                    ? selectedModel
                    : string.IsNullOrWhiteSpace(AppSettings.InterpretationEvaluationModel)
                    ? interpretationOptions.DefaultModel
                    : AppSettings.InterpretationEvaluationModel;
                interpretationModelCombo.SelectedItem = interpretationModelCombo.Items.Cast<string>().FirstOrDefault(value => value == model)
                    ?? interpretationModelCombo.Items.Cast<string>().FirstOrDefault();
                PopulateReasoningChoices(selectedReasoning);
                interpretationSelectionEnabled = interpretationOptions != null;
                interpretationModelCombo.IsEnabled = interpretationSelectionEnabled;
                interpretationReasoningCombo.IsEnabled = interpretationSelectionEnabled;
                SetAccessibilityName(interpretationModelCombo, "Interpretation model");
                SetAccessibilityName(interpretationReasoningCombo, "Interpretation reasoning effort");
            }
            else
            {
                interpretationPresetSelectionRow.IsVisible = true;
                interpretationModelSelectionRow.IsVisible = false;
                interpretationReasoningSelectionRow.IsVisible = false;
                var presets = interpretationOptions?.Presets?.Count > 0
                    ? interpretationOptions.Presets
                    : new List<InterpretationPresetOption> { new InterpretationPresetOption { Id = "instant", Name = "Default" } };
                interpretationPresetCombo.ItemsSource = presets;
                var selectedId = !string.IsNullOrWhiteSpace(selectedPreset)
                    ? selectedPreset
                    : string.IsNullOrWhiteSpace(AppSettings.InterpretationGenerationPreset)
                    ? "instant" : AppSettings.InterpretationGenerationPreset;
                interpretationPresetCombo.SelectedItem = presets.FirstOrDefault(preset => preset.Id == selectedId)
                    ?? presets.FirstOrDefault();
                interpretationPresetCombo.IsEnabled = interpretationOptions != null;
                interpretationSelectionEnabled = interpretationOptions != null;
                SetAccessibilityName(interpretationPresetCombo, "Interpretation preset");
            }
            generationSettingLabel.IsVisible = interpretationOptions?.Mode == "custom";
            UpdateInterpretationSetting();
        }

        void UpdateInterpretationAccountSummary()
        {
            InterpretationAccountResponse? account = null;
            if (!string.IsNullOrWhiteSpace(AppSettings.InterpretationOperatorCode)
                && AppSettings.TryGetInterpretationAccount(AppSettings.InterpretationOperatorCode, out var cached, out _))
                account = cached;
            interpretationAccountSummary.Text = account == null && interpretationOptions == null
                ? "Account: Not available · Tier: Not available · Usage left: Not available"
                : InterpretationAccessDisplay.AccountSummary(account, interpretationOptions);
        }

        void PopulateReasoningChoices(string? preferred = null)
        {
            if (interpretationOptions?.Mode != "custom") return;
            var modelId = interpretationModelCombo.SelectedItem as string;
            var model = interpretationOptions.Models.FirstOrDefault(item => item.Id == modelId);
            var choices = model?.ReasoningEfforts ?? new List<string>();
            interpretationReasoningCombo.ItemsSource = choices;
            var selected = !string.IsNullOrWhiteSpace(preferred)
                ? preferred
                : string.IsNullOrWhiteSpace(AppSettings.InterpretationEvaluationReasoningEffort)
                ? interpretationOptions.DefaultReasoningEffort
                : AppSettings.InterpretationEvaluationReasoningEffort;
            interpretationReasoningCombo.SelectedItem = choices.FirstOrDefault(value => value == selected)
                ?? choices.FirstOrDefault();
            interpretationReasoningCombo.IsEnabled = model?.SelectionType != "summary";
        }

        AnalysisInterpretationGenerationSelection? CurrentGenerationSelection()
        {
            if (!interpretationSelectionEnabled) return null;
            if (interpretationOptions?.Mode == "custom")
            {
                var model = interpretationModelCombo.SelectedItem as string;
                if (model == "summary")
                    return new AnalysisInterpretationGenerationSelection { TaskType = "summary", PresetId = "summary" };
                return new AnalysisInterpretationGenerationSelection
                {
                    Model = model,
                    ReasoningEffort = interpretationReasoningCombo.SelectedItem as string,
                };
            }
            var preset = interpretationPresetCombo.SelectedItem as InterpretationPresetOption;
            return new AnalysisInterpretationGenerationSelection
                { TaskType = preset?.TaskType ?? "interpretation", PresetId = preset?.Id ?? "instant" };
        }

        void UpdateInterpretationSetting()
        {
            UpdateInterpretationOptionDescription();
            if (interpretationOptions?.Mode == "custom")
            {
                var model = interpretationModelCombo.SelectedItem as string;
                var reasoning = interpretationReasoningCombo.SelectedItem as string;
                if (model == "summary") { interpretationSetting.Text = "Selected interpretation: Summary"; return; }
                interpretationSetting.Text = string.IsNullOrWhiteSpace(model)
                    ? InterpretationAccessDisplay.CurrentSetting()
                    : $"Selected interpretation: {model} model · {reasoning ?? "reasoning unavailable"} reasoning";
                return;
            }
            var preset = interpretationPresetCombo.SelectedItem as InterpretationPresetOption;
            interpretationSetting.Text = "Selected preset: " + (preset?.Name ?? "Default");
        }

        void UpdateInterpretationOptionDescription()
        {
            var preset = interpretationPresetCombo.SelectedItem as InterpretationPresetOption;
            var model = interpretationModelCombo.SelectedItem as string;
            var description = InterpretationAccessDisplay.GenerationOptionDescription(
                interpretationOptions, preset?.Id, model);
            interpretationOptionDescription.Text = description ?? "";
            interpretationOptionDescription.IsVisible = !string.IsNullOrWhiteSpace(description);
        }

        async Task RefreshInterpretationAccountAsync()
        {
            try
            {
                var client = new FtItcInterpretationClient(httpClient, new Uri("https://app.ft-itc.org"));
                var options = await client.GetInterpretationOptionsAsync(
                    AppSettings.InterpretationOperatorCode ?? "", lifetime.Token);
                if (!lifetime.IsCancellationRequested) PopulateInterpretationChoices(options);
            }
            catch (OperationCanceledException) { }
            catch (AnalysisInterpretationProviderException) { }
            catch (HttpRequestException) { }
        }

        internal static string FormatInterpretationAccount(
            InterpretationOperatorOptionsResponse? options,
            InterpretationPresetOption? preset)
        {
            if (options == null) return "Account information unavailable.";
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(options.AccessDetails?.Name))
                parts.Add("Registered as " + options.AccessDetails.Name);
            else if (string.Equals(options.AccessTier, "public", StringComparison.OrdinalIgnoreCase))
                parts.Add("Public interpretation access");
            else if (!string.IsNullOrWhiteSpace(options.AccessTierName))
                parts.Add(options.AccessTierName + " access");
            else
                parts.Add("Interpretation access active");

            if (options.AccessDetails?.ExpiresAtUtc is DateTime expiry)
                parts.Add("expires " + expiry.ToLocalTime().ToString("yyyy-MM-dd"));
            if (preset?.Quota?.Limited == true)
            {
                parts.Add(preset.Quota.RemainingPercent + "% usage remaining");
                if (preset.Quota.ResetsAtUtc != default)
                    parts.Add("resets " + preset.Quota.ResetsAtUtc.ToLocalTime().ToString("yyyy-MM-dd"));
            }
            return string.Join(" · ", parts) + ".";
        }

        static StackPanel SelectionRow(string label, Control control, double minLabelWidth = 128) => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = label, MinWidth = minLabelWidth, VerticalAlignment = VerticalAlignment.Center },
                control,
            }
        };

        static void SetAccessibilityName(Control control, string name) => AutomationProperties.SetName(control, name);

        void UpdatePackageSize()
        {
            packageSize.Text = "Scientific package: Calculating…";
            try
            {
                var oldSettings = report.InterpretationSettings;
                var oldContext = report.StudyContext;
                SaveInputs();
                var package = AnalysisInterpretationPackageBuilder.Build(report, resultResolver, experimentResolver, report.InterpretationSettings);
                var bytes = System.Text.Encoding.UTF8.GetByteCount(AnalysisInterpretationModelInputWriter.Write(package));
                packageSize.Text = $"Scientific package: {bytes / 1024.0:0.0} KiB";
                report.UpdateInterpretationSettings(oldSettings); report.UpdateStudyContext(oldContext);
            }
            catch { packageSize.Text = "Scientific package: Size unavailable"; }
        }

        async Task SavePackageAsync()
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save AI package",
                SuggestedFileName = "ftitc-ai-package.zip",
                FileTypeChoices = new[] { new FilePickerFileType("AI package archive") { Patterns = new[] { "*.zip" } } },
            });
            if (file == null) return;
            SetBusy(true);
            SetStatus("Building local AI package…");
            try
            {
                SaveInputs();
                await Task.Yield();
                var package = AnalysisInterpretationPackageBuilder.Build(report, resultResolver, experimentResolver, report.InterpretationSettings);
                var bytes = AnalysisInterpretationDebugExport.CreateArchive(package);
                await using var stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                await stream.WriteAsync(bytes);
                SetStatus("AI package saved locally. Nothing was sent to the server.");
            }
            catch (Exception ex) { SetError("Could not save AI package. " + ex.Message); }
            finally { SetBusy(false); }
        }

        async Task GenerateAsync(HttpClient httpClient)
        {
            if (cancellation != null) return;
            if (!serviceAllowsGeneration) { SetError(serviceStatus.Text ?? "Interpretation generation is unavailable."); return; }
            var warnings = AnalysisInterpretationService.GetGenerationWarnings(report, resultResolver, experimentResolver);
            if (warnings.Count > 0 && !await ConfirmWarningsAsync(warnings)) return;
            SaveInputs();
            var generationCancellation = new CancellationTokenSource();
            cancellation = generationCancellation;
            var generationToken = generationCancellation.Token;
            SetBusy(true);
            SetStatus("Building the analysis package…");
            await Task.Yield();
            try
            {
                var provider = new FtItcInterpretationClient(httpClient, new Uri("https://app.ft-itc.org"));
                var generationProgress = new Progress<AnalysisInterpretationProgressUpdate>(update =>
                {
                    if (ReferenceEquals(cancellation, generationCancellation) && !generationToken.IsCancellationRequested)
                        SetStatus(update.Message);
                });
                var generated = await new AnalysisInterpretationService(provider).GenerateAsync(
                    report, resultResolver, experimentResolver, report.InterpretationSettings, generationToken, generationProgress,
                    CurrentGenerationSelection());
                generationToken.ThrowIfCancellationRequested();
                generatedRecord = generated.Interpretation;
                draftBox.Text = generatedRecord.InterpretationMarkdown;
                draftBox.IsVisible = true;
                use.IsVisible = true;
                Height = Math.Max(Height, 720);
                SetStatus("Finished — interpretation ready. Review the draft before adding it to the report.");
            }
            catch (AnalysisInterpretationProviderException ex) when (ex.Kind == AnalysisInterpretationFailureKind.Cancelled)
            { SetStatus("Finished — generation cancelled."); }
            catch (OperationCanceledException) { SetStatus("Finished — generation cancelled."); }
            catch (Exception ex) { SetError("Finished — generation failed. " + ex.Message); }
            finally
            {
                generationCancellation.Dispose();
                if (ReferenceEquals(cancellation, generationCancellation)) cancellation = null;
                SetBusy(false);
            }
        }

        void SetBusy(bool value)
        {
            progress.IsVisible = value;
            var selectionEnabled = !value && interpretationSelectionEnabled;
            questionBox.IsEnabled = contextBox.IsEnabled = includeThermograms.IsEnabled = savePackage.IsEnabled = use.IsEnabled = !value;
            generate.IsEnabled = !value && serviceAllowsGeneration;
            interpretationPresetCombo.IsEnabled = selectionEnabled;
            interpretationModelCombo.IsEnabled = selectionEnabled;
            interpretationReasoningCombo.IsEnabled = selectionEnabled
                && interpretationModelCombo.SelectedItem as string != "summary";
            cancel.Content = value ? "Cancel generation" : "Cancel";
        }

        async Task RefreshServiceStatusAsync()
        {
            retryServiceStatus.IsEnabled = false;
            retryServiceStatus.IsVisible = false;
            try
            {
                var client = new FtItcInterpretationClient(httpClient, new Uri("https://app.ft-itc.org"));
                var result = await client.GetInterpretationStatusAsync(lifetime.Token);
                serviceAllowsGeneration = result.Status == "available";
                serviceStatus.Text = result.Status == "available" ? "Service: Available" : "Service: " + (result.Message ?? (result.Status == "retired" ? "Retired" : "Temporarily unavailable"));
                AppTheme.Bind(serviceStatus, TextBlock.ForegroundProperty, serviceAllowsGeneration ? AppTheme.MutedText : AppTheme.StatusWarning);
                retryServiceStatus.IsVisible = !serviceAllowsGeneration;
                generate.IsEnabled = serviceAllowsGeneration && cancellation == null;
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                serviceAllowsGeneration = true;
                serviceStatus.Text = "Service availability could not be verified. You may try generation manually.";
                AppTheme.Bind(serviceStatus, TextBlock.ForegroundProperty, AppTheme.StatusWarning);
                retryServiceStatus.IsVisible = true;
            }
            finally { retryServiceStatus.IsEnabled = true; }
        }

        void SetError(string message)
        {
            status.Text = message;
            AppTheme.Bind(status, TextBlock.ForegroundProperty, AppTheme.StatusError);
        }

        void SetStatus(string message)
        {
            status.Text = message ?? "";
            AppTheme.Bind(status, TextBlock.ForegroundProperty, AppTheme.MutedText);
        }

        static TextBox ContextBox(double minHeight = 72) => new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = minHeight,
            VerticalContentAlignment = VerticalAlignment.Top,
            [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled,
            [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
        };
        static TextBlock Heading(string value) => new TextBlock { Text = value, FontWeight = FontWeight.SemiBold };
        static TextBlock Hint(string value)
        {
            var text = new TextBlock { Text = value, FontSize = 11, TextWrapping = TextWrapping.Wrap };
            AppTheme.Bind(text, TextBlock.ForegroundProperty, AppTheme.MutedText);
            return text;
        }
    }

    sealed class AnalysisReportPreviewPage : Border, IDisposable
    {
        const double PageWidth = 595;
        const double PageHeight = 842;
        readonly SkiaAnalysisReportRenderer renderer;
        readonly AnalysisReportDocument document;
        readonly AnalysisReportLayoutPlan plan;
        readonly int pageIndex;
        readonly Image image = new Image { Stretch = Stretch.Uniform };
        readonly TextBlock placeholder = new TextBlock { Text = "Rendering page...", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        Bitmap? bitmap;
        int requestedPixelWidth = PixelWidthForZoom(1.0);
        int renderedPixelWidth;
        int renderGeneration;
        bool attached;

        public AnalysisReportPreviewPage(SkiaAnalysisReportRenderer renderer,
            AnalysisReportDocument document, AnalysisReportLayoutPlan plan, int pageIndex)
        {
            this.renderer = renderer;
            this.document = document;
            this.plan = plan;
            this.pageIndex = pageIndex;
            Width = PageWidth;
            Height = PageHeight;
            Margin = new Thickness(0, 7);
            HorizontalAlignment = HorizontalAlignment.Center;
            Background = Brushes.White;
            AppTheme.Bind(this, Border.BorderBrushProperty, AppTheme.PanelBorder);
            BorderThickness = new Thickness(1);
            RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
            Child = placeholder;
        }

        public void SetZoom(double zoom)
        {
            Width = PageWidth * zoom;
            Height = PageHeight * zoom;
            var pixelWidth = PixelWidthForZoom(zoom);
            if (requestedPixelWidth == pixelWidth) return;
            requestedPixelWidth = pixelWidth;
            if (attached) StartRender();
        }

        internal static int PixelWidthForZoom(double zoom) =>
            Math.Max(900, Math.Min(2700, (int)Math.Round(1800 * Math.Max(0.5, zoom))));

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            attached = true;
            if (bitmap == null || renderedPixelWidth != requestedPixelWidth) StartRender();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            attached = false;
            renderGeneration++;
            DisposeBitmap();
            base.OnDetachedFromVisualTree(e);
        }

        void StartRender()
        {
            var generation = ++renderGeneration;
            var pixelWidth = requestedPixelWidth;
            _ = RenderAsync(pixelWidth, generation, cancellation.Token);
        }

        async Task RenderAsync(int pixelWidth, int generation, CancellationToken token)
        {
            try
            {
                var bytes = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using var rendered = renderer.RenderPageBitmap(document, plan, pageIndex, pixelWidth);
                    using var skImage = SKImage.FromBitmap(rendered);
                    using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 95);
                    return encoded.ToArray();
                }, token);
                if (token.IsCancellationRequested || generation != renderGeneration || !attached) return;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || generation != renderGeneration || !attached) return;
                    using var stream = new MemoryStream(bytes);
                    var replacement = new Bitmap(stream);
                    var previous = bitmap;
                    bitmap = replacement;
                    renderedPixelWidth = pixelWidth;
                    image.Source = replacement;
                    Child = image;
                    previous?.Dispose();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (generation == renderGeneration && attached)
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
