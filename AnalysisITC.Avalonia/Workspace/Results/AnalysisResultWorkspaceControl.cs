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
using AnalysisITC.Avalonia.Workspace;
using static AnalysisITC.Avalonia.Workspace.WorkspaceControlBuilder;

namespace AnalysisITC.Avalonia.Results
{
    public sealed class AnalysisResultWorkspaceControl : UserControl
    {
        static readonly EnergyUnit[] EvaluationEnergyUnits =
        {
            EnergyUnit.Joule,
            EnergyUnit.KiloJoule,
            EnergyUnit.Cal,
            EnergyUnit.KCal
        };

        static readonly string[] EvaluationEnergyUnitNames = { "J", "kJ", "cal", "kcal" };
        static readonly string[] UncertaintyStyleNames = { "Automatic", "Standard deviation", "95% confidence interval", "SD + 95% CI" };
        static readonly string[] SaltModeNames = { "Affinity vs Salt", "Debye-Huckel", "Counter Ion Release" };

        readonly ResultParameterGraphControl graph = new ResultParameterGraphControl();
        readonly ResultDependenceGraphControl dependenceGraph = new ResultDependenceGraphControl();
        readonly ContentControl graphHost = new ContentControl();
        readonly StackPanel tableHost = new StackPanel { Spacing = 0 };
        readonly StackPanel summaryPanel = WorkspaceControlBuilder.InspectorPanel();
        readonly StackPanel experimentsPanel = WorkspaceControlBuilder.InspectorPanel();
        readonly StackPanel modelPanel = WorkspaceControlBuilder.InspectorPanel();
        readonly StackPanel analysisPanel = WorkspaceControlBuilder.InspectorPanel();
        readonly ComboBox temperatureUnitCombo = WorkspaceControlBuilder.Combo(new[] { "Celsius", "Kelvin" }, 0, 170);
        readonly ComboBox displayEnergyUnitCombo = WorkspaceControlBuilder.Combo(EvaluationEnergyUnitNames, 1, 170);
        readonly ComboBox uncertaintyStyleCombo = WorkspaceControlBuilder.Combo(UncertaintyStyleNames, 0, 170);
        readonly TextBox evaluationTemperatureBox = WorkspaceControlBuilder.TextBox("");
        readonly StackPanel evaluationRowsPanel = WorkspaceControlBuilder.VerticalGroup();
        readonly Border displaySection;
        readonly Border parameterEvaluationSection;

        AnalysisResult? result;
        ResultAnalysisViewMode activeViewMode = ResultAnalysisViewMode.Parameters;
        readonly List<ResultAnalysisViewMode> availableViewModes = new List<ResultAnalysisViewMode>();
        FTSRMethod.SRFoldedMode selectedSrFoldedMode = FTSRMethod.SRFoldedMode.Glob;
        FTSRMethod.SRTempMode selectedSrTemperatureMode = FTSRMethod.SRTempMode.IsoEntropicPoint;
        ElectrostaticsAnalysis.DissocFitMode selectedSaltMode = ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel;
        bool isUpdatingSelection;
        bool isUpdatingEvaluationControls;
        bool isUpdatingResult;
        bool isRunningAdvancedAnalysis;
        bool evaluationUseKelvin;
        bool isUpdatingDisplayControls;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? ResultUpdated;

        public AnalysisResultWorkspaceControl()
        {
            displaySection = Section("Display", new Control[]
            {
                Labeled("Errors", uncertaintyStyleCombo),
                Labeled("Temperature", temperatureUnitCombo),
                Labeled("Energy", displayEnergyUnitCombo)
            });
            parameterEvaluationSection = BuildParameterEvaluationSection();

            SyncDisplayControls();
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
                ResetEvaluationTemperature();
                Refresh();
            }
        }

        public void FitToData()
        {
            if (activeViewMode == ResultAnalysisViewMode.Parameters)
                graph.FitToData();
            else
                dependenceGraph.FitToData();
        }

        public ResultAnalysisViewMode ActiveViewMode => activeViewMode;

        public bool IsResultViewModeAvailable(ResultAnalysisViewMode mode)
        {
            return availableViewModes.Contains(mode);
        }

