using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using CoreAnalysisWorkspace = AnalysisITC.Core.Analysis.AnalysisWorkspace;
using static AnalysisITC.Avalonia.Workspace.WorkspaceControlBuilder;

namespace AnalysisITC.Avalonia.Analysis
{
    public sealed class AnalysisWorkspaceControl : UserControl
    {
        readonly IntegratedHeatsGraphControl graph = new IntegratedHeatsGraphControl();
        readonly CoreAnalysisWorkspace workspace = new CoreAnalysisWorkspace();

        readonly ComboBox modeCombo = Combo(new[] { "Single experiment", "Global included" }, 190);
        readonly ComboBox modelCombo = Combo(190);
        readonly ComboBox algorithmCombo = Combo(new[] { "Nelder-Mead", "Levenberg-Marquardt" }, 190);
        readonly ComboBox errorMethodCombo = Combo(new[] { "None", "Bootstrap residuals", "Leave-one-out" }, 190);
        readonly TextBox bootstrapIterationsBox = TextBox("100");
        readonly CheckBox weightedFitCheck = Check("Weight by injection error", false);
        readonly ComboBox parameterLimitsCombo = Combo(new[] { "Standard", "Expanded", "No limits" }, 190);
        readonly CheckBox createResultCheck = Check("Create analysis result", true);
        readonly CheckBox autoOpenResultCheck = Check("Auto-open new result", true);
        readonly Button runFitButton = Button("Run Fit", 92);
        readonly Button stopFitButton = Button("Stop", 70);
        readonly Button restoreDefaultsButton = Button("Restore defaults", 124);
        readonly TextBlock fitStatusText = Text();

        readonly StackPanel parameterPanel = WorkspaceControlBuilder.InspectorPanel();
        readonly StackPanel optionPanel = WorkspaceControlBuilder.InspectorPanel();

        readonly CheckBox fitCheck = Check("Fit line", true);
        readonly CheckBox residualsCheck = Check("Residuals", true);
        readonly CheckBox errorBarsCheck = Check("Error bars", true);
        readonly CheckBox confidenceCheck = Check("Confidence band", true);
        readonly CheckBox labelsCheck = Check("Point labels", true);
        readonly CheckBox parametersCheck = Check("Parameter box", true);
        readonly CheckBox excludedCheck = Check("Excluded points", true);
        readonly CheckBox scaleIncludedCheck = Check("Scale to included", true);
        readonly CheckBox unifiedXCheck = Check("Unified X axis", false);
        readonly CheckBox unifiedYCheck = Check("Unified Y axis", false);
        readonly CheckBox offsetCheck = Check("Show fitted offset", true);
        readonly ComboBox fitLineInterpolationCombo = Combo(new[] { "Linear", "Smooth" }, 170);
        readonly CheckBox displayModelCheck = Check("Model parameters", true);
        readonly CheckBox displayFittedCheck = Check("Fitted parameters", true);
        readonly CheckBox displayDerivedCheck = Check("Derived parameters", true);
        readonly AnalysisModel[] modelChoices = AnalysisModelAttribute.GetAll().ToArray();

        ExperimentData? experiment;
        SolverInterface? activeSolver;
        ErrorEstimationMethod activeErrorMethod;
        bool isUpdatingControls;
        bool isFitting;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? GraphChanged;
        public event EventHandler? FittingChanged;

        public bool IsGlobalMode => modeCombo.SelectedIndex == 1 && GlobalModeAvailable();

        public AnalysisWorkspaceControl()
        {
            BuildLayout();
            WireEvents();
            RefreshModelChoices();
            ApplyGraphOptions();
            UpdateStatus();
        }

        public ExperimentData? Experiment
        {
            get => experiment;
            set
            {
                if (ReferenceEquals(experiment, value)) return;

                UnsubscribeExperiment();
                experiment = value;
                graph.Experiment = value;
                SubscribeExperiment();
                RebuildAnalysisContext();
                UpdateStatus();
            }
        }

