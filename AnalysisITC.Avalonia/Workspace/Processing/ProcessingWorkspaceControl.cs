using System;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Processing;
using static AnalysisITC.Avalonia.Workspace.WorkspaceControlBuilder;

namespace AnalysisITC.Avalonia.Processing
{
    public sealed class ProcessingWorkspaceControl : UserControl
    {
        readonly ProcessingGraphControl graph = new ProcessingGraphControl();
        readonly TextBlock summaryText = TrimmingText();
        readonly TextBlock baselineHeader = Header("Baseline");
        readonly TextBlock integrationHeader = Header("Integration");
        readonly TextBlock degreeLabel = TrimmingText();
        readonly TextBlock startLabel = TrimmingText();
        readonly TextBlock lengthLabel = TrimmingText();
        readonly TextBlock selectionLabel = TrimmingText();

        readonly ComboBox baselineTypeCombo = Combo(new[] { "Spline", "Polynomial", "Segmented" });
        readonly ComboBox splineAlgorithmCombo = Combo(new[] { "Linear", "Smooth" });
        readonly ComboBox splineDensityCombo = Combo(new[] { "Sparse", "Balanced", "Dense" });
        readonly ComboBox splineHandleCombo = Combo(new[] { "Mean", "Median", "Min volatility" });
        readonly NumericUpDown peakWidthStepper = Stepper(3, 1, 5, 2);

        readonly Slider degreeSlider = Slider(0, 10, 1);
        readonly Slider integrationStartSlider = Slider(-30, 30, 0.1);
        readonly Slider integrationLengthSlider = Slider(0, 120, 0.1);

        readonly CheckBox showBaselineCheck = Check("Display baseline", true, "Show the currently configured baseline on the thermogram.");
        readonly CheckBox showIntegrationCheck = Check("Integration regions", true, "Show the time ranges used to integrate injection peaks.");
        readonly CheckBox correctedCheck = Check("Corrected data", false, "Show data after subtraction of the configured baseline.");
        readonly CheckBox cursorInfoCheck = Check("Cursor information", true, "Show the time and power values under the graph cursor.");
        readonly CheckBox discardIntegratedCheck = Check("Discard integrated regions", true, "Exclude already integrated regions when recalculating the baseline.");
        readonly CheckBox showSplineHandlesCheck = Check("Show spline handles", false, "Show editable control points for a spline baseline.");
        readonly CheckBox moveSplinePointsCheck = Check("Move spline points in time", false, "Allow drag operations to change a spline point's time as well as its value.");
        readonly CheckBox copyIntegrationStartCheck = Check("Copy start time to next", true, "Include the integration start time when copying a region to the next injection.");

        readonly Button lockProcessorButton = Button("Lock", 72);
        readonly Button copyActiveButton = Button("Active", 96);
        readonly Button copyNewButton = Button("New", 96);
        readonly Button convertSmoothButton = Button("Smooth", 96);
        readonly Button convertLinearButton = Button("Linear", 96);
        readonly Button fitPeaksButton = Button("Fit Peaks", 120);
        readonly Button clearSelectionButton = Button("Clear Selection", 112);
        readonly Button previousButton = Button("<", 38);
        readonly Button nextButton = Button(">", 38);
        readonly Button copyNextButton = Button("Copy to next peak", 124);
        readonly ToggleButton allDataButton = Toggle("All Y", 64);
        readonly ToggleButton baselineZoomButton = Toggle("Baseline Y", 88);
        readonly ToggleButton allPeaksButton = Toggle("All Peaks", 84);
        readonly ToggleButton focusPeakButton = Toggle("Selected Peak", 104);

        readonly StackPanel splineOptionsPanel = VerticalGroup();
        readonly StackPanel degreePanel = VerticalGroup();
        readonly StackPanel baselineEditingPanel = VerticalGroup();
        readonly StackPanel integrationEditingPanel = VerticalGroup();
        TabControl controlsPanel = new TabControl();

        ExperimentData? experiment;
        bool isUpdatingControls;
        bool isPeakFitting;
        bool integrationSliderDragging;
        bool integrationSliderChanged;
        int processingRefreshGeneration;
        bool processingRefreshQueued;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? ProcessingChanged;

        public ProcessingWorkspaceControl()
        {
            ToolTip.SetTip(
                lockProcessorButton,
                "Freeze processing results and disable integration-region and spline-point editing.");
            BuildLayout();
            WireEvents();
            ApplyViewOptions();
            UpdateControls();
        }

        public ExperimentData? Experiment
        {
            get => experiment;
            set
            {
                if (ReferenceEquals(experiment, value)) return;

                UnsubscribeExperiment();
                CancelQueuedProcessingRefresh();
                experiment = value;
                graph.Experiment = value;
                SubscribeExperiment();
                UpdateControls();

                _ = InitializeExperimentAsync();
            }
        }

        public void FitToData()
        {
            graph.FitToData();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            UnsubscribeExperiment();
            CancelQueuedProcessingRefresh();
            base.OnDetachedFromVisualTree(e);
        }