        public void SetResultViewMode(ResultAnalysisViewMode mode)
        {
            RefreshAvailableViewModes();
            if (!availableViewModes.Contains(mode)) return;

            activeViewMode = mode;
            SyncModeCombo();
            RefreshGraphMode();
            RefreshAnalysis();
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

        public void SetEnergyDisplay(EnergyUnit unit)
        {
            isUpdatingDisplayControls = true;
            try
            {
                SetDisplayEnergyUnit(unit);
            }
            finally
            {
                isUpdatingDisplayControls = false;
            }

            if (AppSettings.EnergyUnit != unit)
            {
                AppSettings.EnergyUnit = unit;
                AppSettings.Save();
            }

            RefreshTable();
            graph.InvalidateVisual();
            dependenceGraph.Rebuild();
            RefreshParameterEvaluation();
            SyncDisplayControls();
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

            try
            {
                isUpdatingResult = true;
                RefreshSummary();
                StatusChanged?.Invoke(this, "Updating analysis result...");

                var convergence = await AnalysisResultUpdater.UpdateAsync(result);

                Refresh();
                ResultUpdated?.Invoke(this, EventArgs.Empty);
                StatusChanged?.Invoke(this, $"{convergence.Algorithm.GetProperties().ShortName} | RMSD = {convergence.Loss:G4}");
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
            displayEnergyUnitCombo.SelectionChanged += (_, _) => ChangeDisplayEnergyUnit();
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
            availableViewModes.Add(ResultAnalysisViewMode.Parameters);

            if (result?.IsAdvancedAnalysisAvailable == true)
            {
                if (result.IsTemperatureDependenceEnabled) availableViewModes.Add(ResultAnalysisViewMode.Temperature);
                if (result.IsElectrostaticsAnalysisDependenceEnabled) availableViewModes.Add(ResultAnalysisViewMode.Salt);
                if (result.IsProtonationAnalysisEnabled) availableViewModes.Add(ResultAnalysisViewMode.Protonation);
            }

        }

        void SyncModeCombo()
        {
        }

        void ChangeResultViewModeFromCombo(ComboBox combo)
        {
            var index = combo.SelectedIndex;
            if (index < 0 || index >= availableViewModes.Count) return;

            activeViewMode = availableViewModes[index];
            RefreshGraphMode();
            RefreshAnalysis();
        }

        void RefreshGraphMode()
        {
            if (activeViewMode == ResultAnalysisViewMode.Parameters)
            {
                graphHost.Content = graph;
                graph.Result = result;
                graph.InvalidateVisual();
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

            RefreshTable();
            graph.InvalidateVisual();
            dependenceGraph.InvalidateVisual();
        }

        void OnAdvancedAnalysisStarted(object? sender, TerminationFlag e)
        {
            isRunningAdvancedAnalysis = true;
            RefreshAnalysis();
            StatusBarManager.StartInderminateProgress();
            StatusBarManager.SetStatus("Advanced analysis started...", 0, priority: 1);
            StatusChanged?.Invoke(this, "Advanced analysis started...");
        }

        void OnAdvancedAnalysisIterationFinished(object? sender, Tuple<int, int, float, string> e)
        {
            var status = string.IsNullOrWhiteSpace(e.Item4)
                ? $"Advanced analysis {100 * e.Item3:F0}%"
                : $"{e.Item4}: {100 * e.Item3:F0}%";
            StatusBarManager.SetProgress(e.Item3);
            StatusBarManager.SetStatus(status, 1000, priority: 1);
            StatusChanged?.Invoke(this, status);
        }

        void OnAdvancedAnalysisFinished(object? sender, Tuple<int, TimeSpan> e)
        {
            isRunningAdvancedAnalysis = false;
            dependenceGraph.Rebuild();
            RefreshAnalysis();
            var status = $"Advanced analysis completed ({e.Item1} iterations).";
            StatusBarManager.ClearAppStatus();
            StatusBarManager.SetStatus(status, 5000);
            StatusChanged?.Invoke(this, status);
        }

        public void Refresh()
        {
            RefreshAvailableViewModes();
            if (!availableViewModes.Contains(activeViewMode))
                activeViewMode = ResultAnalysisViewMode.Parameters;
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
                MinHeight = 26,
                Padding = new Thickness(8, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = !isUpdatingResult && result.Solution?.Model != null
            };
            updateButton.Click += async (_, _) => await UpdateResultAsync();

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
            summaryPanel.Children.Add(BuildValiditySection(report));
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
                AppSettings.EnergyUnit,
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
                experimentsPanel.Children.Add(Section(data?.Name ?? "Experiment", new Control[]
                {
                    Pair("Date", data?.UIShortDateWithTime ?? ""),
                    Pair("Temperature", data == null ? "" : $"{data.MeasuredTemperature:G3} °C"),
                    Pair("Status", solution.IsValid ? "Solution valid" : "Solution invalid")
                }));
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
                    .Select(option => Pair(OptionName(option.Key, option.Value), OptionValue(option.Key, option.Value)))
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

            analysisPanel.Children.Add(BuildResultViewSection());
            analysisPanel.Children.Add(parameterEvaluationSection);

            if (!result.IsAdvancedAnalysisAvailable)
            {
                analysisPanel.Children.Add(Section("Advanced Analysis", new Control[]
                {
                    Text("Advanced analyses are available for OneSetOfSites results.")
                }));
                return;
            }

            switch (activeViewMode)
            {
                case ResultAnalysisViewMode.Parameters:
                    analysisPanel.Children.Add(Section("Advanced Analysis", new Control[]
                    {
                        Text("Select Temperature, Salt, or Protonation to run an advanced result analysis.")
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
            var combo = WorkspaceControlBuilder.Combo(availableViewModes.Select(ModeTitle).ToArray(), Math.Max(0, availableViewModes.IndexOf(activeViewMode)), 170);
            combo.SelectionChanged += (_, _) => ChangeResultViewModeFromCombo(combo);

            return Section("View", new Control[]
            {
                Labeled("Result", combo)
            });
        }

        Border BuildAvailabilitySection()
        {
            return Section("Available Analyses", new Control[]
            {
                Pair("Temperature", result?.IsTemperatureDependenceEnabled == true ? "Available" : "Unavailable"),
                Pair("Salt", result?.IsElectrostaticsAnalysisDependenceEnabled == true ? "Available" : "Unavailable"),
                Pair("Protonation", result?.IsProtonationAnalysisEnabled == true ? "Available" : "Unavailable")
            });
        }

        void RefreshTemperatureAnalysis()
        {
            if (result?.SpolarRecordAnalysis == null)
            {
                analysisPanel.Children.Add(Section("Temperature", new Control[] { Text("Temperature dependence is not available for this result.") }));
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
                analysisPanel.Children.Add(Section("Output", new Control[] { Text("Run the analysis to calculate Spolar record values.") }));
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
                Pair("Hydration", new Energy(analysis.Result.HydrationContribution(evaluationTemperature)).ToFormattedString(AppSettings.EnergyUnit, permole: true)),
                Pair("Conformation", new Energy(analysis.Result.ConformationalContribution(evaluationTemperature)).ToFormattedString(AppSettings.EnergyUnit, permole: true)),
                Pair("Residues", analysis.Result.Rvalue.AsNumber())
            }));
        }

        void RefreshSaltAnalysis()
        {
            if (result?.ElectrostaticsAnalysis == null)
            {
                analysisPanel.Children.Add(Section("Salt", new Control[] { Text("Salt dependence is not available for this result.") }));
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
                Pair("Kd0", analysis.Kd0.AsFormattedConcentration(withunit: true)),
                Pair("Counter ion", analysis.CounterIonRelease.AsNumber()),
            }));
        }

        void RefreshProtonationAnalysis()
        {
            if (result?.ProtonationAnalysis == null)
            {
                analysisPanel.Children.Add(Section("Protonation", new Control[] { Text("Protonation analysis is not available for this result.") }));
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
                    ? analysis.BindingEnthalpy.ToFormattedString(AppSettings.EnergyUnit, permole: true)
                    : new Energy(fit.Evaluate(0)).ToFormattedString(AppSettings.EnergyUnit, true, true, false)),
            }));
        }

