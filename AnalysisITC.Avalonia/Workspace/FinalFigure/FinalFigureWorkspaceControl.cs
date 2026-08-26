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
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
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
        static readonly EnergyUnit?[] EnergyUnitOverrides =
        {
            null,
            EnergyUnit.Joule,
            EnergyUnit.KiloJoule,
            EnergyUnit.Cal,
            EnergyUnit.KCal
        };
        static readonly string[] EnergyUnitOverrideNames = { "Automatic", "J", "kJ", "cal", "kcal" };
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

        readonly Button exportCurrentButton = Button("Current", 0);
        readonly Button exportActiveButton = Button("Active", 0);
        readonly Button exportAllButton = Button("All", 0);

        readonly NumericUpDown widthStepper = Stepper(6, 3, 20, 0.5m, formatString: "0.##");
        readonly NumericUpDown heightStepper = Stepper(10, 4, 28, 0.5m, formatString: "0.##");
        readonly NumericUpDown fontSizeStepper = Stepper(14, 5, 24);
        readonly ComboBox energyUnitCombo = Combo(EnergyUnitOverrideNames, 0, 126);
        readonly ComboBox timeUnitCombo = Combo(TimeUnits.Select(unit => unit.GetProperties().Name).ToArray(), 1, 126);
        readonly ComboBox uncertaintyCombo = Combo(new[] { "Automatic", "SD", "CI", "SD + CI", "None" }, 1, 126);
        readonly ComboBox infoPlacementCombo = Combo(new[] { "Auto", "Upper", "Lower" }, 0, 126);

        readonly CheckBox showThermogramCheck = Check("Data graph", true, "Include the differential-power trace above the fit graph.");
        readonly CheckBox axisTitlesCheck = Check("Axis titles", true, "Draw titles for the graph axes.");
        readonly CheckBox experimentDetailsCheck = Check("Experiment details", true, "Include experiment metadata in the figure information box.");
        readonly CheckBox modelInfoCheck = Check("Model info", true, "Include the selected analysis model in the figure information box.");
        readonly CheckBox fitParametersCheck = Check("Fit parameters", true, "Include fitted parameter values in the figure information box.");
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
        readonly CheckBox fitLineCheck = Check("Fit line", true, "Draw the fitted binding curve.");
        readonly CheckBox residualsCheck = Check("Show residuals graph", true, "Include a residuals graph below the fitted heats.");
        readonly CheckBox residualGapCheck = Check("Residual gap", true, "Leave a visual gap between the fit and residual graphs.");
        readonly CheckBox zeroLineCheck = Check("Display zero enthalpy line", true, "Draw a horizontal reference line at zero enthalpy.");
        readonly CheckBox confidenceCheck = Check("Confidence band", true, "Draw the fit confidence interval around the fitted curve.");
        readonly CheckBox errorBarsCheck = Check("Error bars", true, "Draw uncertainty bars for integrated heats.");
        readonly CheckBox excludedCheck = Check("Excluded points", true, "Show points excluded from the fit.");
        readonly CheckBox excludedErrorBarsCheck = Check("Excluded error bars", false, "Draw uncertainty bars for excluded points.");
        readonly CheckBox offsetCorrectedCheck = Check("Offset-corrected heats", true, "Plot heats after applying the fitted offset correction.");

        Bitmap? bitmap;
        ITCDataContainer? selectedItem;
        ExperimentData? figureExperiment;
        string? cacheKey;
        bool isApplyingSettingsDefaults;

        public event EventHandler<string>? StatusChanged;

        internal ComboBox EnergyUnitComboForTesting => energyUnitCombo;

        public FinalFigureWorkspaceControl()
        {
            BuildLayout();
            WireEvents();
            ApplySettingsDefaults();
        }

        public ITCDataContainer? SelectedItem
        {
            get => selectedItem;
            set
            {
                if (ReferenceEquals(selectedItem, value)) return;
                selectedItem = value;
                UpdateContext();
            }
        }

        public void InvalidatePreview()
        {
            cacheKey = null;
            if (figureExperiment != null)
                RefreshPreview(force: true);
        }

        internal bool TryGetPrintTarget(out GraphPrintTarget? target)
        {
            if (figureExperiment != null)
            {
                var experiment = figureExperiment;
                target = GraphPrintTarget.FromPublicationFigure(
                    $"{experiment.Name} – Final Figure",
                    () => PublicationFigureBuilder.Build(experiment, BuildEffectiveOptions(experiment)),
                    renderer);
                return true;
            }

            target = null;
            return false;
        }

        public void ApplySettingsDefaults()
        {
            try
            {
                isApplyingSettingsDefaults = true;

                var dimensions = AppSettings.FinalFigureDimensions;
                if (dimensions.Length > 0) widthStepper.Value = (decimal)dimensions[0];
                if (dimensions.Length > 1) heightStepper.Value = (decimal)dimensions[1];

                // Automatic is intentionally independent of the legacy exact-unit
                // preference.  The figure builder resolves it from plotted values.
                energyUnitCombo.SelectedIndex = 0;
                fitLineSmoothnessCombo.SelectedIndex = Math.Max(0, Array.IndexOf(FitLineSmoothnessOptions, AppSettings.FitLineSmoothness));
                uncertaintyCombo.SelectedIndex = Math.Max(0, Array.IndexOf(UncertaintyStyles, AppSettings.UncertaintyDisplayStyle));
                experimentDetailsCheck.IsChecked = AppSettings.FinalFigureShowDetailsAsDefault;
                modelInfoCheck.IsChecked = AppSettings.FinalFigureShowModelInfoAsDefault;
                fitParametersCheck.IsChecked = AppSettings.FinalFigureShowParameterBoxAsDefault;
                residualsCheck.IsChecked = AppSettings.ShowResidualGraph;
                residualGapCheck.IsChecked = AppSettings.ShowResidualGraphGap;

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

            if (figureExperiment != null && image.Source == null)
                RefreshPreview(force: true);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            ClearBitmap();
            base.OnDetachedFromVisualTree(e);
        }

        void BuildLayout()
        {
            exportCurrentButton.Classes.Add("accent");
            ToolTip.SetTip(exportCurrentButton, "Export the currently displayed figure");
            ToolTip.SetTip(exportActiveButton, "Export figures for all active experiments");
            ToolTip.SetTip(exportAllButton, "Export figures for all experiments");

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
                InspectorTab("Data Graph", BuildDataGraphTab()),
                InspectorTab("Fit Graph", BuildFitGraphTab()));

            var exportFooter = WorkspaceControlBuilder.VerticalGroup();
            exportFooter.Spacing = 5;
            exportFooter.Children.Add(WorkspaceControlBuilder.Header("Export PDF"));
            exportFooter.Children.Add(WorkspaceControlBuilder.EqualWidthRow(
                exportCurrentButton,
                exportActiveButton,
                exportAllButton));

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
                Labeled("Base font pt", fontSizeStepper),
                Text("Ticks use the base size; axis titles use base + 1 pt; parameter boxes use 12 pt."),
                Labeled("Energy", energyUnitCombo),
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
            return panel;
        }

        Control BuildFitGraphTab()
        {
            var panel = WorkspaceControlBuilder.InspectorPanel();
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
            panel.Children.Add(Section("Shared axes", new Control[]
            {
                sharedFitXAxisCheck,
                sharedEnthalpyAxisCheck
            }));
            panel.Children.Add(Section("Fit line", new Control[]
            {
                fitLineCheck,
                Labeled("Width", fitLineWidthStepper),
                Labeled("Smoothness", fitLineSmoothnessCombo)
            }));
            panel.Children.Add(Section("Residuals", new Control[]
            {
                residualsCheck,
                residualGapCheck
            }));
            panel.Children.Add(Section("Display", new Control[]
            {
                zeroLineCheck,
                confidenceCheck,
                errorBarsCheck,
                excludedCheck,
                excludedErrorBarsCheck,
                offsetCorrectedCheck
            }));

            return panel;
        }

        void WireEvents()
        {
            previewHost.SizeChanged += (_, _) => RefreshPreview();
            exportCurrentButton.Click += async (_, _) => await ExportCurrentPdfAsync();
            exportActiveButton.Click += async (_, _) => await ExportActivePdfAsync();
            exportAllButton.Click += async (_, _) => await ExportAllPdfAsync();

            foreach (var check in AllChecks())
                check.IsCheckedChanged += (_, _) =>
                {
                    if (!isApplyingSettingsDefaults) RefreshPreview(force: true);
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
                fitLineCheck,
                zeroLineCheck,
                confidenceCheck,
                errorBarsCheck,
                excludedCheck,
                excludedErrorBarsCheck,
                offsetCorrectedCheck,
                sharedFitXAxisCheck,
                sharedEnthalpyAxisCheck
            };
        }

        IEnumerable<ComboBox> AllCombos()
        {
            return new[]
            {
                energyUnitCombo,
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
            if (figureExperiment == null)
            {
                statusText.Text = "No figure selected";
                return;
            }

            var solutionKey = figureExperiment.Solution == null ? "no-solution" : figureExperiment.Solution.GetHashCode().ToString();

            try
            {
                var options = BuildEffectiveOptions(figureExperiment);
                var document = PublicationFigureBuilder.Build(figureExperiment, options);
                var pageSize = renderer.GetPageSize(document);
                var pixelWidth = PreviewPixelWidth(pageSize);
                var nextKey = $"{figureExperiment.UniqueID}|{solutionKey}|{pixelWidth}|{options.CacheKey}";

                if (!force && cacheKey == nextKey) return;

                using var rendered = renderer.RenderBitmap(document, pixelWidth);
                var nextBitmap = ToAvaloniaBitmap(rendered);

                image.Width = pageSize.Width;
                image.Height = pageSize.Height;
                ReplaceBitmap(nextBitmap);
                cacheKey = nextKey;
                statusText.Text = figureExperiment.Solution == null
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
                EnergyUnitFamily = AppSettings.EnergyUnitFamily,
                EnergyUnitOverride = SelectedEnergyUnitOverride(),
                TimeUnit = SelectedTimeUnit(),
                ShowThermogram = showThermogramCheck.IsChecked == true,
                ShowResiduals = residualsCheck.IsChecked == true,
                ShowErrorBars = errorBarsCheck.IsChecked == true,
                ShowConfidenceBand = confidenceCheck.IsChecked == true,
                ShowExperimentDetails = experimentDetailsCheck.IsChecked == true,
                ShowFitParameters = modelInfoCheck.IsChecked == true || fitParametersCheck.IsChecked == true,
                ShowAxisTitles = axisTitlesCheck.IsChecked == true,
                ShowFitLine = fitLineCheck.IsChecked == true,
                DrawFitOffsetCorrected = offsetCorrectedCheck.IsChecked == true,
                ShowBadData = excludedCheck.IsChecked == true,
                ShowBadDataErrorBars = excludedErrorBarsCheck.IsChecked == true,
                AutoAxesIgnoresBadData = AppSettings.AutoAxesIgnoresBadData,
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

        PublicationFigureOptions BuildEffectiveOptions(ExperimentData target)
        {
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

            var documents = references
                .Select(experiment => new SharedAxisDocument(
                    experiment,
                    PublicationFigureBuilder.Build(experiment, options)))
                .ToList();

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

        EnergyUnit? SelectedEnergyUnitOverride()
        {
            var index = energyUnitCombo.SelectedIndex;
            return index >= 0 && index < EnergyUnitOverrides.Length
                ? EnergyUnitOverrides[index]
                : null;
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

            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Final Figure",
                SuggestedFileName = SanitizeFileName(figureExperiment.Name) + ".pdf",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PDF figure") { Patterns = new[] { "*.pdf" } },
                    FilePickerFileTypes.All
                }
            });

            var path = file == null ? null : GetLocalPath(file);
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";

            ExportExperimentFigure(figureExperiment, path);
            StatusChanged?.Invoke(this, "Final figure exported");
        }

        public async Task ExportCurrentPdfAsync()
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
            var experiments = DataManager.Data
                .Where(experiment => experiment.Include)
                .ToList();

            await ExportExperimentSetAsync(experiments, "Choose Active Figure Export Folder");
        }

        public async Task ExportAllPdfAsync()
        {
            await ExportExperimentSetAsync(DataManager.Data.ToList(), "Choose Figure Export Folder");
        }

        async Task ExportSingleFigureAsync(ExperimentData experiment)
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Final Figure",
                SuggestedFileName = SanitizeFileName(experiment.Name) + ".pdf",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PDF figure") { Patterns = new[] { "*.pdf" } },
                    FilePickerFileTypes.All
                }
            });

            var path = file == null ? null : GetLocalPath(file);
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";

            ExportExperimentFigure(experiment, path);
            StatusChanged?.Invoke(this, "Final figure exported");
        }

        async Task ExportExperimentSetAsync(IReadOnlyList<ExperimentData> experiments, string title)
        {
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

            Directory.CreateDirectory(folderPath);

            foreach (var target in CreateFigureExportTargets(exportable, folderPath))
                ExportExperimentFigure(target.Experiment, target.Path);

            StatusChanged?.Invoke(this, $"{exportable.Count} final figure{(exportable.Count == 1 ? "" : "s")} exported");
        }

        async Task ExportResultFiguresAsync(AnalysisResult result)
        {
            DataManager.LoadResultSolutionsToExperiments(result, markDocumentDirty: false);
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

            Directory.CreateDirectory(folderPath);

            foreach (var target in CreateFigureExportTargets(experiments, folderPath))
                ExportExperimentFigure(target.Experiment, target.Path);

            StatusChanged?.Invoke(this, $"{experiments.Count} final figure{(experiments.Count == 1 ? "" : "s")} exported");
        }

        void ExportExperimentFigure(ExperimentData experiment, string path)
        {
            var document = PublicationFigureBuilder.Build(experiment, BuildEffectiveOptions(experiment));
            renderer.WritePdf(document, path);
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

        static List<FigureExportTarget> CreateFigureExportTargets(IEnumerable<ExperimentData> experiments, string folderPath)
        {
            var targets = new List<FigureExportTarget>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var experiment in experiments)
            {
                var baseName = SanitizeFileName(experiment.Name);
                var fileName = baseName + ".pdf";
                var suffix = 2;

                while (usedNames.Contains(fileName))
                {
                    fileName = $"{baseName} ({suffix}).pdf";
                    suffix++;
                }

                usedNames.Add(fileName);
                targets.Add(new FigureExportTarget(experiment, Path.Combine(folderPath, fileName)));
            }

            return targets;
        }

        static string? GetLocalPath(IStorageFile file)
        {
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) return path;

            return file.Path.IsFile ? file.Path.LocalPath : null;
        }

        static string SanitizeFileName(string name)
        {
            var cleanName = string.IsNullOrWhiteSpace(name) ? "Untitled Figure" : name.Trim();

            foreach (var invalidChar in Path.GetInvalidFileNameChars())
                cleanName = cleanName.Replace(invalidChar, '_');

            return string.IsNullOrWhiteSpace(cleanName) ? "Untitled Figure" : cleanName;
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
