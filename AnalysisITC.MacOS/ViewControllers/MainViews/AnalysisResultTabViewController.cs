using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using AppKit;
using CoreGraphics;
using Foundation;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.UI.MacOS.CustomViews;

namespace AnalysisITC
{
    public partial class AnalysisResultTabViewController : NSViewController
    {
        const double DefaultResultColumnWidth = 100;

        static readonly EnergyUnit[] DisplayEnergyUnits =
        {
            EnergyUnit.Joule,
            EnergyUnit.KiloJoule,
            EnergyUnit.Cal,
            EnergyUnit.KCal,
        };

        static readonly string[] DisplayEnergyUnitNames =
        {
            "Joule",
            "Kilojoule",
            "Calorie",
            "Kilocalorie",
        };

        static readonly string[] UncertaintyStyleNames =
        {
            "Automatic",
            "Standard deviation",
            "95% confidence interval",
            "SD + 95% CI",
        };

        static readonly string[] SaltModeNames =
        {
            "Affinity vs Salt",
            "Debye-Hückel",
            "Counter Ion Release",
        };

        const string TemperatureUnitPreferenceKey =
            "AnalysisResultUseKelvin";

        readonly List<ResultGraphView.ResultGraphType> availableGraphTypes = new();
        readonly Dictionary<NSStackView, NSView> pageSpacers = new();

        NSStackView summaryStack;
        NSStackView analysisStack;
        NSStackView experimentsStack;
        NSStackView modelStack;

        NSPopUpButton uncertaintyStyleControl;
        NSPopUpButton temperatureUnitControl;
        NSPopUpButton energyUnitControl;
        NSPopUpButton resultViewControl;
        NSTextField evaluationTemperatureControl;
        NSButton updateResultControl;

        ResultViewDataSource resultTableSource;
        ResultViewDelegate resultTableDelegate;
        AnalysisResult analysisResult;

        ResultGraphView.ResultGraphType displayedGraphType =
            ResultGraphView.ResultGraphType.Parameters;
        FTSRMethod.SRFoldedMode selectedSrFoldedMode =
            FTSRMethod.SRFoldedMode.Glob;
        FTSRMethod.SRTempMode selectedSrTemperatureMode =
            FTSRMethod.SRTempMode.IsoEntropicPoint;
        ElectrostaticsAnalysis.DissocFitMode selectedSaltMode =
            ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel;

        string evaluationTemperatureText = string.Empty;
        bool evaluationTextUsesKelvin;
        bool isUpdatingResult;
        bool isRunningAdvancedAnalysis;
        bool refreshQueued;
        bool eventsSubscribed;
        bool eventsUnsubscribed;

        GlobalSolution Solution => analysisResult?.Solution;
        EnergyUnit EnergyUnit => AppSettings.EnergyUnit;
        bool UseKelvin => CurrentUseKelvin;

        public static TerminationFlag AnalysisTerminationFlag { get; } =
            new TerminationFlag();
        public static event EventHandler DisplayOptionsDidChange;
        public static event EventHandler RefreshDisplayRequested;
        public static event Action<bool> TemperatureUnitRequested;
        public static bool CurrentUseKelvin { get; private set; } =
            NSUserDefaults.StandardUserDefaults.BoolForKey(
                TemperatureUnitPreferenceKey);

        public static void RequestDisplayRefresh() =>
            RefreshDisplayRequested?.Invoke(null, EventArgs.Empty);

        public static void RequestTemperatureUnit(bool useKelvin) =>
            TemperatureUnitRequested?.Invoke(useKelvin);

        public AnalysisResultTabViewController(IntPtr handle) : base(handle)
        {
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            summaryStack = RegisterInspectorPage(ResultSummaryStack);
            analysisStack = RegisterInspectorPage(ResultAnalysisStack);
            experimentsStack = RegisterInspectorPage(
                ResultExperimentsStack);
            modelStack = RegisterInspectorPage(ResultModelStack);

            ResultInspectorTabControl.SelectedSegment = 0;
            ResultInspectorTabView.SelectAt(0);

            ConfigureResultsTable();
            SubscribeEvents();
            SetAnalysisResult(DataManager.SelectedResult, resetViewMode: true);
        }

        public override void ViewDidAppear()
        {
            base.ViewDidAppear();

            if (!ReferenceEquals(analysisResult, DataManager.SelectedResult))
                SetAnalysisResult(DataManager.SelectedResult, resetViewMode: true);
            else
                RefreshAll();
        }

        partial void ResultInspectorTabChanged(NSObject sender)
        {
            if (ResultInspectorTabControl == null
                || ResultInspectorTabView == null) return;

            var index = (int)ResultInspectorTabControl.SelectedSegment;
            if (index < 0 || index >= ResultInspectorTabView.Items.Length) return;

            ResultInspectorTabView.SelectAt(index);
        }

        void SubscribeEvents()
        {
            if (eventsSubscribed) return;
            eventsSubscribed = true;

            DataManager.AnalysisResultSelected +=
                DataManager_AnalysisResultSelected;
            DataManager.ResultSolutionSelectionDidChange +=
                DataManager_ResultSolutionSelectionDidChange;
            ResultAnalysisController.AnalysisStarted +=
                ResultAnalysisStarted;
            ResultAnalysisController.IterationFinished +=
                ResultAnalysisProgressReport;
            ResultAnalysisController.AnalysisFinished +=
                ResultsAnalysisCompleted;
            AppDelegate.StartPrintOperation +=
                AppDelegate_StartPrintOperation;
            RefreshDisplayRequested +=
                AnalysisResultTabViewController_RefreshDisplayRequested;
            TemperatureUnitRequested +=
                AnalysisResultTabViewController_TemperatureUnitRequested;
            AppSettings.SettingsDidUpdate +=
                AppSettings_SettingsDidUpdate;
        }

        void AppSettings_SettingsDidUpdate(object sender, EventArgs e)
        {
            QueueRefresh();
        }

        void AnalysisResultTabViewController_RefreshDisplayRequested(
            object sender,
            EventArgs e)
        {
            QueueRefresh();
        }

        void AnalysisResultTabViewController_TemperatureUnitRequested(
            bool useKelvin)
        {
            BeginInvokeOnMainThread(() => ApplyTemperatureUnit(useKelvin));
        }

        void DataManager_AnalysisResultSelected(
            object sender,
            AnalysisResult result)
        {
            BeginInvokeOnMainThread(() =>
                SetAnalysisResult(result, resetViewMode: false));
        }

        void DataManager_ResultSolutionSelectionDidChange(
            object sender,
            SolutionInterface solution)
        {
            BeginInvokeOnMainThread(() =>
            {
                SyncTableSelection(solution);
                if (displayedGraphType == ResultGraphView.ResultGraphType.SelectedFit)
                {
                    SetupGraphView();
                    RefreshAnalysis();
                }
            });
        }

