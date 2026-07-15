using System;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        readonly ComboBox integrationModeCombo = Combo(new[] { "Time", "Factor", "Fit" });
        readonly ComboBox peakWidthCombo = Combo(new[] { "1", "3", "5" });

        readonly Slider degreeSlider = Slider(0, 10, 1);
        readonly Slider integrationStartSlider = Slider(-30, 30, 0.1);
        readonly Slider integrationLengthSlider = Slider(0, 120, 0.1);

        readonly CheckBox showBaselineCheck = Check("Baseline", true);
        readonly CheckBox showIntegrationCheck = Check("Regions", true);
        readonly CheckBox correctedCheck = Check("Corrected", false);
        readonly CheckBox cursorInfoCheck = Check("Cursor", true);
        readonly CheckBox discardIntegratedCheck = Check("Discard integrated regions", true);
        readonly CheckBox showSplineHandlesCheck = Check("Show spline handles", false);
        readonly CheckBox moveSplinePointsCheck = Check("Move spline points in time", false);
        readonly CheckBox copyIntegrationStartCheck = Check("Copy start time to next", true);

        readonly Button lockProcessorButton = Button("Lock", 72);
        readonly Button copyActiveButton = Button("Active", 96);
        readonly Button copyNewButton = Button("New", 96);
        readonly Button convertSmoothButton = Button("Smooth", 96);
        readonly Button convertLinearButton = Button("Linear", 96);
        readonly Button allInjectionButton = Button("All", 56);
        readonly Button previousButton = Button("<", 38);
        readonly Button nextButton = Button(">", 38);
        readonly Button copyNextButton = Button("Copy to next peak", 124);
        readonly Button allDataButton = Button("All Y", 64);
        readonly Button baselineZoomButton = Button("Baseline Y", 88);
        readonly Button allPeaksButton = Button("All peaks", 84);
        readonly Button focusPeakButton = Button("Focus", 68);

        readonly StackPanel splineOptionsPanel = VerticalGroup();
        readonly StackPanel degreePanel = VerticalGroup();
        TabControl controlsPanel = new TabControl();

        ExperimentData? experiment;
        bool isUpdatingControls;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? ProcessingChanged;

        public ProcessingWorkspaceControl()
        {
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
            base.OnDetachedFromVisualTree(e);
        }

        void BuildLayout()
        {
            splineOptionsPanel.Children.Add(Labeled("Spline", splineAlgorithmCombo));
            splineOptionsPanel.Children.Add(Labeled("Density", splineDensityCombo));
            splineOptionsPanel.Children.Add(Labeled("Handle", splineHandleCombo));

            degreePanel.Children.Add(Labeled("Degree", degreeSlider));
            degreePanel.Children.Add(degreeLabel);

            controlsPanel = WorkspaceControlBuilder.Inspector(
                InspectorTab("Processing", BuildProcessingTab()),
                InspectorTab("Display", BuildSelectionViewTab()));

            var graphBorder = WorkspaceControlBuilder.ContentBorder(graph);
            Content = WorkspaceControlBuilder.Workspace(graphBorder, controlsPanel);
        }

        Control BuildProcessingTab()
        {
            var panel = WorkspaceControlBuilder.InspectorPanel();
            panel.Children.Add(Section(baselineHeader, new Control[]
            {
                Labeled("Type", baselineTypeCombo),
                splineOptionsPanel,
                degreePanel,
                showSplineHandlesCheck,
                moveSplinePointsCheck
            }));
            panel.Children.Add(Section(integrationHeader, new Control[]
            {
                Labeled("Mode", integrationModeCombo),
                Labeled("Start", integrationStartSlider),
                startLabel,
                Labeled("Length", integrationLengthSlider),
                lengthLabel,
                discardIntegratedCheck,
                copyIntegrationStartCheck,
                copyNextButton
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
            panel.Children.Add(Section("Selection", new Control[]
            {
                selectionLabel,
                Row(allInjectionButton, previousButton, nextButton)
            }));
            panel.Children.Add(Section("View", new Control[]
            {
                showBaselineCheck,
                showIntegrationCheck,
                correctedCheck,
                cursorInfoCheck,
                Row(allDataButton, baselineZoomButton),
                Row(allPeaksButton, focusPeakButton),
                Labeled("Peak width", peakWidthCombo)
            }));

            return panel;
        }

        void WireEvents()
        {
            baselineTypeCombo.SelectionChanged += async (_, _) => await ChangeBaselineTypeAsync();
            splineAlgorithmCombo.SelectionChanged += async (_, _) => await ChangeSplineAlgorithmAsync();
            splineDensityCombo.SelectionChanged += async (_, _) => await ChangeSplineDensityAsync();
            splineHandleCombo.SelectionChanged += async (_, _) => await ChangeSplineHandleModeAsync();
            integrationModeCombo.SelectionChanged += async (_, _) => await ChangeIntegrationModeAsync();
            peakWidthCombo.SelectionChanged += (_, _) => ChangePeakWidth();

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
            allInjectionButton.Click += (_, _) => SelectAllInjections();
            previousButton.Click += (_, _) => SelectPreviousInjection();
            nextButton.Click += (_, _) => SelectNextInjection();
            copyNextButton.Click += async (_, _) => await CopySelectedIntegrationToNextAsync();
            allDataButton.Click += (_, _) => graph.ShowAllVertical();
            baselineZoomButton.Click += (_, _) => graph.ZoomBaseline();
            allPeaksButton.Click += (_, _) => graph.ShowAllInjections();
            focusPeakButton.Click += (_, _) => graph.FocusSelectedInjection();

            graph.SelectedInjectionChanged += (_, _) => UpdateControls();
            graph.IntegrationEdited += (_, _) => UpdateControls();
            graph.IntegrationEditCompleted += async (_, _) => await CompleteGraphIntegrationEditAsync();
            graph.SplineEditCompleted += async (_, _) => await CompleteGraphSplineEditAsync();
            graph.CopySelectedIntegrationToNextRequested += async (_, _) => await CopySelectedIntegrationToNextAsync();
        }

        void SubscribeExperiment()
        {
            if (experiment == null) return;

            experiment.ProcessingUpdated += ExperimentProcessingUpdated;
            experiment.InjectionIncludeChanged += ExperimentProcessingUpdated;
        }

        void UnsubscribeExperiment()
        {
            if (experiment == null) return;

            experiment.ProcessingUpdated -= ExperimentProcessingUpdated;
            experiment.InjectionIncludeChanged -= ExperimentProcessingUpdated;
        }

        void ExperimentProcessingUpdated(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                graph.InvalidateVisual();
                UpdateControls();
                ProcessingChanged?.Invoke(this, EventArgs.Empty);
            });
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
            if (isUpdatingControls || !ContextIsValid) return;

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
            if (isUpdatingControls || !ContextIsValid) return;
            if (experiment!.Processor.Interpolator is not SplineInterpolator spline) return;

            spline.Algorithm = splineAlgorithmCombo.SelectedIndex == 0
                ? SplineInterpolator.SplineInterpolatorAlgorithm.Linear
                : SplineInterpolator.SplineInterpolatorAlgorithm.Smooth;
            spline.ApplyPointDensity();

            await ProcessDataAsync(replace: true, status: "Spline baseline updated");
        }

        async Task ChangeSplineDensityAsync()
        {
            if (isUpdatingControls || !ContextIsValid) return;
            if (experiment!.Processor.Interpolator is not SplineInterpolator spline) return;
            if (splineDensityCombo.SelectedIndex < 0) return;

            spline.PointDensity = (SplineInterpolator.SplinePointDensity)splineDensityCombo.SelectedIndex;
            spline.ApplyPointDensity();

            await ProcessDataAsync(replace: true, status: "Spline density updated");
        }

        async Task ChangeSplineHandleModeAsync()
        {
            if (isUpdatingControls || !ContextIsValid) return;
            if (experiment!.Processor.Interpolator is not SplineInterpolator spline) return;
            if (splineHandleCombo.SelectedIndex < 0) return;

            spline.HandleMode = (SplineInterpolator.SplineHandleMode)splineHandleCombo.SelectedIndex;

            await ProcessDataAsync(replace: true, status: "Spline handle mode updated");
        }

        async Task ChangeDegreeAsync()
        {
            if (isUpdatingControls || !ContextIsValid) return;

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

        async Task ChangeIntegrationModeAsync()
        {
            if (isUpdatingControls || !ContextIsValid) return;
            if (integrationModeCombo.SelectedIndex < 0) return;

            experiment!.Processor.IntegrationLengthMode = (InjectionData.IntegrationLengthMode)integrationModeCombo.SelectedIndex;
            UpdateIntegrationLabels();
            await ApplyIntegrationLengthAsync();
        }

        async Task ChangeIntegrationStartAsync()
        {
            if (isUpdatingControls || !ContextIsValid) return;

            if (graph.SelectedInjectionIndex == -1)
                experiment!.SetIntegrationStartTime((float)integrationStartSlider.Value);
            else
                experiment!.Injections[graph.SelectedInjectionIndex].SetIntegrationStartTime((float)integrationStartSlider.Value);

            UpdateIntegrationLabels();
            await ProcessOrIntegrateAfterRangeChangeAsync();
        }

        async Task ChangeIntegrationLengthAsync()
        {
            if (isUpdatingControls || !ContextIsValid) return;

            await ApplyIntegrationLengthAsync();
        }

        async Task ChangeDiscardIntegratedAsync()
        {
            if (isUpdatingControls || !ContextIsValid) return;

            experiment!.Processor.DiscardIntegratedPoints = discardIntegratedCheck.IsChecked == true;
            await ProcessDataAsync(replace: true, status: "Processing updated");
        }

        async Task ApplyIntegrationLengthAsync()
        {
            if (!ContextIsValid) return;

            var parameter = GetLengthSliderParameter();
            var selected = graph.SelectedInjectionIndex;

            switch (experiment!.Processor.IntegrationLengthMode)
            {
                case InjectionData.IntegrationLengthMode.Time:
                    if (selected == -1) experiment.SetIntegrationLengthByTime(parameter);
                    else experiment.Injections[selected].SetIntegrationLengthByTime(parameter);
                    break;
                case InjectionData.IntegrationLengthMode.Factor:
                    experiment.Processor.IntegrationLengthFactor = parameter;
                    if (selected == -1) experiment.SetIntegrationLengthByFactor(parameter);
                    else experiment.Injections[selected].SetIntegrationLengthByFactor(parameter);
                    break;
                case InjectionData.IntegrationLengthMode.Fit:
                    if (selected == -1) experiment.FitIntegrationPeaks();
                    else experiment.Injections[selected].SetIntegrationLengthByPeakFitting();
                    break;
            }

            UpdateIntegrationLabels();
            await ProcessOrIntegrateAfterRangeChangeAsync();
        }

        async Task ProcessOrIntegrateAfterRangeChangeAsync()
        {
            if (!ContextIsValid) return;

            if (experiment!.Processor.DiscardIntegratedPoints)
                await ProcessDataAsync(replace: true, status: "Integration updated");
            else
            {
                experiment.Processor.IntegratePeaks();
                graph.InvalidateVisual();
                ProcessingChanged?.Invoke(this, EventArgs.Empty);
                StatusChanged?.Invoke(this, "Integration updated");
            }
        }

        async Task CompleteGraphIntegrationEditAsync()
        {
            if (!ContextIsValid) return;

            isUpdatingControls = true;
            integrationModeCombo.SelectedIndex = (int)InjectionData.IntegrationLengthMode.Time;
            isUpdatingControls = false;

            if (experiment!.Processor.BaselineCompleted && experiment.Processor.DiscardIntegratedPoints)
                await ProcessDataAsync(replace: true, status: "Integration updated");
            else
            {
                experiment.Processor.IntegratePeaks();
                UpdateControls();
                ProcessingChanged?.Invoke(this, EventArgs.Empty);
                StatusChanged?.Invoke(this, "Integration updated");
            }
        }

        async Task CompleteGraphSplineEditAsync()
        {
            if (!ContextIsValid) return;

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
                ProcessingChanged?.Invoke(this, EventArgs.Empty);
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
            if (!ContextIsValid) return;

            experiment!.Processor.ToggleLock();
            UpdateControls();
        }

        public void ToggleSplineHandles()
        {
            if (!ContextIsValid || experiment!.Processor.Interpolator is not SplineInterpolator spline) return;
            if (spline.Algorithm != SplineInterpolator.SplineInterpolatorAlgorithm.Smooth) return;

            spline.ShowHandles = !spline.ShowHandles;
            UpdateControls();
            graph.InvalidateVisual();
        }

        public void ToggleSplinePointTimeDragging()
        {
            if (!ContextIsValid || experiment!.Processor.Interpolator is not SplineInterpolator spline) return;

            spline.AllowPointTimeDragging = !spline.AllowPointTimeDragging;
            UpdateControls();
        }

        public void ToggleIntegrationRegionCopyIncludesStart()
        {
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
            if (!ContextIsValid || experiment!.Processor.Interpolator == null) return;

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
            if (!ContextIsValid) return;

            DataManager.CopySelectedProcessToActive();
            StatusChanged?.Invoke(this, "Processing copied to active data");
        }

        void CopyProcessingToNonProcessed()
        {
            if (!ContextIsValid) return;

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
            if (!ContextIsValid) return;

            var selected = graph.SelectedInjectionIndex;
            if (selected < 0 || selected >= experiment!.InjectionCount - 1) return;

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
            graph.PeakZoomWidth = peakWidthCombo.SelectedIndex switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                _ => 1,
            };

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

                if (experiment == null)
                {
                    summaryText.Text = "No experiment selected";
                    graph.InvalidateVisual();
                    return;
                }

                if (!experiment.HasThermogram)
                {
                    summaryText.Text = "Selected item has no raw thermogram";
                    graph.InvalidateVisual();
                    return;
                }

                var processor = experiment.Processor;
                summaryText.Text = $"{experiment.DataPoints.Count} points, {experiment.InjectionCount} injections";
                baselineHeader.Text = processor.IsLocked ? "Baseline [locked]" : "Baseline";
                integrationHeader.Text = processor.IntegrationCompleted ? "Integration [complete]" : "Integration";

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

                showSplineHandlesCheck.IsEnabled = processor.Interpolator is SplineInterpolator
                {
                    Algorithm: SplineInterpolator.SplineInterpolatorAlgorithm.Smooth
                };
                moveSplinePointsCheck.IsEnabled = processor.Interpolator is SplineInterpolator;
                copyIntegrationStartCheck.IsChecked = AppSettings.IntegrationRegionCopyIncludesStart;
                convertSmoothButton.IsEnabled = processor.Interpolator is PolynomialLeastSquaresInterpolator or SegmentedBaselineInterpolator;
                convertLinearButton.IsEnabled = processor.Interpolator is not SplineInterpolator
                {
                    Algorithm: SplineInterpolator.SplineInterpolatorAlgorithm.Linear
                };

                ConfigureDegreeControls();
                ConfigureIntegrationControls();

                discardIntegratedCheck.IsChecked = processor.DiscardIntegratedPoints;
                lockProcessorButton.Content = processor.IsLocked ? "Unlock" : "Lock";
                copyNextButton.IsEnabled = graph.SelectedInjectionIndex >= 0 && graph.SelectedInjectionIndex < experiment.InjectionCount - 1;
                previousButton.IsEnabled = graph.SelectedInjectionIndex > 0;
                nextButton.IsEnabled = graph.SelectedInjectionIndex < experiment.InjectionCount - 1;

                selectionLabel.Text = graph.SelectedInjectionIndex == -1
                    ? "All injections"
                    : $"Injection #{graph.SelectedInjectionIndex + 1}";

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
                integrationModeCombo.SelectedIndex = -1;
                integrationStartSlider.IsEnabled = false;
                integrationLengthSlider.IsEnabled = false;
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

            integrationModeCombo.SelectedIndex = (int)processor.IntegrationLengthMode;
            integrationStartSlider.Minimum = minDelay;
            integrationStartSlider.Maximum = maxDelay;
            integrationStartSlider.Value = injection.IntegrationStartDelay;
            integrationLengthSlider.Minimum = 0;
            integrationLengthSlider.Maximum = maxDelay;
            integrationLengthSlider.Value = processor.IntegrationLengthMode == InjectionData.IntegrationLengthMode.Factor
                ? FactorToSlider(processor.IntegrationLengthFactor)
                : injection.IntegrationEndOffset;
            integrationLengthSlider.IsEnabled = processor.IntegrationLengthMode != InjectionData.IntegrationLengthMode.Fit;

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
                : experiment.Processor.IntegrationLengthMode == InjectionData.IntegrationLengthMode.Fit
                    ? "fit"
                    : $"{GetLengthSliderParameter():F1} s";
        }

        float GetLengthSliderParameter()
        {
            if (!ContextIsValid) return 0;

            return experiment!.Processor.IntegrationLengthMode switch
            {
                InjectionData.IntegrationLengthMode.Factor => (float)Math.Pow(5, integrationLengthSlider.Value / Math.Max(1, integrationLengthSlider.Maximum)),
                InjectionData.IntegrationLengthMode.Fit => 0,
                _ => (float)integrationLengthSlider.Value
            };
        }

        float FactorToSlider(float value)
        {
            value = Math.Max(1, value);
            return (float)(Math.Log(value, 5) * Math.Max(1, integrationLengthSlider.Maximum));
        }

        bool ContextIsValid => experiment != null && experiment.HasThermogram && experiment.Processor != null;

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
