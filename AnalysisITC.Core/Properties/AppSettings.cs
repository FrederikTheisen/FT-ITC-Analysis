using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnalysisITC.Core.Interpretation;

using AnalysisITC.Platform;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Core.Application
{
    public static class AppSettings
    {
        public static event EventHandler SettingsDidUpdate;
        public static event EventHandler SettingsApplied;

        static AppSettings() => PreferencesState.Defaults().ApplyToSettings();

        static ISettingsStore Storage => PlatformServices.SettingsStore;
        public static string Locale { get; set; } = "en-US";

        //General
        public static double ReferenceTemperature { get; set; }
        /// <summary>
        /// The preferred automatic display family.  Exact units remain available
        /// to import and export APIs through <see cref="EnergyUnit"/>.
        /// </summary>
        public static EnergyUnitFamily EnergyUnitFamily { get; set; }

        /// <summary>
        /// Legacy in-memory exact preference retained for source compatibility.
        /// It is no longer persisted or used by the automatic presentation APIs.
        /// </summary>
        public static EnergyUnit EnergyUnit { get; set; }
        public static ColorSchemes ColorScheme { get; set; }
        public static ColorSchemeGradientMode ColorSchemeGradientMode { get; set; }
        public static ConcentrationUnit DefaultConcentrationUnit { get; set; }
        public static ITCInstrument DefaultDesignerInstrument { get; set; }
        public static int MaxDegreeOfParallelism { get; set; } = 10;
        public static bool PerformOnlineChecksOnLaunch { get; set; }
        public static string InterpretationOperatorCode { get; set; } = "";
        public static bool UseInterpretationEvaluationSettings { get; set; }
        public static string InterpretationEvaluationModel { get; set; } = "";
        public static string InterpretationEvaluationReasoningEffort { get; set; } = "";
        public static string InterpretationGenerationPreset { get; set; } = "instant";
        public static bool InterpretationAccessVerified { get; set; }
        public static string InterpretationAccessCodeHash { get; set; } = "";
        public static string InterpretationAccessOptionsJson { get; set; } = "";
        /// <summary>Access tier from the last locally verified interpretation code.</summary>
        public static string InterpretationAccessTier { get; set; } = "";

        public static string InterpretationAccessHash(string operatorCode)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(operatorCode ?? ""))).Replace("-", "").ToLowerInvariant();
        }

        public static bool TryGetInterpretationAccessOptions(string operatorCode, out InterpretationOperatorOptionsResponse options)
        {
            options = null;
            if (!InterpretationAccessVerified || !string.Equals(InterpretationAccessCodeHash, InterpretationAccessHash(operatorCode), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(InterpretationAccessOptionsJson)) return false;
            try
            {
                options = JsonSerializer.Deserialize<InterpretationOperatorOptionsResponse>(InterpretationAccessOptionsJson);
                var valid = options != null && (options.Mode == "custom"
                    ? options.Models != null && options.Models.Count > 0 && options.Models.All(model => model != null && !string.IsNullOrWhiteSpace(model.Id) && model.ReasoningEfforts != null)
                    : options.Mode == "presets" && options.Presets != null && options.Presets.Count > 0);
                if (valid && string.IsNullOrWhiteSpace(InterpretationAccessTier))
                    InterpretationAccessTier = options.AccessTier ?? "";
                return valid;
            }
            catch (JsonException) { return false; }
        }

        public static void CacheInterpretationAccess(string operatorCode, InterpretationOperatorOptionsResponse options)
        {
            if (options == null) { ClearInterpretationAccessVerification(); return; }
            InterpretationAccessCodeHash = InterpretationAccessHash(operatorCode);
            InterpretationAccessOptionsJson = JsonSerializer.Serialize(options);
            InterpretationAccessTier = options.AccessTier ?? "";
            InterpretationAccessVerified = true;
        }

        /// <summary>
        /// Persist a successful access verification independently of the staged
        /// preferences dialog. The generation request still verifies the code
        /// with the relay before using it.
        /// </summary>
        public static void PersistInterpretationAccessVerification(string operatorCode, InterpretationOperatorOptionsResponse options)
        {
            InterpretationOperatorCode = operatorCode ?? "";
            CacheInterpretationAccess(InterpretationOperatorCode, options);
            SaveToStorage();
            Storage.Synchronize();
            SettingsDidUpdate?.Invoke(null, null);
        }

        public static void ClearInterpretationAccessVerification()
        {
            InterpretationAccessVerified = false;
            InterpretationAccessCodeHash = "";
            InterpretationAccessOptionsJson = "";
            InterpretationAccessTier = "";
        }
        public static bool ConfirmRemoveDelete { get; set; }
        public static bool AutoSaveEnabled { get; set; }
        public static int AutoSaveIntervalMinutes { get; set; }
        public static int AutoSaveFileLimit { get; set; }
        public static bool PromptForAutoSaveRecovery { get; set; }
        public static bool AutomaticallyDiscardOrphanInjectionsOnLoad { get; set; }

        public static bool Verbose { get; set; } = false;

        private static string lastDocumentPath = null;
        private static string[] lastDocumentPaths = null;
        public static string LastDocumentPath { get => lastDocumentPath; set { lastDocumentPath = NormalizeDocumentPath(value); Save(); } }
        public static string[] LastDocumentPaths { get => lastDocumentPaths; set { lastDocumentPaths = NormalizeDocumentPaths(value); Save(); } }

        //Processing
        public static bool DiscardIntegrationRegionForBaseline { get; set; }
        public static bool IncludeBufferInIonicStrengthCalc { get; set; }
        public static DilutionMethod DilutionCalculationMethod { get; set; }
        public static BufferSubtractionMethod BufferSubtractionDefaultMethod { get; set; }
        public static bool ReprocessIntegratedHeatDataOnLoad { get; set; }
        public static SplineInterpolator.SplinePointDensity DefaultSplinePointDensity { get; set; }
        public static SplineInterpolator.SplineHandleMode DefaultSplineHandleMode { get; set; }
        public static bool DefaultSplinePointTimeDragging { get; set; }
        public static bool IntegrationRegionCopyIncludesStart { get; set; }

        //Fitting
        public static bool InputAffinityAsDissociationConstant { get; set; }

        public static ErrorEstimationMethod DefaultErrorEstimationMethod { get; set; }
        public static int DefaultBootstrapIterations { get; set; }
        public static double MinimumTemperatureSpanForFitting { get; set; }
        public static double MinimumIonSpanForFitting { get; set; }
        public static bool IncludeConcentrationErrorsInBootstrap { get; set; }
        public static double ConcentrationAutoVariance { get; set; }
        public static bool IsConcentrationAutoVarianceEnabled { get; set; }

        public static double OptimizerTolerance { get; set; }
        public static int MaximumOptimizerIterations { get; set; }
        public static bool EnableExtendedParameterLimits { get; set; }
        public static ParameterLimitSetting ParameterLimitSetting { get; set; }
        public static SolverAlgorithm DefaultSolverAlgorithm { get; set; }
        public static bool UseInjectionErrorWeightedFitting { get; set; }

        //Analysis
        public static bool BuffersPreparedAtRoomTemperature { get; set; }
        public static bool CreateSingleAnalysisResult { get; set; }
        public static bool CreateGlobalAnalysisResult { get; set; }
        public static bool AutoOpenNewAnalysisResult { get; set; }
        public static bool RememberResultTableColumnWidthsForSession { get; set; }
        public static FinalFigureDisplayParameters AnalysisParameterDisplay { get; set; }
        public static bool UseLargeAnalysisParameterText { get; set; }
        public static bool AutoSelectReportReferenceExperiments { get; set; }
        //Final figure
        public static double[] FinalFigureDimensions { get; set; }
        public static PublicationFont PublicationFigureFont { get; set; }
        public static FinalFigureDisplayParameters FinalFigureParameterDisplay { get; set; }
        public static DisplayAttributeOptions DisplayAttributeOptions { get; set; }
        public static bool FinalFigureShowParameterBoxAsDefault { get; set; }
        public static bool FinalFigureShowDetailsAsDefault { get; set; }
        public static bool FinalFigureShowModelInfoAsDefault { get; set; }
        public static NumberPrecision NumberPrecision { get; set; }
        public static UncertaintyDisplayStyle UncertaintyDisplayStyle { get; set; }
        public static bool ShowResidualGraph { get; set; }
        public static bool ShowResidualGraphGap { get; set; }
        public static bool UnifyResidualGraphAxis { get; set; }
        public static LineSmoothness FitLineSmoothness { get; set; }
        public static bool AutoAxesIgnoresBadData { get; set; }

        //Export
        public static bool UnifyTimeAxisForExport { get; set; }
        public static bool ExportBaselineCorrectedData { get; set; }
        public static bool ExportFitPointsWithPeaks { get; set; }
        public static ExportDataSelection ExportSelectionMode { get; set; }
        public static int NumOfDecimalsToExport { get; set; }
        public static ExportColumns ExportColumns { get; set; }
        public static ExportType DefaultExportType { get; set; }
        public static string ExportOutputBaseName { get; set; }

        public static void Initialize()
        {
            AppEventHandler.PrintAndLog("Initializing Settings...");

            Load();
        }

        public static void Save()
        {
            ApplySettings();
            SaveToStorage();
            Storage.Synchronize();
            SettingsDidUpdate?.Invoke(null, null);
        }

        static void SaveToStorage()
        {
            Storage.SetDouble("ReferenceTemperature", ReferenceTemperature);
            Storage.SetInt("EnergyUnitFamily", (int)NormalizeEnergyUnitFamily(EnergyUnitFamily));
            Storage.SetInt("DefaultErrorEstimationMethod", (int)DefaultErrorEstimationMethod);
            Storage.SetInt("DefaultBootstrapIterations", DefaultBootstrapIterations);
            Storage.SetDouble("MinimumTemperatureSpanForFitting", MinimumTemperatureSpanForFitting);
            Storage.SetBool("IncludeConcentrationErrorsInBootstrap", IncludeConcentrationErrorsInBootstrap);
            Storage.SetDouble("OptimizerTolerance", OptimizerTolerance);
            Storage.SetInt("MaximumOptimizerIterations", MaximumOptimizerIterations);
            Storage.SetInt("ColorScheme", (int)ColorScheme);
            Storage.SetInt("ColorShcemeGradientMode", (int)ColorSchemeGradientMode);
            Storage.SetDouble("ConcentrationAutoVariance", ConcentrationAutoVariance);
            Storage.SetBool("UnifyTimeAxisForExport", UnifyTimeAxisForExport);
            Storage.SetBool("ExportFitPointsWithPeaks", ExportFitPointsWithPeaks);
            Storage.SetInt("ExportSelectionMode", (int)ExportSelectionMode);
            PublicationFigureFont = NormalizePublicationFont((int)PublicationFigureFont);
            Storage.SetInt("PublicationFigureFont", (int)PublicationFigureFont);
            Storage.SetInt("FinalFigureParameterDisplay", (int)FinalFigureParameterDisplay);
            Storage.SetBool("ExportBaselineCorrectedData", ExportBaselineCorrectedData);
            Storage.SetInt("DefaultConcentrationUnit", (int)DefaultConcentrationUnit);
            Storage.SetInt("DefaultDesignerInstrument", (int)DefaultDesignerInstrument);
            Storage.SetBool("InputAffinityAsDissociationConstant", InputAffinityAsDissociationConstant);
            Storage.SetString("LastDocumentUrl", LastDocumentPath);
            Storage.SetBool("EnableExtendedParameterLimits", EnableExtendedParameterLimits);
            Storage.SetInt("ParameterLimitSetting", (int)ParameterLimitSetting);
            Storage.SetBool("BuffersPreparedAtRoomTemperature", BuffersPreparedAtRoomTemperature);
            Storage.SetBool("CreateSingleAnalysisResult", CreateSingleAnalysisResult);
            Storage.SetBool("CreateGlobalAnalysisResult", CreateGlobalAnalysisResult);
            Storage.SetBool("AutoOpenNewAnalysisResult", AutoOpenNewAnalysisResult);
            Storage.SetBool("RememberResultTableColumnWidthsForSession", RememberResultTableColumnWidthsForSession);
            Storage.SetInt("AnalysisParameterDisplay", (int)AnalysisParameterDisplay);
            Storage.SetBool("UseLargeAnalysisParameterText", UseLargeAnalysisParameterText);
            Storage.SetBool("AutoSelectReportReferenceExperiments", AutoSelectReportReferenceExperiments);
            Storage.SetInt("NumOfDecimalsToExport", NumOfDecimalsToExport);
            Storage.SetDouble("MinimumIonSpanForFitting", MinimumIonSpanForFitting);
            Storage.SetBool("FinalFigureShowParameterBoxAsDefault", FinalFigureShowParameterBoxAsDefault);
            Storage.SetBool("FinalFigureShowDetailsAsDefault", FinalFigureShowDetailsAsDefault);
            Storage.SetBool("FinalFigureShowModelInfoAsDefault", FinalFigureShowModelInfoAsDefault);
            Storage.SetInt("NumberPrecision", (int)NumberPrecision);
            Storage.SetInt("UncertaintyDisplayStyle", (int)UncertaintyDisplayStyle);
            Storage.SetBool("IncludeBufferInIonicStrengthCalc", IncludeBufferInIonicStrengthCalc);
            Storage.SetInt("DisplayAttributeOptions", (int)DisplayAttributeOptions);
            Storage.SetInt("ExportColumns", (int)ExportColumns);
            Storage.SetInt("DefaultExportType", (int)DefaultExportType);
            Storage.SetString("ExportOutputBaseName", ExportOutputBaseName);
            Storage.SetBool("ShowResidualGraph", ShowResidualGraph);
            Storage.SetBool("ShowResidualGraphGap", ShowResidualGraphGap);
            Storage.SetBool("UnifyResidualGraphAxis", UnifyResidualGraphAxis);
            Storage.SetInt("FitLineSmoothness", (int)FitLineSmoothness);
            Storage.SetBool("DiscardIntegrationRegionForBaseline", DiscardIntegrationRegionForBaseline);
            Storage.SetInt("SolverAlgorithm", (int)DefaultSolverAlgorithm);
            Storage.SetBool("UseInjectionErrorWeightedFitting", UseInjectionErrorWeightedFitting);
            Storage.SetBool("AutoAxesIgnoresBadData", AutoAxesIgnoresBadData);
            Storage.SetInt("DilutionCalculationMethod", (int)DilutionCalculationMethod);
            Storage.SetInt("BufferSubtractionDefaultMethod", (int)BufferSubtractionDefaultMethod);
            Storage.SetBool("ReprocessIntegratedHeatDataOnLoad", ReprocessIntegratedHeatDataOnLoad);
            Storage.SetInt("DefaultSplinePointDensity", (int)DefaultSplinePointDensity);
            Storage.SetInt("DefaultSplineHandleMode", (int)DefaultSplineHandleMode);
            Storage.SetBool("DefaultSplinePointTimeDragging", DefaultSplinePointTimeDragging);
            Storage.SetBool("IntegrationRegionCopyIncludesStart", IntegrationRegionCopyIncludesStart);
            Storage.SetBool("PerformOnlineChecksOnLaunch", PerformOnlineChecksOnLaunch);
            Storage.SetString("InterpretationOperatorCode", InterpretationOperatorCode);
            Storage.SetBool("UseInterpretationEvaluationSettings", UseInterpretationEvaluationSettings);
            Storage.SetString("InterpretationEvaluationModel", InterpretationEvaluationModel);
            Storage.SetString("InterpretationEvaluationReasoningEffort", InterpretationEvaluationReasoningEffort);
            Storage.SetString("InterpretationGenerationPreset", InterpretationGenerationPreset);
            Storage.SetBool("InterpretationAccessVerified", InterpretationAccessVerified);
            Storage.SetString("InterpretationAccessCodeHash", InterpretationAccessCodeHash);
            Storage.SetString("InterpretationAccessOptionsJson", InterpretationAccessOptionsJson);
            Storage.SetString("InterpretationAccessTier", InterpretationAccessTier);
            Storage.SetBool("ConfirmRemoveDelete", ConfirmRemoveDelete);
            Storage.SetBool("AutoSaveEnabled", AutoSaveEnabled);
            Storage.SetInt("AutoSaveIntervalMinutes", AutoSaveIntervalMinutes);
            Storage.SetInt("AutoSaveFileLimit", AutoSaveFileLimit);
            Storage.SetBool("PromptForAutoSaveRecovery", PromptForAutoSaveRecovery);
            Storage.SetBool("AutomaticallyDiscardOrphanInjectionsOnLoad", AutomaticallyDiscardOrphanInjectionsOnLoad);

            Storage.SetStringArray("LastDocumentUrls", LastDocumentPaths);
            Storage.SetDoubleArray("FinalFigureDimensions", FinalFigureDimensions);

            Storage.SetBool("IsSaved", true);
        }

        public static void Load()
        {
            if (!Storage.Contains("IsSaved") || !Storage.GetBool("IsSaved"))
            {
                Console.WriteLine("No settings are stored.");
            }
            else Console.WriteLine("There are {0} settings stored.", Storage.Count);

            ReferenceTemperature = Storage.GetDouble("ReferenceTemperature", ReferenceTemperature);
            EnergyUnitFamily = ReadEnergyUnitFamily();
            // Persist the migrated family immediately.  The old exact-unit key is
            // intentionally left untouched so it remains a one-time fallback.
            Storage.SetInt("EnergyUnitFamily", (int)EnergyUnitFamily);
            Storage.Synchronize();
            EnergyUnit = LegacyDefaultExactUnit(EnergyUnitFamily);
            DefaultErrorEstimationMethod = (ErrorEstimationMethod)Storage.GetInt("DefaultErrorEstimationMethod", (int)DefaultErrorEstimationMethod);
            DefaultBootstrapIterations = Storage.GetInt("DefaultBootstrapIterations", DefaultBootstrapIterations);
            MinimumTemperatureSpanForFitting = Storage.GetDouble("MinimumTemperatureSpanForFitting", MinimumTemperatureSpanForFitting);
            IncludeConcentrationErrorsInBootstrap = Storage.GetBool("IncludeConcentrationErrorsInBootstrap", IncludeConcentrationErrorsInBootstrap);
            OptimizerTolerance = Storage.GetDouble("OptimizerTolerance", OptimizerTolerance);
            MaximumOptimizerIterations = Storage.GetInt("MaximumOptimizerIterations", MaximumOptimizerIterations);
            ColorScheme = (ColorSchemes)Storage.GetInt("ColorScheme", (int)ColorScheme);
            ColorSchemeGradientMode = (ColorSchemeGradientMode)Storage.GetInt("ColorShcemeGradientMode", (int)ColorSchemeGradientMode);
            ConcentrationAutoVariance = Storage.GetDouble("ConcentrationAutoVariance", ConcentrationAutoVariance);
            UnifyTimeAxisForExport = Storage.GetBool("UnifyTimeAxisForExport", UnifyTimeAxisForExport);
            ExportFitPointsWithPeaks = Storage.GetBool("ExportFitPointsWithPeaks", ExportFitPointsWithPeaks);
            ExportSelectionMode = (ExportDataSelection)Storage.GetInt("ExportSelectionMode", (int)ExportSelectionMode);
            PublicationFigureFont = NormalizePublicationFont(
                Storage.GetInt("PublicationFigureFont", (int)PublicationFont.Native));
            EnableExtendedParameterLimits = Storage.GetBool("EnableExtendedParameterLimits", EnableExtendedParameterLimits);
            ParameterLimitSetting = (ParameterLimitSetting)Storage.GetInt("ParameterLimitSetting", (int)ParameterLimitSetting);
            FinalFigureParameterDisplay = (FinalFigureDisplayParameters)Storage.GetInt("FinalFigureParameterDisplay", (int)FinalFigureParameterDisplay);
            FinalFigureDimensions = Storage.GetDoubleArray("FinalFigureDimensions", FinalFigureDimensions);
            ExportBaselineCorrectedData = Storage.GetBool("ExportBaselineCorrectedData", ExportBaselineCorrectedData);
            DefaultConcentrationUnit = (ConcentrationUnit)Storage.GetInt("DefaultConcentrationUnit", (int)DefaultConcentrationUnit);
            DefaultDesignerInstrument = NormalizeDesignerInstrument(Storage.GetInt("DefaultDesignerInstrument", (int)DefaultDesignerInstrument));
            InputAffinityAsDissociationConstant = Storage.GetBool("InputAffinityAsDissociationConstant", InputAffinityAsDissociationConstant);
            lastDocumentPath = NormalizeDocumentPath(Storage.GetString("LastDocumentUrl"));
            BuffersPreparedAtRoomTemperature = Storage.GetBool("BuffersPreparedAtRoomTemperature", BuffersPreparedAtRoomTemperature);
            CreateSingleAnalysisResult = Storage.GetBool("CreateSingleAnalysisResult", CreateSingleAnalysisResult);
            CreateGlobalAnalysisResult = Storage.GetBool("CreateGlobalAnalysisResult", CreateGlobalAnalysisResult);
            AutoOpenNewAnalysisResult = Storage.GetBool("AutoOpenNewAnalysisResult", AutoOpenNewAnalysisResult);
            RememberResultTableColumnWidthsForSession = Storage.GetBool(
                "RememberResultTableColumnWidthsForSession",
                RememberResultTableColumnWidthsForSession);
            AnalysisParameterDisplay = (FinalFigureDisplayParameters)Storage.GetInt("AnalysisParameterDisplay", (int)AnalysisParameterDisplay);
            UseLargeAnalysisParameterText = Storage.GetBool("UseLargeAnalysisParameterText", UseLargeAnalysisParameterText);
            AutoSelectReportReferenceExperiments = Storage.GetBool(
                "AutoSelectReportReferenceExperiments",
                AutoSelectReportReferenceExperiments);
            NumOfDecimalsToExport = Storage.GetInt("NumOfDecimalsToExport", NumOfDecimalsToExport);
            MinimumIonSpanForFitting = Storage.GetDouble("MinimumIonSpanForFitting", MinimumIonSpanForFitting);
            FinalFigureShowParameterBoxAsDefault = Storage.GetBool("FinalFigureShowParameterBoxAsDefault", FinalFigureShowParameterBoxAsDefault);
            FinalFigureShowDetailsAsDefault = Storage.GetBool("FinalFigureShowDetailsAsDefault", FinalFigureShowDetailsAsDefault);
            FinalFigureShowModelInfoAsDefault = Storage.GetBool("FinalFigureShowModelInfoAsDefault", FinalFigureShowModelInfoAsDefault);
            NumberPrecision = (NumberPrecision)Storage.GetInt("NumberPrecision", (int)NumberPrecision);
            UncertaintyDisplayStyle = (UncertaintyDisplayStyle)Storage.GetInt("UncertaintyDisplayStyle", (int)UncertaintyDisplayStyle);
            IncludeBufferInIonicStrengthCalc = Storage.GetBool("IncludeBufferInIonicStrengthCalc", IncludeBufferInIonicStrengthCalc);
            DisplayAttributeOptions = (DisplayAttributeOptions)Storage.GetInt("DisplayAttributeOptions", (int)DisplayAttributeOptions);
            ExportColumns = (ExportColumns)Storage.GetInt("ExportColumns", (int)ExportColumns.Default);
            DefaultExportType = (ExportType)Storage.GetInt("DefaultExportType", (int)DefaultExportType);
            ExportOutputBaseName = Storage.GetString("ExportOutputBaseName") ?? ExportOutputBaseName;
            ShowResidualGraph = Storage.GetBool("ShowResidualGraph", ShowResidualGraph);
            ShowResidualGraphGap = Storage.GetBool("ShowResidualGraphGap", ShowResidualGraphGap);
            UnifyResidualGraphAxis = Storage.GetBool("UnifyResidualGraphAxis", UnifyResidualGraphAxis);
            FitLineSmoothness = (LineSmoothness)Storage.GetInt("FitLineSmoothness", (int)FitLineSmoothness);
            DiscardIntegrationRegionForBaseline = Storage.GetBool("DiscardIntegrationRegionForBaseline", DiscardIntegrationRegionForBaseline);
            DefaultSolverAlgorithm = (SolverAlgorithm)Storage.GetInt("SolverAlgorithm", (int)DefaultSolverAlgorithm);
            UseInjectionErrorWeightedFitting = Storage.GetBool("UseInjectionErrorWeightedFitting", UseInjectionErrorWeightedFitting);
            AutoAxesIgnoresBadData = Storage.GetBool("AutoAxesIgnoresBadData", AutoAxesIgnoresBadData);
            DilutionCalculationMethod = (DilutionMethod)Storage.GetInt("DilutionCalculationMethod", (int)DilutionCalculationMethod);
            BufferSubtractionDefaultMethod = NormalizeBufferSubtractionMethod(Storage.GetInt("BufferSubtractionDefaultMethod", (int)BufferSubtractionDefaultMethod));
            ReprocessIntegratedHeatDataOnLoad = Storage.GetBool("ReprocessIntegratedHeatDataOnLoad", ReprocessIntegratedHeatDataOnLoad);
            DefaultSplinePointDensity = (SplineInterpolator.SplinePointDensity)Storage.GetInt("DefaultSplinePointDensity", (int)DefaultSplinePointDensity);
            DefaultSplineHandleMode = (SplineInterpolator.SplineHandleMode)Storage.GetInt("DefaultSplineHandleMode", (int)DefaultSplineHandleMode);
            DefaultSplinePointTimeDragging = Storage.GetBool("DefaultSplinePointTimeDragging", DefaultSplinePointTimeDragging);
            IntegrationRegionCopyIncludesStart = Storage.GetBool("IntegrationRegionCopyIncludesStart", IntegrationRegionCopyIncludesStart);
            PerformOnlineChecksOnLaunch = Storage.GetBool("PerformOnlineChecksOnLaunch", PerformOnlineChecksOnLaunch);
            InterpretationOperatorCode = Storage.GetString("InterpretationOperatorCode") ?? "";
            UseInterpretationEvaluationSettings = Storage.GetBool("UseInterpretationEvaluationSettings", UseInterpretationEvaluationSettings);
            InterpretationEvaluationModel = Storage.GetString("InterpretationEvaluationModel") ?? "";
            InterpretationEvaluationReasoningEffort = Storage.GetString("InterpretationEvaluationReasoningEffort") ?? "";
            InterpretationGenerationPreset = Storage.GetString("InterpretationGenerationPreset") ?? "instant";
            InterpretationAccessVerified = Storage.GetBool("InterpretationAccessVerified", false);
            InterpretationAccessCodeHash = Storage.GetString("InterpretationAccessCodeHash") ?? "";
            InterpretationAccessOptionsJson = Storage.GetString("InterpretationAccessOptionsJson") ?? "";
            InterpretationAccessTier = Storage.GetString("InterpretationAccessTier") ?? "";
            if (!TryGetInterpretationAccessOptions(InterpretationOperatorCode, out _))
                ClearInterpretationAccessVerification();
            ConfirmRemoveDelete = Storage.GetBool("ConfirmRemoveDelete", ConfirmRemoveDelete);
            AutoSaveEnabled = Storage.GetBool("AutoSaveEnabled", AutoSaveEnabled);
            AutoSaveIntervalMinutes = Math.Max(1, Math.Min(60, Storage.GetInt("AutoSaveIntervalMinutes", AutoSaveIntervalMinutes)));
            AutoSaveFileLimit = Math.Max(1, Math.Min(100, Storage.GetInt("AutoSaveFileLimit", AutoSaveFileLimit)));
            PromptForAutoSaveRecovery = Storage.GetBool("PromptForAutoSaveRecovery", PromptForAutoSaveRecovery);
            AutomaticallyDiscardOrphanInjectionsOnLoad = Storage.GetBool(
                "AutomaticallyDiscardOrphanInjectionsOnLoad",
                AutomaticallyDiscardOrphanInjectionsOnLoad);

            lastDocumentPaths = NormalizeDocumentPaths(Storage.GetStringArray("LastDocumentUrls"));

            ApplySettings();

            StatusBarManager.ClearAppStatus();
        }

        public static void Reset()
        {
            PreferencesState.Defaults().ApplyToSettings();
            lastDocumentPath = null;
            lastDocumentPaths = null;
        }

        static string NormalizeDocumentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
                return uri.LocalPath;

            return path;
        }

        static string[] NormalizeDocumentPaths(string[] paths)
        {
            if (paths == null) return null;

            return paths
                .Select(NormalizeDocumentPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
        }

        static BufferSubtractionMethod NormalizeBufferSubtractionMethod(int method)
        {
            if (Enum.IsDefined(typeof(BufferSubtractionMethod), method))
                return (BufferSubtractionMethod)method;

            return BufferSubtractionMethod.MatchedInjection;
        }

        static EnergyUnitFamily ReadEnergyUnitFamily()
        {
            if (Storage.Contains("EnergyUnitFamily"))
                return NormalizeEnergyUnitFamily(Storage.GetInt("EnergyUnitFamily", (int)EnergyUnitFamily.Joules));

            if (Storage.Contains("EnergyUnit"))
            {
                var legacy = Storage.GetInt("EnergyUnit", (int)EnergyUnit.KiloJoule);
                if (Enum.IsDefined(typeof(EnergyUnit), legacy))
                    return EnergyUnitResolver.FamilyOf((EnergyUnit)legacy);
            }

            return EnergyUnitFamily.Joules;
        }

        static EnergyUnitFamily NormalizeEnergyUnitFamily(int family)
        {
            return Enum.IsDefined(typeof(EnergyUnitFamily), family)
                ? (EnergyUnitFamily)family
                : EnergyUnitFamily.Joules;
        }

        static EnergyUnitFamily NormalizeEnergyUnitFamily(EnergyUnitFamily family)
        {
            return Enum.IsDefined(typeof(EnergyUnitFamily), family)
                ? family
                : EnergyUnitFamily.Joules;
        }

        static EnergyUnit LegacyDefaultExactUnit(EnergyUnitFamily family)
        {
            return EnergyUnitResolver.DefaultUnit(family);
        }

        static PublicationFont NormalizePublicationFont(int font)
        {
            return Enum.IsDefined(typeof(PublicationFont), font)
                ? (PublicationFont)font
                : PublicationFont.Native;
        }

        static ITCInstrument NormalizeDesignerInstrument(int instrument)
        {
            var normalized = (ITCInstrument)instrument;

            if (ITCInstrumentAttribute.GetITCInstruments().Contains(normalized))
                return normalized;

            return ITCInstrument.MicroCalITC200;
        }

        internal static void UpdateDerivedSettings()
        {
            IsConcentrationAutoVarianceEnabled = ConcentrationAutoVariance > double.Epsilon;
            EnableExtendedParameterLimits = ParameterLimitSetting != ParameterLimitSetting.Standard;
        }

        public static void ApplySettings()
        {
            UpdateDerivedSettings();
            FittingOptionsController.BootstrapIterations = DefaultBootstrapIterations;
            FittingOptionsController.ErrorEstimationMethod = DefaultErrorEstimationMethod;
            FittingOptionsController.IncludeConcentrationVariance = IncludeConcentrationErrorsInBootstrap;
            FittingOptionsController.AutoConcentrationVariance = ConcentrationAutoVariance;
            FittingOptionsController.EnableAutoConcentrationVariance = IsConcentrationAutoVarianceEnabled;
            FittingOptionsController.Algorithm = DefaultSolverAlgorithm;
            FittingOptionsController.UseErrorWeightedFitting = UseInjectionErrorWeightedFitting;
            SettingsApplied?.Invoke(null, null);
        }
    }

    public enum NumberPrecision
    {
        Strict,
        Standard,
        SingleDecimal,
        AllDecimals,
    }

    public enum UncertaintyDisplayStyle
    {
        Automatic,
        StandardDeviation,
        ConfidenceInterval,
        StandardDeviationAndConfidenceInterval,
        None,
    }

    public enum ParameterLimitSetting
    {
        Standard,
        Extended,
        NoLimit,
    }
}