        void AppDelegate_StartPrintOperation(object sender, EventArgs e)
        {
            if (StateManager.CurrentState != ProgramState.AnalysisView
                || analysisResult == null) return;

            Graph.Print();
        }

        void SetAnalysisResult(
            AnalysisResult result,
            bool resetViewMode)
        {
            var changed = !ReferenceEquals(analysisResult, result);
            analysisResult = result;

            if (changed)
            {
                if (DataManager.SelectedResultSolution != null
                    && result?.Solution?.Solutions?.Contains(
                        DataManager.SelectedResultSolution) != true)
                {
                    DataManager.ClearResultSolutionSelection();
                }

                selectedSrFoldedMode =
                    result?.SpolarRecordAnalysis?.FoldedMode
                    == FTSRMethod.SRFoldedMode.ID
                        ? FTSRMethod.SRFoldedMode.ID
                        : FTSRMethod.SRFoldedMode.Glob;
                selectedSrTemperatureMode =
                    result?.SpolarRecordAnalysis?.TempMode
                    ?? FTSRMethod.SRTempMode.IsoEntropicPoint;
                selectedSaltMode =
                    ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel;
                ResetEvaluationTemperature();
            }

            RefreshAvailableGraphTypes();
            if (resetViewMode
                || !availableGraphTypes.Contains(displayedGraphType))
            {
                displayedGraphType =
                    ResultGraphView.ResultGraphType.Parameters;
            }

            RefreshAll();
        }

        public void ClearUI()
        {
            analysisResult = null;
            displayedGraphType = ResultGraphView.ResultGraphType.Parameters;
            evaluationTemperatureText = string.Empty;
            RefreshAll();
        }

        public void SetupAnalysisTabView()
        {
            RefreshAvailableGraphTypes();
            if (!availableGraphTypes.Contains(displayedGraphType))
                displayedGraphType =
                    ResultGraphView.ResultGraphType.Parameters;
            RefreshAll();
        }

        public void SetupResultView()
        {
            RefreshAll();
        }

        void RefreshAll()
        {
            if (summaryStack == null) return;

            RefreshAvailableGraphTypes();
            if (!availableGraphTypes.Contains(displayedGraphType))
                displayedGraphType =
                    ResultGraphView.ResultGraphType.Parameters;

            RefreshSummary();
            RefreshAnalysis();
            RefreshExperiments();
            RefreshModel();
            PopulateTable();
            SetupGraphView();
            UpdateAnalysisViewSubState();
            NotifyDisplayOptionsDidChange();
        }

        void QueueRefresh()
        {
            if (refreshQueued || View == null) return;
            refreshQueued = true;

            BeginInvokeOnMainThread(() =>
            {
                refreshQueued = false;
                RefreshAll();
            });
        }

        void RefreshSummary()
        {
            ClearPage(summaryStack);

            uncertaintyStyleControl = null;
            temperatureUnitControl = null;
            energyUnitControl = null;
            updateResultControl = null;

            if (analysisResult == null || Solution == null)
            {
                AddPageView(
                    summaryStack,
                    Message("No analysis result selected."));
                return;
            }

            AddPageView(summaryStack, BuildValiditySection());

            AddPageView(summaryStack, Section(
                "Result",
                Pair("Name", analysisResult.Name),
                Pair("Model", Solution.SolutionName),
                Pair(
                    "Experiments",
                    Solution.Solutions.Count.ToString(
                        CultureInfo.CurrentCulture)),
                Pair(
                    "RMSD",
                    Solution.Loss.ToString(
                        "G4",
                        CultureInfo.CurrentCulture))));

            var hasComment =
                !string.IsNullOrWhiteSpace(analysisResult.Comments);
            AddPageView(summaryStack, Section(
                "Comment",
                Label(
                    hasComment
                        ? analysisResult.Comments.Trim()
                        : "No comment.",
                    NSFont.SystemFontOfSize(
                        NSFont.SystemFontSize),
                    hasComment
                        ? NSColor.Label
                        : NSColor.SecondaryLabel)));

            var solverRows = new List<NSView>
            {
                Pair(
                    "Algorithm",
                    Solution.Convergence?.Algorithm.GetProperties().Name ?? ""),
                Pair(
                    "Iterations",
                    Solution.Convergence?.Iterations.ToString(
                        CultureInfo.CurrentCulture) ?? ""),
                Pair(
                    "Fitting",
                    Solution.UseWeightedFitting
                        ? "Weighted injection errors"
                        : "Unweighted"),
                Pair(
                    "Errors",
                    Solution.ErrorEstimationMethod.Description()),
                Pair(
                    "Bootstrap",
                    Solution.BootstrapIterations.ToString(
                        CultureInfo.CurrentCulture)),
            };

            var cloneOptions = Solution.ModelCloneOptions;
            if (cloneOptions != null)
            {
                var value =
                    !cloneOptions.IncludeConcentrationErrorsInBootstrap
                        ? "Not included"
                        : cloneOptions.EnableAutoConcentrationVariance
                            ? $"Included · Auto {100 * cloneOptions.AutoConcentrationVariance:F1}%"
                            : "Included · Experiment values";
                solverRows.Add(Pair("Concentration error", value));
            }

            AddPageView(
                summaryStack,
                Section("Solver", solverRows.ToArray()));

            //BuildDisplayControls();
            //AddPageView(summaryStack, Section(
            //    "Display",
            //    LabeledControl("Errors", uncertaintyStyleControl),
            //    LabeledControl("Temperature", temperatureUnitControl),
            //    LabeledControl("Energy", energyUnitControl)));

            updateResultControl = Button(
                isUpdatingResult ? "Updating…" : "Update Result");
            updateResultControl.Enabled =
                !isUpdatingResult
                && !isRunningAdvancedAnalysis
                && Solution.Model != null;
            updateResultControl.ToolTip =
                "Refit this saved result using its stored model settings and the current experiment data.";
            updateResultControl.Activated += async (_, _) =>
                await UpdateResultAsync();

            var exportButton = Button("Export Analysis Result");
            exportButton.ToolTip =
                "Open the analysis result exporter for the selected result.";
            exportButton.Activated += (_, _) =>
                AppDelegate.LaunchResultExporter();

            AddPageView(summaryStack, Section(
                "Actions",
                EqualButtonRow(updateResultControl, exportButton)));
        }

