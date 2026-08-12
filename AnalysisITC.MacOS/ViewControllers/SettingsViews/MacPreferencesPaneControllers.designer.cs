// WARNING
//
// This file stores the outlets and actions defined by Preferences.storyboard.
// Changes should remain synchronized with the storyboard connections.
//
using Foundation;

namespace AnalysisITC
{
    [Register("MacGeneralPreferencesViewController")]
    partial class MacGeneralPreferencesViewController
    {
        [Outlet] AppKit.NSPopUpButton EnergyUnitPopup { get; set; }
        [Outlet] AppKit.NSPopUpButton ConcentrationUnitPopup { get; set; }
        [Outlet] AppKit.NSPopUpButton NumberPrecisionPopup { get; set; }
        [Outlet] AppKit.NSPopUpButton UncertaintyPopup { get; set; }
        [Outlet] AppKit.NSPopUpButton InstrumentPopup { get; set; }
        [Outlet] AppKit.NSTextField ReferenceTemperatureField { get; set; }
        [Outlet] AppKit.NSTextField MinimumTemperatureSpanField { get; set; }
        [Outlet] AppKit.NSTextField MinimumIonSpanField { get; set; }
        [Outlet] AppKit.NSButton IncludeBufferCheck { get; set; }
        [Outlet] AppKit.NSButton OnlineChecksCheck { get; set; }
        [Outlet] AppKit.NSButton ConfirmDeleteCheck { get; set; }
        [Outlet] AppKit.NSButton DiscardOrphanCheck { get; set; }
        [Outlet] AppKit.NSButton AutoSaveEnabledCheck { get; set; }
        [Outlet] AppKit.NSTextField AutoSaveIntervalField { get; set; }
        [Outlet] AppKit.NSTextField AutoSaveLimitField { get; set; }
        [Outlet] AppKit.NSButton RecoveryPromptCheck { get; set; }

        [Action("autoSaveEnabledChanged:")]
        partial void AutoSaveEnabledChanged(NSObject sender);

        [Action("openAutoSaveFolder:")]
        partial void OpenAutoSaveFolder(NSObject sender);

        void ReleaseDesignerOutlets()
        {
            EnergyUnitPopup = Release(EnergyUnitPopup);
            ConcentrationUnitPopup = Release(ConcentrationUnitPopup);
            NumberPrecisionPopup = Release(NumberPrecisionPopup);
            UncertaintyPopup = Release(UncertaintyPopup);
            InstrumentPopup = Release(InstrumentPopup);
            ReferenceTemperatureField = Release(ReferenceTemperatureField);
            MinimumTemperatureSpanField = Release(MinimumTemperatureSpanField);
            MinimumIonSpanField = Release(MinimumIonSpanField);
            IncludeBufferCheck = Release(IncludeBufferCheck);
            OnlineChecksCheck = Release(OnlineChecksCheck);
            ConfirmDeleteCheck = Release(ConfirmDeleteCheck);
            DiscardOrphanCheck = Release(DiscardOrphanCheck);
            AutoSaveEnabledCheck = Release(AutoSaveEnabledCheck);
            AutoSaveIntervalField = Release(AutoSaveIntervalField);
            AutoSaveLimitField = Release(AutoSaveLimitField);
            RecoveryPromptCheck = Release(RecoveryPromptCheck);
        }

        static T Release<T>(T outlet) where T : Foundation.NSObject
        {
            outlet?.Dispose();
            return null;
        }
    }

    [Register("MacProcessingPreferencesViewController")]
    partial class MacProcessingPreferencesViewController
    {
        [Outlet] AppKit.NSPopUpButton DilutionPopup { get; set; }
        [Outlet] AppKit.NSPopUpButton BufferSubtractionPopup { get; set; }
        [Outlet] AppKit.NSPopUpButton SplineDensityPopup { get; set; }
        [Outlet] AppKit.NSPopUpButton SplineHandlePopup { get; set; }
        [Outlet] AppKit.NSButton DiscardIntegrationCheck { get; set; }
        [Outlet] AppKit.NSButton ReprocessIntegratedCheck { get; set; }
        [Outlet] AppKit.NSButton SplineTimeDraggingCheck { get; set; }
        [Outlet] AppKit.NSButton CopyIntegrationStartCheck { get; set; }

