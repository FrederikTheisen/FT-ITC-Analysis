using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using AppKit;
using CoreGraphics;
using Foundation;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
namespace AnalysisITC
{
    sealed class MacPreferencesWindowController : NSWindowController
    {
        const double WindowWidth = 600;
        const double InitialContentHeight = 520;
        const double MaximumWindowHeight = 800;
        const double WindowChromeHeight = 88;
        static readonly int[] AutoSaveIntervalChoices = { 1, 2, 5, 10, 20, 30 };

        readonly PreferencesTabViewController tabController;
        readonly Dictionary<int, PreferencesPaneController> panes =
            new Dictionary<int, PreferencesPaneController>();

        int selectedPaneIndex;
        bool hasShown;

        readonly TaggedSegmentedControl energyUnitControl;
        readonly NSPopUpButton concentrationUnitPopup;
        readonly NSPopUpButton instrumentPopup;
        readonly TaggedSegmentedControl numberPrecisionControl;
        readonly NSPopUpButton uncertaintyPopup;
        ColorSchemes stagedColorScheme;
        ColorSchemeGradientMode stagedColorGradientMode;
        readonly NSTextField referenceTemperatureField = Field();
        readonly NSTextField minimumTemperatureSpanField = Field();
        readonly NSTextField minimumIonSpanField = Field();
        readonly NSButton includeBufferCheck = Check("Include buffer in ionic-strength calculation");
        readonly NSButton onlineChecksCheck = Check("Check for updates and online resources on launch");
        readonly NSButton confirmDeleteCheck = Check("Confirm remove and delete actions");
        readonly NSButton discardOrphanCheck = Check("Automatically discard injections outside the thermogram range");
        readonly NSButton autoSaveEnabledCheck = Check("Enable autosave");
        readonly SliderValueControl autoSaveIntervalSlider;
        readonly NSTextField autoSaveLimitField = Field();
        readonly NSButton recoveryPromptCheck = Check("Prompt to recover after an interrupted session");

        readonly TaggedSegmentedControl dilutionControl;
        readonly NSPopUpButton peakFitPopup;
        readonly NSPopUpButton bufferSubtractionPopup;
        readonly TaggedSegmentedControl splineDensityControl;
        readonly NSPopUpButton splineHandlePopup;
        readonly NSButton discardIntegrationCheck = Check("Discard integration regions when estimating the baseline");
        readonly NSButton reprocessIntegratedCheck = Check("Reprocess integrated heats when loading a project");
        readonly NSButton splineTimeDraggingCheck = Check("Allow spline-point time dragging by default");
        readonly NSButton copyIntegrationStartCheck = Check("Copy integration start with the selected region");

        readonly TaggedSegmentedControl solverControl;
        readonly NSPopUpButton errorMethodPopup;
        readonly TaggedSegmentedControl parameterLimitControl;
        readonly SliderValueControl bootstrapIterationsSlider;
        readonly SliderValueControl optimizerToleranceSlider;
        readonly SliderValueControl maximumIterationsSlider;
        readonly NSTextField concentrationVarianceField = Field();
        readonly NSButton concentrationBootstrapCheck = Check("Include concentration errors in bootstrap analysis");
        readonly NSButton weightedFittingCheck = Check("Use injection-error weighted fitting");
        readonly NSButton createSingleResultCheck = Check("Create single-experiment analysis results");
        readonly NSButton createGlobalResultCheck = Check("Create global analysis results");
        readonly NSButton autoOpenResultCheck = Check("Open new analysis results automatically");

        readonly NSPopUpButton exportSelectionPopup;
        readonly TaggedSegmentedControl fitLineControl;
        readonly NSPopUpButton attributeDisplayPopup;
        readonly NSTextField exportDecimalsField = Field();
        readonly NSTextField figureWidthField = Field();
        readonly NSTextField figureHeightField = Field();
        readonly NSButton unifyTimeAxisCheck = Check("Use a unified time axis for exports");
        readonly NSButton exportCorrectedCheck = Check("Export baseline-corrected data");
        readonly NSButton exportFitPointsCheck = Check("Export fit points with peaks");
        readonly NSButton exportMolarRatioCheck = Check("Molar ratio");
        readonly NSButton exportInjectionInfoCheck = Check("Injection information");
        readonly NSButton exportConcentrationsCheck = Check("Concentrations");
        readonly NSButton exportIncludedCheck = Check("Included state");
        readonly NSButton exportPeakCheck = Check("Peak heats");
        readonly NSButton exportFitCheck = Check("Fit values");
        readonly NSButton showResidualCheck = Check("Show residual graph");
        readonly NSButton residualGapCheck = Check("Show a gap above the residual graph");
        readonly NSButton unifyResidualAxisCheck = Check("Use the same residual axis across graphs");
        readonly NSButton parameterBoxCheck = Check("Show parameter box by default");
        readonly NSButton experimentDetailsCheck = Check("Show experiment details by default");
        readonly NSButton modelInfoCheck = Check("Show model information by default");
        readonly NSButton autoAxesCheck = Check("Automatic axes ignore excluded or invalid points");
        readonly NSButton thermodynamicCheck = Check("Thermodynamic parameters");
        readonly NSButton offsetCheck = Check("Offset parameter");
        readonly NSButton derivedCheck = Check("Derived parameters");
        readonly NSButton temperatureCheck = Check("Temperature");
        readonly NSButton concentrationsCheck = Check("Concentrations");
        readonly NSButton injectionDelayCheck = Check("Injection delay");
        readonly NSButton instrumentInfoCheck = Check("Instrument");
        readonly NSButton attributesCheck = Check("Experiment attributes");