        void BuildDisplayControls()
        {
            uncertaintyStyleControl = Popup(
                UncertaintyStyleNames,
                AppSettings.UncertaintyDisplayStyle switch
                {
                    UncertaintyDisplayStyle.StandardDeviation => 1,
                    UncertaintyDisplayStyle.ConfidenceInterval => 2,
                    UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval => 3,
                    _ => 0,
                });
            uncertaintyStyleControl.ToolTip =
                "Choose how fitted parameter uncertainty is displayed.";
            uncertaintyStyleControl.Activated += (_, _) =>
            {
                AppSettings.UncertaintyDisplayStyle =
                    (int)uncertaintyStyleControl.IndexOfSelectedItem switch
                    {
                        1 => UncertaintyDisplayStyle.StandardDeviation,
                        2 => UncertaintyDisplayStyle.ConfidenceInterval,
                        3 => UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
                        _ => UncertaintyDisplayStyle.Automatic,
                    };
                AppSettings.Save();
                QueueRefresh();
            };

            temperatureUnitControl = Popup(
                new[] { "Celsius", "Kelvin" },
                UseKelvin ? 1 : 0);
            temperatureUnitControl.ToolTip =
                "Choose the temperature unit used in the inspector and result table.";
            temperatureUnitControl.Activated += (_, _) =>
            {
                ApplyTemperatureUnit(
                    temperatureUnitControl.IndexOfSelectedItem == 1);
            };

            var energyIndex = Array.IndexOf(
                DisplayEnergyUnits,
                AppSettings.EnergyUnit);
            energyUnitControl = Popup(
                DisplayEnergyUnitNames,
                energyIndex >= 0 ? energyIndex : 1);
            energyUnitControl.ToolTip =
                "Choose the energy unit used for parameters, analyses, and the result table.";
            energyUnitControl.Activated += (_, _) =>
            {
                var index = (int)energyUnitControl.IndexOfSelectedItem;
                if (index < 0 || index >= DisplayEnergyUnits.Length) return;

                AppSettings.EnergyUnit = DisplayEnergyUnits[index];
                AppSettings.Save();
                QueueRefresh();
            };
        }

        NSView BuildValiditySection()
        {
            var report = analysisResult.ValidityReport;
            var status = Label(
                AnalysisResultValidityPresentation.ButtonTitle(
                    analysisResult,
                    report),
                NSFont.SystemFontOfSize(
                    NSFont.SystemFontSize,
                    NSFontWeight.Medium),
                AnalysisResultValidityPresentation.ButtonColor(report));
            status.ToolTip =
                AnalysisResultValidityPresentation.ButtonTooltip(
                    analysisResult,
                    report);

            var rows = new List<NSView> { status };
            if (report.Reasons.Count == 0)
            {
                rows.Add(Message(
                    report.Status == AnalysisResultValidity.Valid
                        ? "Cached data matches the current experiment data."
                        : "Validity could not be determined."));
            }
            else
            {
                rows.AddRange(report.Reasons.Select(Message));
            }

            return Section("Validity", rows.ToArray());
        }

        async Task UpdateResultAsync()
        {
            if (analysisResult == null
                || isUpdatingResult
                || isRunningAdvancedAnalysis) return;

            try
            {
                isUpdatingResult = true;
                RefreshSummary();
                StatusBarManager.StartInderminateProgress();
                StatusBarManager.SetStatus(
                    "Updating analysis result…",
                    0,
                    priority: 1);

                var convergence =
                    await AnalysisResultUpdater.UpdateAsync(analysisResult);

                ResetEvaluationTemperature();
                RefreshAll();
                StatusBarManager.SetStatus(
                    $"{convergence.Algorithm.GetProperties().ShortName} | RMSD = {convergence.Loss:G4}",
                    5000);
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
                StatusBarManager.SetStatus(
                    $"Result update failed: {ex.Message}",
                    5000);
            }
            finally
            {
                isUpdatingResult = false;
                StatusBarManager.StopIndeterminateProgress();
                QueueRefresh();
            }
        }

        void RefreshAnalysis()
        {
            ClearPage(analysisStack);
            resultViewControl = null;
            evaluationTemperatureControl = null;

            if (analysisResult == null)
            {
                AddPageView(
                    analysisStack,
                    Message("No analysis result selected."));
                return;
            }

            var selectedIndex = Math.Max(
                0,
                availableGraphTypes.IndexOf(displayedGraphType));
            resultViewControl = Popup(
                availableGraphTypes.Select(GraphModeTitle).ToArray(),
                selectedIndex);
            resultViewControl.ToolTip =
                "Choose which result or advanced analysis is shown in the graph.";
            resultViewControl.Activated += (_, _) =>
            {
                var index = (int)resultViewControl.IndexOfSelectedItem;
                if (index < 0 || index >= availableGraphTypes.Count) return;

                displayedGraphType = availableGraphTypes[index];
                QueueRefresh();
            };

            AddPageView(
                analysisStack,
                Section(
                    "View",
                    LabeledControl("Result", resultViewControl)));
            AddPageView(
                analysisStack,
                BuildParameterEvaluationSection());

            if (displayedGraphType == ResultGraphView.ResultGraphType.SelectedFit)
            {
                var selected = SelectedResultSolution();
                AddPageView(
                    analysisStack,
                    Section(
                        "Selected Fit",
                        Message(selected?.Data?.Name
                            ?? "Select an experiment in the table or an overview graph.")));
                return;
            }

            if (!analysisResult.IsAdvancedAnalysisAvailable)
            {
                AddPageView(analysisStack, Section(
                    "Advanced Analysis",
                    Message(
                        "Advanced analyses are available for one-site analysis results.")));
                return;
            }

            switch (displayedGraphType)
            {
                case ResultGraphView.ResultGraphType.TemperatureDependence:
                    AddTemperatureAnalysisSections();
                    break;
                case ResultGraphView.ResultGraphType.IonicStrengthDependence:
                    AddSaltAnalysisSections();
                    break;
                case ResultGraphView.ResultGraphType.ProtonationAnalysis:
                    AddProtonationAnalysisSections();
                    break;
                default:
                    AddPageView(analysisStack, Section(
                        "Available Analyses",
                        Pair(
                            "Temperature",
                            analysisResult.IsTemperatureDependenceEnabled
                                ? "Available"
                                : "Unavailable"),
                        Pair(
                            "Salt",
                            analysisResult.IsElectrostaticsAnalysisDependenceEnabled
                                ? "Available"
                                : "Unavailable"),
                        Pair(
                            "Protonation",
                            analysisResult.IsProtonationAnalysisEnabled
                                ? "Available"
                                : "Unavailable")));
                    break;
            }
        }

        NSView BuildParameterEvaluationSection()
        {
            evaluationTemperatureControl = TextField(evaluationTemperatureText);
            evaluationTemperatureControl.ToolTip = "Evaluate temperature-dependent fitted parameters at this temperature.";
            evaluationTemperatureControl.Activated += (_, _) => EvaluationTemperatureChanged();
            evaluationTemperatureControl.EditingEnded += (_, _) => EvaluationTemperatureChanged();

            var unitLabel = Label(UseKelvin ? "K" : "°C", NSFont.SystemFontOfSize(NSFont.SystemFontSize), NSColor.SecondaryLabel);

            var editor = HorizontalStack(6,
                evaluationTemperatureControl,
                unitLabel);

            evaluationTemperatureControl.WidthAnchor.ConstraintEqualToConstant(100).Active = true;
            evaluationTemperatureControl.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
            unitLabel.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Horizontal);