        void BuildLayout()
        {
            splineOptionsPanel.Children.Add(Labeled("Spline", splineAlgorithmCombo));
            splineOptionsPanel.Children.Add(Labeled("Density", splineDensityCombo));
            splineOptionsPanel.Children.Add(Labeled("Handle", splineHandleCombo));

            degreePanel.Children.Add(Labeled("Degree", degreeSlider));
            degreePanel.Children.Add(degreeLabel);

            baselineEditingPanel.Children.Add(Labeled("Type", baselineTypeCombo));
            baselineEditingPanel.Children.Add(splineOptionsPanel);
            baselineEditingPanel.Children.Add(degreePanel);

            fitPeaksButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            integrationEditingPanel.Children.Add(Labeled("Start", FieldWithSuffix(integrationStartSlider, startLabel)));
            integrationEditingPanel.Children.Add(Labeled("Length", FieldWithSuffix(integrationLengthSlider, lengthLabel)));
            integrationEditingPanel.Children.Add(fitPeaksButton);
            copyNextButton.MinWidth = 0;
            copyNextButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            integrationEditingPanel.Children.Add(copyNextButton);

            controlsPanel = WorkspaceControlBuilder.Inspector(
                InspectorTab("Processing", BuildProcessingTab()),
                InspectorTab("Display", BuildSelectionViewTab()));

            var graphBorder = WorkspaceControlBuilder.ContentBorder(graph);
            var graphContent = WorkspaceControlBuilder.ContentWithFooter(
                graphBorder,
                WorkspaceControlBuilder.InspectorFooter(BuildGraphFooter()));
            Content = WorkspaceControlBuilder.Workspace(graphContent, controlsPanel);
        }

        Control BuildProcessingTab()
        {
            var panel = WorkspaceControlBuilder.InspectorPanel();
            panel.Children.Add(Section(baselineHeader, new Control[]
            {
                baselineEditingPanel,
                showSplineHandlesCheck,
                moveSplinePointsCheck
            }));
            panel.Children.Add(Section(integrationHeader, new Control[]
            {
                integrationEditingPanel,
                discardIntegratedCheck,
                copyIntegrationStartCheck
            }));
            panel.Children.Add(Section("Processing Actions", new Control[]
            {
                lockProcessorButton,
                Text("Convert to spline"),
                Row(convertLinearButton, convertSmoothButton),
                Text("Copy processing"),
                Row(copyActiveButton, copyNewButton),
                summaryText
            }));

            return panel;
        }

        Control BuildSelectionViewTab()
        {
            var panel = WorkspaceControlBuilder.InspectorPanel();
            panel.Children.Add(Section("View", new Control[]
            {
                showBaselineCheck,
                showIntegrationCheck,
                correctedCheck,
                cursorInfoCheck,
                Labeled("Zoom width", NumericFieldWithSuffix(peakWidthStepper, "peaks"))
            }));

            return panel;
        }

        Control BuildGraphFooter()
        {
            selectionLabel.Width = 120;
            selectionLabel.TextAlignment = TextAlignment.Center;

            var footer = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto")
            };

            var zoomControls = Row(allDataButton, baselineZoomButton, allPeaksButton, focusPeakButton);
            zoomControls.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(zoomControls, 0);
            footer.Children.Add(zoomControls);

            var selectionControls = Row(previousButton, selectionLabel, nextButton, clearSelectionButton);
            selectionControls.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(selectionControls, 1);
            footer.Children.Add(selectionControls);

            return footer;
        }

