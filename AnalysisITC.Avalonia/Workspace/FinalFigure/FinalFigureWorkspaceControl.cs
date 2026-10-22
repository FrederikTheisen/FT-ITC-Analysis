using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SkiaSharp;

using AnalysisITC.Avalonia.Controls;
using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Avalonia.Printing;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Platform;
using static AnalysisITC.Avalonia.Workspace.WorkspaceControlBuilder;

namespace AnalysisITC.Avalonia.FinalFigure
{
    internal enum FinalFigureTickDensity
    {
        Sparse = 0,
        Normal = 1,
        Dense = 2
    }

    public sealed class FinalFigureWorkspaceControl : UserControl
    {
        static readonly EnergyUnitFamily[] EnergyFamilies = { EnergyUnitFamily.Joules, EnergyUnitFamily.Calories };
        static readonly TimeUnit[] TimeUnits = { TimeUnit.Second, TimeUnit.Minute, TimeUnit.Hour };
        static readonly LineSmoothness[] FitLineSmoothnessOptions =
        {
            LineSmoothness.Smooth,
            LineSmoothness.Spline,
            LineSmoothness.Linear
        };
        static readonly UncertaintyDisplayStyle[] UncertaintyStyles =
        {
            UncertaintyDisplayStyle.Automatic,
            UncertaintyDisplayStyle.StandardDeviation,
            UncertaintyDisplayStyle.ConfidenceInterval,
            UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
            UncertaintyDisplayStyle.None
        };
        static readonly string[] TickDensityOptions = { "Sparse", "Normal", "Dense" };
        const double PreviewRenderScale = 4.0;

        readonly SkiaFigureRenderer renderer = new SkiaFigureRenderer();
        readonly Border previewHost = new Border();
        readonly Image image = new Image
        {
            Stretch = Stretch.Fill
        };
        readonly TextBlock statusText = Text();
        readonly TextBlock exportSummaryText = Text();

        readonly SegmentedSelector exportSelectionSelector = new SegmentedSelector(new[] { "Selected", "Active", "All" }, 0)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        readonly Button exportButton = Button("Export", 0);
        internal Func<FilePickerSaveOptions, Task<IStorageFile?>>? SaveFilePickerForTesting { get; set; }

        readonly NumericUpDown widthStepper = Stepper(6, 3, 20, 0.5m, formatString: "0.##");
        readonly NumericUpDown heightStepper = Stepper(10, 4, 28, 0.5m, formatString: "0.##");
        readonly NumericUpDown fontSizeStepper = Stepper(14, 5, 24);
        readonly SegmentedSelector energyUnitSelector = Segmented(new[] { "Joule", "Calories" },
            Array.IndexOf(EnergyFamilies, FinalFigureModelPresentation.EnergyUnitFamily));
        readonly ComboBox timeUnitCombo = Combo(TimeUnits.Select(unit => unit.GetProperties().Name).ToArray(), 1, 126);
        readonly ComboBox uncertaintyCombo = Combo(new[] { "Automatic", "SD", "CI", "SD + CI", "None" }, 1, 126);
        readonly ComboBox infoPlacementCombo = Combo(new[] { "Auto", "Upper", "Lower" }, 0, 126);

        readonly CheckBox showThermogramCheck = Check("Data graph", true, FinalFigureModelPresentation.DataGraphTooltip);
        readonly CheckBox axisTitlesCheck = Check("Axis titles", true, "Draw titles for the graph axes.");
        readonly CheckBox experimentDetailsCheck = Check("Experiment details", true, FinalFigureModelPresentation.ExperimentDetailsTooltip);
        readonly CheckBox modelInfoCheck = Check("Model info", true, FinalFigureModelPresentation.ModelInfoTooltip);
        readonly CheckBox fitParametersCheck = Check("Fit parameters", true, FinalFigureModelPresentation.FitParametersTooltip);
        readonly CheckBox thermodynamicCheck = Check("Thermodynamic", true, "Include thermodynamic parameters in the information box.");
        readonly CheckBox derivedCheck = Check("Derived", true, "Include calculated or derived parameters in the information box.");
        readonly CheckBox offsetParameterCheck = Check("Offset parameter", false, "Include the fitted offset parameter in the information box.");
        readonly CheckBox temperatureCheck = Check("Temperature", true, "Include the experiment temperature in the information box.");
        readonly CheckBox concentrationsCheck = Check("Concentrations", true, "Include cell and syringe concentrations in the information box.");
        readonly CheckBox injectionDelayCheck = Check("Injection delay", true, "Include the injection-delay setting in the information box.");
        readonly CheckBox instrumentCheck = Check("Instrument", true, "Include the instrument name in the information box.");
        readonly CheckBox attributesCheck = Check("Attributes", true, "Include user-defined experiment attributes in the information box.");

        readonly TextBox powerAxisTitleBox = TextBox("Differential Power (<unit>)");
        readonly TextBox timeAxisTitleBox = TextBox("Time (<unit>)");
        readonly SegmentedSelector dataXTickDensitySelector = TickDensitySelector();
        readonly SegmentedSelector dataYTickDensitySelector = TickDensitySelector();
        readonly TextBox dataXMinBox = TextBox("");
        readonly TextBox dataXMaxBox = TextBox("");
        readonly TextBox dataYMinBox = TextBox("");
        readonly TextBox dataYMaxBox = TextBox("");
        readonly CheckBox sharedPowerAxisCheck = Check("Shared power axis", false, tooltip: "Use same power axis for all active experiments.");
        readonly CheckBox correctedDataCheck = Check("Corrected data", true, "Plot baseline-corrected differential-power data.");
        readonly CheckBox baselineCheck = Check("Include baseline", false, "Overlay the fitted baseline on the data graph.");
        readonly ComboBox baselineStyleCombo = Combo(new[] { "Solid", "Dashed" }, 0, 126);
        readonly ComboBox baselineLayerCombo = Combo(new[] { "Under data", "Over data" }, 1, 126);
        readonly NumericUpDown baselineWidthStepper = Stepper(2, 0.25m, 8, 0.25m, formatString: "0.##");
        readonly CheckBox integrationRegionsCheck = Check("Integration ranges", false, "Show the time ranges used to integrate injection peaks.");
        readonly ComboBox integrationRegionStyleCombo = Combo(new[] { "Bar", "Fill", "Endpoint lines" }, 1, 126);