        public void FitToData()
        {
            graph.FitToData();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            workspace.ContextRebuilt += OnContextRebuilt;
            workspace.RebuildFailed += OnRebuildFailed;
            DataManager.DataInclusionDidChange += OnDataInclusionDidChange;
            SolverInterface.AnalysisFinished += OnAnalysisFinished;
            SolverInterface.AnalysisStepFinished += OnAnalysisStepFinished;
            SolverInterface.ErrorEstimationIterationCompleted += OnErrorIteration;
            SolverInterface.SolverUpdated += OnSolverUpdated;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            UnsubscribeExperiment();
            DataManager.DataInclusionDidChange -= OnDataInclusionDidChange;
            workspace.ContextRebuilt -= OnContextRebuilt;
            workspace.RebuildFailed -= OnRebuildFailed;
            SolverInterface.AnalysisFinished -= OnAnalysisFinished;
            SolverInterface.AnalysisStepFinished -= OnAnalysisStepFinished;
            SolverInterface.ErrorEstimationIterationCompleted -= OnErrorIteration;
            SolverInterface.SolverUpdated -= OnSolverUpdated;

            base.OnDetachedFromVisualTree(e);
        }

        void BuildLayout()
        {
            algorithmCombo.SelectedIndex = FittingOptionsController.Algorithm == SolverAlgorithm.LevenbergMarquardt ? 1 : 0;
            errorMethodCombo.SelectedIndex = FittingOptionsController.ErrorEstimationMethod switch
            {
                ErrorEstimationMethod.BootstrapResiduals => 1,
                ErrorEstimationMethod.LeaveOneOut => 2,
                _ => 0,
            };
            weightedFitCheck.IsChecked = FittingOptionsController.UseErrorWeightedFitting;
            bootstrapIterationsBox.Text = FittingOptionsController.BootstrapIterations.ToString(CultureInfo.CurrentCulture);
            SyncPreferenceControls();

            var graphBorder = WorkspaceControlBuilder.ContentBorder(graph);

            var inspector = WorkspaceControlBuilder.Inspector(
                InspectorTab("Fit", BuildFitTab()),
                InspectorTab("Parameters", parameterPanel),
                InspectorTab("Options", optionPanel),
                InspectorTab("Display", BuildGraphTab()));

            Content = WorkspaceControlBuilder.Workspace(
                graphBorder,
                inspector,
                WorkspaceControlBuilder.InspectorFooter(Section("Fit", new Control[]
                {
                    Row(runFitButton, stopFitButton),
                    fitStatusText
                })));
        }

        Control BuildFitTab()
        {
            var panel = WorkspaceControlBuilder.InspectorPanel();
            panel.Children.Add(Section("Fit setup", new Control[]
            {
                Labeled("Mode", modeCombo),
                Labeled("Model", modelCombo),
                Labeled("Algorithm", algorithmCombo),
                Labeled("Errors", errorMethodCombo),
                Labeled("Bootstrap", bootstrapIterationsBox),
                Labeled("Limits", parameterLimitsCombo),
                weightedFitCheck
            }));
            panel.Children.Add(Section("Result", new Control[]
            {
                createResultCheck,
                autoOpenResultCheck
            }));
            panel.Children.Add(Section("Actions", new Control[]
            {
                restoreDefaultsButton
            }));

            return panel;
        }

        Control BuildGraphTab()
        {
            var panel = WorkspaceControlBuilder.InspectorPanel();
            panel.Children.Add(Section("Graph", new Control[]
            {
                fitCheck,
                residualsCheck,
                errorBarsCheck,
                confidenceCheck,
                labelsCheck,
                parametersCheck,
                excludedCheck,
                scaleIncludedCheck,
                unifiedXCheck,
                unifiedYCheck,
                offsetCheck
            }));
            panel.Children.Add(Section("Fit line", new Control[]
            {
                Labeled("Interpolation", fitLineInterpolationCombo)
            }));
            panel.Children.Add(Section("Parameter box", new Control[]
            {
                displayModelCheck,
                displayFittedCheck,
                displayDerivedCheck
            }));
            return panel;
        }

