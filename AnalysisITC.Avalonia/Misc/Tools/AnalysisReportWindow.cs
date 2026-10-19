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
using Shape = Avalonia.Controls.Shapes.Shape;
using Avalonia.Controls.Templates;
using Avalonia.Automation;
using Avalonia.Data;
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
using AnalysisITC.Platform;
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
        static readonly double[] PreviewZoomLevels = Enumerable.Range(5, 16)
            .Select(step => step / 10.0).ToArray();
        static readonly HttpClient InterpretationHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        static readonly Uri InterpretationBaseUri = new Uri("https://app.ft-itc.org");

        readonly SkiaAnalysisReportRenderer renderer = new SkiaAnalysisReportRenderer();
        readonly Button savedReportsButton = Button(AnalysisReportEditingPresentation.SavedReportsLabel, 0);
        readonly Flyout savedReportsFlyout = new Flyout();
        readonly StackPanel savedReportsItems = new StackPanel { Spacing = 6 };
        readonly Button selectResultsButton = Button("Select results…", 0);
        readonly Flyout resultPickerFlyout = new Flyout();
        readonly StackPanel resultPickerItems = new StackPanel { Spacing = 4 };
        readonly TextBlock resultSummaryText = Text("No result selected.");
        readonly TextBox labelBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 96,
            VerticalContentAlignment = VerticalAlignment.Top,
            [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled,
            [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
        };
        readonly TextBox titleBox = TextBox();
        readonly TextBox reportIdBox = TextBox();
        Control reportIdRow = null!;
        readonly ComboBox energyCombo = Combo(new[] { "Joule", "Calories" });
        readonly ComboBox temperatureCombo = Combo(new[] { "Celsius", "Kelvin" });
        readonly ComboBox uncertaintyCombo = Combo(new[] { "Automatic", "Standard deviation (SD)", "95% CI", "SD + 95% CI", "None" });
        readonly ComboBox outputPurposeCombo = Combo(new[] { "Standard report", "Diagnostic report" });
        readonly StackPanel advancedPanel = new StackPanel { Spacing = 2 };
        readonly CheckBox injectionTablesCheck = Check("Injection tables");
        readonly CheckBox condenseRepeatedCheck = Check("Condense repeated experiments");
        readonly CheckBox expandedExplanationsCheck = Check("Expanded explanations");
#if DEBUG
        readonly CheckBox extraTraceabilityCheck = Check("Extra traceability");
#endif
        readonly TextBlock preparedByText = new TextBlock { TextWrapping = TextWrapping.Wrap };
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
        readonly Button editInterpretationButton = Button("Edit", 48);
        readonly Button generateInterpretationButton = Button("Generate interpretation…", 0);
        readonly Grid interpretationHost = new Grid();
        readonly Grid previewHost = new Grid();
        readonly ItemsControl previewPages = new ItemsControl { Focusable = false };
        readonly ComboBox previewZoomCombo = Combo(
            PreviewZoomLevels.Select(zoom => $"{zoom * 100:0}%").ToArray(),
            Array.IndexOf(PreviewZoomLevels, 1.0), 86);
        readonly Button previewZoomOutButton = PreviewZoomButton(false);
        readonly Button previewZoomInButton = PreviewZoomButton(true);
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
        readonly List<ITCDataContainer> observedSources = new();

        AnalysisReportDocument? currentDocument;
        AnalysisReportLayoutPlan? currentPlan;
        bool previewStale = true;
        bool changingResult;
        bool automaticTitle = true;
        bool changingWorkspace;
        bool busy;
        bool loadingInterpretation;
#if DEBUG
        bool loadingReportOptions;
#endif
        bool extraTraceabilityChoice;
        bool lastTraceabilityMode;
        bool initialPreviewStarted;
        bool previewPinchActive;
        double previewPinchStartZoom = 1.0;
        DateTime lastPreviewWheelEventUtc;
        int lastPreviewWheelDirection;
        DateTime lastPreviewMagnifyEventUtc;
        double previewMagnifyAccumulator;
        AnalysisReport? report;
        readonly Guid documentId = DocumentDirtyTracker.CurrentDocumentId;
        AnalysisReportEditSession? editSession;
        readonly Button saveReportButton = Button(AnalysisReportEditingPresentation.SaveLabel, 0);
        readonly Button saveReportAsButton = Button(AnalysisReportEditingPresentation.SaveAsLabel, 0);
        readonly TextBlock interpretationReviewText = new TextBlock { Text = AnalysisReportEditingPresentation.ReviewText, TextWrapping = TextWrapping.Wrap };
        readonly Button confirmInterpretationButton = Button(AnalysisReportEditingPresentation.ConfirmLabel, 0);
        bool allowingClose;
        Task<bool>? pendingLeave;
        internal Func<Task<int>>? LeavePromptOverride { get; set; }
        internal Func<Task>? SaveProjectCommand { get; set; }
        internal Func<Task>? SaveProjectAsCommand { get; set; }
        bool invalidated;
        bool closed;

        public AnalysisReportWindow(AnalysisResult? selectedResult = null, AnalysisReport? savedReport = null)
        {
            if (savedReport != null && (!DataManager.Reports.Contains(savedReport)
                || AnalysisReportPresentation.MissingResultCount(savedReport,
                    id => DataManager.Results.FirstOrDefault(result => result.UniqueID == id)) > 0))
                throw new ArgumentException("The saved report is unavailable in this project.", nameof(savedReport));
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
            lastTraceabilityMode = AppSettings.TraceabilityModeEnabled;
            injectionTablesCheck.IsChecked = sessionIncludeInjectionTables;
            condenseRepeatedCheck.IsChecked = sessionCondenseRepeatedExperiments;
            BuildLayout();
            AddHandler(KeyDownEvent, async (_, e) =>
            {
                var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
                if (e.Key != Key.S || (e.KeyModifiers & primary) == 0) return;
                e.Handled = true;
                var command = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? SaveProjectAsCommand : SaveProjectCommand;
                if (command != null) await command();
            }, RoutingStrategies.Tunnel);
            WireEvents();
            ApplyPreviewZoom();
            PopulateResults(selectedResult, savedReport);
            ObserveSourceChanges();
            AppSettings.SettingsDidUpdate += OnSettingsDidUpdate;
            DataManager.DataDidChange += OnProjectDataChanged;
            DataManager.ReportsDidChange += OnProjectReportsChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            closed = true;
            foreach (var source in observedSources)
            {
                source.ContentChanged -= OnSourceDataChanged;
                if (source is AnalysisResult result) result.BindingAssessmentChanged -= OnSourceDataChanged;
            }
            observedSources.Clear();
            AppSettings.SettingsDidUpdate -= OnSettingsDidUpdate;
            DataManager.DataDidChange -= OnProjectDataChanged;
            DataManager.ReportsDidChange -= OnProjectReportsChanged;
            ClearPreview();
            base.OnClosed(e);
        }

        bool CanWriteReport() => !closed && !invalidated
            && documentId == DocumentDirtyTracker.CurrentDocumentId
            && (editSession?.IsCurrent ?? true)
            && selectedResults.All(DataManager.Results.Contains);

        void OnProjectDataChanged(object? sender, ExperimentData e) => CheckProjectOwnership();
        void OnProjectReportsChanged(object? sender, EventArgs e) => CheckProjectOwnership();

        void CheckProjectOwnership()
        {
            void Check()
            {
                if (closed || invalidated) return;
                if (CanWriteReport()) return;
                invalidated = true;
                Close();
            }
            if (Dispatcher.UIThread.CheckAccess()) Check();
            else Dispatcher.UIThread.Post(Check);
        }

        void ObserveSourceChanges()
        {
            foreach (var source in observedSources)
            {
                source.ContentChanged -= OnSourceDataChanged;
                if (source is AnalysisResult result) result.BindingAssessmentChanged -= OnSourceDataChanged;
            }
            observedSources.Clear();
            observedSources.AddRange(selectedResults);
            observedSources.AddRange(selectedSupportingExperiments);
            observedSources.AddRange(selectedResults.SelectMany(result => result.Solution?.Solutions
                ?.Select(solution => solution?.Data).OfType<ExperimentData>() ?? Enumerable.Empty<ExperimentData>()));
            foreach (var source in observedSources.Distinct())
            {
                source.ContentChanged += OnSourceDataChanged;
                if (source is AnalysisResult result) result.BindingAssessmentChanged += OnSourceDataChanged;
            }
        }

        void OnSourceDataChanged(object? sender, EventArgs e)
        {
            void ApplyChange()
            {
                if (automaticTitle)
                {
                    changingResult = true;
                    titleBox.Text = AnalysisReportPresentation.AutomaticTitle(selectedResults);
                    changingResult = false;
                }
                MarkStale();
            }
            if (Dispatcher.UIThread.CheckAccess()) ApplyChange();
            else Dispatcher.UIThread.Post(ApplyChange);
        }

        void OnSettingsDidUpdate(object? sender, EventArgs e)
        {
            void ApplyUpdate()
            {
                var modeChanged = lastTraceabilityMode != AppSettings.TraceabilityModeEnabled;
                lastTraceabilityMode = AppSettings.TraceabilityModeEnabled;
                ApplyTraceabilityCheckboxState();
                RefreshPreparerDisplay();
                var preparerChanged = currentDocument != null
                    && AnalysisReportBuilder.NeedsPreparerRefresh(currentDocument);
                if (!modeChanged && !preparerChanged) return;

                previewStale = true;
                if (previewPages.IsVisible && CurrentValidation().IsValid)
                    SetStatus("Preview is out of date. Select Update Preview to refresh.", false, true);
                UpdateInterpretationStatus();
            }

            if (Dispatcher.UIThread.CheckAccess()) ApplyUpdate();
            else Dispatcher.UIThread.Post(ApplyUpdate);
        }

        void ApplyTraceabilityCheckboxState()
        {
#if DEBUG
            loadingReportOptions = true;
            extraTraceabilityCheck.IsChecked = AppSettings.TraceabilityModeEnabled || extraTraceabilityChoice;
            extraTraceabilityCheck.IsEnabled = !busy && !AppSettings.TraceabilityModeEnabled && selectedResults.Count > 0;
#else
            extraTraceabilityChoice = false;
#endif
            reportIdRow.IsVisible = AppSettings.TraceabilityModeEnabled || extraTraceabilityChoice;
            reportIdBox.IsEnabled = !busy && selectedResults.Count > 0;
#if DEBUG
            loadingReportOptions = false;
#endif
        }

        void RefreshPreparerDisplay() => preparedByText.Text = string.IsNullOrWhiteSpace(AppSettings.UserName)
            ? "Not recorded"
            : AppSettings.UserName.Trim();

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (initialPreviewStarted) return;
            initialPreviewStarted = true;
            await PreviewAsync();
        }

        void BuildLayout()
        {
            selectResultsButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            selectResultsButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            selectResultsButton.Height = 42;
            selectResultsButton.MinHeight = 42;
            BuildResultPicker();
            BuildSavedReportsPicker();
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
            previewZoomCombo.Height = 32;
            previewZoomCombo.MinHeight = 32;
            foreach (var button in new[] { previewZoomOutButton, previewZoomInButton })
            {
                button.Bind(TemplatedControl.BorderBrushProperty,
                    previewZoomCombo.GetObservable(TemplatedControl.BorderBrushProperty));
                button.Bind(TemplatedControl.BorderThicknessProperty,
                    previewZoomCombo.GetObservable(TemplatedControl.BorderThicknessProperty));
                button.Bind(TemplatedControl.CornerRadiusProperty,
                    previewZoomCombo.GetObservable(TemplatedControl.CornerRadiusProperty));
            }
            var previewZoomToolbar = new Border
            {
                Padding = new Thickness(10, 6),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = Row(Text("Zoom"), previewZoomCombo, previewZoomOutButton, previewZoomInButton),
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
            var formattingHint = new TextBlock
            {
                Text = "Formatting: ## Heading · ### Subheading · **bold** · *italic* · - bullet",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            };
            AppTheme.Bind(formattingHint, TextBlock.ForegroundProperty, AppTheme.MutedText);
            AutomationProperties.SetName(formattingHint, "Supported Markdown formatting");
            var interpretationHeading = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock { Text = "Report interpretation", FontSize = 18, FontWeight = FontWeight.SemiBold },
                    interpretationWorkspaceStatus,
                    formattingHint,
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
            previewHost.IsVisible = true;
            interpretationHost.IsVisible = false;
            workspaceSelector.SelectedIndex = 1;
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
            savedReportsButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            savedReportsButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            savedReportsButton.Height = 42;
            savedReportsButton.MinHeight = 42;
            ToolTip.SetTip(savedReportsButton, AnalysisReportEditingPresentation.SavedReportsTooltip);
            ToolTip.SetTip(saveReportButton, AnalysisReportEditingPresentation.SaveTooltip);
            ToolTip.SetTip(saveReportAsButton, AnalysisReportEditingPresentation.SaveAsTooltip);
            saveReportButton.Click += (_, _) => SaveReport(false);
            saveReportAsButton.Click += (_, _) => SaveReport(true);
            inspector.Children.Add(Section("Report", savedReportsButton,
                EqualWidthRow(saveReportButton, saveReportAsButton)));
            inspector.Children.Add(Section("Report contents", selectResultsButton, resultSummaryText));
            titleBox.PlaceholderText = "Report title";
            titleBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            labelBox.PlaceholderText = "Introduce the study and the purpose of this report. Appears below the title on the front page.";
            labelBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            reportIdBox.PlaceholderText = "Report ID";
            reportIdBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            reportIdRow = Labeled("Report ID", reportIdBox);
            inspector.Children.Add(Section("Document",
                titleBox, labelBox, reportIdRow));
            inspector.Children.Add(Section("Presentation",
                Labeled("Energy", energyCombo),
                Labeled("Temperature", temperatureCombo),
                Labeled("Uncertainties", uncertaintyCombo),
                Labeled("Output", outputPurposeCombo)));

            selectAllButton.Click += (_, _) => SetAllAdvanced(true);
            clearButton.Click += (_, _) => SetAllAdvanced(false);
            var actions = EqualWidthRow(selectAllButton, clearButton);
#if DEBUG
            inspector.Children.Add(Section("Optional content", injectionTablesCheck,
                condenseRepeatedCheck, expandedExplanationsCheck, extraTraceabilityCheck, advancedPanel, actions));
#else
            inspector.Children.Add(Section("Optional content", injectionTablesCheck,
                condenseRepeatedCheck, expandedExplanationsCheck, advancedPanel, actions));
#endif
            inspector.Children.Add(Section("Prepared by", preparedByText));
            interpretationSummaryText.FontSize = 11;
            interpretationSummaryText.TextWrapping = TextWrapping.Wrap;
            AppTheme.Bind(interpretationSummaryText, TextBlock.ForegroundProperty, AppTheme.MutedText);
            var interpretationActions = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = RowSpacing,
                Margin = ControlMargin,
            };
            generateInterpretationButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            interpretationActions.Children.Add(editInterpretationButton);
            Grid.SetColumn(generateInterpretationButton, 1);
            interpretationActions.Children.Add(generateInterpretationButton);
            AppTheme.Bind(interpretationReviewText, TextBlock.ForegroundProperty, AppTheme.StatusWarning);
            ToolTip.SetTip(interpretationReviewText, AnalysisReportEditingPresentation.ReviewTooltip);
            ToolTip.SetTip(confirmInterpretationButton, AnalysisReportEditingPresentation.ConfirmTooltip);
            confirmInterpretationButton.Click += (_, _) =>
            {
                if (!CommitInterpretationEditor() || editSession == null) return;
                editSession.ConfirmInterpretation();
                UpdateInterpretationStatus(); UpdateSaveState();
            };
            inspector.Children.Add(Section("Interpretation", interpretationSummaryText, interpretationActions,
                interpretationReviewText, confirmInterpretationButton));

            var cancel = Button("Close", 78);
            ToolTip.SetTip(cancel, AnalysisReportEditingPresentation.CloseTooltip);
            cancel.Click += (_, _) => Close(false);
            previewButton.Click += async (_, _) => await PreviewAsync();
            exportButton.Click += async (_, _) => await ExportAsync();
            var buttons = Row(cancel, previewButton, exportButton);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            statusHost.Child = statusText;
            var footer = InspectorFooter(Section("PDF export", progress, statusHost, buttons));

            AutomationProperties.SetName(savedReportsButton, "Open saved report");
            AutomationProperties.SetName(selectResultsButton, "Select report contents");
            AutomationProperties.SetName(resultSummaryText, "Selected report contents details");
            AutomationProperties.SetName(labelBox, "Report subtitle");
            AutomationProperties.SetName(reportIdBox, "Report ID");
            AutomationProperties.SetName(titleBox, "Report title");
            AutomationProperties.SetName(energyCombo, "Energy units");
            AutomationProperties.SetName(temperatureCombo, "Temperature units");
            AutomationProperties.SetName(uncertaintyCombo, "Uncertainties");
            AutomationProperties.SetName(outputPurposeCombo, "Report purpose");
            AutomationProperties.SetName(workspaceSelector, "Report workspace view");
            AutomationProperties.SetName(interpretationHost, "Interpretation workspace");
            AutomationProperties.SetName(previewHost, "Report preview workspace");
            AutomationProperties.SetName(previewPages, "Report preview pages");
            AutomationProperties.SetName(previewZoomCombo, "Report preview zoom");
            AutomationProperties.SetHelpText(previewZoomCombo,
                "Change the report preview magnification. Control-scroll or Command-scroll also adjusts zoom.");
            AutomationProperties.SetName(previewZoomOutButton, "Decrease report preview zoom");
            AutomationProperties.SetName(previewZoomInButton, "Increase report preview zoom");
            ToolTip.SetTip(previewZoomOutButton, "Reduce preview magnification");
            ToolTip.SetTip(previewZoomInButton, "Increase preview magnification");
            AutomationProperties.SetName(interpretationBox, "Report interpretation editor");
            AutomationProperties.SetHelpText(interpretationBox, "Write an interpretation or approve an automatically generated draft for inclusion in the report.");
            AutomationProperties.SetName(interpretationSummaryText, "Interpretation status");
            AutomationProperties.SetName(editInterpretationButton, "Edit report interpretation");
            AutomationProperties.SetName(generateInterpretationButton, "Generate interpretation");
            AutomationProperties.SetName(injectionTablesCheck, "Include injection tables");
            AutomationProperties.SetName(condenseRepeatedCheck, "Condense repeated experiments");
            AutomationProperties.SetName(expandedExplanationsCheck, "Expanded explanations");
            AutomationProperties.SetHelpText(expandedExplanationsCheck,
                "Adds explanations of summary uncertainty, weighted fit diagnostics, and parameter correlations to the report.");
            ToolTip.SetTip(expandedExplanationsCheck,
                "Adds explanations of summary uncertainty, weighted fit diagnostics, and parameter correlations.");
#if DEBUG
            AutomationProperties.SetName(extraTraceabilityCheck, "Extra traceability");
            AutomationProperties.SetHelpText(extraTraceabilityCheck, "Adds the preparer, generation time, and a signature and date line to the front page, plus analysis operators, filesystem experiment dates, and report and result identifiers.");
            ToolTip.SetTip(extraTraceabilityCheck, "Adds the preparer, generation time, and a signature and date line to the front page, plus analysis operators, filesystem experiment dates, and report and result identifiers.");
#endif
            AutomationProperties.SetHelpText(condenseRepeatedCheck,
                "For later appearances of the same experiment, retain figures, comments, and core conditions while omitting repeated processing details.");
            AutomationProperties.SetName(previewButton, "Update report preview");
            AutomationProperties.SetName(exportButton, "Export analysis report as PDF");
            AutomationProperties.SetName(statusHost, "Report status");
            AutomationProperties.SetName(footer, "Report export footer");
            AutomationProperties.SetLiveSetting(statusHost, AutomationLiveSetting.Polite);

            Content = WorkspaceControlBuilder.Workspace(border, Scroll(inspector), footer, useOuterMargin: true);
        }

        static Button PreviewZoomButton(bool increase)
        {
            var button = new Button
            {
                Width = 32,
                Height = 32,
                MinHeight = 32,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var icon = new global::Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M3,10 L17,10" + (increase ? " M10,3 L10,17" : "")),
                Width = 20,
                Height = 20,
                Stretch = Stretch.None,
                StrokeThickness = 1.5,
                Opacity = 0.8,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
            };
            icon.Bind(Shape.StrokeProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
                {
                    AncestorType = typeof(global::Avalonia.Controls.Presenters.ContentPresenter),
                },
            });
            button.Content = icon;
            AppTheme.Bind(button, global::Avalonia.Controls.Button.BackgroundProperty, AppTheme.PanelBackground);
            return button;
        }

        void WireEvents()
        {
            savedReportsButton.Click += (_, _) => OpenSavedReportsPicker();
            selectResultsButton.Click += (_, _) => OpenResultPicker();
            labelBox.PropertyChanged += (_, e) =>
            {
                if (e.Property == global::Avalonia.Controls.TextBox.TextProperty)
                    MarkStale();
            };
            reportIdBox.PropertyChanged += (_, e) =>
            {
                if (e.Property == global::Avalonia.Controls.TextBox.TextProperty) MarkStale();
            };
            titleBox.PropertyChanged += (_, e) =>
            {
                if (e.Property != global::Avalonia.Controls.TextBox.TextProperty) return;
                if (!changingResult) automaticTitle = false;
                MarkStale();
            };
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
#if DEBUG
            extraTraceabilityCheck.IsCheckedChanged += (_, _) =>
            {
                if (loadingReportOptions || AppSettings.TraceabilityModeEnabled) return;
                extraTraceabilityChoice = extraTraceabilityCheck.IsChecked == true;
                MarkStale();
            };
#endif
            expandedExplanationsCheck.IsCheckedChanged += (_, _) => MarkStale();
            outputPurposeCombo.SelectionChanged += (_, _) => MarkStale();
            workspaceSelector.SelectionChanged += async (_, _) => await WorkspaceSelectionChangedAsync();
            previewZoomCombo.SelectionChanged += (_, _) => ApplyPreviewZoom();
            previewZoomOutButton.Click += (_, _) => StepPreviewZoom(-1);
            previewZoomInButton.Click += (_, _) => StepPreviewZoom(1);
            previewScroll.AddHandler(InputElement.PointerWheelChangedEvent,
                PreviewPointerWheelChanged, RoutingStrategies.Tunnel, true);
            previewScroll.AddHandler(InputElement.PointerTouchPadGestureMagnifyEvent,
                PreviewTouchPadMagnify, RoutingStrategies.Tunnel, true);
            previewScroll.Pinch += PreviewPinch;
            previewScroll.PinchEnded += PreviewPinchEnded;
            interpretationBox.PropertyChanged += (_, e) =>
            {
                if (e.Property == global::Avalonia.Controls.TextBox.TextProperty && !loadingInterpretation)
                {
                    ApplyInterpretationEditor(userEdit: true);
                    MarkStale();
                }
            };
            interpretationBox.LostFocus += (_, _) => CommitInterpretationEditor();
            editInterpretationButton.Click += (_, _) => ShowInterpretationWorkspace(focusEditor: true);
            generateInterpretationButton.Click += async (_, _) => await GenerateInterpretationAsync();
        }

        void PopulateResults(AnalysisResult? selected, AnalysisReport? savedReport)
        {
            availableResults.Clear();
            availableResults.AddRange(DataManager.Results);
            availableExperiments.Clear();
            availableExperiments.AddRange(DataManager.Data);
            if (savedReport != null)
            {
                ApplyReportSelection(AnalysisReportPresentation.ResultsInProjectOrder(savedReport, availableResults),
                    false, exactReport: savedReport);
                return;
            }
            var initial = selected != null && availableResults.Contains(selected) ? selected
                : DataManager.SelectedResult != null && availableResults.Contains(DataManager.SelectedResult)
                    ? DataManager.SelectedResult : availableResults.FirstOrDefault();
            var initialResults = initial == null ? Array.Empty<AnalysisResult>() : new[] { initial };
            ApplyResultSelection(initialResults, false);
        }

        void BuildSavedReportsPicker()
        {
            savedReportsFlyout.Opened += (_, _) =>
            {
                savedReportsItems.Children.Clear();
                foreach (var saved in AnalysisReportEditingPresentation.SavedReports(DataManager.Reports))
                {
                    var missing = AnalysisReportPresentation.MissingResultCount(saved,
                        id => availableResults.FirstOrDefault(result => result.UniqueID == id));
                    var open = Button("Open", 64);
                    open.IsEnabled = missing == 0 && saved.ResultIds.Count > 0;
                    ToolTip.SetTip(open, open.IsEnabled ? ProjectInformationPresentation.OpenTooltip
                        : ProjectInformationPresentation.MissingResultsTooltip);
                    var title = AnalysisReportPresentation.DisplayName(saved, id => availableResults.FirstOrDefault(result => result.UniqueID == id));
                    var resultNames = string.Join(" · ", saved.ResultIds.Select(id => availableResults.FirstOrDefault(result => result.UniqueID == id)?.Name ?? "Missing result"));
                    var details = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
                    details.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
                    details.Children.Add(new TextBlock { Text = resultNames, FontSize = 11,
                        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
                    details.Children.Add(new TextBlock { Text = "Last saved: " + ProjectInformationPresentation.LastEditedText(saved),
                        FontSize = 11, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
                    ToolTip.SetTip(details, title + "\n" + resultNames);
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, MinHeight = 52 };
                    row.Children.Add(details); Grid.SetColumn(open, 1); row.Children.Add(open);
                    if (savedReportsItems.Children.Count > 0)
                        savedReportsItems.Children.Add(new Separator());
                    savedReportsItems.Children.Add(row);
                    open.Click += async (_, _) =>
                    {
                        savedReportsFlyout.IsOpen = false;
                        if (!await ResolveDraftBeforeLeavingAsync()) return;
                        ApplyReportSelection(AnalysisReportPresentation.ResultsInProjectOrder(saved, availableResults), false, exactReport: saved);
                    };
                }
                if (savedReportsItems.Children.Count == 0)
                    savedReportsItems.Children.Add(Text(AnalysisReportEditingPresentation.NoSavedReportsText));
            };
            savedReportsFlyout.Content = new ScrollViewer
            {
                Content = savedReportsItems, Width = 420, MaxHeight = 390,
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            };
        }

        void OpenSavedReportsPicker() => savedReportsFlyout.ShowAt(savedReportsButton);

        void BuildResultPicker()
        {
            var selectAll = Button("Select all", 78);
            var clear = Button("Clear", 58);
            var cancel = Button("Cancel", 68);
            var apply = Button("Apply", 68);
            void UpdateExperimentAvailability()
            {
                var checkedResults = resultPickerChecks
                    .Where(check => check.Tag is AnalysisResult && check.IsChecked == true)
                    .Select(check => (AnalysisResult)check.Tag!).ToList();
                var coveredIds = checkedResults
                    .SelectMany(result => result.Solution?.Solutions ?? new List<SolutionInterface>())
                    .Select(solution => solution?.Data?.UniqueID)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var check in resultPickerChecks.Where(check => check.Tag is ExperimentData))
                {
                    var experiment = (ExperimentData)check.Tag!;
                    check.IsEnabled = !coveredIds.Contains(experiment.UniqueID);
                    var role = AnalysisReportExperimentReferences.PickerRole(experiment, checkedResults);
                    if (check.Content is StackPanel content)
                        content.Children.OfType<TextBlock>().Last().Text = ExperimentPickerDetails(experiment, role);
                    ToolTip.SetTip(check, AnalysisReportExperimentReferences.PickerTooltip(experiment, checkedResults));
                }
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
                resultPickerItems.Children.Add(Text("Analysis results"));
                Action<ITCDataContainer> addPickerRow = item =>
                {
                    var result = item as AnalysisResult;
                    var experiment = item as ExperimentData;
                    var role = AnalysisReportExperimentReferences.PickerRole(experiment, selectedResults);
                    var details = new TextBlock
                    {
                        Text = result != null ? ResultPickerDetails(result) : ExperimentPickerDetails(experiment!, role), FontSize = 11,
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
                    ToolTip.SetTip(check, result != null ? AnalysisReportExperimentReferences.ResultTooltip
                        : AnalysisReportExperimentReferences.PickerTooltip(experiment, selectedResults));
                    check.IsCheckedChanged += (_, _) =>
                    {
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
            return LocalDateTimeFormatter.ShortDateTime(result.Date) + " • " + model + " • " + count +
                " experiment" + (count == 1 ? "" : "s");
        }

        string ExperimentPickerDetails(ExperimentData experiment, string role)
        {
            var processing = experiment.HasThermogram
                ? experiment.Processor?.BaselineCompleted == true ? "processed thermogram" : "thermogram; baseline incomplete"
                : experiment.Injections?.Any(injection => injection.IsIntegrated) == true ? "integrated heats only" : "processing incomplete";
            var relation = string.IsNullOrEmpty(role) ? "" : " • " + role;
            var date = experiment.DateSource == ExperimentDateSource.DataFile
                || experiment.DateSource == ExperimentDateSource.UserModified
                ? LocalDateTimeFormatter.ShortDate(experiment.Date) + " • " : "";
            return date + processing + relation;
        }

        void ApplyResultSelection(IEnumerable<AnalysisResult> selection, bool preserveCustomTitle,
            IEnumerable<ExperimentData>? supportingSelection = null) =>
            ApplyReportSelection(selection, preserveCustomTitle, supportingSelection);

        void ApplyReportSelection(IEnumerable<AnalysisResult> selection, bool preserveCustomTitle,
            IEnumerable<ExperimentData>? supportingSelection = null, AnalysisReport? exactReport = null)
        {
            if (!CanWriteReport()) return;
            if (editSession != null && exactReport == null && !CommitInterpretationEditor()) return;
            changingResult = true;
            try
            {
                selectedResults.Clear();
                selectedResults.AddRange(availableResults.Where(result => selection.Contains(result)));
                var ids = selectedResults.Select(result => result.UniqueID).ToList();
                var initializing = editSession == null || exactReport != null;
                if (initializing)
                {
                    var initial = exactReport ?? new AnalysisReport();
                    if (exactReport == null)
                    {
                        initial.SetResultIds(ids);
                        if (AppSettings.AutoSelectReportReferenceExperiments)
                            initial.SetSupportingExperimentIds(ReferenceExperimentsFor(selectedResults).Select(experiment => experiment.UniqueID));
                    }
                    editSession = new AnalysisReportEditSession(initial, exactReport);
                    report = editSession.Draft;
                }
                else
                {
                    report!.SetResultIds(ids);
                    if (supportingSelection != null)
                        report.SetSupportingExperimentIds(availableExperiments.Where(supportingSelection.Contains).Select(experiment => experiment.UniqueID));
                }
                condenseRepeatedCheck.IsEnabled = !busy
                    && AnalysisReportBuilder.HasRepeatedExperiments(selectedResults);
                selectedSupportingExperiments.Clear();
                selectedSupportingExperiments.AddRange(availableExperiments.Where(experiment =>
                    report?.SupportingExperimentIds.Contains(experiment.UniqueID) == true));
                ObserveSourceChanges();
                var effectiveCount = EffectiveSupportingExperiments().Count;
                selectResultsButton.Content = selectedResults.Count == 0 ? "Select report contents…"
                    : selectedResults.Count + " result" + (selectedResults.Count == 1 ? "" : "s")
                    + (effectiveCount > 0 ? " · " + effectiveCount + " supporting" : "");
                resultSummaryText.Text = selectedResults.Count == 0 ? "No result selected."
                    : ResultSummary(selectedResults) + " · " + effectiveCount
                        + " supporting experiment" + (effectiveCount == 1 ? "" : "s")
                        + (selectedResults.Count > 1 ? "\n" + string.Join(" · ", selectedResults.Select(result => result.Name)) : "");
                if (initializing || automaticTitle)
                {
                    automaticTitle = true;
                    titleBox.Text = AnalysisReportPresentation.AutomaticTitle(selectedResults);
                }
                RebuildAdvancedChoices(selectedResults);
                LoadReport();
                if (initializing)
                {
                    editSession!.AcceptBaseline();
                    ClearReportUndoHistory();
                }
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

        void ClearReportUndoHistory()
        {
            foreach (var editor in new[] { labelBox, titleBox, reportIdBox, interpretationBox })
            {
                var enabled = editor.IsUndoEnabled;
                editor.IsUndoEnabled = false;
                editor.IsUndoEnabled = enabled;
            }
        }

        void LoadReport()
        {
#if DEBUG
            loadingReportOptions = true;
#endif
            reportIdBox.Text = report?.PresentationSettings.ReportId ?? "";
            if (report != null && report.HasPresentationSettings)
            {
                var saved = report.PresentationSettings;
                labelBox.Text = saved.DocumentLabel;
                automaticTitle = saved.AutomaticTitle;
                titleBox.Text = automaticTitle
                    ? AnalysisReportPresentation.AutomaticTitle(selectedResults)
                    : saved.Title;
                energyCombo.SelectedIndex = Array.IndexOf(EnergyFamilies, saved.EnergyUnitFamily);
                temperatureCombo.SelectedIndex = saved.UseKelvin ? 1 : 0;
                injectionTablesCheck.IsChecked = saved.IncludeInjectionTables;
                condenseRepeatedCheck.IsChecked = saved.CondenseRepeatedExperiments;
                expandedExplanationsCheck.IsChecked = saved.ExpandedExplanations;
#if DEBUG
                extraTraceabilityChoice = saved.ExtraTraceability;
#endif
                uncertaintyCombo.SelectedIndex = saved.UncertaintyDisplayStyle switch
                {
                    UncertaintyDisplayStyle.StandardDeviation => 1,
                    UncertaintyDisplayStyle.ConfidenceInterval => 2,
                    UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval => 3,
                    UncertaintyDisplayStyle.None => 4,
                    _ => 0,
                };
                var savedAdvanced = saved.AdvancedSections.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
                foreach (var check in advancedChecks)
                    check.IsChecked = savedAdvanced.Contains(((AnalysisReportAdvancedSectionDescriptor)check.Tag!).Request.Key);
            }
            else if (report != null)
            {
                labelBox.Text = "";
                automaticTitle = true;
                titleBox.Text = AnalysisReportPresentation.AutomaticTitle(selectedResults);
                energyCombo.SelectedIndex = Array.IndexOf(EnergyFamilies, AppSettings.EnergyUnitFamily);
                temperatureCombo.SelectedIndex = 0;
                uncertaintyCombo.SelectedIndex = 3;
                injectionTablesCheck.IsChecked = true;
                condenseRepeatedCheck.IsChecked = true;
                expandedExplanationsCheck.IsChecked = false;
                extraTraceabilityChoice = false;
                // Advanced choices were rebuilt from their independent per-selection draft above.
                report.InitializePresentationSettings(CurrentOptions());
            }
            loadingInterpretation = true;
            interpretationBox.Text = report?.ApprovedInterpretation?.InterpretationMarkdown ?? "";
            loadingInterpretation = false;
#if DEBUG
            loadingReportOptions = false;
#endif
            ApplyTraceabilityCheckboxState();
            RefreshPreparerDisplay();
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
            return AnalysisReportExperimentReferences.Candidates(selection, availableExperiments);
        }

        void UpdateSaveState()
        {
            if (editSession == null) return;
            var rawChanged = editSession.HasInterpretationTextChanges(interpretationBox.Text ?? "");
            var canSave = !busy && selectedResults.Count > 0 && CanWriteReport();
            saveReportButton.IsEnabled = canSave && (editSession.NeedsSave || rawChanged);
            saveReportAsButton.IsEnabled = canSave;
        }

        internal bool SaveReport(bool saveAs)
        {
            if (busy || editSession == null || !CanWriteReport() || !CommitInterpretationEditor()) return false;
            try
            {
                report!.UpdatePresentationSettings(CurrentOptions());
                editSession.Save(saveAs, id => availableResults.FirstOrDefault(result => result.UniqueID == id)!,
                    id => availableExperiments.FirstOrDefault(experiment => experiment.UniqueID == id)!);
                report = editSession.Draft;
                MarkStale();
                SetStatus("Report saved in project. Use File > Save to save the project file.");
                return true;
            }
            catch (Exception ex) { SetStatus(ex.Message, true); return false; }
        }

        internal Task<bool> ResolveDraftBeforeLeavingAsync()
        {
            if (pendingLeave != null) return pendingLeave;
            UpdateSaveState();
            var rawChanged = editSession?.HasInterpretationTextChanges(interpretationBox.Text ?? "") == true;
            if (editSession?.HasChanges != true && !rawChanged) return Task.FromResult(true);
            return pendingLeave = PromptAsync();
        }

        async Task<bool> PromptAsync()
        {
            await Task.Yield();
            try
            {
                var action = LeavePromptOverride != null ? await LeavePromptOverride() : await ShowLeavePromptAsync();
                return action == 1 || action == 0 && SaveReport(false);
            }
            finally { pendingLeave = null; }
        }

        async Task<int> ShowLeavePromptAsync()
        {
            var dialog = new Window { Title = AnalysisReportEditingPresentation.LeaveTitle, Width = 440, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
            var save = Button("Save", 80); var discard = Button("Discard", 80); var cancel = Button("Cancel", 80);
            ToolTip.SetTip(save, AnalysisReportEditingPresentation.SaveTooltip);
            ToolTip.SetTip(discard, AnalysisReportEditingPresentation.DiscardTooltip);
            ToolTip.SetTip(cancel, AnalysisReportEditingPresentation.CancelTooltip);
            save.Click += (_, _) => dialog.Close(0); discard.Click += (_, _) => dialog.Close(1); cancel.Click += (_, _) => dialog.Close(2);
            cancel.IsCancel = true;
            dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 18,
                Children = { new TextBlock { Text = AnalysisReportEditingPresentation.LeaveText, TextWrapping = TextWrapping.Wrap }, Row(save, discard, cancel) } };
            // Closing the prompt is Cancel, never an implicit Save.
            var answer = 2;
            return await dialog.ShowDialog<int?>(this) ?? answer;
        }

        internal async Task<bool> RequestCloseAsync()
        {
            if (!await ResolveDraftBeforeLeavingAsync()) return false;
            allowingClose = true; Close(); return true;
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!allowingClose && !invalidated && !closed)
            {
                if (editSession?.HasChanges == true || editSession?.HasInterpretationTextChanges(interpretationBox.Text ?? "") == true)
                {
                    e.Cancel = true;
                    _ = RequestCloseAsync();
                    return;
                }
            }
            base.OnClosing(e);
        }

        bool CommitInterpretationEditor()
            => ApplyInterpretationEditor(userEdit: false);

        bool ApplyInterpretationEditor(bool userEdit)
        {
            if (loadingInterpretation || report == null || !CanWriteReport()) return true;
            var edited = interpretationBox.Text ?? "";
            if (!userEdit && !editSession!.HasInterpretationTextChanges(edited)) return true;
            try
            {
                editSession!.UpdateInterpretationText(edited,
                    AnalysisInterpretationPackageBuilder.AssessmentContextFingerprintFromResults(selectedResults));
                UpdateSaveState();
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
            if (busy || report == null || selectedResults.Count == 0 || !CanWriteReport()) return;
            var generatingReport = report;
            var dialog = new AnalysisInterpretationDialog(generatingReport,
                id => availableResults.FirstOrDefault(result => result.UniqueID == id)!,
                id => availableExperiments.FirstOrDefault(experiment => experiment.UniqueID == id)!,
                InterpretationHttpClient, () =>
            {
                UpdateSaveState();
                MarkStale();
                UpdateInterpretationStatus();
            }, () => CanWriteReport() && ReferenceEquals(report, generatingReport));
            var draft = await dialog.ShowDialog<AnalysisInterpretationRecord?>(this);
            if (draft == null || !CanWriteReport() || !ReferenceEquals(report, generatingReport)) return;
            report.ApproveInterpretation(draft);
            UpdateSaveState();
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
                .GroupBy(item => item.Request.Key, StringComparer.Ordinal)
                .Select(group => group.First()).ToList();
            var key = ResultKey(results);
            var selected = SessionAdvancedSelections.TryGetValue(key, out var saved)
                ? saved : descriptors.Select(item => item.Request.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var descriptor in descriptors)
            {
                var check = Check(descriptor.Title);
                check.Tag = descriptor;
                check.IsChecked = selected.Contains(descriptor.Request.Key);
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
                .Select(check => ((AnalysisReportAdvancedSectionDescriptor)check.Tag!).Request.Key)
                .ToHashSet();
        }

        AnalysisReportOptions CurrentOptions()
        {
            var options = new AnalysisReportOptions
            {
                DocumentLabel = labelBox.Text ?? "",
                ReportId = reportIdBox.Text?.Trim() ?? "",
                Title = titleBox.Text ?? "",
                EnergyUnitFamily = energyCombo.SelectedIndex >= 0 && energyCombo.SelectedIndex < EnergyFamilies.Length
                    ? EnergyFamilies[energyCombo.SelectedIndex] : AppSettings.EnergyUnitFamily,
                EnergyUnitOverride = report?.PresentationSettings.EnergyUnitOverride,
                UseKelvin = temperatureCombo.SelectedIndex == 1,
                IncludeInjectionTables = injectionTablesCheck.IsChecked == true,
                CondenseRepeatedExperiments = condenseRepeatedCheck.IsChecked == true,
                ExpandedExplanations = expandedExplanationsCheck.IsChecked == true,
                UncertaintyDisplayStyle = uncertaintyCombo.SelectedIndex switch
                {
                    1 => UncertaintyDisplayStyle.StandardDeviation,
                    2 => UncertaintyDisplayStyle.ConfidenceInterval,
                    3 => UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
                    4 => UncertaintyDisplayStyle.None,
                    _ => UncertaintyDisplayStyle.Automatic
                },
                OutputPurpose = outputPurposeCombo.SelectedIndex == 1
                    ? ResultOutputPurpose.Diagnostic : ResultOutputPurpose.Standard,
                AutomaticTitle = automaticTitle,
#if DEBUG
                ExtraTraceability = extraTraceabilityChoice,
#else
                ExtraTraceability = false,
#endif
                Author = AppSettings.UserName,
                GeneratedAtUtc = DateTime.UtcNow,
                ApplicationVersion = AppVersion.FullVersionString,
                ResultOrderIds = DataManager.Results.Select(result => result.UniqueID).ToList(),
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
            if (selectedResults.Count == 0 || !CanWriteReport()) return false;
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
                if (!CanWriteReport()) return false;
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
            previewZoomOutButton.IsEnabled = previewZoomCombo.SelectedIndex > 0;
            previewZoomInButton.IsEnabled = previewZoomCombo.SelectedIndex >= 0
                && previewZoomCombo.SelectedIndex < PreviewZoomLevels.Length - 1;
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
            if (!await EnsureExportDocumentAsync()) return;

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

        internal async Task<bool> EnsureExportDocumentAsync()
        {
            if (!CanWriteReport()) return false;
            var preparerNeedsRefresh = currentDocument != null
                && AnalysisReportBuilder.NeedsPreparerRefresh(currentDocument);
            if (!previewStale && !preparerNeedsRefresh && currentDocument != null && currentPlan != null) return true;
            if (preparerNeedsRefresh) return await BuildAsync(showPreview: true);
            return await BuildAsync(showPreview: false);
        }

        void MarkStale()
        {
            if (changingResult || !CanWriteReport()) return;
            ApplyTraceabilityCheckboxState();
            if (report != null && selectedResults.Count > 0
                && !report.PresentationSettingsEqual(CurrentOptions()))
            {
                report.UpdatePresentationSettings(CurrentOptions());
                UpdateSaveState();
            }
            previewStale = true;
            currentDocument = null;
            currentPlan = null;
            if (previewPages.IsVisible && CurrentValidation().IsValid)
                SetStatus("Preview is out of date. Select Update Preview to refresh.", false, true);
            UpdateInterpretationStatus();
            UpdateSaveState();
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
            savedReportsButton.IsEnabled = !value;
            selectResultsButton.IsEnabled = !value;
            labelBox.IsEnabled = !value;
            titleBox.IsEnabled = !value;
            energyCombo.IsEnabled = !value;
            temperatureCombo.IsEnabled = !value;
            uncertaintyCombo.IsEnabled = !value;
            outputPurposeCombo.IsEnabled = !value;
            injectionTablesCheck.IsEnabled = !value && selectedResults.Count > 0;
            expandedExplanationsCheck.IsEnabled = !value && selectedResults.Count > 0;
            condenseRepeatedCheck.IsEnabled = !value
                && AnalysisReportBuilder.HasRepeatedExperiments(selectedResults);
            ApplyTraceabilityCheckboxState();
            advancedPanel.IsEnabled = !value;
            workspaceSelector.IsEnabled = !value;
            interpretationBox.IsEnabled = !value && selectedResults.Count > 0;
            editInterpretationButton.IsEnabled = !value && selectedResults.Count > 0;
            generateInterpretationButton.IsEnabled = !value && selectedResults.Count > 0;
            selectAllButton.IsEnabled = clearButton.IsEnabled = !value && advancedChecks.Count > 0;
            previewButton.IsEnabled = !value && CurrentValidation().IsValid;
            exportButton.IsEnabled = previewButton.IsEnabled;
            UpdateSaveState();
            if (!string.IsNullOrWhiteSpace(message)) SetStatus(message);
        }

        AnalysisReportValidationResult CurrentValidation() => report == null
            ? AnalysisReportBuilder.Validate(selectedResults, CurrentOptions().OutputPurpose)
            : AnalysisReportBuilder.Validate(report,
                id => availableResults.FirstOrDefault(result => result.UniqueID == id),
                id => availableExperiments.FirstOrDefault(experiment => experiment.UniqueID == id),
                CurrentOptions().OutputPurpose);

        void UpdateInterpretationStatus()
        {
            var record = report?.ApprovedInterpretation;
            string summary;
            if (record == null) summary = "No interpretation";
            else if (record.Origin == AnalysisInterpretationOrigin.Manual) summary = "Manual";
            else
            {
                summary = record.UserEdited ? "Automatically generated, user edited" : "Automatically generated";
                var freshness = AnalysisInterpretationService.EvaluateFreshness(report,
                    id => availableResults.FirstOrDefault(result => result.UniqueID == id)!,
                    id => availableExperiments.FirstOrDefault(experiment => experiment.UniqueID == id)!,
                    DataManager.Results.Select(result => result.UniqueID).ToList()).Status;
                summary += freshness == AnalysisInterpretationFreshness.Current ? " • Current"
                    : freshness == AnalysisInterpretationFreshness.Stale ? " • Out of date" : " • Cannot verify";
            }
            interpretationSummaryText.Text = summary;
            interpretationWorkspaceStatus.Text = summary == "No interpretation"
                ? "Write remarks or a scientific interpretation to include in the report."
                : summary;
            var review = AnalysisReportEditingPresentation.NeedsInterpretationReview(report!, id => availableResults.FirstOrDefault(result => result.UniqueID == id)!);
            interpretationReviewText.IsVisible = confirmInterpretationButton.IsVisible = review;
            confirmInterpretationButton.IsEnabled = !busy;
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
        readonly Action draftChanged;
        readonly Func<bool> canApply;
        readonly IReadOnlyList<string> projectResultOrder = DataManager.Results.Select(result => result.UniqueID).ToList();
        readonly ComboBox evidenceLevelCombo = Combo(220);
        readonly TextBlock evidenceLevelDescription = Hint("");
        readonly StackPanel evidenceLevelOptions = new StackPanel { Spacing = 4 };
        readonly TextBlock dataInclusionLabel = Heading("Evidence level");
        readonly AnalysisInterpretationEvidenceSelection evidenceSelection;
        bool updatingEvidenceLevel;
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
        readonly TextBlock generatedProvenance = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        readonly TextBlock generationLabel = Heading("Generation");
        readonly ComboBox interpretationPresetCombo = Combo(170);
        readonly ComboBox interpretationModelCombo = Combo(170);
        readonly ComboBox interpretationReasoningCombo = Combo(170);
        readonly CheckBox omitScientificGuidance = new CheckBox { Content = "Omit scientific guidance", IsChecked = false };
        readonly StackPanel interpretationSelectionControls = new StackPanel { Spacing = 4 };
        readonly StackPanel interpretationPresetSelectionRow;
        readonly StackPanel interpretationModelSelectionRow;
        readonly StackPanel interpretationReasoningSelectionRow;
        readonly ProgressBar progress = new ProgressBar { IsIndeterminate = true, IsVisible = false, Height = 3 };
        readonly Button savePackage = WorkspaceControlBuilder.Button("Save package", 112);
        readonly Button generate = WorkspaceControlBuilder.Button("Generate", 92);
        readonly Button use = WorkspaceControlBuilder.Button("Use in report", 112);
        readonly Button cancel = WorkspaceControlBuilder.Button("Cancel", 78);
        CancellationTokenSource? cancellation;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        AnalysisInterpretationRecord? generatedRecord;
        InterpretationOperatorOptionsResponse? interpretationOptions;
        bool interpretationSelectionEnabled;
        bool serviceAllowsGeneration;
        bool interpretationAccessAllowsGeneration;
        bool interpretationAccessCheckFailed;
        CancellationTokenSource? packagePreviewCancellation;
        int packagePreviewRevision;
        bool packageSizeAllowsGeneration;

        public AnalysisInterpretationDialog(AnalysisReport report, AnalysisResult result, HttpClient httpClient, Action draftChanged)
            : this(report, id => result?.UniqueID == id ? result : null!, _ => null!, httpClient, draftChanged) { }

        public AnalysisInterpretationDialog(AnalysisReport report, Func<string, AnalysisResult> resultResolver,
            Func<string, ExperimentData> experimentResolver, HttpClient httpClient, Action draftChanged,
            Func<bool>? canApply = null)
        {
            this.report = report;
            this.resultResolver = resultResolver;
            this.experimentResolver = experimentResolver;
            this.httpClient = httpClient;
            this.draftChanged = draftChanged;
            this.canApply = canApply ?? (() => true);
            evidenceSelection = new AnalysisInterpretationEvidenceSelection(report.InterpretationSettings);
            evidenceLevelOptions.Children.Add(evidenceLevelCombo);
            evidenceLevelOptions.Children.Add(evidenceLevelDescription);
            ToolTip.SetTip(evidenceLevelCombo, AnalysisInterpretationEvidencePolicy.SelectorTooltip);
            ToolTip.SetTip(dataInclusionLabel, AnalysisInterpretationEvidencePolicy.SelectorTooltip);
            AutomationProperties.SetName(evidenceLevelCombo, "Interpretation evidence level");
            interpretationPresetSelectionRow = SelectionRow("Interpretation depth", interpretationPresetCombo, 180);
            interpretationModelSelectionRow = SelectionRow("Model", interpretationModelCombo);
            interpretationReasoningSelectionRow = SelectionRow("Reasoning", interpretationReasoningCombo);
            interpretationSelectionControls.Children.Add(interpretationPresetSelectionRow);
            interpretationSelectionControls.Children.Add(interpretationModelSelectionRow);
            interpretationSelectionControls.Children.Add(interpretationReasoningSelectionRow);
            interpretationSelectionControls.Children.Add(omitScientificGuidance);
            PopulateInterpretationChoices();
            interpretationAccountSummary.Text = "Checking interpretation access…";
            generate.IsEnabled = false;
            retryServiceStatus.IsVisible = false;
            Opened += async (_, _) => await RefreshGenerationAvailabilityAsync();
            Title = "Generate Interpretation";
            Width = 660; Height = 660; MinWidth = 580; MinHeight = 580;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var context = report.StudyContext;
            questionBox.Text = context.ScientificQuestion;
            contextBox.Text = string.Join("\n\n", new[] { context.SystemDescription, context.AdditionalNotes }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            questionBox.TextChanged += (_, _) => UpdatePackageSize();
            contextBox.TextChanged += (_, _) => UpdatePackageSize();
            evidenceLevelCombo.SelectionChanged += (_, _) =>
            {
                if (updatingEvidenceLevel || evidenceLevelCombo.SelectedIndex < 0) return;
                evidenceSelection.Select(evidenceSelection.AvailableLevels[evidenceLevelCombo.SelectedIndex]);
                evidenceLevelDescription.Text = AnalysisInterpretationEvidencePolicy.Description(evidenceSelection.EffectiveLevel);
                UpdatePackageSize();
            };
            draftBox.IsVisible = false;
            draftBox.IsReadOnly = true;
            ToolTip.SetTip(draftBox, AnalysisReportEditingPresentation.GeneratedTextTooltip);
            use.IsVisible = false;
            use.Click += async (_, _) =>
            {
                if (generatedRecord == null || !this.canApply()) return;
                try
                {
                    var candidate = generatedRecord.Copy();
                    if (report.ApprovedInterpretation != null && !await ConfirmReplacementAsync()) return;
                    if (!this.canApply()) return;
                    Close(candidate);
                }
                catch (AnalysisInterpretationValidationException ex) { SetError(ex.Errors.FirstOrDefault() ?? ex.Message); }
            };
            savePackage.Click += async (_, _) => await SavePackageAsync(StorageProvider.SaveFilePickerAsync);
            AutomationProperties.SetName(savePackage, "Save interpretation package locally without generation");
            generate.Click += async (_, _) => await GenerateAsync(httpClient);
            interpretationPresetCombo.SelectionChanged += (_, _) => UpdateInterpretationSetting();
            interpretationModelCombo.SelectionChanged += (_, _) =>
            {
                PopulateReasoningChoices();
                UpdateInterpretationSetting();
            };
            interpretationReasoningCombo.SelectionChanged += (_, _) => UpdateInterpretationSetting();
            omitScientificGuidance.IsCheckedChanged += (_, _) => UpdateInterpretationSetting();
            retryServiceStatus.Click += async (_, _) => await RefreshGenerationAvailabilityAsync();
            AutomationProperties.SetName(retryServiceStatus, "Retry interpretation access and service availability checks");
            cancel.Click += (_, _) => { if (cancellation != null) cancellation.Cancel(); else Close(null); };
            AutomationProperties.SetName(questionBox, "Main question");
            AutomationProperties.SetName(contextBox, "Additional context");
            AutomationProperties.SetName(draftBox, "Generated interpretation draft");
            AutomationProperties.SetName(interpretationSetting, "Interpretation account status");
            AutomationProperties.SetName(interpretationAccountSummary, "Interpretation account");
            ToolTip.SetTip(packageSize, "Compact package size and current request allowance.");
            AutomationProperties.SetName(interpretationOptionDescription, "Selected generation option description");
            AutomationProperties.SetName(omitScientificGuidance, "Omit scientific guidance");
            AutomationProperties.SetName(generatedProvenance, "Generated interpretation details");
            AppTheme.Bind(serviceStatus, TextBlock.ForegroundProperty, AppTheme.MutedText);
            AppTheme.Bind(packageSize, TextBlock.ForegroundProperty, AppTheme.MutedText);
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
                                Hint("Sends report evidence at the selected level, your question and context to the FT-ITC interpretation service and OpenAI's API. Your data is not used to train models. Abuse-monitoring retention may last up to 30 days; FT-ITC retains usage metadata without automatic expiry. See Help: Analysis Report for details."),
                                Heading("Main question"), questionBox,
                                Heading("Additional context"), Hint("Describe the system, cell and syringe contents, controls, limitations, or caveats."), contextBox,
                                dataInclusionLabel, evidenceLevelOptions,
                                generationLabel,
                                new StackPanel
                                {
                                    Spacing = 2,
                                    Children =
                                    {
                                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { serviceStatus, retryServiceStatus } },
                                        packageSize,
                                        interpretationAccountSummary,
                                        interpretationSelectionControls,
                                        interpretationSetting,
                                        interpretationOptionDescription,
                                    }
                                },
                                progress, status, generatedProvenance, draftBox,
                            }
                        }
                    }
                }
            };
            UpdatePackageSize();
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
            packagePreviewCancellation?.Cancel();
            packagePreviewCancellation?.Dispose();
            packagePreviewCancellation = null;
            lifetime.Cancel();
            base.OnClosing(e);
        }

        bool SaveInputs()
        {
            if (!canApply()) return false;
            var inputs = CreateDialogReport();
            report.UpdateInterpretationSettings(inputs.InterpretationSettings);
            report.UpdateStudyContext(inputs.StudyContext);
            draftChanged();
            return true;
        }

        AnalysisReport CreateDialogReport()
        {
            var settings = evidenceSelection.RequestOptions(report.InterpretationSettings);
            var context = report.StudyContext.Copy();
            context.ScientificQuestion = questionBox.Text ?? "";
            context.SystemDescription = "";
            context.AdditionalNotes = contextBox.Text ?? "";
            return report.CreateDetachedCopy(context, settings);
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
            interpretationAccessCheckFailed = false;
            interpretationAccessAllowsGeneration = options != null
                && InterpretationAccessDisplay.PublicAllowancePermitsGeneration(options);
            UpdateInterpretationAccountSummary();
            UpdateOptionalPackageInclusionControls(options);

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
            UpdateInterpretationSetting();
            UpdatePackageSize();
        }

        void UpdateOptionalPackageInclusionControls(InterpretationOperatorOptionsResponse? options)
        {
            evidenceSelection.ApplyCapabilities(options);
            updatingEvidenceLevel = true;
            try
            {
                evidenceLevelCombo.ItemsSource = evidenceSelection.AvailableLevels.Select(AnalysisInterpretationEvidencePolicy.Label).ToArray();
                evidenceLevelCombo.SelectedIndex = evidenceSelection.AvailableLevels.ToList().IndexOf(evidenceSelection.EffectiveLevel);
                evidenceLevelCombo.IsEnabled = options != null && cancellation == null;
                evidenceLevelDescription.Text = AnalysisInterpretationEvidencePolicy.Description(evidenceSelection.EffectiveLevel);
                evidenceLevelOptions.IsVisible = dataInclusionLabel.IsVisible = evidenceSelection.IsVisible;
            }
            finally { updatingEvidenceLevel = false; }
        }

        void UpdateInterpretationAccountSummary()
        {
            InterpretationAccountResponse? account = null;
            if (!string.IsNullOrWhiteSpace(AppSettings.InterpretationOperatorCode)
                && AppSettings.TryGetInterpretationAccount(AppSettings.InterpretationOperatorCode, out var cached, out _))
                account = cached;
            interpretationAccountSummary.Text = string.Equals(interpretationOptions?.AccessTier, "public", StringComparison.OrdinalIgnoreCase)
                ? InterpretationAccessDisplay.PublicAllowanceSummary(interpretationOptions)
                : account == null && interpretationOptions == null
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
            UpdateGuidanceOmissionControl();
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
                    OmitScientificGuidance = omitScientificGuidance.IsChecked == true,
                };
            }
            var preset = interpretationPresetCombo.SelectedItem as InterpretationPresetOption;
            return new AnalysisInterpretationGenerationSelection
                { TaskType = preset?.TaskType ?? "interpretation", PresetId = preset?.Id ?? "instant" };
        }

        void UpdateInterpretationSetting()
        {
            UpdateGuidanceOmissionControl();
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

        void UpdateGuidanceOmissionControl()
        {
            var isSummary = interpretationModelCombo.SelectedItem as string == "summary";
            var visible = interpretationOptions?.Mode == "custom"
                && interpretationOptions.SupportsGuidanceOmission && !isSummary;
            omitScientificGuidance.IsVisible = visible;
            omitScientificGuidance.IsEnabled = visible && interpretationSelectionEnabled && cancellation == null;
            if (!visible) omitScientificGuidance.IsChecked = false;
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
            interpretationAccessAllowsGeneration = false;
            interpretationSelectionEnabled = false;
            interpretationAccountSummary.Text = "Checking interpretation access…";
            SetBusy(false);
            try
            {
                var client = new FtItcInterpretationClient(httpClient, new Uri("https://app.ft-itc.org"));
                var options = await client.GetInterpretationOptionsAsync(
                    AppSettings.InterpretationOperatorCode ?? "", lifetime.Token);
                if (!lifetime.IsCancellationRequested) { PopulateInterpretationChoices(options); SetBusy(false); }
            }
            catch (OperationCanceledException) { }
            catch (AnalysisInterpretationProviderException ex) when (ex.Kind == AnalysisInterpretationFailureKind.PublicAccessDenied)
            { SetInterpretationAccessFailure(ex.Message); }
            catch (AnalysisInterpretationProviderException)
            { SetInterpretationAccessFailure("Interpretation access could not be checked. Try again."); }
            catch (HttpRequestException)
            { SetInterpretationAccessFailure("Interpretation access could not be checked. Try again."); }
        }

        void SetInterpretationAccessFailure(string message)
        {
            interpretationOptions = null;
            UpdateOptionalPackageInclusionControls(null);
            interpretationSelectionEnabled = false;
            interpretationAccessAllowsGeneration = false;
            interpretationAccessCheckFailed = true;
            interpretationAccountSummary.Text = message;
            retryServiceStatus.IsVisible = true;
            UpdatePackageSize();
            SetBusy(false);
        }

        async Task RefreshGenerationAvailabilityAsync()
        {
            await RefreshInterpretationAccountAsync();
            await RefreshServiceStatusAsync();
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
                parts.Add("expires " + LocalDateTimeFormatter.ShortDate(expiry.ToLocalTime()));
            if (preset?.Quota?.Limited == true)
            {
                parts.Add(preset.Quota.RemainingPercent + "% usage remaining");
                if (preset.Quota.ResetsAtUtc != default)
                    parts.Add("resets " + LocalDateTimeFormatter.ShortDate(preset.Quota.ResetsAtUtc.ToLocalTime()));
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
            var revision = ++packagePreviewRevision;
            packagePreviewCancellation?.Cancel();
            packagePreviewCancellation?.Dispose();
            packagePreviewCancellation = null;
            packageSizeAllowsGeneration = false;
            var maximumBytes = interpretationOptions?.MaximumRequestBytes;
            if (maximumBytes is not > 0)
            {
                packageSize.Text = "Scientific package: Limit unavailable";
                AppTheme.Bind(packageSize, TextBlock.ForegroundProperty, AppTheme.MutedText);
                UpdateGenerateButton();
                return;
            }

            packageSize.Text = "Scientific package: Calculating…";
            AppTheme.Bind(packageSize, TextBlock.ForegroundProperty, AppTheme.MutedText);
            UpdateGenerateButton();
            AnalysisReport snapshot;
            Dictionary<string, AnalysisResult> results;
            Dictionary<string, ExperimentData> experiments;
            try
            {
                snapshot = CreateDialogReport();
                results = snapshot.ResultIds.Distinct(StringComparer.Ordinal)
                    .ToDictionary(id => id, id => resultResolver(id), StringComparer.Ordinal);
                experiments = snapshot.SupportingExperimentIds.Distinct(StringComparer.Ordinal)
                    .ToDictionary(id => id, id => experimentResolver(id), StringComparer.Ordinal);
            }
            catch
            {
                packageSize.Text = "Scientific package: Size unavailable";
                packageSizeAllowsGeneration = false;
                UpdateGenerateButton();
                return;
            }
            var cancellationTokenSource = packagePreviewCancellation = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, cancellationTokenSource.Token).ConfigureAwait(false);
                    var package = AnalysisInterpretationPackageBuilder.Build(snapshot,
                        id => results.TryGetValue(id, out var result) ? result : null,
                        id => experiments.TryGetValue(id, out var experiment) ? experiment : null,
                        snapshot.InterpretationSettings, projectResultOrder);
                    var bytes = System.Text.Encoding.UTF8.GetByteCount(AnalysisInterpretationModelInputWriter.Write(package));
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!InterpretationPackageSizeEstimate.CanApplyPreview(revision, packagePreviewRevision, cancellationTokenSource.IsCancellationRequested)) return;
                        packageSizeAllowsGeneration = InterpretationPackageSizeEstimate.Fits(bytes, maximumBytes.Value);
                        packageSize.Text = $"Package {bytes / 1024.0:0.0} KiB / {maximumBytes.Value / 1024.0:0.0} KiB";
                        if (!packageSizeAllowsGeneration)
                        {
                            packageSize.Text += " · Package too large";
                            AppTheme.Bind(packageSize, TextBlock.ForegroundProperty, AppTheme.StatusError);
                        }
                        else AppTheme.Bind(packageSize, TextBlock.ForegroundProperty, AppTheme.MutedText);
                        UpdateGenerateButton();
                    });
                }
                catch (OperationCanceledException) { }
                catch
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!InterpretationPackageSizeEstimate.CanApplyPreview(revision, packagePreviewRevision, cancellationTokenSource.IsCancellationRequested)) return;
                        packageSize.Text = "Scientific package: Size unavailable";
                        AppTheme.Bind(packageSize, TextBlock.ForegroundProperty, AppTheme.MutedText);
                        packageSizeAllowsGeneration = false;
                        UpdateGenerateButton();
                    });
                }
            });
        }

        void UpdateGenerateButton() => generate.IsEnabled = InterpretationPackageSizeEstimate.CanGenerate(
            serviceAllowsGeneration, interpretationAccessAllowsGeneration, cancellation != null, packageSizeAllowsGeneration);

        internal async Task SavePackageAsync(Func<FilePickerSaveOptions, Task<IStorageFile?>> saveFilePicker)
        {
            var file = await saveFilePicker(new FilePickerSaveOptions
            {
                Title = "Save interpretation package",
                SuggestedFileName = "ftitc-interpretation-package.zip",
                FileTypeChoices = new[] { new FilePickerFileType("Interpretation package archive") { Patterns = new[] { "*.zip" } } },
            });
            if (file == null) return;
            SetBusy(true);
            SetStatus("Building local interpretation package…");
            try
            {
                var snapshot = CreateDialogReport();
                await Task.Yield();
                var package = AnalysisInterpretationPackageBuilder.Build(snapshot, resultResolver, experimentResolver,
                    snapshot.InterpretationSettings, projectResultOrder);
                var bytes = AnalysisInterpretationDebugExport.CreateArchive(package);
                await using var stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                await stream.WriteAsync(bytes);
                SetStatus("Interpretation package saved locally. Nothing was sent to the server.");
            }
            catch (Exception ex) { SetError("Could not save interpretation package. " + ex.Message); }
            finally { SetBusy(false); }
        }

        async Task GenerateAsync(HttpClient httpClient)
        {
            if (cancellation != null) return;
            if (!serviceAllowsGeneration) { SetError(serviceStatus.Text ?? "Interpretation generation is unavailable."); return; }
            var warnings = AnalysisInterpretationService.GetGenerationWarnings(report, resultResolver, experimentResolver);
            if (warnings.Count > 0 && !await ConfirmWarningsAsync(warnings)) return;
            if (!SaveInputs()) return;
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
                    CurrentGenerationSelection(), projectResultOrder);
                generationToken.ThrowIfCancellationRequested();
                if (!canApply()) return;
                generatedRecord = generated.Interpretation;
                draftBox.Text = generatedRecord.InterpretationMarkdown;
                generatedProvenance.Text = InterpretationAccessDisplay.GenerationProvenance(generatedRecord);
                generatedProvenance.IsVisible = true;
                draftBox.IsVisible = true;
                use.IsVisible = true;
                SetStatus("Finished — interpretation ready. Review the draft before adding it to the report.");
                PlatformServices.AppNotificationService.ShowSystemNotification(
                    "Interpretation ready",
                    "The analysis report interpretation is ready to review.");
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
                await RefreshInterpretationAccountAsync();
            }
        }

        void SetBusy(bool value)
        {
            progress.IsVisible = value;
            var selectionEnabled = !value && interpretationSelectionEnabled;
            questionBox.IsEnabled = contextBox.IsEnabled = savePackage.IsEnabled = use.IsEnabled = !value;
            evidenceLevelCombo.IsEnabled = !value && interpretationOptions != null;
            if (value) generate.IsEnabled = false; else UpdateGenerateButton();
            interpretationPresetCombo.IsEnabled = selectionEnabled;
            interpretationModelCombo.IsEnabled = selectionEnabled;
            interpretationReasoningCombo.IsEnabled = selectionEnabled
                && interpretationModelCombo.SelectedItem as string != "summary";
            omitScientificGuidance.IsEnabled = selectionEnabled && omitScientificGuidance.IsVisible;
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
                retryServiceStatus.IsVisible = !serviceAllowsGeneration || interpretationAccessCheckFailed;
                UpdateGenerateButton();
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                serviceAllowsGeneration = true;
                serviceStatus.Text = "Service availability could not be verified. You may try generation manually.";
                retryServiceStatus.IsVisible = true;
                UpdateGenerateButton();
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
