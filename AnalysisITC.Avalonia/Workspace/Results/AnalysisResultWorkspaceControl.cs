using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Avalonia.Printing;
using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Avalonia.Units;
using AnalysisITC.Platform;
using static AnalysisITC.Avalonia.Workspace.WorkspaceControlBuilder;

namespace AnalysisITC.Avalonia.Results
{
    public sealed class AnalysisResultWorkspaceControl : UserControl
    {
        static readonly string[] UncertaintyStyleNames = { "Automatic", "Standard deviation", "95% confidence interval", "SD + 95% CI" };
        static readonly string[] SaltModeNames = { "Affinity vs Salt", "Debye-Huckel", "Counter Ion Release" };
        static ResultAnalysisViewMode sessionViewMode = ResultAnalysisViewMode.Summary;

        readonly ResultParameterGraphControl graph = new ResultParameterGraphControl();
        readonly ResultCorrelationGraphControl correlationGraph = new ResultCorrelationGraphControl();
        readonly ResultDependenceGraphControl dependenceGraph = new ResultDependenceGraphControl();
        readonly IntegratedHeatsGraphControl selectedFitGraph = new IntegratedHeatsGraphControl
        {
            IsReadOnly = true,
            ShowFit = true,
            ShowResiduals = true,
            ShowErrorBars = true,
            ShowConfidenceBand = true,
            ShowPointLabels = false,
            ShowFitParameters = false,
            ShowExcludedPoints = true,
            ScaleToIncludedPoints = true,
            DrawWithOffset = false,
            EmptyStateTitle = "No experiment selected",
            EmptyStateMessage = "Select an experiment in the result table or an overview graph to inspect its saved fit."
        };
        readonly ContentControl graphHost = new ContentControl();
        readonly StackPanel tableHost = new StackPanel { Spacing = 0 };
        readonly StackPanel summaryPanel = WorkspaceControlBuilder.InspectorPanel();
        readonly StackPanel experimentsPanel = WorkspaceControlBuilder.InspectorPanel();
        readonly StackPanel modelPanel = WorkspaceControlBuilder.InspectorPanel();
        readonly StackPanel analysisPanel = WorkspaceControlBuilder.InspectorPanel();
        readonly ComboBox temperatureUnitCombo = WorkspaceControlBuilder.Combo(new[] { "Celsius", "Kelvin" }, 0, 170);
        readonly ComboBox uncertaintyStyleCombo = WorkspaceControlBuilder.Combo(UncertaintyStyleNames, 0, 170);
        readonly TextBox evaluationTemperatureBox = WorkspaceControlBuilder.TextBox("");
        readonly StackPanel evaluationRowsPanel = WorkspaceControlBuilder.VerticalGroup();
        readonly Border displaySection;
        readonly Border parameterEvaluationSection;
        readonly Border resultViewSection;
        readonly ComboBox resultViewCombo = new ComboBox { MinWidth = 170, HorizontalAlignment = HorizontalAlignment.Stretch };

        AnalysisResult? result;
        ResultAnalysisViewMode activeViewMode = ResultAnalysisViewMode.Summary;
        readonly List<ResultAnalysisViewMode> availableViewModes = new List<ResultAnalysisViewMode>();
        bool hasAppliedSessionView;
        bool sessionViewWasUnavailable;
        bool isUpdatingResultViewCombo;
        FTSRMethod.SRFoldedMode selectedSrFoldedMode = FTSRMethod.SRFoldedMode.Glob;
        FTSRMethod.SRTempMode selectedSrTemperatureMode = FTSRMethod.SRTempMode.IsoEntropicPoint;
        ElectrostaticsAnalysis.DissocFitMode selectedSaltMode = ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel;
        bool isUpdatingSelection;
        bool isUpdatingEvaluationControls;
        bool isUpdatingResult;
        bool isRunningAdvancedAnalysis;
        bool evaluationUseKelvin;
        bool isUpdatingDisplayControls;
        BootstrapCorrelationResult? correlationResult;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? ResultUpdated;
        public event EventHandler? ActiveGraphChanged;

        public AnalysisResultWorkspaceControl()
        {
            graphHost.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            graphHost.VerticalContentAlignment = VerticalAlignment.Stretch;
            displaySection = Section("Display", new Control[]
            {
                Labeled("Errors", uncertaintyStyleCombo),
                Labeled("Temperature", temperatureUnitCombo)
            });
            parameterEvaluationSection = BuildParameterEvaluationSection();
            resultViewSection = BuildResultViewSection();

            SyncDisplayControls();
            resultViewCombo.SelectionChanged += (_, _) => ChangeResultViewModeFromCombo(resultViewCombo);
            BuildLayout();
            WireEvents();
            Refresh();
        }