        void WireEvents()
        {
            baselineTypeCombo.SelectionChanged += async (_, _) => await ChangeBaselineTypeAsync();
            splineAlgorithmCombo.SelectionChanged += async (_, _) => await ChangeSplineAlgorithmAsync();
            splineDensityCombo.SelectionChanged += async (_, _) => await ChangeSplineDensityAsync();
            splineHandleCombo.SelectionChanged += async (_, _) => await ChangeSplineHandleModeAsync();
            peakWidthStepper.PropertyChanged += (_, e) =>
            {
                if (e.Property == NumericUpDown.ValueProperty)
                    ChangePeakWidth();
            };

            degreeSlider.PropertyChanged += async (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                    await ChangeDegreeAsync();
            };

            integrationStartSlider.PropertyChanged += async (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                    await ChangeIntegrationStartAsync();
            };

            integrationLengthSlider.PropertyChanged += async (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                    await ChangeIntegrationLengthAsync();
            };

            integrationStartSlider.AddHandler(Thumb.DragStartedEvent, IntegrationSliderDragStarted, RoutingStrategies.Bubble, true);
            integrationStartSlider.AddHandler(Thumb.DragCompletedEvent, IntegrationSliderDragCompleted, RoutingStrategies.Bubble, true);
            integrationLengthSlider.AddHandler(Thumb.DragStartedEvent, IntegrationSliderDragStarted, RoutingStrategies.Bubble, true);
            integrationLengthSlider.AddHandler(Thumb.DragCompletedEvent, IntegrationSliderDragCompleted, RoutingStrategies.Bubble, true);

            showBaselineCheck.IsCheckedChanged += (_, _) => ApplyViewOptions();
            showIntegrationCheck.IsCheckedChanged += (_, _) => ApplyViewOptions();
            correctedCheck.IsCheckedChanged += (_, _) => ApplyViewOptions(refit: true);
            cursorInfoCheck.IsCheckedChanged += (_, _) => ApplyViewOptions();
            discardIntegratedCheck.IsCheckedChanged += async (_, _) => await ChangeDiscardIntegratedAsync();
            showSplineHandlesCheck.IsCheckedChanged += (_, _) => SetSplineHandlesFromControl();
            moveSplinePointsCheck.IsCheckedChanged += (_, _) => SetSplinePointTimeDraggingFromControl();
            copyIntegrationStartCheck.IsCheckedChanged += (_, _) => SetIntegrationStartCopyFromControl();

            lockProcessorButton.Click += (_, _) => ToggleLock();
            convertSmoothButton.Click += async (_, _) => await ConvertCurrentProcessorToSplineAsync(SplineInterpolator.SplineInterpolatorAlgorithm.Smooth);
            convertLinearButton.Click += async (_, _) => await ConvertCurrentProcessorToSplineAsync(SplineInterpolator.SplineInterpolatorAlgorithm.Linear);
            copyActiveButton.Click += (_, _) => CopyProcessingToActive();
            copyNewButton.Click += (_, _) => CopyProcessingToNonProcessed();
            fitPeaksButton.Click += async (_, _) => await RunPeakFitAsync();
            clearSelectionButton.Click += (_, _) => SelectAllInjections();
            previousButton.Click += (_, _) => SelectPreviousInjection();
            nextButton.Click += (_, _) => SelectNextInjection();
            copyNextButton.Click += async (_, _) => await CopySelectedIntegrationToNextAsync();
            allDataButton.Click += (_, _) => graph.ShowAllVertical();
            baselineZoomButton.Click += (_, _) => graph.ZoomBaseline();
            allPeaksButton.Click += (_, _) => graph.ShowAllInjections();
            focusPeakButton.Click += (_, _) => graph.FocusSelectedInjection();

            graph.SelectedInjectionChanged += (_, _) => UpdateControls();
            graph.ViewModeChanged += (_, _) => UpdateGraphFooter();
            graph.IntegrationEdited += (_, _) => UpdateControls();
            graph.IntegrationEditCompleted += async (_, _) => await CompleteGraphIntegrationEditAsync();
            graph.SplineEditCompleted += async (_, _) => await CompleteGraphSplineEditAsync();
            graph.CopySelectedIntegrationToNextRequested += async (_, _) => await CopySelectedIntegrationToNextAsync();
        }

        void SubscribeExperiment()
        {
            if (experiment == null) return;

            experiment.ProcessingUpdated += ExperimentProcessingUpdated;
            experiment.InjectionIncludeChanged += ExperimentInjectionIncludeChanged;
        }

        void UnsubscribeExperiment()
        {
            if (experiment == null) return;

            experiment.ProcessingUpdated -= ExperimentProcessingUpdated;
            experiment.InjectionIncludeChanged -= ExperimentInjectionIncludeChanged;
        }

        void ExperimentProcessingUpdated(object? sender, EventArgs e)
        {
            var source = sender as ExperimentData;
            if (source?.Processor.BaselineCompleted == false) return;
            QueueProcessingRefresh(source);
        }

        void ExperimentInjectionIncludeChanged(object? sender, EventArgs e)
        {
            QueueProcessingRefresh(sender as ExperimentData);
        }