        public MacPreferencesWindowController()
            : base(CreateWindow())
        {
            energyUnitControl = Segmented(
                new[] { EnergyUnit.KiloJoule, EnergyUnit.KCal },
                unit => unit.GetProperties().LongName);
            concentrationUnitPopup = Popup(EnumValues<ConcentrationUnit>(), unit => unit.GetProperties().Name);
            instrumentPopup = Popup(ITCInstrumentAttribute.GetITCInstruments(), instrument => instrument.GetProperties().Name);
            numberPrecisionControl = Segmented(EnumValues<NumberPrecision>(), precision =>
                precision == NumberPrecision.SingleDecimal ? "1 Decimal" : FriendlyName(precision));
            uncertaintyPopup = Popup(EnumValues<UncertaintyDisplayStyle>(), FriendlyName);

            dilutionControl = Segmented(EnumValues<DilutionMethod>(), FriendlyName);
            peakFitPopup = Popup(EnumValues<PeakFitAlgorithm>(), FriendlyName);
            bufferSubtractionPopup = Popup(EnumValues<BufferSubtractionMethod>(), method => method.GetDisplayName());
            splineDensityControl = Segmented(EnumValues<SplineInterpolator.SplinePointDensity>(), FriendlyName);
            splineHandlePopup = Popup(EnumValues<SplineInterpolator.SplineHandleMode>(), FriendlyName);

            solverControl = Segmented(EnumValues<SolverAlgorithm>(), algorithm =>
                algorithm == SolverAlgorithm.NelderMead ? "Nelder–Mead" : "Levenberg–Marquardt");
            errorMethodPopup = Popup(EnumValues<ErrorEstimationMethod>(), method => method.Description());
            parameterLimitControl = Segmented(EnumValues<ParameterLimitSetting>(), FriendlyName);

            exportSelectionPopup = Popup(EnumValues<ExportDataSelection>(), FriendlyName);
            fitLineControl = Segmented(EnumValues<LineSmoothness>(), FriendlyName);
            attributeDisplayPopup = Popup(new[]
            {
                DisplayAttributeOptions.UsedInAnalysis,
                DisplayAttributeOptions.All,
                DisplayAttributeOptions.None
            }, FriendlyName);

            autoSaveIntervalSlider = Slider(
                0, AutoSaveIntervalChoices.Length - 1,
                AutoSaveIntervalFromPosition,
                AutoSaveIntervalPosition,
                value => $"{value:0} min", step: 1,
                tickMarkCount: AutoSaveIntervalChoices.Length,
                allowsTickMarkValuesOnly: true);
            bootstrapIterationsSlider = Slider(
                1, 3, value => Math.Pow(10, value), value => Math.Log10(Math.Max(10, value)),
                value => $"{Math.Round(value):N0}", step: 0.1);
            optimizerToleranceSlider = Slider(
                0, 1, value => value, value => value,
                ToleranceLabel, step: 0.05);
            maximumIterationsSlider = Slider(
                1, 4, value => Math.Pow(10, value), value => Math.Log10(Math.Max(10, value)),
                value => $"{Math.Round(value):N0}", step: 0.1);

            tabController = new PreferencesTabViewController
            {
                TabStyle = NSTabViewControllerTabStyle.Toolbar,
                CanPropagateSelectedChildViewControllerTitle = false
            };
            tabController.SelectionChanged = index =>
            {
                selectedPaneIndex = index;
                BeginInvokeOnMainThread(() => ResizeForPane(index, hasShown));
            };

            AddPane(0, "General", "gearshape", BuildGeneralPage());
            AddPane(1, "Processing", "beziercurve", BuildProcessingPage());
            AddPane(2, "Fitting", "chart.xyaxis.line", BuildFittingPage());
            AddPane(3, "Export", "square.and.arrow.up", BuildExportPage());

            Window.ContentViewController = tabController;
            Window.ToolbarStyle = NSWindowToolbarStyle.Preference;
            tabController.View.Frame = new CGRect(0, 0, WindowWidth, InitialContentHeight);
            tabController.View.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
            tabController.SelectedTabViewItemIndex = selectedPaneIndex;

            autoSaveEnabledCheck.Activated += (_, _) => UpdateDependentControls();
            showResidualCheck.Activated += (_, _) => UpdateDependentControls();

            LoadState(MacPreferencesState.FromSettings());
        }

        public void ShowPreferences()
        {
            LoadState(MacPreferencesState.FromSettings());
            SetStatus("");
            ShowWindow(this);
            tabController.SelectedTabViewItemIndex = selectedPaneIndex;
            Window.ContentView?.LayoutSubtreeIfNeeded();
            ResizeForPane(selectedPaneIndex, false);
            if (!hasShown) Window.Center();
            hasShown = true;
            BeginInvokeOnMainThread(() => ResizeForPane(selectedPaneIndex, false));
            Window.MakeKeyAndOrderFront(this);
            NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
            Window.MakeFirstResponder(null);
            BeginInvokeOnMainThread(() => Window.MakeFirstResponder(null));
        }

        void AddPane(int index, string title, string symbol, FlippedStackView content)
        {
            var pane = new PreferencesPaneController(
                content,
                RestoreDefaults,
                () => Window.PerformClose(this),
                Apply);
            panes[index] = pane;

            var item = NSTabViewItem.GetTabViewItem(pane);
            item.Label = title;
            item.Image = NSImage.GetSystemSymbol(symbol, title);
            tabController.AddTabViewItem(item);
        }

        void RestoreDefaults()
        {
            LoadState(MacPreferencesState.Defaults());
            SetStatus("Defaults staged. Choose Apply to save them.");
        }

        void ResizeForPane(int index, bool animate)
        {
            if (!panes.TryGetValue(index, out var pane) || Window == null) return;

            pane.View.LayoutSubtreeIfNeeded();
            var requestedFrameHeight = Math.Max(420,
                Math.Min(MaximumWindowHeight, pane.PreferredContentHeight + WindowChromeHeight));
            var screen = Window.Screen ?? NSScreen.MainScreen;
            if (screen != null)
            {
                requestedFrameHeight = Math.Min(requestedFrameHeight, screen.VisibleFrame.Height - 40);
            }

            var currentFrame = Window.Frame;
            var top = currentFrame.GetMaxY();
            var targetFrame = new CGRect(
                currentFrame.X,
                top - requestedFrameHeight,
                WindowWidth,
                requestedFrameHeight);
            if (screen != null) targetFrame = Window.ConstrainFrameRect(targetFrame, screen);
            Window.SetFrame(targetFrame, true, animate);
        }

        FlippedStackView BuildGeneralPage()
        {
            var page = Page();
            AddSection(page, "Units and Formatting",
                Row("Energy unit", energyUnitControl),
                Row("Concentration unit", concentrationUnitPopup),
                Row("Number precision", numberPrecisionControl),
                Row("Uncertainty display", uncertaintyPopup),
                Row("Designer instrument", instrumentPopup));
            AddSection(page, "Analysis Context",
                Row("Reference temperature (°C)", referenceTemperatureField),
                Row("Minimum temperature span (°C)", minimumTemperatureSpanField),
                Row("Minimum ionic-strength span (mM)", minimumIonSpanField),
                Full(includeBufferCheck));
            AddSection(page, "Behavior and File Loading",
                Full(onlineChecksCheck),
                Full(confirmDeleteCheck),
                Full(discardOrphanCheck));

            var openFolder = Button("Open Autosave Folder");
            openFolder.Activated += (_, _) =>
            {
                try
                {
                    Directory.CreateDirectory(AutoSaveManager.Shared.AutoSaveDirectory);
                    AppDelegate.OpenAutoSaveFolder();
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message, error: true);
                }
            };
            AddSection(page, "Autosave and Recovery",
                Full(autoSaveEnabledCheck),
                Row("Interval", autoSaveIntervalSlider, 30),
                Row("Maximum files", autoSaveLimitField),
                Full(recoveryPromptCheck),
                Full(openFolder));
            return page;
        }

