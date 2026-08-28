using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

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
using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Avalonia.Support;
using AnalysisITC.Platform;

namespace AnalysisITC.Avalonia.Preferences;

internal sealed class PreferencesWindow : Window
{
    const double FormLabelWidth = 220;
    const double FormControlWidth = 280;
    const double SliderValueWidth = 82;
    const double FormColumnSpacing = 10;
    const double SliderColumnSpacing = 8;
    static int activeTabIndex;

    static readonly int[] AutoSaveIntervalValues = { 1, 2, 5, 10, 20, 30 };
    static readonly int[] BootstrapIterationValues =
        FittingOptionsController.BootstrapIterationPresets.ToArray();
    static readonly int[] MaximumIterationValues =
        { 1, 10, 100, 1_000, 5_000, 10_000, 20_000, 30_000 };
    static readonly double[] OptimizerToleranceValues = { 0, 0.25, 0.5, 0.8, 1 };
    static readonly string[] OptimizerToleranceLabels =
        { "Fast", "Relaxed", "Balanced", "Strict", "Very Strict" };

    readonly TextBlock statusText = Text("");

    readonly ComboBox energyUnitCombo;
    readonly ComboBox concentrationUnitCombo;
    readonly ComboBox designerInstrumentCombo;
    readonly TextBox referenceTemperatureBox = Box("");
    readonly TextBox minimumTemperatureSpanBox = Box("");
    readonly TextBox minimumIonSpanBox = Box("");
    readonly ComboBox numberPrecisionCombo;
    readonly ComboBox uncertaintyStyleCombo;
    readonly CheckBox includeBufferInIonicStrengthCheck = Check("Include buffer in ionic-strength calculation");
    readonly CheckBox onlineChecksCheck = Check("Check for updates and online resources on launch");
    readonly CheckBox confirmRemoveDeleteCheck = Check("Confirm remove/delete actions");
    readonly CheckBox automaticallyDiscardOrphanInjectionsCheck =
        Check("Automatically discard injections outside the thermogram range");
    readonly CheckBox autoSaveEnabledCheck = Check("Enable autosave");
    readonly Slider autoSaveIntervalSlider = DiscreteSlider(AutoSaveIntervalValues.Length);
    readonly TextBlock autoSaveIntervalValueLabel = ValueLabel();
    readonly TextBox autoSaveFileLimitBox = Box("");
    readonly CheckBox recoveryPromptCheck = Check("Prompt to recover after an interrupted session");
    readonly Button openAutoSaveFolderButton = Button("Open Autosave Folder", 160);

    readonly ComboBox dilutionMethodCombo;
    readonly ComboBox bufferSubtractionMethodCombo;
    readonly CheckBox discardIntegrationRegionCheck = Check("Discard integration regions for baseline");
    readonly CheckBox reprocessIntegratedHeatsCheck = Check("Reprocess integrated heats on load");
    readonly ComboBox splineDensityCombo;
    readonly ComboBox splineHandleModeCombo;
    readonly CheckBox splinePointTimeDraggingCheck = Check("Allow spline point time dragging by default");
    readonly CheckBox copyIncludesStartCheck = Check("Copy integration start with selected region");

    readonly ComboBox solverAlgorithmCombo;
    readonly ComboBox errorEstimationCombo;
    readonly Slider bootstrapIterationsSlider = DiscreteSlider(BootstrapIterationValues.Length);
    readonly TextBlock bootstrapIterationsValueLabel = ValueLabel();
    readonly CheckBox concentrationBootstrapCheck = Check("Include concentration errors in bootstrap");
    readonly TextBox concentrationVarianceBox = Box("");
    readonly Slider optimizerToleranceSlider = DiscreteSlider(OptimizerToleranceValues.Length);
    readonly TextBlock optimizerToleranceValueLabel = ValueLabel();
    readonly Slider maximumIterationsSlider = DiscreteSlider(MaximumIterationValues.Length);
    readonly TextBlock maximumIterationsValueLabel = ValueLabel();
    readonly ComboBox parameterLimitCombo;
    readonly CheckBox weightedFittingCheck = Check("Use injection-error weighted fitting");
    readonly CheckBox createSingleResultCheck = Check("Create single-experiment analysis result");
    readonly CheckBox createGlobalResultCheck = Check("Create global analysis result");
    readonly CheckBox autoOpenResultCheck = Check("Auto-open new analysis result");

    readonly ComboBox exportSelectionCombo;
    readonly TextBox decimalsBox = Box("");
    readonly CheckBox exportCorrectedDataCheck = Check("Export baseline-corrected data");
    readonly CheckBox exportFitPointsCheck = Check("Export fit points with peaks");
    readonly CheckBox exportMolarRatioCheck = Check("Molar ratio");
    readonly CheckBox exportInjectionInfoCheck = Check("Injection info");
    readonly CheckBox exportConcentrationsCheck = Check("Concentrations");
    readonly CheckBox exportIncludedCheck = Check("Included state");
    readonly CheckBox exportPeakCheck = Check("Peak heats");
    readonly CheckBox exportFitCheck = Check("Fit values");