        public AnalysisResult? Result
        {
            get => result;
            set
            {
                if (ReferenceEquals(result, value)) return;

                result = value;
                graph.Result = value;
                dependenceGraph.Result = value;
                DataManager.ClearResultSolutionSelection();
                RefreshCorrelationData();
                hasAppliedSessionView = false;
                sessionViewWasUnavailable = false;
                ResetEvaluationTemperature();
                Refresh();
                ActiveGraphChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void FitToData()
        {
            if (activeViewMode == ResultAnalysisViewMode.Summary)
                graph.FitToData();
            else if (activeViewMode == ResultAnalysisViewMode.Fit)
                selectedFitGraph.FitToData();
            else if (activeViewMode == ResultAnalysisViewMode.Correlation)
                correlationGraph.InvalidateVisual();
            else
                dependenceGraph.FitToData();
        }

        public ResultAnalysisViewMode ActiveViewMode => activeViewMode;
        public IReadOnlyList<ResultAnalysisViewMode> AvailableViewModes => availableViewModes;
        public ComboBox ResultViewCombo => resultViewCombo;

        internal Control? GraphHostContentForTesting => graphHost.Content as Control;
        internal ResultCorrelationGraphControl CorrelationGraphForTesting => correlationGraph;
        internal StackPanel SummaryPanelForTesting => summaryPanel;
        internal StackPanel ExperimentsPanelForTesting => experimentsPanel;
        internal StackPanel ParameterTableHostForTesting => tableHost;

        public static string ViewModeId(ResultAnalysisViewMode mode) => ViewId(mode);

        internal static void ResetSessionViewForTesting()
            => sessionViewMode = ResultAnalysisViewMode.Summary;

        public bool IsResultViewModeAvailable(ResultAnalysisViewMode mode)
        {
            return availableViewModes.Contains(mode);
        }

        public void SetResultViewMode(ResultAnalysisViewMode mode)
        {
            RefreshAvailableViewModes();
            if (!availableViewModes.Contains(mode)) return;

            activeViewMode = mode;
            sessionViewMode = mode;
            SyncModeCombo();
            RefreshGraphMode();
            RefreshAnalysis();
            ActiveGraphChanged?.Invoke(this, EventArgs.Empty);
        }

        internal bool TryGetPrintTarget(out GraphPrintTarget? target)
        {
            target = null;
            // Hover presentation is transient and must never leak into a printed graph.
            correlationGraph.ClearHoverState();
            if (result == null) return false;

            var title = string.IsNullOrWhiteSpace(result.Name) ? "Analysis Result" : result.Name;
            switch (activeViewMode)
            {
                case ResultAnalysisViewMode.Summary when graph.HasPrintableData:
                    target = GraphPrintTarget.FromVisual($"{title} – Summary", graph);
                    return true;
                case ResultAnalysisViewMode.Fit:
                    var selected = DataManager.SelectedResultSolution;
                    if (selected?.Data?.Processor?.IntegrationCompleted == true
                        && result.Solution.Solutions.Contains(selected))
                    {
                        target = GraphPrintTarget.FromVisual($"{title} – Selected Fit", selectedFitGraph);
                        return true;
                    }
                    return false;
                case ResultAnalysisViewMode.Correlation when correlationGraph.HasPrintableData:
                    target = GraphPrintTarget.FromVisual($"{title} – Correlation", correlationGraph);
                    return true;
                default:
                    if (dependenceGraph.HasPrintableData)
                    {
                        target = GraphPrintTarget.FromVisual($"{title} – {ModeTitle(activeViewMode)}", dependenceGraph);
                        return true;
                    }
                    return false;
            }
        }

        public async Task RunActiveAdvancedAnalysisAsync()
        {
            switch (activeViewMode)
            {
                case ResultAnalysisViewMode.Temperature:
                    await RunTemperatureAnalysisAsync();
                    break;
                case ResultAnalysisViewMode.Salt:
                    await RunSaltAnalysisAsync();
                    break;
                case ResultAnalysisViewMode.Protonation:
                    await RunProtonationAnalysisAsync();
                    break;
            }
        }

        public void SetTemperatureDisplay(bool kelvin)
        {
            temperatureUnitCombo.SelectedIndex = kelvin ? 1 : 0;
        }

        public void SetUncertaintyDisplay(UncertaintyDisplayStyle style)
        {
            AppSettings.UncertaintyDisplayStyle = style;
            AppSettings.Save();
            RefreshTable();
            RefreshParameterEvaluation();
            RefreshAnalysis();
            SyncDisplayControls();
        }

        public async Task UpdateResultAsync()
        {
            if (result == null || isUpdatingResult) return;

            var options = AnalysisResultUpdateOptions.StoredSettings;
            if (AnalysisResultUpdater.CanOverrideBootstrapIterations(result))
            {
                options = await PlatformServices.AnalysisResultUpdatePromptService
                    .ChooseOptionsAsync(result);
                if (options == null) return;
            }

            try
            {
                isUpdatingResult = true;
                RefreshSummary();
                StatusChanged?.Invoke(this, "Updating analysis result...");

                var convergence = await AnalysisResultUpdater.UpdateAsync(result, options);

                Refresh();
                ResultUpdated?.Invoke(this, EventArgs.Empty);
                StatusChanged?.Invoke(this, $"{convergence.Algorithm.GetProperties().ShortName} | RMSD = {convergence.Loss:G4}");

                var boundaryWarning = ParameterBoundaryWarningFormatter.Format(convergence.ParameterBoundaryContacts);
                if (!string.IsNullOrWhiteSpace(boundaryWarning))
                    StatusBarManager.QueueStatus(boundaryWarning, 5000);
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
                StatusChanged?.Invoke(this, $"Result update failed: {ex.Message}");
                RefreshSummary();
            }
            finally
            {
                isUpdatingResult = false;
                RefreshSummary();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            DataManager.ResultSolutionSelectionDidChange += OnResultSolutionSelectionChanged;
            ResultAnalysisController.AnalysisStarted += OnAdvancedAnalysisStarted;
            ResultAnalysisController.IterationFinished += OnAdvancedAnalysisIterationFinished;
            ResultAnalysisController.AnalysisFinished += OnAdvancedAnalysisFinished;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            DataManager.ResultSolutionSelectionDidChange -= OnResultSolutionSelectionChanged;
            ResultAnalysisController.AnalysisStarted -= OnAdvancedAnalysisStarted;
            ResultAnalysisController.IterationFinished -= OnAdvancedAnalysisIterationFinished;
            ResultAnalysisController.AnalysisFinished -= OnAdvancedAnalysisFinished;
            base.OnDetachedFromVisualTree(e);
        }

        void BuildLayout()
        {
            var main = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                RowSpacing = WorkspaceControlBuilder.InspectorGap
            };

            graphHost.Content = graph;
            var graphBorder = WorkspaceControlBuilder.ContentBorder(graphHost);
            Grid.SetRow(graphBorder, 0);

            var tableBorder = WorkspaceControlBuilder.ContentBorder(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = tableHost
            });
            tableBorder.MinHeight = 190;
            tableBorder.MaxHeight = 270;
            Grid.SetRow(tableBorder, 1);

            main.Children.Add(graphBorder);
            main.Children.Add(tableBorder);

            var inspector = WorkspaceControlBuilder.Inspector(
                InspectorTab("Summary", summaryPanel),
                InspectorTab("Analysis", analysisPanel),
                InspectorTab("Experiments", experimentsPanel),
                InspectorTab("Model", modelPanel));

            Content = WorkspaceControlBuilder.Workspace(main, inspector);
        }

        void WireEvents()
        {
            temperatureUnitCombo.SelectionChanged += (_, _) =>
            {
                if (isUpdatingDisplayControls) return;
                ChangeTemperatureDisplay();
            };
            uncertaintyStyleCombo.SelectionChanged += (_, _) => ChangeUncertaintyStyle();
            evaluationTemperatureBox.LostFocus += (_, _) => RefreshParameterEvaluation();
            evaluationTemperatureBox.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;

                RefreshParameterEvaluation();
                e.Handled = true;
            };
        }