        readonly TextBox enthalpyAxisTitleBox = TextBox("<unit> of injectant");
        readonly TextBox fitXAxisTitleBox = TextBox("");
        readonly SegmentedSelector fitXTickDensitySelector = TickDensitySelector();
        readonly SegmentedSelector fitYTickDensitySelector = TickDensitySelector();
        readonly TextBox fitXMinBox = TextBox("");
        readonly TextBox fitXMaxBox = TextBox("");
        readonly TextBox fitYMinBox = TextBox("");
        readonly TextBox fitYMaxBox = TextBox("");
        readonly CheckBox sharedFitXAxisCheck = Check("Shared X axis", false, tooltip: "Use same x for all active experiments.");
        readonly CheckBox sharedEnthalpyAxisCheck = Check("Shared enthalpy axis", false, tooltip: "Use same enthalpy for all active experiments.");
        readonly NumericUpDown symbolSizeStepper = Stepper(8, 3, 14, 0.5m, formatString: "0.#");
        readonly NumericUpDown fitLineWidthStepper = Stepper(2, 0.25m, 8, 0.25m, formatString: "0.##");
        readonly ComboBox symbolCombo = Combo(new[] { "Square", "Circle" }, 0, 126);
        readonly ComboBox fitLineSmoothnessCombo = Combo(FitLineSmoothnessOptions.Select(DisplayName).ToArray(), 1, 126);
        readonly SegmentedSelector plottedModelSelector = Segmented(new[] { "Binding", "Null", "None" },
            FinalFigureModelPresentation.SelectionIndex);
        readonly TextBlock assessmentText = Text();
        readonly CheckBox residualsCheck = Check("Show residuals graph", true, "Include a residuals graph below the fitted heats.");
        readonly CheckBox residualGapCheck = Check("Residual gap", true, "Leave a visual gap between the fit and residual graphs.");
        readonly CheckBox zeroLineCheck = Check("Display zero enthalpy line", true, "Draw a horizontal reference line at zero enthalpy.");
        readonly CheckBox confidenceCheck = Check("Confidence band", true, FinalFigureModelPresentation.ConfidenceTooltip);
        readonly CheckBox errorBarsCheck = Check("Error bars", true, "Draw uncertainty bars for integrated heats.");
        readonly CheckBox excludedCheck = Check("Excluded points", true, ExcludedInjectionPresentation.VisibilityTooltip);
        readonly CheckBox excludedErrorBarsCheck = Check("Excluded error bars", false, "Draw uncertainty bars for excluded points.");
        readonly CheckBox includeExcludedInAutoYScaleCheck = Check("Auto Y axis includes excluded points", false, FinalFigureModelPresentation.ExcludedAutoYScaleTooltip);
        readonly CheckBox offsetCorrectedCheck = Check("Offset correction", true, FinalFigureModelPresentation.OffsetCorrectionTooltip);

        Bitmap? bitmap;
        ITCDataContainer? selectedItem;
        ExperimentData? figureExperiment;
        string? cacheKey;
        bool isAttached;
        bool isApplyingSettingsDefaults;
        bool showExcludedPoints = true;

        public event EventHandler<string>? StatusChanged;

        internal SegmentedSelector EnergyUnitSelectorForTesting => energyUnitSelector;
        internal SegmentedSelector ExportSelectionSelectorForTesting => exportSelectionSelector;
        internal Button ExportButtonForTesting => exportButton;
        internal SegmentedSelector PlottedModelSelectorForTesting => plottedModelSelector;
        internal NumericUpDown LineWidthForTesting => fitLineWidthStepper;
        internal ComboBox SmoothnessForTesting => fitLineSmoothnessCombo;
        internal CheckBox ResidualsForTesting => residualsCheck;
        internal CheckBox ModelInfoForTesting => modelInfoCheck;
        internal string AssessmentTextForTesting => assessmentText.Text ?? "";
        internal CheckBox OffsetCorrectionForTesting => offsetCorrectedCheck;
        internal CheckBox ConfidenceForTesting => confidenceCheck;
        internal CheckBox SharedEnthalpyAxisForTesting => sharedEnthalpyAxisCheck;
        internal CheckBox ExcludedPointsForTesting => excludedCheck;
        internal string PreviewCacheKeyForTesting => cacheKey ?? "";

        public FinalFigureWorkspaceControl()
        {
            BuildLayout();
            WireEvents();
            ApplySettingsDefaults();
            UpdateModelControls();
        }

        public ITCDataContainer? SelectedItem
        {
            get => selectedItem;
            set
            {
                if (ReferenceEquals(selectedItem, value)) return;
                selectedItem = value;
                UpdateContext();
                RefreshExportSummary();
            }
        }

        public void InvalidatePreview()
        {
            UpdateExcludedInjectionControl();
            cacheKey = null;
            if (figureExperiment != null)
                RefreshPreview(force: true);
        }

        internal bool TryGetPrintTarget(out GraphPrintTarget? target)
        {
            if (figureExperiment != null)
            {
                var experiment = figureExperiment;
                var selection = FinalFigureModelPresentation.Selection;
                var source = CreateFigureSource(experiment, selection);
                var options = BuildEffectiveOptions(experiment, selection: selection);
                target = GraphPrintTarget.FromPublicationFigure(
                    $"{experiment.Name} – Final Figure",
                    () => PublicationFigureBuilder.Build(source, options),
                    renderer);
                return true;
            }

            target = null;
            return false;
        }

        public void ApplySettingsDefaults()
            => ApplySettingsDefaults(PreferencesChangeSet.Full);