        FlippedStackView BuildProcessingPage()
        {
            var page = Page();
            AddSection(page, "Processing Defaults",
                Row("Dilution method", dilutionControl),
                Row("Peak-fit algorithm", peakFitPopup),
                Row("Buffer subtraction", bufferSubtractionPopup),
                Full(discardIntegrationCheck),
                Full(reprocessIntegratedCheck));
            AddSection(page, "Spline Defaults",
                Row("Point density", splineDensityControl),
                Row("Handle mode", splineHandlePopup),
                Full(splineTimeDraggingCheck),
                Full(copyIntegrationStartCheck));
            return page;
        }

        FlippedStackView BuildFittingPage()
        {
            var page = Page();
            AddSection(page, "Solver",
                Row("Default solver", solverControl),
                Row("Error estimation", errorMethodPopup),
                Row("Bootstrap iterations", bootstrapIterationsSlider),
                Row("Optimizer tolerance", optimizerToleranceSlider),
                Row("Maximum iterations", maximumIterationsSlider),
                Row("Parameter limits", parameterLimitControl),
                Full(weightedFittingCheck));
            AddSection(page, "Concentration Error",
                Full(concentrationBootstrapCheck),
                Row("Automatic variance (%)", concentrationVarianceField));
            AddSection(page, "Result Creation",
                Full(createSingleResultCheck),
                Full(createGlobalResultCheck),
                Full(autoOpenResultCheck));
            return page;
        }

        FlippedStackView BuildExportPage()
        {
            var page = Page();
            AddSection(page, "Data Export",
                Row("Selection", exportSelectionPopup),
                Row("Decimal places", exportDecimalsField),
                Full(unifyTimeAxisCheck),
                Full(exportCorrectedCheck),
                Full(exportFitPointsCheck));
            AddSection(page, "Export Columns",
                TwoChecks(exportMolarRatioCheck, exportInjectionInfoCheck),
                TwoChecks(exportConcentrationsCheck, exportIncludedCheck),
                TwoChecks(exportPeakCheck, exportFitCheck));
            AddSection(page, "Final Figure Defaults",
                Row("Width (cm)", figureWidthField),
                Row("Height (cm)", figureHeightField),
                Row("Fit line", fitLineControl),
                Full(showResidualCheck),
                Full(residualGapCheck),
                Full(unifyResidualAxisCheck),
                Full(parameterBoxCheck),
                Full(experimentDetailsCheck),
                Full(modelInfoCheck),
                Full(autoAxesCheck));
            AddSection(page, "Final Figure Content",
                TwoChecks(thermodynamicCheck, offsetCheck),
                TwoChecks(derivedCheck, temperatureCheck),
                TwoChecks(concentrationsCheck, injectionDelayCheck),
                TwoChecks(instrumentInfoCheck, attributesCheck),
                Row("Attributes", attributeDisplayPopup));
            return page;
        }