        Task RunTemperatureAnalysisAsync()
        {
            if (result?.SpolarRecordAnalysis == null || isRunningAdvancedAnalysis) return Task.CompletedTask;

            result.SpolarRecordAnalysis.FoldedMode = selectedSrFoldedMode;
            result.SpolarRecordAnalysis.TempMode = selectedSrTemperatureMode;
            result.SpolarRecordAnalysis.PerformAnalysis();
            return Task.CompletedTask;
        }

        Task RunSaltAnalysisAsync()
        {
            if (result?.ElectrostaticsAnalysis == null || isRunningAdvancedAnalysis) return Task.CompletedTask;

            result.ElectrostaticsAnalysis.PerformAnalysis();
            return Task.CompletedTask;
        }

        Task RunProtonationAnalysisAsync()
        {
            if (result?.ProtonationAnalysis == null || isRunningAdvancedAnalysis) return Task.CompletedTask;

            result.ProtonationAnalysis.PerformAnalysis();
            return Task.CompletedTask;
        }

        void RefreshTable()
        {
            tableHost.Children.Clear();

            if (result == null)
            {
                tableHost.Children.Add(Message("No analysis result selected."));
                return;
            }

            var table = AnalysisResultOverviewTable.Build(result, AppSettings.EnergyUnit, UseKelvin);
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
            var color = report.Status switch
            {
                AnalysisResultValidity.Valid => AppTheme.StatusValid,
                AnalysisResultValidity.PartialInvalid => AppTheme.StatusWarning,
                AnalysisResultValidity.Invalid => AppTheme.StatusError,
                _ => AppTheme.StatusWarning
            };

            var title = new TextBlock
            {
                Text = ValidityTitle(report.Status),
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
                FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal,
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
                    RefreshTable();
                    graph.InvalidateVisual();
                    StatusChanged?.Invoke(this, solution.Data?.Name ?? "Solution selected");
                    e.Handled = true;
                };
            }