        void RefreshAvailableViewModes()
        {
            availableViewModes.Clear();
            availableViewModes.Add(ResultAnalysisViewMode.Fit);
            availableViewModes.Add(ResultAnalysisViewMode.Correlation);
            availableViewModes.Add(ResultAnalysisViewMode.Summary);

            if (result?.IsTemperatureDependenceEnabled == true)
                availableViewModes.Add(ResultAnalysisViewMode.Temperature);

            if (result?.IsAdvancedAnalysisAvailable == true)
            {
                if (result.IsElectrostaticsAnalysisDependenceEnabled) availableViewModes.Add(ResultAnalysisViewMode.Salt);
                if (result.IsProtonationAnalysisEnabled) availableViewModes.Add(ResultAnalysisViewMode.Protonation);
            }

        }

        void SyncModeCombo()
        {
            isUpdatingResultViewCombo = true;
            try
            {
                resultViewCombo.Items.Clear();
                foreach (var mode in availableViewModes.Take(2))
                    resultViewCombo.Items.Add(CreateModeItem(mode));
                // A real Separator is part of the item list even when no advanced analyses
                // are currently available; this keeps keyboard navigation and automation
                // stable across result types.
                resultViewCombo.Items.Add(new Separator());
                foreach (var mode in availableViewModes.Skip(2))
                    resultViewCombo.Items.Add(CreateModeItem(mode));

                var item = resultViewCombo.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(candidate => candidate.Tag is string tag && tag == ViewId(activeViewMode));
                resultViewCombo.SelectedItem = item;
            }
            finally { isUpdatingResultViewCombo = false; }
        }

        static ComboBoxItem CreateModeItem(ResultAnalysisViewMode mode)
            => new ComboBoxItem { Content = ModeTitle(mode), Tag = ViewId(mode) };

        void ChangeResultViewModeFromCombo(ComboBox combo)
        {
            if (isUpdatingResultViewCombo || combo.SelectedItem is not ComboBoxItem item || item.Tag is not string id) return;
            var selected = ModeFromId(id);
            if (!selected.HasValue || !availableViewModes.Contains(selected.Value)) return;
            activeViewMode = selected.Value;
            sessionViewMode = selected.Value;
            RefreshGraphMode();
            RefreshAnalysis();
            ActiveGraphChanged?.Invoke(this, EventArgs.Empty);
        }

        void RefreshGraphMode()
        {
            correlationGraph.ClearHoverState();
            if (activeViewMode == ResultAnalysisViewMode.Summary)
            {
                graphHost.Content = graph;
                graph.Result = result;
                graph.InvalidateVisual();
                return;
            }

            if (activeViewMode == ResultAnalysisViewMode.Fit)
            {
                graphHost.Content = selectedFitGraph;
                RefreshSelectedFitGraph();
                return;
            }

            if (activeViewMode == ResultAnalysisViewMode.Correlation)
            {
                RefreshCorrelationData();
                graphHost.Content = correlationGraph;
                correlationGraph.HorizontalAlignment = HorizontalAlignment.Stretch;
                correlationGraph.VerticalAlignment = VerticalAlignment.Stretch;
                return;
            }

            graphHost.Content = dependenceGraph;
            dependenceGraph.Result = result;
            dependenceGraph.Mode = activeViewMode;
            dependenceGraph.SaltMode = selectedSaltMode;
            dependenceGraph.Rebuild();
        }

        void ChangeSaltMode()
        {
            dependenceGraph.SaltMode = selectedSaltMode;
            dependenceGraph.Rebuild();
            RefreshAnalysis();
        }

        void OnResultSolutionSelectionChanged(object? sender, SolutionInterface? e)
        {
            if (isUpdatingSelection) return;

            RefreshSolutionSelectionPresentation();
        }

        void RefreshCorrelationData()
        {
            if (result == null)
            {
                correlationResult = null;
                correlationGraph.Clear("No analysis result selected.");
                return;
            }

            try
            {
                var selected = DataManager.SelectedResultSolution;
                var members = result.Solution?.Solutions ?? new List<SolutionInterface>();
                var analyzer = new BootstrapCorrelationAnalyzer();
                if (members.Count == 1)
                    correlationResult = analyzer.Analyze(members[0]);
                else if (selected != null && result.Solution?.Model?.Models != null && result.Solution.Model.Models.Count > 1)
                    correlationResult = analyzer.Analyze(result.Solution, selected);
                else
                    correlationResult = analyzer.Analyze(result);

                var selectedCount = selected == null ? members.Count : 1;
                var selectedLabel = selected?.Data?.Name ?? selected?.Data?.FileName;
                correlationGraph.SetCorrelationResult(correlationResult, selectedCount, selectedLabel, members.Count > 1);
            }
            catch (Exception ex)
            {
                correlationResult = null;
                correlationGraph.Clear("Correlation is unavailable: " + ex.Message);
            }
        }

        void RefreshSolutionSelectionPresentation()
        {
            RefreshTable();
            RefreshCorrelationData();
            RefreshSelectedFitGraph();
            graph.InvalidateVisual();
            dependenceGraph.InvalidateVisual();
            if (activeViewMode == ResultAnalysisViewMode.Fit || activeViewMode == ResultAnalysisViewMode.Correlation)
                RefreshAnalysis();
            ActiveGraphChanged?.Invoke(this, EventArgs.Empty);
        }

        void RefreshSelectedFitGraph()
        {
            var selected = DataManager.SelectedResultSolution;
            if (selected != null && result?.Solution?.Solutions?.Contains(selected) != true)
                selected = null;

            selectedFitGraph.SetSource(selected?.Data, selected);
        }

        void OnAdvancedAnalysisStarted(object? sender, TerminationFlag e)
        {
            isRunningAdvancedAnalysis = true;
            RefreshAnalysis();
            StatusBarManager.SetStatus("Advanced analysis started...", 0, priority: 1);
            StatusChanged?.Invoke(this, "Advanced analysis started...");
        }

        void OnAdvancedAnalysisIterationFinished(object? sender, Tuple<int, int, float, string> e)
        {
            var status = string.IsNullOrWhiteSpace(e.Item4)
                ? $"Advanced analysis {100 * e.Item3:F0}%"
                : $"{e.Item4}: {100 * e.Item3:F0}%";
            StatusBarManager.SetStatus(status, 1000, priority: 1);
            StatusChanged?.Invoke(this, status);
        }

