using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Avalonia.Printing;
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

        readonly ComboBox modeCombo = Combo(new[] { "Single experiment", "Multiple experiments" }, 190);
        readonly ComboBox modelCombo = Combo(190);
        readonly ComboBox algorithmCombo = Combo(new[] { "Nelder-Mead", "Levenberg-Marquardt" }, 190);
        readonly ComboBox errorMethodCombo = Combo(new[] { "None", "Bootstrap residuals", "Leave-one-out", "Profile likelihood" }, 190);
        readonly TextBox bootstrapIterationsBox = TextBox("100");
        readonly CheckBox weightedFitCheck = Check(
            "Weight by injection error",
            false,
            "Weight each data point by its estimated injection uncertainty during fitting. Every included point must have a finite peak-area SD larger than zero.");
        readonly CheckBox concentrationUncertaintyCheck = Check(
            "Concentration uncertainty",
            false,
            "Include cell and syringe concentration uncertainty during residual-bootstrap error estimation.");
        readonly CheckBox unlockParametersCheck = Check("Unlock parameters", false, "Unlock locked parameters during the error estimation pass.");
        readonly ComboBox parameterLimitsCombo = Combo(new[] { "Standard", "Expanded", "No limits" }, 190);
        readonly CheckBox createResultCheck = Check("Create analysis result", true, "Save the fit as an analysis result when fitting completes.");
        readonly CheckBox autoOpenResultCheck = Check("Auto-open new result", true, "Open the newly created analysis result after a successful fit.");
        readonly Button runFitButton = Button("Run Fit", 92);
        readonly Button stopFitButton = Button("Stop", 70);
        readonly Button restoreDefaultsButton = Button("Restore defaults", 124);
        readonly TextBlock analysisSummaryText = Text("No analysis ready");
        readonly TextBlock fitStatusText = Text();

        readonly StackPanel parameterPanel = WorkspaceControlBuilder.InspectorPanel();
        readonly StackPanel optionPanel = WorkspaceControlBuilder.InspectorPanel();

        readonly CheckBox fitCheck = Check("Fit line", true, "Draw the fitted model curve.");
        readonly CheckBox residualsCheck = Check("Residuals", true, "Show differences between observed and fitted heats.");
        readonly CheckBox errorBarsCheck = Check("Error bars", true, "Draw uncertainty bars for integrated heats.");
        readonly CheckBox confidenceCheck = Check("Confidence band", true, "Draw the confidence interval around the fitted curve.");
        readonly CheckBox labelsCheck = Check("Point labels", true, "Label each plotted injection point.");
        readonly CheckBox parametersCheck = Check("Parameter box", true, "Show the fitted parameter summary on the graph.");
        readonly CheckBox excludedCheck = Check("Excluded points", true, "Show injections excluded from the fit.");
        readonly CheckBox scaleIncludedCheck = Check("Scale to included", true, "Calculate automatic graph limits from included points only.");
        readonly CheckBox unifiedXCheck = Check("Unified X axis", false, "Use the same x-axis range for comparable graphs.");
        readonly CheckBox unifiedYCheck = Check("Unified Y axis", false, "Use the same y-axis range for comparable graphs.");
        readonly CheckBox offsetCheck = Check("Show fitted offset", true, "Display the fitted baseline offset on the graph.");
        readonly ComboBox fitLineInterpolationCombo = Combo(new[] { "Linear", "Smooth" }, 170);
        readonly CheckBox displayModelCheck = Check("Model parameters", true, "Show parameters defined by the selected model.");
        readonly CheckBox displayFittedCheck = Check("Fitted parameters", true, "Show parameters optimized by the fit.");
        readonly CheckBox displayDerivedCheck = Check("Derived parameters", true, "Show parameters calculated from the fitted values.");
        readonly AnalysisModel[] modelChoices = AnalysisModelAttribute.GetAll().ToArray();

        ExperimentData? experiment;
        SolverInterface? activeSolver;
        ErrorEstimationMethod activeErrorMethod;
        bool isUpdatingControls;
        bool isFitting;
        bool experimentSubscribed;
        int experimentRefreshGeneration;
        bool experimentRefreshQueued;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? GraphChanged;
        public event EventHandler? FittingChanged;

        public bool IsGlobalMode => modeCombo.SelectedIndex == 1 && GlobalModeAvailable();

        internal CheckBox UnlockParametersCheck => unlockParametersCheck;
        internal CheckBox ConcentrationUncertaintyCheck => concentrationUncertaintyCheck;
        internal CheckBox WeightedFitCheckForTesting => weightedFitCheck;
        internal ComboBox ErrorMethodComboForTesting => errorMethodCombo;
        internal TextBox BootstrapIterationsBoxForTesting => bootstrapIterationsBox;
        internal ComboBox ModeComboForTesting => modeCombo;
        internal ComboBox ModelComboForTesting => modelCombo;
        internal StackPanel ParameterPanelForTesting => parameterPanel;
        internal StackPanel OptionPanelForTesting => optionPanel;
        internal AnalysisContext? ContextForTesting => workspace.Context;

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
                CancelQueuedExperimentRefresh();
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

        internal bool TryGetPrintTarget(out GraphPrintTarget? target)
        {
            if (experiment?.Processor?.IntegrationCompleted == true)
            {
                target = GraphPrintTarget.FromVisual($"{experiment.Name} – Analysis", graph);
                return true;
            }

            target = null;
            return false;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            workspace.ContextRebuilt += OnContextRebuilt;
            workspace.ContextInvalidated += OnContextInvalidated;
            workspace.RebuildFailed += OnRebuildFailed;
            DataManager.DataInclusionDidChange += OnDataInclusionDidChange;
            SolverInterface.AnalysisFinished += OnAnalysisFinished;
            SolverInterface.AnalysisStepFinished += OnAnalysisStepFinished;
            SolverInterface.ErrorEstimationIterationCompleted += OnErrorIteration;
            SolverInterface.SolverUpdated += OnSolverUpdated;

            SubscribeExperiment();
            RefreshWorkspaceViews();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            UnsubscribeExperiment();
            CancelQueuedExperimentRefresh();
            DataManager.DataInclusionDidChange -= OnDataInclusionDidChange;
            workspace.ContextRebuilt -= OnContextRebuilt;
            workspace.ContextInvalidated -= OnContextInvalidated;
            workspace.RebuildFailed -= OnRebuildFailed;
            SolverInterface.AnalysisFinished -= OnAnalysisFinished;
            SolverInterface.AnalysisStepFinished -= OnAnalysisStepFinished;
            SolverInterface.ErrorEstimationIterationCompleted -= OnErrorIteration;
            SolverInterface.SolverUpdated -= OnSolverUpdated;

            base.OnDetachedFromVisualTree(e);
        }

        void BuildLayout()
        {
            SyncFittingControls();
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
                    fitStatusText,
                    analysisSummaryText
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
                weightedFitCheck,
                concentrationUncertaintyCheck,
                unlockParametersCheck
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
            errorMethodCombo.SelectionChanged += (_, _) => ChangeErrorMethod();
            concentrationUncertaintyCheck.IsCheckedChanged += (_, _) => ChangeConcentrationUncertainty();
            unlockParametersCheck.IsCheckedChanged += (_, _) => ChangeUnlockParameters();
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
                QueueExperimentRefresh(experiment, requireCompletedBaseline: false);
                UpdateStatus();
            };
        }

        void SubscribeExperiment()
        {
            if (experiment == null || experimentSubscribed) return;

            experiment.ProcessingUpdated += ExperimentProcessingChanged;
            experiment.SolutionChanged += ExperimentSolutionChanged;
            experiment.InjectionIncludeChanged += ExperimentPointInclusionChanged;
            experimentSubscribed = true;
        }

        void UnsubscribeExperiment()
        {
            if (experiment == null || !experimentSubscribed) return;

            experiment.ProcessingUpdated -= ExperimentProcessingChanged;
            experiment.SolutionChanged -= ExperimentSolutionChanged;
            experiment.InjectionIncludeChanged -= ExperimentPointInclusionChanged;
            experimentSubscribed = false;
        }

        void ExperimentProcessingChanged(object? sender, EventArgs e)
        {
            QueueExperimentRefresh(sender as ExperimentData, requireCompletedBaseline: true);
        }

        void ExperimentSolutionChanged(object? sender, EventArgs e)
        {
            QueueExperimentRefresh(sender as ExperimentData, requireCompletedBaseline: true);
        }

        void ExperimentPointInclusionChanged(object? sender, EventArgs e)
        {
            QueueExperimentRefresh(sender as ExperimentData, requireCompletedBaseline: false);
        }

        void QueueExperimentRefresh(ExperimentData? source, bool requireCompletedBaseline)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => QueueExperimentRefresh(source, requireCompletedBaseline));
                return;
            }

            var current = experiment;
            if (current == null || (source != null && !ReferenceEquals(source, current))) return;
            if (requireCompletedBaseline && !current.Processor.BaselineCompleted) return;
            if (experimentRefreshQueued) return;

            var generation = experimentRefreshGeneration;
            experimentRefreshQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != experimentRefreshGeneration || !ReferenceEquals(current, experiment))
                    return;

                experimentRefreshQueued = false;
                RebuildAnalysisContext();
                graph.FitToData();
                UpdateStatus();
                GraphChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        void CancelQueuedExperimentRefresh()
        {
            experimentRefreshGeneration++;
            experimentRefreshQueued = false;
        }

        void OnDataInclusionDidChange(object? sender, ExperimentData? e)
        {
            Dispatcher.UIThread.Post(RefreshIncludedDataState);
        }

        public void RefreshIncludedDataState()
        {
            var globalAvailable = GlobalModeAvailable();

            if (!globalAvailable && modeCombo.SelectedIndex == 1)
            {
                var wasUpdatingControls = isUpdatingControls;
                isUpdatingControls = true;
                try
                {
                    modeCombo.SelectedIndex = 0;
                }
                finally
                {
                    isUpdatingControls = wasUpdatingControls;
                }
            }

            RebuildAnalysisContext();
            graph.FitToData();
            UpdateStatus();
        }

        void SyncFittingControls()
        {
            var wasUpdatingControls = isUpdatingControls;
            isUpdatingControls = true;
            try
            {
                algorithmCombo.SelectedIndex =
                    FittingOptionsController.Algorithm == SolverAlgorithm.LevenbergMarquardt ? 1 : 0;
                errorMethodCombo.SelectedIndex = FittingOptionsController.ErrorEstimationMethod switch
                {
                    ErrorEstimationMethod.BootstrapResiduals => 1,
                    ErrorEstimationMethod.LeaveOneOut => 2,
                    ErrorEstimationMethod.ProfileLikelihood => 3,
                    _ => 0,
                };
                weightedFitCheck.IsChecked = FittingOptionsController.UseErrorWeightedFitting;
                concentrationUncertaintyCheck.IsChecked = FittingOptionsController.IncludeConcentrationVariance;
                unlockParametersCheck.IsChecked = FittingOptionsController.UnlockBootstrapParameters;
                bootstrapIterationsBox.Text =
                    FittingOptionsController.BootstrapIterations.ToString(CultureInfo.CurrentCulture);
            }
            finally
            {
                isUpdatingControls = wasUpdatingControls;
            }

            UpdateErrorEstimationControlState();
        }

        void RebuildAnalysisContext()
        {
            if (isFitting) return;

            RefreshModelChoices();

            if (experiment == null)
            {
                parameterPanel.Children.Clear();
                optionPanel.Children.Clear();
                RefreshAnalysisSummary();
                return;
            }

            var modeChanged = workspace.Session.IsGlobal != IsGlobalMode;
            workspace.SetGlobalMode(IsGlobalMode);
            if (!modeChanged && !workspace.TryRebuild())
                RefreshWorkspaceViews();
            else if (modeChanged && !AnalysisInputsAreReady())
                RefreshWorkspaceViews();
        }

        void OnContextRebuilt(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(RefreshWorkspaceViews);
        }

        void OnContextInvalidated(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(RefreshWorkspaceViews);
        }

        void OnRebuildFailed(object? sender, Exception e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                analysisSummaryText.Text = "No analysis ready";
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
            RefreshAnalysisSummary();
            UpdateStatus();
            UpdateFitButtonState();
            graph.InvalidateVisual();
        }

        void RefreshAnalysisSummary()
        {
            analysisSummaryText.Text = AnalysisInputsAreReady() && workspace.IsReady
                ? AnalysisContextSummaryPresentation.BuildText(workspace.Context)
                : "No analysis ready";
        }

        bool AnalysisInputsAreReady()
        {
            var requiredData = workspace.Session.IsGlobal
                ? DataManager.IncludedData.ToList()
                : experiment == null
                    ? new List<ExperimentData>()
                    : new List<ExperimentData> { experiment };

            return requiredData.Count > 0
                && (!workspace.Session.IsGlobal || requiredData.Count > 1)
                && requiredData.All(AnalysisBuilder.IsAnalysisReady);
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

            RefreshModelChoices();
            RebuildAnalysisContext();
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

            foreach (var parameter in workspace.Context.ExposedParameters
                .Where(parameter => IsParameterApplicable(parameter.Key)))
                parameterPanel.Children.Add(BuildParameterRow(parameter));
        }

        void AddConstraintRows()
        {
            var descriptors = workspace.Context.ExposedConstraintFamilies
                .Where(descriptor => IsParameterApplicable(descriptor.Key))
                .ToList();
            if (descriptors.Count == 0)
            {
                descriptors = workspace.Context.ExposedConstraintOptions
                    .Where(option => IsParameterApplicable(option.Key))
                    .Select(option => new GlobalConstraintFamilyDescriptor(option.Key, new[] { option.Key }, option.Value))
                    .ToList();
            }
            if (descriptors.Count == 0) return;

            var panel = new StackPanel { Spacing = 2 };
            foreach (var descriptor in descriptors)
                panel.Children.Add(BuildConstraintRow(descriptor));

            parameterPanel.Children.Add(Section("Global constraints", new Control[] { panel }));
        }

        bool IsParameterApplicable(ParameterType key)
        {
            if (key != ParameterType.Nvalue2 || !workspace.IsReady) return true;

            var options = workspace.Context.ExposedModelOptions;
            var useSyringeCorrection = options.TryGetValue(AttributeKey.UseSyringeActiveFraction, out var syringeOption)
                && syringeOption.BoolValue;
            var shareNValues = options.TryGetValue(AttributeKey.LockDuplicateParameter, out var sharedOption)
                && sharedOption.BoolValue;

            return !useSyringeCorrection && !shareNValues;
        }

        Control BuildConstraintRow(GlobalConstraintFamilyDescriptor descriptor)
        {
            var key = descriptor.Key;
            var options = descriptor.Options;
            var combo = Combo(170);
            foreach (var option in options)
            {
                var item = new ComboBoxItem
                {
                    Tag = option,
                    Content = ConstraintDisplayName(option)
                };
                ToolTip.SetTip(item, option.GetEnumDescription());
                combo.Items.Add(item);
            }

            var selected = workspace.Context.GlobalModelParameters.GetConstraintForParameter(key);
            var index = options.ToList().IndexOf(selected);
            combo.SelectedIndex = index >= 0 ? index : 0;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not ComboBoxItem item || item.Tag is not VariableConstraint constraint) return;
                if (descriptor.IsFamily && workspace.Session.ModelType == AnalysisModel.SequentialBindingSites)
                    workspace.SetSequentialConstraintFamily(key, constraint);
                else
                    workspace.SetConstraint(key, constraint);
                fitStatusText.Text = $"{key.GetProperties().Name}: {constraint.GetEnumDescription()}";
                FittingChanged?.Invoke(this, EventArgs.Empty);
            };

            return Labeled(ConstraintFamilyLabel(descriptor), combo);
        }

        static string ConstraintFamilyLabel(GlobalConstraintFamilyDescriptor descriptor)
        {
            if (!descriptor.IsFamily) return descriptor.Key.GetProperties().Name;
            if (ThermodynamicParameterSlots.TryResolve(descriptor.Key, out _, out var family))
            {
                if (family == ThermodynamicParameterFamily.Affinity) return "Affinity";
                if (family == ThermodynamicParameterFamily.Enthalpy) return "Enthalpy";
            }
            return descriptor.Key.GetProperties().Name;
        }

        static string ConstraintDisplayName(VariableConstraint constraint)
        {
            return constraint == VariableConstraint.TemperatureDependent
                ? "Temp. dependent"
                : constraint.GetEnumDescription();
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
                FittingOptionsController.IncludeConcentrationVariance = concentrationUncertaintyCheck.IsChecked == true;
                FittingOptionsController.UnlockBootstrapParameters = unlockParametersCheck.IsChecked == true;
                var requestedWeightedFitting = weightedFitCheck.IsChecked == true;
                var useErrorWeightedFitting = requestedWeightedFitting
                    && CanUseErrorWeightedFitting();
                var solver = workspace.PrepareForSolve(useErrorWeightedFitting);
                solver.SolverAlgorithm = SelectedAlgorithm();
                solver.ErrorEstimationMethod = SelectedErrorMethod();
                solver.BootstrapIterations = BootstrapIterations();

                // Only enter the fitting state after all preflight checks have
                // succeeded, so an out-of-range starting value leaves Run Fit
                // available and the editors untouched.
                isFitting = true;
                UpdateFitButtonState();

                FittingOptionsController.Algorithm = solver.SolverAlgorithm;
                FittingOptionsController.ErrorEstimationMethod = solver.ErrorEstimationMethod;
                FittingOptionsController.BootstrapIterations = solver.BootstrapIterations;
                FittingOptionsController.UseErrorWeightedFitting = requestedWeightedFitting;

                activeSolver = solver;
                activeErrorMethod = solver.ErrorEstimationMethod;

                var fitDescription = DescribeFit(solver);
                fitStatusText.Text = fitDescription;
                StatusBarManager.SetStatus(fitDescription, 0, priority: 1);
                StatusChanged?.Invoke(this, fitDescription);
                AppEventHandler.PrintAndLog(
                    $"Fit started: {DescribeFitScope(solver)}, model={DescribeFitModel(solver)}, optimizer={solver.SolverAlgorithm.GetProperties().ShortName}, errors={solver.ErrorEstimationMethod}");

                solver.Analyze();
            }
            catch (Exception ex)
            {
                if (ex is InitialParameterLimitException limitException)
                    FocusFirstInitialLimitViolation(limitException);
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

        void FocusFirstInitialLimitViolation(InitialParameterLimitException exception)
        {
            var editableKeys = workspace.Context?.ExposedParameters
                .Select(parameter => parameter.Key)
                .ToHashSet() ?? new HashSet<ParameterType>();
            var violation = exception.Violations.FirstOrDefault(item => editableKeys.Contains(item.Parameter));
            if (violation == null) return;

            parameterPanel.GetVisualDescendants()
                .OfType<TextBox>()
                .FirstOrDefault(editor => Equals(editor.Tag, violation.Parameter))
                ?.Focus();
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

            RunOnUiThread(() =>
            {
                if (!ReferenceEquals(sender, activeSolver)) return;

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

                var finishedProfileStatus = activeErrorMethod == ErrorEstimationMethod.ProfileLikelihood
                    ? (activeSolver is Solver singleSolver
                        ? ProfileLikelihoodDisplayFormatter.CompactSummary(
                            singleSolver.Model?.Solution?.ProfileLikelihoodRun)
                        : activeSolver is GlobalSolver globalSolver
                            ? ProfileLikelihoodDisplayFormatter.CompactSummary(
                                ProfileLikelihoodEstimator.Summarize(globalSolver.Model?.Solution))
                            : "Profile status: Unavailable | 95% CI endpoints: Not applicable | Profile calculation time: Not applicable")
                    : string.Empty;
                var finishedErrorMethod = activeErrorMethod;

                activeSolver = null;
                activeErrorMethod = ErrorEstimationMethod.None;
                graph.FitToData();
                RefreshWorkspaceViews();
                FittingChanged?.Invoke(this, EventArgs.Empty);

                var completionMessage = convergence.Message;
                StatusBarManager.ClearAppStatus();
                StatusBarManager.QueueStatus(completionMessage, 3000);
                var boundaryWarning = ParameterBoundaryWarningFormatter.Format(convergence.ParameterBoundaryContacts);
                if (!string.IsNullOrWhiteSpace(boundaryWarning))
                    StatusBarManager.QueueStatus(boundaryWarning, 5000);
                StatusBarManager.QueueStatus($"{convergence.Iterations} iterations | {elapsed}", 3000);
                if (convergence.Success)
                    StatusBarManager.QueueStatus($"{convergence.Algorithm.GetProperties().ShortName} | RMSD = {convergence.Loss:G4}", 2000);
                if (convergence.ErrorEstimationOutcome != ErrorEstimationOutcome.None)
                {
                    var errorStatus = finishedErrorMethod == ErrorEstimationMethod.ProfileLikelihood
                        ? finishedProfileStatus
                        : convergence.ErrorEstimationSummary;
                    StatusBarManager.QueueStatus(errorStatus, 2000);
                }
                StatusChanged?.Invoke(this, completionMessage);
            });
        }

        void OnAnalysisStepFinished(object? sender, EventArgs e)
        {
            if (!isFitting) return;

            RunOnUiThread(() =>
            {
                if (!isFitting) return;

                graph.InvalidateVisual();
                if (activeErrorMethod == ErrorEstimationMethod.None) return;

                fitStatusText.Text = $"Starting {DescribeErrorMethod(activeErrorMethod)}...";
                StatusBarManager.SetStatus(fitStatusText.Text, 0, priority: 1);
            });
        }

        void OnErrorIteration(object? sender, Tuple<int, int, float> e)
        {
            if (!isFitting || activeErrorMethod == ErrorEstimationMethod.None) return;

            RunOnUiThread(() =>
            {
                if (!isFitting || activeErrorMethod == ErrorEstimationMethod.None) return;

                fitStatusText.Text = $"{DescribeErrorMethod(activeErrorMethod)} {e.Item1}/{e.Item2}";
                StatusBarManager.SetStatus(fitStatusText.Text, 0, priority: 1);
            });
        }

        void OnSolverUpdated(object? sender, SolverUpdate update)
        {
            if (!isFitting) return;

            RunOnUiThread(() =>
            {
                if (!isFitting) return;

                if (activeErrorMethod != ErrorEstimationMethod.ProfileLikelihood
                    && activeSolver is GlobalSolver globalSolver
                    && globalSolver.Model.ShouldFitIndividually && update.Progress >= 0)
                {
                    var total = globalSolver.Model.Models.Count;
                    var completed = Math.Clamp((int)Math.Round(update.Progress * total), 0, total);
                    fitStatusText.Text = $"Fitting experiments {completed}/{total}";
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
                    StatusBarManager.SetStatus(fitStatusText.Text, 0, priority: 1);
                }
            });
        }

        static void RunOnUiThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Post(action);
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
            ErrorEstimationMethod.ProfileLikelihood => "Profile likelihood",
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
                3 => ErrorEstimationMethod.ProfileLikelihood,
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
                if (!workspace.IsReady)
                    fitStatusText.Text = $"Global fit needs at least two ready included experiments ({ready}/{includedExperiments.Count})";
                else
                    fitStatusText.Text = InitialLimitStatus(workspace.Context.DetectInitialParameterLimitViolations());
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
            var initialLimitStatus = workspace.IsReady
                ? InitialLimitStatus(workspace.Context.DetectInitialParameterLimitViolations())
                : string.Empty;
            fitStatusText.Text = !string.IsNullOrWhiteSpace(initialLimitStatus)
                ? initialLimitStatus
                : experiment.Solution == null
                ? $"{included}/{experiment.InjectionCount} integrated points"
                : $"{included}/{experiment.InjectionCount} points with fitted solution";
            UpdateFitButtonState();
        }

        static string InitialLimitStatus(IReadOnlyList<InitialParameterLimitViolation> violations)
        {
            if (violations == null || violations.Count == 0) return string.Empty;
            var first = violations[0];
            var subject = string.IsNullOrWhiteSpace(first.ExperimentName)
                ? first.DisplayName
                : $"{first.DisplayName} ({first.ExperimentName})";
            var formatted = AnalysisParameterRowBuilder.FormatValueAndLimits(
                first.Parameter, first.StartingValue, first.LowerBound, first.UpperBound);
            return $"Fit blocked: {violations.Count} starting value(s) outside {InitialParameterLimitViolationDetector.ActivePolicyName} Limits. First: {subject} = {formatted}. Edit, restore defaults, or widen Limits.";
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
            bootstrapIterationsBox.IsEnabled = !isFitting
                && SelectedErrorMethod() == ErrorEstimationMethod.BootstrapResiduals;
            weightedFitCheck.IsEnabled = !isFitting && CanUseErrorWeightedFitting();
            concentrationUncertaintyCheck.IsEnabled = !isFitting
                && SelectedErrorMethod() == ErrorEstimationMethod.BootstrapResiduals;
            unlockParametersCheck.IsEnabled = !isFitting
                && SelectedErrorMethod() == ErrorEstimationMethod.BootstrapResiduals;
            parameterLimitsCombo.IsEnabled = !isFitting;
            createResultCheck.IsEnabled = !isFitting && CanCreateAnalysisResult();
            autoOpenResultCheck.IsEnabled = !isFitting;
            restoreDefaultsButton.IsEnabled = !isFitting;
            parameterPanel.IsEnabled = !isFitting;
            optionPanel.IsEnabled = !isFitting;
        }

        bool CanUseErrorWeightedFitting()
        {
            return workspace.Session.IsGlobal
                ? AnalysisBuilder.CanUseErrorWeightedFitting(DataManager.IncludedData)
                : AnalysisBuilder.CanUseErrorWeightedFitting(experiment);
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
            FittingOptionsController.ResetToPreferenceDefaults();
            ModelFactory.ResetStoredAnalysisState();
            workspace.ResetStoredAnalysisState();
            SyncFittingControls();
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

        void ChangeErrorMethod()
        {
            if (isUpdatingControls) return;
            UpdateErrorEstimationControlState();
        }

        void UpdateErrorEstimationControlState()
        {
            var canConfigureBootstrap = !isFitting
                && SelectedErrorMethod() == ErrorEstimationMethod.BootstrapResiduals;
            bootstrapIterationsBox.IsEnabled = canConfigureBootstrap;
            concentrationUncertaintyCheck.IsEnabled = canConfigureBootstrap;
            unlockParametersCheck.IsEnabled = canConfigureBootstrap;
        }

        void ChangeConcentrationUncertainty()
        {
            if (isUpdatingControls) return;
            FittingOptionsController.IncludeConcentrationVariance =
                concentrationUncertaintyCheck.IsChecked == true;
        }

        void ChangeUnlockParameters()
        {
            if (isUpdatingControls) return;
            FittingOptionsController.UnlockBootstrapParameters = unlockParametersCheck.IsChecked == true;
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