        void LoadState(MacPreferencesState state)
        {
            Select(energyUnitControl, state.EnergyUnit);
            Select(concentrationUnitPopup, state.DefaultConcentrationUnit);
            Select(instrumentPopup, state.DefaultDesignerInstrument);
            Select(numberPrecisionControl, state.NumberPrecision);
            Select(uncertaintyPopup, state.UncertaintyDisplayStyle);
            stagedColorScheme = state.ColorScheme;
            stagedColorGradientMode = state.ColorSchemeGradientMode;
            referenceTemperatureField.StringValue = Format(state.ReferenceTemperature);
            minimumTemperatureSpanField.StringValue = Format(state.MinimumTemperatureSpanForFitting);
            minimumIonSpanField.StringValue = Format(state.MinimumIonSpanForFitting * 1000);
            Set(includeBufferCheck, state.IncludeBufferInIonicStrengthCalc);
            Set(onlineChecksCheck, state.PerformOnlineChecksOnLaunch);
            Set(confirmDeleteCheck, state.ConfirmRemoveDelete);
            Set(discardOrphanCheck, state.AutomaticallyDiscardOrphanInjectionsOnLoad);
            Set(autoSaveEnabledCheck, state.AutoSaveEnabled);
            autoSaveIntervalSlider.Value = state.AutoSaveIntervalMinutes;
            autoSaveLimitField.IntValue = state.AutoSaveFileLimit;
            Set(recoveryPromptCheck, state.PromptForAutoSaveRecovery);

            Select(dilutionControl, state.DilutionCalculationMethod);
            Select(peakFitPopup, state.PeakFitAlgorithm);
            Select(bufferSubtractionPopup, state.BufferSubtractionDefaultMethod);
            Select(splineDensityControl, state.DefaultSplinePointDensity);
            Select(splineHandlePopup, state.DefaultSplineHandleMode);
            Set(discardIntegrationCheck, state.DiscardIntegrationRegionForBaseline);
            Set(reprocessIntegratedCheck, state.ReprocessIntegratedHeatDataOnLoad);
            Set(splineTimeDraggingCheck, state.DefaultSplinePointTimeDragging);
            Set(copyIntegrationStartCheck, state.IntegrationRegionCopyIncludesStart);

            Select(solverControl, state.DefaultSolverAlgorithm);
            Select(errorMethodPopup, state.DefaultErrorEstimationMethod);
            Select(parameterLimitControl, state.ParameterLimitSetting);
            bootstrapIterationsSlider.Value = state.DefaultBootstrapIterations;
            optimizerToleranceSlider.Value = state.OptimizerTolerance;
            maximumIterationsSlider.Value = state.MaximumOptimizerIterations;
            concentrationVarianceField.StringValue = Format(state.ConcentrationAutoVariance * 100);
            Set(concentrationBootstrapCheck, state.IncludeConcentrationErrorsInBootstrap);
            Set(weightedFittingCheck, state.UseInjectionErrorWeightedFitting);
            Set(createSingleResultCheck, state.CreateSingleAnalysisResult);
            Set(createGlobalResultCheck, state.CreateGlobalAnalysisResult);
            Set(autoOpenResultCheck, state.AutoOpenNewAnalysisResult);

            Select(exportSelectionPopup, state.ExportSelectionMode);
            Select(fitLineControl, state.FitLineSmoothness);
            Select(attributeDisplayPopup, NormalizeAttributeOptions(state.DisplayAttributeOptions));
            exportDecimalsField.IntValue = state.NumOfDecimalsToExport;
            figureWidthField.StringValue = Format(state.FinalFigureWidthCentimeters);
            figureHeightField.StringValue = Format(state.FinalFigureHeightCentimeters);
            Set(unifyTimeAxisCheck, state.UnifyTimeAxisForExport);
            Set(exportCorrectedCheck, state.ExportBaselineCorrectedData);
            Set(exportFitPointsCheck, state.ExportFitPointsWithPeaks);
            Set(exportMolarRatioCheck, state.ExportColumns.HasFlag(ExportColumns.MolarRatio));
            Set(exportInjectionInfoCheck, state.ExportColumns.HasFlag(ExportColumns.InjectionInfo));
            Set(exportConcentrationsCheck, state.ExportColumns.HasFlag(ExportColumns.Concentrations));
            Set(exportIncludedCheck, state.ExportColumns.HasFlag(ExportColumns.Included));
            Set(exportPeakCheck, state.ExportColumns.HasFlag(ExportColumns.Peak));
            Set(exportFitCheck, state.ExportColumns.HasFlag(ExportColumns.Fit));
            Set(showResidualCheck, state.ShowResidualGraph);
            Set(residualGapCheck, state.ShowResidualGraphGap);
            Set(unifyResidualAxisCheck, state.UnifyResidualGraphAxis);
            Set(parameterBoxCheck, state.FinalFigureShowParameterBoxAsDefault);
            Set(experimentDetailsCheck, state.FinalFigureShowDetailsAsDefault);
            Set(modelInfoCheck, state.FinalFigureShowModelInfoAsDefault);
            Set(autoAxesCheck, state.AutoAxesIgnoresBadData);
            Set(thermodynamicCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Thermodynamic));
            Set(offsetCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Offset));
            Set(derivedCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Derived));
            Set(temperatureCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Temperature));
            Set(concentrationsCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Concentrations));
            Set(injectionDelayCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.InjectionDelay));
            Set(instrumentInfoCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Instrument));
            Set(attributesCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Attributes));
            UpdateDependentControls();
        }

        void Apply()
        {
            if (!TryBuildState(out var state)) return;
            try
            {
                state.Apply();
                SetStatus("");
                Window.PerformClose(this);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, error: true);
            }
        }

        bool TryBuildState(out MacPreferencesState state)
        {
            state = new MacPreferencesState();
            if (!ReadDouble(0, referenceTemperatureField, "reference temperature", -273.15, 500, out var referenceTemperature)) return false;
            if (!ReadDouble(0, minimumTemperatureSpanField, "minimum temperature span", 0, 100, out var minimumTemperatureSpan)) return false;
            if (!ReadDouble(0, minimumIonSpanField, "minimum ionic-strength span", 0, 10000, out var minimumIonSpan)) return false;
            if (!ReadInt(0, autoSaveLimitField, "autosave file limit", 1, 100, out var autoSaveLimit)) return false;
            if (!ReadDouble(2, concentrationVarianceField, "concentration variance", 0, 100, out var concentrationVariance)) return false;
            if (!ReadInt(3, exportDecimalsField, "export decimals", 0, 12, out var exportDecimals)) return false;
            if (!ReadDouble(3, figureWidthField, "figure width", 1, 50, out var figureWidth)) return false;
            if (!ReadDouble(3, figureHeightField, "figure height", 1, 50, out var figureHeight)) return false;

            state.ReferenceTemperature = referenceTemperature;
            state.EnergyUnit = Value<EnergyUnit>(energyUnitControl);
            state.DefaultConcentrationUnit = Value<ConcentrationUnit>(concentrationUnitPopup);
            state.DefaultDesignerInstrument = Value<ITCInstrument>(instrumentPopup);
            state.MinimumTemperatureSpanForFitting = minimumTemperatureSpan;
            state.MinimumIonSpanForFitting = minimumIonSpan / 1000;
            state.NumberPrecision = Value<NumberPrecision>(numberPrecisionControl);
            state.UncertaintyDisplayStyle = Value<UncertaintyDisplayStyle>(uncertaintyPopup);
            state.ColorScheme = stagedColorScheme;
            state.ColorSchemeGradientMode = stagedColorGradientMode;
            state.IncludeBufferInIonicStrengthCalc = IsOn(includeBufferCheck);
            state.PerformOnlineChecksOnLaunch = IsOn(onlineChecksCheck);
            state.ConfirmRemoveDelete = IsOn(confirmDeleteCheck);
            state.AutomaticallyDiscardOrphanInjectionsOnLoad = IsOn(discardOrphanCheck);
            state.AutoSaveEnabled = IsOn(autoSaveEnabledCheck);
            state.AutoSaveIntervalMinutes = (int)Math.Round(autoSaveIntervalSlider.Value);
            state.AutoSaveFileLimit = autoSaveLimit;
            state.PromptForAutoSaveRecovery = IsOn(recoveryPromptCheck);

            state.DilutionCalculationMethod = Value<DilutionMethod>(dilutionControl);
            state.PeakFitAlgorithm = Value<PeakFitAlgorithm>(peakFitPopup);
            state.BufferSubtractionDefaultMethod = Value<BufferSubtractionMethod>(bufferSubtractionPopup);
            state.DefaultSplinePointDensity = Value<SplineInterpolator.SplinePointDensity>(splineDensityControl);
            state.DefaultSplineHandleMode = Value<SplineInterpolator.SplineHandleMode>(splineHandlePopup);
            state.DiscardIntegrationRegionForBaseline = IsOn(discardIntegrationCheck);
            state.ReprocessIntegratedHeatDataOnLoad = IsOn(reprocessIntegratedCheck);
            state.DefaultSplinePointTimeDragging = IsOn(splineTimeDraggingCheck);
            state.IntegrationRegionCopyIncludesStart = IsOn(copyIntegrationStartCheck);

            state.DefaultSolverAlgorithm = Value<SolverAlgorithm>(solverControl);
            state.DefaultErrorEstimationMethod = Value<ErrorEstimationMethod>(errorMethodPopup);
            state.ParameterLimitSetting = Value<ParameterLimitSetting>(parameterLimitControl);
            state.DefaultBootstrapIterations = (int)Math.Round(bootstrapIterationsSlider.Value);
            state.OptimizerTolerance = optimizerToleranceSlider.Value;
            state.MaximumOptimizerIterations = (int)Math.Round(maximumIterationsSlider.Value);
            state.ConcentrationAutoVariance = concentrationVariance / 100;
            state.IncludeConcentrationErrorsInBootstrap = IsOn(concentrationBootstrapCheck);
            state.UseInjectionErrorWeightedFitting = IsOn(weightedFittingCheck);
            state.CreateSingleAnalysisResult = IsOn(createSingleResultCheck);
            state.CreateGlobalAnalysisResult = IsOn(createGlobalResultCheck);
            state.AutoOpenNewAnalysisResult = IsOn(autoOpenResultCheck);

            state.ExportSelectionMode = Value<ExportDataSelection>(exportSelectionPopup);
            state.FitLineSmoothness = Value<LineSmoothness>(fitLineControl);
            state.DisplayAttributeOptions = Value<DisplayAttributeOptions>(attributeDisplayPopup);
            state.NumOfDecimalsToExport = exportDecimals;
            state.FinalFigureWidthCentimeters = figureWidth;
            state.FinalFigureHeightCentimeters = figureHeight;
            state.UnifyTimeAxisForExport = IsOn(unifyTimeAxisCheck);
            state.ExportBaselineCorrectedData = IsOn(exportCorrectedCheck);
            state.ExportFitPointsWithPeaks = IsOn(exportFitPointsCheck);
            state.ExportColumns = BuildExportColumns();
            state.ShowResidualGraph = IsOn(showResidualCheck);
            state.ShowResidualGraphGap = IsOn(residualGapCheck);
            state.UnifyResidualGraphAxis = IsOn(unifyResidualAxisCheck);
            state.FinalFigureShowParameterBoxAsDefault = IsOn(parameterBoxCheck);
            state.FinalFigureShowDetailsAsDefault = IsOn(experimentDetailsCheck);
            state.FinalFigureShowModelInfoAsDefault = IsOn(modelInfoCheck);
            state.AutoAxesIgnoresBadData = IsOn(autoAxesCheck);
            state.FinalFigureParameterDisplay = BuildFigureDisplay();
            return true;
        }

        ExportColumns BuildExportColumns()
        {
            var columns = ExportColumns.None;
            if (IsOn(exportMolarRatioCheck)) columns |= ExportColumns.MolarRatio;
            if (IsOn(exportInjectionInfoCheck)) columns |= ExportColumns.InjectionInfo;
            if (IsOn(exportConcentrationsCheck)) columns |= ExportColumns.Concentrations;
            if (IsOn(exportIncludedCheck)) columns |= ExportColumns.Included;
            if (IsOn(exportPeakCheck)) columns |= ExportColumns.Peak;
            if (IsOn(exportFitCheck)) columns |= ExportColumns.Fit;
            return columns;
        }

        FinalFigureDisplayParameters BuildFigureDisplay()
        {
            var display = FinalFigureDisplayParameters.None;
            if (IsOn(modelInfoCheck)) display |= FinalFigureDisplayParameters.Model;
            if (IsOn(thermodynamicCheck)) display |= FinalFigureDisplayParameters.Thermodynamic;
            if (IsOn(offsetCheck)) display |= FinalFigureDisplayParameters.Offset;
            if (IsOn(derivedCheck)) display |= FinalFigureDisplayParameters.Derived;
            if (IsOn(temperatureCheck)) display |= FinalFigureDisplayParameters.Temperature;
            if (IsOn(concentrationsCheck)) display |= FinalFigureDisplayParameters.Concentrations;
            if (IsOn(injectionDelayCheck)) display |= FinalFigureDisplayParameters.InjectionDelay;
            if (IsOn(instrumentInfoCheck)) display |= FinalFigureDisplayParameters.Instrument;
            if (IsOn(attributesCheck)) display |= FinalFigureDisplayParameters.Attributes;
            return display;
        }

        void UpdateDependentControls()
        {
            var autoSaveEnabled = IsOn(autoSaveEnabledCheck);
            autoSaveIntervalSlider.Enabled = autoSaveEnabled;

            var residualsEnabled = IsOn(showResidualCheck);
            residualGapCheck.Enabled = residualsEnabled;
            unifyResidualAxisCheck.Enabled = residualsEnabled;
        }

        bool ReadDouble(int paneIndex, NSTextField field, string label, double minimum, double maximum, out double value)
        {
            var text = field.StringValue;
            if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                || value < minimum || value > maximum)
            {
                SetStatus($"{label} must be between {minimum:G5} and {maximum:G5}.", error: true, paneIndex: paneIndex);
                return false;
            }
            return true;
        }

        bool ReadInt(int paneIndex, NSTextField field, string label, int minimum, int maximum, out int value)
        {
            if (!int.TryParse(field.StringValue, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
                || value < minimum || value > maximum)
            {
                SetStatus($"{label} must be an integer between {minimum} and {maximum}.", error: true, paneIndex: paneIndex);
                return false;
            }
            return true;
        }

        void SetStatus(string message, bool error = false, int? paneIndex = null)
        {
            if (string.IsNullOrEmpty(message))
            {
                foreach (var pane in panes.Values) pane.SetStatus("", false);
                return;
            }

            var targetIndex = paneIndex ?? selectedPaneIndex;
            if (paneIndex.HasValue && targetIndex != selectedPaneIndex)
            {
                selectedPaneIndex = targetIndex;
                tabController.SelectedTabViewItemIndex = targetIndex;
            }
            if (panes.TryGetValue(targetIndex, out var target)) target.SetStatus(message, error);
        }

        static NSWindow CreateWindow()
        {
            var window = new NSWindow(
                new CGRect(0, 0, WindowWidth, InitialContentHeight),
                NSWindowStyle.Titled | NSWindowStyle.Closable,
                NSBackingStore.Buffered,
                false)
            {
                Title = "FT-ITC Analysis Preferences",
                ReleasedWhenClosed = false,
                AnimationBehavior = NSWindowAnimationBehavior.DocumentWindow,
                ToolbarStyle = NSWindowToolbarStyle.Preference,
                MinSize = new CGSize(WindowWidth, 360),
                MaxSize = new CGSize(WindowWidth, MaximumWindowHeight)
            };
            return window;
        }

        static FlippedStackView Page() => new FlippedStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Distribution = NSStackViewDistribution.Fill,
            Alignment = NSLayoutAttribute.Width,
            Spacing = 14,
            TranslatesAutoresizingMaskIntoConstraints = false
        };

        static void AddSection(NSStackView page, string title, params FormRow[] rows)
        {
            var section = VerticalStack(8);
            section.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);

            if (page.ArrangedSubviews.Length > 0)
            {
                var separator = new NSBox
                {
                    BoxType = NSBoxType.NSBoxSeparator,
                    TranslatesAutoresizingMaskIntoConstraints = false
                };
                separator.HeightAnchor.ConstraintEqualToConstant(1).Active = true;
                AddFullWidth(section, separator);
            }

            AddFullWidth(section, Label(title, 13, true));

            var gridRows = rows.Select(row => row.FullWidth
                ? new[] { row.Control, new NSView() }
                : new[] { FormLabel(row.Title), row.Control }).ToArray();
            var grid = NSGridView.Create(gridRows);
            grid.ColumnSpacing = 16;
            grid.RowSpacing = 5;
            grid.RowAlignment = NSGridRowAlignment.FirstBaseline;
            grid.X = NSGridCellPlacement.Fill;
            grid.Y = NSGridCellPlacement.Center;
            grid.TranslatesAutoresizingMaskIntoConstraints = false;
            grid.GetColumn(0).Width = 210;
            grid.GetColumn(0).X = NSGridCellPlacement.Leading;
            grid.GetColumn(1).X = NSGridCellPlacement.Trailing;

            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                var row = rows[rowIndex];
                grid.GetRow(rowIndex).Height = row.Height;
                if (!row.FullWidth) continue;
                grid.MergeCells(new NSRange(0, 2), new NSRange(rowIndex, 1));
                grid.GetCell(0, rowIndex).X = NSGridCellPlacement.Leading;
            }

            AddFullWidth(section, grid);
            AddFullWidth(page, section);
        }

        static FormRow Row(string title, NSView control, float height = 26)
        {
            control.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
            return new FormRow(title, control, false, height);
        }

        static FormRow Full(NSView control) => new FormRow(null, control, true, control is NSButton ? 24 : 30);

        static FormRow TwoChecks(NSButton left, NSButton right)
        {
            left.WidthAnchor.ConstraintEqualToConstant(290).Active = true;
            var row = HorizontalStack(8);
            row.AddArrangedSubview(left);
            row.AddArrangedSubview(right);
            return new FormRow(null, row, true, 24);
        }

        static NSTextField FormLabel(string text)
        {
            var label = Label(text, 13, false);
            label.TextColor = NSColor.SecondaryLabel;
            return label;
        }

        static NSStackView VerticalStack(nfloat spacing) => new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Distribution = NSStackViewDistribution.Fill,
            Alignment = NSLayoutAttribute.Width,
            Spacing = spacing,
            TranslatesAutoresizingMaskIntoConstraints = false
        };

        static NSStackView HorizontalStack(nfloat spacing) => new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Distribution = NSStackViewDistribution.Fill,
            Alignment = NSLayoutAttribute.CenterY,
            Spacing = spacing,
            TranslatesAutoresizingMaskIntoConstraints = false
        };

        static NSTextField Label(string text, nfloat size, bool bold) => new NSTextField
        {
            StringValue = text ?? "",
            Editable = false,
            Selectable = false,
            RefusesFirstResponder = true,
            Bezeled = false,
            DrawsBackground = false,
            Font = bold ? NSFont.BoldSystemFontOfSize(size) : NSFont.SystemFontOfSize(size),
            TranslatesAutoresizingMaskIntoConstraints = false
        };

        static NSTextField Field()
        {
            var field = new NSTextField
            {
                ControlSize = NSControlSize.Regular,
                Font = NSFont.SystemFontOfSize(13),
                Alignment = NSTextAlignment.Right,
                TranslatesAutoresizingMaskIntoConstraints = false
            };
            field.WidthAnchor.ConstraintEqualToConstant(180).Active = true;
            return field;
        }

        static NSButton Check(string title)
        {
            var check = new NSButton
            {
                Title = title,
                ControlSize = NSControlSize.Large,
                Font = NSFont.SystemFontOfSize(13),
                RefusesFirstResponder = true,
                TranslatesAutoresizingMaskIntoConstraints = false
            };
            check.SetButtonType(NSButtonType.Switch);
            return check;
        }

        static NSButton Button(string title, string keyEquivalent = null) => new NSButton
        {
            Title = title,
            BezelStyle = NSBezelStyle.Rounded,
            KeyEquivalent = keyEquivalent ?? "",
            RefusesFirstResponder = true,
            TranslatesAutoresizingMaskIntoConstraints = false
        };

        static NSPopUpButton Popup<T>(IEnumerable<T> values, Func<T, string> title)
        {
            var popup = new NSPopUpButton
            {
                ControlSize = NSControlSize.Regular,
                RefusesFirstResponder = true,
                TranslatesAutoresizingMaskIntoConstraints = false
            };
            popup.RemoveAllItems();
            foreach (var value in values)
            {
                popup.Menu.AddItem(new NSMenuItem(title(value)) { Tag = Convert.ToInt32(value) });
            }
            popup.WidthAnchor.ConstraintEqualToConstant(300).Active = true;
            return popup;
        }

        static TaggedSegmentedControl Segmented<T>(IEnumerable<T> values, Func<T, string> title)
        {
            var options = values.ToArray();
            return new TaggedSegmentedControl(
                options.Select(value => Convert.ToInt32(value)).ToArray(),
                options.Select(title).ToArray());
        }

        static SliderValueControl Slider(
            double minimum,
            double maximum,
            Func<double, double> valueFromPosition,
            Func<double, double> positionFromValue,
            Func<double, string> valueText,
            double step = 0,
            int tickMarkCount = 0,
            bool allowsTickMarkValuesOnly = false)
        {
            return new SliderValueControl(
                minimum,
                maximum,
                valueFromPosition,
                positionFromValue,
                valueText,
                step,
                tickMarkCount,
                allowsTickMarkValuesOnly);
        }

        static IEnumerable<T> EnumValues<T>() => Enum.GetValues(typeof(T)).Cast<T>();
        static string FriendlyName<T>(T value) => FriendlyName((Enum)(object)value);
        static string FriendlyName(Enum value)
        {
            var text = value.GetEnumDescription();
            return string.Concat(text.Select((character, index) =>
                index > 0 && char.IsUpper(character) && !char.IsUpper(text[index - 1])
                    ? " " + character
                    : character.ToString()));
        }

        static T Value<T>(NSPopUpButton popup) where T : struct => (T)Enum.ToObject(typeof(T), (int)popup.SelectedTag);
        static void Select<T>(NSPopUpButton popup, T value) => popup.SelectItemWithTag(Convert.ToInt32(value));
        static T Value<T>(TaggedSegmentedControl control) where T : struct =>
            (T)Enum.ToObject(typeof(T), control.SelectedValue);
        static void Select<T>(TaggedSegmentedControl control, T value) =>
            control.SelectValue(Convert.ToInt32(value));
        static void Set(NSButton button, bool value) => button.State = value ? NSCellStateValue.On : NSCellStateValue.Off;
        static bool IsOn(NSButton button) => button.State == NSCellStateValue.On;
        static string Format(double value) => value.ToString("G6", CultureInfo.CurrentCulture);

        static string ToleranceLabel(double value)
        {
            string description;
            if (value >= 0.90) description = "Very strict";
            else if (value >= 0.70) description = "Strict";
            else if (value >= 0.35) description = "Balanced";
            else if (value >= 0.15) description = "Relaxed";
            else description = "Fast";
            return description;
        }

        static double AutoSaveIntervalFromPosition(double position)
        {
            var index = Math.Max(0, Math.Min(AutoSaveIntervalChoices.Length - 1, (int)Math.Round(position)));
            return AutoSaveIntervalChoices[index];
        }

        static double AutoSaveIntervalPosition(double interval)
        {
            var nearestIndex = 0;
            var nearestDifference = double.MaxValue;
            for (var index = 0; index < AutoSaveIntervalChoices.Length; index++)
            {
                var difference = Math.Abs(AutoSaveIntervalChoices[index] - interval);
                if (difference >= nearestDifference) continue;
                nearestDifference = difference;
                nearestIndex = index;
            }
            return nearestIndex;
        }

        static DisplayAttributeOptions NormalizeAttributeOptions(DisplayAttributeOptions options)
        {
            if (options == DisplayAttributeOptions.All || options == DisplayAttributeOptions.None) return options;
            return DisplayAttributeOptions.UsedInAnalysis;
        }

        static void AddFullWidth(NSStackView stack, NSView view)
        {
            stack.AddArrangedSubview(view);
            view.WidthAnchor.ConstraintEqualToAnchor(stack.WidthAnchor).Active = true;
        }

        static void Pin(NSView view, NSView parent)
        {
            view.TopAnchor.ConstraintEqualToAnchor(parent.TopAnchor).Active = true;
            view.BottomAnchor.ConstraintEqualToAnchor(parent.BottomAnchor).Active = true;
            view.LeadingAnchor.ConstraintEqualToAnchor(parent.LeadingAnchor).Active = true;
            view.TrailingAnchor.ConstraintEqualToAnchor(parent.TrailingAnchor).Active = true;
        }

        sealed class TaggedSegmentedControl : NSSegmentedControl
        {
            readonly int[] values;

            public TaggedSegmentedControl(int[] values, string[] labels)
            {
                this.values = values;
                SegmentCount = values.Length;
                SegmentStyle = NSSegmentStyle.Rounded;
                ControlSize = NSControlSize.Regular;
                RefusesFirstResponder = true;
                TranslatesAutoresizingMaskIntoConstraints = false;
                WidthAnchor.ConstraintEqualToConstant(300).Active = true;

                var segmentWidth = 300.0 / Math.Max(1, labels.Length);
                for (var index = 0; index < labels.Length; index++)
                {
                    SetLabel(labels[index], index);
                    SetWidth((nfloat)segmentWidth, index);
                }
                SelectedSegment = values.Length > 0 ? 0 : -1;
            }

            public int SelectedValue
            {
                get
                {
                    var index = (int)SelectedSegment;
                    return index >= 0 && index < values.Length ? values[index] : 0;
                }
            }

            public void SelectValue(int value)
            {
                var index = Array.IndexOf(values, value);
                SelectedSegment = index >= 0 ? index : 0;
            }
        }

        sealed class SliderValueControl : NSStackView
        {
            readonly NSSlider slider;
            readonly NSTextField valueLabel;
            readonly Func<double, double> valueFromPosition;
            readonly Func<double, double> positionFromValue;
            readonly Func<double, string> valueText;
            readonly double step;

            public SliderValueControl(
                double minimum,
                double maximum,
                Func<double, double> valueFromPosition,
                Func<double, double> positionFromValue,
                Func<double, string> valueText,
                double step,
                int tickMarkCount,
                bool allowsTickMarkValuesOnly)
            {
                this.valueFromPosition = valueFromPosition;
                this.positionFromValue = positionFromValue;
                this.valueText = valueText;
                this.step = step;

                Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
                Distribution = NSStackViewDistribution.Fill;
                Alignment = NSLayoutAttribute.CenterY;
                Spacing = 8;
                TranslatesAutoresizingMaskIntoConstraints = false;
                WidthAnchor.ConstraintEqualToConstant(300).Active = true;

                slider = new NSSlider
                {
                    MinValue = minimum,
                    MaxValue = maximum,
                    Continuous = true,
                    TickMarksCount = tickMarkCount,
                    TickMarkPosition = NSTickMarkPosition.Below,
                    AllowsTickMarkValuesOnly = allowsTickMarkValuesOnly,
                    ControlSize = NSControlSize.Regular,
                    RefusesFirstResponder = true,
                    TranslatesAutoresizingMaskIntoConstraints = false
                };
                slider.WidthAnchor.ConstraintEqualToConstant(185).Active = true;
                slider.Activated += (_, _) =>
                {
                    if (this.step > 0)
                    {
                        slider.DoubleValue = Math.Max(
                            slider.MinValue,
                            Math.Min(slider.MaxValue, Math.Round(slider.DoubleValue / this.step) * this.step));
                    }
                    UpdateLabel();
                };

                valueLabel = Label("", 12, false);
                valueLabel.Alignment = NSTextAlignment.Right;
                valueLabel.TextColor = NSColor.SecondaryLabel;
                valueLabel.WidthAnchor.ConstraintEqualToConstant(107).Active = true;

                AddArrangedSubview(slider);
                AddArrangedSubview(valueLabel);
                UpdateLabel();
            }

            public double Value
            {
                get => valueFromPosition(slider.DoubleValue);
                set
                {
                    slider.DoubleValue = Math.Max(
                        slider.MinValue,
                        Math.Min(slider.MaxValue, positionFromValue(value)));
                    UpdateLabel();
                }
            }

            public bool Enabled
            {
                get => slider.Enabled;
                set
                {
                    slider.Enabled = value;
                    valueLabel.TextColor = value ? NSColor.SecondaryLabel : NSColor.DisabledControlText;
                }
            }

            void UpdateLabel() => valueLabel.StringValue = valueText(Value);
        }

        sealed class FormRow
        {
            public FormRow(string title, NSView control, bool fullWidth, nfloat height)
            {
                Title = title;
                Control = control;
                FullWidth = fullWidth;
                Height = height;
            }

            public string Title { get; }
            public NSView Control { get; }
            public bool FullWidth { get; }
            public nfloat Height { get; }
        }

        sealed class PreferencesPaneController : NSViewController
        {
            const double FooterHeight = 55;
            const double VerticalContentMargin = 32;

            readonly FlippedStackView content;
            readonly NSTextField statusLabel;

            public PreferencesPaneController(
                FlippedStackView content,
                Action restoreDefaults,
                Action cancel,
                Action apply)
            {
                this.content = content;

                var root = new NSView(new CGRect(0, 0, WindowWidth, InitialContentHeight))
                {
                    AutoresizingMask = NSViewResizingMask.HeightSizable,
                    TranslatesAutoresizingMaskIntoConstraints = false
                };
                root.WidthAnchor.ConstraintEqualToConstant((nfloat)WindowWidth).Active = true;
                View = root;

                var scroll = new NSScrollView
                {
                    HasVerticalScroller = true,
                    HasHorizontalScroller = false,
                    AutohidesScrollers = true,
                    BorderType = NSBorderType.NoBorder,
                    DrawsBackground = false,
                    TranslatesAutoresizingMaskIntoConstraints = false
                };
                var document = new FlippedDocumentView
                {
                    TranslatesAutoresizingMaskIntoConstraints = false
                };
                document.AddSubview(content);
                content.TopAnchor.ConstraintEqualToAnchor(document.TopAnchor, 16).Active = true;
                content.BottomAnchor.ConstraintEqualToAnchor(document.BottomAnchor, -16).Active = true;
                content.LeadingAnchor.ConstraintEqualToAnchor(document.LeadingAnchor, 20).Active = true;
                content.TrailingAnchor.ConstraintEqualToAnchor(document.TrailingAnchor, -20).Active = true;
                scroll.DocumentView = document;
                document.WidthAnchor.ConstraintEqualToAnchor(scroll.ContentView.WidthAnchor).Active = true;

                var footer = new NSView
                {
                    TranslatesAutoresizingMaskIntoConstraints = false
                };
                var separator = new NSBox
                {
                    BoxType = NSBoxType.NSBoxSeparator,
                    TranslatesAutoresizingMaskIntoConstraints = false
                };
                var restore = Button("Restore Defaults");
                restore.Activated += (_, _) => restoreDefaults();
                var cancelButton = Button("Cancel");
                cancelButton.Activated += (_, _) => cancel();
                var applyButton = Button("Apply", "\r");
                applyButton.Activated += (_, _) => apply();

                statusLabel = Label("", 12, false);
                statusLabel.TextColor = NSColor.SecondaryLabel;
                statusLabel.LineBreakMode = NSLineBreakMode.TruncatingTail;
                statusLabel.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);

                var buttons = HorizontalStack(8);
                buttons.AddArrangedSubview(restore);
                buttons.AddArrangedSubview(statusLabel);
                buttons.AddArrangedSubview(cancelButton);
                buttons.AddArrangedSubview(applyButton);

                footer.AddSubview(separator);
                footer.AddSubview(buttons);
                separator.TopAnchor.ConstraintEqualToAnchor(footer.TopAnchor).Active = true;
                separator.LeadingAnchor.ConstraintEqualToAnchor(footer.LeadingAnchor).Active = true;
                separator.TrailingAnchor.ConstraintEqualToAnchor(footer.TrailingAnchor).Active = true;
                separator.HeightAnchor.ConstraintEqualToConstant(1).Active = true;
                buttons.LeadingAnchor.ConstraintEqualToAnchor(footer.LeadingAnchor, 20).Active = true;
                buttons.TrailingAnchor.ConstraintEqualToAnchor(footer.TrailingAnchor, -20).Active = true;
                buttons.CenterYAnchor.ConstraintEqualToAnchor(footer.CenterYAnchor, 1).Active = true;

                root.AddSubview(scroll);
                root.AddSubview(footer);
                footer.HeightAnchor.ConstraintEqualToConstant((nfloat)FooterHeight).Active = true;
                footer.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor).Active = true;
                footer.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor).Active = true;
                footer.BottomAnchor.ConstraintEqualToAnchor(root.BottomAnchor).Active = true;
                scroll.TopAnchor.ConstraintEqualToAnchor(root.TopAnchor).Active = true;
                scroll.LeadingAnchor.ConstraintEqualToAnchor(root.LeadingAnchor).Active = true;
                scroll.TrailingAnchor.ConstraintEqualToAnchor(root.TrailingAnchor).Active = true;
                scroll.BottomAnchor.ConstraintEqualToAnchor(footer.TopAnchor).Active = true;
            }

            public nfloat PreferredContentHeight
            {
                get
                {
                    content.LayoutSubtreeIfNeeded();
                    return (nfloat)Math.Ceiling(content.FittingSize.Height + VerticalContentMargin + FooterHeight);
                }
            }

            public void SetStatus(string message, bool error)
            {
                statusLabel.StringValue = message ?? "";
                statusLabel.TextColor = error ? NSColor.SystemRed : NSColor.SecondaryLabel;
            }
        }

        sealed class PreferencesTabViewController : NSTabViewController
        {
            public Action<int> SelectionChanged { get; set; }

            public override void DidSelect(NSTabView tabView, NSTabViewItem item)
            {
                base.DidSelect(tabView, item);
                var index = Array.IndexOf(TabViewItems, item);
                if (index >= 0) SelectionChanged?.Invoke(index);
            }
        }

        sealed class FlippedDocumentView : NSView
        {
            public override bool IsFlipped => true;
        }

        sealed class FlippedStackView : NSStackView
        {
            public override bool IsFlipped => true;
        }
    }
}
