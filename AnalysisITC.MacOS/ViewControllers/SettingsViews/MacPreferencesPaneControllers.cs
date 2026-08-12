using System;
using System.IO;
using System.Linq;

using AppKit;
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
    public sealed partial class MacGeneralPreferencesViewController : MacPreferencesPaneController
    {
        public MacGeneralPreferencesViewController(IntPtr handle) : base(handle) { }

        internal override int PaneIndex => 0;

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            PopulatePopup(EnergyUnitPopup,
                new[] { EnergyUnit.KiloJoule, EnergyUnit.KCal },
                value => value.GetProperties().LongName);
            PopulatePopup(ConcentrationUnitPopup, EnumValues<ConcentrationUnit>(),
                value => value.GetProperties().Name);
            PopulatePopup(NumberPrecisionPopup, EnumValues<NumberPrecision>(), FriendlyName);
            PopulatePopup(UncertaintyPopup, EnumValues<UncertaintyDisplayStyle>(), FriendlyName);
            PopulatePopup(InstrumentPopup, ITCInstrumentAttribute.GetITCInstruments().ToArray(),
                value => value.GetProperties().Name);
            UpdateAutoSaveControls();
        }

        internal override void LoadState(MacPreferencesState state)
        {
            SelectPopup(EnergyUnitPopup, state.EnergyUnit);
            SelectPopup(ConcentrationUnitPopup, state.DefaultConcentrationUnit);
            SelectPopup(NumberPrecisionPopup, state.NumberPrecision);
            SelectPopup(UncertaintyPopup, state.UncertaintyDisplayStyle);
            SelectPopup(InstrumentPopup, state.DefaultDesignerInstrument);
            ReferenceTemperatureField.StringValue = Format(state.ReferenceTemperature);
            MinimumTemperatureSpanField.StringValue = Format(state.MinimumTemperatureSpanForFitting);
            MinimumIonSpanField.StringValue = Format(state.MinimumIonSpanForFitting * 1000);
            Set(IncludeBufferCheck, state.IncludeBufferInIonicStrengthCalc);
            Set(OnlineChecksCheck, state.PerformOnlineChecksOnLaunch);
            Set(ConfirmDeleteCheck, state.ConfirmRemoveDelete);
            Set(DiscardOrphanCheck, state.AutomaticallyDiscardOrphanInjectionsOnLoad);
            Set(AutoSaveEnabledCheck, state.AutoSaveEnabled);
            AutoSaveIntervalField.IntValue = state.AutoSaveIntervalMinutes;
            AutoSaveLimitField.IntValue = state.AutoSaveFileLimit;
            Set(RecoveryPromptCheck, state.PromptForAutoSaveRecovery);
            UpdateAutoSaveControls();
        }

        internal override bool TryUpdateState(MacPreferencesState state, out PreferencesValidationError error)
        {
            if (!ReadDouble(ReferenceTemperatureField, "reference temperature", -273.15, 500,
                out var referenceTemperature, out error)) return false;
            if (!ReadDouble(MinimumTemperatureSpanField, "minimum temperature span", 0, 100,
                out var minimumTemperatureSpan, out error)) return false;
            if (!ReadDouble(MinimumIonSpanField, "minimum ionic-strength span", 0, 10000,
                out var minimumIonSpan, out error)) return false;
            if (!ReadInt(AutoSaveIntervalField, "autosave interval", 1, 60,
                out var autoSaveInterval, out error)) return false;
            if (!ReadInt(AutoSaveLimitField, "autosave file limit", 1, 100,
                out var autoSaveLimit, out error)) return false;

            state.EnergyUnit = PopupValue<EnergyUnit>(EnergyUnitPopup);
            state.DefaultConcentrationUnit = PopupValue<ConcentrationUnit>(ConcentrationUnitPopup);
            state.NumberPrecision = PopupValue<NumberPrecision>(NumberPrecisionPopup);
            state.UncertaintyDisplayStyle = PopupValue<UncertaintyDisplayStyle>(UncertaintyPopup);
            state.DefaultDesignerInstrument = PopupValue<ITCInstrument>(InstrumentPopup);
            state.ReferenceTemperature = referenceTemperature;
            state.MinimumTemperatureSpanForFitting = minimumTemperatureSpan;
            state.MinimumIonSpanForFitting = minimumIonSpan / 1000;
            state.IncludeBufferInIonicStrengthCalc = IsOn(IncludeBufferCheck);
            state.PerformOnlineChecksOnLaunch = IsOn(OnlineChecksCheck);
            state.ConfirmRemoveDelete = IsOn(ConfirmDeleteCheck);
            state.AutomaticallyDiscardOrphanInjectionsOnLoad = IsOn(DiscardOrphanCheck);
            state.AutoSaveEnabled = IsOn(AutoSaveEnabledCheck);
            state.AutoSaveIntervalMinutes = autoSaveInterval;
            state.AutoSaveFileLimit = autoSaveLimit;
            state.PromptForAutoSaveRecovery = IsOn(RecoveryPromptCheck);
            error = null;
            return true;
        }

        partial void AutoSaveEnabledChanged(NSObject sender) => UpdateAutoSaveControls();

        partial void OpenAutoSaveFolder(NSObject sender)
        {
            try
            {
                Directory.CreateDirectory(AutoSaveManager.Shared.AutoSaveDirectory);
                AppDelegate.OpenAutoSaveFolder();
                Coordinator?.SetCurrentStatus("", false);
            }
            catch (Exception ex)
            {
                Coordinator?.SetCurrentStatus(ex.Message, true);
            }
        }

        void UpdateAutoSaveControls()
        {
            if (AutoSaveEnabledCheck == null) return;
            var enabled = IsOn(AutoSaveEnabledCheck);
            AutoSaveIntervalField.Enabled = enabled;
            AutoSaveLimitField.Enabled = enabled;
            RecoveryPromptCheck.Enabled = enabled;
        }
    }

    public sealed partial class MacProcessingPreferencesViewController : MacPreferencesPaneController
    {
        public MacProcessingPreferencesViewController(IntPtr handle) : base(handle) { }

        internal override int PaneIndex => 1;

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            PopulatePopup(DilutionPopup, EnumValues<DilutionMethod>(), FriendlyName);
            PopulatePopup(BufferSubtractionPopup, EnumValues<BufferSubtractionMethod>(),
                value => value.GetDisplayName());
            PopulatePopup(SplineDensityPopup, EnumValues<SplineInterpolator.SplinePointDensity>(), FriendlyName);
            PopulatePopup(SplineHandlePopup, EnumValues<SplineInterpolator.SplineHandleMode>(), FriendlyName);
        }

        internal override void LoadState(MacPreferencesState state)
        {
            SelectPopup(DilutionPopup, state.DilutionCalculationMethod);
            SelectPopup(BufferSubtractionPopup, state.BufferSubtractionDefaultMethod);
            SelectPopup(SplineDensityPopup, state.DefaultSplinePointDensity);
            SelectPopup(SplineHandlePopup, state.DefaultSplineHandleMode);
            Set(DiscardIntegrationCheck, state.DiscardIntegrationRegionForBaseline);
            Set(ReprocessIntegratedCheck, state.ReprocessIntegratedHeatDataOnLoad);
            Set(SplineTimeDraggingCheck, state.DefaultSplinePointTimeDragging);
            Set(CopyIntegrationStartCheck, state.IntegrationRegionCopyIncludesStart);
        }

        internal override bool TryUpdateState(MacPreferencesState state, out PreferencesValidationError error)
        {
            state.DilutionCalculationMethod = PopupValue<DilutionMethod>(DilutionPopup);
            state.BufferSubtractionDefaultMethod = PopupValue<BufferSubtractionMethod>(BufferSubtractionPopup);
            state.DefaultSplinePointDensity = PopupValue<SplineInterpolator.SplinePointDensity>(SplineDensityPopup);
            state.DefaultSplineHandleMode = PopupValue<SplineInterpolator.SplineHandleMode>(SplineHandlePopup);
            state.DiscardIntegrationRegionForBaseline = IsOn(DiscardIntegrationCheck);
            state.ReprocessIntegratedHeatDataOnLoad = IsOn(ReprocessIntegratedCheck);
            state.DefaultSplinePointTimeDragging = IsOn(SplineTimeDraggingCheck);
            state.IntegrationRegionCopyIncludesStart = IsOn(CopyIntegrationStartCheck);
            error = null;
            return true;
        }
    }

    public sealed partial class MacFittingPreferencesViewController : MacPreferencesPaneController
    {
        public MacFittingPreferencesViewController(IntPtr handle) : base(handle) { }

        internal override int PaneIndex => 2;

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            PopulatePopup(SolverPopup, EnumValues<SolverAlgorithm>(), value =>
                value == SolverAlgorithm.NelderMead ? "Nelder–Mead" : "Levenberg–Marquardt");
            PopulatePopup(ErrorMethodPopup, EnumValues<ErrorEstimationMethod>(), value => value.Description());
            PopulatePopup(ParameterLimitPopup, EnumValues<ParameterLimitSetting>(), FriendlyName);
        }

        internal override void LoadState(MacPreferencesState state)
        {
            SelectPopup(SolverPopup, state.DefaultSolverAlgorithm);
            SelectPopup(ErrorMethodPopup, state.DefaultErrorEstimationMethod);
            SelectPopup(ParameterLimitPopup, state.ParameterLimitSetting);
            BootstrapIterationsField.IntValue = state.DefaultBootstrapIterations;
            OptimizerToleranceField.StringValue = Format(state.OptimizerTolerance);
            MaximumIterationsField.IntValue = state.MaximumOptimizerIterations;
            ConcentrationVarianceField.StringValue = Format(state.ConcentrationAutoVariance * 100);
            Set(ConcentrationBootstrapCheck, state.IncludeConcentrationErrorsInBootstrap);
            Set(WeightedFittingCheck, state.UseInjectionErrorWeightedFitting);
            Set(CreateSingleResultCheck, state.CreateSingleAnalysisResult);
            Set(CreateGlobalResultCheck, state.CreateGlobalAnalysisResult);
            Set(AutoOpenResultCheck, state.AutoOpenNewAnalysisResult);
        }

        internal override bool TryUpdateState(MacPreferencesState state, out PreferencesValidationError error)
        {
            if (!ReadInt(BootstrapIterationsField, "bootstrap iterations", 0, 1_000_000,
                out var bootstrapIterations, out error)) return false;
            if (!ReadDouble(OptimizerToleranceField, "optimizer tolerance", 0, 1,
                out var optimizerTolerance, out error)) return false;
            if (!ReadInt(MaximumIterationsField, "maximum optimizer iterations", 1, 10_000_000,
                out var maximumIterations, out error)) return false;
            if (!ReadDouble(ConcentrationVarianceField, "concentration variance", 0, 100,
                out var concentrationVariance, out error)) return false;

            state.DefaultSolverAlgorithm = PopupValue<SolverAlgorithm>(SolverPopup);
            state.DefaultErrorEstimationMethod = PopupValue<ErrorEstimationMethod>(ErrorMethodPopup);
            state.ParameterLimitSetting = PopupValue<ParameterLimitSetting>(ParameterLimitPopup);
            state.DefaultBootstrapIterations = bootstrapIterations;
            state.OptimizerTolerance = optimizerTolerance;
            state.MaximumOptimizerIterations = maximumIterations;
            state.ConcentrationAutoVariance = concentrationVariance / 100;
            state.IncludeConcentrationErrorsInBootstrap = IsOn(ConcentrationBootstrapCheck);
            state.UseInjectionErrorWeightedFitting = IsOn(WeightedFittingCheck);
            state.CreateSingleAnalysisResult = IsOn(CreateSingleResultCheck);
            state.CreateGlobalAnalysisResult = IsOn(CreateGlobalResultCheck);
            state.AutoOpenNewAnalysisResult = IsOn(AutoOpenResultCheck);
            error = null;
            return true;
        }
    }

    public sealed partial class MacExportPreferencesViewController : MacPreferencesPaneController
    {
        public MacExportPreferencesViewController(IntPtr handle) : base(handle) { }

        internal override int PaneIndex => 3;

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            PopulatePopup(ExportSelectionPopup, EnumValues<ExportDataSelection>(), FriendlyName);
            PopulatePopup(FitLinePopup, EnumValues<LineSmoothness>(), FriendlyName);
            PopulatePopup(AttributeDisplayPopup, new[]
            {
                DisplayAttributeOptions.UsedInAnalysis,
                DisplayAttributeOptions.All,
                DisplayAttributeOptions.None
            }, FriendlyName);
            UpdateResidualControls();
        }

        internal override void LoadState(MacPreferencesState state)
        {
            SelectPopup(ExportSelectionPopup, state.ExportSelectionMode);
            SelectPopup(FitLinePopup, state.FitLineSmoothness);
            SelectPopup(AttributeDisplayPopup, NormalizeAttributeOptions(state.DisplayAttributeOptions));
            ExportDecimalsField.IntValue = state.NumOfDecimalsToExport;
            FigureWidthField.StringValue = Format(state.FinalFigureWidthCentimeters);
            FigureHeightField.StringValue = Format(state.FinalFigureHeightCentimeters);
            Set(ExportCorrectedCheck, state.ExportBaselineCorrectedData);
            Set(ExportFitPointsCheck, state.ExportFitPointsWithPeaks);
            Set(ExportMolarRatioCheck, state.ExportColumns.HasFlag(ExportColumns.MolarRatio));
            Set(ExportInjectionInfoCheck, state.ExportColumns.HasFlag(ExportColumns.InjectionInfo));
            Set(ExportConcentrationsCheck, state.ExportColumns.HasFlag(ExportColumns.Concentrations));
            Set(ExportIncludedCheck, state.ExportColumns.HasFlag(ExportColumns.Included));
            Set(ExportPeakCheck, state.ExportColumns.HasFlag(ExportColumns.Peak));
            Set(ExportFitCheck, state.ExportColumns.HasFlag(ExportColumns.Fit));
            Set(ShowResidualCheck, state.ShowResidualGraph);
            Set(ResidualGapCheck, state.ShowResidualGraphGap);
            Set(UnifyResidualAxisCheck, state.UnifyResidualGraphAxis);
            Set(ParameterBoxCheck, state.FinalFigureShowParameterBoxAsDefault);
            Set(ExperimentDetailsCheck, state.FinalFigureShowDetailsAsDefault);
            Set(ModelInfoCheck, state.FinalFigureShowModelInfoAsDefault);
            Set(AutoAxesCheck, state.AutoAxesIgnoresBadData);
            Set(ThermodynamicCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Thermodynamic));
            Set(OffsetCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Offset));
            Set(DerivedCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Derived));
            Set(TemperatureCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Temperature));
            Set(ConcentrationsCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Concentrations));
            Set(InjectionDelayCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.InjectionDelay));
            Set(InstrumentInfoCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Instrument));
            Set(AttributesCheck, state.FinalFigureParameterDisplay.HasFlag(FinalFigureDisplayParameters.Attributes));
            UpdateResidualControls();
        }

        internal override bool TryUpdateState(MacPreferencesState state, out PreferencesValidationError error)
        {
            if (!ReadInt(ExportDecimalsField, "export decimals", 0, 12,
                out var exportDecimals, out error)) return false;
            if (!ReadDouble(FigureWidthField, "figure width", 1, 50,
                out var figureWidth, out error)) return false;
            if (!ReadDouble(FigureHeightField, "figure height", 1, 50,
                out var figureHeight, out error)) return false;

            state.ExportSelectionMode = PopupValue<ExportDataSelection>(ExportSelectionPopup);
            state.FitLineSmoothness = PopupValue<LineSmoothness>(FitLinePopup);
            state.DisplayAttributeOptions = PopupValue<DisplayAttributeOptions>(AttributeDisplayPopup);
            state.NumOfDecimalsToExport = exportDecimals;
            state.FinalFigureWidthCentimeters = figureWidth;
            state.FinalFigureHeightCentimeters = figureHeight;
            state.ExportBaselineCorrectedData = IsOn(ExportCorrectedCheck);
            state.ExportFitPointsWithPeaks = IsOn(ExportFitPointsCheck);
            state.ExportColumns = BuildExportColumns();
            state.ShowResidualGraph = IsOn(ShowResidualCheck);
            state.ShowResidualGraphGap = IsOn(ResidualGapCheck);
            state.UnifyResidualGraphAxis = IsOn(UnifyResidualAxisCheck);
            state.FinalFigureShowParameterBoxAsDefault = IsOn(ParameterBoxCheck);
            state.FinalFigureShowDetailsAsDefault = IsOn(ExperimentDetailsCheck);
            state.FinalFigureShowModelInfoAsDefault = IsOn(ModelInfoCheck);
            state.AutoAxesIgnoresBadData = IsOn(AutoAxesCheck);
            state.FinalFigureParameterDisplay = BuildFigureDisplay();
            error = null;
            return true;
        }

        partial void ResidualVisibilityChanged(NSObject sender) => UpdateResidualControls();

        void UpdateResidualControls()
        {
            if (ShowResidualCheck == null) return;
            var enabled = IsOn(ShowResidualCheck);
            ResidualGapCheck.Enabled = enabled;
            UnifyResidualAxisCheck.Enabled = enabled;
        }

        ExportColumns BuildExportColumns()
        {
            var columns = ExportColumns.None;
            if (IsOn(ExportMolarRatioCheck)) columns |= ExportColumns.MolarRatio;
            if (IsOn(ExportInjectionInfoCheck)) columns |= ExportColumns.InjectionInfo;
            if (IsOn(ExportConcentrationsCheck)) columns |= ExportColumns.Concentrations;
            if (IsOn(ExportIncludedCheck)) columns |= ExportColumns.Included;
            if (IsOn(ExportPeakCheck)) columns |= ExportColumns.Peak;
            if (IsOn(ExportFitCheck)) columns |= ExportColumns.Fit;
            return columns;
        }

        FinalFigureDisplayParameters BuildFigureDisplay()
        {
            var display = FinalFigureDisplayParameters.None;
            if (IsOn(ModelInfoCheck)) display |= FinalFigureDisplayParameters.Model;
            if (IsOn(ThermodynamicCheck)) display |= FinalFigureDisplayParameters.Thermodynamic;
            if (IsOn(OffsetCheck)) display |= FinalFigureDisplayParameters.Offset;
            if (IsOn(DerivedCheck)) display |= FinalFigureDisplayParameters.Derived;
            if (IsOn(TemperatureCheck)) display |= FinalFigureDisplayParameters.Temperature;
            if (IsOn(ConcentrationsCheck)) display |= FinalFigureDisplayParameters.Concentrations;
            if (IsOn(InjectionDelayCheck)) display |= FinalFigureDisplayParameters.InjectionDelay;
            if (IsOn(InstrumentInfoCheck)) display |= FinalFigureDisplayParameters.Instrument;
            if (IsOn(AttributesCheck)) display |= FinalFigureDisplayParameters.Attributes;
            return display;
        }

        static DisplayAttributeOptions NormalizeAttributeOptions(DisplayAttributeOptions options)
        {
            if (options == DisplayAttributeOptions.All || options == DisplayAttributeOptions.None) return options;
            return DisplayAttributeOptions.UsedInAnalysis;
        }
    }
}