        void WireEvents()
        {
            modeCombo.SelectionChanged += (_, _) => ChangeMode();
            modelCombo.SelectionChanged += (_, _) => ChangeModel();
            runFitButton.Click += (_, _) => RunFit();
            stopFitButton.Click += (_, _) => StopFit();
            restoreDefaultsButton.Click += (_, _) => RestoreAnalysisDefaults();
            parameterLimitsCombo.SelectionChanged += (_, _) => ChangeParameterLimits();
            createResultCheck.IsCheckedChanged += (_, _) => ChangeCreateResult();
            autoOpenResultCheck.IsCheckedChanged += (_, _) => ChangeAutoOpenResult();
            fitLineInterpolationCombo.SelectionChanged += (_, _) => ChangeFitLineInterpolation();
            displayModelCheck.IsCheckedChanged += (_, _) => ChangeParameterDisplay(FinalFigureDisplayParameters.Model, displayModelCheck);
            displayFittedCheck.IsCheckedChanged += (_, _) => ChangeParameterDisplay(FinalFigureDisplayParameters.Fitted, displayFittedCheck);
            displayDerivedCheck.IsCheckedChanged += (_, _) => ChangeParameterDisplay(FinalFigureDisplayParameters.Derived, displayDerivedCheck);

            fitCheck.IsCheckedChanged += (_, _) => ApplyGraphOptions(refit: false);
            residualsCheck.IsCheckedChanged += (_, _) => ApplyGraphOptions(refit: true);
            errorBarsCheck.IsCheckedChanged += (_, _) => ApplyGraphOptions(refit: true);
            confidenceCheck.IsCheckedChanged += (_, _) => ApplyGraphOptions(refit: false);
            labelsCheck.IsCheckedChanged += (_, _) => ApplyGraphOptions(refit: false);
            parametersCheck.IsCheckedChanged += (_, _) => ApplyGraphOptions(refit: false);
            excludedCheck.IsCheckedChanged += (_, _) => ApplyGraphOptions(refit: true);
            scaleIncludedCheck.IsCheckedChanged += (_, _) => ApplyGraphOptions(refit: true);
            unifiedXCheck.IsCheckedChanged += (_, _) => ApplyGraphOptions(refit: true);
            unifiedYCheck.IsCheckedChanged += (_, _) => ApplyGraphOptions(refit: true);
            offsetCheck.IsCheckedChanged += (_, _) => ApplyGraphOptions(refit: true);

            graph.StatusChanged += (_, status) => StatusChanged?.Invoke(this, status);
            graph.GraphChanged += (_, _) =>
            {
                RebuildAnalysisContext();
                UpdateStatus();
                GraphChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        void SubscribeExperiment()
        {
            if (experiment == null) return;

            experiment.ProcessingUpdated += ExperimentChanged;
            experiment.SolutionChanged += ExperimentChanged;
            experiment.InjectionIncludeChanged += ExperimentChanged;
        }

        void UnsubscribeExperiment()
        {
            if (experiment == null) return;

            experiment.ProcessingUpdated -= ExperimentChanged;
            experiment.SolutionChanged -= ExperimentChanged;
            experiment.InjectionIncludeChanged -= ExperimentChanged;
        }

        void ExperimentChanged(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                RebuildAnalysisContext();
                graph.FitToData();
                UpdateStatus();
                GraphChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        void OnDataInclusionDidChange(object? sender, ExperimentData? e)
        {
            Dispatcher.UIThread.Post(RefreshIncludedDataState);
        }

        public void RefreshIncludedDataState()
        {
            var globalAvailable = GlobalModeAvailable();

            if (!globalAvailable && modeCombo.SelectedIndex == 1)
                modeCombo.SelectedIndex = 0;

            RebuildAnalysisContext();
            graph.FitToData();
            UpdateStatus();
        }

        void RebuildAnalysisContext()
        {
            if (isFitting) return;

            RefreshModelChoices();

            if (experiment == null)
            {
                parameterPanel.Children.Clear();
                optionPanel.Children.Clear();
                return;
            }

            workspace.SetGlobalMode(IsGlobalMode);
            workspace.TryRebuild();
            RefreshWorkspaceViews();
        }

        void OnContextRebuilt(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(RefreshWorkspaceViews);
        }

        void OnRebuildFailed(object? sender, Exception e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                fitStatusText.Text = e.Message;
                UpdateFitButtonState();
            });
        }

        void RefreshWorkspaceViews()
        {
            RefreshModelChoices();
            RebuildParameterRows();
            RebuildOptionRows();
            SyncPreferenceControls();
            UpdateStatus();
            UpdateFitButtonState();
            graph.InvalidateVisual();
        }

        void RefreshModelChoices()
        {
            isUpdatingControls = true;

            try
            {
                var selectedModel = workspace.Session.ModelType;

                if (modelCombo.Items.Count == 0)
                {
                    foreach (var model in modelChoices)
                    {
                        modelCombo.Items.Add(new ComboBoxItem
                        {
                            Tag = model
                        });
                    }
                }

                for (var i = 0; i < modelChoices.Length; i++)
                {
                    if (modelCombo.Items[i] is not ComboBoxItem item) continue;

                    var model = modelChoices[i];
                    var available = AnalysisBuilder.IsModelAvailable(model, workspace.Session.IsGlobal);
                    item.Content = available ? model.GetProperties().Name : model.GetProperties().Name + " (unavailable)";
                    item.IsEnabled = available;
                }

                var selectedIndex = Array.FindIndex(modelChoices, model => model == selectedModel);
                if (selectedIndex < 0) selectedIndex = 0;

                if (modelCombo.SelectedIndex != selectedIndex)
                    modelCombo.SelectedIndex = selectedIndex;
            }
            finally
            {
                isUpdatingControls = false;
            }
        }

        void ChangeModel()
        {
            if (isUpdatingControls) return;
            if (modelCombo.SelectedItem is not ComboBoxItem item || item.Tag is not AnalysisModel model || !item.IsEnabled) return;

            workspace.SetModelType(model);
            RefreshWorkspaceViews();
        }

        void ChangeMode()
        {
            if (isUpdatingControls) return;

            if (modeCombo.SelectedIndex == 1 && !GlobalModeAvailable())
            {
                fitStatusText.Text = "Global fitting needs at least two included, processed experiments";
                isUpdatingControls = true;
                modeCombo.SelectedIndex = 0;
                isUpdatingControls = false;
            }

            workspace.SetGlobalMode(IsGlobalMode);
            RefreshModelChoices();
            workspace.TryRebuild();
            RefreshWorkspaceViews();
            graph.FitToData();
            FittingChanged?.Invoke(this, EventArgs.Empty);
        }

        void RebuildParameterRows()
        {
            parameterPanel.Children.Clear();

            if (!workspace.IsReady)
            {
                parameterPanel.Children.Add(Text("No analysis model is ready."));
                return;
            }

            if (workspace.Session.IsGlobal)
                AddConstraintRows();

            foreach (var parameter in workspace.Context.ExposedParameters)
                parameterPanel.Children.Add(BuildParameterRow(parameter));
        }

        void AddConstraintRows()
        {
            var constraints = workspace.Context.ExposedConstraintOptions;
            if (constraints.Count == 0) return;

            var panel = new StackPanel { Spacing = 2 };
            foreach (var constraint in constraints)
                panel.Children.Add(BuildConstraintRow(constraint.Key, constraint.Value));

            parameterPanel.Children.Add(Section("Global constraints", new Control[] { panel }));
        }

        Control BuildConstraintRow(ParameterType key, IReadOnlyList<VariableConstraint> options)
        {
            var combo = Combo(170);
            foreach (var option in options)
            {
                combo.Items.Add(new ComboBoxItem
                {
                    Tag = option,
                    Content = option.GetEnumDescription()
                });
            }

            var selected = workspace.Session.Active.Constraints.TryGetValue(key, out var stored)
                ? stored
                : VariableConstraint.None;
            var index = options.ToList().IndexOf(selected);
            combo.SelectedIndex = index >= 0 ? index : 0;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not ComboBoxItem item || item.Tag is not VariableConstraint constraint) return;
                workspace.SetConstraint(key, constraint);
                fitStatusText.Text = $"{key.GetProperties().Name}: {constraint.GetEnumDescription()}";
                FittingChanged?.Invoke(this, EventArgs.Empty);
            };