            Grid.SetColumn(border, column);
            Grid.SetRow(border, row);
            grid.Children.Add(border);
        }

        void SetDisplayEnergyUnit(EnergyUnit unit)
        {
            var index = Array.IndexOf(EvaluationEnergyUnits, unit);
            displayEnergyUnitCombo.SelectedIndex = index >= 0 ? index : 1;
        }

        void ChangeDisplayEnergyUnit()
        {
            if (isUpdatingDisplayControls) return;
            var index = displayEnergyUnitCombo.SelectedIndex;
            if (index < 0 || index >= EvaluationEnergyUnits.Length) return;
            SetEnergyDisplay(EvaluationEnergyUnits[index]);
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
                SetDisplayEnergyUnit(AppSettings.EnergyUnit);
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
                ResultAnalysisViewMode.Temperature => "Temperature",
                ResultAnalysisViewMode.Salt => "Salt",
                ResultAnalysisViewMode.Protonation => "Protonation",
                _ => "Parameters"
            };
        }

        static string ValidityTitle(AnalysisResultValidity status)
        {
            return status switch
            {
                AnalysisResultValidity.Valid => "Analysis is valid",
                AnalysisResultValidity.PartialInvalid => "Partially invalid",
                AnalysisResultValidity.Invalid => "Invalid",
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
                AttributeKey.PreboundLigandEnthalpy => new Energy(option.ParameterValue).ToFormattedString(AppSettings.EnergyUnit, true, true),
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

        static Border Pair(string label, string value)
        {
            var panel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions($"Auto,*"),
                ColumnSpacing = RowSpacing,
            };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = WorkspaceControlBuilder.LabelBrush,
                VerticalAlignment = VerticalAlignment.Top
            });
            var valueText = new TextBlock
            {
                Text = value ?? "",
                Foreground = WorkspaceControlBuilder.SectionHeaderBrush,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right
            };
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
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = WorkspaceControlBuilder.LabelBrush,
                VerticalAlignment = VerticalAlignment.Top
            });
            var valueText = new TextBlock
            {
                Text = value ?? "",
                Foreground = WorkspaceControlBuilder.SectionHeaderBrush,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Left
            };
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
            return new TextBlock
            {
                Text = text,
                Foreground = WorkspaceControlBuilder.LabelBrush,
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap
            };
        }
    }
}