    readonly TextBox figureWidthBox = Box("");
    readonly TextBox figureHeightBox = Box("");
    readonly ComboBox publicationFontCombo;
    readonly TextBlock publicationFontResolutionText = Note();
    readonly CheckBox residualGraphCheck = Check("Show residual graph");
    readonly CheckBox residualGapCheck = Check("Show residual graph gap");
    readonly CheckBox unifyResidualAxisCheck = Check("Unify residual graph axis");
    readonly ComboBox fitLineSmoothnessCombo;
    readonly CheckBox parameterBoxDefaultCheck = Check("Show parameter box by default");
    readonly CheckBox detailsDefaultCheck = Check("Show experiment details by default");
    readonly CheckBox modelInfoDefaultCheck = Check("Show model info by default");
    readonly CheckBox displayThermodynamicCheck = Check("Thermodynamic parameters");
    readonly CheckBox displayOffsetCheck = Check("Offset parameter");
    readonly CheckBox displayDerivedCheck = Check("Derived parameters");
    readonly CheckBox displayTemperatureCheck = Check("Temperature");
    readonly CheckBox displayConcentrationsCheck = Check("Concentrations");
    readonly CheckBox displayInjectionDelayCheck = Check("Injection delay");
    readonly CheckBox displayInstrumentCheck = Check("Instrument");
    readonly CheckBox displayAttributesCheck = Check("Attributes");
    readonly ComboBox attributeDisplayCombo;
    readonly CheckBox autoAxesIgnoreBadDataCheck = Check("Auto axes ignore excluded/bad points");

    int loadedAutoSaveInterval;
    int loadedBootstrapIterations;
    int loadedMaximumIterations;
    double loadedOptimizerTolerance;
    bool autoSaveIntervalChanged;
    bool bootstrapIterationsChanged;
    bool maximumIterationsChanged;
    bool optimizerToleranceChanged;
    bool loadingDiscreteValues;

    public bool Applied { get; private set; }

    internal Slider AutoSaveIntervalSlider => autoSaveIntervalSlider;
    internal TextBlock AutoSaveIntervalValueLabel => autoSaveIntervalValueLabel;
    internal Slider BootstrapIterationsSlider => bootstrapIterationsSlider;
    internal TextBlock BootstrapIterationsValueLabel => bootstrapIterationsValueLabel;
    internal Slider OptimizerToleranceSlider => optimizerToleranceSlider;
    internal TextBlock OptimizerToleranceValueLabel => optimizerToleranceValueLabel;
    internal Slider MaximumIterationsSlider => maximumIterationsSlider;
    internal TextBlock MaximumIterationsValueLabel => maximumIterationsValueLabel;
    internal CheckBox AutoSaveEnabledCheck => autoSaveEnabledCheck;
    internal ComboBox EnergyUnitCombo => energyUnitCombo;
    internal ComboBox DefaultDesignerInstrumentCombo => designerInstrumentCombo;
    internal ComboBox PublicationFontCombo => publicationFontCombo;
    internal TextBlock PublicationFontResolutionText => publicationFontResolutionText;

    public PreferencesWindow()
    {
        Title = "Preferences";
        Width = 820;
        Height = 680;
        MinWidth = 680;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        energyUnitCombo = Combo(new[]
        {
            Option("Joule", EnergyUnitFamily.Joules),
            Option("Calories", EnergyUnitFamily.Calories)
        });
        concentrationUnitCombo = Combo(Enum.GetValues<ConcentrationUnit>().Select(unit => Option(unit.GetProperties().Name, unit)));
        designerInstrumentCombo = Combo(ITCInstrumentAttribute.GetITCInstruments().Select(instrument => Option(instrument.GetProperties().Name, instrument)));
        numberPrecisionCombo = Combo(new[]
        {
            Option("Strict", NumberPrecision.Strict),
            Option("Standard", NumberPrecision.Standard),
            Option("Single decimal", NumberPrecision.SingleDecimal),
            Option("All decimals", NumberPrecision.AllDecimals)
        });
        uncertaintyStyleCombo = Combo(new[]
        {
            Option("Automatic", UncertaintyDisplayStyle.Automatic),
            Option("Standard deviation", UncertaintyDisplayStyle.StandardDeviation),
            Option("Confidence interval", UncertaintyDisplayStyle.ConfidenceInterval),
            Option("SD + confidence interval", UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval),
            Option("None", UncertaintyDisplayStyle.None)
        });

        dilutionMethodCombo = Combo(Enum.GetValues<DilutionMethod>().Select(method => Option(DisplayName(method), method)));
        bufferSubtractionMethodCombo = Combo(Enum.GetValues<BufferSubtractionMethod>().Select(method => Option(method.GetDisplayName(), method)));
        splineDensityCombo = Combo(Enum.GetValues<SplineInterpolator.SplinePointDensity>().Select(density => Option(DisplayName(density), density)));
        splineHandleModeCombo = Combo(Enum.GetValues<SplineInterpolator.SplineHandleMode>()
            .Where(mode => mode != SplineInterpolator.SplineHandleMode.MinVolatility)
            .Select(mode => Option(DisplayName(mode), mode)));

        solverAlgorithmCombo = Combo(Enum.GetValues<SolverAlgorithm>().Select(algorithm => Option(algorithm.GetProperties().Name, algorithm)));
        errorEstimationCombo = Combo(Enum.GetValues<ErrorEstimationMethod>().Select(method => Option(method.Description(), method)));
        parameterLimitCombo = Combo(new[]
        {
            Option("Standard", ParameterLimitSetting.Standard),
            Option("Extended", ParameterLimitSetting.Extended),
            Option("No limit", ParameterLimitSetting.NoLimit)
        });

        exportSelectionCombo = Combo(new[]
        {
            Option("Selected experiment", ExportDataSelection.SelectedData),
            Option("Active experiments", ExportDataSelection.IncludedData),
            Option("All experiments", ExportDataSelection.AllData)
        });
        fitLineSmoothnessCombo = Combo(Enum.GetValues<LineSmoothness>().Select(smoothness => Option(DisplayName(smoothness), smoothness)));
        publicationFontCombo = Combo(new[]
        {
            Option("Native", PublicationFont.Native),
            Option("Inter", PublicationFont.Inter),
            Option("Liberation Sans", PublicationFont.LiberationSans)
        });
        attributeDisplayCombo = Combo(new[]
        {
            Option("Used in analysis", DisplayAttributeOptions.UsedInAnalysis),
            Option("All", DisplayAttributeOptions.All),
            Option("None", DisplayAttributeOptions.None)
        });

        BuildLayout();
        openAutoSaveFolderButton.Click += (_, _) => OpenAutoSaveFolder();
        autoSaveEnabledCheck.IsCheckedChanged += (_, _) => UpdateAutoSaveControls();
        autoSaveIntervalSlider.ValueChanged += (_, _) => AutoSaveIntervalChanged();
        bootstrapIterationsSlider.ValueChanged += (_, _) => BootstrapIterationsChanged();
        optimizerToleranceSlider.ValueChanged += (_, _) => OptimizerToleranceChanged();
        maximumIterationsSlider.ValueChanged += (_, _) => MaximumIterationsChanged();
        publicationFontCombo.SelectionChanged += (_, _) => UpdatePublicationFontResolution();
        LoadState(PreferencesState.FromSettings());
    }