        void OnAdvancedAnalysisFinished(object? sender, Tuple<int, TimeSpan> e)
        {
            isRunningAdvancedAnalysis = false;
            dependenceGraph.Rebuild();
            RefreshAnalysis();
            ActiveGraphChanged?.Invoke(this, EventArgs.Empty);
            var status = $"Advanced analysis completed ({e.Item1} iterations).";
            StatusBarManager.SetStatus(status, 5000);
            StatusChanged?.Invoke(this, status);
        }

        public void Refresh()
        {
            RefreshAvailableViewModes();
            if (!hasAppliedSessionView)
            {
                activeViewMode = availableViewModes.Contains(sessionViewMode)
                    ? sessionViewMode
                    : ResultAnalysisViewMode.Summary;
                sessionViewWasUnavailable = !availableViewModes.Contains(sessionViewMode);
                hasAppliedSessionView = true;
            }
            else if (sessionViewWasUnavailable)
            {
                if (availableViewModes.Contains(sessionViewMode))
                {
                    activeViewMode = sessionViewMode;
                    sessionViewWasUnavailable = false;
                }
            }
            if (!availableViewModes.Contains(activeViewMode))
                activeViewMode = ResultAnalysisViewMode.Summary;
            SyncModeCombo();
            RefreshGraphMode();
            RefreshSummary();
            RefreshExperiments();
            RefreshModel();
            RefreshAnalysis();
            RefreshTable();
            FitToData();
        }

        void RefreshSummary()
        {
            summaryPanel.Children.Clear();
            SyncDisplayControls();

            if (result == null)
            {
                summaryPanel.Children.Add(Text("No analysis result selected."));
                return;
            }

            var solution = result.Solution;
            var convergence = solution.Convergence;
            var report = result.ValidityReport;
            var updateButton = new Button
            {
                Content = isUpdatingResult ? "Updating..." : "Update Result",
                //MinHeight = 30,
                Padding = new Thickness(8, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = !isUpdatingResult && result.Solution?.Model != null
            };
            updateButton.Click += async (_, _) => await UpdateResultAsync();

            summaryPanel.Children.Add(BuildValiditySection(report));

            summaryPanel.Children.Add(Section("Result", new Control[]
            {
                Pair("Name", result.Name),
                Pair("Model", solution.SolutionName),
                Pair("Experiments", solution.Solutions.Count.ToString(CultureInfo.CurrentCulture)),
                Pair("RMSD", solution.Loss.ToString("G4", CultureInfo.CurrentCulture))
            }));

            summaryPanel.Children.Add(Section("Solver", new Control[]
            {
                Pair("Algorithm", convergence?.Algorithm.GetProperties().Name ?? ""),
                Pair("Iterations", convergence?.Iterations.ToString(CultureInfo.CurrentCulture) ?? ""),
                Pair("Fitting", solution.UseWeightedFitting ? "Weighted injection errors" : "Unweighted"),
                Pair("Errors", solution.ErrorEstimationMethod.Description()),
                Pair("Bootstrap", solution.BootstrapIterations.ToString(CultureInfo.CurrentCulture))
            }));

            summaryPanel.Children.Add(displaySection);
            summaryPanel.Children.Add(Section("Actions", new Control[]
            {
                updateButton
            }));
            RefreshParameterEvaluation();
        }

        Border BuildParameterEvaluationSection()
        {
            return WorkspaceControlBuilder.Section(
                "Parameter Evaluation",
                WorkspaceControlBuilder.Labeled("Temperature", evaluationTemperatureBox),
                evaluationRowsPanel);
        }

        void ResetEvaluationTemperature()
        {
            isUpdatingEvaluationControls = true;
            try
            {
                evaluationUseKelvin = UseKelvin;

                if (result == null)
                    evaluationTemperatureBox.Text = "";
                else
                    SetEvaluationTemperatureText(AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result));
            }
            finally
            {
                isUpdatingEvaluationControls = false;
            }
        }

        void ChangeTemperatureDisplay()
        {
            if (isUpdatingEvaluationControls) return;

            var temperatureCelsius = TryReadEvaluationTemperatureCelsius(out var parsedTemperature)
                ? parsedTemperature
                : result == null
                    ? 25.0
                    : AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result);

            evaluationUseKelvin = UseKelvin;
            SetEvaluationTemperatureText(temperatureCelsius);
            RefreshTable();
            graph.InvalidateVisual();
            dependenceGraph.Rebuild();
            RefreshParameterEvaluation();
        }

        void RefreshParameterEvaluation()
        {
            evaluationRowsPanel.Children.Clear();

            if (result == null)
            {
                evaluationRowsPanel.Children.Add(WorkspaceControlBuilder.Text("No analysis result selected."));
                return;
            }

            if (!TryReadEvaluationTemperatureCelsius(out var temperatureCelsius))
            {
                evaluationRowsPanel.Children.Add(WorkspaceControlBuilder.Text("Invalid evaluation temperature."));
                return;
            }

            if (temperatureCelsius < -273.15)
            {
                temperatureCelsius = -273.15;
                SetEvaluationTemperatureText(temperatureCelsius);
            }

            var evaluation = AnalysisResultParameterEvaluator.Evaluate(
                result,
                temperatureCelsius,
                AppSettings.EnergyUnitFamily,
                energyUnitOverride: null,
                AppSettings.UncertaintyDisplayStyle);

            if (!evaluation.IsAvailable)
            {
                evaluationRowsPanel.Children.Add(WorkspaceControlBuilder.Text(evaluation.Message));
                return;
            }

            foreach (var row in evaluation.Rows)
            {
                var pair = ParameterPair(row.Label, row.Value);
                if (!string.IsNullOrWhiteSpace(row.Tooltip))
                    ToolTip.SetTip(pair, row.Tooltip);
                evaluationRowsPanel.Children.Add(pair);
            }
        }