        void ReleaseDesignerOutlets()
        {
            DilutionPopup = Release(DilutionPopup);
            BufferSubtractionPopup = Release(BufferSubtractionPopup);
            SplineDensityPopup = Release(SplineDensityPopup);
            SplineHandlePopup = Release(SplineHandlePopup);
            DiscardIntegrationCheck = Release(DiscardIntegrationCheck);
            ReprocessIntegratedCheck = Release(ReprocessIntegratedCheck);
            SplineTimeDraggingCheck = Release(SplineTimeDraggingCheck);
            CopyIntegrationStartCheck = Release(CopyIntegrationStartCheck);
        }

        static T Release<T>(T outlet) where T : Foundation.NSObject
        {
            outlet?.Dispose();
            return null;
        }
    }

    [Register("MacFittingPreferencesViewController")]
    partial class MacFittingPreferencesViewController
    {
        [Outlet] AppKit.NSPopUpButton SolverPopup { get; set; }
        [Outlet] AppKit.NSPopUpButton ErrorMethodPopup { get; set; }
        [Outlet] AppKit.NSPopUpButton ParameterLimitPopup { get; set; }
        [Outlet] AppKit.NSTextField BootstrapIterationsField { get; set; }
        [Outlet] AppKit.NSTextField OptimizerToleranceField { get; set; }
        [Outlet] AppKit.NSTextField MaximumIterationsField { get; set; }
        [Outlet] AppKit.NSTextField ConcentrationVarianceField { get; set; }
        [Outlet] AppKit.NSButton ConcentrationBootstrapCheck { get; set; }
        [Outlet] AppKit.NSButton WeightedFittingCheck { get; set; }
        [Outlet] AppKit.NSButton CreateSingleResultCheck { get; set; }
        [Outlet] AppKit.NSButton CreateGlobalResultCheck { get; set; }
        [Outlet] AppKit.NSButton AutoOpenResultCheck { get; set; }

        void ReleaseDesignerOutlets()
        {
            SolverPopup = Release(SolverPopup);
            ErrorMethodPopup = Release(ErrorMethodPopup);
            ParameterLimitPopup = Release(ParameterLimitPopup);
            BootstrapIterationsField = Release(BootstrapIterationsField);
            OptimizerToleranceField = Release(OptimizerToleranceField);
            MaximumIterationsField = Release(MaximumIterationsField);
            ConcentrationVarianceField = Release(ConcentrationVarianceField);
            ConcentrationBootstrapCheck = Release(ConcentrationBootstrapCheck);
            WeightedFittingCheck = Release(WeightedFittingCheck);
            CreateSingleResultCheck = Release(CreateSingleResultCheck);
            CreateGlobalResultCheck = Release(CreateGlobalResultCheck);
            AutoOpenResultCheck = Release(AutoOpenResultCheck);
        }

        static T Release<T>(T outlet) where T : Foundation.NSObject
        {
            outlet?.Dispose();
            return null;
        }
    }