            var rows = new List<NSView>
            {
                LabeledControl("Temperature", editor),
            };

            if (!TryReadEvaluationTemperature(out var temperatureCelsius))
            {
                rows.Add(Message("Enter a valid evaluation temperature."));
            }
            else
            {
                if (temperatureCelsius < -273.15)
                {
                    temperatureCelsius = -273.15;
                    SetEvaluationTemperatureText(temperatureCelsius);
                    evaluationTemperatureControl.StringValue = evaluationTemperatureText;
                }

                var evaluation =
                    AnalysisResultParameterEvaluator.Evaluate(
                        analysisResult,
                        temperatureCelsius,
                        EnergyUnit,
                        AppSettings.UncertaintyDisplayStyle);
                if (!evaluation.IsAvailable)
                {
                    rows.Add(Message(evaluation.Message));
                }
                else
                {
                    foreach (var row in evaluation.Rows)
                    {
                        var pair = Pair(row.Label, row.Value);
                        if (!string.IsNullOrWhiteSpace(row.Tooltip))
                            pair.ToolTip = row.Tooltip;
                        rows.Add(pair);
                    }
                }
            }

            return Section("Parameter Evaluation", rows.ToArray());
        }

        void EvaluationTemperatureChanged()
        {
            if (evaluationTemperatureControl == null) return;

            evaluationTemperatureText = evaluationTemperatureControl.StringValue;
            if (TryReadEvaluationTemperature(out var temperatureCelsius) && temperatureCelsius < -273.15)
            {
                SetEvaluationTemperatureText(-273.15);
            }
            QueueRefresh();
        }

        void AddTemperatureAnalysisSections()
        {
            var analysis = analysisResult.SpolarRecordAnalysis;
            if (analysis == null)
            {
                AddPageView(analysisStack, Section(
                    "Temperature",
                    Message(
                        "Temperature dependence is not available for this result.")));
                return;
            }

            var foldedControl = Popup(
                new[] { "Globular", "ID interaction" },
                selectedSrFoldedMode == FTSRMethod.SRFoldedMode.ID
                    ? 1
                    : 0);
            foldedControl.ToolTip =
                "Choose the folded-state model used for the temperature analysis.";
            foldedControl.Activated += (_, _) =>
            {
                selectedSrFoldedMode =
                    foldedControl.IndexOfSelectedItem == 1
                        ? FTSRMethod.SRFoldedMode.ID
                        : FTSRMethod.SRFoldedMode.Glob;
            };

            var temperatureModeControl = Popup(
                new[]
                {
                    "Isoentropic point",
                    "Mean temperature",
                    "Reference temperature",
                },
                selectedSrTemperatureMode switch
                {
                    FTSRMethod.SRTempMode.MeanTemperature => 1,
                    FTSRMethod.SRTempMode.ReferenceTemperature => 2,
                    _ => 0,
                });
            temperatureModeControl.ToolTip =
                "Choose the reference temperature used to separate hydration and conformational contributions.";
            temperatureModeControl.Activated += (_, _) =>
            {
                selectedSrTemperatureMode =
                    (int)temperatureModeControl.IndexOfSelectedItem switch
                    {
                        1 => FTSRMethod.SRTempMode.MeanTemperature,
                        2 => FTSRMethod.SRTempMode.ReferenceTemperature,
                        _ => FTSRMethod.SRTempMode.IsoEntropicPoint,
                    };
            };

            var runButton = Button(
                isRunningAdvancedAnalysis ? "Running…" : "Run Analysis");
            runButton.Enabled = !isRunningAdvancedAnalysis
                && !isUpdatingResult;
            runButton.ToolTip =
                "Calculate temperature-dependent hydration and conformational contributions.";
            runButton.Activated += (_, _) =>
                RunTemperatureAnalysis();

            AddPageView(analysisStack, Section(
                "Temperature",
                LabeledControl("Folded mode", foldedControl),
                LabeledControl("Temperature", temperatureModeControl),
                ButtonRow(runButton)));

            if (analysis.Result == null)
            {
                AddPageView(analysisStack, Section(
                    "Output",
                    Message(
                        "Run the analysis to calculate Spolar record values.")));
                return;
            }

            var evaluationTemperature =
                analysis.EvalutationTemperature(false);
            AddPageView(analysisStack, Section(
                "Output",
                Pair(
                    "Mode",
                    analysis.FoldedMode switch
                    {
                        FTSRMethod.SRFoldedMode.ID => "ID interaction",
                        FTSRMethod.SRFoldedMode.Intermediate => "Intermediate",
                        _ => "Globular",
                    }),
                Pair(
                    "Reference T",
                    FormatTemperature(
                        analysis.Result.ReferenceTemperature.Value)),
                Pair(
                    "Hydration",
                    new Energy(
                            analysis.Result.HydrationContribution(
                                evaluationTemperature))
                        .ToFormattedString(
                            EnergyUnit,
                            permole: true)),
                Pair(
                    "Conformation",
                    new Energy(
                            analysis.Result.ConformationalContribution(
                                evaluationTemperature))
                        .ToFormattedString(
                            EnergyUnit,
                            permole: true)),
                Pair("Residues", analysis.Result.Rvalue.AsNumber())));
        }

        void AddSaltAnalysisSections()
        {
            var analysis = analysisResult.ElectrostaticsAnalysis;
            if (analysis == null)
            {
                AddPageView(analysisStack, Section(
                    "Salt",
                    Message(
                        "Salt dependence is not available for this result.")));
                return;
            }

            var modeControl = Popup(
                SaltModeNames,
                selectedSaltMode switch
                {
                    ElectrostaticsAnalysis.DissocFitMode.AffinityVsSalt => 0,
                    ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease => 2,
                    _ => 1,
                });
            modeControl.ToolTip =
                "Choose how the salt-dependence data and fitted relationship are displayed.";
            modeControl.Activated += (_, _) =>
            {
                selectedSaltMode =
                    (int)modeControl.IndexOfSelectedItem switch
                    {
                        0 => ElectrostaticsAnalysis.DissocFitMode.AffinityVsSalt,
                        2 => ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease,
                        _ => ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel,
                    };
                QueueRefresh();
            };

            var runButton = Button(
                isRunningAdvancedAnalysis ? "Running…" : "Run Analysis");
            runButton.Enabled = !isRunningAdvancedAnalysis
                && !isUpdatingResult;
            runButton.ToolTip =
                "Calculate electrostatic and counter-ion-release parameters from the result.";
            runButton.Activated += (_, _) => RunSaltAnalysis();

            AddPageView(analysisStack, Section(
                "Salt",
                LabeledControl("Graph mode", modeControl),
                ButtonRow(runButton)));

            if (!analysis.Calculated)
            {
                AddPageView(analysisStack, Section(
                    "Output",
                    Message(
                        "Run the analysis to calculate electrostatic parameters.")));
                return;
            }

            AddPageView(analysisStack, Section(
                "Output",
                Pair(
                    "Kd0",
                    analysis.Kd0.AsFormattedConcentration(
                        withunit: true)),
                Pair(
                    "Counter ion",
                    analysis.CounterIonRelease.AsNumber())));
        }