        void RefreshExperiments()
        {
            experimentsPanel.Children.Clear();

            if (result?.Solution?.Solutions == null || result.Solution.Solutions.Count == 0)
            {
                experimentsPanel.Children.Add(Text("No experiments are included."));
                return;
            }

            foreach (var solution in result.Solution.Solutions)
            {
                var data = solution.Data;
                var experimentName = Header(data?.Name ?? "Experiment");
                experimentName.TextWrapping = TextWrapping.Wrap;

                var rows = new List<Control>
                {
                    Pair("Date", data?.UIShortDateWithTime ?? ""),
                    Pair("Temperature", data == null ? "" : $"{data.MeasuredTemperature:G3} °C"),
                    Pair(
                        "Status",
                        solution.IsValid ? "Valid solution" : "Invalid solution",
                        solution.IsValid ? AppTheme.StatusValid : AppTheme.StatusError)
                };
                foreach (var warning in ParameterBoundaryWarningFormatter.MessagesFor(
                    solution,
                    result.Solution.ErrorEstimationMethod))
                {
                    var warningText = Text(warning);
                    AppTheme.Bind(warningText, TextBlock.ForegroundProperty, AppTheme.StatusWarning);
                    rows.Add(warningText);
                }

                experimentsPanel.Children.Add(Section(experimentName, rows.ToArray()));
            }
        }

        void RefreshModel()
        {
            modelPanel.Children.Clear();

            if (result == null)
            {
                modelPanel.Children.Add(Text("No model selected."));
                return;
            }

            var options = result.Solution.Model.ModelOptions;
            if (options != null && options.Count > 0)
            {
                modelPanel.Children.Add(Section("Model options", options
                    .Select(option => Pair(OptionName(option.Key, option.Value), OptionValue(option.Key, option.Value), labelContainsMarkdown: true))
                    .Cast<Control>()
                    .ToArray()));
            }
            else
            {
                modelPanel.Children.Add(Section("Model options", new Control[] { Text("None") }));
            }

            var constraints = result.Solution.Model.Parameters.Constraints;
            var activeConstraints = constraints.Where(constraint => constraint.Value != VariableConstraint.None).ToList();
            if (activeConstraints.Count == 0)
            {
                modelPanel.Children.Add(Section("Constraints", new Control[] { Text("None") }));
            }
            else
            {
                modelPanel.Children.Add(Section("Constraints", activeConstraints
                    .Select(constraint => Pair(constraint.Key.GetEnumDescription(), constraint.Value.GetEnumDescription()))
                    .Cast<Control>()
                    .ToArray()));
            }
        }

        void RefreshAnalysis()
        {
            analysisPanel.Children.Clear();

            if (result == null)
            {
                analysisPanel.Children.Add(Text("No analysis result selected."));
                return;
            }

            analysisPanel.Children.Add(resultViewSection);
            if (activeViewMode != ResultAnalysisViewMode.Correlation)
                analysisPanel.Children.Add(parameterEvaluationSection);

            if (activeViewMode == ResultAnalysisViewMode.Fit)
            {
                var selected = DataManager.SelectedResultSolution;
                var selectionText = selected != null && result.Solution.Solutions.Contains(selected)
                    ? selected.Data?.Name ?? "Selected experiment"
                    : "Select an experiment in the table or an overview graph.";
                analysisPanel.Children.Add(Section("Selected Fit", new Control[] { Text(selectionText) }));
                return;
            }

            if (activeViewMode == ResultAnalysisViewMode.Correlation)
            {
                RefreshCorrelationAnalysis();
                return;
            }

            if (!result.IsAdvancedAnalysisAvailable
                && activeViewMode != ResultAnalysisViewMode.Summary
                && activeViewMode != ResultAnalysisViewMode.Temperature)
            {
                analysisPanel.Children.Add(Section("Advanced Analysis", new Control[]
                {
                    Text("Unavailable")
                }));
                return;
            }

            switch (activeViewMode)
            {
                case ResultAnalysisViewMode.Summary:
                    analysisPanel.Children.Add(Section("Advanced Analysis", new Control[]
                    {
                        Text(result.IsAdvancedAnalysisAvailable
                            ? "Select Temperature, Salt, or Protonation to run an advanced result analysis."
                            : "Unavailable")
                    }));
                    analysisPanel.Children.Add(BuildAvailabilitySection());
                    break;
                case ResultAnalysisViewMode.Temperature:
                    RefreshTemperatureAnalysis();
                    break;
                case ResultAnalysisViewMode.Salt:
                    RefreshSaltAnalysis();
                    break;
                case ResultAnalysisViewMode.Protonation:
                    RefreshProtonationAnalysis();
                    break;
            }
        }

        Border BuildResultViewSection()
        {
            return Section("View", new Control[]
            {
                Labeled("Result", resultViewCombo)
            });
        }

        Border BuildAvailabilitySection()
        {
            return Section("Available Analyses", new Control[]
            {
                Pair("Temperature presentation", result?.IsTemperatureDependenceEnabled == true ? "Available" : "Unavailable"),
                Pair("Spolar Record method", result?.IsSpolarRecordAnalysisEnabled == true ? "Available" : "Unavailable"),
                Pair("Electrostatics", result?.IsElectrostaticsAnalysisDependenceEnabled == true ? "Available" : "Unavailable"),
                Pair("Protonation", result?.IsProtonationAnalysisEnabled == true ? "Available" : "Unavailable")
            });
        }

        void RefreshCorrelationAnalysis()
        {
            var lines = new List<Control>();
            if (result == null)
            {
                lines.Add(Text("No analysis result selected."));
            }
            else
            {
                lines.Add(Pair("Method", string.IsNullOrWhiteSpace(correlationGraph.Method) ? "Pearson" : correlationGraph.Method));
                lines.Add(Pair("Scope", string.IsNullOrWhiteSpace(correlationGraph.Scope) ? "Global" : correlationGraph.Scope));
                lines.Add(Pair("Selected", correlationGraph.SelectedLabel));
                lines.Add(Pair("Used", (correlationGraph.UsedCount > 0 ? correlationGraph.UsedCount : correlationGraph.Count).ToString(CultureInfo.CurrentCulture)));
                lines.Add(Pair("Omitted", correlationGraph.OmittedCount.ToString(CultureInfo.CurrentCulture)));
                if (!correlationGraph.HasPrintableData)
                    lines.Add(WarnText(correlationResult?.Availability?.Reason ?? "Correlation is unavailable for this result."));
                if (correlationGraph.UnlockedParameters)
                    lines.Add(WarnText("* Locked in the primary fit; unlocked during bootstrap."));
                if (correlationGraph.RankWarning)
                    lines.Add(WarnText("Warning: parameter covariance rank is deficient."));
            }
            analysisPanel.Children.Add(Section("Correlation", lines.ToArray()));
        }

        static TextBlock WarnText(string text)
        {
            var control = Text(text);
            AppTheme.Bind(control, TextBlock.ForegroundProperty, AppTheme.StatusWarning);
            return control;
        }