    [Register("MacExportPreferencesViewController")]
    partial class MacExportPreferencesViewController
    {
        [Outlet] AppKit.NSPopUpButton ExportSelectionPopup { get; set; }
        [Outlet] AppKit.NSPopUpButton FitLinePopup { get; set; }
        [Outlet] AppKit.NSPopUpButton AttributeDisplayPopup { get; set; }
        [Outlet] AppKit.NSTextField ExportDecimalsField { get; set; }
        [Outlet] AppKit.NSTextField FigureWidthField { get; set; }
        [Outlet] AppKit.NSTextField FigureHeightField { get; set; }
        [Outlet] AppKit.NSButton ExportCorrectedCheck { get; set; }
        [Outlet] AppKit.NSButton ExportFitPointsCheck { get; set; }
        [Outlet] AppKit.NSButton ExportMolarRatioCheck { get; set; }
        [Outlet] AppKit.NSButton ExportInjectionInfoCheck { get; set; }
        [Outlet] AppKit.NSButton ExportConcentrationsCheck { get; set; }
        [Outlet] AppKit.NSButton ExportIncludedCheck { get; set; }
        [Outlet] AppKit.NSButton ExportPeakCheck { get; set; }
        [Outlet] AppKit.NSButton ExportFitCheck { get; set; }
        [Outlet] AppKit.NSButton ShowResidualCheck { get; set; }
        [Outlet] AppKit.NSButton ResidualGapCheck { get; set; }
        [Outlet] AppKit.NSButton UnifyResidualAxisCheck { get; set; }
        [Outlet] AppKit.NSButton ParameterBoxCheck { get; set; }
        [Outlet] AppKit.NSButton ExperimentDetailsCheck { get; set; }
        [Outlet] AppKit.NSButton ModelInfoCheck { get; set; }
        [Outlet] AppKit.NSButton AutoAxesCheck { get; set; }
        [Outlet] AppKit.NSButton ThermodynamicCheck { get; set; }
        [Outlet] AppKit.NSButton OffsetCheck { get; set; }
        [Outlet] AppKit.NSButton DerivedCheck { get; set; }
        [Outlet] AppKit.NSButton TemperatureCheck { get; set; }
        [Outlet] AppKit.NSButton ConcentrationsCheck { get; set; }
        [Outlet] AppKit.NSButton InjectionDelayCheck { get; set; }
        [Outlet] AppKit.NSButton InstrumentInfoCheck { get; set; }
        [Outlet] AppKit.NSButton AttributesCheck { get; set; }

        [Action("residualVisibilityChanged:")]
        partial void ResidualVisibilityChanged(NSObject sender);

        void ReleaseDesignerOutlets()
        {
            ExportSelectionPopup = Release(ExportSelectionPopup);
            FitLinePopup = Release(FitLinePopup);
            AttributeDisplayPopup = Release(AttributeDisplayPopup);
            ExportDecimalsField = Release(ExportDecimalsField);
            FigureWidthField = Release(FigureWidthField);
            FigureHeightField = Release(FigureHeightField);
            ExportCorrectedCheck = Release(ExportCorrectedCheck);
            ExportFitPointsCheck = Release(ExportFitPointsCheck);
            ExportMolarRatioCheck = Release(ExportMolarRatioCheck);
            ExportInjectionInfoCheck = Release(ExportInjectionInfoCheck);
            ExportConcentrationsCheck = Release(ExportConcentrationsCheck);
            ExportIncludedCheck = Release(ExportIncludedCheck);
            ExportPeakCheck = Release(ExportPeakCheck);
            ExportFitCheck = Release(ExportFitCheck);
            ShowResidualCheck = Release(ShowResidualCheck);
            ResidualGapCheck = Release(ResidualGapCheck);
            UnifyResidualAxisCheck = Release(UnifyResidualAxisCheck);
            ParameterBoxCheck = Release(ParameterBoxCheck);
            ExperimentDetailsCheck = Release(ExperimentDetailsCheck);
            ModelInfoCheck = Release(ModelInfoCheck);
            AutoAxesCheck = Release(AutoAxesCheck);
            ThermodynamicCheck = Release(ThermodynamicCheck);
            OffsetCheck = Release(OffsetCheck);
            DerivedCheck = Release(DerivedCheck);
            TemperatureCheck = Release(TemperatureCheck);
            ConcentrationsCheck = Release(ConcentrationsCheck);
            InjectionDelayCheck = Release(InjectionDelayCheck);
            InstrumentInfoCheck = Release(InstrumentInfoCheck);
            AttributesCheck = Release(AttributesCheck);
        }

        static T Release<T>(T outlet) where T : Foundation.NSObject
        {
            outlet?.Dispose();
            return null;
        }
    }
}