            return Labeled(key.GetProperties().Name, combo);
        }

        Control BuildParameterRow(Parameter parameter)
        {
            return AnalysisParameterRowBuilder.Build(
                parameter,
                apply: (key, value, isLocked) =>
                {
                    workspace.SetParameterOverride(key, value, isLocked);
                    FittingChanged?.Invoke(this, EventArgs.Empty);
                },
                reset: key =>
                {
                    workspace.ResetParameterOverride(key);
                    FittingChanged?.Invoke(this, EventArgs.Empty);
                },
                setStatus: message => fitStatusText.Text = message,
                isUpdating: () => isUpdatingControls);
        }

        void RebuildOptionRows()
        {
            optionPanel.Children.Clear();

            if (!workspace.IsReady || workspace.Context.ExposedModelOptions.Count == 0)
            {
                optionPanel.Children.Add(Text("No model options for this model."));
                return;
            }

            foreach (var option in workspace.Context.ExposedModelOptions)
                optionPanel.Children.Add(ModelOptionRowBuilder.Build(
                    option.Key,
                    option.Value,
                    workspace.Context.ExposedModelOptions,
                    apply: (key, copy) =>
                    {
                        workspace.SetModelOption(key, copy);
                        FittingChanged?.Invoke(this, EventArgs.Empty);
                    },
                    setStatus: message => fitStatusText.Text = message));
        }

