using System;
using System.Collections.Generic;
using System.Globalization;
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
using AnalysisITC.Avalonia.Styling;

namespace AnalysisITC.Avalonia.Preferences;

internal sealed class PreferencesWindow : Window
{
    readonly TextBlock statusText = Text("");

    readonly ComboBox energyUnitCombo;
    readonly ComboBox concentrationUnitCombo;
    readonly TextBox referenceTemperatureBox = Box("");
    readonly TextBox minimumTemperatureSpanBox = Box("");
    readonly TextBox minimumIonSpanBox = Box("");
    readonly ComboBox numberPrecisionCombo;
    readonly ComboBox uncertaintyStyleCombo;
    readonly CheckBox includeBufferInIonicStrengthCheck = Check("Include buffer in ionic-strength calculation");
    readonly CheckBox confirmRemoveDeleteCheck = Check("Confirm remove/delete actions");

    readonly ComboBox dilutionMethodCombo;
    readonly ComboBox peakFitAlgorithmCombo;
    readonly ComboBox bufferSubtractionMethodCombo;
    readonly CheckBox discardIntegrationRegionCheck = Check("Discard integration regions for baseline");
    readonly CheckBox reprocessIntegratedHeatsCheck = Check("Reprocess integrated heats on load");
    readonly ComboBox splineDensityCombo;
    readonly ComboBox splineHandleModeCombo;
    readonly CheckBox splinePointTimeDraggingCheck = Check("Allow spline point time dragging by default");
    readonly CheckBox copyIncludesStartCheck = Check("Copy integration start with selected region");

    readonly ComboBox solverAlgorithmCombo;
    readonly ComboBox errorEstimationCombo;
    readonly TextBox bootstrapIterationsBox = Box("");
    readonly CheckBox concentrationBootstrapCheck = Check("Include concentration errors in bootstrap");
    readonly TextBox concentrationVarianceBox = Box("");
    readonly TextBox optimizerToleranceBox = Box("");
    readonly TextBox maximumIterationsBox = Box("");
    readonly ComboBox parameterLimitCombo;
    readonly CheckBox weightedFittingCheck = Check("Use injection-error weighted fitting");
    readonly CheckBox createSingleResultCheck = Check("Create single-experiment analysis result");
    readonly CheckBox createGlobalResultCheck = Check("Create global analysis result");
    readonly CheckBox autoOpenResultCheck = Check("Auto-open new analysis result");

    readonly ComboBox exportSelectionCombo;
    readonly TextBox decimalsBox = Box("");
    readonly CheckBox unifyTimeAxisCheck = Check("Unify time axis for export");
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

    public bool Applied { get; private set; }