        void AddProtonationAnalysisSections()
        {
            var analysis = analysisResult.ProtonationAnalysis;
            if (analysis == null)
            {
                AddPageView(analysisStack, Section(
                    "Protonation",
                    Message(
                        "Protonation analysis is not available for this result.")));
                return;
            }

            var runButton = Button(
                isRunningAdvancedAnalysis ? "Running…" : "Run Analysis");
            runButton.Enabled = !isRunningAdvancedAnalysis
                && !isUpdatingResult;
            runButton.ToolTip =
                "Calculate proton uptake and intrinsic binding enthalpy from buffer-dependent data.";
            runButton.Activated += (_, _) => RunProtonationAnalysis();

            AddPageView(
                analysisStack,
                Section("Protonation", ButtonRow(runButton)));

            if (analysis.Fit == null)
            {
                AddPageView(analysisStack, Section(
                    "Output",
                    Message(
                        "Run the analysis to calculate protonation-corrected binding parameters.")));
                return;
            }

            var fit = analysis.Fit as LinearFitWithError;
            AddPageView(analysisStack, Section(
                "Output",
                Pair(
                    "Protons",
                    fit == null
                        ? analysis.ProtonationChange.AsNumber()
                        : (-1 * fit.Slope).AsNumber()),
                Pair(
                    "Binding H",
                    fit == null
                        ? analysis.BindingEnthalpy.ToFormattedString(
                            EnergyUnit,
                            permole: true)
                        : new Energy(fit.Evaluate(0))
                            .ToFormattedString(
                                EnergyUnit,
                                true,
                                true,
                                false))));
        }

        void RunTemperatureAnalysis()
        {
            var analysis = analysisResult?.SpolarRecordAnalysis;
            if (analysis == null || isRunningAdvancedAnalysis) return;

            analysis.FoldedMode = selectedSrFoldedMode;
            analysis.TempMode = selectedSrTemperatureMode;
            isRunningAdvancedAnalysis = true;
            QueueRefresh();
            analysis.PerformAnalysis();
        }

        void RunSaltAnalysis()
        {
            var analysis = analysisResult?.ElectrostaticsAnalysis;
            if (analysis == null || isRunningAdvancedAnalysis) return;

            isRunningAdvancedAnalysis = true;
            QueueRefresh();
            analysis.PerformAnalysis();
        }

        void RunProtonationAnalysis()
        {
            var analysis = analysisResult?.ProtonationAnalysis;
            if (analysis == null || isRunningAdvancedAnalysis) return;

            isRunningAdvancedAnalysis = true;
            QueueRefresh();
            analysis.PerformAnalysis();
        }

        void ResultAnalysisStarted(
            object sender,
            TerminationFlag terminationFlag)
        {
            BeginInvokeOnMainThread(() =>
            {
                isRunningAdvancedAnalysis = true;
                StatusBarManager.StartInderminateProgress();
                StatusBarManager.SetStatus(
                    "Advanced analysis started…",
                    0,
                    priority: 1);
                QueueRefresh();
            });
        }

        void ResultAnalysisProgressReport(
            object sender,
            Tuple<int, int, float, string> progress)
        {
            BeginInvokeOnMainThread(() =>
            {
                var message = string.IsNullOrWhiteSpace(progress.Item4)
                    ? $"Advanced analysis {100 * progress.Item3:F0}%"
                    : $"{progress.Item4}: {100 * progress.Item3:F0}%";
                StatusBarManager.SetProgress(progress.Item3);
                StatusBarManager.SetStatus(
                    message,
                    1000,
                    priority: 1);
            });
        }

        void ResultsAnalysisCompleted(
            object sender,
            Tuple<int, TimeSpan> result)
        {
            BeginInvokeOnMainThread(() =>
            {
                isRunningAdvancedAnalysis = false;
                StatusBarManager.ClearAppStatus();
                StatusBarManager.SetStatus(
                    $"Advanced analysis completed ({result.Item1} iterations).",
                    5000);
                QueueRefresh();
            });
        }

        void RefreshExperiments()
        {
            ClearPage(experimentsStack);

            if (Solution?.Solutions == null
                || Solution.Solutions.Count == 0)
            {
                AddPageView(
                    experimentsStack,
                    Message("No experiments are included."));
                return;
            }

            foreach (var solution in Solution.Solutions)
            {
                var data = solution.Data;
                AddPageView(experimentsStack, Section(
                    data?.Name ?? "Experiment",
                    Pair("Date", data?.UIShortDateWithTime ?? ""),
                    Pair(
                        "Temperature",
                        data == null
                            ? ""
                            : FormatTemperature(
                                data.MeasuredTemperature)),
                    Pair(
                        "Status",
                        solution.IsValid
                            ? "Valid"
                            : "Solution expired")));
            }
        }

        void RefreshModel()
        {
            ClearPage(modelStack);

            if (Solution?.Model == null)
            {
                AddPageView(
                    modelStack,
                    Message("No model selected."));
                return;
            }

            var options = Solution.Model.ModelOptions;
            if (options == null || options.Count == 0)
            {
                AddPageView(
                    modelStack,
                    Section("Model Options", Message("None")));
            }
            else
            {
                AddPageView(
                    modelStack,
                    Section(
                        "Model Options",
                        options
                            .OrderBy(option =>
                                AnalysisInspectorDisplayCatalog.OptionOrder(
                                    option.Key))
                            .Select(option =>
                                Pair(
                                    AnalysisInspectorDisplayCatalog
                                        .OptionTitle(option.Value),
                                    OptionValue(
                                        option.Key,
                                        option.Value)))
                            .ToArray()));
            }

            var constraints = Solution.Model.Parameters?.Constraints?
                .Where(constraint =>
                    constraint.Value != VariableConstraint.None)
                .ToList()
                ?? new List<
                    KeyValuePair<ParameterType, VariableConstraint>>();
            if (constraints.Count == 0)
            {
                AddPageView(
                    modelStack,
                    Section("Constraints", Message("None")));
            }
            else
            {
                AddPageView(
                    modelStack,
                    Section(
                        "Constraints",
                        constraints
                            .Select(constraint =>
                                Pair(
                                    constraint.Key.GetProperties().Name,
                                    AnalysisInspectorDisplayCatalog
                                        .ConstraintTitle(
                                            constraint.Value)))
                            .ToArray()));
            }
        }