        public void RunFit()
        {
            if (experiment == null)
            {
                fitStatusText.Text = "No experiment selected";
                return;
            }

            if (!workspace.IsReady)
                workspace.Rebuild();

            if (!workspace.IsReady)
            {
                fitStatusText.Text = "Analysis model is not ready";
                return;
            }

            try
            {
                isFitting = true;
                UpdateFitButtonState();

                var solver = workspace.PrepareForSolve();
                solver.SolverAlgorithm = SelectedAlgorithm();
                solver.ErrorEstimationMethod = SelectedErrorMethod();
                solver.BootstrapIterations = BootstrapIterations();
                solver.UseErrorWeightedFitting = weightedFitCheck.IsChecked == true;

                FittingOptionsController.Algorithm = solver.SolverAlgorithm;
                FittingOptionsController.ErrorEstimationMethod = solver.ErrorEstimationMethod;
                FittingOptionsController.BootstrapIterations = solver.BootstrapIterations;
                FittingOptionsController.UseErrorWeightedFitting = solver.UseErrorWeightedFitting;

                activeSolver = solver;
                activeErrorMethod = solver.ErrorEstimationMethod;

                var fitDescription = DescribeFit(solver);
                fitStatusText.Text = fitDescription;
                StatusBarManager.StartInderminateProgress();
                StatusBarManager.SetStatus(fitDescription, 0, priority: 1);
                StatusChanged?.Invoke(this, fitDescription);
                AppEventHandler.PrintAndLog(
                    $"Fit started: {DescribeFitScope(solver)}, model={DescribeFitModel(solver)}, optimizer={solver.SolverAlgorithm.GetProperties().ShortName}, errors={solver.ErrorEstimationMethod}");

                solver.Analyze();
            }
            catch (Exception ex)
            {
                isFitting = false;
                activeSolver = null;
                UpdateFitButtonState();
                StatusBarManager.ClearAppStatus();
                AppEventHandler.DisplayHandledException(ex);
                fitStatusText.Text = $"Fit failed: {ex.Message}";
                AppEventHandler.PrintAndLog(fitStatusText.Text);
                StatusBarManager.SetStatus(fitStatusText.Text, 5000);
            }
        }

        public void StopFit()
        {
            SolverInterface.TerminateAnalysisFlag.Raise();
            fitStatusText.Text = "Stopping fit...";
            StatusBarManager.SetStatus("Stopping fit...", 0, priority: 3);
        }

        void OnAnalysisFinished(object? sender, SolverConvergence convergence)
        {
            if (!ReferenceEquals(sender, activeSolver)) return;

            Dispatcher.UIThread.Post(() =>
            {
                isFitting = false;
                var elapsed = TimeUnitAttribute.FormatTimeSpanShort(convergence.TotalTime);
                fitStatusText.Text = $"{convergence.Termination} | RMSD {convergence.Loss:G4} | {convergence.Iterations} iterations | {elapsed}";

                AppEventHandler.PrintAndLog(
                    $"Fit ended: outcome={convergence.Termination}, iterations={convergence.Iterations}, RMSD={convergence.Loss:G17}, optimizerTime={convergence.Time.TotalMilliseconds:0.###}ms, totalTime={convergence.TotalTime.TotalMilliseconds:0.###}ms");
                if (activeErrorMethod != ErrorEstimationMethod.None)
                {
                    AppEventHandler.PrintAndLog(
                        $"Error estimation ended: method={activeErrorMethod}, outcome={convergence.ErrorEstimationOutcome}, {convergence.ErrorEstimationSummary}, time={convergence.ErrorEstimationTime.TotalMilliseconds:0.###}ms");
                }

                activeSolver = null;
                activeErrorMethod = ErrorEstimationMethod.None;
                graph.FitToData();
                RefreshWorkspaceViews();
                FittingChanged?.Invoke(this, EventArgs.Empty);
                StatusBarManager.ClearAppStatus();
                StatusBarManager.SetStatus(fitStatusText.Text, 5000);
                StatusChanged?.Invoke(this, fitStatusText.Text);
            });
        }