        void QueueProcessingRefresh(ExperimentData? source)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => QueueProcessingRefresh(source));
                return;
            }

            var current = experiment;
            if (current == null || (source != null && !ReferenceEquals(source, current))) return;
            if (processingRefreshQueued) return;

            var generation = processingRefreshGeneration;
            processingRefreshQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != processingRefreshGeneration || !ReferenceEquals(current, experiment))
                    return;

                processingRefreshQueued = false;
                graph.InvalidateVisual();
                UpdateControls();
                ProcessingChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        void CancelQueuedProcessingRefresh()
        {
            processingRefreshGeneration++;
            processingRefreshQueued = false;
        }

        async Task InitializeExperimentAsync()
        {
            if (!ContextIsValid)
            {
                graph.FitToData();
                return;
            }

            if (experiment!.Processor.BaselineType != BaselineInterpolatorTypes.None)
            {
                graph.FitToData();
                UpdateControls();
                return;
            }

            if (DocumentDirtyTracker.IsRestoringDocument)
            {
                graph.FitToData();
                UpdateControls();
                return;
            }

            experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Spline);

            if (experiment.Injections.Count > 0)
                experiment.SetIntegrationLengthByTime((float)(experiment.Injections[0].Delay / 2));

            await ProcessDataAsync(replace: true, status: "Baseline initialized");
            graph.FitToData();
        }

        async Task ChangeBaselineTypeAsync()
        {
            if (isUpdatingControls || !ProcessingIsEditable) return;

            var type = baselineTypeCombo.SelectedIndex switch
            {
                0 => BaselineInterpolatorTypes.Spline,
                1 => BaselineInterpolatorTypes.Polynomial,
                2 => BaselineInterpolatorTypes.Segmented,
                _ => experiment!.Processor.BaselineType,
            };

            experiment!.Processor.InitializeBaseline(type);
            UpdateControls();
            await ProcessDataAsync(replace: true, status: "Baseline updated");
        }

        async Task ChangeSplineAlgorithmAsync()
        {
            if (isUpdatingControls || !ProcessingIsEditable) return;
            if (experiment!.Processor.Interpolator is not SplineInterpolator spline) return;

            spline.Algorithm = splineAlgorithmCombo.SelectedIndex == 0
                ? SplineInterpolator.SplineInterpolatorAlgorithm.Linear
                : SplineInterpolator.SplineInterpolatorAlgorithm.Smooth;
            spline.ApplyPointDensity();

            await ProcessDataAsync(replace: true, status: "Spline baseline updated");
        }

        async Task ChangeSplineDensityAsync()
        {
            if (isUpdatingControls || !ProcessingIsEditable) return;
            if (experiment!.Processor.Interpolator is not SplineInterpolator spline) return;
            if (splineDensityCombo.SelectedIndex < 0) return;

            spline.PointDensity = (SplineInterpolator.SplinePointDensity)splineDensityCombo.SelectedIndex;
            spline.ApplyPointDensity();

            await ProcessDataAsync(replace: true, status: "Spline density updated");
        }

        async Task ChangeSplineHandleModeAsync()
        {
            if (isUpdatingControls || !ProcessingIsEditable) return;
            if (experiment!.Processor.Interpolator is not SplineInterpolator spline) return;
            if (splineHandleCombo.SelectedIndex < 0) return;

            spline.HandleMode = (SplineInterpolator.SplineHandleMode)splineHandleCombo.SelectedIndex;

            await ProcessDataAsync(replace: true, status: "Spline handle mode updated");
        }

        async Task ChangeDegreeAsync()
        {
            if (isUpdatingControls || !ProcessingIsEditable) return;

            if (experiment!.Processor.Interpolator is PolynomialLeastSquaresInterpolator polynomial)
            {
                polynomial.Degree = PolynomialDegreeFromSlider((int)Math.Round(degreeSlider.Value));
            }
            else if (experiment.Processor.Interpolator is SegmentedBaselineInterpolator segmented)
            {
                segmented.Degree = SegmentedBaselineInterpolator.ClampDegree((int)Math.Round(degreeSlider.Value));
            }
            else return;

            UpdateDegreeLabel();
            await ProcessDataAsync(replace: true, status: "Baseline degree updated");
        }

        async Task ChangeIntegrationStartAsync()
        {
            if (isUpdatingControls || !ProcessingIsEditable) return;

            UseTimeModeForLegacyFit();
            if (graph.SelectedInjectionIndex == -1)
                experiment!.SetIntegrationStartTime((float)integrationStartSlider.Value);
            else
                experiment!.Injections[graph.SelectedInjectionIndex].SetIntegrationStartTime((float)integrationStartSlider.Value);

            if (integrationSliderDragging)
                integrationSliderChanged = true;

            UpdateIntegrationLabels();
            await ProcessOrIntegrateAfterRangeChangeAsync(refreshBaseline: !integrationSliderDragging);
        }

        async Task ChangeIntegrationLengthAsync()
        {
            if (isUpdatingControls || !ProcessingIsEditable) return;

            UseTimeModeForLegacyFit();
            await ApplyIntegrationLengthAsync();
        }

        void IntegrationSliderDragStarted(object? sender, VectorEventArgs e)
        {
            integrationSliderDragging = true;
            integrationSliderChanged = false;
        }

        async void IntegrationSliderDragCompleted(object? sender, VectorEventArgs e)
        {
            integrationSliderDragging = false;
            if (!integrationSliderChanged) return;

            integrationSliderChanged = false;
            await CompleteIntegrationSliderEditAsync();
        }

        async Task ChangeDiscardIntegratedAsync()
        {
            if (isUpdatingControls || !ProcessingIsEditable) return;

            experiment!.Processor.DiscardIntegratedPoints = discardIntegratedCheck.IsChecked == true;
            await ProcessDataAsync(replace: true, status: "Processing updated");
        }

        async Task ApplyIntegrationLengthAsync()
        {
            if (!ProcessingIsEditable) return;

            var parameter = GetLengthSliderParameter();
            var selected = graph.SelectedInjectionIndex;

            switch (experiment!.Processor.IntegrationLengthMode)
            {
                case InjectionData.IntegrationLengthMode.Time:
                case InjectionData.IntegrationLengthMode.Fit:
                    if (selected == -1) experiment.SetIntegrationLengthByTime(parameter);
                    else experiment.Injections[selected].SetIntegrationLengthByTime(parameter);
                    break;
                case InjectionData.IntegrationLengthMode.Factor:
                    experiment.Processor.IntegrationLengthFactor = parameter;
                    if (selected == -1) experiment.SetIntegrationLengthByFactor(parameter);
                    else experiment.Injections[selected].SetIntegrationLengthByFactor(parameter);
                    break;
            }

            if (integrationSliderDragging)
                integrationSliderChanged = true;

            UpdateIntegrationLabels();
            await ProcessOrIntegrateAfterRangeChangeAsync(refreshBaseline: !integrationSliderDragging);
        }

        async Task RunPeakFitAsync()
        {
            if (!ContextIsValid || experiment!.InjectionCount == 0 || experiment.Processor.IsLocked || isPeakFitting)
                return;

            var targetExperiment = experiment!;
            isPeakFitting = true;
            UpdateControls();
            StatusChanged?.Invoke(this, "Fitting integration peaks...");

            try
            {
                var fitResult = await targetExperiment.FitIntegrationPeaksAsync();
                targetExperiment.Processor.IntegrationLengthMode = InjectionData.IntegrationLengthMode.Time;

                if (!ReferenceEquals(experiment, targetExperiment))
                    return;

                ConfigureIntegrationControls();
                graph.InvalidateVisual();
                StatusChanged?.Invoke(this, PeakFitStatusMessage(fitResult));
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
                if (ReferenceEquals(experiment, targetExperiment))
                    StatusChanged?.Invoke(this, $"Peak fitting failed: {ex.Message}");
            }
            finally
            {
                isPeakFitting = false;
                UpdateControls();
            }
        }

        static string PeakFitStatusMessage(PeakFitResult result) => result.Status switch
        {
            PeakFitStatus.Converged => $"Peaks fitted ({result.Iterations} pass{(result.Iterations == 1 ? "" : "es")})",
            PeakFitStatus.CycleResolved => "Peaks fitted; a stable cycle was resolved",
            PeakFitStatus.NonConvergent => "Peak fitting did not converge; integration regions were unchanged",
            PeakFitStatus.NoData => "No peak data available to fit",
            PeakFitStatus.Locked => "Peak fitting skipped because processing is locked",
            _ => "Peak fitting failed; integration regions were unchanged",
        };

        async Task ProcessOrIntegrateAfterRangeChangeAsync(bool refreshBaseline = true)
        {
            if (!ProcessingIsEditable) return;

            if (refreshBaseline && experiment!.Processor.DiscardIntegratedPoints)
                await ProcessDataAsync(replace: true, status: "Integration updated");
            else
            {
                experiment.Processor.IntegratePeaks(invalidate: refreshBaseline, notify: refreshBaseline);
                graph.InvalidateVisual();
                StatusChanged?.Invoke(this, "Integration updated");
            }
        }

        async Task CompleteIntegrationSliderEditAsync()
        {
            if (!ProcessingIsEditable) return;

            await ProcessOrIntegrateAfterRangeChangeAsync();
            UpdateControls();
        }

        async Task CompleteGraphIntegrationEditAsync()
        {
            if (!ProcessingIsEditable) return;

            if (experiment!.Processor.BaselineCompleted && experiment.Processor.DiscardIntegratedPoints)
                await ProcessDataAsync(replace: true, status: "Integration updated");
            else
            {
                experiment.Processor.IntegratePeaks();
                UpdateControls();
                StatusChanged?.Invoke(this, "Integration updated");
            }
        }

        async Task CompleteGraphSplineEditAsync()
        {
            if (!ProcessingIsEditable) return;

            await ProcessDataAsync(replace: false, status: "Spline baseline updated");
        }

        async Task ProcessDataAsync(bool replace, string status)
        {
            if (!ContextIsValid) return;

            try
            {
                StatusChanged?.Invoke(this, "Processing data...");
                await experiment!.Processor.ProcessData(replace);
                graph.InvalidateVisual();
                UpdateControls();
                StatusChanged?.Invoke(this, status);
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
                StatusChanged?.Invoke(this, $"Processing failed: {ex.Message}");
            }
        }

        void ApplyViewOptions(bool refit = false)
        {
            graph.SetFeatureVisibility(
                showBaselineCheck.IsChecked == true,
                showIntegrationCheck.IsChecked == true,
                correctedCheck.IsChecked == true,
                cursorInfoCheck.IsChecked == true);

            if (refit)
                graph.FitToData();
        }

        void ToggleLock()
        {
            if (!ContextIsValid || isPeakFitting) return;

            experiment!.Processor.ToggleLock();
            UpdateControls();
        }

        public void ToggleSplineHandles()
        {
            if (!ProcessingIsEditable || experiment!.Processor.Interpolator is not SplineInterpolator spline) return;
            if (spline.Algorithm != SplineInterpolator.SplineInterpolatorAlgorithm.Smooth) return;

            spline.ShowHandles = !spline.ShowHandles;
            UpdateControls();
            graph.InvalidateVisual();
        }

        public void ToggleSplinePointTimeDragging()
        {
            if (!ProcessingIsEditable || experiment!.Processor.Interpolator is not SplineInterpolator spline) return;

            spline.AllowPointTimeDragging = !spline.AllowPointTimeDragging;
            UpdateControls();
        }

        public void ToggleIntegrationRegionCopyIncludesStart()
        {
            if (!ProcessingIsEditable) return;

            AppSettings.IntegrationRegionCopyIncludesStart = !AppSettings.IntegrationRegionCopyIncludesStart;
            AppSettings.Save();
            UpdateControls();
        }

        void SetSplineHandlesFromControl()
        {
            if (isUpdatingControls || experiment?.Processor.Interpolator is not SplineInterpolator spline) return;
            if (spline.ShowHandles == (showSplineHandlesCheck.IsChecked == true)) return;
            ToggleSplineHandles();
        }

        void SetSplinePointTimeDraggingFromControl()
        {
            if (isUpdatingControls || experiment?.Processor.Interpolator is not SplineInterpolator spline) return;
            if (spline.AllowPointTimeDragging == (moveSplinePointsCheck.IsChecked == true)) return;
            ToggleSplinePointTimeDragging();
        }

        void SetIntegrationStartCopyFromControl()
        {
            if (isUpdatingControls || AppSettings.IntegrationRegionCopyIncludesStart == (copyIntegrationStartCheck.IsChecked == true)) return;
            ToggleIntegrationRegionCopyIncludesStart();
        }

        public async Task ConvertCurrentProcessorToSplineAsync(SplineInterpolator.SplineInterpolatorAlgorithm algorithm)
        {
            if (!ProcessingIsEditable || experiment!.Processor.Interpolator == null) return;

            var interpolator = experiment.Processor.Interpolator;
            if (interpolator is SplineInterpolator spline)
            {
                if (algorithm != SplineInterpolator.SplineInterpolatorAlgorithm.Linear) return;
                if (spline.Algorithm == SplineInterpolator.SplineInterpolatorAlgorithm.Linear) return;

                spline.Algorithm = SplineInterpolator.SplineInterpolatorAlgorithm.Linear;
                spline.ApplyPointDensity();
                await ProcessDataAsync(replace: false, status: "Converted to linear spline");
                return;
            }

            if (algorithm == SplineInterpolator.SplineInterpolatorAlgorithm.Smooth
                && interpolator is not PolynomialLeastSquaresInterpolator
                && interpolator is not SegmentedBaselineInterpolator)
                return;

            SplineInterpolator.PolynomialToSplineConversionTargetAlgorithm = algorithm;
            interpolator.ConvertToSpline(SplineConversionPointDensity(algorithm));
            await ProcessDataAsync(replace: true, status: algorithm == SplineInterpolator.SplineInterpolatorAlgorithm.Linear
                ? "Converted to linear spline"
                : "Converted to smooth spline");
        }

        void CopyProcessingToActive()
        {
            if (!ContextIsValid || isPeakFitting) return;

            DataManager.CopySelectedProcessToActive();
            StatusChanged?.Invoke(this, "Processing copied to active data");
        }

        void CopyProcessingToNonProcessed()
        {
            if (!ContextIsValid || isPeakFitting) return;

            DataManager.CopySelectedProcessToNonProcessed();
            StatusChanged?.Invoke(this, "Processing copied to unprocessed data");
        }

        void SelectAllInjections()
        {
            graph.SelectedInjectionIndex = -1;
            UpdateControls();
        }

        void SelectPreviousInjection()
        {
            if (!ContextIsValid) return;

            graph.SelectedInjectionIndex = graph.SelectedInjectionIndex <= 0 ? 0 : graph.SelectedInjectionIndex - 1;
            graph.FocusSelectedInjection();
            UpdateControls();
        }

        void SelectNextInjection()
        {
            if (!ContextIsValid) return;

            graph.SelectedInjectionIndex = graph.SelectedInjectionIndex < 0 ? 0 : graph.SelectedInjectionIndex + 1;
            graph.FocusSelectedInjection();
            UpdateControls();
        }

        async Task CopySelectedIntegrationToNextAsync()
        {
            if (!ContextIsValid || experiment!.Processor.IsLocked || isPeakFitting) return;

            var selected = graph.SelectedInjectionIndex;
            if (selected < 0 || selected >= experiment.InjectionCount - 1) return;

            var source = experiment.Injections[selected];
            var target = experiment.Injections[selected + 1];

            if (AppSettings.IntegrationRegionCopyIncludesStart)
                target.SetIntegrationStartTime(source.IntegrationStartDelay);

            target.SetIntegrationLengthByTime(source.IntegrationEndOffset);
            graph.SelectedInjectionIndex = selected + 1;
            graph.FocusSelectedInjection();

            await ProcessDataAsync(replace: true, status: "Integration copied to next injection");
        }

        void ChangePeakWidth()
        {
            var displayedPeakCount = (int)(peakWidthStepper.Value ?? 3);
            graph.PeakZoomWidth = Math.Max(0, (displayedPeakCount - 1) / 2);

            if (graph.IsInjectionFocused)
                graph.FocusSelectedInjection();
        }

        void UpdateControls()
        {
            isUpdatingControls = true;

            try
            {
                var valid = ContextIsValid;
                controlsPanel.IsEnabled = valid;
                baselineEditingPanel.IsEnabled = false;
                integrationEditingPanel.IsEnabled = false;
                lockProcessorButton.IsEnabled = valid && !isPeakFitting;
                graph.IsEditingEnabled = false;

                if (experiment == null)
                {
                    summaryText.Text = "No experiment selected";
                    UpdateGraphFooter();
                    graph.InvalidateVisual();
                    return;
                }

                if (!experiment.HasThermogram)
                {
                    summaryText.Text = "Selected item has no raw thermogram";
                    UpdateGraphFooter();
                    graph.InvalidateVisual();
                    return;
                }

                var processor = experiment.Processor;
                var hasInjections = experiment.InjectionCount > 0;
                var canEdit = !processor.IsLocked && !isPeakFitting;
                summaryText.Text = $"{experiment.DataPoints.Count} points, {experiment.InjectionCount} injections";
                baselineHeader.Text = processor.IsLocked ? "Baseline [locked]" : "Baseline";
                integrationHeader.Text = isPeakFitting
                    ? "Integration [fitting]"
                    : "Integration";
                baselineEditingPanel.IsEnabled = canEdit;
                integrationEditingPanel.IsEnabled = hasInjections && canEdit;
                graph.IsEditingEnabled = canEdit;

                baselineTypeCombo.SelectedIndex = processor.BaselineType switch
                {
                    BaselineInterpolatorTypes.Spline => 0,
                    BaselineInterpolatorTypes.Polynomial => 1,
                    BaselineInterpolatorTypes.Segmented => 2,
                    _ => -1,
                };

                splineOptionsPanel.IsVisible = processor.Interpolator is SplineInterpolator;
                degreePanel.IsVisible = processor.Interpolator is PolynomialLeastSquaresInterpolator or SegmentedBaselineInterpolator;

                if (processor.Interpolator is SplineInterpolator spline)
                {
                    splineAlgorithmCombo.SelectedIndex = spline.Algorithm == SplineInterpolator.SplineInterpolatorAlgorithm.Linear ? 0 : 1;
                    splineDensityCombo.SelectedIndex = (int)spline.PointDensity;
                    splineHandleCombo.SelectedIndex = (int)spline.HandleMode;
                    showSplineHandlesCheck.IsChecked = spline.ShowHandles;
                    moveSplinePointsCheck.IsChecked = spline.AllowPointTimeDragging;
                }
                else
                {
                    showSplineHandlesCheck.IsChecked = false;
                    moveSplinePointsCheck.IsChecked = false;
                }

                showSplineHandlesCheck.IsEnabled = canEdit && processor.Interpolator is SplineInterpolator
                {
                    Algorithm: SplineInterpolator.SplineInterpolatorAlgorithm.Smooth
                };
                moveSplinePointsCheck.IsEnabled = canEdit && processor.Interpolator is SplineInterpolator;
                copyIntegrationStartCheck.IsChecked = AppSettings.IntegrationRegionCopyIncludesStart;
                copyIntegrationStartCheck.IsEnabled = canEdit;
                discardIntegratedCheck.IsEnabled = canEdit;
                convertSmoothButton.IsEnabled = canEdit
                    && (processor.Interpolator is PolynomialLeastSquaresInterpolator or SegmentedBaselineInterpolator);
                convertLinearButton.IsEnabled = canEdit && processor.Interpolator is not SplineInterpolator
                {
                    Algorithm: SplineInterpolator.SplineInterpolatorAlgorithm.Linear
                };
                copyActiveButton.IsEnabled = !isPeakFitting;
                copyNewButton.IsEnabled = !isPeakFitting;

                ConfigureDegreeControls();
                ConfigureIntegrationControls();

                discardIntegratedCheck.IsChecked = processor.DiscardIntegratedPoints;
                lockProcessorButton.Content = processor.IsLocked ? "Unlock" : "Lock";
                fitPeaksButton.IsEnabled = hasInjections && canEdit;
                copyNextButton.IsEnabled = canEdit
                    && graph.SelectedInjectionIndex >= 0
                    && graph.SelectedInjectionIndex < experiment.InjectionCount - 1;

                selectionLabel.Text = graph.SelectedInjectionIndex == -1
                    ? "All injections"
                    : $"Injection #{graph.SelectedInjectionIndex + 1}";

                UpdateGraphFooter();
                graph.InvalidateVisual();
            }
            finally
            {
                isUpdatingControls = false;
            }
        }

        void ConfigureDegreeControls()
        {
            if (experiment?.Processor.Interpolator is PolynomialLeastSquaresInterpolator polynomial)
            {
                degreeSlider.Minimum = 0;
                degreeSlider.Maximum = 10;
                degreeSlider.TickFrequency = 1;
                degreeSlider.Value = SliderPositionFromPolynomialDegree(polynomial.Degree);
            }
            else if (experiment?.Processor.Interpolator is SegmentedBaselineInterpolator segmented)
            {
                degreeSlider.Minimum = SegmentedBaselineInterpolator.MinimumDegree;
                degreeSlider.Maximum = SegmentedBaselineInterpolator.MaximumDegree;
                degreeSlider.TickFrequency = 1;
                degreeSlider.Value = segmented.Degree;
            }

            UpdateDegreeLabel();
        }

        void ConfigureIntegrationControls()
        {
            if (!ContextIsValid || experiment!.InjectionCount == 0)
            {
                startLabel.Text = "";
                lengthLabel.Text = "";
                return;
            }

            var processor = experiment.Processor;
            var injection = graph.SelectedInjectionIndex == -1
                ? experiment.Injections.Last()
                : experiment.Injections[graph.SelectedInjectionIndex];
            var maxDelay = Math.Max(1, experiment.Injections.Max(inj => inj.Delay));
            var minDelay = Math.Min(-maxDelay, experiment.Injections.Min(inj => -inj.Delay));

            integrationStartSlider.Minimum = minDelay;
            integrationStartSlider.Maximum = maxDelay;
            integrationStartSlider.Value = injection.IntegrationStartDelay;
            integrationLengthSlider.Minimum = 0;
            integrationLengthSlider.Maximum = maxDelay;
            integrationLengthSlider.Value = processor.IntegrationLengthMode == InjectionData.IntegrationLengthMode.Factor
                ? FactorToSlider(processor.IntegrationLengthFactor)
                : injection.IntegrationEndOffset;

            UpdateIntegrationLabels();
        }

        void UpdateDegreeLabel()
        {
            if (experiment?.Processor.Interpolator is PolynomialLeastSquaresInterpolator polynomial)
                degreeLabel.Text = polynomial.Degree.ToString();
            else if (experiment?.Processor.Interpolator is SegmentedBaselineInterpolator segmented)
                degreeLabel.Text = segmented.Degree.ToString();
            else
                degreeLabel.Text = "";
        }

        void UpdateIntegrationLabels()
        {
            if (!ContextIsValid || experiment!.InjectionCount == 0)
            {
                startLabel.Text = "";
                lengthLabel.Text = "";
                return;
            }

            startLabel.Text = $"{integrationStartSlider.Value:F1} s";

            lengthLabel.Text = experiment.Processor.IntegrationLengthMode == InjectionData.IntegrationLengthMode.Factor
                ? $"{GetLengthSliderParameter():F1}x"
                : $"{integrationLengthSlider.Value:F1} s";
        }

        float GetLengthSliderParameter()
        {
            if (!ContextIsValid) return 0;

            return experiment!.Processor.IntegrationLengthMode switch
            {
                InjectionData.IntegrationLengthMode.Factor => (float)Math.Pow(5, integrationLengthSlider.Value / Math.Max(1, integrationLengthSlider.Maximum)),
                _ => (float)integrationLengthSlider.Value
            };
        }

        void UseTimeModeForLegacyFit()
        {
            if (experiment?.Processor.IntegrationLengthMode == InjectionData.IntegrationLengthMode.Fit)
                experiment.Processor.IntegrationLengthMode = InjectionData.IntegrationLengthMode.Time;
        }

        void UpdateGraphFooter()
        {
            var valid = ContextIsValid;
            var selected = graph.SelectedInjectionIndex;
            var hasSelection = valid && selected >= 0;

            allDataButton.IsEnabled = valid;
            baselineZoomButton.IsEnabled = valid;
            allPeaksButton.IsEnabled = valid;
            focusPeakButton.IsEnabled = hasSelection;
            previousButton.IsEnabled = hasSelection && selected > 0;
            nextButton.IsEnabled = hasSelection && selected < experiment!.InjectionCount - 1;
            clearSelectionButton.IsEnabled = hasSelection;

            allDataButton.IsChecked = graph.CurrentVerticalZoomMode == ProcessingGraphControl.VerticalZoomMode.AllData;
            baselineZoomButton.IsChecked = graph.CurrentVerticalZoomMode == ProcessingGraphControl.VerticalZoomMode.Baseline;
            allPeaksButton.IsChecked = graph.CurrentHorizontalZoomMode == ProcessingGraphControl.HorizontalZoomMode.AllPeaks;
            focusPeakButton.IsChecked = graph.CurrentHorizontalZoomMode == ProcessingGraphControl.HorizontalZoomMode.SelectedPeak;

            selectionLabel.Text = hasSelection
                ? $"Injection #{selected + 1}"
                : valid ? "All injections" : "No selection";
        }

        float FactorToSlider(float value)
        {
            value = Math.Max(1, value);
            return (float)(Math.Log(value, 5) * Math.Max(1, integrationLengthSlider.Maximum));
        }

        bool ContextIsValid => experiment != null && experiment.HasThermogram && experiment.Processor != null;
        bool ProcessingIsEditable => ContextIsValid && !experiment!.Processor.IsLocked && !isPeakFitting;

        static int SplineConversionPointDensity(SplineInterpolator.SplineInterpolatorAlgorithm algorithm)
        {
            return algorithm == SplineInterpolator.SplineInterpolatorAlgorithm.Linear ? 4 : 2;
        }

        static int PolynomialDegreeFromSlider(int sliderValue)
        {
            return sliderValue switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                3 => 3,
                4 => 4,
                5 => 6,
                6 => 8,
                7 => 12,
                8 => 16,
                9 => 24,
                10 => 32,
                _ => 12,
            };
        }

        static int SliderPositionFromPolynomialDegree(int degree)
        {
            return degree switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                3 => 3,
                4 => 4,
                6 => 5,
                8 => 6,
                12 => 7,
                16 => 8,
                24 => 9,
                32 => 10,
                _ => 5,
            };
        }

    }
}