        void RefreshAvailableGraphTypes()
        {
            availableGraphTypes.Clear();
            availableGraphTypes.Add(
                ResultGraphView.ResultGraphType.Parameters);
            availableGraphTypes.Add(
                ResultGraphView.ResultGraphType.SelectedFit);

            if (analysisResult?.IsAdvancedAnalysisAvailable != true) return;
            if (analysisResult.IsTemperatureDependenceEnabled)
                availableGraphTypes.Add(
                    ResultGraphView.ResultGraphType.TemperatureDependence);
            if (analysisResult.IsElectrostaticsAnalysisDependenceEnabled)
                availableGraphTypes.Add(
                    ResultGraphView.ResultGraphType.IonicStrengthDependence);
            if (analysisResult.IsProtonationAnalysisEnabled)
                availableGraphTypes.Add(
                    ResultGraphView.ResultGraphType.ProtonationAnalysis);
        }

        void SetupGraphView()
        {
            if (Graph == null) return;

            Graph.Hidden = analysisResult == null;
            if (analysisResult == null) return;

            switch (displayedGraphType)
            {
                case ResultGraphView.ResultGraphType.SelectedFit:
                    Graph.SetupSelectedFit(SelectedResultSolution());
                    break;
                case ResultGraphView.ResultGraphType.TemperatureDependence:
                    Graph.Setup(
                        ResultGraphView.ResultGraphType.TemperatureDependence,
                        analysisResult);
                    break;
                case ResultGraphView.ResultGraphType.IonicStrengthDependence:
                    if (analysisResult.ElectrostaticsAnalysis != null)
                        Graph.Setup(
                            analysisResult.ElectrostaticsAnalysis,
                            selectedSaltMode);
                    break;
                case ResultGraphView.ResultGraphType.ProtonationAnalysis:
                    if (analysisResult.ProtonationAnalysis != null)
                        Graph.Setup(
                            analysisResult.ProtonationAnalysis);
                    break;
                default:
                    Graph.Setup(
                        ResultGraphView.ResultGraphType.Parameters,
                        analysisResult);
                    break;
            }
        }

        SolutionInterface SelectedResultSolution()
        {
            var selected = DataManager.SelectedResultSolution;
            return selected != null
                && analysisResult?.Solution?.Solutions?.Contains(selected) == true
                    ? selected
                    : null;
        }

        void UpdateAnalysisViewSubState()
        {
            StateManager.SetProgramSubState(displayedGraphType switch
            {
                ResultGraphView.ResultGraphType.TemperatureDependence =>
                    ProgramSubState.ResultStructuring,
                ResultGraphView.ResultGraphType.IonicStrengthDependence =>
                    ProgramSubState.ResultSalt,
                ResultGraphView.ResultGraphType.ProtonationAnalysis =>
                    ProgramSubState.ResultProtonation,
                _ => ProgramSubState.None,
            });
        }

        void ConfigureResultsTable()
        {
            ResultsTableView.ColumnAutoresizingStyle =
                NSTableViewColumnAutoresizingStyle.None;
            ResultsTableView.AutoresizingMask =
                NSViewResizingMask.WidthSizable
                | NSViewResizingMask.HeightSizable;

            var scrollView = ResultsTableView.EnclosingScrollView;
            if (scrollView == null) return;

            scrollView.HasHorizontalScroller = true;
            scrollView.HasVerticalScroller = true;
            scrollView.AutohidesScrollers = true;
            scrollView.UsesPredominantAxisScrolling = false;
            scrollView.HorizontalScrollElasticity =
                NSScrollElasticity.Allowed;
        }

        void PopulateTable()
        {
            if (ResultsTableView == null) return;

            ResultsTableView.DataSource = null;
            ResultsTableView.Delegate = null;
            resultTableSource?.Dispose();
            resultTableDelegate?.Dispose();
            resultTableSource = null;
            resultTableDelegate = null;

            while (ResultsTableView.ColumnCount > 0)
            {
                ResultsTableView.RemoveColumn(
                    ResultsTableView.TableColumns()[0]);
            }

            if (analysisResult == null)
            {
                ResultsTableView.ReloadData();
                return;
            }

            resultTableSource = new ResultViewDataSource(
                analysisResult,
                EnergyUnit,
                UseKelvin);
            resultTableDelegate =
                new ResultViewDelegate(resultTableSource);

            foreach (var presentationColumn
                in resultTableSource.Presentation.Columns)
            {
                var column = new NSTableColumn(
                    presentationColumn.Id)
                {
                    Title = presentationColumn.Title,
                    Width = (nfloat)DefaultResultColumnWidth,
                    MinWidth = (nfloat)DefaultResultColumnWidth,
                    MaxWidth = 1000,
                    Editable = false,
                    ResizingMask = NSTableColumnResizing.UserResizingMask,
                };
                column.HeaderCell.Alignment =
                    TextAlignment(presentationColumn.Alignment);
                column.HeaderCell.Wraps = false;
                column.HeaderCell.Scrollable = false;
                column.HeaderCell.UsesSingleLineMode = true;
                column.HeaderCell.LineBreakMode =
                    NSLineBreakMode.TruncatingTail;
                column.HeaderCell.TruncatesLastVisibleLine = true;
                ResultsTableView.AddColumn(column);
            }

            ResultsTableView.DataSource = resultTableSource;
            ResultsTableView.Delegate = resultTableDelegate;
            ResultsTableView.ReloadData();
            ResizeResultsTableForHorizontalScrolling();
            SyncTableSelection(DataManager.SelectedResultSolution);
        }

        void ResizeResultsTableForHorizontalScrolling()
        {
            if (resultTableSource == null) return;

            var columns = ResultsTableView.TableColumns();
            var intercellWidth = ResultsTableView.IntercellSpacing.Width * Math.Max(columns.Length - 1, 0);
            var tableWidth = columns.Sum(column => column.Width) + intercellWidth;
            var visibleWidth =
                ResultsTableView.EnclosingScrollView?.ContentSize.Width
                ?? ResultsTableView.Frame.Width;
            var headerHeight =
                ResultsTableView.HeaderView?.Frame.Height ?? 0;
            var tableHeight = Math.Max(
                ResultsTableView.Frame.Height,
                headerHeight
                + ResultsTableView.RowHeight
                * resultTableSource.Data.Count);

            ResultsTableView.Frame = new CGRect(
                0,
                0,
                Math.Max(tableWidth, visibleWidth),
                tableHeight);
        }

        void SyncTableSelection(SolutionInterface solution)
        {
            if (ResultsTableView == null
                || resultTableSource == null) return;

            var row = solution == null
                ? -1
                : resultTableSource.Data.IndexOf(solution);
            if (row < 0)
            {
                ResultsTableView.DeselectAll(this);
                return;
            }

            ResultsTableView.SelectRow(row, false);
            ResultsTableView.ScrollRowToVisible(row);
        }