        void OnAnalysisStepFinished(object? sender, EventArgs e)
        {
            if (!isFitting) return;

            Dispatcher.UIThread.Post(() =>
            {
                graph.InvalidateVisual();
                if (activeErrorMethod == ErrorEstimationMethod.None) return;

                fitStatusText.Text = $"Starting {DescribeErrorMethod(activeErrorMethod)}...";
                StatusBarManager.SetProgress(0);
                StatusBarManager.SetStatus(fitStatusText.Text, 0, priority: 1);
            });
        }

        void OnErrorIteration(object? sender, Tuple<int, int, float> e)
        {
            if (!isFitting || activeErrorMethod == ErrorEstimationMethod.None) return;

            Dispatcher.UIThread.Post(() =>
            {
                fitStatusText.Text = $"{DescribeErrorMethod(activeErrorMethod)} {e.Item1}/{e.Item2}";
                StatusBarManager.SetProgress(e.Item3);
                StatusBarManager.SetStatus(fitStatusText.Text, 0, priority: 1);
            });
        }

        void OnSolverUpdated(object? sender, SolverUpdate update)
        {
            if (!isFitting) return;

            Dispatcher.UIThread.Post(() =>
            {
                if (activeSolver is GlobalSolver globalSolver && globalSolver.Model.ShouldFitIndividually && update.Progress >= 0)
                {
                    var total = globalSolver.Model.Models.Count;
                    var completed = Math.Clamp((int)Math.Round(update.Progress * total), 0, total);
                    fitStatusText.Text = $"Fitting experiments {completed}/{total}";
                    StatusBarManager.SetProgress(update.Progress);
                    StatusBarManager.SetStatus(fitStatusText.Text, 0, priority: 1);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(update.Message))
                {
                    fitStatusText.Text = update.Message;
                    StatusBarManager.SetStatus(update.Message, 0, priority: 1);
                }
                else if (update.Progress >= 0)
                {
                    fitStatusText.Text = $"Fitting progress {update.Progress:P0}";
                    StatusBarManager.SetProgress(update.Progress);
                    StatusBarManager.SetStatus(fitStatusText.Text, 0, priority: 1);
                }
            });
        }

        static string DescribeFit(SolverInterface solver) =>
            $"Fitting {DescribeFitScope(solver)} {DescribeFitModel(solver)} using {solver.SolverAlgorithm.GetProperties().ShortName}...";

        static string DescribeFitScope(SolverInterface solver) => solver is GlobalSolver ? "global" : "single";

        static string DescribeFitModel(SolverInterface solver) => solver switch
        {
            Solver single => single.Model.ModelType.ToString(),
            GlobalSolver global => global.Model.ModelType.ToString(),
            _ => "unknown",
        };

        static string DescribeErrorMethod(ErrorEstimationMethod method) => method switch
        {
            ErrorEstimationMethod.BootstrapResiduals => "Bootstrap residuals",
            ErrorEstimationMethod.LeaveOneOut => "Leave-one-out",
            _ => "Error estimation",
        };

        SolverAlgorithm SelectedAlgorithm()
        {
            return algorithmCombo.SelectedIndex == 1
                ? SolverAlgorithm.LevenbergMarquardt
                : SolverAlgorithm.NelderMead;
        }

        ErrorEstimationMethod SelectedErrorMethod()
        {
            return errorMethodCombo.SelectedIndex switch
            {
                1 => ErrorEstimationMethod.BootstrapResiduals,
                2 => ErrorEstimationMethod.LeaveOneOut,
                _ => ErrorEstimationMethod.None,
            };
        }