        void RefreshTemperatureAnalysis()
        {
            if (result?.SpolarRecordAnalysis == null)
            {
                analysisPanel.Children.Add(Section("Spolar Record method", new Control[]
                {
                    Text("Unavailable")
                }));
                return;
            }

            var runButton = WorkspaceControlBuilder.Button(isRunningAdvancedAnalysis ? "Running..." : "Run Analysis", 120);
            runButton.IsEnabled = !isRunningAdvancedAnalysis;
            runButton.Click += async (_, _) => await RunTemperatureAnalysisAsync();
            var foldedModeCombo = WorkspaceControlBuilder.Combo(new[] { "Globular", "ID interaction" }, selectedSrFoldedMode == FTSRMethod.SRFoldedMode.ID ? 1 : 0, 170);
            foldedModeCombo.SelectionChanged += (_, _) =>
            {
                selectedSrFoldedMode = foldedModeCombo.SelectedIndex == 1
                    ? FTSRMethod.SRFoldedMode.ID
                    : FTSRMethod.SRFoldedMode.Glob;
                RefreshAnalysis();
            };
            var temperatureModeCombo = WorkspaceControlBuilder.Combo(new[] { "Isoentropic point", "Mean temperature", "Reference temperature" }, selectedSrTemperatureMode switch
            {
                FTSRMethod.SRTempMode.MeanTemperature => 1,
                FTSRMethod.SRTempMode.ReferenceTemperature => 2,
                _ => 0
            }, 170);
            temperatureModeCombo.SelectionChanged += (_, _) =>
            {
                selectedSrTemperatureMode = temperatureModeCombo.SelectedIndex switch
                {
                    1 => FTSRMethod.SRTempMode.MeanTemperature,
                    2 => FTSRMethod.SRTempMode.ReferenceTemperature,
                    _ => FTSRMethod.SRTempMode.IsoEntropicPoint
                };
                RefreshAnalysis();
            };

            analysisPanel.Children.Add(Section("Temperature", new Control[]
            {
                Labeled("Folded mode", foldedModeCombo),
                Labeled("Temp mode", temperatureModeCombo),
                WorkspaceControlBuilder.Row(runButton)
            }));

            var analysis = result.SpolarRecordAnalysis;
            if (analysis.Result == null)
            {
                analysisPanel.Children.Add(Section("Output", new Control[] { Text("Run the analysis to calculate Spolar Record values.") }));
                return;
            }

            var evaluationTemperature = analysis.EvalutationTemperature(false);
            analysisPanel.Children.Add(Section("Output", new Control[]
            {
                Pair("Mode", analysis.FoldedMode switch
                {
                    FTSRMethod.SRFoldedMode.ID => "ID interaction",
                    FTSRMethod.SRFoldedMode.Intermediate => "Intermediate",
                    _ => "Globular"
                }),
                Pair("Reference T", analysis.Result.ReferenceTemperature.AsNumber() + " °C"),
                Pair("Hydration", new Energy(analysis.Result.HydrationContribution(evaluationTemperature)).ToFormattedString(EnergyDisplay.ResultMolarUnit(result), permole: true)),
                Pair("Conformation", new Energy(analysis.Result.ConformationalContribution(evaluationTemperature)).ToFormattedString(EnergyDisplay.ResultMolarUnit(result), permole: true)),
                Pair("Residues", analysis.Result.Rvalue.AsNumber())
            }));
        }

        void RefreshSaltAnalysis()
        {
            if (result?.ElectrostaticsAnalysis == null)
            {
                analysisPanel.Children.Add(Section("Salt", new Control[] { Text("Unavailable") }));
                return;
            }

            var runButton = WorkspaceControlBuilder.Button(isRunningAdvancedAnalysis ? "Running..." : "Run Analysis", 120);
            runButton.IsEnabled = !isRunningAdvancedAnalysis;
            runButton.Click += async (_, _) => await RunSaltAnalysisAsync();
            var saltModeCombo = WorkspaceControlBuilder.Combo(SaltModeNames, selectedSaltMode switch
            {
                ElectrostaticsAnalysis.DissocFitMode.AffinityVsSalt => 0,
                ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease => 2,
                _ => 1
            }, 170);
            saltModeCombo.SelectionChanged += (_, _) =>
            {
                selectedSaltMode = saltModeCombo.SelectedIndex switch
                {
                    0 => ElectrostaticsAnalysis.DissocFitMode.AffinityVsSalt,
                    2 => ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease,
                    _ => ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel
                };
                ChangeSaltMode();
            };

            analysisPanel.Children.Add(Section("Salt", new Control[]
            {
                Labeled("Graph mode", saltModeCombo),
                WorkspaceControlBuilder.Row(runButton)
            }));

            var analysis = result.ElectrostaticsAnalysis;
            if (!analysis.Calculated)
            {
                analysisPanel.Children.Add(Section("Output", new Control[] { Text("Run the analysis to calculate electrostatic parameters.") }));
                return;
            }

            analysisPanel.Children.Add(Section("Output", new Control[]
            {
                Pair("*K*{d} at *I* = 0 M", analysis.Kd0.AsFormattedConcentration(withunit: true), labelContainsMarkdown: true),
                Pair("Counter ion", analysis.CounterIonRelease.AsNumber()),
            }));
        }

        void RefreshProtonationAnalysis()
        {
            if (result?.ProtonationAnalysis == null)
            {
                analysisPanel.Children.Add(Section("Protonation", new Control[] { Text("Unavailable") }));
                return;
            }

            var runButton = WorkspaceControlBuilder.Button(isRunningAdvancedAnalysis ? "Running..." : "Run Analysis", 120);
            runButton.IsEnabled = !isRunningAdvancedAnalysis;
            runButton.Click += async (_, _) => await RunProtonationAnalysisAsync();

            analysisPanel.Children.Add(Section("Protonation", new Control[]
            {
                WorkspaceControlBuilder.Row(runButton)
            }));

            var analysis = result.ProtonationAnalysis;
            if (analysis.Fit == null)
            {
                analysisPanel.Children.Add(Section("Output", new Control[] { Text("Run the analysis to calculate protonation-corrected binding parameters.") }));
                return;
            }

            var fit = analysis.Fit as LinearFitWithError;
            analysisPanel.Children.Add(Section("Output", new Control[]
            {
                Pair("Protons", fit == null ? analysis.ProtonationChange.AsNumber() : (-1 * fit.Slope).AsNumber()),
                Pair("Binding H", fit == null
                    ? analysis.BindingEnthalpy.ToFormattedString(EnergyDisplay.ResultMolarUnit(result), permole: true)
                    : new Energy(fit.Evaluate(0)).ToFormattedString(EnergyDisplay.ResultMolarUnit(result), true, true, false)),
            }));
        }

