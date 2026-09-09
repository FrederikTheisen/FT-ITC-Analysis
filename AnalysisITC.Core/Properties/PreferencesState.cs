using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Application
{
    /// <summary>
    /// A detached preferences snapshot shared by the desktop dialogs and application defaults.
    /// Changes are staged until Apply is called.
    /// </summary>
    public sealed class PreferencesState
    {
        public static IReadOnlyList<int> OptimizerIterationPresets { get; } = Array.AsReadOnly(new[]
        {
            1, 10, 100, 1_000, 5_000, 10_000, 20_000, 30_000
        });

        public double ReferenceTemperature { get; set; } = 25;
        public EnergyUnitFamily EnergyUnitFamily { get; set; } = EnergyUnitFamily.Joules;
        public EnergyUnit EnergyUnit { get; set; } = EnergyUnit.KiloJoule;
        public ColorSchemes ColorScheme { get; set; } = ColorSchemes.Default;
        public ColorSchemeGradientMode ColorSchemeGradientMode { get; set; } = ColorSchemeGradientMode.Smooth;
        public ConcentrationUnit DefaultConcentrationUnit { get; set; } = ConcentrationUnit.µM;
        public ITCInstrument DefaultDesignerInstrument { get; set; } = ITCInstrument.MicroCalITC200;
        public bool PerformOnlineChecksOnLaunch { get; set; } = true;
        public string InterpretationOperatorCode { get; set; } = "";
        public bool UseInterpretationEvaluationSettings { get; set; }
        public string InterpretationEvaluationModel { get; set; } = "";
        public string InterpretationEvaluationReasoningEffort { get; set; } = "";
        public string InterpretationGenerationPreset { get; set; } = "instant";
        public bool InterpretationAccessVerified { get; set; }
        public string InterpretationAccessCodeHash { get; set; } = "";
        public string InterpretationAccessOptionsJson { get; set; } = "";

        public bool TryGetInterpretationAccessOptions(out Interpretation.InterpretationOperatorOptionsResponse options)
        {
            options = null;
            if (!InterpretationAccessVerified || string.IsNullOrWhiteSpace(InterpretationAccessCodeHash)
                || !string.Equals(InterpretationAccessCodeHash, AppSettings.InterpretationAccessHash(InterpretationOperatorCode), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(InterpretationAccessOptionsJson)) return false;
            try
            {
                options = JsonSerializer.Deserialize<Interpretation.InterpretationOperatorOptionsResponse>(InterpretationAccessOptionsJson);
                return options != null && (options.Mode == "custom"
                    ? options.Models != null && options.Models.Count > 0 && options.Models.All(model => model != null && !string.IsNullOrWhiteSpace(model.Id) && model.ReasoningEfforts != null)
                    : options.Mode == "presets" && options.Presets != null && options.Presets.Count > 0);
            }
            catch (JsonException) { return false; }
        }
        public bool ConfirmRemoveDelete { get; set; } = true;
        public bool AutoSaveEnabled { get; set; } = true;
        public int AutoSaveIntervalMinutes { get; set; } = 5;
        public int AutoSaveFileLimit { get; set; } = 10;
        public bool PromptForAutoSaveRecovery { get; set; } = true;
        public bool AutomaticallyDiscardOrphanInjectionsOnLoad { get; set; } = true;
        public bool DiscardIntegrationRegionForBaseline { get; set; } = true;
        public bool IncludeBufferInIonicStrengthCalc { get; set; } = true;
        public DilutionMethod DilutionCalculationMethod { get; set; } = DilutionMethod.MicroCal;
        public BufferSubtractionMethod BufferSubtractionDefaultMethod { get; set; } = BufferSubtractionMethod.MatchedInjection;
        public bool ReprocessIntegratedHeatDataOnLoad { get; set; } = true;
        public SplineInterpolator.SplinePointDensity DefaultSplinePointDensity { get; set; } = SplineInterpolator.SplinePointDensity.Balanced;
        public SplineInterpolator.SplineHandleMode DefaultSplineHandleMode { get; set; } = SplineInterpolator.SplineHandleMode.Mean;
        public bool DefaultSplinePointTimeDragging { get; set; } = false;
        public bool IntegrationRegionCopyIncludesStart { get; set; } = false;
        public bool InputAffinityAsDissociationConstant { get; set; } = true;
        public ErrorEstimationMethod DefaultErrorEstimationMethod { get; set; } = ErrorEstimationMethod.BootstrapResiduals;
        public int DefaultBootstrapIterations { get; set; } = 100;
        public double MinimumTemperatureSpanForFitting { get; set; } = 3;
        public double MinimumIonSpanForFitting { get; set; } = 0.03;
        public bool IncludeConcentrationErrorsInBootstrap { get; set; } = false;
        public double ConcentrationAutoVariance { get; set; } = 0.1;
        public double OptimizerTolerance { get; set; } = 0.5;
        public int MaximumOptimizerIterations { get; set; } = 20_000;
        public ParameterLimitSetting ParameterLimitSetting { get; set; } = ParameterLimitSetting.Standard;
        public SolverAlgorithm DefaultSolverAlgorithm { get; set; } = SolverAlgorithm.NelderMead;
        public bool UseInjectionErrorWeightedFitting { get; set; } = false;
        public bool BuffersPreparedAtRoomTemperature { get; set; } = true;
        public bool CreateSingleAnalysisResult { get; set; } = false;
        public bool CreateGlobalAnalysisResult { get; set; } = true;
        public bool AutoOpenNewAnalysisResult { get; set; } = true;
        public bool RememberResultTableColumnWidthsForSession { get; set; } = false;
        public FinalFigureDisplayParameters AnalysisParameterDisplay { get; set; } = FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Fitted | FinalFigureDisplayParameters.Derived;
        public bool UseLargeAnalysisParameterText { get; set; } = false;
        public bool AutoSelectReportReferenceExperiments { get; set; } = true;
        public PublicationFont PublicationFigureFont { get; set; } = PublicationFont.Native;
        public FinalFigureDisplayParameters FinalFigureParameterDisplay { get; set; } = FinalFigureDisplayParameters.Default;
        public DisplayAttributeOptions DisplayAttributeOptions { get; set; } = DisplayAttributeOptions.Default;
        public bool FinalFigureShowParameterBoxAsDefault { get; set; } = true;
        public bool FinalFigureShowDetailsAsDefault { get; set; } = true;
        public bool FinalFigureShowModelInfoAsDefault { get; set; } = true;
        public NumberPrecision NumberPrecision { get; set; } = NumberPrecision.Standard;
        public UncertaintyDisplayStyle UncertaintyDisplayStyle { get; set; } = UncertaintyDisplayStyle.StandardDeviation;
        public bool ShowResidualGraph { get; set; } = true;
        public bool ShowResidualGraphGap { get; set; } = true;
        public bool UnifyResidualGraphAxis { get; set; } = false;
        public LineSmoothness FitLineSmoothness { get; set; } = LineSmoothness.Spline;
        public bool AutoAxesIgnoresBadData { get; set; } = true;
        public bool UnifyTimeAxisForExport { get; set; } = true;
        public bool ExportBaselineCorrectedData { get; set; } = true;
        public bool ExportFitPointsWithPeaks { get; set; } = true;
        public ExportDataSelection ExportSelectionMode { get; set; } = ExportDataSelection.IncludedData;
        public int NumOfDecimalsToExport { get; set; } = 1;
        public ExportColumns ExportColumns { get; set; } = ExportColumns.Default;
        public ExportType DefaultExportType { get; set; } = ExportType.InterchangeCsv;
        public string ExportOutputBaseName { get; set; } = "FT-ITC Export";
        public double FinalFigureWidthCentimeters { get; set; } = 6.5;
        public double FinalFigureHeightCentimeters { get; set; } = 10;

        public static PreferencesState Defaults() => new PreferencesState();

        public static PreferencesState FromSettings()
        {
            var state = new PreferencesState
            {
                ReferenceTemperature = AppSettings.ReferenceTemperature,
                EnergyUnitFamily = AppSettings.EnergyUnitFamily,
                EnergyUnit = AppSettings.EnergyUnit,
                ColorScheme = AppSettings.ColorScheme,
                ColorSchemeGradientMode = AppSettings.ColorSchemeGradientMode,
                DefaultConcentrationUnit = AppSettings.DefaultConcentrationUnit,
                DefaultDesignerInstrument = AppSettings.DefaultDesignerInstrument,
                PerformOnlineChecksOnLaunch = AppSettings.PerformOnlineChecksOnLaunch,
                InterpretationOperatorCode = AppSettings.InterpretationOperatorCode,
                UseInterpretationEvaluationSettings = AppSettings.UseInterpretationEvaluationSettings,
                InterpretationEvaluationModel = AppSettings.InterpretationEvaluationModel,
                InterpretationEvaluationReasoningEffort = AppSettings.InterpretationEvaluationReasoningEffort,
                InterpretationGenerationPreset = AppSettings.InterpretationGenerationPreset,
                InterpretationAccessVerified = AppSettings.InterpretationAccessVerified,
                InterpretationAccessCodeHash = AppSettings.InterpretationAccessCodeHash,
                InterpretationAccessOptionsJson = AppSettings.InterpretationAccessOptionsJson,
                ConfirmRemoveDelete = AppSettings.ConfirmRemoveDelete,
                AutoSaveEnabled = AppSettings.AutoSaveEnabled,
                AutoSaveIntervalMinutes = AppSettings.AutoSaveIntervalMinutes,
                AutoSaveFileLimit = AppSettings.AutoSaveFileLimit,
                PromptForAutoSaveRecovery = AppSettings.PromptForAutoSaveRecovery,
                AutomaticallyDiscardOrphanInjectionsOnLoad = AppSettings.AutomaticallyDiscardOrphanInjectionsOnLoad,
                DiscardIntegrationRegionForBaseline = AppSettings.DiscardIntegrationRegionForBaseline,
                IncludeBufferInIonicStrengthCalc = AppSettings.IncludeBufferInIonicStrengthCalc,
                DilutionCalculationMethod = AppSettings.DilutionCalculationMethod,
                BufferSubtractionDefaultMethod = AppSettings.BufferSubtractionDefaultMethod,
                ReprocessIntegratedHeatDataOnLoad = AppSettings.ReprocessIntegratedHeatDataOnLoad,
                DefaultSplinePointDensity = AppSettings.DefaultSplinePointDensity,
                DefaultSplineHandleMode = AppSettings.DefaultSplineHandleMode,
                DefaultSplinePointTimeDragging = AppSettings.DefaultSplinePointTimeDragging,
                IntegrationRegionCopyIncludesStart = AppSettings.IntegrationRegionCopyIncludesStart,
                InputAffinityAsDissociationConstant = AppSettings.InputAffinityAsDissociationConstant,
                DefaultErrorEstimationMethod = AppSettings.DefaultErrorEstimationMethod,
                DefaultBootstrapIterations = AppSettings.DefaultBootstrapIterations,
                MinimumTemperatureSpanForFitting = AppSettings.MinimumTemperatureSpanForFitting,
                MinimumIonSpanForFitting = AppSettings.MinimumIonSpanForFitting,
                IncludeConcentrationErrorsInBootstrap = AppSettings.IncludeConcentrationErrorsInBootstrap,
                ConcentrationAutoVariance = AppSettings.ConcentrationAutoVariance,
                OptimizerTolerance = AppSettings.OptimizerTolerance,
                MaximumOptimizerIterations = AppSettings.MaximumOptimizerIterations,
                ParameterLimitSetting = AppSettings.ParameterLimitSetting,
                DefaultSolverAlgorithm = AppSettings.DefaultSolverAlgorithm,
                UseInjectionErrorWeightedFitting = AppSettings.UseInjectionErrorWeightedFitting,
                BuffersPreparedAtRoomTemperature = AppSettings.BuffersPreparedAtRoomTemperature,
                CreateSingleAnalysisResult = AppSettings.CreateSingleAnalysisResult,
                CreateGlobalAnalysisResult = AppSettings.CreateGlobalAnalysisResult,
                AutoOpenNewAnalysisResult = AppSettings.AutoOpenNewAnalysisResult,
                RememberResultTableColumnWidthsForSession = AppSettings.RememberResultTableColumnWidthsForSession,
                AnalysisParameterDisplay = AppSettings.AnalysisParameterDisplay,
                UseLargeAnalysisParameterText = AppSettings.UseLargeAnalysisParameterText,
                AutoSelectReportReferenceExperiments = AppSettings.AutoSelectReportReferenceExperiments,
                PublicationFigureFont = AppSettings.PublicationFigureFont,
                FinalFigureParameterDisplay = AppSettings.FinalFigureParameterDisplay,
                DisplayAttributeOptions = AppSettings.DisplayAttributeOptions,
                FinalFigureShowParameterBoxAsDefault = AppSettings.FinalFigureShowParameterBoxAsDefault,
                FinalFigureShowDetailsAsDefault = AppSettings.FinalFigureShowDetailsAsDefault,
                FinalFigureShowModelInfoAsDefault = AppSettings.FinalFigureShowModelInfoAsDefault,
                NumberPrecision = AppSettings.NumberPrecision,
                UncertaintyDisplayStyle = AppSettings.UncertaintyDisplayStyle,
                ShowResidualGraph = AppSettings.ShowResidualGraph,
                ShowResidualGraphGap = AppSettings.ShowResidualGraphGap,
                UnifyResidualGraphAxis = AppSettings.UnifyResidualGraphAxis,
                FitLineSmoothness = AppSettings.FitLineSmoothness,
                AutoAxesIgnoresBadData = AppSettings.AutoAxesIgnoresBadData,
                UnifyTimeAxisForExport = AppSettings.UnifyTimeAxisForExport,
                ExportBaselineCorrectedData = AppSettings.ExportBaselineCorrectedData,
                ExportFitPointsWithPeaks = AppSettings.ExportFitPointsWithPeaks,
                ExportSelectionMode = AppSettings.ExportSelectionMode,
                NumOfDecimalsToExport = AppSettings.NumOfDecimalsToExport,
                ExportColumns = AppSettings.ExportColumns,
                DefaultExportType = AppSettings.DefaultExportType,
                ExportOutputBaseName = AppSettings.ExportOutputBaseName,
            };
            var dimensions = AppSettings.FinalFigureDimensions;
            if (dimensions != null && dimensions.Length > 0) state.FinalFigureWidthCentimeters = dimensions[0];
            if (dimensions != null && dimensions.Length > 1) state.FinalFigureHeightCentimeters = dimensions[1];
            return state;
        }

        public void Apply()
        {
            ApplyToSettings();
            AppSettings.Save();
        }

        internal void ApplyToSettings()
        {
            AppSettings.ReferenceTemperature = ReferenceTemperature;
            AppSettings.EnergyUnitFamily = EnergyUnitFamily;
            AppSettings.EnergyUnit = EnergyUnit;
            AppSettings.ColorScheme = ColorScheme;
            AppSettings.ColorSchemeGradientMode = ColorSchemeGradientMode;
            AppSettings.DefaultConcentrationUnit = DefaultConcentrationUnit;
            AppSettings.DefaultDesignerInstrument = DefaultDesignerInstrument;
            AppSettings.PerformOnlineChecksOnLaunch = PerformOnlineChecksOnLaunch;
            AppSettings.InterpretationOperatorCode = InterpretationOperatorCode ?? "";
            AppSettings.UseInterpretationEvaluationSettings = UseInterpretationEvaluationSettings;
            AppSettings.InterpretationEvaluationModel = InterpretationEvaluationModel ?? "";
            AppSettings.InterpretationEvaluationReasoningEffort = InterpretationEvaluationReasoningEffort ?? "";
            AppSettings.InterpretationGenerationPreset = InterpretationGenerationPreset ?? "instant";
            AppSettings.InterpretationAccessVerified = InterpretationAccessVerified;
            AppSettings.InterpretationAccessCodeHash = InterpretationAccessCodeHash ?? "";
            AppSettings.InterpretationAccessOptionsJson = InterpretationAccessOptionsJson ?? "";
            AppSettings.ConfirmRemoveDelete = ConfirmRemoveDelete;
            AppSettings.AutoSaveEnabled = AutoSaveEnabled;
            AppSettings.AutoSaveIntervalMinutes = AutoSaveIntervalMinutes;
            AppSettings.AutoSaveFileLimit = AutoSaveFileLimit;
            AppSettings.PromptForAutoSaveRecovery = PromptForAutoSaveRecovery;
            AppSettings.AutomaticallyDiscardOrphanInjectionsOnLoad = AutomaticallyDiscardOrphanInjectionsOnLoad;
            AppSettings.DiscardIntegrationRegionForBaseline = DiscardIntegrationRegionForBaseline;
            AppSettings.IncludeBufferInIonicStrengthCalc = IncludeBufferInIonicStrengthCalc;
            AppSettings.DilutionCalculationMethod = DilutionCalculationMethod;
            AppSettings.BufferSubtractionDefaultMethod = BufferSubtractionDefaultMethod;
            AppSettings.ReprocessIntegratedHeatDataOnLoad = ReprocessIntegratedHeatDataOnLoad;
            AppSettings.DefaultSplinePointDensity = DefaultSplinePointDensity;
            AppSettings.DefaultSplineHandleMode = DefaultSplineHandleMode;
            AppSettings.DefaultSplinePointTimeDragging = DefaultSplinePointTimeDragging;
            AppSettings.IntegrationRegionCopyIncludesStart = IntegrationRegionCopyIncludesStart;
            AppSettings.InputAffinityAsDissociationConstant = InputAffinityAsDissociationConstant;
            AppSettings.DefaultErrorEstimationMethod = DefaultErrorEstimationMethod;
            AppSettings.DefaultBootstrapIterations = DefaultBootstrapIterations;
            AppSettings.MinimumTemperatureSpanForFitting = MinimumTemperatureSpanForFitting;
            AppSettings.MinimumIonSpanForFitting = MinimumIonSpanForFitting;
            AppSettings.IncludeConcentrationErrorsInBootstrap = IncludeConcentrationErrorsInBootstrap;
            AppSettings.ConcentrationAutoVariance = ConcentrationAutoVariance;
            AppSettings.OptimizerTolerance = OptimizerTolerance;
            AppSettings.MaximumOptimizerIterations = MaximumOptimizerIterations;
            AppSettings.ParameterLimitSetting = ParameterLimitSetting;
            AppSettings.DefaultSolverAlgorithm = DefaultSolverAlgorithm;
            AppSettings.UseInjectionErrorWeightedFitting = UseInjectionErrorWeightedFitting;
            AppSettings.BuffersPreparedAtRoomTemperature = BuffersPreparedAtRoomTemperature;
            AppSettings.CreateSingleAnalysisResult = CreateSingleAnalysisResult;
            AppSettings.CreateGlobalAnalysisResult = CreateGlobalAnalysisResult;
            AppSettings.AutoOpenNewAnalysisResult = AutoOpenNewAnalysisResult;
            AppSettings.RememberResultTableColumnWidthsForSession = RememberResultTableColumnWidthsForSession;
            AppSettings.AnalysisParameterDisplay = AnalysisParameterDisplay;
            AppSettings.UseLargeAnalysisParameterText = UseLargeAnalysisParameterText;
            AppSettings.AutoSelectReportReferenceExperiments = AutoSelectReportReferenceExperiments;
            AppSettings.PublicationFigureFont = PublicationFigureFont;
            AppSettings.FinalFigureParameterDisplay = FinalFigureParameterDisplay;
            AppSettings.DisplayAttributeOptions = DisplayAttributeOptions;
            AppSettings.FinalFigureShowParameterBoxAsDefault = FinalFigureShowParameterBoxAsDefault;
            AppSettings.FinalFigureShowDetailsAsDefault = FinalFigureShowDetailsAsDefault;
            AppSettings.FinalFigureShowModelInfoAsDefault = FinalFigureShowModelInfoAsDefault;
            AppSettings.NumberPrecision = NumberPrecision;
            AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle;
            AppSettings.ShowResidualGraph = ShowResidualGraph;
            AppSettings.ShowResidualGraphGap = ShowResidualGraphGap;
            AppSettings.UnifyResidualGraphAxis = UnifyResidualGraphAxis;
            AppSettings.FitLineSmoothness = FitLineSmoothness;
            AppSettings.AutoAxesIgnoresBadData = AutoAxesIgnoresBadData;
            AppSettings.UnifyTimeAxisForExport = UnifyTimeAxisForExport;
            AppSettings.ExportBaselineCorrectedData = ExportBaselineCorrectedData;
            AppSettings.ExportFitPointsWithPeaks = ExportFitPointsWithPeaks;
            AppSettings.ExportSelectionMode = ExportSelectionMode;
            AppSettings.NumOfDecimalsToExport = NumOfDecimalsToExport;
            AppSettings.ExportColumns = ExportColumns;
            AppSettings.DefaultExportType = DefaultExportType;
            AppSettings.ExportOutputBaseName = ExportOutputBaseName;
            AppSettings.FinalFigureDimensions = new[] { FinalFigureWidthCentimeters, FinalFigureHeightCentimeters };
            AppSettings.UpdateDerivedSettings();
        }
    }
}