        int BootstrapIterations()
        {
            return int.TryParse(bootstrapIterationsBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
                ? Math.Max(0, value)
                : FittingOptionsController.BootstrapIterations;
        }

        void ApplyGraphOptions(bool refit = false)
        {
            graph.ShowFit = fitCheck.IsChecked == true;
            graph.ShowResiduals = residualsCheck.IsChecked == true;
            graph.ShowErrorBars = errorBarsCheck.IsChecked == true;
            graph.ShowConfidenceBand = confidenceCheck.IsChecked == true;
            graph.ShowPointLabels = labelsCheck.IsChecked == true;
            graph.ShowFitParameters = parametersCheck.IsChecked == true;
            graph.ShowExcludedPoints = excludedCheck.IsChecked == true;
            graph.ScaleToIncludedPoints = scaleIncludedCheck.IsChecked == true;
            graph.UnifiedXAxis = unifiedXCheck.IsChecked == true;
            graph.UnifiedYAxis = unifiedYCheck.IsChecked == true;
            graph.DrawWithOffset = offsetCheck.IsChecked == true;
            graph.FitLineSmoothness = AnalysisFitLineSmoothness();

            if (refit) graph.FitToData();
            else graph.InvalidateVisual();
        }

        void UpdateStatus()
        {
            if (experiment == null)
            {
                fitStatusText.Text = "No experiment selected";
                UpdateFitButtonState();
                return;
            }

            if (workspace.Session.IsGlobal)
            {
                var includedExperiments = DataManager.IncludedData.ToList();
                var ready = includedExperiments.Count(AnalysisBuilder.IsAnalysisReady);
                fitStatusText.Text = workspace.IsReady
                    ? $"Global fit: {ready}/{includedExperiments.Count} included experiments ready"
                    : $"Global fit needs at least two ready included experiments ({ready}/{includedExperiments.Count})";
                UpdateFitButtonState();
                return;
            }

            if (!experiment.Processor.IntegrationCompleted)
            {
                fitStatusText.Text = "Process data before fitting";
                UpdateFitButtonState();
                return;
            }

            var included = experiment.Injections.FindAll(injection => injection.Include).Count;
            fitStatusText.Text = experiment.Solution == null
                ? $"{included}/{experiment.InjectionCount} integrated points"
                : $"{included}/{experiment.InjectionCount} points with fitted solution";
            UpdateFitButtonState();
        }

        void UpdateFitButtonState()
        {
            var canFit = experiment != null && workspace.IsReady && !isFitting && AnalysisBuilder.IsModelAvailable(workspace.Session.ModelType, workspace.Session.IsGlobal);
            runFitButton.IsEnabled = canFit;
            stopFitButton.IsEnabled = isFitting;
            modeCombo.IsEnabled = !isFitting;
            modelCombo.IsEnabled = !isFitting;
            algorithmCombo.IsEnabled = !isFitting;
            errorMethodCombo.IsEnabled = !isFitting;
            bootstrapIterationsBox.IsEnabled = !isFitting;
            weightedFitCheck.IsEnabled = !isFitting;
            parameterLimitsCombo.IsEnabled = !isFitting;
            createResultCheck.IsEnabled = !isFitting && CanCreateAnalysisResult();
            autoOpenResultCheck.IsEnabled = !isFitting;
            restoreDefaultsButton.IsEnabled = !isFitting;
            parameterPanel.IsEnabled = !isFitting;
            optionPanel.IsEnabled = !isFitting;
        }

        public bool CanRunFit => runFitButton.IsEnabled;
        public bool CanStopFit => stopFitButton.IsEnabled;

        public bool CanCreateAnalysisResult()
        {
            if (experiment == null) return false;
            return !IsGlobalMode || DataManager.Data.Count(data => data.Include) > 1;
        }

        public bool IsCreateAnalysisResultEnabled()
        {
            return IsGlobalMode ? AppSettings.CreateGlobalAnalysisResult : AppSettings.CreateSingleAnalysisResult;
        }

        public void ToggleCreateAnalysisResult()
        {
            if (IsGlobalMode)
                AppSettings.CreateGlobalAnalysisResult = !AppSettings.CreateGlobalAnalysisResult;
            else
                AppSettings.CreateSingleAnalysisResult = !AppSettings.CreateSingleAnalysisResult;

            AppSettings.Save();
            SyncPreferenceControls();
            FittingChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleAutoOpenNewResult()
        {
            AppSettings.AutoOpenNewAnalysisResult = !AppSettings.AutoOpenNewAnalysisResult;
            AppSettings.Save();
            SyncPreferenceControls();
        }

        public void SetFitLineSmoothness(LineSmoothness smoothness)
        {
            AppSettings.FitLineSmoothness = smoothness;
            AppSettings.Save();
            SyncPreferenceControls();
            graph.FitLineSmoothness = AnalysisFitLineSmoothness();
            graph.InvalidateVisual();
            GraphChanged?.Invoke(this, EventArgs.Empty);
        }

        public static LineSmoothness AnalysisFitLineSmoothness()
        {
            return AppSettings.FitLineSmoothness == LineSmoothness.Linear
                ? LineSmoothness.Linear
                : LineSmoothness.Smooth;
        }

        public void SetParameterLimitSetting(ParameterLimitSetting setting)
        {
            AppSettings.ParameterLimitSetting = setting;
            AppSettings.EnableExtendedParameterLimits = setting != ParameterLimitSetting.Standard;
            AppSettings.Save();
            SyncPreferenceControls();
            RebuildAnalysisContext();
            FittingChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleAnalysisParameterDisplay(FinalFigureDisplayParameters flag)
        {
            if (AppSettings.AnalysisParameterDisplay.HasFlag(flag))
                AppSettings.AnalysisParameterDisplay &= ~flag;
            else
                AppSettings.AnalysisParameterDisplay |= flag;

            AppSettings.Save();
            SyncPreferenceControls();
            graph.InvalidateVisual();
            GraphChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RestoreAnalysisDefaults()
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
            SyncPreferenceControls();

            RebuildAnalysisContext();
            graph.FitToData();
            fitStatusText.Text = "Analysis defaults restored";
            StatusChanged?.Invoke(this, "Analysis defaults restored");
            FittingChanged?.Invoke(this, EventArgs.Empty);
        }

        void ChangeParameterLimits()
        {
            if (isUpdatingControls || parameterLimitsCombo.SelectedIndex < 0) return;
            SetParameterLimitSetting(parameterLimitsCombo.SelectedIndex switch
            {
                1 => ParameterLimitSetting.Extended,
                2 => ParameterLimitSetting.NoLimit,
                _ => ParameterLimitSetting.Standard
            });
        }

        void ChangeCreateResult()
        {
            if (isUpdatingControls || IsCreateAnalysisResultEnabled() == (createResultCheck.IsChecked == true)) return;
            ToggleCreateAnalysisResult();
        }

        void ChangeAutoOpenResult()
        {
            if (isUpdatingControls || AppSettings.AutoOpenNewAnalysisResult == (autoOpenResultCheck.IsChecked == true)) return;
            ToggleAutoOpenNewResult();
        }

        void ChangeFitLineInterpolation()
        {
            if (isUpdatingControls || fitLineInterpolationCombo.SelectedIndex < 0) return;
            SetFitLineSmoothness(fitLineInterpolationCombo.SelectedIndex == 0 ? LineSmoothness.Linear : LineSmoothness.Smooth);
        }

        void ChangeParameterDisplay(FinalFigureDisplayParameters flag, CheckBox control)
        {
            if (isUpdatingControls || AppSettings.AnalysisParameterDisplay.HasFlag(flag) == (control.IsChecked == true)) return;
            ToggleAnalysisParameterDisplay(flag);
        }

        void SyncPreferenceControls()
        {
            isUpdatingControls = true;
            try
            {
                parameterLimitsCombo.SelectedIndex = AppSettings.ParameterLimitSetting switch
                {
                    ParameterLimitSetting.Extended => 1,
                    ParameterLimitSetting.NoLimit => 2,
                    _ => 0
                };
                createResultCheck.IsChecked = IsCreateAnalysisResultEnabled();
                createResultCheck.IsEnabled = CanCreateAnalysisResult();
                autoOpenResultCheck.IsChecked = AppSettings.AutoOpenNewAnalysisResult;
                fitLineInterpolationCombo.SelectedIndex = AnalysisFitLineSmoothness() == LineSmoothness.Linear ? 0 : 1;
                displayModelCheck.IsChecked = AppSettings.AnalysisParameterDisplay.HasFlag(FinalFigureDisplayParameters.Model);
                displayFittedCheck.IsChecked = AppSettings.AnalysisParameterDisplay.HasFlag(FinalFigureDisplayParameters.Fitted);
                displayDerivedCheck.IsChecked = AppSettings.AnalysisParameterDisplay.HasFlag(FinalFigureDisplayParameters.Derived);
            }
            finally
            {
                isUpdatingControls = false;
            }
        }

        bool GlobalModeAvailable()
        {
            var included = DataManager.IncludedData.ToList();
            return included.Count >= 2 && included.All(AnalysisBuilder.IsAnalysisReady);
        }

    }
}