        void ResetEvaluationTemperature()
        {
            evaluationTextUsesKelvin = UseKelvin;
            if (analysisResult == null)
            {
                evaluationTemperatureText = string.Empty;
                return;
            }

            SetEvaluationTemperatureText(
                AnalysisResultParameterEvaluator
                    .DefaultEvaluationTemperatureCelsius(
                        analysisResult));
        }

        void ApplyTemperatureUnit(bool useKelvin)
        {
            var temperatureCelsius =
                TryReadEvaluationTemperature(out var parsed)
                    ? parsed
                    : analysisResult == null
                        ? 25
                        : AnalysisResultParameterEvaluator
                            .DefaultEvaluationTemperatureCelsius(
                                analysisResult);

            CurrentUseKelvin = useKelvin;
            NSUserDefaults.StandardUserDefaults.SetBool(
                useKelvin,
                TemperatureUnitPreferenceKey);
            NSUserDefaults.StandardUserDefaults.Synchronize();
            evaluationTextUsesKelvin = useKelvin;
            SetEvaluationTemperatureText(temperatureCelsius);
            QueueRefresh();
            NotifyDisplayOptionsDidChange();
        }

        bool TryReadEvaluationTemperature(
            out double temperatureCelsius)
        {
            temperatureCelsius = 0;
            var text = evaluationTemperatureText?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return false;

            if (!double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out var displayed)
                && !double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out displayed))
            {
                return false;
            }

            temperatureCelsius =
                evaluationTextUsesKelvin
                    ? displayed - 273.15
                    : displayed;
            return true;
        }

        void SetEvaluationTemperatureText(
            double temperatureCelsius)
        {
            var displayed = evaluationTextUsesKelvin
                ? temperatureCelsius + 273.15
                : temperatureCelsius;
            evaluationTemperatureText =
                displayed.ToString(
                    "G6",
                    CultureInfo.CurrentCulture);
        }

        void NotifyDisplayOptionsDidChange()
        {
            DisplayOptionsDidChange?.Invoke(this, EventArgs.Empty);
        }

        static string GraphModeTitle(
            ResultGraphView.ResultGraphType type)
        {
            return type switch
            {
                ResultGraphView.ResultGraphType.TemperatureDependence =>
                    "Temperature",
                ResultGraphView.ResultGraphType.IonicStrengthDependence =>
                    "Salt",
                ResultGraphView.ResultGraphType.ProtonationAnalysis =>
                    "Protonation",
                ResultGraphView.ResultGraphType.SelectedFit =>
                    "Selected Fit",
                _ => "Parameters",
            };
        }

        string FormatTemperature(double celsius)
        {
            var value = celsius + (UseKelvin ? 273.15 : 0);
            return $"{value:G3} {(UseKelvin ? "K" : "°C")}";
        }

        static string OptionValue(
            AttributeKey key,
            ExperimentAttribute option)
        {
            if (option == null) return "";

            if (key == AttributeKey.PreboundLigandConc
                && option.BoolValue)
            {
                return "From experiment attribute";
            }

            var type = key.GetProperties().Type;
            if (type == ExperimentAttribute.AttributeType.Parameter
                || type
                    == ExperimentAttribute.AttributeType.ParameterAffinity
                || type
                    == ExperimentAttribute.AttributeType.ParameterConcentration)
            {
                var display =
                    AnalysisInspectorDisplayCatalog.OptionDisplayValue(
                        option);
                var formatted = new FloatWithError(
                        display.value,
                        display.error)
                    .AsNumber(AppSettings.UncertaintyDisplayStyle);
                var unit =
                    AnalysisInspectorDisplayCatalog.OptionUnit(key);
                return string.IsNullOrWhiteSpace(unit)
                    ? formatted
                    : formatted + " " + unit;
            }

            return key switch
            {
                AttributeKey.NumberOfSites1
                    or AttributeKey.NumberOfSites2 =>
                    StoichiometryOptions.FormatAsTitle(
                        option.DoubleValue > 0
                            ? option.DoubleValue
                            : option.IntValue),
                _ => option.GetDisplayValue(),
            };
        }

        static NSTextAlignment TextAlignment(
            AnalysisResultColumnAlignment alignment)
        {
            return alignment switch
            {
                AnalysisResultColumnAlignment.Left =>
                    NSTextAlignment.Left,
                AnalysisResultColumnAlignment.Center =>
                    NSTextAlignment.Center,
                _ => NSTextAlignment.Right,
            };
        }

        NSStackView RegisterInspectorPage(NSStackView stack)
        {
            if (stack == null) return null;
            pageSpacers[stack] =
                stack.ArrangedSubviews.LastOrDefault();
            return stack;
        }

        void ClearPage(NSStackView stack)
        {
            if (stack == null) return;
            if (!pageSpacers.TryGetValue(stack, out var spacer))
            {
                spacer = stack.ArrangedSubviews.LastOrDefault();
                pageSpacers[stack] = spacer;
            }

            foreach (var view in stack.ArrangedSubviews.ToArray())
            {
                if (ReferenceEquals(view, spacer)) continue;
                stack.RemoveArrangedSubview(view);
                view.RemoveFromSuperview();
                view.Dispose();
            }
        }

        void AddPageView(NSStackView stack, NSView view)
        {
            if (stack == null || view == null) return;
            if (!pageSpacers.TryGetValue(stack, out var spacer))
            {
                spacer = stack.ArrangedSubviews.LastOrDefault();
                pageSpacers[stack] = spacer;
            }

            var index = spacer == null
                ? stack.ArrangedSubviews.Length
                : Array.IndexOf(stack.ArrangedSubviews, spacer);
            if (index < 0) index = stack.ArrangedSubviews.Length;

            stack.InsertArrangedSubview(view, index);
            view.WidthAnchor.ConstraintEqualToAnchor(
                stack.WidthAnchor).Active = true;
        }

        static NSView Section(
            string title,
            params NSView[] controls)
        {
            var stack = new NSStackView
            {
                Orientation =
                    NSUserInterfaceLayoutOrientation.Vertical,
                Distribution = NSStackViewDistribution.Fill,
                Alignment = NSLayoutAttribute.Width,
                Spacing = 4,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };

            var header = Label(
                title,
                NSFont.SystemFontOfSize(
                    NSFont.SystemFontSize,
                    NSFontWeight.Semibold),
                NSColor.Label);
            stack.AddArrangedSubview(header);
            header.WidthAnchor.ConstraintEqualToAnchor(
                stack.WidthAnchor).Active = true;

            foreach (var control in controls.Where(control => control != null))
            {
                stack.AddArrangedSubview(control);
                control.WidthAnchor.ConstraintEqualToAnchor(
                    stack.WidthAnchor).Active = true;
            }

            var dividerContainer = new NSView
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            dividerContainer.HeightAnchor
                .ConstraintEqualToConstant(11)
                .Active = true;
            var divider = new NSBox
            {
                BoxType = NSBoxType.NSBoxSeparator,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            dividerContainer.AddSubview(divider);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                divider.LeadingAnchor.ConstraintEqualToAnchor(
                    dividerContainer.LeadingAnchor),
                divider.TrailingAnchor.ConstraintEqualToAnchor(
                    dividerContainer.TrailingAnchor),
                divider.CenterYAnchor.ConstraintEqualToAnchor(
                    dividerContainer.CenterYAnchor),
            });
            stack.AddArrangedSubview(dividerContainer);
            dividerContainer.WidthAnchor.ConstraintEqualToAnchor(
                stack.WidthAnchor).Active = true;

            return stack;
        }

        static NSView Pair(
            string label,
            string value)
        {
            var labelField = Label(
                label,
                NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                NSColor.SecondaryLabel);
            labelField.SetContentHuggingPriorityForOrientation(
                251,
                NSLayoutConstraintOrientation.Horizontal);
            labelField.SetContentCompressionResistancePriority(
                750,
                NSLayoutConstraintOrientation.Horizontal);

            var valueField = Label(
                value ?? "",
                NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                NSColor.Label);
            valueField.Alignment = NSTextAlignment.Right;
            valueField.SetContentHuggingPriorityForOrientation(
                249,
                NSLayoutConstraintOrientation.Horizontal);
            valueField.SetContentCompressionResistancePriority(
                250,
                NSLayoutConstraintOrientation.Horizontal);

            return HorizontalStack(8, labelField, valueField);
        }

        static NSView LabeledControl(
            string label,
            NSView control)
        {
            var labelField = Label(
                label,
                NSFont.SystemFontOfSize(NSFont.SystemFontSize),
                NSColor.SecondaryLabel);
            labelField.WidthAnchor
                .ConstraintGreaterThanOrEqualToConstant(92)
                .Active = true;
            labelField.SetContentHuggingPriorityForOrientation(
                251,
                NSLayoutConstraintOrientation.Horizontal);
            control.SetContentHuggingPriorityForOrientation(
                249,
                NSLayoutConstraintOrientation.Horizontal);
            return HorizontalStack(8, labelField, control);
        }

        static NSView Message(string text)
        {
            return Label(
                text ?? "",
                NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                NSColor.SecondaryLabel);
        }

        static NSTextField Label(
            string text,
            NSFont font,
            NSColor color)
        {
            var label = new NSTextField
            {
                StringValue = text ?? "",
                Bordered = false,
                Editable = false,
                Selectable = true,
                DrawsBackground = false,
                Font = font,
                TextColor = color,
                LineBreakMode = NSLineBreakMode.ByWordWrapping,
                UsesSingleLineMode = false,
                MaximumNumberOfLines = 0,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            label.Cell.Wraps = true;
            label.Cell.UsesSingleLineMode = false;
            return label;
        }

        static NSTextField TextField(string text)
        {
            return new NSTextField
            {
                StringValue = text ?? "",
                BezelStyle = NSTextFieldBezelStyle.Rounded,
                Bezeled = true,
                DrawsBackground = true,
                Editable = true,
                Selectable = true,
                Alignment = NSTextAlignment.Right,
                ControlSize = NSControlSize.Regular,
                Font = NSFont.SystemFontOfSize(
                    NSFont.SystemFontSize),
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
        }

        static NSPopUpButton Popup(
            IEnumerable<string> titles,
            int selectedIndex)
        {
            var popup = new NSPopUpButton
            {
                ControlSize = NSControlSize.Regular,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            popup.AddItems(titles?.ToArray() ?? Array.Empty<string>());
            if (popup.ItemCount > 0)
            {
                popup.SelectItem(
                    Math.Max(
                        0,
                        Math.Min(
                            selectedIndex,
                            (int)popup.ItemCount - 1)));
            }
            return popup;
        }

        static NSButton Button(string title)
        {
            return new NSButton
            {
                Title = title ?? "",
                BezelStyle = NSBezelStyle.Rounded,
                ControlSize = NSControlSize.Regular,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
        }

        static NSStackView HorizontalStack(
            nfloat spacing,
            params NSView[] views)
        {
            var stack = new NSStackView
            {
                Orientation =
                    NSUserInterfaceLayoutOrientation.Horizontal,
                Distribution = NSStackViewDistribution.Fill,
                Alignment = NSLayoutAttribute.FirstBaseline,
                Spacing = spacing,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            foreach (var view in views.Where(view => view != null))
                stack.AddArrangedSubview(view);
            return stack;
        }

        static NSView ButtonRow(params NSButton[] buttons)
        {
            return HorizontalStack(6, buttons.Cast<NSView>().ToArray());
        }

        static NSView EqualButtonRow(params NSButton[] buttons)
        {
            var row = HorizontalStack(
                6,
                buttons.Cast<NSView>().ToArray());
            row.Distribution =
                NSStackViewDistribution.FillEqually;
            return row;
        }

        [Export("UseResultTemperatureCelsius:")]
        public void UseResultTemperatureCelsius(NSObject sender)
        {
            ApplyTemperatureUnit(false);
        }

        [Export("UseResultTemperatureKelvin:")]
        public void UseResultTemperatureKelvin(NSObject sender)
        {
            ApplyTemperatureUnit(true);
        }

        [Export("RefreshAnalysisResultDisplay:")]
        public void RefreshAnalysisResultDisplay(NSObject sender)
        {
            QueueRefresh();
        }

        [Export("CopyToClipboard:")]
        public void CopyToClipboard(NSObject sender)
        {
            AppDelegate.LaunchResultExporter();
        }

        void UnsubscribeEvents()
        {
            if (eventsUnsubscribed || !eventsSubscribed) return;
            eventsUnsubscribed = true;

            DataManager.AnalysisResultSelected -=
                DataManager_AnalysisResultSelected;
            DataManager.ResultSolutionSelectionDidChange -=
                DataManager_ResultSolutionSelectionDidChange;
            ResultAnalysisController.AnalysisStarted -=
                ResultAnalysisStarted;
            ResultAnalysisController.IterationFinished -=
                ResultAnalysisProgressReport;
            ResultAnalysisController.AnalysisFinished -=
                ResultsAnalysisCompleted;
            AppDelegate.StartPrintOperation -=
                AppDelegate_StartPrintOperation;
            RefreshDisplayRequested -=
                AnalysisResultTabViewController_RefreshDisplayRequested;
            TemperatureUnitRequested -=
                AnalysisResultTabViewController_TemperatureUnitRequested;
            AppSettings.SettingsDidUpdate -=
                AppSettings_SettingsDidUpdate;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnsubscribeEvents();
                if (ResultsTableView != null)
                {
                    ResultsTableView.DataSource = null;
                    ResultsTableView.Delegate = null;
                }
                resultTableSource?.Dispose();
                resultTableDelegate?.Dispose();
            }

            base.Dispose(disposing);
        }

    }
}