    void BuildLayout()
    {
        var root = new DockPanel
        {
            LastChildFill = true
        };
        AppTheme.Bind(root, Panel.BackgroundProperty, AppTheme.WorkspaceBackground);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(12, 10)
        };

        var restore = Button("Restore Defaults", 126);
        restore.Click += (_, _) => RestoreDefaults();
        footer.Children.Add(restore);

        Grid.SetColumn(statusText, 1);
        footer.Children.Add(statusText);

        var cancel = Button("Cancel", 82);
        cancel.Click += (_, _) => Close(false);
        Grid.SetColumn(cancel, 2);
        footer.Children.Add(cancel);

        var apply = Button("Apply", 82);
        apply.Click += (_, _) => Apply();
        Grid.SetColumn(apply, 3);
        footer.Children.Add(apply);

        var footerBorder = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footer
        };
        AppTheme.Bind(footerBorder, Border.BackgroundProperty, AppTheme.PanelBackground);
        AppTheme.Bind(footerBorder, Border.BorderBrushProperty, AppTheme.PanelBorder);
        DockPanel.SetDock(footerBorder, Dock.Bottom);
        root.Children.Add(footerBorder);

        var header = Header();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var tabs = new TabControl
        {
            Margin = new Thickness(12),
            Items =
            {
                Tab("General", Scroll(BuildGeneralTab())),
                Tab("Processing", Scroll(BuildProcessingTab())),
                Tab("Fitting", Scroll(BuildFittingTab())),
                Tab("Export", Scroll(BuildExportTab()))
            }
        };
        tabs.SelectedIndex = Math.Clamp(activeTabIndex, 0, Math.Max(0, tabs.Items.Count - 1));
        tabs.SelectionChanged += (_, _) => RememberActiveTab(tabs);
        root.Children.Add(tabs);

        Content = root;
    }

    static void RememberActiveTab(TabControl tabs)
    {
        if (tabs.SelectedIndex < 0) return;

        activeTabIndex = tabs.SelectedIndex;
    }

    Control BuildGeneralTab()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Section("Units and Formatting", new Control[]
        {
            Row("Energy unit", energyUnitCombo),
            Row("Concentration unit", concentrationUnitCombo),
            Row("Designer instrument", designerInstrumentCombo),
            Row("Number precision", numberPrecisionCombo),
            Row("Uncertainty display", uncertaintyStyleCombo)
        }));
        panel.Children.Add(Section("Analysis Context", new Control[]
        {
            Row("Reference temperature (°C)", referenceTemperatureBox),
            Row("Minimum temperature span (°C)", minimumTemperatureSpanBox),
            Row("Minimum salt span (mM)", minimumIonSpanBox),
            includeBufferInIonicStrengthCheck
        }));
        panel.Children.Add(Section("Behavior", new Control[]
        {
            onlineChecksCheck,
            confirmRemoveDeleteCheck
        }));
        panel.Children.Add(Section("File Loading", new Control[]
        {
            automaticallyDiscardOrphanInjectionsCheck
        }));
        panel.Children.Add(Section("Autosave and Recovery", new Control[]
        {
            autoSaveEnabledCheck,
            SliderRow("Interval (minutes)", autoSaveIntervalSlider, autoSaveIntervalValueLabel),
            Row("Maximum files", autoSaveFileLimitBox),
            recoveryPromptCheck,
            openAutoSaveFolderButton
        }));
        return panel;
    }

    Control BuildProcessingTab()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Section("Processing Defaults", new Control[]
        {
            Row("Dilution method", dilutionMethodCombo),
            Row("Buffer subtraction", bufferSubtractionMethodCombo),
            discardIntegrationRegionCheck,
            reprocessIntegratedHeatsCheck
        }));
        panel.Children.Add(Section("Spline Defaults", new Control[]
        {
            Row("Point density", splineDensityCombo),
            Row("Handle mode", splineHandleModeCombo),
            splinePointTimeDraggingCheck,
            copyIncludesStartCheck
        }));
        return panel;
    }

    Control BuildFittingTab()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Section("Solver", new Control[]
        {
            Row("Default solver", solverAlgorithmCombo),
            Row("Error estimation", errorEstimationCombo),
            SliderRow("Bootstrap iterations", bootstrapIterationsSlider, bootstrapIterationsValueLabel),
            SliderRow("Optimizer tolerance", optimizerToleranceSlider, optimizerToleranceValueLabel),
            SliderRow("Max iterations", maximumIterationsSlider, maximumIterationsValueLabel),
            Row("Parameter limits", parameterLimitCombo),
            weightedFittingCheck
        }));
        panel.Children.Add(Section("Concentration Error", new Control[]
        {
            concentrationBootstrapCheck,
            Row("Auto variance (%)", concentrationVarianceBox)
        }));
        panel.Children.Add(Section("Result Creation", new Control[]
        {
            createSingleResultCheck,
            createGlobalResultCheck,
            autoOpenResultCheck
        }));
        return panel;
    }

    Control BuildExportTab()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Section("Data Export", new Control[]
        {
            Row("Selection", exportSelectionCombo),
            Row("Decimals", decimalsBox),
            exportCorrectedDataCheck,
            exportFitPointsCheck
        }));
        panel.Children.Add(Section("Export Columns", new Control[]
        {
            TwoColumnChecks(exportMolarRatioCheck, exportInjectionInfoCheck, exportConcentrationsCheck, exportIncludedCheck, exportPeakCheck, exportFitCheck)
        }));
        panel.Children.Add(Section("Final Figure Defaults", new Control[]
        {
            Row("Width cm", figureWidthBox),
            Row("Height cm", figureHeightBox),
            Row("Publication font", new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    publicationFontCombo,
                    publicationFontResolutionText
                }
            }),
            residualGraphCheck,
            residualGapCheck,
            unifyResidualAxisCheck,
            Row("Fit line", fitLineSmoothnessCombo),
            parameterBoxDefaultCheck,
            detailsDefaultCheck,
            modelInfoDefaultCheck,
            autoAxesIgnoreBadDataCheck
        }));
        panel.Children.Add(Section("Final Figure Content", new Control[]
        {
            TwoColumnChecks(displayThermodynamicCheck, displayOffsetCheck, displayDerivedCheck, displayTemperatureCheck, displayConcentrationsCheck, displayInjectionDelayCheck, displayInstrumentCheck, displayAttributesCheck),
            Row("Attributes", attributeDisplayCombo)
        }));
        return panel;
    }

    internal void LoadState(PreferencesState state)
    {
        SetCombo(energyUnitCombo, state.EnergyUnitFamily);
        SetCombo(concentrationUnitCombo, state.DefaultConcentrationUnit);
        SetCombo(designerInstrumentCombo, state.DefaultDesignerInstrument);
        referenceTemperatureBox.Text = Format(state.ReferenceTemperature);
        minimumTemperatureSpanBox.Text = Format(state.MinimumTemperatureSpanForFitting);
        minimumIonSpanBox.Text = Format(state.MinimumIonSpanForFitting * 1000);
        SetCombo(numberPrecisionCombo, state.NumberPrecision);
        SetCombo(uncertaintyStyleCombo, state.UncertaintyDisplayStyle);
        includeBufferInIonicStrengthCheck.IsChecked = state.IncludeBufferInIonicStrengthCalc;
        onlineChecksCheck.IsChecked = state.PerformOnlineChecksOnLaunch;
        confirmRemoveDeleteCheck.IsChecked = state.ConfirmRemoveDelete;
        automaticallyDiscardOrphanInjectionsCheck.IsChecked = state.AutomaticallyDiscardOrphanInjectionsOnLoad;
        autoSaveEnabledCheck.IsChecked = state.AutoSaveEnabled;
        loadedAutoSaveInterval = state.AutoSaveIntervalMinutes;
        autoSaveIntervalChanged = false;
        SetDiscreteSliderValue(autoSaveIntervalSlider, NearestIndex(AutoSaveIntervalValues, loadedAutoSaveInterval));
        UpdateAutoSaveIntervalLabel();
        autoSaveFileLimitBox.Text = state.AutoSaveFileLimit.ToString(CultureInfo.CurrentCulture);
        recoveryPromptCheck.IsChecked = state.PromptForAutoSaveRecovery;

        SetCombo(dilutionMethodCombo, state.DilutionCalculationMethod);
        SetCombo(bufferSubtractionMethodCombo, state.BufferSubtractionDefaultMethod);
        discardIntegrationRegionCheck.IsChecked = state.DiscardIntegrationRegionForBaseline;
        reprocessIntegratedHeatsCheck.IsChecked = state.ReprocessIntegratedHeatDataOnLoad;
        SetCombo(splineDensityCombo, state.DefaultSplinePointDensity);
        SetCombo(splineHandleModeCombo, state.DefaultSplineHandleMode);
        splinePointTimeDraggingCheck.IsChecked = state.DefaultSplinePointTimeDragging;
        copyIncludesStartCheck.IsChecked = state.IntegrationRegionCopyIncludesStart;

        SetCombo(solverAlgorithmCombo, state.DefaultSolverAlgorithm);
        SetCombo(errorEstimationCombo, state.DefaultErrorEstimationMethod);
        loadedBootstrapIterations = state.DefaultBootstrapIterations;
        loadedOptimizerTolerance = state.OptimizerTolerance;
        loadedMaximumIterations = state.MaximumOptimizerIterations;
        bootstrapIterationsChanged = false;
        optimizerToleranceChanged = false;
        maximumIterationsChanged = false;
        SetDiscreteSliderValue(bootstrapIterationsSlider, NearestIndex(BootstrapIterationValues, loadedBootstrapIterations));
        SetDiscreteSliderValue(optimizerToleranceSlider, NearestIndex(OptimizerToleranceValues, loadedOptimizerTolerance));
        SetDiscreteSliderValue(maximumIterationsSlider, NearestIndex(MaximumIterationValues, loadedMaximumIterations));
        UpdateBootstrapIterationsLabel();
        UpdateOptimizerToleranceLabel();
        UpdateMaximumIterationsLabel();
        concentrationBootstrapCheck.IsChecked = state.IncludeConcentrationErrorsInBootstrap;
        concentrationVarianceBox.Text = Format(state.ConcentrationAutoVariance * 100);
        SetCombo(parameterLimitCombo, state.ParameterLimitSetting);
        weightedFittingCheck.IsChecked = state.UseInjectionErrorWeightedFitting;
        createSingleResultCheck.IsChecked = state.CreateSingleAnalysisResult;
        createGlobalResultCheck.IsChecked = state.CreateGlobalAnalysisResult;
        autoOpenResultCheck.IsChecked = state.AutoOpenNewAnalysisResult;

        SetCombo(exportSelectionCombo, state.ExportSelectionMode);
        decimalsBox.Text = state.NumOfDecimalsToExport.ToString(CultureInfo.CurrentCulture);
        exportCorrectedDataCheck.IsChecked = state.ExportBaselineCorrectedData;
        exportFitPointsCheck.IsChecked = state.ExportFitPointsWithPeaks;
        exportMolarRatioCheck.IsChecked = state.ExportColumns.HasFlag(ExportColumns.MolarRatio);
        exportInjectionInfoCheck.IsChecked = state.ExportColumns.HasFlag(ExportColumns.InjectionInfo);
        exportConcentrationsCheck.IsChecked = state.ExportColumns.HasFlag(ExportColumns.Concentrations);
        exportIncludedCheck.IsChecked = state.ExportColumns.HasFlag(ExportColumns.Included);
        exportPeakCheck.IsChecked = state.ExportColumns.HasFlag(ExportColumns.Peak);
        exportFitCheck.IsChecked = state.ExportColumns.HasFlag(ExportColumns.Fit);

        figureWidthBox.Text = Format(state.FinalFigureWidthCentimeters);
        figureHeightBox.Text = Format(state.FinalFigureHeightCentimeters);
        SetCombo(publicationFontCombo, state.PublicationFigureFont);
        UpdatePublicationFontResolution();
        residualGraphCheck.IsChecked = state.ShowResidualGraph;
        residualGapCheck.IsChecked = state.ShowResidualGraphGap;
        unifyResidualAxisCheck.IsChecked = state.UnifyResidualGraphAxis;
        SetCombo(fitLineSmoothnessCombo, state.FitLineSmoothness);
        parameterBoxDefaultCheck.IsChecked = state.FinalFigureShowParameterBoxAsDefault;
        detailsDefaultCheck.IsChecked = state.FinalFigureShowDetailsAsDefault;
        modelInfoDefaultCheck.IsChecked = state.FinalFigureShowModelInfoAsDefault;
        displayThermodynamicCheck.IsChecked = state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Thermodynamic);
        displayOffsetCheck.IsChecked = state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Offset);
        displayDerivedCheck.IsChecked = state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Derived);
        displayTemperatureCheck.IsChecked = state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Temperature);
        displayConcentrationsCheck.IsChecked = state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Concentrations);
        displayInjectionDelayCheck.IsChecked = state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.InjectionDelay);
        displayInstrumentCheck.IsChecked = state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Instrument);
        displayAttributesCheck.IsChecked = state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Attributes);
        SetCombo(attributeDisplayCombo, NormalizeAttributeOptions(state.DisplayAttributeOptions));
        autoAxesIgnoreBadDataCheck.IsChecked = state.AutoAxesIgnoresBadData;
        UpdateAutoSaveControls();
    }

    void Apply()
    {
        if (!TryBuildState(out var state)) return;

        try
        {
            state.Apply();
            Applied = true;
            Close(true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    internal bool TryBuildState(out PreferencesState state)
    {
        state = new PreferencesState();

        if (!TryReadDouble(referenceTemperatureBox, "reference temperature", -273.15, 500, out var referenceTemperature)) return false;
        if (!TryReadDouble(minimumTemperatureSpanBox, "minimum temperature span", 0, 100, out var minimumTemperatureSpan)) return false;
        if (!TryReadDouble(minimumIonSpanBox, "minimum ionic-strength span", 0, 10000, out var minimumIonSpanMm)) return false;
        if (!TryReadDouble(concentrationVarianceBox, "concentration variance", 0, 100, out var concentrationVariancePercent)) return false;
        if (!TryReadInt(decimalsBox, "export decimals", 0, 12, out var decimals)) return false;
        if (!TryReadDouble(figureWidthBox, "figure width", 1, 50, out var figureWidth)) return false;
        if (!TryReadDouble(figureHeightBox, "figure height", 1, 50, out var figureHeight)) return false;
        if (!TryReadInt(autoSaveFileLimitBox, "autosave file limit", 1, 100, out var autoSaveFileLimit)) return false;

        state.ReferenceTemperature = referenceTemperature;
        state.EnergyUnitFamily = Value(energyUnitCombo, AppSettings.EnergyUnitFamily);
        state.DefaultConcentrationUnit = Value(concentrationUnitCombo, AppSettings.DefaultConcentrationUnit);
        state.DefaultDesignerInstrument = Value(designerInstrumentCombo, AppSettings.DefaultDesignerInstrument);
        state.MinimumTemperatureSpanForFitting = minimumTemperatureSpan;
        state.MinimumIonSpanForFitting = minimumIonSpanMm / 1000.0;
        state.NumberPrecision = Value(numberPrecisionCombo, AppSettings.NumberPrecision);
        state.UncertaintyDisplayStyle = Value(uncertaintyStyleCombo, AppSettings.UncertaintyDisplayStyle);
        state.IncludeBufferInIonicStrengthCalc = includeBufferInIonicStrengthCheck.IsChecked == true;
        state.PerformOnlineChecksOnLaunch = onlineChecksCheck.IsChecked == true;
        state.ConfirmRemoveDelete = confirmRemoveDeleteCheck.IsChecked == true;
        state.AutomaticallyDiscardOrphanInjectionsOnLoad = automaticallyDiscardOrphanInjectionsCheck.IsChecked == true;
        state.AutoSaveEnabled = autoSaveEnabledCheck.IsChecked == true;
        state.AutoSaveIntervalMinutes = autoSaveIntervalChanged
            ? AutoSaveIntervalValues[SliderIndex(autoSaveIntervalSlider, AutoSaveIntervalValues.Length)]
            : loadedAutoSaveInterval;
        state.AutoSaveFileLimit = autoSaveFileLimit;
        state.PromptForAutoSaveRecovery = recoveryPromptCheck.IsChecked == true;

        state.DilutionCalculationMethod = Value(dilutionMethodCombo, AppSettings.DilutionCalculationMethod);
        state.BufferSubtractionDefaultMethod = Value(bufferSubtractionMethodCombo, AppSettings.BufferSubtractionDefaultMethod);
        state.DiscardIntegrationRegionForBaseline = discardIntegrationRegionCheck.IsChecked == true;
        state.ReprocessIntegratedHeatDataOnLoad = reprocessIntegratedHeatsCheck.IsChecked == true;
        state.DefaultSplinePointDensity = Value(splineDensityCombo, AppSettings.DefaultSplinePointDensity);
        state.DefaultSplineHandleMode = Value(splineHandleModeCombo, AppSettings.DefaultSplineHandleMode);
        state.DefaultSplinePointTimeDragging = splinePointTimeDraggingCheck.IsChecked == true;
        state.IntegrationRegionCopyIncludesStart = copyIncludesStartCheck.IsChecked == true;

        state.DefaultSolverAlgorithm = Value(solverAlgorithmCombo, AppSettings.DefaultSolverAlgorithm);
        state.DefaultErrorEstimationMethod = Value(errorEstimationCombo, AppSettings.DefaultErrorEstimationMethod);
        state.DefaultBootstrapIterations = bootstrapIterationsChanged
            ? BootstrapIterationValues[SliderIndex(bootstrapIterationsSlider, BootstrapIterationValues.Length)]
            : loadedBootstrapIterations;
        state.IncludeConcentrationErrorsInBootstrap = concentrationBootstrapCheck.IsChecked == true;
        state.ConcentrationAutoVariance = concentrationVariancePercent / 100.0;
        state.OptimizerTolerance = optimizerToleranceChanged
            ? OptimizerToleranceValues[SliderIndex(optimizerToleranceSlider, OptimizerToleranceValues.Length)]
            : loadedOptimizerTolerance;
        state.MaximumOptimizerIterations = maximumIterationsChanged
            ? MaximumIterationValues[SliderIndex(maximumIterationsSlider, MaximumIterationValues.Length)]
            : loadedMaximumIterations;
        state.ParameterLimitSetting = Value(parameterLimitCombo, AppSettings.ParameterLimitSetting);
        state.UseInjectionErrorWeightedFitting = weightedFittingCheck.IsChecked == true;
        state.CreateSingleAnalysisResult = createSingleResultCheck.IsChecked == true;
        state.CreateGlobalAnalysisResult = createGlobalResultCheck.IsChecked == true;
        state.AutoOpenNewAnalysisResult = autoOpenResultCheck.IsChecked == true;

        state.ExportSelectionMode = Value(exportSelectionCombo, AppSettings.ExportSelectionMode);
        state.ExportColumns = BuildExportColumns();
        state.NumOfDecimalsToExport = decimals;
        state.ExportBaselineCorrectedData = exportCorrectedDataCheck.IsChecked == true;
        state.ExportFitPointsWithPeaks = exportFitPointsCheck.IsChecked == true;
        state.FinalFigureWidthCentimeters = figureWidth;
        state.FinalFigureHeightCentimeters = figureHeight;
        state.PublicationFigureFont = Value(publicationFontCombo, AppSettings.PublicationFigureFont);
        state.ShowResidualGraph = residualGraphCheck.IsChecked == true;
        state.ShowResidualGraphGap = residualGapCheck.IsChecked == true;
        state.UnifyResidualGraphAxis = unifyResidualAxisCheck.IsChecked == true;
        state.FitLineSmoothness = Value(fitLineSmoothnessCombo, AppSettings.FitLineSmoothness);
        state.FinalFigureShowParameterBoxAsDefault = parameterBoxDefaultCheck.IsChecked == true;
        state.FinalFigureShowDetailsAsDefault = detailsDefaultCheck.IsChecked == true;
        state.FinalFigureShowModelInfoAsDefault = modelInfoDefaultCheck.IsChecked == true;
        state.FinalFigureParameterDisplay = BuildFinalFigureDisplayParameters();
        state.DisplayAttributeOptions = Value(attributeDisplayCombo, AppSettings.DisplayAttributeOptions);
        state.AutoAxesIgnoresBadData = autoAxesIgnoreBadDataCheck.IsChecked == true;

        return true;
    }

    internal void RestoreDefaults()
    {
        LoadState(PreferencesState.Defaults());
        SetStatus("Defaults staged. Apply to save them.");
    }

    void AutoSaveIntervalChanged()
    {
        if (loadingDiscreteValues) return;
        autoSaveIntervalChanged = true;
        UpdateAutoSaveIntervalLabel();
    }

    void BootstrapIterationsChanged()
    {
        if (loadingDiscreteValues) return;
        bootstrapIterationsChanged = true;
        UpdateBootstrapIterationsLabel();
    }

    void OptimizerToleranceChanged()
    {
        if (loadingDiscreteValues) return;
        optimizerToleranceChanged = true;
        UpdateOptimizerToleranceLabel();
    }

    void MaximumIterationsChanged()
    {
        if (loadingDiscreteValues) return;
        maximumIterationsChanged = true;
        UpdateMaximumIterationsLabel();
    }

    void UpdateAutoSaveControls()
    {
        var enabled = autoSaveEnabledCheck.IsChecked == true;
        autoSaveIntervalSlider.IsEnabled = enabled;
        autoSaveIntervalValueLabel.IsEnabled = enabled;
    }

    void UpdateAutoSaveIntervalLabel()
    {
        var value = autoSaveIntervalChanged
            ? AutoSaveIntervalValues[SliderIndex(autoSaveIntervalSlider, AutoSaveIntervalValues.Length)]
            : loadedAutoSaveInterval;
        autoSaveIntervalValueLabel.Text = $"{value} min";
    }

    void UpdateBootstrapIterationsLabel()
    {
        var value = bootstrapIterationsChanged
            ? BootstrapIterationValues[SliderIndex(bootstrapIterationsSlider, BootstrapIterationValues.Length)]
            : loadedBootstrapIterations;
        bootstrapIterationsValueLabel.Text = value.ToString("N0", CultureInfo.CurrentCulture);
    }

    void UpdateOptimizerToleranceLabel() =>
        optimizerToleranceValueLabel.Text = OptimizerToleranceLabels[
            SliderIndex(optimizerToleranceSlider, OptimizerToleranceValues.Length)];

    void UpdateMaximumIterationsLabel()
    {
        var value = maximumIterationsChanged
            ? MaximumIterationValues[SliderIndex(maximumIterationsSlider, MaximumIterationValues.Length)]
            : loadedMaximumIterations;
        maximumIterationsValueLabel.Text = value.ToString("N0", CultureInfo.CurrentCulture);
    }

    void UpdatePublicationFontResolution()
    {
        try
        {
            var selected = Value(publicationFontCombo, PublicationFont.Native);
            var resolved = SkiaPublicationFontResolver.Shared.Resolve(selected);
            publicationFontResolutionText.Text = $"Resolved on this computer: {resolved.ResolutionDescription}";
        }
        catch (Exception ex)
        {
            publicationFontResolutionText.Text = $"Could not resolve publication font: {ex.Message}";
        }
    }

    void SetDiscreteSliderValue(Slider slider, int index)
    {
        loadingDiscreteValues = true;
        slider.Value = index;
        loadingDiscreteValues = false;
    }

    void OpenAutoSaveFolder()
    {
        try
        {
            Directory.CreateDirectory(AutoSaveManager.Shared.AutoSaveDirectory);
            if (!ExternalLinkLauncher.TryOpen(AutoSaveManager.Shared.AutoSaveDirectory))
                SetStatus("Could not open the autosave folder.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    ExportColumns BuildExportColumns()
    {
        var columns = ExportColumns.None;
        if (exportMolarRatioCheck.IsChecked == true) columns |= ExportColumns.MolarRatio;
        if (exportInjectionInfoCheck.IsChecked == true) columns |= ExportColumns.InjectionInfo;
        if (exportConcentrationsCheck.IsChecked == true) columns |= ExportColumns.Concentrations;
        if (exportIncludedCheck.IsChecked == true) columns |= ExportColumns.Included;
        if (exportPeakCheck.IsChecked == true) columns |= ExportColumns.Peak;
        if (exportFitCheck.IsChecked == true) columns |= ExportColumns.Fit;
        return columns;
    }

    FinalFigureDisplayParameters BuildFinalFigureDisplayParameters()
    {
        var display = FinalFigureDisplayParameters.None;
        if (modelInfoDefaultCheck.IsChecked == true) display |= FinalFigureDisplayParameters.Model;
        if (displayThermodynamicCheck.IsChecked == true) display |= FinalFigureDisplayParameters.Thermodynamic;
        if (displayOffsetCheck.IsChecked == true) display |= FinalFigureDisplayParameters.Offset;
        if (displayDerivedCheck.IsChecked == true) display |= FinalFigureDisplayParameters.Derived;
        if (displayTemperatureCheck.IsChecked == true) display |= FinalFigureDisplayParameters.Temperature;
        if (displayConcentrationsCheck.IsChecked == true) display |= FinalFigureDisplayParameters.Concentrations;
        if (displayInjectionDelayCheck.IsChecked == true) display |= FinalFigureDisplayParameters.InjectionDelay;
        if (displayInstrumentCheck.IsChecked == true) display |= FinalFigureDisplayParameters.Instrument;
        if (displayAttributesCheck.IsChecked == true) display |= FinalFigureDisplayParameters.Attributes;
        return display;
    }

    bool TryReadDouble(TextBox box, string label, double min, double max, out double value)
    {
        value = 0;
        var text = box.Text;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            SetStatus($"Invalid {label}.");
            return false;
        }

        if (value >= min && value <= max) return true;

        SetStatus($"{label} must be between {min:G5} and {max:G5}.");
        return false;
    }

    bool TryReadInt(TextBox box, string label, int min, int max, out int value)
    {
        value = 0;
        var text = box.Text;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
            || int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            if (value >= min && value <= max) return true;
        }

        SetStatus($"{label} must be an integer between {min} and {max}.");
        return false;
    }

    void SetStatus(string status)
    {
        statusText.Text = status;
    }

    static DisplayAttributeOptions NormalizeAttributeOptions(DisplayAttributeOptions options)
    {
        if (options == DisplayAttributeOptions.All || options == DisplayAttributeOptions.None)
            return options;

        return DisplayAttributeOptions.UsedInAnalysis;
    }

    static string Format(double value) => value.ToString("G6", CultureInfo.CurrentCulture);

    static string DisplayName(Enum value)
    {
        var text = value.GetEnumDescription();
        return string.Concat(text.Select((character, index) =>
            index > 0 && char.IsUpper(character) && !char.IsUpper(text[index - 1]) ? " " + character : character.ToString()));
    }

    static Border Header()
    {
        var panel = new StackPanel { Spacing = 2 };
        var title = new TextBlock
        {
            Text = "Preferences",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold
        };
        AppTheme.Bind(title, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
        panel.Children.Add(title);
        var subtitle = new TextBlock
        {
            Text = "Global application settings",
            FontSize = 12
        };
        AppTheme.Bind(subtitle, TextBlock.ForegroundProperty, AppTheme.MutedText);
        panel.Children.Add(subtitle);

        var border = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 12),
            Child = panel
        };
        AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
        AppTheme.Bind(border, Border.BorderBrushProperty, AppTheme.PanelBorder);
        return border;
    }

    static Border Section(string title, Control[] controls)
    {
        var panel = new StackPanel { Spacing = 7 };
        var titleBlock = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold
        };
        AppTheme.Bind(titleBlock, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
        panel.Children.Add(titleBlock);
        foreach (var control in controls)
            panel.Children.Add(control);

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10),
            Child = panel
        };
        AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
        AppTheme.Bind(border, Border.BorderBrushProperty, AppTheme.PanelBorder);
        return border;
    }

    static Control Row(string label, Control control)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{FormLabelWidth},*"),
            ColumnSpacing = FormColumnSpacing,
            MinHeight = 30
        };
        grid.Children.Add(Label(label));
        Grid.SetColumn(control, 1);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(control);
        return grid;
    }

    static Control SliderRow(string label, Slider slider, TextBlock valueLabel)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{FormLabelWidth},*"),
            ColumnSpacing = FormColumnSpacing,
            MinHeight = 30
        };
        grid.Children.Add(Label(label));

        var group = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                $"{SliderValueWidth},{FormControlWidth - SliderValueWidth - SliderColumnSpacing}"),
            ColumnSpacing = SliderColumnSpacing,
            Width = FormControlWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        group.Children.Add(valueLabel);
        Grid.SetColumn(slider, 1);
        group.Children.Add(slider);

        Grid.SetColumn(group, 1);
        grid.Children.Add(group);
        return grid;
    }

    static Grid TwoColumnChecks(params CheckBox[] checks)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowSpacing = 2,
            ColumnSpacing = 10
        };

        for (int i = 0; i < checks.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(checks[i], i % 2);
            Grid.SetRow(checks[i], i / 2);
            grid.Children.Add(checks[i]);
        }

        return grid;
    }

    static TabItem Tab(string header, Control content)
    {
        return new TabItem
        {
            Header = new TextBlock
            {
                Text = header,
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap
            },
            Content = content
        };
    }

    static ScrollViewer Scroll(Control content)
    {
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content
        };
    }

    static TextBox Box(string text)
    {
        return new TextBox
        {
            Text = text,
            Width = FormControlWidth,
            Height = 28,
            MinHeight = 28,
            MaxHeight = 28,
            Padding = new Thickness(8, 0),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    static CheckBox Check(string text)
    {
        return new CheckBox
        {
            Content = text,
            FontSize = 13,
            MinHeight = 24,
        };
    }

    static ComboBox Combo<T>(IEnumerable<PreferenceOption<T>> options)
    {
        return new ComboBox
        {
            ItemsSource = options.ToList(),
            Width = FormControlWidth,
            Height = 28,
            MinHeight = 28,
            MaxHeight = 28,
            Padding = new Thickness(8, 0),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    static Slider DiscreteSlider(int valueCount)
    {
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = valueCount - 1,
            TickFrequency = 1,
            TickPlacement = TickPlacement.None,
            IsSnapToTickEnabled = true,
            SmallChange = 1,
            LargeChange = 1,
            Height = 28,
            VerticalAlignment = VerticalAlignment.Center
        };
        slider.Resources["SliderPreContentMargin"] = new GridLength(4);
        slider.Resources["SliderPostContentMargin"] = new GridLength(4);
        slider.Resources["SliderHorizontalHeight"] = 28d;
        return slider;
    }

    static TextBlock ValueLabel()
    {
        var label = new TextBlock
        {
            FontSize = 13,
            TextAlignment = TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        };
        AppTheme.Bind(label, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
        return label;
    }

    static TextBlock Note()
    {
        var note = new TextBlock
        {
            Width = FormControlWidth,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        AppTheme.Bind(note, TextBlock.ForegroundProperty, AppTheme.MutedText);
        return note;
    }

    static Button Button(string text, double width)
    {
        return new Button
        {
            Content = text,
            MinWidth = width,
            Height = 26,
            Padding = new Thickness(8, 1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    static TextBlock Label(string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        };
        AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
        return textBlock;
    }

    static TextBlock Text(string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
        return textBlock;
    }

    static PreferenceOption<T> Option<T>(string label, T value) => new(label, value);

    static void SetCombo<T>(ComboBox combo, T value)
    {
        combo.SelectedItem = combo.ItemsSource?.OfType<PreferenceOption<T>>().FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, value));
    }

    static T Value<T>(ComboBox combo, T fallback)
    {
        return combo.SelectedItem is PreferenceOption<T> option ? option.Value : fallback;
    }

    static int NearestIndex(int[] values, int value)
    {
        var nearest = 0;
        var smallestDistance = Math.Abs((long)values[0] - value);
        for (var index = 1; index < values.Length; index++)
        {
            var distance = Math.Abs((long)values[index] - value);
            if (distance < smallestDistance)
            {
                nearest = index;
                smallestDistance = distance;
            }
        }
        return nearest;
    }

    static int NearestIndex(double[] values, double value)
    {
        var nearest = 0;
        var smallestDistance = Math.Abs(values[0] - value);
        for (var index = 1; index < values.Length; index++)
        {
            var distance = Math.Abs(values[index] - value);
            if (distance < smallestDistance)
            {
                nearest = index;
                smallestDistance = distance;
            }
        }
        return nearest;
    }

    static int SliderIndex(Slider slider, int valueCount) =>
        Math.Clamp((int)Math.Round(slider.Value), 0, valueCount - 1);

    sealed class PreferenceOption<T>
    {
        public PreferenceOption(string label, T value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }
        public T Value { get; }

        public override string ToString() => Label;
    }
}