        internal void ApplySettingsDefaults(PreferencesChangeSet changes)
        {
            bool Changed(params string[] names) => changes.IsFullApply || changes.ContainsAny(names);
            try
            {
                isApplyingSettingsDefaults = true;

                var dimensions = AppSettings.FinalFigureDimensions;
                if (Changed(nameof(PreferencesState.FinalFigureWidthCentimeters)) && dimensions.Length > 0)
                    widthStepper.Value = (decimal)dimensions[0];
                if (Changed(nameof(PreferencesState.FinalFigureHeightCentimeters)) && dimensions.Length > 1)
                    heightStepper.Value = (decimal)dimensions[1];

                if (Changed(nameof(PreferencesState.FitLineSmoothness)))
                    fitLineSmoothnessCombo.SelectedIndex = Math.Max(0, Array.IndexOf(FitLineSmoothnessOptions, AppSettings.FitLineSmoothness));
                if (Changed(nameof(PreferencesState.UncertaintyDisplayStyle)))
                    uncertaintyCombo.SelectedIndex = Math.Max(0, Array.IndexOf(UncertaintyStyles, AppSettings.UncertaintyDisplayStyle));
                if (Changed(nameof(PreferencesState.FinalFigureShowDetailsAsDefault)))
                    experimentDetailsCheck.IsChecked = AppSettings.FinalFigureShowDetailsAsDefault;
                if (Changed(nameof(PreferencesState.FinalFigureShowModelInfoAsDefault)))
                    modelInfoCheck.IsChecked = AppSettings.FinalFigureShowModelInfoAsDefault;
                if (Changed(nameof(PreferencesState.FinalFigureShowParameterBoxAsDefault)))
                    fitParametersCheck.IsChecked = AppSettings.FinalFigureShowParameterBoxAsDefault;
                if (Changed(nameof(PreferencesState.ShowResidualGraph)))
                    residualsCheck.IsChecked = AppSettings.ShowResidualGraph;
                if (Changed(nameof(PreferencesState.ShowResidualGraphGap)))
                    residualGapCheck.IsChecked = AppSettings.ShowResidualGraphGap;
                if (Changed(nameof(PreferencesState.AutoAxesIgnoresBadData)))
                    includeExcludedInAutoYScaleCheck.IsChecked = !AppSettings.AutoAxesIgnoresBadData;
                if (Changed(nameof(PreferencesState.TraceabilityModeEnabled), nameof(PreferencesState.AutoAxesIgnoresBadData)))
                    UpdateExcludedInjectionControl();

                if (Changed(nameof(PreferencesState.FinalFigureParameterDisplay)))
                {
                    var display = AppSettings.FinalFigureParameterDisplay;
                    thermodynamicCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Thermodynamic);
                    derivedCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Derived);
                    offsetParameterCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Offset);
                    temperatureCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Temperature);
                    concentrationsCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Concentrations);
                    injectionDelayCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.InjectionDelay);
                    instrumentCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Instrument);
                    attributesCheck.IsChecked = display.HasFlag(FinalFigureDisplayParameters.Attributes);
                }
            }
            finally
            {
                isApplyingSettingsDefaults = false;
            }

            cacheKey = null;
            if (figureExperiment != null)
                RefreshPreview(force: true);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            isAttached = true;

            DataManager.DataDidChange += OnExportDataChanged;
            DataManager.DataInclusionDidChange += OnExportDataChanged;
            AppSettings.SettingsDidUpdate += OnSettingsDidUpdate;
            RefreshExportSummary();

            if (figureExperiment != null && image.Source == null)
                RefreshPreview(force: true);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            isAttached = false;
            DataManager.DataDidChange -= OnExportDataChanged;
            DataManager.DataInclusionDidChange -= OnExportDataChanged;
            AppSettings.SettingsDidUpdate -= OnSettingsDidUpdate;
            ClearBitmap();
            base.OnDetachedFromVisualTree(e);
        }

        void OnSettingsDidUpdate(object? sender, EventArgs e)
        {
            var changes = PreferencesChangeSet.FromEventArgs(e);
            var refreshDefaults = changes.IsFullApply || changes.ContainsAny(
                nameof(PreferencesState.FinalFigureWidthCentimeters),
                nameof(PreferencesState.FinalFigureHeightCentimeters),
                nameof(PreferencesState.FitLineSmoothness),
                nameof(PreferencesState.UncertaintyDisplayStyle),
                nameof(PreferencesState.FinalFigureShowDetailsAsDefault),
                nameof(PreferencesState.FinalFigureShowModelInfoAsDefault),
                nameof(PreferencesState.FinalFigureShowParameterBoxAsDefault),
                nameof(PreferencesState.ShowResidualGraph),
                nameof(PreferencesState.ShowResidualGraphGap),
                nameof(PreferencesState.AutoAxesIgnoresBadData),
                nameof(PreferencesState.FinalFigureParameterDisplay),
                nameof(PreferencesState.TraceabilityModeEnabled));
            var refreshPreview = refreshDefaults || changes.ContainsAny(
                nameof(PreferencesState.EnergyUnitFamily),
                nameof(PreferencesState.NumberPrecision),
                nameof(PreferencesState.DefaultConcentrationUnit),
                nameof(PreferencesState.UnifyResidualGraphAxis),
                nameof(PreferencesState.DisplayAttributeOptions),
                nameof(PreferencesState.PublicationFigureFont),
                nameof(PreferencesState.ColorScheme),
                nameof(PreferencesState.ColorSchemeGradientMode));
            if (!refreshPreview) return;

            Dispatcher.UIThread.Post(() =>
            {
                if (refreshDefaults) ApplySettingsDefaults(changes);
                else InvalidatePreview();
            });
        }

        void BuildLayout()
        {
            exportSelectionSelector.HorizontalAlignment = HorizontalAlignment.Stretch;
            exportButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            exportButton.Classes.Add("accent");
            ToolTip.SetTip(exportSelectionSelector, "Choose which experiments to include in the final figure export");
            ToolTip.SetTip(energyUnitSelector, FinalFigureModelPresentation.EnergyUnitTooltip);

            image.Stretch = Stretch.Fill;
            AppTheme.Bind(previewHost, Border.BackgroundProperty, AppTheme.PreviewBackground);
            AppTheme.Bind(previewHost, Border.BorderBrushProperty, AppTheme.PanelBorder);
            previewHost.BorderThickness = new Thickness(1);
            previewHost.Padding = new Thickness(12);
            previewHost.Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = image
                }
            };

            var inspector = WorkspaceControlBuilder.Inspector(
                InspectorTab("General", BuildGeneralTab()),
                InspectorTab("Data graph", BuildDataGraphTab()),
                InspectorTab("Fit graph", BuildFitGraphTab()));

            var exportFooter = WorkspaceControlBuilder.VerticalGroup();
            exportFooter.Spacing = 5;
            exportFooter.HorizontalAlignment = HorizontalAlignment.Stretch;
            exportSummaryText.HorizontalAlignment = HorizontalAlignment.Stretch;
            exportFooter.Children.Add(exportSummaryText);
            exportFooter.Children.Add(exportSelectionSelector);
            exportFooter.Children.Add(exportButton);

            Content = WorkspaceControlBuilder.Workspace(
                previewHost,
                inspector,
                WorkspaceControlBuilder.InspectorFooter(exportFooter));
        }

        Control BuildGeneralTab()
        {
            var panel = WorkspaceControlBuilder.InspectorPanel();
            panel.Children.Add(Section("Page", new Control[]
            {
                Labeled("Width cm", widthStepper),
                Labeled("Height cm", heightStepper),
                Labeled("Base font", fontSizeStepper),
                Text("Ticks use the base size; axis titles use base + 1; parameter boxes use 12."),
                Labeled("Energy", energyUnitSelector),
                Labeled("Time", timeUnitCombo)
            }));
            panel.Children.Add(Section("Content", new Control[]
            {
                showThermogramCheck,
                axisTitlesCheck,
                experimentDetailsCheck,
                modelInfoCheck,
                fitParametersCheck,
                Labeled("Info box", infoPlacementCombo),
                Labeled("Uncertainty", uncertaintyCombo)
            }));
            panel.Children.Add(Section("Parameters", new Control[]
            {
                thermodynamicCheck,
                derivedCheck,
                offsetParameterCheck,
                temperatureCheck,
                concentrationsCheck,
                injectionDelayCheck,
                instrumentCheck,
                attributesCheck
            }));
            panel.Children.Add(statusText);

            return panel;
        }

        Control BuildDataGraphTab()
        {
            var panel = WorkspaceControlBuilder.InspectorPanel();

            panel.Children.Add(Section("Shared axes", new Control[]
            {
                sharedPowerAxisCheck
            }));
            panel.Children.Add(Section("Baseline", new Control[]
            {
                baselineCheck,
                Labeled("Style", baselineStyleCombo),
                Labeled("Layer", baselineLayerCombo),
                Labeled("Width", baselineWidthStepper)
            }));
            panel.Children.Add(Section("Integration ranges", new Control[]
            {
                integrationRegionsCheck,
                Labeled("Display", integrationRegionStyleCombo)
            }));
            panel.Children.Add(Section("Data graph", new Control[]
            {
                Labeled("Power title", powerAxisTitleBox),
                Labeled("Time title", timeAxisTitleBox),
                Labeled("X tick density", dataXTickDensitySelector),
                Labeled("Y tick density", dataYTickDensitySelector),
                Labeled("X min", dataXMinBox),
                Labeled("X max", dataXMaxBox),
                Labeled("Y min", dataYMinBox),
                Labeled("Y max", dataYMaxBox),
                correctedDataCheck
            }));
            return panel;
        }

        Control BuildFitGraphTab()
        {
            var panel = WorkspaceControlBuilder.InspectorPanel();
            ToolTip.SetTip(plottedModelSelector, FinalFigureModelPresentation.SelectorTooltip);
            ToolTip.SetTip(assessmentText, FinalFigureModelPresentation.AssessmentTooltip);
            ToolTip.SetTip(fitLineWidthStepper, FinalFigureModelPresentation.LineWidthTooltip);
            ToolTip.SetTip(fitLineSmoothnessCombo, FinalFigureModelPresentation.SmoothnessTooltip);
            panel.Children.Add(Section("Model", new Control[]
            {
                Labeled("Plotted model", plottedModelSelector),
                assessmentText,
                Labeled("Line width", fitLineWidthStepper),
                Labeled("Smoothness", fitLineSmoothnessCombo),
                confidenceCheck,
                offsetCorrectedCheck
            }));

            panel.Children.Add(Section("Shared axes", new Control[]
            {
                sharedFitXAxisCheck,
                sharedEnthalpyAxisCheck
            }));
            panel.Children.Add(Section("Residuals", new Control[]
            {
                residualsCheck,
                residualGapCheck
            }));
            panel.Children.Add(Section("Display", new Control[]
            {
                zeroLineCheck,
                errorBarsCheck,
                excludedCheck,
                excludedErrorBarsCheck,
                includeExcludedInAutoYScaleCheck
            }));

            panel.Children.Add(Section("Fit graph", new Control[]
            {
                Labeled("Y title", enthalpyAxisTitleBox),
                Labeled("X title", fitXAxisTitleBox),
                Labeled("X tick density", fitXTickDensitySelector),
                Labeled("Y tick density", fitYTickDensitySelector),
                Labeled("X min", fitXMinBox),
                Labeled("X max", fitXMaxBox),
                Labeled("Y min", fitYMinBox),
                Labeled("Y max", fitYMaxBox),
                Labeled("Symbol", symbolCombo),
                Labeled("Point size", symbolSizeStepper)
            }));
            return panel;
        }

        void WireEvents()
        {
            plottedModelSelector.SelectionChanged += (_, _) =>
            {
                FinalFigureModelPresentation.Selection = FinalFigureModelPresentation.FromSelectionIndex(plottedModelSelector.SelectedIndex);
                UpdateModelControls();
                RefreshPreview(force: true);
            };
            previewHost.SizeChanged += (_, _) => RefreshPreview();
            exportButton.Click += async (_, _) => await ExportSelectedSetPdfAsync();
            exportSelectionSelector.SelectionChanged += (_, _) => RefreshExportSummary();
            energyUnitSelector.SelectionChanged += (_, _) =>
            {
                FinalFigureModelPresentation.EnergyUnitFamily = EnergyFamilies[Math.Max(0, energyUnitSelector.SelectedIndex)];
                RefreshPreview(force: true);
            };

            foreach (var check in AllChecks())
                check.IsCheckedChanged += (_, _) =>
                {
                    if (isApplyingSettingsDefaults) return;
                    if (ReferenceEquals(check, excludedCheck))
                    {
                        if (ExcludedInjectionPresentation.CanHideExcludedInjections)
                            showExcludedPoints = excludedCheck.IsChecked == true;
                        UpdateExcludedInjectionControl();
                    }
                    RefreshPreview(force: true);
                };

            foreach (var combo in AllCombos())
                combo.SelectionChanged += (_, _) =>
                {
                    if (!isApplyingSettingsDefaults) RefreshPreview(force: true);
                };

            foreach (var selector in AllSegmentedSelectors())
                selector.SelectionChanged += (_, _) =>
                {
                    if (!isApplyingSettingsDefaults) RefreshPreview(force: true);
                };

            foreach (var textBox in AllTextBoxes())
            {
                textBox.LostFocus += (_, _) => RefreshPreview(force: true);
                textBox.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                        RefreshPreview(force: true);
                };
            }

            foreach (var stepper in AllSteppers())
                stepper.ValueChanged += (_, _) => RefreshPreview(force: true);
        }

        void UpdateExcludedInjectionControl()
        {
            var wasApplyingSettings = isApplyingSettingsDefaults;
            try
            {
                isApplyingSettingsDefaults = true;
                excludedCheck.IsChecked = ExcludedInjectionPresentation.ShowExcludedInjections(showExcludedPoints);
                excludedCheck.IsEnabled = ExcludedInjectionPresentation.CanHideExcludedInjections;
                ToolTip.SetTip(excludedCheck, ExcludedInjectionPresentation.VisibilityTooltip);
            }
            finally
            {
                isApplyingSettingsDefaults = wasApplyingSettings;
            }
        }

        IEnumerable<CheckBox> AllChecks()
        {
            return new[]
            {
                showThermogramCheck,
                axisTitlesCheck,
                experimentDetailsCheck,
                modelInfoCheck,
                fitParametersCheck,
                thermodynamicCheck,
                derivedCheck,
                offsetParameterCheck,
                temperatureCheck,
                concentrationsCheck,
                injectionDelayCheck,
                instrumentCheck,
                attributesCheck,
                correctedDataCheck,
                sharedPowerAxisCheck,
                baselineCheck,
                integrationRegionsCheck,
                residualsCheck,
                residualGapCheck,
                zeroLineCheck,
                confidenceCheck,
                errorBarsCheck,
                excludedCheck,
                excludedErrorBarsCheck,
                includeExcludedInAutoYScaleCheck,
                offsetCorrectedCheck,
                sharedFitXAxisCheck,
                sharedEnthalpyAxisCheck
            };
        }

        IEnumerable<ComboBox> AllCombos()
        {
            return new[]
            {
                timeUnitCombo,
                uncertaintyCombo,
                infoPlacementCombo,
                symbolCombo,
                fitLineSmoothnessCombo,
                baselineStyleCombo,
                baselineLayerCombo,
                integrationRegionStyleCombo
            };
        }

        IEnumerable<SegmentedSelector> AllSegmentedSelectors()
        {
            return TickDensitySelectors;
        }

        internal IReadOnlyList<SegmentedSelector> TickDensitySelectors => new[]
        {
            dataXTickDensitySelector,
            dataYTickDensitySelector,
            fitXTickDensitySelector,
            fitYTickDensitySelector
        };

        IEnumerable<TextBox> AllTextBoxes()
        {
            return new[]
            {
                powerAxisTitleBox,
                timeAxisTitleBox,
                dataXMinBox,
                dataXMaxBox,
                dataYMinBox,
                dataYMaxBox,
                enthalpyAxisTitleBox,
                fitXAxisTitleBox,
                fitXMinBox,
                fitXMaxBox,
                fitYMinBox,
                fitYMaxBox
            };
        }

        IEnumerable<NumericUpDown> AllSteppers()
        {
            return new[]
            {
                widthStepper,
                heightStepper,
                fontSizeStepper,
                baselineWidthStepper,
                symbolSizeStepper,
                fitLineWidthStepper
            };
        }

        void UpdateContext()
        {
            cacheKey = null;
            UpdateModelControls();

            if (selectedItem is ExperimentData experiment)
            {
                figureExperiment = experiment;
                RefreshPreview(force: true);
                return;
            }

            if (selectedItem is AnalysisResult)
            {
                figureExperiment = null;
                ClearBitmap();
                statusText.Text = "Select an experiment to preview its final figure";
                return;
            }

            figureExperiment = null;
            ClearBitmap();
            statusText.Text = "No figure selected";
        }

        void RefreshPreview(bool force = false)
        {
            if (!isAttached)
            {
                UpdateModelControls();
                cacheKey = null;
                return;
            }

            if (figureExperiment == null)
            {
                statusText.Text = "No figure selected";
                return;
            }

            var solutionKey = figureExperiment.Solution == null ? "no-solution" : figureExperiment.Solution.GetHashCode().ToString();

            try
            {
                var selection = FinalFigureModelPresentation.Selection;
                var source = CreateFigureSource(figureExperiment, selection);
                var resolved = PublicationFigureBuilder.ResolveModel(source);
                var options = BuildEffectiveOptions(figureExperiment, selection: selection);
                var document = PublicationFigureBuilder.Build(source, options);
                UpdateModelControls();
                var pageSize = renderer.GetPageSize(document);
                var pixelWidth = PreviewPixelWidth(pageSize);
                var nextKey = $"{figureExperiment.UniqueID}|{solutionKey}|{selection}|{resolved.Solution?.Guid}|{resolved.Solution?.GetHashCode()}|{figureExperiment.ProcessingRevision}|{pixelWidth}|{options.CacheKey}";

                if (!force && cacheKey == nextKey) return;

                using var rendered = renderer.RenderBitmap(document, pixelWidth);
                var nextBitmap = ToAvaloniaBitmap(rendered);

                image.Width = pageSize.Width;
                image.Height = pageSize.Height;
                ReplaceBitmap(nextBitmap);
                cacheKey = nextKey;
                statusText.Text = resolved.IsUnavailable
                    ? $"{figureExperiment.Name}: preview without fitted solution"
                    : $"{figureExperiment.Name}: publication figure";
            }
            catch (Exception ex)
            {
                ClearBitmap();
                statusText.Text = $"Could not render figure: {ex.Message}";
            }
        }

        int PreviewPixelWidth(SKSize pageSize)
        {
            return Math.Max(800, Math.Min(4096, (int)Math.Round(pageSize.Width * PreviewRenderScale)));
        }

        void ReplaceBitmap(Bitmap nextBitmap)
        {
            var oldBitmap = bitmap;
            bitmap = nextBitmap;
            image.Source = nextBitmap;
            oldBitmap?.Dispose();
        }

        void ClearBitmap()
        {
            var oldBitmap = bitmap;
            bitmap = null;
            cacheKey = null;
            image.Source = null;
            oldBitmap?.Dispose();
        }

        PublicationFigureOptions BuildOptions()
        {
            var defaults = new PublicationFigureOptions();
            var display = BuildDisplayParameters();

            return new PublicationFigureOptions
            {
                PlotWidthCentimeters = StepperValue(widthStepper, defaults.PlotWidthCentimeters),
                PlotHeightCentimeters = StepperValue(heightStepper, defaults.PlotHeightCentimeters),
                FontSize = StepperValue(fontSizeStepper, defaults.FontSize),
                EnergyUnitFamily = FinalFigureModelPresentation.EnergyUnitFamily,
                TimeUnit = SelectedTimeUnit(),
                ShowThermogram = showThermogramCheck.IsChecked == true,
                ShowResiduals = residualsCheck.IsChecked == true,
                ShowErrorBars = errorBarsCheck.IsChecked == true,
                ShowConfidenceBand = confidenceCheck.IsChecked == true,
                ShowExperimentDetails = experimentDetailsCheck.IsChecked == true,
                ShowFitParameters = modelInfoCheck.IsChecked == true || fitParametersCheck.IsChecked == true,
                ShowAxisTitles = axisTitlesCheck.IsChecked == true,
                ShowFitLine = true,
                DrawFitOffsetCorrected = offsetCorrectedCheck.IsChecked == true,
                ShowBadData = showExcludedPoints,
                ShowBadDataErrorBars = excludedErrorBarsCheck.IsChecked == true,
                AutoAxesIgnoresBadData = includeExcludedInAutoYScaleCheck.IsChecked != true,
                IncludeResidualGraphGap = residualGapCheck.IsChecked == true,
                SanitizeTicks = true,
                DrawBaselineCorrected = correctedDataCheck.IsChecked == true,
                ShowBaseline = baselineCheck.IsChecked == true,
                BaselineStyle = baselineStyleCombo.SelectedIndex == 1 ? PublicationBaselineStyle.Dashed : PublicationBaselineStyle.Solid,
                BaselineLayer = baselineLayerCombo.SelectedIndex == 0 ? PublicationBaselineLayer.UnderData : PublicationBaselineLayer.OverData,
                BaselineWidth = StepperValue(baselineWidthStepper, defaults.BaselineWidth),
                ShowIntegrationRegions = integrationRegionsCheck.IsChecked == true,
                IntegrationRegionStyle = (PublicationIntegrationRegionStyle)Math.Max(0, integrationRegionStyleCombo.SelectedIndex),
                ShowZeroLine = zeroLineCheck.IsChecked == true,
                DataXTickCount = TickCountForDensity(dataXTickDensitySelector),
                DataYTickCount = TickCountForDensity(dataYTickDensitySelector),
                FitXTickCount = TickCountForDensity(fitXTickDensitySelector),
                FitYTickCount = TickCountForDensity(fitYTickDensitySelector),
                InformationBoxPlacement = SelectedInfoBoxPlacement(),
                SymbolShape = symbolCombo.SelectedIndex == 1 ? PublicationSymbolShape.Circle : PublicationSymbolShape.Square,
                SymbolSize = StepperValue(symbolSizeStepper, defaults.SymbolSize),
                FitLineWidth = StepperValue(fitLineWidthStepper, defaults.FitLineWidth),
                FitLineSmoothness = SelectedFitLineSmoothness(),
                PowerAxisTitle = string.IsNullOrWhiteSpace(powerAxisTitleBox.Text) ? defaults.PowerAxisTitle : powerAxisTitleBox.Text!,
                TimeAxisTitle = string.IsNullOrWhiteSpace(timeAxisTitleBox.Text) ? defaults.TimeAxisTitle : timeAxisTitleBox.Text!,
                EnthalpyAxisTitle = string.IsNullOrWhiteSpace(enthalpyAxisTitleBox.Text) ? defaults.EnthalpyAxisTitle : enthalpyAxisTitleBox.Text!,
                XAxisTitle = fitXAxisTitleBox.Text ?? "",
                DataXAxisMinimum = ParseOptionalDouble(dataXMinBox.Text),
                DataXAxisMaximum = ParseOptionalDouble(dataXMaxBox.Text),
                DataYAxisMinimum = ParseOptionalDouble(dataYMinBox.Text),
                DataYAxisMaximum = ParseOptionalDouble(dataYMaxBox.Text),
                FitXAxisMinimum = ParseOptionalDouble(fitXMinBox.Text),
                FitXAxisMaximum = ParseOptionalDouble(fitXMaxBox.Text),
                FitYAxisMinimum = ParseOptionalDouble(fitYMinBox.Text),
                FitYAxisMaximum = ParseOptionalDouble(fitYMaxBox.Text),
                DisplayParameters = display,
                AttributeOptions = AppSettings.DisplayAttributeOptions,
                TextUncertaintyStyle = SelectedUncertaintyStyle()
            };
        }

        PublicationFigureOptions BuildEffectiveOptions(ExperimentData target, AnalysisResult? owner = null,
            PublicationFigureModelSelection? selection = null)
        {
            var selectedModel = selection ?? FinalFigureModelPresentation.Selection;
            var options = BuildOptions();
            if (sharedPowerAxisCheck.IsChecked != true &&
                sharedFitXAxisCheck.IsChecked != true &&
                sharedEnthalpyAxisCheck.IsChecked != true)
            {
                return options;
            }

            var references = DataManager.IncludedData
                .Where(experiment => experiment != null)
                .GroupBy(experiment => experiment.UniqueID)
                .Select(group => group.First())
                .ToList();

            if (references.Count == 0)
                references.Add(target);

            // Each build resolves its unit into the shared options, so record it per figure.
            var units = new List<EnergyUnit>();
            List<SharedAxisDocument> BuildDocuments() => references
                .Select(experiment =>
                {
                    var document = PublicationFigureBuilder.Build(SharedAxisSource(experiment, selectedModel, owner), options);
                    units.Add(options.ResolvedEnergyUnit);
                    return new SharedAxisDocument(experiment, document);
                })
                .ToList();
            var documents = BuildDocuments();

            // Shared enthalpy ranges need one unit. The automatic resolver picks the larger unit
            // when any value reaches its threshold, so the largest per-figure unit equals the
            // unit resolved from all figures together.
            if (sharedEnthalpyAxisCheck.IsChecked == true && options.EnergyUnitOverride == null
                && units.Distinct().Count() > 1)
            {
                options.EnergyUnitOverride = units.OrderBy(Energy.ScaleFactor).First();
                documents = BuildDocuments();
            }

            if (sharedPowerAxisCheck.IsChecked == true)
            {
                var range = SharedRange(
                    documents.Select(item => item.Document.ThermogramPanel?.YAxis),
                    options.DataYAxisMinimum,
                    options.DataYAxisMaximum);
                options.DataYAxisMinimum = range.Minimum;
                options.DataYAxisMaximum = range.Maximum;
            }

            if (sharedFitXAxisCheck.IsChecked == true)
            {
                var range = SharedRange(
                    documents
                        .Where(item => item.Experiment.AxisType == target.AxisType)
                        .Select(item => item.Document.FitPanel?.XAxis),
                    options.FitXAxisMinimum,
                    options.FitXAxisMaximum);
                options.FitXAxisMinimum = range.Minimum;
                options.FitXAxisMaximum = range.Maximum;
            }

            if (sharedEnthalpyAxisCheck.IsChecked == true)
            {
                var range = SharedRange(
                    documents.Select(item => item.Document.FitPanel?.YAxis),
                    options.FitYAxisMinimum,
                    options.FitYAxisMaximum);
                options.FitYAxisMinimum = range.Minimum;
                options.FitYAxisMaximum = range.Maximum;

                if (AppSettings.UnifyResidualGraphAxis)
                {
                    var residualRange = SharedRange(
                        documents.Select(item => item.Document.ResidualPanel?.YAxis),
                        options.ResidualYAxisMinimum,
                        options.ResidualYAxisMaximum);
                    options.ResidualYAxisMinimum = residualRange.Minimum;
                    options.ResidualYAxisMaximum = residualRange.Maximum;
                }
            }

            return options;
        }

        internal static PublicationFigureSource CreateFigureSource(ExperimentData experiment,
            PublicationFigureModelSelection selection, AnalysisResult? owner = null, SolutionInterface? member = null)
        {
            if (owner != null && member == null)
                member = owner.Solution?.Solutions?.FirstOrDefault(solution => ReferenceEquals(solution?.Data, experiment));
            return new PublicationFigureSource(experiment, owner == null ? experiment.Solution : member,
                owner, modelSelection: selection);
        }

        static PublicationFigureSource SharedAxisSource(ExperimentData experiment,
            PublicationFigureModelSelection selection, AnalysisResult? owner)
        {
            var member = owner?.Solution?.Solutions?.FirstOrDefault(solution => ReferenceEquals(solution?.Data, experiment));
            return CreateFigureSource(experiment, selection, member == null ? null : owner, member);
        }

        void UpdateModelControls()
        {
            var binding = FinalFigureModelPresentation.Selection == PublicationFigureModelSelection.BindingModel;
            var hasModel = FinalFigureModelPresentation.Selection != PublicationFigureModelSelection.None;
            fitLineWidthStepper.IsEnabled = hasModel;
            fitLineSmoothnessCombo.IsEnabled = hasModel;
            residualsCheck.IsEnabled = hasModel;
            residualGapCheck.IsEnabled = hasModel;
            foreach (var control in new[] { modelInfoCheck, fitParametersCheck, thermodynamicCheck, derivedCheck, offsetParameterCheck })
                control.IsEnabled = hasModel;
            offsetCorrectedCheck.IsEnabled = binding;
            confidenceCheck.IsEnabled = binding;
            var data = selectedItem as ExperimentData;
            assessmentText.Text = data == null ? FinalFigureModelPresentation.NoAssessmentText : FinalFigureModelPresentation.AssessmentText(
                CreateFigureSource(data, FinalFigureModelPresentation.Selection));
        }

        internal PublicationFigureDocument BuildPreviewDocumentForTesting()
        {
            if (figureExperiment == null) throw new InvalidOperationException("No figure selected.");
            var selection = FinalFigureModelPresentation.Selection;
            return PublicationFigureBuilder.Build(CreateFigureSource(figureExperiment, selection),
                BuildEffectiveOptions(figureExperiment, selection: selection));
        }

        static (double? Minimum, double? Maximum) SharedRange(
            IEnumerable<PublicationAxis?> axes,
            double? minimum,
            double? maximum)
        {
            var available = axes.Where(axis => axis != null).ToList();
            if (available.Count == 0) return (minimum, maximum);

            if (!minimum.HasValue)
                minimum = available.Min(axis => axis!.Minimum);
            if (!maximum.HasValue)
                maximum = available.Max(axis => axis!.Maximum);

            return (minimum, maximum);
        }

        internal PublicationFigureOptions GetOptionsSnapshot()
        {
            return BuildOptions();
        }

        FinalFigureDisplayParameters BuildDisplayParameters()
        {
            var display = FinalFigureDisplayParameters.None;

            if (modelInfoCheck.IsChecked == true)
                display |= FinalFigureDisplayParameters.Model;

            if (fitParametersCheck.IsChecked == true)
            {
                if (thermodynamicCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Thermodynamic;
                if (derivedCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Derived;
                if (offsetParameterCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Offset;
            }

            if (experimentDetailsCheck.IsChecked == true)
            {
                if (temperatureCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Temperature;
                if (concentrationsCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Concentrations;
                if (injectionDelayCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.InjectionDelay;
                if (instrumentCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Instrument;
                if (attributesCheck.IsChecked == true)
                    display |= FinalFigureDisplayParameters.Attributes;
            }

            return display;
        }

        TimeUnit SelectedTimeUnit()
        {
            return timeUnitCombo.SelectedIndex >= 0 && timeUnitCombo.SelectedIndex < TimeUnits.Length
                ? TimeUnits[timeUnitCombo.SelectedIndex]
                : TimeUnit.Minute;
        }

        UncertaintyDisplayStyle SelectedUncertaintyStyle()
        {
            return uncertaintyCombo.SelectedIndex >= 0 && uncertaintyCombo.SelectedIndex < UncertaintyStyles.Length
                ? UncertaintyStyles[uncertaintyCombo.SelectedIndex]
                : AppSettings.UncertaintyDisplayStyle;
        }

        PublicationInfoBoxPlacement SelectedInfoBoxPlacement()
        {
            return infoPlacementCombo.SelectedIndex switch
            {
                1 => PublicationInfoBoxPlacement.Upper,
                2 => PublicationInfoBoxPlacement.Lower,
                _ => PublicationInfoBoxPlacement.Auto
            };
        }

        LineSmoothness SelectedFitLineSmoothness()
        {
            return fitLineSmoothnessCombo.SelectedIndex >= 0 && fitLineSmoothnessCombo.SelectedIndex < FitLineSmoothnessOptions.Length
                ? FitLineSmoothnessOptions[fitLineSmoothnessCombo.SelectedIndex]
                : AppSettings.FitLineSmoothness;
        }

        static SegmentedSelector TickDensitySelector()
        {
            var selector = Segmented(TickDensityOptions, (int)FinalFigureTickDensity.Normal, 126);
            ToolTip.SetTip(selector, "Choose a sparse, normal, or dense arrangement of rounded tick locations.");
            return selector;
        }

        internal static int TickCountForDensity(FinalFigureTickDensity density)
        {
            return density switch
            {
                FinalFigureTickDensity.Sparse => 3,
                FinalFigureTickDensity.Dense => 14,
                _ => 7
            };
        }

        static int TickCountForDensity(SegmentedSelector selector)
        {
            var density = selector.SelectedIndex switch
            {
                (int)FinalFigureTickDensity.Sparse => FinalFigureTickDensity.Sparse,
                (int)FinalFigureTickDensity.Dense => FinalFigureTickDensity.Dense,
                _ => FinalFigureTickDensity.Normal
            };
            return TickCountForDensity(density);
        }

        static string DisplayName(LineSmoothness smoothness)
        {
            return smoothness switch
            {
                LineSmoothness.Linear => "Linear",
                LineSmoothness.Smooth => "Smooth",
                LineSmoothness.Spline => "Spline",
                _ => smoothness.ToString()
            };
        }

        public async Task ExportPdfAsync()
        {
            await RunExportActionAsync("Final figure export", ExportPdfCoreAsync);
        }

        async Task ExportPdfCoreAsync()
        {
            var selection = FinalFigureModelPresentation.Selection;
            if (selectedItem is AnalysisResult result)
            {
                await ExportResultFiguresAsync(result);
                return;
            }

            if (figureExperiment == null)
            {
                StatusChanged?.Invoke(this, "No figure selected");
                return;
            }

            var experiment = figureExperiment;

            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null && SaveFilePickerForTesting == null) return;

            var pickerOptions = new FilePickerSaveOptions
            {
                Title = "Export Final Figure",
                SuggestedFileName = SanitizeFileName(experiment.Name) + ".pdf",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PDF figure") { Patterns = new[] { "*.pdf" } },
                    FilePickerFileTypes.All
                }
            };
            var file = SaveFilePickerForTesting != null
                ? await SaveFilePickerForTesting(pickerOptions)
                : await storage!.SaveFilePickerAsync(pickerOptions);

            var selectedPath = file == null ? null : GetLocalPath(file);
            if (string.IsNullOrWhiteSpace(selectedPath)) return;
            var path = selectedPath!;
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";
            var approved = ExportBatchWriter.CaptureExistingPaths(new[] { path });
            if (!string.Equals(selectedPath, path, StringComparison.Ordinal) && approved.Contains(path) &&
                !PlatformServices.ExportPromptService.ConfirmOverwrite(new[] { path }))
                return;

            WriteSingleFigure(experiment, path, selection, approved);
        }

        public async Task ExportCurrentPdfAsync()
        {
            await RunExportActionAsync("Final figure export", ExportCurrentPdfCoreAsync);
        }

        async Task ExportCurrentPdfCoreAsync()
        {
            if (figureExperiment == null)
            {
                StatusChanged?.Invoke(this, "No figure selected");
                return;
            }

            await ExportSingleFigureAsync(figureExperiment);
        }

        public async Task ExportActivePdfAsync()
        {
            await RunExportActionAsync("Final figure export", ExportActivePdfCoreAsync);
        }

        async Task ExportActivePdfCoreAsync()
        {
            var experiments = DataManager.Data
                .Where(experiment => experiment.Include)
                .ToList();

            await ExportExperimentSetAsync(experiments, "Choose Active Figure Export Folder");
        }

        public async Task ExportAllPdfAsync()
        {
            await RunExportActionAsync("Final figure export", ExportAllPdfCoreAsync);
        }

        async Task ExportAllPdfCoreAsync()
        {
            await ExportExperimentSetAsync(DataManager.Data.ToList(), "Choose Figure Export Folder");
        }

        async Task ExportSelectedSetPdfAsync()
        {
            await RunExportActionAsync("Final figure export", async () =>
            {
                switch ((ExportDataSelection)exportSelectionSelector.SelectedIndex)
                {
                    case ExportDataSelection.SelectedData:
                        await ExportCurrentPdfCoreAsync();
                        break;
                    case ExportDataSelection.IncludedData:
                        await ExportActivePdfCoreAsync();
                        break;
                    case ExportDataSelection.AllData:
                        await ExportAllPdfCoreAsync();
                        break;
                }
            });
        }

        void OnExportDataChanged(object? sender, ExperimentData? experiment)
        {
            Dispatcher.UIThread.Post(() =>
            {
                RefreshExportSummary();
                InvalidatePreview();
            });
        }

        void RefreshExportSummary()
        {
            var count = GetExportSelectionCount();
            exportSummaryText.Text = count switch
            {
                0 => "No figures selected for export",
                1 => "1 figure selected for export",
                _ => $"{count:N0} figures selected for export"
            };
        }

        int GetExportSelectionCount()
        {
            IEnumerable<ExperimentData> experiments = (ExportDataSelection)exportSelectionSelector.SelectedIndex switch
            {
                ExportDataSelection.SelectedData => figureExperiment == null
                    ? Enumerable.Empty<ExperimentData>()
                    : new[] { figureExperiment },
                ExportDataSelection.IncludedData => DataManager.Data.Where(experiment => experiment.Include),
                ExportDataSelection.AllData => DataManager.Data,
                _ => Enumerable.Empty<ExperimentData>()
            };

            return experiments
                .GroupBy(experiment => experiment.UniqueID)
                .Count();
        }

        async Task ExportSingleFigureAsync(ExperimentData experiment)
        {
            var selection = FinalFigureModelPresentation.Selection;
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null && SaveFilePickerForTesting == null) return;

            var pickerOptions = new FilePickerSaveOptions
            {
                Title = "Export Final Figure",
                SuggestedFileName = SanitizeFileName(experiment.Name) + ".pdf",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PDF figure") { Patterns = new[] { "*.pdf" } },
                    FilePickerFileTypes.All
                }
            };
            var file = SaveFilePickerForTesting != null
                ? await SaveFilePickerForTesting(pickerOptions)
                : await storage!.SaveFilePickerAsync(pickerOptions);

            var selectedPath = file == null ? null : GetLocalPath(file);
            if (string.IsNullOrWhiteSpace(selectedPath)) return;
            var path = selectedPath!;
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";
            var approved = ExportBatchWriter.CaptureExistingPaths(new[] { path });
            if (!string.Equals(selectedPath, path, StringComparison.Ordinal) && approved.Contains(path) &&
                !PlatformServices.ExportPromptService.ConfirmOverwrite(new[] { path }))
                return;

            WriteSingleFigure(experiment, path, selection, approved);
        }

        async Task ExportExperimentSetAsync(IReadOnlyList<ExperimentData> experiments, string title)
        {
            var selection = FinalFigureModelPresentation.Selection;
            var exportable = experiments
                .Where(experiment => experiment != null)
                .GroupBy(experiment => experiment.UniqueID)
                .Select(group => group.First())
                .ToList();

            if (exportable.Count == 0)
            {
                StatusChanged?.Invoke(this, "No experiment figures to export");
                return;
            }

            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

            var folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(folderPath)) return;

            var projectFolderName = GetProjectFolderName();
            var projectFolderPath = Path.Combine(folderPath, projectFolderName);

            ExportFigureBatch(exportable, projectFolderPath, selection, projectFolderName);
        }

        async Task ExportResultFiguresAsync(AnalysisResult result)
        {
            var selection = FinalFigureModelPresentation.Selection;
            var experiments = GetResultExperiments(result).ToList();

            if (experiments.Count == 0)
            {
                StatusChanged?.Invoke(this, "Selected result has no experiment figures");
                return;
            }

            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose Figure Export Folder",
                AllowMultiple = false
            });

            var folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(folderPath)) return;

            ExportResultFiguresToFolder(result, experiments, folderPath, selection);
        }

        internal int ExportResultFiguresToFolder(AnalysisResult result, IReadOnlyList<ExperimentData> experiments,
            string folderPath, PublicationFigureModelSelection selection)
        {
            var resultFolderName = FigureExportPlanner.SanitizeFileName(result.Name) ?? "Analysis Result";
            var resultFolderPath = Path.Combine(folderPath, resultFolderName);
            var exportTargets = FigureExportPlanner.CreateTargets(experiments, resultFolderPath);
            if (exportTargets.Count == 0) return 0;

            var members = exportTargets.Select(target =>
            {
                var solution = result.Solution.Solutions.FirstOrDefault(candidate =>
                    ReferenceEquals(candidate?.Data, target.Experiment));
                if (solution?.Model == null)
                    throw new InvalidOperationException($"No fitted model is available for {target.Experiment.Name} in this analysis result.");
                return (Target: target, Solution: solution);
            }).ToArray();
            var paths = members.Select(member => member.Target.Path).ToArray();
            var approved = ExportBatchWriter.CaptureExistingPaths(paths);
            if (!PlatformServices.ExportPromptService.ConfirmOverwrite(paths)) return 0;

            var outcome = ExportBatchWriter.Write(paths, approved,
                (index, stream) => WriteFigureToStream(members[index].Target.Experiment,
                    members[index].Target.Path, selection, result, members[index].Solution, stream),
                () => Directory.CreateDirectory(resultFolderPath));
            if (!outcome.IsSuccess)
            {
                ReportExportFailure(outcome, "Associated final figure export");
                return outcome.CompletedCount;
            }

            StatusChanged?.Invoke(this, $"{paths.Length} final figure{(paths.Length == 1 ? "" : "s")} exported");
            return paths.Length;
        }

        internal void ExportExperimentFigure(ExperimentData experiment, string path,
            PublicationFigureModelSelection selection, AnalysisResult? owner = null, SolutionInterface? ownerSolution = null)
        {
            var options = BuildEffectiveOptions(experiment, owner, selection);
            var source = CreateFigureSource(experiment, selection, owner, ownerSolution);
            var document = PublicationFigureBuilder.Build(source, options);
            renderer.WritePdf(document, path);
        }

        void WriteSingleFigure(ExperimentData experiment, string path, PublicationFigureModelSelection selection,
            ISet<string>? approvedPaths = null)
        {
            var approved = approvedPaths ?? ExportBatchWriter.CaptureExistingPaths(new[] { path });
            var outcome = ExportBatchWriter.Write(new[] { path }, approved,
                (_, stream) => WriteFigureToStream(experiment, path, selection, null, null, stream));
            if (!outcome.IsSuccess)
            {
                ReportExportFailure(outcome, "Final figure export");
                return;
            }

            StatusChanged?.Invoke(this, "Final figure exported");
        }

        internal void ExportSingleFigureToPathForTesting(ExperimentData experiment, string path,
            PublicationFigureModelSelection selection) => WriteSingleFigure(experiment, path, selection);

        void WriteFigureToStream(ExperimentData experiment, string path,
            PublicationFigureModelSelection selection, AnalysisResult? owner, SolutionInterface? ownerSolution, Stream stream)
        {
            var options = BuildEffectiveOptions(experiment, owner, selection);
            var source = CreateFigureSource(experiment, selection, owner, ownerSolution);
            var document = PublicationFigureBuilder.Build(source, options);
            renderer.WritePdf(document, stream);
        }

        internal int ExportFigureBatch(IEnumerable<ExperimentData> experiments, string folderPath,
            PublicationFigureModelSelection selection, string? statusFolderName = null)
        {
            var targets = FigureExportPlanner.CreateTargets(experiments, folderPath);
            if (targets.Count == 0) return 0;

            var paths = targets.Select(target => target.Path).ToArray();
            var approved = ExportBatchWriter.CaptureExistingPaths(paths);
            if (!PlatformServices.ExportPromptService.ConfirmOverwrite(paths)) return 0;

            var outcome = ExportBatchWriter.Write(paths, approved,
                (index, stream) => WriteFigureToStream(targets[index].Experiment, targets[index].Path,
                    selection, null, null, stream),
                () => Directory.CreateDirectory(folderPath));
            if (!outcome.IsSuccess)
            {
                ReportExportFailure(outcome, "Final figure export");
                return outcome.CompletedCount;
            }

            var folderDescription = string.IsNullOrWhiteSpace(statusFolderName) ? Path.GetFileName(folderPath) : statusFolderName;
            StatusChanged?.Invoke(this, $"{paths.Length} final figure{(paths.Length == 1 ? "" : "s")} exported to {folderDescription}");
            return paths.Length;
        }

        async Task RunExportActionAsync(string description, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                ReportExportFailure(ExportOutcome.Failure(1, ex), description);
            }
        }

        internal Task RunExportActionForTesting(Func<Task> action) =>
            RunExportActionAsync("Final figure export", action);

        void ReportExportFailure(ExportOutcome outcome, string description)
        {
            var message = ExportFailurePresentation.FormatMessage(description, outcome);
            statusText.Text = message;
            StatusChanged?.Invoke(this, message);
            StatusBarManager.SetStatus(message, 5000);
            ExportFailurePresentation.Report(description, outcome);
        }

        static string GetProjectFolderName()
        {
            var projectPath = FTITCFormat.CurrentAccessedAppDocumentPath;
            return FigureExportPlanner.GetProjectFolderName(projectPath, DateTime.Now);
        }

        static IEnumerable<ExperimentData> GetResultExperiments(AnalysisResult result)
        {
            return result.Solution?.Solutions?
                .Where(solution => solution?.Data != null)
                .Select(solution => solution.Data)
                .Where(experiment => experiment != null)
                .GroupBy(experiment => experiment.UniqueID)
                .Select(group => group.First())
                ?? Enumerable.Empty<ExperimentData>();
        }

        static string? GetLocalPath(IStorageFile file)
        {
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) return path;

            return file.Path.IsFile ? file.Path.LocalPath : null;
        }

        static string SanitizeFileName(string name)
        {
            return FigureExportPlanner.SanitizeFileName(name) ?? "Untitled Figure";
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

        static double? ParseOptionalDouble(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return null;
        }

        static double StepperValue(NumericUpDown stepper, double fallback)
        {
            return stepper.Value.HasValue ? decimal.ToDouble(stepper.Value.Value) : fallback;
        }

        static int StepperIntValue(NumericUpDown stepper, int fallback)
        {
            return stepper.Value.HasValue ? decimal.ToInt32(stepper.Value.Value) : fallback;
        }

        sealed class FigureExportTarget
        {
            public FigureExportTarget(ExperimentData experiment, string path)
            {
                Experiment = experiment;
                Path = path;
            }

            public ExperimentData Experiment { get; }
            public string Path { get; }
        }

        sealed class SharedAxisDocument
        {
            public SharedAxisDocument(ExperimentData experiment, PublicationFigureDocument document)
            {
                Experiment = experiment;
                Document = document;
            }

            public ExperimentData Experiment { get; }
            public PublicationFigureDocument Document { get; }
        }
    }
}