        async Task RunTemperatureAnalysisAsync()
        {
            if (result?.SpolarRecordAnalysis == null || isRunningAdvancedAnalysis) return;

            result.SpolarRecordAnalysis.FoldedMode = selectedSrFoldedMode;
            result.SpolarRecordAnalysis.TempMode = selectedSrTemperatureMode;
            await result.SpolarRecordAnalysis.PerformAnalysisAsync();
        }

        async Task RunSaltAnalysisAsync()
        {
            if (result?.ElectrostaticsAnalysis == null || isRunningAdvancedAnalysis) return;

            await result.ElectrostaticsAnalysis.PerformAnalysisAsync();
        }

        async Task RunProtonationAnalysisAsync()
        {
            if (result?.ProtonationAnalysis == null || isRunningAdvancedAnalysis) return;

            await result.ProtonationAnalysis.PerformAnalysisAsync();
        }

        void RefreshTable()
        {
            tableHost.Children.Clear();

            if (result == null)
            {
                tableHost.Children.Add(Message("No analysis result selected."));
                return;
            }

            var table = AnalysisResultOverviewTable.Build(
                result,
                AppSettings.EnergyUnitFamily,
                energyUnitOverride: null,
                useKelvin: UseKelvin);
            if (table.Columns.Count == 0 || table.Rows.Count == 0)
            {
                tableHost.Children.Add(Message("No fitted solutions are available."));
                return;
            }

            var grid = new Grid();
            AppTheme.Bind(grid, Panel.BackgroundProperty, AppTheme.PanelBackground);

            foreach (var column in table.Columns)
                grid.ColumnDefinitions.Add(new ColumnDefinition(column.PreferredWidth, GridUnitType.Pixel));

            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int i = 0; i < table.Rows.Count; i++)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                AddTableCell(grid, column.Title, columnIndex, 0, column.Alignment, isHeader: true, isSelected: false, null);
            }

            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                var row = table.Rows[rowIndex];
                var selected = ReferenceEquals(row.Solution, DataManager.SelectedResultSolution);

                for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
                {
                    var column = table.Columns[columnIndex];
                    AddTableCell(grid, row[column.Id], columnIndex, rowIndex + 1, column.Alignment, isHeader: false, selected, row.Solution);
                }
            }

            tableHost.Children.Add(grid);
        }

        Border BuildValiditySection(AnalysisResultValidityReport report)
        {
            var health = result?.Health ?? AnalysisResultHealth.Unknown;
            var color = health switch
            {
                AnalysisResultHealth.Valid => AppTheme.StatusValid,
                AnalysisResultHealth.Warning => AppTheme.StatusWarning,
                AnalysisResultHealth.PartialInvalid => AppTheme.StatusWarning,
                AnalysisResultHealth.Invalid => AppTheme.StatusError,
                _ => AppTheme.StatusWarning
            };

            var title = new TextBlock
            {
                Text = HealthTitle(health),
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            AppTheme.Bind(title, TextBlock.ForegroundProperty, color);

            var lines = new List<Control> { title };

            if (report.Reasons.Count == 0)
            {
                lines.Add(Text(report.Status == AnalysisResultValidity.Valid
                    ? "Cached data matches current data."
                    : "Validity could not be determined."));

                if (health == AnalysisResultHealth.Warning && result?.Solution?.Solutions != null)
                {
                    foreach (var warning in result.Solution.Solutions
                        .SelectMany(solution => ParameterBoundaryWarningFormatter.MessagesFor(
                            solution,
                            result.Solution.ErrorEstimationMethod))
                        .Distinct())
                    {
                        lines.Add(Text(warning));
                    }
                }
            }
            else
            {
                foreach (var reason in report.Reasons)
                    lines.Add(Text(reason));
            }

            return Section("Validity", lines.ToArray());
        }

        void AddTableCell(Grid grid, string text, int column, int row, AnalysisResultColumnAlignment alignment, bool isHeader, bool isSelected, SolutionInterface? solution)
        {
            var textBlock = new TextBlock
            {
                Text = text ?? "",
                Margin = new Thickness(8, 5),
                FontSize = isHeader ? 11 : 12,
                FontWeight = isHeader ? FontWeight.SemiBold : AppTheme.BodyFontWeight,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignmentFor(alignment)
            };
            AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

            var border = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child = textBlock,
                MinHeight = isHeader ? 30 : 28
            };
            AppTheme.Bind(border, Border.BorderBrushProperty, AppTheme.SectionBorder);
            AppTheme.Bind(border, Border.BackgroundProperty, isHeader
                ? AppTheme.TableHeaderBackground
                : isSelected
                    ? AppTheme.SelectionBackground
                    : row % 2 == 0 ? AppTheme.PanelBackground : AppTheme.TableAlternateRow);

            if (!isHeader && solution != null)
            {
                border.Cursor = new Cursor(StandardCursorType.Hand);
                border.PointerPressed += (_, e) =>
                {
                    isUpdatingSelection = true;
                    DataManager.SelectResultSolution(solution);
                    isUpdatingSelection = false;
                    RefreshSolutionSelectionPresentation();
                    StatusChanged?.Invoke(this, solution.Data?.Name ?? "Solution selected");
                    e.Handled = true;
                };
            }

            Grid.SetColumn(border, column);
            Grid.SetRow(border, row);
            grid.Children.Add(border);
        }

        void ChangeUncertaintyStyle()
        {
            if (isUpdatingDisplayControls) return;
            SetUncertaintyDisplay(uncertaintyStyleCombo.SelectedIndex switch
            {
                1 => UncertaintyDisplayStyle.StandardDeviation,
                2 => UncertaintyDisplayStyle.ConfidenceInterval,
                3 => UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
                _ => UncertaintyDisplayStyle.Automatic
            });
        }

        void SyncDisplayControls()
        {
            isUpdatingDisplayControls = true;
            try
            {
                uncertaintyStyleCombo.SelectedIndex = AppSettings.UncertaintyDisplayStyle switch
                {
                    UncertaintyDisplayStyle.StandardDeviation => 1,
                    UncertaintyDisplayStyle.ConfidenceInterval => 2,
                    UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval => 3,
                    _ => 0
                };
            }
            finally
            {
                isUpdatingDisplayControls = false;
            }
        }

        bool TryReadEvaluationTemperatureCelsius(out double temperatureCelsius)
        {
            temperatureCelsius = 0;
            var text = evaluationTemperatureBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return false;

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var displayed) &&
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out displayed))
            {
                return false;
            }

            temperatureCelsius = evaluationUseKelvin ? displayed - 273.15 : displayed;
            return true;
        }

        void SetEvaluationTemperatureText(double temperatureCelsius)
        {
            var displayed = evaluationUseKelvin ? temperatureCelsius + 273.15 : temperatureCelsius;
            evaluationTemperatureBox.Text = displayed.ToString("G6", CultureInfo.CurrentCulture);
        }

        bool UseKelvin => temperatureUnitCombo.SelectedIndex == 1;

        static string ModeTitle(ResultAnalysisViewMode mode)
        {
            return mode switch
            {
                ResultAnalysisViewMode.Fit => "Fit",
                ResultAnalysisViewMode.Correlation => "Correlation",
                ResultAnalysisViewMode.Summary => "Summary",
                ResultAnalysisViewMode.Temperature => "Temperature",
                ResultAnalysisViewMode.Salt => "Salt",
                ResultAnalysisViewMode.Protonation => "Protonation",
                _ => "Summary"
            };
        }

        static string ViewId(ResultAnalysisViewMode mode)
        {
            return mode switch
            {
                ResultAnalysisViewMode.Fit => "fit",
                ResultAnalysisViewMode.Correlation => "correlation",
                ResultAnalysisViewMode.Temperature => "temperature",
                ResultAnalysisViewMode.Salt => "salt",
                ResultAnalysisViewMode.Protonation => "protonation",
                _ => "summary"
            };
        }

        static ResultAnalysisViewMode? ModeFromId(string? id)
        {
            return (id ?? "").Trim().ToLowerInvariant() switch
            {
                "fit" or "selected-fit" => ResultAnalysisViewMode.Fit,
                "correlation" => ResultAnalysisViewMode.Correlation,
                "temperature" => ResultAnalysisViewMode.Temperature,
                "salt" => ResultAnalysisViewMode.Salt,
                "protonation" => ResultAnalysisViewMode.Protonation,
                "summary" or "parameters" => ResultAnalysisViewMode.Summary,
                _ => null
            };
        }

        static string HealthTitle(AnalysisResultHealth status)
        {
            return status switch
            {
                AnalysisResultHealth.Valid => "Analysis is valid",
                AnalysisResultHealth.Warning => "Warning",
                AnalysisResultHealth.PartialInvalid => "Partially invalid",
                AnalysisResultHealth.Invalid => "Invalid",
                _ => "Unknown status"
            };
        }

        static string OptionName(AttributeKey key, ExperimentAttribute option)
        {
            return string.IsNullOrWhiteSpace(option.OptionName)
                ? key.GetEnumDescription()
                : option.OptionName;
        }

        static string OptionValue(AttributeKey key, ExperimentAttribute option)
        {
            return key switch
            {
                AttributeKey.PreboundLigandAffinity => (1.0 / FWEMath.Pow(10.0, option.ParameterValue)).AsConcentration(AppSettings.DefaultConcentrationUnit, withunit: true),
                AttributeKey.PreboundLigandEnthalpy => new Energy(option.ParameterValue).ToFormattedString(
                    EnergyDisplay.Resolve(AppSettings.EnergyUnitFamily, option.ParameterValue.Value),
                    true,
                    true),
                AttributeKey.PreboundLigandConc when option.BoolValue => "From experiment attribute",
                AttributeKey.NumberOfSites1 => StoichiometryOptions.FormatAsTitle(option.DoubleValue > 0 ? option.DoubleValue : option.IntValue),
                AttributeKey.NumberOfSites2 => StoichiometryOptions.FormatAsTitle(option.DoubleValue > 0 ? option.DoubleValue : option.IntValue),
                _ => option.ToString()
            };
        }

        static HorizontalAlignment HorizontalAlignmentFor(AnalysisResultColumnAlignment alignment)
        {
            return alignment switch
            {
                AnalysisResultColumnAlignment.Left => HorizontalAlignment.Left,
                AnalysisResultColumnAlignment.Center => HorizontalAlignment.Center,
                _ => HorizontalAlignment.Right,
            };
        }

        static Border Pair(string label, string value, string? valueBrush = null, bool labelContainsMarkdown = false)
        {
            var panel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions($"Auto,*"),
                ColumnSpacing = RowSpacing,
            };
            var labelText = labelContainsMarkdown
                ? WorkspaceControlBuilder.MarkdownText(label)
                : new TextBlock { Text = label };
            labelText.VerticalAlignment = VerticalAlignment.Top;
            AppTheme.Bind(labelText, TextBlock.ForegroundProperty, AppTheme.MutedText);
            panel.Children.Add(labelText);
            var valueText = new TextBlock
            {
                Text = value ?? "",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right
            };
            AppTheme.Bind(valueText, TextBlock.ForegroundProperty, valueBrush ?? AppTheme.PrimaryText);
            if (valueBrush != null)
                valueText.FontWeight = FontWeight.SemiBold;
            Grid.SetColumn(valueText, 1);
            panel.Children.Add(valueText);

            return new Border
            {
                Margin = WorkspaceControlBuilder.ControlMargin,
                Child = panel
            };
        }

        static Border ParameterPair(string label, string value)
        {
            var panel = new Grid
            {
                //ColumnDefinitions = new ColumnDefinitions($"*,*"),
                RowDefinitions = new RowDefinitions($"*,*"),
                RowSpacing = 0,
            };
            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Top
            };
            AppTheme.Bind(labelText, TextBlock.ForegroundProperty, AppTheme.MutedText);
            panel.Children.Add(labelText);
            var valueText = new TextBlock
            {
                Text = value ?? "",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Left
            };
            AppTheme.Bind(valueText, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
            Grid.SetRow(valueText, 1);
            panel.Children.Add(valueText);

            return new Border
            {
                Margin = WorkspaceControlBuilder.ControlMargin,
                Child = panel
            };
        }

        static TextBlock Message(string text)
        {
            var message = new TextBlock
            {
                Text = text,
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap
            };
            AppTheme.Bind(message, TextBlock.ForegroundProperty, AppTheme.MutedText);
            return message;
        }
    }
}