    public PreferencesWindow()
    {
        Title = "Preferences";
        Width = 820;
        Height = 680;
        MinWidth = 680;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        energyUnitCombo = Combo(EnergyUnitAttribute.GetSelectableUnits().Select(unit => Option($"{unit.GetName()} ({unit.GetUnit()})", unit)));
        concentrationUnitCombo = Combo(Enum.GetValues<ConcentrationUnit>().Select(unit => Option(unit.GetProperties().Name, unit)));
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
        peakFitAlgorithmCombo = Combo(Enum.GetValues<PeakFitAlgorithm>().Select(algorithm => Option(DisplayName(algorithm), algorithm)));
        bufferSubtractionMethodCombo = Combo(Enum.GetValues<BufferSubtractionMethod>().Select(method => Option(method.GetDisplayName(), method)));
        splineDensityCombo = Combo(Enum.GetValues<SplineInterpolator.SplinePointDensity>().Select(density => Option(DisplayName(density), density)));
        splineHandleModeCombo = Combo(Enum.GetValues<SplineInterpolator.SplineHandleMode>().Select(mode => Option(DisplayName(mode), mode)));

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
            Option("Selected data", ExportDataSelection.SelectedData),
            Option("Included data", ExportDataSelection.IncludedData),
            Option("All data", ExportDataSelection.AllData)
        });
        fitLineSmoothnessCombo = Combo(Enum.GetValues<LineSmoothness>().Select(smoothness => Option(DisplayName(smoothness), smoothness)));
        attributeDisplayCombo = Combo(new[]
        {
            Option("Used in analysis", DisplayAttributeOptions.UsedInAnalysis),
            Option("All", DisplayAttributeOptions.All),
            Option("None", DisplayAttributeOptions.None)
        });

        BuildLayout();
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
        restore.Click += (_, _) =>
        {
            LoadState(PreferencesState.Defaults());
            SetStatus("Defaults staged. Apply to save them.");
        };
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
        root.Children.Add(tabs);

        Content = root;
    }

    Control BuildGeneralTab()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Section("Units and Formatting", new Control[]
        {
            Row("Energy unit", energyUnitCombo),
            Row("Concentration unit", concentrationUnitCombo),
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
            confirmRemoveDeleteCheck
        }));
        return panel;
    }

    Control BuildProcessingTab()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Section("Processing Defaults", new Control[]
        {
            Row("Dilution method", dilutionMethodCombo),
            Row("Peak fit algorithm", peakFitAlgorithmCombo),
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
            Row("Bootstrap iterations", bootstrapIterationsBox),
            Row("Optimizer tolerance", optimizerToleranceBox),
            Row("Max iterations", maximumIterationsBox),
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
            unifyTimeAxisCheck,
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

    void LoadState(PreferencesState state)
    {
        SetCombo(energyUnitCombo, state.EnergyUnit);
        SetCombo(concentrationUnitCombo, state.DefaultConcentrationUnit);
        referenceTemperatureBox.Text = Format(state.ReferenceTemperature);
        minimumTemperatureSpanBox.Text = Format(state.MinimumTemperatureSpanForFitting);
        minimumIonSpanBox.Text = Format(state.MinimumIonSpanForFitting * 1000);
        SetCombo(numberPrecisionCombo, state.NumberPrecision);
        SetCombo(uncertaintyStyleCombo, state.UncertaintyDisplayStyle);
        includeBufferInIonicStrengthCheck.IsChecked = state.IncludeBufferInIonicStrengthCalc;
        confirmRemoveDeleteCheck.IsChecked = state.ConfirmRemoveDelete;

        SetCombo(dilutionMethodCombo, state.DilutionCalculationMethod);
        SetCombo(peakFitAlgorithmCombo, state.PeakFitAlgorithm);
        SetCombo(bufferSubtractionMethodCombo, state.BufferSubtractionDefaultMethod);
        discardIntegrationRegionCheck.IsChecked = state.DiscardIntegrationRegionForBaseline;
        reprocessIntegratedHeatsCheck.IsChecked = state.ReprocessIntegratedHeatDataOnLoad;
        SetCombo(splineDensityCombo, state.DefaultSplinePointDensity);
        SetCombo(splineHandleModeCombo, state.DefaultSplineHandleMode);
        splinePointTimeDraggingCheck.IsChecked = state.DefaultSplinePointTimeDragging;
        copyIncludesStartCheck.IsChecked = state.IntegrationRegionCopyIncludesStart;

        SetCombo(solverAlgorithmCombo, state.DefaultSolverAlgorithm);
        SetCombo(errorEstimationCombo, state.DefaultErrorEstimationMethod);
        bootstrapIterationsBox.Text = state.DefaultBootstrapIterations.ToString(CultureInfo.CurrentCulture);
        concentrationBootstrapCheck.IsChecked = state.IncludeConcentrationErrorsInBootstrap;
        concentrationVarianceBox.Text = Format(state.ConcentrationAutoVariance * 100);
        optimizerToleranceBox.Text = Format(state.OptimizerTolerance);
        maximumIterationsBox.Text = state.MaximumOptimizerIterations.ToString(CultureInfo.CurrentCulture);
        SetCombo(parameterLimitCombo, state.ParameterLimitSetting);
        weightedFittingCheck.IsChecked = state.UseInjectionErrorWeightedFitting;
        createSingleResultCheck.IsChecked = state.CreateSingleAnalysisResult;
        createGlobalResultCheck.IsChecked = state.CreateGlobalAnalysisResult;
        autoOpenResultCheck.IsChecked = state.AutoOpenNewAnalysisResult;

        SetCombo(exportSelectionCombo, state.ExportSelectionMode);
        decimalsBox.Text = state.NumOfDecimalsToExport.ToString(CultureInfo.CurrentCulture);
        unifyTimeAxisCheck.IsChecked = state.UnifyTimeAxisForExport;
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

    bool TryBuildState(out PreferencesState state)
    {
        state = new PreferencesState();

        if (!TryReadDouble(referenceTemperatureBox, "reference temperature", -273.15, 500, out var referenceTemperature)) return false;
        if (!TryReadDouble(minimumTemperatureSpanBox, "minimum temperature span", 0, 100, out var minimumTemperatureSpan)) return false;
        if (!TryReadDouble(minimumIonSpanBox, "minimum ionic-strength span", 0, 10000, out var minimumIonSpanMm)) return false;
        if (!TryReadInt(bootstrapIterationsBox, "bootstrap iterations", 0, 1_000_000, out var bootstrapIterations)) return false;
        if (!TryReadDouble(concentrationVarianceBox, "concentration variance", 0, 100, out var concentrationVariancePercent)) return false;
        if (!TryReadDouble(optimizerToleranceBox, "optimizer tolerance", 0, 1, out var optimizerTolerance)) return false;
        if (!TryReadInt(maximumIterationsBox, "maximum optimizer iterations", 1, 10_000_000, out var maximumIterations)) return false;
        if (!TryReadInt(decimalsBox, "export decimals", 0, 12, out var decimals)) return false;
        if (!TryReadDouble(figureWidthBox, "figure width", 1, 50, out var figureWidth)) return false;
        if (!TryReadDouble(figureHeightBox, "figure height", 1, 50, out var figureHeight)) return false;

        state.ReferenceTemperature = referenceTemperature;
        state.EnergyUnit = Value(energyUnitCombo, AppSettings.EnergyUnit);
        state.DefaultConcentrationUnit = Value(concentrationUnitCombo, AppSettings.DefaultConcentrationUnit);
        state.MinimumTemperatureSpanForFitting = minimumTemperatureSpan;
        state.MinimumIonSpanForFitting = minimumIonSpanMm / 1000.0;
        state.NumberPrecision = Value(numberPrecisionCombo, AppSettings.NumberPrecision);
        state.UncertaintyDisplayStyle = Value(uncertaintyStyleCombo, AppSettings.UncertaintyDisplayStyle);
        state.IncludeBufferInIonicStrengthCalc = includeBufferInIonicStrengthCheck.IsChecked == true;
        state.ConfirmRemoveDelete = confirmRemoveDeleteCheck.IsChecked == true;

        state.DilutionCalculationMethod = Value(dilutionMethodCombo, AppSettings.DilutionCalculationMethod);
        state.PeakFitAlgorithm = Value(peakFitAlgorithmCombo, AppSettings.PeakFitAlgorithm);
        state.BufferSubtractionDefaultMethod = Value(bufferSubtractionMethodCombo, AppSettings.BufferSubtractionDefaultMethod);
        state.DiscardIntegrationRegionForBaseline = discardIntegrationRegionCheck.IsChecked == true;
        state.ReprocessIntegratedHeatDataOnLoad = reprocessIntegratedHeatsCheck.IsChecked == true;
        state.DefaultSplinePointDensity = Value(splineDensityCombo, AppSettings.DefaultSplinePointDensity);
        state.DefaultSplineHandleMode = Value(splineHandleModeCombo, AppSettings.DefaultSplineHandleMode);
        state.DefaultSplinePointTimeDragging = splinePointTimeDraggingCheck.IsChecked == true;
        state.IntegrationRegionCopyIncludesStart = copyIncludesStartCheck.IsChecked == true;

        state.DefaultSolverAlgorithm = Value(solverAlgorithmCombo, AppSettings.DefaultSolverAlgorithm);
        state.DefaultErrorEstimationMethod = Value(errorEstimationCombo, AppSettings.DefaultErrorEstimationMethod);
        state.DefaultBootstrapIterations = bootstrapIterations;
        state.IncludeConcentrationErrorsInBootstrap = concentrationBootstrapCheck.IsChecked == true;
        state.ConcentrationAutoVariance = concentrationVariancePercent / 100.0;
        state.OptimizerTolerance = optimizerTolerance;
        state.MaximumOptimizerIterations = maximumIterations;
        state.ParameterLimitSetting = Value(parameterLimitCombo, AppSettings.ParameterLimitSetting);
        state.UseInjectionErrorWeightedFitting = weightedFittingCheck.IsChecked == true;
        state.CreateSingleAnalysisResult = createSingleResultCheck.IsChecked == true;
        state.CreateGlobalAnalysisResult = createGlobalResultCheck.IsChecked == true;
        state.AutoOpenNewAnalysisResult = autoOpenResultCheck.IsChecked == true;

        state.ExportSelectionMode = Value(exportSelectionCombo, AppSettings.ExportSelectionMode);
        state.ExportColumns = BuildExportColumns();
        state.NumOfDecimalsToExport = decimals;
        state.UnifyTimeAxisForExport = unifyTimeAxisCheck.IsChecked == true;
        state.ExportBaselineCorrectedData = exportCorrectedDataCheck.IsChecked == true;
        state.ExportFitPointsWithPeaks = exportFitPointsCheck.IsChecked == true;
        state.FinalFigureWidthCentimeters = figureWidth;
        state.FinalFigureHeightCentimeters = figureHeight;
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
            ColumnDefinitions = new ColumnDefinitions("190,*"),
            ColumnSpacing = 10,
            MinHeight = 30
        };
        grid.Children.Add(Label(label));
        Grid.SetColumn(control, 1);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(control);
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
            Width = 150,
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
            Width = 220,
            Height = 28,
            MinHeight = 28,
            MaxHeight = 28,
            Padding = new Thickness(8, 0),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center
        };
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
